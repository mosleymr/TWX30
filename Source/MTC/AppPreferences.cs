using System.Xml.Linq;

namespace MTC;

/// <summary>
/// Lightweight application-level preferences (not per-connection).
/// Persisted under the shared twxproxy user-data root.
/// Stores UI-level preferences such as recent files and scripts directory.
/// </summary>
public class AppPreferences
{
    public const int MaxRecentFiles = 5;

    /// <summary>Paths of recently opened game configs or databases, newest first.</summary>
    public List<string> RecentFiles { get; } = [];

    /// <summary>Directory where the user stores TWX scripts. Empty = not configured.</summary>
    public string ScriptsDirectory { get; set; } = string.Empty;

    /// <summary>When true, writes debug output to the shared MTC debug log.</summary>
    public bool DebugLoggingEnabled { get; set; }

    /// <summary>When true, includes very high-frequency diagnostic logging.</summary>
    public bool VerboseDebugLogging { get; set; }

    /// <summary>When true, scripts prefer the prepared VM execution path.</summary>
    public bool PreparedVmEnabled { get; set; } = true;

    /// <summary>When true, VM load/execute metrics are written to the shared log.</summary>
    public bool VmMetricsEnabled { get; set; } = true;

    /// <summary>Global native haggle mode used by MTC across all games.</summary>
    public string NativeHaggleMode { get; set; } = TWXProxy.Core.NativeHaggleModes.ClampHeuristic;

    // ── Paths ──────────────────────────────────────────────────────────────

    private static string DefaultPath()
    {
        var dir = AppPaths.AppDataDir;
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, "prefs.xml");
    }

    private static string LegacyDefaultPath()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".config", "MTC");
        return Path.Combine(dir, "prefs.xml");
    }

    // ── Recent file helpers ────────────────────────────────────────────────

    /// <summary>
    /// Adds <paramref name="path"/> to the front of the recent list,
    /// de-duplicating and capping at <see cref="MaxRecentFiles"/>.
    /// </summary>
    public void AddRecent(string path)
    {
        RecentFiles.RemoveAll(p =>
            string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
        RecentFiles.Insert(0, path);
        while (RecentFiles.Count > MaxRecentFiles)
            RecentFiles.RemoveAt(RecentFiles.Count - 1);
    }

    // ── Serialisation ──────────────────────────────────────────────────────

    public void Save()
    {
        try
        {
            var doc = new XDocument(
                new XElement("MtcPrefs",
                    new XElement("ScriptsDirectory", ScriptsDirectory),
                    new XElement("DebugLoggingEnabled", DebugLoggingEnabled),
                    new XElement("VerboseDebugLogging", VerboseDebugLogging),
                    new XElement("PreparedVmEnabled", PreparedVmEnabled),
                    new XElement("VmMetricsEnabled", VmMetricsEnabled),
                    new XElement("NativeHaggleMode", NativeHaggleMode),
                    new XElement("RecentFiles",
                        RecentFiles.Select(p => new XElement("File", p))
                    )
                )
            );
            doc.Save(DefaultPath());
        }
        catch { /* non-fatal – prefs are best-effort */ }
    }

    public static AppPreferences Load()
    {
        var prefs = new AppPreferences();
        try
        {
            string path = DefaultPath();
            if (!File.Exists(path))
            {
                string legacyPath = LegacyDefaultPath();
                if (!File.Exists(legacyPath))
                    return prefs;
                path = legacyPath;
            }

            var root = XDocument.Load(path).Root;
            if (root == null) return prefs;

            string? sd = (string?)root.Element("ScriptsDirectory");
            if (!string.IsNullOrWhiteSpace(sd)) prefs.ScriptsDirectory = sd;

            if (bool.TryParse((string?)root.Element("DebugLoggingEnabled"), out bool debugEnabled))
                prefs.DebugLoggingEnabled = debugEnabled;
            if (bool.TryParse((string?)root.Element("VerboseDebugLogging"), out bool verboseEnabled))
                prefs.VerboseDebugLogging = verboseEnabled;
            if (bool.TryParse((string?)root.Element("PreparedVmEnabled"), out bool preparedVmEnabled))
                prefs.PreparedVmEnabled = preparedVmEnabled;
            if (bool.TryParse((string?)root.Element("VmMetricsEnabled"), out bool vmMetricsEnabled))
                prefs.VmMetricsEnabled = vmMetricsEnabled;

            string? nativeHaggleMode = (string?)root.Element("NativeHaggleMode");
            prefs.NativeHaggleMode = TWXProxy.Core.NativeHaggleModes.Normalize(nativeHaggleMode);

            foreach (var el in root.Element("RecentFiles")?.Elements("File")
                                   ?? Enumerable.Empty<XElement>())
            {
                string? p = (string?)el;
                if (!string.IsNullOrWhiteSpace(p) && File.Exists(p))
                    prefs.RecentFiles.Add(p);
            }
        }
        catch { /* ignore corrupt prefs */ }
        return prefs;
    }
}
