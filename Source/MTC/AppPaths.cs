using System;
using System.IO;
using Core = TWXProxy.Core;

namespace MTC;

/// <summary>
/// Resolves filesystem paths for MTC. User-visible data is shared under the
/// same ~/Library/twxproxy tree used by the standalone proxy.
/// </summary>
public static class AppPaths
{
    private static readonly string _appDataDir = Core.SharedPaths.AppDataDir;
    private static readonly string _legacyLocalAppDataDir = BuildLegacyLocalAppDataDir();

    /// <summary>Root directory for all MTC application data.</summary>
    public static string AppDataDir => _appDataDir;

    /// <summary>Directory where .xdb database files are stored.</summary>
    public static string DatabaseDir => Core.SharedPaths.DatabaseDir;

    /// <summary>Legacy pre-3.0 MTC-only database directory used for one-time migration.</summary>
    public static string LegacyDatabaseDir => Path.Combine(_legacyLocalAppDataDir, "databases");

    /// <summary>Directory where session/capture logs are stored.</summary>
    public static string LogDir => Core.SharedPaths.LogDir;

    /// <summary>Directory where MTC-only expansion modules can be placed.</summary>
    public static string ModulesDir => Path.Combine(AppDataDir, "modules-mtc");

    /// <summary>Directory where MTC expansion modules can persist per-module state.</summary>
    public static string ModuleDataDir => Path.Combine(AppDataDir, "module-data", "mtc");

    /// <summary>
    /// Directory where embedded-proxy mode shares databases with the standalone proxy.
    /// On macOS this is ~/Library/twxproxy/databases.
    /// </summary>
    public static string TwxproxyDatabaseDir => Core.SharedPaths.DatabaseDir;

    /// <summary>Directory where shared expansion modules can be placed for both MTC and TWXP.</summary>
    public static string SharedModulesDir => Core.SharedPaths.ModulesDir;

    /// <summary>Returns the .xdb path for a given game name.</summary>
    public static string DatabasePathForGame(string gameName)
    {
        string safe = string.Concat(gameName.Split(Path.GetInvalidFileNameChars()));
        if (string.IsNullOrWhiteSpace(safe)) safe = "game";
        return Path.Combine(DatabaseDir, safe + ".xdb");
    }

    public static string LegacyDatabasePathForGame(string gameName)
    {
        string safe = string.Concat(gameName.Split(Path.GetInvalidFileNameChars()));
        if (string.IsNullOrWhiteSpace(safe)) safe = "game";
        return Path.Combine(LegacyDatabaseDir, safe + ".xdb");
    }

    /// <summary>Returns the shared TWX Proxy .xdb path for a given game name.</summary>
    public static string TwxproxyDatabasePathForGame(string gameName)
        => Core.SharedPaths.DatabasePathForGame(gameName);

    /// <summary>Ensure required directories exist.</summary>
    public static void EnsureDirectories()
    {
        Directory.CreateDirectory(DatabaseDir);
        Directory.CreateDirectory(LogDir);
        Directory.CreateDirectory(TwxproxyGamesDir);
        Directory.CreateDirectory(ModulesDir);
        Directory.CreateDirectory(ModuleDataDir);
    }

    public static void EnsureTwxproxyDatabaseDir()
    {
        Core.SharedPaths.EnsureDatabaseDir();
    }

    public static void EnsureSharedModulesDir()
    {
        Core.SharedPaths.EnsureModuleDir();
    }

    /// <summary>
    /// Directory where shared game JSON files live (shared with the standalone proxy).
    /// </summary>
    public static string TwxproxyGamesDir => Core.SharedPaths.GamesDir;

    /// <summary>Returns the path to the shared TWXP game config JSON for a given game name.</summary>
    public static string TwxproxyGameConfigFileFor(string gameName) => Core.SharedPaths.GameConfigPathForGame(gameName);

    public static string TwxpConfigPath => Core.SharedPaths.TwxpConfigPath;

    /// <summary>Ensure the shared twxproxy games directory exists.</summary>
    public static void EnsureTwxproxyGamesDir() => Directory.CreateDirectory(TwxproxyGamesDir);

    private static string BuildLegacyLocalAppDataDir()
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

        string xdgData = Environment.GetEnvironmentVariable("XDG_DATA_HOME")
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share");
        return Path.Combine(xdgData, "mtc");
    }
}
