using System.Diagnostics;
using System.Text;

namespace TWXProxy.Core;

/// <summary>
/// Holds an exclusive process-scoped lock for a game's JSON/database pair.
/// The lock file is intentionally left on disk; the OS releases the exclusive
/// handle when the owning process exits, so stale metadata does not block reuse.
/// </summary>
public sealed class GameFileLock : IDisposable
{
    private readonly FileStream _stream;
    private bool _disposed;

    private GameFileLock(string lockFilePath, FileStream stream)
    {
        LockFilePath = lockFilePath;
        _stream = stream;
    }

    public string LockFilePath { get; }

    public static GameFileLock Acquire(string owner, string configPath, string databasePath)
    {
        string lockFilePath = GetLockFilePath(configPath);
        Directory.CreateDirectory(Path.GetDirectoryName(lockFilePath)!);

        FileStream stream;
        try
        {
            stream = new FileStream(
                lockFilePath,
                FileMode.OpenOrCreate,
                FileAccess.ReadWrite,
                FileShare.None);
        }
        catch (IOException ex)
        {
            throw new IOException(
                $"Game files are already in use by another running process. Lock: {lockFilePath}",
                ex);
        }

        try
        {
            WriteMetadata(stream, owner, configPath, databasePath);
            return new GameFileLock(lockFilePath, stream);
        }
        catch
        {
            stream.Dispose();
            throw;
        }
    }

    public static string GetLockFilePath(string configPath)
    {
        string fullConfigPath = Path.GetFullPath(configPath);
        return fullConfigPath + ".lock";
    }

    private static void WriteMetadata(FileStream stream, string owner, string configPath, string databasePath)
    {
        var process = Process.GetCurrentProcess();
        string metadata = string.Join(
            Environment.NewLine,
            "{",
            $"  \"owner\": \"{Escape(owner)}\",",
            $"  \"pid\": {process.Id},",
            $"  \"processName\": \"{Escape(process.ProcessName)}\",",
            $"  \"configPath\": \"{Escape(Path.GetFullPath(configPath))}\",",
            $"  \"databasePath\": \"{Escape(Path.GetFullPath(databasePath))}\",",
            $"  \"acquiredUtc\": \"{DateTimeOffset.UtcNow:O}\"",
            "}",
            string.Empty);

        byte[] data = Encoding.UTF8.GetBytes(metadata);
        stream.SetLength(0);
        stream.Write(data, 0, data.Length);
        stream.Flush(flushToDisk: true);
        stream.Position = 0;
    }

    private static string Escape(string value)
    {
        return value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal)
            .Replace("\r", "\\r", StringComparison.Ordinal)
            .Replace("\n", "\\n", StringComparison.Ordinal);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _stream.Dispose();
    }
}
