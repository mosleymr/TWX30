using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Core = TWXProxy.Core;

namespace MTC;

public partial class MainWindow
{
    private sealed class MtcTabPrototype
    {
        public int Id { get; init; }
        public bool IsLiveSession { get; init; }
        public string Title { get; set; } = string.Empty;
        public DateTime CreatedUtc { get; init; } = DateTime.UtcNow;
        public Core.TwxRuntimeContext RuntimeContext { get; init; } = null!;
        public GameState State { get; init; } = null!;
        public TerminalBuffer Buffer { get; init; } = null!;
        public AnsiParser Parser { get; init; } = null!;
        public TelnetClient Telnet { get; init; } = null!;
        public Core.ShipInfoParser ShipParser { get; init; } = null!;
        public Core.ModLog SessionLog { get; init; } = null!;
        public Core.ModDatabase? SessionDb { get; set; }
        public Core.GameInstance? GameInstance { get; set; }
        public Core.ExpansionModuleHost? ModuleHost { get; set; }
        public Core.GameFileLock? GameFileLock { get; set; }
        public Action<bool, Core.NativeHaggleChangeSource>? EmbeddedNativeHaggleChangedHandler { get; set; }
        public Action? EmbeddedNativeHaggleStatsChangedHandler { get; set; }
        public Action<Core.ShipStatus>? EmbeddedShipStatusUpdatedHandler { get; set; }
        public ConcurrentQueue<PendingDisplayChunk> PendingDisplayChunks { get; } = new();
        public int PendingDisplayChunkCount;
        public ConcurrentQueue<byte[]> PendingSessionLogChunks { get; } = new();
        public object TerminalDisplayArtifactSync { get; } = new();
        public object TerminalBufferSync { get; } = new();
        public List<byte[]> PausedTerminalChunks { get; } = [];
        public int DisplayDrainScheduled;
        public int SessionLogDrainScheduled;
        public bool TerminalLivePaused { get; set; }
        public bool DeferredInfoPanelsRefresh { get; set; }
        public bool DeferredOnlinePanelRefresh { get; set; }
        public int InfoPanelsRefreshPostScheduled;
        public DispatcherTimer? InfoPanelsRefreshTimer { get; set; }
        public bool InfoPanelsRefreshTimerWired { get; set; }
        public DispatcherTimer? StatusRefreshTimer { get; set; }
        public bool StatusRefreshTimerWired { get; set; }
        public int OnlineAutoRefreshRunning;
        public int ServerInputPendingCharacters;
        public long LastInfoPanelsRefreshTicks;
        public long LastGameTrafficTicks;
        public long LastOnlineRefreshTicks;
        public bool RedAlertEnabled { get; set; }
        public DispatcherTimer? RedAlertTimer { get; set; }
        public bool RedAlertTimerWired { get; set; }
        public DispatcherTimer? CurrentGameConfigSaveTimer { get; set; }
        public bool CurrentGameConfigSaveRunning { get; set; }
        public bool CurrentGameConfigSaveAgain { get; set; }
        public bool CurrentGameConfigSaveTimerWired { get; set; }
        public Core.NativeHaggleEngine StandaloneNativeHaggle { get; init; } = null!;
        public MTC.mombot.mombotService Mombot { get; init; } = null!;
        public GameAgentRuntime GameAgent { get; init; } = null!;
        public Action<byte[]>? TerminalInputHandler { get; set; }
        public List<CommEntry> CommEntries { get; } = [];
        public bool CommWindowVisible { get; set; }
        public Core.CommMessageChannel CommSelectedChannel { get; set; } = Core.CommMessageChannel.FedComm;
        public string CommPrivateTarget { get; set; } = string.Empty;
        public List<byte[]> TemporaryMacroChunks { get; } = [];
        public bool TemporaryMacroRecording { get; set; }
        public bool SuppressTemporaryMacroRecording { get; set; }
        public TerminalSessionRecorder? TerminalRecorder { get; set; }
        public bool MombotPromptOpen { get; set; }
        public bool MombotHotkeyPromptOpen { get; set; }
        public bool MombotScriptPromptOpen { get; set; }
        public bool MombotPreferencesOpen { get; set; }
        public bool MombotPreferencesMenuDeafActive { get; set; }
        public bool MombotPreferencesMenuDeafRestore { get; set; }
        public bool MombotPreferencesCaptureSingleKey { get; set; }
        public string MombotPreferencesInputPrompt { get; set; } = string.Empty;
        public string MombotPreferencesInputBuffer { get; set; } = string.Empty;
        public Action<string>? MombotPreferencesInputHandler { get; set; }
        public MombotPreferencesBlankSubmitBehavior MombotPreferencesBlankSubmitBehavior { get; set; } = MombotPreferencesBlankSubmitBehavior.Ignore;
        public int MombotPreferencesHotkeySlot { get; set; }
        public int MombotPreferencesShipPageStart { get; set; } = 1;
        public int MombotPreferencesPlanetTypePageStart { get; set; } = 1;
        public int MombotPreferencesPlanetListCursor { get; set; } = 2;
        public int MombotPreferencesPlanetListNextCursor { get; set; } = 2;
        public bool MombotPreferencesPlanetListHasMore { get; set; }
        public int MombotPreferencesTraderListCursor { get; set; } = 2;
        public int MombotPreferencesTraderListNextCursor { get; set; } = 2;
        public bool MombotPreferencesTraderListHasMore { get; set; }
        public bool MombotMacroPromptOpen { get; set; }
        public MombotGridContext? MombotMacroContext { get; set; }
        public IReadOnlyList<MombotHotkeyScriptEntry> MombotHotkeyScripts { get; set; } = Array.Empty<MombotHotkeyScriptEntry>();
        public List<string> MombotCommandHistory { get; } = [];
        public string MombotPromptBuffer { get; set; } = string.Empty;
        public string MombotPromptDraft { get; set; } = string.Empty;
        public Func<string, string>? MombotPromptSubmitTransform { get; set; }
        public int MombotPromptHistoryIndex { get; set; }
        public int MombotPromptCursorIndex { get; set; }
        public MombotPreferencesPage MombotPreferencesPage { get; set; }
        public string MombotLastKeepaliveLine { get; set; } = string.Empty;
        public int MombotObservedGamePromptVersion { get; set; }
        public int MombotMacroPromptRedrawTicket { get; set; }
        public string MombotLastObservedGamePromptAnsi { get; set; } = string.Empty;
        public string MombotLastObservedGamePromptPlain { get; set; } = string.Empty;
        public int PendingNativeMombotEscapeEchoSuppressions { get; set; }
        public long NativeMombotEscapeEchoSuppressUntilUtcTicks { get; set; }
        public string PendingNativeMombotPostLoginMacro { get; set; } = string.Empty;
        public bool SuppressingPendingNativeMombotEscapeSequence { get; set; }
        public bool SuppressingPendingNativeMombotEscapeCsiBody { get; set; }
        public bool PendingTerminalSyncMarkerLeadByte { get; set; }
        public bool PendingTerminalSyncMarkerUtf8LeadByte { get; set; }
        public bool MombotKeepaliveTickRunning { get; set; }
        public bool MombotStartupDataGatherPending { get; set; }
        public bool MombotStartupDataGatherRunning { get; set; }
        public bool MombotStartupPostInitPending { get; set; }
        public bool MombotStartupFinalizeRunning { get; set; }
        public bool NativeBotAutoStartInFlight { get; set; }
        public FinderPrewarmKey? LastFinderPrewarmKey { get; set; }
        public int NativeMombotStartupWatchScheduled { get; set; }
        public List<string> OnlinePlayers { get; } = [];
        public List<string> PendingOnlinePlayers { get; } = [];
        public bool CapturingOnlinePlayers { get; set; }
        public bool OnlinePlayersCaptureSawPlayer { get; set; }
        public string CurrentShipType { get; set; } = string.Empty;
        public string CurrentShipClass { get; set; } = string.Empty;
        public string CurrentComputerShipType { get; set; } = string.Empty;
        public bool AwaitingComputerShipTypeLine { get; set; }
        public CancellationTokenSource? ProxyCts { get; set; }
        public Task PendingEmbeddedStop { get; set; } = Task.CompletedTask;
        public object EmbeddedStopSync { get; } = new();
        public SemaphoreSlim RuntimeStopGate { get; } = new(1, 1);
        public EmbeddedGameConfig? EmbeddedGameConfig { get; set; }
        public string? EmbeddedGameName { get; set; }
        public string? CurrentProfilePath { get; set; }
        public MapWindow? MapWindow { get; set; }
        public CacheWindow? CacheWindow { get; set; }
        public AliensWindow? AliensWindow { get; set; }
        public QCannonCalculatorWindow? QCannonCalculatorWindow { get; set; }
        public DataMiningWindow? DataMiningWindow { get; set; }
        public RouteWindow? RouteWindow { get; set; }
        public BubblesWindow? BubblesWindow { get; set; }
        public SectorInfoWindow? SectorInfoWindow { get; set; }
        public GameInfoWindow? GameInfoWindow { get; set; }
        public ScriptDebuggerWindow? ScriptDebuggerWindow { get; set; }
        public MacroSettingsDialog? MacroSettingsDialog { get; set; }
        public MacroPlayDialog? QuickMacroPlayWindow { get; set; }
        public GameAgentWindow? GameAgentWindow { get; set; }
        public GameAgentReplayWindow? GameAgentReplayWindow { get; set; }
        public TerminalRecordingPlaybackWindow? RecordingPlaybackWindow { get; set; }
        public bool NotesPanelVisible { get; set; }
        public bool NotesLoading { get; set; }
        public bool NotesDirty { get; set; }
        public string? NotesGameName { get; set; }
        public string? NotesFilePath { get; set; }
        public string NotesText { get; set; } = string.Empty;
        public string NotesStatus { get; set; } = string.Empty;
        public string LastGameAgentShipStatusSignature { get; set; } = string.Empty;
        public DateTime LastGameAgentShipStatusUtc { get; set; } = DateTime.MinValue;
        public string LastGameAgentServerEventSignature { get; set; } = string.Empty;
        public DateTime LastGameAgentServerEventUtc { get; set; } = DateTime.MinValue;
        public List<Window> AuxiliaryWindows { get; } = [];
    }

    private readonly List<MtcTabPrototype> _mtcTabs = [];
    private readonly Border _tabStripHost = new();
    private readonly StackPanel _tabStripItems = new()
    {
        Orientation = Orientation.Horizontal,
        Spacing = 7,
        VerticalAlignment = VerticalAlignment.Center,
    };

    private Control? _liveTabShell;
    private int _activeMtcTabId;
    private int _nextMtcTabId = 1;
    private readonly object _mtcTabSessionBindLock = new();
    private static readonly AsyncLocal<MtcTabPrototype?> _asyncMtcTabContext = new();
    private MtcTabPrototype? _boundMtcTab;
    private readonly Dictionary<Window, string> _mtcTabOwnedWindowBaseTitles = [];

    private MtcTabPrototype? ActiveMtcTab
        => _mtcTabs.FirstOrDefault(tab => tab.Id == _activeMtcTabId);

    private Core.TwxRuntimeContext? ActiveMtcRuntimeContext
        => ActiveMtcTab?.RuntimeContext;

    private bool IsLiveMtcTabActive()
        => ActiveMtcTab is null || ActiveMtcTab.IsLiveSession;

    private sealed class MtcTabSynchronizationContext : SynchronizationContext
    {
        private readonly MainWindow _window;
        private readonly MtcTabPrototype _tab;
        private readonly SynchronizationContext? _inner;

        public MtcTabSynchronizationContext(MainWindow window, MtcTabPrototype tab, SynchronizationContext? inner)
        {
            _window = window;
            _tab = tab;
            _inner = ReferenceEquals(inner, this) ? null : inner;
        }

        public override SynchronizationContext CreateCopy()
            => new MtcTabSynchronizationContext(_window, _tab, _inner);

        public override void OperationStarted()
            => _inner?.OperationStarted();

        public override void OperationCompleted()
            => _inner?.OperationCompleted();

        public override void Post(SendOrPostCallback d, object? state)
        {
            void Invoke(object? s)
            {
                SynchronizationContext? previousContext = Current;
                var previousTab = _asyncMtcTabContext.Value;
                SetSynchronizationContext(this);
                _asyncMtcTabContext.Value = _tab;
                try
                {
                    _window.RebindMtcTabSessionAfterAwait(_tab);
                    using (Core.GlobalModules.UseRuntimeContext(_tab.RuntimeContext))
                        d(s);
                }
                finally
                {
                    _window.RestoreActiveMtcTabSessionAfterContinuation();
                    _asyncMtcTabContext.Value = previousTab;
                    SetSynchronizationContext(previousContext);
                }
            }

            if (_inner is not null)
            {
                _inner.Post(Invoke, state);
                return;
            }

            Dispatcher.UIThread.Post(() => Invoke(state), DispatcherPriority.Background);
        }

        public override void Send(SendOrPostCallback d, object? state)
        {
            void Invoke(object? s)
            {
                SynchronizationContext? previousContext = Current;
                var previousTab = _asyncMtcTabContext.Value;
                SetSynchronizationContext(this);
                _asyncMtcTabContext.Value = _tab;
                try
                {
                    _window.RebindMtcTabSessionAfterAwait(_tab);
                    using (Core.GlobalModules.UseRuntimeContext(_tab.RuntimeContext))
                        d(s);
                }
                finally
                {
                    _window.RestoreActiveMtcTabSessionAfterContinuation();
                    _asyncMtcTabContext.Value = previousTab;
                    SetSynchronizationContext(previousContext);
                }
            }

            if (_inner is not null)
            {
                _inner.Send(Invoke, state);
                return;
            }

            if (Dispatcher.UIThread.CheckAccess())
            {
                Invoke(state);
                return;
            }

            Dispatcher.UIThread.InvokeAsync(() => Invoke(state)).GetAwaiter().GetResult();
        }
    }

    private void InitializeTabbedShell()
    {
        EnsureInitialMtcTab();

        _tabStripHost.Background = HudStatus;
        _tabStripHost.BorderBrush = HudInnerEdge;
        _tabStripHost.BorderThickness = new Thickness(0, 0, 0, 1);
        _tabStripHost.Padding = UiThickness(10, 6, 10, 0);
        _tabStripHost.Child = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = _tabStripItems,
        };

        RefreshMtcTabStrip();
    }

    private MtcTabPrototype EnsureInitialMtcTab()
    {
        if (_mtcTabs.Count > 0)
            return _mtcTabs[0];

        var tab = CreateMtcTabSession(GetLiveMtcTabTitle(null), isLiveSession: true);

        _mtcTabs.Add(tab);
        _activeMtcTabId = tab.Id;
        BindMtcTabSession(tab);
        return tab;
    }

    private MtcTabPrototype CreateMtcTabSession(string title, bool isLiveSession)
    {
        int id = _nextMtcTabId++;
        var buffer = new TerminalBuffer(80, 24)
        {
            ScrollbackLines = AppPreferences.NormalizeScrollbackLines(_appPrefs.ScrollbackLines),
        };
        var parser = new AnsiParser(buffer);
        var runtimeContext = new Core.TwxRuntimeContext($"mtc-tab-{id}");
        var tab = new MtcTabPrototype
        {
            Id = id,
            IsLiveSession = isLiveSession,
            Title = string.IsNullOrWhiteSpace(title) ? $"Game {id}" : title.Trim(),
            RuntimeContext = runtimeContext,
            State = new GameState(),
            Buffer = buffer,
            Parser = parser,
            Telnet = new TelnetClient(buffer, parser),
            ShipParser = new Core.ShipInfoParser(),
            SessionLog = new Core.ModLog(),
            StandaloneNativeHaggle = new Core.NativeHaggleEngine(),
            Mombot = new MTC.mombot.mombotService(runtimeContext),
            GameAgent = new GameAgentRuntime(),
            NotesPanelVisible = _appPrefs.ShowNotesPanel,
        };

        tab.GameAgent.EventRecorded += evt => _jsonRpcServer?.PublishGameAgentEvent(evt);

        parser.RawBytesObserved = (bytes, offset, length) =>
        {
            tab.TerminalRecorder?.RecordOutput(bytes, offset, length);
        };
        tab.TerminalInputHandler = bytes =>
            ExecuteInMtcTabSession(tab, () => RouteTerminalInput(bytes, SendToTelnet));

        ConfigureMtcTabSessionEvents(tab);
        tab.StandaloneNativeHaggle.SetEnabled(true);
        tab.StandaloneNativeHaggle.SetPortHaggleMode(ResolveGlobalPortHaggleMode());
        tab.StandaloneNativeHaggle.SetPlanetHaggleMode(ResolveGlobalPlanetHaggleMode());
        return tab;
    }

    private void ConfigureMtcTabSessionEvents(MtcTabPrototype tab)
    {
        tab.State.Changed += () =>
        {
            if (tab.Id != _activeMtcTabId)
                return;

            Dispatcher.UIThread.Post(() =>
            {
                if (tab.Id != _activeMtcTabId)
                    return;

                ExecuteInMtcTabSession(tab, () => RequestInfoPanelsRefresh());
            }, DispatcherPriority.Background);
        };

        tab.StandaloneNativeHaggle.EnabledChanged += _ =>
            Dispatcher.UIThread.Post(() =>
            {
                if (tab.Id != _activeMtcTabId)
                    return;

                ExecuteInMtcTabSession(tab, () =>
                {
                    UpdateHaggleToggleState();
                    RequestStatusBarRefresh();
                });
            }, DispatcherPriority.Background);

        tab.StandaloneNativeHaggle.StatsChanged += () =>
            Dispatcher.UIThread.Post(() =>
            {
                if (tab.Id != _activeMtcTabId)
                    return;

                ExecuteInMtcTabSession(tab, RequestStatusBarRefresh);
            }, DispatcherPriority.Background);

        tab.Telnet.Connected += () =>
            Dispatcher.UIThread.Post(() => ExecuteInMtcTabSession(tab, OnTelnetConnected), DispatcherPriority.Background);

        tab.Telnet.Disconnected += () =>
            Dispatcher.UIThread.Post(() => ExecuteInMtcTabSession(tab, OnTelnetDisconnected), DispatcherPriority.Background);

        tab.Telnet.Error += message =>
            Dispatcher.UIThread.Post(() => ExecuteInMtcTabSession(tab, () => OnTelnetError(message)), DispatcherPriority.Background);

        tab.Telnet.TextLineReceived += tab.ShipParser.FeedLine;
        tab.ShipParser.Updated += status =>
        {
            if (tab.Id != _activeMtcTabId)
            {
                ExecuteInMtcTabBackgroundContext(tab, () => ApplyShipStatusToTabState(tab, status, notifyChanged: false, observeAgent: false));
                return;
            }

            Dispatcher.UIThread.Post(() => ExecuteInMtcTabSession(tab, () => OnShipStatusUpdated(status)), DispatcherPriority.Background);
        };

        tab.Telnet.TextLineAnsiReceived += (ansiLine, strippedLine) =>
        {
            if (tab.Id != _activeMtcTabId)
            {
                ExecuteInMtcTabBackgroundContext(tab, () =>
                {
                    Core.GlobalModules.GlobalAutoRecorder.RecordLine(strippedLine, ansiLine);
                    ProcessStandaloneNativeHaggleLine(tab, strippedLine);
                });
                return;
            }

            ExecuteInMtcTabSession(tab, () =>
            {
                Core.GlobalModules.GlobalAutoRecorder.RecordLine(strippedLine, ansiLine);
                ObserveGameAgentServerLine(strippedLine, ansiLine, isPrompt: LooksLikeAgentPrompt(strippedLine));
                ObserveOnlinePlayersLine(strippedLine);
                HandlePotentialCommLine(ansiLine);
                ProcessStandaloneNativeHaggleLine(strippedLine);
            });
        };

        tab.Telnet.AppDataDecoded += text =>
        {
            if (tab.Id != _activeMtcTabId)
            {
                ExecuteInMtcTabBackgroundContext(tab, () =>
                {
                    Volatile.Write(ref tab.LastGameTrafficTicks, Stopwatch.GetTimestamp());
                    tab.SessionLog.RecordServerText(text);
                });
                return;
            }

            ExecuteInMtcTabSession(tab, () =>
            {
                MarkGameTrafficActivity();
                _sessionLog.RecordServerText(text);
            });
        };

        var recorder = tab.RuntimeContext.AutoRecorder;
        recorder.CurrentSectorChanged += sn =>
        {
            if (tab.Id != _activeMtcTabId)
            {
                ExecuteInMtcTabBackgroundContext(tab, () => ApplyCurrentSectorToTabState(tab, sn, notifyChanged: false, observeAgent: false));
                return;
            }

            Dispatcher.UIThread.Post(() => ExecuteInMtcTabSession(tab, () =>
            {
                ApplyCurrentSectorToTabState(tab, sn, notifyChanged: true, observeAgent: true);
            }), DispatcherPriority.Background);
        };

        recorder.LandmarkSectorsChanged += () =>
        {
            if (tab.Id != _activeMtcTabId)
                return;

            Dispatcher.UIThread.Post(() => ExecuteInMtcTabSession(tab, () =>
            {
                if (tab.Id != _activeMtcTabId)
                    return;

                SyncMombotSpecialSectorVarsFromDatabase(persist: true);
                RefreshStatusBar();
                _buffer.Dirty = true;
            }), DispatcherPriority.Background);
        };

        recorder.GenesisTorpsChanged += delta =>
        {
            if (tab.Id != _activeMtcTabId)
                return;

            Dispatcher.UIThread.Post(() =>
            {
                if (tab.Id != _activeMtcTabId)
                    return;

                ExecuteInMtcTabSession(tab, () => OnGenesisTorpsChanged(delta));
            }, DispatcherPriority.Background);
        };

        recorder.AtomicDetChanged += delta =>
        {
            if (tab.Id != _activeMtcTabId)
                return;

            Dispatcher.UIThread.Post(() =>
            {
                if (tab.Id != _activeMtcTabId)
                    return;

                ExecuteInMtcTabSession(tab, () => OnAtomicDetChanged(delta));
            }, DispatcherPriority.Background);
        };

        recorder.ShipStatusDeltaDetected += delta =>
        {
            if (tab.Id != _activeMtcTabId)
            {
                ExecuteInMtcTabBackgroundContext(tab, () => ApplyShipStatusDeltaToTabParser(tab, delta));
                return;
            }

            ExecuteInMtcTabSession(tab, () =>
            {
                ApplyShipStatusDeltaToTabParser(tab, delta);
            });
        };
    }

    private void ApplyCurrentSectorToTabState(MtcTabPrototype tab, int sector, bool notifyChanged, bool observeAgent)
    {
        Core.ScriptRef.SetCurrentSector(tab.RuntimeContext, sector);
        SetMombotCurrentVars(tab.RuntimeContext, sector.ToString(), "$PLAYER~CURRENT_SECTOR", "$player~current_sector");

        ApplyShipStatusDeltaToTabParser(tab, new Core.ShipStatusDelta
        {
            CurrentSector = sector
        });

        if (tab.State.Sector == sector)
            return;

        tab.State.Sector = sector;
        if (observeAgent)
            ObserveGameAgentCurrentSectorChanged(sector);
        if (notifyChanged)
            tab.State.NotifyChanged();
    }

    private static void ProcessStandaloneNativeHaggleLine(MtcTabPrototype tab, string strippedLine)
    {
        if (tab.State.EmbeddedProxy ||
            tab.GameInstance is not null ||
            !tab.Telnet.IsConnected ||
            string.IsNullOrWhiteSpace(strippedLine))
            return;

        string? response = tab.StandaloneNativeHaggle.HandleLine(strippedLine);
        if (string.IsNullOrEmpty(response))
            return;

        tab.Telnet.SendRaw(System.Text.Encoding.ASCII.GetBytes(response + "\r"));
        Core.GlobalModules.DebugLog($"[MTC.NativeHaggle] standalone SEND '{response}\\r'\n");
    }

    private static void ApplyShipStatusDeltaToTabParser(MtcTabPrototype tab, Core.ShipStatusDelta delta)
    {
        if (tab.GameInstance != null)
        {
            tab.GameInstance.ApplyShipStatusDelta(delta);
            return;
        }

        tab.ShipParser.ApplyDelta(delta);
    }

    private void CaptureMtcTabSession(MtcTabPrototype tab)
    {
        tab.SessionDb = _sessionDb;
        tab.GameInstance = _gameInstance;
        tab.ModuleHost = _moduleHost;
        tab.GameFileLock = _gameFileLock;
        tab.TerminalLivePaused = _terminalLivePaused;
        tab.DeferredInfoPanelsRefresh = _deferredInfoPanelsRefresh;
        tab.DeferredOnlinePanelRefresh = _deferredOnlinePanelRefresh;
        tab.InfoPanelsRefreshPostScheduled = _infoPanelsRefreshPostScheduled;
        tab.InfoPanelsRefreshTimer = _infoPanelsRefreshTimer;
        tab.OnlineAutoRefreshRunning = _onlineAutoRefreshRunning;
        tab.ServerInputPendingCharacters = _serverInputPendingCharacters;
        tab.LastInfoPanelsRefreshTicks = _lastInfoPanelsRefreshTicks;
        tab.LastGameTrafficTicks = _lastGameTrafficTicks;
        tab.LastOnlineRefreshTicks = _lastOnlineRefreshTicks;
        tab.RedAlertEnabled = _redAlertEnabled;
        tab.CurrentGameConfigSaveTimer = _currentGameConfigSaveTimer;
        tab.CurrentGameConfigSaveRunning = _currentGameConfigSaveRunning;
        tab.CurrentGameConfigSaveAgain = _currentGameConfigSaveAgain;
        tab.TerminalInputHandler = _terminalInputHandler ?? tab.TerminalInputHandler;
        tab.ProxyCts = _proxyCts;
        tab.PendingEmbeddedStop = _pendingEmbeddedStop;
        tab.EmbeddedGameConfig = _embeddedGameConfig;
        tab.EmbeddedGameName = _embeddedGameName;
        tab.CurrentProfilePath = _currentProfilePath;
        tab.CommWindowVisible = _commWindowVisible;
        tab.CommSelectedChannel = _commSelectedChannel;
        tab.CommPrivateTarget = _commPrivateTarget;
        tab.TemporaryMacroRecording = _temporaryMacroRecording;
        tab.SuppressTemporaryMacroRecording = _suppressTemporaryMacroRecording;
        tab.TerminalRecorder = _terminalRecorder;
        tab.MombotPromptOpen = _mombotPromptOpen;
        tab.MombotHotkeyPromptOpen = _mombotHotkeyPromptOpen;
        tab.MombotScriptPromptOpen = _mombotScriptPromptOpen;
        tab.MombotPreferencesOpen = _mombotPreferencesOpen;
        tab.MombotPreferencesMenuDeafActive = _mombotPreferencesMenuDeafActive;
        tab.MombotPreferencesMenuDeafRestore = _mombotPreferencesMenuDeafRestore;
        tab.MombotPreferencesCaptureSingleKey = _mombotPreferencesCaptureSingleKey;
        tab.MombotPreferencesInputPrompt = _mombotPreferencesInputPrompt;
        tab.MombotPreferencesInputBuffer = _mombotPreferencesInputBuffer;
        tab.MombotPreferencesInputHandler = _mombotPreferencesInputHandler;
        tab.MombotPreferencesBlankSubmitBehavior = _mombotPreferencesBlankSubmitBehavior;
        tab.MombotPreferencesHotkeySlot = _mombotPreferencesHotkeySlot;
        tab.MombotPreferencesShipPageStart = _mombotPreferencesShipPageStart;
        tab.MombotPreferencesPlanetTypePageStart = _mombotPreferencesPlanetTypePageStart;
        tab.MombotPreferencesPlanetListCursor = _mombotPreferencesPlanetListCursor;
        tab.MombotPreferencesPlanetListNextCursor = _mombotPreferencesPlanetListNextCursor;
        tab.MombotPreferencesPlanetListHasMore = _mombotPreferencesPlanetListHasMore;
        tab.MombotPreferencesTraderListCursor = _mombotPreferencesTraderListCursor;
        tab.MombotPreferencesTraderListNextCursor = _mombotPreferencesTraderListNextCursor;
        tab.MombotPreferencesTraderListHasMore = _mombotPreferencesTraderListHasMore;
        tab.MombotMacroPromptOpen = _mombotMacroPromptOpen;
        tab.MombotMacroContext = _mombotMacroContext;
        tab.MombotHotkeyScripts = _mombotHotkeyScripts;
        if (!ReferenceEquals(tab.MombotCommandHistory, _mombotCommandHistory))
        {
            tab.MombotCommandHistory.Clear();
            tab.MombotCommandHistory.AddRange(_mombotCommandHistory);
        }
        tab.MombotPromptBuffer = _mombotPromptBuffer;
        tab.MombotPromptDraft = _mombotPromptDraft;
        tab.MombotPromptSubmitTransform = _mombotPromptSubmitTransform;
        tab.MombotPromptHistoryIndex = _mombotPromptHistoryIndex;
        tab.MombotPromptCursorIndex = _mombotPromptCursorIndex;
        tab.MombotPreferencesPage = _mombotPreferencesPage;
        tab.MombotLastKeepaliveLine = _mombotLastKeepaliveLine;
        tab.MombotObservedGamePromptVersion = _mombotObservedGamePromptVersion;
        tab.MombotMacroPromptRedrawTicket = _mombotMacroPromptRedrawTicket;
        tab.MombotLastObservedGamePromptAnsi = _mombotLastObservedGamePromptAnsi;
        tab.MombotLastObservedGamePromptPlain = _mombotLastObservedGamePromptPlain;
        tab.PendingNativeMombotEscapeEchoSuppressions = _pendingNativeMombotEscapeEchoSuppressions;
        tab.NativeMombotEscapeEchoSuppressUntilUtcTicks = _nativeMombotEscapeEchoSuppressUntilUtcTicks;
        tab.PendingNativeMombotPostLoginMacro = _pendingNativeMombotPostLoginMacro;
        tab.SuppressingPendingNativeMombotEscapeSequence = _suppressingPendingNativeMombotEscapeSequence;
        tab.SuppressingPendingNativeMombotEscapeCsiBody = _suppressingPendingNativeMombotEscapeCsiBody;
        tab.PendingTerminalSyncMarkerLeadByte = _pendingTerminalSyncMarkerLeadByte;
        tab.PendingTerminalSyncMarkerUtf8LeadByte = _pendingTerminalSyncMarkerUtf8LeadByte;
        tab.MombotKeepaliveTickRunning = _mombotKeepaliveTickRunning;
        tab.MombotStartupDataGatherPending = _mombotStartupDataGatherPending;
        tab.MombotStartupDataGatherRunning = _mombotStartupDataGatherRunning;
        tab.MombotStartupPostInitPending = _mombotStartupPostInitPending;
        tab.MombotStartupFinalizeRunning = _mombotStartupFinalizeRunning;
        tab.NativeBotAutoStartInFlight = _nativeBotAutoStartInFlight;
        tab.LastFinderPrewarmKey = _lastFinderPrewarmKey;
        tab.NativeMombotStartupWatchScheduled = _nativeMombotStartupWatchScheduled;
        tab.CapturingOnlinePlayers = _capturingOnlinePlayers;
        tab.OnlinePlayersCaptureSawPlayer = _onlinePlayersCaptureSawPlayer;
        tab.CurrentShipType = _currentShipType;
        tab.CurrentShipClass = _currentShipClass;
        tab.CurrentComputerShipType = _currentComputerShipType;
        tab.AwaitingComputerShipTypeLine = _awaitingComputerShipTypeLine;
        CaptureNotesState(tab);
        tab.LastGameAgentShipStatusSignature = _lastGameAgentShipStatusSignature;
        tab.LastGameAgentShipStatusUtc = _lastGameAgentShipStatusUtc;
        tab.LastGameAgentServerEventSignature = _lastGameAgentServerEventSignature;
        tab.LastGameAgentServerEventUtc = _lastGameAgentServerEventUtc;
    }

    private void BindMtcTabSession(MtcTabPrototype tab)
    {
        _state = tab.State;
        _buffer = tab.Buffer;
        _parser = tab.Parser;
        _telnet = tab.Telnet;
        _shipParser = tab.ShipParser;
        _sessionLog = tab.SessionLog;
        _sessionDb = tab.SessionDb;
        _gameInstance = tab.GameInstance;
        _moduleHost = tab.ModuleHost;
        _gameFileLock = tab.GameFileLock;
        _terminalLivePaused = tab.TerminalLivePaused;
        _deferredInfoPanelsRefresh = tab.DeferredInfoPanelsRefresh;
        _deferredOnlinePanelRefresh = tab.DeferredOnlinePanelRefresh;
        _infoPanelsRefreshPostScheduled = tab.InfoPanelsRefreshPostScheduled;
        _infoPanelsRefreshTimer = tab.InfoPanelsRefreshTimer;
        _onlineAutoRefreshRunning = tab.OnlineAutoRefreshRunning;
        _serverInputPendingCharacters = tab.ServerInputPendingCharacters;
        _lastInfoPanelsRefreshTicks = tab.LastInfoPanelsRefreshTicks;
        _lastGameTrafficTicks = tab.LastGameTrafficTicks;
        _lastOnlineRefreshTicks = tab.LastOnlineRefreshTicks;
        _redAlertEnabled = tab.RedAlertEnabled;
        _currentGameConfigSaveTimer = tab.CurrentGameConfigSaveTimer;
        _currentGameConfigSaveRunning = tab.CurrentGameConfigSaveRunning;
        _currentGameConfigSaveAgain = tab.CurrentGameConfigSaveAgain;
        _standaloneNativeHaggle = tab.StandaloneNativeHaggle;
        _mombot = tab.Mombot;
        _gameAgent = tab.GameAgent;
        _terminalInputHandler = tab.TerminalInputHandler;
        _proxyCts = tab.ProxyCts;
        _pendingEmbeddedStop = tab.PendingEmbeddedStop;
        _embeddedStopSync = tab.EmbeddedStopSync;
        _runtimeStopGate = tab.RuntimeStopGate;
        _embeddedGameConfig = tab.EmbeddedGameConfig;
        _embeddedGameName = tab.EmbeddedGameName;
        _currentProfilePath = tab.CurrentProfilePath;
        _commEntries = tab.CommEntries;
        _commWindowVisible = tab.CommWindowVisible;
        _commSelectedChannel = tab.CommSelectedChannel;
        _commPrivateTarget = tab.CommPrivateTarget;
        _temporaryMacroChunks = tab.TemporaryMacroChunks;
        _temporaryMacroRecording = tab.TemporaryMacroRecording;
        _suppressTemporaryMacroRecording = tab.SuppressTemporaryMacroRecording;
        _terminalRecorder = tab.TerminalRecorder;
        _mombotPromptOpen = tab.MombotPromptOpen;
        _mombotHotkeyPromptOpen = tab.MombotHotkeyPromptOpen;
        _mombotScriptPromptOpen = tab.MombotScriptPromptOpen;
        _mombotPreferencesOpen = tab.MombotPreferencesOpen;
        _mombotPreferencesMenuDeafActive = tab.MombotPreferencesMenuDeafActive;
        _mombotPreferencesMenuDeafRestore = tab.MombotPreferencesMenuDeafRestore;
        _mombotPreferencesCaptureSingleKey = tab.MombotPreferencesCaptureSingleKey;
        _mombotPreferencesInputPrompt = tab.MombotPreferencesInputPrompt;
        _mombotPreferencesInputBuffer = tab.MombotPreferencesInputBuffer;
        _mombotPreferencesInputHandler = tab.MombotPreferencesInputHandler;
        _mombotPreferencesBlankSubmitBehavior = tab.MombotPreferencesBlankSubmitBehavior;
        _mombotPreferencesHotkeySlot = tab.MombotPreferencesHotkeySlot;
        _mombotPreferencesShipPageStart = tab.MombotPreferencesShipPageStart;
        _mombotPreferencesPlanetTypePageStart = tab.MombotPreferencesPlanetTypePageStart;
        _mombotPreferencesPlanetListCursor = tab.MombotPreferencesPlanetListCursor;
        _mombotPreferencesPlanetListNextCursor = tab.MombotPreferencesPlanetListNextCursor;
        _mombotPreferencesPlanetListHasMore = tab.MombotPreferencesPlanetListHasMore;
        _mombotPreferencesTraderListCursor = tab.MombotPreferencesTraderListCursor;
        _mombotPreferencesTraderListNextCursor = tab.MombotPreferencesTraderListNextCursor;
        _mombotPreferencesTraderListHasMore = tab.MombotPreferencesTraderListHasMore;
        _mombotMacroPromptOpen = tab.MombotMacroPromptOpen;
        _mombotMacroContext = tab.MombotMacroContext;
        _mombotHotkeyScripts = tab.MombotHotkeyScripts;
        _mombotCommandHistory = tab.MombotCommandHistory;
        _mombotPromptBuffer = tab.MombotPromptBuffer;
        _mombotPromptDraft = tab.MombotPromptDraft;
        _mombotPromptSubmitTransform = tab.MombotPromptSubmitTransform;
        _mombotPromptHistoryIndex = tab.MombotPromptHistoryIndex;
        _mombotPromptCursorIndex = tab.MombotPromptCursorIndex;
        _mombotPreferencesPage = tab.MombotPreferencesPage;
        _mombotLastKeepaliveLine = tab.MombotLastKeepaliveLine;
        _mombotObservedGamePromptVersion = tab.MombotObservedGamePromptVersion;
        _mombotMacroPromptRedrawTicket = tab.MombotMacroPromptRedrawTicket;
        _mombotLastObservedGamePromptAnsi = tab.MombotLastObservedGamePromptAnsi;
        _mombotLastObservedGamePromptPlain = tab.MombotLastObservedGamePromptPlain;
        _pendingNativeMombotEscapeEchoSuppressions = tab.PendingNativeMombotEscapeEchoSuppressions;
        _nativeMombotEscapeEchoSuppressUntilUtcTicks = tab.NativeMombotEscapeEchoSuppressUntilUtcTicks;
        _pendingNativeMombotPostLoginMacro = tab.PendingNativeMombotPostLoginMacro;
        _suppressingPendingNativeMombotEscapeSequence = tab.SuppressingPendingNativeMombotEscapeSequence;
        _suppressingPendingNativeMombotEscapeCsiBody = tab.SuppressingPendingNativeMombotEscapeCsiBody;
        _pendingTerminalSyncMarkerLeadByte = tab.PendingTerminalSyncMarkerLeadByte;
        _pendingTerminalSyncMarkerUtf8LeadByte = tab.PendingTerminalSyncMarkerUtf8LeadByte;
        _mombotKeepaliveTickRunning = tab.MombotKeepaliveTickRunning;
        _mombotStartupDataGatherPending = tab.MombotStartupDataGatherPending;
        _mombotStartupDataGatherRunning = tab.MombotStartupDataGatherRunning;
        _mombotStartupPostInitPending = tab.MombotStartupPostInitPending;
        _mombotStartupFinalizeRunning = tab.MombotStartupFinalizeRunning;
        _nativeBotAutoStartInFlight = tab.NativeBotAutoStartInFlight;
        _lastFinderPrewarmKey = tab.LastFinderPrewarmKey;
        _nativeMombotStartupWatchScheduled = tab.NativeMombotStartupWatchScheduled;
        _onlinePlayers = tab.OnlinePlayers;
        _pendingOnlinePlayers = tab.PendingOnlinePlayers;
        _capturingOnlinePlayers = tab.CapturingOnlinePlayers;
        _onlinePlayersCaptureSawPlayer = tab.OnlinePlayersCaptureSawPlayer;
        _currentShipType = tab.CurrentShipType;
        _currentShipClass = tab.CurrentShipClass;
        _currentComputerShipType = tab.CurrentComputerShipType;
        _awaitingComputerShipTypeLine = tab.AwaitingComputerShipTypeLine;
        _lastGameAgentShipStatusSignature = tab.LastGameAgentShipStatusSignature;
        _lastGameAgentShipStatusUtc = tab.LastGameAgentShipStatusUtc;
        _lastGameAgentServerEventSignature = tab.LastGameAgentServerEventSignature;
        _lastGameAgentServerEventUtc = tab.LastGameAgentServerEventUtc;
        _boundMtcTab = tab;

        BindNotesState(tab);
        ApplyMtcTabTerminalInputHandler(tab);
        ApplyMtcTabTerminalSurface(tab);
    }

    private MtcTabPrototype? CurrentMtcTabContext()
    {
        if (_asyncMtcTabContext.Value is { } asyncTab)
        {
            EnsureMtcTabSessionBound(asyncTab);
            return asyncTab;
        }

        if (BoundRuntimeMtcTabContext() is { } boundRuntimeTab)
        {
            EnsureMtcTabSessionBound(boundRuntimeTab);
            return boundRuntimeTab;
        }

        if (Dispatcher.UIThread.CheckAccess() && ActiveMtcTab is { } activeTab)
        {
            EnsureMtcTabSessionBound(activeTab);
            return activeTab;
        }

        if (FindMtcTabForRuntimeContext(Core.GlobalModules.CurrentContext) is { } runtimeTab)
        {
            EnsureMtcTabSessionBound(runtimeTab);
            return runtimeTab;
        }

        return null;
    }

    private MtcTabPrototype? ResolveCurrentMtcTabContext()
    {
        if (_asyncMtcTabContext.Value is { } asyncTab)
        {
            EnsureMtcTabSessionBound(asyncTab);
            return asyncTab;
        }

        if (BoundRuntimeMtcTabContext() is { } boundRuntimeTab)
        {
            EnsureMtcTabSessionBound(boundRuntimeTab);
            return boundRuntimeTab;
        }

        if (Dispatcher.UIThread.CheckAccess())
        {
            var activeTab = ActiveMtcTab;
            if (activeTab is not null)
                EnsureMtcTabSessionBound(activeTab);
            return activeTab;
        }

        if (FindMtcTabForRuntimeContext(Core.GlobalModules.CurrentContext) is { } runtimeTab)
        {
            EnsureMtcTabSessionBound(runtimeTab);
            return runtimeTab;
        }

        return null;
    }

    private MtcTabPrototype? BoundRuntimeMtcTabContext()
    {
        if (!Dispatcher.UIThread.CheckAccess())
            return null;

        return _boundMtcTab is not null &&
               ReferenceEquals(Core.GlobalModules.CurrentContext, _boundMtcTab.RuntimeContext)
            ? _boundMtcTab
            : null;
    }

    private MtcTabPrototype? FindMtcTabForRuntimeContext(Core.TwxRuntimeContext? runtimeContext)
    {
        if (runtimeContext is null)
            return null;

        lock (_mtcTabSessionBindLock)
            return _mtcTabs.FirstOrDefault(tab => ReferenceEquals(tab.RuntimeContext, runtimeContext));
    }

    private void EnsureMtcTabSessionBound(MtcTabPrototype tab)
    {
        if (!Dispatcher.UIThread.CheckAccess())
            return;

        lock (_mtcTabSessionBindLock)
        {
            if (ReferenceEquals(_boundMtcTab, tab))
                return;

            if (_boundMtcTab is not null)
                CaptureMtcTabSession(_boundMtcTab);

            BindMtcTabSession(tab);
        }
    }

    private void RebindMtcTabSessionAfterAwait(MtcTabPrototype? tab)
    {
        if (tab is not null && Dispatcher.UIThread.CheckAccess())
            EnsureMtcTabSessionBound(tab);
    }

    private void RestoreActiveMtcTabSessionAfterContinuation()
    {
        if (!Dispatcher.UIThread.CheckAccess())
            return;

        var active = ActiveMtcTab;
        if (active is null)
            return;

        lock (_mtcTabSessionBindLock)
        {
            if (_boundMtcTab is not null)
                CaptureMtcTabSession(_boundMtcTab);

            if (!ReferenceEquals(active, _boundMtcTab))
                BindMtcTabSession(active);
        }
    }

    private bool PrepareMtcTabVisualRefresh()
    {
        if (!Dispatcher.UIThread.CheckAccess())
            return false;

        var asyncContext = _asyncMtcTabContext.Value;
        if (asyncContext is not null)
        {
            if (asyncContext.Id != _activeMtcTabId)
                return false;

            EnsureMtcTabSessionBound(asyncContext);
            return IsMtcTabSessionBoundForVisualRefresh(asyncContext);
        }

        if (BoundRuntimeMtcTabContext() is { } boundRuntimeTab)
        {
            if (boundRuntimeTab.Id != _activeMtcTabId)
                return false;

            EnsureMtcTabSessionBound(boundRuntimeTab);
            return IsMtcTabSessionBoundForVisualRefresh(boundRuntimeTab);
        }

        var active = ActiveMtcTab;
        if (active is not null)
        {
            EnsureMtcTabSessionBound(active);
            return IsMtcTabSessionBoundForVisualRefresh(active);
        }

        if (FindMtcTabForRuntimeContext(Core.GlobalModules.CurrentContext) is { } runtimeTab)
        {
            EnsureMtcTabSessionBound(runtimeTab);
            return runtimeTab.Id == _activeMtcTabId &&
                   IsMtcTabSessionBoundForVisualRefresh(runtimeTab);
        }

        return true;
    }

    private bool IsMtcTabSessionBoundForVisualRefresh(MtcTabPrototype tab)
        => ReferenceEquals(_boundMtcTab, tab) &&
           ReferenceEquals(_state, tab.State) &&
           ReferenceEquals(_buffer, tab.Buffer);

    private void ApplyMtcTabTerminalInputHandler(MtcTabPrototype tab)
    {
        if (tab.Id != _activeMtcTabId || tab.TerminalInputHandler == null)
            return;

        var handler = tab.TerminalInputHandler;
        if (Dispatcher.UIThread.CheckAccess())
        {
            ApplyTerminalInputHandlerToControls(handler);
            return;
        }

        Dispatcher.UIThread.Post(() =>
        {
            if (tab.Id == _activeMtcTabId)
                ApplyTerminalInputHandlerToControls(handler);
        }, DispatcherPriority.Background);
    }

    private void ApplyMtcTabTerminalSurface(MtcTabPrototype tab)
    {
        if (tab.Id != _activeMtcTabId)
            return;

        void Apply()
        {
            if (tab.Id != _activeMtcTabId)
                return;

            _termCtrl?.SetBuffer(tab.Buffer);
            _deckTermCtrl?.SetBuffer(tab.Buffer);
            SetTerminalConnected(_state.Connected || _telnet.IsConnected || (_gameInstance?.IsRunning == true));
            UpdateTerminalLiveSelector();
            UpdateClassicTerminalSizeStatus();
        }

        if (Dispatcher.UIThread.CheckAccess())
        {
            Apply();
            return;
        }

        Dispatcher.UIThread.Post(Apply, DispatcherPriority.Background);
    }

    private void ExecuteInMtcTabSession(MtcTabPrototype tab, Action action)
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(() => ExecuteInMtcTabSession(tab, action), DispatcherPriority.Background);
            return;
        }

        var previousAsyncTab = _asyncMtcTabContext.Value;
        _asyncMtcTabContext.Value = tab;

        try
        {
            lock (_mtcTabSessionBindLock)
            {
                var previous = _boundMtcTab;
                if (previous is not null)
                    CaptureMtcTabSession(previous);

                BindMtcTabSession(tab);
                try
                {
                    using (Core.GlobalModules.UseRuntimeContext(tab.RuntimeContext))
                    {
                        action();
                    }
                }
                finally
                {
                    if (_boundMtcTab is not null)
                        CaptureMtcTabSession(_boundMtcTab);

                    var restore = ActiveMtcTab ?? previous;
                    if (restore is not null)
                    {
                        if (!ReferenceEquals(restore, _boundMtcTab))
                            BindMtcTabSession(restore);
                    }
                }
            }
        }
        finally
        {
            _asyncMtcTabContext.Value = previousAsyncTab;
        }
    }

    private void ExecuteInOptionalMtcTabSession(MtcTabPrototype? tab, Action action)
    {
        tab ??= ResolveCurrentMtcTabContext();
        if (tab is null)
        {
            if (!Dispatcher.UIThread.CheckAccess())
                return;

            tab = ActiveMtcTab;
            if (tab is null)
            {
                action();
                return;
            }
        }

        ExecuteInMtcTabSession(tab, action);
    }

    private T ExecuteInOptionalMtcTabSession<T>(MtcTabPrototype? tab, Func<T> action)
    {
        tab ??= ResolveCurrentMtcTabContext();
        if (tab is null)
        {
            if (!Dispatcher.UIThread.CheckAccess())
                return default!;

            tab = ActiveMtcTab;
            if (tab is null)
                return action();
        }

        if (!Dispatcher.UIThread.CheckAccess())
            return Dispatcher.UIThread.InvokeAsync(() => ExecuteInOptionalMtcTabSession(tab, action), DispatcherPriority.Background)
                .GetAwaiter()
                .GetResult();

        T result = default!;
        ExecuteInMtcTabSession(tab, () => result = action());
        return result;
    }

    private bool IsMtcTabBackgroundContext(MtcTabPrototype tab)
        => ReferenceEquals(_asyncMtcTabContext.Value, tab) &&
           ReferenceEquals(Core.GlobalModules.CurrentContext, tab.RuntimeContext);

    private void ExecuteInMtcTabBackgroundContext(MtcTabPrototype tab, Action action)
    {
        var previousAsyncTab = _asyncMtcTabContext.Value;
        _asyncMtcTabContext.Value = tab;

        try
        {
            using (Core.GlobalModules.UseRuntimeContext(tab.RuntimeContext))
            {
                action();
            }
        }
        finally
        {
            _asyncMtcTabContext.Value = previousAsyncTab;
        }
    }

    private void ExecuteInActiveMtcTabSession(Action action)
        => ExecuteInOptionalMtcTabSession(ActiveMtcTab, action);

    private Task ExecuteInActiveMtcTabSessionAsync(Func<Task> action)
        => ExecuteInOptionalMtcTabSessionAsync(ActiveMtcTab, action);

    private void PostToMtcTabSession(
        MtcTabPrototype? owner,
        Action action,
        DispatcherPriority? priority = null)
    {
        owner ??= ResolveCurrentMtcTabContext();
        if (owner is null)
        {
            if (!Dispatcher.UIThread.CheckAccess())
                return;
            owner = ActiveMtcTab;
        }

        Dispatcher.UIThread.Post(
            () => ExecuteInOptionalMtcTabSession(owner, action),
            priority ?? DispatcherPriority.Background);
    }

    private void PostToCurrentMtcTabSession(Action action, DispatcherPriority? priority = null)
        => PostToMtcTabSession(ResolveCurrentMtcTabContext(), action, priority);

    private void PostToMtcTabSessionAsync(
        MtcTabPrototype? owner,
        Func<Task> action,
        DispatcherPriority? priority = null)
    {
        owner ??= ResolveCurrentMtcTabContext();
        if (owner is null)
        {
            if (!Dispatcher.UIThread.CheckAccess())
                return;
            owner = ActiveMtcTab;
        }

        Dispatcher.UIThread.Post(
            async () => await ExecuteInOptionalMtcTabSessionAsync(owner, action),
            priority ?? DispatcherPriority.Background);
    }

    private async Task ExecuteInOptionalMtcTabSessionAsync(MtcTabPrototype? tab, Func<Task> action)
    {
        tab ??= ResolveCurrentMtcTabContext();
        if (tab is null)
        {
            if (!Dispatcher.UIThread.CheckAccess())
                return;

            tab = ActiveMtcTab;
            if (tab is null)
            {
                await action();
                return;
            }
        }

        if (!Dispatcher.UIThread.CheckAccess())
        {
            var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            Dispatcher.UIThread.Post(async () =>
            {
                try
                {
                    await ExecuteInOptionalMtcTabSessionAsync(tab, action);
                    tcs.SetResult();
                }
                catch (Exception ex)
                {
                    tcs.SetException(ex);
                }
            }, DispatcherPriority.Background);
            await tcs.Task;
            return;
        }

        var previousAsyncTab = _asyncMtcTabContext.Value;
        var previousSyncContext = SynchronizationContext.Current;
        var tabSyncContext = new MtcTabSynchronizationContext(this, tab, previousSyncContext);
        _asyncMtcTabContext.Value = tab;
        SynchronizationContext.SetSynchronizationContext(tabSyncContext);

        Task operation = Task.CompletedTask;
        try
        {
            lock (_mtcTabSessionBindLock)
            {
                if (_boundMtcTab is not null)
                    CaptureMtcTabSession(_boundMtcTab);
                BindMtcTabSession(tab);
            }

            try
            {
                using (Core.GlobalModules.UseRuntimeContext(tab.RuntimeContext))
                {
                    operation = action();
                }
            }
            finally
            {
                RestoreActiveMtcTabSessionAfterContinuation();
            }
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previousSyncContext);
            _asyncMtcTabContext.Value = previousAsyncTab;
        }

        await operation;
    }

    private async Task<T> ExecuteInOptionalMtcTabSessionAsync<T>(MtcTabPrototype? tab, Func<Task<T>> action)
    {
        tab ??= ResolveCurrentMtcTabContext();
        if (tab is null)
        {
            if (!Dispatcher.UIThread.CheckAccess())
                return default!;

            tab = ActiveMtcTab;
            if (tab is null)
                return await action();
        }

        if (!Dispatcher.UIThread.CheckAccess())
        {
            var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
            Dispatcher.UIThread.Post(async () =>
            {
                try
                {
                    tcs.SetResult(await ExecuteInOptionalMtcTabSessionAsync(tab, action));
                }
                catch (Exception ex)
                {
                    tcs.SetException(ex);
                }
            }, DispatcherPriority.Background);
            return await tcs.Task;
        }

        var previousAsyncTab = _asyncMtcTabContext.Value;
        var previousSyncContext = SynchronizationContext.Current;
        var tabSyncContext = new MtcTabSynchronizationContext(this, tab, previousSyncContext);
        _asyncMtcTabContext.Value = tab;
        SynchronizationContext.SetSynchronizationContext(tabSyncContext);

        Task<T> operation = Task.FromResult(default(T)!);
        try
        {
            lock (_mtcTabSessionBindLock)
            {
                if (_boundMtcTab is not null)
                    CaptureMtcTabSession(_boundMtcTab);
                BindMtcTabSession(tab);
            }

            try
            {
                using (Core.GlobalModules.UseRuntimeContext(tab.RuntimeContext))
                {
                    operation = action();
                }
            }
            finally
            {
                RestoreActiveMtcTabSessionAfterContinuation();
            }
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previousSyncContext);
            _asyncMtcTabContext.Value = previousAsyncTab;
        }

        return await operation;
    }

    private void BindActiveMtcTabSession()
    {
        var active = ActiveMtcTab;
        if (active is null)
            return;

        lock (_mtcTabSessionBindLock)
        {
            if (_boundMtcTab is not null)
                CaptureMtcTabSession(_boundMtcTab);

            BindMtcTabSession(active);
        }
    }

    private string GetLiveMtcTabTitle(string? gameName)
    {
        if (ActiveMtcTab is { } active)
            return GetLiveMtcTabTitle(active, gameName);

        return string.IsNullOrWhiteSpace(gameName) ? "Game" : gameName.Trim();
    }

    private static string GetLiveMtcTabTitle(MtcTabPrototype tab, string? gameName)
    {
        if (!string.IsNullOrWhiteSpace(gameName))
            return gameName.Trim();
        if (!string.IsNullOrWhiteSpace(tab.EmbeddedGameName))
            return tab.EmbeddedGameName.Trim();
        if (tab.State is not null && !string.IsNullOrWhiteSpace(tab.State.GameName))
            return NormalizeGameName(tab.State.GameName);
        if (!string.IsNullOrWhiteSpace(tab.CurrentProfilePath))
            return System.IO.Path.GetFileNameWithoutExtension(tab.CurrentProfilePath);
        return "Game";
    }

    private void UpdateLiveMtcTabTitle(string? gameName)
    {
        var owner = ResolveCurrentMtcTabContext();
        if (owner is null && Dispatcher.UIThread.CheckAccess())
            owner = ActiveMtcTab;
        UpdateLiveMtcTabTitle(owner, gameName);
    }

    private void UpdateLiveMtcTabTitle(MtcTabPrototype? owner, string? gameName)
    {
        EnsureInitialMtcTab();

        bool changed = false;
        if (owner is { IsLiveSession: true } liveTab)
        {
            string nextTitle = GetLiveMtcTabTitle(liveTab, gameName);
            if (!string.Equals(liveTab.Title, nextTitle, StringComparison.Ordinal))
            {
                liveTab.Title = nextTitle;
                changed = true;
            }
        }

        if (changed)
        {
            RefreshMtcTabStrip();
            RefreshMtcTabOwnedWindowTitles(owner);
        }
    }

    private void CaptureLiveMtcTabShell()
    {
        if (_boundMtcTab is not null)
            CaptureMtcTabSession(_boundMtcTab);

        if (IsLiveMtcTabActive() && _shellHost.Child is not null)
            _liveTabShell = _shellHost.Child;
    }

    private void CreateStagedMtcTab()
    {
        EnsureInitialMtcTab();
        CaptureLiveMtcTabShell();

        var tab = CreateMtcTabSession($"Tab {_nextMtcTabId}", isLiveSession: true);

        _mtcTabs.Add(tab);
        ActivateMtcTab(tab.Id);
    }

    private void ActivateMtcTab(int tabId)
    {
        if (_activeMtcTabId == tabId)
            return;

        CaptureLiveMtcTabShell();
        _activeMtcTabId = tabId;
        BindActiveMtcTabSession();
        RestoreActiveMtcTabContent();
        RefreshMtcTabStrip();
    }

    private void CloseActiveMtcTab()
    {
        var active = ActiveMtcTab;
        if (active is null)
            return;

        _ = CloseMtcTabAsync(active.Id);
    }

    private void CloseMtcTab(int tabId)
        => _ = CloseMtcTabAsync(tabId);

    private async Task CloseMtcTabAsync(int tabId)
    {
        var tab = _mtcTabs.FirstOrDefault(item => item.Id == tabId);
        if (tab is null)
            return;

        if (tab.IsLiveSession && _mtcTabs.Count <= 1)
        {
            Close();
            return;
        }

        var index = _mtcTabs.IndexOf(tab);
        CloseMtcTabOwnedWindows(tab);
        tab.InfoPanelsRefreshTimer?.Stop();
        tab.StatusRefreshTimer?.Stop();
        tab.RedAlertTimer?.Stop();
        tab.CurrentGameConfigSaveTimer?.Stop();
        await StopMtcTabSessionAsync(tab);
        _mtcTabs.Remove(tab);

        if (_activeMtcTabId == tabId)
        {
            var next = _mtcTabs.ElementAtOrDefault(Math.Clamp(index - 1, 0, Math.Max(0, _mtcTabs.Count - 1)))
                ?? _mtcTabs.FirstOrDefault();
            _activeMtcTabId = next?.Id ?? 0;
            RestoreActiveMtcTabContent();
        }

        RefreshMtcTabStrip();
    }

    private void RestoreActiveMtcTabContent()
    {
        var active = ActiveMtcTab;
        if (active is null || active.IsLiveSession)
        {
            if (active is not null)
                FlushPendingDisplayChunksToBuffer(active);

            BindActiveMtcTabSession();
            ApplySelectedSkinSafe();

            RestoreLiveMtcTabStatusBar();
            RefreshActiveMtcTabUiStateScoped();
            UpdateWindowTitle();
            PostToCurrentMtcTabSession(FocusActiveTerminal, DispatcherPriority.Input);
            return;
        }

        _shellHost.Child = BuildStagedMtcTabContent(active);
        ShowStagedMtcTabStatusBar(active);
        Title = $"{BaseWindowTitle} [{active.Title}]";
    }

    private void RestoreLiveMtcTabStatusBar()
    {
        _statusBar.IsVisible = _appPrefs.ShowBottomBar;
        _statusBarLayoutSignature = string.Empty;
        EnsureStatusBarLayout();
        UpdateClassicTerminalSizeStatus();
    }

    private void ShowStagedMtcTabStatusBar(MtcTabPrototype tab)
    {
        _statusBar.IsVisible = _appPrefs.ShowBottomBar;
        _statusTerminalSizeText.IsVisible = false;
        _statusBarLayoutSignature = $"staged:{tab.Id}";
        _statusBarContent.Children.Clear();
        _statusBarContent.Children.Add(new Border
        {
            Background = HudHeaderAlt,
            BorderBrush = HudEdge,
            BorderThickness = new Thickness(1),
            CornerRadius = UiCornerRadius(12),
            Padding = UiThickness(10, 4, 10, 4),
            Child = new TextBlock
            {
                Text = $"{tab.Title} is staged - no game session loaded",
                Foreground = HudMuted,
                FontSize = UiFontSize(12),
                FontWeight = FontWeight.SemiBold,
                VerticalAlignment = VerticalAlignment.Center,
            },
        });
    }

    private void RegisterMtcTabOwnedWindow(MtcTabPrototype? owner, Window? window)
    {
        if (window == null)
            return;

        RegisterOwnedChildWindow(window);
        if (owner == null)
            return;

        ApplyMtcTabOwnedWindowTitle(owner, window);

        if (!owner.AuxiliaryWindows.Contains(window))
            owner.AuxiliaryWindows.Add(window);

        window.Closed += (_, _) =>
        {
            owner.AuxiliaryWindows.Remove(window);
            _mtcTabOwnedWindowBaseTitles.Remove(window);
        };
    }

    private void RefreshMtcTabOwnedWindowTitles(MtcTabPrototype? owner)
    {
        if (owner == null)
            return;

        foreach (Window window in owner.AuxiliaryWindows.ToArray())
            ApplyMtcTabOwnedWindowTitle(owner, window);
    }

    private void ApplyMtcTabOwnedWindowTitle(MtcTabPrototype owner, Window window)
    {
        string gameName = ResolveMtcTabOwnedWindowGameName(owner);
        if (string.IsNullOrWhiteSpace(gameName))
            return;

        if (!_mtcTabOwnedWindowBaseTitles.TryGetValue(window, out string? baseTitle))
        {
            baseTitle = NormalizeMtcTabOwnedWindowBaseTitle(window.Title, gameName);
            _mtcTabOwnedWindowBaseTitles[window] = baseTitle;
        }

        window.Title = FormatMtcTabOwnedWindowTitle(baseTitle, gameName);
    }

    private static string ResolveMtcTabOwnedWindowGameName(MtcTabPrototype owner)
    {
        string title = owner.Title.Trim();
        if (string.IsNullOrWhiteSpace(title) ||
            string.Equals(title, "Game", StringComparison.OrdinalIgnoreCase) ||
            title.StartsWith("Game ", StringComparison.OrdinalIgnoreCase))
        {
            return string.Empty;
        }

        return title;
    }

    private static string NormalizeMtcTabOwnedWindowBaseTitle(string? title, string gameName)
    {
        string value = string.IsNullOrWhiteSpace(title) ? "Window" : title.Trim();
        string[] suffixes =
        [
            $" - {gameName}",
            $" [{gameName}]",
        ];

        foreach (string suffix in suffixes)
        {
            if (value.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                return value[..^suffix.Length].Trim();
        }

        return value;
    }

    private static string FormatMtcTabOwnedWindowTitle(string baseTitle, string gameName)
    {
        string title = string.IsNullOrWhiteSpace(baseTitle) ? "Window" : baseTitle.Trim();
        string game = gameName.Trim();
        return string.IsNullOrWhiteSpace(game) ? title : $"{title} - {game}";
    }

    private Action<Window>? ResolveScriptWindowRegistration(Core.TwxRuntimeContext runtimeContext)
    {
        MtcTabPrototype? owner = _mtcTabs.FirstOrDefault(tab =>
            ReferenceEquals(tab.RuntimeContext, runtimeContext));

        return owner == null
            ? null
            : window => RegisterMtcTabOwnedWindow(owner, window);
    }

    private void ShowMtcTabOwnedWindow(MtcTabPrototype? owner, Window window, bool activate = true)
    {
        RegisterMtcTabOwnedWindow(owner, window);
        window.Show(this);

        if (activate)
            window.Activate();
    }

    private void CloseMtcTabOwnedWindows(MtcTabPrototype tab)
    {
        Window[] windows = tab.AuxiliaryWindows.ToArray();
        tab.AuxiliaryWindows.Clear();

        tab.MapWindow = null;
        tab.CacheWindow = null;
        tab.AliensWindow = null;
        tab.QCannonCalculatorWindow = null;
        tab.DataMiningWindow = null;
        tab.RouteWindow = null;
        tab.BubblesWindow = null;
        tab.SectorInfoWindow = null;
        tab.GameInfoWindow = null;
        tab.ScriptDebuggerWindow = null;
        tab.MacroSettingsDialog = null;
        tab.QuickMacroPlayWindow = null;
        tab.GameAgentWindow = null;
        tab.GameAgentReplayWindow = null;
        tab.RecordingPlaybackWindow = null;

        foreach (Window window in windows.Reverse())
        {
            try
            {
                if (!ReferenceEquals(window, this))
                    window.Close();
            }
            catch (Exception ex)
            {
                Core.GlobalModules.DebugLog($"[MTC.TabbedShell] failed to close tab child window: {ex.Message}\n");
            }
        }

        tab.GameAgent.Dispose();
    }

    private void RefreshActiveMtcTabUiState()
    {
        if (!PrepareMtcTabVisualRefresh())
            return;

        bool hasGame =
            !string.IsNullOrWhiteSpace(_state.GameName) ||
            !string.IsNullOrWhiteSpace(_currentProfilePath) ||
            _embeddedGameConfig is not null;
        bool proxyRunning = _gameInstance?.IsRunning == true;
        bool connected = _state.Connected || _telnet.IsConnected || (_gameInstance?.IsConnected == true);

        _fileEdit.IsEnabled = hasGame;
        _fileConnect.IsEnabled = hasGame && !connected;
        _fileDisconnect.IsEnabled = connected || proxyRunning;

        RefreshNotesMenuState();
        UpdateNotesForActiveGame();
        RefreshCommWindowUi();
        UpdateTemporaryMacroControls();
        UpdateTerminalRecordButton();
        RefreshMombotUi();
        UpdateHaggleToggleState();
        ApplyVisibleRedAlertUi();
        RebuildProxyMenu();
        RebuildScriptsMenu();
        RefreshStatusBar();
    }

    private void RefreshActiveMtcTabUiStateScoped()
    {
        var active = ActiveMtcTab;
        if (active is null)
        {
            RefreshActiveMtcTabUiState();
            return;
        }

        using var runtimeScope = Core.GlobalModules.UseRuntimeContext(active.RuntimeContext);
        EnsureMtcTabSessionBound(active);
        RefreshActiveMtcTabUiState();
    }

    private void RefreshMtcTabStrip(bool force = false)
    {
        if (_tabStripItems is null)
            return;

        if (AreSharedMenusOpen && !force)
        {
            _tabStripRefreshPending = true;
            return;
        }

        _tabStripRefreshPending = false;
        _tabStripItems.Children.Clear();

        foreach (var tab in _mtcTabs)
            _tabStripItems.Children.Add(BuildMtcTabButton(tab));

        var addButton = new Button
        {
            Content = "+",
            MinWidth = UiSize(40),
            Height = UiSize(34),
            Padding = UiThickness(10, 0, 10, 0),
            Background = HudHeaderAlt,
            Foreground = HudAccent,
            BorderBrush = HudEdge,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(UiSize(18), UiSize(18), UiSize(10), UiSize(10)),
            FontWeight = FontWeight.Bold,
            FontSize = UiFontSize(16),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = UiThickness(2, 4, 0, 0),
        };
        addButton.Click += (_, _) => CreateStagedMtcTab();
        _tabStripItems.Children.Add(addButton);
    }

    private async Task StopMtcTabSessionAsync(MtcTabPrototype tab)
    {
        await ExecuteInOptionalMtcTabSessionAsync(tab, async () =>
        {
            try { _telnet.Disconnect(); } catch { }
            _proxyCts?.Cancel();
            _proxyCts = null;
            if (_gameInstance != null)
            {
                await StopEmbeddedAsync();
            }
            else
            {
                _gameFileLock?.Dispose();
                _gameFileLock = null;
                try { _sessionDb?.CloseDatabase(); } catch { }
                _sessionDb = null;
                Core.ScriptRef.SetActiveDatabase(tab.RuntimeContext, null);
            }

            _sessionLog.Dispose();
        });
    }

    private void StopAllMtcTabSessions()
    {
        foreach (var tab in _mtcTabs.ToArray())
            _ = StopMtcTabSessionAsync(tab);
    }

    private Control BuildMtcTabButton(MtcTabPrototype tab)
    {
        var active = tab.Id == _activeMtcTabId;
        var tabBackground = active ? HudAccent : HudHeaderAlt;
        var tabBorder = active
            ? HudAccentOk
            : (tab.IsLiveSession ? HudEdge : HudAccentHot);
        var tabForeground = active ? HudAccentInk : HudText;
        var tabMutedForeground = active ? HudAccentInk : HudMuted;
        var frame = new Border
        {
            Background = tabBackground,
            BorderBrush = tabBorder,
            BorderThickness = active ? new Thickness(1, 1, 1, 0) : new Thickness(1),
            CornerRadius = new CornerRadius(
                UiSize(18),
                UiSize(18),
                UiSize(active ? 3 : 10),
                UiSize(active ? 3 : 10)),
            Padding = UiThickness(2, 2, 2, active ? 0 : 2),
            Margin = active
                ? new Thickness(0, 0, UiSize(4), -1)
                : new Thickness(0, UiSize(4), UiSize(4), 1),
            MinHeight = UiSize(34),
            VerticalAlignment = VerticalAlignment.Center,
        };

        var chrome = new Grid();
        chrome.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        chrome.RowDefinitions.Add(new RowDefinition { Height = new GridLength(active ? UiSize(3) : 0) });

        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 4,
            VerticalAlignment = VerticalAlignment.Center,
        };

        row.Children.Add(new Border
        {
            Width = UiSize(8),
            Height = UiSize(8),
            CornerRadius = UiCornerRadius(4),
            Background = tab.IsLiveSession
                ? (active ? HudAccentInk : HudAccent)
                : HudAccentHot,
            Opacity = active ? 1.0 : 0.65,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = UiThickness(6, 0, 0, 0),
        });

        var selectButton = new Button
        {
            Content = new TextBlock
            {
                Text = tab.IsLiveSession ? tab.Title : $"{tab.Title} (staged)",
                Foreground = tabForeground,
                FontSize = UiFontSize(12.5),
                FontWeight = active ? FontWeight.Bold : FontWeight.SemiBold,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
            },
            Background = Brushes.Transparent,
            Foreground = tabForeground,
            BorderThickness = new Thickness(0),
            Padding = UiThickness(8, 5, 8, 5),
            MinWidth = UiSize(92),
            MaxWidth = UiSize(230),
            HorizontalContentAlignment = HorizontalAlignment.Left,
        };
        selectButton.Click += (_, _) => ActivateMtcTab(tab.Id);
        row.Children.Add(selectButton);

        var closeButton = new Button
        {
            Content = "x",
            Background = Brushes.Transparent,
            Foreground = tabMutedForeground,
            BorderThickness = new Thickness(0),
            Padding = UiThickness(5, 2, 8, 2),
            MinWidth = UiSize(22),
            Height = UiSize(24),
            FontSize = UiFontSize(11),
            FontWeight = FontWeight.Bold,
            VerticalAlignment = VerticalAlignment.Center,
        };
        closeButton.Click += (_, _) => CloseMtcTab(tab.Id);
        row.Children.Add(closeButton);

        Grid.SetRow(row, 0);
        chrome.Children.Add(row);

        if (active)
        {
            var connector = new Border
            {
                Height = UiSize(4),
                Background = HudAccentInk,
                CornerRadius = new CornerRadius(0, 0, UiSize(3), UiSize(3)),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Margin = UiThickness(14, 0, 14, 0),
                Opacity = 0.3,
            };
            Grid.SetRow(connector, 1);
            chrome.Children.Add(connector);
        }

        frame.Child = chrome;
        return frame;
    }

    private Control BuildStagedMtcTabContent(MtcTabPrototype tab)
    {
        var outer = new Border
        {
            Background = HudHeader,
            BorderBrush = HudEdge,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(14),
            Padding = UiThickness(24),
            Margin = UiThickness(8),
        };

        var stack = new StackPanel
        {
            Spacing = 14,
            MaxWidth = UiSize(760),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };

        stack.Children.Add(new TextBlock
        {
            Text = "Tabbed Client Prototype",
            Foreground = HudAccent,
            FontSize = UiFontSize(26),
            FontWeight = FontWeight.Bold,
        });
        stack.Children.Add(new TextBlock
        {
            Text = "This staged tab is intentionally not connected yet. It exists to validate the tab layout, close behavior, and child-window ownership boundary before live multi-game sessions are allowed in one process.",
            Foreground = HudText,
            FontSize = UiFontSize(15),
            TextWrapping = TextWrapping.Wrap,
            LineHeight = UiSize(22),
        });
        stack.Children.Add(new TextBlock
        {
            Text = "Next implementation step: extract the current MainWindow game state into an MtcGameSessionHost so each tab owns its own terminal, proxy, database handle, timers, menus, and child windows.",
            Foreground = HudMuted,
            FontSize = UiFontSize(13),
            TextWrapping = TextWrapping.Wrap,
            LineHeight = UiSize(20),
        });

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 10,
            Margin = UiThickness(0, 8, 0, 0),
        };

        var returnButton = new Button
        {
            Content = "Return to live game",
            Background = HudAccent,
            Foreground = Brushes.Black,
            BorderBrush = HudAccent,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = UiThickness(16, 8, 16, 8),
            FontWeight = FontWeight.Bold,
        };
        returnButton.Click += (_, _) =>
        {
            var live = _mtcTabs.FirstOrDefault(item => item.IsLiveSession);
            if (live is not null)
                ActivateMtcTab(live.Id);
        };
        buttons.Children.Add(returnButton);

        var newWindowButton = new Button
        {
            Content = "Open separate window",
            Background = HudHeaderAlt,
            Foreground = HudText,
            BorderBrush = HudEdge,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = UiThickness(16, 8, 16, 8),
        };
        newWindowButton.Click += (_, _) => OpenNewWindowInNewProcess();
        buttons.Children.Add(newWindowButton);

        stack.Children.Add(buttons);
        outer.Child = stack;
        return outer;
    }
}
