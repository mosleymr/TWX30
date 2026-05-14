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
    private async Task StartConfiguredBotAsync(StoredBotSection bot)
    {
        await Task.Yield();

        if (_gameInstance == null || CurrentInterpreter == null)
        {
            await ShowMessageAsync("Bot", "Bots can be started only while the embedded proxy is running.");
            return;
        }

        if (bot.IsNative)
        {
            if (!IsNativeMombotConfiguredForStart())
            {
                await ShowMessageAsync("Bot", "Configure native MomBot before starting it.");
                return;
            }

            StopActiveExternalBot();
            await StartInternalMombotAsync(bot.Config, requestedBotName: string.Empty, interactiveOfflinePrompt: true, publishMissingGameMessage: true);
        }
        else
        {
            if (!bot.ScriptAvailable)
            {
                await ShowMessageAsync("Bot", $"The script configured for {bot.DisplayName} could not be found.");
                return;
            }

            if (_mombot.Enabled)
                await StopInternalMombotAsync();

            CloseMombotInteractiveState();

            ReloadRegisteredBotConfigs();

            try
            {
                CurrentInterpreter.SwitchBot(string.Empty, bot.Config.Name, stopBotScripts: true);
                _parser.Feed($"\x1b[1;36m[Started bot: {bot.Config.Name}]\x1b[0m\r\n");
                _buffer.Dirty = true;
            }
            catch (Exception ex)
            {
                await ShowMessageAsync("Start Bot Failed", ex.Message);
            }
        }

        RefreshStatusBar();
        RebuildProxyMenu();
        FocusActiveTerminal();
    }

    private async Task TryAutoStartNativeBotAsync(string trigger)
    {
        await Task.Yield();

        if (_nativeBotAutoStartInFlight || _mombot.Enabled)
            return;

        if (_gameInstance == null || CurrentInterpreter == null)
            return;

        StoredBotSection? nativeBot = LoadConfiguredBotSections().FirstOrDefault(bot => bot.IsNative);
        if (nativeBot == null || !nativeBot.Config.AutoStart)
            return;

        if (!IsNativeMombotConfiguredForStart())
            return;

        if (!string.IsNullOrWhiteSpace(_gameInstance.ActiveBotName))
        {
            Core.GlobalModules.DebugLog(
                $"[MTC.NativeBotAutoStart] skipping trigger='{trigger}' activeBot='{_gameInstance.ActiveBotName}'\n");
            Core.GlobalModules.FlushDebugLog();
            return;
        }

        _nativeBotAutoStartInFlight = true;
        try
        {
            Core.GlobalModules.DebugLog(
                $"[MTC.NativeBotAutoStart] starting trigger='{trigger}' connected={_gameInstance.IsConnected} game='{_gameInstance.GameName}'\n");
            Core.GlobalModules.FlushDebugLog();

            await StartInternalMombotAsync(
                nativeBot.Config,
                requestedBotName: string.Empty,
                interactiveOfflinePrompt: false,
                publishMissingGameMessage: false);
        }
        finally
        {
            _nativeBotAutoStartInFlight = false;
        }
    }

    private async Task StopActiveBotAsync()
    {
        await Task.Yield();

        await _runtimeStopGate.WaitAsync();
        bool stoppedAny;
        try
        {
            stoppedAny = await StopActiveBotCoreAsync(
                publishNativeStopMessage: true,
                publishExternalStopMessage: true,
                suppressMissingGameMessage: false);
        }
        finally
        {
            _runtimeStopGate.Release();
        }

        if (stoppedAny)
        {
            RefreshStatusBar();
            RebuildProxyMenu();
            FocusActiveTerminal();
        }
    }

    private async Task<bool> StopActiveBotCoreAsync(
        bool publishNativeStopMessage,
        bool publishExternalStopMessage,
        bool suppressMissingGameMessage)
    {
        bool stoppedAny = false;
        if (_mombot.Enabled)
        {
            await StopInternalMombotCoreAsync(
                publishStopMessage: publishNativeStopMessage,
                suppressMissingGameMessage: suppressMissingGameMessage);
            stoppedAny = true;
        }

        if (StopActiveExternalBotCore(publishExternalStopMessage))
            stoppedAny = true;

        return stoppedAny;
    }

    private bool StopActiveExternalBot()
    {
        return StopActiveExternalBotCore(publishStopMessage: true);
    }

    private bool StopActiveExternalBotCore(bool publishStopMessage)
    {
        string activeBotName = _gameInstance?.ActiveBotName ?? string.Empty;
        if (string.IsNullOrWhiteSpace(activeBotName))
            return false;

        Core.ModInterpreter? interpreter = CurrentInterpreter;
        Core.BotConfig? botConfig = _gameInstance?.GetBotConfig(activeBotName);
        string scriptDirectory = interpreter?.ScriptDirectory ?? GetEffectiveProxyScriptDirectory();
        string programDir = interpreter?.ProgramDir ?? GetEffectiveProxyProgramDir(scriptDirectory);
        string lastLoadedModule = Core.ScriptRef.GetCurrentGameVar("$BOT~LAST_LOADED_MODULE", string.Empty);

        bool preserveShipDestroyed = HasNativeMombotShipDestroyedFlag();
        bool preserveDoNotResuscitate = HasNativeMombotDoNotResuscitateFlag() || preserveShipDestroyed;
        TraceRuntimeStop($"[BotStop] external begin bot='{activeBotName}' lastLoaded='{lastLoadedModule}' preserveDnr={preserveDoNotResuscitate} preserveShipDestroyed={preserveShipDestroyed}");
        ClearMombotRelogState(preserveDoNotResuscitate, preserveShipDestroyed);
        interpreter?.StopBot(activeBotName);

        int drainedScripts = 0;
        if (interpreter != null && botConfig != null)
            drainedScripts = StopScriptsForBotTree($"external:{activeBotName}", botConfig, lastLoadedModule, scriptDirectory, programDir);

        if (publishStopMessage)
        {
            string suffix = drainedScripts > 0 ? $" ({drainedScripts} module script{(drainedScripts == 1 ? string.Empty : "s")} drained)" : string.Empty;
            _parser.Feed($"\x1b[1;36m[Stopped active external bot{suffix}]\x1b[0m\r\n");
            _buffer.Dirty = true;
        }

        TraceRuntimeStop($"[BotStop] external complete bot='{activeBotName}' drained={drainedScripts}");
        return true;
    }

    private void TraceRuntimeStop(string message)
    {
        Core.GlobalModules.DebugLog(message + "\n");
        Core.GlobalModules.FlushDebugLog();
    }

    private void ClearMombotRelogState(bool preserveDoNotResuscitate = false, bool preserveShipDestroyed = false)
    {
        if (preserveShipDestroyed)
            preserveDoNotResuscitate = true;

        Core.ScriptRef.SetCurrentGameVar("$doRelog", "0");
        Core.ScriptRef.SetCurrentGameVar("$BOT~DORELOG", "0");
        if (preserveDoNotResuscitate)
        {
            Core.ScriptRef.SetCurrentGameVar("$BOT~DO_NOT_RESUSCITATE", "1");
            Core.ScriptRef.SetCurrentGameVar("$bot~do_not_resuscitate", "1");
            Core.ScriptRef.SetCurrentGameVar("$do_not_resuscitate", "1");
        }
        else
        {
            Core.ScriptRef.SetCurrentGameVar("$BOT~DO_NOT_RESUSCITATE", "0");
            Core.ScriptRef.SetCurrentGameVar("$bot~do_not_resuscitate", "0");
            Core.ScriptRef.SetCurrentGameVar("$do_not_resuscitate", "0");
        }
        Core.ScriptRef.SetCurrentGameVar("$relogging", "0");
        Core.ScriptRef.SetCurrentGameVar("$connectivity~relogging", "0");
        Core.ScriptRef.SetCurrentGameVar("$relog_message", string.Empty);
        Core.ScriptRef.SetCurrentGameVar("$BOT~LAST_LOADED_MODULE", string.Empty);
        Core.ScriptRef.SetCurrentGameVar("$BOT~MODE", "General");
        if (preserveShipDestroyed)
        {
            Core.ScriptRef.SetCurrentGameVar("$BOT~ISSHIPDESTROYED", "1");
            Core.ScriptRef.SetCurrentGameVar("$bot~isShipDestroyed", "1");
        }
        else
        {
            Core.ScriptRef.SetCurrentGameVar("$BOT~ISSHIPDESTROYED", "0");
            Core.ScriptRef.SetCurrentGameVar("$bot~isShipDestroyed", "0");
        }
        _mombotLastKeepaliveLine = string.Empty;
    }

    private void SuppressNativeMombotRelogState(bool preserveDoNotResuscitate, bool preserveShipDestroyed = false)
    {
        if (preserveShipDestroyed)
            preserveDoNotResuscitate = true;

        SetMombotSessionVar("$doRelog", "0");
        SetMombotSessionVar("$BOT~DORELOG", "0");
        SetMombotSessionVar("$relogging", "0");
        SetMombotSessionVar("$connectivity~relogging", "0");
        SetMombotSessionVar("$CONNECTIVITY~RELOGGING", "0");

        if (preserveDoNotResuscitate)
        {
            SetMombotSessionVar("$BOT~DO_NOT_RESUSCITATE", "1");
            SetMombotSessionVar("$bot~do_not_resuscitate", "1");
            SetMombotSessionVar("$do_not_resuscitate", "1");
        }

        if (preserveShipDestroyed)
        {
            SetMombotSessionVar("$BOT~ISSHIPDESTROYED", "1");
            SetMombotSessionVar("$bot~isShipDestroyed", "1");
        }
    }

    private void SetMombotSessionVar(string name, string value)
    {
        if (string.IsNullOrWhiteSpace(name))
            return;

        Core.ScriptRef.SetCurrentGameVar(name, value);

        Core.ModInterpreter? interpreter = CurrentInterpreter;
        if (interpreter == null)
            return;

        for (int i = 0; i < interpreter.Count; i++)
            interpreter.GetScript(i)?.SetScriptVarIgnoreCase(name, value);
    }

    private void ArmNativeMombotStartupDataGather()
    {
        _mombotStartupDataGatherPending = true;
        _mombotStartupDataGatherRunning = false;
        _mombotStartupPostInitPending = true;
        _mombotStartupFinalizeRunning = false;
    }

    private void ClearNativeMombotStartupDataGather()
    {
        _mombotStartupDataGatherPending = false;
        _mombotStartupDataGatherRunning = false;
        _mombotStartupPostInitPending = false;
        _mombotStartupFinalizeRunning = false;
    }

    private static bool HasNonEmptyMombotDataFile(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return false;

        try
        {
            return new FileInfo(path).Length > 0;
        }
        catch
        {
            return false;
        }
    }

    private bool HasCachedNativeMombotGameSettings()
    {
        static bool HasValue(string value) => !string.IsNullOrWhiteSpace(value) && value != "0";

        string portMax = ReadCurrentMombotVar("0", "$GAME~PORT_MAX", "$GAME~port_max", "$PORT_MAX", "$port_max");
        string photonDuration = ReadCurrentMombotVar("0", "$GAME~PHOTON_DURATION", "$game~photon_duration", "$PHOTON_DURATION", "$photon_duration");
        string mbbs = ReadCurrentMombotVar("0", "$GAME~MBBS", "$MBBS", "$mbbs");
        string ptrade = ReadCurrentMombotVar("0", "$GAME~PTRADESETTING", "$PTRADESETTING", "$ptradesetting");
        string stealFactor = ReadCurrentMombotVar("0", "$GAME~STEAL_FACTOR", "$STEAL_FACTOR", "$steal_factor");
        string robFactor = ReadCurrentMombotVar("0", "$GAME~ROB_FACTOR", "$ROB_FACTOR", "$rob_factor");

        return HasValue(portMax) &&
               (HasValue(photonDuration) ||
                HasValue(mbbs) ||
                HasValue(ptrade) ||
                HasValue(stealFactor) ||
                HasValue(robFactor));
    }

    private bool ShouldRunNativeMombotStartupRefresh()
    {
        string gconfigPath = ResolveMombotCurrentFilePath("$gconfig_file");
        string shipCapPath = ResolveMombotCurrentFilePath("$SHIP~cap_file");
        string planetFilePath = ResolveMombotCurrentFilePath("$PLANET~planet_file");
        string gameSettingsPath = ResolveMombotCurrentFilePath("$GAME~GAME_SETTINGS_FILE");

        if (!HasNonEmptyMombotDataFile(gconfigPath))
            return true;

        if (!HasNonEmptyMombotDataFile(shipCapPath))
            return true;

        if (!HasNonEmptyMombotDataFile(planetFilePath))
            return true;

        if (!HasNonEmptyMombotDataFile(gameSettingsPath) &&
            !HasCachedNativeMombotGameSettings())
            return true;

        return false;
    }

    private bool IsNativeMombotRefreshScriptLoaded()
        => IsNativeMombotScriptLoaded("refresh.cts");

    private bool TryStartNativeMombotStartupRefresh()
    {
        IReadOnlyList<MTC.mombot.mombotDispatchResult> results = _mombot.ExecuteCommandLine(
            "refresh",
            selfCommand: true,
            route: "startup",
            userName: "self");
        ApplyMombotExecutionRefresh();
        return results.Any(result => result.Success && result.Kind == MTC.mombot.mombotDispatchKind.Script);
    }

    private async Task TryRunNativeMombotInitialSettingsAsync()
    {
        await Task.Yield();

        if (!_mombotStartupDataGatherPending ||
            _mombotStartupDataGatherRunning ||
            !_mombot.Enabled ||
            _gameInstance == null ||
            !_gameInstance.IsConnected ||
            _gameInstance.IsProxyMenuActive)
        {
            return;
        }

        if (IsNativeMombotRelogScriptLoaded())
            return;

        string currentLine = NormalizeMombotPromptComparisonValue(Core.ScriptRef.GetCurrentLine());
        if (!TryGetMombotPromptNameFromLine(currentLine, out string promptName))
            return;

        SetMombotCurrentVars(promptName, "$PLAYER~CURRENT_PROMPT", "$PLAYER~startingLocation", "$bot~startingLocation");
        _mombotStartupDataGatherRunning = true;

        if (ShouldRunNativeMombotStartupRefresh() && !IsNativeMombotRefreshScriptLoaded())
        {
            if (!TryStartNativeMombotStartupRefresh())
            {
                _mombotStartupDataGatherRunning = false;
                return;
            }
        }

        _mombotStartupDataGatherPending = false;

        await FinalizeNativeMombotStartupAsync();
    }

    private async Task FinalizeNativeMombotStartupAsync()
    {
        if (_mombotStartupFinalizeRunning)
            return;

        _mombotStartupFinalizeRunning = true;
        try
        {
            await Task.Yield();

            if (!_mombotStartupPostInitPending ||
                _mombotStartupDataGatherPending ||
                !_mombot.Enabled ||
                _gameInstance == null ||
                !_gameInstance.IsConnected ||
                _gameInstance.IsProxyMenuActive ||
                IsNativeMombotRelogScriptLoaded())
            {
                return;
            }

            MombotPromptSurface promptSurface = GetMombotPromptSurface();
            if (promptSurface != MombotPromptSurface.Command &&
                promptSurface != MombotPromptSurface.Citadel)
            {
                return;
            }

            if (_mombotStartupDataGatherRunning)
            {
                if (IsNativeMombotRefreshScriptLoaded())
                    return;

                _mombotStartupDataGatherRunning = false;
            }

            _mombotStartupDataGatherRunning = false;
            _mombotStartupPostInitPending = false;
            LoadMombotStartupScripts();
            await SendMombotStartupAnnouncementsAsync();
            ApplyMombotExecutionRefresh();
        }
        finally
        {
            _mombotStartupFinalizeRunning = false;
        }
    }

    private bool IsNativeMombotScriptLoaded(string scriptReference)
    {
        Core.ModInterpreter? interpreter = CurrentInterpreter;
        if (interpreter == null || string.IsNullOrWhiteSpace(scriptReference))
            return false;

        string normalizedReference = scriptReference.Replace('\\', '/').Trim();
        return Core.ProxyGameOperations
            .GetRunningScripts(interpreter)
            .Any(script =>
                script.Reference.Replace('\\', '/').EndsWith(normalizedReference, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(Path.GetFileName(script.Reference.Replace('\\', '/')), Path.GetFileName(normalizedReference), StringComparison.OrdinalIgnoreCase) ||
                string.Equals(script.Name, scriptReference, StringComparison.OrdinalIgnoreCase));
    }

    private bool ShouldStopNativeMombotAfterDisconnect()
    {
        if (!_mombot.Enabled)
            return false;

        return HasNativeMombotDoNotResuscitateFlag() || HasNativeMombotShipDestroyedFlag();
    }

    private bool HasNativeMombotDoNotResuscitateFlag()
    {
        return AnyCurrentMombotVarTruthy("$BOT~DO_NOT_RESUSCITATE", "$bot~do_not_resuscitate", "$do_not_resuscitate");
    }

    private bool HasNativeMombotShipDestroyedFlag()
    {
        return AnyCurrentMombotVarTruthy("$BOT~ISSHIPDESTROYED", "$bot~isShipDestroyed");
    }

    private static bool AnyCurrentMombotVarTruthy(params string[] names)
    {
        foreach (string name in names)
        {
            if (IsMombotTruthy(Core.ScriptRef.GetCurrentGameVar(name, string.Empty)))
                return true;
        }

        return false;
    }

    private async Task StopNativeMombotAfterDisconnectAsync()
    {
        await _runtimeStopGate.WaitAsync();
        try
        {
            await StopInternalMombotCoreAsync(
                publishStopMessage: false,
                suppressMissingGameMessage: true);
        }
        finally
        {
            _runtimeStopGate.Release();
        }
    }

    private async Task HandleNativeMombotDisconnectAsync()
    {
        await Task.Yield();

        if (!_mombot.Enabled)
            return;

        // Intentional logoff can mark "do not resuscitate" just before or just
        // after the disconnect completes. Poll briefly so both the script-side
        // Connection Lost trigger and MTC's native relog path can honor that.
        DateTime stopDecisionDeadlineUtc = DateTime.UtcNow.AddSeconds(1.5);
        while (DateTime.UtcNow < stopDecisionDeadlineUtc)
        {
            if (!_mombot.Enabled)
                return;

            if (ShouldStopNativeMombotAfterDisconnect())
            {
                SuppressNativeMombotRelogState(
                    preserveDoNotResuscitate: true,
                    preserveShipDestroyed: HasNativeMombotShipDestroyedFlag());
                await StopNativeMombotAfterDisconnectAsync();
                return;
            }

            await Task.Delay(100);
        }

        if (!_mombot.Enabled)
            return;

        if (ShouldStopNativeMombotAfterDisconnect())
        {
            SuppressNativeMombotRelogState(
                preserveDoNotResuscitate: true,
                preserveShipDestroyed: HasNativeMombotShipDestroyedFlag());
            await StopNativeMombotAfterDisconnectAsync();
            return;
        }

        await TriggerNativeMombotRelogAsync(
            relogMessage: string.Empty,
            disconnectFirst: false);
    }

    private int StopScriptsForBotTree(string origin, Core.BotConfig config, string lastLoadedModule, string scriptDirectory, string programDir)
    {
        IReadOnlyList<string> directScriptPaths = GetConfiguredBotScriptPaths(config, scriptDirectory);
        string? scriptRootPath = GetConfiguredBotScriptRootPath(config, scriptDirectory);
        return StopScriptsMatchingTree(origin, directScriptPaths, scriptRootPath, lastLoadedModule, scriptDirectory, programDir);
    }

    private int StopScriptsMatchingTree(
        string origin,
        IReadOnlyList<string> directScriptPaths,
        string? scriptRootPath,
        string lastLoadedModule,
        string scriptDirectory,
        string programDir)
    {
        Core.ModInterpreter? interpreter = CurrentInterpreter;
        if (interpreter == null)
            return 0;

        var normalizedDirectScripts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string directScript in directScriptPaths)
        {
            string normalized = NormalizeScriptStopPath(directScript, scriptDirectory, programDir);
            if (!string.IsNullOrWhiteSpace(normalized))
                normalizedDirectScripts.Add(normalized);
        }

        int totalStopped = 0;
        for (int pass = 1; pass <= 3; pass++)
        {
            int stoppedThisPass = 0;
            IReadOnlyList<Core.RunningScriptInfo> runningScripts = Core.ProxyGameOperations.GetRunningScripts(interpreter);
            foreach (Core.RunningScriptInfo script in runningScripts)
            {
                string reference = string.IsNullOrWhiteSpace(script.Reference) ? script.Name : script.Reference;
                if (!ShouldStopBotScript(reference, normalizedDirectScripts, scriptRootPath, lastLoadedModule, scriptDirectory, programDir))
                    continue;

                TraceRuntimeStop($"[BotStop] {origin} pass={pass} stopping ref='{reference}' display='{script.Name}'");
                if (Core.ProxyGameOperations.StopScriptByName(interpreter, reference))
                    stoppedThisPass++;
            }

            totalStopped += stoppedThisPass;
            if (stoppedThisPass == 0)
                break;
        }

        return totalStopped;
    }

    private static bool ShouldStopBotScript(
        string reference,
        HashSet<string> normalizedDirectScripts,
        string? scriptRootPath,
        string lastLoadedModule,
        string scriptDirectory,
        string programDir)
    {
        string normalizedReference = NormalizeScriptStopPath(reference, scriptDirectory, programDir);
        if (normalizedDirectScripts.Contains(normalizedReference))
            return true;

        if (!string.IsNullOrWhiteSpace(scriptRootPath) && IsScriptUnderRoot(normalizedReference, scriptRootPath))
            return true;

        return !string.IsNullOrWhiteSpace(lastLoadedModule) &&
               (ScriptReferenceMatches(reference, lastLoadedModule, scriptDirectory, programDir) ||
                string.Equals(
                    normalizedReference,
                    NormalizeScriptStopPath(lastLoadedModule, scriptDirectory, programDir),
                    StringComparison.OrdinalIgnoreCase));
    }

    private static IReadOnlyList<string> GetConfiguredBotScriptPaths(Core.BotConfig config, string scriptDirectory)
    {
        IReadOnlyList<string> scripts = config.ScriptFiles.Count > 0
            ? config.ScriptFiles
            : string.IsNullOrWhiteSpace(config.ScriptFile)
                ? Array.Empty<string>()
                : new[] { config.ScriptFile };

        return scripts
            .Where(script => !string.IsNullOrWhiteSpace(script))
            .Select(script => NormalizeScriptStopPath(script, scriptDirectory, scriptDirectory))
            .Where(script => !string.IsNullOrWhiteSpace(script))
            .ToArray();
    }

    private static string? GetConfiguredBotScriptRootPath(Core.BotConfig config, string scriptDirectory)
    {
        string script = config.ScriptFiles.Count > 0
            ? config.ScriptFiles[0]
            : config.ScriptFile;
        if (string.IsNullOrWhiteSpace(script))
            return null;

        string normalized = script.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar).Trim();
        string? directory = Path.GetDirectoryName(normalized);
        if (string.IsNullOrWhiteSpace(directory))
            return Path.GetFullPath(scriptDirectory);

        return Path.GetFullPath(Path.IsPathRooted(directory)
            ? directory
            : Path.Combine(scriptDirectory, directory));
    }

    private static bool ScriptReferenceMatches(string left, string right, string scriptDirectory, string programDir)
    {
        string leftScriptDir = NormalizeScriptStopPath(left, scriptDirectory, programDir);
        string rightScriptDir = NormalizeScriptStopPath(right, scriptDirectory, programDir);
        if (leftScriptDir.Equals(rightScriptDir, StringComparison.OrdinalIgnoreCase))
            return true;

        string leftProgramDir = NormalizeScriptStopPath(left, programDir, scriptDirectory);
        string rightProgramDir = NormalizeScriptStopPath(right, programDir, scriptDirectory);
        if (leftProgramDir.Equals(rightProgramDir, StringComparison.OrdinalIgnoreCase))
            return true;

        string leftLeaf = Path.GetFileName(left.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar).Trim());
        string rightLeaf = Path.GetFileName(right.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar).Trim());
        return !string.IsNullOrWhiteSpace(leftLeaf) &&
               leftLeaf.Equals(rightLeaf, StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeScriptStopPath(string reference, string primaryBaseDir, string secondaryBaseDir)
    {
        if (string.IsNullOrWhiteSpace(reference))
            return string.Empty;

        string normalized = reference
            .Trim()
            .Replace('\\', Path.DirectorySeparatorChar)
            .Replace('/', Path.DirectorySeparatorChar);

        if (Path.IsPathRooted(normalized))
        {
            try
            {
                return Path.GetFullPath(normalized);
            }
            catch
            {
                return normalized;
            }
        }

        foreach (string baseDir in new[] { primaryBaseDir, secondaryBaseDir })
        {
            if (string.IsNullOrWhiteSpace(baseDir))
                continue;

            try
            {
                return Path.GetFullPath(Path.Combine(baseDir, normalized));
            }
            catch
            {
            }
        }

        return normalized;
    }

    private static bool IsScriptUnderRoot(string scriptPath, string scriptRootPath)
    {
        if (string.IsNullOrWhiteSpace(scriptPath) || string.IsNullOrWhiteSpace(scriptRootPath))
            return false;

        string normalizedRoot = scriptRootPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (string.Equals(scriptPath, normalizedRoot, StringComparison.OrdinalIgnoreCase))
            return true;

        string prefix = normalizedRoot + Path.DirectorySeparatorChar;
        return scriptPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }

    private async Task ConfigureBotAsync(StoredBotSection bot)
    {
        BotConfigDialogResult defaults = BuildBotDialogDefaults(bot);
        BotConfigDialogResult? result;
        if (bot.IsNative)
        {
            MTC.mombot.mombotRelogDialogResult relogDefaults = BuildMombotRelogDefaults();
            bool needsRelogSetup = ShouldPromptForMombotRelogSettings(relogDefaults) ||
                ShouldReviewMombotRelogSettings(relogDefaults);

            if (needsRelogSetup)
            {
                var relogDialog = new MTC.mombot.mombotRelogDialog(relogDefaults);
                if (!await relogDialog.ShowDialog<bool>(this) || relogDialog.Result == null)
                {
                    FocusActiveTerminal();
                    return;
                }

                ApplyMombotRelogDialogResult(relogDialog.Result);
                await SaveCurrentGameConfigAsync();
                ReloadRegisteredBotConfigs();
                SyncMombotRuntimeConfigFromTwxpCfg();
                if (_mombot.IsAttached)
                    _mombot.ApplyConfig(_embeddedGameConfig != null ? GetOrCreateEmbeddedMombotConfig(_embeddedGameConfig) : null);
                RefreshActiveBotContextFromConfig(bot);
                RefreshStatusBar();
                RebuildProxyMenu();
                FocusActiveTerminal();
                return;
            }

            var dialog = new MTC.mombot.mombotNativeConfigDialog("Configure MomBot (native)", defaults);
            if (!await dialog.ShowDialog<bool>(this) || dialog.Result == null)
            {
                FocusActiveTerminal();
                return;
            }

            result = dialog.Result;
        }
        else
        {
            var dialog = new BotConfigDialog($"Configure {bot.DisplayName}", defaults, isNative: false);
            if (!await dialog.ShowDialog<bool>(this) || dialog.Result == null)
            {
                FocusActiveTerminal();
                return;
            }

            result = dialog.Result;
        }

        if (!TryValidateBotDialogResult(result, bot.IsNative, bot.SectionName, out string error, out BotConfigDialogResult normalized))
        {
            await ShowMessageAsync("Bot", error);
            return;
        }

        SaveBotSection(bot, normalized);

        ReloadRegisteredBotConfigs();
        SyncMombotRuntimeConfigFromTwxpCfg();
        if (_mombot.IsAttached)
            _mombot.ApplyConfig(_embeddedGameConfig != null ? GetOrCreateEmbeddedMombotConfig(_embeddedGameConfig) : null);
        RefreshActiveBotContextFromConfig(bot);

        RefreshStatusBar();
        RebuildProxyMenu();
        FocusActiveTerminal();
    }

    private async Task AddBotAsync()
    {
        var dialog = new BotConfigDialog(
            "Add Bot",
            new BotConfigDialogResult(
                Alias: "newbot",
                Name: "New Bot",
                Script: "mombot/mombot.cts",
                Description: string.Empty,
                AutoStart: false,
                NameVar: "BotName",
                CommsVar: "BotComms",
                ServerName: string.Empty,
                LoginName: string.Empty,
                GameLetter: string.Empty,
                LoginScript: "0_Login.cts",
                Theme: "5|[BOT]|~D|~G"),
            isNative: false);
        if (!await dialog.ShowDialog<bool>(this) || dialog.Result == null)
        {
            FocusActiveTerminal();
            return;
        }

        if (!TryValidateBotDialogResult(dialog.Result, isNative: false, currentSectionName: null, out string error, out BotConfigDialogResult normalized))
        {
            await ShowMessageAsync("Bot", error);
            return;
        }

        SaveBotSection(existing: null, normalized);
        ReloadRegisteredBotConfigs();
        RebuildProxyMenu();
        FocusActiveTerminal();
    }

    private BotConfigDialogResult BuildBotDialogDefaults(StoredBotSection bot)
    {
        string dialogNameValue = bot.IsNative
            ? ReadCurrentMombotVar(
                bot.Config.Name,
                "$BOT~BOT_NAME",
                "$SWITCHBOARD~BOT_NAME",
                "$SWITCHBOARD~bot_name",
                "$bot~bot_name",
                "$bot_name",
                "$bot~name")
            : bot.Config.NameVar;
        string dialogCommsValue = bot.IsNative
            ? ReadCurrentMombotVar(
                dialogNameValue,
                "$BOT~BOT_TEAM_NAME",
                "$BOT~bot_team_name",
                "$bot~bot_team_name",
                "$bot_team_name")
            : bot.Config.CommsVar;
        string dialogServerName = bot.IsNative
            ? ReadCurrentMombotVar(
                NormalizeMombotValue(_embeddedGameConfig?.LoginName, treatSelfAsEmpty: true),
                "$BOT~SERVERNAME",
                "$servername")
            : string.Empty;
        string dialogLoginName = bot.IsNative
            ? ReadCurrentMombotVar(
                NormalizeMombotValue(_embeddedGameConfig?.LoginName, treatSelfAsEmpty: true),
                "$BOT~USERNAME",
                "$username")
            : string.Empty;
        string dialogGameLetter = bot.IsNative
            ? ReadCurrentMombotVar(
                NormalizeGameLetter(_embeddedGameConfig?.GameLetter),
                "$BOT~LETTER",
                "$letter",
                "$LETTER")
            : string.Empty;

        return new BotConfigDialogResult(
            Alias: bot.Alias,
            Name: bot.Config.Name,
            Script: bot.Config.ScriptFiles.Count > 0
                ? string.Join(", ", bot.Config.ScriptFiles)
                : bot.Config.ScriptFile,
            Description: bot.Config.Description,
            AutoStart: bot.Config.AutoStart,
            NameVar: dialogNameValue,
            CommsVar: dialogCommsValue,
            ServerName: dialogServerName,
            LoginName: dialogLoginName,
            GameLetter: dialogGameLetter,
            LoginScript: bot.Config.LoginScript,
            Theme: bot.Config.Theme);
    }

    private bool TryValidateBotDialogResult(
        BotConfigDialogResult result,
        bool isNative,
        string? currentSectionName,
        out string error,
        out BotConfigDialogResult normalized)
    {
        error = string.Empty;
        string alias = isNative ? Core.ProxyMenuCatalog.GetBotAlias(Core.ProxyMenuCatalog.NativeMombotSectionName) : SanitizeBotSectionAlias(result.Alias);
        if (!isNative && string.IsNullOrWhiteSpace(alias))
        {
            normalized = result;
            error = "Bot alias is required.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(result.Name))
        {
            normalized = result;
            error = "Bot name is required.";
            return false;
        }

        string scriptList = NormalizeBotScriptList(result.Script);
        if (!isNative && string.IsNullOrWhiteSpace(scriptList))
        {
            normalized = result;
            error = "At least one script path is required.";
            return false;
        }

        if (!isNative)
        {
            string sectionName = "bot:" + alias;
            string programDir = GetEffectiveProxyProgramDir(GetEffectiveProxyScriptDirectory());
            bool duplicateAlias = Core.TwxpConfigStore.LoadSections(programDir).Any(section =>
                section.Name.StartsWith("bot:", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(section.Name, currentSectionName, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(section.Name, sectionName, StringComparison.OrdinalIgnoreCase));
            if (duplicateAlias)
            {
                normalized = result;
                error = $"A bot named '{alias}' already exists in TwxpCfg.";
                return false;
            }
        }

        normalized = result with
        {
            Alias = alias,
            Script = scriptList,
        };
        return true;
    }

    private void SaveBotSection(StoredBotSection? existing, BotConfigDialogResult result)
    {
        string scriptDirectory = GetEffectiveProxyScriptDirectory();
        string programDir = GetEffectiveProxyProgramDir(scriptDirectory);

        if (existing?.IsNative == true)
        {
            _embeddedGameConfig ??= new EmbeddedGameConfig
            {
                Name = NormalizeGameName(DeriveGameName()),
                DatabasePath = DatabasePathForMode(DeriveGameName(), _state.EmbeddedProxy),
            };

            MTC.mombot.mombotConfig nativeConfig = GetOrCreateEmbeddedMombotConfig(_embeddedGameConfig);
            NormalizeNativeMombotRuntimeConfig(nativeConfig);
            nativeConfig.Configured = true;
            nativeConfig.Name = string.IsNullOrWhiteSpace(result.Name) ? nativeConfig.Name : result.Name.Trim();
            nativeConfig.Description = string.IsNullOrWhiteSpace(result.Description) ? nativeConfig.Description : result.Description.Trim();
            nativeConfig.AutoStart = result.AutoStart;
            nativeConfig.LoginScript = string.IsNullOrWhiteSpace(result.LoginScript) ? "disabled" : result.LoginScript.Trim();
            nativeConfig.Theme = string.IsNullOrWhiteSpace(result.Theme) ? nativeConfig.Theme : result.Theme.Trim();

            string currentBotName = FirstMeaningfulMombotValue(
                Core.ScriptRef.GetCurrentGameVar("$BOT~BOT_NAME", string.Empty),
                Core.ScriptRef.GetCurrentGameVar("$SWITCHBOARD~BOT_NAME", string.Empty),
                Core.ScriptRef.GetCurrentGameVar("$SWITCHBOARD~bot_name", string.Empty),
                Core.ScriptRef.GetCurrentGameVar("$bot~bot_name", string.Empty),
                Core.ScriptRef.GetCurrentGameVar("$bot_name", string.Empty),
                Core.ScriptRef.GetCurrentGameVar("$bot~name", string.Empty),
                nativeConfig.Name,
                "MomBot");
            string currentCommsName = FirstMeaningfulMombotValue(
                Core.ScriptRef.GetCurrentGameVar("$BOT~BOT_TEAM_NAME", string.Empty),
                Core.ScriptRef.GetCurrentGameVar("$BOT~bot_team_name", string.Empty),
                Core.ScriptRef.GetCurrentGameVar("$bot~bot_team_name", string.Empty),
                Core.ScriptRef.GetCurrentGameVar("$bot_team_name", string.Empty),
                currentBotName);
            string botName = FirstMeaningfulMombotValue(result.NameVar, nativeConfig.Name, "MomBot");
            RememberNativeMombotBotName(botName);
            string submittedCommsName = NormalizeMombotValue(result.CommsVar);
            bool botNameChanged = !string.Equals(botName, currentBotName, StringComparison.OrdinalIgnoreCase);
            bool commsFollowedBotName = string.IsNullOrWhiteSpace(currentCommsName) ||
                                        string.Equals(currentCommsName, currentBotName, StringComparison.OrdinalIgnoreCase);
            bool commsWasLeftOnPriorName = string.IsNullOrWhiteSpace(submittedCommsName) ||
                                           string.Equals(submittedCommsName, currentCommsName, StringComparison.OrdinalIgnoreCase) ||
                                           string.Equals(submittedCommsName, currentBotName, StringComparison.OrdinalIgnoreCase);
            string commsName = botNameChanged && commsFollowedBotName && commsWasLeftOnPriorName
                ? botName
                : FirstMeaningfulMombotValue(result.CommsVar, botName);
            PersistMombotVars(
                botName,
                "$BOT~BOT_NAME",
                "$SWITCHBOARD~BOT_NAME",
                "$SWITCHBOARD~bot_name",
                "$bot~bot_name",
                "$bot_name",
                "$bot~name");
            PersistMombotVars(
                commsName,
                "$BOT~BOT_TEAM_NAME",
                "$BOT~bot_team_name",
                "$bot~bot_team_name",
                "$bot_team_name");
            string loginName = NormalizeMombotValue(result.LoginName, treatSelfAsEmpty: true);
            string serverName = NormalizeMombotValue(result.ServerName, treatSelfAsEmpty: true);
            string gameLetter = NormalizeGameLetter(result.GameLetter);
            PersistMombotVars(loginName, "$BOT~USERNAME", "$username");
            PersistMombotVars(serverName, "$BOT~SERVERNAME", "$servername");
            PersistMombotVars(gameLetter, "$BOT~LETTER", "$letter", "$LETTER");
            if (_embeddedGameConfig != null)
            {
                _embeddedGameConfig.LoginName = loginName;
                _embeddedGameConfig.GameLetter = gameLetter;
            }

            ReloadRegisteredBotConfigs();
            SyncMombotRuntimeConfigFromTwxpCfg();
            if (_mombot.IsAttached)
                _mombot.ApplyConfig(_embeddedGameConfig != null ? GetOrCreateEmbeddedMombotConfig(_embeddedGameConfig) : null);
            RefreshActiveBotContextFromConfig(CreateNativeStoredBotSection(programDir, scriptDirectory));

            RefreshStatusBar();
            RebuildProxyMenu();
            _ = SaveCurrentGameConfigAsync();
            return;
        }

        List<Core.TwxpConfigSection> sections = Core.TwxpConfigStore.LoadSections(programDir).ToList();
        string sectionName = existing?.IsNative == true
            ? Core.ProxyMenuCatalog.NativeMombotSectionName
            : "bot:" + result.Alias;

        sections.RemoveAll(section =>
            string.Equals(section.Name, sectionName, StringComparison.OrdinalIgnoreCase) ||
            (!string.IsNullOrWhiteSpace(existing?.SectionName) &&
             string.Equals(section.Name, existing.SectionName, StringComparison.OrdinalIgnoreCase)));

        var values = existing != null
            ? new Dictionary<string, string>(existing.Values, StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        values["Name"] = result.Name.Trim();
        values["Script"] = NormalizeBotScriptList(result.Script);
        values["Description"] = result.Description.Trim();
        values["AutoStart"] = result.AutoStart ? "1" : "0";
        values["NameVar"] = result.NameVar.Trim();
        values["CommsVar"] = result.CommsVar.Trim();
        values["LoginScript"] = existing?.IsNative == true ? "disabled" : result.LoginScript.Trim();
        values["Theme"] = result.Theme.Trim();

        if (existing?.IsNative == true)
            values["Native"] = "1";
        else
            values.Remove("Native");

        sections.Add(new Core.TwxpConfigSection(sectionName, values));
        Core.TwxpConfigStore.SaveSections(programDir, sections);
    }

    private void ReloadRegisteredBotConfigs()
    {
        if (_gameInstance == null)
            return;

        string scriptDirectory = GetEffectiveProxyScriptDirectory();
        string programDir = GetEffectiveProxyProgramDir(scriptDirectory);
        _gameInstance.ReloadBotConfigs(programDir, scriptDirectory, includeNative: false);
        _gameInstance.RegisterOrUpdateBotConfig(BuildCurrentGameNativeBotConfig());
    }

    private void RefreshActiveBotContextFromConfig(StoredBotSection updatedBot)
    {
        Core.ModInterpreter? interpreter = CurrentInterpreter;
        if (interpreter == null)
            return;

        bool refreshNative = updatedBot.IsNative && _mombot.Enabled;
        bool refreshExternal = !updatedBot.IsNative &&
                               string.Equals(_gameInstance?.ActiveBotName, updatedBot.Config.Name, StringComparison.OrdinalIgnoreCase);
        if (!refreshNative && !refreshExternal)
            return;

        string requestedBotName = interpreter.ActiveBotName;
        interpreter.ActivateBotContext(updatedBot.Config, requestedBotName);
    }

    private void SyncMombotRuntimeConfigFromTwxpCfg(EmbeddedGameConfig? gameConfig = null)
    {
        EmbeddedGameConfig? targetConfig = gameConfig ?? _embeddedGameConfig;
        if (targetConfig == null)
            return;

        MTC.mombot.mombotConfig runtimeConfig = GetOrCreateEmbeddedMombotConfig(targetConfig);
        NormalizeNativeMombotRuntimeConfig(runtimeConfig);
        runtimeConfig.WatcherEnabled = runtimeConfig.Enabled;
    }

    private Core.BotConfig BuildCurrentGameNativeBotConfig()
    {
        MTC.mombot.mombotConfig runtimeConfig = BuildCurrentGameNativeMombotConfig();
        string scriptFile = "mombot/mombot.cts";
        return new Core.BotConfig
        {
            Alias = Core.ProxyMenuCatalog.GetBotAlias(Core.ProxyMenuCatalog.NativeMombotSectionName),
            Name = runtimeConfig.Name,
            ScriptFile = scriptFile,
            ScriptFiles = new List<string> { scriptFile },
            Description = runtimeConfig.Description,
            AutoStart = runtimeConfig.AutoStart,
            NameVar = runtimeConfig.NameVar,
            CommsVar = runtimeConfig.CommsVar,
            LoginScript = runtimeConfig.LoginScript,
            Theme = runtimeConfig.Theme,
            Properties = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Native"] = "1",
                ["Configured"] = runtimeConfig.Configured ? "1" : "0",
                ["Name"] = runtimeConfig.Name,
                ["Script"] = scriptFile,
                ["Description"] = runtimeConfig.Description,
                ["AutoStart"] = runtimeConfig.AutoStart ? "1" : "0",
                ["NameVar"] = runtimeConfig.NameVar,
                ["CommsVar"] = runtimeConfig.CommsVar,
                ["LoginScript"] = runtimeConfig.LoginScript,
                ["Theme"] = runtimeConfig.Theme,
            },
        };
    }

    private MTC.mombot.mombotConfig BuildCurrentGameNativeMombotConfig()
    {
        EmbeddedGameConfig config = _embeddedGameConfig ?? new EmbeddedGameConfig();
        MTC.mombot.mombotConfig runtimeConfig = GetOrCreateEmbeddedMombotConfig(config);
        NormalizeNativeMombotRuntimeConfig(runtimeConfig);
        return runtimeConfig;
    }

    private static void NormalizeNativeMombotRuntimeConfig(MTC.mombot.mombotConfig config)
    {
        if (string.IsNullOrWhiteSpace(config.Name))
            config.Name = "MomBot";
        if (string.IsNullOrWhiteSpace(config.Description))
            config.Description = "Built-in native Mombot runtime";
        if (string.IsNullOrWhiteSpace(config.NameVar))
            config.NameVar = "BotName";
        if (string.IsNullOrWhiteSpace(config.CommsVar))
            config.CommsVar = "BotComms";
        if (string.IsNullOrWhiteSpace(config.LoginScript))
            config.LoginScript = "disabled";
        if (string.IsNullOrWhiteSpace(config.Theme))
            config.Theme = "7|[MOMBOT]|~D|~G|~B|~C";
        if (string.IsNullOrWhiteSpace(config.ScriptRoot))
            config.ScriptRoot = "scripts/mombot";
    }

    private static Dictionary<string, string> MergeBotValues(
        IDictionary<string, string> source,
        IDictionary<string, string> defaults)
    {
        var merged = new Dictionary<string, string>(defaults, StringComparer.OrdinalIgnoreCase);
        foreach (KeyValuePair<string, string> entry in source)
            merged[entry.Key] = entry.Value;
        return merged;
    }

    private static Dictionary<string, string> BuildDefaultNativeBotValues()
    {
        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Native"] = "1",
            ["Configured"] = "0",
            ["Name"] = "MomBot",
            ["Script"] = "mombot/mombot.cts",
            ["Description"] = "Built-in native Mombot runtime",
            ["AutoStart"] = "0",
            ["NameVar"] = "BotName",
            ["CommsVar"] = "BotComms",
            ["LoginScript"] = "disabled",
            ["Theme"] = "7|[MOMBOT]|~D|~G|~B|~C",
        };
    }

    private static bool ConfigValuesEqual(
        IDictionary<string, string> left,
        IDictionary<string, string> right)
    {
        if (left.Count != right.Count)
            return false;

        foreach ((string key, string value) in left)
        {
            if (!right.TryGetValue(key, out string? otherValue) ||
                !string.Equals(value, otherValue, StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    private static bool BotScriptsExist(Core.BotConfig config, string programDir, string scriptDirectory)
    {
        string scriptsRoot = Path.GetFullPath(scriptDirectory);
        IReadOnlyList<string> scripts = config.ScriptFiles.Count > 0
            ? config.ScriptFiles
            : string.IsNullOrWhiteSpace(config.ScriptFile)
                ? Array.Empty<string>()
                : new[] { config.ScriptFile };
        if (scripts.Count == 0)
            return false;

        foreach (string script in scripts)
        {
            string normalized = script.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar);
            string fullPath = Path.IsPathRooted(normalized)
                ? Path.GetFullPath(normalized)
                : Path.Combine(scriptsRoot, normalized);
            if (!File.Exists(fullPath))
                return false;
        }

        return true;
    }

    private static string NormalizeBotScriptList(string scriptList)
    {
        return string.Join(",",
            scriptList
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(script => script.Replace('\\', '/').Trim().TrimStart('/'))
                .Where(script => !string.IsNullOrWhiteSpace(script)));
    }

    private static string SanitizeBotSectionAlias(string alias)
    {
        string trimmed = alias.Trim();
        if (trimmed.Length == 0)
            return string.Empty;

        var buffer = new System.Text.StringBuilder(trimmed.Length);
        foreach (char ch in trimmed)
        {
            if (char.IsLetterOrDigit(ch))
                buffer.Append(char.ToLowerInvariant(ch));
            else if (ch == '_' || ch == '-')
                buffer.Append(ch);
        }

        return buffer.ToString().Trim('_', '-');
    }

    private static bool ParseTwxpBool(string? value, bool fallback)
    {
        if (string.IsNullOrWhiteSpace(value))
            return fallback;

        if (bool.TryParse(value, out bool parsed))
            return parsed;

        return value.Equals("1", StringComparison.OrdinalIgnoreCase) ||
               value.Equals("yes", StringComparison.OrdinalIgnoreCase) ||
               value.Equals("y", StringComparison.OrdinalIgnoreCase);
    }

    private static string GetNativeMombotScriptRoot(Core.BotConfig config)
    {
        string script = config.ScriptFiles.Count > 0
            ? config.ScriptFiles[0]
            : config.ScriptFile;
        string normalized = script.Replace('\\', '/').Trim().Trim('/');
        if (string.IsNullOrWhiteSpace(normalized))
            return "scripts/mombot";

        string directory = Path.GetDirectoryName(normalized.Replace('/', Path.DirectorySeparatorChar))?
            .Replace('\\', '/')
            .Trim('/') ?? string.Empty;
        if (string.IsNullOrWhiteSpace(directory))
            return "scripts/mombot";
        if (directory.Equals("scripts", StringComparison.OrdinalIgnoreCase) ||
            directory.StartsWith("scripts/", StringComparison.OrdinalIgnoreCase))
        {
            return directory;
        }

        return Path.Combine("scripts", directory).Replace('\\', '/');
    }

    private async Task ToggleNativeMombotFromToolbarAsync()
    {
        if (_mombot.Enabled)
        {
            await StopInternalMombotAsync();
            return;
        }

        StoredBotSection? nativeBot = LoadConfiguredBotSections().FirstOrDefault(section => section.IsNative);
        if (nativeBot == null)
        {
            PublishMombotLocalMessage("No native MomBot configuration is available.");
            return;
        }

        if (!IsNativeMombotConfiguredForStart())
        {
            PublishMombotLocalMessage("Configure native MomBot before starting it.");
            return;
        }

        StopActiveExternalBot();
        await StartInternalMombotAsync(
            nativeBot.Config,
            requestedBotName: string.Empty,
            interactiveOfflinePrompt: true,
            publishMissingGameMessage: true);
    }

    private async Task StartInternalMombotAsync(
        Core.BotConfig? nativeBotConfig = null,
        string requestedBotName = "",
        bool interactiveOfflinePrompt = true,
        bool publishMissingGameMessage = true)
    {
        await Task.Yield();

        if (_gameInstance == null)
        {
            if (publishMissingGameMessage)
                PublishMombotLocalMessage("Mombot controls are only available while the embedded proxy is running.");
            return;
        }

        if (!_gameInstance.IsConnected && !interactiveOfflinePrompt)
        {
            MTC.mombot.mombotRelogDialogResult offlineDefaults = BuildMombotRelogDefaults();
            if (!CanStartNativeMombotOfflineWithoutPrompt(
                    offlineDefaults,
                    repairInvalidRelogState: true,
                    out string offlineSkipReason))
            {
                string dorelog = ReadCurrentMombotVar("0", "$BOT~DORELOG", "$doRelog");
                string loginName = FirstMeaningfulMombotValue(
                    Core.ScriptRef.GetCurrentGameVar("$BOT~USERNAME", string.Empty),
                    Core.ScriptRef.GetCurrentGameVar("$username", string.Empty));
                string gameLetter = FirstMeaningfulMombotValue(
                    Core.ScriptRef.GetCurrentGameVar("$BOT~LETTER", string.Empty),
                    Core.ScriptRef.GetCurrentGameVar("$letter", string.Empty));
                Core.GlobalModules.DebugLog(
                    $"[MTC.NativeBotStart] skipping offline noninteractive start game='{_gameInstance.GameName}' reason='{offlineSkipReason}' dorelog='{dorelog}' login='{loginName}' letter='{gameLetter}'\n");
                Core.GlobalModules.FlushDebugLog();
                return;
            }
        }

        Core.BotConfig botConfig = nativeBotConfig ?? LoadConfiguredBotSections().First(bot => bot.IsNative).Config;
        if (!IsNativeMombotConfiguredForStart())
        {
            if (publishMissingGameMessage)
                PublishMombotLocalMessage("Configure native MomBot before starting it.");
            return;
        }

        bool preserveOfflineDoNotResuscitate = !_gameInstance.IsConnected && HasNativeMombotDoNotResuscitateFlag();
        bool preserveOfflineShipDestroyed = !_gameInstance.IsConnected && HasNativeMombotShipDestroyedFlag();
        Core.GlobalModules.DebugLog(
            $"[MTC.NativeBotStart] bootstrap connected={_gameInstance.IsConnected} preserveDnr={preserveOfflineDoNotResuscitate} preserveShipDestroyed={preserveOfflineShipDestroyed}\n");
        Core.GlobalModules.FlushDebugLog();
        PrimeMombotBootstrapState(
            botConfig,
            preserveDoNotResuscitate: preserveOfflineDoNotResuscitate,
            preserveShipDestroyed: preserveOfflineShipDestroyed);
        CurrentInterpreter?.ActivateBotContext(botConfig, requestedBotName);
        SyncMombotRuntimeConfigFromTwxpCfg();
        ArmNativeMombotStartupDataGather();

        if (_gameInstance.IsConnected)
        {
            SeedMombotRelogVarsFromCurrentState();
            ApplyMombotConfigChange(config => config.Enabled = true);
            ShowMombotStartupBanner(connected: true);
            await TryRunNativeMombotInitialSettingsAsync();
            ApplyMombotExecutionRefresh();
        }
        else
        {
            MTC.mombot.mombotRelogDialogResult relogSettings = BuildMombotRelogDefaults();
            if (interactiveOfflinePrompt &&
                (ShouldPromptForMombotRelogSettings(relogSettings) ||
                 ShouldReviewMombotRelogSettings(relogSettings)))
            {
                Core.GlobalModules.DebugLog(
                    $"[MTC.NativeBotStart] opening relog dialog game='{_gameInstance.GameName}' loginType='{relogSettings.LoginType}' preserveDnr={preserveOfflineDoNotResuscitate} shipDestroyed={preserveOfflineShipDestroyed}\n");
                Core.GlobalModules.FlushDebugLog();

                var dialog = new MTC.mombot.mombotRelogDialog(relogSettings);
                if (!await dialog.ShowDialog<bool>(this) || dialog.Result == null)
                {
                    FocusActiveTerminal();
                    return;
                }

                relogSettings = dialog.Result;
            }

            ApplyMombotRelogDialogResult(relogSettings);
            await SaveCurrentGameConfigAsync();
            SeedMombotRelogVarsFromCurrentState();
            NormalizeOptionalMombotCorpVars();
            ArmNativeMombotPostLoginMacro(relogSettings);
            SetMombotCurrentVars("1", "$relogging", "$connectivity~relogging");
            ApplyMombotConfigChange(config => config.Enabled = true);
            LoadMombotStartupScripts();
            bool launchedConnectivityRelog = false;
            if (relogSettings.LoginType != MTC.mombot.mombotRelogLoginType.NormalRelog)
            {
                IReadOnlyDictionary<string, string> initialVars = BuildNativeMombotConnectivityRelogVars(relogSettings);
                var launchErrors = new List<string>();
                foreach (string entryLabel in new[] { ":CONNECTIVITY~ENTER_NEW_GAME" })
                {
                    if (_mombot.TryLoadInstalledScriptAtSubroutine(
                            "mombot.cts",
                            entryLabel,
                            initialVars,
                            out string? connectivityScriptReference,
                            out string? connectivityError))
                    {
                        launchedConnectivityRelog = true;
                        Core.GlobalModules.DebugLog(
                            $"[MTC.NativeBotStart] launched connectivity relog path script='{connectivityScriptReference}' label='{entryLabel}' loginType='{relogSettings.LoginType}'\n");
                        Core.GlobalModules.FlushDebugLog();
                        break;
                    }

                    launchErrors.Add($"{entryLabel}: {connectivityError}");
                }

                if (!launchedConnectivityRelog)
                {
                    Core.GlobalModules.DebugLog(
                        $"[MTC.NativeBotStart] connectivity relog path failed loginType='{relogSettings.LoginType}' errors='{string.Join(" | ", launchErrors)}'\n");
                    Core.GlobalModules.FlushDebugLog();
                }
            }

            if (!launchedConnectivityRelog)
                await ExecuteMombotUiCommandAsync("relog");
        }

        FocusActiveTerminal();
    }

    private IReadOnlyDictionary<string, string> BuildNativeMombotConnectivityRelogVars(MTC.mombot.mombotRelogDialogResult relogSettings)
    {
        string newGame = relogSettings.LoginType == MTC.mombot.mombotRelogLoginType.NewGameAccountCreation ? "1" : "0";
        string gameLetter = NormalizeGameLetter(relogSettings.GameLetter);
        string postLoginMacro = GetNativeMombotPostLoginMacro(relogSettings);
        string startShipName = FirstMeaningfulMombotValue(
            Core.ScriptRef.GetCurrentGameVar("$BOT~STARTSHIPNAME", string.Empty),
            Core.ScriptRef.GetCurrentGameVar("$bot~startShipName", string.Empty),
            "Mind ()ver Matter");
        string isCeo = IsMombotTruthy(Core.ScriptRef.GetCurrentGameVar("$BOT~ISCEO", "0")) ? "TRUE" : "FALSE";
        string corpName = NormalizeMombotValue(Core.ScriptRef.GetCurrentGameVar("$BOT~CORPNAME", string.Empty));
        string corpPassword = NormalizeMombotValue(Core.ScriptRef.GetCurrentGameVar("$BOT~CORPPASSWORD", string.Empty));

        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["$CONNECTIVITY~NEWGAME"] = newGame,
            ["$connectivity~newgame"] = newGame,
            ["$BOT~BOT_NAME"] = relogSettings.BotName,
            ["$SWITCHBOARD~BOT_NAME"] = relogSettings.BotName,
            ["$bot_name"] = relogSettings.BotName,
            ["$BOT~SERVERNAME"] = relogSettings.ServerName,
            ["$servername"] = relogSettings.ServerName,
            ["$BOT~USERNAME"] = relogSettings.LoginName,
            ["$username"] = relogSettings.LoginName,
            ["$BOT~PASSWORD"] = relogSettings.Password,
            ["$password"] = relogSettings.Password,
            ["$BOT~LETTER"] = gameLetter,
            ["$letter"] = gameLetter,
            ["$LETTER"] = gameLetter,
            ["$BOT~STARTGAMEDELAY"] = relogSettings.DelayMinutes.ToString(),
            ["$startGameDelay"] = relogSettings.DelayMinutes.ToString(),
            ["$BOT~STARTSHIPNAME"] = startShipName,
            ["$BOT~ISCEO"] = isCeo,
            ["$BOT~CORPNAME"] = corpName,
            ["$BOT~CORPPASSWORD"] = corpPassword,
            ["$BOT~NEWGAMEDAY1"] = newGame,
            ["$newGameDay1"] = newGame,
            ["$BOT~NEWGAMEOLDER"] = newGame == "1" ? "0" : "1",
            ["$newGameOlder"] = newGame == "1" ? "0" : "1",
            ["$BOT~ISSHIPDESTROYED"] = relogSettings.LoginType == MTC.mombot.mombotRelogLoginType.ReturnAfterDestroyed ? "1" : "0",
            ["$bot~isShipDestroyed"] = relogSettings.LoginType == MTC.mombot.mombotRelogLoginType.ReturnAfterDestroyed ? "1" : "0",
            ["$command_to_issue"] = relogSettings.BotCommand,
            ["$BOT~STARTMACRO"] = postLoginMacro,
            ["$bot~startMacro"] = postLoginMacro,
            ["$startMacro"] = postLoginMacro,
        };
    }

    private void ArmNativeMombotPostLoginMacro(MTC.mombot.mombotRelogDialogResult relogSettings)
    {
        string macro = GetNativeMombotPostLoginMacro(relogSettings);
        lock (_nativeMombotPostLoginMacroLock)
        {
            _pendingNativeMombotPostLoginMacro = macro;
        }

        if (!string.IsNullOrWhiteSpace(macro))
        {
            Core.GlobalModules.DebugLog(
                $"[MTC.NativeBotStart] armed post-login macro action='{relogSettings.AfterLoginAction}' macro='{macro}'\n");
            Core.GlobalModules.FlushDebugLog();
        }
    }

    private string ConsumeNativeMombotPostLoginMacro()
    {
        lock (_nativeMombotPostLoginMacroLock)
        {
            string macro = _pendingNativeMombotPostLoginMacro;
            _pendingNativeMombotPostLoginMacro = string.Empty;
            return macro;
        }
    }

    private static string GetNativeMombotAfterLoginAction(string botCommand, string startMacro)
    {
        if (!string.IsNullOrWhiteSpace(botCommand))
            return "command";

        if (string.Equals(NormalizeMombotValue(startMacro), "pt", StringComparison.OrdinalIgnoreCase))
            return "terra";

        return string.IsNullOrWhiteSpace(startMacro)
            ? "nothing"
            : "macro";
    }

    private static bool IsNativeMombotMacroAfterLoginAction(string action)
    {
        return string.Equals(action, "macro", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(action, "terra", StringComparison.OrdinalIgnoreCase);
    }

    private static string GetNativeMombotPostLoginMacro(MTC.mombot.mombotRelogDialogResult relogSettings)
    {
        return string.Equals(relogSettings.AfterLoginAction, "terra", StringComparison.OrdinalIgnoreCase)
            ? "pt"
            : string.Equals(relogSettings.AfterLoginAction, "macro", StringComparison.OrdinalIgnoreCase)
                ? relogSettings.MacroAfterLogin
                : string.Empty;
    }

    private async Task StopInternalMombotAsync()
    {
        await Task.Yield();

        await _runtimeStopGate.WaitAsync();
        try
        {
            await StopInternalMombotCoreAsync(
                publishStopMessage: true,
                suppressMissingGameMessage: false,
                disconnectServerAfterStop: false);
        }
        finally
        {
            _runtimeStopGate.Release();
        }
    }

    private async Task StopInternalMombotCoreAsync(
        bool publishStopMessage,
        bool suppressMissingGameMessage,
        bool disconnectServerAfterStop = false)
    {
        await Task.Yield();

        if (_gameInstance == null)
        {
            if (!suppressMissingGameMessage)
                PublishMombotLocalMessage("Mombot controls are only available while the embedded proxy is running.");
            return;
        }

        CloseMombotInteractiveState();
        string programDir = CurrentInterpreter?.ProgramDir ?? GetEffectiveProxyProgramDir(GetEffectiveProxyScriptDirectory());
        string scriptDirectory = CurrentInterpreter?.ScriptDirectory ?? GetEffectiveProxyScriptDirectory();
        string lastLoadedModule = Core.ScriptRef.GetCurrentGameVar("$BOT~LAST_LOADED_MODULE", string.Empty);
        string scriptRoot = (_mombot.Config.ScriptRoot ?? string.Empty)
            .Replace('\\', '/')
            .Trim()
            .Trim('/');
        string scriptRootPath = string.IsNullOrWhiteSpace(scriptRoot)
            ? string.Empty
            : NormalizeScriptStopPath(scriptRoot, programDir, scriptDirectory);

        bool preserveShipDestroyed = HasNativeMombotShipDestroyedFlag();
        bool preserveDoNotResuscitate = HasNativeMombotDoNotResuscitateFlag() || preserveShipDestroyed;
        TraceRuntimeStop($"[BotStop] native begin root='{scriptRootPath}' lastLoaded='{lastLoadedModule}' preserveDnr={preserveDoNotResuscitate} preserveShipDestroyed={preserveShipDestroyed}");
        ClearMombotRelogState(preserveDoNotResuscitate, preserveShipDestroyed);
        ClearNativeMombotStartupDataGather();
        StoredBotSection nativeBotSection = LoadConfiguredBotSections().First(bot => bot.IsNative);
        Core.BotConfig nativeBotConfig = nativeBotSection.Config;
        string nativeBotName = nativeBotConfig.Name;
        CurrentInterpreter?.ClearActiveBotContext(nativeBotName);

        ApplyMombotConfigChange(config => config.Enabled = false);
        _gameInstance.ActiveBotName = string.Empty;
        var nativeScriptReferences = GetConfiguredBotScriptPaths(nativeBotConfig, scriptDirectory)
            .Concat(_mombot.GetStartupScriptReferences())
            .Where(reference => !string.IsNullOrWhiteSpace(reference))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        int drainedScripts = StopScriptsMatchingTree(
            origin: "native-mombot",
            directScriptPaths: nativeScriptReferences,
            scriptRootPath: scriptRootPath,
            lastLoadedModule: lastLoadedModule,
            scriptDirectory: scriptDirectory,
            programDir: programDir);

        foreach (string scriptReference in nativeScriptReferences)
            _mombot.StopScriptByName(scriptReference);

        if (disconnectServerAfterStop && _gameInstance.IsConnected)
            await _gameInstance.DisconnectFromServerAsync();

        if (publishStopMessage)
            PublishMombotLocalMessage("Mombot stopped.");
        ApplyMombotExecutionRefresh();
        TraceRuntimeStop($"[BotStop] native complete drained={drainedScripts}");
    }

    private MTC.mombot.mombotRelogDialogResult BuildMombotRelogDefaults()
    {
        string configLogin = NormalizeMombotValue(_embeddedGameConfig?.LoginName, treatSelfAsEmpty: true);
        string configPassword = NormalizeMombotValue(_embeddedGameConfig?.Password);
        string configGameLetter = NormalizeMombotValue(_embeddedGameConfig?.GameLetter);
        string configuredNativeBotName = NormalizeMombotValue(
            _embeddedGameConfig?.mombot?.Name ??
            _embeddedGameConfig?.Mtc?.mombot?.Name);
        string rememberedBotName = NormalizeMombotValue(_appPrefs.LastNativeMombotBotName);
        string botName = FirstMeaningfulMombotValue(
            Core.ScriptRef.GetCurrentGameVar("$BOT~BOT_NAME", string.Empty),
            Core.ScriptRef.GetCurrentGameVar("$SWITCHBOARD~BOT_NAME", string.Empty),
            Core.ScriptRef.GetCurrentGameVar("$bot_name", string.Empty),
            configuredNativeBotName,
            rememberedBotName,
            _mombot.Settings.BotName,
            "mombot");
        string loginName = FirstMeaningfulMombotValue(
            Core.ScriptRef.GetCurrentGameVar("$BOT~USERNAME", string.Empty),
            Core.ScriptRef.GetCurrentGameVar("$username", string.Empty),
            configLogin);
        string serverName = FirstMeaningfulMombotValue(
            Core.ScriptRef.GetCurrentGameVar("$BOT~SERVERNAME", string.Empty),
            Core.ScriptRef.GetCurrentGameVar("$servername", string.Empty),
            loginName);
        string password = FirstMeaningfulMombotValue(
            Core.ScriptRef.GetCurrentGameVar("$BOT~PASSWORD", string.Empty),
            Core.ScriptRef.GetCurrentGameVar("$password", string.Empty),
            configPassword);
        string gameLetter = FirstMeaningfulMombotValue(
            Core.ScriptRef.GetCurrentGameVar("$BOT~LETTER", string.Empty),
            Core.ScriptRef.GetCurrentGameVar("$letter", string.Empty),
            configGameLetter);
        string delayValue = FirstMeaningfulMombotValue(
            Core.ScriptRef.GetCurrentGameVar("$BOT~STARTGAMEDELAY", string.Empty),
            Core.ScriptRef.GetCurrentGameVar("$startGameDelay", string.Empty),
            "0");
        int delayMinutes = int.TryParse(delayValue, out int parsedDelay) && parsedDelay >= 0 ? parsedDelay : 0;
        string botCommand = NormalizeMombotValue(Core.ScriptRef.GetCurrentGameVar("$command_to_issue", string.Empty));
        string startMacro = FirstMeaningfulMombotValue(
            Core.ScriptRef.GetCurrentGameVar("$BOT~STARTMACRO", string.Empty),
            Core.ScriptRef.GetCurrentGameVar("$bot~startMacro", string.Empty),
            Core.ScriptRef.GetCurrentGameVar("$startMacro", string.Empty));
        string afterLoginAction = GetNativeMombotAfterLoginAction(botCommand, startMacro);

        bool newGameDay1 = string.Equals(Core.ScriptRef.GetCurrentGameVar("$BOT~NEWGAMEDAY1", "0"), "1", StringComparison.OrdinalIgnoreCase) ||
                           string.Equals(Core.ScriptRef.GetCurrentGameVar("$BOT~NEWGAMEDAY1", "false"), "true", StringComparison.OrdinalIgnoreCase);
        bool newGameOlder = string.Equals(Core.ScriptRef.GetCurrentGameVar("$BOT~NEWGAMEOLDER", "0"), "1", StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(Core.ScriptRef.GetCurrentGameVar("$BOT~NEWGAMEOLDER", "false"), "true", StringComparison.OrdinalIgnoreCase);
        bool wasShipDestroyed = HasNativeMombotShipDestroyedFlag();
        bool doNotResuscitate = HasNativeMombotDoNotResuscitateFlag();

        bool establishedGameEvidence = LooksLikeEstablishedRelogProfile(
            loginName,
            password,
            NormalizeGameLetter(gameLetter),
            Core.ScriptRef.GetCurrentGameVar("$PLAYER~TRADER_NAME", string.Empty),
            Core.ScriptRef.GetCurrentGameVar("$GAME~GAMESTATS", "0"),
            Core.ScriptRef.GetCurrentGameVar("$PLAYER~CURRENT_SECTOR", "0"));

        if (establishedGameEvidence && (newGameDay1 || !newGameOlder))
        {
            Core.GlobalModules.DebugLog(
                $"[MTC.RelogDefaults] overriding stale new-game flags for loaded game='{_embeddedGameName ?? _embeddedGameConfig?.Name ?? "-"}' newGameDay1={newGameDay1} newGameOlder={newGameOlder}\n");
            PersistMombotVars("0", "$BOT~NEWGAMEDAY1", "$newGameDay1");
            PersistMombotVars("1", "$BOT~NEWGAMEOLDER", "$newGameOlder");
            newGameDay1 = false;
            newGameOlder = true;
        }

        bool missingRelogSetup =
            string.IsNullOrWhiteSpace(botName) ||
            string.IsNullOrWhiteSpace(serverName) ||
            string.IsNullOrWhiteSpace(loginName) ||
            string.IsNullOrWhiteSpace(password) ||
            string.IsNullOrWhiteSpace(gameLetter);

        MTC.mombot.mombotRelogLoginType loginType = wasShipDestroyed
            ? MTC.mombot.mombotRelogLoginType.ReturnAfterDestroyed
            : missingRelogSetup
                ? MTC.mombot.mombotRelogLoginType.NewGameAccountCreation
                : newGameDay1
                    ? MTC.mombot.mombotRelogLoginType.NewGameAccountCreation
                    : newGameOlder || doNotResuscitate
                        ? MTC.mombot.mombotRelogLoginType.NormalRelog
                        : MTC.mombot.mombotRelogLoginType.NormalRelog;

        return new MTC.mombot.mombotRelogDialogResult(
            loginType,
            botName,
            serverName,
            loginName,
            password,
            NormalizeGameLetter(gameLetter),
            delayMinutes,
            afterLoginAction,
            botCommand,
            startMacro);
    }

    private static bool ShouldPromptForMombotRelogSettings(MTC.mombot.mombotRelogDialogResult defaults)
    {
        return string.IsNullOrWhiteSpace(defaults.BotName) ||
            string.IsNullOrWhiteSpace(defaults.ServerName) ||
            string.IsNullOrWhiteSpace(defaults.LoginName) ||
            string.IsNullOrWhiteSpace(defaults.Password) ||
            string.IsNullOrWhiteSpace(defaults.GameLetter);
    }

    private bool ShouldReviewMombotRelogSettings(MTC.mombot.mombotRelogDialogResult defaults)
    {
        return HasNativeMombotShipDestroyedFlag() ||
            defaults.LoginType == MTC.mombot.mombotRelogLoginType.ReturnAfterDestroyed;
    }

    private bool CanStartNativeMombotOfflineWithoutPrompt(
        MTC.mombot.mombotRelogDialogResult defaults,
        bool repairInvalidRelogState,
        out string reason)
    {
        if (HasNativeMombotDoNotResuscitateFlag() || HasNativeMombotShipDestroyedFlag())
        {
            reason = "do-not-resuscitate";
            return false;
        }

        bool dorelogEnabled = IsMombotTruthy(ReadCurrentMombotVar("0", "$BOT~DORELOG", "$doRelog"));
        if (!dorelogEnabled)
        {
            reason = "relog-disabled";
            return false;
        }

        if (ShouldPromptForMombotRelogSettings(defaults))
        {
            if (repairInvalidRelogState)
                PersistMombotVars("0", "$BOT~DORELOG", "$doRelog");

            reason = "missing-relog-settings";
            return false;
        }

        reason = string.Empty;
        return true;
    }

    private void ApplyMombotRelogDialogResult(MTC.mombot.mombotRelogDialogResult result)
    {
        RememberNativeMombotBotName(result.BotName);

        if (_embeddedGameConfig != null)
        {
            GetOrCreateEmbeddedMombotConfig(_embeddedGameConfig).Configured = true;
            _embeddedGameConfig.LoginName = result.LoginName;
            _embeddedGameConfig.Password = result.Password;
            _embeddedGameConfig.GameLetter = NormalizeGameLetter(result.GameLetter);
        }

        PersistMombotVars(result.BotName, "$BOT~BOT_NAME", "$SWITCHBOARD~BOT_NAME", "$bot_name");
        PersistMombotVars(
            FirstMeaningfulMombotValue(
                Core.ScriptRef.GetCurrentGameVar("$BOT~BOT_TEAM_NAME", string.Empty),
                Core.ScriptRef.GetCurrentGameVar("$bot_team_name", string.Empty),
                result.BotName),
            "$BOT~BOT_TEAM_NAME",
            "$bot_team_name");
        PersistMombotVars(result.ServerName, "$BOT~SERVERNAME", "$servername");
        PersistMombotVars(result.LoginName, "$BOT~USERNAME", "$username");
        PersistMombotVars(result.Password, "$BOT~PASSWORD", "$password");
        PersistMombotVars(NormalizeGameLetter(result.GameLetter), "$BOT~LETTER", "$letter");
        PersistMombotVars(result.DelayMinutes.ToString(), "$BOT~STARTGAMEDELAY", "$startGameDelay");
        PersistMombotVars(
            string.Equals(result.AfterLoginAction, "command", StringComparison.OrdinalIgnoreCase) ? result.BotCommand : string.Empty,
            "$command_to_issue");
        PersistMombotVars(
            IsNativeMombotMacroAfterLoginAction(result.AfterLoginAction) ? GetNativeMombotPostLoginMacro(result) : string.Empty,
            "$BOT~STARTMACRO",
            "$bot~startMacro",
            "$startMacro");
        PersistMombotVars("General", "$BOT~MODE", "$mode");
        PersistMombotVars(string.Empty, "$BOT~LAST_LOADED_MODULE", "$LAST_LOADED_MODULE");
        PersistMombotVars("1", "$BOT~DORELOG", "$doRelog");
        PersistMombotVars("0", "$BOT~DO_NOT_RESUSCITATE", "$bot~do_not_resuscitate", "$do_not_resuscitate");

        switch (result.LoginType)
        {
            case MTC.mombot.mombotRelogLoginType.NewGameAccountCreation:
                PersistMombotVars("1", "$BOT~NEWGAMEDAY1", "$newGameDay1");
                PersistMombotVars("0", "$BOT~NEWGAMEOLDER", "$newGameOlder");
                PersistMombotVars("0", "$BOT~ISSHIPDESTROYED", "$bot~isShipDestroyed");
                break;
            case MTC.mombot.mombotRelogLoginType.ReturnAfterDestroyed:
                PersistMombotVars("0", "$BOT~NEWGAMEDAY1", "$newGameDay1");
                PersistMombotVars("0", "$BOT~NEWGAMEOLDER", "$newGameOlder");
                PersistMombotVars("1", "$BOT~ISSHIPDESTROYED", "$bot~isShipDestroyed");
                break;
            default:
                PersistMombotVars("0", "$BOT~NEWGAMEDAY1", "$newGameDay1");
                PersistMombotVars("1", "$BOT~NEWGAMEOLDER", "$newGameOlder");
                PersistMombotVars("0", "$BOT~ISSHIPDESTROYED", "$bot~isShipDestroyed");
                break;
        }

        string relogMessage = TranslateMombotBurstText($"{result.BotName} connected and ready.*");
        PersistMombotVars(relogMessage, "$relog_message");
    }

    private void SeedMombotRelogVarsFromCurrentState()
    {
        string configLogin = NormalizeMombotValue(_embeddedGameConfig?.LoginName, treatSelfAsEmpty: true);
        string configPassword = NormalizeMombotValue(_embeddedGameConfig?.Password);
        string configGameLetter = NormalizeMombotValue(_embeddedGameConfig?.GameLetter);
        string configuredNativeBotName = NormalizeMombotValue(
            _embeddedGameConfig?.mombot?.Name ??
            _embeddedGameConfig?.Mtc?.mombot?.Name);
        string rememberedBotName = NormalizeMombotValue(_appPrefs.LastNativeMombotBotName);
        string botName = FirstMeaningfulMombotValue(
            Core.ScriptRef.GetCurrentGameVar("$BOT~BOT_NAME", string.Empty),
            Core.ScriptRef.GetCurrentGameVar("$SWITCHBOARD~BOT_NAME", string.Empty),
            Core.ScriptRef.GetCurrentGameVar("$bot_name", string.Empty),
            configuredNativeBotName,
            rememberedBotName,
            _mombot.Settings.BotName,
            "mombot");
        string loginName = FirstMeaningfulMombotValue(
            Core.ScriptRef.GetCurrentGameVar("$BOT~USERNAME", string.Empty),
            Core.ScriptRef.GetCurrentGameVar("$username", string.Empty),
            configLogin);
        string serverName = FirstMeaningfulMombotValue(
            Core.ScriptRef.GetCurrentGameVar("$BOT~SERVERNAME", string.Empty),
            Core.ScriptRef.GetCurrentGameVar("$servername", string.Empty),
            loginName);
        string password = FirstMeaningfulMombotValue(
            Core.ScriptRef.GetCurrentGameVar("$BOT~PASSWORD", string.Empty),
            Core.ScriptRef.GetCurrentGameVar("$password", string.Empty),
            configPassword);
        string gameLetter = FirstMeaningfulMombotValue(
            Core.ScriptRef.GetCurrentGameVar("$BOT~LETTER", string.Empty),
            Core.ScriptRef.GetCurrentGameVar("$letter", string.Empty),
            configGameLetter);
        string doRelog = IsMombotTruthy(FirstMeaningfulMombotValue(
            Core.ScriptRef.GetCurrentGameVar("$BOT~DORELOG", string.Empty),
            Core.ScriptRef.GetCurrentGameVar("$doRelog", string.Empty),
            "1")) ? "1" : "0";
        string newGameOlder = IsMombotTruthy(Core.ScriptRef.GetCurrentGameVar("$BOT~NEWGAMEOLDER", "0")) ? "1" : "0";
        string newGameDay1 = IsMombotTruthy(Core.ScriptRef.GetCurrentGameVar("$BOT~NEWGAMEDAY1", "0")) ? "1" : "0";
        string isShipDestroyed = HasNativeMombotShipDestroyedFlag() ? "1" : "0";

        SetMombotCurrentVars(botName, "$BOT~BOT_NAME", "$SWITCHBOARD~BOT_NAME", "$bot_name");
        SetMombotCurrentVars(serverName, "$BOT~SERVERNAME", "$servername");
        SetMombotCurrentVars(loginName, "$BOT~USERNAME", "$username");
        SetMombotCurrentVars(password, "$BOT~PASSWORD", "$password");
        SetMombotCurrentVars(NormalizeGameLetter(gameLetter), "$BOT~LETTER", "$letter");
        SetMombotCurrentVars(doRelog, "$BOT~DORELOG", "$doRelog");
        SetMombotCurrentVars(newGameOlder, "$BOT~NEWGAMEOLDER", "$newGameOlder");
        SetMombotCurrentVars(newGameDay1, "$BOT~NEWGAMEDAY1", "$newGameDay1");
        SetMombotCurrentVars(isShipDestroyed, "$BOT~ISSHIPDESTROYED", "$bot~isShipDestroyed");
        SetMombotCurrentVars("General", "$BOT~MODE", "$mode");
        SetMombotCurrentVars(string.Empty, "$BOT~LAST_LOADED_MODULE", "$LAST_LOADED_MODULE");
    }

    private void BackfillScriptMombotBootstrapState(EmbeddedGameConfig gameConfig, string gameName, string programDir)
    {
        string configuredNativeBotName = NormalizeMombotValue(
            gameConfig?.mombot?.Name ??
            gameConfig?.Mtc?.mombot?.Name);
        string botName = FirstMeaningfulMombotValue(
            Core.ScriptRef.GetCurrentGameVar("$BOT~BOT_NAME", string.Empty),
            Core.ScriptRef.GetCurrentGameVar("$SWITCHBOARD~BOT_NAME", string.Empty),
            Core.ScriptRef.GetCurrentGameVar("$SWITCHBOARD~bot_name", string.Empty),
            Core.ScriptRef.GetCurrentGameVar("$bot~bot_name", string.Empty),
            Core.ScriptRef.GetCurrentGameVar("$bot_name", string.Empty),
            Core.ScriptRef.GetCurrentGameVar("$bot~name", string.Empty),
            configuredNativeBotName);
        if (string.IsNullOrWhiteSpace(botName))
            return;

        PersistMombotVars(
            botName,
            "$BOT~BOT_NAME",
            "$SWITCHBOARD~BOT_NAME",
            "$SWITCHBOARD~bot_name",
            "$bot~bot_name",
            "$bot_name",
            "$bot~name");

        string gconfigPath = Path.Combine(programDir, "games", gameName, "bot.cfg");
        try
        {
            string? directory = Path.GetDirectoryName(gconfigPath);
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);

            string existingBotName = File.Exists(gconfigPath)
                ? (File.ReadLines(gconfigPath).FirstOrDefault()?.Trim() ?? string.Empty)
                : string.Empty;
            if (!string.Equals(existingBotName, botName, StringComparison.OrdinalIgnoreCase))
                File.WriteAllText(gconfigPath, botName + Environment.NewLine);
        }
        catch
        {
        }
    }

    private void NormalizeOptionalMombotCorpVars()
    {
        string corpName = NormalizeMombotValue(Core.ScriptRef.GetCurrentGameVar("$BOT~CORPNAME", string.Empty));
        string corpPassword = NormalizeMombotValue(Core.ScriptRef.GetCurrentGameVar("$BOT~CORPPASSWORD", string.Empty));
        string isCeo = IsMombotTruthy(Core.ScriptRef.GetCurrentGameVar("$BOT~ISCEO", "0")) ? "1" : "0";

        PersistMombotVars(corpName, "$BOT~CORPNAME");
        PersistMombotVars(corpPassword, "$BOT~CORPPASSWORD");
        PersistMombotVars(isCeo, "$BOT~ISCEO");

        SetMombotCurrentVars(corpName, "$BOT~CORPNAME");
        SetMombotCurrentVars(corpPassword, "$BOT~CORPPASSWORD");
        SetMombotCurrentVars(isCeo, "$BOT~ISCEO");
    }

    private void LoadMombotStartupScripts()
    {
        foreach (string startupScript in _mombot.GetStartupScriptReferences())
        {
            string startupName = Path.GetFileNameWithoutExtension(startupScript.Replace('\\', '/'));
            SetMombotCurrentVars(startupName, "$BOT~COMMAND", "$bot~command", "$command");
            _mombot.StopScriptByName(startupScript);
            if (!_mombot.TryLoadScript(startupScript, out string? error))
                PublishMombotLocalMessage($"mombot: failed to load startup '{startupScript}': {error}");
        }
    }

    private string ReadEmbeddedPersistedMombotVar(string fallback, params string[] names)
    {
        if (_embeddedGameConfig?.Variables != null)
        {
            foreach (string name in names)
            {
                if (string.IsNullOrWhiteSpace(name))
                    continue;

                if (_embeddedGameConfig.Variables.TryGetValue(name, out string? value))
                {
                    string normalized = NormalizeMombotValue(value);
                    if (!string.IsNullOrEmpty(normalized))
                        return normalized;
                }
            }
        }

        return fallback;
    }

    private void ShowMombotStartupBanner(bool connected)
    {
        string botName = FirstMeaningfulMombotValue(
            Core.ScriptRef.GetCurrentGameVar("$BOT~BOT_NAME", string.Empty),
            Core.ScriptRef.GetCurrentGameVar("$SWITCHBOARD~BOT_NAME", string.Empty),
            Core.ScriptRef.GetCurrentGameVar("$bot_name", string.Empty),
            _mombot.Settings.BotName,
            "mombot");
        string version = GetMombotVersionDisplay();
        if (connected)
            return;

        string message =
            $"\r\n{{{botName}}} is ACTIVE: Version - {version} - type \"{botName} help\" for command list\r\n";

        if (_gameInstance != null)
            _gameInstance.ClientMessage(message);
        else
            _parser.Feed(message);

        _buffer.Dirty = true;
    }

    private async Task SendMombotStartupAnnouncementsAsync()
    {
        if (_gameInstance == null || !_gameInstance.IsConnected)
            return;

        MombotPromptSurface promptSurface = GetMombotPromptSurface();
        if (promptSurface != MombotPromptSurface.Command &&
            promptSurface != MombotPromptSurface.Citadel)
        {
            return;
        }

        string botName = FirstMeaningfulMombotValue(
            Core.ScriptRef.GetCurrentGameVar("$BOT~BOT_NAME", string.Empty),
            Core.ScriptRef.GetCurrentGameVar("$SWITCHBOARD~BOT_NAME", string.Empty),
            Core.ScriptRef.GetCurrentGameVar("$bot_name", string.Empty),
            _mombot.Settings.BotName,
            "mombot");
        string loginName = FirstMeaningfulMombotValue(
            Core.ScriptRef.GetCurrentGameVar("$BOT~USERNAME", string.Empty),
            Core.ScriptRef.GetCurrentGameVar("$username", string.Empty));
        string gameLetter = FirstMeaningfulMombotValue(
            Core.ScriptRef.GetCurrentGameVar("$BOT~LETTER", string.Empty),
            Core.ScriptRef.GetCurrentGameVar("$letter", string.Empty));
        string dorelog = FirstMeaningfulMombotValue(
            Core.ScriptRef.GetCurrentGameVar("$BOT~DORELOG", string.Empty),
            Core.ScriptRef.GetCurrentGameVar("$doRelog", string.Empty),
            "0");
        string version = GetMombotVersionDisplay();

        await _gameInstance.SendToServerAsync(System.Text.Encoding.ASCII.GetBytes(
            TranslateMombotBurstText($"'{{{botName}}} - is ACTIVE: Version - {version} - type \"{botName} help\" for command list*")));
        await _gameInstance.SendToServerAsync(System.Text.Encoding.ASCII.GetBytes(
            TranslateMombotBurstText($"'{{{botName}}} - to login - send a corporate memo*")));

        string? corpUsersMessage = BuildMombotStartupCorpUsersMessage();
        if (!string.IsNullOrWhiteSpace(corpUsersMessage))
        {
            await _gameInstance.SendToServerAsync(System.Text.Encoding.ASCII.GetBytes(
                TranslateMombotBurstText(corpUsersMessage)));
        }

        if (string.IsNullOrWhiteSpace(loginName) ||
            string.IsNullOrWhiteSpace(gameLetter) ||
            !string.Equals(dorelog, "1", StringComparison.OrdinalIgnoreCase))
        {
            await _gameInstance.SendToServerAsync(System.Text.Encoding.ASCII.GetBytes(
                TranslateMombotBurstText($"'{{{botName}}} - Auto Relog - Not Active*")));
        }
    }

    private string? BuildMombotStartupCorpUsersMessage()
    {
        string? botUsersPath = ResolveCurrentMombotBotUsersFilePath();
        if (string.IsNullOrWhiteSpace(botUsersPath) || !File.Exists(botUsersPath))
            return null;

        string[] names = File.ReadLines(botUsersPath)
            .Select(line => line.Trim())
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (names.Length == 0)
            return null;

        string namesDisplay = names.Length switch
        {
            1 => names[0],
            2 => $"{names[0]} and {names[1]}",
            _ => $"{string.Join(", ", names.Take(names.Length - 1))}, and {names[^1]}"
        };
        string suffix = names.Length == 1 ? " is added.*" : " are added.*";
        return $"'[General] {{{ReadCurrentMombotVar("mombot", "$SWITCHBOARD~BOT_NAME", "$SWITCHBOARD~bot_name", "$bot~bot_name", "$bot_name")}}} - Logging corp mates automatically - {namesDisplay}{suffix}";
    }

    private string? ResolveCurrentMombotBotUsersFilePath()
    {
        string relativePath = ReadCurrentMombotVar(string.Empty, "$BOT_USER_FILE");
        if (string.IsNullOrWhiteSpace(relativePath))
            return null;

        string normalizedRelativePath = relativePath.Replace('\\', '/').Trim();
        if (Path.IsPathRooted(normalizedRelativePath))
            return normalizedRelativePath;

        string programDir = CurrentInterpreter?.ProgramDir ?? GetEffectiveProxyProgramDir(GetEffectiveProxyScriptDirectory());
        return Path.GetFullPath(Path.Combine(programDir, normalizedRelativePath.Replace('/', Path.DirectorySeparatorChar)));
    }

    private void PrimeMombotBootstrapState(
        Core.BotConfig botConfig,
        bool preserveDoNotResuscitate = false,
        bool preserveShipDestroyed = false)
    {
        string programDir = CurrentInterpreter?.ProgramDir ?? GetEffectiveProxyProgramDir(GetEffectiveProxyScriptDirectory());
        string scriptRoot = GetNativeMombotScriptRoot(botConfig).Trim().Trim('/');
        string scriptRootRelative = GetMombotScriptRootRelative(scriptRoot);
        string majorVersion = "5";
        string minorVersion = "0beta";

        string gameName = _embeddedGameName ?? DeriveGameName();
        string legacyFolderRelative = Path.Combine(scriptRoot, "games", gameName).Replace('\\', '/');
        string legacyFolderFullPath = Path.Combine(programDir, legacyFolderRelative.Replace('/', Path.DirectorySeparatorChar));
        string folderRelative = Path.Combine("games", gameName).Replace('\\', '/');
        string folderFullPath = Path.Combine(programDir, folderRelative.Replace('/', Path.DirectorySeparatorChar));
        EnsureMombotGameFolderMigrated(legacyFolderFullPath, folderFullPath);
        Directory.CreateDirectory(folderFullPath);

        string folderConfigRelative = Path.Combine("scripts", "mombot4_7beta.cfg").Replace('\\', '/');
        string folderConfigFullPath = Path.Combine(programDir, folderConfigRelative.Replace('/', Path.DirectorySeparatorChar));
        string mombotConfigRelative = Path.Combine(scriptRoot, "mombot.cfg").Replace('\\', '/');
        string mombotConfigFullPath = Path.Combine(programDir, mombotConfigRelative.Replace('/', Path.DirectorySeparatorChar));
        string aliasesConfigRelative = Path.Combine(scriptRoot, "aliases.cfg").Replace('\\', '/');
        string aliasesConfigFullPath = Path.Combine(programDir, aliasesConfigRelative.Replace('/', Path.DirectorySeparatorChar));
        string legacyHotkeysFullPath = Path.Combine(programDir, scriptRoot, "hotkeys.cfg");
        string legacyCustomKeysFullPath = Path.Combine(programDir, scriptRoot, "custom_keys.cfg");
        string legacyCustomCommandsFullPath = Path.Combine(programDir, scriptRoot, "custom_commands.cfg");
        string gconfigPath = Path.Combine(folderFullPath, "bot.cfg");
        bool hadExistingBotConfig = File.Exists(gconfigPath);
        string gconfigRelative = Path.Combine(folderRelative, "bot.cfg").Replace('\\', '/');
        string botUsersRelative = Path.Combine(folderRelative, "bot_users.lst").Replace('\\', '/');
        string ckFigRelative = Path.Combine(folderRelative, "_ck_" + gameName + ".figs").Replace('\\', '/');
        string shipCapRelative = Path.Combine(folderRelative, "ships.cfg").Replace('\\', '/');
        string planetFileRelative = Path.Combine(folderRelative, "planets.cfg").Replace('\\', '/');
        string planetProdsRelative = Path.Combine(folderRelative, "planetprods.cfg").Replace('\\', '/');
        string figFileRelative = Path.Combine(folderRelative, "fighters.cfg").Replace('\\', '/');
        string figCountRelative = Path.Combine(folderRelative, "fighters.cnt").Replace('\\', '/');
        string limpetFileRelative = Path.Combine(folderRelative, "limpets.cfg").Replace('\\', '/');
        string limpetCountRelative = Path.Combine(folderRelative, "limpets.cnt").Replace('\\', '/');
        string armidFileRelative = Path.Combine(folderRelative, "armids.cfg").Replace('\\', '/');
        string armidCountRelative = Path.Combine(folderRelative, "armids.cnt").Replace('\\', '/');
        string gameSettingsRelative = Path.Combine(folderRelative, "game_settings.cfg").Replace('\\', '/');
        string scriptFileRelative = Path.Combine(scriptRoot, "hotkey_scripts.cfg").Replace('\\', '/');
        string bustFileRelative = Path.Combine(folderRelative, "busts.cfg").Replace('\\', '/');
        string timerFileRelative = Path.Combine(folderRelative, "timer.cfg").Replace('\\', '/');
        string mcicFileRelative = Path.Combine(folderRelative, "planet.nego").Replace('\\', '/');

        EnsureMombotFolderConfigFile(folderConfigFullPath, scriptRootRelative);
        EnsureMombotHotkeyConfigFile(
            mombotConfigFullPath,
            legacyHotkeysFullPath,
            legacyCustomKeysFullPath,
            legacyCustomCommandsFullPath);
        EnsureMombotAliasConfigFile(aliasesConfigFullPath);

        string fileBotName = string.Empty;
        try
        {
            if (hadExistingBotConfig)
                fileBotName = File.ReadLines(gconfigPath).FirstOrDefault()?.Trim() ?? string.Empty;
        }
        catch
        {
        }

        string botName = FirstMeaningfulMombotValue(
            Core.ScriptRef.GetCurrentGameVar("$BOT~BOT_NAME", string.Empty),
            Core.ScriptRef.GetCurrentGameVar("$SWITCHBOARD~BOT_NAME", string.Empty),
            Core.ScriptRef.GetCurrentGameVar("$SWITCHBOARD~bot_name", string.Empty),
            Core.ScriptRef.GetCurrentGameVar("$bot~bot_name", string.Empty),
            Core.ScriptRef.GetCurrentGameVar("$bot_name", string.Empty),
            fileBotName,
            _mombot.Settings.BotName,
            "mombot");
        string teamName = FirstMeaningfulMombotValue(
            Core.ScriptRef.GetCurrentGameVar("$BOT~BOT_TEAM_NAME", string.Empty),
            Core.ScriptRef.GetCurrentGameVar("$BOT~bot_team_name", string.Empty),
            Core.ScriptRef.GetCurrentGameVar("$bot~bot_team_name", string.Empty),
            Core.ScriptRef.GetCurrentGameVar("$bot_team_name", string.Empty),
            _mombot.Settings.TeamName,
            botName);
        string subspace = ReadCurrentMombotVar("0", "$BOT~SUBSPACE", "$bot~subspace", "$subspace");
        string botPassword = ReadCurrentMombotVar(string.Empty, "$BOT~BOT_PASSWORD", "$bot~bot_password", "$bot_password");
        if (string.IsNullOrWhiteSpace(botPassword) && subspace != "0")
            botPassword = subspace;
        string loginName = ReadEmbeddedPersistedMombotVar(
            NormalizeMombotValue(_embeddedGameConfig?.LoginName, treatSelfAsEmpty: true),
            "$BOT~USERNAME",
            "$username");
        string serverName = ReadEmbeddedPersistedMombotVar(
            loginName,
            "$BOT~SERVERNAME",
            "$servername");
        string loginPassword = ReadEmbeddedPersistedMombotVar(
            NormalizeMombotValue(_embeddedGameConfig?.Password),
            "$BOT~PASSWORD",
            "$password");
        string gameLetter = ReadEmbeddedPersistedMombotVar(
            NormalizeGameLetter(_embeddedGameConfig?.GameLetter),
            "$BOT~LETTER",
            "$letter",
            "$LETTER");
        string currentSector = Core.ScriptRef.GetCurrentSector() > 0 ? Core.ScriptRef.GetCurrentSector().ToString() : FormatMombotSector((ushort)_state.Sector);
        string currentPrompt = GetInitialMombotPromptName();

        SetMombotCurrentVars(majorVersion, "$bot~major_version", "$major_version", "$BOT~MAJOR_VERSION");
        SetMombotCurrentVars(minorVersion, "$bot~minor_version", "$minor_version", "$BOT~MINOR_VERSION");
        SetMombotCurrentVars(scriptRootRelative, "$bot~default_bot_directory", "$default_bot_directory");
        SetMombotCurrentVars(scriptRootRelative, "$bot~mombot_directory", "$mombot_directory", "$BOT~MOMBOT_DIRECTORY");
        PersistMombotVars(folderConfigRelative, "$mombot_folder_config");
        PersistMombotVars(mombotConfigRelative, "$mombot_config_file");
        PersistMombotVars(aliasesConfigRelative, "$aliases_file");
        PersistMombotVars(mombotConfigRelative, "$hotkeys_file");
        PersistMombotVars(mombotConfigRelative, "$custom_keys_file");
        PersistMombotVars(mombotConfigRelative, "$custom_commands_file");
        PersistMombotVars(folderRelative, "$folder", "$BOT~FOLDER");
        PersistMombotVars(gconfigRelative, "$gconfig_file", "$BOT~GCONFIG_FILE");
        PersistMombotVars(botUsersRelative, "$BOT_USER_FILE", "$BOT~BOT_USER_FILE");
        PersistMombotVars(ckFigRelative, "$CK_FIG_FILE", "$BOT~CK_FIG_FILE");
        PersistMombotVars(shipCapRelative, "$SHIP~cap_file", "$SHIP~CAP_FILE", "$ship~cap_file", "$cap_file");
        PersistMombotVars(planetFileRelative, "$PLANET~planet_file", "$PLANET~PLANET_FILE", "$planet~planet_file", "$planet_file");
        PersistMombotVars(planetProdsRelative, "$PLANET~planet_prods_file", "$PLANET~PLANET_PRODS_FILE", "$planet~planet_prods_file", "$planet_prods_file");
        PersistMombotVars(figFileRelative, "$FIG_FILE", "$BOT~FIG_FILE");
        PersistMombotVars(figCountRelative, "$FIG_COUNT_FILE", "$BOT~FIG_COUNT_FILE");
        PersistMombotVars(limpetFileRelative, "$LIMP_FILE", "$BOT~LIMP_FILE");
        PersistMombotVars(limpetCountRelative, "$LIMP_COUNT_FILE", "$BOT~LIMP_COUNT_FILE");
        PersistMombotVars(armidFileRelative, "$ARMID_FILE", "$BOT~ARMID_FILE");
        PersistMombotVars(armidCountRelative, "$ARMID_COUNT_FILE", "$BOT~ARMID_COUNT_FILE");
        PersistMombotVars(gameSettingsRelative, "$GAME~GAME_SETTINGS_FILE");
        PersistMombotVars(scriptFileRelative, "$SCRIPT_FILE", "$BOT~SCRIPT_FILE");
        PersistMombotVars(bustFileRelative, "$BUST_FILE", "$BOT~BUST_FILE");
        PersistMombotVars(timerFileRelative, "$timer_file", "$BOT~TIMER_FILE");
        PersistMombotVars(mcicFileRelative, "$MCIC_FILE", "$BOT~MCIC_FILE");

        SetMombotCurrentVars(botName, "$BOT~BOT_NAME", "$SWITCHBOARD~BOT_NAME", "$SWITCHBOARD~bot_name", "$bot~bot_name", "$bot_name", "$bot~name");
        SetMombotCurrentVars(teamName, "$BOT~BOT_TEAM_NAME", "$BOT~bot_team_name", "$bot~bot_team_name", "$bot_team_name");
        SetMombotCurrentVars(botPassword, "$BOT~BOT_PASSWORD", "$bot~bot_password", "$bot_password");
        SetMombotCurrentVars(_state.TraderName?.Trim() ?? string.Empty, "$PLAYER~TRADER_NAME");
        SetMombotCurrentVars(currentSector, "$PLAYER~CURRENT_SECTOR", "$player~current_sector");
        SetMombotCurrentVars(currentPrompt, "$PLAYER~CURRENT_PROMPT", "$PLAYER~startingLocation", "$bot~startingLocation");
        SetMombotCurrentVars(string.Empty, "$BOT~COMMAND", "$bot~command", "$command");
        SetMombotCurrentVars(string.Empty, "$BOT~USER_COMMAND_LINE", "$bot~user_command_line", "$USER_COMMAND_LINE", "$user_command_line");
        SetMombotCurrentVars(loginPassword, "$BOT~PASSWORD", "$password");
        SetMombotCurrentVars(loginName, "$BOT~USERNAME", "$username");
        SetMombotCurrentVars(serverName, "$BOT~SERVERNAME", "$servername");
        SetMombotCurrentVars(gameLetter, "$BOT~LETTER", "$letter", "$LETTER");
        MirrorMombotCurrentVars(subspace, "$BOT~SUBSPACE", "$bot~subspace", "$subspace");
        MirrorMombotCurrentVars("General", "$BOT~MODE", "$bot~mode", "$mode");
        MirrorMombotCurrentVars(string.Empty, "$BOT~LAST_LOADED_MODULE", "$LAST_LOADED_MODULE");
        MirrorMombotCurrentVars("0", "$BOT~BOT_TURN_LIMIT", "$bot~bot_turn_limit", "$bot_turn_limit");
        MirrorMombotCurrentVars("0", "$BOT~SAFE_SHIP", "$bot~safe_ship", "$safe_ship");
        MirrorMombotCurrentVars("0", "$BOT~SAFE_PLANET", "$bot~safe_planet", "$safe_planet");
        MirrorMombotCurrentVars("0", "$BOT~BOTISDEAF", "$BOT~botIsDeaf", "$bot~botIsDeaf", "$botIsDeaf");
        MirrorMombotCurrentVars("0", "$BOT~SILENT_RUNNING", "$bot~silent_running", "$silent_running");
        MirrorMombotCurrentVars("0", "$PLAYER~UNLIMITEDGAME", "$PLAYER~unlimitedGame", "$unlimitedGame");
        MirrorMombotCurrentVars("0", "$PLAYER~defenderCapping");
        MirrorMombotCurrentVars("0", "$PLAYER~offenseCapping", "$offenseCapping");
        MirrorMombotCurrentVars("0", "$PLAYER~cappingAliens", "$cappingAliens");
        MirrorMombotCurrentVars("0", "$PLAYER~dropOffensive", "$PLAYER~DROPOFFENSIVE");
        MirrorMombotCurrentVars("0", "$PLAYER~dropToll", "$PLAYER~DROPTOLL");
        // Normal fresh starts clear stale stop state, but ship-destroyed restarts
        // must keep it long enough to force the relog menu.
        SetMombotCurrentVars(preserveDoNotResuscitate ? "1" : "0", "$BOT~DO_NOT_RESUSCITATE", "$bot~do_not_resuscitate", "$do_not_resuscitate");
        MirrorMombotCurrentVars("0", "$SETTINGS~OVERRIDE", "$settings~override");
        MirrorMombotCurrentVars("0", "$GAME~PORT_MAX", "$GAME~port_max", "$game~port_max");
        MirrorMombotCurrentVars("0", "$GAME~PHOTON_DURATION", "$game~photon_duration");
        MirrorMombotCurrentVars("0", "$PLAYER~surroundFigs", "$PLAYER~SURROUNDFIGS");
        MirrorMombotCurrentVars("0", "$PLAYER~surroundLimp", "$PLAYER~SURROUNDLIMP");
        MirrorMombotCurrentVars("0", "$PLAYER~surroundMine", "$PLAYER~SURROUNDMINE");
        MirrorMombotCurrentVars("0", "$PLAYER~surroundOverwrite");
        MirrorMombotCurrentVars("0", "$PLAYER~surroundPassive");
        MirrorMombotCurrentVars("0", "$PLAYER~surroundNormal");
        MirrorMombotCurrentVars("0", "$PLAYER~surroundAvoidShieldedOnly");
        MirrorMombotCurrentVars("0", "$PLAYER~surroundAvoidAllPlanets");
        MirrorMombotCurrentVars("0", "$PLAYER~surroundDontAvoid");
        MirrorMombotCurrentVars("0", "$PLAYER~surround_before_hkill");
        MirrorMombotCurrentVars("0", "$surroundAutoCapture");
        MirrorMombotCurrentVars("0", "$pgrid_bot");
        MirrorMombotCurrentVars("0", "$autoattack");
        MirrorMombotCurrentVars(string.Empty, "$BOT~HISTORYSTRING", "$HISTORYSTRING");
        MirrorMombotCurrentVars(string.Empty, "$command_prompt_extras");
        MirrorMombotCurrentVars("5760", "$echoInterval");
        MirrorMombotCurrentVars(hadExistingBotConfig ? "1" : "0", "$BOT~DORELOG", "$doRelog");
        MirrorMombotCurrentVars("0", "$BOT~NEWGAMEDAY1", "$newGameDay1");
        MirrorMombotCurrentVars("0", "$BOT~NEWGAMEOLDER", "$newGameOlder");
        SetMombotCurrentVars(preserveShipDestroyed ? "1" : "0", "$BOT~ISSHIPDESTROYED", "$bot~isShipDestroyed");
        MirrorMombotCurrentVars("0", "$relogging", "$connectivity~relogging");
        MirrorMombotCurrentVars(string.Empty, "$command_caller", "$BOT~COMMAND_CALLER", "$bot~command_caller");
        MirrorMombotCurrentVars("0", "$SWITCHBOARD~SELF_COMMAND", "$switchboard~self_command", "$BOT~SELF_COMMAND", "$bot~self_command", "$self_command");
        SetRedAlertVars("FALSE");
        PersistMombotVars(shipCapRelative, "$cap_file");
        PersistMombotVars(planetFileRelative, "$planet_file");
        PersistMombotVars(planetProdsRelative, "$planet_prods_file");

        SyncMombotSpecialSectorVarsFromDatabase(persist: true);
        MirrorMombotCurrentVars("0", "$MAP~BACKDOOR", "$MAP~backdoor", "$backdoor");
        MirrorMombotCurrentVars("0", "$MAP~HOME_SECTOR", "$MAP~home_sector", "$BOT~HOME_SECTOR", "$home_sector");

        if (!string.IsNullOrWhiteSpace(botName))
        {
            try
            {
                File.WriteAllText(gconfigPath, botName + Environment.NewLine);
            }
            catch
            {
            }
        }

        string surroundShieldedOnly = ReadCurrentMombotVar("0", "$PLAYER~surroundAvoidShieldedOnly");
        string surroundAllPlanets = ReadCurrentMombotVar("0", "$PLAYER~surroundAvoidAllPlanets");
        string surroundDontAvoid = ReadCurrentMombotVar("0", "$PLAYER~surroundDontAvoid");
        if (surroundShieldedOnly == "0" && surroundAllPlanets == "0" && surroundDontAvoid == "0")
            SetMombotCurrentVars("1", "$PLAYER~surroundAvoidAllPlanets");

        if (ReadCurrentMombotVar("0", "$PLAYER~surroundFigs", "$PLAYER~SURROUNDFIGS") == "0")
            SetMombotCurrentVars("1", "$PLAYER~surroundFigs", "$PLAYER~SURROUNDFIGS");
    }

    private static string GetMombotScriptRootRelative(string scriptRoot)
    {
        string normalized = (scriptRoot ?? string.Empty)
            .Replace('\\', '/')
            .Trim()
            .Trim('/');
        if (normalized.StartsWith("scripts/", StringComparison.OrdinalIgnoreCase))
            normalized = normalized["scripts/".Length..];
        else if (string.Equals(normalized, "scripts", StringComparison.OrdinalIgnoreCase))
            normalized = string.Empty;

        return string.IsNullOrWhiteSpace(normalized) ? "mombot" : normalized;
    }

    private static void EnsureMombotFolderConfigFile(string fullPath, string scriptRootRelative)
    {
        try
        {
            string? directory = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);

            string currentValue = File.Exists(fullPath)
                ? File.ReadLines(fullPath).FirstOrDefault()?.Trim() ?? string.Empty
                : string.Empty;
            if (!string.Equals(currentValue, scriptRootRelative, StringComparison.Ordinal))
                File.WriteAllText(fullPath, scriptRootRelative + Environment.NewLine);
        }
        catch
        {
        }
    }

    private sealed record MombotHotkeyConfigData(
        string[] Hotkeys,
        string[] CustomKeys,
        string[] CustomCommands);

    private static void EnsureMombotHotkeyConfigFile(
        string configPath,
        string legacyHotkeysPath,
        string legacyCustomKeysPath,
        string legacyCustomCommandsPath)
    {
        try
        {
            string? directory = Path.GetDirectoryName(configPath);
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);

            if (!TryLoadMombotHotkeyConfigFromFile(configPath, out _))
            {
                MombotHotkeyConfigData config = TryLoadLegacyMombotHotkeyConfig(
                    legacyCustomKeysPath,
                    legacyCustomCommandsPath,
                    out MombotHotkeyConfigData? migrated)
                    ? migrated!
                    : BuildDefaultMombotHotkeyConfigData();
                WriteMombotHotkeyConfigFile(configPath, config);
            }

            foreach (string legacyPath in new[] { legacyHotkeysPath, legacyCustomKeysPath, legacyCustomCommandsPath })
            {
                try
                {
                    if (!string.Equals(Path.GetFullPath(legacyPath), Path.GetFullPath(configPath), StringComparison.OrdinalIgnoreCase) &&
                        File.Exists(legacyPath))
                    {
                        File.Delete(legacyPath);
                    }
                }
                catch
                {
                }
            }
        }
        catch
        {
        }
    }

    private static void EnsureMombotAliasConfigFile(string configPath)
    {
        try
        {
            string? directory = Path.GetDirectoryName(configPath);
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);

            if (File.Exists(configPath))
                return;

            File.WriteAllLines(configPath, BuildDefaultMombotAliasConfigFileLines());
        }
        catch
        {
        }
    }

    private static IReadOnlyList<string> BuildDefaultMombotAliasConfigFileLines()
    {
        return new[]
        {
            "# mombot command aliases",
            "# format: alias[,alias...]=real command",
            string.Empty,
        };
    }

    private static void EnsureMombotGameFolderMigrated(string legacyFolderPath, string folderPath)
    {
        try
        {
            Directory.CreateDirectory(folderPath);
            if (string.IsNullOrWhiteSpace(legacyFolderPath) ||
                string.Equals(Path.GetFullPath(legacyFolderPath), Path.GetFullPath(folderPath), StringComparison.OrdinalIgnoreCase) ||
                !Directory.Exists(legacyFolderPath))
            {
                return;
            }

            MergeMombotGameFolderContents(legacyFolderPath, folderPath);
            DeleteEmptyMombotGameFolderTree(legacyFolderPath);
        }
        catch
        {
        }
    }

    private static void MergeMombotGameFolderContents(string sourceDirectory, string destinationDirectory)
    {
        Directory.CreateDirectory(destinationDirectory);

        foreach (string directory in Directory.EnumerateDirectories(sourceDirectory))
        {
            string name = Path.GetFileName(directory);
            if (string.IsNullOrWhiteSpace(name))
                continue;

            MergeMombotGameFolderContents(directory, Path.Combine(destinationDirectory, name));
        }

        foreach (string file in Directory.EnumerateFiles(sourceDirectory))
        {
            string name = Path.GetFileName(file);
            if (string.IsNullOrWhiteSpace(name))
                continue;

            string destinationFile = Path.Combine(destinationDirectory, name);
            if (File.Exists(destinationFile))
                continue;

            string? destinationParent = Path.GetDirectoryName(destinationFile);
            if (!string.IsNullOrWhiteSpace(destinationParent))
                Directory.CreateDirectory(destinationParent);

            File.Move(file, destinationFile);
        }
    }

    private static void DeleteEmptyMombotGameFolderTree(string directory)
    {
        foreach (string child in Directory.EnumerateDirectories(directory))
            DeleteEmptyMombotGameFolderTree(child);

        if (!Directory.EnumerateFileSystemEntries(directory).Any())
            Directory.Delete(directory, false);
    }

    private static IReadOnlyList<string> BuildDefaultMombotCustomKeyFileLines()
    {
        string[] lines = Enumerable.Repeat("0", 33).ToArray();
        string[] defaults =
        {
            "K", "C", "R", "S", "H", "T", "P", "Q", "L", "\t", "D", "X", "M", "F", "Z", "~", "B",
        };

        Array.Copy(defaults, lines, defaults.Length);
        return lines;
    }

    private static IReadOnlyList<string> BuildDefaultMombotCustomCommandFileLines()
    {
        string[] lines = Enumerable.Repeat("0", 33).ToArray();
        string[] defaults =
        {
            ":INTERNAL_COMMANDS~autokill",
            ":INTERNAL_COMMANDS~autocap",
            ":INTERNAL_COMMANDS~autorefurb",
            ":INTERNAL_COMMANDS~surround",
            ":INTERNAL_COMMANDS~htorp",
            ":INTERNAL_COMMANDS~twarpswitch",
            ":INTERNAL_COMMANDS~kit",
            ":USER_INTERFACE~script_access",
            ":INTERNAL_COMMANDS~hkill",
            ":INTERNAL_COMMANDS~stopModules",
            ":INTERNAL_COMMANDS~kit",
            ":INTERNAL_COMMANDS~xenter",
            ":INTERNAL_COMMANDS~mowswitch",
            ":INTERNAL_COMMANDS~fotonswitch",
            ":INTERNAL_COMMANDS~clear",
            ":MENUS~preferencesMenu",
            ":INTERNAL_COMMANDS~dock_shopper",
        };

        Array.Copy(defaults, lines, defaults.Length);
        return lines;
    }

    private static MombotHotkeyConfigData BuildDefaultMombotHotkeyConfigData()
    {
        string[] customKeys = BuildDefaultMombotCustomKeyFileLines().ToArray();
        string[] customCommands = BuildDefaultMombotCustomCommandFileLines().ToArray();
        return new MombotHotkeyConfigData(
            BuildMombotHotkeyIndex(customKeys),
            customKeys,
            customCommands);
    }

    private static bool TryLoadLegacyMombotHotkeyConfig(
        string legacyCustomKeysPath,
        string legacyCustomCommandsPath,
        out MombotHotkeyConfigData? config)
    {
        config = null;
        try
        {
            if (!File.Exists(legacyCustomKeysPath) || !File.Exists(legacyCustomCommandsPath))
                return false;

            string[] customKeys = File.ReadAllLines(legacyCustomKeysPath);
            string[] customCommands = File.ReadAllLines(legacyCustomCommandsPath);
            if (customKeys.Length != 33 || customCommands.Length != 33)
                return false;

            config = new MombotHotkeyConfigData(
                BuildMombotHotkeyIndex(customKeys),
                NormalizeMombotCustomLines(customKeys, 33),
                NormalizeMombotCustomLines(customCommands, 33));
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryLoadMombotHotkeyConfigFromFile(string configPath, out MombotHotkeyConfigData? config)
    {
        config = null;
        try
        {
            if (!File.Exists(configPath))
                return false;

            string[] lines = File.ReadAllLines(configPath);
            if (lines.Length != 33)
                return false;

            string[] customKeys = Enumerable.Repeat("0", 33).ToArray();
            string[] customCommands = Enumerable.Repeat("0", 33).ToArray();
            for (int lineIndex = 0; lineIndex < lines.Length; lineIndex++)
            {
                string line = lines[lineIndex].Trim();
                if (string.IsNullOrWhiteSpace(line))
                    return false;

                string[] parts = line.Split('$');
                int slot = lineIndex + 1;
                string keyToken;
                string commandToken;
                if (parts.Length >= 3 && int.TryParse(parts[0].Trim(), out int explicitSlot))
                {
                    if (explicitSlot < 1 || explicitSlot > 33)
                        return false;
                    slot = explicitSlot;
                    keyToken = parts[1].Trim();
                    commandToken = string.Join("$", parts.Skip(2)).Trim();
                }
                else if (parts.Length >= 2)
                {
                    keyToken = parts[0].Trim();
                    commandToken = string.Join("$", parts.Skip(1)).Trim();
                }
                else
                {
                    return false;
                }

                if (!TryDecodeMombotHotkeyToken(keyToken, out string normalizedKey))
                    return false;

                customKeys[slot - 1] = normalizedKey;
                customCommands[slot - 1] = string.IsNullOrWhiteSpace(commandToken) ? "0" : commandToken;
            }

            config = new MombotHotkeyConfigData(
                BuildMombotHotkeyIndex(customKeys),
                customKeys,
                customCommands);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string[] NormalizeMombotCustomLines(string[] source, int count)
    {
        string[] normalized = Enumerable.Repeat("0", count).ToArray();
        for (int i = 0; i < Math.Min(count, source.Length); i++)
        {
            string value = source[i].Trim();
            normalized[i] = string.IsNullOrWhiteSpace(value) ? "0" : value;
        }

        return normalized;
    }

    private static string[] BuildMombotHotkeyIndex(IReadOnlyList<string> customKeys)
    {
        string[] hotkeys = Enumerable.Repeat("0", 255).ToArray();
        for (int slot = 1; slot <= Math.Min(33, customKeys.Count); slot++)
        {
            string keyToken = customKeys[slot - 1];
            if (!TryDecodeMombotHotkeyToken(keyToken, out string normalizedKey) ||
                string.IsNullOrWhiteSpace(normalizedKey) ||
                normalizedKey == "0")
            {
                continue;
            }

            char hotkey = normalizedKey[0];
            int lower = char.ToLowerInvariant(hotkey);
            int upper = char.ToUpperInvariant(hotkey);
            if (lower >= 1 && lower <= hotkeys.Length)
                hotkeys[lower - 1] = slot.ToString();
            if (upper >= 1 && upper <= hotkeys.Length)
                hotkeys[upper - 1] = slot.ToString();
        }

        return hotkeys;
    }

    private static bool TryDecodeMombotHotkeyToken(string token, out string normalizedKey)
    {
        normalizedKey = "0";
        string trimmed = (token ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(trimmed) || trimmed == "0")
            return true;

        return trimmed.ToUpperInvariant() switch
        {
            "TAB" => AssignHotkey("\t", out normalizedKey),
            "ENTER" => AssignHotkey("\r", out normalizedKey),
            "BACKSPACE" => AssignHotkey("\b", out normalizedKey),
            "SPACE" => AssignHotkey(" ", out normalizedKey),
            _ => AssignHotkey(trimmed[..1], out normalizedKey),
        };
    }

    private static bool AssignHotkey(string value, out string normalizedKey)
    {
        normalizedKey = value;
        return !string.IsNullOrEmpty(value);
    }

    private static string EncodeMombotHotkeyToken(string key)
    {
        return key switch
        {
            "\t" => "TAB",
            "\r" => "ENTER",
            "\b" => "BACKSPACE",
            " " => "SPACE",
            "" => "0",
            "0" => "0",
            _ => key[..1],
        };
    }

    private static IReadOnlyList<string> BuildMombotConfigLines(MombotHotkeyConfigData config)
    {
        var lines = new string[33];
        for (int slot = 1; slot <= 33; slot++)
        {
            string key = slot <= config.CustomKeys.Length ? config.CustomKeys[slot - 1] : "0";
            string command = slot <= config.CustomCommands.Length ? config.CustomCommands[slot - 1] : "0";
            if (string.IsNullOrWhiteSpace(command))
                command = "0";
            lines[slot - 1] = $"{slot}${EncodeMombotHotkeyToken(key)}${command}";
        }

        return lines;
    }

    private static void WriteMombotHotkeyConfigFile(string configPath, MombotHotkeyConfigData config)
    {
        string? directory = Path.GetDirectoryName(configPath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);
        File.WriteAllLines(configPath, BuildMombotConfigLines(config));
    }


    private async Task ShowMombotGridMenuAsync(bool photonMode = false)
    {
        if (_gameInstance == null)
        {
            await ShowMessageAsync("Mombot", "Mombot commands are only available while the embedded proxy is running.");
            return;
        }

        if (!_mombot.Enabled)
        {
            await ShowMessageAsync("Mombot", "Enable Mombot first.");
            return;
        }

        MombotGridContext context = BuildMombotGridContext();
        if (!context.Connected)
        {
            await ShowMessageAsync("Mombot", "The grid menu needs an active game connection.");
            return;
        }

        if (context.Surface != MombotPromptSurface.Command && context.Surface != MombotPromptSurface.Citadel)
        {
            await ShowMessageAsync("Mombot", "The grid menu is only available from command or citadel prompts.");
            return;
        }

        string? action = null;
        string surfaceLabel = context.Surface == MombotPromptSurface.Citadel ? "Citadel" : "Command";
        Window? gridDialog = null;
        var actions = new WrapPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            ItemHeight = 34,
            ItemWidth = 120,
        };

        void AddActionButton(string label, string actionValue, bool enabled = true)
        {
            var button = new Button
            {
                Content = label,
                IsEnabled = enabled,
                Margin = new Thickness(4),
                MinWidth = 110,
            };
            button.Click += (_, _) =>
            {
                action = actionValue;
                gridDialog?.Close();
            };
            actions.Children.Add(button);
        }

        if (!photonMode)
        {
            AddActionButton("Holo", "scan:holo");
            AddActionButton("Density", "scan:density");
            AddActionButton("Surround", "cmd:surround");
            AddActionButton("Photon…", "menu:photon", context.PhotonCount > 0 && context.AdjacentSectors.Count > 0);
        }

        foreach (int sector in context.AdjacentSectors)
        {
            string verb = photonMode
                ? $"Photon {sector}"
                : (context.Surface == MombotPromptSurface.Citadel ? $"PGrid {sector}" : $"Move {sector}");
            AddActionButton(verb, (photonMode ? "photon:" : "move:") + sector);
        }

        var closeBtn = new Button { Content = "Close", MinWidth = 96 };
        var dlg = new Window
        {
            Title = photonMode ? "Mombot Photon Menu" : "Mombot Grid Menu",
            Width = 620,
            Height = 280,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
            Background = BgPanel,
            Content = new StackPanel
            {
                Margin = new Thickness(20),
                Spacing = 14,
                Children =
                {
                    new TextBlock
                    {
                        Text = photonMode ? "Photon Menu" : "Grid Menu",
                        Foreground = FgTitle,
                        FontSize = 13,
                        FontWeight = Avalonia.Media.FontWeight.SemiBold,
                    },
                    new TextBlock
                    {
                        Text = $"Prompt: {surfaceLabel}   Sector: {context.CurrentSector}",
                        Foreground = FgKey,
                    },
                    new TextBlock
                    {
                        Text = context.AdjacentSectors.Count == 0
                            ? "No adjacent sectors are known in the current database."
                            : "Choose a scan, a bot action, or an adjacent sector.",
                        Foreground = FgKey,
                        TextWrapping = TextWrapping.Wrap,
                    },
                    actions,
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        Children = { closeBtn },
                    },
                },
            },
        };
        gridDialog = dlg;

        closeBtn.Click += (_, _) => dlg.Close();
        await dlg.ShowDialog(this);

        if (string.IsNullOrWhiteSpace(action))
        {
            FocusActiveTerminal();
            return;
        }

        if (string.Equals(action, "scan:holo", StringComparison.Ordinal))
        {
            await SendMombotServerMacroAsync(BuildMombotScanMacro(holo: true, context));
            return;
        }

        if (string.Equals(action, "scan:density", StringComparison.Ordinal))
        {
            await SendMombotServerMacroAsync(BuildMombotScanMacro(holo: false, context));
            return;
        }

        if (string.Equals(action, "cmd:surround", StringComparison.Ordinal))
        {
            await ExecuteMombotUiCommandAsync("surround");
            return;
        }

        if (string.Equals(action, "menu:photon", StringComparison.Ordinal))
        {
            await ShowMombotGridMenuAsync(photonMode: true);
            return;
        }

        if (action.StartsWith("photon:", StringComparison.Ordinal) &&
            int.TryParse(action["photon:".Length..], out int photonSector))
        {
            await ExecuteMombotUiCommandAsync($"photon {photonSector}");
            return;
        }

        if (action.StartsWith("move:", StringComparison.Ordinal) &&
            int.TryParse(action["move:".Length..], out int moveSector))
        {
            if (context.Surface == MombotPromptSurface.Citadel)
                await ExecuteMombotUiCommandAsync($"pgrid {moveSector} scan");
            else
                await SendMombotServerMacroAsync(BuildMombotMoveMacro(moveSector));
        }
    }

}
