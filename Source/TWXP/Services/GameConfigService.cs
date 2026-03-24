using System.Collections.ObjectModel;
using TWXP.Models;

namespace TWXP.Services;

public interface IGameConfigService
{
    Task<ObservableCollection<GameConfig>> LoadConfigsAsync();
    Task SaveConfigAsync(GameConfig config);
    /// <summary>Remove the game from the active list without deleting its data file.</summary>
    Task RemoveConfigAsync(string configId);
    /// <summary>Remove the game from the active list and delete its GAMENAME.json data file.</summary>
    Task DeleteConfigAsync(string configId);
    Task<GameConfig?> GetConfigAsync(string configId);
}

public class GameConfigService : IGameConfigService
{
    // The registry file stores a JSON array of file paths to loaded GAMENAME.json files.
    private readonly string _registryFile;

    public GameConfigService()
    {
        AppPaths.EnsureDirectories();
        _registryFile = AppPaths.GameConfigFile;
    }

    public static string GetDefaultScriptDirectory()
    {
        return AppPaths.DefaultScriptDir;
    }

    private static void ApplyDefaults(GameConfig config)
    {
        if (string.IsNullOrWhiteSpace(config.ScriptDirectory))
        {
            config.ScriptDirectory = GetDefaultScriptDirectory();
        }
    }

    // -------------------------------------------------------------------------
    // Registry helpers (gameconfigs.json stores List<string> of file paths)
    // -------------------------------------------------------------------------

    private async Task<List<string>> LoadRegistryAsync()
    {
        if (!File.Exists(_registryFile))
            return new List<string>();

        try
        {
            var json = await File.ReadAllTextAsync(_registryFile);
            var trimmed = json.AsSpan().TrimStart();

            // New format: ["path1","path2"]  — first non-bracket char is '"'
            // Old format: [{...}]            — first non-bracket char is '{'
            if (trimmed.Length >= 2 && trimmed[1] == '{')
            {
                // Migrate from old single-file format
                var oldConfigs = System.Text.Json.JsonSerializer.Deserialize(
                    json, GameConfigJsonContext.Default.ListGameConfig);
                if (oldConfigs != null)
                    return await MigrateOldConfigsAsync(oldConfigs);
                return new List<string>();
            }

            return System.Text.Json.JsonSerializer.Deserialize(
                json, GameConfigJsonContext.Default.ListString) ?? new List<string>();
        }
        catch
        {
            return new List<string>();
        }
    }

    private async Task SaveRegistryAsync(List<string> paths)
    {
        var json = System.Text.Json.JsonSerializer.Serialize(
            paths, GameConfigJsonContext.Default.ListString);
        await File.WriteAllTextAsync(_registryFile, json);
    }

    /// <summary>
    /// One-time migration: write each old GameConfig to its own GAMENAME.json
    /// and return the resulting list of file paths.
    /// </summary>
    private async Task<List<string>> MigrateOldConfigsAsync(List<GameConfig> oldConfigs)
    {
        var paths = new List<string>();
        foreach (var config in oldConfigs)
        {
            ApplyDefaults(config);
            if (string.IsNullOrEmpty(config.GameDataFilePath))
                config.GameDataFilePath = AppPaths.GameDataFileFor(config.Name);

            Directory.CreateDirectory(Path.GetDirectoryName(config.GameDataFilePath)!);
            var json = System.Text.Json.JsonSerializer.Serialize(
                config, GameConfigJsonContext.Default.GameConfig);
            await File.WriteAllTextAsync(config.GameDataFilePath, json);
            paths.Add(config.GameDataFilePath);
        }

        await SaveRegistryAsync(paths);
        return paths;
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

        foreach (var filePath in registry)
        {
            if (!File.Exists(filePath))
                continue;

            var config = await ReadGameFileAsync(filePath);
            if (config == null)
                continue;

            ApplyDefaults(config);
            config.GameDataFilePath = filePath;
            configs.Add(config);
        }

        return configs;
    }

    public async Task SaveConfigAsync(GameConfig config)
    {
        ApplyDefaults(config);

        // Determine the file path for this game's data file.
        if (string.IsNullOrEmpty(config.GameDataFilePath))
            config.GameDataFilePath = AppPaths.GameDataFileFor(config.Name);

        // Write the game data file (config + variables).
        Directory.CreateDirectory(Path.GetDirectoryName(config.GameDataFilePath)!);
        var json = System.Text.Json.JsonSerializer.Serialize(
            config, GameConfigJsonContext.Default.GameConfig);
        await File.WriteAllTextAsync(config.GameDataFilePath, json);

        // Register the file path if not already tracked.
        var registry = await LoadRegistryAsync();
        if (!registry.Contains(config.GameDataFilePath))
        {
            registry.Add(config.GameDataFilePath);
            await SaveRegistryAsync(registry);
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
        registry.Remove(config.GameDataFilePath);
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
        var configs = await LoadConfigsAsync();
        var config = configs.FirstOrDefault(c => c.Id == configId);
        if (config != null)
            ApplyDefaults(config);
        return config;
    }
}
