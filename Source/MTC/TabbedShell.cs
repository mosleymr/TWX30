using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
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
        public ConcurrentQueue<PendingDisplayChunk> PendingDisplayChunks { get; } = new();
        public ConcurrentQueue<byte[]> PendingSessionLogChunks { get; } = new();
        public object TerminalDisplayArtifactSync { get; } = new();
        public List<byte[]> PausedTerminalChunks { get; } = [];
        public int DisplayDrainScheduled;
        public int SessionLogDrainScheduled;
        public bool TerminalLivePaused { get; set; }
        public Core.NativeHaggleEngine StandaloneNativeHaggle { get; init; } = null!;
        public MTC.mombot.mombotService Mombot { get; init; } = null!;
        public Action<byte[]>? TerminalInputHandler { get; set; }
        public List<CommEntry> CommEntries { get; } = [];
        public Core.CommMessageChannel CommSelectedChannel { get; set; } = Core.CommMessageChannel.FedComm;
        public string CommPrivateTarget { get; set; } = string.Empty;
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
        public ScriptDebuggerWindow? ScriptDebuggerWindow { get; set; }
        public MacroSettingsDialog? MacroSettingsDialog { get; set; }
        public MacroPlayDialog? QuickMacroPlayWindow { get; set; }
        public GameAgentWindow? GameAgentWindow { get; set; }
        public GameAgentReplayWindow? GameAgentReplayWindow { get; set; }
        public TerminalRecordingPlaybackWindow? RecordingPlaybackWindow { get; set; }
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
    private readonly SemaphoreSlim _mtcAsyncSessionGate = new(1, 1);
    private static readonly AsyncLocal<int> _mtcAsyncSessionDepth = new();
    private static readonly AsyncLocal<MtcTabPrototype?> _asyncMtcTabContext = new();
    private MtcTabPrototype? _boundMtcTab;

    private MtcTabPrototype? ActiveMtcTab
        => _mtcTabs.FirstOrDefault(tab => tab.Id == _activeMtcTabId);

    private Core.TwxRuntimeContext? ActiveMtcRuntimeContext
        => ActiveMtcTab?.RuntimeContext;

    private bool IsLiveMtcTabActive()
        => ActiveMtcTab is null || ActiveMtcTab.IsLiveSession;

    private void InitializeTabbedShell()
    {
        EnsureInitialMtcTab();

        _tabStripHost.Background = HudStatus;
        _tabStripHost.BorderBrush = HudInnerEdge;
        _tabStripHost.BorderThickness = new Thickness(0, 0, 0, 1);
        _tabStripHost.Padding = UiThickness(10, 7, 10, 0);
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
        var tab = new MtcTabPrototype
        {
            Id = id,
            IsLiveSession = isLiveSession,
            Title = string.IsNullOrWhiteSpace(title) ? $"Game {id}" : title.Trim(),
            RuntimeContext = new Core.TwxRuntimeContext($"mtc-tab-{id}"),
            State = new GameState(),
            Buffer = buffer,
            Parser = parser,
            Telnet = new TelnetClient(buffer, parser),
            ShipParser = new Core.ShipInfoParser(),
            SessionLog = new Core.ModLog(),
            StandaloneNativeHaggle = new Core.NativeHaggleEngine(),
            Mombot = new MTC.mombot.mombotService(),
        };

        parser.RawBytesObserved = (bytes, offset, length) =>
        {
            if (tab.Id == _activeMtcTabId)
                ObserveTerminalOutputBytesForRecording(bytes, offset, length);
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
            Dispatcher.UIThread.Post(() =>
            {
                if (tab.Id == _activeMtcTabId)
                    RequestInfoPanelsRefresh();
            }, DispatcherPriority.Background);

        tab.StandaloneNativeHaggle.EnabledChanged += _ =>
            Dispatcher.UIThread.Post(() => ExecuteInMtcTabSession(tab, () =>
            {
                UpdateHaggleToggleState();
                RequestStatusBarRefresh();
            }), DispatcherPriority.Background);

        tab.StandaloneNativeHaggle.StatsChanged += () =>
            Dispatcher.UIThread.Post(() => ExecuteInMtcTabSession(tab, RequestStatusBarRefresh), DispatcherPriority.Background);

        tab.Telnet.Connected += () =>
            Dispatcher.UIThread.Post(() => ExecuteInMtcTabSession(tab, OnTelnetConnected), DispatcherPriority.Background);

        tab.Telnet.Disconnected += () =>
            Dispatcher.UIThread.Post(() => ExecuteInMtcTabSession(tab, OnTelnetDisconnected), DispatcherPriority.Background);

        tab.Telnet.Error += message =>
            Dispatcher.UIThread.Post(() => ExecuteInMtcTabSession(tab, () => OnTelnetError(message)), DispatcherPriority.Background);

        tab.Telnet.TextLineReceived += tab.ShipParser.FeedLine;
        tab.ShipParser.Updated += status =>
            Dispatcher.UIThread.Post(() => ExecuteInMtcTabSession(tab, () => OnShipStatusUpdated(status)), DispatcherPriority.Background);

        tab.Telnet.TextLineAnsiReceived += (ansiLine, strippedLine) =>
            ExecuteInMtcTabSession(tab, () =>
            {
                Core.GlobalModules.GlobalAutoRecorder.RecordLine(strippedLine, ansiLine);
                ObserveGameAgentServerLine(strippedLine, ansiLine, isPrompt: LooksLikeAgentPrompt(strippedLine));
                ObserveOnlinePlayersLine(strippedLine);
                HandlePotentialCommLine(ansiLine);
                ProcessStandaloneNativeHaggleLine(strippedLine);
            });

        tab.Telnet.AppDataDecoded += text =>
            ExecuteInMtcTabSession(tab, () =>
            {
                MarkGameTrafficActivity();
                _sessionLog.RecordServerText(text);
            });

        var recorder = tab.RuntimeContext.AutoRecorder;
        recorder.CurrentSectorChanged += sn =>
            Dispatcher.UIThread.Post(() => ExecuteInMtcTabSession(tab, () =>
            {
                Core.ScriptRef.SetCurrentSector(sn);
                SetMombotCurrentVars(sn.ToString(), "$PLAYER~CURRENT_SECTOR", "$player~current_sector");
                var sectorDelta = new Core.ShipStatusDelta
                {
                    CurrentSector = sn
                };
                if (_gameInstance != null)
                    _gameInstance.ApplyShipStatusDelta(sectorDelta);
                else
                    _shipParser.ApplyDelta(sectorDelta);
                if (_state.Sector != sn)
                {
                    _state.Sector = sn;
                    ObserveGameAgentCurrentSectorChanged(sn);
                    _state.NotifyChanged();
                }
            }), DispatcherPriority.Background);

        recorder.LandmarkSectorsChanged += () =>
            Dispatcher.UIThread.Post(() => ExecuteInMtcTabSession(tab, () =>
            {
                SyncMombotSpecialSectorVarsFromDatabase(persist: true);
                RefreshStatusBar();
                _buffer.Dirty = true;
            }), DispatcherPriority.Background);

        recorder.GenesisTorpsChanged += delta =>
            Dispatcher.UIThread.Post(() => ExecuteInMtcTabSession(tab, () => OnGenesisTorpsChanged(delta)), DispatcherPriority.Background);

        recorder.AtomicDetChanged += delta =>
            Dispatcher.UIThread.Post(() => ExecuteInMtcTabSession(tab, () => OnAtomicDetChanged(delta)), DispatcherPriority.Background);

        recorder.ShipStatusDeltaDetected += delta =>
            ExecuteInMtcTabSession(tab, () =>
            {
                if (_gameInstance != null)
                {
                    _gameInstance.ApplyShipStatusDelta(delta);
                    return;
                }

                _shipParser.ApplyDelta(delta);
            });
    }

    private void CaptureMtcTabSession(MtcTabPrototype tab)
    {
        tab.SessionDb = _sessionDb;
        tab.GameInstance = _gameInstance;
        tab.ModuleHost = _moduleHost;
        tab.GameFileLock = _gameFileLock;
        tab.TerminalLivePaused = _terminalLivePaused;
        tab.TerminalInputHandler = _terminalInputHandler ?? tab.TerminalInputHandler;
        tab.ProxyCts = _proxyCts;
        tab.PendingEmbeddedStop = _pendingEmbeddedStop;
        tab.EmbeddedGameConfig = _embeddedGameConfig;
        tab.EmbeddedGameName = _embeddedGameName;
        tab.CurrentProfilePath = _currentProfilePath;
        tab.CommSelectedChannel = _commSelectedChannel;
        tab.CommPrivateTarget = _commPrivateTarget;
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
        _standaloneNativeHaggle = tab.StandaloneNativeHaggle;
        _mombot = tab.Mombot;
        _terminalInputHandler = tab.TerminalInputHandler;
        _proxyCts = tab.ProxyCts;
        _pendingEmbeddedStop = tab.PendingEmbeddedStop;
        _embeddedStopSync = tab.EmbeddedStopSync;
        _runtimeStopGate = tab.RuntimeStopGate;
        _embeddedGameConfig = tab.EmbeddedGameConfig;
        _embeddedGameName = tab.EmbeddedGameName;
        _currentProfilePath = tab.CurrentProfilePath;
        _commEntries = tab.CommEntries;
        _commSelectedChannel = tab.CommSelectedChannel;
        _commPrivateTarget = tab.CommPrivateTarget;
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
        _boundMtcTab = tab;

        ApplyMtcTabTerminalInputHandler(tab);
        ApplyMtcTabTerminalSurface(tab);
    }

    private MtcTabPrototype? CurrentMtcTabContext()
        => _asyncMtcTabContext.Value ?? _boundMtcTab ?? ActiveMtcTab;

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
        MtcTabPrototype? restore = null;
        bool tabWasActive = tab.Id == _activeMtcTabId;
        bool refreshActiveUiAfterRestore = false;
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
                    CaptureMtcTabSession(tab);

                    restore = previous ?? ActiveMtcTab;
                    if (restore is not null && !ReferenceEquals(restore, tab))
                    {
                        BindMtcTabSession(restore);
                        refreshActiveUiAfterRestore = !tabWasActive && Dispatcher.UIThread.CheckAccess();
                    }
                }
            }
        }
        finally
        {
            _asyncMtcTabContext.Value = previousAsyncTab;
        }

        if (refreshActiveUiAfterRestore)
            RefreshActiveMtcTabUiState();
    }

    private void ExecuteInOptionalMtcTabSession(MtcTabPrototype? tab, Action action)
    {
        if (tab is null)
        {
            action();
            return;
        }

        ExecuteInMtcTabSession(tab, action);
    }

    private T ExecuteInOptionalMtcTabSession<T>(MtcTabPrototype? tab, Func<T> action)
    {
        if (tab is null)
            return action();

        T result = default!;
        ExecuteInMtcTabSession(tab, () => result = action());
        return result;
    }

    private void ExecuteInActiveMtcTabSession(Action action)
        => ExecuteInOptionalMtcTabSession(ActiveMtcTab, action);

    private Task ExecuteInActiveMtcTabSessionAsync(Func<Task> action)
        => ExecuteInOptionalMtcTabSessionAsync(ActiveMtcTab, action);

    private async Task ExecuteInOptionalMtcTabSessionAsync(MtcTabPrototype? tab, Func<Task> action)
    {
        if (tab is null)
        {
            await action();
            return;
        }

        bool ownsGate = _mtcAsyncSessionDepth.Value == 0;
        if (ownsGate)
            await _mtcAsyncSessionGate.WaitAsync();

        _mtcAsyncSessionDepth.Value++;
        var previousAsyncTab = _asyncMtcTabContext.Value;
        _asyncMtcTabContext.Value = tab;
        bool tabWasActive = tab.Id == _activeMtcTabId;
        bool refreshActiveUiAfterRestore = false;

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
                    await action();
                }
            }
            finally
            {
                lock (_mtcTabSessionBindLock)
                {
                    CaptureMtcTabSession(tab);
                    var restore = ActiveMtcTab;
                    if (restore is not null && !ReferenceEquals(restore, tab))
                    {
                        BindMtcTabSession(restore);
                        refreshActiveUiAfterRestore = !tabWasActive && Dispatcher.UIThread.CheckAccess();
                    }
                }
            }
        }
        finally
        {
            _asyncMtcTabContext.Value = previousAsyncTab;
            _mtcAsyncSessionDepth.Value--;
            if (ownsGate)
                _mtcAsyncSessionGate.Release();
        }

        if (refreshActiveUiAfterRestore)
            RefreshActiveMtcTabUiState();
    }

    private async Task<T> ExecuteInOptionalMtcTabSessionAsync<T>(MtcTabPrototype? tab, Func<Task<T>> action)
    {
        if (tab is null)
            return await action();

        bool ownsGate = _mtcAsyncSessionDepth.Value == 0;
        if (ownsGate)
            await _mtcAsyncSessionGate.WaitAsync();

        _mtcAsyncSessionDepth.Value++;
        var previousAsyncTab = _asyncMtcTabContext.Value;
        _asyncMtcTabContext.Value = tab;
        bool tabWasActive = tab.Id == _activeMtcTabId;
        bool refreshActiveUiAfterRestore = false;

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
                    return await action();
                }
            }
            finally
            {
                lock (_mtcTabSessionBindLock)
                {
                    CaptureMtcTabSession(tab);
                    var restore = ActiveMtcTab;
                    if (restore is not null && !ReferenceEquals(restore, tab))
                    {
                        BindMtcTabSession(restore);
                        refreshActiveUiAfterRestore = !tabWasActive && Dispatcher.UIThread.CheckAccess();
                    }
                }
            }
        }
        finally
        {
            _asyncMtcTabContext.Value = previousAsyncTab;
            _mtcAsyncSessionDepth.Value--;
            if (ownsGate)
                _mtcAsyncSessionGate.Release();

            if (refreshActiveUiAfterRestore)
                RefreshActiveMtcTabUiState();
        }
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
        if (!string.IsNullOrWhiteSpace(gameName))
            return gameName.Trim();
        if (!string.IsNullOrWhiteSpace(_embeddedGameName))
            return _embeddedGameName.Trim();
        if (_state is not null && !string.IsNullOrWhiteSpace(_state.GameName))
            return NormalizeGameName(_state.GameName);
        if (!string.IsNullOrWhiteSpace(_currentProfilePath))
            return System.IO.Path.GetFileNameWithoutExtension(_currentProfilePath);
        return "Game";
    }

    private void UpdateLiveMtcTabTitle(string? gameName)
    {
        EnsureInitialMtcTab();

        var contextTab = CurrentMtcTabContext();
        var liveTab = contextTab is { IsLiveSession: true }
            ? contextTab
            : ActiveMtcTab is { IsLiveSession: true } active
            ? active
            : _mtcTabs.FirstOrDefault(tab => tab.IsLiveSession);
        if (liveTab is not null)
            liveTab.Title = GetLiveMtcTabTitle(gameName);

        RefreshMtcTabStrip();
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
            BindActiveMtcTabSession();
            ApplySelectedSkinSafe();

            RestoreLiveMtcTabStatusBar();
            RefreshActiveMtcTabUiState();
            UpdateWindowTitle();
            Dispatcher.UIThread.Post(FocusActiveTerminal, DispatcherPriority.Input);
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

        if (!owner.AuxiliaryWindows.Contains(window))
            owner.AuxiliaryWindows.Add(window);

        window.Closed += (_, _) => owner.AuxiliaryWindows.Remove(window);
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
    }

    private void RefreshActiveMtcTabUiState()
    {
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
        RefreshMombotUi();
        UpdateHaggleToggleState();
        RebuildProxyMenu();
        RebuildScriptsMenu();
        RefreshStatusBar();
    }

    private void RefreshMtcTabStrip()
    {
        if (_tabStripItems is null)
            return;

        _tabStripItems.Children.Clear();

        foreach (var tab in _mtcTabs)
            _tabStripItems.Children.Add(BuildMtcTabButton(tab));

        var addButton = new Button
        {
            Content = "+",
            MinWidth = UiSize(36),
            Height = UiSize(30),
            Padding = UiThickness(10, 0, 10, 0),
            Background = HudHeader,
            Foreground = HudAccent,
            BorderBrush = HudEdge,
            BorderThickness = new Thickness(1),
            CornerRadius = UiCornerRadius(15),
            FontWeight = FontWeight.Bold,
            FontSize = UiFontSize(16),
            VerticalAlignment = VerticalAlignment.Center,
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
                Core.ScriptRef.SetActiveDatabase(null);
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
        var frame = new Border
        {
            Background = active ? HudHeaderAlt : HudStatus,
            BorderBrush = active ? HudAccent : HudInnerEdge,
            BorderThickness = new Thickness(active ? 2 : 1),
            CornerRadius = UiCornerRadius(16),
            Padding = UiThickness(3, 2, 3, active ? 0 : 2),
            Margin = active ? new Thickness(0, 0, 0, -1) : new Thickness(0, 0, 0, 1),
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
                ? (active ? HudAccentOk : HudAccent)
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
                Foreground = active ? HudText : HudMuted,
                FontSize = UiFontSize(12.5),
                FontWeight = active ? FontWeight.Bold : FontWeight.SemiBold,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
            },
            Background = Brushes.Transparent,
            Foreground = active ? HudText : HudMuted,
            BorderThickness = new Thickness(0),
            Padding = UiThickness(8, 4, 8, 4),
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
            Foreground = active ? HudText : HudMuted,
            BorderThickness = new Thickness(0),
            Padding = UiThickness(5, 2, 7, 2),
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
                Height = UiSize(3),
                Background = HudAccent,
                CornerRadius = UiCornerRadius(2),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Margin = UiThickness(12, 0, 12, 0),
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
