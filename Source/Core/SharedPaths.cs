using System;
using System.IO;

namespace TWXProxy.Core;

/// <summary>
/// Shared filesystem paths that should be identical across MTC embedded-proxy mode
/// and the standalone TWXP proxy.
/// </summary>
public static class SharedPaths
{
    private const string ConfigFileName = "config.twx";
    private const string ProgramDirLocatorFileName = "programdir.txt";

    /// <summary>
     /// Shared TWX Proxy root directory.
     /// macOS   : ~/Library/twxproxy
     /// Windows : %LOCALAPPDATA%\twxproxy
     /// Linux   : ~/.local/share/twxproxy
    /// </summary>
    public static string AppDataDir => BuildSharedDataDir();

    /// <summary>Directory where shared .xdb database files are stored.</summary>
    public static string DatabaseDir => Path.Combine(AppDataDir, "databases");

    /// <summary>Directory where shared proxy capture/log files are stored.</summary>
    public static string LogDir => Path.Combine(AppDataDir, "logs");

    /// <summary>Path to the shared XML config file.</summary>
    public static string ConfigFilePath => GetConfigFilePath();

    /// <summary>Current shared TWX program directory locator target.</summary>
    public static string ProgramDir => ResolveProgramDir();

    /// <summary>Current shared TWX games directory.</summary>
    public static string GamesDir => GetGamesDir();

    /// <summary>Legacy shared game JSON directory used before the programdir migration.</summary>
    public static string LegacyGamesDir => Path.Combine(AppDataDir, "games");

    /// <summary>Persistent path locator used on non-Windows platforms to remember the program directory.</summary>
    public static string ProgramDirLocatorPath => Path.Combine(AppDataDir, ProgramDirLocatorFileName);

    /// <summary>Directory where shared expansion-module DLLs can be dropped for both apps.</summary>
    public static string ModulesDir => Path.Combine(AppDataDir, "modules");

    /// <summary>Legacy TWX proxy configuration file used for quick-load and bot metadata.</summary>
    public static string TwxpConfigPath => BuildTwxpConfigPath();

    /// <summary>Returns the shared .xdb path for a given game name.</summary>
    public static string DatabasePathForGame(string gameName)
    {
        string safe = SanitizeFileComponent(gameName);
        return Path.Combine(DatabaseDir, safe + ".xdb");
    }

    public static string GameConfigPathForGame(string gameName)
    {
        string safe = SanitizeFileComponent(gameName);
        return Path.Combine(GamesDir, safe + ".json");
    }

    public static void EnsureDatabaseDir() => Directory.CreateDirectory(DatabaseDir);
    public static void EnsureLogDir() => Directory.CreateDirectory(LogDir);
    public static void EnsureGamesDir()
    {
        Directory.CreateDirectory(GamesDir);
        MigrateLegacyGamesDir();
    }
    public static void EnsureModuleDir() => Directory.CreateDirectory(ModulesDir);

    public static string ResolveProgramDir(string? scriptDirectory = null)
    {
        if (!string.IsNullOrWhiteSpace(scriptDirectory))
        {
            string trimmed = NormalizeDirectory(scriptDirectory);
            string? parent = Path.GetDirectoryName(trimmed);
            if (!string.IsNullOrWhiteSpace(parent))
                return NormalizeDirectory(parent);

            return trimmed;
        }

        string? storedProgramDir = TryGetStoredProgramDir();
        if (!string.IsNullOrWhiteSpace(storedProgramDir))
            return NormalizeDirectory(storedProgramDir);

        if (!string.IsNullOrWhiteSpace(GlobalModules.ProgramDir))
            return NormalizeDirectory(GlobalModules.ProgramDir);

        if (OperatingSystem.IsWindows())
            return NormalizeDirectory(WindowsInstallInfo.GetInstalledProgramDirOrDefault());

        return NormalizeDirectory(GetDefaultProgramDir());
    }

    public static string GetConfigFilePath(string? programDir = null)
        => Path.Combine(NormalizeDirectory(programDir ?? ResolveProgramDir()), ConfigFileName);

    public static string GetGamesDir(string? programDir = null)
        => Path.Combine(NormalizeDirectory(programDir ?? ResolveProgramDir()), "games");

    public static bool IsPathUnderDirectory(string? path, string? directory)
    {
        if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(directory))
            return false;

        string fullPath = NormalizeDirectory(Path.GetFullPath(path));
        string fullDirectory = NormalizeDirectory(Path.GetFullPath(directory)) + Path.DirectorySeparatorChar;
        return fullPath.StartsWith(fullDirectory, OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal);
    }

    public static bool HasStoredProgramDir()
        => !string.IsNullOrWhiteSpace(TryGetStoredProgramDir());

    public static string? TryGetStoredProgramDir()
    {
        if (OperatingSystem.IsWindows())
            return NormalizeIfDirectoryExists(WindowsInstallInfo.TryGetInstalledProgramDir());

        try
        {
            if (!File.Exists(ProgramDirLocatorPath))
                return null;

            string? path = File.ReadAllText(ProgramDirLocatorPath).Trim();
            return NormalizeIfDirectoryExists(path);
        }
        catch
        {
            return null;
        }
    }

    public static void StoreProgramDir(string? programDir)
    {
        string? normalized = NormalizeIfDirectoryExists(programDir);
        if (string.IsNullOrWhiteSpace(normalized))
            return;

        if (OperatingSystem.IsWindows())
        {
            WindowsInstallInfo.SetInstalledProgramDir(normalized);
            return;
        }

        try
        {
            Directory.CreateDirectory(AppDataDir);
            File.WriteAllText(ProgramDirLocatorPath, normalized + Environment.NewLine);
        }
        catch
        {
            // Best-effort persistence.
        }
    }

    public static string GetDefaultProgramDir()
    {
        if (OperatingSystem.IsWindows())
            return WindowsInstallInfo.GetDefaultProgramDir();

        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return NormalizeDirectory(Path.Combine(home, "twx"));
    }

    public static string SanitizeFileComponent(string value)
    {
        string safe = string.Concat(value.Split(Path.GetInvalidFileNameChars()));
        return string.IsNullOrWhiteSpace(safe) ? "game" : safe;
    }

    private static string BuildSharedDataDir()
    {
        if (OperatingSystem.IsMacOS() || OperatingSystem.IsMacCatalyst())
        {
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return Path.Combine(home, "Library", "twxproxy");
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

    private static string BuildTwxpConfigPath()
    {
        if (OperatingSystem.IsWindows())
            return Path.Combine(WindowsInstallInfo.GetInstalledProgramDirOrDefault(), "twxp.cfg");

        return Path.Combine(AppDataDir, "twxp.cfg");
    }

    private static string NormalizeDirectory(string path)
        => Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

    private static string? NormalizeIfDirectoryExists(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;

        try
        {
            string normalized = NormalizeDirectory(path);
            return Directory.Exists(normalized) ? normalized : null;
        }
        catch
        {
            return null;
        }
    }

    private static void MigrateLegacyGamesDir()
    {
        try
        {
            string legacyDir = LegacyGamesDir;
            string activeDir = GamesDir;
            if (!Directory.Exists(legacyDir))
                return;

            foreach (string legacyPath in Directory.EnumerateFiles(legacyDir, "*.json"))
            {
                string destinationPath = Path.Combine(activeDir, Path.GetFileName(legacyPath));
                if (string.Equals(
                        NormalizeDirectory(legacyPath),
                        NormalizeDirectory(destinationPath),
                        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
                {
                    continue;
                }

                if (File.Exists(destinationPath))
                    continue;

                File.Move(legacyPath, destinationPath);
            }
        }
        catch
        {
            // Best-effort migration.
        }
    }
}
