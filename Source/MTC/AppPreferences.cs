using System.Xml.Linq;

namespace MTC;

/// <summary>
/// Lightweight application-level preferences (not per-connection).
/// Persisted to <c>~/.config/MTC/prefs.xml</c>.
/// Currently stores the recent-files list.
/// </summary>
public class AppPreferences
{
    public const int MaxRecentFiles = 5;

    /// <summary>Paths of recently opened .mtc files, newest first.</summary>
    public List<string> RecentFiles { get; } = [];

    /// <summary>Directory where the user stores TWX scripts. Empty = not configured.</summary>
    public string ScriptsDirectory { get; set; } = string.Empty;

    // ── Paths ──────────────────────────────────────────────────────────────

    private static string DefaultPath()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".config", "MTC");
        Directory.CreateDirectory(dir);
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
            if (!File.Exists(path)) return prefs;

            var root = XDocument.Load(path).Root;
            if (root == null) return prefs;

            string? sd = (string?)root.Element("ScriptsDirectory");
            if (!string.IsNullOrWhiteSpace(sd)) prefs.ScriptsDirectory = sd;

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
