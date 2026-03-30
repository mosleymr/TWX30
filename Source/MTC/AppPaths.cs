using System;
using System.IO;
using Core = TWXProxy.Core;

namespace MTC;

/// <summary>
/// Resolves platform-specific paths for MTC application data.
/// Databases are stored under ~/Library/MTC/databases (macOS)
/// or the platform equivalent.
/// </summary>
public static class AppPaths
{
    private static readonly string _appDataDir = BuildAppDataDir();

    /// <summary>Root directory for all MTC application data.</summary>
    public static string AppDataDir => _appDataDir;

    /// <summary>Directory where .xdb database files are stored.</summary>
    public static string DatabaseDir => Path.Combine(AppDataDir, "databases");

    /// <summary>Directory where session/capture logs are stored.</summary>
    public static string LogDir => Path.Combine(AppDataDir, "logs");

    /// <summary>
    /// Directory where embedded-proxy mode shares databases with the standalone proxy.
    /// On macOS this is ~/Library/twxproxy/databases.
    /// </summary>
    public static string TwxproxyDatabaseDir => Core.SharedPaths.DatabaseDir;

    /// <summary>Returns the .xdb path for a given game name.</summary>
    public static string DatabasePathForGame(string gameName)
    {
        string safe = string.Concat(gameName.Split(Path.GetInvalidFileNameChars()));
        if (string.IsNullOrWhiteSpace(safe)) safe = "game";
        return Path.Combine(DatabaseDir, safe + ".xdb");
    }

    /// <summary>Returns the shared TWX Proxy .xdb path for a given game name.</summary>
    public static string TwxproxyDatabasePathForGame(string gameName)
        => Core.SharedPaths.DatabasePathForGame(gameName);

    /// <summary>Ensure required directories exist.</summary>
    public static void EnsureDirectories()
    {
        Directory.CreateDirectory(DatabaseDir);
        Directory.CreateDirectory(LogDir);
    }

    public static void EnsureTwxproxyDatabaseDir()
    {
        Core.SharedPaths.EnsureDatabaseDir();
    }

    /// <summary>
    /// Directory where shared TWXP game config JSON files live (shared with the standalone proxy).
    /// macOS   : ~/Library/Application Support/twxproxy/games/
    /// Windows : %APPDATA%\twxproxy\games\
    /// Linux   : ~/.local/share/twxproxy/games/
    /// </summary>
    public static string TwxproxyGamesDir
    {
        get
        {
            if (OperatingSystem.IsMacOS() || OperatingSystem.IsMacCatalyst())
            {
                string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                return Path.Combine(home, "Library", "Application Support", "twxproxy", "games");
            }
            if (OperatingSystem.IsWindows())
            {
                string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                return Path.Combine(appData, "twxproxy", "games");
            }
            // Linux / other (XDG)
            string xdgData = Environment.GetEnvironmentVariable("XDG_DATA_HOME")
                ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share");
            return Path.Combine(xdgData, "twxproxy", "games");
        }
    }

    /// <summary>Returns the path to the shared TWXP game config JSON for a given game name.</summary>
    public static string TwxproxyGameConfigFileFor(string gameName)
    {
        string safe = string.Concat(gameName.Split(Path.GetInvalidFileNameChars()));
        if (string.IsNullOrWhiteSpace(safe)) safe = "game";
        return Path.Combine(TwxproxyGamesDir, safe + ".json");
    }

    /// <summary>Ensure the shared twxproxy games directory exists.</summary>
    public static void EnsureTwxproxyGamesDir() => Directory.CreateDirectory(TwxproxyGamesDir);

    private static string BuildAppDataDir()
    {
        if (OperatingSystem.IsMacOS() || OperatingSystem.IsMacCatalyst())
        {
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return Path.Combine(home, "Library", "MTC");
        }

        if (OperatingSystem.IsWindows())
        {
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return Path.Combine(localAppData, "MTC");
        }

        // Linux / other
        string xdgData = Environment.GetEnvironmentVariable("XDG_DATA_HOME")
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share");
        return Path.Combine(xdgData, "mtc");
    }
}
