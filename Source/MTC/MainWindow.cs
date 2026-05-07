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

/// <summary>
/// Main application window – SWATH-style layout:
///   [Menu bar]
///   [Sidebar 165 px | ANSI terminal (expandable)]
///   [Status bar]
/// </summary>
public partial class MainWindow : Window
{
    private sealed record CommEntry(Core.CommMessageChannel Channel, string Sender, string Message, bool IsLocal);
    private readonly record struct PendingDisplayChunk(byte[] Bytes, int LineCount, bool RewrotePromptOverwrite);
    private readonly record struct FinderPrewarmKey(
        string DatabasePath,
        long ChangeStamp,
        int BubbleMaxSize,
        int DeadEndMaxSize,
        int TunnelMaxSize,
        bool AllowSeparatedByGates);

    private const string BaseWindowTitle = "Mayhem Tradewars Client v1.0";
    private const int MaxCommEntries = 500;
    private const double ClassicCommWindowDefaultHeight = 140;
    private const double DeckCommWindowDefaultHeight = 150;
    private const double CommWindowMinHeight = 90;
    private const double ClassicCommSplitterHeight = 6;
    private const double DeckCommSplitterHeight = 8;
    private const double DeckPanelSnapThreshold = 18;
    private const double DeckPanelSnapGap = 18;
    private const double DeckPanelGridSize = 18;
    private const int TemporaryMacroMaxCharacters = 200;
    private const int EmbeddedLocalClientIndex = 0;
    private static readonly double[] TerminalFontSizeOptions = [10, 11, 12, 13, 14, 15, 16, 18, 20, 22, 24, 28, 32];

    // ── Core components ────────────────────────────────────────────────────
    private readonly GameState       _state;
    private readonly TerminalBuffer  _buffer;
    private readonly AnsiParser      _parser;
    private readonly TelnetClient    _telnet;
    private TerminalControl _termCtrl = null!;
    private TerminalControl _deckTermCtrl = null!;
    private readonly DispatcherTimer _statusRefreshTimer;
    private readonly DispatcherTimer _redAlertTimer;
    private readonly Core.ShipInfoParser _shipParser = new();
    private readonly DispatcherTimer _mombotKeepaliveTimer;
    // ── Current saved profile path (null = not yet saved) ──────────────────
    private string?         _currentProfilePath;
    private AppPreferences  _appPrefs = new();
    private Core.ModDatabase?              _sessionDb;
    private Core.GameInstance?             _gameInstance;   // non-null only in embedded proxy mode
    private Core.GameFileLock?             _gameFileLock;
    private Core.ExpansionModuleHost?      _moduleHost;
    private readonly Core.NativeHaggleEngine _standaloneNativeHaggle = new();
    private CancellationTokenSource?       _proxyCts;       // cancels the pipe-reader task
    private Task                           _pendingEmbeddedStop = Task.CompletedTask; // tracks in-flight StopEmbeddedAsync
    private readonly object                _embeddedStopSync = new();
    private readonly SemaphoreSlim         _runtimeStopGate = new(1, 1);
    private readonly Core.ModLog           _sessionLog = new();
    private EmbeddedGameConfig?            _embeddedGameConfig;
    private string?                        _embeddedGameName;
    private readonly Dictionary<string, AiAssistantWindow> _assistantWindows = new(StringComparer.OrdinalIgnoreCase);
    private ScriptDebuggerWindow?          _scriptDebuggerWindow;
    private static readonly System.Text.Json.JsonSerializerOptions _jsonOpts = new()
    {
        WriteIndented             = true,
        PropertyNameCaseInsensitive = true,
    };
    private const string NativeMombotMenuLabel = "MomBot (native)";
    private MenuItem        _recentMenu    = new() { Header = "_Recent" };
    private MenuItem        _proxyMenu     = new() { Header = "_Proxy" };
    private MenuItem        _scriptsMenu   = new() { Header = "_Scripts" };
    private MenuItem        _botMenu       = new() { Header = "_Bot" };
    private MenuItem        _quickMenu     = new() { Header = "_Quick" };
    private MenuItem        _toolsMenu     = new() { Header = "_Tools" };
    private MenuItem        _aiMenu        = new() { Header = "_AI", IsVisible = false };
    private readonly MenuItem _viewClearRecents = new() { Header = "Clear _Recents" };
    private MenuItem        _fileEdit       = new() { Header = "_Edit Connection…", IsEnabled = false };
    private MenuItem        _fileConnect    = new() { Header = "_Connect",    IsEnabled = false };
    private MenuItem        _fileDisconnect = new() { Header = "_Disconnect", IsEnabled = false };
    private Menu            _menuBar       = new();
    private readonly MenuItem _viewClassicSkin = new() { Header = "_Classic Console" };
    private readonly MenuItem _viewCommandDeckSkin = new() { Header = "_Command Deck" };
    private readonly MenuItem _viewCommWindow = new() { Header = "_Comm Window" };
    private readonly MenuItem _viewShowHaggleDetails = new() { Header = "Haggle _Statistics" };
    private readonly MenuItem _viewBottomBar = new() { Header = "_Status Bar" };
    private readonly List<(MenuItem Item, double Size)> _viewFontSizeItems = [];
    private readonly NativeMenu _nativeAppMenu = new();
    private readonly NativeMenu _nativeDockMenu = new();
    private readonly MTC.mombot.mombotService _mombot = new();
    private readonly Border _shellHost = new();
    private readonly Border _statusBar = new();
    private readonly Border _menuBarHost = new();
    private readonly Border _menuFontSizeFrame = new();
    private readonly Button _menuFontSizeDecreaseButton = new() { Content = "-" };
    private readonly Button _menuFontSizeIncreaseButton = new() { Content = "+" };
    private readonly Border _statusMacroHost = new();
    private readonly Grid _statusBarLayoutRoot = new();
    private readonly StackPanel _statusBarContent = new()
    {
        Orientation = Orientation.Horizontal,
        Spacing = 8,
        VerticalAlignment = VerticalAlignment.Center,
        Margin = new Thickness(8, 0),
    };
    private DockPanel? _rootDock;
    private Canvas? _deckSurface;
    private readonly Dictionary<string, FloatingDeckPanel> _deckPanels = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, DeckPanelState> _deckPanelStates = new(StringComparer.OrdinalIgnoreCase);
    private int _deckNextZIndex = 100;
    private bool _deckPanelsInitialized;
    private bool _suppressDeckPanelStateSync;
    private TacticalMapControl? _tacticalMap;
    private MapWindow? _mapWindow;
    private CacheWindow? _cacheWindow;
    private bool _useCommandDeckSkin;
    private bool _nativeAppMenuReady;
    private bool _nativeAppMenuAttached;
    private bool _nativeDockMenuAttached;
    private bool _commWindowVisible;
    private double _classicCommWindowHeight = ClassicCommWindowDefaultHeight;
    private double _deckCommWindowHeight = DeckCommWindowDefaultHeight;
    private Core.CommMessageChannel _commSelectedChannel = Core.CommMessageChannel.FedComm;
    private string _commPrivateTarget = string.Empty;
    private Button? _macroRecordButton;
    private Button? _macroStopButton;
    private Button? _macroPlayButton;
    private Button? _deckMacroRecordButton;
    private Button? _deckMacroStopButton;
    private Button? _deckMacroPlayButton;
    private readonly List<byte[]> _temporaryMacroChunks = [];
    private bool _temporaryMacroRecording;
    private bool _suppressTemporaryMacroRecording;
    private MacroSettingsDialog? _macroSettingsDialog;
    private readonly Button _statusMacrosButton = new();
    private readonly Button _statusStopAllButton = new();
    private readonly Button _statusCommButton = new();
    private readonly Button _statusBotButton = new();
    private readonly Button _statusMapButton = new();
    private readonly Button _statusHaggleButton = new() { Content = "HAGGLE" };
    private readonly Button _statusLivePausedButton = new() { Content = "LIVE" };
    private readonly Button _statusRedAlertButton = new() { Content = "RED ALERT" };
    private readonly Border _statusMacrosFrame = new();
    private readonly Border _statusStopAllFrame = new();
    private readonly Border _statusCommFrame = new();
    private readonly Border _statusBotFrame = new();
    private readonly Border _statusMapFrame = new();
    private readonly Border _statusHaggleFrame = new();
    private readonly Border _statusLivePausedFrame = new();
    private readonly Border _statusRedAlertFrame = new();
    private readonly object _pausedTerminalSync = new();
    private readonly object _terminalDisplayArtifactSync = new();
    private readonly object _finderPrewarmSync = new();
    private readonly List<byte[]> _pausedTerminalChunks = [];
    private readonly ConcurrentQueue<PendingDisplayChunk> _pendingDisplayChunks = new();
    private bool _terminalLivePaused;
    private int _displayDrainScheduled;
    private string _statusBarLayoutSignature = string.Empty;
    private bool _statusMacrosHovered;
    private bool _statusStopAllHovered;
    private bool _statusCommHovered;
    private bool _statusBotHovered;
    private bool _statusMapHovered;
    private bool _statusHaggleHovered;
    private bool _statusLivePausedHovered;
    private bool _redAlertEnabled;
    private Avalonia.Controls.Shapes.Path? _statusStopAllSign;
    private TextBlock? _statusStopAllLabel;
    private Border? _statusCommFlap;
    private Border? _statusCommBody;
    private Border? _statusCommIndicator;
    private Border? _statusBotHead;
    private Border? _statusBotBody;
    private Border? _statusBotEyeLeft;
    private Border? _statusBotEyeRight;
    private Border? _statusBotAntenna;
    private Border? _statusBotAntennaTip;
    private Border? _statusHaggleSpark;
    private Border? _statusHaggleBeam;
    private Border? _statusHaggleStem;
    private Border? _statusHaggleLeftLink;
    private Border? _statusHaggleRightLink;
    private Border? _statusHaggleLeftPan;
    private Border? _statusHaggleRightPan;
    private Border? _statusHaggleBase;
    private Border? _statusMapPanelLeft;
    private Border? _statusMapPanelCenter;
    private Border? _statusMapPanelRight;
    private Avalonia.Controls.Shapes.Path? _statusMapRoute;
    private Border? _statusMapNodeA;
    private Border? _statusMapNodeB;
    private Border? _statusMapNodeC;
    private Border? _statusMacrosLineTop;
    private Border? _statusMacrosLineMiddle;
    private Border? _statusMacrosLineBottom;
    private Avalonia.Controls.Shapes.Path? _statusMacrosPlay;
    private Border? _commPanelBorder;
    private Button? _commFedTabButton;
    private Button? _commSubspaceTabButton;
    private Button? _commPrivateTabButton;
    private TextBlock? _commFedTextBlock;
    private TextBlock? _commSubspaceTextBlock;
    private TextBlock? _commPrivateTextBlock;
    private ScrollViewer? _commFedScrollViewer;
    private ScrollViewer? _commSubspaceScrollViewer;
    private ScrollViewer? _commPrivateScrollViewer;
    private TextBox? _commComposeTextBox;
    private TextBox? _commPrivateTargetTextBox;
    private TextBlock? _commPrivateTargetLabel;
    private RowDefinition? _commSplitterRow;
    private RowDefinition? _commPanelRow;
    private GridSplitter? _commGridSplitter;
    private Border? _deckCommPanelBorder;
    private Button? _deckCommFedTabButton;
    private Button? _deckCommSubspaceTabButton;
    private Button? _deckCommPrivateTabButton;
    private TextBlock? _deckCommFedTextBlock;
    private TextBlock? _deckCommSubspaceTextBlock;
    private TextBlock? _deckCommPrivateTextBlock;
    private ScrollViewer? _deckCommFedScrollViewer;
    private ScrollViewer? _deckCommSubspaceScrollViewer;
    private ScrollViewer? _deckCommPrivateScrollViewer;
    private TextBox? _deckCommComposeTextBox;
    private TextBox? _deckCommPrivateTargetTextBox;
    private TextBlock? _deckCommPrivateTargetLabel;
    private RowDefinition? _deckCommSplitterRow;
    private RowDefinition? _deckCommPanelRow;
    private GridSplitter? _deckCommGridSplitter;
    private readonly List<CommEntry> _commEntries = [];
    private Action<byte[]>? _terminalInputHandler;
    private string? _terminalFontFamilyName;
    private double _terminalFontSize = TerminalControl.DefaultFontSize;
    private bool _mombotPromptOpen;
    private bool _mombotHotkeyPromptOpen;
    private bool _mombotScriptPromptOpen;
    private bool _mombotPreferencesOpen;
    private bool _mombotPreferencesCaptureSingleKey;
    private string _mombotPreferencesInputPrompt = string.Empty;
    private string _mombotPreferencesInputBuffer = string.Empty;
    private Action<string>? _mombotPreferencesInputHandler;
    private MombotPreferencesBlankSubmitBehavior _mombotPreferencesBlankSubmitBehavior = MombotPreferencesBlankSubmitBehavior.Ignore;
    private int _mombotPreferencesHotkeySlot;
    private int _mombotPreferencesShipPageStart = 1;
    private int _mombotPreferencesPlanetTypePageStart = 1;
    private int _mombotPreferencesPlanetListCursor = 2;
    private int _mombotPreferencesPlanetListNextCursor = 2;
    private bool _mombotPreferencesPlanetListHasMore;
    private int _mombotPreferencesTraderListCursor = 2;
    private int _mombotPreferencesTraderListNextCursor = 2;
    private bool _mombotPreferencesTraderListHasMore;
    private bool _mombotMacroPromptOpen;
    private MombotGridContext? _mombotMacroContext;
    private IReadOnlyList<MombotHotkeyScriptEntry> _mombotHotkeyScripts = Array.Empty<MombotHotkeyScriptEntry>();
    private readonly List<string> _mombotCommandHistory = [];
    private string _mombotPromptBuffer = string.Empty;
    private string _mombotPromptDraft = string.Empty;
    private Func<string, string>? _mombotPromptSubmitTransform;
    private int _mombotPromptHistoryIndex;
    private int _mombotPromptCursorIndex;
    private MombotPreferencesPage _mombotPreferencesPage;
    private string _mombotLastKeepaliveLine = string.Empty;
    private int _mombotObservedGamePromptVersion;
    private int _mombotPromptRestoreTicket;
    private int _mombotMacroPromptRedrawTicket;
    private int _serverOverwritePromptRestoreTicket;
    private string _mombotLastObservedGamePromptAnsi = string.Empty;
    private string _mombotLastObservedGamePromptPlain = string.Empty;
    private long _mombotLastServerOutputUtcTicks;
    private long _mombotLastTerminalOutputUtcTicks;
    private int _pendingNativeMombotEscapeEchoSuppressions;
    private long _nativeMombotEscapeEchoSuppressUntilUtcTicks;
    private bool _suppressingPendingNativeMombotEscapeSequence;
    private bool _suppressingPendingNativeMombotEscapeCsiBody;
    private bool _pendingTerminalSyncMarkerLeadByte;
    private bool _pendingTerminalSyncMarkerUtf8LeadByte;
    private bool _mombotKeepaliveTickRunning;
    private bool _mombotStartupDataGatherPending;
    private bool _mombotStartupDataGatherRunning;
    private bool _mombotStartupPostInitPending;
    private bool _mombotStartupFinalizeRunning;
    private bool _nativeBotAutoStartInFlight;
    private FinderPrewarmKey? _lastFinderPrewarmKey;
    private string _currentShipType = string.Empty;
    private string _currentShipClass = string.Empty;
    private string _currentComputerShipType = string.Empty;
    private bool _awaitingComputerShipTypeLine;
    private readonly List<string> _onlinePlayers = [];
    private bool _capturingOnlinePlayers;
    private sealed record StoredBotSection(
        string SectionName,
        string Alias,
        string DisplayName,
        bool IsNative,
        bool ScriptAvailable,
        Core.BotConfig Config,
        Dictionary<string, string> Values);
    private sealed record BotRuntimeState(bool NativeRunning, string ExternalBotName)
    {
        public bool IsRunning => NativeRunning || !string.IsNullOrWhiteSpace(ExternalBotName);

        public string DisplayName =>
            NativeRunning
                ? NativeMombotMenuLabel
                : string.IsNullOrWhiteSpace(ExternalBotName)
                    ? "Off"
                    : ExternalBotName;
    }
    // ── Sidebar value TextBlocks (updated when GameState fires Changed) ────
    private TextBlock _valName     = new();
    private StackPanel _onlinePlayersHost = new() { Spacing = 2 };
    private TextBlock _valSector    = new();
    private Border    _sectorBustIndicator = new();
    private TextBlock _valTurns     = new();
    private TextBlock _valExper     = new();
    private TextBlock _valAlignm    = new();
    private TextBlock _valCred      = new();
    private TextBlock _valHTotal    = new();
    private TextBlock _valFuelOre   = new();
    private TextBlock _valOrganics  = new();
    private TextBlock _valEquipment = new();
    private TextBlock _valColonists = new();
    private TextBlock _valEmpty     = new();
    private ColumnDefinition _holdsFuelOreColumn = new();
    private ColumnDefinition _holdsOrganicsColumn = new();
    private ColumnDefinition _holdsEquipmentColumn = new();
    private ColumnDefinition _holdsColonistsColumn = new();
    private ColumnDefinition _holdsEmptyColumn = new();
    private Border? _holdsFuelOreSegment;
    private Border? _holdsOrganicsSegment;
    private Border? _holdsEquipmentSegment;
    private Border? _holdsColonistsSegment;
    private Border? _holdsEmptySegment;
    private TextBlock _shipInfoHeaderText = new();
    private TextBlock _valFighters  = new();
    private TextBlock _valShields   = new();
    private TextBlock _valTrnWarp   = new();
    // Ship Info – compact paired equipment rows
    private TextBlock _valEther     = new();
    private TextBlock _valBeacon    = new();
    private TextBlock _valDisruptor = new();
    private TextBlock _valPhoton    = new();
    private TextBlock _valArmid     = new();
    private TextBlock _valLimpet    = new();
    private TextBlock _valGenesis   = new();
    private TextBlock _valAtomic    = new();
    private TextBlock _valCorbo     = new();
    private TextBlock _valCloak     = new();
    private TextBlock _valTW1       = new();
    private TextBlock _valTW2       = new();
    private Border    _scanIndTW1   = new();
    private Border    _scanIndTW2   = new();
    private Border    _scanIndD     = new();
    private Border    _scanIndH     = new();
    private Border    _scanIndP     = new();
    private TextBlock _deckValName     = new();
    private TextBlock _deckValSector   = new();
    private TextBlock _deckValTurns    = new();
    private TextBlock _deckValExper    = new();
    private TextBlock _deckValAlignm   = new();
    private TextBlock _deckValCred     = new();
    private TextBlock _deckValHTotal   = new();
    private TextBlock _deckValFuelOre  = new();
    private TextBlock _deckValOrganics = new();
    private TextBlock _deckValEquipment = new();
    private TextBlock _deckValColonists = new();
    private TextBlock _deckValEmpty     = new();
    private TextBlock _deckValFighters  = new();
    private TextBlock _deckValShields   = new();
    private TextBlock _deckValTrnWarp   = new();
    private TextBlock _deckValEther     = new();
    private TextBlock _deckValBeacon    = new();
    private TextBlock _deckValDisruptor = new();
    private TextBlock _deckValPhoton    = new();
    private TextBlock _deckValArmid     = new();
    private TextBlock _deckValLimpet    = new();
    private TextBlock _deckValGenesis   = new();
    private TextBlock _deckValAtomic    = new();
    private TextBlock _deckValCorbo     = new();
    private TextBlock _deckValCloak     = new();
    private TextBlock _deckValTW1       = new();
    private TextBlock _deckValTW2       = new();
    private Border    _deckScanIndTW1   = new();
    private Border    _deckScanIndTW2   = new();
    private Border    _deckScanIndD     = new();
    private Border    _deckScanIndH     = new();
    private Border    _deckScanIndP     = new();

    // ── Status bar text ───────────────────────────────────────────────────
    private TextBlock _statusText = new();
    private TextBlock _statusTerminalSizeText = new();
    private TextBlock _statusStarDockValue = new();
    private TextBlock _statusBackdoorValue = new();
    private TextBlock _statusRylosValue = new();
    private TextBlock _statusAlphaValue = new();
    private Control? _statusStarDockChip;
    private Control? _statusBackdoorChip;
    private Control? _statusRylosChip;
    private Control? _statusAlphaChip;
    private TextBlock _deckHudHeaderSector = new();
    private TextBlock _deckHudHeaderConnection = new();
    private TextBlock _deckHudShipName = new();
    private TextBlock _deckHudShipSubtitle = new();
    private TextBlock _deckHudStarDock = new();
    private TextBlock _deckHudRylos = new();
    private TextBlock _deckHudAlpha = new();
    private TextBlock _deckHudUniverse = new();

    // ── Colors ────────────────────────────────────────────────────────────
    // BgChrome is the medium-gray frame that encases the whole window
    private static readonly IBrush BgChrome    = new SolidColorBrush(Color.FromRgb(105, 105, 105));
    private static readonly IBrush BgWindow    = new SolidColorBrush(Color.FromRgb(105, 105, 105));
    private static readonly IBrush BgSidebar   = new SolidColorBrush(Color.FromRgb(80,  80,  80));
    private static readonly IBrush BgPanel     = new SolidColorBrush(Color.FromRgb(88,  88,  88));
    private static readonly IBrush FgKey       = new SolidColorBrush(Color.FromRgb(220, 220, 220));
    private static readonly IBrush FgValue     = new SolidColorBrush(Color.FromRgb(85,  255, 85));
    private static readonly IBrush FgTitle     = new SolidColorBrush(Color.FromRgb(255, 255, 85));
    private static readonly IBrush BgStatus    = new SolidColorBrush(Color.FromRgb(70,  70,  70));
    private static readonly IBrush FgStatus    = new SolidColorBrush(Color.FromRgb(230, 230, 230));
    private static readonly IBrush BorderColor    = new SolidColorBrush(Color.FromRgb(40,  40,  40));
    private static readonly IBrush BorderHi       = new SolidColorBrush(Color.FromRgb(140, 140, 140));
    private static readonly IBrush ScannerActive  = new SolidColorBrush(Color.FromRgb(85,  255, 85));
    private static readonly IBrush ScannerInactive= new SolidColorBrush(Color.FromRgb(50,  50,  50));
    private static readonly IBrush ScannerFgInact = new SolidColorBrush(Color.FromRgb(130, 130, 130));
    private static readonly IBrush HudWindow    = new SolidColorBrush(Color.FromRgb(8,  14, 20));
    private static readonly IBrush HudMenu      = new SolidColorBrush(Color.FromRgb(16, 27, 36));
    private static readonly IBrush HudShell     = new SolidColorBrush(Color.FromRgb(10,  21, 29));
    private static readonly IBrush HudFrame     = new SolidColorBrush(Color.FromRgb(14,  33, 42));
    private static readonly IBrush HudFrameAlt  = new SolidColorBrush(Color.FromRgb(18,  43, 53));
    private static readonly IBrush HudHeader    = new SolidColorBrush(Color.FromRgb(16,  53, 67));
    private static readonly IBrush HudHeaderAlt = new SolidColorBrush(Color.FromRgb(20,  64, 74));

    private static string NormalizeLegacyInterrogLineForScripts(string line)
    {
        if (string.IsNullOrEmpty(line) || line[0] != ':')
            return line;

        int index = 1;
        while (index < line.Length && char.IsWhiteSpace(line[index]))
            index++;

        if (index >= line.Length)
            return line;

        ReadOnlySpan<char> tail = line.AsSpan(index);
        if (tail.StartsWith("FM >", StringComparison.Ordinal) ||
            tail.StartsWith("TO >", StringComparison.Ordinal) ||
            tail.StartsWith("The shortest path (", StringComparison.Ordinal))
        {
            return line[index..];
        }

        return line;
    }

    private static bool NormalizeDensityScanner(bool densityScanner, bool holoScanner)
        => densityScanner || holoScanner;

    private static readonly IBrush HudText      = new SolidColorBrush(Color.FromRgb(222, 238, 242));
    private static readonly IBrush HudMuted     = new SolidColorBrush(Color.FromRgb(126, 170, 180));
    private static readonly IBrush HudEdge      = new SolidColorBrush(Color.FromRgb(57, 112, 128));
    private static readonly IBrush HudInnerEdge = new SolidColorBrush(Color.FromRgb(23,  81, 94));
    private static readonly IBrush HudAccent    = new SolidColorBrush(Color.FromRgb(0,   212, 201));
    private static readonly IBrush HudAccentInk = new SolidColorBrush(Color.FromRgb(8,  26, 30));
    private static readonly IBrush HudAccentHot = new SolidColorBrush(Color.FromRgb(255, 193, 74));
    private static readonly IBrush HudAccentOk  = new SolidColorBrush(Color.FromRgb(118, 255, 141));
    private static readonly IBrush HudAccentWarn= new SolidColorBrush(Color.FromRgb(255, 112, 112));
    private static readonly IBrush HudBustBg    = new SolidColorBrush(Color.FromRgb(196, 48, 48));
    private static readonly IBrush HudStatus    = new SolidColorBrush(Color.FromRgb(11,  20, 28));
    private static readonly IBrush HudInset     = new SolidColorBrush(Color.FromRgb(5,   12, 18));
    private static readonly IBrush HudInsetEdge = new SolidColorBrush(Color.FromRgb(69,  128, 144));
    private static readonly IBrush HoldsOreBrush = new SolidColorBrush(Color.FromRgb(214, 164, 96));
    private static readonly IBrush HoldsOrgBrush = new SolidColorBrush(Color.FromRgb(118, 178, 116));
    private static readonly IBrush HoldsEqBrush = new SolidColorBrush(Color.FromRgb(96, 171, 194));
    private static readonly IBrush HoldsColsBrush = new SolidColorBrush(Color.FromRgb(164, 128, 198));
    private static readonly IBrush HoldsFreeBrush = new SolidColorBrush(Color.FromRgb(123, 145, 156));
    private static readonly Regex OnlinePlayerLineWithCorpRegex = new(
        @"^(?:[A-Za-z0-9][A-Za-z0-9'/-]*\s+)*([A-Za-z0-9][A-Za-z0-9'/-]*)\s+\[(\d+)\]\s*$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex OnlinePlayerLineWithoutCorpRegex = new(
        @"^(?:[A-Za-z0-9][A-Za-z0-9'/-]*\s+)+([A-Za-z0-9][A-Za-z0-9'/-]*)\s*$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex OnlinePlayerEnteredGameRegex = new(
        @"^(.+?)\s+enters the game\.$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex OnlinePlayerExitedGameRegex = new(
        @"^(.+?)\s+exits the game\.$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private const int FinderPrewarmMaxSize = Core.ModBubble.DefaultMaxBubbleSize;

    private static void SetBrushColor(IBrush brush, Color color)
    {
        if (brush is SolidColorBrush solidBrush)
            solidBrush.Color = color;
    }

    private void ApplyRedAlertPalette(bool enabled)
    {
        if (enabled)
        {
            SetBrushColor(BgWindow,    Color.FromRgb(54, 20, 24));
            SetBrushColor(HudWindow,   Color.FromRgb(19,  7, 10));
            SetBrushColor(HudMenu,     Color.FromRgb(34, 11, 15));
            SetBrushColor(HudShell,    Color.FromRgb(28, 10, 14));
            SetBrushColor(HudEdge,     Color.FromRgb(184, 52, 58));
        }
        else
        {
            SetBrushColor(BgWindow,    Color.FromRgb(105, 105, 105));
            SetBrushColor(HudWindow,   Color.FromRgb(8,   14,  20));
            SetBrushColor(HudMenu,     Color.FromRgb(16,  27,  36));
            SetBrushColor(HudShell,    Color.FromRgb(10,  21,  29));
            SetBrushColor(HudEdge,     Color.FromRgb(57,  112, 128));
        }
    }
    private static readonly FontFamily HudTitleFont = new("Eurostile, Bank Gothic, Bahnschrift, Segoe UI, sans-serif");
    private static readonly Bitmap AboutLogo = new(AssetLoader.Open(new Uri("avares://MTC/mtc.png")));
    private static readonly Bitmap HudLogo = new(AssetLoader.Open(new Uri("avares://MTC/mtc2.png")));

    private sealed class DeckPanelState
    {
        public required string PanelId { get; init; }
        public required double Left { get; set; }
        public required double Top { get; set; }
        public required double Width { get; set; }
        public required double BodyHeight { get; set; }
        public required int ZIndex { get; set; }
        public bool Closed { get; set; }
        public bool Minimized { get; set; }
    }

    // ── Constructor ────────────────────────────────────────────────────────
    public MainWindow()
    {
        Title          = BaseWindowTitle;
        Icon           = new WindowIcon(AssetLoader.Open(new Uri("avares://MTC/mtc2.png")));
        Width          = 1100;
        Height         = 650;
        MinWidth       = 800;
        MinHeight      = 500;
        Background     = BgWindow;
        FontFamily     = new FontFamily("Cascadia Code, Menlo, Consolas, Courier New, monospace");

        _state    = new GameState();
        _buffer   = new TerminalBuffer(80, 24);
        _parser   = new AnsiParser(_buffer);
        RecreateClassicShellControls();
        RecreateDeckShellControls();
        _telnet   = new TelnetClient(_buffer, _parser);

        _telnet.Connected    += OnTelnetConnected;
        _telnet.Disconnected += OnTelnetDisconnected;
        _telnet.Error        += OnTelnetError;

        // Ship status: feed every server line through the parser
        _telnet.TextLineReceived += _shipParser.FeedLine;
        _shipParser.Updated      += OnShipStatusUpdated;

        UpdateWindowTitle();

        // Database recording: feed server lines through the AutoRecorder
        _telnet.TextLineAnsiReceived += (ansiLine, strippedLine) =>
        {
            Core.GlobalModules.GlobalAutoRecorder.RecordLine(strippedLine, ansiLine);
            HandlePotentialCommLine(ansiLine);
            ProcessStandaloneNativeHaggleLine(strippedLine);
        };

        // Session logging for direct telnet mode is handled through the shared Core logger.
        RefreshSessionLogTarget();
        _telnet.AppDataDecoded += text =>
        {
            _sessionLog.RecordServerText(text);
        };

        // Update current sector from the command prompt — fires on every "Command [TL=...]:[N]"
        Core.GlobalModules.GlobalAutoRecorder.CurrentSectorChanged += sn =>
            Dispatcher.UIThread.Post(() =>
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
                    _state.NotifyChanged();
                }
            });

        Core.GlobalModules.GlobalAutoRecorder.LandmarkSectorsChanged += () =>
            Dispatcher.UIThread.Post(() =>
            {
                SyncMombotSpecialSectorVarsFromDatabase(persist: true);
                RefreshStatusBar();
                _buffer.Dirty = true;
            });

        Core.GlobalModules.GlobalAutoRecorder.GenesisTorpsChanged += delta =>
            Dispatcher.UIThread.Post(() => OnGenesisTorpsChanged(delta));

        Core.GlobalModules.GlobalAutoRecorder.AtomicDetChanged += delta =>
            Dispatcher.UIThread.Post(() => OnAtomicDetChanged(delta));

        Core.GlobalModules.GlobalAutoRecorder.ShipStatusDeltaDetected += delta =>
        {
            if (_gameInstance != null)
            {
                _gameInstance.ApplyShipStatusDelta(delta);
                return;
            }

            _shipParser.ApplyDelta(delta);
        };

        _state.Changed += () => Dispatcher.UIThread.Post(RefreshInfoPanels);

        // Wire keyboard → telnet
        SetTerminalInputHandler(bytes => RouteTerminalInput(bytes, SendToTelnet));

        // Load persisted preferences (recent file list etc.) before the first shell build
        // so we don't compose the visual tree twice on startup.
        _appPrefs = AppPreferences.Load();
        _standaloneNativeHaggle.SetEnabled(true);
        _standaloneNativeHaggle.SetPortHaggleMode(ResolveGlobalPortHaggleMode());
        _standaloneNativeHaggle.SetPlanetHaggleMode(ResolveGlobalPlanetHaggleMode());
        _standaloneNativeHaggle.EnabledChanged += _ => Dispatcher.UIThread.Post(() =>
        {
            UpdateHaggleToggleState();
            RequestStatusBarRefresh();
        });
        _standaloneNativeHaggle.StatsChanged += () => Dispatcher.UIThread.Post(RequestStatusBarRefresh);
        bool resetCommandDeckLayout =
            _appPrefs.CommandDeckLayoutVersion < AppPreferences.CurrentCommandDeckLayoutVersion ||
            _appPrefs.CommandDeckPanels.Values.Any(layout => layout.Width <= 0 || layout.BodyHeight <= 0);
        if (resetCommandDeckLayout)
        {
            _appPrefs.CommandDeckPanels.Clear();
            _appPrefs.CommandDeckLayoutVersion = AppPreferences.CurrentCommandDeckLayoutVersion;
            _appPrefs.Save();
        }
        AppPaths.SetConfiguredProgramDir(_appPrefs.ProgramDirectory);
        _useCommandDeckSkin = _appPrefs.CommandDeckSkinEnabled;

        _statusRefreshTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(120) };
        _statusRefreshTimer.Tick += (_, _) =>
        {
            _statusRefreshTimer.Stop();
            RefreshStatusBar();
        };

        _redAlertTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
        _redAlertTimer.Tick += (_, _) =>
        {
            _redAlertTimer.Stop();
            ClearRedAlert();
        };

        Content = BuildLayout();
        PositionChanged += (_, _) => NotifyTerminalWindowMove();

        ApplyDebugLoggingPreferences();
        ApplyRedAlertPreference();
        RebuildRecentMenu();
        RebuildProxyMenu();
        RebuildScriptsMenu();
        RebuildAiMenu();
        _parser.Feed("\x1b[2J\x1b[H");
        _parser.Feed("\x1b[1;33mMayhem Tradewars Client v1.0\x1b[0m\r\n");
        _parser.Feed("\x1b[37mUse \x1b[1;32mFile \u25b6 New Connection\x1b[0;37m or \x1b[1;32mOpen\x1b[0;37m to select a game, then \x1b[1;32mFile \u25b6 Connect\x1b[0;37m to connect.\x1b[0m\r\n");
        _buffer.Dirty = true;

        _mombotKeepaliveTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
        _mombotKeepaliveTimer.Tick += (_, _) =>
        {
            if (_mombotKeepaliveTickRunning)
                return;

            _mombotKeepaliveTickRunning = true;
            _ = RunNativeMombotKeepaliveTickAsync();
        };
        _mombotKeepaliveTimer.Start();

        Opened += (_, _) =>
        {
            _nativeAppMenuReady = true;
            _nativeAppMenuAttached = false;
            _nativeDockMenuAttached = false;
            RefreshNativeAppMenu();
            RefreshNativeDockMenu();
            _ = EnsureSharedPathsConfiguredAsync();
        };
        Activated += (_, _) => FocusActiveTerminal();
        Closed    += (_, _) =>
        {
            _appPrefs.Save();
            _nativeAppMenuReady = false;
            _nativeAppMenuAttached = false;
            _nativeDockMenuAttached = false;
            _mombotKeepaliveTimer.Stop();
            _telnet.Disconnect();
            _proxyCts?.Cancel();
            if (_moduleHost != null)
                _ = _moduleHost.DisposeAsync().AsTask();
            foreach (AiAssistantWindow window in _assistantWindows.Values.ToList())
                window.Close();
            _assistantWindows.Clear();
            if (_gameInstance != null) _ = _gameInstance.StopAsync();
            _gameFileLock?.Dispose();
            _gameFileLock = null;
            _sessionLog.Dispose();
            _redAlertTimer.Stop();
            _statusRefreshTimer.Stop();
        };
    }

    private void OnTelnetConnected()
    {
        _state.Connected = true;
        RefreshSessionLogTarget(CurrentInterpreter?.ScriptDirectory);
        // Open (or create) the sector database for this game connection
        OpenSessionDatabase(DeriveGameName(), _state.Sectors, useSharedProxyDatabase: false);
        Dispatcher.UIThread.Post(() =>
        {
            SetTerminalConnected(true);
            OnGameConnected();
            UpdateTemporaryMacroControls();
            _parser.Feed($"\x1b[1;32m[Connected to {_state.Host}:{_state.Port}]\x1b[0m\r\n");
            RefreshStatusBar();
            _buffer.Dirty = true;
        });
    }

    private void OnTelnetDisconnected()
    {
        _state.Connected = false;
        _sessionLog.CloseLog();
        // Flush and close the database
        try { _sessionDb?.CloseDatabase(); } catch { /* best-effort */ }
        _sessionDb = null;
        _gameFileLock?.Dispose();
        _gameFileLock = null;
        Core.ScriptRef.SetActiveDatabase(null);
        Dispatcher.UIThread.Post(() =>
        {
            SetTerminalConnected(false);
            OnGameDisconnected();
            UpdateTemporaryMacroControls();
            _parser.Feed("\x1b[1;31m[Disconnected]\x1b[0m\r\n");
            RefreshStatusBar();
            _buffer.Dirty = true;
        });
    }

    private void OnTelnetError(string msg)
    {
        Dispatcher.UIThread.Post(() =>
        {
            _parser.Feed($"\x1b[1;31m[Error: {msg}]\x1b[0m\r\n");
            _buffer.Dirty = true;
        });
    }

    // ── Connection menu state helpers ──────────────────────────────────────

    /// <summary>Derives a filesystem-safe game name for log/DB file naming.</summary>
    private string DeriveGameName()
    {
        string name = !string.IsNullOrWhiteSpace(_state.GameName)
            ? _state.GameName
            : (!string.IsNullOrEmpty(_currentProfilePath)
                ? Path.GetFileNameWithoutExtension(_currentProfilePath)
                : $"{_state.Host}_{_state.Port}");
        name = string.Concat(name.Split(Path.GetInvalidFileNameChars()));
        return string.IsNullOrWhiteSpace(name) ? "game" : name;
    }

    /// <summary>Call after a profile is applied (game selected) to enable Connect.</summary>
    private void OnGameSelected()
    {
        ClearOnlinePlayers();
        _fileEdit.IsEnabled       = true;
        _fileConnect.IsEnabled    = true;
        _fileDisconnect.IsEnabled = false;
        RebuildProxyMenu();
        RebuildScriptsMenu();
    }

    /// <summary>Call when TCP connection is established.</summary>
    private void OnGameConnected()
    {
        _fileConnect.IsEnabled    = false;
        _fileDisconnect.IsEnabled = true;
        UpdateHaggleToggleState();
        RefreshMombotUi();
        RebuildProxyMenu();
        RebuildScriptsMenu();
    }

    /// <summary>Call when TCP connection is lost / disconnected.</summary>
    private void OnGameDisconnected()
    {
        ClearOnlinePlayers();
        ClearRedAlert();
        _fileConnect.IsEnabled    = true;
        _fileDisconnect.IsEnabled = false;
        UpdateHaggleToggleState();
        RefreshMombotUi();
        RebuildProxyMenu();
        RebuildScriptsMenu();
    }

    private void OnHaggleToggleRequested()
    {
        if (_gameInstance == null)
        {
            if (CanUseRemoteProxyScripts())
            {
                SendProxyMenuCommand("h");
                Dispatcher.UIThread.Post(FocusActiveTerminal, DispatcherPriority.Input);
                return;
            }

            if (!_state.EmbeddedProxy && _telnet.IsConnected)
            {
                bool enabled = _standaloneNativeHaggle.Toggle();
                _parser.Feed($"\x1b[1;36m[Native haggle {(enabled ? "enabled" : "disabled")}]\x1b[0m\r\n");
                _buffer.Dirty = true;
            }
            UpdateHaggleToggleState();
            return;
        }

        _termCtrl.SendInput?.Invoke(System.Text.Encoding.ASCII.GetBytes("$h"));
        Dispatcher.UIThread.Post(FocusActiveTerminal, DispatcherPriority.Input);
    }

    private void UpdateHaggleToggleState()
    {
        bool haggleAvailable = _gameInstance != null || (!_state.EmbeddedProxy && _telnet.IsConnected);
        _statusHaggleButton.IsEnabled = haggleAvailable;
        UpdateTerminalLiveSelector();
    }

    private void ProcessStandaloneNativeHaggleLine(string strippedLine)
    {
        if (_state.EmbeddedProxy ||
            CanUseRemoteProxyScripts() ||
            !_telnet.IsConnected ||
            string.IsNullOrWhiteSpace(strippedLine))
            return;

        string? response = _standaloneNativeHaggle.HandleLine(strippedLine);
        if (string.IsNullOrEmpty(response))
            return;

        _telnet.SendRaw(System.Text.Encoding.ASCII.GetBytes(response + "\r"));
        Core.GlobalModules.DebugLog($"[MTC.NativeHaggle] standalone SEND '{response}\\r'\n");
    }

    private void ApplyMombotConfigChange(Action<MTC.mombot.mombotConfig> update)
    {
        _embeddedGameConfig ??= new EmbeddedGameConfig();
        MTC.mombot.mombotConfig config = GetOrCreateEmbeddedMombotConfig(_embeddedGameConfig);

        update(config);
        config.WatcherEnabled = config.Enabled;
        _mombot.ApplyConfig(config);
        RefreshStatusBar();
        RebuildProxyMenu();
        _ = SaveCurrentGameConfigAsync();
    }

    private BotRuntimeState GetBotRuntimeState()
    {
        string externalBotName = _gameInstance?.ActiveBotName ?? string.Empty;
        return new BotRuntimeState(_mombot.Enabled, externalBotName);
    }

    private void RefreshMombotUi()
    {
        if (_mombot.Enabled)
            return;

        if (HasMombotInteractiveState())
            CloseMombotInteractiveState();
    }

    private bool HasMombotInteractiveState()
    {
        return _mombotPromptOpen ||
            _mombotHotkeyPromptOpen ||
            _mombotScriptPromptOpen ||
            _mombotPreferencesOpen ||
            _mombotMacroPromptOpen ||
            _mombotPreferencesInputHandler != null ||
            _mombotPreferencesInputBuffer.Length > 0;
    }

    private void CloseMombotInteractiveState(bool clearBotIsDeaf = true)
    {
        if (!HasMombotInteractiveState() && !clearBotIsDeaf)
            return;

        ResetMombotPromptState();
        if (clearBotIsDeaf)
            PersistMombotBoolean(false, "$BOT~BOTISDEAF", "$BOT~botIsDeaf", "$bot~botIsDeaf", "$botIsDeaf");

        _parser.Feed("\r\x1b[K");
        _buffer.Dirty = true;
        FocusActiveTerminal();
    }

    private void EnsureEmbeddedMombotClientAudible()
    {
        PersistMombotBoolean(false, "$BOT~BOTISDEAF", "$BOT~botIsDeaf", "$bot~botIsDeaf", "$botIsDeaf");

        if (_gameInstance == null)
            return;

        if (_terminalLivePaused)
        {
            SetTerminalLivePaused(false);
            return;
        }

        _gameInstance.SetClientType(EmbeddedLocalClientIndex, Core.ClientType.Standard);
    }

    private void OnNativeHaggleChanged(bool enabled, Core.NativeHaggleChangeSource source)
    {
        var gameConfig = _embeddedGameConfig;
        var gameName = _embeddedGameName;
        if (source == Core.NativeHaggleChangeSource.User &&
            gameConfig != null &&
            !string.IsNullOrWhiteSpace(gameName) &&
            gameConfig.NativeHaggleEnabled != enabled)
        {
            gameConfig.NativeHaggleEnabled = enabled;
            _ = SaveEmbeddedGameConfigAsync(gameName, gameConfig);
        }

        Dispatcher.UIThread.Post(() =>
        {
            UpdateHaggleToggleState();
            RefreshMombotUi();
            RequestStatusBarRefresh();
            _buffer.Dirty = true;
        });
    }

    private void OnNativeHaggleStatsChanged()
    {
        Dispatcher.UIThread.Post(() =>
        {
            RefreshMombotUi();
            if (ShouldShowStatusBarHaggleInfo())
            {
                RequestStatusBarRefresh();
                _buffer.Dirty = true;
            }
        });
    }

    private async Task OnAdvancedProxySettingsAsync()
    {
        await Task.Yield();

        string currentPortMode = ResolveGlobalPortHaggleMode();
        string currentPlanetMode = ResolveGlobalPlanetHaggleMode();
        _appPrefs.PortHaggleMode = currentPortMode;
        _appPrefs.PlanetHaggleMode = currentPlanetMode;
        IReadOnlyList<Core.NativeHaggleModeInfo> availablePortModes =
            _gameInstance?.NativePortHaggleModes ?? DiscoverAvailableNativeHaggleModes(Core.NativeHaggleTradeKind.Port);
        IReadOnlyList<Core.NativeHaggleModeInfo> availablePlanetModes =
            _gameInstance?.NativePlanetHaggleModes ?? DiscoverAvailableNativeHaggleModes(Core.NativeHaggleTradeKind.Planet);
        var dialog = new AdvancedProxySettingsDialog(currentPortMode, currentPlanetMode, availablePortModes, availablePlanetModes);
        bool saved = await dialog.ShowDialog<bool>(this);
        if (!saved)
            return;

        string selectedPortMode = Core.NativeHaggleModes.Normalize(dialog.SelectedPortHaggleMode);
        string selectedPlanetMode = Core.NativeHaggleModes.Normalize(dialog.SelectedPlanetHaggleMode);
        if (string.Equals(currentPortMode, selectedPortMode, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(currentPlanetMode, selectedPlanetMode, StringComparison.OrdinalIgnoreCase))
            return;

        _appPrefs.PortHaggleMode = selectedPortMode;
        _appPrefs.PlanetHaggleMode = selectedPlanetMode;
        _appPrefs.Save();

        if (_gameInstance != null)
            _gameInstance.SetNativeHaggleModes(selectedPortMode, selectedPlanetMode);
        else
        {
            _standaloneNativeHaggle.SetPortHaggleMode(selectedPortMode);
            _standaloneNativeHaggle.SetPlanetHaggleMode(selectedPlanetMode);
        }

        string selectedPortLabel = availablePortModes
            .FirstOrDefault(info => string.Equals(info.Id, selectedPortMode, StringComparison.OrdinalIgnoreCase))
            ?.DisplayName ?? selectedPortMode;
        string selectedPlanetLabel = availablePlanetModes
            .FirstOrDefault(info => string.Equals(info.Id, selectedPlanetMode, StringComparison.OrdinalIgnoreCase))
            ?.DisplayName ?? selectedPlanetMode;
        _parser.Feed($"\x1b[1;36m[Native haggle modes: Port={selectedPortLabel} ({selectedPortMode}), Planet={selectedPlanetLabel} ({selectedPlanetMode})]\x1b[0m\r\n");
        _buffer.Dirty = true;
        RebuildProxyMenu();
    }

    private IReadOnlyList<Core.NativeHaggleModeInfo> DiscoverAvailableNativeHaggleModes(Core.NativeHaggleTradeKind tradeKind)
    {
        return Core.NativeHaggleModeDiscovery.DiscoverFromDirectories(new[]
        {
            AppPaths.ModulesDir,
            Core.SharedPaths.LegacyModulesDir,
        })
        .Where(info => info.SupportsTradeKind(tradeKind))
        .ToList();
    }

    // ── Menu actions ───────────────────────────────────────────────────────

    private async void OnConnect()
    {
        if (_state.EmbeddedProxy)
        {
            string targetGameName = GetEmbeddedGameName();
            if (_gameInstance != null &&
                (!_gameInstance.IsRunning ||
                 !string.Equals(_gameInstance.GameName, targetGameName, StringComparison.OrdinalIgnoreCase)))
                await StopEmbeddedAsync();

            if (_gameInstance == null)
                await DoConnectEmbeddedAsync();

            if (_gameInstance != null && !_gameInstance.IsConnected)
                await ConnectEmbeddedServerAsync();
        }
        else
            DoConnect();
    }

    private async void OnDisconnect()
    {
        if (_gameInstance != null)
        {
            if (_gameInstance.IsConnected)
                await _gameInstance.DisconnectFromServerAsync();
            return;
        }
        if (!_telnet.IsConnected)
        {
            _ = ShowMessageAsync("Disconnect", "No active connection.");
            return;
        }
        _telnet.Disconnect();
    }

    private async Task OnResetGameAsync()
    {
        _menuBar.Close();

        string gameName = NormalizeGameName(_embeddedGameName ?? DeriveGameName());
        if (string.IsNullOrWhiteSpace(gameName))
        {
            await ShowMessageAsync("Reset Game", "No game is currently loaded.");
            return;
        }

        bool confirmed = await ShowConfirmAsync(
            "Reset Game",
            $"This will reset all game data and settings for '{gameName}'.\n\nAre you sure?",
            "Yes",
            "Cancel");
        if (!confirmed)
            return;

        bool restartEmbeddedProxy = _state.EmbeddedProxy && _gameInstance != null;
        string configPath = GameConfigPathForMode(gameName, _state.EmbeddedProxy);
        EmbeddedGameConfig config = _embeddedGameConfig ?? await LoadOrCreateEmbeddedGameConfigAsync(gameName);
        config.Name = gameName;
        config.DatabasePath = ResolveResetDatabasePath(gameName, config);
        ResetEmbeddedGameIdentity(config);

        Core.DataHeader sourceHeader = ResolveResetSourceHeader(config.DatabasePath);
        Core.DataHeader resetHeader = BuildResetDatabaseHeader(config, sourceHeader);

        try
        {
            if (_gameInstance != null)
            {
                await StopEmbeddedAsync();
            }
            else if (_telnet.IsConnected)
            {
                _telnet.Disconnect();
            }

            try { _sessionDb?.CloseDatabase(); } catch { }
            _sessionDb = null;
            _gameFileLock?.Dispose();
            _gameFileLock = null;
            Core.ScriptRef.SetActiveDatabase(null);
            Core.ScriptRef.OnVariableSaved = null;
            Core.ScriptRef.ClearCurrentGameVars();
            ClearMombotRelogState();
            ResetMombotGameStorage(gameName);

            Directory.CreateDirectory(Path.GetDirectoryName(config.DatabasePath)!);
            using var resetLock = Core.GameFileLock.Acquire("MTC reset game", configPath, config.DatabasePath);
            var db = new Core.ModDatabase();
            db.CreateDatabase(config.DatabasePath, resetHeader);
            db.CloseDatabase();

            config.Mtc ??= new EmbeddedMtcConfig();
            config.Mtc.State = new EmbeddedMtcState();
            await SaveEmbeddedGameConfigAsync(gameName, config);

            _currentProfilePath = configPath;
            _embeddedGameConfig = config;
            _embeddedGameName = gameName;
            ApplyProfile(BuildProfileFromConfig(config));
            OnGameSelected();

            _parser.Feed($"\x1b[1;36m[Game reset: {gameName}]\x1b[0m\r\n");
            _buffer.Dirty = true;

            if (restartEmbeddedProxy)
                await DoConnectEmbeddedAsync();
        }
        catch (Exception ex)
        {
            await ShowMessageAsync("Reset Game Error", ex.Message);
        }
    }

    private void ResetEmbeddedGameIdentity(EmbeddedGameConfig config)
    {
        config.UseLogin = false;
        config.UseRLogin = false;
        config.LoginScript = "0_Login.cts";
        config.LoginName = string.Empty;
        config.Password = string.Empty;
        config.GameLetter = string.Empty;
        config.Variables.Clear();

        if (config.Extra != null)
        {
            config.Extra.Remove("CharacterName");
            config.Extra.Remove("LastConnected");
        }
    }

    private void ResetMombotGameStorage(string gameName)
    {
        string normalizedGameName = NormalizeGameName(gameName);
        if (string.IsNullOrWhiteSpace(normalizedGameName))
            return;

        string scriptDirectory = CurrentInterpreter?.ScriptDirectory ?? GetEffectiveProxyScriptDirectory();
        string programDir = CurrentInterpreter?.ProgramDir ?? GetEffectiveProxyProgramDir(scriptDirectory);
        string folderPath = Path.Combine(programDir, "games", normalizedGameName);

        DeleteDirectoryIfPresent(folderPath);

        string nativeScriptRoot = GetMombotScriptRootRelative(GetNativeMombotScriptRoot(BuildCurrentGameNativeBotConfig()));
        string legacyFolderPath = Path.Combine(
            programDir,
            nativeScriptRoot.Replace('/', Path.DirectorySeparatorChar),
            "games",
            normalizedGameName);
        DeleteDirectoryIfPresent(legacyFolderPath);
    }

    private static void DeleteDirectoryIfPresent(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
            return;

        try
        {
            Directory.Delete(path, recursive: true);
        }
        catch
        {
        }
    }

    private string ResolveResetDatabasePath(string gameName, EmbeddedGameConfig config)
    {
        if (!string.IsNullOrWhiteSpace(_sessionDb?.DatabasePath))
            return _sessionDb.DatabasePath;

        if (!string.IsNullOrWhiteSpace(config.DatabasePath))
            return config.DatabasePath;

        return _state.EmbeddedProxy
            ? AppPaths.TwxproxyDatabasePathForGame(gameName)
            : AppPaths.MtcStandaloneDatabasePathForGame(gameName);
    }

    private Core.DataHeader ResolveResetSourceHeader(string databasePath)
    {
        if (_sessionDb != null)
            return _sessionDb.DBHeader;

        if (!string.IsNullOrWhiteSpace(databasePath) && File.Exists(databasePath))
        {
            try
            {
                var db = new Core.ModDatabase();
                db.OpenDatabase(databasePath);
                var header = db.DBHeader;
                db.CloseDatabase();
                return header;
            }
            catch
            {
            }
        }

        return new Core.DataHeader();
    }

    private Core.DataHeader BuildResetDatabaseHeader(EmbeddedGameConfig config, Core.DataHeader sourceHeader)
    {
        string loginScript = string.IsNullOrWhiteSpace(config.LoginScript)
            ? (string.IsNullOrWhiteSpace(sourceHeader.LoginScript) ? "0_Login.cts" : sourceHeader.LoginScript)
            : config.LoginScript;

        char gameLetter = !string.IsNullOrWhiteSpace(config.GameLetter)
            ? char.ToUpperInvariant(config.GameLetter[0])
            : sourceHeader.Game;

        char commandChar = config.CommandChar == '\0'
            ? (sourceHeader.CommandChar == '\0' ? '$' : sourceHeader.CommandChar)
            : config.CommandChar;

        int sectorCount = config.Sectors > 0
            ? config.Sectors
            : (sourceHeader.Sectors > 0 ? sourceHeader.Sectors : (_state.Sectors > 0 ? _state.Sectors : 1000));

        int serverPort = config.Port > 0
            ? config.Port
            : (sourceHeader.ServerPort > 0 ? sourceHeader.ServerPort : _state.Port);

        int listenPort = config.ListenPort > 0
            ? config.ListenPort
            : (sourceHeader.ListenPort > 0 ? sourceHeader.ListenPort : 2300);

        return new Core.DataHeader
        {
            ProgramName = sourceHeader.ProgramName,
            Version = sourceHeader.Version == 0 ? (byte)Core.DatabaseConstants.DatabaseVersion : sourceHeader.Version,
            Sectors = sectorCount,
            Address = string.IsNullOrWhiteSpace(config.Host)
                ? (string.IsNullOrWhiteSpace(sourceHeader.Address) ? _state.Host : sourceHeader.Address)
                : config.Host,
            Description = sourceHeader.Description,
            ServerPort = (ushort)Math.Clamp(serverPort, 0, ushort.MaxValue),
            ListenPort = (ushort)Math.Clamp(listenPort, 0, ushort.MaxValue),
            LoginScript = loginScript,
            Password = config.Password ?? string.Empty,
            LoginName = config.LoginName ?? string.Empty,
            Game = gameLetter,
            IconFile = sourceHeader.IconFile,
            UseRLogin = config.UseRLogin,
            UseLogin = config.UseLogin,
            RobFactor = sourceHeader.RobFactor,
            StealFactor = sourceHeader.StealFactor,
            CommandChar = commandChar,
        };
    }

    private async Task ShowAboutAsync()
    {
        const double aboutImageSize = 330;

        var okBtn = new Button
        {
            Content = "OK",
            MinWidth = 110,
        };

        var aboutText = new TextBlock
        {
            Width = aboutImageSize,
            Text =
                "Mayhem Tradewars Client (MTC)\n" +
                "Version 1.0.0\n\n" +
                "Cross-platform Trade Wars 2002 client\n" +
                "built on TWXProxy Core.\n\n" +
                "Copyright (C) 2026 Matt Mosley\n" +
                "Licensed under GPL v2+",
            Foreground = FgKey,
            TextWrapping = TextWrapping.Wrap,
            TextAlignment = TextAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
        };

        var dlg = new Window
        {
            Title = "About MTC",
            SizeToContent = SizeToContent.WidthAndHeight,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
            Background = BgPanel,
            Content = new Border
            {
                Padding = new Thickness(18),
                Child = new StackPanel
                {
                    Width = aboutImageSize,
                    Spacing = 14,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Children =
                    {
                        new Image
                        {
                            Source = AboutLogo,
                            Width = aboutImageSize,
                            Height = aboutImageSize,
                            Stretch = Stretch.Uniform,
                            HorizontalAlignment = HorizontalAlignment.Center,
                        },
                        aboutText,
                        new StackPanel
                        {
                            Margin = new Thickness(0, 4, 0, 0),
                            HorizontalAlignment = HorizontalAlignment.Center,
                            Children = { okBtn },
                        },
                    },
                },
            },
        };

        okBtn.Click += (_, _) => dlg.Close();
        await dlg.ShowDialog(this);
    }

    private async Task OnPreferencesAsync()
    {
        bool saved = await new PreferencesDialog(_appPrefs).ShowDialog<bool>(this);
        if (!saved)
        {
            Dispatcher.UIThread.Post(FocusActiveTerminal, DispatcherPriority.Input);
            return;
        }

        AppPaths.SetConfiguredProgramDir(_appPrefs.ProgramDirectory);
        await ClearScriptDirectoryFromAllGameConfigsAsync();
        RefreshRuntimeScriptDirectoryFromPreferences();
        ApplyDebugLoggingPreferences();
        ApplyRedAlertPreference();
        RebuildScriptsMenu();
        Dispatcher.UIThread.Post(FocusActiveTerminal, DispatcherPriority.Input);
    }

    private async Task OnMacrosAsync()
    {
        if (_macroSettingsDialog != null)
        {
            if (_macroSettingsDialog.WindowState == WindowState.Minimized)
                _macroSettingsDialog.WindowState = WindowState.Normal;

            _macroSettingsDialog.Activate();
            return;
        }

        var dialog = new MacroSettingsDialog(
            _appPrefs.MacroBindings
                .Select(binding => new AppPreferences.MacroBinding
                {
                    Hotkey = binding.Hotkey,
                    Macro = binding.Macro,
                })
                .ToArray(),
            PlayConfiguredMacroBurstAsync);

        _macroSettingsDialog = dialog;
        UpdateTerminalLiveSelector();

        try
        {
            bool saved = await dialog.ShowDialog<bool>(this);
            if (!saved)
                return;

            _appPrefs.MacroBindings.Clear();
            foreach (AppPreferences.MacroBinding binding in dialog.Result)
            {
                _appPrefs.MacroBindings.Add(new AppPreferences.MacroBinding
                {
                    Hotkey = binding.Hotkey,
                    Macro = binding.Macro,
                });
            }

            _appPrefs.Save();
        }
        finally
        {
            if (ReferenceEquals(_macroSettingsDialog, dialog))
                _macroSettingsDialog = null;

            UpdateTerminalLiveSelector();
        }
    }

    private void ApplyDebugLoggingPreferences()
    {
        AppPaths.SetConfiguredProgramDir(_appPrefs.ProgramDirectory);
        string programDir = AppPaths.ProgramDir;
        Core.GlobalModules.ProgramDir = programDir;
        Core.GlobalModules.PreferPreparedVm = _appPrefs.PreparedVmEnabled;
        Core.GlobalModules.EnableVmMetrics = _appPrefs.VmMetricsEnabled;
        Core.GlobalModules.PreparedScriptCacheLimitBytes =
            Math.Max(1, _appPrefs.PreparedScriptCacheLimitKb) * 1024L;
        Core.GlobalModules.MombotHotkeyPrewarmLimitBytes =
            Math.Max(1, _appPrefs.MombotHotkeyPrewarmLimitKb) * 1024L;
        AppPaths.EnsureDebugLogDir();
        string debugGameName = GetDebugLogGameName();
        Core.GlobalModules.ConfigureDebugLogging(
            string.IsNullOrWhiteSpace(debugGameName)
                ? AppPaths.GetDebugLogPath()
                : AppPaths.GetDebugLogPathForGame(debugGameName),
            _appPrefs.DebugLoggingEnabled,
            _appPrefs.VerboseDebugLogging,
            _appPrefs.TriggerDebugLogging,
            _appPrefs.ScriptTraceDebugLogging,
            _appPrefs.AutoRecorderDebugLogging);
        Core.GlobalModules.ConfigureHaggleDebugLogging(
            AppPaths.GetPortHaggleDebugLogPath(),
            _appPrefs.DebugPortHaggleEnabled,
            AppPaths.GetPlanetHaggleDebugLogPath(),
            _appPrefs.DebugPlanetHaggleEnabled);
        _standaloneNativeHaggle.SetPortHaggleMode(ResolveGlobalPortHaggleMode());
        _standaloneNativeHaggle.SetPlanetHaggleMode(ResolveGlobalPlanetHaggleMode());
        RefreshSessionLogTarget();
        if (_gameInstance != null)
            _gameInstance.Logger.LogDirectory = AppPaths.GetDebugLogDir();
    }

    private void RequestStatusBarRefresh()
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(RequestStatusBarRefresh, DispatcherPriority.Background);
            return;
        }

        DispatcherTimer? statusRefreshTimer = _statusRefreshTimer;
        if (statusRefreshTimer == null)
        {
            RefreshStatusBar();
            return;
        }

        if (statusRefreshTimer.IsEnabled)
            return;

        statusRefreshTimer.Start();
    }

    private static int CountTransportLines(byte[] bytes)
    {
        int count = 0;
        for (int i = 0; i < bytes.Length; i++)
        {
            if (bytes[i] == 0x0D)
                count++;
        }

        return count;
    }
    private string GetDebugLogGameName()
    {
        if (_gameInstance != null && !string.IsNullOrWhiteSpace(_gameInstance.GameName))
            return NormalizeGameName(_gameInstance.GameName);

        if (!string.IsNullOrWhiteSpace(_embeddedGameName))
            return NormalizeGameName(_embeddedGameName);

        if (!string.IsNullOrWhiteSpace(_embeddedGameConfig?.Name))
            return NormalizeGameName(_embeddedGameConfig.Name);

        if (!string.IsNullOrWhiteSpace(_currentProfilePath) || !string.IsNullOrWhiteSpace(_state.GameName))
            return DeriveGameName();

        return string.Empty;
    }

    private void RefreshSessionLogTarget(string? scriptDirectory = null)
    {
        string programDir = AppPaths.ProgramDir;
        _sessionLog.ProgramDir = programDir;
        _sessionLog.LogDirectory = AppPaths.GetDebugLogDir();
        _sessionLog.SetLogIdentity(DeriveGameName());
        _sessionLog.ScriptLoggingScope = CurrentInterpreter;
    }

    private void ApplySessionLogSettings(EmbeddedGameConfig? gameConfig)
    {
        if (gameConfig == null)
            return;

        _sessionLog.LogEnabled = gameConfig.LogEnabled;
        _sessionLog.LogData = gameConfig.LogEnabled;
        _sessionLog.LogANSI = gameConfig.LogAnsi;
        _sessionLog.LogAnsiCompanion = gameConfig.LogAnsiCompanion;
        _sessionLog.BinaryLogs = gameConfig.LogBinary;
        _sessionLog.NotifyPlayCuts = gameConfig.NotifyPlayCuts;
        _sessionLog.MaxPlayDelay = gameConfig.MaxPlayDelay;
    }

    // ── Connection profile helpers ──────────────────────────────────────────

    /// <summary>Builds a <see cref="ConnectionProfile"/> from the current live state.</summary>
    private ConnectionProfile BuildProfileFromState() => new ConnectionProfile
    {
        Name            = DeriveGameName(),
        // Connection
        Server          = _state.Host,
        Port            = _state.Port,
        Protocol        = _state.Protocol,
        LocalTwxProxy   = _state.LocalTwxProxy,
        TwxProxyDbPath  = _state.TwxProxyDbPath,
        EmbeddedProxy   = _state.EmbeddedProxy,
        Sectors         = _state.Sectors,
        AutoReconnect   = _state.AutoReconnect,
        UseLogin        = _state.UseLogin,
        UseRLogin       = _state.UseRLogin,
        LoginScript     = string.IsNullOrWhiteSpace(_state.LoginScript) ? "0_Login.cts" : _state.LoginScript,
        LoginName       = _state.LoginName,
        Password        = _state.Password,
        GameLetter      = _state.GameLetter,
        LoginSettingsConfigured = _state.EmbeddedProxy,
        ScrollbackLines = _buffer.ScrollbackLines,
        // Trader
        TraderName      = _state.TraderName,
        Sector          = _state.Sector,
        Turns           = _state.Turns,
        Experience      = _state.Experience,
        Alignment       = _state.Alignment,
        Credits         = _state.Credits,
        Corp            = _state.Corp,
        // Ship
        ShipName        = _state.ShipName,
        HoldsTotal      = _state.HoldsTotal,
        FuelOre         = _state.FuelOre,
        Organics        = _state.Organics,
        Equipment       = _state.Equipment,
        Colonists       = _state.Colonists,
        HoldsEmpty      = _state.HoldsEmpty,
        Fighters        = _state.Fighters,
        Shields         = _state.Shields,
        TurnsPerWarp    = _state.TurnsPerWarp,
        // Combat
        Etheral         = _state.Etheral,
        Beacon          = _state.Beacon,
        Disruptor       = _state.Disruptor,
        Photon          = _state.Photon,
        Armor           = _state.Armor,
        Limpet          = _state.Limpet,
        Genesis         = _state.Genesis,
        Atomic          = _state.Atomic,
        Corbomite       = _state.Corbomite,
        Cloak           = _state.Cloak,
        HasTranswarpDrive1 = _state.HasTranswarpDrive1,
        HasTranswarpDrive2 = _state.HasTranswarpDrive2,
        TranswarpDrive1 = _state.TranswarpDrive1,
        TranswarpDrive2 = _state.TranswarpDrive2,
        ScannerD        = _state.ScannerD,
        ScannerH        = _state.ScannerH,
        ScannerP        = _state.ScannerP,
    };

    private ConnectionProfile BuildProfileFromConfig(EmbeddedGameConfig config)
    {
        EmbeddedMtcConfig mtc = config.Mtc ?? new EmbeddedMtcConfig();
        EmbeddedMtcState state = mtc.State ?? new EmbeddedMtcState();
        return new ConnectionProfile
        {
            Name = string.IsNullOrWhiteSpace(config.Name) ? DeriveGameName() : config.Name,
            Server = config.Host,
            Port = config.Port,
            Protocol = Enum.TryParse<TwProtocol>(mtc.Protocol, true, out TwProtocol protocol)
                ? protocol
                : TwProtocol.Telnet,
            LocalTwxProxy = mtc.LocalTwxProxy,
            TwxProxyDbPath = mtc.TwxProxyDbPath,
            EmbeddedProxy = mtc.EmbeddedProxy,
            Sectors = config.Sectors,
            AutoReconnect = config.AutoReconnect,
            UseLogin = config.UseLogin,
            UseRLogin = config.UseRLogin,
            LoginScript = string.IsNullOrWhiteSpace(config.LoginScript) ? "0_Login.cts" : config.LoginScript,
            LoginName = config.LoginName,
            Password = config.Password,
            GameLetter = config.GameLetter,
            LoginSettingsConfigured = mtc.EmbeddedProxy,
            ScrollbackLines = mtc.ScrollbackLines <= 0 ? 2000 : mtc.ScrollbackLines,
            TraderName = state.TraderName,
            Sector = state.Sector,
            Turns = state.Turns,
            Experience = state.Experience,
            Alignment = string.IsNullOrWhiteSpace(state.Alignment) ? "0" : state.Alignment,
            Credits = state.Credits,
            Corp = state.Corp,
            ShipName = string.IsNullOrWhiteSpace(state.ShipName) ? "-" : state.ShipName,
            HoldsTotal = state.HoldsTotal,
            FuelOre = state.FuelOre,
            Organics = state.Organics,
            Equipment = state.Equipment,
            Colonists = state.Colonists,
            HoldsEmpty = state.HoldsEmpty,
            Fighters = state.Fighters,
            Shields = state.Shields,
            TurnsPerWarp = state.TurnsPerWarp,
            Etheral = state.Etheral,
            Beacon = state.Beacon,
            Disruptor = state.Disruptor,
            Photon = state.Photon,
            Armor = state.Armor,
            Limpet = state.Limpet,
            Genesis = state.Genesis,
            Atomic = state.Atomic,
            Corbomite = state.Corbomite,
            Cloak = state.Cloak,
            HasTranswarpDrive1 = state.HasTranswarpDrive1 || state.TranswarpDrive1 > 0,
            HasTranswarpDrive2 = state.HasTranswarpDrive2 || state.TranswarpDrive2 > 0,
            TranswarpDrive1 = state.TranswarpDrive1,
            TranswarpDrive2 = state.TranswarpDrive2,
            ScannerD = NormalizeDensityScanner(state.ScannerD, state.ScannerH),
            ScannerH = state.ScannerH,
            ScannerP = state.ScannerP,
        };
    }

    private EmbeddedMtcState BuildEmbeddedMtcState()
    {
        return new EmbeddedMtcState
        {
            TraderName = _state.TraderName,
            Sector = _state.Sector,
            Turns = _state.Turns,
            Experience = _state.Experience,
            Alignment = _state.Alignment,
            Credits = _state.Credits,
            Corp = _state.Corp,
            ShipName = _state.ShipName,
            HoldsTotal = _state.HoldsTotal,
            FuelOre = _state.FuelOre,
            Organics = _state.Organics,
            Equipment = _state.Equipment,
            Colonists = _state.Colonists,
            HoldsEmpty = _state.HoldsEmpty,
            Fighters = _state.Fighters,
            Shields = _state.Shields,
            TurnsPerWarp = _state.TurnsPerWarp,
            Etheral = _state.Etheral,
            Beacon = _state.Beacon,
            Disruptor = _state.Disruptor,
            Photon = _state.Photon,
            Armor = _state.Armor,
            Limpet = _state.Limpet,
            Genesis = _state.Genesis,
            Atomic = _state.Atomic,
            Corbomite = _state.Corbomite,
            Cloak = _state.Cloak,
            HasTranswarpDrive1 = _state.HasTranswarpDrive1,
            HasTranswarpDrive2 = _state.HasTranswarpDrive2,
            TranswarpDrive1 = _state.TranswarpDrive1,
            TranswarpDrive2 = _state.TranswarpDrive2,
            ScannerD = NormalizeDensityScanner(_state.ScannerD, _state.ScannerH),
            ScannerH = _state.ScannerH,
            ScannerP = _state.ScannerP,
        };
    }

    private string ResolveGlobalPortHaggleMode() => Core.NativeHaggleModes.Normalize(_appPrefs.PortHaggleMode);

    private string ResolveGlobalPlanetHaggleMode()
    {
        if (string.IsNullOrWhiteSpace(_appPrefs.PlanetHaggleMode))
            return Core.NativeHaggleModes.DefaultPlanet;

        string normalized = Core.NativeHaggleModes.Normalize(_appPrefs.PlanetHaggleMode);
        return string.IsNullOrWhiteSpace(normalized) ? Core.NativeHaggleModes.DefaultPlanet : normalized;
    }

    private static string NormalizeScriptDirectoryValue(string? scriptDirectory)
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

    private string GetConfiguredScriptsDirectoryValue()
        => NormalizeScriptDirectoryValue(_appPrefs.ScriptsDirectory);

    private string? ResolvePersistedGameScriptDirectory(string? existingGameScriptDirectory)
    {
        if (!string.IsNullOrWhiteSpace(GetConfiguredScriptsDirectoryValue()))
            return null;

        string normalizedExisting = NormalizeScriptDirectoryValue(existingGameScriptDirectory);
        return string.IsNullOrWhiteSpace(normalizedExisting) ? null : normalizedExisting;
    }

    private string ResolveEffectiveScriptDirectory(string? gameScriptDirectory = null)
    {
        string configuredScriptsDirectory = GetConfiguredScriptsDirectoryValue();
        if (!string.IsNullOrWhiteSpace(configuredScriptsDirectory))
            return configuredScriptsDirectory;

        string normalizedGameScriptDirectory = NormalizeScriptDirectoryValue(gameScriptDirectory);
        if (!string.IsNullOrWhiteSpace(normalizedGameScriptDirectory))
            return normalizedGameScriptDirectory;

        return NormalizeScriptDirectoryValue(
            OperatingSystem.IsWindows()
                ? Path.Combine(AppPaths.ProgramDir, "scripts")
                : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments));
    }

    private bool ClearGameConfigScriptDirectory(EmbeddedGameConfig config)
    {
        if (string.IsNullOrWhiteSpace(config.ScriptDirectory))
        {
            return false;
        }

        config.ScriptDirectory = null;
        return true;
    }

    private async Task ClearScriptDirectoryFromAllGameConfigsAsync()
    {
        if (string.IsNullOrWhiteSpace(GetConfiguredScriptsDirectoryValue()))
            return;

        if (_embeddedGameConfig != null)
            ClearGameConfigScriptDirectory(_embeddedGameConfig);

        AppPaths.EnsureTwxproxyGamesDir();
        foreach (string path in Directory.EnumerateFiles(AppPaths.TwxproxyGamesDir, "*.json"))
        {
            EmbeddedGameConfig? config = await TryLoadGameConfigAsync(path);
            if (config == null || !ClearGameConfigScriptDirectory(config))
                continue;

            await SaveEmbeddedGameConfigAsync(NormalizeGameName(config.Name), config);
        }
    }

    private void RefreshRuntimeScriptDirectoryFromPreferences()
    {
        Core.ModInterpreter? interpreter = CurrentInterpreter;
        if (interpreter != null)
        {
            interpreter.ScriptDirectory = ResolveEffectiveScriptDirectory(_embeddedGameConfig?.ScriptDirectory);
            interpreter.ProgramDir = GetEffectiveProxyProgramDir(interpreter.ScriptDirectory);
        }
    }

    private EmbeddedGameConfig BuildEmbeddedGameConfigFromState(string gameName, EmbeddedGameConfig? existing = null)
    {
        EmbeddedGameConfig config = existing ?? new EmbeddedGameConfig();
        config.Name = gameName;
        config.Host = _state.Host;
        config.Port = _state.Port;
        config.Sectors = _state.Sectors;
        config.DatabasePath = string.IsNullOrWhiteSpace(config.DatabasePath)
            ? DatabasePathForMode(gameName, _state.EmbeddedProxy)
            : config.DatabasePath;
        config.ScriptDirectory = ResolvePersistedGameScriptDirectory(config.ScriptDirectory);
        config.NativeHaggleMode = null;
        config.AutoReconnect = _state.AutoReconnect;
        config.Mtc ??= new EmbeddedMtcConfig();
        config.Mtc.Protocol = _state.Protocol.ToString();
        config.Mtc.LocalTwxProxy = _state.LocalTwxProxy;
        config.Mtc.TwxProxyDbPath = _state.TwxProxyDbPath;
        config.Mtc.EmbeddedProxy = _state.EmbeddedProxy;
        config.Mtc.ScrollbackLines = _buffer.ScrollbackLines;
        config.Mtc.State = BuildEmbeddedMtcState();
        config.Variables = NormalizeEmbeddedVariables(config.Variables);
        return config;
    }

    private void ApplyEmbeddedConnectionState(string gameName, EmbeddedGameConfig config)
    {
        _state.GameName = NormalizeGameName(string.IsNullOrWhiteSpace(config.Name) ? gameName : config.Name);
        _state.Host = config.Host;
        _state.Port = config.Port;
        _state.Sectors = config.Sectors;
        _state.AutoReconnect = config.AutoReconnect;
        _state.UseLogin = config.UseLogin;
        _state.UseRLogin = config.UseRLogin;
        _state.LoginScript = string.IsNullOrWhiteSpace(config.LoginScript) ? "0_Login.cts" : config.LoginScript;
        _state.LoginName = config.LoginName;
        _state.Password = config.Password;
        _state.GameLetter = string.IsNullOrWhiteSpace(config.GameLetter)
            ? string.Empty
            : config.GameLetter.Trim().Substring(0, 1).ToUpperInvariant();
        _state.EmbeddedProxy = config.Mtc?.EmbeddedProxy ?? _state.EmbeddedProxy;
        _state.LocalTwxProxy = config.Mtc?.LocalTwxProxy ?? _state.LocalTwxProxy;
        _state.TwxProxyDbPath = config.Mtc?.TwxProxyDbPath ?? _state.TwxProxyDbPath;
        _state.Protocol = Enum.TryParse<TwProtocol>(config.Mtc?.Protocol, true, out TwProtocol protocol)
            ? protocol
            : TwProtocol.Telnet;

        int scrollbackLines = config.Mtc?.ScrollbackLines ?? 0;
        if (scrollbackLines > 0)
            _buffer.ScrollbackLines = scrollbackLines;
    }

    private static EmbeddedMtcState BuildEmbeddedMtcState(ConnectionProfile profile)
    {
        return new EmbeddedMtcState
        {
            TraderName = profile.TraderName,
            Sector = profile.Sector,
            Turns = profile.Turns,
            Experience = profile.Experience,
            Alignment = profile.Alignment,
            Credits = profile.Credits,
            Corp = profile.Corp,
            ShipName = profile.ShipName,
            HoldsTotal = profile.HoldsTotal,
            FuelOre = profile.FuelOre,
            Organics = profile.Organics,
            Equipment = profile.Equipment,
            Colonists = profile.Colonists,
            HoldsEmpty = profile.HoldsEmpty,
            Fighters = profile.Fighters,
            Shields = profile.Shields,
            TurnsPerWarp = profile.TurnsPerWarp,
            Etheral = profile.Etheral,
            Beacon = profile.Beacon,
            Disruptor = profile.Disruptor,
            Photon = profile.Photon,
            Armor = profile.Armor,
            Limpet = profile.Limpet,
            Genesis = profile.Genesis,
            Atomic = profile.Atomic,
            Corbomite = profile.Corbomite,
            Cloak = profile.Cloak,
            HasTranswarpDrive1 = profile.HasTranswarpDrive1 || profile.TranswarpDrive1 > 0,
            HasTranswarpDrive2 = profile.HasTranswarpDrive2 || profile.TranswarpDrive2 > 0,
            TranswarpDrive1 = profile.TranswarpDrive1,
            TranswarpDrive2 = profile.TranswarpDrive2,
            ScannerD = NormalizeDensityScanner(profile.ScannerD, profile.ScannerH),
            ScannerH = profile.ScannerH,
            ScannerP = profile.ScannerP,
        };
    }

    private EmbeddedGameConfig BuildEmbeddedGameConfigFromProfile(
        ConnectionProfile profile,
        string databasePath,
        EmbeddedGameConfig? existing = null)
    {
        EmbeddedGameConfig config = existing ?? new EmbeddedGameConfig();
        config.Name = NormalizeGameName(profile.Name);
        config.Host = profile.Server;
        config.Port = profile.Port;
        config.Sectors = profile.Sectors;
        config.DatabasePath = databasePath;
        config.ScriptDirectory = ResolvePersistedGameScriptDirectory(config.ScriptDirectory);
        config.NativeHaggleMode = null;
        config.AutoReconnect = profile.AutoReconnect;
        config.UseLogin = profile.UseLogin;
        config.UseRLogin = profile.UseRLogin;
        config.LoginScript = string.IsNullOrWhiteSpace(profile.LoginScript) ? "0_Login.cts" : profile.LoginScript;
        config.LoginName = profile.LoginName;
        config.Password = profile.Password;
        config.GameLetter = profile.GameLetter;
        config.Mtc ??= new EmbeddedMtcConfig();
        config.Mtc.Protocol = profile.Protocol.ToString();
        config.Mtc.LocalTwxProxy = profile.LocalTwxProxy;
        config.Mtc.TwxProxyDbPath = profile.TwxProxyDbPath;
        config.Mtc.EmbeddedProxy = profile.EmbeddedProxy;
        config.Mtc.ScrollbackLines = profile.ScrollbackLines;
        config.Mtc.State = BuildEmbeddedMtcState(profile);
        config.Variables = NormalizeEmbeddedVariables(config.Variables);
        return config;
    }

    private static Dictionary<string, string> NormalizeEmbeddedVariables(IDictionary<string, string>? source)
    {
        var normalized = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (source == null)
            return normalized;

        foreach (KeyValuePair<string, string> entry in source)
            normalized[entry.Key] = entry.Value;

        return normalized;
    }

    private static string NormalizeGameName(string? value)
    {
        string name = string.Concat((value ?? string.Empty).Split(Path.GetInvalidFileNameChars())).Trim();
        return string.IsNullOrWhiteSpace(name) ? "game" : name;
    }

    private static string GameConfigPathForMode(string gameName, bool embeddedProxy)
        => embeddedProxy
            ? AppPaths.TwxproxyGameConfigFileFor(gameName)
            : AppPaths.MtcStandaloneGameConfigFileFor(gameName);

    private static string DatabasePathForMode(string gameName, bool embeddedProxy)
        => embeddedProxy
            ? AppPaths.TwxproxyDatabasePathForGame(gameName)
            : AppPaths.MtcStandaloneDatabasePathForGame(gameName);

    private static string GameConfigPathForConfig(EmbeddedGameConfig config)
        => GameConfigPathForMode(NormalizeGameName(config.Name), config.Mtc?.EmbeddedProxy ?? true);

    private bool GameNameConflicts(string gameName, bool embeddedProxy, string? currentConfigPath = null, string? currentDatabasePath = null)
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

    /// <summary>Applies a profile to GameState and the terminal buffer.</summary>
    private void ApplyProfile(ConnectionProfile p)
    {
        if (p.EmbeddedProxy && !HasExplicitEmbeddedLoginSettings(p))
        {
            var sharedConfig = TryLoadEmbeddedGameConfigForGame(GetEmbeddedGameName(p));
            if (sharedConfig != null)
            {
                p.UseLogin = sharedConfig.UseLogin;
                p.UseRLogin = sharedConfig.UseRLogin;
                p.LoginScript = sharedConfig.LoginScript;
                p.LoginName = sharedConfig.LoginName;
                p.Password = sharedConfig.Password;
                p.GameLetter = sharedConfig.GameLetter;
            }
        }

        if (_gameInstance == null &&
            !_telnet.IsConnected &&
            _embeddedGameConfig != null)
        {
            LoadOfflineCurrentGameVars(_embeddedGameConfig);
        }

        // Connection
        _state.GameName       = NormalizeGameName(p.Name);
        _state.Host           = p.Server;
        _state.Port           = p.Port;
        _state.Protocol       = p.Protocol;
        _state.LocalTwxProxy  = p.LocalTwxProxy;
        _state.TwxProxyDbPath = p.TwxProxyDbPath;
        _state.EmbeddedProxy   = p.EmbeddedProxy;
        _state.Sectors         = p.Sectors;
        _state.AutoReconnect   = p.AutoReconnect;
        _state.UseLogin        = p.UseLogin;
        _state.UseRLogin       = p.UseRLogin;
        _state.LoginScript     = string.IsNullOrWhiteSpace(p.LoginScript) ? "0_Login.cts" : p.LoginScript;
        _state.LoginName       = p.LoginName;
        _state.Password        = p.Password;
        _state.GameLetter      = string.IsNullOrWhiteSpace(p.GameLetter)
            ? string.Empty
            : p.GameLetter.Trim().Substring(0, 1).ToUpperInvariant();
        _buffer.ScrollbackLines = p.ScrollbackLines;
        // Trader
        _state.TraderName     = p.TraderName;
        _state.Sector         = p.Sector;
        _state.Turns          = p.Turns;
        _state.Experience     = p.Experience;
        _state.Alignment      = p.Alignment;
        _state.Credits        = p.Credits;
        _state.Corp           = p.Corp;
        // Ship
        _state.ShipName       = string.IsNullOrEmpty(p.ShipName) ? "-" : p.ShipName;
        _currentShipType      = string.Empty;
        _currentShipClass     = string.Empty;
        _currentComputerShipType = string.Empty;
        _awaitingComputerShipTypeLine = false;
        ClearOnlinePlayers();
        _state.HoldsTotal     = p.HoldsTotal;
        _state.FuelOre        = p.FuelOre;
        _state.Organics       = p.Organics;
        _state.Equipment      = p.Equipment;
        _state.Colonists      = p.Colonists;
        _state.HoldsEmpty     = p.HoldsEmpty;
        _state.Fighters       = p.Fighters;
        _state.Shields        = p.Shields;
        _state.TurnsPerWarp   = p.TurnsPerWarp;
        // Combat
        _state.Etheral        = p.Etheral;
        _state.Beacon         = p.Beacon;
        _state.Disruptor      = p.Disruptor;
        _state.Photon         = p.Photon;
        _state.Armor          = p.Armor;
        _state.Limpet         = p.Limpet;
        _state.Genesis        = p.Genesis;
        _state.Atomic         = p.Atomic;
        _state.Corbomite      = p.Corbomite;
        _state.Cloak          = p.Cloak;
        _state.HasTranswarpDrive1 = p.HasTranswarpDrive1 || p.TranswarpDrive1 > 0;
        _state.HasTranswarpDrive2 = p.HasTranswarpDrive2 || p.TranswarpDrive2 > 0;
        _state.TranswarpDrive1 = p.TranswarpDrive1;
        _state.TranswarpDrive2 = p.TranswarpDrive2;
        _state.ScannerD       = NormalizeDensityScanner(p.ScannerD, p.ScannerH);
        _state.ScannerH       = p.ScannerH;
        _state.ScannerP       = p.ScannerP;
        SyncMombotRuntimeConfigFromTwxpCfg();
        _mombot.ApplyConfig(_embeddedGameConfig != null ? GetOrCreateEmbeddedMombotConfig(_embeddedGameConfig) : null);
        UpdateWindowTitle();
        RefreshStatusBar();
        _state.NotifyChanged();
    }

    private static void LoadOfflineCurrentGameVars(EmbeddedGameConfig config)
    {
        config.Variables = NormalizeEmbeddedVariables(config.Variables);
        NormalizeEmbeddedMombotConfig(config);

        var varsToLoad = new Dictionary<string, string>(config.Variables, StringComparer.OrdinalIgnoreCase);
        varsToLoad.Remove("$gfile_chk");
        varsToLoad.Remove("$doRelog");
        ApplySessionStartupVarDefaults(varsToLoad);

        Core.ScriptRef.ClearCurrentGameVars();
        Core.ScriptRef.LoadVarsForGame(varsToLoad);
    }

    private static void ApplySessionStartupVarDefaults(IDictionary<string, string> vars)
    {
        vars["$BOT~REDALERT"] = "FALSE";
        vars["$BOT~redalert"] = "FALSE";
        vars["$bot~redalert"] = "FALSE";
        vars["$redalert"] = "FALSE";
    }

    private static EmbeddedGameConfig NormalizeEmbeddedMombotConfig(EmbeddedGameConfig config)
    {
        _ = GetOrCreateEmbeddedMombotConfig(config);
        return config;
    }

    // Native mombot enablement is live runtime state. Persist these flags as disabled so
    // a crash or forced shutdown cannot poison the next startup.
    private static EmbeddedGameConfig BuildPersistedEmbeddedGameConfig(EmbeddedGameConfig source)
    {
        string snapshotJson = System.Text.Json.JsonSerializer.Serialize(source, _jsonOpts);
        EmbeddedGameConfig persisted =
            System.Text.Json.JsonSerializer.Deserialize<EmbeddedGameConfig>(snapshotJson, _jsonOpts) ??
            new EmbeddedGameConfig();

        NormalizeEmbeddedMombotConfig(persisted);
        MTC.mombot.mombotConfig persistedMombot = GetOrCreateEmbeddedMombotConfig(persisted);
        persistedMombot.Enabled = false;
        persistedMombot.WatcherEnabled = false;
        persisted.Variables = NormalizeEmbeddedVariables(persisted.Variables);
        return persisted;
    }

    private static MTC.mombot.mombotConfig GetOrCreateEmbeddedMombotConfig(EmbeddedGameConfig config)
    {
        config.Mtc ??= new EmbeddedMtcConfig();
        config.Mtc.State ??= new EmbeddedMtcState();
        config.mombot ??= config.Mtc.mombot ?? new MTC.mombot.mombotConfig();
        config.Mtc.mombot = config.mombot;
        return config.mombot;
    }

    private void UpdateWindowTitle()
    {
        string? gameName = null;
        if (!string.IsNullOrWhiteSpace(_embeddedGameName))
        {
            gameName = _embeddedGameName;
        }
        else if (!string.IsNullOrWhiteSpace(_state.GameName))
        {
            gameName = NormalizeGameName(_state.GameName);
        }
        else if (!string.IsNullOrWhiteSpace(_currentProfilePath))
        {
            gameName = Path.GetFileNameWithoutExtension(_currentProfilePath);
        }

        Title = string.IsNullOrWhiteSpace(gameName)
            ? BaseWindowTitle
            : $"{BaseWindowTitle} [{gameName}]";
    }

    private static bool HasExplicitEmbeddedLoginSettings(ConnectionProfile profile)
    {
        return profile.LoginSettingsConfigured;
    }

    private string GetEmbeddedGameName(ConnectionProfile? profile = null)
    {
        if (!string.IsNullOrWhiteSpace(profile?.Name))
            return NormalizeGameName(profile.Name);
        if (!string.IsNullOrWhiteSpace(_state.GameName))
            return NormalizeGameName(_state.GameName);
        string gameName = !string.IsNullOrEmpty(_currentProfilePath)
            ? System.IO.Path.GetFileNameWithoutExtension(_currentProfilePath)
            : $"{(profile?.Server ?? _state.Host)}_{(profile?.Port ?? _state.Port)}";
        return NormalizeGameName(gameName);
    }

    private static EmbeddedGameConfig? TryLoadEmbeddedGameConfigForGame(string gameName)
    {
        try
        {
            string path = AppPaths.TwxproxyGameConfigFileFor(gameName);
            if (!File.Exists(path))
                return null;

            var json = File.ReadAllText(path);
            EmbeddedGameConfig? config = System.Text.Json.JsonSerializer.Deserialize<EmbeddedGameConfig>(json, _jsonOpts);
            if (config == null)
                return null;
            config.Name = string.IsNullOrWhiteSpace(config.Name) ? NormalizeGameName(gameName) : NormalizeGameName(config.Name);
            config.DatabasePath = string.IsNullOrWhiteSpace(config.DatabasePath)
                ? AppPaths.TwxproxyDatabasePathForGame(config.Name)
                : config.DatabasePath;
            config.Variables = NormalizeEmbeddedVariables(config.Variables);
            return NormalizeEmbeddedMombotConfig(config);
        }
        catch
        {
            return null;
        }
    }

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

        // Create GameInstance. listenPort=0: we never call StartAsync, so no TCP listener.
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
        var serverToTerm = new System.IO.Pipelines.Pipe();
        var termToServer = new System.IO.Pipelines.Pipe();

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
            var buf = new byte[4096];
            try
            {
                while (!cts.Token.IsCancellationRequested)
                {
                    int n = await termReader.ReadAsync(buf, 0, buf.Length, cts.Token).ConfigureAwait(false);
                    if (n == 0) break;
                    var chunk = buf[..n].ToArray();
                    if (!IsEmbeddedTerminalClientDeaf())
                        _sessionLog.RecordServerData(chunk);
                    Interlocked.Exchange(ref _mombotLastTerminalOutputUtcTicks, DateTime.UtcNow.Ticks);
                    byte[] displayChunk = FilterTerminalDisplayArtifacts(chunk, out bool rewrotePromptOverwrite);
                    EnqueueDisplayChunk(displayChunk, CountTransportLines(displayChunk), rewrotePromptOverwrite);
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
            Interlocked.Exchange(ref _mombotLastServerOutputUtcTicks, DateTime.UtcNow.Ticks);

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
                        interpreter.AutoTextEvent(scriptRemainder, false);
                        interpreter.TextEvent(scriptRemainder, false);
                        if (!string.IsNullOrWhiteSpace(strippedRemainder))
                        {
                            ObserveComputerShipTypeLine(strippedRemainder);
                            ObserveOnlinePlayersLine(strippedRemainder);
                            SyncMombotPromptStateFromLine(strippedRemainder, remainderAnsi);
                            _ = HandleEmbeddedKeepaliveWatchLineAsync(strippedRemainder);
                            _ = HandleNativeMombotWatchLineAsync(strippedRemainder);
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

                string lineRaw = bufferedAnsi[lastAnsiProcessedPos..ansiCrPos];
                string lineForScript = NormalizeLegacyInterrogLineForScripts(buffered[lastProcessedPos..crPos]);
                string lineStripped = Core.AnsiCodes.NormalizeTerminalText(lineForScript);

                if (!string.IsNullOrEmpty(lineStripped))
                {
                    gi.FeedShipStatusLine(lineStripped);
                    Core.GlobalModules.GlobalAutoRecorder.RecordLine(lineStripped, lineRaw);
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
                interpreter.TextLineEvent(lineForScript, false);
                interpreter.TextEvent(lineForScript, false);
                interpreter.ActivateTriggers();

                if (!string.IsNullOrWhiteSpace(lineStripped))
                {
                    SyncMombotPromptStateFromLine(lineStripped, lineRaw);
                    _ = HandleEmbeddedKeepaliveWatchLineAsync(lineStripped);
                    _ = HandleNativeMombotWatchLineAsync(lineStripped);
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
                serverAnsiLineBuf.Clear();
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
                SuppressNativeMombotRelogState(preserveDoNotResuscitate: true);

            // Fire 'Connection Lost' so scripts can re-register triggers, etc.
            interpreter.ProgramEvent("Connection Lost", "", false);
            Dispatcher.UIThread.Post(() =>
            {
                _state.Connected = false;
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

            string promptAnsi = _mombotLastObservedGamePromptAnsi;
            string promptPlain = _mombotLastObservedGamePromptPlain;
            int promptVersion = _mombotObservedGamePromptVersion;
            if (string.IsNullOrWhiteSpace(promptPlain))
                return;

            _ = RestoreCurrentGamePromptAfterMombotCommandAsync(
                Array.Empty<MTC.mombot.mombotDispatchResult>(),
                promptAnsi,
                promptPlain,
                promptVersion);
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
        AppPaths.EnsureSharedModulesDir();
        _moduleHost = await Core.ExpansionModuleHost.CreateAsync(new Core.ExpansionModuleHostOptions
        {
            HostTargets = Core.ExpansionHostTargets.Mtc,
            HostName = "MTC",
            GameName = gameName,
            ProgramDir = programDir,
            ScriptDirectory = effectiveScriptDir,
            ModuleDataRootDirectory = AppPaths.ModuleDataDir,
            ModuleDirectories = new[]
            {
                AppPaths.ModulesDir,
                Core.SharedPaths.LegacyModulesDir,
            },
            GameInstance = gi,
            Interpreter = interpreter,
            Database = _sessionDb,
        });

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
        var moduleHost = _moduleHost;
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
        _moduleHost = null;
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
        if (moduleHost != null)
        {
            TraceRuntimeStop($"[MTC.StopEmbedded] disposing module host");
            await moduleHost.DisposeAsync();
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

    private void ConfigureStatusModeSelector()
    {
        ConfigureStatusMacrosButton();
        ConfigureStatusMapButton();
        ConfigureStatusStopAllButton();
        ConfigureStatusCommButton();
        ConfigureStatusBotButton();
        ConfigureStatusHaggleButton();
        ConfigureStatusToggleButton();
        ConfigureStatusRedAlertButton();

        _statusMacrosFrame.Padding = new Thickness(3, 2);
        _statusMacrosFrame.CornerRadius = new CornerRadius(8);
        _statusMacrosFrame.Child = _statusMacrosButton;

        _statusMapFrame.Padding = new Thickness(3, 2);
        _statusMapFrame.CornerRadius = new CornerRadius(8);
        _statusMapFrame.Child = _statusMapButton;

        _statusStopAllFrame.Padding = new Thickness(3, 2);
        _statusStopAllFrame.CornerRadius = new CornerRadius(8);
        _statusStopAllFrame.Child = _statusStopAllButton;

        _statusCommFrame.Padding = new Thickness(3, 2);
        _statusCommFrame.CornerRadius = new CornerRadius(8);
        _statusCommFrame.Child = _statusCommButton;

        _statusBotFrame.Padding = new Thickness(3, 2);
        _statusBotFrame.CornerRadius = new CornerRadius(8);
        _statusBotFrame.Child = _statusBotButton;

        _statusHaggleFrame.Padding = new Thickness(3, 2);
        _statusHaggleFrame.CornerRadius = new CornerRadius(8);
        _statusHaggleFrame.Child = _statusHaggleButton;

        _statusLivePausedFrame.Padding = new Thickness(4, 2);
        _statusLivePausedFrame.CornerRadius = new CornerRadius(8);
        _statusLivePausedFrame.Child = _statusLivePausedButton;

        _statusRedAlertFrame.Padding = new Thickness(4, 2);
        _statusRedAlertFrame.CornerRadius = new CornerRadius(8);
        _statusRedAlertFrame.Child = _statusRedAlertButton;

        UpdateTerminalLiveSelector();
    }

    private void ConfigureStatusMacrosButton()
    {
        _statusMacrosButton.MinWidth = 0;
        _statusMacrosButton.Width = 28;
        _statusMacrosButton.Height = 20;
        _statusMacrosButton.Padding = new Thickness(2, 1);
        _statusMacrosButton.VerticalAlignment = VerticalAlignment.Center;
        _statusMacrosButton.HorizontalAlignment = HorizontalAlignment.Center;
        _statusMacrosButton.Content = BuildStatusMacrosIcon();
        ToolTip.SetTip(_statusMacrosButton, "Open macro settings");
        _statusMacrosButton.Click += (_, _) =>
        {
            _ = OnMacrosAsync();
            Dispatcher.UIThread.Post(FocusActiveTerminal, DispatcherPriority.Input);
        };
        _statusMacrosButton.PointerEntered += (_, _) =>
        {
            _statusMacrosHovered = true;
            UpdateTerminalLiveSelector();
        };
        _statusMacrosButton.PointerExited += (_, _) =>
        {
            _statusMacrosHovered = false;
            UpdateTerminalLiveSelector();
        };
    }

    private Control BuildStatusMacrosIcon()
    {
        _statusMacrosLineTop = BuildStatusMacrosLine(new Thickness(1, 2, 5, 0), 10);
        _statusMacrosLineMiddle = BuildStatusMacrosLine(new Thickness(1, 0, 5, 0), 12);
        _statusMacrosLineBottom = BuildStatusMacrosLine(new Thickness(1, 0, 5, 2), 8);
        _statusMacrosPlay = new Avalonia.Controls.Shapes.Path
        {
            Width = 5.5,
            Height = 6.5,
            Stretch = Stretch.Fill,
            Data = Geometry.Parse("M 0,0 L 5.5,3.25 L 0,6.5 Z"),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 1, 0),
            IsHitTestVisible = false,
        };

        return new Grid
        {
            Width = 18,
            Height = 16,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Children =
            {
                new StackPanel
                {
                    Spacing = 2,
                    HorizontalAlignment = HorizontalAlignment.Left,
                    VerticalAlignment = VerticalAlignment.Center,
                    Children =
                    {
                        _statusMacrosLineTop,
                        _statusMacrosLineMiddle,
                        _statusMacrosLineBottom,
                    },
                },
                _statusMacrosPlay,
            },
        };
    }

    private static Border BuildStatusMacrosLine(Thickness margin, double width)
    {
        return new Border
        {
            Width = width,
            Height = 2,
            CornerRadius = new CornerRadius(1),
            Margin = margin,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false,
        };
    }

    private void ConfigureStatusMapButton()
    {
        _statusMapButton.MinWidth = 0;
        _statusMapButton.Width = 28;
        _statusMapButton.Height = 20;
        _statusMapButton.Padding = new Thickness(2, 1);
        _statusMapButton.VerticalAlignment = VerticalAlignment.Center;
        _statusMapButton.HorizontalAlignment = HorizontalAlignment.Center;
        _statusMapButton.Content = BuildStatusMapIcon();
        ToolTip.SetTip(_statusMapButton, "Open map window");
        _statusMapButton.Click += (_, _) =>
        {
            OnViewMap();
            Dispatcher.UIThread.Post(FocusActiveTerminal, DispatcherPriority.Input);
        };
        _statusMapButton.PointerEntered += (_, _) =>
        {
            _statusMapHovered = true;
            UpdateTerminalLiveSelector();
        };
        _statusMapButton.PointerExited += (_, _) =>
        {
            _statusMapHovered = false;
            UpdateTerminalLiveSelector();
        };
    }

    private Control BuildStatusMapIcon()
    {
        _statusMapPanelLeft = new Border
        {
            Width = 4.5,
            Height = 12,
            CornerRadius = new CornerRadius(1.4, 0.8, 0.8, 1.4),
            BorderThickness = new Thickness(1),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
            RenderTransform = new SkewTransform(-8, 0),
            Margin = new Thickness(0, 0, 0, 0),
        };

        _statusMapPanelCenter = new Border
        {
            Width = 5,
            Height = 12,
            CornerRadius = new CornerRadius(0.8),
            BorderThickness = new Thickness(1),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };

        _statusMapPanelRight = new Border
        {
            Width = 4.5,
            Height = 12,
            CornerRadius = new CornerRadius(0.8, 1.4, 1.4, 0.8),
            BorderThickness = new Thickness(1),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            RenderTransform = new SkewTransform(8, 0),
            Margin = new Thickness(0, 0, 0, 0),
        };

        _statusMapRoute = new Avalonia.Controls.Shapes.Path
        {
            Width = 16,
            Height = 12,
            Stretch = Stretch.Fill,
            StrokeThickness = 1.1,
            Data = Geometry.Parse("M 2,9 L 6,5 L 10,7 L 14,3"),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false,
        };

        _statusMapNodeA = BuildStatusMapNode(new Thickness(1, 8, 0, 0), HorizontalAlignment.Left);
        _statusMapNodeB = BuildStatusMapNode(new Thickness(0, 4, 0, 0), HorizontalAlignment.Center);
        _statusMapNodeC = BuildStatusMapNode(new Thickness(0, 2, 1, 0), HorizontalAlignment.Right);

        return new Grid
        {
            Width = 18,
            Height = 16,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Children =
            {
                new Grid
                {
                    Width = 15,
                    Height = 12,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    Children =
                    {
                        _statusMapPanelLeft,
                        _statusMapPanelCenter,
                        _statusMapPanelRight,
                    },
                },
                _statusMapRoute,
                _statusMapNodeA,
                _statusMapNodeB,
                _statusMapNodeC,
            },
        };
    }

    private static Border BuildStatusMapNode(Thickness margin, HorizontalAlignment alignment)
    {
        return new Border
        {
            Width = 2.8,
            Height = 2.8,
            CornerRadius = new CornerRadius(1.4),
            HorizontalAlignment = alignment,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = margin,
            IsHitTestVisible = false,
        };
    }

    private void ConfigureStatusStopAllButton()
    {
        _statusStopAllButton.MinWidth = 0;
        _statusStopAllButton.Width = 28;
        _statusStopAllButton.Height = 20;
        _statusStopAllButton.Padding = new Thickness(2, 1);
        _statusStopAllButton.VerticalAlignment = VerticalAlignment.Center;
        _statusStopAllButton.HorizontalAlignment = HorizontalAlignment.Center;
        _statusStopAllButton.Content = BuildStatusStopAllIcon();
        ToolTip.SetTip(_statusStopAllButton, "Force stop active scripts and modes");
        _statusStopAllButton.Click += (_, _) =>
        {
            _ = OnProxyForceStopInterruptibleScriptsAsync();
            Dispatcher.UIThread.Post(FocusActiveTerminal, DispatcherPriority.Input);
        };
        _statusStopAllButton.PointerEntered += (_, _) =>
        {
            _statusStopAllHovered = true;
            RefreshStatusBar();
        };
        _statusStopAllButton.PointerExited += (_, _) =>
        {
            _statusStopAllHovered = false;
            RefreshStatusBar();
        };
    }

    private Control BuildStatusStopAllIcon()
    {
        _statusStopAllSign = new Avalonia.Controls.Shapes.Path
        {
            Width = 15,
            Height = 15,
            Stretch = Stretch.Fill,
            StrokeThickness = 1.1,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Data = Geometry.Parse("M 5,0 L 10,0 L 15,5 L 15,10 L 10,15 L 5,15 L 0,10 L 0,5 Z"),
        };

        _statusStopAllLabel = new TextBlock
        {
            Text = "STOP",
            FontSize = 4.8,
            FontWeight = FontWeight.Bold,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, -0.5, 0, 0),
        };

        return new Grid
        {
            Width = 18,
            Height = 18,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Children =
            {
                _statusStopAllSign,
                _statusStopAllLabel,
            },
        };
    }

    private void ConfigureStatusCommButton()
    {
        _statusCommButton.MinWidth = 0;
        _statusCommButton.Width = 28;
        _statusCommButton.Height = 20;
        _statusCommButton.Padding = new Thickness(2, 1);
        _statusCommButton.VerticalAlignment = VerticalAlignment.Center;
        _statusCommButton.HorizontalAlignment = HorizontalAlignment.Center;
        _statusCommButton.Content = BuildStatusCommIcon();
        ToolTip.SetTip(_statusCommButton, "Toggle Comm Window");
        _statusCommButton.Click += (_, _) =>
        {
            ToggleCommWindow();
            Dispatcher.UIThread.Post(FocusActiveTerminal, DispatcherPriority.Input);
        };
        _statusCommButton.PointerEntered += (_, _) =>
        {
            _statusCommHovered = true;
            UpdateTerminalLiveSelector();
        };
        _statusCommButton.PointerExited += (_, _) =>
        {
            _statusCommHovered = false;
            UpdateTerminalLiveSelector();
        };
    }

    private void ConfigureStatusBotButton()
    {
        _statusBotButton.MinWidth = 0;
        _statusBotButton.Width = 28;
        _statusBotButton.Height = 20;
        _statusBotButton.Padding = new Thickness(2, 1);
        _statusBotButton.VerticalAlignment = VerticalAlignment.Center;
        _statusBotButton.HorizontalAlignment = HorizontalAlignment.Center;
        _statusBotButton.Content = BuildStatusBotIcon();
        ToolTip.SetTip(_statusBotButton, "Start or stop native MomBot");
        _statusBotButton.Click += async (_, _) =>
        {
            await ToggleNativeMombotFromToolbarAsync();
            Dispatcher.UIThread.Post(FocusActiveTerminal, DispatcherPriority.Input);
        };
        _statusBotButton.PointerEntered += (_, _) =>
        {
            _statusBotHovered = true;
            UpdateTerminalLiveSelector();
        };
        _statusBotButton.PointerExited += (_, _) =>
        {
            _statusBotHovered = false;
            UpdateTerminalLiveSelector();
        };
    }

    private Control BuildStatusCommIcon()
    {
        _statusCommFlap = new Border
        {
            Width = 10,
            Height = 5,
            CornerRadius = new CornerRadius(4, 4, 2, 2),
            BorderThickness = new Thickness(1),
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, -1),
        };

        _statusCommIndicator = new Border
        {
            Width = 4,
            Height = 4,
            CornerRadius = new CornerRadius(2),
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 1),
        };

        _statusCommBody = new Border
        {
            Width = 16,
            Height = 11,
            CornerRadius = new CornerRadius(3),
            BorderThickness = new Thickness(1),
            Child = new StackPanel
            {
                Spacing = 1,
                Margin = new Thickness(2, 1, 2, 1),
                Children =
                {
                    _statusCommIndicator,
                    BuildStatusCommGrilleLine(9),
                    BuildStatusCommGrilleLine(9),
                    BuildStatusCommGrilleLine(8),
                },
            },
        };

        return new StackPanel
        {
            Spacing = 0,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Children =
            {
                _statusCommFlap,
                _statusCommBody,
            },
        };
    }

    private Control BuildStatusBotIcon()
    {
        _statusBotAntenna = new Border
        {
            Width = 1.5,
            Height = 3,
            CornerRadius = new CornerRadius(1),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 0, 0, -1),
        };

        _statusBotAntennaTip = new Border
        {
            Width = 3,
            Height = 3,
            CornerRadius = new CornerRadius(1.5),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Top,
        };

        _statusBotEyeLeft = new Border
        {
            Width = 2.4,
            Height = 2.4,
            CornerRadius = new CornerRadius(1.2),
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
        };

        _statusBotEyeRight = new Border
        {
            Width = 2.4,
            Height = 2.4,
            CornerRadius = new CornerRadius(1.2),
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
        };

        var eyes = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = GridLength.Auto },
                new ColumnDefinition { Width = new GridLength(3) },
                new ColumnDefinition { Width = GridLength.Auto },
            },
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Children =
            {
                _statusBotEyeLeft,
                _statusBotEyeRight,
            },
        };
        Grid.SetColumn(_statusBotEyeRight, 2);

        _statusBotHead = new Border
        {
            Width = 12,
            Height = 8,
            CornerRadius = new CornerRadius(2),
            BorderThickness = new Thickness(1),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Padding = new Thickness(2, 0),
            Child = eyes,
        };

        _statusBotBody = new Border
        {
            Width = 9,
            Height = 4,
            CornerRadius = new CornerRadius(1.5),
            BorderThickness = new Thickness(1),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 1, 0, 0),
        };

        return new StackPanel
        {
            Spacing = 0,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Children =
            {
                _statusBotAntennaTip,
                _statusBotAntenna,
                _statusBotHead,
                _statusBotBody,
            },
        };
    }

    private static Border BuildStatusCommGrilleLine(double width)
    {
        return new Border
        {
            Width = width,
            Height = 1,
            CornerRadius = new CornerRadius(1),
            Background = new SolidColorBrush(Color.Parse("#B7D5DF")),
            HorizontalAlignment = HorizontalAlignment.Center,
            Opacity = 0.9,
        };
    }

    private void ConfigureStatusHaggleButton()
    {
        _statusHaggleButton.MinWidth = 0;
        _statusHaggleButton.Width = 28;
        _statusHaggleButton.Height = 20;
        _statusHaggleButton.Padding = new Thickness(2, 1);
        _statusHaggleButton.VerticalAlignment = VerticalAlignment.Center;
        _statusHaggleButton.HorizontalAlignment = HorizontalAlignment.Center;
        _statusHaggleButton.Content = BuildStatusHaggleIcon();
        ToolTip.SetTip(_statusHaggleButton, "Toggle native haggle");
        _statusHaggleButton.Click += (_, _) =>
        {
            OnHaggleToggleRequested();
            Dispatcher.UIThread.Post(FocusActiveTerminal, DispatcherPriority.Input);
        };
        _statusHaggleButton.PointerEntered += (_, _) =>
        {
            _statusHaggleHovered = true;
            UpdateTerminalLiveSelector();
        };
        _statusHaggleButton.PointerExited += (_, _) =>
        {
            _statusHaggleHovered = false;
            UpdateTerminalLiveSelector();
        };
    }

    private Control BuildStatusHaggleIcon()
    {
        _statusHaggleSpark = new Border
        {
            Width = 3,
            Height = 3,
            CornerRadius = new CornerRadius(1.5),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 0, 0, 0),
        };

        _statusHaggleStem = new Border
        {
            Width = 1.5,
            Height = 8,
            CornerRadius = new CornerRadius(1),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 2, 0, 0),
        };

        _statusHaggleBeam = new Border
        {
            Width = 12,
            Height = 1.6,
            CornerRadius = new CornerRadius(1),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 4.5, 0, 0),
        };

        _statusHaggleLeftLink = new Border
        {
            Width = 1.2,
            Height = 3.6,
            CornerRadius = new CornerRadius(0.8),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(3.2, 5.7, 0, 0),
        };

        _statusHaggleRightLink = new Border
        {
            Width = 1.2,
            Height = 3.6,
            CornerRadius = new CornerRadius(0.8),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 5.7, 3.2, 0),
        };

        _statusHaggleLeftPan = new Border
        {
            Width = 5.6,
            Height = 2.6,
            CornerRadius = new CornerRadius(1.3),
            BorderThickness = new Thickness(1),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(1.1, 9.4, 0, 0),
        };

        _statusHaggleRightPan = new Border
        {
            Width = 5.6,
            Height = 2.6,
            CornerRadius = new CornerRadius(1.3),
            BorderThickness = new Thickness(1),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 9.4, 1.1, 0),
        };

        _statusHaggleBase = new Border
        {
            Width = 8,
            Height = 2.1,
            CornerRadius = new CornerRadius(1.1),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, 0, 1.2),
        };

        return new Grid
        {
            Width = 18,
            Height = 16,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Children =
            {
                _statusHaggleSpark,
                _statusHaggleStem,
                _statusHaggleBeam,
                _statusHaggleLeftLink,
                _statusHaggleRightLink,
                _statusHaggleLeftPan,
                _statusHaggleRightPan,
                _statusHaggleBase,
            },
        };
    }

    private void ConfigureStatusToggleButton()
    {
        _statusLivePausedButton.MinWidth = 56;
        _statusLivePausedButton.Height = 20;
        _statusLivePausedButton.Padding = new Thickness(4, 1);
        _statusLivePausedButton.FontSize = 11;
        _statusLivePausedButton.FontWeight = FontWeight.SemiBold;
        _statusLivePausedButton.VerticalAlignment = VerticalAlignment.Center;
        _statusLivePausedButton.Click += (_, _) =>
        {
            SetTerminalLivePaused(!_terminalLivePaused);
            Dispatcher.UIThread.Post(FocusActiveTerminal, DispatcherPriority.Input);
        };
        _statusLivePausedButton.PointerEntered += (_, _) =>
        {
            _statusLivePausedHovered = true;
            UpdateTerminalLiveSelector();
        };
        _statusLivePausedButton.PointerExited += (_, _) =>
        {
            _statusLivePausedHovered = false;
            UpdateTerminalLiveSelector();
        };
    }

    private void ConfigureStatusRedAlertButton()
    {
        _statusRedAlertButton.MinWidth = 84;
        _statusRedAlertButton.Height = 20;
        _statusRedAlertButton.Padding = new Thickness(6, 1);
        _statusRedAlertButton.FontSize = 10.5;
        _statusRedAlertButton.FontWeight = FontWeight.Bold;
        _statusRedAlertButton.VerticalAlignment = VerticalAlignment.Center;
        ToolTip.SetTip(_statusRedAlertButton, "Clear active red alert");
        _statusRedAlertButton.Click += (_, _) =>
        {
            if (_redAlertEnabled)
                ClearRedAlert();
            Dispatcher.UIThread.Post(FocusActiveTerminal, DispatcherPriority.Input);
        };
    }

    private void UpdateTerminalLiveSelector()
    {
        bool enabled = _gameInstance != null;
        bool remoteProxyScripts = CanUseRemoteProxyScripts();
        bool haggleAvailable = enabled || remoteProxyScripts || (!_state.EmbeddedProxy && _telnet.IsConnected);
        bool haggleSelected = enabled
            ? _gameInstance?.NativeHaggleEnabled == true
            : !remoteProxyScripts && _standaloneNativeHaggle.Enabled;
        BotRuntimeState botRuntime = GetBotRuntimeState();
        ApplyStatusToggleFrameStyle(_statusMacrosFrame, true);
        ApplyStatusToggleFrameStyle(_statusMapFrame, true);
        ApplyStatusToggleFrameStyle(_statusCommFrame, true);
        ApplyStatusToggleFrameStyle(_statusBotFrame, enabled);
        ApplyStatusToggleFrameStyle(_statusHaggleFrame, haggleAvailable);
        ApplyStatusToggleFrameStyle(_statusLivePausedFrame, enabled);
        ApplyStatusToggleFrameStyle(_statusRedAlertFrame, _appPrefs.EnableRedAlertMode);
        _statusRedAlertFrame.IsVisible = _appPrefs.EnableRedAlertMode && _redAlertEnabled;

        ApplyStatusMacrosButtonStyle(_statusMacrosButton, _macroSettingsDialog != null);
        ApplyStatusMapButtonStyle(_statusMapButton, _mapWindow != null);
        ApplyStatusCommButtonStyle(_statusCommButton, _commWindowVisible);
        ApplyStatusBotButtonStyle(_statusBotButton, selected: botRuntime.NativeRunning, enabled);
        ApplyStatusHaggleButtonStyle(_statusHaggleButton, selected: haggleSelected, haggleAvailable);
        ToolTip.SetTip(_statusHaggleButton,
            !haggleAvailable
                ? "Native haggle unavailable"
                : remoteProxyScripts
                    ? "Toggle native haggle in standalone proxy"
                : (haggleSelected ? "Disable native haggle" : "Enable native haggle"));
        _statusLivePausedButton.Content = enabled && _statusLivePausedHovered
            ? (_terminalLivePaused ? "RESUME" : "PAUSE")
            : (_terminalLivePaused ? "PAUSED" : "LIVE");
        ApplyStatusModeButtonStyle(_statusLivePausedButton, paused: _terminalLivePaused, enabled);
        ApplyStatusRedAlertButtonStyle(_statusRedAlertButton, _redAlertEnabled);
    }

    private void ApplyStatusToggleFrameStyle(Border frame, bool enabled)
    {
        frame.Background = HudFrame;
        frame.BorderBrush = HudInnerEdge;
        frame.BorderThickness = new Thickness(1);
        frame.Opacity = enabled ? 1.0 : 0.55;
    }

    private void ApplyStatusStopAllButtonStyle(Button button, bool enabled)
    {
        button.IsEnabled = enabled;
        button.Background = enabled
            ? (_statusStopAllHovered ? new SolidColorBrush(Color.Parse("#291617")) : HudFrame)
            : HudFrame;
        button.BorderBrush = enabled
            ? (_statusStopAllHovered ? new SolidColorBrush(Color.Parse("#8D3B3B")) : HudInnerEdge)
            : HudInnerEdge;
        button.BorderThickness = new Thickness(1);
        button.Foreground = Brushes.Transparent;

        if (_statusStopAllSign != null)
        {
            _statusStopAllSign.Fill = enabled
                ? (_statusStopAllHovered ? new SolidColorBrush(Color.Parse("#F04438")) : new SolidColorBrush(Color.Parse("#C81E1E")))
                : new SolidColorBrush(Color.Parse("#5D3030"));
            _statusStopAllSign.Stroke = enabled
                ? (_statusStopAllHovered ? new SolidColorBrush(Color.Parse("#FFE0DB")) : new SolidColorBrush(Color.Parse("#FFD0C7")))
                : HudInnerEdge;
        }

        if (_statusStopAllLabel != null)
        {
            _statusStopAllLabel.Foreground = enabled ? Brushes.White : HudMuted;
            _statusStopAllLabel.Opacity = enabled ? 1.0 : 0.72;
        }
    }

    private void ApplyStatusHaggleButtonStyle(Button button, bool selected, bool enabled)
    {
        button.IsEnabled = enabled;
        button.Background = selected
            ? new SolidColorBrush(Color.Parse("#F5C158"))
            : (_statusHaggleHovered ? HudHeaderAlt : HudFrame);
        button.BorderBrush = selected
            ? new SolidColorBrush(Color.Parse("#FFE19B"))
            : (_statusHaggleHovered ? HudAccent : HudInnerEdge);
        button.BorderThickness = new Thickness(1);
        button.Foreground = Brushes.Transparent;

        Color lineColor = selected
            ? Color.Parse("#6A4710")
            : (_statusHaggleHovered ? Color.Parse("#CFEAF3") : Color.Parse("#7F97A1"));
        Color panFillColor = selected
            ? Color.Parse("#FFF2C9")
            : (_statusHaggleHovered ? Color.Parse("#243845") : Color.Parse("#17242D"));
        Color panBorderColor = selected
            ? Color.Parse("#8A5F18")
            : (_statusHaggleHovered ? Color.Parse("#9FC0CB") : Color.Parse("#6B8590"));
        Color sparkColor = selected
            ? Color.Parse("#FFF9E7")
            : (_statusHaggleHovered ? Color.Parse("#F1D58A") : Color.Parse("#7F8E95"));
        Color baseColor = selected
            ? Color.Parse("#7E5617")
            : (_statusHaggleHovered ? Color.Parse("#A7D8E4") : Color.Parse("#6B818B"));

        if (_statusHaggleSpark != null)
            _statusHaggleSpark.Background = new SolidColorBrush(sparkColor);
        if (_statusHaggleBeam != null)
            _statusHaggleBeam.Background = new SolidColorBrush(lineColor);
        if (_statusHaggleStem != null)
            _statusHaggleStem.Background = new SolidColorBrush(lineColor);
        if (_statusHaggleLeftLink != null)
            _statusHaggleLeftLink.Background = new SolidColorBrush(lineColor);
        if (_statusHaggleRightLink != null)
            _statusHaggleRightLink.Background = new SolidColorBrush(lineColor);
        if (_statusHaggleBase != null)
            _statusHaggleBase.Background = new SolidColorBrush(baseColor);

        if (_statusHaggleLeftPan != null)
        {
            _statusHaggleLeftPan.Background = new SolidColorBrush(panFillColor);
            _statusHaggleLeftPan.BorderBrush = new SolidColorBrush(panBorderColor);
        }

        if (_statusHaggleRightPan != null)
        {
            _statusHaggleRightPan.Background = new SolidColorBrush(panFillColor);
            _statusHaggleRightPan.BorderBrush = new SolidColorBrush(panBorderColor);
        }
    }

    private void ApplyStatusMacrosButtonStyle(Button button, bool selected)
    {
        button.IsEnabled = true;
        button.Background = selected
            ? new SolidColorBrush(Color.Parse("#5CD5FF"))
            : (_statusMacrosHovered ? HudHeaderAlt : HudFrame);
        button.BorderBrush = selected
            ? HudAccentHot
            : (_statusMacrosHovered ? HudAccent : HudInnerEdge);
        button.BorderThickness = new Thickness(1);

        Color lineColor = selected
            ? Color.Parse("#E8FBFF")
            : (_statusMacrosHovered ? Color.Parse("#A7F1FF") : Color.Parse("#7CD0DE"));
        Color playColor = selected
            ? Color.Parse("#FFE28A")
            : (_statusMacrosHovered ? Color.Parse("#DDFBFF") : Color.Parse("#B7D5DF"));

        if (_statusMacrosLineTop != null)
            _statusMacrosLineTop.Background = new SolidColorBrush(lineColor);
        if (_statusMacrosLineMiddle != null)
            _statusMacrosLineMiddle.Background = new SolidColorBrush(lineColor);
        if (_statusMacrosLineBottom != null)
            _statusMacrosLineBottom.Background = new SolidColorBrush(lineColor);
        if (_statusMacrosPlay != null)
            _statusMacrosPlay.Fill = new SolidColorBrush(playColor);
    }

    private void ApplyStatusCommButtonStyle(Button button, bool selected)
    {
        button.IsEnabled = true;
        button.Background = selected
            ? new SolidColorBrush(Color.Parse("#5CD5FF"))
            : (_statusCommHovered ? HudHeaderAlt : HudFrame);
        button.BorderBrush = selected
            ? HudAccentHot
            : (_statusCommHovered ? HudAccent : HudInnerEdge);
        button.BorderThickness = new Thickness(1);

        if (_statusCommFlap != null)
        {
            _statusCommFlap.Background = new SolidColorBrush(selected
                ? Color.Parse("#F7E4A5")
                : (_statusCommHovered ? Color.Parse("#E4C57B") : Color.Parse("#BE9952")));
            _statusCommFlap.BorderBrush = new SolidColorBrush(selected
                ? Color.Parse("#FFF4D0")
                : Color.Parse("#7D6031"));
        }

        if (_statusCommBody != null)
        {
            _statusCommBody.Background = new SolidColorBrush(selected
                ? Color.Parse("#1B3A54")
                : (_statusCommHovered ? Color.Parse("#253644") : Color.Parse("#1A232C")));
            _statusCommBody.BorderBrush = new SolidColorBrush(selected
                ? Color.Parse("#8CE6FF")
                : Color.Parse("#6E8794"));
        }

        if (_statusCommIndicator != null)
        {
            _statusCommIndicator.Background = selected
                ? HudAccentOk
                : new SolidColorBrush(Color.Parse("#3A5360"));
        }
    }

    private void ApplyStatusMapButtonStyle(Button button, bool selected)
    {
        button.IsEnabled = true;
        button.Background = selected
            ? new SolidColorBrush(Color.Parse("#5CD5FF"))
            : (_statusMapHovered ? HudHeaderAlt : HudFrame);
        button.BorderBrush = selected
            ? HudAccentHot
            : (_statusMapHovered ? HudAccent : HudInnerEdge);
        button.BorderThickness = new Thickness(1);

        Color panelBorder = selected
            ? Color.Parse("#E8FBFF")
            : (_statusMapHovered ? Color.Parse("#9DC3CF") : Color.Parse("#7894A0"));
        Color leftFill = selected
            ? Color.Parse("#103D56")
            : (_statusMapHovered ? Color.Parse("#1A3240") : Color.Parse("#152733"));
        Color centerFill = selected
            ? Color.Parse("#12384E")
            : (_statusMapHovered ? Color.Parse("#18303C") : Color.Parse("#13242D"));
        Color rightFill = selected
            ? Color.Parse("#153246")
            : (_statusMapHovered ? Color.Parse("#1A2D38") : Color.Parse("#14222A"));
        Color routeColor = selected
            ? Color.Parse("#FFE28A")
            : (_statusMapHovered ? Color.Parse("#A7F1FF") : Color.Parse("#6CC7D7"));
        Color nodeColor = selected
            ? Color.Parse("#FFF5C5")
            : (_statusMapHovered ? Color.Parse("#DDFBFF") : Color.Parse("#9FD9E4"));

        if (_statusMapPanelLeft != null)
        {
            _statusMapPanelLeft.Background = new SolidColorBrush(leftFill);
            _statusMapPanelLeft.BorderBrush = new SolidColorBrush(panelBorder);
        }

        if (_statusMapPanelCenter != null)
        {
            _statusMapPanelCenter.Background = new SolidColorBrush(centerFill);
            _statusMapPanelCenter.BorderBrush = new SolidColorBrush(panelBorder);
        }

        if (_statusMapPanelRight != null)
        {
            _statusMapPanelRight.Background = new SolidColorBrush(rightFill);
            _statusMapPanelRight.BorderBrush = new SolidColorBrush(panelBorder);
        }

        if (_statusMapRoute != null)
            _statusMapRoute.Stroke = new SolidColorBrush(routeColor);

        if (_statusMapNodeA != null)
            _statusMapNodeA.Background = new SolidColorBrush(nodeColor);
        if (_statusMapNodeB != null)
            _statusMapNodeB.Background = new SolidColorBrush(nodeColor);
        if (_statusMapNodeC != null)
            _statusMapNodeC.Background = new SolidColorBrush(nodeColor);
    }

    private void ApplyStatusBotButtonStyle(Button button, bool selected, bool enabled)
    {
        button.IsEnabled = enabled;
        button.Background = selected
            ? new SolidColorBrush(Color.Parse("#1EF0AE"))
            : (_statusBotHovered ? HudHeaderAlt : HudFrame);
        button.BorderBrush = selected
            ? HudAccentHot
            : (_statusBotHovered ? HudAccent : HudInnerEdge);
        button.BorderThickness = new Thickness(1);

        Color shellColor = selected
            ? Color.Parse("#083327")
            : (_statusBotHovered ? Color.Parse("#A7D8E4") : Color.Parse("#89A2AC"));
        Color headFillColor = selected
            ? Color.Parse("#C8FFF0")
            : (_statusBotHovered ? Color.Parse("#173342") : Color.Parse("#112530"));
        Color eyeColor = selected
            ? Color.Parse("#0ACB86")
            : (_statusBotHovered ? Color.Parse("#7CEFFF") : Color.Parse("#4E7D89"));
        Color antennaTipColor = selected
            ? Color.Parse("#FFF1C2")
            : (_statusBotHovered ? Color.Parse("#DCE8ED") : Color.Parse("#8FA2AB"));

        if (_statusBotHead != null)
        {
            _statusBotHead.Background = new SolidColorBrush(headFillColor);
            _statusBotHead.BorderBrush = new SolidColorBrush(shellColor);
        }

        if (_statusBotBody != null)
        {
            _statusBotBody.Background = new SolidColorBrush(headFillColor);
            _statusBotBody.BorderBrush = new SolidColorBrush(shellColor);
        }

        if (_statusBotEyeLeft != null)
            _statusBotEyeLeft.Background = new SolidColorBrush(eyeColor);
        if (_statusBotEyeRight != null)
            _statusBotEyeRight.Background = new SolidColorBrush(eyeColor);
        if (_statusBotAntenna != null)
            _statusBotAntenna.Background = new SolidColorBrush(shellColor);
        if (_statusBotAntennaTip != null)
            _statusBotAntennaTip.Background = new SolidColorBrush(antennaTipColor);
    }

    private void ApplyStatusModeButtonStyle(Button button, bool paused, bool enabled)
    {
        button.IsEnabled = enabled;
        button.Background = paused ? HudAccentWarn : HudAccentOk;
        button.BorderBrush = paused ? HudAccentHot : HudAccent;
        button.BorderThickness = new Thickness(1);
        button.Foreground = HudAccentInk;
    }

    private void ApplyStatusRedAlertButtonStyle(Button button, bool enabled)
    {
        button.IsEnabled = enabled;
        button.Background = enabled
            ? new SolidColorBrush(Color.FromRgb(196, 28, 36))
            : new SolidColorBrush(Color.FromRgb(55, 61, 68));
        button.BorderBrush = enabled
            ? new SolidColorBrush(Color.FromRgb(255, 208, 208))
            : new SolidColorBrush(Color.FromRgb(103, 112, 122));
        button.BorderThickness = new Thickness(1);
        button.Foreground = enabled ? Brushes.White : new SolidColorBrush(Color.FromRgb(176, 184, 190));
        button.Content = "RED ALERT";
    }

    private static void SetRedAlertVars(string value)
        => PersistMombotVars(value, "$BOT~REDALERT", "$BOT~redalert", "$bot~redalert", "$redalert");

    private void ApplyRedAlertPreference()
    {
        if (_appPrefs.EnableRedAlertMode)
        {
            SyncRedAlertFromMombotVar();
            return;
        }

        if (IsMombotTruthy(ReadCurrentMombotVar("FALSE", "$BOT~REDALERT", "$BOT~redalert", "$bot~redalert", "$redalert")))
            SetRedAlertVars("FALSE");

        SetRedAlertEnabled(false);
    }

    private void SyncRedAlertFromMombotVar()
    {
        bool requested = IsMombotTruthy(ReadCurrentMombotVar("FALSE", "$BOT~REDALERT", "$BOT~redalert", "$bot~redalert", "$redalert"));
        if (!_appPrefs.EnableRedAlertMode)
        {
            if (requested)
                SetRedAlertVars("FALSE");

            SetRedAlertEnabled(false);
            return;
        }

        SetRedAlertEnabled(requested);
    }

    internal void TriggerRedAlert()
    {
        if (!_appPrefs.EnableRedAlertMode)
        {
            SetRedAlertVars("FALSE");
            SetRedAlertEnabled(false);
            return;
        }

        RestartRedAlertTimer();
        SetRedAlertVars("TRUE");
        SetRedAlertEnabled(true);
    }

    internal void ClearRedAlert()
    {
        _redAlertTimer.Stop();
        SetRedAlertVars("FALSE");
        SetRedAlertEnabled(false);
    }

    private void SetRedAlertEnabled(bool enabled)
    {
        bool effectiveEnabled = _appPrefs.EnableRedAlertMode && enabled;

        if (_redAlertEnabled == effectiveEnabled)
        {
            _statusRedAlertFrame.IsVisible = _appPrefs.EnableRedAlertMode && _redAlertEnabled;
            ApplyStatusRedAlertButtonStyle(_statusRedAlertButton, _appPrefs.EnableRedAlertMode && _redAlertEnabled);
            return;
        }

        _redAlertEnabled = effectiveEnabled;
        if (_redAlertEnabled)
            RestartRedAlertTimer();
        else
            _redAlertTimer.Stop();
        ApplyRedAlertPalette(_redAlertEnabled);
        Background = BgWindow;
        UpdateTerminalLiveSelector();
        RefreshStatusBar();
        RefreshInfoPanels();
        _buffer.Dirty = true;
        _termCtrl?.InvalidateVisual();
        _deckTermCtrl?.InvalidateVisual();
    }

    private void RestartRedAlertTimer()
    {
        _redAlertTimer.Stop();
        _redAlertTimer.Start();
    }

    private void SetTerminalLivePaused(bool paused)
    {
        if (_gameInstance == null)
        {
            _terminalLivePaused = paused;
            if (paused)
                ClearPendingTerminalOutputBacklog();
            else
                ClearPausedTerminalChunks();
            UpdateTerminalLiveSelector();
            return;
        }

        Core.ClientType targetType = paused ? Core.ClientType.Deaf : Core.ClientType.Standard;
        if (_gameInstance.GetClientType(EmbeddedLocalClientIndex) == targetType)
        {
            SyncEmbeddedTerminalClientType(targetType);
            return;
        }

        _gameInstance.SetClientType(EmbeddedLocalClientIndex, targetType);
    }

    private void ApplyEmbeddedTerminalOutputMode()
    {
        if (_gameInstance == null)
            return;

        _gameInstance.SetClientType(
            EmbeddedLocalClientIndex,
            _terminalLivePaused ? Core.ClientType.Deaf : Core.ClientType.Standard);
    }

    private bool IsEmbeddedTerminalClientDeaf()
    {
        return _gameInstance?.GetClientType(EmbeddedLocalClientIndex) == Core.ClientType.Deaf;
    }

    private void SyncEmbeddedTerminalClientType(Core.ClientType clientType)
    {
        _terminalLivePaused = clientType == Core.ClientType.Deaf;

        if (_terminalLivePaused)
            ClearPendingTerminalOutputBacklog();
        else
            ClearPausedTerminalChunks();

        UpdateTerminalLiveSelector();
    }

    private void ClearPendingTerminalOutputBacklog()
    {
        ClearPausedTerminalChunks();
        while (_pendingDisplayChunks.TryDequeue(out _))
        {
        }

        Interlocked.Exchange(ref _displayDrainScheduled, 0);
    }

    private void EnqueueDisplayChunk(byte[] chunk, int lineCount, bool rewrotePromptOverwrite)
    {
        if (chunk.Length == 0 && !rewrotePromptOverwrite)
            return;

        _pendingDisplayChunks.Enqueue(new PendingDisplayChunk(chunk, lineCount, rewrotePromptOverwrite));
        if (Interlocked.Exchange(ref _displayDrainScheduled, 1) != 0)
            return;

        Dispatcher.UIThread.Post(DrainPendingDisplayChunks, DispatcherPriority.Render);
    }

    private bool HasPendingTerminalDisplayBacklog()
    {
        if (!_pendingDisplayChunks.IsEmpty)
            return true;

        return Interlocked.CompareExchange(ref _displayDrainScheduled, 0, 0) != 0;
    }

    private void DrainPendingDisplayChunks()
    {
        bool replayed = false;
        bool rewrotePromptOverwrite = false;
        int processedChunks = 0;
        int processedBytes = 0;
        long startedAt = Stopwatch.GetTimestamp();

        const int maxChunksPerPass = 64;
        const int maxBytesPerPass = 64 * 1024;
        const double maxMillisecondsPerPass = 8.0;

        while (_pendingDisplayChunks.TryDequeue(out PendingDisplayChunk chunk))
        {
            if (chunk.Bytes.Length > 0)
            {
                _parser.Feed(chunk.Bytes, chunk.Bytes.Length);
                replayed = true;
                processedBytes += chunk.Bytes.Length;
            }

            rewrotePromptOverwrite |= chunk.RewrotePromptOverwrite;
            processedChunks++;

            if (!_pendingDisplayChunks.IsEmpty &&
                (processedChunks >= maxChunksPerPass ||
                 processedBytes >= maxBytesPerPass ||
                 Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds >= maxMillisecondsPerPass))
            {
                break;
            }
        }

        Interlocked.Exchange(ref _displayDrainScheduled, 0);

        if (!_pendingDisplayChunks.IsEmpty &&
            Interlocked.Exchange(ref _displayDrainScheduled, 1) == 0)
        {
            Dispatcher.UIThread.Post(DrainPendingDisplayChunks, DispatcherPriority.Render);
        }

        if (rewrotePromptOverwrite)
            ScheduleLatestObservedGamePromptRestoreAfterQuiet();

        if (replayed)
            _buffer.Dirty = true;
    }

    private void QueuePausedTerminalChunk(byte[] chunk)
    {
        byte[] filteredChunk = FilterTerminalDisplayArtifacts(chunk, out _);
        if (filteredChunk.Length == 0)
            return;

        var copy = new byte[filteredChunk.Length];
        Buffer.BlockCopy(filteredChunk, 0, copy, 0, filteredChunk.Length);

        lock (_pausedTerminalSync)
            _pausedTerminalChunks.Add(copy);
    }

    private void ClearPausedTerminalChunks()
    {
        lock (_pausedTerminalSync)
            _pausedTerminalChunks.Clear();
    }

    private void FlushPausedTerminalChunksToDisplay()
    {
        bool replayed = false;

        while (true)
        {
            List<byte[]> pending;
            lock (_pausedTerminalSync)
            {
                if (_pausedTerminalChunks.Count == 0)
                    break;

                pending = new List<byte[]>(_pausedTerminalChunks);
                _pausedTerminalChunks.Clear();
            }

            foreach (byte[] chunk in pending)
            {
                _parser.Feed(chunk, chunk.Length);
                replayed = true;
            }
        }

        if (!replayed)
            return;

        if (_mombotPromptOpen)
            RedrawMombotPrompt();
        _buffer.Dirty = true;
    }

    private async Task ConnectEmbeddedServerAsync()
    {
        if (_gameInstance != null && !_gameInstance.IsRunning)
        {
            await StopEmbeddedAsync();
            await DoConnectEmbeddedAsync();
        }

        if (_gameInstance == null || _gameInstance.IsConnected)
            return;

        try
        {
            await _gameInstance.SendToLocalAsync(System.Text.Encoding.ASCII.GetBytes("\r\nConnecting to server...\r\n"));
            await _gameInstance.ConnectToServerAsync();
            await _gameInstance.SendToLocalAsync(System.Text.Encoding.ASCII.GetBytes("Connected!\r\n"));
        }
        catch (Exception ex)
        {
            await _gameInstance.SendToLocalAsync(System.Text.Encoding.ASCII.GetBytes($"\r\nConnection failed: {ex.Message}\r\n"));
        }
    }

    /// <summary>
    /// Loads the shared TWXP game config JSON for <paramref name="gameName"/>.
    /// Creates and saves a new config (seeded from current state) if none exists yet.
    /// </summary>
    private async Task<EmbeddedGameConfig> LoadOrCreateEmbeddedGameConfigAsync(string gameName)
    {
        string path = AppPaths.TwxproxyGameConfigFileFor(gameName);
        if (File.Exists(path))
        {
            try
            {
                var json = await File.ReadAllTextAsync(path);
                var cfg  = System.Text.Json.JsonSerializer.Deserialize<EmbeddedGameConfig>(json, _jsonOpts);
                if (cfg != null)
                {
                    cfg.Name = string.IsNullOrWhiteSpace(cfg.Name) ? NormalizeGameName(gameName) : NormalizeGameName(cfg.Name);
                    cfg.DatabasePath = string.IsNullOrWhiteSpace(cfg.DatabasePath)
                        ? AppPaths.TwxproxyDatabasePathForGame(cfg.Name)
                        : cfg.DatabasePath;
                    return NormalizeEmbeddedMombotConfig(cfg);
                }
            }
            catch { }
        }

        // First run — seed from current profile.
        var newCfg = new EmbeddedGameConfig
        {
            Name    = gameName,
            Host    = _state.Host,
            Port    = _state.Port,
            Sectors = _state.Sectors,
            DatabasePath = AppPaths.TwxproxyDatabasePathForGame(gameName),
            ScriptDirectory = null,
            NativeHaggleEnabled = true,
            NativeHaggleMode = null,
            UseLogin = false,
            UseRLogin = false,
            LoginScript = "0_Login.cts",
            LoginName = string.Empty,
            Password = string.Empty,
            GameLetter = string.Empty,
            mombot = new MTC.mombot.mombotConfig(),
            Mtc = new EmbeddedMtcConfig
            {
                Protocol = _state.Protocol.ToString(),
                LocalTwxProxy = _state.LocalTwxProxy,
                TwxProxyDbPath = _state.TwxProxyDbPath,
                EmbeddedProxy = _state.EmbeddedProxy,
                ScrollbackLines = _buffer.ScrollbackLines,
                State = BuildEmbeddedMtcState(),
            },
        };
        NormalizeEmbeddedMombotConfig(newCfg);
        await SaveEmbeddedGameConfigAsync(gameName, newCfg);
        return newCfg;
    }

    /// <summary>Persists <paramref name="cfg"/> to the shared TWXP games directory.</summary>
    private static async Task SaveEmbeddedGameConfigAsync(string gameName, EmbeddedGameConfig cfg)
    {
        try
        {
            AppPaths.EnsureTwxproxyGamesDir();
            cfg.Name = string.IsNullOrWhiteSpace(cfg.Name) ? NormalizeGameName(gameName) : NormalizeGameName(cfg.Name);
            string path = GameConfigPathForConfig(cfg);
            EmbeddedGameConfig persisted = BuildPersistedEmbeddedGameConfig(cfg);
            var json = System.Text.Json.JsonSerializer.Serialize(persisted, _jsonOpts);
            await File.WriteAllTextAsync(path, json);
        }
        catch (Exception ex)
        {
            Core.GlobalModules.DebugLog(
                $"[MTC.StatusBarConfig] save failed for '{gameName}': {ex}\n");
            Core.GlobalModules.FlushDebugLog();
        }
    }

    private async Task SaveCurrentGameConfigAsync()
    {
        string gameName = DeriveGameName();
        if (string.IsNullOrWhiteSpace(gameName))
            return;

        EmbeddedGameConfig config = _embeddedGameConfig ?? (_state.EmbeddedProxy
            ? await LoadOrCreateEmbeddedGameConfigAsync(gameName)
            : BuildEmbeddedGameConfigFromState(gameName, new EmbeddedGameConfig
            {
                Name = gameName,
                Host = _state.Host,
                Port = _state.Port,
                Sectors = _state.Sectors,
                DatabasePath = AppPaths.MtcStandaloneDatabasePathForGame(gameName),
            }));
        config = BuildEmbeddedGameConfigFromState(gameName, config);
        if (string.IsNullOrWhiteSpace(config.DatabasePath))
            config.DatabasePath = DatabasePathForMode(gameName, _state.EmbeddedProxy);
        await SaveEmbeddedGameConfigAsync(gameName, config);
        _embeddedGameConfig = config;
        _embeddedGameName = gameName;
        _currentProfilePath ??= GameConfigPathForMode(gameName, _state.EmbeddedProxy);
        if (!string.IsNullOrWhiteSpace(_currentProfilePath))
            AddToRecentAndSave(_currentProfilePath);
    }

    private async Task OpenPathAsync(string path, bool addToRecent)
    {
        string extension = Path.GetExtension(path);
        if (extension.Equals(".json", StringComparison.OrdinalIgnoreCase))
        {
            EmbeddedGameConfig? config = await TryLoadGameConfigAsync(path);
            if (config == null)
            {
                await ShowMessageAsync("Load Error", $"Could not read game config:\n{path}");
                return;
            }

            string modeConfigPath = GameConfigPathForConfig(config);
            if (string.Equals(Path.GetFullPath(path), Path.GetFullPath(modeConfigPath), StringComparison.OrdinalIgnoreCase))
            {
                await ApplyLoadedGameConfigAsync(config, path, addToRecent);
                return;
            }

            ConnectionProfile importedProfile = BuildProfileFromConfig(config);
            ConnectionProfile? uniqueProfile = await EnsureUniqueProfileAsync(
                importedProfile,
                currentConfigPath: path,
                currentDatabasePath: config.DatabasePath);
            if (uniqueProfile == null)
                return;

            importedProfile = uniqueProfile;
            string gameName = importedProfile.Name;
            string importedDatabasePath = DatabasePathForMode(gameName, importedProfile.EmbeddedProxy);
            if (!string.IsNullOrWhiteSpace(config.DatabasePath) && File.Exists(config.DatabasePath))
            {
                if (!await ImportDatabaseIntoSharedStoreAsync(config.DatabasePath, gameName, importedProfile.EmbeddedProxy))
                    return;
            }

            EmbeddedGameConfig importedConfig = BuildEmbeddedGameConfigFromProfile(importedProfile, importedDatabasePath, config);
            importedConfig.Variables = NormalizeEmbeddedVariables(config.Variables);
            await SaveEmbeddedGameConfigAsync(gameName, importedConfig);
            await ApplyLoadedGameConfigAsync(importedConfig, GameConfigPathForMode(gameName, importedProfile.EmbeddedProxy), addToRecent);
            return;
        }

        if (extension.Equals(".xdb", StringComparison.OrdinalIgnoreCase))
        {
            await ImportDatabaseAsGameAsync(path, addToRecent);
            return;
        }

        if (extension.Equals(".mtc", StringComparison.OrdinalIgnoreCase))
        {
            ConnectionProfile legacy;
            try
            {
                legacy = ConnectionProfile.LoadXml(path);
            }
            catch (Exception ex)
            {
                await ShowMessageAsync("Load Error", ex.Message);
                return;
            }

            if (string.IsNullOrWhiteSpace(legacy.Name))
                legacy.Name = NormalizeGameName(Path.GetFileNameWithoutExtension(path));

            ConnectionProfile? uniqueLegacy = await EnsureUniqueProfileAsync(legacy);
            if (uniqueLegacy == null)
                return;

            legacy = uniqueLegacy;
            string gameName = legacy.Name;
            string sharedDbPath = DatabasePathForMode(gameName, legacy.EmbeddedProxy);
            if (!string.IsNullOrWhiteSpace(legacy.TwxProxyDbPath) && File.Exists(legacy.TwxProxyDbPath))
            {
                if (!await ImportDatabaseIntoSharedStoreAsync(legacy.TwxProxyDbPath, gameName, legacy.EmbeddedProxy))
                    return;
            }

            EmbeddedGameConfig config = BuildEmbeddedGameConfigFromProfile(legacy, sharedDbPath);
            await SaveEmbeddedGameConfigAsync(gameName, config);
            string configPath = GameConfigPathForMode(gameName, legacy.EmbeddedProxy);
            await ApplyLoadedGameConfigAsync(config, configPath, addToRecent);
            return;
        }

        await ShowMessageAsync("Unsupported File", $"MTC can open .json game configs, .xdb databases, or legacy .mtc files.\n\n{path}");
    }

    private async Task ApplyLoadedGameConfigAsync(EmbeddedGameConfig config, string configPath, bool addToRecent)
    {
        string targetGameName = NormalizeGameName(config.Name);
        bool switchingProfile =
            !string.IsNullOrWhiteSpace(_currentProfilePath) &&
            !PathsEqualSafe(_currentProfilePath, configPath);
        bool switchingEmbeddedGame =
            _gameInstance != null
                ? !string.Equals(_gameInstance.GameName, targetGameName, StringComparison.OrdinalIgnoreCase)
                : !string.IsNullOrWhiteSpace(_embeddedGameName) &&
                  !string.Equals(_embeddedGameName, targetGameName, StringComparison.OrdinalIgnoreCase);

        if (_gameInstance != null && (switchingProfile || switchingEmbeddedGame))
        {
            Core.GlobalModules.DebugLog(
                $"[MTC] Switching loaded game: stopping embedded runtime currentGame='{_gameInstance.GameName}' targetGame='{targetGameName}' currentProfile='{_currentProfilePath ?? "<none>"}' targetProfile='{configPath}'\n");
            await StopEmbeddedAsync();
        }

        if (switchingProfile || switchingEmbeddedGame)
            Core.ScriptRef.ClearCurrentGameVars();

        if (NormalizeEmbeddedRelogFlagsIfEstablished(config))
            await SaveEmbeddedGameConfigAsync(targetGameName, config);

        _currentProfilePath = configPath;
        _embeddedGameConfig = config;
        _embeddedGameName = targetGameName;
        ApplyProfile(BuildProfileFromConfig(config));
        ApplyDebugLoggingPreferences();
        if (addToRecent)
            AddToRecentAndSave(configPath);
        OnGameSelected();

        if (_state.EmbeddedProxy && _gameInstance == null)
        {
            await DoConnectEmbeddedAsync();
        }
        else
        {
            _parser.Feed($"\x1b[1;36m[Game loaded: {_state.Host}:{_state.Port}  —  use File \u25b6 Connect to connect]\x1b[0m\r\n");
            _buffer.Dirty = true;
        }
    }

    private static bool PathsEqualSafe(string? left, string? right)
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

    private async Task ImportDatabaseAsGameAsync(string databasePath, bool addToRecent)
    {
        ConnectionProfile draft = BuildProfileFromDatabase(databasePath);
        string defaultGameName = NormalizeGameName(draft.Name);
        string defaultSharedDatabasePath = DatabasePathForMode(defaultGameName, draft.EmbeddedProxy);
        string defaultConfigPath = GameConfigPathForMode(defaultGameName, draft.EmbeddedProxy);

        if (File.Exists(defaultConfigPath))
        {
            EmbeddedGameConfig? existingConfig = await TryLoadGameConfigAsync(defaultConfigPath);
            if (existingConfig != null)
            {
                string existingDbPath = string.IsNullOrWhiteSpace(existingConfig.DatabasePath)
                    ? defaultSharedDatabasePath
                    : existingConfig.DatabasePath;
                if (string.Equals(Path.GetFullPath(databasePath), Path.GetFullPath(existingDbPath), StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(Path.GetFullPath(databasePath), Path.GetFullPath(defaultSharedDatabasePath), StringComparison.OrdinalIgnoreCase))
                {
                    await ApplyLoadedGameConfigAsync(existingConfig, defaultConfigPath, addToRecent);
                    return;
                }
            }
        }

        var dialog = new NewConnectionDialog(draft);
        if (!await dialog.ShowDialog<bool>(this) || dialog.Result == null)
            return;

        ConnectionProfile? uniqueProfile = await EnsureUniqueProfileAsync(dialog.Result, currentDatabasePath: databasePath);
        if (uniqueProfile == null)
            return;

        ConnectionProfile imported = uniqueProfile;
        string gameName = imported.Name;
        if (!await ImportDatabaseIntoSharedStoreAsync(databasePath, gameName, imported.EmbeddedProxy))
            return;

        string sharedDbPath = DatabasePathForMode(gameName, imported.EmbeddedProxy);
        EmbeddedGameConfig config = BuildEmbeddedGameConfigFromProfile(imported, sharedDbPath);
        await SaveEmbeddedGameConfigAsync(gameName, config);
        string configPath = GameConfigPathForMode(gameName, imported.EmbeddedProxy);
        await ApplyLoadedGameConfigAsync(config, configPath, addToRecent);
    }

    private async Task<EmbeddedGameConfig?> TryLoadGameConfigAsync(string path)
    {
        try
        {
            string json = await File.ReadAllTextAsync(path);
            EmbeddedGameConfig? config = System.Text.Json.JsonSerializer.Deserialize<EmbeddedGameConfig>(json, _jsonOpts);
            if (config == null)
                return null;
            if (string.IsNullOrWhiteSpace(config.Name))
                config.Name = NormalizeGameName(Path.GetFileNameWithoutExtension(path));
            if (config.Sectors <= 0)
                config.Sectors = 1000;
            if (string.IsNullOrWhiteSpace(config.DatabasePath))
                config.DatabasePath = DatabasePathForMode(config.Name, config.Mtc?.EmbeddedProxy ?? true);
            config.Variables = NormalizeEmbeddedVariables(config.Variables);
            return config;
        }
        catch
        {
            return null;
        }
    }

    private ConnectionProfile BuildProfileFromDatabase(string databasePath)
    {
        string gameName = NormalizeGameName(Path.GetFileNameWithoutExtension(databasePath));
        var profile = new ConnectionProfile
        {
            Name = gameName,
            Server = _state.Host,
            Port = _state.Port,
            Protocol = TwProtocol.Telnet,
            EmbeddedProxy = true,
            LocalTwxProxy = true,
            TwxProxyDbPath = AppPaths.TwxproxyDatabasePathForGame(gameName),
            Sectors = 1000,
            ScrollbackLines = _buffer.ScrollbackLines,
            LoginScript = "0_Login.cts",
        };

        try
        {
            var database = new Core.ModDatabase();
            database.OpenDatabase(databasePath);
            Core.DataHeader header = database.DBHeader;
            profile.Server = string.IsNullOrWhiteSpace(header.Address) ? profile.Server : header.Address;
            profile.Port = header.ServerPort == 0 ? profile.Port : header.ServerPort;
            profile.Sectors = header.Sectors > 0 ? header.Sectors : profile.Sectors;
            profile.UseLogin = header.UseLogin;
            profile.UseRLogin = header.UseRLogin;
            profile.LoginScript = string.IsNullOrWhiteSpace(header.LoginScript) ? "0_Login.cts" : header.LoginScript;
            profile.LoginName = header.LoginName ?? string.Empty;
            profile.Password = header.Password ?? string.Empty;
            profile.GameLetter = header.Game == '\0' ? string.Empty : header.Game.ToString();
            database.CloseDatabase();
        }
        catch
        {
        }

        return profile;
    }

    private async Task<ConnectionProfile?> EnsureUniqueProfileAsync(ConnectionProfile profile, string? currentConfigPath = null, string? currentDatabasePath = null)
    {
        ConnectionProfile working = profile;
        while (true)
        {
            working.Name = NormalizeGameName(working.Name);
            if (!GameNameConflicts(working.Name, working.EmbeddedProxy, currentConfigPath, currentDatabasePath))
                return working;

            await ShowMessageAsync(
                "Game Name In Use",
                $"A game or database named '{working.Name}' already exists under the shared twxproxy folder.\n\nPlease choose a different game name.");

            var dialog = new NewConnectionDialog(working);
            if (!await dialog.ShowDialog<bool>(this) || dialog.Result == null)
                return null;
            working = dialog.Result;
        }
    }

    private async Task<bool> ImportDatabaseIntoSharedStoreAsync(string sourceDatabasePath, string targetGameName, bool embeddedProxy = true)
    {
        string targetPath = DatabasePathForMode(targetGameName, embeddedProxy);
        if (string.Equals(Path.GetFullPath(sourceDatabasePath), Path.GetFullPath(targetPath), StringComparison.OrdinalIgnoreCase))
            return true;

        try
        {
            AppPaths.EnsureTwxproxyDatabaseDir();
            Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
            File.Copy(sourceDatabasePath, targetPath, overwrite: false);
            return true;
        }
        catch (Exception ex)
        {
            await ShowMessageAsync("Database Import Error", ex.Message);
            return false;
        }
    }

    /// <summary>
    /// Opens or creates the sector database for the current connection.
    /// Named after the profile file (if saved) or the host:port string.
    /// Non-proxy mode uses MTC's local database store. Embedded-proxy mode uses the
    /// shared TWX Proxy database store so MTC and the proxy read/write the same .xdb.
    /// </summary>
    private void OpenSessionDatabase(string? gameName = null, int sectors = 0, bool useSharedProxyDatabase = false)
    {
        try
        {
            int databaseSectors = sectors > 0
                ? sectors
                : (_state.Sectors > 0 ? _state.Sectors : 1000);

            if (gameName == null)
            {
                gameName = !string.IsNullOrEmpty(_currentProfilePath)
                    ? Path.GetFileNameWithoutExtension(_currentProfilePath)
                    : $"{_state.Host}_{_state.Port}";

                // Strip chars unsafe in filenames
                gameName = string.Concat(gameName.Split(Path.GetInvalidFileNameChars()));
                if (string.IsNullOrWhiteSpace(gameName)) gameName = "game";
            }

            string dbPath;
            if (useSharedProxyDatabase)
            {
                AppPaths.EnsureTwxproxyDatabaseDir();
                dbPath = !string.IsNullOrWhiteSpace(_embeddedGameConfig?.DatabasePath)
                    ? _embeddedGameConfig!.DatabasePath
                    : AppPaths.TwxproxyDatabasePathForGame(gameName);

                string legacyMtcDbPath = AppPaths.LegacyDatabasePathForGame(gameName);
                if (!File.Exists(dbPath) && File.Exists(legacyMtcDbPath))
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);
                    File.Copy(legacyMtcDbPath, dbPath, overwrite: false);
                }
            }
            else
            {
                AppPaths.EnsureDirectories();
                dbPath = AppPaths.MtcStandaloneDatabasePathForGame(gameName);
            }

            string configPath = useSharedProxyDatabase
                ? AppPaths.TwxproxyGameConfigFileFor(gameName)
                : AppPaths.MtcStandaloneGameConfigFileFor(gameName);
            _gameFileLock?.Dispose();
            _gameFileLock = null;
            _gameFileLock = Core.GameFileLock.Acquire(
                useSharedProxyDatabase ? "MTC embedded proxy" : "MTC standalone client",
                configPath,
                dbPath);

            var db = new Core.ModDatabase();
            if (File.Exists(dbPath))
            {
                db.OpenDatabase(dbPath);
                db.UseCache = _embeddedGameConfig?.UseCache ?? true;
                var header = db.DBHeader;
                bool headerDirty = false;
                if (databaseSectors > 0)
                {
                    headerDirty |= header.Sectors != databaseSectors;
                    header.Sectors = databaseSectors;
                }
                string configLoginScript = string.IsNullOrWhiteSpace(_embeddedGameConfig?.LoginScript) ? "0_Login.cts" : _embeddedGameConfig.LoginScript;
                string configLoginName = _embeddedGameConfig?.LoginName ?? string.Empty;
                string configPassword = _embeddedGameConfig?.Password ?? string.Empty;
                char configGameChar = string.IsNullOrWhiteSpace(_embeddedGameConfig?.GameLetter) ? '\0' : char.ToUpperInvariant(_embeddedGameConfig.GameLetter[0]);
                headerDirty |= header.Address != _state.Host;
                header.Address = _state.Host;
                headerDirty |= header.ServerPort != (ushort)_state.Port;
                header.ServerPort = (ushort)_state.Port;
                headerDirty |= header.ListenPort != (ushort)(_embeddedGameConfig?.ListenPort ?? 2300);
                header.ListenPort = (ushort)(_embeddedGameConfig?.ListenPort ?? 2300);
                headerDirty |= header.CommandChar != (_embeddedGameConfig?.CommandChar ?? '$');
                header.CommandChar = _embeddedGameConfig?.CommandChar ?? '$';
                headerDirty |= header.UseLogin != (_embeddedGameConfig?.UseLogin ?? false);
                header.UseLogin = _embeddedGameConfig?.UseLogin ?? false;
                headerDirty |= header.UseRLogin != (_embeddedGameConfig?.UseRLogin ?? false);
                header.UseRLogin = _embeddedGameConfig?.UseRLogin ?? false;
                headerDirty |= header.LoginScript != configLoginScript;
                header.LoginScript = configLoginScript;
                headerDirty |= header.LoginName != configLoginName;
                header.LoginName = configLoginName;
                headerDirty |= header.Password != configPassword;
                header.Password = configPassword;
                headerDirty |= header.Game != configGameChar;
                header.Game = configGameChar;
                db.ReplaceHeader(header);
                if (headerDirty)
                    db.SaveDatabase();
            }
            else
            {
                db.CreateDatabase(dbPath, new Core.DataHeader
                {
                    Address    = _state.Host,
                    ServerPort = (ushort)_state.Port,
                    ListenPort = (ushort)(_embeddedGameConfig?.ListenPort ?? 2300),
                    CommandChar = _embeddedGameConfig?.CommandChar ?? '$',
                    Sectors    = databaseSectors,
                    UseLogin   = _embeddedGameConfig?.UseLogin ?? false,
                    UseRLogin  = _embeddedGameConfig?.UseRLogin ?? false,
                    LoginScript = string.IsNullOrWhiteSpace(_embeddedGameConfig?.LoginScript) ? "0_Login.cts" : _embeddedGameConfig.LoginScript,
                    LoginName  = _embeddedGameConfig?.LoginName ?? string.Empty,
                    Password   = _embeddedGameConfig?.Password ?? string.Empty,
                    Game       = string.IsNullOrWhiteSpace(_embeddedGameConfig?.GameLetter) ? '\0' : char.ToUpperInvariant(_embeddedGameConfig.GameLetter[0]),
                });
            }

            _sessionDb = db;
            Core.ScriptRef.SetActiveDatabase(db);
            QueueFinderPrewarm(db);

            Dispatcher.UIThread.Post(() =>
                _parser.Feed($"\x1b[1;36m[Database: {dbPath}]\x1b[0m\r\n"));
        }
        catch (Exception ex)
        {
            Dispatcher.UIThread.Post(() =>
                _parser.Feed($"\x1b[1;31m[DB open failed: {ex.Message}]\x1b[0m\r\n"));
        }
    }

    private Core.ModInterpreter? CurrentInterpreter => Core.GlobalModules.TWXInterpreter as Core.ModInterpreter;

    private bool CanUseRemoteProxyScripts()
    {
        return !_state.EmbeddedProxy &&
            _gameInstance == null &&
            _telnet.IsConnected &&
            _state.LocalTwxProxy &&
            IsLoopbackHost(_state.Host) &&
            IsConfiguredForSameProxyProgramDirectory();
    }

    private bool CanRunProxyScripts()
        => CurrentInterpreter != null || CanUseRemoteProxyScripts();

    private bool IsConfiguredForSameProxyProgramDirectory()
    {
        try
        {
            string mtcProgramDir = Path.GetFullPath(AppPaths.ProgramDir)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string proxyProgramDir = Path.GetFullPath(Core.SharedPathSettingsStore.Load().ProgramDirectory)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return string.Equals(mtcProgramDir, proxyProgramDir, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static bool IsLoopbackHost(string? host)
    {
        if (string.IsNullOrWhiteSpace(host))
            return false;

        string trimmed = host.Trim();
        if (trimmed.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Equals("::1", StringComparison.Ordinal) ||
            trimmed.Equals("[::1]", StringComparison.Ordinal) ||
            trimmed.StartsWith("127.", StringComparison.Ordinal))
            return true;

        return System.Net.IPAddress.TryParse(trimmed, out var address) &&
            System.Net.IPAddress.IsLoopback(address);
    }

    private void OpenNewWindowInNewProcess()
    {
        try
        {
            string[] args = Environment.GetCommandLineArgs().Skip(1).ToArray();

            if (UnixAutoDetach.TryLaunchAdditionalInstance(args, out string? unixLaunchError))
                return;

            string? processPath = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(processPath))
                processPath = Process.GetCurrentProcess().MainModule?.FileName;

            if (string.IsNullOrWhiteSpace(processPath))
            {
                _parser.Feed("\x1b[1;31m[Unable to open a new MTC window: current executable path is unavailable]\x1b[0m\r\n");
                return;
            }

            var startInfo = new ProcessStartInfo
            {
                FileName = processPath,
                WorkingDirectory = Environment.CurrentDirectory,
                UseShellExecute = false,
            };

            foreach (string arg in args)
                startInfo.ArgumentList.Add(arg);

            Process.Start(startInfo);
        }
        catch (Exception ex)
        {
            _parser.Feed($"\x1b[1;31m[Unable to open a new MTC window: {ex.Message}]\x1b[0m\r\n");
        }
    }

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

        var advancedSettings = new MenuItem { Header = "_Advanced Settings…", IsEnabled = true };
        advancedSettings.Click += (_, _) => _ = OnAdvancedProxySettingsAsync();
        items.Add(advancedSettings);

        return items;
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

        var startMenu = new MenuItem { Header = "_Start", IsEnabled = enabled };
        startMenu.ItemsSource = BuildBotStartMenuItems(enabled, LoadConfiguredBotSections());
        startMenu.SubmenuOpened += (_, _) =>
            startMenu.ItemsSource = BuildBotStartMenuItems(enabled, LoadConfiguredBotSections());
        items.Add(startMenu);

        var stopItem = new MenuItem { Header = "S_top", IsEnabled = runtime.IsRunning };
        stopItem.Click += (_, _) => _ = StopActiveBotAsync();
        items.Add(stopItem);

        var configureMenu = new MenuItem { Header = "_Configure" };
        configureMenu.ItemsSource = BuildBotConfigureMenuItems(LoadConfiguredBotSections());
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
        var nativeItem = new MenuItem
        {
            Header = runtime.NativeRunning ? $"{NativeMombotMenuLabel} (running)" : NativeMombotMenuLabel,
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

        bool preserveDoNotResuscitate = ShouldStopNativeMombotAfterDisconnect();
        TraceRuntimeStop($"[BotStop] external begin bot='{activeBotName}' lastLoaded='{lastLoadedModule}' preserveDnr={preserveDoNotResuscitate}");
        ClearMombotRelogState(preserveDoNotResuscitate);
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

    private void ClearMombotRelogState(bool preserveDoNotResuscitate = false)
    {
        Core.ScriptRef.SetCurrentGameVar("$doRelog", "0");
        Core.ScriptRef.SetCurrentGameVar("$BOT~DORELOG", "0");
        if (!preserveDoNotResuscitate)
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
        _mombotLastKeepaliveLine = string.Empty;
    }

    private void SuppressNativeMombotRelogState(bool preserveDoNotResuscitate)
    {
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

        string stopRequested = ReadCurrentMombotVar("0", "$BOT~DO_NOT_RESUSCITATE", "$bot~do_not_resuscitate", "$do_not_resuscitate");
        return string.Equals(stopRequested, "true", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(stopRequested, "1", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(stopRequested, "yes", StringComparison.OrdinalIgnoreCase);
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
                SuppressNativeMombotRelogState(preserveDoNotResuscitate: true);
                await StopNativeMombotAfterDisconnectAsync();
                return;
            }

            await Task.Delay(100);
        }

        if (!_mombot.Enabled || ShouldStopNativeMombotAfterDisconnect())
            return;

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
            bool needsRelogSetup = ShouldPromptForMombotRelogSettings(relogDefaults);

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
                StoredBotSection? refreshedNativeBot = LoadConfiguredBotSections().FirstOrDefault(section => section.IsNative);
                if (refreshedNativeBot != null)
                {
                    StopActiveExternalBot();
                    await StartInternalMombotAsync(
                        refreshedNativeBot.Config,
                        requestedBotName: string.Empty,
                        interactiveOfflinePrompt: false,
                        publishMissingGameMessage: true);
                }
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
        PrimeMombotBootstrapState(botConfig);
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
            if (interactiveOfflinePrompt && ShouldPromptForMombotRelogSettings(relogSettings))
            {
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
            SetMombotCurrentVars("1", "$relogging", "$connectivity~relogging");
            ApplyMombotConfigChange(config => config.Enabled = true);
            LoadMombotStartupScripts();
            bool launchedConnectivityRelog = false;
            if (relogSettings.LoginType != MTC.mombot.mombotRelogLoginType.NormalRelog)
            {
                var initialVars = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["$CONNECTIVITY~NEWGAME"] = relogSettings.LoginType == MTC.mombot.mombotRelogLoginType.NewGameAccountCreation ? "1" : "0",
                };

                if (_mombot.TryLoadInstalledScriptAtLabel(
                        "mombot.cts",
                        ":NATIVE~ENTER_NEW_GAME",
                        initialVars,
                        out string? connectivityScriptReference,
                        out string? connectivityError))
                {
                    launchedConnectivityRelog = true;
                    Core.GlobalModules.DebugLog(
                        $"[MTC.NativeBotStart] launched connectivity relog path script='{connectivityScriptReference}' loginType='{relogSettings.LoginType}'\n");
                    Core.GlobalModules.FlushDebugLog();
                }
                else
                {
                    Core.GlobalModules.DebugLog(
                        $"[MTC.NativeBotStart] connectivity relog path failed loginType='{relogSettings.LoginType}' error='{connectivityError}'\n");
                    Core.GlobalModules.FlushDebugLog();
                }
            }

            if (!launchedConnectivityRelog)
                await ExecuteMombotUiCommandAsync("relog");
        }

        FocusActiveTerminal();
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

        bool preserveDoNotResuscitate = ShouldStopNativeMombotAfterDisconnect();
        TraceRuntimeStop($"[BotStop] native begin root='{scriptRootPath}' lastLoaded='{lastLoadedModule}' preserveDnr={preserveDoNotResuscitate}");
        ClearMombotRelogState(preserveDoNotResuscitate);
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
        string botName = FirstMeaningfulMombotValue(
            Core.ScriptRef.GetCurrentGameVar("$BOT~BOT_NAME", string.Empty),
            Core.ScriptRef.GetCurrentGameVar("$SWITCHBOARD~BOT_NAME", string.Empty),
            Core.ScriptRef.GetCurrentGameVar("$bot_name", string.Empty),
            configuredNativeBotName,
            _mombot.Settings.BotName,
            "mombot");
        string serverName = FirstMeaningfulMombotValue(
            Core.ScriptRef.GetCurrentGameVar("$BOT~SERVERNAME", string.Empty),
            Core.ScriptRef.GetCurrentGameVar("$servername", string.Empty),
            configLogin);
        string loginName = FirstMeaningfulMombotValue(
            Core.ScriptRef.GetCurrentGameVar("$BOT~USERNAME", string.Empty),
            Core.ScriptRef.GetCurrentGameVar("$username", string.Empty),
            configLogin);
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

        bool newGameDay1 = string.Equals(Core.ScriptRef.GetCurrentGameVar("$BOT~NEWGAMEDAY1", "0"), "1", StringComparison.OrdinalIgnoreCase) ||
                           string.Equals(Core.ScriptRef.GetCurrentGameVar("$BOT~NEWGAMEDAY1", "false"), "true", StringComparison.OrdinalIgnoreCase);
        bool newGameOlder = string.Equals(Core.ScriptRef.GetCurrentGameVar("$BOT~NEWGAMEOLDER", "0"), "1", StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(Core.ScriptRef.GetCurrentGameVar("$BOT~NEWGAMEOLDER", "false"), "true", StringComparison.OrdinalIgnoreCase);

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

        MTC.mombot.mombotRelogLoginType loginType = missingRelogSetup
            ? MTC.mombot.mombotRelogLoginType.NewGameAccountCreation
            : newGameDay1
                ? MTC.mombot.mombotRelogLoginType.NewGameAccountCreation
                : newGameOlder
                    ? MTC.mombot.mombotRelogLoginType.NormalRelog
                    : MTC.mombot.mombotRelogLoginType.ReturnAfterDestroyed;

        return new MTC.mombot.mombotRelogDialogResult(
            loginType,
            botName,
            serverName,
            loginName,
            password,
            NormalizeGameLetter(gameLetter),
            delayMinutes,
            "nothing",
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

    private bool CanStartNativeMombotOfflineWithoutPrompt(
        MTC.mombot.mombotRelogDialogResult defaults,
        bool repairInvalidRelogState,
        out string reason)
    {
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
        if (_embeddedGameConfig != null)
        {
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
        PersistMombotVars(result.BotCommand, "$command_to_issue");
        PersistMombotVars(result.MacroAfterLogin, "$BOT~STARTMACRO", "$bot~startMacro", "$startMacro");
        PersistMombotVars("General", "$BOT~MODE", "$mode");
        PersistMombotVars(string.Empty, "$BOT~LAST_LOADED_MODULE", "$LAST_LOADED_MODULE");
        PersistMombotVars("1", "$BOT~DORELOG", "$doRelog");

        switch (result.LoginType)
        {
            case MTC.mombot.mombotRelogLoginType.NewGameAccountCreation:
                PersistMombotVars("1", "$BOT~NEWGAMEDAY1", "$newGameDay1");
                PersistMombotVars("0", "$BOT~NEWGAMEOLDER", "$newGameOlder");
                PersistMombotVars("0", "$BOT~ISSHIPDESTROYED");
                break;
            case MTC.mombot.mombotRelogLoginType.ReturnAfterDestroyed:
                PersistMombotVars("0", "$BOT~NEWGAMEDAY1", "$newGameDay1");
                PersistMombotVars("0", "$BOT~NEWGAMEOLDER", "$newGameOlder");
                PersistMombotVars("1", "$BOT~ISSHIPDESTROYED");
                break;
            default:
                PersistMombotVars("0", "$BOT~NEWGAMEDAY1", "$newGameDay1");
                PersistMombotVars("1", "$BOT~NEWGAMEOLDER", "$newGameOlder");
                PersistMombotVars("0", "$BOT~ISSHIPDESTROYED");
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
        string botName = FirstMeaningfulMombotValue(
            Core.ScriptRef.GetCurrentGameVar("$BOT~BOT_NAME", string.Empty),
            Core.ScriptRef.GetCurrentGameVar("$SWITCHBOARD~BOT_NAME", string.Empty),
            Core.ScriptRef.GetCurrentGameVar("$bot_name", string.Empty),
            configuredNativeBotName,
            _mombot.Settings.BotName,
            "mombot");
        string serverName = FirstMeaningfulMombotValue(
            Core.ScriptRef.GetCurrentGameVar("$BOT~SERVERNAME", string.Empty),
            Core.ScriptRef.GetCurrentGameVar("$servername", string.Empty),
            configLogin);
        string loginName = FirstMeaningfulMombotValue(
            Core.ScriptRef.GetCurrentGameVar("$BOT~USERNAME", string.Empty),
            Core.ScriptRef.GetCurrentGameVar("$username", string.Empty),
            configLogin);
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
        string isShipDestroyed = IsMombotTruthy(Core.ScriptRef.GetCurrentGameVar("$BOT~ISSHIPDESTROYED", "0")) ? "1" : "0";

        SetMombotCurrentVars(botName, "$BOT~BOT_NAME", "$SWITCHBOARD~BOT_NAME", "$bot_name");
        SetMombotCurrentVars(serverName, "$BOT~SERVERNAME", "$servername");
        SetMombotCurrentVars(loginName, "$BOT~USERNAME", "$username");
        SetMombotCurrentVars(password, "$BOT~PASSWORD", "$password");
        SetMombotCurrentVars(NormalizeGameLetter(gameLetter), "$BOT~LETTER", "$letter");
        SetMombotCurrentVars(doRelog, "$BOT~DORELOG", "$doRelog");
        SetMombotCurrentVars(newGameOlder, "$BOT~NEWGAMEOLDER", "$newGameOlder");
        SetMombotCurrentVars(newGameDay1, "$BOT~NEWGAMEDAY1", "$newGameDay1");
        SetMombotCurrentVars(isShipDestroyed, "$BOT~ISSHIPDESTROYED");
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

    private void PrimeMombotBootstrapState(Core.BotConfig botConfig)
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
        // Match script Mombot startup: a fresh bot start clears any stale
        // "do not resuscitate" state left behind by a prior logoff.
        SetMombotCurrentVars("0", "$BOT~DO_NOT_RESUSCITATE", "$bot~do_not_resuscitate", "$do_not_resuscitate");
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
        MirrorMombotCurrentVars("0", "$BOT~ISSHIPDESTROYED");
        MirrorMombotCurrentVars("0", "$relogging", "$connectivity~relogging");
        MirrorMombotCurrentVars(string.Empty, "$command_caller", "$BOT~COMMAND_CALLER", "$bot~command_caller");
        MirrorMombotCurrentVars("0", "$SWITCHBOARD~SELF_COMMAND", "$switchboard~self_command", "$BOT~SELF_COMMAND", "$bot~self_command", "$self_command");
        SetRedAlertVars("FALSE");
        PersistMombotVars(shipCapRelative, "$cap_file");
        PersistMombotVars(planetFileRelative, "$planet_file");

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

    private async Task OnProxyStopAllScriptsAsync(bool includeSystemScripts)
    {
        await Task.Yield();

        try
        {
            Core.ProxyGameOperations.StopAllScripts(CurrentInterpreter, includeSystemScripts);
        }
        catch (Exception ex)
        {
            await ShowMessageAsync("Stop Scripts Failed", ex.Message);
        }

        RebuildProxyMenu();
        FocusActiveTerminal();
    }

    private async Task OnProxyForceStopAllScriptsAsync(bool includeSystemScripts)
    {
        await Task.Yield();

        try
        {
            Core.ProxyGameOperations.ForceStopAllScripts(CurrentInterpreter, includeSystemScripts);
        }
        catch (Exception ex)
        {
            await ShowMessageAsync("STOPALL Failed", ex.Message);
        }

        RebuildProxyMenu();
        RefreshStatusBar();
        FocusActiveTerminal();
    }

    private async Task OnProxyForceStopInterruptibleScriptsAsync()
    {
        await Task.Yield();

        try
        {
            Core.ProxyGameOperations.ForceStopInterruptibleScripts(CurrentInterpreter);
        }
        catch (Exception ex)
        {
            await ShowMessageAsync("STOP Failed", ex.Message);
        }

        RebuildProxyMenu();
        RefreshStatusBar();
        FocusActiveTerminal();
    }

    private async Task OnProxyStopScriptAsync(int scriptId)
    {
        await Task.Yield();

        try
        {
            if (!Core.ProxyGameOperations.StopScriptById(CurrentInterpreter, scriptId))
                await ShowMessageAsync("Stop Script", $"Script ID {scriptId} was not found.");
        }
        catch (Exception ex)
        {
            await ShowMessageAsync("Stop Script Failed", ex.Message);
        }

        RebuildProxyMenu();
        FocusActiveTerminal();
    }

    private async Task OnRemoteProxyKillScriptByIdAsync()
    {
        string? scriptId = await ShowTextPromptAsync(
            "Kill Script",
            "Enter the script ID to kill in the standalone proxy.",
            string.Empty,
            "Kill");
        if (string.IsNullOrWhiteSpace(scriptId))
            return;

        SendProxyMenuCommand($"sk {scriptId.Trim()}");
        FocusActiveTerminal();
    }

    private async Task ExportWarpsAsync()
    {
        await Task.Yield();

        if (_sessionDb == null)
            return;

        string? path = await PickProxySavePathAsync("Export Warps", "warpspec", "txt", "Text file", "*.txt");
        if (path == null)
            return;

        try
        {
            Core.ProxyGameOperations.ExportWarps(_sessionDb, path);
            await ShowMessageAsync("Export Complete", $"Warp data exported to:\n{path}");
        }
        catch (Exception ex)
        {
            await ShowMessageAsync("Export Failed", ex.Message);
        }
        finally
        {
            RebuildProxyMenu();
            FocusActiveTerminal();
        }
    }

    private async Task ExportBubblesAsync()
    {
        await Task.Yield();

        if (_sessionDb == null)
            return;

        string? path = await PickProxySavePathAsync("Export Bubbles", "bubbles", "txt", "Text file", "*.txt");
        if (path == null)
            return;

        try
        {
            int bubbleSize = _embeddedGameConfig?.BubbleSize ?? Core.ModBubble.DefaultMaxBubbleSize;
            Core.ProxyGameOperations.ExportBubbles(_sessionDb, path, bubbleSize);
            await ShowMessageAsync("Export Complete", $"Bubble data exported to:\n{path}");
        }
        catch (Exception ex)
        {
            await ShowMessageAsync("Export Failed", ex.Message);
        }
        finally
        {
            RebuildProxyMenu();
            FocusActiveTerminal();
        }
    }

    private async Task ExportDeadendsAsync()
    {
        await Task.Yield();

        if (_sessionDb == null)
            return;

        string? path = await PickProxySavePathAsync("Export Deadends", "deadends", "txt", "Text file", "*.txt");
        if (path == null)
            return;

        try
        {
            Core.ProxyGameOperations.ExportDeadends(_sessionDb, path);
            await ShowMessageAsync("Export Complete", $"Deadends exported to:\n{path}");
        }
        catch (Exception ex)
        {
            await ShowMessageAsync("Export Failed", ex.Message);
        }
        finally
        {
            RebuildProxyMenu();
            FocusActiveTerminal();
        }
    }

    private async Task ExportTwxAsync()
    {
        await Task.Yield();

        if (_sessionDb == null)
            return;

        string suggested = string.IsNullOrWhiteSpace(_sessionDb.DatabaseName) ? "game" : _sessionDb.DatabaseName;
        string? path = await PickProxySavePathAsync("Export TWX", suggested, "twx", "TWX export", "*.twx");
        if (path == null)
            return;

        try
        {
            Core.ProxyGameOperations.ExportTwx(_sessionDb, path);
            await ShowMessageAsync("Export Complete", $"TWX export written to:\n{path}");
        }
        catch (Exception ex)
        {
            await ShowMessageAsync("Export Failed", ex.Message);
        }
        finally
        {
            RebuildProxyMenu();
            FocusActiveTerminal();
        }
    }

    private async Task ImportWarpsAsync()
    {
        await Task.Yield();

        if (_sessionDb == null)
            return;

        string? path = await PickProxyOpenPathAsync("Import Warps", "Text file", "*.txt");
        if (path == null)
            return;

        try
        {
            int imported = Core.ProxyGameOperations.ImportWarps(_sessionDb, path);
            await ShowMessageAsync("Import Complete", $"Imported {imported} warp rows.");
        }
        catch (Exception ex)
        {
            await ShowMessageAsync("Import Failed", ex.Message);
        }
        finally
        {
            RebuildProxyMenu();
            FocusActiveTerminal();
        }
    }

    private async Task ImportTwxAsync()
    {
        await Task.Yield();

        if (_sessionDb == null)
            return;

        string? path = await PickProxyOpenPathAsync("Import TWX", "TWX export", "*.twx");
        if (path == null)
            return;

        bool keepRecent = await ShowConfirmAsync(
            "Import TWX",
            "Keep existing data when it is newer than the imported TWX data?",
            "Keep Newer Data",
            "Overwrite");

        try
        {
            Core.TwxImportResult result = Core.ProxyGameOperations.ImportTwx(_sessionDb, path, keepRecent);
            string message = result.WasTruncated || result.SkippedInvalidWarps > 0
                ? $"Imported {result.ImportedSectorRecords} of {result.ExpectedSectorRecords} sector records."
                : "TWX import completed successfully.";

            if (result.WasTruncated)
                message += " The file ended before all header-declared sector records were present.";
            if (result.SkippedInvalidWarps > 0)
                message += $" Skipped {result.SkippedInvalidWarps} out-of-range warp entries.";

            await ShowMessageAsync("Import Complete", message);
        }
        catch (Exception ex)
        {
            await ShowMessageAsync("Import Failed", ex.Message);
        }
        finally
        {
            RebuildProxyMenu();
            FocusActiveTerminal();
        }
    }

    private async Task PlayCaptureAsync()
    {
        if (_gameInstance == null)
            return;

        string? path = await PickProxyOpenPathAsync("Play Capture File", "Binary capture files", "*.cap");
        if (path == null)
            return;

        bool started = _gameInstance.Logger.BeginPlayLog(path);
        await ShowMessageAsync(
            started ? "Playback Started" : "Playback Busy",
            started ? "Capture playback has started." : "A capture is already playing.");
    }

    private async Task ShowProxyHistoryAsync()
    {
        if (_gameInstance == null)
        {
            await ShowMessageAsync("Proxy History", "Proxy history is only available while the embedded proxy is running.");
            return;
        }

        var window = new HistoryWindow(
            $"History - {DeriveGameName()}",
            () => _gameInstance.History.GetSnapshot(),
            type => _gameInstance.History.Clear(type));
        await window.ShowDialog(this);
    }

    // ── Scripts menu ───────────────────────────────────────────────────────

    // Pure-data tree node — produced on the background thread, no Avalonia types.
    private sealed record ScriptNode(
        bool   IsDir,
        string Name,
        string RelPath,   // empty for dirs
        IReadOnlyList<ScriptNode> Children);

    /// <summary>
    /// Rebuilds the Scripts top-level menu from the configured scripts directory.
    /// Disk scanning runs on a background thread; MenuItem objects are created
    /// on the UI thread in the continuation.
    /// </summary>
    private void RebuildScriptsMenu()
    {
        bool canRunProxyScripts = CanRunProxyScripts();
        _scriptsMenu.IsEnabled = canRunProxyScripts;
        var reloadItem = new MenuItem { Header = "_Reload All Scripts" };
        reloadItem.Click += (_, _) => RebuildScriptsMenu();

        if (!canRunProxyScripts)
        {
            _scriptsMenu.ItemsSource = new List<object>
            {
                reloadItem, new Separator(),
                new MenuItem { Header = "Proxy scripts unavailable", IsEnabled = false },
            };
            RefreshNativeAppMenu();
            return;
        }

        var dir = _appPrefs.ScriptsDirectory;

        if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir))
        {
            var msg = !string.IsNullOrWhiteSpace(dir)
                ? "Scripts directory not found"
                : "No scripts directory configured";
            _scriptsMenu.ItemsSource = new List<object>
            {
                reloadItem, new Separator(),
                new MenuItem { Header = msg, IsEnabled = false },
            };
            RefreshNativeAppMenu();
            return;
        }

        // Show placeholder while scanning
        _scriptsMenu.ItemsSource = new List<object>
        {
            reloadItem, new Separator(),
            new MenuItem { Header = "Scanning…", IsEnabled = false },
        };

        var baseDir = dir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar, ' ');

        _ = Task.Run(() => ScanScriptNodes(baseDir, baseDir, depth: 0))
                .ContinueWith(t =>
                {
                    if (t.IsFaulted) return;

                    var items = new List<object> { reloadItem, new Separator() };
                    if (t.Result.Count == 0)
                        items.Add(new MenuItem { Header = "(no scripts found)", IsEnabled = false });
                    else
                        BuildMenuItems(items, t.Result);

                    _scriptsMenu.ItemsSource = items;
                    RefreshNativeAppMenu();
                }, TaskScheduler.FromCurrentSynchronizationContext());
    }

    /// <summary>
    /// Pure data scan — no Avalonia/UI objects created.  Safe on any thread.
    /// Directories are listed before files at every level.
    /// </summary>
    private static List<ScriptNode> ScanScriptNodes(string dir, string baseDir, int depth)
    {
        const int MaxDepth = 5;
        var nodes = new List<ScriptNode>();

        // ── Subdirectories first ───────────────────────────────────────
        if (depth < MaxDepth)
        {
            try
            {
                var subdirs = Directory
                    .EnumerateDirectories(dir)
                    .OrderBy(d => Path.GetFileName(d), StringComparer.OrdinalIgnoreCase);

                foreach (var sub in subdirs)
                {
                    if (!DirectoryHasScripts(sub, MaxDepth - depth - 1)) continue;
                    var children = ScanScriptNodes(sub, baseDir, depth + 1);
                    if (children.Count == 0) continue;
                    nodes.Add(new ScriptNode(
                        IsDir: true, Name: Path.GetFileName(sub),
                        RelPath: string.Empty, Children: children));
                }
            }
            catch { /* permission denied */ }
        }

        // ── Script files ───────────────────────────────────────────────
        try
        {
            var files = Directory
                .EnumerateFiles(dir, "*.ts",  SearchOption.TopDirectoryOnly)
                .Concat(Directory.EnumerateFiles(dir, "*.cts", SearchOption.TopDirectoryOnly))
                .OrderBy(f => Path.GetFileName(f), StringComparer.OrdinalIgnoreCase);

            foreach (var fp in files)
                nodes.Add(new ScriptNode(
                    IsDir: false, Name: Path.GetFileName(fp),
                    RelPath: StripRelativePrefix(
                               Path.GetRelativePath(baseDir, fp).Replace('\\', '/')),
                    Children: []));
        }
        catch { /* permission denied */ }

        return nodes;
    }

    /// <summary>
    /// Converts data nodes into MenuItem objects.  Must run on the UI thread.
    /// </summary>
    private void BuildMenuItems(List<object> target, IReadOnlyList<ScriptNode> nodes)
    {
        foreach (var node in nodes)
        {
            if (node.IsDir)
            {
                var subItems = new List<object>();
                BuildMenuItems(subItems, node.Children);
                if (subItems.Count == 0) continue;
                var sub = new MenuItem { Header = EscapeMenuHeaderText(node.Name) };
                sub.ItemsSource = subItems;
                target.Add(sub);
            }
            else
            {
                var relPath = node.RelPath;  // capture
                var item    = new MenuItem { Header = EscapeMenuHeaderText(node.Name) };
                ToolTip.SetTip(item, relPath);
                item.Click += (_, _) =>
                {
                    SendProxyMenuCommand($"ss {relPath}");
                };
                target.Add(item);
            }
        }
    }

    private void SendProxyMenuCommand(string command)
    {
        char commandChar = _embeddedGameConfig?.CommandChar is { } configured && configured != '\0'
            ? configured
            : '$';
        string line = $"{commandChar}{command}\r\n";
        _termCtrl.SendInput?.Invoke(System.Text.Encoding.Latin1.GetBytes(line));
    }

    /// <summary>Strips a leading <c>./</c> produced by <see cref="Path.GetRelativePath"/>
    /// when the file is directly inside the base directory, then trims whitespace.</summary>
    private static string StripRelativePrefix(string rel)
    {
        rel = rel.Trim();
        if (rel.StartsWith("./") || rel.StartsWith(".\\"))
            rel = rel[2..].TrimStart('/', '\\');
        return rel;
    }

    /// <summary>Returns true if <paramref name="dir"/> (or any sub-dir within
    /// <paramref name="remainingDepth"/> levels) contains at least one .ts or .cts file.</summary>
    private static bool DirectoryHasScripts(string dir, int remainingDepth)
    {
        try
        {
            if (Directory.EnumerateFiles(dir, "*.ts",  SearchOption.TopDirectoryOnly).Any()) return true;
            if (Directory.EnumerateFiles(dir, "*.cts", SearchOption.TopDirectoryOnly).Any()) return true;
            if (remainingDepth > 0)
                foreach (var sub in Directory.EnumerateDirectories(dir))
                    if (DirectoryHasScripts(sub, remainingDepth - 1)) return true;
        }
        catch { /* ignore */ }
        return false;
    }

    /// <summary>Adds path to recent list, persists prefs, rebuilds the Recent submenu.</summary>
    private void AddToRecentAndSave(string path)
    {
        _appPrefs.AddRecent(path);
        _appPrefs.Save();
        RebuildRecentMenu();
    }

    /// <summary>Rebuilds the items inside the Recent submenu from <see cref="_appPrefs"/>.</summary>
    private void RebuildRecentMenu()
    {
        var items = new List<object>();
        foreach (var path in _appPrefs.RecentFiles)
        {
            var p    = path;  // capture
            var name = Path.GetFileNameWithoutExtension(p);
            if (string.IsNullOrWhiteSpace(name))
                name = Path.GetFileName(p);
            var item = new MenuItem { Header = EscapeMenuHeaderText(name) };
            ToolTip.SetTip(item, p);
            item.Click += (_, _) => _ = OpenRecentAsync(p);
            items.Add(item);
        }
        if (items.Count == 0)
            items.Add(new MenuItem { Header = "(none)", IsEnabled = false });

        _recentMenu.ItemsSource = items;
        _viewClearRecents.IsEnabled = _appPrefs.RecentFiles.Count > 0;
        RefreshNativeAppMenu();
    }

    private void RefreshNativeAppMenu()
    {
        if (!OperatingSystem.IsMacOS())
            return;

        if (!_nativeAppMenuReady)
            return;

        _nativeAppMenu.Items.Clear();
        foreach (object? item in _menuBar.Items)
        {
            if (item is MenuItem menuItem && !menuItem.IsVisible)
                continue;

            NativeMenuItemBase? nativeItem = ConvertToNativeMenuItem(item);
            if (nativeItem != null)
                _nativeAppMenu.Add(nativeItem);
        }

        if (!_nativeAppMenuAttached)
        {
            NativeMenu.SetMenu(this, _nativeAppMenu);
            _nativeAppMenuAttached = true;
        }
    }

    private void RefreshNativeDockMenu()
    {
        if (!OperatingSystem.IsMacOS())
            return;

        if (!_nativeAppMenuReady)
            return;

        _nativeDockMenu.Items.Clear();
        AddDockRoot(_scriptsMenu, "_Scripts");
        AddDockRoot(_proxyMenu, "_Proxy");
        AddDockRoot(_botMenu, "_Bot");
        AddDockRoot(_quickMenu, "_Quick");
        AddDockRoot(_toolsMenu, "_Tools");
        AddDockRoot(_aiMenu, "_AI");

        if (!_nativeDockMenuAttached)
        {
            NativeDock.SetMenu(this, _nativeDockMenu);
            _nativeDockMenuAttached = true;
        }
    }

    private void AddDockRoot(MenuItem sourceMenu, string header)
    {
        if (!sourceMenu.IsVisible)
            return;

        var dockRoot = new MenuItem
        {
            Header = header,
            ItemsSource = sourceMenu.ItemsSource,
            IsEnabled = sourceMenu.IsEnabled,
            IsVisible = sourceMenu.IsVisible,
        };

        NativeMenuItemBase? nativeItem = ConvertToNativeMenuItem(dockRoot);
        if (nativeItem != null)
            _nativeDockMenu.Add(nativeItem);
    }

    private static NativeMenuItemBase? ConvertToNativeMenuItem(object? item)
    {
        if (item is Separator)
            return new NativeMenuItemSeparator();

        if (item is not MenuItem menuItem)
            return null;

        var nativeItem = new NativeMenuItem
        {
            Header = NormalizeNativeMenuHeader(menuItem.Header?.ToString()),
            IsEnabled = menuItem.IsEnabled,
            IsVisible = menuItem.IsVisible,
        };

        var children = GetMenuChildren(menuItem)
            .Select(ConvertToNativeMenuItem)
            .Where(child => child != null)
            .Cast<NativeMenuItemBase>()
            .ToList();

        if (children.Count > 0)
        {
            var submenu = new NativeMenu();
            foreach (NativeMenuItemBase child in children)
                submenu.Add(child);
            nativeItem.Menu = submenu;
        }
        else
        {
            nativeItem.Click += (_, _) =>
                menuItem.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(MenuItem.ClickEvent));
        }

        return nativeItem;
    }

    private static IEnumerable<object?> GetMenuChildren(MenuItem menuItem)
    {
        if (menuItem.ItemsSource is IEnumerable source)
        {
            foreach (object? item in source)
                yield return item;
            yield break;
        }

        foreach (object? item in menuItem.Items)
            yield return item;
    }

    private static string NormalizeNativeMenuHeader(string? header)
    {
        if (string.IsNullOrWhiteSpace(header))
            return string.Empty;

        var sb = new System.Text.StringBuilder(header.Length);
        for (int i = 0; i < header.Length; i++)
        {
            if (header[i] != '_')
            {
                sb.Append(header[i]);
                continue;
            }

            if (i + 1 < header.Length && header[i + 1] == '_')
            {
                sb.Append('_');
                i++;
            }
        }

        return sb.ToString();
    }

    private static string EscapeMenuHeaderText(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return string.Empty;

        return text.Replace("_", "__");
    }

    /// <summary>Opens a recently used game config or database directly (no file picker, no connect).</summary>
    private async Task OpenRecentAsync(string path)
    {
        try
        {
            _menuBar.Close();
            if (!File.Exists(path))
            {
                await ShowMessageAsync("File Not Found",
                    $"The file\n{path}\nno longer exists.\n\nIt will be removed from the recent list.");
                _appPrefs.RecentFiles.Remove(path);
                _appPrefs.Save();
                RebuildRecentMenu();
                return;
            }

            await OpenPathAsync(path, addToRecent: true);
        }
        catch (Exception ex)
        {
            Core.GlobalModules.DebugLog($"[MTC.OpenRecent] failed path='{path}': {ex}\n");
            Core.GlobalModules.FlushDebugLog();
            await ShowMessageAsync("Open Recent Failed", ex.Message);
        }
    }

    /// <summary>File > Edit Connection: update the shared game config in-place.</summary>
    private async Task OnEditConnectionAsync()
    {
        string previousGameName = DeriveGameName();
        string previousConfigPath = _currentProfilePath ?? GameConfigPathForMode(previousGameName, _state.EmbeddedProxy);
        string previousDatabasePath = _embeddedGameConfig?.DatabasePath ?? string.Empty;
        string previousHost = _embeddedGameConfig?.Host ?? _state.Host;
        int previousPort = _embeddedGameConfig?.Port > 0 ? _embeddedGameConfig.Port : _state.Port;
        if (string.IsNullOrWhiteSpace(previousDatabasePath))
            previousDatabasePath = DatabasePathForMode(previousGameName, _state.EmbeddedProxy);

        var dlg = new NewConnectionDialog(BuildProfileFromState());
        if (!await dlg.ShowDialog<bool>(this) || dlg.Result == null) return;

        ConnectionProfile? uniqueEditedProfile = await EnsureUniqueProfileAsync(
            dlg.Result,
            currentConfigPath: previousConfigPath,
            currentDatabasePath: previousDatabasePath);
        if (uniqueEditedProfile == null)
            return;

        ConnectionProfile editedProfile = uniqueEditedProfile;
        string resolvedGameName = editedProfile.Name;

        string targetDatabasePath = previousDatabasePath;
        string oldDefaultDatabasePath = DatabasePathForMode(previousGameName, _state.EmbeddedProxy);
        if (!string.Equals(previousGameName, resolvedGameName, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(previousDatabasePath, oldDefaultDatabasePath, StringComparison.OrdinalIgnoreCase))
        {
            targetDatabasePath = DatabasePathForMode(resolvedGameName, editedProfile.EmbeddedProxy);
            try
            {
                if (File.Exists(previousDatabasePath) && !File.Exists(targetDatabasePath))
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(targetDatabasePath)!);
                    File.Move(previousDatabasePath, targetDatabasePath);
                }
            }
            catch
            {
                targetDatabasePath = previousDatabasePath;
            }
        }

        EmbeddedGameConfig config = BuildEmbeddedGameConfigFromProfile(
            editedProfile,
            string.IsNullOrWhiteSpace(targetDatabasePath) ? DatabasePathForMode(resolvedGameName, editedProfile.EmbeddedProxy) : targetDatabasePath,
            _embeddedGameConfig);
        await SaveEmbeddedGameConfigAsync(resolvedGameName, config);

        string newConfigPath = GameConfigPathForMode(resolvedGameName, editedProfile.EmbeddedProxy);
        if (!string.IsNullOrWhiteSpace(previousConfigPath) &&
            !string.Equals(previousConfigPath, newConfigPath, StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                if (File.Exists(previousConfigPath))
                    File.Delete(previousConfigPath);
            }
            catch { }
        }

        _currentProfilePath = newConfigPath;
        _embeddedGameConfig = config;
        _embeddedGameName = resolvedGameName;
        ApplyProfile(BuildProfileFromConfig(config));
        UpdateOpenStandaloneDatabaseHeader(config);
        ApplyDebugLoggingPreferences();
        AddToRecentAndSave(newConfigPath);
        await SyncEmbeddedProxySettingsAsync(previousHost, previousPort);

        _parser.Feed($"\x1b[1;36m[Connection settings updated]\x1b[0m\r\n");
        _buffer.Dirty = true;
    }

    private void UpdateOpenStandaloneDatabaseHeader(EmbeddedGameConfig config)
    {
        if (_state.EmbeddedProxy || _sessionDb == null)
            return;

        try
        {
            Core.DataHeader header = _sessionDb.DBHeader;
            bool headerDirty = false;
            int sectors = config.Sectors > 0 ? config.Sectors : _state.Sectors;
            if (sectors > 0)
            {
                headerDirty |= header.Sectors != sectors;
                header.Sectors = sectors;
            }

            headerDirty |= header.Address != _state.Host;
            header.Address = _state.Host;
            headerDirty |= header.ServerPort != (ushort)_state.Port;
            header.ServerPort = (ushort)_state.Port;

            if (headerDirty)
            {
                _sessionDb.ReplaceHeader(header);
                _sessionDb.SaveDatabase();
            }
        }
        catch (Exception ex)
        {
            Core.GlobalModules.DebugLog($"[MTC.EditConnection] failed to update standalone database header: {ex}\n");
        }
    }

    private async Task SyncEmbeddedProxySettingsAsync(string? previousHostOverride = null, int? previousPortOverride = null)
    {
        if (!_state.EmbeddedProxy)
        {
            if (_gameInstance != null)
                await StopEmbeddedAsync();
            return;
        }

        string gameName = GetEmbeddedGameName();
        var gameConfig = _embeddedGameConfig ?? await LoadOrCreateEmbeddedGameConfigAsync(gameName);
        string? originalNativeHaggleMode = gameConfig.NativeHaggleMode;
        gameConfig.NativeHaggleMode = null;
        string previousHost = previousHostOverride ?? gameConfig.Host;
        int previousPort = previousPortOverride ?? gameConfig.Port;

        bool configChanged =
            !string.Equals(originalNativeHaggleMode ?? string.Empty, gameConfig.NativeHaggleMode ?? string.Empty, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(gameConfig.Name, gameName, StringComparison.Ordinal) ||
            gameConfig.Host != _state.Host ||
            gameConfig.Port != _state.Port ||
            gameConfig.Sectors != _state.Sectors;

        gameConfig.Name = gameName;
        gameConfig.Host = _state.Host;
        gameConfig.Port = _state.Port;
        gameConfig.Sectors = _state.Sectors;

        if (configChanged)
            await SaveEmbeddedGameConfigAsync(gameName, gameConfig);

        _embeddedGameConfig = gameConfig;
        _embeddedGameName = gameName;

        if (_sessionDb != null)
        {
            var header = _sessionDb.DBHeader;
            bool headerDirty = false;
            if (gameConfig.Sectors > 0)
            {
                headerDirty |= header.Sectors != gameConfig.Sectors;
                header.Sectors = gameConfig.Sectors;
            }
            headerDirty |= header.Address != _state.Host;
            header.Address = _state.Host;
            headerDirty |= header.ServerPort != (ushort)_state.Port;
            header.ServerPort = (ushort)_state.Port;
            headerDirty |= header.ListenPort != (ushort)gameConfig.ListenPort;
            header.ListenPort = (ushort)gameConfig.ListenPort;
            headerDirty |= header.CommandChar != (gameConfig.CommandChar == '\0' ? '$' : gameConfig.CommandChar);
            header.CommandChar = gameConfig.CommandChar == '\0' ? '$' : gameConfig.CommandChar;
            string configLoginScript = string.IsNullOrWhiteSpace(gameConfig.LoginScript) ? "0_Login.cts" : gameConfig.LoginScript;
            string configLoginName = gameConfig.LoginName ?? string.Empty;
            string configPassword = gameConfig.Password ?? string.Empty;
            char configGameChar = string.IsNullOrWhiteSpace(gameConfig.GameLetter) ? '\0' : char.ToUpperInvariant(gameConfig.GameLetter[0]);
            headerDirty |= header.UseLogin != gameConfig.UseLogin;
            header.UseLogin = gameConfig.UseLogin;
            headerDirty |= header.UseRLogin != gameConfig.UseRLogin;
            header.UseRLogin = gameConfig.UseRLogin;
            headerDirty |= header.LoginScript != configLoginScript;
            header.LoginScript = configLoginScript;
            headerDirty |= header.LoginName != configLoginName;
            header.LoginName = configLoginName;
            headerDirty |= header.Password != configPassword;
            header.Password = configPassword;
            headerDirty |= header.Game != configGameChar;
            header.Game = configGameChar;
            _sessionDb.ReplaceHeader(header);
            if (headerDirty)
                _sessionDb.SaveDatabase();
            Core.ScriptRef.SetActiveDatabase(_sessionDb);
        }

        if (_gameInstance == null)
            return;

        ApplySessionLogSettings(gameConfig);
        _gameInstance.AutoReconnect = _state.AutoReconnect;
        _gameInstance.ReconnectDelayMs = Math.Max(1, gameConfig.ReconnectDelaySeconds) * 1000;
        _gameInstance.LocalEcho = gameConfig.LocalEcho;
        _gameInstance.AcceptExternal = gameConfig.AcceptExternal;
        _gameInstance.AllowLerkers = gameConfig.AllowLerkers;
        _gameInstance.ExternalAddress = gameConfig.ExternalAddress ?? string.Empty;
        _gameInstance.BroadCastMsgs = gameConfig.BroadcastMessages;
        _gameInstance.Logger.LogEnabled = false;
        _gameInstance.Logger.LogData = false;
        _gameInstance.Logger.LogANSI = gameConfig.LogAnsi;
        _gameInstance.Logger.LogAnsiCompanion = gameConfig.LogAnsiCompanion;
        _gameInstance.Logger.BinaryLogs = gameConfig.LogBinary;
        _gameInstance.Logger.NotifyPlayCuts = gameConfig.NotifyPlayCuts;
        _gameInstance.Logger.MaxPlayDelay = gameConfig.MaxPlayDelay;
        _gameInstance.SetNativeHaggleEnabled(gameConfig.NativeHaggleEnabled, Core.NativeHaggleChangeSource.Config);
        Core.GlobalModules.DebugLog(
            $"[MTC] Embedded haggle sync prefsPortMode={ResolveGlobalPortHaggleMode()} prefsPlanetMode={ResolveGlobalPlanetHaggleMode()} legacyGameMode={gameConfig.NativeHaggleMode ?? "-"}\n");
        _gameInstance.SetNativeHaggleModes(ResolveGlobalPortHaggleMode(), ResolveGlobalPlanetHaggleMode());

        bool endpointChanged = !string.Equals(previousHost, _state.Host, StringComparison.Ordinal) || previousPort != _state.Port;
        if (!_gameInstance.IsConnected && endpointChanged)
        {
            await StopEmbeddedAsync();
            await DoConnectEmbeddedAsync();
        }
    }

    private async Task OnNewConnectionAsync()
    {
        var dlg = new NewConnectionDialog();
        if (!await dlg.ShowDialog<bool>(this) || dlg.Result == null) return;

        ConnectionProfile? uniqueNewProfile = await EnsureUniqueProfileAsync(dlg.Result);
        if (uniqueNewProfile == null)
            return;

        ConnectionProfile newProfile = uniqueNewProfile;
        string gameName = newProfile.Name;
        string path = GameConfigPathForMode(gameName, newProfile.EmbeddedProxy);
        EmbeddedGameConfig config = BuildEmbeddedGameConfigFromProfile(
            newProfile,
            DatabasePathForMode(gameName, newProfile.EmbeddedProxy));
        await SaveEmbeddedGameConfigAsync(gameName, config);
        await ApplyLoadedGameConfigAsync(config, path, addToRecent: true);
    }

    /// <summary>File > Open: open or import a shared game JSON or a TWX database.</summary>
    private async Task OnOpenConnectionAsync()
    {
        var storage = TopLevel.GetTopLevel(this)?.StorageProvider;
        if (storage == null) return;

        AppPaths.EnsureTwxproxyGamesDir();
        var games = await storage.TryGetFolderFromPathAsync(AppPaths.TwxproxyGamesDir)
            ?? await GetHomeFolderAsync(storage);
        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title                  = "Open Game",
            SuggestedStartLocation = games,
            AllowMultiple          = false,
            FileTypeFilter         =
            [
                new FilePickerFileType("TWX Game Config") { Patterns = ["*.json"] },
                new FilePickerFileType("TWX Database") { Patterns = ["*.xdb"] },
                new FilePickerFileType("Legacy MTC Connection") { Patterns = ["*.mtc"] },
                new FilePickerFileType("All Files")      { Patterns = ["*"]     },
            ],
        });
        if (files.Count == 0) return;

        string path = files[0].Path.LocalPath;
        await OpenPathAsync(path, addToRecent: true);
    }

    /// <summary>File > Save / Save As: persist the current shared game JSON.</summary>
    private async Task OnSaveConnectionAsync(bool saveAs = false)
    {
        if (!saveAs)
        {
            await SaveCurrentGameConfigAsync();
            if (_currentProfilePath != null)
                AddToRecentAndSave(_currentProfilePath);
            return;
        }

        var dlg = new NewConnectionDialog(BuildProfileFromState());
        if (!await dlg.ShowDialog<bool>(this) || dlg.Result == null)
            return;

        ConnectionProfile? uniqueSaveAsProfile = await EnsureUniqueProfileAsync(
            dlg.Result,
            currentConfigPath: null,
            currentDatabasePath: null);
        if (uniqueSaveAsProfile == null)
            return;

        ConnectionProfile saveAsProfile = uniqueSaveAsProfile;
        string gameName = saveAsProfile.Name;
        string path = GameConfigPathForMode(gameName, saveAsProfile.EmbeddedProxy);
        string targetDatabasePath = DatabasePathForMode(gameName, saveAsProfile.EmbeddedProxy);
        string currentDatabasePath = _embeddedGameConfig?.DatabasePath ?? DatabasePathForMode(DeriveGameName(), _state.EmbeddedProxy);
        if (!string.IsNullOrWhiteSpace(currentDatabasePath) &&
            File.Exists(currentDatabasePath) &&
            !string.Equals(currentDatabasePath, targetDatabasePath, StringComparison.OrdinalIgnoreCase) &&
            !File.Exists(targetDatabasePath))
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(targetDatabasePath)!);
                File.Copy(currentDatabasePath, targetDatabasePath, overwrite: false);
            }
            catch { }
        }
        EmbeddedGameConfig config = BuildEmbeddedGameConfigFromProfile(
            saveAsProfile,
            targetDatabasePath,
            _embeddedGameConfig);
        await SaveEmbeddedGameConfigAsync(gameName, config);
        await ApplyLoadedGameConfigAsync(config, path, addToRecent: true);
    }

    private async Task<string?> PickProxySavePathAsync(
        string title,
        string suggestedName,
        string extension,
        string typeName,
        params string[] patterns)
    {
        var storage = TopLevel.GetTopLevel(this)?.StorageProvider;
        if (storage == null) return null;

        var home = await GetHomeFolderAsync(storage);
        var file = await storage.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = title,
            SuggestedStartLocation = home,
            SuggestedFileName = suggestedName,
            DefaultExtension = extension,
            FileTypeChoices =
            [
                new FilePickerFileType(typeName) { Patterns = patterns },
            ],
        });
        return file?.Path.LocalPath;
    }

    private async Task<string?> PickProxyOpenPathAsync(
        string title,
        string typeName,
        params string[] patterns)
    {
        var storage = TopLevel.GetTopLevel(this)?.StorageProvider;
        if (storage == null) return null;

        var home = await GetHomeFolderAsync(storage);
        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            SuggestedStartLocation = home,
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType(typeName) { Patterns = patterns },
                new FilePickerFileType("All Files") { Patterns = ["*"] },
            ],
        });
        return files.Count > 0 ? files[0].Path.LocalPath : null;
    }

    private static async Task<IStorageFolder?> GetHomeFolderAsync(IStorageProvider storage)
    {
        var homePath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return await storage.TryGetFolderFromPathAsync(homePath);
    }

    private async Task<string?> ShowTextPromptAsync(string title, string prompt, string initialValue, string confirmText)
    {
        string? result = null;
        var input = new TextBox
        {
            Text = initialValue,
            MinWidth = 420,
        };
        var okBtn = new Button { Content = confirmText, MinWidth = 100 };
        var cancelBtn = new Button { Content = "Cancel", MinWidth = 100 };

        var dlg = new Window
        {
            Title = title,
            Width = 520,
            Height = 180,
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
                        Text = prompt,
                        Foreground = FgKey,
                        TextWrapping = TextWrapping.Wrap,
                    },
                    input,
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        Spacing = 10,
                        Children = { okBtn, cancelBtn },
                    },
                },
            },
        };

        void Accept()
        {
            result = input.Text?.Trim();
            dlg.Close();
        }

        okBtn.Click += (_, _) => Accept();
        cancelBtn.Click += (_, _) => dlg.Close();
        input.AttachedToVisualTree += (_, _) => input.Focus();
        input.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter && !e.KeyModifiers.HasFlag(KeyModifiers.Shift))
            {
                e.Handled = true;
                Accept();
            }
            else if (e.Key == Key.Escape)
            {
                e.Handled = true;
                dlg.Close();
            }
        };

        await dlg.ShowDialog(this);
        return result;
    }

    private async Task ShowMessageAsync(string title, string message)
    {
        var okBtn = new Button { Content = "OK" };
        var dlg = new Window
        {
            Title           = title,
            Width           = 420,
            Height          = 200,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize       = false,
            Background      = BgPanel,
            Content         = new StackPanel
            {
                Margin      = new Thickness(20),
                Spacing     = 16,
                Children    =
                {
                    new TextBlock
                    {
                        Text       = message,
                        Foreground = FgKey,
                        TextWrapping = TextWrapping.Wrap,
                    },
                    new StackPanel
                    {
                        HorizontalAlignment = HorizontalAlignment.Center,
                        Children = { okBtn },
                    },
                },
            },
        };
        okBtn.Click += (_, _) => dlg.Close();
        await dlg.ShowDialog(this);
    }

    private async Task<bool> ShowConfirmAsync(string title, string message, string yesText, string noText)
    {
        bool result = false;
        var yesBtn = new Button { Content = yesText, MinWidth = 110 };
        var noBtn = new Button { Content = noText, MinWidth = 110 };

        var dlg = new Window
        {
            Title = title,
            Width = 520,
            Height = 220,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
            Background = BgPanel,
            Content = new StackPanel
            {
                Margin = new Thickness(20),
                Spacing = 16,
                Children =
                {
                    new TextBlock
                    {
                        Text = message,
                        Foreground = FgKey,
                        TextWrapping = TextWrapping.Wrap,
                    },
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        Spacing = 10,
                        Children = { yesBtn, noBtn },
                    },
                },
            },
        };

        yesBtn.Click += (_, _) =>
        {
            result = true;
            dlg.Close();
        };
        noBtn.Click += (_, _) =>
        {
            result = false;
            dlg.Close();
        };

        await dlg.ShowDialog(this);
        return result;
    }
}
