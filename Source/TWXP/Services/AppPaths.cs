namespace TWXP.Services;

/// <summary>
/// Resolves platform-specific paths for TWX Proxy application data.
/// All persistent files (game configs, databases, logs) live under AppDataDir.
/// </summary>
public static class AppPaths
{
    private static readonly string _appDataDir = TWXProxy.Core.SharedPaths.AppDataDir;
    private static readonly string _legacyAppDataDir = BuildLegacyAppDataDir();

    /// <summary>
    /// Root directory for all TWX Proxy application data.
    ///   macOS   : ~/Library/Containers/&lt;bundle-id&gt;/Data/Library/Application Support/twxproxy/
    ///   Windows : C:\Users\&lt;Username&gt;\AppData\Local\twxproxy\
    ///   Linux   : ~/.local/share/twxproxy/
    /// </summary>
    public static string AppDataDir => _appDataDir;

    /// <summary>Directory where game configuration JSON is stored.</summary>
    public static string ConfigDir => AppDataDir;

    /// <summary>Legacy pre-unification app-data root used for migration.</summary>
    public static string LegacyConfigDir => _legacyAppDataDir;

    /// <summary>Path to the game registry JSON file (list of loaded game data file paths).</summary>
    public static string GameConfigFile => Path.Combine(ConfigDir, "gameconfigs.json");

    public static string LegacyGameConfigFile => Path.Combine(LegacyConfigDir, "gameconfigs.json");

    /// <summary>Directory where per-game data JSON files are stored.</summary>
    public static string GamesDir => TWXProxy.Core.SharedPaths.GamesDir;

    /// <summary>Returns the GAMENAME.json path for a given game name.</summary>
    public static string GameDataFileFor(string gameName)
    {
        string safe = string.Concat(gameName.Split(Path.GetInvalidFileNameChars()));
        if (string.IsNullOrWhiteSpace(safe)) safe = "game";
        return TWXProxy.Core.SharedPaths.GameConfigPathForGame(gameName);
    }

    /// <summary>
    /// Directory where shared .xdb database files are stored.
    /// This is intentionally outside the app-specific config root so MTC embedded-proxy
    /// mode and the standalone proxy see the same universe database.
    /// </summary>
    public static string DatabaseDir => TWXProxy.Core.SharedPaths.DatabaseDir;

    /// <summary>
    /// Legacy per-app database directory used before the shared-database change.
    /// Still used for one-time migration of existing default databases.
    /// </summary>
    public static string LegacyDatabaseDir => Path.Combine(AppDataDir, "databases");

    /// <summary>
    /// Default scripts directory (used when a game config has no explicit ScriptDirectory).
    ///   macOS   : /Library/Application Support/twxproxy/scripts
    ///   Windows : ProgramDir\scripts, where ProgramDir comes from the installer registry key
    ///   Linux   : /usr/local/share/twxproxy/scripts
    /// Users can override this per-game in Settings.
    /// </summary>
    public static string DefaultScriptDir => BuildDefaultScriptDir();

    /// <summary>Returns the .xdb path for a given game name.</summary>
    public static string DatabasePathForGame(string gameName)
    {
        return TWXProxy.Core.SharedPaths.DatabasePathForGame(gameName);
    }

    public static string LegacyDatabasePathForGame(string gameName)
    {
        string safe = TWXProxy.Core.SharedPaths.SanitizeFileComponent(gameName);
        return Path.Combine(LegacyDatabaseDir, safe + ".xdb");
    }

    /// <summary>Directory where per-game debug logs are stored.</summary>
    public static string LogsDir => TWXProxy.Core.SharedPaths.LogDir;

    /// <summary>Directory where TWXP-only expansion modules can be placed.</summary>
    public static string ModulesDir => Path.Combine(AppDataDir, "modules-twxp");

    /// <summary>Directory where TWXP expansion modules can persist per-module state.</summary>
    public static string ModuleDataDir => Path.Combine(AppDataDir, "module-data", "twxp");

    /// <summary>Directory where shared expansion modules can be placed for both apps.</summary>
    public static string SharedModulesDir => TWXProxy.Core.SharedPaths.ModulesDir;

    /// <summary>Returns the debug log path for a given game name.</summary>
    public static string DebugLogPathForGame(string gameName)
    {
        string safe = string.Concat(gameName.Split(Path.GetInvalidFileNameChars()));
        if (string.IsNullOrWhiteSpace(safe)) safe = "game";
        return Path.Combine(LogsDir, safe + "_debug.log");
    }

    /// <summary>Ensure all required directories exist.</summary>
    public static void EnsureDirectories()
    {
        Directory.CreateDirectory(ConfigDir);
        TWXProxy.Core.SharedPaths.EnsureGamesDir();
        Directory.CreateDirectory(DatabaseDir);
        Directory.CreateDirectory(LegacyDatabaseDir);
        Directory.CreateDirectory(LogsDir);
        Directory.CreateDirectory(ModulesDir);
        Directory.CreateDirectory(ModuleDataDir);
        TWXProxy.Core.SharedPaths.EnsureModuleDir();
        // Do NOT auto-create DefaultScriptDir — it may be a shared system path
        // that already exists or requires admin rights to create.
    }

    private static string BuildDefaultScriptDir()
    {
        if (OperatingSystem.IsWindows())
        {
            return TWXProxy.Core.WindowsInstallInfo.GetDefaultScriptsDirectory();
        }

        if (OperatingSystem.IsMacOS() || OperatingSystem.IsMacCatalyst())
        {
            // /Library/Application Support/twxproxy/scripts
            return Path.Combine("/Library", "Application Support", "twxproxy", "scripts");
        }

        // Linux / other
        return "/usr/local/share/twxproxy/scripts";
    }

    private static string BuildLegacyAppDataDir()
    {
        if (OperatingSystem.IsMacOS() || OperatingSystem.IsMacCatalyst())
        {
            try
            {
                return Path.Combine(Microsoft.Maui.Storage.FileSystem.AppDataDirectory, "twxproxy");
            }
            catch
            {
                string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                return Path.Combine(home, "Library", "Application Support", "twxproxy");
            }
        }

        if (OperatingSystem.IsWindows())
        {
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return Path.Combine(localAppData, "twxproxy");
        }

        string xdgData = Environment.GetEnvironmentVariable("XDG_DATA_HOME")
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share");
        return Path.Combine(xdgData, "twxproxy");
    }
}
