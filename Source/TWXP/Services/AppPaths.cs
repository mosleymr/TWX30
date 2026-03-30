namespace TWXP.Services;

/// <summary>
/// Resolves platform-specific paths for TWX Proxy application data.
/// All persistent files (game configs, databases, logs) live under AppDataDir.
/// </summary>
public static class AppPaths
{
    private static readonly string _appDataDir = BuildAppDataDir();

    /// <summary>
    /// Root directory for all TWX Proxy application data.
    ///   macOS   : ~/Library/Containers/&lt;bundle-id&gt;/Data/Library/Application Support/twxproxy/
    ///   Windows : C:\Users\&lt;Username&gt;\AppData\Local\twxproxy\
    ///   Linux   : ~/.local/share/twxproxy/
    /// </summary>
    public static string AppDataDir => _appDataDir;

    /// <summary>Directory where game configuration JSON is stored.</summary>
    public static string ConfigDir => AppDataDir;

    /// <summary>Path to the game registry JSON file (list of loaded game data file paths).</summary>
    public static string GameConfigFile => Path.Combine(ConfigDir, "gameconfigs.json");

    /// <summary>Directory where per-game data JSON files are stored.</summary>
    public static string GamesDir => Path.Combine(AppDataDir, "games");

    /// <summary>Returns the GAMENAME.json path for a given game name.</summary>
    public static string GameDataFileFor(string gameName)
    {
        string safe = string.Concat(gameName.Split(Path.GetInvalidFileNameChars()));
        if (string.IsNullOrWhiteSpace(safe)) safe = "game";
        return Path.Combine(GamesDir, safe + ".json");
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
    ///   Windows : C:\ProgramData\twxproxy\scripts
    ///   Linux   : /usr/local/share/twxproxy/scripts  (or XDG_DATA_DIRS fallback)
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
    public static string LogsDir => Path.Combine(AppDataDir, "logs");

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
        Directory.CreateDirectory(GamesDir);
        Directory.CreateDirectory(DatabaseDir);
        Directory.CreateDirectory(LegacyDatabaseDir);
        Directory.CreateDirectory(LogsDir);
        // Do NOT auto-create DefaultScriptDir — it may be a shared system path
        // that already exists or requires admin rights to create.
    }

    private static string BuildAppDataDir()
    {
        // OperatingSystem.IsMacOS/IsMacCatalyst work correctly on .NET for both macOS
        // and Mac Catalyst targets. RuntimeInformation.IsOSPlatform(OSPlatform.OSX) does
        // NOT match Mac Catalyst builds, causing silent fallthrough to the Linux branch.
        if (OperatingSystem.IsMacOS() || OperatingSystem.IsMacCatalyst())
        {
            // Use MAUI's FileSystem.AppDataDirectory which resolves correctly for both
            // sandboxed (~/Library/Containers/<id>/Data/Library/Application Support)
            // and non-sandboxed (~/Library/Application Support) Mac apps.
            try
            {
                return Path.Combine(Microsoft.Maui.Storage.FileSystem.AppDataDirectory, "twxproxy");
            }
            catch
            {
                // Fallback for unit-test / non-MAUI contexts
                string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                return Path.Combine(home, "Library", "Application Support", "twxproxy");
            }
        }

        if (OperatingSystem.IsWindows())
        {
            // C:\Users\<Username>\AppData\Local\twxproxy\
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return Path.Combine(localAppData, "twxproxy");
        }

        // Linux / other
        string xdgData = Environment.GetEnvironmentVariable("XDG_DATA_HOME")
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share");
        return Path.Combine(xdgData, "twxproxy");
    }

    private static string BuildDefaultScriptDir()
    {
        if (OperatingSystem.IsMacOS() || OperatingSystem.IsMacCatalyst())
        {
            // /Library/Application Support/twxproxy/scripts
            return Path.Combine("/Library", "Application Support", "twxproxy", "scripts");
        }

        if (OperatingSystem.IsWindows())
        {
            // C:\ProgramData\twxproxy\scripts
            string programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
            return Path.Combine(programData, "twxproxy", "scripts");
        }

        // Linux / other
        return "/usr/local/share/twxproxy/scripts";
    }}
