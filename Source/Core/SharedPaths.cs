using System;
using System.IO;

namespace TWXProxy.Core;

/// <summary>
/// Shared filesystem paths that should be identical across MTC embedded-proxy mode
/// and the standalone TWXP proxy.
/// </summary>
public static class SharedPaths
{
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

    /// <summary>Directory where shared expansion-module DLLs can be dropped for both apps.</summary>
    public static string ModulesDir => Path.Combine(AppDataDir, "modules");

    /// <summary>Returns the shared .xdb path for a given game name.</summary>
    public static string DatabasePathForGame(string gameName)
    {
        string safe = SanitizeFileComponent(gameName);
        return Path.Combine(DatabaseDir, safe + ".xdb");
    }

    public static void EnsureDatabaseDir() => Directory.CreateDirectory(DatabaseDir);
    public static void EnsureLogDir() => Directory.CreateDirectory(LogDir);
    public static void EnsureModuleDir() => Directory.CreateDirectory(ModulesDir);

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
}
