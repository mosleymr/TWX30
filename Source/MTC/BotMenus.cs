using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using Avalonia;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using SkiaSharp;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Core = TWXProxy.Core;

namespace MTC;

public partial class MainWindow
{
    private void RebuildProxyMenu()
    {
        string gameName = _embeddedGameName ?? DeriveGameName();
        bool hasGame = !string.IsNullOrWhiteSpace(gameName);
        bool hasDatabase = _sessionDb != null;
        bool hasInterpreter = CurrentInterpreter != null;
        bool canPlayCapture = _gameInstance != null;
        bool canRunProxyScripts = hasInterpreter || CanUseRemoteProxyScripts();

        var proxyItems = BuildProxyMenuItems(gameName, hasGame, hasDatabase, hasInterpreter, canPlayCapture);
        _proxyMenu.ItemsSource = proxyItems;
        _proxyMenu.IsEnabled = _gameInstance != null;
        _botMenu.ItemsSource = BuildTopLevelBotMenuItems(hasInterpreter);
        _botMenu.IsEnabled = hasInterpreter;
        _quickMenu.ItemsSource = BuildQuickMenuItems(canRunProxyScripts);
        _quickMenu.IsEnabled = canRunProxyScripts;
        _scriptsMenu.IsEnabled = canRunProxyScripts;
        RebuildAiMenu();
        RefreshNativeAppMenu();
        RefreshNativeDockMenu();
    }

    private void RebuildAiMenu()
    {
        List<object> items = BuildAiMenuItems();
        _aiMenu.ItemsSource = items;
        bool hasItems = items.OfType<MenuItem>().Any(item => item.IsEnabled);
        _aiMenu.IsEnabled = hasItems;
        _aiMenu.IsVisible = hasItems;
    }

    private List<object> BuildProxyMenuItems(string gameName, bool hasGame, bool hasDatabase, bool hasInterpreter, bool canPlayCapture)
    {
        var items = new List<object>
        {
            new MenuItem
            {
                Header = hasGame ? EscapeMenuHeaderText($"Current Game: {gameName}") : "No game selected",
                IsEnabled = false,
            },
            new Separator(),
        };

        var stopMenu = new MenuItem { Header = "_Stop", IsEnabled = hasInterpreter };
        stopMenu.ItemsSource = BuildStopMenuItems();
        stopMenu.SubmenuOpened += (_, _) => stopMenu.ItemsSource = BuildStopMenuItems();
        items.Add(stopMenu);

        items.Add(new Separator());

        var exportMenu = new MenuItem { Header = "_Export", IsEnabled = hasDatabase };
        exportMenu.ItemsSource = BuildProxyExportItems(hasDatabase);
        items.Add(exportMenu);

        var importMenu = new MenuItem { Header = "_Import", IsEnabled = hasDatabase };
        importMenu.ItemsSource = BuildProxyImportItems(hasDatabase);
        items.Add(importMenu);

        var loggingMenu = new MenuItem { Header = "_Logging", IsEnabled = hasGame };
        loggingMenu.ItemsSource = BuildProxyLoggingItems(canPlayCapture, hasGame);
        items.Add(loggingMenu);
        items.Add(new Separator());

        int listenPort = GetConfiguredProxyListenPort();
        bool listenConfigured = _state.EmbeddedProxy && _state.ListenForConnections;
        bool listenerActive = _gameInstance?.IsLocalListenerActive == true;
        var listenItem = new MenuItem
        {
            Header = EscapeMenuHeaderText($"Listen on Port {listenPort}"),
            IsEnabled = _gameInstance != null && listenConfigured,
            ToggleType = MenuItemToggleType.CheckBox,
            IsChecked = listenerActive,
        };
        listenItem.Click += (_, _) => _ = ToggleProxyListenerAsync(!listenerActive);
        items.Add(listenItem);
        items.Add(new Separator());

        var advancedSettings = new MenuItem { Header = "_Advanced Settings…", IsEnabled = true };
        advancedSettings.Click += (_, _) => _ = OnAdvancedProxySettingsAsync();
        items.Add(advancedSettings);

        return items;
    }

    private int GetConfiguredProxyListenPort()
    {
        int port = _embeddedGameConfig?.ListenPort > 0
            ? _embeddedGameConfig.ListenPort
            : _state.ListenPort;
        return NormalizeListenPort(port);
    }

    private async Task ToggleProxyListenerAsync(bool enabled)
    {
        if (_gameInstance == null || !_state.ListenForConnections)
            return;

        int listenPort = GetConfiguredProxyListenPort();
        try
        {
            await _gameInstance.ConfigureLocalListenerAsync(enabled, listenPort);
            string status = enabled ? "listening" : "stopped listening";
            _parser.Feed($"\x1b[1;36m[Proxy {status} on port {listenPort}]\x1b[0m\r\n");
        }
        catch (Exception ex)
        {
            _parser.Feed($"\x1b[1;31m[Listen failed: {ex.Message}]\x1b[0m\r\n");
            Core.GlobalModules.DebugLog($"[MTC.ProxyMenu] failed to toggle listener: {ex}\n");
        }
        finally
        {
            _buffer.Dirty = true;
            RebuildProxyMenu();
        }
    }

    private List<object> BuildStopMenuItems()
    {
        var items = new List<object>();
        var interpreter = CurrentInterpreter;
        if (interpreter == null)
        {
            if (CanUseRemoteProxyScripts())
            {
                var killRemote = new MenuItem { Header = "_Kill Script by ID…" };
                killRemote.Click += (_, _) => _ = OnRemoteProxyKillScriptByIdAsync();
                items.Add(killRemote);
            }
            else
            {
                items.Add(new MenuItem { Header = "No proxy scripts active", IsEnabled = false });
            }
            return items;
        }

        var stopAll = new MenuItem { Header = "_All Scripts" };
        stopAll.Click += (_, _) => _ = OnProxyForceStopAllScriptsAsync(includeSystemScripts: false);
        items.Add(stopAll);

        var stopNonSystem = new MenuItem { Header = "All _Non-System Scripts" };
        stopNonSystem.Click += (_, _) => _ = OnProxyStopAllScriptsAsync(includeSystemScripts: false);
        items.Add(stopNonSystem);

        var scripts = Core.ProxyGameOperations.GetRunningScripts(interpreter);
        if (scripts.Count == 0)
        {
            items.Add(new Separator());
            items.Add(new MenuItem { Header = "No active scripts", IsEnabled = false });
            return items;
        }

        items.Add(new Separator());

        foreach (var script in scripts)
        {
            int scriptId = script.Id;
            var item = new MenuItem
            {
                Header = EscapeMenuHeaderText(script.IsSystemScript ? $"{script.Name} (system)" : script.Name)
            };
            item.Click += (_, _) => _ = OnProxyStopScriptAsync(scriptId);
            items.Add(item);
        }

        return items;
    }

    private List<object> BuildProxyExportItems(bool enabled)
    {
        var items = new List<object>();

        var exportWarps = new MenuItem { Header = "Export _Warps", IsEnabled = enabled };
        exportWarps.Click += (_, _) => _ = ExportWarpsAsync();
        items.Add(exportWarps);

        var exportBubbles = new MenuItem { Header = "Export _Bubbles", IsEnabled = enabled };
        exportBubbles.Click += (_, _) => _ = ExportBubblesAsync();
        items.Add(exportBubbles);

        var exportDeadends = new MenuItem { Header = "Export _Deadends", IsEnabled = enabled };
        exportDeadends.Click += (_, _) => _ = ExportDeadendsAsync();
        items.Add(exportDeadends);

        var exportTwx = new MenuItem { Header = "Export _TWX", IsEnabled = enabled };
        exportTwx.Click += (_, _) => _ = ExportTwxAsync();
        items.Add(exportTwx);

        return items;
    }

    private List<object> BuildProxyImportItems(bool enabled)
    {
        var items = new List<object>();

        var importWarps = new MenuItem { Header = "Import _Warps", IsEnabled = enabled };
        importWarps.Click += (_, _) => _ = ImportWarpsAsync();
        items.Add(importWarps);

        var importTwx = new MenuItem { Header = "Import T_WX", IsEnabled = enabled };
        importTwx.Click += (_, _) => _ = ImportTwxAsync();
        items.Add(importTwx);

        return items;
    }

    private List<object> BuildProxyLoggingItems(bool canPlayCapture, bool hasGame)
    {
        var items = new List<object>();

        var playCapture = new MenuItem { Header = "_Play Capture…", IsEnabled = canPlayCapture };
        playCapture.Click += (_, _) => _ = PlayCaptureAsync();
        items.Add(playCapture);

        var history = new MenuItem { Header = "_History…", IsEnabled = hasGame && _gameInstance != null };
        history.Click += (_, _) => _ = ShowProxyHistoryAsync();
        items.Add(history);

        var ansiCompanion = new MenuItem
        {
            Header = (_embeddedGameConfig?.LogAnsiCompanion ?? false) ? "Disable ANSI Companion Log" : "Record ANSI Companion Log",
            IsEnabled = hasGame,
        };
        ansiCompanion.Click += (_, _) => _ = ToggleAnsiCompanionLoggingAsync();
        items.Add(ansiCompanion);

        items.Add(new Separator());

        var debugPortHaggle = new MenuItem
        {
            Header = _appPrefs.DebugPortHaggleEnabled ? "Disable Port Haggle Debug" : "Debug Port Haggle",
            IsEnabled = true,
        };
        debugPortHaggle.Click += (_, _) => TogglePortHaggleDebugLogging();
        items.Add(debugPortHaggle);

        var debugPlanetHaggle = new MenuItem
        {
            Header = _appPrefs.DebugPlanetHaggleEnabled ? "Disable Planet Haggle Debug" : "Debug Planet Haggle",
            IsEnabled = true,
        };
        debugPlanetHaggle.Click += (_, _) => TogglePlanetHaggleDebugLogging();
        items.Add(debugPlanetHaggle);

        return items;
    }

    private async Task ToggleAnsiCompanionLoggingAsync()
    {
        string gameName = DeriveGameName();
        if (string.IsNullOrWhiteSpace(gameName))
            return;

        EmbeddedGameConfig config = _embeddedGameConfig ?? await LoadOrCreateEmbeddedGameConfigAsync(gameName);
        config.LogAnsiCompanion = !config.LogAnsiCompanion;
        _embeddedGameConfig = config;
        ApplySessionLogSettings(config);
        if (_gameInstance != null)
            _gameInstance.Logger.LogAnsiCompanion = config.LogAnsiCompanion;
        await SaveEmbeddedGameConfigAsync(gameName, config);

        string safeGameName = Core.SharedPaths.SanitizeFileComponent(gameName);
        string ansiPath = Path.Combine(AppPaths.GetDebugLogDir(), $"{DateTime.Today:yyyy-MM-dd} {safeGameName}_ansi.log");
        string status = config.LogAnsiCompanion ? "enabled" : "disabled";
        string pathText = config.LogAnsiCompanion ? $": {ansiPath}" : string.Empty;
        _parser.Feed($"\x1b[1;36m[ANSI companion log {status}{pathText}]\x1b[0m\r\n");
        _buffer.Dirty = true;
        RebuildScriptsMenu();
        RefreshNativeAppMenu();
    }

    private void TogglePortHaggleDebugLogging()
    {
        _appPrefs.DebugPortHaggleEnabled = !_appPrefs.DebugPortHaggleEnabled;
        _appPrefs.Save();
        ApplyDebugLoggingPreferences();
        string status = _appPrefs.DebugPortHaggleEnabled ? "enabled" : "disabled";
        _parser.Feed($"\x1b[1;36m[Port haggle debug {status}: {AppPaths.GetPortHaggleDebugLogPath(CurrentInterpreter?.ScriptDirectory ?? _appPrefs.ScriptsDirectory)}]\x1b[0m\r\n");
        _buffer.Dirty = true;
        RebuildScriptsMenu();
        RefreshNativeAppMenu();
    }

    private void TogglePlanetHaggleDebugLogging()
    {
        _appPrefs.DebugPlanetHaggleEnabled = !_appPrefs.DebugPlanetHaggleEnabled;
        _appPrefs.Save();
        ApplyDebugLoggingPreferences();
        string status = _appPrefs.DebugPlanetHaggleEnabled ? "enabled" : "disabled";
        _parser.Feed($"\x1b[1;36m[Planet haggle debug {status}: {AppPaths.GetPlanetHaggleDebugLogPath(CurrentInterpreter?.ScriptDirectory ?? _appPrefs.ScriptsDirectory)}]\x1b[0m\r\n");
        _buffer.Dirty = true;
        RebuildScriptsMenu();
        RefreshNativeAppMenu();
    }

    private List<object> BuildQuickMenuItems(bool enabled)
    {
        var items = new List<object>();
        if (!enabled)
        {
            items.Add(new MenuItem { Header = "Proxy scripts are not active", IsEnabled = false });
            return items;
        }

        string scriptDirectory = GetEffectiveProxyScriptDirectory();
        string programDir = GetEffectiveProxyProgramDir(scriptDirectory);
        var groups = Core.ProxyMenuCatalog.BuildQuickLoadGroups(programDir, scriptDirectory);

        foreach (var group in groups)
        {
            var groupMenu = new MenuItem { Header = EscapeMenuHeaderText(group.Name) };
            var groupItems = new List<object>();
            foreach (var entry in group.Entries)
            {
                string relativePath = entry.RelativePath;
                var item = new MenuItem { Header = EscapeMenuHeaderText(entry.DisplayName) };
                item.Click += (_, _) => _ = LoadQuickScriptAsync(relativePath);
                groupItems.Add(item);
            }

            groupMenu.ItemsSource = groupItems;
            items.Add(groupMenu);
        }

        if (groups.Count == 0)
            items.Add(new MenuItem { Header = "No quick-load scripts found", IsEnabled = false });

        return items;
    }

    private List<object> BuildAiMenuItems()
    {
        var items = new List<object>();
        if (_moduleHost == null)
            return items;

        string localModuleRoot = Path.GetFullPath(Path.Combine(GetEffectiveProxyProgramDir(GetEffectiveProxyScriptDirectory()), "modules"));
        var modules = _moduleHost
            .GetModules<Core.IExpansionChatModule>()
            .Where(binding =>
            {
                string assemblyPath = Path.GetFullPath(binding.Info.AssemblyPath);
                return assemblyPath.StartsWith(localModuleRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(Path.GetDirectoryName(assemblyPath), localModuleRoot, StringComparison.OrdinalIgnoreCase);
            })
            .ToArray();

        if (modules.Length == 0)
            return items;

        foreach (Core.ExpansionModuleBinding<Core.IExpansionChatModule> binding in modules)
        {
            string moduleId = binding.Info.Id;
            var item = new MenuItem
            {
                Header = EscapeMenuHeaderText(binding.Info.DisplayName),
            };
            item.Click += (_, _) => _ = OpenAiAssistantAsync(moduleId);
            items.Add(item);
        }

        return items;
    }

    private List<object> BuildTopLevelBotMenuItems(bool enabled)
    {
        var items = new List<object>();
        BotRuntimeState runtime = GetBotRuntimeState();
        IReadOnlyList<StoredBotSection> bots = LoadConfiguredBotSections();
        bool nativeConfigured = IsNativeMombotConfiguredForStart();
        bool hasStartableExternalBot = bots.Any(bot => !bot.IsNative && bot.ScriptAvailable);

        var startMenu = new MenuItem { Header = "_Start", IsEnabled = enabled && (nativeConfigured || hasStartableExternalBot) };
        startMenu.ItemsSource = BuildBotStartMenuItems(enabled, bots);
        startMenu.SubmenuOpened += (_, _) =>
            startMenu.ItemsSource = BuildBotStartMenuItems(enabled, LoadConfiguredBotSections());
        items.Add(startMenu);

        var stopItem = new MenuItem { Header = "S_top", IsEnabled = runtime.IsRunning };
        stopItem.Click += (_, _) => _ = StopActiveBotAsync();
        items.Add(stopItem);

        var configureMenu = new MenuItem { Header = "_Configure" };
        configureMenu.ItemsSource = BuildBotConfigureMenuItems(bots);
        configureMenu.SubmenuOpened += (_, _) =>
            configureMenu.ItemsSource = BuildBotConfigureMenuItems(LoadConfiguredBotSections());
        items.Add(configureMenu);

        var addBot = new MenuItem { Header = "_Add Bot…" };
        addBot.Click += (_, _) => _ = AddBotAsync();
        items.Add(addBot);

        return items;
    }

    private List<object> BuildBotStartMenuItems(bool proxyReady, IReadOnlyList<StoredBotSection> bots)
    {
        var items = new List<object>();
        if (!proxyReady || _gameInstance == null || CurrentInterpreter == null)
        {
            items.Add(new MenuItem { Header = "Embedded proxy is not running", IsEnabled = false });
            return items;
        }

        BotRuntimeState runtime = GetBotRuntimeState();
        StoredBotSection nativeBot = bots.First(bot => bot.IsNative);
        bool nativeConfigured = IsNativeMombotConfiguredForStart();
        var nativeItem = new MenuItem
        {
            Header = runtime.NativeRunning
                ? $"{NativeMombotMenuLabel} (running)"
                : nativeConfigured
                    ? NativeMombotMenuLabel
                    : $"{NativeMombotMenuLabel} (configure first)",
            IsEnabled = runtime.NativeRunning || nativeConfigured,
        };
        nativeItem.Click += (_, _) => _ = StartConfiguredBotAsync(nativeBot);
        items.Add(nativeItem);

        List<StoredBotSection> externalBots = bots
            .Where(bot => !bot.IsNative)
            .OrderBy(bot => bot.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (externalBots.Count == 0)
        {
            items.Add(new Separator());
            items.Add(new MenuItem { Header = "No external bots configured", IsEnabled = false });
            return items;
        }

        items.Add(new Separator());
        foreach (StoredBotSection bot in externalBots)
        {
            string header = bot.DisplayName;
            if (string.Equals(runtime.ExternalBotName, bot.Config.Name, StringComparison.OrdinalIgnoreCase))
                header += " (running)";
            else if (!bot.ScriptAvailable)
                header += " (script missing)";

            var item = new MenuItem
            {
                Header = EscapeMenuHeaderText(header),
                IsEnabled = bot.ScriptAvailable,
            };
            item.Click += (_, _) => _ = StartConfiguredBotAsync(bot);
            items.Add(item);
        }

        return items;
    }

    private List<object> BuildBotConfigureMenuItems(IReadOnlyList<StoredBotSection> bots)
    {
        var items = new List<object>();

        StoredBotSection nativeBot = bots.First(bot => bot.IsNative);
        var nativeItem = new MenuItem { Header = NativeMombotMenuLabel };
        nativeItem.Click += (_, _) => _ = ConfigureBotAsync(nativeBot);
        items.Add(nativeItem);

        List<StoredBotSection> externalBots = bots
            .Where(bot => !bot.IsNative)
            .OrderBy(bot => bot.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (externalBots.Count == 0)
        {
            items.Add(new Separator());
            items.Add(new MenuItem { Header = "No external bots configured", IsEnabled = false });
            return items;
        }

        items.Add(new Separator());
        foreach (StoredBotSection bot in externalBots)
        {
            var item = new MenuItem
            {
                Header = EscapeMenuHeaderText(bot.DisplayName),
            };
            item.Click += (_, _) => _ = ConfigureBotAsync(bot);
            items.Add(item);
        }

        return items;
    }

    private IReadOnlyList<StoredBotSection> LoadConfiguredBotSections()
    {
        string scriptDirectory = GetEffectiveProxyScriptDirectory();
        string programDir = GetEffectiveProxyProgramDir(scriptDirectory);
        IReadOnlyList<Core.TwxpConfigSection> sections = Core.TwxpConfigStore.LoadSections(programDir);
        var storedBots = new List<StoredBotSection>
        {
            CreateNativeStoredBotSection(programDir, scriptDirectory)
        };

        foreach (Core.TwxpConfigSection section in sections)
        {
            if (!section.Name.StartsWith("bot:", StringComparison.OrdinalIgnoreCase) ||
                Core.ProxyMenuCatalog.IsNativeBotSection(section))
            {
                continue;
            }

            storedBots.Add(CreateStoredBotSection(section, programDir, scriptDirectory));
        }

        return storedBots;
    }

    private StoredBotSection CreateNativeStoredBotSection(string programDir, string scriptDirectory)
    {
        Core.BotConfig config = BuildCurrentGameNativeBotConfig();
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Native"] = "1",
            ["Configured"] = config.Properties.TryGetValue("Configured", out string? configured) ? configured : "0",
            ["Name"] = config.Name,
            ["Script"] = config.ScriptFile,
            ["Description"] = config.Description,
            ["AutoStart"] = config.AutoStart ? "1" : "0",
            ["NameVar"] = config.NameVar,
            ["CommsVar"] = config.CommsVar,
            ["LoginScript"] = config.LoginScript,
            ["Theme"] = config.Theme,
        };

        return new StoredBotSection(
            Core.ProxyMenuCatalog.NativeMombotSectionName,
            Core.ProxyMenuCatalog.GetBotAlias(Core.ProxyMenuCatalog.NativeMombotSectionName),
            NativeMombotMenuLabel,
            true,
            BotScriptsExist(config, programDir, scriptDirectory),
            config,
            values);
    }

    private IReadOnlyList<Core.TwxpConfigSection> EnsureNativeBotSectionInTwxpCfg(string programDir)
    {
        List<Core.TwxpConfigSection> sections = Core.TwxpConfigStore.LoadSections(programDir).ToList();
        Dictionary<string, string> defaults = BuildDefaultNativeBotValues();
        int nativeIndex = sections.FindIndex(Core.ProxyMenuCatalog.IsNativeBotSection);
        bool changed = false;

        if (nativeIndex < 0)
        {
            sections.Add(new Core.TwxpConfigSection(Core.ProxyMenuCatalog.NativeMombotSectionName, defaults));
            changed = true;
        }
        else
        {
            Core.TwxpConfigSection existing = sections[nativeIndex];
            Dictionary<string, string> merged = MergeBotValues(existing.Values, defaults);
            if (!ConfigValuesEqual(existing.Values, merged))
            {
                sections[nativeIndex] = new Core.TwxpConfigSection(existing.Name, merged);
                changed = true;
            }
        }

        if (changed)
            Core.TwxpConfigStore.SaveSections(programDir, sections);

        return sections;
    }

    private StoredBotSection CreateStoredBotSection(Core.TwxpConfigSection section, string programDir, string scriptDirectory)
    {
        bool isNative = Core.ProxyMenuCatalog.IsNativeBotSection(section);
        var values = isNative
            ? MergeBotValues(section.Values, BuildDefaultNativeBotValues())
            : new Dictionary<string, string>(section.Values, StringComparer.OrdinalIgnoreCase);
        if (isNative)
            values["LoginScript"] = "disabled";

        string alias = isNative
            ? Core.ProxyMenuCatalog.GetBotAlias(Core.ProxyMenuCatalog.NativeMombotSectionName)
            : Core.ProxyMenuCatalog.GetBotAlias(section.Name);
        string displayName = values.TryGetValue("Name", out string? configuredName) && !string.IsNullOrWhiteSpace(configuredName)
            ? configuredName.Trim()
            : alias;
        string scriptList = values.TryGetValue("Script", out string? configuredScripts)
            ? NormalizeBotScriptList(configuredScripts)
            : string.Empty;
        List<string> scripts = scriptList
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(script => script.Replace('\\', '/'))
            .Where(script => !string.IsNullOrWhiteSpace(script))
            .ToList();

        var config = new Core.BotConfig
        {
            Alias = alias,
            Name = displayName,
            ScriptFile = scripts.FirstOrDefault() ?? string.Empty,
            ScriptFiles = scripts,
            Description = values.TryGetValue("Description", out string? description) ? description : string.Empty,
            AutoStart = ParseTwxpBool(values.TryGetValue("AutoStart", out string? autoStart) ? autoStart : null, fallback: !isNative),
            NameVar = values.TryGetValue("NameVar", out string? nameVar) ? nameVar : string.Empty,
            CommsVar = values.TryGetValue("CommsVar", out string? commsVar) ? commsVar : string.Empty,
            LoginScript = values.TryGetValue("LoginScript", out string? loginScript) ? loginScript : string.Empty,
            Theme = values.TryGetValue("Theme", out string? theme) ? theme : string.Empty,
            Properties = new Dictionary<string, string>(values, StringComparer.OrdinalIgnoreCase),
        };

        return new StoredBotSection(
            section.Name,
            alias,
            isNative ? NativeMombotMenuLabel : displayName,
            isNative,
            isNative || BotScriptsExist(config, programDir, scriptDirectory),
            config,
            values);
    }

}
