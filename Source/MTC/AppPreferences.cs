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

    public sealed class DeckPanelLayout
    {
        public string PanelId { get; set; } = string.Empty;
        public double Left { get; set; }
        public double Top { get; set; }
        public int ZIndex { get; set; }
        public bool Closed { get; set; }
        public bool Minimized { get; set; }
    }

    public List<string> RecentFiles { get; } = [];
    public Dictionary<string, DeckPanelLayout> CommandDeckPanels { get; }
        = new(StringComparer.OrdinalIgnoreCase);

    public string ProgramDirectory { get; set; } = string.Empty;
    public string ScriptsDirectory { get; set; } = string.Empty;
    public bool HasConfiguredSharedPaths { get; private set; }

    public bool DebugLoggingEnabled { get; set; }
    public bool VerboseDebugLogging { get; set; }
    public bool DebugPortHaggleEnabled { get; set; }
    public bool DebugPlanetHaggleEnabled { get; set; }
    public bool PreparedVmEnabled { get; set; } = true;
    public bool VmMetricsEnabled { get; set; } = true;
    public string PortHaggleMode { get; set; } = TWXProxy.Core.NativeHaggleModes.Default;
    public string PlanetHaggleMode { get; set; } = TWXProxy.Core.NativeHaggleModes.DefaultPlanet;
    public bool CommandDeckSkinEnabled { get; set; }

    private static string LegacySharedPrefsPath()
    {
        Directory.CreateDirectory(AppPaths.AppDataDir);
        return Path.Combine(AppPaths.AppDataDir, "prefs.xml");
    }

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

    public void SetDeckPanelLayout(string panelId, double left, double top, int zIndex, bool closed, bool minimized)
    {
        CommandDeckPanels[panelId] = new DeckPanelLayout
        {
            PanelId = panelId,
            Left = left,
            Top = top,
            ZIndex = zIndex,
            Closed = closed,
            Minimized = minimized,
        };
    }

    public void Save()
    {
        try
        {
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
                new XElement("PreparedVmEnabled", PreparedVmEnabled),
                new XElement("VmMetricsEnabled", VmMetricsEnabled),
                new XElement("PortHaggleMode", PortHaggleMode),
                new XElement("PlanetHaggleMode", PlanetHaggleMode),
                new XElement("CommandDeckSkinEnabled", CommandDeckSkinEnabled),
                new XElement("RecentFiles", RecentFiles.Select(path => new XElement("File", path))),
                new XElement("CommandDeckPanels",
                    CommandDeckPanels.Values
                        .OrderBy(layout => layout.PanelId, StringComparer.OrdinalIgnoreCase)
                        .Select(layout => new XElement("Panel",
                            new XAttribute("Id", layout.PanelId),
                            new XAttribute("Left", layout.Left.ToString(CultureInfo.InvariantCulture)),
                            new XAttribute("Top", layout.Top.ToString(CultureInfo.InvariantCulture)),
                            new XAttribute("ZIndex", layout.ZIndex),
                            new XAttribute("Closed", layout.Closed),
                            new XAttribute("Minimized", layout.Minimized))))
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
            if (bool.TryParse((string?)root.Element("PreparedVmEnabled"), out bool preparedVmEnabled))
                prefs.PreparedVmEnabled = preparedVmEnabled;
            if (bool.TryParse((string?)root.Element("VmMetricsEnabled"), out bool vmMetricsEnabled))
                prefs.VmMetricsEnabled = vmMetricsEnabled;
            if (bool.TryParse((string?)root.Element("CommandDeckSkinEnabled"), out bool commandDeckEnabled))
                prefs.CommandDeckSkinEnabled = commandDeckEnabled;

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
                    ZIndex = ParseInt(panel.Attribute("ZIndex")),
                    Closed = ParseBool(panel.Attribute("Closed")),
                    Minimized = ParseBool(panel.Attribute("Minimized")),
                };
            }

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
}
