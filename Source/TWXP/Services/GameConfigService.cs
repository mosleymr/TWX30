using System.Collections.ObjectModel;
using TWXP.Models;

namespace TWXP.Services;

public interface IGameConfigService
{
    event EventHandler<string>? ScriptsDirectoryChanged;
    Task<ObservableCollection<GameConfig>> LoadConfigsAsync();
    Task SaveConfigAsync(GameConfig config);
    /// <summary>Remove the game from the active list without deleting its data file.</summary>
    Task RemoveConfigAsync(string configId);
    /// <summary>Remove the game from the active list and delete its GAMENAME.json data file.</summary>
    Task DeleteConfigAsync(string configId);
    Task<GameConfig?> GetConfigAsync(string configId);
    Task<string> GetScriptsDirectoryAsync();
    Task SetScriptsDirectoryAsync(string scriptDirectory);
    Task<string> GetPortHaggleModeAsync();
    Task SetPortHaggleModeAsync(string haggleMode);
    Task<string> GetPlanetHaggleModeAsync();
    Task SetPlanetHaggleModeAsync(string haggleMode);
}

public class GameConfigService : IGameConfigService
{
    // The registry file stores a JSON array of file paths to loaded GAMENAME.json files.
    private readonly string _registryFile;

    public event EventHandler<string>? ScriptsDirectoryChanged;

    public GameConfigService()
    {
        AppPaths.EnsureDirectories();
        _registryFile = AppPaths.GameConfigFile;
    }

    public static string GetDefaultScriptDirectory()
    {
        return AppPaths.DefaultScriptDir;
    }

    private static string NormalizeScriptDirectory(string? scriptDirectory)
    {
        if (string.IsNullOrWhiteSpace(scriptDirectory))
            return string.Empty;

        string trimmed = scriptDirectory.Trim();
        try
        {
            trimmed = Path.GetFullPath(trimmed);
        }
        catch
        {
            // Keep the original text if it cannot be normalized yet.
        }

        return trimmed.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    private static string ResolveEffectiveScriptDirectory(string? configuredScriptDirectory)
    {
        string normalized = NormalizeScriptDirectory(configuredScriptDirectory);
        return string.IsNullOrWhiteSpace(normalized) ? GetDefaultScriptDirectory() : normalized;
    }

    private static string NormalizePortHaggleMode(string? haggleMode)
    {
        string normalized = TWXProxy.Core.NativeHaggleModes.Normalize(haggleMode);
        return string.IsNullOrWhiteSpace(normalized)
            ? TWXProxy.Core.NativeHaggleModes.Default
            : normalized;
    }

    private static string NormalizePlanetHaggleMode(string? haggleMode)
    {
        string normalized = TWXProxy.Core.NativeHaggleModes.Normalize(haggleMode);
        return string.IsNullOrWhiteSpace(normalized)
            ? TWXProxy.Core.NativeHaggleModes.DefaultPlanet
            : normalized;
    }

    private static string NormalizeStoredPortHaggleMode(string? haggleMode) =>
        string.IsNullOrWhiteSpace(haggleMode) ? string.Empty : NormalizePortHaggleMode(haggleMode);

    private static string NormalizeStoredPlanetHaggleMode(string? haggleMode) =>
        string.IsNullOrWhiteSpace(haggleMode) ? string.Empty : NormalizePlanetHaggleMode(haggleMode);

    private static void ApplyDefaults(GameConfig config, string? registryScriptsDirectory)
    {
        config.ScriptDirectory = ResolveEffectiveScriptDirectory(registryScriptsDirectory);

        if (string.IsNullOrWhiteSpace(config.LoginScript))
        {
            config.LoginScript = "0_Login.cts";
        }

        if (config.CommandChar == '\0')
            config.CommandChar = '$';

        if (config.ListenPort == 0)
            config.ListenPort = 2300;

        if (config.Sectors <= 0)
            config.Sectors = 1000;

        if (config.BubbleSize <= 0)
            config.BubbleSize = 25;

        if (config.ReconnectDelaySeconds <= 0)
            config.ReconnectDelaySeconds = 5;

        if (config.MaxPlayDelay <= 0)
            config.MaxPlayDelay = 10000;
    }

    // -------------------------------------------------------------------------
    // Registry helpers (gameconfigs.json stores List<string> of file paths)
    // -------------------------------------------------------------------------

    private async Task<GameRegistry> LoadRegistryAsync()
    {
        if (!File.Exists(_registryFile))
        {
            if (File.Exists(AppPaths.LegacyGameConfigFile))
                return await MigrateLegacyRegistryAsync(AppPaths.LegacyGameConfigFile);
            return new GameRegistry();
        }

        try
        {
            var json = await File.ReadAllTextAsync(_registryFile);
            var trimmed = json.AsSpan().TrimStart();

            if (trimmed.Length == 0)
                return new GameRegistry();

            // New format: ["path1","path2"]  — first non-bracket char is '"'
            // Old format: [{...}]            — first non-bracket char is '{'
            if (trimmed[0] == '[')
            {
                if (trimmed.Length >= 2 && trimmed[1] == '{')
                {
                    // Migrate from old single-file format
                    var oldConfigs = System.Text.Json.JsonSerializer.Deserialize(
                        json, GameConfigJsonContext.Default.ListGameConfig);
                    if (oldConfigs != null)
                        return await MigrateOldConfigsAsync(oldConfigs);
                    return new GameRegistry();
                }

                return new GameRegistry
                {
                    Games = System.Text.Json.JsonSerializer.Deserialize(
                        json, GameConfigJsonContext.Default.ListString) ?? new List<string>()
                };
            }

            var registry = System.Text.Json.JsonSerializer.Deserialize(
                json, GameConfigJsonContext.Default.GameRegistry) ?? new GameRegistry();
            registry.Games ??= new List<string>();
            registry.ScriptsDirectory = NormalizeScriptDirectory(registry.ScriptsDirectory);
            registry.PortHaggleMode = NormalizeStoredPortHaggleMode(registry.PortHaggleMode);
            registry.PlanetHaggleMode = NormalizeStoredPlanetHaggleMode(registry.PlanetHaggleMode);
            return registry;
        }
        catch
        {
            return new GameRegistry();
        }
    }

    private async Task SaveRegistryAsync(GameRegistry registry)
    {
        registry.Games ??= new List<string>();
        registry.ScriptsDirectory = NormalizeScriptDirectory(registry.ScriptsDirectory);
        registry.PortHaggleMode = NormalizeStoredPortHaggleMode(registry.PortHaggleMode);
        registry.PlanetHaggleMode = NormalizeStoredPlanetHaggleMode(registry.PlanetHaggleMode);
        var json = System.Text.Json.JsonSerializer.Serialize(
            registry, GameConfigJsonContext.Default.GameRegistry);
        await File.WriteAllTextAsync(_registryFile, json);
    }

    /// <summary>
    /// One-time migration: write each old GameConfig to its own GAMENAME.json
    /// and return the resulting list of file paths.
    /// </summary>
    private async Task<GameRegistry> MigrateOldConfigsAsync(List<GameConfig> oldConfigs)
    {
        var registry = new GameRegistry();
        foreach (var config in oldConfigs)
        {
            if (string.IsNullOrWhiteSpace(registry.ScriptsDirectory) && !string.IsNullOrWhiteSpace(config.ScriptDirectory))
                registry.ScriptsDirectory = NormalizeScriptDirectory(config.ScriptDirectory);
            if (string.IsNullOrWhiteSpace(registry.PortHaggleMode) && !string.IsNullOrWhiteSpace(config.NativeHaggleMode))
                registry.PortHaggleMode = NormalizePortHaggleMode(config.NativeHaggleMode);

            ApplyDefaults(config, registry.ScriptsDirectory);
            if (string.IsNullOrEmpty(config.GameDataFilePath))
                config.GameDataFilePath = AppPaths.GameDataFileFor(config.Name);

            Directory.CreateDirectory(Path.GetDirectoryName(config.GameDataFilePath)!);
            string? runtimeScriptDirectory = config.ScriptDirectory;
            string? runtimeNativeHaggleMode = config.NativeHaggleMode;
            config.ScriptDirectory = null;
            config.NativeHaggleMode = null;
            var json = System.Text.Json.JsonSerializer.Serialize(
                config, GameConfigJsonContext.Default.GameConfig);
            config.ScriptDirectory = runtimeScriptDirectory;
            config.NativeHaggleMode = runtimeNativeHaggleMode;
            await File.WriteAllTextAsync(config.GameDataFilePath, json);
            registry.Games.Add(config.GameDataFilePath);
        }

        await SaveRegistryAsync(registry);
        return registry;
    }

    private async Task<GameRegistry> MigrateLegacyRegistryAsync(string legacyRegistryFile)
    {
        try
        {
            var json = await File.ReadAllTextAsync(legacyRegistryFile);
            var trimmed = json.AsSpan().TrimStart();

            if (trimmed.Length == 0)
                return new GameRegistry();

            if (trimmed[0] == '[' && trimmed.Length >= 2 && trimmed[1] == '{')
            {
                var oldConfigs = System.Text.Json.JsonSerializer.Deserialize(
                    json, GameConfigJsonContext.Default.ListGameConfig);
                if (oldConfigs != null)
                    return await MigrateOldConfigsAsync(oldConfigs);
                return new GameRegistry();
            }

            var oldPaths = System.Text.Json.JsonSerializer.Deserialize(
                json, GameConfigJsonContext.Default.ListString) ?? new List<string>();

            var registry = new GameRegistry();
            foreach (string oldPath in oldPaths)
            {
                if (!File.Exists(oldPath))
                    continue;

                GameConfig? config = await ReadGameFileAsync(oldPath);
                if (config == null)
                    continue;

                if (string.IsNullOrWhiteSpace(registry.ScriptsDirectory) && !string.IsNullOrWhiteSpace(config.ScriptDirectory))
                    registry.ScriptsDirectory = NormalizeScriptDirectory(config.ScriptDirectory);
                if (string.IsNullOrWhiteSpace(registry.PortHaggleMode) && !string.IsNullOrWhiteSpace(config.NativeHaggleMode))
                    registry.PortHaggleMode = NormalizePortHaggleMode(config.NativeHaggleMode);

                ApplyDefaults(config, registry.ScriptsDirectory);
                config.GameDataFilePath = AppPaths.GameDataFileFor(config.Name);
                Directory.CreateDirectory(Path.GetDirectoryName(config.GameDataFilePath)!);
                string? runtimeScriptDirectory = config.ScriptDirectory;
                string? runtimeNativeHaggleMode = config.NativeHaggleMode;
                config.ScriptDirectory = null;
                config.NativeHaggleMode = null;
                var gameJson = System.Text.Json.JsonSerializer.Serialize(
                    config, GameConfigJsonContext.Default.GameConfig);
                config.ScriptDirectory = runtimeScriptDirectory;
                config.NativeHaggleMode = runtimeNativeHaggleMode;
                await File.WriteAllTextAsync(config.GameDataFilePath, gameJson);
                registry.Games.Add(config.GameDataFilePath);
            }

            await SaveRegistryAsync(registry);
            return registry;
        }
        catch
        {
            return new GameRegistry();
        }
    }

    // -------------------------------------------------------------------------
    // Game-file helpers
    // -------------------------------------------------------------------------

    private static async Task<GameConfig?> ReadGameFileAsync(string filePath)
    {
        try
        {
            var json = await File.ReadAllTextAsync(filePath);
            return System.Text.Json.JsonSerializer.Deserialize(
                json, GameConfigJsonContext.Default.GameConfig);
        }
        catch
        {
            return null;
        }
    }

    // -------------------------------------------------------------------------
    // IGameConfigService implementation
    // -------------------------------------------------------------------------

    public async Task<ObservableCollection<GameConfig>> LoadConfigsAsync()
    {
        var registry = await LoadRegistryAsync();
        var configs = new ObservableCollection<GameConfig>();

        foreach (var filePath in registry.Games)
        {
            if (!File.Exists(filePath))
                continue;

            var config = await ReadGameFileAsync(filePath);
            if (config == null)
                continue;

            ApplyDefaults(config, registry.ScriptsDirectory);
            config.GameDataFilePath = filePath;
            configs.Add(config);
        }

        return configs;
    }

    public async Task SaveConfigAsync(GameConfig config)
    {
        var registry = await LoadRegistryAsync();
        string requestedScriptDirectory = NormalizeScriptDirectory(config.ScriptDirectory);
        string currentRegistryScriptDirectory = NormalizeScriptDirectory(registry.ScriptsDirectory);
        bool registryChanged = !string.Equals(
            requestedScriptDirectory,
            currentRegistryScriptDirectory,
            StringComparison.OrdinalIgnoreCase);

        if (registryChanged)
            registry.ScriptsDirectory = requestedScriptDirectory;

        if (!string.IsNullOrWhiteSpace(config.NativeHaggleMode))
        {
            string normalizedPortHaggleMode = NormalizePortHaggleMode(config.NativeHaggleMode);
            if (!string.Equals(registry.PortHaggleMode, normalizedPortHaggleMode, StringComparison.OrdinalIgnoreCase))
            {
                registry.PortHaggleMode = normalizedPortHaggleMode;
                registryChanged = true;
            }
        }

        ApplyDefaults(config, registry.ScriptsDirectory);

        // Determine the file path for this game's data file.
        if (string.IsNullOrEmpty(config.GameDataFilePath))
            config.GameDataFilePath = AppPaths.GameDataFileFor(config.Name);

        // Write the game data file (config + variables).
        Directory.CreateDirectory(Path.GetDirectoryName(config.GameDataFilePath)!);
        string? runtimeScriptDirectory = config.ScriptDirectory;
        string? runtimeNativeHaggleMode = config.NativeHaggleMode;
        config.ScriptDirectory = null;
        config.NativeHaggleMode = null;
        var json = System.Text.Json.JsonSerializer.Serialize(
            config, GameConfigJsonContext.Default.GameConfig);
        config.ScriptDirectory = runtimeScriptDirectory;
        config.NativeHaggleMode = runtimeNativeHaggleMode;
        await File.WriteAllTextAsync(config.GameDataFilePath, json);

        // Register the file path if not already tracked.
        if (!registry.Games.Contains(config.GameDataFilePath))
        {
            registry.Games.Add(config.GameDataFilePath);
            registryChanged = true;
        }

        if (registryChanged)
        {
            await SaveRegistryAsync(registry);
            ScriptsDirectoryChanged?.Invoke(this, ResolveEffectiveScriptDirectory(registry.ScriptsDirectory));
        }
    }

    public async Task RemoveConfigAsync(string configId)
    {
        // Remove from registry only — do NOT delete the game data file.
        var configs = await LoadConfigsAsync();
        var config = configs.FirstOrDefault(c => c.Id == configId);
        if (config == null || string.IsNullOrEmpty(config.GameDataFilePath))
            return;

        var registry = await LoadRegistryAsync();
        registry.Games.Remove(config.GameDataFilePath);
        await SaveRegistryAsync(registry);
    }

    public async Task DeleteConfigAsync(string configId)
    {
        // Same as RemoveConfigAsync: remove from the active registry only.
        // The game data file is kept on disk and can be re-loaded later.
        await RemoveConfigAsync(configId);
    }

    public async Task<GameConfig?> GetConfigAsync(string configId)
    {
        var registry = await LoadRegistryAsync();
        var configs = await LoadConfigsAsync();
        var config = configs.FirstOrDefault(c => c.Id == configId);
        if (config != null)
            ApplyDefaults(config, registry.ScriptsDirectory);
        return config;
    }

    public async Task<string> GetScriptsDirectoryAsync()
    {
        var registry = await LoadRegistryAsync();
        return ResolveEffectiveScriptDirectory(registry.ScriptsDirectory);
    }

    public async Task SetScriptsDirectoryAsync(string scriptDirectory)
    {
        var registry = await LoadRegistryAsync();
        string normalized = NormalizeScriptDirectory(scriptDirectory);
        if (string.Equals(
                normalized,
                NormalizeScriptDirectory(registry.ScriptsDirectory),
                StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        registry.ScriptsDirectory = normalized;
        await SaveRegistryAsync(registry);
        ScriptsDirectoryChanged?.Invoke(this, ResolveEffectiveScriptDirectory(registry.ScriptsDirectory));
    }

    public async Task<string> GetPortHaggleModeAsync()
    {
        var registry = await LoadRegistryAsync();
        return NormalizeStoredPortHaggleMode(registry.PortHaggleMode);
    }

    public async Task SetPortHaggleModeAsync(string haggleMode)
    {
        var registry = await LoadRegistryAsync();
        string normalized = NormalizePortHaggleMode(haggleMode);
        if (string.Equals(normalized, registry.PortHaggleMode, StringComparison.OrdinalIgnoreCase))
            return;

        registry.PortHaggleMode = normalized;
        await SaveRegistryAsync(registry);
    }

    public async Task<string> GetPlanetHaggleModeAsync()
    {
        var registry = await LoadRegistryAsync();
        return NormalizeStoredPlanetHaggleMode(registry.PlanetHaggleMode);
    }

    public async Task SetPlanetHaggleModeAsync(string haggleMode)
    {
        var registry = await LoadRegistryAsync();
        string normalized = NormalizePlanetHaggleMode(haggleMode);
        if (string.Equals(normalized, registry.PlanetHaggleMode, StringComparison.OrdinalIgnoreCase))
            return;

        registry.PlanetHaggleMode = normalized;
        await SaveRegistryAsync(registry);
    }
}
