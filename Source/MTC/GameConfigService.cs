using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Core = TWXProxy.Core;

namespace MTC;

internal static class GameConfigService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
    };

    public static string NormalizeGameName(string? value)
    {
        string name = string.Concat((value ?? string.Empty).Split(Path.GetInvalidFileNameChars())).Trim();
        return string.IsNullOrWhiteSpace(name) ? "game" : name;
    }

    public static Dictionary<string, string> NormalizeVariables(IDictionary<string, string>? source)
    {
        var normalized = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (source == null)
            return normalized;

        foreach (KeyValuePair<string, string> entry in source)
            normalized[entry.Key] = entry.Value;

        return normalized;
    }

    public static string GameConfigPathForMode(string gameName, bool embeddedProxy)
        => embeddedProxy
            ? AppPaths.TwxproxyGameConfigFileFor(gameName)
            : AppPaths.MtcStandaloneGameConfigFileFor(gameName);

    public static string DatabasePathForMode(string gameName, bool embeddedProxy)
        => embeddedProxy
            ? AppPaths.TwxproxyDatabasePathForGame(gameName)
            : AppPaths.MtcStandaloneDatabasePathForGame(gameName);

    public static string GameConfigPathForConfig(EmbeddedGameConfig config)
        => GameConfigPathForMode(NormalizeGameName(config.Name), config.Mtc?.EmbeddedProxy ?? true);

    public static bool HasGameNameConflict(
        string gameName,
        bool embeddedProxy,
        string? currentConfigPath = null,
        string? currentDatabasePath = null)
    {
        string configPath = GameConfigPathForMode(gameName, embeddedProxy);
        if (File.Exists(configPath) &&
            !string.Equals(configPath, currentConfigPath, StringComparison.OrdinalIgnoreCase))
            return true;

        string databasePath = DatabasePathForMode(gameName, embeddedProxy);
        if (File.Exists(databasePath) &&
            !string.Equals(databasePath, currentDatabasePath, StringComparison.OrdinalIgnoreCase))
            return true;

        return false;
    }

    public static bool PathsEqualSafe(string? left, string? right)
    {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
            return false;

        try
        {
            return string.Equals(
                Path.GetFullPath(left),
                Path.GetFullPath(right),
                StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
        }
    }

    public static MTC.mombot.mombotConfig GetOrCreateMombotConfig(EmbeddedGameConfig config)
    {
        config.Mtc ??= new EmbeddedMtcConfig();
        config.Mtc.State ??= new EmbeddedMtcState();
        config.Mtc.Debug ??= new EmbeddedMtcDebugConfig();
        config.mombot ??= config.Mtc.mombot ?? new MTC.mombot.mombotConfig();
        config.Mtc.mombot = config.mombot;
        return config.mombot;
    }

    public static EmbeddedGameConfig NormalizeMombotConfig(EmbeddedGameConfig config)
    {
        _ = GetOrCreateMombotConfig(config);
        return config;
    }

    public static EmbeddedGameConfig BuildPersistableConfig(EmbeddedGameConfig source)
    {
        string snapshotJson = JsonSerializer.Serialize(source, JsonOptions);
        EmbeddedGameConfig persisted =
            JsonSerializer.Deserialize<EmbeddedGameConfig>(snapshotJson, JsonOptions) ??
            new EmbeddedGameConfig();

        NormalizeMombotConfig(persisted);
        MTC.mombot.mombotConfig persistedMombot = GetOrCreateMombotConfig(persisted);
        persistedMombot.Enabled = false;
        persistedMombot.WatcherEnabled = false;
        persisted.Variables = NormalizeVariables(persisted.Variables);
        return persisted;
    }

    public static EmbeddedGameConfig? TryLoadSharedGameConfig(string gameName)
    {
        try
        {
            string path = AppPaths.TwxproxyGameConfigFileFor(gameName);
            if (!File.Exists(path))
                return null;

            string json = File.ReadAllText(path);
            EmbeddedGameConfig? config = JsonSerializer.Deserialize<EmbeddedGameConfig>(json, JsonOptions);
            if (config == null)
                return null;

            config.Name = string.IsNullOrWhiteSpace(config.Name)
                ? NormalizeGameName(gameName)
                : NormalizeGameName(config.Name);
            config.DatabasePath = string.IsNullOrWhiteSpace(config.DatabasePath)
                ? AppPaths.TwxproxyDatabasePathForGame(config.Name)
                : config.DatabasePath;
            config.Variables = NormalizeVariables(config.Variables);
            return NormalizeMombotConfig(config);
        }
        catch
        {
            return null;
        }
    }

    public static async Task<EmbeddedGameConfig?> LoadConfigAsync(string path)
    {
        try
        {
            string json = await File.ReadAllTextAsync(path);
            EmbeddedGameConfig? config = JsonSerializer.Deserialize<EmbeddedGameConfig>(json, JsonOptions);
            if (config == null)
                return null;
            if (string.IsNullOrWhiteSpace(config.Name))
                config.Name = NormalizeGameName(Path.GetFileNameWithoutExtension(path));
            if (config.Sectors <= 0)
                config.Sectors = 1000;
            if (string.IsNullOrWhiteSpace(config.DatabasePath))
                config.DatabasePath = DatabasePathForMode(config.Name, config.Mtc?.EmbeddedProxy ?? true);
            config.Variables = NormalizeVariables(config.Variables);
            return NormalizeMombotConfig(config);
        }
        catch
        {
            return null;
        }
    }

    public static async Task SaveConfigAsync(string gameName, EmbeddedGameConfig config)
    {
        try
        {
            AppPaths.EnsureTwxproxyGamesDir();
            config.Name = string.IsNullOrWhiteSpace(config.Name)
                ? NormalizeGameName(gameName)
                : NormalizeGameName(config.Name);
            string path = GameConfigPathForConfig(config);
            EmbeddedGameConfig persisted = BuildPersistableConfig(config);
            string json = JsonSerializer.Serialize(persisted, JsonOptions);
            await File.WriteAllTextAsync(path, json);
        }
        catch (Exception ex)
        {
            Core.GlobalModules.DebugLog($"[MTC.GameConfig] save failed for '{gameName}': {ex}\n");
            Core.GlobalModules.FlushDebugLog();
        }
    }
}
