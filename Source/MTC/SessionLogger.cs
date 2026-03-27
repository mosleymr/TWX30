using System.Text;
using System.Text.RegularExpressions;

namespace MTC;

/// <summary>
/// Logs the game session to a daily log file in ~/Library/mtc/logs/&lt;gamename&gt;-yyyy-MM-dd.log.
/// Captures all data received from the server (ANSI codes stripped) so the log matches
/// what the user sees on screen.  Thread-safe.
/// </summary>
public sealed class SessionLogger : IDisposable
{
    private static readonly Regex RxAnsi =
        new(@"\x1B(\[[0-9;]*[A-Za-z]|[()][AB012]|=)", RegexOptions.Compiled);

    private StreamWriter? _writer;
    private readonly object _lock = new();

    /// <summary>Returns true when a log file is currently open.</summary>
    public bool IsOpen { get { lock (_lock) return _writer != null; } }

    // ── Open / Close ───────────────────────────────────────────────────────

    /// <summary>
    /// Opens (or appends to) today's log file for <paramref name="gameName"/>.
    /// Closes any previously open file first.
    /// </summary>
    public void Open(string gameName)
    {
        lock (_lock)
        {
            CloseCore();

            string dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "Library", "mtc", "logs");
            Directory.CreateDirectory(dir);

            char[] invalid = Path.GetInvalidFileNameChars();
            string safe = string.Concat(gameName.Select(c => Array.IndexOf(invalid, c) >= 0 ? '_' : c));
            if (string.IsNullOrWhiteSpace(safe)) safe = "game";

            string path = Path.Combine(dir, $"{safe}-{DateTime.Now:yyyy-MM-dd}.log");
            _writer = new StreamWriter(path, append: true, Encoding.UTF8) { AutoFlush = true };
            _writer.WriteLine($"\r\n--- Session started {DateTime.Now:yyyy-MM-dd HH:mm:ss} ---");
        }
    }

    /// <summary>Writes a session-end marker and closes the file.</summary>
    public void Close()
    {
        lock (_lock) CloseCore();
    }

    private void CloseCore()
    {
        if (_writer == null) return;
        try
        {
            _writer.WriteLine($"\r\n--- Session ended {DateTime.Now:yyyy-MM-dd HH:mm:ss} ---");
            _writer.Dispose();
        }
        catch { /* best effort */ }
        _writer = null;
    }

    public void Dispose() => Close();

    // ── Logging ─────────────────────────────────────────────────────────--

    /// <summary>
    /// Logs a raw chunk of data received from the server (ANSI codes stripped).
    /// May be called from any thread.
    /// </summary>
    public void LogFromServer(string text)
    {
        if (string.IsNullOrEmpty(text)) return;
        lock (_lock)
        {
            if (_writer == null) return;
            string clean = RxAnsi.Replace(text, string.Empty);
            // Normalise CR LF / bare CR to LF so the log has consistent line endings.
            clean = clean.Replace("\r\n", "\n").Replace("\r", "\n");
            _writer.Write(clean);
        }
    }

}
