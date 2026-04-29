using System.Globalization;
using System.Xml.Linq;
using Core = TWXProxy.Core;

namespace MTC;

/// <summary>
/// Lightweight application-level preferences (not per-connection).
/// Persisted in <programdir>/config.twx under the MtcPrefs section, with
/// shared program/scripts paths stored in the SharedPaths section.
/// </summary>
public class AppPreferences
{
    public const int MaxRecentFiles = 5;
    public const int CurrentCommandDeckLayoutVersion = 4;

    public sealed class MacroBinding
    {
        public string Hotkey { get; set; } = string.Empty;
        public string Macro { get; set; } = string.Empty;
    }

    public sealed class DeckPanelLayout
    {
        public string PanelId { get; set; } = string.Empty;
        public double Left { get; set; }
        public double Top { get; set; }
        public double Width { get; set; }
        public double BodyHeight { get; set; }
        public int ZIndex { get; set; }
        public bool Closed { get; set; }
        public bool Minimized { get; set; }
    }

    public sealed class StatusPanelSectionPreference
    {
        public string Id { get; set; } = string.Empty;
        public bool Visible { get; set; } = true;
        public int Order { get; set; }
    }

    public const string StatusPanelTrader = "trader";
    public const string StatusPanelHolds = "holds";
    public const string StatusPanelShipInfo = "ship";

    private static readonly string[] DefaultStatusPanelSectionOrder =
    [
        StatusPanelTrader,
        StatusPanelHolds,
        StatusPanelShipInfo,
    ];

    public List<string> RecentFiles { get; } = [];
    public List<MacroBinding> MacroBindings { get; } = [];
    public Dictionary<string, DeckPanelLayout> CommandDeckPanels { get; }
        = new(StringComparer.OrdinalIgnoreCase);
    public List<StatusPanelSectionPreference> StatusPanelSections { get; } = [];

    public string ProgramDirectory { get; set; } = string.Empty;
    public string ScriptsDirectory { get; set; } = string.Empty;
    public bool HasConfiguredSharedPaths { get; private set; }

    public bool DebugLoggingEnabled { get; set; }
    public bool VerboseDebugLogging { get; set; }
    public bool DebugPortHaggleEnabled { get; set; }
    public bool DebugPlanetHaggleEnabled { get; set; }
    public bool ShowHaggleDetails { get; set; }
    public bool ShowBottomBar { get; set; } = true;
    public bool PreparedVmEnabled { get; set; } = true;
    public bool VmMetricsEnabled { get; set; }
    public string PortHaggleMode { get; set; } = TWXProxy.Core.NativeHaggleModes.Default;
    public string PlanetHaggleMode { get; set; } = TWXProxy.Core.NativeHaggleModes.DefaultPlanet;
    public bool CommandDeckSkinEnabled { get; set; }
    public int CommandDeckLayoutVersion { get; set; }

    private static string LegacySharedPrefsPath()
        => Path.Combine(AppPaths.AppDataDir, "prefs.xml");

    private static string LegacyDefaultPath()
        => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".config",
            "MTC",
            "prefs.xml");

    public void AddRecent(string path)
    {
        RecentFiles.RemoveAll(existing =>
            string.Equals(existing, path, StringComparison.OrdinalIgnoreCase));
        RecentFiles.Insert(0, path);
        while (RecentFiles.Count > MaxRecentFiles)
            RecentFiles.RemoveAt(RecentFiles.Count - 1);
    }

    public bool TryGetDeckPanelLayout(string panelId, out DeckPanelLayout layout)
        => CommandDeckPanels.TryGetValue(panelId, out layout!);

    public void SetDeckPanelLayout(string panelId, double left, double top, double width, double bodyHeight, int zIndex, bool closed, bool minimized)
    {
        CommandDeckPanels[panelId] = new DeckPanelLayout
        {
            PanelId = panelId,
            Left = left,
            Top = top,
            Width = width,
            BodyHeight = bodyHeight,
            ZIndex = zIndex,
            Closed = closed,
            Minimized = minimized,
        };
    }

    public void Save()
    {
        try
        {
            EnsureStatusPanelSections();

            if (CommandDeckLayoutVersion < CurrentCommandDeckLayoutVersion)
                CommandDeckLayoutVersion = CurrentCommandDeckLayoutVersion;

            ProgramDirectory = NormalizeDirectoryValue(ProgramDirectory);
            if (string.IsNullOrWhiteSpace(ProgramDirectory))
                ProgramDirectory = Core.SharedPaths.ResolveProgramDir(ScriptsDirectory);

            ScriptsDirectory = NormalizeDirectoryValue(ScriptsDirectory);
            if (string.IsNullOrWhiteSpace(ScriptsDirectory))
                ScriptsDirectory = Core.SharedPathSettingsStore.GetDefaultScriptsDirectory(ProgramDirectory);

            Core.SharedPathSettingsStore.Save(ProgramDirectory, ScriptsDirectory);
            string configPath = Core.SharedPaths.GetConfigFilePath(ProgramDirectory);
            var document = Core.SharedConfigFile.LoadOrCreate(configPath);

            var section = new XElement(
                Core.SharedConfigFile.MtcPrefsSectionName,
                new XElement("DebugLoggingEnabled", DebugLoggingEnabled),
                new XElement("VerboseDebugLogging", VerboseDebugLogging),
                new XElement("DebugPortHaggleEnabled", DebugPortHaggleEnabled),
                new XElement("DebugPlanetHaggleEnabled", DebugPlanetHaggleEnabled),
                new XElement("ShowHaggleDetails", ShowHaggleDetails),
                new XElement("ShowBottomBar", ShowBottomBar),
                new XElement("PreparedVmEnabled", PreparedVmEnabled),
                new XElement("VmMetricsEnabled", VmMetricsEnabled),
                new XElement("PortHaggleMode", PortHaggleMode),
                new XElement("PlanetHaggleMode", PlanetHaggleMode),
                new XElement("CommandDeckSkinEnabled", CommandDeckSkinEnabled),
                new XElement("CommandDeckLayoutVersion", CommandDeckLayoutVersion),
                new XElement("RecentFiles", RecentFiles.Select(path => new XElement("File", path))),
                new XElement("Macros",
                    MacroBindings
                        .Where(binding => !string.IsNullOrWhiteSpace(binding.Hotkey) &&
                                          !string.IsNullOrWhiteSpace(binding.Macro))
                        .Select(binding => new XElement(
                            "Macro",
                            new XAttribute("Hotkey", NormalizeMacroHotkey(binding.Hotkey)),
                            binding.Macro))),
                new XElement("CommandDeckPanels",
                    CommandDeckPanels.Values
                        .OrderBy(layout => layout.PanelId, StringComparer.OrdinalIgnoreCase)
                        .Select(layout => new XElement("Panel",
                            new XAttribute("Id", layout.PanelId),
                            new XAttribute("Left", layout.Left.ToString(CultureInfo.InvariantCulture)),
                            new XAttribute("Top", layout.Top.ToString(CultureInfo.InvariantCulture)),
                            new XAttribute("Width", layout.Width.ToString(CultureInfo.InvariantCulture)),
                            new XAttribute("BodyHeight", layout.BodyHeight.ToString(CultureInfo.InvariantCulture)),
                            new XAttribute("ZIndex", layout.ZIndex),
                            new XAttribute("Closed", layout.Closed),
                            new XAttribute("Minimized", layout.Minimized)))),
                new XElement("StatusPanelSections",
                    StatusPanelSections
                        .OrderBy(section => section.Order)
                        .ThenBy(section => GetDefaultStatusPanelSectionIndex(section.Id))
                        .Select(section => new XElement("Section",
                            new XAttribute("Id", NormalizeStatusPanelSectionId(section.Id)),
                            new XAttribute("Visible", section.Visible),
                            new XAttribute("Order", section.Order))))
            );

            Core.SharedConfigFile.ReplaceSection(document, Core.SharedConfigFile.MtcPrefsSectionName, section);
            Core.SharedConfigFile.Save(document, configPath);
            HasConfiguredSharedPaths = true;
        }
        catch
        {
            // Best-effort persistence.
        }
    }

    public static AppPreferences Load()
    {
        var prefs = new AppPreferences();

        try
        {
            Core.SharedPathSettings sharedPaths = Core.SharedPathSettingsStore.Load();
            prefs.ProgramDirectory = NormalizeDirectoryValue(sharedPaths.ProgramDirectory);
            prefs.ScriptsDirectory = NormalizeDirectoryValue(sharedPaths.ScriptsDirectory);
            prefs.HasConfiguredSharedPaths = sharedPaths.IsConfigured;

            string configPath = Core.SharedPaths.GetConfigFilePath(prefs.ProgramDirectory);
            XDocument document;
            if (File.Exists(configPath))
            {
                document = XDocument.Load(configPath);
            }
            else
            {
                document = LoadLegacyDocument();
            }

            XElement? root = Core.SharedConfigFile.GetSection(document, Core.SharedConfigFile.MtcPrefsSectionName);
            if (root == null)
                return prefs;

            if (bool.TryParse((string?)root.Element("DebugLoggingEnabled"), out bool debugEnabled))
                prefs.DebugLoggingEnabled = debugEnabled;
            if (bool.TryParse((string?)root.Element("VerboseDebugLogging"), out bool verboseEnabled))
                prefs.VerboseDebugLogging = verboseEnabled;
            if (bool.TryParse((string?)root.Element("DebugPortHaggleEnabled"), out bool debugPortHaggleEnabled))
                prefs.DebugPortHaggleEnabled = debugPortHaggleEnabled;
            if (bool.TryParse((string?)root.Element("DebugPlanetHaggleEnabled"), out bool debugPlanetHaggleEnabled))
                prefs.DebugPlanetHaggleEnabled = debugPlanetHaggleEnabled;
            if (bool.TryParse((string?)root.Element("ShowHaggleDetails"), out bool showHaggleDetails))
                prefs.ShowHaggleDetails = showHaggleDetails;
            if (bool.TryParse((string?)root.Element("ShowBottomBar"), out bool showBottomBar))
                prefs.ShowBottomBar = showBottomBar;
            if (bool.TryParse((string?)root.Element("PreparedVmEnabled"), out bool preparedVmEnabled))
                prefs.PreparedVmEnabled = preparedVmEnabled;
            if (bool.TryParse((string?)root.Element("VmMetricsEnabled"), out bool vmMetricsEnabled))
                prefs.VmMetricsEnabled = vmMetricsEnabled;
            if (bool.TryParse((string?)root.Element("CommandDeckSkinEnabled"), out bool commandDeckEnabled))
                prefs.CommandDeckSkinEnabled = commandDeckEnabled;
            if (int.TryParse((string?)root.Element("CommandDeckLayoutVersion"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int commandDeckLayoutVersion))
                prefs.CommandDeckLayoutVersion = commandDeckLayoutVersion;

            string? portHaggleMode = (string?)root.Element("PortHaggleMode");
            string? planetHaggleMode = (string?)root.Element("PlanetHaggleMode");
            string? legacyNativeHaggleMode = (string?)root.Element("NativeHaggleMode");
            prefs.PortHaggleMode = TWXProxy.Core.NativeHaggleModes.Normalize(
                string.IsNullOrWhiteSpace(portHaggleMode) ? legacyNativeHaggleMode : portHaggleMode);
            prefs.PlanetHaggleMode = string.IsNullOrWhiteSpace(planetHaggleMode)
                ? TWXProxy.Core.NativeHaggleModes.DefaultPlanet
                : TWXProxy.Core.NativeHaggleModes.Normalize(planetHaggleMode);

            foreach (XElement element in root.Element("RecentFiles")?.Elements("File")
                                   ?? Enumerable.Empty<XElement>())
            {
                string? path = ResolveRecentFilePath((string?)element, prefs.ProgramDirectory);
                if (!string.IsNullOrWhiteSpace(path))
                    prefs.RecentFiles.Add(path);
            }

            foreach (XElement element in root.Element("Macros")?.Elements("Macro")
                                   ?? Enumerable.Empty<XElement>())
            {
                string hotkey = NormalizeMacroHotkey((string?)element.Attribute("Hotkey"));
                string macro = (string?)element ?? string.Empty;
                if (string.IsNullOrWhiteSpace(macro))
                    continue;

                prefs.MacroBindings.Add(new MacroBinding
                {
                    Hotkey = hotkey,
                    Macro = macro,
                });
            }

            foreach (XElement panel in root.Element("CommandDeckPanels")?.Elements("Panel")
                                   ?? Enumerable.Empty<XElement>())
            {
                string? panelId = (string?)panel.Attribute("Id");
                if (string.IsNullOrWhiteSpace(panelId))
                    continue;

                prefs.CommandDeckPanels[panelId] = new DeckPanelLayout
                {
                    PanelId = panelId,
                    Left = ParseDouble(panel.Attribute("Left")),
                    Top = ParseDouble(panel.Attribute("Top")),
                    Width = ParseDouble(panel.Attribute("Width")),
                    BodyHeight = ParseDouble(panel.Attribute("BodyHeight")),
                    ZIndex = ParseInt(panel.Attribute("ZIndex")),
                    Closed = ParseBool(panel.Attribute("Closed")),
                    Minimized = ParseBool(panel.Attribute("Minimized")),
                };
            }

            foreach (XElement section in root.Element("StatusPanelSections")?.Elements("Section")
                                      ?? Enumerable.Empty<XElement>())
            {
                string id = NormalizeStatusPanelSectionId((string?)section.Attribute("Id"));
                if (string.IsNullOrWhiteSpace(id))
                    continue;

                prefs.StatusPanelSections.Add(new StatusPanelSectionPreference
                {
                    Id = id,
                    Visible = ParseBool(section.Attribute("Visible"), defaultValue: true),
                    Order = ParseInt(section.Attribute("Order")),
                });
            }

            prefs.EnsureStatusPanelSections();

            string? legacyScriptsDirectory = NormalizeDirectoryValue((string?)root.Element("ScriptsDirectory"));
            if (!string.IsNullOrWhiteSpace(legacyScriptsDirectory) &&
                !prefs.HasConfiguredSharedPaths)
            {
                prefs.ScriptsDirectory = legacyScriptsDirectory;
                prefs.ProgramDirectory = Core.SharedPaths.ResolveProgramDir(legacyScriptsDirectory);
                prefs.HasConfiguredSharedPaths = true;
            }
        }
        catch
        {
            // Ignore corrupt prefs.
        }

        return prefs;
    }

    public IReadOnlyList<StatusPanelSectionPreference> GetOrderedStatusPanelSections()
    {
        EnsureStatusPanelSections();
        return StatusPanelSections
            .OrderBy(section => section.Order)
            .ThenBy(section => GetDefaultStatusPanelSectionIndex(section.Id))
            .Select(section => new StatusPanelSectionPreference
            {
                Id = section.Id,
                Visible = section.Visible,
                Order = section.Order,
            })
            .ToList();
    }

    public void SetStatusPanelSections(IEnumerable<StatusPanelSectionPreference> sections)
    {
        StatusPanelSections.Clear();

        int order = 0;
        foreach (StatusPanelSectionPreference section in sections ?? Enumerable.Empty<StatusPanelSectionPreference>())
        {
            string normalizedId = NormalizeStatusPanelSectionId(section.Id);
            if (string.IsNullOrWhiteSpace(normalizedId))
                continue;

            if (StatusPanelSections.Any(existing => string.Equals(existing.Id, normalizedId, StringComparison.OrdinalIgnoreCase)))
                continue;

            StatusPanelSections.Add(new StatusPanelSectionPreference
            {
                Id = normalizedId,
                Visible = section.Visible,
                Order = order++,
            });
        }

        EnsureStatusPanelSections();
    }

    public static string GetStatusPanelSectionLabel(string id)
        => NormalizeStatusPanelSectionId(id) switch
        {
            StatusPanelTrader => "Trader",
            StatusPanelHolds => "Holds",
            StatusPanelShipInfo => "Ship Info",
            _ => id,
        };

    private static XDocument LoadLegacyDocument()
    {
        foreach (string legacyPath in new[] { LegacySharedPrefsPath(), LegacyDefaultPath() })
        {
            try
            {
                if (File.Exists(legacyPath))
                    return XDocument.Load(legacyPath);
            }
            catch
            {
            }
        }

        return Core.SharedConfigFile.CreateEmptyDocument();
    }

    private static string? ResolveRecentFilePath(string? path, string programDirectory)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;

        string normalized = NormalizeDirectoryValue(path);
        if (File.Exists(normalized))
            return normalized;

        string gamesDir = Path.Combine(programDirectory, "games");
        string candidate = Path.Combine(gamesDir, Path.GetFileName(normalized));
        if (File.Exists(candidate))
            return candidate;

        string legacyCandidate = Path.Combine(Core.SharedPaths.LegacyGamesDir, Path.GetFileName(normalized));
        return File.Exists(legacyCandidate) ? legacyCandidate : null;
    }

    private static string NormalizeDirectoryValue(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        try
        {
            return Path.GetFullPath(value.Trim())
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        catch
        {
            return value.Trim();
        }
    }

    private static string NormalizeMacroHotkey(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "F1";

        string normalized = value.Trim().ToUpperInvariant();
        return normalized is "F1" or "F2" or "F3" or "F4" or "F5" or "F6" or "F7" or "F8" or "F9" or "F10" or "F11"
            ? normalized
            : "F1";
    }

    private void EnsureStatusPanelSections()
    {
        var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var normalizedSections = new List<StatusPanelSectionPreference>();

        foreach (StatusPanelSectionPreference section in StatusPanelSections)
        {
            string normalizedId = NormalizeStatusPanelSectionId(section.Id);
            if (string.IsNullOrWhiteSpace(normalizedId) || !seenIds.Add(normalizedId))
                continue;

            normalizedSections.Add(new StatusPanelSectionPreference
            {
                Id = normalizedId,
                Visible = section.Visible,
                Order = section.Order,
            });
        }

        foreach (string defaultId in DefaultStatusPanelSectionOrder)
        {
            if (seenIds.Add(defaultId))
            {
                normalizedSections.Add(new StatusPanelSectionPreference
                {
                    Id = defaultId,
                    Visible = true,
                    Order = int.MaxValue,
                });
            }
        }

        StatusPanelSections.Clear();
        int order = 0;
        foreach (StatusPanelSectionPreference section in normalizedSections
                     .OrderBy(section => section.Order)
                     .ThenBy(section => GetDefaultStatusPanelSectionIndex(section.Id)))
        {
            section.Order = order++;
            StatusPanelSections.Add(section);
        }
    }

    private static string NormalizeStatusPanelSectionId(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        string normalized = value.Trim().ToLowerInvariant();
        return normalized switch
        {
            StatusPanelTrader => StatusPanelTrader,
            StatusPanelHolds => StatusPanelHolds,
            StatusPanelShipInfo => StatusPanelShipInfo,
            _ => string.Empty,
        };
    }

    private static int GetDefaultStatusPanelSectionIndex(string? id)
    {
        string normalizedId = NormalizeStatusPanelSectionId(id);
        int index = Array.IndexOf(DefaultStatusPanelSectionOrder, normalizedId);
        return index >= 0 ? index : int.MaxValue;
    }

    private static double ParseDouble(XAttribute? attribute)
        => double.TryParse(attribute?.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double value)
            ? value
            : 0;

    private static int ParseInt(XAttribute? attribute)
        => int.TryParse(attribute?.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value)
            ? value
            : 0;

    private static bool ParseBool(XAttribute? attribute)
        => bool.TryParse(attribute?.Value, out bool value) && value;

    private static bool ParseBool(XAttribute? attribute, bool defaultValue)
        => attribute == null
            ? defaultValue
            : bool.TryParse(attribute.Value, out bool value)
                ? value
                : defaultValue;
}
