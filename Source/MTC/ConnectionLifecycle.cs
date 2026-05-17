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
    /// <summary>Connects using the current Host/Port already set in state.</summary>
    private void DoConnect()
    {
        if (_telnet.IsConnected) _telnet.Disconnect();
        _telnet.SetWindowSize(_buffer.Columns, _buffer.Rows);
        _ = _telnet.ConnectAsync(_state.Host, _state.Port)
                   .ContinueWith(t =>
                   {
                       if (t.IsFaulted)
                           Dispatcher.UIThread.Post(() =>
                               _parser.Feed($"\x1b[1;31m[Connect failed: {t.Exception?.InnerException?.Message}]\x1b[0m\r\n"));
                   });
    }

    /// <summary>
    /// Connects in embedded proxy mode: creates a <see cref="Core.GameInstance"/>,
    /// wires it to the terminal via in-process pipes, and lets scripts / user
    /// interact before the game server connection is made.
    /// </summary>
    private async Task DoConnectEmbeddedAsync()
    {
        // Wait for any in-flight stop to fully complete so its cleanup cannot
        // race with our setup (e.g. fast Disconnect→Connect or Reconnect).
        await _pendingEmbeddedStop;
        _pendingEmbeddedStop = Task.CompletedTask;

        // Stop an existing instance if somehow still attached.
        if (_gameInstance != null)
            await StopEmbeddedAsync();

        // Derive game name first (needed for the game config path and database path).
        string gameName = GetEmbeddedGameName();

        // Load (or create) the shared TWXP game config JSON.
        // This gives us the persisted variable state and the authoritative sector count.
        var gameConfig = await LoadOrCreateEmbeddedGameConfigAsync(gameName);
        ApplyEmbeddedConnectionState(gameName, gameConfig);
        bool configChanged =
            !string.Equals(gameConfig.Name, gameName, StringComparison.Ordinal) ||
            gameConfig.Host != _state.Host ||
            gameConfig.Port != _state.Port ||
            gameConfig.Sectors != _state.Sectors ||
            gameConfig.ListenPort != _state.ListenPort ||
            (gameConfig.Mtc?.ListenForConnections ?? false) != _state.ListenForConnections ||
            !string.Equals(gameConfig.DatabasePath, AppPaths.TwxproxyDatabasePathForGame(gameName), StringComparison.OrdinalIgnoreCase) ||
            gameConfig.AutoReconnect != _state.AutoReconnect;
        gameConfig = BuildEmbeddedGameConfigFromState(gameName, gameConfig);
        gameConfig.DatabasePath = AppPaths.TwxproxyDatabasePathForGame(gameName);
        if (configChanged)
            await SaveEmbeddedGameConfigAsync(gameName, gameConfig);
        _embeddedGameConfig = gameConfig;
        _embeddedGameName = gameName;
        _currentProfilePath = AppPaths.TwxproxyGameConfigFileFor(gameName);
        AddToRecentAndSave(_currentProfilePath);
        SyncMombotRuntimeConfigFromTwxpCfg(gameConfig);
        ApplySessionLogSettings(gameConfig);

        // Open / create the session database using sectors from the game config.
        OpenSessionDatabase(gameName, gameConfig.Sectors, useSharedProxyDatabase: true);

        // Resolve the effective script directory from the MTC-wide preference first,
        // then fall back to older per-game state only when no app-level setting exists.
        string effectiveScriptDir = ResolveEffectiveScriptDirectory(gameConfig.ScriptDirectory);

        // Create the script interpreter.
        string programDir = AppPaths.ProgramDir;
        var interpreter = new Core.ModInterpreter();
        interpreter.ScriptDirectory = effectiveScriptDir;
        interpreter.ProgramDir      = programDir;
        Core.GlobalModules.ProgramDir = programDir;  // shared global used by some script commands
        ApplyDebugLoggingPreferences();

        // Embedded mode needs a live menu manager so OPENMENU pauses and displays
        // configuration menus (same behavior as TWXP ProxyService startup).
        Core.GlobalModules.TWXMenu = new Core.MenuManager();

        // Load previously saved variables (excluding session-startup flags).
        gameConfig.Variables = NormalizeEmbeddedVariables(gameConfig.Variables);

        var varsToLoad = new System.Collections.Generic.Dictionary<string, string>(gameConfig.Variables, StringComparer.OrdinalIgnoreCase);
        varsToLoad.Remove("$gfile_chk");
        varsToLoad.Remove("$doRelog");
        ApplySessionStartupVarDefaults(varsToLoad);
        Core.ScriptRef.LoadVarsForGame(varsToLoad);

        // When savevar is called, persist the value into the TWXP game config JSON.
        Core.ScriptRef.OnVariableSaved = (varName, value) =>
        {
            if (string.Equals(varName, "$gfile_chk", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(varName, "$doRelog",   StringComparison.OrdinalIgnoreCase))
                return;
            gameConfig.Variables[varName] = value;
            _ = SaveEmbeddedGameConfigAsync(gameName, gameConfig);
        };

        SyncMombotSpecialSectorVarsFromDatabase(persist: true);
        BackfillScriptMombotBootstrapState(gameConfig, gameName, programDir);

        // Create GameInstance. MTC always attaches its own direct client; the TCP
        // listener is only started when the profile explicitly enables it.
        var gi = new Core.GameInstance(
            gameName,
            _state.Host,
            _state.Port,
            listenPort: gameConfig.ListenPort,
            commandChar: gameConfig.CommandChar == '\0' ? '$' : gameConfig.CommandChar,
            interpreter: interpreter,
            scriptDirectory: effectiveScriptDir)
        {
            Verbose       = false,          // suppress diagnostic Console.WriteLine in embedded mode
            AutoReconnect = _state.AutoReconnect,
        };
        gi.Logger.LogDirectory = AppPaths.GetDebugLogDir(effectiveScriptDir);
        gi.Logger.SetLogIdentity(gameName);
        gi.ReconnectDelayMs = Math.Max(1, gameConfig.ReconnectDelaySeconds) * 1000;
        gi.LocalEcho = gameConfig.LocalEcho;
        gi.AcceptExternal = gameConfig.AcceptExternal;
        gi.AllowLerkers = gameConfig.AllowLerkers;
        gi.ExternalAddress = gameConfig.ExternalAddress ?? string.Empty;
        gi.BroadCastMsgs = gameConfig.BroadcastMessages;
        gi.Logger.LogEnabled = false;
        gi.Logger.LogData = false;
        gi.Logger.LogANSI = gameConfig.LogAnsi;
        gi.Logger.LogAnsiCompanion = gameConfig.LogAnsiCompanion;
        gi.Logger.BinaryLogs = gameConfig.LogBinary;
        gi.Logger.NotifyPlayCuts = gameConfig.NotifyPlayCuts;
        gi.Logger.MaxPlayDelay = gameConfig.MaxPlayDelay;
        gi.SeedShipStatus(BuildShipStatusSeedFromCurrentState());
        gi.SetNativeHaggleEnabled(gameConfig.NativeHaggleEnabled, Core.NativeHaggleChangeSource.Config);
        Core.GlobalModules.DebugLog(
            $"[MTC] Embedded haggle startup prefsPortMode={ResolveGlobalPortHaggleMode()} prefsPlanetMode={ResolveGlobalPlanetHaggleMode()} legacyGameMode={gameConfig.NativeHaggleMode ?? "-"}\n");
        gi.SetNativeHaggleModes(ResolveGlobalPortHaggleMode(), ResolveGlobalPlanetHaggleMode());
        gi.NativeHaggleChanged += OnNativeHaggleChanged;
        gi.NativeHaggleStatsChanged += OnNativeHaggleStatsChanged;
        gi.ShipStatusUpdated += OnShipStatusUpdated;
        gi.NativeBotActivator = (botConfig, requestedBotName) =>
        {
            Dispatcher.UIThread.Post(async () =>
            {
                if (_gameInstance != null &&
                    !string.IsNullOrWhiteSpace(_gameInstance.ActiveBotName) &&
                    !_mombot.Enabled)
                {
                    StopActiveExternalBotCore(publishStopMessage: false);
                }

                if (_mombot.Enabled)
                    return;

                await StartInternalMombotAsync(
                    botConfig,
                    requestedBotName,
                    interactiveOfflinePrompt: false,
                    publishMissingGameMessage: false);
            });
            return true;
        };
        gi.NativeBotStopper = _ =>
        {
            Dispatcher.UIThread.Post(async () =>
            {
                await _runtimeStopGate.WaitAsync();
                try
                {
                    await StopInternalMombotCoreAsync(
                        publishStopMessage: false,
                        suppressMissingGameMessage: true,
                        disconnectServerAfterStop: true);
                }
                finally
                {
                    _runtimeStopGate.Release();
                }
            });
            return true;
        };
        gi.NativeBotRebooter = _ =>
        {
            Dispatcher.UIThread.Post(async () =>
            {
                Core.BotConfig rebootBotConfig = LoadConfiguredBotSections()
                    .First(bot => bot.IsNative)
                    .Config;
                Core.GlobalModules.DebugLog(
                    $"[MTC.NativeBotReboot] begin enabled={_mombot.Enabled} connected={(_gameInstance?.IsConnected ?? false)} bot='{rebootBotConfig?.Name ?? string.Empty}'\n");
                Core.GlobalModules.FlushDebugLog();

                try
                {
                    await _runtimeStopGate.WaitAsync();
                    try
                    {
                        if (_mombot.Enabled)
                        {
                            await StopInternalMombotCoreAsync(
                                publishStopMessage: false,
                                suppressMissingGameMessage: true);
                        }
                    }
                    finally
                    {
                        _runtimeStopGate.Release();
                    }

                    Core.GlobalModules.DebugLog(
                        $"[MTC.NativeBotReboot] starting bot='{rebootBotConfig?.Name ?? string.Empty}' connected={(_gameInstance?.IsConnected ?? false)}\n");
                    Core.GlobalModules.FlushDebugLog();
                    await StartInternalMombotAsync(
                        rebootBotConfig,
                        requestedBotName: string.Empty,
                        interactiveOfflinePrompt: false,
                        publishMissingGameMessage: false);

                    if (_mombot.Enabled)
                        PublishMombotLocalMessage("Mombot reboot complete.");

                    Core.GlobalModules.DebugLog(
                        $"[MTC.NativeBotReboot] complete enabled={_mombot.Enabled}\n");
                    Core.GlobalModules.FlushDebugLog();
                }
                catch (Exception ex)
                {
                    Core.GlobalModules.DebugLog(
                        $"[MTC.NativeBotReboot] failed: {ex}\n");
                    Core.GlobalModules.FlushDebugLog();
                    PublishMombotLocalMessage($"Mombot reboot failed: {ex.Message}");
                }
            });
            return true;
        };
        // Two in-process pipes for bidirectional communication.
        // serverToTerm: gi writes game output → MTC reads for the ANSI parser.
        // termToServer: MTC writes keystrokes → gi reads as "local client" input.
        var serverToTerm = new System.IO.Pipelines.Pipe(new System.IO.Pipelines.PipeOptions(
            pauseWriterThreshold: 16 * 1024 * 1024,
            resumeWriterThreshold: 8 * 1024 * 1024,
            minimumSegmentSize: 64 * 1024,
            useSynchronizationContext: false));
        var termToServer = new System.IO.Pipelines.Pipe(new System.IO.Pipelines.PipeOptions(
            pauseWriterThreshold: 1024 * 1024,
            resumeWriterThreshold: 512 * 1024,
            minimumSegmentSize: 4096,
            useSynchronizationContext: false));

        if (gameConfig.Mtc?.ListenForConnections == true)
            await gi.StartAsync();

        // Wire the GameInstance to the pipe streams.
        gi.ConnectDirectClient(
            toTerminal:   serverToTerm.Writer.AsStream(),   // gi writes game output here
            fromTerminal: termToServer.Reader.AsStream());  // gi reads keystrokes from here

        // Replace the keyboard → telnet wiring with keyboard → pipe.
        var termWriter = termToServer.Writer.AsStream();
        SetTerminalInputHandler(bytes =>
        {
            RouteTerminalInput(bytes, data =>
            {
                try { termWriter.Write(data, 0, data.Length); termWriter.Flush(); }
                catch { }
            });
        });

        // Background task: pipe-reader → AnsiParser.
        _proxyCts = new CancellationTokenSource();
        var cts       = _proxyCts;
        var termReader = serverToTerm.Reader.AsStream();
        _ = Task.Run(async () =>
        {
            var buf = new byte[64 * 1024];
            try
            {
                while (!cts.Token.IsCancellationRequested)
                {
                    int n = await termReader.ReadAsync(buf, 0, buf.Length, cts.Token).ConfigureAwait(false);
                    if (n == 0) break;
                    var chunk = buf[..n].ToArray();
                    bool terminalDeaf = IsEmbeddedTerminalClientDeaf();

                    byte[] displayChunk = FilterTerminalDisplayArtifacts(chunk);
                    EnqueueDisplayChunk(displayChunk, force: terminalDeaf);
                    if (!terminalDeaf)
                        QueueSessionLogChunk(chunk);
                }
            }
            catch (OperationCanceledException) { }
            catch { }
        }, cts.Token);

        // Wire ServerDataReceived → trigger engine + ShipInfoParser + AutoRecorder.
        // Mirrors ProxyService.ServerDataReceived: splits on \r (TW2002 line terminator),
        // fires TextLineEvent / TextEvent / ActivateTriggers for each complete line,
        // and uses Pascal prompt semantics for partial prompts.
        var serverLineBuf = new System.Text.StringBuilder();
        var serverAnsiLineBuf = new System.Text.StringBuilder();
        bool serverScriptInAnsi = false;

        gi.ServerDataReceived += (_, e) =>
        {
            string ansiChunk = Core.AnsiCodes.PrepareScriptAnsiText(e.Text);
            string plainChunk = Core.AnsiCodes.StripANSIStateful(ansiChunk, ref serverScriptInAnsi);

                serverLineBuf.Append(plainChunk);
                serverAnsiLineBuf.Append(ansiChunk);

                string buffered = serverLineBuf.ToString();
                string bufferedAnsi = serverAnsiLineBuf.ToString();
                int searchPos = 0;
                int ansiSearchPos = 0;
                int lastProcessedPos = 0;
                int lastAnsiProcessedPos = 0;

                while (searchPos < buffered.Length)
                {
                    int crPos = buffered.IndexOf('\r', searchPos);

                    if (crPos == -1)
                    {
                        // No complete line yet — remainder is a partial line / prompt.
                        string remainder = buffered[lastProcessedPos..];
                        string remainderAnsi = bufferedAnsi[lastAnsiProcessedPos..];
                        serverLineBuf.Clear();
                        serverLineBuf.Append(remainder);
                        serverAnsiLineBuf.Clear();
                        serverAnsiLineBuf.Append(remainderAnsi);

                    if (!string.IsNullOrEmpty(remainder))
                    {
                        string scriptRemainder = remainder;
                        string strippedRemainder = Core.AnsiCodes.NormalizeTerminalText(scriptRemainder);
                        Core.GlobalModules.GlobalAutoRecorder.ProcessPrompt(strippedRemainder, remainderAnsi);
                        ObserveGameAgentServerLine(strippedRemainder, remainderAnsi, isPrompt: true);
                        if (Core.GlobalModules.GlobalAutoRecorder.CurrentSector > 0)
                            Core.ScriptRef.SetCurrentSector(Core.GlobalModules.GlobalAutoRecorder.CurrentSector);
                        bool nativeHaggleResponded = gi.ProcessNativeHaggleLine(strippedRemainder);
                        Core.ScriptRef.SetCurrentAnsiLine(remainderAnsi);
                        Core.ScriptRef.SetCurrentLine(scriptRemainder);
                        // Server prompts and partial lines must keep flowing to the interpreter
                        // even while a proxy menu is open, otherwise waitfor/text triggers stall.
                        // Match Pascal TWX here: partial prompts go through AutoTextEvent and
                        // then TextEvent only. They do not fire TextLineEvent and do not
                        // re-activate triggers until a full CR-terminated line is processed.
                        Core.ScriptRef.SetCurrentAnsiLine(remainderAnsi);
                        Core.ScriptRef.SetCurrentLine(scriptRemainder);
                        interpreter.AutoTextEvent(scriptRemainder, false);
                        Core.ScriptRef.SetCurrentAnsiLine(remainderAnsi);
                        Core.ScriptRef.SetCurrentLine(scriptRemainder);
                        interpreter.TextEvent(scriptRemainder, false);
                        if (!string.IsNullOrWhiteSpace(strippedRemainder))
                        {
                            ObserveComputerShipTypeLine(strippedRemainder);
                            ObserveOnlinePlayersLine(strippedRemainder);
                            SyncMombotPromptStateFromLine(strippedRemainder, remainderAnsi);
                            ObserveEmbeddedKeepaliveWatchLine(strippedRemainder);
                            ObserveNativeMombotWatchLine(strippedRemainder);
                        }
                        if (nativeHaggleResponded)
                        {
                            serverLineBuf.Clear();
                        }
                    }
                        break;
                    }

                    // Complete \r-terminated line.
                    int ansiCrPos = bufferedAnsi.IndexOf('\r', ansiSearchPos);
                    if (ansiCrPos == -1)
                        break;

                string lineRaw = bufferedAnsi[lastAnsiProcessedPos..(ansiCrPos + 1)];
                string lineForScript = NormalizeLegacyInterrogLineForScripts(buffered[lastProcessedPos..crPos]);
                string lineStripped = Core.AnsiCodes.NormalizeTerminalText(lineForScript);

                if (!string.IsNullOrEmpty(lineStripped))
                {
                    gi.FeedShipStatusLine(lineStripped);
                    Core.GlobalModules.GlobalAutoRecorder.RecordLine(lineStripped, lineRaw);
                    ObserveGameAgentServerLine(lineStripped, lineRaw, isPrompt: false);
                    if (Core.GlobalModules.GlobalAutoRecorder.CurrentSector > 0)
                        Core.ScriptRef.SetCurrentSector(Core.GlobalModules.GlobalAutoRecorder.CurrentSector);
                    ObserveComputerShipTypeLine(lineStripped);
                    ObserveOnlinePlayersLine(lineStripped);
                }

                gi.History.ProcessLine(lineStripped);
                gi.ProcessNativeHaggleLine(lineStripped);
                HandlePotentialCommLine(lineRaw);
                Core.ScriptRef.SetCurrentAnsiLine(lineRaw);
                Core.ScriptRef.SetCurrentLine(lineForScript);

                // Real server lines must continue to advance script waits/triggers even if a
                // proxy menu is open locally.
                Core.ScriptRef.SetCurrentAnsiLine(lineRaw);
                Core.ScriptRef.SetCurrentLine(lineForScript);
                interpreter.TextLineEvent(lineForScript, false);
                Core.ScriptRef.SetCurrentAnsiLine(lineRaw);
                Core.ScriptRef.SetCurrentLine(lineForScript);
                interpreter.TextEvent(lineForScript, false);
                interpreter.ActivateTriggers();

                if (!string.IsNullOrWhiteSpace(lineStripped))
                {
                    SyncMombotPromptStateFromLine(lineStripped, lineRaw);
                    ObserveEmbeddedKeepaliveWatchLine(lineStripped);
                    ObserveNativeMombotWatchLine(lineStripped);
                }

                if (_appPrefs.EnableRedAlertMode &&
                    !string.IsNullOrWhiteSpace(lineStripped) &&
                    _mombot.ObserveServerLine(lineStripped))
                {
                    Dispatcher.UIThread.Post(() =>
                    {
                        RefreshMombotUi();
                        RequestStatusBarRefresh();
                        RebuildProxyMenu();
                        _buffer.Dirty = true;
                    });
                }

                    searchPos = crPos + 1;
                    lastProcessedPos = searchPos;
                    ansiSearchPos = ansiCrPos + 1;
                    lastAnsiProcessedPos = ansiSearchPos;
                }

                if (lastProcessedPos >= buffered.Length)
                {
                    serverLineBuf.Clear();
                    string ansiRemainder = lastAnsiProcessedPos < bufferedAnsi.Length
                        ? bufferedAnsi[lastAnsiProcessedPos..]
                        : string.Empty;
                    serverAnsiLineBuf.Clear();
                    if (ansiRemainder.Length > 0)
                        serverAnsiLineBuf.Append(ansiRemainder);
                }
        };

        // Wire Connected / Disconnected events.
        // Note: OnGameConnected() was already called when the proxy started; we only need to
        // update game-connection state (status bar, _state.Connected) here.
        gi.Connected += (_, _) =>
        {
            Dispatcher.UIThread.Post(() =>
            {
                _state.Connected = true;
                ObserveGameAgentConnectionChanged(connected: true);
                SetTerminalConnected(true);
                OnGameConnected();
                _ = TryAutoStartNativeBotAsync("server-connect");
                _parser.Feed($"\x1b[1;32m[Connected to {_state.Host}:{_state.Port}]\x1b[0m\r\n");
                RefreshStatusBar();
                _buffer.Dirty = true;
            });
        };

        gi.Disconnected += (_, _) =>
        {
            bool stopNativeMombot = _mombot.Enabled && ShouldStopNativeMombotAfterDisconnect();
            if (stopNativeMombot)
            {
                SuppressNativeMombotRelogState(
                    preserveDoNotResuscitate: true,
                    preserveShipDestroyed: HasNativeMombotShipDestroyedFlag());
            }

            // Fire 'Connection Lost' so scripts can re-register triggers, etc.
            interpreter.ProgramEvent("Connection Lost", "", false);
            Dispatcher.UIThread.Post(() =>
            {
                _state.Connected = false;
                ObserveGameAgentConnectionChanged(connected: false);
                // In embedded mode the proxy is still alive after a server
                // disconnect, so keep the terminal "connected" unless the
                // GameInstance itself is being torn down.
                bool proxyStillRunning = _gameInstance?.IsRunning == true;
                SetTerminalConnected(proxyStillRunning);
                OnGameDisconnected();
                RefreshStatusBar();
                _buffer.Dirty = true;
            });

            if (_mombot.Enabled)
                Dispatcher.UIThread.Post(() => _ = HandleNativeMombotDisconnectAsync());

            if (stopNativeMombot)
                Dispatcher.UIThread.Post(() => _ = StopNativeMombotAfterDisconnectAsync());
        };

        // Wire getinput / getconsoleinput input buffering — mirrors what ProxyService does.
        // LocalDataReceived fires byte-by-byte; we accumulate into lines and call
        // interpreter.LocalInputEvent(line) when Enter arrives.
        var getInputBuffer = new System.Text.StringBuilder();

        gi.ClearInputBufferRequested += (_, _) => getInputBuffer.Clear();

        gi.LocalDataReceived += (_, e) =>
        {
            // Backspace / DEL
            if (e.Data.Length == 1 && (e.Data[0] == 8 || e.Data[0] == 127))
            {
                if (getInputBuffer.Length > 0)
                    getInputBuffer.Length--;
                return;
            }

            string text = e.Text;
            getInputBuffer.Append(text);

            // Keypress mode: fire immediately on any printable character.
            if (interpreter.HasKeypressInputWaiting && getInputBuffer.Length > 0)
            {
                string key = getInputBuffer.ToString();
                getInputBuffer.Clear();
                interpreter.LocalInputEvent(key);
                return;
            }

            // Not waiting for input and connected — discard the buffer so stale
            // data doesn't trigger a line event next time getinput is active.
            if (gi.IsConnected && !interpreter.IsAnyScriptWaitingForInput())
            {
                getInputBuffer.Clear();
                return;
            }

            // Full-line getinput: deliver when Enter (\r or \n) arrives.
            if (getInputBuffer.ToString().Contains('\r') || getInputBuffer.ToString().Contains('\n'))
            {
                string line = getInputBuffer.ToString().TrimEnd('\r', '\n');
                getInputBuffer.Clear();
                // Blank Enter is a valid response for getinput/getconsoleinput and
                // must be delivered to scripts to preserve TWX27 behavior.
                interpreter.LocalInputEvent(line);
            }
        };

        gi.ScriptStopped += (_, _) =>
        {
            Dispatcher.UIThread.Post(() =>
            {
                RefreshStatusBar();
                RebuildProxyMenu();
            });

            _mombot.HandleObservedScriptStop();
            HandleNativeMombotPostLoginScriptStop();
        };

        gi.ScriptLoaded += (_, _) =>
        {
            Dispatcher.UIThread.Post(() =>
            {
                RefreshStatusBar();
                RebuildProxyMenu();
            });
        };

        gi.ClientTypeChanged += (_, e) =>
        {
            if (e.ClientIndex != EmbeddedLocalClientIndex)
                return;

            Dispatcher.UIThread.Post(() => SyncEmbeddedTerminalClientType(e.ClientType));
        };

        _gameInstance = gi;
        ApplyEmbeddedTerminalOutputMode();
        SyncEmbeddedTerminalClientType(gi.GetClientType(EmbeddedLocalClientIndex));
        ReloadRegisteredBotConfigs();
        SyncMombotRuntimeConfigFromTwxpCfg(gameConfig);
        _mombot.AttachSession(gi, _sessionDb, interpreter, GetOrCreateEmbeddedMombotConfig(gameConfig));
        RefreshStatusBar();
        Core.ScriptRef.SetActiveGameInstance(gi);  // routes getinput through the pipe, not the system console
        OnNativeHaggleChanged(gi.NativeHaggleEnabled, Core.NativeHaggleChangeSource.Config);
        AppPaths.EnsureDirectories();

        // The proxy is now running. Scripts can execute and communicate with the user
        // before any server connection is made. The server connection is triggered by
        // the $c command (typed by the user or called from a script via the connect command).
        SetTerminalConnected(true);
        OnGameDisconnected();   // proxy is live, but the game server is not connected yet
        _parser.Feed($"\x1b[1;32m[Embedded proxy ready — type \x1b[1;33m$c\x1b[1;32m to connect to {_state.Host}:{_state.Port}, or start a script]\x1b[0m\r\n");
        _buffer.Dirty = true;
        await TryAutoStartNativeBotAsync("open-game");
    }

    /// <summary>Stops the embedded <see cref="Core.GameInstance"/> and restores normal state.
    /// Must be awaited (not fire-and-forget) from DoConnectEmbeddedAsync to avoid races.</summary>
    private Task StopEmbeddedAsync()
    {
        lock (_embeddedStopSync)
        {
            if (_pendingEmbeddedStop.IsCompleted)
                _pendingEmbeddedStop = StopEmbeddedSerializedAsync();

            return _pendingEmbeddedStop;
        }
    }

    private async Task StopEmbeddedSerializedAsync()
    {
        await _runtimeStopGate.WaitAsync();
        try
        {
            await StopEmbeddedCoreAsync();
        }
        finally
        {
            _runtimeStopGate.Release();
        }
    }

    private async Task StopEmbeddedCoreAsync()
    {
        TraceRuntimeStop($"[MTC.StopEmbedded] begin game={_embeddedGameName ?? "-"} hasGame={(_gameInstance != null)} nativeMombot={_mombot.Enabled} externalBot={_gameInstance?.ActiveBotName ?? string.Empty}");
        _proxyCts?.Cancel();
        _proxyCts = null;

        var gi = _gameInstance;
        bool hadActiveBot = _mombot.Enabled || !string.IsNullOrWhiteSpace(gi?.ActiveBotName);
        if (hadActiveBot)
        {
            TraceRuntimeStop($"[MTC.StopEmbedded] draining active bots before proxy stop");
            await StopActiveBotCoreAsync(
                publishNativeStopMessage: false,
                publishExternalStopMessage: false,
                suppressMissingGameMessage: true);
        }

        _gameInstance = null;
        if (gi != null)
            gi.NativeHaggleChanged -= OnNativeHaggleChanged;
        if (gi != null)
            gi.NativeHaggleStatsChanged -= OnNativeHaggleStatsChanged;
        if (gi != null)
            gi.ShipStatusUpdated -= OnShipStatusUpdated;
        if (gi != null)
        {
            TraceRuntimeStop($"[MTC.StopEmbedded] awaiting GameInstance.StopAsync");
            await gi.StopAsync();  // no ConfigureAwait(false) — continuation returns to UI thread
        }
        _mombot.DetachSession();
        _terminalLivePaused = false;
        ClearPausedTerminalChunks();
        UpdateTerminalLiveSelector();

        Core.ScriptRef.SetActiveGameInstance(null);
        Core.ScriptRef.OnVariableSaved = null;  // detach savevar persistence for this game
        _embeddedGameConfig = null;
        _embeddedGameName = null;
        ApplyDebugLoggingPreferences();

        try { _sessionDb?.CloseDatabase(); } catch { }
        _sessionDb = null;
        _gameFileLock?.Dispose();
        _gameFileLock = null;
        Core.ScriptRef.SetActiveDatabase(null);

        // Restore default keyboard → telnet wiring (runs on UI thread, no Dispatcher.Post needed).
        SetTerminalInputHandler(bytes => RouteTerminalInput(bytes, SendToTelnet));

        _state.Connected      = false;
        SetTerminalConnected(false);
        OnGameDisconnected();
        _parser.Feed("\x1b[1;31m[Embedded proxy stopped]\x1b[0m\r\n");
        RefreshStatusBar();
        UpdateHaggleToggleState();
        _buffer.Dirty = true;
        TraceRuntimeStop($"[MTC.StopEmbedded] complete game={_embeddedGameName ?? "-"}");
    }

}
