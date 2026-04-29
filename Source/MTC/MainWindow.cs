using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
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
public class MainWindow : Window
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
    private Core.ExpansionModuleHost?      _moduleHost;
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
    private int _pendingNativeMombotEscapeEchoSuppressions;
    private long _nativeMombotEscapeEchoSuppressUntilUtcTicks;
    private bool _suppressingPendingNativeMombotEscapeSequence;
    private bool _suppressingPendingNativeMombotEscapeCsiBody;
    private bool _pendingTerminalSyncMarkerLeadByte;
    private bool _mombotKeepaliveTickRunning;
    private bool _mombotStartupDataGatherPending;
    private bool _mombotStartupDataGatherRunning;
    private bool _mombotStartupPostInitPending;
    private bool _mombotStartupFinalizeRunning;
    private bool _nativeBotAutoStartInFlight;
    private FinderPrewarmKey? _lastFinderPrewarmKey;
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
    private static readonly IBrush HoldsOreBrush = new SolidColorBrush(Color.FromRgb(214, 164, 96));
    private static readonly IBrush HoldsOrgBrush = new SolidColorBrush(Color.FromRgb(118, 178, 116));
    private static readonly IBrush HoldsEqBrush = new SolidColorBrush(Color.FromRgb(96, 171, 194));
    private static readonly IBrush HoldsColsBrush = new SolidColorBrush(Color.FromRgb(164, 128, 198));
    private static readonly IBrush HoldsFreeBrush = new SolidColorBrush(Color.FromRgb(123, 145, 156));

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
            _sessionLog.Dispose();
            _redAlertTimer.Stop();
            _statusRefreshTimer.Stop();
        };
    }

    private TerminalControl CreateTerminalControl()
    {
        var control = new TerminalControl(_buffer);
        if (_terminalInputHandler != null)
            control.SendInput = _terminalInputHandler;
        control.IsConnected = _state.Connected;
        control.SetFontSize(_terminalFontSize);
        if (!string.IsNullOrWhiteSpace(_terminalFontFamilyName))
            control.SetFont(_terminalFontFamilyName);
        return control;
    }

    private void NotifyTerminalWindowMove()
    {
        _termCtrl.NotifyHostWindowPositionChanged();
        _deckTermCtrl.NotifyHostWindowPositionChanged();
    }

    private void RecreateClassicShellControls()
    {
        _termCtrl = CreateTerminalControl();
        _commPanelBorder = null;
        _commFedTabButton = null;
        _commSubspaceTabButton = null;
        _commPrivateTabButton = null;
        _commFedTextBlock = null;
        _commSubspaceTextBlock = null;
        _commPrivateTextBlock = null;
        _commFedScrollViewer = null;
        _commSubspaceScrollViewer = null;
        _commPrivateScrollViewer = null;
        _commComposeTextBox = null;
        _commPrivateTargetTextBox = null;
        _commPrivateTargetLabel = null;
        _commSplitterRow = null;
        _commPanelRow = null;
        _commGridSplitter = null;

        _valName = new();
        _valSector = new();
        _valTurns = new();
        _valExper = new();
        _valAlignm = new();
        _valCred = new();
        _valHTotal = new();
        _valFuelOre = new();
        _valOrganics = new();
        _valEquipment = new();
        _valColonists = new();
        _valEmpty = new();
        _holdsFuelOreColumn = new();
        _holdsOrganicsColumn = new();
        _holdsEquipmentColumn = new();
        _holdsColonistsColumn = new();
        _holdsEmptyColumn = new();
        _holdsFuelOreSegment = null;
        _holdsOrganicsSegment = null;
        _holdsEquipmentSegment = null;
        _holdsColonistsSegment = null;
        _holdsEmptySegment = null;
        _valFighters = new();
        _valShields = new();
        _valTrnWarp = new();
        _valEther = new();
        _valBeacon = new();
        _valDisruptor = new();
        _valPhoton = new();
        _valArmid = new();
        _valLimpet = new();
        _valGenesis = new();
        _valAtomic = new();
        _valCorbo = new();
        _valCloak = new();
        _valTW1 = new();
        _valTW2 = new();
        _scanIndD = new();
        _scanIndH = new();
        _scanIndP = new();
    }

    private void RecreateDeckShellControls()
    {
        _deckTermCtrl = CreateTerminalControl();
        _deckMacroRecordButton = null;
        _deckMacroStopButton = null;
        _deckMacroPlayButton = null;
        _deckCommPanelBorder = null;
        _deckCommFedTabButton = null;
        _deckCommSubspaceTabButton = null;
        _deckCommPrivateTabButton = null;
        _deckCommFedTextBlock = null;
        _deckCommSubspaceTextBlock = null;
        _deckCommPrivateTextBlock = null;
        _deckCommFedScrollViewer = null;
        _deckCommSubspaceScrollViewer = null;
        _deckCommPrivateScrollViewer = null;
        _deckCommComposeTextBox = null;
        _deckCommPrivateTargetTextBox = null;
        _deckCommPrivateTargetLabel = null;
        _deckCommSplitterRow = null;
        _deckCommPanelRow = null;
        _deckCommGridSplitter = null;

        _deckValName = new();
        _deckValSector = new();
        _deckValTurns = new();
        _deckValExper = new();
        _deckValAlignm = new();
        _deckValCred = new();
        _deckValHTotal = new();
        _deckValFuelOre = new();
        _deckValOrganics = new();
        _deckValEquipment = new();
        _deckValColonists = new();
        _deckValEmpty = new();
        _deckValFighters = new();
        _deckValShields = new();
        _deckValTrnWarp = new();
        _deckValEther = new();
        _deckValBeacon = new();
        _deckValDisruptor = new();
        _deckValPhoton = new();
        _deckValArmid = new();
        _deckValLimpet = new();
        _deckValGenesis = new();
        _deckValAtomic = new();
        _deckValCorbo = new();
        _deckValCloak = new();
        _deckValTW1 = new();
        _deckValTW2 = new();
        _deckScanIndD = new();
        _deckScanIndH = new();
        _deckScanIndP = new();
        _deckHudHeaderSector = new();
        _deckHudHeaderConnection = new();
        _deckHudShipName = new();
        _deckHudShipSubtitle = new();
        _deckHudStarDock = new();
        _deckHudRylos = new();
        _deckHudAlpha = new();
        _deckHudUniverse = new();
    }

    // ── Layout ─────────────────────────────────────────────────────────────

    private Control BuildLayout()
    {
        // Root: DockPanel – menu top, status bottom, swappable shell in the middle.
        var dock = new DockPanel { Background = HudWindow };
        _rootDock = dock;

        ConfigureStatusModeSelector();

        // ── Menu ──────────────────────────────────────────────────────────
        _menuBar = BuildMenuBar();
        _menuBarHost.Child = BuildMenuBarHost();
        DockPanel.SetDock(_menuBarHost, Dock.Top);
        dock.Children.Add(_menuBarHost);

        // ── Status bar ────────────────────────────────────────────────────
        _statusText.Text              = "[ disconnected ]";
        _statusText.Foreground         = HudText;
        _statusText.VerticalAlignment  = VerticalAlignment.Center;
        _statusText.Margin             = new Thickness(6, 0, 0, 0);
        _statusText.FontSize           = 13;
        _statusText.IsVisible          = false;

        _statusBar.Background = BgStatus;
        _statusBar.Height = 34;
        InvalidateStatusBarLayout();
        _statusBar.Child = _statusBarContent;
        _statusBar.IsVisible = _appPrefs.ShowBottomBar;
        DockPanel.SetDock(_statusBar, Dock.Bottom);
        dock.Children.Add(_statusBar);

        _shellHost.Background = Brushes.Transparent;
        _shellHost.Padding = new Thickness(6, 4, 6, 4);
        dock.Children.Add(_shellHost);

        ApplySelectedSkinSafe();
        return dock;
    }

    private Control BuildClassicShell()
    {
        RecreateClassicShellControls();
        _tacticalMap = null;
        bool hasSidebarSections = HasVisibleStatusPanelSections();

        // Margin lets the gray BgChrome peek in on all four sides as a frame.
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = hasSidebarSections ? new GridLength(200) : new GridLength(0) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = hasSidebarSections ? new GridLength(6) : new GridLength(0) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        if (hasSidebarSections)
        {
            var sidebar = BuildSidebar();
            Grid.SetColumn(sidebar, 0);
            grid.Children.Add(sidebar);
        }

        var termArea = BuildTerminalArea();
        Grid.SetColumn(termArea, 2);
        grid.Children.Add(termArea);

        return new Border
        {
            Background = HudShell,
            BorderBrush = HudEdge,
            BorderThickness = new Thickness(1.5),
            CornerRadius = new CornerRadius(22),
            Padding = new Thickness(14),
            Child = grid,
        };
    }

    private Control BuildCommandDeckShell()
    {
        RecreateDeckShellControls();
        _deckPanels.Clear();
        _deckPanelsInitialized = false;
        _suppressDeckPanelStateSync = false;
        _tacticalMap = new TacticalMapControl(
            () => _state.Sector,
            () => _sessionDb,
            () => _state)
        {
            MinHeight = 220,
        };
        _tacticalMap.SectorDoubleClicked += (_, sectorNumber) => _tacticalMap?.CenterOnSector(sectorNumber);

        var rootGrid = new Grid();
        rootGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        rootGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var banner = BuildDeckBanner();
        Grid.SetRow(banner, 0);
        rootGrid.Children.Add(banner);

        _deckSurface = new Canvas
        {
            ClipToBounds = true,
        };
        _deckSurface.SizeChanged += (_, _) => EnsureDeckPanelsInitialized();

        var surfaceRoot = new Grid { Margin = new Thickness(0, 14, 0, 0) };
        surfaceRoot.Children.Add(new Border
        {
            Background = HudFrame,
            BorderBrush = HudInnerEdge,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(18),
        });
        surfaceRoot.Children.Add(_deckSurface);

        CreateDeckPanel("map", "TACTICAL OVERLAY", "LIVE", BuildDeckMapBody(), canClose: true);
        CreateDeckPanel("console", "GAMEPLAY CONSOLE", "ANSI", BuildDeckTerminalBody(), canClose: false);
        CreateDeckPanel("ship", "SHIP BAY", "SYSTEMS", BuildDeckShipPanel(), canClose: true);
        CreateDeckPanel("intel", "COMMAND MATRIX", "INTEL", BuildDeckCenterPanels(), canClose: true);
        CreateDeckPanel("logo", "AUXILIARY PANEL", "STANDBY", BuildLogoPanel(), canClose: true);
        Dispatcher.UIThread.Post(EnsureDeckPanelsInitialized, DispatcherPriority.Loaded);

        Grid.SetRow(surfaceRoot, 1);
        rootGrid.Children.Add(surfaceRoot);

        return new Border
        {
            Background = HudShell,
            BorderBrush = HudEdge,
            BorderThickness = new Thickness(1.5),
            CornerRadius = new CornerRadius(22),
            Padding = new Thickness(16),
            Child = rootGrid,
        };
    }

    private void CreateDeckPanel(string panelId, string title, string tag, Control body, bool canClose)
    {
        if (_deckSurface == null)
            return;

        DeckPanelState state = GetOrCreateDeckPanelState(panelId);
        var panel = new FloatingDeckPanel(
            panelId,
            title,
            tag,
            body,
            state.Width,
            state.BodyHeight,
            canClose,
            HudFrame,
            HudFrameAlt,
            HudHeader,
            HudHeaderAlt,
            HudEdge,
            HudInnerEdge,
            HudAccent,
            HudMuted,
            HudTitleFont);

        panel.Activated += BringDeckPanelToFront;
        panel.StateChanged += OnDeckPanelStateChanged;
        panel.DragSnapHandler = GetSnappedDeckPanelPosition;
        panel.IsVisible = false;

        _deckSurface.Children.Add(panel);
        _deckPanels[panelId] = panel;
    }

    private DeckPanelState GetOrCreateDeckPanelState(string panelId)
    {
        if (_deckPanelStates.TryGetValue(panelId, out DeckPanelState? state))
            return state;

        state = CreateDefaultDeckPanelState(panelId);
        if (_appPrefs.TryGetDeckPanelLayout(panelId, out AppPreferences.DeckPanelLayout? savedLayout))
        {
            state.Left = savedLayout.Left;
            state.Top = savedLayout.Top;
            if (savedLayout.Width > 0)
                state.Width = savedLayout.Width;
            if (savedLayout.BodyHeight > 0)
                state.BodyHeight = savedLayout.BodyHeight;
            state.ZIndex = savedLayout.ZIndex > 0 ? savedLayout.ZIndex : state.ZIndex;
            state.Closed = savedLayout.Closed;
            state.Minimized = savedLayout.Minimized;
        }
        _deckPanelStates[panelId] = state;
        _deckNextZIndex = Math.Max(_deckNextZIndex, state.ZIndex + 1);
        return state;
    }

    private DeckPanelState CreateDefaultDeckPanelState(string panelId)
    {
        double surfaceWidth = Math.Max(860, (Bounds.Width > 100 ? Bounds.Width : Width) - 64);
        double surfaceHeight = Math.Max(460, (Bounds.Height > 100 ? Bounds.Height : Height) - 170);
        double horizontalMargin = 18;
        double panelGap = 18;
        double availableWidth = Math.Max(680, surfaceWidth - (horizontalMargin * 2) - panelGap);
        double fullBodyHeight = Math.Max(300, surfaceHeight - 94);
        double mapWidth = Math.Clamp(availableWidth * 0.38, 300, 560);
        double consoleLeft = horizontalMargin + mapWidth + panelGap;
        double consoleWidth = Math.Max(420, surfaceWidth - consoleLeft - horizontalMargin);

        return panelId switch
        {
            "map" => new DeckPanelState
            {
                PanelId = panelId,
                Left = horizontalMargin,
                Top = 18,
                Width = mapWidth,
                BodyHeight = fullBodyHeight,
                ZIndex = 110,
            },
            "console" => new DeckPanelState
            {
                PanelId = panelId,
                Left = consoleLeft,
                Top = 18,
                Width = consoleWidth,
                BodyHeight = fullBodyHeight,
                ZIndex = 120,
            },
            "ship" => new DeckPanelState
            {
                PanelId = panelId,
                Left = 24,
                Top = 120,
                Width = 340,
                BodyHeight = Math.Min(290, Math.Max(230, surfaceHeight * 0.42)),
                ZIndex = 130,
                Closed = true,
            },
            "intel" => new DeckPanelState
            {
                PanelId = panelId,
                Left = Math.Max(260, surfaceWidth * 0.30),
                Top = Math.Max(120, surfaceHeight * 0.18),
                Width = Math.Min(450, Math.Max(380, surfaceWidth * 0.40)),
                BodyHeight = Math.Min(250, Math.Max(210, surfaceHeight * 0.34)),
                ZIndex = 140,
                Closed = true,
            },
            "logo" => new DeckPanelState
            {
                PanelId = panelId,
                Left = Math.Max(620, surfaceWidth - 314),
                Top = Math.Max(120, surfaceHeight * 0.22),
                Width = 280,
                BodyHeight = Math.Min(220, Math.Max(180, surfaceHeight * 0.28)),
                ZIndex = 150,
                Closed = true,
            },
            _ => new DeckPanelState
            {
                PanelId = panelId,
                Left = 20,
                Top = 20,
                Width = 360,
                BodyHeight = 220,
                ZIndex = _deckNextZIndex++,
            },
        };
    }

    private void BringDeckPanelToFront(FloatingDeckPanel panel)
    {
        panel.ZIndex = _deckNextZIndex++;
        if (_deckPanelStates.TryGetValue(panel.PanelId, out DeckPanelState? state))
            state.ZIndex = panel.ZIndex;
    }

    private void OnDeckPanelStateChanged(FloatingDeckPanel panel)
    {
        if (_suppressDeckPanelStateSync)
            return;

        if (!_deckPanelStates.TryGetValue(panel.PanelId, out DeckPanelState? state))
            return;

        (state.Left, state.Top) = panel.GetPosition();
        state.Width = panel.PanelWidth;
        state.BodyHeight = panel.BodyHeight;
        state.Minimized = panel.IsMinimized;
        state.Closed = panel.IsClosed;
        state.ZIndex = panel.ZIndex;
        _appPrefs.SetDeckPanelLayout(panel.PanelId, state.Left, state.Top, state.Width, state.BodyHeight, state.ZIndex, state.Closed, state.Minimized);
    }

    private void ShowDeckPanel(string panelId)
    {
        EnsureDeckPanelsInitialized();
        if (!_useCommandDeckSkin)
        {
            SetSkin(true);
            return;
        }

        if (_deckPanels.TryGetValue(panelId, out FloatingDeckPanel? panel) &&
            _deckPanelStates.TryGetValue(panelId, out DeckPanelState? state))
        {
            panel.Restore(state.Left, state.Top);
            BringDeckPanelToFront(panel);
        }
    }

    private void RestoreDeckLayout()
    {
        _deckPanelStates.Clear();
        _appPrefs.CommandDeckPanels.Clear();
        _appPrefs.Save();
        if (_useCommandDeckSkin)
            ApplySelectedSkin();
    }

    private (double Left, double Top) GetSnappedDeckPanelPosition(FloatingDeckPanel panel, double proposedLeft, double proposedTop)
    {
        double width = panel.PanelWidth;
        double height = panel.PanelHeight;
        double snappedLeft = GetBestDeckSnap(proposedLeft, GetDeckHorizontalSnapTargets(panel, proposedLeft, proposedTop, width, height));
        double snappedTop = GetBestDeckSnap(proposedTop, GetDeckVerticalSnapTargets(panel, snappedLeft, proposedTop, width, height));
        return (snappedLeft, snappedTop);
    }

    private void EnsureDeckPanelsInitialized()
    {
        if (_deckPanelsInitialized || _deckSurface == null || _deckSurface.Bounds.Width <= 0 || _deckSurface.Bounds.Height <= 0)
            return;

        _suppressDeckPanelStateSync = true;
        try
        {
            foreach ((string panelId, FloatingDeckPanel panel) in _deckPanels)
            {
                if (_deckPanelStates.TryGetValue(panelId, out DeckPanelState? state))
                    ApplyDeckPanelState(panel, state);
            }
        }
        finally
        {
            _suppressDeckPanelStateSync = false;
        }

        _deckPanelsInitialized = true;
        foreach (FloatingDeckPanel panel in _deckPanels.Values)
            OnDeckPanelStateChanged(panel);

        _appPrefs.Save();
    }

    private static void ApplyDeckPanelState(FloatingDeckPanel panel, DeckPanelState state)
    {
        panel.ZIndex = state.ZIndex;
        panel.SetClosed(false);
        panel.SetMinimized(false);
        panel.MoveTo(state.Left, state.Top, clampToHost: false);
        if (state.Minimized)
            panel.SetMinimized(true);
        if (state.Closed)
            panel.SetClosed(true);
    }

    private IEnumerable<double> GetDeckHorizontalSnapTargets(FloatingDeckPanel movingPanel, double proposedLeft, double proposedTop, double movingWidth, double movingHeight)
    {
        yield return 0;
        if (_deckSurface != null)
            yield return Math.Max(0, _deckSurface.Bounds.Width - movingWidth);
        yield return SnapToDeckGrid(proposedLeft);

        foreach (FloatingDeckPanel panel in _deckPanels.Values)
        {
            if (panel == movingPanel || !panel.IsVisible)
                continue;

            (double left, double top) = panel.GetPosition();
            double right = left + panel.PanelWidth;
            double bottom = top + panel.PanelHeight;
            if (!RangesOverlapOrNear(proposedTop, proposedTop + movingHeight, top, bottom, DeckPanelSnapThreshold * 2))
                continue;

            yield return left;
            yield return right - movingWidth;
            yield return right + DeckPanelSnapGap;
            yield return left - movingWidth - DeckPanelSnapGap;
        }
    }

    private IEnumerable<double> GetDeckVerticalSnapTargets(FloatingDeckPanel movingPanel, double proposedLeft, double proposedTop, double movingWidth, double movingHeight)
    {
        yield return 0;
        if (_deckSurface != null)
            yield return Math.Max(0, _deckSurface.Bounds.Height - movingHeight);
        yield return SnapToDeckGrid(proposedTop);

        foreach (FloatingDeckPanel panel in _deckPanels.Values)
        {
            if (panel == movingPanel || !panel.IsVisible)
                continue;

            (double left, double top) = panel.GetPosition();
            double right = left + panel.PanelWidth;
            double bottom = top + panel.PanelHeight;
            if (!RangesOverlapOrNear(proposedLeft, proposedLeft + movingWidth, left, right, DeckPanelSnapThreshold * 2))
                continue;

            yield return top;
            yield return bottom - movingHeight;
            yield return bottom + DeckPanelSnapGap;
            yield return top - movingHeight - DeckPanelSnapGap;
        }
    }

    private static double GetBestDeckSnap(double proposed, IEnumerable<double> targets)
    {
        double snapped = proposed;
        double bestDistance = DeckPanelSnapThreshold + 0.001;

        foreach (double target in targets)
        {
            double distance = Math.Abs(target - proposed);
            if (distance > DeckPanelSnapThreshold || distance >= bestDistance)
                continue;

            snapped = target;
            bestDistance = distance;
        }

        return snapped;
    }

    private static bool RangesOverlapOrNear(double startA, double endA, double startB, double endB, double tolerance)
        => endA >= startB - tolerance && endB >= startA - tolerance;

    private static double SnapToDeckGrid(double proposed)
    {
        return Math.Round(proposed / DeckPanelGridSize) * DeckPanelGridSize;
    }

    private Control BuildDeckBanner()
    {
        _deckHudHeaderSector.FontFamily = HudTitleFont;
        _deckHudHeaderSector.FontSize = 22;
        _deckHudHeaderSector.FontWeight = FontWeight.SemiBold;
        _deckHudHeaderSector.Foreground = HudAccentHot;
        _deckHudHeaderSector.Text = "SECTOR ---";

        var bannerGrid = new Grid();
        bannerGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        bannerGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        bannerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        bannerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        bannerGrid.Children.Add(new StackPanel
        {
            Spacing = 2,
            Children =
            {
                new TextBlock
                {
                    Text = "Mayhem Tradewars Console",
                    FontFamily = HudTitleFont,
                    FontSize = 28,
                    FontWeight = FontWeight.Bold,
                    Foreground = HudAccent,
                },
                new TextBlock
                {
                    Text = "Drag, minimize, or close the internal windows. Gameplay console stays anchored to the deck.",
                    Foreground = HudMuted,
                    FontSize = 13,
                },
            },
        });

        var chips = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 10,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            Children =
            {
                BuildDeckInfoChip("Sector", _deckHudHeaderSector, HudAccentHot),
                BuildDeckInfoChip("Link", _deckHudHeaderConnection, HudAccent),
            },
        };
        Grid.SetColumn(chips, 1);
        bannerGrid.Children.Add(chips);

        var launchBar = new WrapPanel
        {
            ItemSpacing = 8,
            Margin = new Thickness(0, 12, 0, 0),
        };
        launchBar.Children.Add(BuildDeckLauncherButton("Map", () => ShowDeckPanel("map")));
        launchBar.Children.Add(BuildDeckLauncherButton("Console", () => ShowDeckPanel("console")));
        launchBar.Children.Add(BuildDeckLauncherButton("Ship", () => ShowDeckPanel("ship")));
        launchBar.Children.Add(BuildDeckLauncherButton("Intel", () => ShowDeckPanel("intel")));
        launchBar.Children.Add(BuildDeckLauncherButton("Logo", () => ShowDeckPanel("logo")));
        launchBar.Children.Add(BuildDeckLauncherButton("Reset Layout", RestoreDeckLayout));
        Grid.SetRow(launchBar, 1);
        Grid.SetColumnSpan(launchBar, 2);
        bannerGrid.Children.Add(launchBar);

        return new Border
        {
            Background = HudFrame,
            BorderBrush = HudEdge,
            BorderThickness = new Thickness(1.5),
            CornerRadius = new CornerRadius(18),
            Padding = new Thickness(18, 14),
            Child = bannerGrid,
        };
    }

    private Control BuildDeckMapBody()
    {
        var zoomText = new TextBlock();
        Button bubbleButton = null!;
        Button hexButton = null!;
        bubbleButton = BuildDeckToggleToolButton("Modern", () =>
        {
            _tacticalMap?.SetViewMode(TacticalMapViewMode.Bubble);
            UpdateViewButtons();
        }, 78);
        hexButton = BuildDeckToggleToolButton("Hex", () =>
        {
            _tacticalMap?.SetViewMode(TacticalMapViewMode.Hex);
            UpdateViewButtons();
        }, 62);

        void UpdateZoomText()
        {
            zoomText.Text = _tacticalMap != null ? $"{_tacticalMap.ZoomPercent}%" : "--";
        }

        void UpdateViewButtons()
        {
            SetDeckToggleToolButtonState(bubbleButton, _tacticalMap?.ViewMode == TacticalMapViewMode.Bubble);
            SetDeckToggleToolButtonState(hexButton, _tacticalMap?.ViewMode == TacticalMapViewMode.Hex);
        }

        UpdateZoomText();
        if (_tacticalMap != null)
        {
            _tacticalMap.ZoomChanged += _ =>
                Dispatcher.UIThread.Post(UpdateZoomText, DispatcherPriority.Background);
            _tacticalMap.ViewModeChanged += _ =>
                Dispatcher.UIThread.Post(UpdateViewButtons, DispatcherPriority.Background);
        }
        UpdateViewButtons();

        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(10) });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var topBar = new Grid();
        topBar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        topBar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        topBar.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Children =
            {
                BuildDeckInfoChip("Mode", new TextBlock { Text = "Live overlay" }, HudAccentHot),
                new Border
                {
                    Background = HudHeaderAlt,
                    BorderBrush = HudInnerEdge,
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(11),
                    Padding = new Thickness(10, 6),
                    Child = new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        Spacing = 8,
                        Children =
                        {
                            new TextBlock
                            {
                                Text = "VIEW",
                                Foreground = HudMuted,
                                FontSize = 11,
                                FontWeight = FontWeight.SemiBold,
                                VerticalAlignment = VerticalAlignment.Center,
                            },
                            bubbleButton,
                            hexButton,
                        },
                    },
                },
            },
        });

        var zoomBar = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            Children =
            {
                BuildDeckToolButton("-", () =>
                {
                    _tacticalMap?.AdjustZoom(-0.12f);
                    UpdateZoomText();
                }, 34),
                BuildDeckInfoChip("Zoom", zoomText, HudAccent),
                BuildDeckToolButton("+", () =>
                {
                    _tacticalMap?.AdjustZoom(0.12f);
                    UpdateZoomText();
                }, 34),
                BuildDeckToolButton("Reset", () =>
                {
                    _tacticalMap?.ResetZoom();
                    UpdateZoomText();
                }, 62),
            },
        };
        Grid.SetColumn(zoomBar, 1);
        topBar.Children.Add(zoomBar);

        grid.Children.Add(topBar);

        var mapBorder = new Border
        {
            BorderBrush = HudInnerEdge,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(14),
            ClipToBounds = true,
            Child = _tacticalMap!,
        };
        Grid.SetRow(mapBorder, 2);
        grid.Children.Add(mapBorder);

        return grid;
    }

    private Control BuildDeckTerminalBody()
    {
        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        _deckCommSplitterRow = new RowDefinition { Height = new GridLength(0) };
        _deckCommPanelRow = new RowDefinition { Height = new GridLength(0) };
        grid.RowDefinitions.Add(_deckCommSplitterRow);
        grid.RowDefinitions.Add(_deckCommPanelRow);

        grid.Children.Add(BuildDeckInfoChip("Mode", new TextBlock { Text = "Gameplay feed" }, HudAccent));

        var terminalBorder = new Border
        {
            Margin = new Thickness(0, 12, 0, 0),
            MinHeight = 160,
            Background = Brushes.Black,
            BorderBrush = HudInnerEdge,
            BorderThickness = new Thickness(1.5),
            CornerRadius = new CornerRadius(14),
            Padding = new Thickness(6),
            Child = new Border
            {
                Background = Brushes.Black,
                BorderBrush = new SolidColorBrush(Color.FromRgb(24, 54, 66)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Child = _deckTermCtrl,
            },
        };
        Grid.SetRow(terminalBorder, 1);
        grid.Children.Add(terminalBorder);

        var splitter = BuildCommGridSplitter(deckSkin: true);
        _deckCommGridSplitter = splitter;
        Grid.SetRow(splitter, 2);
        grid.Children.Add(splitter);

        var commPanel = BuildCommPanel(deckSkin: true);
        Grid.SetRow(commPanel, 3);
        grid.Children.Add(commPanel);
        ApplyCommWindowVisibility();
        RefreshCommWindowUi();

        return grid;
    }

    private Control BuildDeckShipPanel()
    {
        _deckHudShipName.FontFamily = HudTitleFont;
        _deckHudShipName.FontSize = 22;
        _deckHudShipName.FontWeight = FontWeight.Bold;
        _deckHudShipName.Foreground = HudAccentOk;
        _deckHudShipName.Text = "-";

        _deckHudShipSubtitle.Foreground = HudMuted;
        _deckHudShipSubtitle.FontSize = 12;
        _deckHudShipSubtitle.Text = "Independent captain";

        var badgeGrid = new Grid { Margin = new Thickness(0, 8, 0, 0) };
        badgeGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        badgeGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
        badgeGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        badgeGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
        badgeGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var fightersBadge = BuildDeckStatBadge("FTR", _deckValFighters, HudAccentOk);
        Grid.SetColumn(fightersBadge, 0);
        badgeGrid.Children.Add(fightersBadge);

        var shieldsBadge = BuildDeckStatBadge("SHD", _deckValShields, HudAccent);
        Grid.SetColumn(shieldsBadge, 2);
        badgeGrid.Children.Add(shieldsBadge);

        var holdsBadge = BuildDeckStatBadge("HLD", _deckValHTotal, HudAccentHot);
        Grid.SetColumn(holdsBadge, 4);
        badgeGrid.Children.Add(holdsBadge);

        var devices = new WrapPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            ItemWidth = 88,
            Margin = new Thickness(0, 6, 0, 0),
        };
        devices.Children.Add(BuildDeckDeviceChip("ETH", _deckValEther, HudAccent));
        devices.Children.Add(BuildDeckDeviceChip("BEA", _deckValBeacon, HudAccent));
        devices.Children.Add(BuildDeckDeviceChip("DIS", _deckValDisruptor, HudAccent));
        devices.Children.Add(BuildDeckDeviceChip("PHO", _deckValPhoton, HudAccentHot));
        devices.Children.Add(BuildDeckDeviceChip("ARM", _deckValArmid, HudAccentWarn));
        devices.Children.Add(BuildDeckDeviceChip("LIM", _deckValLimpet, HudAccentWarn));
        devices.Children.Add(BuildDeckDeviceChip("GEN", _deckValGenesis, HudAccentOk));
        devices.Children.Add(BuildDeckDeviceChip("ATO", _deckValAtomic, HudAccentHot));
        devices.Children.Add(BuildDeckDeviceChip("COR", _deckValCorbo, HudAccent));
        devices.Children.Add(BuildDeckDeviceChip("CLK", _deckValCloak, HudAccentOk));

        var hero = new Grid();
        hero.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(68) });
        hero.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        hero.Children.Add(new Border
        {
            Width = 58,
            Height = 58,
            CornerRadius = new CornerRadius(29),
            Background = HudHeader,
            BorderBrush = HudAccent,
            BorderThickness = new Thickness(1.5),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Child = new TextBlock
            {
                Text = "MTC",
                FontFamily = HudTitleFont,
                FontWeight = FontWeight.Bold,
                FontSize = 18,
                Foreground = HudText,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
        });
        var shipIdentity = new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Center,
            Spacing = 2,
            Children = { _deckHudShipName, _deckHudShipSubtitle },
        };
        Grid.SetColumn(shipIdentity, 1);
        hero.Children.Add(shipIdentity);

        var content = new StackPanel
        {
            Spacing = 12,
            Children =
            {
                hero,
                badgeGrid,
                BuildDeckSection("Cargo Grid", new StackPanel
                {
                    Spacing = 6,
                    Children =
                    {
                        BuildDeckMetricRow("Fuel Ore", _deckValFuelOre),
                        BuildDeckMetricRow("Organics", _deckValOrganics),
                        BuildDeckMetricRow("Equipment", _deckValEquipment),
                        BuildDeckMetricRow("Colonists", _deckValColonists),
                        BuildDeckMetricRow("Empty", _deckValEmpty),
                    },
                }),
                BuildDeckSection("Device Rack", devices),
                BuildDeckSection("Aux Systems", new StackPanel
                {
                    Spacing = 4,
                    Children =
                    {
                        BuildDeckScannerRow(),
                    },
                }),
            },
        };

        return new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = content,
        };
    }

    private Control BuildDeckCenterPanels()
    {
        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(12) });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        _deckValName.Foreground = HudText;
        _deckValName.FontSize = 17;
        _deckValName.FontWeight = FontWeight.SemiBold;
        _deckValName.TextAlignment = TextAlignment.Right;
        _deckValName.TextTrimming = TextTrimming.CharacterEllipsis;
        _deckValName.TextWrapping = TextWrapping.NoWrap;
        _deckValName.MinWidth = 0;

        var commander = BuildDeckMetricCard(
            "Commander Link",
            BuildDeckMetricStretchRow("Trader", _deckValName),
            BuildDeckMetricRow("Sector", _deckValSector),
            BuildDeckMetricRow("Turns", _deckValTurns));
        Grid.SetRow(commander, 0);
        Grid.SetColumn(commander, 0);
        grid.Children.Add(commander);

        var economy = BuildDeckMetricCard(
            "Economy",
            ("Credits", _deckValCred),
            ("Experience", _deckValExper),
            ("Alignment", _deckValAlignm));
        Grid.SetRow(economy, 0);
        Grid.SetColumn(economy, 2);
        grid.Children.Add(economy);

        var routes = BuildDeckMetricCard(
            "Route Markers",
            ("StarDock", _deckHudStarDock),
            ("Rylos", _deckHudRylos),
            ("Alpha", _deckHudAlpha),
            ("Universe", _deckHudUniverse));
        Grid.SetRow(routes, 2);
        Grid.SetColumn(routes, 0);
        grid.Children.Add(routes);

        var drives = BuildDeckMetricCard(
            "Drive Core",
            ("Turns/Warp", _deckValTrnWarp),
            ("TW-I", _deckValTW1),
            ("TW-II", _deckValTW2));
        Grid.SetRow(drives, 2);
        Grid.SetColumn(drives, 2);
        grid.Children.Add(drives);

        return grid;
    }

    private Control BuildLogoPanel()
    {
        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var logoWell = new Border
        {
            Background = HudFrameAlt,
            BorderBrush = HudInnerEdge,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(16),
            Padding = new Thickness(18),
            Child = new Grid
            {
                Children =
                {
                    new Border
                    {
                        Margin = new Thickness(12),
                        CornerRadius = new CornerRadius(18),
                        BorderBrush = HudAccent,
                        BorderThickness = new Thickness(1),
                    },
                    new Image
                    {
                        Source = HudLogo,
                        Stretch = Stretch.Uniform,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center,
                        Opacity = 0.92,
                    },
                },
            },
        };
        grid.Children.Add(logoWell);

        var caption = new TextBlock
        {
            Margin = new Thickness(0, 12, 0, 0),
            Text = "Default auxiliary frame. Good home for corp intel, bot telemetry, or message traffic.",
            Foreground = HudMuted,
            TextWrapping = TextWrapping.Wrap,
            FontSize = 12,
        };
        Grid.SetRow(caption, 1);
        grid.Children.Add(caption);

        return grid;
    }

    private Control BuildHudFrame(string title, Control body, string tag)
    {
        var headerGrid = new Grid();
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        headerGrid.Children.Add(new TextBlock
        {
            Text = title,
            FontFamily = HudTitleFont,
            FontSize = 17,
            FontWeight = FontWeight.Bold,
            Foreground = HudAccent,
            VerticalAlignment = VerticalAlignment.Center,
        });

        var tagBorder = new Border
        {
            Background = HudHeaderAlt,
            BorderBrush = HudInnerEdge,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(10, 4),
            Child = new TextBlock
            {
                Text = tag,
                Foreground = HudMuted,
                FontSize = 11,
                FontWeight = FontWeight.SemiBold,
                VerticalAlignment = VerticalAlignment.Center,
            },
        };
        Grid.SetColumn(tagBorder, 1);
        headerGrid.Children.Add(tagBorder);

        var frameGrid = new Grid();
        frameGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        frameGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        frameGrid.Children.Add(new Border
        {
            Background = HudHeader,
            CornerRadius = new CornerRadius(14, 14, 0, 0),
            Padding = new Thickness(14, 10),
            Child = headerGrid,
        });

        var bodyBorder = new Border
        {
            Padding = new Thickness(14),
            Child = body,
        };
        Grid.SetRow(bodyBorder, 1);
        frameGrid.Children.Add(bodyBorder);

        return new Border
        {
            Background = HudFrame,
            BorderBrush = HudEdge,
            BorderThickness = new Thickness(1.4),
            CornerRadius = new CornerRadius(18),
            Padding = new Thickness(2),
            Child = new Border
            {
                Background = HudFrameAlt,
                BorderBrush = HudInnerEdge,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(16),
                Child = frameGrid,
            },
        };
    }

    private Control BuildDeckInfoChip(string label, Control valueControl, IBrush accent)
    {
        if (valueControl is TextBlock valueText)
        {
            valueText.Foreground = HudText;
            valueText.FontFamily = HudTitleFont;
            valueText.FontSize = 15;
            valueText.FontWeight = FontWeight.SemiBold;
            valueText.VerticalAlignment = VerticalAlignment.Center;
        }

        return new Border
        {
            Background = HudHeaderAlt,
            BorderBrush = accent,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(11),
            Padding = new Thickness(10, 6),
            Child = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                Children =
                {
                    new TextBlock
                    {
                        Text = label.ToUpperInvariant(),
                        Foreground = HudMuted,
                        FontSize = 11,
                        FontWeight = FontWeight.SemiBold,
                        VerticalAlignment = VerticalAlignment.Center,
                    },
                    valueControl,
                },
            },
        };
    }

    private Control BuildStatusLocationChip(string label, TextBlock valueBlock, IBrush accent)
    {
        valueBlock.Text = "-";
        valueBlock.Foreground = HudText;
        valueBlock.FontFamily = HudTitleFont;
        valueBlock.FontSize = 13;
        valueBlock.FontWeight = FontWeight.SemiBold;
        valueBlock.VerticalAlignment = VerticalAlignment.Center;
        valueBlock.MinWidth = 28;
        valueBlock.TextAlignment = TextAlignment.Center;

        return new Border
        {
            Background = HudHeaderAlt,
            BorderBrush = accent,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(10, 4),
            Child = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 7,
                Children =
                {
                    new TextBlock
                    {
                        Text = label.ToUpperInvariant(),
                        Foreground = HudMuted,
                        FontSize = 10,
                        FontWeight = FontWeight.SemiBold,
                        VerticalAlignment = VerticalAlignment.Center,
                    },
                    valueBlock,
                },
            },
        };
    }

    private Control BuildStatusLocationChip(string label, string value, IBrush accent)
    {
        string safeLabel = string.IsNullOrWhiteSpace(label) ? "?" : label.Trim();
        var valueBlock = new TextBlock();
        valueBlock.Text = value;
        valueBlock.Foreground = HudText;
        valueBlock.FontFamily = HudTitleFont;
        valueBlock.FontSize = 13;
        valueBlock.FontWeight = FontWeight.SemiBold;
        valueBlock.VerticalAlignment = VerticalAlignment.Center;
        valueBlock.MinWidth = 28;
        valueBlock.TextAlignment = TextAlignment.Center;

        return new Border
        {
            Background = HudHeaderAlt,
            BorderBrush = accent,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(10, 4),
            Child = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 7,
                Children =
                {
                    new TextBlock
                    {
                        Text = safeLabel.ToUpperInvariant(),
                        Foreground = HudMuted,
                        FontSize = 10,
                        FontWeight = FontWeight.SemiBold,
                        VerticalAlignment = VerticalAlignment.Center,
                    },
                    valueBlock,
                },
            },
        };
    }

    private EmbeddedMtcStatusBarConfig GetStatusBarConfigForDisplay()
    {
        EmbeddedMtcStatusBarConfig? config = _embeddedGameConfig?.Mtc?.StatusBar;
        if (config == null)
            return new EmbeddedMtcStatusBarConfig();

        config.CustomSectors = SanitizeStatusSectorChips(config.CustomSectors);
        return config;
    }

    private EmbeddedMtcStatusBarConfig GetOrCreateCurrentStatusBarConfig()
    {
        _embeddedGameConfig ??= new EmbeddedGameConfig();
        _embeddedGameConfig.Mtc ??= new EmbeddedMtcConfig();
        _embeddedGameConfig.Mtc.StatusBar ??= new EmbeddedMtcStatusBarConfig();
        _embeddedGameConfig.Mtc.StatusBar.CustomSectors =
            SanitizeStatusSectorChips(_embeddedGameConfig.Mtc.StatusBar.CustomSectors);
        return _embeddedGameConfig.Mtc.StatusBar;
    }

    private static List<EmbeddedMtcStatusSectorChip> SanitizeStatusSectorChips(
        IEnumerable<EmbeddedMtcStatusSectorChip?>? chips)
    {
        if (chips == null)
            return [];

        return chips
            .Where(static chip => chip != null)
            .Select(static chip => new EmbeddedMtcStatusSectorChip
            {
                Name = (chip!.Name ?? string.Empty).Trim(),
                Sector = chip.Sector,
            })
            .Where(static chip =>
                !string.IsNullOrWhiteSpace(chip.Name) &&
                chip.Sector > 0 &&
                chip.Sector != ushort.MaxValue)
            .ToList();
    }

    private void EnsureFixedStatusLocationChips()
    {
        _statusStarDockChip ??= BuildStatusLocationChip("SD", _statusStarDockValue, HudAccentHot);
        _statusBackdoorChip ??= BuildStatusLocationChip("BD", _statusBackdoorValue, HudAccentWarn);
        _statusRylosChip ??= BuildStatusLocationChip("Rylos", _statusRylosValue, HudAccent);
        _statusAlphaChip ??= BuildStatusLocationChip("Alpha", _statusAlphaValue, HudAccentOk);
    }

    private bool ShouldShowStatusBarHaggleInfo()
        => GetStatusBarConfigForDisplay().ShowHaggleInfo;

    private string BuildStatusBarLayoutSignature()
    {
        EmbeddedMtcStatusBarConfig config = GetStatusBarConfigForDisplay();
        string custom = string.Join("|",
            config.CustomSectors.Select(static chip => $"{chip.Name}:{chip.Sector}"));
        return string.Join(";",
            config.ShowStarDock ? "sd1" : "sd0",
            config.ShowBackdoor ? "bd1" : "bd0",
            config.ShowRylos ? "ry1" : "ry0",
            config.ShowAlpha ? "al1" : "al0",
            custom);
    }

    private void EnsureStatusBarLayout()
    {
        EnsureFixedStatusLocationChips();

        string signature = BuildStatusBarLayoutSignature();
        if (string.Equals(signature, _statusBarLayoutSignature, StringComparison.Ordinal))
            return;

        _statusBarContent.Children.Clear();
        EmbeddedMtcStatusBarConfig config = GetStatusBarConfigForDisplay();

        if (config.ShowStarDock)
            _statusBarContent.Children.Add(_statusStarDockChip!);
        if (config.ShowBackdoor)
            _statusBarContent.Children.Add(_statusBackdoorChip!);
        if (config.ShowRylos)
            _statusBarContent.Children.Add(_statusRylosChip!);
        if (config.ShowAlpha)
            _statusBarContent.Children.Add(_statusAlphaChip!);

        foreach (EmbeddedMtcStatusSectorChip chip in config.CustomSectors)
        {
            if (string.IsNullOrWhiteSpace(chip.Name) || chip.Sector <= 0 || chip.Sector == ushort.MaxValue)
                continue;

            _statusBarContent.Children.Add(BuildStatusLocationChip(chip.Name.Trim(), chip.Sector.ToString(CultureInfo.InvariantCulture), HudAccent));
        }

        _statusText.Margin = _statusBarContent.Children.Count > 0
            ? new Thickness(6, 0, 0, 0)
            : new Thickness(0);
        _statusBarContent.Children.Add(_statusText);
        _statusBarLayoutSignature = signature;
    }

    private void InvalidateStatusBarLayout()
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(InvalidateStatusBarLayout, DispatcherPriority.Background);
            return;
        }

        _statusBarLayoutSignature = string.Empty;
        EnsureStatusBarLayout();
    }

    private Control BuildDeckLauncherButton(string text, Action onClick)
    {
        var button = new Button
        {
            Content = text,
            Background = HudHeaderAlt,
            BorderBrush = HudInnerEdge,
            BorderThickness = new Thickness(1),
            Foreground = HudText,
            Padding = new Thickness(12, 6),
            FontSize = 12,
        };
        button.Click += (_, _) => onClick();
        return button;
    }

    private Control BuildDeckToolButton(string text, Action onClick, double width)
    {
        var button = new Button
        {
            Content = text,
            Width = width,
            Height = 34,
            Background = HudHeaderAlt,
            BorderBrush = HudInnerEdge,
            BorderThickness = new Thickness(1),
            Foreground = HudText,
            Padding = new Thickness(10, 4),
            FontSize = 12,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        button.Click += (_, _) => onClick();
        return button;
    }

    private Button BuildDeckToggleToolButton(string text, Action onClick, double minWidth)
    {
        var button = new Button
        {
            Content = text,
            MinWidth = minWidth,
            Height = 34,
            Background = HudHeaderAlt,
            BorderBrush = HudInnerEdge,
            BorderThickness = new Thickness(1),
            Foreground = HudText,
            Padding = new Thickness(12, 4),
            FontSize = 12,
            FontWeight = FontWeight.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        button.Click += (_, _) => onClick();
        return button;
    }

    private static void ApplyHudActionButtonStyle(Button button, bool primary)
    {
        button.Background = primary ? HudAccent : HudHeaderAlt;
        button.BorderBrush = primary ? HudAccentHot : HudInnerEdge;
        button.BorderThickness = new Thickness(1);
        button.Foreground = primary ? HudAccentInk : HudText;
        button.FontWeight = FontWeight.SemiBold;
        button.CornerRadius = new CornerRadius(10);
        button.Padding = new Thickness(12, 5);
    }

    private static void ApplyHudTextBoxStyle(TextBox textBox)
    {
        textBox.Background = HudHeaderAlt;
        textBox.BorderBrush = HudInnerEdge;
        textBox.BorderThickness = new Thickness(1);
        textBox.Foreground = HudText;
        textBox.CaretBrush = HudAccent;
    }

    private void SetDeckToggleToolButtonState(Button button, bool isActive)
    {
        button.Background = isActive ? HudAccent : HudHeaderAlt;
        button.BorderBrush = isActive ? HudAccentHot : HudInnerEdge;
        button.Foreground = isActive ? HudAccentInk : HudText;
        button.FontWeight = isActive ? FontWeight.Bold : FontWeight.SemiBold;
    }

    private Control BuildDeckStatBadge(string label, TextBlock value, IBrush accent)
    {
        value.Foreground = accent;
        value.FontFamily = HudTitleFont;
        value.FontSize = 24;
        value.FontWeight = FontWeight.Bold;
        value.TextAlignment = TextAlignment.Right;
        value.Text = "0";

        return new Border
        {
            Background = HudFrameAlt,
            BorderBrush = accent,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(14),
            Padding = new Thickness(10, 8),
            Child = new StackPanel
            {
                Spacing = 2,
                Children =
                {
                    new TextBlock
                    {
                        Text = label,
                        Foreground = HudMuted,
                        FontSize = 11,
                        FontWeight = FontWeight.SemiBold,
                    },
                    value,
                },
            },
        };
    }

    private Control BuildDeckDeviceChip(string label, TextBlock value, IBrush accent)
    {
        value.Foreground = HudText;
        value.FontSize = 18;
        value.FontWeight = FontWeight.Bold;
        value.TextAlignment = TextAlignment.Right;
        value.Text = "0";

        var chipGrid = new Grid();
        chipGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        chipGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        chipGrid.Children.Add(new TextBlock
        {
            Text = label,
            Foreground = HudMuted,
            FontSize = 10,
            FontWeight = FontWeight.SemiBold,
        });
        var valueHost = new ContentControl { Content = value };
        Grid.SetRow(valueHost, 1);
        chipGrid.Children.Add(valueHost);

        return new Border
        {
            Margin = new Thickness(0, 0, 8, 8),
            Background = HudHeaderAlt,
            BorderBrush = accent,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(11),
            Padding = new Thickness(8, 6),
            Child = chipGrid,
        };
    }

    private Control BuildDeckSection(string title, Control content)
    {
        return new StackPanel
        {
            Spacing = 8,
            Children =
            {
                new TextBlock
                {
                    Text = title,
                    FontFamily = HudTitleFont,
                    FontSize = 14,
                    FontWeight = FontWeight.SemiBold,
                    Foreground = HudAccent,
                },
                content,
            },
        };
    }

    private Control BuildDeckMetricCard(string title, params (string Label, TextBlock Value)[] rows)
    {
        var builtRows = new List<Control>(rows.Length);
        foreach ((string label, TextBlock value) in rows)
            builtRows.Add(BuildDeckMetricRow(label, value));

        return BuildDeckMetricCard(title, builtRows.ToArray());
    }

    private Control BuildDeckMetricCard(string title, params Control[] rows)
    {
        var stack = new StackPanel { Spacing = 8 };
        foreach (Control row in rows)
            stack.Children.Add(row);

        return new Border
        {
            Background = HudFrameAlt,
            BorderBrush = HudInnerEdge,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(14),
            Padding = new Thickness(12),
            Child = new StackPanel
            {
                Spacing = 12,
                Children =
                {
                    new TextBlock
                    {
                        Text = title,
                        FontFamily = HudTitleFont,
                        FontSize = 14,
                        FontWeight = FontWeight.SemiBold,
                        Foreground = HudAccent,
                    },
                    stack,
                },
            },
        };
    }

    private Control BuildDeckMetricRow(string label, TextBlock value)
    {
        value.Foreground = HudText;
        value.FontSize = 17;
        value.FontWeight = FontWeight.SemiBold;
        value.TextAlignment = TextAlignment.Right;
        value.MinWidth = 60;

        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        Grid.SetColumn(value, 1);
        row.Children.Add(new TextBlock
        {
            Text = label,
            Foreground = HudMuted,
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
        });
        row.Children.Add(value);
        return row;
    }

    private Control BuildDeckMetricStretchRow(string label, TextBlock value)
    {
        value.Foreground = HudText;
        value.FontSize = 17;
        value.FontWeight = FontWeight.SemiBold;
        value.TextAlignment = TextAlignment.Right;
        value.TextTrimming = TextTrimming.CharacterEllipsis;
        value.TextWrapping = TextWrapping.NoWrap;
        value.MinWidth = 0;
        value.HorizontalAlignment = HorizontalAlignment.Stretch;

        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        Grid.SetColumn(value, 2);
        row.Children.Add(new TextBlock
        {
            Text = label,
            Foreground = HudMuted,
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
        });
        row.Children.Add(value);
        return row;
    }

    private void ApplySelectedSkin()
    {
        Background = HudWindow;
        if (_rootDock != null)
            _rootDock.Background = HudWindow;

        _menuBar.Background = HudMenu;
        _menuBar.Foreground = HudText;
        _menuBarHost.Background = HudMenu;
        _statusBar.Background = HudStatus;
        _statusText.Foreground = HudText;
        UpdateTerminalLiveSelector();
        _shellHost.Padding = new Thickness(10, 8, 10, 10);
        _shellHost.Child = null;
        _shellHost.Child = _useCommandDeckSkin
            ? BuildCommandDeckShell()
            : BuildClassicShell();

        RefreshSkinMenuState();
        RefreshInfoPanels();
        Dispatcher.UIThread.Post(FocusActiveTerminal, DispatcherPriority.Input);
    }

    private void ApplySelectedSkinSafe()
    {
        try
        {
            ApplySelectedSkin();
        }
        catch (Exception ex)
        {
            Core.GlobalModules.DebugLog(
                $"[Skin] Failed to apply {(_useCommandDeckSkin ? "command deck" : "classic")} skin: {ex}\n");

            if (_useCommandDeckSkin)
            {
                _useCommandDeckSkin = false;
                _appPrefs.CommandDeckSkinEnabled = false;

                try
                {
                    ApplySelectedSkin();
                }
                catch (Exception fallbackEx)
                {
                    Core.GlobalModules.DebugLog($"[Skin] Failed to restore classic skin: {fallbackEx}\n");
                    throw;
                }

                _parser.Feed($"\x1b[1;31m[Command Deck unavailable: {ex.Message}]\x1b[0m\r\n");
                _buffer.Dirty = true;
                return;
            }

            throw;
        }
    }

    private void SetSkin(bool useCommandDeckSkin)
    {
        if (_useCommandDeckSkin == useCommandDeckSkin && _shellHost.Child != null)
            return;

        _useCommandDeckSkin = useCommandDeckSkin;
        _appPrefs.CommandDeckSkinEnabled = useCommandDeckSkin;
        _appPrefs.Save();
        ApplySelectedSkinSafe();
    }

    private void SetTerminalInputHandler(Action<byte[]> handler)
    {
        _terminalInputHandler = handler;
        _termCtrl.SendInput = handler;
        _deckTermCtrl.SendInput = handler;
        UpdateTemporaryMacroControls();
    }

    private void SetTerminalConnected(bool connected)
    {
        _termCtrl.IsConnected = connected;
        _deckTermCtrl.IsConnected = connected;
        if (!connected)
            ResetTemporaryMacroSession();
        else
            UpdateTemporaryMacroControls();
    }

    private void SetTerminalFont(string familyName)
    {
        _terminalFontFamilyName = familyName;
        _termCtrl.SetFont(familyName);
        _deckTermCtrl.SetFont(familyName);
        FocusActiveTerminal();
    }

    private void SetTerminalFontSize(double size)
    {
        double normalized = GetNearestTerminalFontSize(size);
        if (Math.Abs(_terminalFontSize - normalized) < 0.01)
        {
            RefreshTerminalFontSizeUi();
            FocusActiveTerminal();
            return;
        }

        _terminalFontSize = normalized;
        _termCtrl.SetFontSize(normalized);
        _deckTermCtrl.SetFontSize(normalized);
        RefreshTerminalFontSizeUi();
        FocusActiveTerminal();
    }

    private void StepTerminalFontSize(int direction)
    {
        int currentIndex = Array.FindIndex(
            TerminalFontSizeOptions,
            size => Math.Abs(size - _terminalFontSize) < 0.01);

        if (currentIndex < 0)
        {
            currentIndex = 0;
            double smallestDistance = double.MaxValue;
            for (int i = 0; i < TerminalFontSizeOptions.Length; i++)
            {
                double distance = Math.Abs(TerminalFontSizeOptions[i] - _terminalFontSize);
                if (distance >= smallestDistance)
                    continue;

                smallestDistance = distance;
                currentIndex = i;
            }
        }

        int targetIndex = Math.Clamp(currentIndex + direction, 0, TerminalFontSizeOptions.Length - 1);
        SetTerminalFontSize(TerminalFontSizeOptions[targetIndex]);
    }

    private void RefreshTerminalFontSizeUi()
    {
        _menuFontSizeDecreaseButton.IsEnabled = _terminalFontSize > TerminalFontSizeOptions[0];
        _menuFontSizeIncreaseButton.IsEnabled = _terminalFontSize < TerminalFontSizeOptions[^1];
        _menuFontSizeDecreaseButton.Opacity = _menuFontSizeDecreaseButton.IsEnabled ? 1.0 : 0.45;
        _menuFontSizeIncreaseButton.Opacity = _menuFontSizeIncreaseButton.IsEnabled ? 1.0 : 0.45;

        foreach ((MenuItem item, double size) in _viewFontSizeItems)
        {
            item.Icon = Math.Abs(size - _terminalFontSize) < 0.01
                ? new TextBlock { Text = "●", Foreground = HudAccentOk }
                : null;
        }
    }

    private static double GetNearestTerminalFontSize(double requested)
    {
        double nearest = TerminalFontSizeOptions[0];
        double bestDistance = Math.Abs(nearest - requested);
        for (int i = 1; i < TerminalFontSizeOptions.Length; i++)
        {
            double candidate = TerminalFontSizeOptions[i];
            double distance = Math.Abs(candidate - requested);
            if (distance >= bestDistance)
                continue;

            bestDistance = distance;
            nearest = candidate;
        }

        return nearest;
    }

    private static string FormatTerminalFontSize(double size)
        => size.ToString(size % 1 == 0 ? "0" : "0.0", CultureInfo.InvariantCulture);

    private Control BuildMenuFontSizeBox()
    {
        _menuFontSizeDecreaseButton.MinWidth = 0;
        _menuFontSizeDecreaseButton.Width = 22;
        _menuFontSizeDecreaseButton.Height = 22;
        _menuFontSizeDecreaseButton.Padding = new Thickness(1);
        _menuFontSizeDecreaseButton.VerticalAlignment = VerticalAlignment.Center;
        _menuFontSizeDecreaseButton.HorizontalAlignment = HorizontalAlignment.Center;
        _menuFontSizeDecreaseButton.Background = Brushes.Transparent;
        _menuFontSizeDecreaseButton.BorderBrush = Brushes.Transparent;
        _menuFontSizeDecreaseButton.BorderThickness = new Thickness(0);
        _menuFontSizeDecreaseButton.Content = BuildMenuFontSizeIcon(increase: false);
        _menuFontSizeDecreaseButton.Click += (_, _) => StepTerminalFontSize(-1);
        ToolTip.SetTip(_menuFontSizeDecreaseButton, "Decrease terminal font size");

        _menuFontSizeIncreaseButton.MinWidth = 0;
        _menuFontSizeIncreaseButton.Width = 22;
        _menuFontSizeIncreaseButton.Height = 22;
        _menuFontSizeIncreaseButton.Padding = new Thickness(1);
        _menuFontSizeIncreaseButton.VerticalAlignment = VerticalAlignment.Center;
        _menuFontSizeIncreaseButton.HorizontalAlignment = HorizontalAlignment.Center;
        _menuFontSizeIncreaseButton.Background = Brushes.Transparent;
        _menuFontSizeIncreaseButton.BorderBrush = Brushes.Transparent;
        _menuFontSizeIncreaseButton.BorderThickness = new Thickness(0);
        _menuFontSizeIncreaseButton.Content = BuildMenuFontSizeIcon(increase: true);
        _menuFontSizeIncreaseButton.Click += (_, _) => StepTerminalFontSize(1);
        ToolTip.SetTip(_menuFontSizeIncreaseButton, "Increase terminal font size");

        _menuFontSizeFrame.Padding = new Thickness(5, 2);
        _menuFontSizeFrame.Background = HudHeaderAlt;
        _menuFontSizeFrame.BorderBrush = HudInnerEdge;
        _menuFontSizeFrame.BorderThickness = new Thickness(1);
        _menuFontSizeFrame.CornerRadius = new CornerRadius(8);
        _menuFontSizeFrame.Child = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 2,
            VerticalAlignment = VerticalAlignment.Center,
            Children =
            {
                _menuFontSizeDecreaseButton,
                _menuFontSizeIncreaseButton,
            },
        };

        RefreshTerminalFontSizeUi();
        return _menuFontSizeFrame;
    }

    private static Control BuildMenuFontSizeIcon(bool increase)
    {
        Color accentColor = increase
            ? Color.FromRgb(118, 255, 141)
            : Color.FromRgb(0, 212, 201);

        var accentBrush = new SolidColorBrush(accentColor);
        var haloBrush = new SolidColorBrush(Color.FromArgb(36, accentColor.R, accentColor.G, accentColor.B));
        var sheenBrush = new SolidColorBrush(Color.FromArgb(140, 255, 255, 255));

        var ring = new Border
        {
            Width = 14,
            Height = 14,
            CornerRadius = new CornerRadius(7),
            BorderThickness = new Thickness(1.15),
            BorderBrush = accentBrush,
            Background = haloBrush,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };

        var sheen = new Border
        {
            Width = 6,
            Height = 1,
            CornerRadius = new CornerRadius(1),
            Background = sheenBrush,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 2, 0, 0),
            Opacity = 0.9,
        };

        var horizontalStroke = new Border
        {
            Width = 7,
            Height = 1.8,
            CornerRadius = new CornerRadius(1),
            Background = accentBrush,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };

        var verticalStroke = new Border
        {
            Width = 1.8,
            Height = 7,
            CornerRadius = new CornerRadius(1),
            Background = accentBrush,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };

        var icon = new Grid
        {
            Width = 16,
            Height = 16,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Children =
            {
                ring,
                sheen,
                horizontalStroke,
            },
        };

        if (increase)
            icon.Children.Add(verticalStroke);

        return icon;
    }

    private void FocusActiveTerminal()
    {
        (_useCommandDeckSkin ? _deckTermCtrl : _termCtrl).Focus();
    }

    private void RefreshSkinMenuState()
    {
        _viewClassicSkin.Icon = _useCommandDeckSkin
            ? null
            : new TextBlock { Text = "●", Foreground = HudAccentOk };
        _viewCommandDeckSkin.Icon = _useCommandDeckSkin
            ? new TextBlock { Text = "●", Foreground = HudAccentOk }
            : null;
        RefreshCommWindowMenuState();
        RefreshHaggleDetailsMenuState();
        RefreshBottomBarMenuState();
    }

    private void RefreshCommWindowMenuState()
    {
        _viewCommWindow.Icon = _commWindowVisible
            ? new TextBlock { Text = "●", Foreground = HudAccentOk }
            : null;
    }

    private void RefreshHaggleDetailsMenuState()
    {
        _viewShowHaggleDetails.Icon = ShouldShowStatusBarHaggleInfo()
            ? new TextBlock { Text = "●", Foreground = HudAccentOk }
            : null;
    }

    private void RefreshBottomBarMenuState()
    {
        _viewBottomBar.Icon = _appPrefs.ShowBottomBar
            ? new TextBlock { Text = "●", Foreground = HudAccentOk }
            : null;
    }

    // ── Menu bar ───────────────────────────────────────────────────────────

    private Menu BuildMenuBar()
    {
        var fileNew    = new MenuItem { Header = "_New Connection…" };
        fileNew.Click += (_, _) => _ = OnNewConnectionAsync();

        var fileNewWin    = new MenuItem { Header = "New _Window" };
        fileNewWin.Click += (_, _) => OpenNewWindowInNewProcess();

        var fileEdit = _fileEdit;
        _fileEdit.Click += (_, _) => _ = OnEditConnectionAsync();

        var fileOpen    = new MenuItem { Header = "_Open…" };
        fileOpen.Click += (_, _) => _ = OnOpenConnectionAsync();

        var fileSave    = new MenuItem { Header = "_Save" };
        fileSave.Click += (_, _) => _ = OnSaveConnectionAsync(saveAs: false);

        var fileSaveAs    = new MenuItem { Header = "Save _As…" };
        fileSaveAs.Click += (_, _) => _ = OnSaveConnectionAsync(saveAs: true);

        var fileResetGame = new MenuItem { Header = "_Reset Game…" };
        fileResetGame.Click += (_, _) => _ = OnResetGameAsync();

        var fileConnect    = _fileConnect;
        _fileConnect.Click += (_, _) => OnConnect();

        var fileDisconnect    = _fileDisconnect;
        _fileDisconnect.Click += (_, _) => OnDisconnect();

        var fileQuit    = new MenuItem { Header = "_Quit" };
        fileQuit.Click += (_, _) => Close();

        var filePrefs    = new MenuItem { Header = "_Preferences…" };
        filePrefs.Click += (_, _) => _ = OnPreferencesAsync();

        var fileMacros = new MenuItem { Header = "_Macros…" };
        fileMacros.Click += (_, _) => _ = OnMacrosAsync();

        var fileMenu = new MenuItem
        {
            Header = "_File",
            Items  = { fileNew, fileEdit, fileOpen, _recentMenu, fileSave, fileSaveAs,
                       new Separator(), fileResetGame,
                       new Separator(), fileNewWin,
                       new Separator(), fileConnect, fileDisconnect,
                       new Separator(), fileMacros,
                       new Separator(), filePrefs,
                       new Separator(), fileQuit },
        };

        // Font submenu – enumerate system font families, let user pick one
        var fontSubItems = new List<MenuItem>();
        try
        {
            var families = SKFontManager.Default.GetFontFamilies();
            foreach (var name in families.OrderBy(n => n))
            {
                var fname = name; // capture
                var item  = new MenuItem { Header = fname };
                item.Click += (_, _) => SetTerminalFont(fname);
                fontSubItems.Add(item);
            }
        }
        catch { /* font enumeration not supported on this backend */ }

        var viewFont = new MenuItem { Header = "_Font" };
        if (fontSubItems.Count > 0)
            viewFont.ItemsSource = fontSubItems;
        else
            viewFont.IsEnabled = false;

        _viewFontSizeItems.Clear();
        var fontSizeSubItems = new List<MenuItem>();
        foreach (double size in TerminalFontSizeOptions)
        {
            double selectedSize = size;
            var item = new MenuItem { Header = FormatTerminalFontSize(selectedSize) };
            item.Click += (_, _) => SetTerminalFontSize(selectedSize);
            _viewFontSizeItems.Add((item, selectedSize));
            fontSizeSubItems.Add(item);
        }

        var viewFontSize = new MenuItem
        {
            Header = "Font Si_ze",
            ItemsSource = fontSizeSubItems,
        };
        RefreshTerminalFontSizeUi();

        var viewDbItem = new MenuItem { Header = "_Database..." };
        viewDbItem.Click += (_, _) => OnViewDatabase();

        var viewBubblesItem = new MenuItem { Header = "_Bubbles..." };
        viewBubblesItem.Click += (_, _) => OnViewBubbles();
        _viewClearRecents.Click += (_, _) => OnViewClearRecents();

        _viewClassicSkin.Click += (_, _) => SetSkin(useCommandDeckSkin: false);
        _viewCommandDeckSkin.Click += (_, _) => SetSkin(useCommandDeckSkin: true);
        _viewCommWindow.Click += (_, _) => ToggleCommWindow();
        _viewShowHaggleDetails.Click += async (_, _) => await ToggleShowHaggleDetailsAsync();
        _viewBottomBar.Click += (_, _) => ToggleBottomBar();
        var skinMenu = new MenuItem
        {
            Header = "_Skin",
            Items = { _viewClassicSkin, _viewCommandDeckSkin },
        };

        var viewMenu = new MenuItem
        {
            Header = "_View",
            Items  = { viewFont, viewFontSize, skinMenu, _viewCommWindow, _viewShowHaggleDetails, _viewBottomBar, new Separator(), viewBubblesItem, viewDbItem, new Separator(), _viewClearRecents },
        };

        var helpAbout    = new MenuItem { Header = "_About" };
        helpAbout.Click += (_, _) => _ = ShowAboutAsync();

        var helpMenu = new MenuItem
        {
            Header = "_Help",
            Items  = { helpAbout },
        };

        var mapViewItem = new MenuItem { Header = "_View Map" };
        mapViewItem.Click += (_, _) => OnViewMap();

        var mapMenu = new MenuItem
        {
            Header = "_Map",
            Items  = { mapViewItem },
        };

        var toolsFindRouteItem = new MenuItem { Header = "_Find Route..." };
        toolsFindRouteItem.Click += (_, _) => OnToolsFindRoute();
        var toolsGameInfoItem = new MenuItem { Header = "_Game Info..." };
        toolsGameInfoItem.Click += (_, _) => OnViewGameInfo();
        var toolsConfigureStatusPanelItem = new MenuItem { Header = "Configure Status _Panel..." };
        toolsConfigureStatusPanelItem.Click += async (_, _) => await OnConfigureStatusPanelAsync();
        var toolsConfigureStatusBarItem = new MenuItem { Header = "Configure _Status Bar..." };
        toolsConfigureStatusBarItem.Click += async (_, _) => await OnConfigureStatusBarAsync();
        var toolsScriptDebuggerItem = new MenuItem { Header = "_Script Debugger" };
        toolsScriptDebuggerItem.Click += (_, _) => OnViewScriptDebugger();
        _toolsMenu.ItemsSource = new object[] { toolsFindRouteItem, toolsGameInfoItem, toolsConfigureStatusPanelItem, toolsConfigureStatusBarItem, toolsScriptDebuggerItem };

        var menu = new Menu
        {
            Background = BgSidebar,
            Foreground = FgKey,
            Items      = { fileMenu, _scriptsMenu, _proxyMenu, _botMenu, _quickMenu, _toolsMenu, _aiMenu, mapMenu, viewMenu, helpMenu },
        };

        RefreshCommWindowMenuState();
        return menu;
    }

    private Control BuildMenuBarHost()
    {
        var layout = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
                new ColumnDefinition { Width = GridLength.Auto },
            },
        };

        _menuBar.HorizontalAlignment = HorizontalAlignment.Stretch;
        _menuBar.VerticalAlignment = VerticalAlignment.Stretch;
        Grid.SetColumn(_menuBar, 0);
        layout.Children.Add(_menuBar);

        _statusMacroHost.Margin = new Thickness(0, 4, 0, 4);
        _statusMacroHost.VerticalAlignment = VerticalAlignment.Center;
        _statusMacroHost.Child = BuildStatusMacroBox();

        _menuFontSizeFrame.Margin = new Thickness(0, 4, 0, 4);
        _menuFontSizeFrame.VerticalAlignment = VerticalAlignment.Center;
        BuildMenuFontSizeBox();

        _statusMacrosFrame.Margin = new Thickness(0, 4, 0, 4);
        _statusMacrosFrame.VerticalAlignment = VerticalAlignment.Center;
        _statusMapFrame.Margin = new Thickness(0, 4, 0, 4);
        _statusMapFrame.VerticalAlignment = VerticalAlignment.Center;
        _statusStopAllFrame.Margin = new Thickness(0, 4, 0, 4);
        _statusStopAllFrame.VerticalAlignment = VerticalAlignment.Center;
        _statusCommFrame.Margin = new Thickness(0, 4, 0, 4);
        _statusCommFrame.VerticalAlignment = VerticalAlignment.Center;
        _statusBotFrame.Margin = new Thickness(0, 4, 0, 4);
        _statusBotFrame.VerticalAlignment = VerticalAlignment.Center;
        _statusHaggleFrame.Margin = new Thickness(0, 4, 0, 4);
        _statusHaggleFrame.VerticalAlignment = VerticalAlignment.Center;
        _statusLivePausedFrame.Margin = new Thickness(0, 4, 0, 4);
        _statusLivePausedFrame.VerticalAlignment = VerticalAlignment.Center;
        _statusRedAlertFrame.Margin = new Thickness(0, 4, 8, 4);
        _statusRedAlertFrame.VerticalAlignment = VerticalAlignment.Center;

        var rightTools = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            Children =
            {
                _statusMapFrame,
                _statusBotFrame,
                _statusCommFrame,
                _statusStopAllFrame,
                _statusMacrosFrame,
                _statusMacroHost,
                _statusHaggleFrame,
                _statusLivePausedFrame,
                _statusRedAlertFrame,
            },
        };
        Grid.SetColumn(rightTools, 1);
        layout.Children.Add(rightTools);

        return layout;
    }

    private void OnViewMap()
    {
        if (_mapWindow != null)
        {
            _mapWindow.Show();
            _mapWindow.Activate();
            return;
        }

        _mapWindow = new MapWindow(
            () => _state.Sector,
            () => _sessionDb,
            () => _state);
        _mapWindow.Closed += (_, _) =>
        {
            _mapWindow = null;
            UpdateTerminalLiveSelector();
        };
        _mapWindow.Show();
        _mapWindow.Activate();
        UpdateTerminalLiveSelector();
    }

    private void OnToolsFindRoute()
    {
        var win = new RouteWindow(
            () => _sessionDb,
            () => _state.Sector,
            () => _state);
        win.Show();
    }

    private void OnViewBubbles()
    {
        var win = new BubblesWindow(
            () => _sessionDb,
            () => _state.Sector,
            () => _state,
            () => Math.Max(1, _embeddedGameConfig?.BubbleMinSize ?? 5),
            () => Math.Max(1, _embeddedGameConfig?.BubbleSize ?? Core.ModBubble.DefaultMaxBubbleSize),
            (minSize, maxSize) =>
            {
                _embeddedGameConfig ??= new EmbeddedGameConfig();
                _embeddedGameConfig.BubbleMinSize = Math.Max(1, minSize);
                _embeddedGameConfig.BubbleSize = Math.Max(1, maxSize);
                _embeddedGameConfig.BubbleSizeCustomized = true;
                _ = SaveCurrentGameConfigAsync();
            },
            () => Math.Max(1, _embeddedGameConfig?.DeadEndMinSize ?? 2),
            () => Math.Max(1, _embeddedGameConfig?.DeadEndMaxSize ?? Core.ModBubble.DefaultMaxBubbleSize),
            (minSize, maxSize) =>
            {
                _embeddedGameConfig ??= new EmbeddedGameConfig();
                _embeddedGameConfig.DeadEndMinSize = Math.Max(1, minSize);
                _embeddedGameConfig.DeadEndMaxSize = Math.Max(1, maxSize);
                _ = SaveCurrentGameConfigAsync();
            },
            () => Math.Max(1, _embeddedGameConfig?.TunnelMinSize ?? 2),
            () => Math.Max(1, _embeddedGameConfig?.TunnelMaxSize ?? Core.ModBubble.DefaultMaxBubbleSize),
            (minSize, maxSize) =>
            {
                _embeddedGameConfig ??= new EmbeddedGameConfig();
                _embeddedGameConfig.TunnelMinSize = Math.Max(1, minSize);
                _embeddedGameConfig.TunnelMaxSize = Math.Max(1, maxSize);
                _ = SaveCurrentGameConfigAsync();
            });
        win.Show();
    }

    private void OnViewClearRecents()
    {
        _appPrefs.RecentFiles.Clear();
        _appPrefs.Save();
        RebuildRecentMenu();
    }

    private void QueueFinderPrewarm(Core.ModDatabase? db)
    {
        if (db == null)
            return;

        int bubbleMaxSize = Math.Max(1, _embeddedGameConfig?.BubbleSize ?? Core.ModBubble.DefaultMaxBubbleSize);
        int deadEndMaxSize = Math.Max(1, _embeddedGameConfig?.DeadEndMaxSize ?? Core.ModBubble.DefaultMaxBubbleSize);
        int tunnelMaxSize = Math.Max(1, _embeddedGameConfig?.TunnelMaxSize ?? Core.ModBubble.DefaultMaxBubbleSize);
        const bool allowSeparatedByGates = true;

        FinderPrewarmKey prewarmKey = new(
            db.DatabasePath,
            db.ChangeStamp,
            bubbleMaxSize,
            deadEndMaxSize,
            tunnelMaxSize,
            allowSeparatedByGates);

        lock (_finderPrewarmSync)
        {
            if (_lastFinderPrewarmKey == prewarmKey)
                return;

            _lastFinderPrewarmKey = prewarmKey;
        }

        _ = Task.Run(() =>
        {
            try
            {
                Core.GlobalModules.DebugLog(
                    $"[MTC.FinderPrewarm] start db={db.DatabasePath} bubbleMax={bubbleMaxSize} deadEndMax={deadEndMaxSize} tunnelMax={tunnelMaxSize} allowSeparated={allowSeparatedByGates}\n");
                _ = Core.ProxyGameOperations.GetBubbles(db, bubbleMaxSize, allowSeparatedByGates);
                _ = Core.ProxyGameOperations.GetDeadEnds(db, deadEndMaxSize);
                _ = Core.ProxyGameOperations.GetTunnels(db, tunnelMaxSize);
                Core.GlobalModules.DebugLog($"[MTC.FinderPrewarm] done db={db.DatabasePath}\n");
            }
            catch (Exception ex)
            {
                Core.GlobalModules.DebugLog($"[MTC.FinderPrewarm] failed: {ex}\n");
            }
        });
    }

    private void OnViewDatabase()
    {
        var win = new SectorInfoWindow(
            () => _sessionDb,
            () => _state.Sector);
        win.Show();
    }

    private void OnViewGameInfo()
    {
        var win = new GameInfoWindow(() => _sessionDb, () => _state);
        win.Show();
    }

    private void OnViewScriptDebugger()
    {
        if (_scriptDebuggerWindow is { IsVisible: true })
        {
            _scriptDebuggerWindow.Activate();
            return;
        }

        _scriptDebuggerWindow = new ScriptDebuggerWindow(
            () => CurrentInterpreter,
            () => DeriveGameName(),
            scriptId => Core.ProxyGameOperations.PauseScriptById(CurrentInterpreter, scriptId),
            scriptId => Core.ProxyGameOperations.ResumeScriptById(CurrentInterpreter, scriptId));
        _scriptDebuggerWindow.Closed += (_, _) => _scriptDebuggerWindow = null;
        _scriptDebuggerWindow.Show(this);
    }

    // ── Sidebar ────────────────────────────────────────────────────────────

    private Control BuildSidebar()
    {
        var stack = new StackPanel
        {
            Background  = Brushes.Transparent,
            Orientation = Orientation.Vertical,
            Margin      = new Thickness(0, 0, 0, 0),
        };

        foreach (AppPreferences.StatusPanelSectionPreference section in _appPrefs.GetOrderedStatusPanelSections())
        {
            if (!section.Visible)
                continue;

            switch (section.Id)
            {
                case AppPreferences.StatusPanelTrader:
                    stack.Children.Add(BuildTraderInfoPanel());
                    break;
                case AppPreferences.StatusPanelHolds:
                    stack.Children.Add(BuildHoldsPanel());
                    break;
                case AppPreferences.StatusPanelShipInfo:
                    stack.Children.Add(BuildShipInfoPanel());
                    break;
            }
        }

        var scroll = new ScrollViewer
        {
            VerticalScrollBarVisibility   = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
            Content = stack,
        };

        // Wrap in a border that gives a raised look against the gray chrome
        var outer = new Border
        {
            Background = HudFrame,
            BorderBrush = HudEdge,
            BorderThickness = new Thickness(1.4),
            CornerRadius = new CornerRadius(18),
            Padding = new Thickness(2),
            Child = new Border
            {
                Background = HudFrameAlt,
                BorderBrush = HudInnerEdge,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(16),
                Child = scroll,
            },
        };

        return outer;
    }

    private bool HasVisibleStatusPanelSections()
        => _appPrefs.GetOrderedStatusPanelSections().Any(section => section.Visible);

    private Control BuildTraderInfoPanel()
    {
        var panel = new StackPanel
        {
            Background = Brushes.Transparent,
            Orientation = Orientation.Vertical,
            Margin = new Thickness(0, 0, 0, 3),
        };

        _valName.Foreground = HudText;
        _valName.FontSize = 14;
        _valName.FontWeight = FontWeight.SemiBold;
        _valName.TextAlignment = TextAlignment.Right;
        _valName.TextTrimming = TextTrimming.CharacterEllipsis;
        _valName.TextWrapping = TextWrapping.NoWrap;
        _valName.MinWidth = 0;
        _valName.HorizontalAlignment = HorizontalAlignment.Stretch;
        _valName.VerticalAlignment = VerticalAlignment.Center;
        _valName.Margin = new Thickness(0, 0, 10, 0);

        var headerGrid = new Grid();
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var headerTitle = new TextBlock
        {
            Text = "Trader",
            Foreground = HudAccent,
            FontFamily = HudTitleFont,
            FontSize = 14,
            FontWeight = Avalonia.Media.FontWeight.SemiBold,
            Margin = new Thickness(10, 6, 0, 6),
            VerticalAlignment = VerticalAlignment.Center,
        };

        Grid.SetColumn(headerTitle, 0);
        Grid.SetColumn(_valName, 2);
        headerGrid.Children.Add(headerTitle);
        headerGrid.Children.Add(_valName);

        panel.Children.Add(new Border
        {
            Background = HudHeader,
            Child = headerGrid,
        });

        panel.Children.Add(new Border
        {
            Background = HudInnerEdge,
            Height = 1,
            Margin = new Thickness(0),
        });

        _sectorBustIndicator = new Border
        {
            Background = HudBustBg,
            BorderBrush = HudAccentWarn,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(3),
            Padding = new Thickness(6, 1),
            Margin = new Thickness(0, 0, 6, 0),
            IsVisible = false,
            Child = new TextBlock
            {
                Text = "BUST",
                Foreground = Brushes.White,
                FontSize = 10,
                FontWeight = FontWeight.Bold,
                VerticalAlignment = VerticalAlignment.Center,
            },
        };

        var sectorValueHost = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Right,
            Children = { _sectorBustIndicator, _valSector },
        };
        panel.Children.Add(BuildPanelRow("Sector", sectorValueHost, _valSector));
        panel.Children.Add(BuildPanelRow("Turns", _valTurns));
        panel.Children.Add(BuildPanelRow("Exper.", _valExper));
        panel.Children.Add(BuildPanelRow("Alignm.", _valAlignm));
        panel.Children.Add(BuildPanelRow("Cred.", _valCred));

        panel.Children.Add(new Border { Height = 8 });
        return new Border
        {
            Background = HudFrame,
            BorderBrush = HudEdge,
            BorderThickness = new Thickness(1.4),
            CornerRadius = new CornerRadius(18),
            Padding = new Thickness(2),
            Margin = new Thickness(0, 0, 0, 3),
            Child = new Border
            {
                Background = HudFrameAlt,
                BorderBrush = HudInnerEdge,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(16),
                Child = panel,
            },
        };
    }

    private Control BuildHoldsPanel()
    {
        _valHTotal.Foreground = HudText;
        _valHTotal.FontSize = 14;
        _valHTotal.FontWeight = FontWeight.SemiBold;
        _valHTotal.VerticalAlignment = VerticalAlignment.Center;
        _valHTotal.Margin = new Thickness(0, 0, 6, 0);

        var headerGrid = new Grid();
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var headerTitle = new TextBlock
        {
            Text = "Holds",
            Foreground = HudAccent,
            FontFamily = HudTitleFont,
            FontSize = 14,
            FontWeight = FontWeight.SemiBold,
            Margin = new Thickness(10, 6, 8, 6),
        };

        Grid.SetColumn(headerTitle, 0);
        Grid.SetColumn(_valHTotal, 1);
        headerGrid.Children.Add(headerTitle);
        headerGrid.Children.Add(_valHTotal);

        var panel = new StackPanel
        {
            Background = Brushes.Transparent,
            Orientation = Orientation.Vertical,
            Margin = new Thickness(0, 0, 0, 3),
            Children =
            {
                new Border
                {
                    Background = HudHeader,
                    Child = headerGrid,
                },
                new Border
                {
                    Background = HudInnerEdge,
                    Height = 1,
                    Margin = new Thickness(0),
                },
                BuildHoldsStackedBar(),
                BuildHoldsLegendCompact(),
                new Border { Height = 4 },
            },
        };

        return new Border
        {
            Background = HudFrame,
            BorderBrush = HudEdge,
            BorderThickness = new Thickness(1.4),
            CornerRadius = new CornerRadius(18),
            Padding = new Thickness(2),
            Margin = new Thickness(0, 0, 0, 3),
            Child = new Border
            {
                Background = HudFrameAlt,
                BorderBrush = HudInnerEdge,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(16),
                Child = panel,
            },
        };
    }

    private Control BuildHoldsStackedBar()
    {
        _holdsFuelOreColumn = new ColumnDefinition { Width = new GridLength(0, GridUnitType.Star) };
        _holdsOrganicsColumn = new ColumnDefinition { Width = new GridLength(0, GridUnitType.Star) };
        _holdsEquipmentColumn = new ColumnDefinition { Width = new GridLength(0, GridUnitType.Star) };
        _holdsColonistsColumn = new ColumnDefinition { Width = new GridLength(0, GridUnitType.Star) };
        _holdsEmptyColumn = new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) };

        var segments = new Grid
        {
            ColumnDefinitions =
            {
                _holdsFuelOreColumn,
                _holdsOrganicsColumn,
                _holdsEquipmentColumn,
                _holdsColonistsColumn,
                _holdsEmptyColumn,
            },
            ClipToBounds = true,
        };

        _holdsFuelOreSegment = AddHoldsSegment(segments, HoldsOreBrush, 0);
        _holdsOrganicsSegment = AddHoldsSegment(segments, HoldsOrgBrush, 1);
        _holdsEquipmentSegment = AddHoldsSegment(segments, HoldsEqBrush, 2);
        _holdsColonistsSegment = AddHoldsSegment(segments, HoldsColsBrush, 3);
        _holdsEmptySegment = AddHoldsSegment(segments, HoldsFreeBrush, 4);

        return new Border
        {
            Margin = new Thickness(10, 8, 10, 3),
            Height = 14,
            Background = HudStatus,
            BorderBrush = HudInnerEdge,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(1),
            Child = segments,
        };
    }

    private static Border AddHoldsSegment(Grid grid, IBrush brush, int column)
    {
        var segment = new Border
        {
            Background = brush,
        };
        Grid.SetColumn(segment, column);
        grid.Children.Add(segment);
        return segment;
    }

    private Control BuildHoldsLegendCompact()
    {
        var legend = new WrapPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(10, 0, 10, 0),
        };
        legend.Children.Add(BuildHoldsLegendItem("Ore", HoldsOreBrush));
        legend.Children.Add(BuildHoldsLegendItem("Org", HoldsOrgBrush));
        legend.Children.Add(BuildHoldsLegendItem("Equ", HoldsEqBrush));
        legend.Children.Add(BuildHoldsLegendItem("Colo", HoldsColsBrush));
        legend.Children.Add(BuildHoldsLegendItem("Free", HoldsFreeBrush));
        return legend;
    }

    private Control BuildHoldsLegendItem(string label, IBrush chipBrush)
    {
        var row = new Grid
        {
            Margin = new Thickness(0, 1, 12, 1),
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(new GridLength(3)),
                new ColumnDefinition(GridLength.Auto),
            },
            Children =
            {
                BuildLegendSwatch(chipBrush),
                new TextBlock
                {
                    Text = label,
                    Foreground = HudMuted,
                    FontSize = 11,
                    FontWeight = FontWeight.SemiBold,
                    VerticalAlignment = VerticalAlignment.Center,
                },
            },
        };

        Grid.SetColumn(row.Children[0], 0);
        Grid.SetColumn(row.Children[1], 2);
        return row;
    }

    private static Control BuildLegendSwatch(IBrush brush)
    {
        return new Border
        {
            Width = 10,
            Height = 10,
            CornerRadius = new CornerRadius(2),
            Background = brush,
            VerticalAlignment = VerticalAlignment.Center,
        };
    }

    // ── Expanded Ship Info panel ───────────────────────────────────────────

    private Control BuildShipInfoPanel()
    {
        var panel = new StackPanel { Background = Brushes.Transparent, Orientation = Orientation.Vertical, Margin = new Thickness(0, 0, 0, 3) };

        // Title header
        panel.Children.Add(new Border
        {
            Background = HudHeader,
            Child = new TextBlock { Text = "Ship Info", Foreground = HudAccent, FontFamily = HudTitleFont, FontSize = 14, FontWeight = Avalonia.Media.FontWeight.SemiBold, Margin = new Thickness(10, 6, 8, 6) },
        });
        panel.Children.Add(new Border { Background = HudInnerEdge, Height = 1 });

        // Full-width rows: Fighters, Shields, Turns/Warp
        foreach (var (key, tb) in new (string, TextBlock)[] {
            ("Fighters",   _valFighters),
            ("Shields",    _valShields),
            ("Turns/Warp", _valTrnWarp),
        })
        {
            tb.Text = "-"; tb.Foreground = HudText; tb.FontSize = 13;
            tb.TextAlignment = TextAlignment.Right; tb.MinWidth = 70;
            var keyTb = new TextBlock { Text = key, Foreground = HudMuted, FontSize = 13, VerticalAlignment = VerticalAlignment.Center };
            var row = new Grid { Margin = new Thickness(6, 2, 6, 2) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Grid.SetColumn(keyTb, 0); Grid.SetColumn(tb, 1);
            row.Children.Add(keyTb); row.Children.Add(tb);
            panel.Children.Add(row);
        }

        // Divider before compact equipment rows
        panel.Children.Add(new Border { Background = HudInnerEdge, Height = 1, Margin = new Thickness(0, 2, 0, 1) });

        // Paired rows: two equipment items per line
        foreach (var (k1, v1, k2, v2) in new (string, TextBlock, string, TextBlock)[] {
            ("Ethr", _valEther,     "Bea",  _valBeacon),
            ("Disr", _valDisruptor, "Pho",  _valPhoton),
            ("Arm",  _valArmid,     "Lim",  _valLimpet),
            ("Gen",  _valGenesis,   "Ato",  _valAtomic),
            ("Corb", _valCorbo,     "Clo",  _valCloak),
        })
            panel.Children.Add(BuildPairedRow(k1, v1, k2, v2));

        // Divider before scanners
        panel.Children.Add(new Border { Background = HudInnerEdge, Height = 1, Margin = new Thickness(0, 2, 0, 1) });

        // Scanner indicators
        panel.Children.Add(BuildScannerRow());
        panel.Children.Add(new Border { Height = 8 });

        return new Border
        {
            Background = HudFrame,
            BorderBrush = HudEdge,
            BorderThickness = new Thickness(1.4),
            CornerRadius = new CornerRadius(18),
            Padding = new Thickness(2),
            Margin = new Thickness(0, 0, 0, 3),
            Child = new Border
            {
                Background = HudFrameAlt,
                BorderBrush = HudInnerEdge,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(16),
                Child = panel,
            },
        };
    }

    private Control BuildPairedRow(string k1, TextBlock v1, string k2, TextBlock v2)
    {
        static Grid MakeHalf(string key, TextBlock valTb)
        {
            valTb.Text = "0"; valTb.Foreground = HudText; valTb.FontSize = 12;
            valTb.TextAlignment = TextAlignment.Right; valTb.MinWidth = 30;
            var keyTb = new TextBlock { Text = key, Foreground = HudMuted, FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Grid.SetColumn(keyTb, 0); Grid.SetColumn(valTb, 1);
            g.Children.Add(keyTb); g.Children.Add(valTb);
            return g;
        }
        var row = new Grid { Margin = new Thickness(6, 1, 6, 1) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(4, GridUnitType.Pixel) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var left = MakeHalf(k1, v1); var right = MakeHalf(k2, v2);
        Grid.SetColumn(left, 0); Grid.SetColumn(right, 2);
        row.Children.Add(left); row.Children.Add(right);
        return row;
    }

    private Control BuildScannerRow()
    {
        static Border MakeScanInd(string label, double width) => new Border
        {
            Width = width, Height = 18, CornerRadius = new CornerRadius(2),
            Background = HudHeaderAlt, Margin = new Thickness(2, 0),
            BorderBrush = HudInnerEdge,
            BorderThickness = new Thickness(1),
            Child = new TextBlock
            {
                Text = label, FontSize = 11, FontWeight = Avalonia.Media.FontWeight.Bold,
                Foreground = HudMuted,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(2, 1),
            },
        };
        _scanIndTW1 = MakeScanInd("TW1", 28);
        _scanIndTW2 = MakeScanInd("TW2", 28);
        _scanIndD = MakeScanInd("D", 20);
        _scanIndH = MakeScanInd("H", 20);
        _scanIndP = MakeScanInd("P", 20);
        var indicators = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        indicators.Children.Add(_scanIndTW1);
        indicators.Children.Add(_scanIndTW2);
        indicators.Children.Add(_scanIndD);
        indicators.Children.Add(_scanIndH);
        indicators.Children.Add(_scanIndP);
        return new Border
        {
            Margin = new Thickness(6, 2, 6, 3),
            Child = indicators,
        };
    }

    private Button CreateMacroControlButton(string glyph, string toolTip, bool deckSkin, Action onClick, bool compact = false)
    {
        var button = new Button
        {
            Content = glyph,
            Width = compact ? 22 : deckSkin ? 34 : 30,
            Height = compact ? 18 : deckSkin ? 30 : 26,
            Padding = Thickness.Parse("0"),
            FontSize = glyph == "●"
                ? (compact ? 14 : deckSkin ? 22 : 20)
                : (compact ? 11 : deckSkin ? 18 : 16),
            FontWeight = FontWeight.SemiBold,
            Background = Brushes.Transparent,
            BorderBrush = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Foreground = HudMuted,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            CornerRadius = new CornerRadius(999),
        };

        ToolTip.SetTip(button, toolTip);
        button.Click += (_, _) => onClick();
        return button;
    }

    private Control BuildStatusMacroBox()
    {
        Button recordButton = CreateMacroControlButton("●", "Record Quick Macro", deckSkin: false, StartTemporaryMacroRecording, compact: true);
        Button stopButton = CreateMacroControlButton("■", "Stop Recording", deckSkin: false, StopTemporaryMacroRecording, compact: true);
        Button playButton = CreateMacroControlButton("▶", "Play Quick Macro", deckSkin: false, () => _ = PlayTemporaryMacroAsync(), compact: true);

        _macroRecordButton = recordButton;
        _macroStopButton = stopButton;
        _macroPlayButton = playButton;

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 2,
            VerticalAlignment = VerticalAlignment.Center,
            Children = { recordButton, stopButton, playButton },
        };

        UpdateTemporaryMacroControls();

        return new Border
        {
            Background = HudHeaderAlt,
            BorderBrush = HudInnerEdge,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(6, 2),
            VerticalAlignment = VerticalAlignment.Center,
            Child = buttons,
        };
    }

    private Control BuildMacroRow(bool deckSkin)
    {
        Button recordButton = CreateMacroControlButton("●", "Record Quick Macro", deckSkin, StartTemporaryMacroRecording);
        Button stopButton = CreateMacroControlButton("■", "Stop Recording", deckSkin, StopTemporaryMacroRecording);
        Button playButton = CreateMacroControlButton("▶", "Play Quick Macro", deckSkin, () => _ = PlayTemporaryMacroAsync());

        if (deckSkin)
        {
            _deckMacroRecordButton = recordButton;
            _deckMacroStopButton = stopButton;
            _deckMacroPlayButton = playButton;
        }
        else
        {
            _macroRecordButton = recordButton;
            _macroStopButton = stopButton;
            _macroPlayButton = playButton;
        }

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = deckSkin ? 6 : 5,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = deckSkin ? new Thickness(0, 2, 0, 3) : new Thickness(6, 2, 6, 3),
            Children = { recordButton, stopButton, playButton },
        };

        UpdateTemporaryMacroControls();
        return buttons;
    }

    private Control BuildDeckScannerRow()
    {
        static Border MakeScanInd(string label, double width) => new Border
        {
            Width = width,
            Height = 20,
            CornerRadius = new CornerRadius(6),
            Background = HudHeaderAlt,
            BorderBrush = HudInnerEdge,
            BorderThickness = new Thickness(1),
            Margin = new Thickness(2, 0),
            Child = new TextBlock
            {
                Text = label,
                FontSize = 11,
                FontWeight = FontWeight.Bold,
                Foreground = HudMuted,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
        };

        _deckScanIndTW1 = MakeScanInd("TW1", 36);
        _deckScanIndTW2 = MakeScanInd("TW2", 36);
        _deckScanIndD = MakeScanInd("D", 24);
        _deckScanIndH = MakeScanInd("H", 24);
        _deckScanIndP = MakeScanInd("P", 24);

        var indicators = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Right,
            Children = { _deckScanIndTW1, _deckScanIndTW2, _deckScanIndD, _deckScanIndH, _deckScanIndP },
        };

        return new Border
        {
            Margin = new Thickness(0, 2, 0, 3),
            Child = indicators,
        };
    }

    private void UpdateTemporaryMacroControls()
    {
        int encodedLength = GetTemporaryMacroText().Length;

        ConfigureMacroControlSet(_macroRecordButton, _macroStopButton, _macroPlayButton, deckSkin: false, encodedLength);
        ConfigureMacroControlSet(_deckMacroRecordButton, _deckMacroStopButton, _deckMacroPlayButton, deckSkin: true, encodedLength);
    }

    private bool HasActiveMacroConnection()
    {
        if (_terminalInputHandler == null)
            return false;

        if (_gameInstance?.IsConnected == true)
            return true;

        if (_telnet.IsConnected)
            return true;

        return _state.Connected;
    }

    private void ConfigureMacroControlSet(Button? recordButton, Button? stopButton, Button? playButton, bool deckSkin, int encodedLength)
    {
        if (recordButton == null || stopButton == null || playButton == null)
            return;

        bool connected = HasActiveMacroConnection();
        bool hasMacro = encodedLength > 0;

        recordButton.IsEnabled = connected && !_temporaryMacroRecording;
        stopButton.IsEnabled = _temporaryMacroRecording;
        playButton.IsEnabled = connected && !_temporaryMacroRecording && hasMacro;

        recordButton.Background = Brushes.Transparent;
        recordButton.BorderBrush = Brushes.Transparent;
        stopButton.Background = Brushes.Transparent;
        stopButton.BorderBrush = Brushes.Transparent;
        playButton.Background = Brushes.Transparent;
        playButton.BorderBrush = Brushes.Transparent;

        IBrush recordIdle = new SolidColorBrush(Color.FromRgb(224, 76, 76));
        IBrush recordActive = new SolidColorBrush(Color.FromRgb(255, 54, 54));
        IBrush stopActive = new SolidColorBrush(Color.FromRgb(255, 214, 120));
        IBrush playActive = new SolidColorBrush(Color.FromRgb(96, 225, 138));
        IBrush muted = HudMuted;

        recordButton.Foreground = _temporaryMacroRecording ? recordActive : recordIdle;
        stopButton.Foreground = stopButton.IsEnabled ? stopActive : muted;
        playButton.Foreground = playButton.IsEnabled ? playActive : muted;

        recordButton.Opacity = _temporaryMacroRecording ? 1.0 : (recordButton.IsEnabled ? 0.92 : 0.30);
        stopButton.Opacity = stopButton.IsEnabled ? 0.95 : 0.26;
        playButton.Opacity = playButton.IsEnabled ? 0.95 : 0.26;
    }

    private static void UpdateScanInd(Border b, bool active)
    {
        b.Background = active ? ScannerActive : ScannerInactive;
        if (b.Child is TextBlock tb)
            tb.Foreground = active ? Brushes.Black : ScannerFgInact;
    }

    private static void UpdateDeckScanInd(Border b, bool active)
    {
        b.Background = active ? HudAccentOk : HudHeaderAlt;
        b.BorderBrush = active ? HudAccent : HudInnerEdge;
        if (b.Child is TextBlock tb)
            tb.Foreground = active ? Brushes.Black : HudMuted;
    }

    private void UpdateHoldsStackedBar(int fuelOre, int organics, int equipment, int colonists, int empty, int total)
    {
        int safeFuelOre = Math.Max(0, fuelOre);
        int safeOrganics = Math.Max(0, organics);
        int safeEquipment = Math.Max(0, equipment);
        int safeColonists = Math.Max(0, colonists);
        int safeEmpty = Math.Max(0, empty);

        int displayTotal = Math.Max(total, safeFuelOre + safeOrganics + safeEquipment + safeColonists + safeEmpty);
        if (displayTotal <= 0)
        {
            _holdsFuelOreColumn.Width = new GridLength(0, GridUnitType.Star);
            _holdsOrganicsColumn.Width = new GridLength(0, GridUnitType.Star);
            _holdsEquipmentColumn.Width = new GridLength(0, GridUnitType.Star);
            _holdsColonistsColumn.Width = new GridLength(0, GridUnitType.Star);
            _holdsEmptyColumn.Width = new GridLength(1, GridUnitType.Star);
            return;
        }

        _holdsFuelOreColumn.Width = new GridLength(safeFuelOre, GridUnitType.Star);
        _holdsOrganicsColumn.Width = new GridLength(safeOrganics, GridUnitType.Star);
        _holdsEquipmentColumn.Width = new GridLength(safeEquipment, GridUnitType.Star);
        _holdsColonistsColumn.Width = new GridLength(safeColonists, GridUnitType.Star);
        _holdsEmptyColumn.Width = new GridLength(safeEmpty, GridUnitType.Star);
    }

    private void UpdateHoldsSegmentTooltips(int fuelOre, int organics, int equipment, int colonists, int empty)
    {
        SetHoldsSegmentToolTip(_holdsFuelOreSegment, "Ore", fuelOre);
        SetHoldsSegmentToolTip(_holdsOrganicsSegment, "Org", organics);
        SetHoldsSegmentToolTip(_holdsEquipmentSegment, "Equ", equipment);
        SetHoldsSegmentToolTip(_holdsColonistsSegment, "Colo", colonists);
        SetHoldsSegmentToolTip(_holdsEmptySegment, "Free", empty);
    }

    private static void SetHoldsSegmentToolTip(Border? segment, string label, int value)
    {
        if (segment == null)
            return;

        ToolTip.SetTip(segment, $"{label}: {Math.Max(0, value):N0}");
    }

    /// <summary>Builds a titled info panel containing key/value rows.</summary>
    private Control BuildPanel(string title, (string Key, TextBlock Value)[] rows, TextBlock? headerValue = null)
    {
        var panel = new StackPanel
        {
            Background  = Brushes.Transparent,
            Orientation = Orientation.Vertical,
            Margin      = new Thickness(0, 0, 0, 3),
        };

        // Title row – dark background strip to contrast against gray panel body
        if (headerValue != null)
        {
            headerValue.Foreground        = HudText;
            headerValue.FontSize          = 14;
            headerValue.FontWeight        = Avalonia.Media.FontWeight.SemiBold;
            headerValue.VerticalAlignment = VerticalAlignment.Center;
            headerValue.Margin            = new Thickness(0, 0, 6, 0);

            var hdrGrid = new Grid();
            hdrGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            hdrGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var hdrTitle = new TextBlock
            {
                Text       = title,
                Foreground = HudAccent,
                FontFamily = HudTitleFont,
                FontSize   = 14,
                FontWeight = Avalonia.Media.FontWeight.SemiBold,
                Margin     = new Thickness(10, 6, 8, 6),
            };
            Grid.SetColumn(hdrTitle,  0);
            Grid.SetColumn(headerValue, 1);
            hdrGrid.Children.Add(hdrTitle);
            hdrGrid.Children.Add(headerValue);
            panel.Children.Add(new Border { Background = HudHeader, Child = hdrGrid });
        }
        else
        {
            panel.Children.Add(new Border
            {
                Background = HudHeader,
                Child = new TextBlock
                {
                    Text       = title,
                    Foreground = HudAccent,
                    FontFamily = HudTitleFont,
                    FontSize   = 14,
                    FontWeight = Avalonia.Media.FontWeight.SemiBold,
                    Margin     = new Thickness(10, 6, 8, 6),
                },
            });
        }

        // Separator
        panel.Children.Add(new Border
        {
            Background = HudInnerEdge,
            Height     = 1,
            Margin     = new Thickness(0),
        });

        // Rows
        foreach (var (key, valTb) in rows)
            panel.Children.Add(BuildPanelRow(key, valTb));

        panel.Children.Add(new Border { Height = 8 }); // bottom padding
        return new Border
        {
            Background = HudFrame,
            BorderBrush = HudEdge,
            BorderThickness = new Thickness(1.4),
            CornerRadius = new CornerRadius(18),
            Padding = new Thickness(2),
            Margin = new Thickness(0, 0, 0, 3),
            Child = new Border
            {
                Background = HudFrameAlt,
                BorderBrush = HudInnerEdge,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(16),
                Child = panel,
            },
        };
    }

    private static void StylePanelValueText(TextBlock valTb)
    {
        valTb.Text = "-";
        valTb.Foreground = HudText;
        valTb.FontSize = 13;
        valTb.TextAlignment = TextAlignment.Right;
        valTb.MinWidth = 70;
        valTb.VerticalAlignment = VerticalAlignment.Center;
    }

    private Control BuildPanelRow(string key, TextBlock value)
    {
        StylePanelValueText(value);
        return BuildPanelRow(key, value, value);
    }

    private static Control BuildPanelRow(string key, Control valueControl, TextBlock? alignReference = null)
    {
        var keyTb = new TextBlock
        {
            Text = key,
            Foreground = HudMuted,
            FontSize = 13,
            VerticalAlignment = VerticalAlignment.Center,
        };

        if (alignReference != null)
            alignReference.VerticalAlignment = VerticalAlignment.Center;

        var row = new Grid { Margin = new Thickness(6, 2, 6, 2) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(keyTb, 0);
        Grid.SetColumn(valueControl, 1);
        row.Children.Add(keyTb);
        row.Children.Add(valueControl);
        return row;
    }

    // ── Terminal area ──────────────────────────────────────────────────────

    private Control BuildTerminalArea()
    {
        var layout = new Grid();
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        _commSplitterRow = new RowDefinition { Height = new GridLength(0) };
        _commPanelRow = new RowDefinition { Height = new GridLength(0) };
        layout.RowDefinitions.Add(_commSplitterRow);
        layout.RowDefinitions.Add(_commPanelRow);

        // Inner black area – the actual terminal surface
        var inner = new Border
        {
            Background          = Brushes.Black,
            BorderBrush         = BorderColor,
            BorderThickness     = new Thickness(2),
            Child               = _termCtrl,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment   = VerticalAlignment.Stretch,
        };

        // Outer raised frame: gray chrome with padding so it shows on all sides
        var outer = new Border
        {
            Background          = HudFrame,
            BorderBrush         = HudEdge,
            BorderThickness     = new Thickness(1.5),
            Padding             = new Thickness(4),
            MinHeight           = 160,
            CornerRadius        = new CornerRadius(18),
            Child               = new Border
            {
                Background = HudFrameAlt,
                BorderBrush = HudInnerEdge,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(16),
                Padding = new Thickness(4),
                Child = inner,
            },
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment   = VerticalAlignment.Stretch,
        };
        Grid.SetRow(outer, 0);
        layout.Children.Add(outer);

        var splitter = BuildCommGridSplitter(deckSkin: false);
        _commGridSplitter = splitter;
        Grid.SetRow(splitter, 1);
        layout.Children.Add(splitter);

        var commPanel = BuildCommPanel(deckSkin: false);
        Grid.SetRow(commPanel, 2);
        layout.Children.Add(commPanel);
        ApplyCommWindowVisibility();
        RefreshCommWindowUi();

        return layout;
    }

    private GridSplitter BuildCommGridSplitter(bool deckSkin)
    {
        var splitter = new GridSplitter
        {
            IsVisible = false,
            Height = deckSkin ? DeckCommSplitterHeight : ClassicCommSplitterHeight,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            ResizeDirection = GridResizeDirection.Rows,
            ResizeBehavior = GridResizeBehavior.PreviousAndNext,
            ShowsPreview = true,
            Background = HudInnerEdge,
        };

        splitter.PointerReleased += (_, _) => CaptureCommWindowHeights();
        return splitter;
    }

    private Border BuildCommPanel(bool deckSkin)
    {
        var (fedScrollViewer, fedTextBlock) = BuildCommLogView(deckSkin);
        var (subScrollViewer, subTextBlock) = BuildCommLogView(deckSkin);
        var (privateScrollViewer, privateTextBlock) = BuildCommLogView(deckSkin);
        var fedButton = BuildCommTabButton("FedComm", Core.CommMessageChannel.FedComm, deckSkin);
        var subButton = BuildCommTabButton("Subspace", Core.CommMessageChannel.Subspace, deckSkin);
        var privateButton = BuildCommTabButton("Private", Core.CommMessageChannel.Private, deckSkin);

        var header = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = deckSkin ? 10 : 8,
            Margin = deckSkin ? new Thickness(8, 6, 8, 4) : new Thickness(6, 4, 6, 2),
            HorizontalAlignment = HorizontalAlignment.Left,
            Children = { fedButton, subButton, privateButton },
        };

        var targetLabel = new TextBlock
        {
            Text = "User",
            Foreground = deckSkin ? HudMuted : FgKey,
            FontSize = deckSkin ? 12 : 13,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 6, 0),
        };

        var targetBox = new TextBox
        {
            Width = deckSkin ? 130 : 120,
            Height = deckSkin ? 28 : 30,
            VerticalAlignment = VerticalAlignment.Center,
            Text = _commPrivateTarget,
            Watermark = "captain",
            FontSize = deckSkin ? 12 : 13,
            FontFamily = new FontFamily("Cascadia Code, Menlo, Consolas, Courier New, monospace"),
        };

        var composeBox = new TextBox
        {
            Height = deckSkin ? 28 : 30,
            VerticalAlignment = VerticalAlignment.Center,
            FontSize = deckSkin ? 12 : 13,
            FontFamily = new FontFamily("Cascadia Code, Menlo, Consolas, Courier New, monospace"),
            Watermark = "Enter message",
        };

        var sendButton = new Button
        {
            Content = "Send",
            MinWidth = deckSkin ? 72 : 76,
            Height = deckSkin ? 28 : 30,
            Margin = new Thickness(8, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        ApplyHudActionButtonStyle(sendButton, primary: true);

        if (!deckSkin)
        {
            ApplyHudTextBoxStyle(targetBox);
            ApplyHudTextBoxStyle(composeBox);
        }

        composeBox.KeyDown += async (_, e) =>
        {
            if (e.Key != Key.Enter)
                return;

            e.Handled = true;
            await SendCommWindowMessageAsync(deckSkin);
        };
        sendButton.Click += async (_, _) => await SendCommWindowMessageAsync(deckSkin);

        var composer = new Grid
        {
            Margin = deckSkin ? new Thickness(8, 0, 8, 8) : new Thickness(6, 0, 6, 6),
        };
        composer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        composer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        composer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
        composer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        composer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(targetLabel, 0);
        Grid.SetColumn(targetBox, 1);
        Grid.SetColumn(composeBox, 3);
        Grid.SetColumn(sendButton, 4);
        composer.Children.Add(targetLabel);
        composer.Children.Add(targetBox);
        composer.Children.Add(composeBox);
        composer.Children.Add(sendButton);

        var logHost = new Grid();
        logHost.Children.Add(fedScrollViewer);
        logHost.Children.Add(subScrollViewer);
        logHost.Children.Add(privateScrollViewer);

        var body = new Grid();
        body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        body.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Grid.SetRow(header, 0);
        Grid.SetRow(logHost, 1);
        Grid.SetRow(composer, 2);
        body.Children.Add(header);
        body.Children.Add(logHost);
        body.Children.Add(composer);

        var panel = new Border
        {
            IsVisible = false,
            MinHeight = CommWindowMinHeight,
            Background = deckSkin ? HudFrame : HudFrame,
            BorderBrush = deckSkin ? HudInnerEdge : HudInnerEdge,
            BorderThickness = new Thickness(deckSkin ? 1.5 : 1.5),
            CornerRadius = deckSkin ? new CornerRadius(12) : new CornerRadius(14),
            Child = body,
        };

        if (deckSkin)
        {
            _deckCommPanelBorder = panel;
            _deckCommFedTabButton = fedButton;
            _deckCommSubspaceTabButton = subButton;
            _deckCommPrivateTabButton = privateButton;
            _deckCommFedTextBlock = fedTextBlock;
            _deckCommSubspaceTextBlock = subTextBlock;
            _deckCommPrivateTextBlock = privateTextBlock;
            _deckCommFedScrollViewer = fedScrollViewer;
            _deckCommSubspaceScrollViewer = subScrollViewer;
            _deckCommPrivateScrollViewer = privateScrollViewer;
            _deckCommComposeTextBox = composeBox;
            _deckCommPrivateTargetTextBox = targetBox;
            _deckCommPrivateTargetLabel = targetLabel;
        }
        else
        {
            _commPanelBorder = panel;
            _commFedTabButton = fedButton;
            _commSubspaceTabButton = subButton;
            _commPrivateTabButton = privateButton;
            _commFedTextBlock = fedTextBlock;
            _commSubspaceTextBlock = subTextBlock;
            _commPrivateTextBlock = privateTextBlock;
            _commFedScrollViewer = fedScrollViewer;
            _commSubspaceScrollViewer = subScrollViewer;
            _commPrivateScrollViewer = privateScrollViewer;
            _commComposeTextBox = composeBox;
            _commPrivateTargetTextBox = targetBox;
            _commPrivateTargetLabel = targetLabel;
        }

        SyncSelectedCommTab();
        RefreshCommComposerState();
        return panel;
    }

    private Button BuildCommTabButton(string label, Core.CommMessageChannel channel, bool deckSkin)
    {
        var button = new Button
        {
            Content = label,
            MinWidth = 0,
            MinHeight = 0,
            Padding = deckSkin ? new Thickness(2, 1, 2, 3) : new Thickness(2, 1, 2, 2),
            Margin = new Thickness(0),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
            FontSize = deckSkin ? 13 : 12,
            FontWeight = FontWeight.SemiBold,
            Background = Brushes.Transparent,
            BorderBrush = Brushes.Transparent,
            BorderThickness = new Thickness(0),
        };
        button.Click += (_, _) =>
        {
            _commSelectedChannel = channel;
            SyncSelectedCommTab();
            RefreshCommComposerState();
            RefreshCommWindowText();
        };
        return button;
    }

    private (ScrollViewer Viewer, TextBlock TextBlock) BuildCommLogView(bool deckSkin)
    {
        var textBlock = new TextBlock
        {
            TextWrapping = TextWrapping.NoWrap,
            Foreground = HudText,
            FontFamily = new FontFamily("Cascadia Code, Menlo, Consolas, Courier New, monospace"),
            FontSize = deckSkin ? 12 : 13,
            Margin = new Thickness(deckSkin ? 8 : 8, 0, deckSkin ? 8 : 8, 0),
        };

        var viewer = new ScrollViewer
        {
            Background = Brushes.Black,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = textBlock,
        };

        return (viewer, textBlock);
    }

    private void ToggleCommWindow()
    {
        bool opening = !_commWindowVisible;
        if (!opening)
            CaptureCommWindowHeights();
        _commWindowVisible = opening;

        ApplyCommWindowVisibility();
        RefreshCommWindowUi();
    }

    private async Task ToggleShowHaggleDetailsAsync()
    {
        EmbeddedMtcStatusBarConfig config = GetOrCreateCurrentStatusBarConfig();
        config.ShowHaggleInfo = !config.ShowHaggleInfo;
        await SaveCurrentGameConfigAsync();
        RefreshHaggleDetailsMenuState();
        RequestStatusBarRefresh();
    }

    private void ToggleBottomBar()
    {
        _appPrefs.ShowBottomBar = !_appPrefs.ShowBottomBar;
        _appPrefs.Save();
        ApplyBottomBarVisibility();
        RefreshBottomBarMenuState();
    }

    private async Task OnConfigureStatusPanelAsync()
    {
        var dialog = new StatusPanelConfigDialog(_appPrefs.GetOrderedStatusPanelSections());
        bool saved = await dialog.ShowDialog<bool>(this);
        if (!saved || dialog.Result == null)
            return;

        _appPrefs.SetStatusPanelSections(dialog.Result.Sections);
        _appPrefs.Save();

        if (!_useCommandDeckSkin)
            ApplySelectedSkinSafe();
    }

    private async Task OnConfigureStatusBarAsync()
    {
        string gameName = DeriveGameName();
        if (string.IsNullOrWhiteSpace(gameName))
        {
            await ShowMessageAsync("Status Bar", "Open or load a game first.");
            return;
        }

        EmbeddedGameConfig config = _embeddedGameConfig ?? await LoadOrCreateEmbeddedGameConfigAsync(gameName);
        _embeddedGameConfig = config;
        EmbeddedMtcStatusBarConfig statusConfig = GetOrCreateCurrentStatusBarConfig();

        var dialog = new StatusBarConfigDialog(statusConfig);
        bool saved = await dialog.ShowDialog<bool>(this);
        if (!saved || dialog.Result == null)
            return;

        List<EmbeddedMtcStatusSectorChip> previousCustomSectors =
            SanitizeStatusSectorChips(statusConfig.CustomSectors);
        bool previousShowStarDock = statusConfig.ShowStarDock;
        bool previousShowBackdoor = statusConfig.ShowBackdoor;
        bool previousShowRylos = statusConfig.ShowRylos;
        bool previousShowAlpha = statusConfig.ShowAlpha;
        bool previousShowIpInfo = statusConfig.ShowIpInfo;
        bool previousShowHaggleInfo = statusConfig.ShowHaggleInfo;

        try
        {
            statusConfig.ShowStarDock = dialog.Result.ShowStarDock;
            statusConfig.ShowBackdoor = dialog.Result.ShowBackdoor;
            statusConfig.ShowRylos = dialog.Result.ShowRylos;
            statusConfig.ShowAlpha = dialog.Result.ShowAlpha;
            statusConfig.ShowIpInfo = dialog.Result.ShowIpInfo;
            statusConfig.ShowHaggleInfo = dialog.Result.ShowHaggleInfo;
            statusConfig.CustomSectors = SanitizeStatusSectorChips(dialog.Result.CustomSectors);

            await SaveCurrentGameConfigAsync();

            Dispatcher.UIThread.Post(() =>
            {
                InvalidateStatusBarLayout();
                RefreshHaggleDetailsMenuState();
                RequestStatusBarRefresh();
            }, DispatcherPriority.Background);
        }
        catch (Exception ex)
        {
            statusConfig.ShowStarDock = previousShowStarDock;
            statusConfig.ShowBackdoor = previousShowBackdoor;
            statusConfig.ShowRylos = previousShowRylos;
            statusConfig.ShowAlpha = previousShowAlpha;
            statusConfig.ShowIpInfo = previousShowIpInfo;
            statusConfig.ShowHaggleInfo = previousShowHaggleInfo;
            statusConfig.CustomSectors = previousCustomSectors;
            InvalidateStatusBarLayout();
            RefreshHaggleDetailsMenuState();
            RequestStatusBarRefresh();
            await ShowMessageAsync("Status Bar Save Failed", ex.Message);
        }
    }

    private void ApplyBottomBarVisibility()
    {
        _statusBar.IsVisible = _appPrefs.ShowBottomBar;
    }

    private void RefreshCommWindowUi()
    {
        if (_commPanelBorder != null)
            _commPanelBorder.IsVisible = _commWindowVisible;
        if (_commGridSplitter != null)
            _commGridSplitter.IsVisible = _commWindowVisible;
        if (_deckCommPanelBorder != null)
            _deckCommPanelBorder.IsVisible = _commWindowVisible;
        if (_deckCommGridSplitter != null)
            _deckCommGridSplitter.IsVisible = _commWindowVisible;

        SyncSelectedCommTab();
        RefreshCommComposerState();
        RefreshCommWindowText();
        RefreshCommWindowMenuState();
        UpdateTerminalLiveSelector();
    }

    private void CaptureCommWindowHeights()
    {
        if (_commPanelBorder is { IsVisible: true } && _commPanelBorder.Bounds.Height >= CommWindowMinHeight)
            _classicCommWindowHeight = _commPanelBorder.Bounds.Height;

        if (_deckCommPanelBorder is { IsVisible: true } && _deckCommPanelBorder.Bounds.Height >= CommWindowMinHeight)
            _deckCommWindowHeight = _deckCommPanelBorder.Bounds.Height;
    }

    private void ApplyCommWindowVisibility()
    {
        ApplyCommWindowVisibility(
            _commSplitterRow,
            _commPanelRow,
            _commGridSplitter,
            deckSkin: false);
        ApplyCommWindowVisibility(
            _deckCommSplitterRow,
            _deckCommPanelRow,
            _deckCommGridSplitter,
            deckSkin: true);
    }

    private void ApplyCommWindowVisibility(
        RowDefinition? splitterRow,
        RowDefinition? panelRow,
        GridSplitter? splitter,
        bool deckSkin)
    {
        if (splitterRow == null || panelRow == null)
            return;

        if (_commWindowVisible)
        {
            splitterRow.Height = new GridLength(deckSkin ? DeckCommSplitterHeight : ClassicCommSplitterHeight);
            panelRow.Height = new GridLength(Math.Max(
                CommWindowMinHeight,
                deckSkin ? _deckCommWindowHeight : _classicCommWindowHeight));
        }
        else
        {
            splitterRow.Height = new GridLength(0);
            panelRow.Height = new GridLength(0);
        }

        if (splitter != null)
            splitter.IsVisible = _commWindowVisible;
    }

    private void SyncSelectedCommTab()
    {
        UpdateCommTabButtonState(_commFedTabButton, "FedComm", _commSelectedChannel == Core.CommMessageChannel.FedComm, deckSkin: false);
        UpdateCommTabButtonState(_commSubspaceTabButton, "Subspace", _commSelectedChannel == Core.CommMessageChannel.Subspace, deckSkin: false);
        UpdateCommTabButtonState(_commPrivateTabButton, "Private", _commSelectedChannel == Core.CommMessageChannel.Private, deckSkin: false);
        UpdateCommTabButtonState(_deckCommFedTabButton, "FedComm", _commSelectedChannel == Core.CommMessageChannel.FedComm, deckSkin: true);
        UpdateCommTabButtonState(_deckCommSubspaceTabButton, "Subspace", _commSelectedChannel == Core.CommMessageChannel.Subspace, deckSkin: true);
        UpdateCommTabButtonState(_deckCommPrivateTabButton, "Private", _commSelectedChannel == Core.CommMessageChannel.Private, deckSkin: true);

        if (_commFedScrollViewer != null) _commFedScrollViewer.IsVisible = _commSelectedChannel == Core.CommMessageChannel.FedComm;
        if (_commSubspaceScrollViewer != null) _commSubspaceScrollViewer.IsVisible = _commSelectedChannel == Core.CommMessageChannel.Subspace;
        if (_commPrivateScrollViewer != null) _commPrivateScrollViewer.IsVisible = _commSelectedChannel == Core.CommMessageChannel.Private;
        if (_deckCommFedScrollViewer != null) _deckCommFedScrollViewer.IsVisible = _commSelectedChannel == Core.CommMessageChannel.FedComm;
        if (_deckCommSubspaceScrollViewer != null) _deckCommSubspaceScrollViewer.IsVisible = _commSelectedChannel == Core.CommMessageChannel.Subspace;
        if (_deckCommPrivateScrollViewer != null) _deckCommPrivateScrollViewer.IsVisible = _commSelectedChannel == Core.CommMessageChannel.Private;
    }

    private void UpdateCommTabButtonState(Button? button, string label, bool selected, bool deckSkin)
    {
        if (button == null)
            return;

        button.Content = label;
        button.Foreground = selected ? HudText : HudMuted;
        button.BorderBrush = selected ? HudAccent : Brushes.Transparent;
        button.BorderThickness = new Thickness(0, 0, 0, selected ? 2 : 0);
    }

    private void RefreshCommComposerState()
    {
        RefreshCommComposerState(
            _commPrivateTargetLabel,
            _commPrivateTargetTextBox,
            _commComposeTextBox,
            deckSkin: false);
        RefreshCommComposerState(
            _deckCommPrivateTargetLabel,
            _deckCommPrivateTargetTextBox,
            _deckCommComposeTextBox,
            deckSkin: true);
    }

    private void RefreshCommComposerState(
        TextBlock? targetLabel,
        TextBox? targetBox,
        TextBox? composeBox,
        bool deckSkin)
    {
        bool isPrivate = _commSelectedChannel == Core.CommMessageChannel.Private;

        if (targetLabel != null)
            targetLabel.IsVisible = isPrivate;

        if (targetBox != null)
        {
            targetBox.IsVisible = isPrivate;
            if ((!targetBox.IsFocused || string.IsNullOrWhiteSpace(targetBox.Text)) &&
                targetBox.Text != _commPrivateTarget)
            {
                targetBox.Text = _commPrivateTarget;
            }
        }

        if (composeBox != null)
        {
            composeBox.Watermark = _commSelectedChannel switch
            {
                Core.CommMessageChannel.Subspace => "Send subspace message",
                Core.CommMessageChannel.Private => "Send private message",
                _ => "Send fedcomm message",
            };
            composeBox.Foreground = HudText;
        }
    }

    private async Task SendCommWindowMessageAsync(bool deckSkin)
    {
        TextBox? composeBox = deckSkin ? _deckCommComposeTextBox : _commComposeTextBox;
        TextBox? targetBox = deckSkin ? _deckCommPrivateTargetTextBox : _commPrivateTargetTextBox;

        if (composeBox == null)
            return;

        string message = (composeBox.Text ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(message))
            return;

        string payload = _commSelectedChannel switch
        {
            Core.CommMessageChannel.Subspace => $"'{message}\r",
            Core.CommMessageChannel.Private => BuildPrivateCommPayload(targetBox, message),
            _ => $"`{message}\r",
        };

        if (string.IsNullOrEmpty(payload))
            return;

        _commEntries.Add(new CommEntry(_commSelectedChannel, string.Empty, message, IsLocal: true));
        if (_commEntries.Count > MaxCommEntries)
            _commEntries.RemoveAt(0);
        RefreshCommWindowText();

        await SendCommPayloadAsync(payload);

        composeBox.Text = string.Empty;
        if (composeBox != _commComposeTextBox && _commComposeTextBox != null)
            _commComposeTextBox.Text = string.Empty;
        if (composeBox != _deckCommComposeTextBox && _deckCommComposeTextBox != null)
            _deckCommComposeTextBox.Text = string.Empty;

        composeBox.Focus();
    }

    private string BuildPrivateCommPayload(TextBox? targetBox, string message)
    {
        string target = (targetBox?.Text ?? _commPrivateTarget).Trim();
        if (string.IsNullOrEmpty(target))
        {
            _parser.Feed("\x1b[33m[private target required]\x1b[0m\r\n");
            return string.Empty;
        }

        _commPrivateTarget = target;
        RefreshCommComposerState();
        return $"={target}\r{message}\r\r";
    }

    private async Task SendCommPayloadAsync(string payload)
    {
        byte[] bytes = System.Text.Encoding.ASCII.GetBytes(payload);
        if (_gameInstance != null)
        {
            if (_gameInstance.IsConnected)
                await _gameInstance.SendToServerAsync(bytes);
            else
                _parser.Feed("\x1b[33m[not connected]\x1b[0m\r\n");
            return;
        }

        SendToTelnet(bytes);
    }

    private void HandlePotentialCommLine(string ansiLine)
    {
        if (!Core.AnsiCodes.TryParseCommMessageLine(ansiLine, out Core.CommMessageInfo info))
            return;

        Dispatcher.UIThread.Post(() =>
        {
            _commEntries.Add(new CommEntry(info.Channel, info.Sender, info.MessageText, IsLocal: false));
            if (_commEntries.Count > MaxCommEntries)
                _commEntries.RemoveAt(0);
            if (info.Channel == Core.CommMessageChannel.Private && !string.IsNullOrWhiteSpace(info.Sender))
                _commPrivateTarget = info.Sender;
            RefreshCommComposerState();
            RefreshCommWindowText();
        });
    }

    private void RefreshCommWindowText()
    {
        UpdateCommTextBlock(_commFedTextBlock, _commFedScrollViewer, Core.CommMessageChannel.FedComm);
        UpdateCommTextBlock(_commSubspaceTextBlock, _commSubspaceScrollViewer, Core.CommMessageChannel.Subspace);
        UpdateCommTextBlock(_commPrivateTextBlock, _commPrivateScrollViewer, Core.CommMessageChannel.Private);
        UpdateCommTextBlock(_deckCommFedTextBlock, _deckCommFedScrollViewer, Core.CommMessageChannel.FedComm);
        UpdateCommTextBlock(_deckCommSubspaceTextBlock, _deckCommSubspaceScrollViewer, Core.CommMessageChannel.Subspace);
        UpdateCommTextBlock(_deckCommPrivateTextBlock, _deckCommPrivateScrollViewer, Core.CommMessageChannel.Private);
    }

    private void UpdateCommTextBlock(TextBlock? textBlock, ScrollViewer? scrollViewer, Core.CommMessageChannel channel)
    {
        if (textBlock == null)
            return;

        textBlock.Inlines?.Clear();
        bool first = true;
        foreach (CommEntry entry in _commEntries.Where(entry => entry.Channel == channel))
        {
            if (!first)
                textBlock.Inlines?.Add(new LineBreak());
            first = false;

            if (!entry.IsLocal && !string.IsNullOrWhiteSpace(entry.Sender))
            {
                textBlock.Inlines?.Add(new Run($"{entry.Sender} ")
                {
                    Foreground = Brushes.Yellow,
                });
            }

            textBlock.Inlines?.Add(new Run(entry.Message)
            {
                Foreground = Brushes.White,
            });
        }

        scrollViewer?.ScrollToEnd();
    }
    // ── Ship status update ─────────────────────────────────────────────────────

    /// <summary>
    /// Called by <see cref="ShipInfoParser"/> whenever a complete "/" or "I"
    /// response has been parsed.  Maps <see cref="ShipStatus"/> fields into
    /// <see cref="GameState"/> and triggers a UI refresh.
    /// </summary>
    private void OnShipStatusUpdated(Core.ShipStatus s)
    {
        // May arrive on the thread-pool read loop – always dispatch to UI thread
        Dispatcher.UIThread.Post(() =>
        {
            _state.Sector       = s.CurrentSector;
            _state.Turns        = s.Turns;
            _state.Credits      = s.Credits;
            _state.Experience   = (int)s.Experience;
            _state.Alignment    = s.Alignment.ToString();
            _state.TraderName   = string.IsNullOrEmpty(s.TraderName) ? _state.TraderName : s.TraderName;
            _state.Corp         = s.Corp;
            _state.ShipName     = string.IsNullOrEmpty(s.ShipName) ? _state.ShipName : s.ShipName;

            _state.Fighters     = s.Fighters;
            _state.Shields      = s.Shields;
            _state.TurnsPerWarp = s.TurnsPerWarp;

            _state.HoldsTotal   = s.TotalHolds;
            _state.FuelOre      = s.FuelOre;
            _state.Organics     = s.Organics;
            _state.Equipment    = s.Equipment;
            _state.Colonists    = s.Colonists;
            _state.HoldsEmpty   = s.HoldsEmpty;

            _state.Photon       = s.Photons;
            _state.Limpet       = s.LimpetMines;
            _state.Armor        = s.ArmidMines;
            _state.Genesis      = s.GenesisTorps;
            _state.Atomic       = s.AtomicDet;
            _state.Corbomite    = s.Corbomite;
            _state.Cloak        = s.Cloaks;
            _state.Beacon       = s.Beacons;
            _state.Etheral      = s.EtherProbes;
            _state.Disruptor    = s.MineDisruptors;
            _state.ScannerP     = s.PlanetScanner;
            bool hasHoloScanner = s.LRSType.Contains("Holo", StringComparison.OrdinalIgnoreCase);
            bool hasDensityScanner = !string.IsNullOrEmpty(s.LRSType) && !hasHoloScanner;
            _state.ScannerH     = hasHoloScanner;
            _state.ScannerD     = NormalizeDensityScanner(hasDensityScanner, hasHoloScanner);
            _state.HasTranswarpDrive1 = s.HasTransWarp1 || s.TransWarp1 > 0;
            _state.HasTranswarpDrive2 = s.HasTransWarp2 || s.TransWarp2 > 0;
            _state.TranswarpDrive1 = s.TransWarp1;
            _state.TranswarpDrive2 = s.TransWarp2;

            _state.NotifyChanged();
            RefreshInfoPanels(); // update UI immediately on this dispatch

            // Auto-save ship state back to the open profile file
            if (_currentProfilePath != null)
            {
                _ = SaveCurrentGameConfigAsync();
            }
        });
    }

    private void OnGenesisTorpsChanged(int delta)
    {
        if (delta == 0)
            return;

        if (_gameInstance != null)
        {
            _gameInstance.AdjustGenesisTorps(delta);
            return;
        }

        int updated = _state.Genesis + delta;
        _state.Genesis = updated < 0 ? 0 : updated;
        _state.NotifyChanged();
        RefreshInfoPanels();

        if (_currentProfilePath != null)
            _ = SaveCurrentGameConfigAsync();
    }

    private void OnAtomicDetChanged(int delta)
    {
        if (delta == 0)
            return;

        if (_gameInstance != null)
        {
            _gameInstance.AdjustAtomicDet(delta);
            return;
        }

        int updated = _state.Atomic + delta;
        _state.Atomic = updated < 0 ? 0 : updated;
        _state.NotifyChanged();
        RefreshInfoPanels();

        if (_currentProfilePath != null)
            _ = SaveCurrentGameConfigAsync();
    }

    // ── Info panel refresh ─────────────────────────────────────────────────

    private void RefreshInfoPanels()
    {
        string traderName = string.IsNullOrEmpty(_state.TraderName) ? "-" : _state.TraderName;
        string turnsDisplay = GetTurnsDisplayText();
        bool currentSectorBusted = IsCurrentSectorBusted();
        _valName.Text      = traderName;
        _deckValName.Text  = traderName;
        _valSector.Text    = _state.Sector.ToString();
        _deckValSector.Text = _valSector.Text;
        _sectorBustIndicator.IsVisible = currentSectorBusted;
        _valTurns.Text     = turnsDisplay;
        _deckValTurns.Text = _valTurns.Text;
        _valExper.Text     = _state.Experience.ToString("N0");
        _deckValExper.Text = _valExper.Text;
        int alignVal = int.TryParse(_state.Alignment, out int av) ? av : 0;
        _valAlignm.Text    = alignVal.ToString("N0");
        _deckValAlignm.Text = _valAlignm.Text;
        IBrush alignBrush = alignVal >= 1000
            ? new SolidColorBrush(Color.FromRgb(100, 180, 255))      // blue  (≥ 1,000)
            : alignVal < 0
                ? new SolidColorBrush(Color.FromRgb(255, 80, 80))    // red   (negative)
                : new SolidColorBrush(Color.FromRgb(255, 255, 255));  // white (0–999)
        _valAlignm.Foreground = alignBrush;
        _deckValAlignm.Foreground = alignBrush;
        _valCred.Text      = _state.Credits.ToString("N0");
        _deckValCred.Text  = _valCred.Text;
        int holdsTotal = Math.Max(0, _state.HoldsTotal);
        int usedHolds = Math.Max(0, holdsTotal - Math.Max(0, _state.HoldsEmpty));
        _valHTotal.Text    = $"{usedHolds} / {holdsTotal}";
        _deckValHTotal.Text = holdsTotal.ToString();
        _valFuelOre.Text   = _state.FuelOre.ToString();
        _deckValFuelOre.Text = _valFuelOre.Text;
        _valOrganics.Text  = _state.Organics.ToString();
        _deckValOrganics.Text = _valOrganics.Text;
        _valEquipment.Text = _state.Equipment.ToString();
        _deckValEquipment.Text = _valEquipment.Text;
        _valColonists.Text = _state.Colonists.ToString();
        _deckValColonists.Text = _valColonists.Text;
        _valEmpty.Text     = _state.HoldsEmpty.ToString();
        _deckValEmpty.Text = _valEmpty.Text;
        UpdateHoldsStackedBar(_state.FuelOre, _state.Organics, _state.Equipment, _state.Colonists, _state.HoldsEmpty, holdsTotal);
        UpdateHoldsSegmentTooltips(_state.FuelOre, _state.Organics, _state.Equipment, _state.Colonists, _state.HoldsEmpty);
        _valFighters.Text  = _state.Fighters.ToString("N0");
        _deckValFighters.Text = _valFighters.Text;
        _valShields.Text   = _state.Shields.ToString("N0");
        _deckValShields.Text = _valShields.Text;
        _valTrnWarp.Text   = _state.TurnsPerWarp.ToString();
        _deckValTrnWarp.Text = _valTrnWarp.Text;
        _valEther.Text     = _state.Etheral.ToString();
        _deckValEther.Text = _valEther.Text;
        _valBeacon.Text    = _state.Beacon.ToString();
        _deckValBeacon.Text = _valBeacon.Text;
        _valDisruptor.Text = _state.Disruptor.ToString();
        _deckValDisruptor.Text = _valDisruptor.Text;
        _valPhoton.Text    = _state.Photon.ToString();
        _deckValPhoton.Text = _valPhoton.Text;
        _valArmid.Text     = _state.Armor.ToString();
        _deckValArmid.Text = _valArmid.Text;
        _valLimpet.Text    = _state.Limpet.ToString();
        _deckValLimpet.Text = _valLimpet.Text;
        _valGenesis.Text   = _state.Genesis.ToString();
        _deckValGenesis.Text = _valGenesis.Text;
        _valAtomic.Text    = _state.Atomic.ToString();
        _deckValAtomic.Text = _valAtomic.Text;
        _valCorbo.Text     = _state.Corbomite.ToString();
        _deckValCorbo.Text = _valCorbo.Text;
        _valCloak.Text     = _state.Cloak.ToString();
        _deckValCloak.Text = _valCloak.Text;
        _valTW1.Text       = _state.HasTranswarpDrive1 ? _state.TranswarpDrive1.ToString() : "-";
        _deckValTW1.Text   = _valTW1.Text;
        _valTW2.Text       = _state.HasTranswarpDrive2 ? _state.TranswarpDrive2.ToString() : "-";
        _deckValTW2.Text   = _valTW2.Text;
        UpdateScanInd(_scanIndTW1, _state.HasTranswarpDrive1);
        UpdateScanInd(_scanIndTW2, _state.HasTranswarpDrive2);
        UpdateScanInd(_scanIndD, _state.ScannerD);
        UpdateScanInd(_scanIndH, _state.ScannerH);
        UpdateScanInd(_scanIndP, _state.ScannerP);
        UpdateDeckScanInd(_deckScanIndTW1, _state.HasTranswarpDrive1);
        UpdateDeckScanInd(_deckScanIndTW2, _state.HasTranswarpDrive2);
        UpdateDeckScanInd(_deckScanIndD, _state.ScannerD);
        UpdateDeckScanInd(_deckScanIndH, _state.ScannerH);
        UpdateDeckScanInd(_deckScanIndP, _state.ScannerP);

        _deckHudHeaderSector.Text = _state.Sector > 0 ? _state.Sector.ToString("N0") : "---";
        _deckHudShipName.Text = string.IsNullOrWhiteSpace(_state.ShipName) || _state.ShipName == "-"
            ? "Unassigned Hull"
            : _state.ShipName;
        string captainText = traderName == "-" ? "Independent captain" : $"Capt. {traderName}";
        string corpText = _state.Corp > 0 ? $"Corp {_state.Corp}" : "Free trader";
        if (!string.IsNullOrWhiteSpace(_state.GameName))
            _deckHudShipSubtitle.Text = $"{captainText}  //  {corpText}  //  {_state.GameName}";
        else
            _deckHudShipSubtitle.Text = $"{captainText}  //  {corpText}";

        RequestStatusBarRefresh();
        _tacticalMap?.InvalidateVisual();
    }

    private bool IsCurrentSectorBusted()
    {
        if (_sessionDb == null || _state.Sector <= 0)
            return false;

        string value = _sessionDb.GetSectorVar(_state.Sector, "BUSTED");
        return !string.IsNullOrWhiteSpace(value) &&
            !string.Equals(value, "0", StringComparison.OrdinalIgnoreCase);
    }

    private string GetTurnsDisplayText()
    {
        if (_state.Turns == 0 &&
            IsMombotTruthy(ReadCurrentMombotVar("0", "$PLAYER~UNLIMITEDGAME", "$PLAYER~unlimitedGame", "$unlimitedGame")))
        {
            return "Unlimited";
        }

        return _state.Turns.ToString();
    }

    private string? GetNativeBotModeStatusText()
    {
        if (!_mombot.Enabled)
            return null;

        string mode = _mombot.GetStatusSnapshot().Mode;
        if (string.IsNullOrWhiteSpace(mode))
            mode = "General";

        return $"Bot Mode: {mode}";
    }

    private string? GetActiveScriptStatusText()
    {
        IReadOnlyList<Core.RunningScriptInfo> scripts = Core.ProxyGameOperations.GetRunningScripts(CurrentInterpreter);
        if (scripts.Count == 0)
            return null;

        Core.RunningScriptInfo? activeScript = scripts
            .Where(static script => !script.IsSystemScript && !script.IsBot)
            .Where(script => !IsNativeMombotModeScriptReference(script.Reference))
            .LastOrDefault(script => !script.Paused);

        if (activeScript == null)
        {
            activeScript = scripts
                .Where(static script => !script.IsSystemScript && !script.IsBot)
                .Where(script => !IsNativeMombotModeScriptReference(script.Reference))
                .LastOrDefault();
        }

        if (activeScript == null)
            return null;

        string scriptName = GetRunningScriptDisplayName(activeScript);
        return string.IsNullOrWhiteSpace(scriptName)
            ? null
            : $"Active Script: {scriptName}";
    }

    private static bool IsNativeMombotModeScriptReference(string? scriptReference)
    {
        if (string.IsNullOrWhiteSpace(scriptReference))
            return false;

        string normalized = scriptReference.Replace('\\', '/').Trim();
        return normalized.StartsWith("scripts/mombot/modes/", StringComparison.OrdinalIgnoreCase) ||
               normalized.StartsWith("modes/", StringComparison.OrdinalIgnoreCase) ||
               normalized.StartsWith("scripts/mombot/local/modes/", StringComparison.OrdinalIgnoreCase) ||
               normalized.StartsWith("local/modes/", StringComparison.OrdinalIgnoreCase) ||
               normalized.Contains("/scripts/mombot/modes/", StringComparison.OrdinalIgnoreCase) ||
               normalized.Contains("/scripts/mombot/local/modes/", StringComparison.OrdinalIgnoreCase);
    }

    private static string GetRunningScriptDisplayName(Core.RunningScriptInfo script)
    {
        string candidate = string.IsNullOrWhiteSpace(script.Name)
            ? script.Reference
            : script.Name;
        if (string.IsNullOrWhiteSpace(candidate))
            return string.Empty;

        string trimmed = candidate.Trim();
        string fileName = Path.GetFileNameWithoutExtension(trimmed);
        return string.IsNullOrWhiteSpace(fileName) ? trimmed : fileName;
    }

    private void RefreshStatusBar()
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(RefreshStatusBar, DispatcherPriority.Background);
            return;
        }

        EnsureStatusBarLayout();
        RefreshHaggleDetailsMenuState();

        EmbeddedMtcStatusBarConfig statusConfig = GetStatusBarConfigForDisplay();
        string? conn = statusConfig.ShowIpInfo
            ? (_state.Connected
                ? $"[ {_state.Host}:{_state.Port} ]"
                : "[ disconnected ]")
            : null;

        string starDock = "-";
        string backdoor = "-";
        string rylos = "-";
        string alpha = "-";
        bool haggleDetailsEnabled = statusConfig.ShowHaggleInfo;
        int hagglePct = 0;
        int haggleGood = 0;
        int haggleGreat = 0;
        int haggleExcellent = 0;
        bool showHagglePct = false;
        if (haggleDetailsEnabled && _gameInstance != null)
        {
            hagglePct = _gameInstance.NativeHaggleSuccessRatePercent;
            haggleGood = _gameInstance.NativeHaggleGoodCount;
            haggleGreat = _gameInstance.NativeHaggleGreatCount;
            haggleExcellent = _gameInstance.NativeHaggleExcellentCount;
            showHagglePct =
                _gameInstance.NativeHaggleEnabled ||
                haggleGood > 0 ||
                haggleGreat > 0 ||
                haggleExcellent > 0;
        }

        if (_sessionDb != null &&
            (statusConfig.ShowStarDock || statusConfig.ShowRylos || statusConfig.ShowAlpha))
        {
            var header = _sessionDb.DBHeader;

            if (statusConfig.ShowStarDock && header.StarDock != 0 && header.StarDock != 65535)
                starDock = header.StarDock.ToString();

            if (statusConfig.ShowRylos && header.Rylos != 0 && header.Rylos != 65535)
                rylos = header.Rylos.ToString();

            if (statusConfig.ShowAlpha && header.AlphaCentauri != 0 && header.AlphaCentauri != 65535)
                alpha = header.AlphaCentauri.ToString();
        }

        if (statusConfig.ShowStarDock && starDock == "-")
        {
            string savedStarDock = ReadCurrentMombotSectorVar("0",
                "$STARDOCK",
                "$MAP~STARDOCK",
                "$MAP~stardock",
                "$BOT~STARDOCK");
            if (IsDefinedMombotSectorValue(savedStarDock))
                starDock = savedStarDock;
        }

        if (statusConfig.ShowBackdoor)
        {
            string savedBackdoor = ReadCurrentMombotSectorVar("0",
                "$MAP~BACKDOOR",
                "$MAP~backdoor",
                "$backdoor");
            if (IsDefinedMombotSectorValue(savedBackdoor))
                backdoor = savedBackdoor;
        }

        if (statusConfig.ShowRylos && rylos == "-")
        {
            string savedRylos = ReadCurrentMombotSectorVar("0",
                "$MAP~RYLOS",
                "$MAP~rylos",
                "$BOT~RYLOS");
            if (IsDefinedMombotSectorValue(savedRylos))
                rylos = savedRylos;
        }

        if (statusConfig.ShowAlpha && alpha == "-")
        {
            string savedAlpha = ReadCurrentMombotSectorVar("0",
                "$MAP~ALPHA_CENTAURI",
                "$MAP~alpha_centauri",
                "$BOT~ALPHA_CENTAURI");
            if (IsDefinedMombotSectorValue(savedAlpha))
                alpha = savedAlpha;
        }

        _statusStarDockValue.Text = starDock;
        _statusBackdoorValue.Text = backdoor;
        _statusRylosValue.Text = rylos;
        _statusAlphaValue.Text = alpha;

        bool hasInterruptibleScripts = CurrentInterpreter?.HasInterruptibleScripts() ?? false;
        ApplyStatusToggleFrameStyle(_statusStopAllFrame, hasInterruptibleScripts);
        ApplyStatusStopAllButtonStyle(_statusStopAllButton, hasInterruptibleScripts);
        string? haggleText = showHagglePct
            ? $"Haggle Pct: {hagglePct}% {haggleGood}/{haggleGreat}/{haggleExcellent}"
            : null;
        string? botModeText = GetNativeBotModeStatusText();
        string? activeScriptText = GetActiveScriptStatusText();
        _statusText.Text = string.Join("  ", new[] { botModeText, activeScriptText, haggleText, conn }.Where(static part => !string.IsNullOrWhiteSpace(part)));
        _statusText.IsVisible = !string.IsNullOrWhiteSpace(_statusText.Text);
        SyncRedAlertFromMombotVar();
        UpdateTerminalLiveSelector();

        _deckHudHeaderConnection.Text = _state.Connected
            ? $"{_state.Host}:{_state.Port}"
            : "OFFLINE";
        _deckHudHeaderConnection.Foreground = _state.Connected ? HudAccentOk : HudAccentWarn;
        _deckHudStarDock.Text = starDock;
        _deckHudRylos.Text = rylos;
        _deckHudAlpha.Text = alpha;
        int universeCount = _sessionDb?.DBHeader.Sectors > 0
            ? _sessionDb.DBHeader.Sectors
            : _state.Sectors;
        _deckHudUniverse.Text = universeCount > 0 ? universeCount.ToString("N0") : "-";
    }

    // ── Telnet events ──────────────────────────────────────────────────────

    private void OnTelnetConnected()
    {
        _state.Connected = true;
        RefreshSessionLogTarget(CurrentInterpreter?.ScriptDirectory);
        // Open (or create) the sector database for this game connection
        OpenSessionDatabase(useSharedProxyDatabase: false);
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
        _fileEdit.IsEnabled       = true;
        _fileConnect.IsEnabled    = true;
        _fileDisconnect.IsEnabled = false;
        RebuildProxyMenu();
    }

    /// <summary>Call when TCP connection is established.</summary>
    private void OnGameConnected()
    {
        _fileConnect.IsEnabled    = false;
        _fileDisconnect.IsEnabled = true;
        UpdateHaggleToggleState();
        RefreshMombotUi();
        RebuildProxyMenu();
    }

    /// <summary>Call when TCP connection is lost / disconnected.</summary>
    private void OnGameDisconnected()
    {
        ClearRedAlert();
        _fileConnect.IsEnabled    = true;
        _fileDisconnect.IsEnabled = false;
        UpdateHaggleToggleState();
        RefreshMombotUi();
        RebuildProxyMenu();
    }

    private void OnHaggleToggleRequested()
    {
        if (_gameInstance == null)
        {
            UpdateHaggleToggleState();
            return;
        }

        _termCtrl.SendInput?.Invoke(System.Text.Encoding.ASCII.GetBytes("$h"));
        Dispatcher.UIThread.Post(FocusActiveTerminal, DispatcherPriority.Input);
    }

    private void UpdateHaggleToggleState()
    {
        bool proxyActive = _gameInstance != null;
        _statusHaggleButton.IsEnabled = proxyActive;
        UpdateTerminalLiveSelector();
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

    private void OnNativeHaggleChanged(bool enabled)
    {
        var gameConfig = _embeddedGameConfig;
        var gameName = _embeddedGameName;
        if (gameConfig != null &&
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
        string configPath = AppPaths.TwxproxyGameConfigFileFor(gameName);
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
            Core.ScriptRef.SetActiveDatabase(null);
            Core.ScriptRef.OnVariableSaved = null;
            Core.ScriptRef.ClearCurrentGameVars();
            ClearMombotRelogState();
            ResetMombotGameStorage(gameName);

            Directory.CreateDirectory(Path.GetDirectoryName(config.DatabasePath)!);
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
            : AppPaths.DatabasePathForGame(gameName);
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
        AppPaths.EnsureDebugLogDir();
        string debugGameName = GetDebugLogGameName();
        Core.GlobalModules.ConfigureDebugLogging(
            string.IsNullOrWhiteSpace(debugGameName)
                ? AppPaths.GetDebugLogPath()
                : AppPaths.GetDebugLogPathForGame(debugGameName),
            _appPrefs.DebugLoggingEnabled,
            _appPrefs.VerboseDebugLogging);
        Core.GlobalModules.ConfigureHaggleDebugLogging(
            AppPaths.GetPortHaggleDebugLogPath(),
            _appPrefs.DebugPortHaggleEnabled,
            AppPaths.GetPlanetHaggleDebugLogPath(),
            _appPrefs.DebugPlanetHaggleEnabled);
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
            ? AppPaths.TwxproxyDatabasePathForGame(gameName)
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

    private bool GameNameConflicts(string gameName, string? currentConfigPath = null, string? currentDatabasePath = null)
    {
        string configPath = AppPaths.TwxproxyGameConfigFileFor(gameName);
        if (File.Exists(configPath) &&
            !string.Equals(configPath, currentConfigPath, StringComparison.OrdinalIgnoreCase))
            return true;

        string databasePath = AppPaths.TwxproxyDatabasePathForGame(gameName);
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

        Core.ScriptRef.ClearCurrentGameVars();
        Core.ScriptRef.LoadVarsForGame(varsToLoad);
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
        gi.SetNativeHaggleEnabled(gameConfig.NativeHaggleEnabled);
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
                    _sessionLog.RecordServerData(chunk);
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

            if (_terminalLivePaused)
            {
                _sessionLog.RecordServerData(e.Data);
                QueuePausedTerminalChunk(e.Data);
            }

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

                if (!string.IsNullOrWhiteSpace(lineStripped) && _mombot.ObserveServerLine(lineStripped))
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

            if (ShouldStopNativeMombotAfterDisconnect())
            {
                Dispatcher.UIThread.Post(async () =>
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
                });
            }
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

        _gameInstance = gi;
        ApplyEmbeddedTerminalOutputMode();
        ReloadRegisteredBotConfigs();
        SyncMombotRuntimeConfigFromTwxpCfg(gameConfig);
        _mombot.AttachSession(gi, _sessionDb, interpreter, GetOrCreateEmbeddedMombotConfig(gameConfig));
        RefreshStatusBar();
        Core.ScriptRef.SetActiveGameInstance(gi);  // routes getinput through the pipe, not the system console
        OnNativeHaggleChanged(gi.NativeHaggleEnabled);
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

        _statusHaggleFrame.Padding = new Thickness(4, 2);
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
        _statusHaggleButton.MinWidth = 56;
        _statusHaggleButton.Height = 20;
        _statusHaggleButton.Padding = new Thickness(4, 1);
        _statusHaggleButton.FontSize = 11;
        _statusHaggleButton.FontWeight = FontWeight.SemiBold;
        _statusHaggleButton.VerticalAlignment = VerticalAlignment.Center;
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
        BotRuntimeState botRuntime = GetBotRuntimeState();
        ApplyStatusToggleFrameStyle(_statusMacrosFrame, true);
        ApplyStatusToggleFrameStyle(_statusMapFrame, true);
        ApplyStatusToggleFrameStyle(_statusCommFrame, true);
        ApplyStatusToggleFrameStyle(_statusBotFrame, enabled);
        ApplyStatusToggleFrameStyle(_statusHaggleFrame, enabled);
        ApplyStatusToggleFrameStyle(_statusLivePausedFrame, enabled);
        ApplyStatusToggleFrameStyle(_statusRedAlertFrame, true);
        _statusRedAlertFrame.IsVisible = _redAlertEnabled;

        ApplyStatusMacrosButtonStyle(_statusMacrosButton, _macroSettingsDialog != null);
        ApplyStatusMapButtonStyle(_statusMapButton, _mapWindow != null);
        ApplyStatusCommButtonStyle(_statusCommButton, _commWindowVisible);
        ApplyStatusBotButtonStyle(_statusBotButton, selected: botRuntime.NativeRunning, enabled);
        ApplyStatusHaggleButtonStyle(_statusHaggleButton, selected: enabled && _gameInstance?.NativeHaggleEnabled == true, enabled);
        _statusHaggleButton.Content = enabled && _statusHaggleHovered
            ? (_gameInstance?.NativeHaggleEnabled == true ? "OFF" : "ON")
            : "HAGGLE";
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
        button.Background = selected ? HudAccent : HudHeaderAlt;
        button.BorderBrush = selected ? HudAccentHot : HudInnerEdge;
        button.BorderThickness = new Thickness(1);
        button.Foreground = selected ? HudAccentInk : HudMuted;
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
        => SetMombotCurrentVars(value, "$BOT~REDALERT", "$BOT~redalert", "$bot~redalert", "$redalert");

    private void SyncRedAlertFromMombotVar()
        => SetRedAlertEnabled(IsMombotTruthy(ReadCurrentMombotVar("FALSE", "$BOT~REDALERT", "$BOT~redalert", "$bot~redalert", "$redalert")));

    internal void TriggerRedAlert()
    {
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
        if (_redAlertEnabled == enabled)
        {
            _statusRedAlertFrame.IsVisible = _redAlertEnabled;
            ApplyStatusRedAlertButtonStyle(_statusRedAlertButton, _redAlertEnabled);
            return;
        }

        _redAlertEnabled = enabled;
        if (enabled)
            RestartRedAlertTimer();
        else
            _redAlertTimer.Stop();
        ApplyRedAlertPalette(enabled);
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
        if (_terminalLivePaused == paused && (_gameInstance != null || !paused))
        {
            UpdateTerminalLiveSelector();
            return;
        }

        if (paused)
        {
            _terminalLivePaused = true;
            ApplyEmbeddedTerminalOutputMode();
            UpdateTerminalLiveSelector();
            return;
        }

        _terminalLivePaused = false;
        ApplyEmbeddedTerminalOutputMode();
        FlushPausedTerminalChunksToDisplay();
        UpdateTerminalLiveSelector();
    }

    private void ApplyEmbeddedTerminalOutputMode()
    {
        if (_gameInstance == null)
            return;

        _gameInstance.SetClientType(
            EmbeddedLocalClientIndex,
            _terminalLivePaused ? Core.ClientType.Deaf : Core.ClientType.Standard);
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

    private void DrainPendingDisplayChunks()
    {
        bool replayed = false;
        bool rewrotePromptOverwrite = false;

        while (_pendingDisplayChunks.TryDequeue(out PendingDisplayChunk chunk))
        {
            if (chunk.Bytes.Length > 0)
            {
                _parser.Feed(chunk.Bytes, chunk.Bytes.Length);
                replayed = true;
            }

            rewrotePromptOverwrite |= chunk.RewrotePromptOverwrite;
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
            string path = AppPaths.TwxproxyGameConfigFileFor(gameName);
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

        EmbeddedGameConfig config = _embeddedGameConfig ?? await LoadOrCreateEmbeddedGameConfigAsync(gameName);
        config = BuildEmbeddedGameConfigFromState(gameName, config);
        if (string.IsNullOrWhiteSpace(config.DatabasePath))
            config.DatabasePath = AppPaths.TwxproxyDatabasePathForGame(gameName);
        await SaveEmbeddedGameConfigAsync(gameName, config);
        _embeddedGameConfig = config;
        _embeddedGameName = gameName;
        _currentProfilePath ??= AppPaths.TwxproxyGameConfigFileFor(gameName);
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

            string sharedConfigPath = AppPaths.TwxproxyGameConfigFileFor(config.Name);
            if (string.Equals(Path.GetFullPath(path), Path.GetFullPath(sharedConfigPath), StringComparison.OrdinalIgnoreCase))
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
            string importedDatabasePath = AppPaths.TwxproxyDatabasePathForGame(gameName);
            if (!string.IsNullOrWhiteSpace(config.DatabasePath) && File.Exists(config.DatabasePath))
            {
                if (!await ImportDatabaseIntoSharedStoreAsync(config.DatabasePath, gameName))
                    return;
            }

            EmbeddedGameConfig importedConfig = BuildEmbeddedGameConfigFromProfile(importedProfile, importedDatabasePath, config);
            importedConfig.Variables = NormalizeEmbeddedVariables(config.Variables);
            await SaveEmbeddedGameConfigAsync(gameName, importedConfig);
            await ApplyLoadedGameConfigAsync(importedConfig, AppPaths.TwxproxyGameConfigFileFor(gameName), addToRecent);
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
            string sharedDbPath = AppPaths.TwxproxyDatabasePathForGame(gameName);
            if (!string.IsNullOrWhiteSpace(legacy.TwxProxyDbPath) && File.Exists(legacy.TwxProxyDbPath))
            {
                if (!await ImportDatabaseIntoSharedStoreAsync(legacy.TwxProxyDbPath, gameName))
                    return;
            }

            EmbeddedGameConfig config = BuildEmbeddedGameConfigFromProfile(legacy, sharedDbPath);
            await SaveEmbeddedGameConfigAsync(gameName, config);
            string configPath = AppPaths.TwxproxyGameConfigFileFor(gameName);
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
        string defaultSharedDatabasePath = AppPaths.TwxproxyDatabasePathForGame(defaultGameName);
        string defaultConfigPath = AppPaths.TwxproxyGameConfigFileFor(defaultGameName);

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
        if (!await ImportDatabaseIntoSharedStoreAsync(databasePath, gameName))
            return;

        string sharedDbPath = AppPaths.TwxproxyDatabasePathForGame(gameName);
        EmbeddedGameConfig config = BuildEmbeddedGameConfigFromProfile(imported, sharedDbPath);
        await SaveEmbeddedGameConfigAsync(gameName, config);
        string configPath = AppPaths.TwxproxyGameConfigFileFor(gameName);
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
            if (string.IsNullOrWhiteSpace(config.DatabasePath))
                config.DatabasePath = AppPaths.TwxproxyDatabasePathForGame(config.Name);
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
            if (!GameNameConflicts(working.Name, currentConfigPath, currentDatabasePath))
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

    private async Task<bool> ImportDatabaseIntoSharedStoreAsync(string sourceDatabasePath, string targetGameName)
    {
        string targetPath = AppPaths.TwxproxyDatabasePathForGame(targetGameName);
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
                dbPath = AppPaths.DatabasePathForGame(gameName);
            }

            var db = new Core.ModDatabase();
            if (File.Exists(dbPath))
            {
                db.OpenDatabase(dbPath);
                db.UseCache = _embeddedGameConfig?.UseCache ?? true;
                var header = db.DBHeader;
                bool headerDirty = false;
                if (sectors > 0)
                {
                    headerDirty |= header.Sectors != sectors;
                    header.Sectors = sectors;
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
                    Sectors    = sectors,
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

        var proxyItems = BuildProxyMenuItems(gameName, hasGame, hasDatabase, hasInterpreter, canPlayCapture);
        _proxyMenu.ItemsSource = proxyItems;
        _botMenu.ItemsSource = BuildTopLevelBotMenuItems(hasInterpreter);
        _botMenu.IsEnabled = true;
        _quickMenu.ItemsSource = BuildQuickMenuItems(hasInterpreter);
        _quickMenu.IsEnabled = true;
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
            items.Add(new MenuItem { Header = "No proxy scripts active", IsEnabled = false });
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

    private async Task HandleNativeMombotDisconnectAsync()
    {
        await Task.Yield();

        if (!_mombot.Enabled)
            return;

        // Original Mombot's logoff path issues disconnect and then sets
        // do_not_resuscitate. Give the script a moment to persist that flag
        // before we decide whether native relog should fire.
        await Task.Delay(250);

        if (ShouldStopNativeMombotAfterDisconnect())
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
                DatabasePath = AppPaths.TwxproxyDatabasePathForGame(DeriveGameName()),
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
        SetMombotCurrentVars("FALSE", "$BOT~REDALERT", "$BOT~redalert", "$bot~redalert", "$redalert");
        PersistMombotVars(shipCapRelative, "$cap_file");
        PersistMombotVars(planetFileRelative, "$planet_file");

        string stardock = ReadCurrentMombotSectorVar(FormatMombotSector(_sessionDb?.DBHeader.StarDock), "$STARDOCK", "$MAP~STARDOCK", "$MAP~stardock", "$BOT~STARDOCK");
        string rylos = ReadCurrentMombotSectorVar(FormatMombotSector(_sessionDb?.DBHeader.Rylos), "$MAP~RYLOS", "$MAP~rylos", "$BOT~RYLOS");
        string alphaCentauri = ReadCurrentMombotSectorVar(FormatMombotSector(_sessionDb?.DBHeader.AlphaCentauri), "$MAP~ALPHA_CENTAURI", "$MAP~alpha_centauri", "$BOT~ALPHA_CENTAURI");
        MirrorMombotCurrentVars(stardock, "$STARDOCK", "$MAP~STARDOCK", "$MAP~stardock", "$BOT~STARDOCK", "$stardock");
        MirrorMombotCurrentVars(rylos, "$MAP~RYLOS", "$MAP~rylos", "$BOT~RYLOS", "$rylos");
        MirrorMombotCurrentVars(alphaCentauri, "$MAP~ALPHA_CENTAURI", "$MAP~alpha_centauri", "$BOT~ALPHA_CENTAURI", "$alpha_centauri");
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

    private void SyncMombotPromptStateFromLine(string line, string? ansiLine = null)
    {
        if (line.Contains("You will have to start over from scratch!", StringComparison.OrdinalIgnoreCase))
            SetMombotCurrentVars("1", "$BOT~ISSHIPDESTROYED");

        bool isGamePromptLine = TryGetMombotPromptNameFromLine(line, out string promptName);
        if (!isGamePromptLine)
        {
            CancelPendingMombotInteractivePromptRedraw();
            return;
        }

        if (isGamePromptLine)
        {
            _mombotObservedGamePromptVersion++;
            _mombotLastObservedGamePromptPlain = Core.AnsiCodes.NormalizeTerminalText(line).TrimEnd();
            _mombotLastObservedGamePromptAnsi = string.IsNullOrWhiteSpace(ansiLine)
                ? _mombotLastObservedGamePromptPlain
                : ansiLine;
            SetMombotCurrentVars(promptName, "$PLAYER~CURRENT_PROMPT", "$PLAYER~startingLocation", "$bot~startingLocation");
            SetMombotCurrentVars("0", "$relogging", "$connectivity~relogging");

            // When a native Mombot interactive prompt is open, reclaim the bottom line
            // after the server returns to a stable command/citadel prompt. We debounce
            // the redraw so echoed game prompts do not repaint over report/scan output.
            if (HasMombotInteractiveState() &&
                (string.Equals(promptName, "Command", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(promptName, "Citadel", StringComparison.OrdinalIgnoreCase)))
            {
                ScheduleMombotInteractivePromptRedraw(_mombotObservedGamePromptVersion);
            }
        }
    }

    private void CancelPendingMombotInteractivePromptRedraw()
    {
        unchecked
        {
            _mombotMacroPromptRedrawTicket++;
        }
    }

    private void ScheduleMombotInteractivePromptRedraw(int promptVersion)
    {
        int ticket;
        unchecked
        {
            ticket = ++_mombotMacroPromptRedrawTicket;
        }

        _ = Task.Run(async () =>
        {
            await Task.Delay(120).ConfigureAwait(false);
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (ticket != _mombotMacroPromptRedrawTicket)
                    return;

                if (!HasMombotInteractiveState())
                    return;

                if (_mombotObservedGamePromptVersion != promptVersion)
                    return;

                RedrawMombotPrompt();
            }, DispatcherPriority.Background);
        });
    }

    private async Task RunNativeMombotKeepaliveTickAsync()
    {
        try
        {
            await Task.Yield();

            if (!_mombot.Enabled || _gameInstance == null)
            {
                _mombotLastKeepaliveLine = string.Empty;
                return;
            }

            if (_gameInstance.IsConnected)
                await SendKeepaliveEscapeAsync();

            if (ShouldStopNativeMombotAfterDisconnect() || !ShouldNativeMombotAutoRelog())
            {
                _mombotLastKeepaliveLine = string.Empty;
                return;
            }

            if (IsNativeMombotRelogInProgress())
            {
                _mombotLastKeepaliveLine = string.Empty;
                return;
            }

            if (!_gameInstance.IsConnected)
            {
                _mombotLastKeepaliveLine = string.Empty;
                await TriggerNativeMombotRelogAsync(relogMessage: string.Empty, disconnectFirst: false);
                return;
            }

            string currentLine = NormalizeMombotPromptComparisonValue(Core.ScriptRef.GetCurrentLine());
            if (string.IsNullOrWhiteSpace(currentLine))
                return;

            bool stuckPrompt = IsNativeMombotReconnectPrompt(currentLine);
            if (stuckPrompt)
            {
                await TriggerNativeMombotRelogAsync(
                    relogMessage: $"Stuck on baffling prompt: [{currentLine}], so I relogged.*",
                    disconnectFirst: true);
                _mombotLastKeepaliveLine = string.Empty;
                return;
            }

            _mombotLastKeepaliveLine = currentLine;
        }
        finally
        {
            _mombotKeepaliveTickRunning = false;
        }
    }

    private async Task HandleEmbeddedKeepaliveWatchLineAsync(string line)
    {
        await Task.Yield();

        if (_gameInstance == null || !_gameInstance.IsConnected || string.IsNullOrWhiteSpace(line))
            return;

        if (line.Contains("Your session will be terminated in ", StringComparison.OrdinalIgnoreCase) ||
            line.Contains("You now have Thirty seconds until termination.", StringComparison.OrdinalIgnoreCase) ||
            line.Contains("Only TEN seconds remain.  Session termination is imminent.", StringComparison.OrdinalIgnoreCase))
        {
            await SendKeepaliveEscapeAsync();
        }
    }

    private async Task HandleNativeMombotWatchLineAsync(string line)
    {
        await Task.Yield();

        if (!_mombot.Enabled || _gameInstance == null || string.IsNullOrWhiteSpace(line))
            return;

        if (_gameInstance.IsConnected &&
            line.Contains("Your session will be terminated in ", StringComparison.OrdinalIgnoreCase))
        {
            await SendKeepaliveEscapeAsync();
        }

        await TryRunNativeMombotInitialSettingsAsync();
        await FinalizeNativeMombotStartupAsync();
    }

    private async Task SendKeepaliveEscapeAsync()
    {
        if (_gameInstance == null || !_gameInstance.IsConnected)
            return;

        await _gameInstance.SendToServerAsync(new byte[] { 0x1B });
        RegisterNativeMombotEscapeEchoSuppression();
    }

    private void RegisterNativeMombotEscapeEchoSuppression()
    {
        Interlocked.Increment(ref _pendingNativeMombotEscapeEchoSuppressions);
        Interlocked.Exchange(
            ref _nativeMombotEscapeEchoSuppressUntilUtcTicks,
            DateTime.UtcNow.AddSeconds(2).Ticks);
    }

    private byte[] FilterTerminalDisplayArtifacts(byte[] chunk, out bool rewrotePromptOverwrite)
    {
        rewrotePromptOverwrite = false;
        if (chunk.Length == 0)
            return chunk;

        lock (_terminalDisplayArtifactSync)
        {
            List<byte>? filtered = null;
            int index = 0;

            if (_pendingTerminalSyncMarkerLeadByte)
            {
                _pendingTerminalSyncMarkerLeadByte = false;

                if (chunk[0] == 0x08)
                {
                    filtered = new List<byte>(Math.Max(0, chunk.Length - 1));
                    index = 1;
                }
                else
                {
                    filtered = new List<byte>(chunk.Length + 1) { 0x91 };
                }
            }

            for (; index < chunk.Length; index++)
            {
                byte value = chunk[index];

                if (_suppressingPendingNativeMombotEscapeSequence)
                {
                    filtered ??= new List<byte>(chunk.Length);

                    if (!_suppressingPendingNativeMombotEscapeCsiBody)
                    {
                        if (value == (byte)'[')
                        {
                            _suppressingPendingNativeMombotEscapeCsiBody = true;
                            continue;
                        }

                        _suppressingPendingNativeMombotEscapeSequence = false;
                        _suppressingPendingNativeMombotEscapeCsiBody = false;
                        continue;
                    }

                    if (value >= 0x40 && value <= 0x7E)
                    {
                        _suppressingPendingNativeMombotEscapeSequence = false;
                        _suppressingPendingNativeMombotEscapeCsiBody = false;
                    }

                    continue;
                }

                if (ShouldSuppressPendingNativeMombotEscapeEcho(value))
                {
                    filtered ??= new List<byte>(chunk.Length);
                    _suppressingPendingNativeMombotEscapeSequence = true;
                    _suppressingPendingNativeMombotEscapeCsiBody = false;
                    continue;
                }

                if (value == 0x91)
                {
                    if (index + 1 < chunk.Length)
                    {
                        if (chunk[index + 1] == 0x08)
                        {
                            filtered ??= new List<byte>(chunk.Length);
                            index++;
                            continue;
                        }
                    }
                    else
                    {
                        filtered ??= new List<byte>(chunk.Length);
                        _pendingTerminalSyncMarkerLeadByte = true;
                        continue;
                    }
                }

                if (value == 0x1B &&
                    index + 6 < chunk.Length &&
                    chunk[index + 1] == (byte)'[' &&
                    chunk[index + 2] == (byte)'K' &&
                    chunk[index + 3] == 0x1B &&
                    chunk[index + 4] == (byte)'[' &&
                    chunk[index + 5] == (byte)'1' &&
                    chunk[index + 6] == (byte)'A')
                {
                    filtered ??= new List<byte>(chunk.Length + 8);
                    filtered.Add(0x0D);
                    filtered.Add(0x1B);
                    filtered.Add((byte)'[');
                    filtered.Add((byte)'K');
                    filtered.Add(0x1B);
                    filtered.Add((byte)'[');
                    filtered.Add((byte)'1');
                    filtered.Add((byte)'A');
                    filtered.Add(0x0D);
                    filtered.Add(0x1B);
                    filtered.Add((byte)'[');
                    filtered.Add((byte)'K');
                    rewrotePromptOverwrite = true;
                    index += 6;
                    continue;
                }

                filtered?.Add(value);
            }

            return filtered == null ? chunk : filtered.ToArray();
        }
    }

    private bool ShouldSuppressPendingNativeMombotEscapeEcho(byte value)
    {
        if (value != 0x1B)
            return false;

        int pending = Interlocked.CompareExchange(ref _pendingNativeMombotEscapeEchoSuppressions, 0, 0);
        if (pending <= 0)
            return false;

        long suppressUntilTicks = Interlocked.Read(ref _nativeMombotEscapeEchoSuppressUntilUtcTicks);
        if (suppressUntilTicks <= 0 || DateTime.UtcNow.Ticks > suppressUntilTicks)
        {
            Interlocked.Exchange(ref _pendingNativeMombotEscapeEchoSuppressions, 0);
            Interlocked.Exchange(ref _nativeMombotEscapeEchoSuppressUntilUtcTicks, 0);
            return false;
        }

        if (Interlocked.Decrement(ref _pendingNativeMombotEscapeEchoSuppressions) <= 0)
            Interlocked.Exchange(ref _nativeMombotEscapeEchoSuppressUntilUtcTicks, 0);

        return true;
    }

    private void ScheduleLatestObservedGamePromptRestoreAfterQuiet()
    {
        int ticket = ++_serverOverwritePromptRestoreTicket;
        _ = Task.Run(async () =>
        {
            while (true)
            {
                await Task.Delay(250).ConfigureAwait(false);

                if (ticket != _serverOverwritePromptRestoreTicket)
                    return;

                long lastServerOutputUtcTicks = Interlocked.Read(ref _mombotLastServerOutputUtcTicks);
                if (lastServerOutputUtcTicks > 0)
                {
                    DateTime lastServerOutputUtc = new(lastServerOutputUtcTicks, DateTimeKind.Utc);
                    if ((DateTime.UtcNow - lastServerOutputUtc) < TimeSpan.FromMilliseconds(250))
                        continue;
                }

                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    if (ticket != _serverOverwritePromptRestoreTicket)
                        return;

                    if (HasNonBotScriptsRunning())
                        return;

                    if (HasMombotInteractiveState())
                        return;

                    TryRestoreLatestObservedGamePrompt();
                }, DispatcherPriority.Background);
                return;
            }
        });
    }

    private bool ShouldNativeMombotAutoRelog()
    {
        if (!IsMombotTruthy(ReadCurrentMombotVar("0", "$BOT~DORELOG", "$doRelog")) ||
            IsNativeMombotShipDestroyed())
        {
            return false;
        }

        return !ShouldPromptForMombotRelogSettings(BuildMombotRelogDefaults());
    }

    private bool IsNativeMombotRelogInProgress()
    {
        return IsMombotTruthy(ReadCurrentMombotVar("0", "$relogging", "$connectivity~relogging")) ||
               IsNativeMombotRelogScriptLoaded();
    }

    private bool IsNativeMombotShipDestroyed()
    {
        return IsMombotTruthy(ReadCurrentMombotVar("0", "$BOT~ISSHIPDESTROYED"));
    }

    private async Task TriggerNativeMombotRelogAsync(string relogMessage, bool disconnectFirst)
    {
        await Task.Yield();

        if (!_mombot.Enabled || _gameInstance == null || ShouldStopNativeMombotAfterDisconnect())
            return;

        if (!ShouldNativeMombotAutoRelog() || IsNativeMombotRelogInProgress())
            return;

        SetMombotCurrentVars("1", "$relogging", "$connectivity~relogging");
        if (!string.IsNullOrWhiteSpace(relogMessage))
            SetMombotCurrentVars(relogMessage, "$relog_message");

        if (disconnectFirst && _gameInstance.IsConnected)
            await _gameInstance.DisconnectFromServerAsync();

        await ExecuteMombotUiCommandAsync("relog");
    }

    private bool IsNativeMombotRelogScriptLoaded()
    {
        return IsNativeMombotScriptLoaded("relog.cts");
    }

    private bool IsNativeMombotReconnectPrompt(string line)
    {
        string normalized = NormalizeMombotPromptComparisonValue(line);
        if (string.IsNullOrWhiteSpace(normalized))
            return false;

        string gameMenuPrompt = NormalizeMombotPromptComparisonValue(
            ReadCurrentMombotVar(string.Empty, "$GAME~GAME_MENU_PROMPT", "$GAME_MENU_PROMPT"));

        return (!string.IsNullOrWhiteSpace(gameMenuPrompt) &&
                string.Equals(normalized, gameMenuPrompt, StringComparison.OrdinalIgnoreCase)) ||
               string.Equals(normalized, "[Pause] - [Press Space or Enter to continue]", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(normalized, "Enter your choice:", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(normalized, "Selection (? for menu):", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeMombotPromptComparisonValue(string value)
    {
        return Core.AnsiCodes.NormalizeTerminalText(value ?? string.Empty).Trim();
    }

    private string GetInitialMombotPromptName()
    {
        return GetMombotPromptSurface() switch
        {
            MombotPromptSurface.Command => "Command",
            MombotPromptSurface.Citadel => "Citadel",
            MombotPromptSurface.Planet => "Planet",
            _ => "Undefined",
        };
    }

    private static bool TryGetMombotPromptNameFromLine(string line, out string promptName)
    {
        promptName = string.Empty;
        if (string.IsNullOrWhiteSpace(line))
            return false;

        if (line.StartsWith("Command [TL=", StringComparison.OrdinalIgnoreCase))
        {
            promptName = "Command";
            return true;
        }

        if (line.StartsWith("Computer command [TL=", StringComparison.OrdinalIgnoreCase))
        {
            promptName = "Computer";
            return true;
        }

        if (line.StartsWith("Citadel command (", StringComparison.OrdinalIgnoreCase) ||
            line.Contains("<Enter Citadel>", StringComparison.OrdinalIgnoreCase) ||
            line.Contains("Citadel treasury contains", StringComparison.OrdinalIgnoreCase))
        {
            promptName = "Citadel";
            return true;
        }

        int commandIndex = line.IndexOf(" command (", StringComparison.OrdinalIgnoreCase);
        if (commandIndex > 0)
        {
            string candidate = line[..commandIndex].Trim();
            if (!string.IsNullOrWhiteSpace(candidate))
            {
                promptName = candidate;
                return true;
            }
        }

        return false;
    }

    private static void SetMombotCurrentVars(string value, params string[] names)
    {
        foreach (string name in names)
            Core.ScriptRef.SetCurrentGameVar(name, value);
    }

    private static void MirrorMombotCurrentVars(string fallback, params string[] names)
    {
        SetMombotCurrentVars(ReadCurrentMombotVar(fallback, names), names);
    }

    private static string ReadCurrentMombotVar(string fallback, params string[] names)
    {
        foreach (string name in names)
        {
            string value = Core.ScriptRef.GetCurrentGameVar(name, string.Empty);
            if (!string.IsNullOrWhiteSpace(value))
                return value;
        }

        return fallback;
    }

    private static string ReadCurrentMombotSectorVar(string fallback, params string[] names)
    {
        string? firstNonEmpty = null;
        foreach (string name in names)
        {
            string value = Core.ScriptRef.GetCurrentGameVar(name, string.Empty);
            if (string.IsNullOrWhiteSpace(value))
                continue;

            firstNonEmpty ??= value;
            if (IsDefinedMombotSectorValue(value))
                return value;
        }

        return IsDefinedMombotSectorValue(fallback) ? fallback : (firstNonEmpty ?? fallback);
    }

    private static string GetMombotVersionDisplay()
    {
        string major = ReadCurrentMombotVar("5", "$BOT~MAJOR_VERSION", "$bot~major_version", "$major_version");
        string minor = ReadCurrentMombotVar("0beta", "$BOT~MINOR_VERSION", "$bot~minor_version", "$minor_version");
        return string.IsNullOrWhiteSpace(minor) ? major : $"{major}.{minor}";
    }

    private static string FormatMombotSector(ushort? sector)
    {
        if (!sector.HasValue)
            return "0";

        ushort value = sector.Value;
        return value == 0 || value == ushort.MaxValue ? "0" : value.ToString();
    }

    private static string FirstMeaningfulMombotValue(params string?[] candidates)
    {
        foreach (string? candidate in candidates)
        {
            string normalized = NormalizeMombotValue(candidate, treatSelfAsEmpty: true);
            if (!string.IsNullOrEmpty(normalized))
                return normalized;
        }

        return string.Empty;
    }

    private static string NormalizeMombotValue(string? value, bool treatSelfAsEmpty = false)
    {
        string trimmed = (value ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
            return string.Empty;
        if (string.Equals(trimmed, "0", StringComparison.OrdinalIgnoreCase))
            return string.Empty;
        if (treatSelfAsEmpty && string.Equals(trimmed, "self", StringComparison.OrdinalIgnoreCase))
            return string.Empty;
        return trimmed;
    }

    private static bool IsDefinedMombotSectorValue(string? value)
    {
        if (!int.TryParse(value, out int sector))
            return false;

        return sector > 0 && sector != ushort.MaxValue;
    }

    private static string NormalizeGameLetter(string? value)
    {
        string normalized = NormalizeMombotValue(value);
        return string.IsNullOrEmpty(normalized) ? string.Empty : normalized[..1].ToUpperInvariant();
    }

    private static bool LooksLikeEstablishedRelogProfile(
        string loginName,
        string password,
        string gameLetter,
        string? traderName,
        string? gameStats,
        string? currentSector)
    {
        bool hasCredentials =
            !string.IsNullOrWhiteSpace(NormalizeMombotValue(loginName, treatSelfAsEmpty: true)) &&
            !string.IsNullOrWhiteSpace(NormalizeMombotValue(password)) &&
            !string.IsNullOrWhiteSpace(NormalizeGameLetter(gameLetter));

        bool hasTrader = !string.IsNullOrWhiteSpace(NormalizeMombotValue(traderName));
        bool hasGameStats = IsMombotTruthy(gameStats ?? string.Empty);
        bool hasCurrentSector = ParseGameVarInt(currentSector ?? "0") > 0;

        return hasCredentials && (hasTrader || hasGameStats || hasCurrentSector);
    }

    private static bool NormalizeEmbeddedRelogFlagsIfEstablished(EmbeddedGameConfig config)
    {
        config.Variables = NormalizeEmbeddedVariables(config.Variables);

        string loginName = NormalizeMombotValue(config.LoginName, treatSelfAsEmpty: true);
        string password = NormalizeMombotValue(config.Password);
        string gameLetter = NormalizeGameLetter(config.GameLetter);
        string traderName = config.Variables.TryGetValue("$PLAYER~TRADER_NAME", out string? trader) ? trader : string.Empty;
        string gameStats = config.Variables.TryGetValue("$GAME~GAMESTATS", out string? stats) ? stats : "0";
        string currentSector = config.Variables.TryGetValue("$PLAYER~CURRENT_SECTOR", out string? sector) ? sector : "0";

        if (!LooksLikeEstablishedRelogProfile(loginName, password, gameLetter, traderName, gameStats, currentSector))
            return false;

        bool changed = false;
        changed |= SetNormalizedEmbeddedVar(config.Variables, "$BOT~NEWGAMEDAY1", "0");
        changed |= SetNormalizedEmbeddedVar(config.Variables, "$BOT~NEWGAMEOLDER", "1");

        if (changed)
        {
            Core.GlobalModules.DebugLog(
                $"[MTC.RelogDefaults] normalized stale relog flags in config for game='{config.Name}'\n");
        }

        return changed;
    }

    private static bool SetNormalizedEmbeddedVar(IDictionary<string, string> vars, string name, string value)
    {
        if (vars.TryGetValue(name, out string? existing) && string.Equals(existing, value, StringComparison.Ordinal))
            return false;

        vars[name] = value;
        return true;
    }

    private async Task OpenAiAssistantAsync(string moduleId)
    {
        var binding = _moduleHost?
            .GetModules<Core.IExpansionChatModule>()
            .FirstOrDefault(module => string.Equals(module.Info.Id, moduleId, StringComparison.OrdinalIgnoreCase));

        if (binding == null)
        {
            await ShowMessageAsync("AI Assistant", "The selected AI module is not currently loaded.");
            return;
        }

        if (_assistantWindows.TryGetValue(moduleId, out AiAssistantWindow? existing))
        {
            existing.Show();
            existing.Activate();
            return;
        }

        var window = new AiAssistantWindow(binding.Module, _embeddedGameName ?? DeriveGameName());
        window.Closed += (_, _) => _assistantWindows.Remove(moduleId);
        _assistantWindows[moduleId] = window;
        window.Show();
        window.Activate();
    }

    private string GetEffectiveProxyScriptDirectory()
    {
        if (!string.IsNullOrWhiteSpace(CurrentInterpreter?.ScriptDirectory))
            return CurrentInterpreter.ScriptDirectory;

        if (!string.IsNullOrWhiteSpace(_appPrefs.ScriptsDirectory))
            return NormalizeScriptDirectoryValue(_appPrefs.ScriptsDirectory);

        if (!string.IsNullOrWhiteSpace(_embeddedGameConfig?.ScriptDirectory))
            return NormalizeScriptDirectoryValue(_embeddedGameConfig.ScriptDirectory);

        return Core.SharedPathSettingsStore.GetDefaultScriptsDirectory(_appPrefs.ProgramDirectory);
    }

    private static string GetEffectiveProxyProgramDir(string scriptDirectory)
    {
        if (!string.IsNullOrWhiteSpace(AppPaths.ProgramDir))
            return AppPaths.ProgramDir;

        string trimmed = scriptDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return Path.GetDirectoryName(trimmed) ?? trimmed;
    }

    private async Task EnsureSharedPathsConfiguredAsync()
    {
        if (_appPrefs.HasConfiguredSharedPaths)
            return;

        var storage = TopLevel.GetTopLevel(this)?.StorageProvider;
        if (storage == null)
            return;

        string defaultProgramDir = Core.SharedPaths.GetDefaultProgramDir();
        IStorageFolder? startFolder = null;
        try
        {
            startFolder = await storage.TryGetFolderFromPathAsync(defaultProgramDir);
        }
        catch
        {
        }

        var folders = await storage.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Select TWX Program Directory",
            SuggestedStartLocation = startFolder,
            AllowMultiple = false,
        });

        if (folders.Count == 0)
            return;

        string programDir = folders[0].Path.LocalPath;
        _appPrefs.ProgramDirectory = programDir;
        _appPrefs.ScriptsDirectory = Core.SharedPathSettingsStore.GetDefaultScriptsDirectory(programDir);
        _appPrefs.Save();
        AppPaths.SetConfiguredProgramDir(_appPrefs.ProgramDirectory);
        ApplyDebugLoggingPreferences();
        RebuildScriptsMenu();
    }

    private async Task OnProxyLoadScriptAsync()
    {
        await Task.Yield();

        var interpreter = CurrentInterpreter;
        if (interpreter == null)
            return;

        var storage = TopLevel.GetTopLevel(this)?.StorageProvider;
        if (storage == null)
            return;

        IStorageFolder? start = null;
        string preferred = !string.IsNullOrWhiteSpace(_appPrefs.ScriptsDirectory)
            ? _appPrefs.ScriptsDirectory
            : GetEffectiveProxyScriptDirectory();

        try
        {
            start = await storage.TryGetFolderFromPathAsync(preferred);
        }
        catch { }

        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Load TWX Script",
            SuggestedStartLocation = start,
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("TWX Scripts") { Patterns = ["*.ts", "*.cts"] },
                new FilePickerFileType("All Files") { Patterns = ["*"] },
            ],
        });

        if (files.Count == 0)
            return;

        string fullPath = files[0].Path.LocalPath;
        string scriptPath = fullPath;
        if (!string.IsNullOrWhiteSpace(interpreter.ScriptDirectory))
        {
            string fullRoot = Path.GetFullPath(interpreter.ScriptDirectory)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                + Path.DirectorySeparatorChar;
            string candidate = Path.GetFullPath(fullPath);
            if (candidate.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase))
                scriptPath = Path.GetRelativePath(interpreter.ScriptDirectory, fullPath).Replace('\\', '/');
        }

        try
        {
            Core.ProxyGameOperations.LoadScript(interpreter, scriptPath);
            _parser.Feed($"\x1b[1;36m[Loaded script: {scriptPath}]\x1b[0m\r\n");
            _buffer.Dirty = true;
        }
        catch (Exception ex)
        {
            await ShowMessageAsync("Load Script Failed", ex.Message);
        }

        RebuildProxyMenu();
        FocusActiveTerminal();
    }

    private async Task LoadQuickScriptAsync(string relativePath)
    {
        // Let the menu close before running synchronous proxy work that can
        // update the terminal and rebuild menus.
        await Task.Yield();

        var interpreter = CurrentInterpreter;
        if (interpreter == null)
            return;

        try
        {
            Core.ProxyGameOperations.LoadScript(interpreter, relativePath);
            _parser.Feed($"\x1b[1;36m[Loaded quick script: {relativePath}]\x1b[0m\r\n");
            _buffer.Dirty = true;
        }
        catch (Exception ex)
        {
            await ShowMessageAsync("Quick Load Failed", ex.Message);
        }

        RebuildProxyMenu();
        FocusActiveTerminal();
    }

    private async Task SwitchBotAsync(string botName)
    {
        // Let the menu close before running synchronous bot-switch logic on
        // the UI thread, otherwise the dropdown can remain visually stuck.
        await Task.Yield();

        try
        {
            CurrentInterpreter?.SwitchBot(string.Empty, botName, stopBotScripts: true);
            _parser.Feed($"\x1b[1;36m[Switched bot: {botName}]\x1b[0m\r\n");
            _buffer.Dirty = true;
        }
        catch (Exception ex)
        {
            await ShowMessageAsync("Switch Bot Failed", ex.Message);
        }

        RebuildProxyMenu();
        FocusActiveTerminal();
    }

    private enum MombotPromptSurface
    {
        Unknown,
        Command,
        Citadel,
        Planet,
        Computer,
    }

    private enum MombotPreferencesPage
    {
        General,
        GameStats,
        Hotkeys,
        ShipInfo,
        PlanetTypes,
        PlanetList,
        TraderList,
    }

    private enum MombotPreferencesBlankSubmitBehavior
    {
        Ignore,
        Submit,
    }

    private sealed record MombotGridContext(
        MombotPromptSurface Surface,
        int CurrentSector,
        IReadOnlyList<int> AdjacentSectors,
        int PlanetNumber,
        bool Connected,
        int PhotonCount);

    private sealed record MombotHotkeyScriptEntry(
        int Slot,
        string LoadReference,
        string DisplayName);

    private sealed record MombotShipCatalogEntry(
        string Name,
        string Shields,
        string DefOdds,
        string OffOdds,
        string Cost,
        string MaxHolds,
        string MaxFighters,
        string InitHolds,
        string Tpw,
        bool Defender);

    private sealed record MombotPlanetCatalogEntry(
        string Name,
        string FuelMin,
        string FuelMax,
        string OrgMin,
        string OrgMax,
        string EquipMin,
        string EquipMax,
        bool Keeper);

    private void SendToTelnet(byte[] bytes)
    {
        if (_telnet.IsConnected)
            _telnet.SendRaw(bytes);
        else
            _parser.Feed("\x1b[33m[not connected]\x1b[0m\r\n");
    }

    private void RouteTerminalInput(byte[] bytes, Action<byte[]> forward)
    {
        RecordTemporaryMacroInput(bytes);

        if (TryHandleConfiguredMacroHotkey(bytes))
            return;

        if (TryHandleMombotPromptInput(bytes))
            return;

        if (bytes.Length > 1 && bytes[0] == 0x09)
        {
            if (TryInterceptMombotHotkeyAccess(new byte[] { 0x09 }))
            {
                byte[] remaining = bytes[1..];
                if (remaining.Length == 0)
                    return;

                if (TryHandleMombotPromptInput(remaining))
                    return;

                if (TryInterceptMombotCommandPrompt(remaining))
                    return;

                forward(remaining);
                return;
            }
        }

        if (TryInterceptMombotHotkeyAccess(bytes))
            return;

        if (TryInterceptMombotCommandPrompt(bytes))
            return;

        forward(bytes);
    }

    private void StartTemporaryMacroRecording()
    {
        if (!HasActiveMacroConnection())
        {
            ShowMacroNotice("temporary macro recording requires an active connection");
            return;
        }

        _temporaryMacroChunks.Clear();
        _temporaryMacroRecording = true;
        UpdateTemporaryMacroControls();
        ShowMacroNotice($"temporary macro recording started ({TemporaryMacroMaxCharacters} characters max)");
        FocusActiveTerminal();
    }

    private void StopTemporaryMacroRecording()
    {
        if (!_temporaryMacroRecording)
        {
            FocusActiveTerminal();
            return;
        }

        _temporaryMacroRecording = false;
        UpdateTemporaryMacroControls();
        ShowMacroNotice($"temporary macro recording stopped ({GetTemporaryMacroText().Length} characters)");
        FocusActiveTerminal();
    }

    private void ResetTemporaryMacroSession()
    {
        _temporaryMacroRecording = false;
        _temporaryMacroChunks.Clear();
        UpdateTemporaryMacroControls();
    }

    private void RecordTemporaryMacroInput(byte[] bytes)
    {
        if (!_temporaryMacroRecording || _suppressTemporaryMacroRecording || bytes.Length == 0)
            return;

        var currentBytes = new List<byte>(GetTemporaryMacroBytes());
        var acceptedBytes = new List<byte>(bytes.Length);
        bool reachedLimit = false;

        foreach (byte value in bytes)
        {
            currentBytes.Add(value);
            if (EncodeTemporaryMacroBytes(currentBytes).Length > TemporaryMacroMaxCharacters)
            {
                currentBytes.RemoveAt(currentBytes.Count - 1);
                reachedLimit = true;
                break;
            }

            acceptedBytes.Add(value);
        }

        if (acceptedBytes.Count > 0)
            _temporaryMacroChunks.Add(acceptedBytes.ToArray());

        if (!reachedLimit)
            return;

        _temporaryMacroRecording = false;
        UpdateTemporaryMacroControls();
        ShowMacroNotice($"temporary macro recorder stopped at {TemporaryMacroMaxCharacters} characters");
    }

    private async Task PlayTemporaryMacroAsync()
    {
        if (_temporaryMacroRecording)
            return;

        if (!HasActiveMacroConnection())
        {
            ShowMacroNotice("temporary macro playback requires an active connection");
            return;
        }

        string macroText = GetTemporaryMacroText();
        if (string.IsNullOrWhiteSpace(macroText))
        {
            ShowMacroNotice("temporary macro is empty");
            return;
        }

        var dialog = new MacroPlayDialog(macroText, ValidateTemporaryMacroText);
        bool accepted = await dialog.ShowDialog<bool>(this);
        if (!accepted)
        {
            FocusActiveTerminal();
            return;
        }

        if (!TryDecodeTemporaryMacroText(dialog.MacroText, out byte[] updatedMacroBytes, out string? parseError))
        {
            ShowMacroNotice(parseError ?? "temporary macro is invalid");
            FocusActiveTerminal();
            return;
        }

        _temporaryMacroChunks.Clear();
        if (updatedMacroBytes.Length > 0)
            _temporaryMacroChunks.Add(updatedMacroBytes);
        UpdateTemporaryMacroControls();

        string? error = await PlayTemporaryMacroBurstAsync(_temporaryMacroChunks, dialog.PlayCount);
        if (!string.IsNullOrWhiteSpace(error))
            ShowMacroNotice(error);

        FocusActiveTerminal();
    }

    private Task<string?> PlayTemporaryMacroBurstAsync(IReadOnlyList<byte[]> macroChunks, int count)
    {
        if (macroChunks.Count == 0 || macroChunks.All(chunk => chunk.Length == 0))
            return Task.FromResult<string?>("Temporary macro is empty.");

        if (!HasActiveMacroConnection())
            return Task.FromResult<string?>("Temporary macros need an active game connection.");

        Action<byte[]>? send = _terminalInputHandler;
        if (send == null)
            return Task.FromResult<string?>("Temporary macros need an active game connection.");

        byte[] macroPayload = GetCombinedMacroPayload(macroChunks);
        if (macroPayload.Length == 0)
            return Task.FromResult<string?>("Temporary macro is empty.");

        if (!TryBuildRepeatedMacroPayload(macroPayload, count, out byte[] burstPayload, out string? burstError))
            return Task.FromResult<string?>(burstError ?? "Temporary macro burst is invalid.");

        _suppressTemporaryMacroRecording = true;
        try
        {
            send(burstPayload);
        }
        finally
        {
            _suppressTemporaryMacroRecording = false;
        }

        return Task.FromResult<string?>(null);
    }

    private string GetTemporaryMacroText()
        => EncodeTemporaryMacroBytes(GetTemporaryMacroBytes());

    private byte[] GetTemporaryMacroBytes()
    {
        return GetCombinedMacroPayload(_temporaryMacroChunks);
    }

    private static string EncodeTemporaryMacroBytes(IEnumerable<byte> bytes)
    {
        var builder = new System.Text.StringBuilder();
        foreach (byte value in bytes)
        {
            switch (value)
            {
                case (byte)'\r':
                    builder.Append('*');
                    break;
                case (byte)'*':
                    builder.Append(@"\*");
                    break;
                case (byte)'\\':
                    builder.Append(@"\\");
                    break;
                default:
                    if (value is >= 32 and <= 126)
                        builder.Append((char)value);
                    else
                        builder.Append(@"\x").Append(value.ToString("X2"));
                    break;
            }
        }

        return builder.ToString();
    }

    private string? ValidateTemporaryMacroText(string macroText)
    {
        if (string.IsNullOrWhiteSpace(macroText))
            return "Enter a macro before playback.";

        if (macroText.Length > TemporaryMacroMaxCharacters)
            return $"Temporary macros are limited to {TemporaryMacroMaxCharacters} characters.";

        return TryDecodeTemporaryMacroText(macroText, out _, out string? error)
            ? null
            : error ?? "Macro text is invalid.";
    }

    private static bool TryDecodeTemporaryMacroText(string macroText, out byte[] bytes, out string? error)
    {
        var values = new List<byte>(macroText.Length);

        for (int index = 0; index < macroText.Length; index++)
        {
            char current = macroText[index];
            if (current == '*')
            {
                values.Add((byte)'\r');
                continue;
            }

            if (current != '\\')
            {
                if (current > byte.MaxValue)
                {
                    bytes = [];
                    error = "Temporary macros support Latin-1 text only.";
                    return false;
                }

                values.Add((byte)current);
                continue;
            }

            if (index == macroText.Length - 1)
            {
                bytes = [];
                error = "A backslash must be followed by \\\\, \\*, or \\xNN.";
                return false;
            }

            char next = macroText[++index];
            switch (next)
            {
                case '\\':
                    values.Add((byte)'\\');
                    break;
                case '*':
                    values.Add((byte)'*');
                    break;
                case 'x':
                case 'X':
                    if (index + 2 >= macroText.Length ||
                        !byte.TryParse(macroText.Substring(index + 1, 2), System.Globalization.NumberStyles.HexNumber, null, out byte hexValue))
                    {
                        bytes = [];
                        error = "Use \\x followed by two hex digits.";
                        return false;
                    }

                    values.Add(hexValue);
                    index += 2;
                    break;
                default:
                    bytes = [];
                    error = "A backslash must be followed by \\\\, \\*, or \\xNN.";
                    return false;
            }
        }

        bytes = values.ToArray();
        error = null;
        return true;
    }

    private void ShowMacroNotice(string message)
    {
        _parser.Feed($"\x1b[33m[{message}]\x1b[0m\r\n");
        _buffer.Dirty = true;
    }

    private bool TryHandleConfiguredMacroHotkey(byte[] bytes)
    {
        if (!TerminalControl.TryGetMacroHotkeyName(bytes, out string hotkey))
            return false;

        AppPreferences.MacroBinding? binding = _appPrefs.MacroBindings
            .LastOrDefault(entry =>
                string.Equals(entry.Hotkey, hotkey, StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(entry.Macro));

        if (binding == null)
            return false;

        _ = PromptAndPlayConfiguredMacroAsync(binding);
        return true;
    }

    private static string ExpandConfiguredMacro(string macro)
        => string.IsNullOrEmpty(macro) ? string.Empty : macro.Replace("*", "\r");

    private static bool TryGetConfiguredCommandMacro(string macro, out string commandText)
    {
        string trimmed = macro?.Trim() ?? string.Empty;
        if (trimmed.StartsWith(">", StringComparison.Ordinal))
        {
            commandText = trimmed[1..].Trim();
            return true;
        }

        commandText = string.Empty;
        return false;
    }

    private static bool TryGetConfiguredScriptMacro(string macro, out string scriptReference)
    {
        string trimmed = macro?.Trim() ?? string.Empty;
        if (trimmed.StartsWith("$", StringComparison.Ordinal))
        {
            scriptReference = trimmed[1..].Trim();
            return true;
        }

        scriptReference = string.Empty;
        return false;
    }

    private async Task PromptAndPlayConfiguredMacroAsync(AppPreferences.MacroBinding binding)
    {
        string macro = binding.Macro;
        if (string.IsNullOrWhiteSpace(macro))
            return;

        if (!HasActiveMacroConnection())
        {
            _parser.Feed("\x1b[33m[macro requires an active connection]\x1b[0m\r\n");
            _buffer.Dirty = true;
            return;
        }

        var dialog = new MacroPlayDialog(macro);
        bool accepted = await dialog.ShowDialog<bool>(this);
        if (!accepted)
        {
            FocusActiveTerminal();
            return;
        }

        string updatedMacro = dialog.MacroText;
        if (!string.Equals(binding.Macro, updatedMacro, StringComparison.Ordinal))
        {
            binding.Macro = updatedMacro;
            _appPrefs.Save();
        }

        string? error = await PlayConfiguredMacroBurstAsync(updatedMacro, dialog.PlayCount);
        if (!string.IsNullOrWhiteSpace(error))
        {
            _parser.Feed($"\x1b[33m[{error}]\x1b[0m\r\n");
            _buffer.Dirty = true;
        }

        FocusActiveTerminal();
    }

    private async Task<string?> PlayConfiguredMacroBurstAsync(string macro, int count)
    {
        if (string.IsNullOrWhiteSpace(macro))
            return "Macro is empty.";

        if (TryGetConfiguredCommandMacro(macro, out string commandText))
            return await PlayConfiguredCommandMacroAsync(commandText, count);

        if (TryGetConfiguredScriptMacro(macro, out string scriptReference))
            return await PlayConfiguredScriptMacroAsync(scriptReference, count);

        if (!HasActiveMacroConnection())
            return "Macros need an active game connection.";

        Action<byte[]>? send = _terminalInputHandler;
        if (send == null)
            return "Macros need an active game connection.";

        string expanded = ExpandConfiguredMacro(macro);
        if (string.IsNullOrEmpty(expanded))
            return null;

        byte[] payload = System.Text.Encoding.Latin1.GetBytes(expanded);
        if (payload.Length == 0)
            return null;

        if (!TryBuildRepeatedMacroPayload(payload, count, out byte[] burstPayload, out string? burstError))
            return burstError ?? "Macro burst is invalid.";

        send(burstPayload);
        return null;
    }

    private async Task<string?> PlayConfiguredCommandMacroAsync(string commandText, int count)
    {
        if (string.IsNullOrWhiteSpace(commandText))
            return "Enter a native Mombot command after >.";

        if (_gameInstance == null)
            return "Command macros need the embedded proxy to be running.";

        for (int i = 0; i < count; i++)
            await ExecuteMombotUiCommandAsync(commandText);

        FocusActiveTerminal();
        return null;
    }

    private async Task<string?> PlayConfiguredScriptMacroAsync(string scriptReference, int count)
    {
        if (string.IsNullOrWhiteSpace(scriptReference))
            return "Enter a script name after $.";

        if (count > 1)
            return "Script macros can only be played once at a time.";

        var interpreter = CurrentInterpreter;
        if (interpreter == null)
            return "Script macros need the embedded proxy to be running.";

        string normalizedReference = scriptReference.Trim().Replace('\\', '/');

        try
        {
            await Task.Yield();
            Core.ProxyGameOperations.LoadScript(interpreter, normalizedReference);
            _parser.Feed($"\x1b[1;36m[Loaded macro script: {normalizedReference}]\x1b[0m\r\n");
            _buffer.Dirty = true;
            RebuildProxyMenu();
            RebuildScriptsMenu();
            FocusActiveTerminal();
            return null;
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
    }

    private static byte[] GetCombinedMacroPayload(IEnumerable<byte[]> chunks)
    {
        int totalLength = 0;
        foreach (byte[] chunk in chunks)
            totalLength += chunk.Length;

        if (totalLength == 0)
            return [];

        byte[] payload = new byte[totalLength];
        int offset = 0;
        foreach (byte[] chunk in chunks)
        {
            if (chunk.Length == 0)
                continue;

            Buffer.BlockCopy(chunk, 0, payload, offset, chunk.Length);
            offset += chunk.Length;
        }

        return payload;
    }

    private static bool TryBuildRepeatedMacroPayload(byte[] payload, int count, out byte[] burstPayload, out string? error)
    {
        burstPayload = [];
        error = null;

        if (payload.Length == 0 || count <= 0)
            return true;

        long totalLength = (long)payload.Length * count;
        if (totalLength > int.MaxValue)
        {
            error = "Macro burst is too large to send in one pass.";
            return false;
        }

        if (count == 1)
        {
            burstPayload = payload;
            return true;
        }

        burstPayload = new byte[(int)totalLength];
        int offset = 0;
        for (int i = 0; i < count; i++)
        {
            Buffer.BlockCopy(payload, 0, burstPayload, offset, payload.Length);
            offset += payload.Length;
        }

        return true;
    }

    private bool TryHandleMombotPromptInput(byte[] bytes)
    {
        if (!_mombot.Enabled)
        {
            if (HasMombotInteractiveState())
                CloseMombotInteractiveState();
            return false;
        }

        if (!_mombotPromptOpen && !_mombotHotkeyPromptOpen && !_mombotScriptPromptOpen && !_mombotPreferencesOpen)
            return false;

        if (_mombotPreferencesOpen)
            return TryHandleMombotPreferencesInput(bytes);

        if (_mombotScriptPromptOpen)
            return TryHandleMombotScriptPromptInput(bytes);

        if (_mombotHotkeyPromptOpen)
            return TryHandleMombotHotkeyPromptInput(bytes);

        if (_mombotMacroPromptOpen)
            return TryHandleMombotMacroPromptInput(bytes);

        if (!_mombotPromptOpen)
            return false;

        if (bytes.Length == 0)
            return true;

        if (MatchesMombotPromptSequence(bytes, 'A'))
        {
            RecallMombotPromptHistory(-1);
            return true;
        }

        if (MatchesMombotPromptSequence(bytes, 'B'))
        {
            RecallMombotPromptHistory(1);
            return true;
        }

        if (MatchesMombotPromptSequence(bytes, 'C'))
        {
            MoveMombotPromptCursor(1);
            return true;
        }

        if (MatchesMombotPromptSequence(bytes, 'D'))
        {
            MoveMombotPromptCursor(-1);
            return true;
        }

        if (MatchesMombotPromptSequence(bytes, 'H'))
        {
            SetMombotPromptCursor(0);
            return true;
        }

        if (MatchesMombotPromptSequence(bytes, 'F'))
        {
            SetMombotPromptCursor(_mombotPromptBuffer.Length);
            return true;
        }

        if (bytes.Length == 4 &&
            bytes[0] == 0x1B &&
            bytes[1] == (byte)'[' &&
            bytes[2] == (byte)'3' &&
            bytes[3] == (byte)'~')
        {
            if (DeleteMombotPromptCharacterAtCursor())
            {
                _mombotPromptHistoryIndex = _mombotCommandHistory.Count;
                _mombotPromptDraft = _mombotPromptBuffer;
                RedrawMombotPrompt();
            }
            return true;
        }

        if (bytes.Length == 1 && bytes[0] == 0x1B)
        {
            CancelMombotPrompt();
            return true;
        }

        bool changed = false;
        foreach (byte value in bytes)
        {
            switch (value)
            {
                case 0x08:
                case 0x7F:
                    changed = DeleteMombotPromptCharacterBeforeCursor() || changed;
                    break;

                case 0x0D:
                case 0x0A:
                    SubmitMombotPrompt();
                    return true;

                case 0x09:
                    BeginMombotHotkeyPrompt();
                    return true;

                default:
                    if (value >= 0x20)
                    {
                        if (value == (byte)'$')
                        {
                            CancelMombotPrompt();
                            return true;
                        }

                        if (value == (byte)'>' && _mombotPromptBuffer.Length == 0)
                        {
                            BeginMombotMacroPrompt();
                            return true;
                        }

                        InsertMombotPromptCharacter((char)value);
                        changed = true;
                    }
                    break;
            }
        }

        if (changed)
        {
            _mombotPromptHistoryIndex = _mombotCommandHistory.Count;
            _mombotPromptDraft = _mombotPromptBuffer;
            RedrawMombotPrompt();
        }

        return true;
    }

    private bool TryHandleMombotMacroPromptInput(byte[] bytes)
    {
        if (!_mombotMacroPromptOpen)
            return false;

        if (bytes.Length == 0)
            return true;

        foreach (byte value in bytes)
        {
            switch (value)
            {
                case 0x1B:
                case 0x0D:
                case 0x0A:
                    EndMombotMacroPrompt();
                    return true;

                case 0x09:
                    EndMombotMacroPrompt();
                    BeginMombotHotkeyPrompt();
                    return true;

                case (byte)'?':
                    PublishMombotLocalMessage(BuildMombotMacroHelpLine());
                    return true;

                default:
                    if (TryHandleMombotMacroKey(value))
                        return true;
                    break;
            }
        }

        PublishMombotLocalMessage(BuildMombotMacroHelpLine());
        return true;
    }

    private bool TryHandleMombotHotkeyPromptInput(byte[] bytes)
    {
        if (!_mombotHotkeyPromptOpen)
            return false;

        if (bytes.Length == 0)
            return true;

        foreach (byte value in bytes)
        {
            switch (value)
            {
                case 0x1B:
                case 0x0D:
                case 0x0A:
                    EndMombotHotkeyPrompt();
                    return true;

                case 0x09:
                    if (TryResolveMombotHotkeyCommand(0x09, out string? tabCommandOrAction) &&
                        !string.IsNullOrWhiteSpace(tabCommandOrAction))
                    {
                        _ = ExecuteMombotHotkeySelectionAsync(tabCommandOrAction);
                    }
                    else
                    {
                        _ = ExecuteMombotHotkeySelectionAsync(":INTERNAL_COMMANDS~stopModules");
                    }
                    return true;

                case (byte)'?':
                    _ = ExecuteMombotHotkeyCommandAsync("help");
                    return true;

                default:
                    if (value >= (byte)'0' && value <= (byte)'9')
                    {
                        _ = ExecuteMombotHotkeyScriptAsync(value == (byte)'0' ? 10 : value - (byte)'0');
                        return true;
                    }

                    if (TryResolveMombotHotkeyCommand(value, out string? commandOrAction) &&
                        !string.IsNullOrWhiteSpace(commandOrAction))
                    {
                        _ = ExecuteMombotHotkeySelectionAsync(commandOrAction);
                        return true;
                    }

                    EndMombotHotkeyPrompt();
                    return true;
            }
        }

        return true;
    }

    private bool TryHandleMombotScriptPromptInput(byte[] bytes)
    {
        if (!_mombotScriptPromptOpen)
            return false;

        if (bytes.Length == 0)
            return true;

        foreach (byte value in bytes)
        {
            switch (value)
            {
                case 0x1B:
                case 0x0D:
                case 0x0A:
                    EndMombotScriptPrompt();
                    return true;

                case (byte)'?':
                    PublishMombotScriptPromptList(_mombotHotkeyScripts);
                    RedrawMombotPrompt();
                    return true;

                default:
                    if (value >= (byte)'0' && value <= (byte)'9')
                    {
                        _ = ExecuteMombotHotkeyScriptAsync(value == (byte)'0' ? 10 : value - (byte)'0');
                        return true;
                    }

                    EndMombotScriptPrompt();
                    return true;
            }
        }

        return true;
    }

    private static bool MatchesMombotPromptSequence(byte[] bytes, char finalChar)
    {
        return bytes.Length == 3 &&
            bytes[0] == 0x1B &&
            bytes[1] == (byte)'[' &&
            bytes[2] == (byte)finalChar;
    }

    private void BeginMombotHotkeyPrompt()
    {
        if (_gameInstance == null)
        {
            PublishMombotLocalMessage("Mombot hotkeys are only available while the embedded proxy is running.");
            return;
        }

        if (!_mombot.Enabled)
        {
            PublishMombotLocalMessage("Enable Mombot first.");
            return;
        }

        bool preservePreferencesBotIsDeaf = _mombotPreferencesOpen;
        ResetMombotPromptState();
        if (preservePreferencesBotIsDeaf)
            PersistMombotBoolean(true, "$BOT~BOTISDEAF", "$BOT~botIsDeaf", "$bot~botIsDeaf", "$botIsDeaf");

        if (_mombotHotkeyPromptOpen)
            return;

        _mombotHotkeyPromptOpen = true;
        _mombotHotkeyScripts = Array.Empty<MombotHotkeyScriptEntry>();
        RedrawMombotPrompt();
    }

    private void EndMombotHotkeyPrompt()
    {
        _mombotHotkeyPromptOpen = false;
        _mombotScriptPromptOpen = false;
        _mombotHotkeyScripts = Array.Empty<MombotHotkeyScriptEntry>();

        if (_mombotPromptOpen)
            RedrawMombotPrompt();
        else
        {
            _parser.Feed("\r\x1b[K");
            _buffer.Dirty = true;
            if (!TryRestoreLatestObservedGamePrompt())
                FocusActiveTerminal();
        }
    }

    private void BeginMombotScriptPrompt()
    {
        IReadOnlyList<MombotHotkeyScriptEntry> scripts = LoadMombotHotkeyScripts();
        if (scripts.Count == 0)
        {
            PublishMombotLocalMessage("No Mombot hotkey scripts are configured.");
            return;
        }

        _mombotHotkeyPromptOpen = false;
        _mombotScriptPromptOpen = true;
        _mombotHotkeyScripts = scripts;

        PublishMombotScriptPromptList(scripts);
        RedrawMombotPrompt();
    }

    private void EndMombotScriptPrompt()
    {
        _mombotScriptPromptOpen = false;
        _mombotHotkeyScripts = Array.Empty<MombotHotkeyScriptEntry>();

        if (_mombotPromptOpen)
            RedrawMombotPrompt();
        else
        {
            _parser.Feed("\r\x1b[K");
            _buffer.Dirty = true;
            if (!TryRestoreLatestObservedGamePrompt())
                FocusActiveTerminal();
        }
    }

    private void BeginMombotPrompt(string initialValue = "", Func<string, string>? submitTransform = null)
    {
        if (_gameInstance == null)
        {
            PublishMombotLocalMessage("Mombot commands are only available while the embedded proxy is running.");
            return;
        }

        if (!_mombot.Enabled)
        {
            PublishMombotLocalMessage("Enable Mombot first.");
            return;
        }

        if (_mombotPromptOpen)
            return;

        EnsureMombotCommandHistoryLoaded();
        _mombotPromptOpen = true;
        _mombotPromptBuffer = initialValue;
        _mombotPromptDraft = initialValue;
        _mombotPromptSubmitTransform = submitTransform;
        _mombotPromptHistoryIndex = _mombotCommandHistory.Count;
        _mombotPromptCursorIndex = initialValue.Length;
        _mombotHotkeyPromptOpen = false;
        _mombotScriptPromptOpen = false;
        _mombotPreferencesOpen = false;
        _mombotMacroPromptOpen = false;
        _mombotMacroContext = null;
        _mombotHotkeyScripts = Array.Empty<MombotHotkeyScriptEntry>();
        RedrawMombotPrompt();
    }

    private void BeginMombotMacroPrompt()
    {
        if (!_mombotPromptOpen || _mombotMacroPromptOpen)
            return;

        if (_gameInstance == null || !_gameInstance.IsConnected)
        {
            PublishMombotLocalMessage("Mombot macros need an active game connection.");
            return;
        }

        MombotGridContext context = BuildMombotGridContext();
        if (context.Surface != MombotPromptSurface.Command &&
            context.Surface != MombotPromptSurface.Citadel)
        {
            PublishMombotLocalMessage("Mombot macros are available from command or citadel prompts.");
            return;
        }

        _mombotMacroContext = context;
        _mombotMacroPromptOpen = true;
        RedrawMombotPrompt();
    }

    private void EndMombotMacroPrompt()
    {
        _mombotMacroPromptOpen = false;
        _mombotMacroContext = null;
        RedrawMombotPrompt();
    }

    private string NormalizeMombotMowHotkeyCommand(string command)
    {
        string trimmed = (command ?? string.Empty).Trim();
        if (trimmed.Length == 0)
            return trimmed;

        string[] parts = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length != 2 || !string.Equals(parts[0], "mow", StringComparison.OrdinalIgnoreCase))
            return trimmed;

        string configuredDropCount = ReadCurrentMombotVar("1", "$PLAYER~surroundFigs", "$PLAYER~SURROUNDFIGS").Trim();
        if (string.IsNullOrWhiteSpace(configuredDropCount))
            configuredDropCount = "1";

        return $"mow {parts[1]} {configuredDropCount}";
    }

    private void RecallMombotPromptHistory(int delta)
    {
        if (!_mombotPromptOpen || _mombotCommandHistory.Count == 0)
            return;

        int count = _mombotCommandHistory.Count;
        if (_mombotPromptHistoryIndex == count)
            _mombotPromptDraft = _mombotPromptBuffer;

        _mombotPromptHistoryIndex = Math.Clamp(_mombotPromptHistoryIndex + delta, 0, count);
        _mombotPromptBuffer = _mombotPromptHistoryIndex >= count
            ? _mombotPromptDraft
            : _mombotCommandHistory[_mombotPromptHistoryIndex];
        _mombotPromptCursorIndex = _mombotPromptBuffer.Length;
        RedrawMombotPrompt();
    }

    private void MoveMombotPromptCursor(int delta)
    {
        if (!_mombotPromptOpen)
            return;

        SetMombotPromptCursor(_mombotPromptCursorIndex + delta);
    }

    private void SetMombotPromptCursor(int index)
    {
        if (!_mombotPromptOpen)
            return;

        int normalized = Math.Clamp(index, 0, _mombotPromptBuffer.Length);
        if (normalized == _mombotPromptCursorIndex)
            return;

        _mombotPromptCursorIndex = normalized;
        RedrawMombotPrompt();
    }

    private void InsertMombotPromptCharacter(char value)
    {
        int cursor = Math.Clamp(_mombotPromptCursorIndex, 0, _mombotPromptBuffer.Length);
        _mombotPromptBuffer = _mombotPromptBuffer.Insert(cursor, value.ToString());
        _mombotPromptCursorIndex = cursor + 1;
    }

    private bool DeleteMombotPromptCharacterBeforeCursor()
    {
        int cursor = Math.Clamp(_mombotPromptCursorIndex, 0, _mombotPromptBuffer.Length);
        if (cursor <= 0)
            return false;

        _mombotPromptBuffer = _mombotPromptBuffer.Remove(cursor - 1, 1);
        _mombotPromptCursorIndex = cursor - 1;
        return true;
    }

    private bool DeleteMombotPromptCharacterAtCursor()
    {
        int cursor = Math.Clamp(_mombotPromptCursorIndex, 0, _mombotPromptBuffer.Length);
        if (cursor >= _mombotPromptBuffer.Length)
            return false;

        _mombotPromptBuffer = _mombotPromptBuffer.Remove(cursor, 1);
        return true;
    }

    private void CancelMombotPrompt()
    {
        if (!HasMombotInteractiveState())
            return;

        CloseMombotInteractiveState();
    }

    private void SubmitMombotPrompt()
    {
        if (!_mombotPromptOpen)
            return;

        string command = _mombotPromptBuffer;
        Func<string, string>? submitTransform = _mombotPromptSubmitTransform;
        string prompt = GetMombotPromptPrefix();

        ResetMombotPromptState();

        if (submitTransform != null)
            command = submitTransform(command);

        if (string.IsNullOrWhiteSpace(command))
        {
            _parser.Feed("\r\x1b[K");
            _buffer.Dirty = true;
            FocusActiveTerminal();
            return;
        }

        _parser.Feed("\r\x1b[K");
        _parser.Feed(prompt);
        _parser.Feed(command);
        _parser.Feed("\r\n");

        ExecuteMombotLocalInput(command);
    }

    private void ResetMombotPromptState()
    {
        _mombotPromptOpen = false;
        _mombotHotkeyPromptOpen = false;
        _mombotScriptPromptOpen = false;
        _mombotPreferencesOpen = false;
        _mombotPreferencesCaptureSingleKey = false;
        _mombotPreferencesBlankSubmitBehavior = MombotPreferencesBlankSubmitBehavior.Ignore;
        _mombotPreferencesInputPrompt = string.Empty;
        _mombotPreferencesInputBuffer = string.Empty;
        _mombotPreferencesInputHandler = null;
        _mombotPreferencesHotkeySlot = 0;
        _mombotMacroPromptOpen = false;
        _mombotMacroContext = null;
        _mombotHotkeyScripts = Array.Empty<MombotHotkeyScriptEntry>();
        _mombotPromptBuffer = string.Empty;
        _mombotPromptDraft = string.Empty;
        _mombotPromptSubmitTransform = null;
        _mombotPromptHistoryIndex = _mombotCommandHistory.Count;
        _mombotPromptCursorIndex = 0;
    }

    private void RedrawMombotPrompt()
    {
        if (!_mombotPromptOpen && !_mombotHotkeyPromptOpen && !_mombotScriptPromptOpen && !_mombotPreferencesOpen)
            return;

        _parser.Feed("\r\x1b[K");
        _parser.Feed(
            _mombotPreferencesOpen ? GetMombotPreferencesPromptPrefix() :
            _mombotScriptPromptOpen ? GetMombotScriptPromptPrefix() :
            _mombotHotkeyPromptOpen ? GetMombotHotkeyPromptPrefix() :
            _mombotMacroPromptOpen ? GetMombotMacroPromptPrefix() :
            GetMombotPromptPrefix());
        if (_mombotPreferencesOpen)
        {
            if (_mombotPreferencesInputBuffer.Length > 0)
                _parser.Feed(_mombotPreferencesInputBuffer);
        }
        else if (!_mombotScriptPromptOpen && !_mombotHotkeyPromptOpen && !_mombotMacroPromptOpen && _mombotPromptBuffer.Length > 0)
        {
            _parser.Feed(_mombotPromptBuffer);
            int charsToMoveLeft = _mombotPromptBuffer.Length - Math.Clamp(_mombotPromptCursorIndex, 0, _mombotPromptBuffer.Length);
            if (charsToMoveLeft > 0)
                _parser.Feed($"\x1b[{charsToMoveLeft}D");
        }
        _buffer.Dirty = true;
        FocusActiveTerminal();
    }

    private string GetMombotPromptPrefix()
    {
        MTC.mombot.mombotStatusSnapshot snapshot = _mombot.GetStatusSnapshot();
        string mode = string.IsNullOrWhiteSpace(snapshot.Mode) ? "General" : snapshot.Mode;
        string botName = string.IsNullOrWhiteSpace(snapshot.BotName) ? "mombot" : snapshot.BotName;
        return $"\x1b[1;34m{{{mode}}}\x1b[0;37m {botName}\x1b[1;32m>\x1b[0m ";
    }

    private string GetMombotMacroPromptPrefix()
    {
        string options = "H=Holo D=Dens S=Surround X=Xenter";
        if (_mombotMacroContext is { AdjacentSectors.Count: > 0 } context)
        {
            string sectorKeys = string.Join(" ", context.AdjacentSectors
                .Take(10)
                .Select((sector, index) => $"{((index + 1) % 10)}={sector}"));
            options += " " + sectorKeys;
        }

        return $"\x1b[1;33m{{{options}}}\x1b[0;37m mombot\x1b[1;32m>\x1b[0m ";
    }

    private static string GetMombotHotkeyPromptPrefix()
    {
        return "\x1b[1;37m**Hotkey\x1b[1;32m>\x1b[0m ";
    }

    private static string GetMombotScriptPromptPrefix()
    {
        return "\x1b[1;37m***Scripts\x1b[1;32m>\x1b[0m ";
    }

    private string GetMombotPreferencesPromptPrefix()
    {
        string label = string.IsNullOrWhiteSpace(_mombotPreferencesInputPrompt)
            ? GetMombotPreferencesPageTitle(_mombotPreferencesPage)
            : _mombotPreferencesInputPrompt;
        return $"\x1b[1;37m{label}\x1b[1;32m>\x1b[0m ";
    }

    private void BeginMombotPreferencesMenu(MombotPreferencesPage page = MombotPreferencesPage.General)
    {
        if (_gameInstance == null)
        {
            PublishMombotLocalMessage("Mombot preferences are only available while the embedded proxy is running.");
            return;
        }

        if (!_mombot.Enabled)
        {
            PublishMombotLocalMessage("Enable Mombot first.");
            return;
        }

        ResetMombotPromptState();
        _mombotPreferencesOpen = true;
        _mombotPreferencesPage = page;
        _mombotPreferencesShipPageStart = 1;
        _mombotPreferencesPlanetTypePageStart = 1;
        _mombotPreferencesPlanetListCursor = 2;
        _mombotPreferencesPlanetListNextCursor = 2;
        _mombotPreferencesPlanetListHasMore = false;
        _mombotPreferencesTraderListCursor = 2;
        _mombotPreferencesTraderListNextCursor = 2;
        _mombotPreferencesTraderListHasMore = false;

        string subspace = ReadCurrentMombotVar("0", "$BOT~SUBSPACE", "$bot~subspace", "$subspace");
        string botPassword = ReadCurrentMombotVar(string.Empty, "$BOT~BOT_PASSWORD", "$bot~bot_password", "$bot_password");
        if (string.IsNullOrWhiteSpace(botPassword) && !string.Equals(subspace, "0", StringComparison.OrdinalIgnoreCase))
            PersistMombotVars(subspace, "$BOT~BOT_PASSWORD", "$bot~bot_password", "$bot_password");

        PersistMombotBoolean(true, "$BOT~BOTISDEAF", "$BOT~botIsDeaf", "$bot~botIsDeaf", "$botIsDeaf");
        RenderMombotPreferencesPage();
    }

    private void EndMombotPreferencesMenu()
    {
        if (!_mombotPreferencesOpen)
            return;

        PersistMombotBoolean(false, "$BOT~BOTISDEAF", "$BOT~botIsDeaf", "$bot~botIsDeaf", "$botIsDeaf");
        _mombotPreferencesOpen = false;
        ClearMombotPreferencesInputState();
        _parser.Feed("\r\x1b[K");
        _buffer.Dirty = true;
        if (!TryRestoreLatestObservedGamePrompt())
            FocusActiveTerminal();
        ApplyMombotExecutionRefresh();
    }

    private void ClearMombotPreferencesInputState()
    {
        _mombotPreferencesCaptureSingleKey = false;
        _mombotPreferencesBlankSubmitBehavior = MombotPreferencesBlankSubmitBehavior.Ignore;
        _mombotPreferencesInputPrompt = string.Empty;
        _mombotPreferencesInputBuffer = string.Empty;
        _mombotPreferencesInputHandler = null;
        _mombotPreferencesHotkeySlot = 0;
    }

    private bool TryHandleMombotPreferencesInput(byte[] bytes)
    {
        if (!_mombotPreferencesOpen)
            return false;

        if (_mombotPreferencesInputHandler != null)
            return TryHandleMombotPreferencesResponseInput(bytes);

        if (bytes.Length == 0)
            return true;

        if (bytes.Length == 1 && bytes[0] == 0x1B)
        {
            EndMombotPreferencesMenu();
            return true;
        }

        foreach (byte value in bytes)
        {
            if (value == 0x0D || value == 0x0A)
            {
                EndMombotPreferencesMenu();
                return true;
            }

            if (value < 0x20 || value > 0x7E)
                continue;

            HandleMombotPreferencesSelection(char.ToUpperInvariant((char)value));
            return true;
        }

        return true;
    }

    private bool TryHandleMombotPreferencesResponseInput(byte[] bytes)
    {
        if (!_mombotPreferencesOpen || _mombotPreferencesInputHandler == null)
            return false;

        if (bytes.Length == 0)
            return true;

        if (_mombotPreferencesCaptureSingleKey)
        {
            foreach (byte value in bytes)
            {
                if (value == 0x1B)
                {
                    CancelMombotPreferencesInput();
                    return true;
                }

                string? input = value switch
                {
                    0x08 => "\b",
                    0x09 => "\t",
                    0x0D => "\r",
                    0x0A => "\n",
                    0x7F => "\b",
                    >= 0x20 and <= 0x7E => ((char)value).ToString(),
                    _ => null,
                };

                if (input != null)
                {
                    CompleteMombotPreferencesInput(input);
                    return true;
                }
            }

            return true;
        }

        bool changed = false;
        foreach (byte value in bytes)
        {
            switch (value)
            {
                case 0x08:
                case 0x7F:
                    if (_mombotPreferencesInputBuffer.Length > 0)
                    {
                        _mombotPreferencesInputBuffer = _mombotPreferencesInputBuffer[..^1];
                        changed = true;
                    }
                    break;

                case 0x1B:
                    CancelMombotPreferencesInput();
                    return true;

                case 0x0D:
                case 0x0A:
                    CompleteMombotPreferencesInput(_mombotPreferencesInputBuffer);
                    return true;

                default:
                    if (value >= 0x20)
                    {
                        _mombotPreferencesInputBuffer += (char)value;
                        changed = true;
                    }
                    break;
            }
        }

        if (changed)
            RedrawMombotPrompt();

        return true;
    }

    private void BeginMombotPreferencesInput(
        string prompt,
        Action<string> handler,
        string initialValue = "",
        bool captureSingleKey = false,
        MombotPreferencesBlankSubmitBehavior blankSubmitBehavior = MombotPreferencesBlankSubmitBehavior.Ignore)
    {
        _mombotPreferencesCaptureSingleKey = captureSingleKey;
        _mombotPreferencesBlankSubmitBehavior = blankSubmitBehavior;
        _mombotPreferencesInputPrompt = prompt;
        // TWX-style preference edits should prompt for a fresh value rather than
        // preloading the current one into the editable buffer.
        _mombotPreferencesInputBuffer = string.Empty;
        _mombotPreferencesInputHandler = handler;
        RedrawMombotPrompt();
    }

    private void CompleteMombotPreferencesInput(string value)
    {
        Action<string>? handler = _mombotPreferencesInputHandler;
        MombotPreferencesBlankSubmitBehavior blankSubmitBehavior = _mombotPreferencesBlankSubmitBehavior;
        ClearMombotPreferencesInputState();

        if (blankSubmitBehavior == MombotPreferencesBlankSubmitBehavior.Submit || !string.IsNullOrWhiteSpace(value))
            handler?.Invoke(value);

        if (_mombotPreferencesOpen && _mombotPreferencesInputHandler == null)
            RenderMombotPreferencesPage();
    }

    private void CancelMombotPreferencesInput()
    {
        ClearMombotPreferencesInputState();
        if (_mombotPreferencesOpen)
            RenderMombotPreferencesPage();
    }

    private void HandleMombotPreferencesSelection(char selection)
    {
        switch (_mombotPreferencesPage)
        {
            case MombotPreferencesPage.General:
                HandleMombotGeneralPreferencesSelection(selection);
                break;

            case MombotPreferencesPage.GameStats:
                HandleMombotGameStatsPreferencesSelection(selection);
                break;

            case MombotPreferencesPage.Hotkeys:
                HandleMombotHotkeyPreferencesSelection(selection);
                break;

            case MombotPreferencesPage.ShipInfo:
                HandleMombotShipPreferencesSelection(selection);
                break;

            case MombotPreferencesPage.PlanetTypes:
                HandleMombotPlanetTypePreferencesSelection(selection);
                break;

            case MombotPreferencesPage.PlanetList:
                HandleMombotPlanetListPreferencesSelection(selection);
                break;

            case MombotPreferencesPage.TraderList:
                HandleMombotTraderListPreferencesSelection(selection);
                break;
        }
    }

    private void HandleMombotGeneralPreferencesSelection(char selection)
    {
        switch (selection)
        {
            case '?':
                RenderMombotPreferencesPage();
                return;

            case '>':
                _mombotPreferencesPage = MombotPreferencesPage.GameStats;
                RenderMombotPreferencesPage();
                return;

            case '<':
                _mombotPreferencesPage = MombotPreferencesPage.TraderList;
                RenderMombotPreferencesPage();
                return;

            case 'N':
                BeginMombotPreferencesInput(
                    "Bot Name",
                    value =>
                    {
                        string newBotName = (value ?? string.Empty).Replace("^", string.Empty).Replace(" ", string.Empty).Trim().ToLowerInvariant();
                        if (string.IsNullOrWhiteSpace(newBotName))
                            return;

                        PersistMombotVars(
                            newBotName,
                            "$BOT~BOT_NAME",
                            "$SWITCHBOARD~BOT_NAME",
                            "$SWITCHBOARD~bot_name",
                            "$bot~bot_name",
                            "$bot_name",
                            "$bot~name");

                        try
                        {
                            string path = ResolveMombotCurrentFilePath("$gconfig_file");
                            if (!string.IsNullOrWhiteSpace(path))
                            {
                                string? directory = Path.GetDirectoryName(path);
                                if (!string.IsNullOrWhiteSpace(directory))
                                    Directory.CreateDirectory(directory);
                                File.WriteAllText(path, newBotName + Environment.NewLine);
                            }
                        }
                        catch
                        {
                        }
                    },
                    ReadCurrentMombotVar("mombot", "$SWITCHBOARD~BOT_NAME", "$SWITCHBOARD~bot_name", "$bot~bot_name", "$bot_name"));
                return;

            case 'P':
                BeginMombotPreferencesInput(
                    "Game Password",
                    value => PersistMombotVars(value.Trim(), "$BOT~PASSWORD", "$bot~password", "$password"),
                    ReadCurrentMombotVar(string.Empty, "$BOT~PASSWORD", "$bot~password", "$password"),
                    blankSubmitBehavior: MombotPreferencesBlankSubmitBehavior.Submit);
                return;

            case 'Z':
                BeginMombotPreferencesInput(
                    "Bot Password",
                    value => PersistMombotVars(value.Trim(), "$BOT~BOT_PASSWORD", "$bot~bot_password", "$bot_password"),
                    ReadCurrentMombotVar(string.Empty, "$BOT~BOT_PASSWORD", "$bot~bot_password", "$bot_password"),
                    blankSubmitBehavior: MombotPreferencesBlankSubmitBehavior.Submit);
                return;

            case 'G':
                BeginMombotPreferencesInput(
                    "Game Letter",
                    value => PersistMombotVars(value.Trim().ToUpperInvariant(), "$BOT~LETTER", "$bot~letter", "$letter"),
                    ReadCurrentMombotVar(string.Empty, "$BOT~LETTER", "$bot~letter", "$letter"),
                    blankSubmitBehavior: MombotPreferencesBlankSubmitBehavior.Submit);
                return;

            case 'C':
                BeginMombotPreferencesInput(
                    "Login Name",
                    value => PersistMombotVars(value.Trim(), "$BOT~USERNAME", "$bot~username", "$username"),
                    ReadCurrentMombotVar(string.Empty, "$BOT~USERNAME", "$bot~username", "$username"),
                    blankSubmitBehavior: MombotPreferencesBlankSubmitBehavior.Submit);
                return;

            case '1':
                if (!IsMombotTruthy(ReadCurrentMombotVar("0", "$PLAYER~UNLIMITEDGAME", "$PLAYER~unlimitedGame", "$unlimitedGame")))
                {
                    BeginMombotPreferencesInput(
                        "Turn Limit",
                        value =>
                        {
                            if (int.TryParse(value.Trim(), out int turnLimit) && turnLimit >= 0 && turnLimit <= 65000)
                                PersistMombotVars(turnLimit.ToString(), "$BOT~BOT_TURN_LIMIT", "$bot~bot_turn_limit", "$bot_turn_limit");
                        },
                        ReadCurrentMombotVar("0", "$BOT~BOT_TURN_LIMIT", "$bot~bot_turn_limit", "$bot_turn_limit"));
                }
                return;

            case '3':
                PromptMombotCountPreference("Surround figs", 0, 50000, MombotCountZeroBehavior.KeepZero, "$PLAYER~surroundFigs", "$PLAYER~SURROUNDFIGS");
                return;

            case '4':
                PromptMombotCountPreference("Surround limpets", 0, 250, MombotCountZeroBehavior.KeepZero, "$PLAYER~surroundLimp", "$PLAYER~SURROUNDLIMP");
                return;

            case '5':
                PromptMombotCountPreference("Surround armids", 0, 250, MombotCountZeroBehavior.KeepZero, "$PLAYER~surroundMine", "$PLAYER~SURROUNDMINE");
                return;

            case '8':
            {
                bool shieldedOnly = IsMombotTruthy(ReadCurrentMombotVar("0", "$PLAYER~surroundAvoidShieldedOnly"));
                bool allPlanets = IsMombotTruthy(ReadCurrentMombotVar("0", "$PLAYER~surroundAvoidAllPlanets"));
                if (shieldedOnly)
                    PersistMombotSurroundPlanetAvoidance(allPlanets: true, shieldedOnly: false, none: false);
                else if (allPlanets)
                    PersistMombotSurroundPlanetAvoidance(allPlanets: false, shieldedOnly: false, none: true);
                else
                    PersistMombotSurroundPlanetAvoidance(allPlanets: false, shieldedOnly: true, none: false);
                RenderMombotPreferencesPage();
                return;
            }

            case '7':
                ToggleMombotBooleanPreference("$bot~autoattack", "$BOT~autoattack", "$autoattack");
                return;

            case '2':
            {
                bool defender = IsMombotTruthy(ReadCurrentMombotVar("0", "$PLAYER~defenderCapping"));
                bool offense = IsMombotTruthy(ReadCurrentMombotVar("0", "$PLAYER~offenseCapping", "$offenseCapping"));
                if (defender)
                {
                    PersistMombotBoolean(false, "$PLAYER~defenderCapping");
                    PersistMombotBoolean(true, "$PLAYER~offenseCapping", "$offenseCapping");
                    PersistMombotBoolean(true, "$PLAYER~cappingAliens", "$cappingAliens");
                }
                else if (offense)
                {
                    PersistMombotBoolean(false, "$PLAYER~defenderCapping");
                    PersistMombotBoolean(false, "$PLAYER~offenseCapping", "$offenseCapping");
                    PersistMombotBoolean(false, "$PLAYER~cappingAliens", "$cappingAliens");
                }
                else
                {
                    PersistMombotBoolean(true, "$PLAYER~defenderCapping");
                    PersistMombotBoolean(false, "$PLAYER~offenseCapping", "$offenseCapping");
                    PersistMombotBoolean(true, "$PLAYER~cappingAliens", "$cappingAliens");
                }

                RenderMombotPreferencesPage();
                return;
            }

            case '6':
            {
                bool offensive = IsMombotTruthy(ReadCurrentMombotVar("0", "$PLAYER~dropOffensive"));
                bool toll = IsMombotTruthy(ReadCurrentMombotVar("0", "$PLAYER~dropToll"));
                if (offensive)
                {
                    PersistMombotBoolean(false, "$PLAYER~dropOffensive");
                    PersistMombotBoolean(true, "$PLAYER~dropToll");
                }
                else if (toll)
                {
                    PersistMombotBoolean(false, "$PLAYER~dropOffensive");
                    PersistMombotBoolean(false, "$PLAYER~dropToll");
                }
                else
                {
                    PersistMombotBoolean(true, "$PLAYER~dropOffensive");
                    PersistMombotBoolean(false, "$PLAYER~dropToll");
                }

                RenderMombotPreferencesPage();
                return;
            }

            case '0':
                ToggleMombotBooleanPreference("$BOT~command_prompt_extras", "$command_prompt_extras");
                return;

            case 'V':
                ToggleMombotBooleanPreference("$BOT~silent_running", "$bot~silent_running", "$silent_running");
                return;

            case 'K':
                ToggleMombotBooleanPreference("$PLAYER~surround_before_hkill");
                return;

            case 'S':
                PromptMombotSectorPreference("Stardock", allowZeroReset: true, ResetMombotSpecialSector.Stardock, "$MAP~STARDOCK", "$MAP~stardock", "$BOT~STARDOCK", "$stardock");
                return;

            case 'J':
                BeginMombotPreferencesInput(
                    "Alarm List",
                    value => PersistMombotVars(value.Trim(), "$BOT~alarm_list", "$bot~alarm_list", "$alarm_list"),
                    ReadCurrentMombotVar(string.Empty, "$BOT~alarm_list", "$bot~alarm_list", "$alarm_list"),
                    blankSubmitBehavior: MombotPreferencesBlankSubmitBehavior.Submit);
                return;

            case 'X':
                PromptMombotCountPreference("Safe Ship", 0, int.MaxValue, MombotCountZeroBehavior.TreatAsUndefined, "$BOT~SAFE_SHIP", "$BOT~safe_ship", "$bot~safe_ship", "$safe_ship");
                return;

            case 'L':
                PromptMombotCountPreference("Safe Planet", 0, int.MaxValue, MombotCountZeroBehavior.TreatAsUndefined, "$BOT~SAFE_PLANET", "$BOT~safe_planet", "$bot~safe_planet", "$safe_planet");
                return;

            case 'E':
                BeginMombotPreferencesInput(
                    "Banner Interval (minutes)",
                    value =>
                    {
                        if (int.TryParse(value.Trim(), out int interval))
                            PersistMombotVars((interval > 0 ? interval : 5760).ToString(), "$BOT~echoInterval", "$echoInterval");
                    },
                    ReadCurrentMombotVar("5760", "$BOT~echoInterval", "$echoInterval"));
                return;

            case 'R':
                PromptMombotSectorPreference("Rylos", allowZeroReset: true, ResetMombotSpecialSector.Rylos, "$MAP~RYLOS", "$MAP~rylos", "$BOT~RYLOS", "$rylos");
                return;

            case 'A':
                PromptMombotSectorPreference("Alpha Centauri", allowZeroReset: true, ResetMombotSpecialSector.Alpha, "$MAP~ALPHA_CENTAURI", "$MAP~alpha_centauri", "$BOT~ALPHA_CENTAURI", "$alpha_centauri");
                return;

            case 'B':
                PromptMombotSectorPreference("Backdoor", allowZeroReset: false, ResetMombotSpecialSector.None, "$MAP~BACKDOOR", "$MAP~backdoor", "$backdoor");
                return;

            case 'H':
                PromptMombotSectorPreference("Home Sector", allowZeroReset: false, ResetMombotSpecialSector.None, "$MAP~HOME_SECTOR", "$MAP~home_sector", "$BOT~HOME_SECTOR", "$home_sector");
                return;

            case '9':
            {
                bool overwrite = IsMombotTruthy(ReadCurrentMombotVar("0", "$PLAYER~surroundOverwrite"));
                bool passive = IsMombotTruthy(ReadCurrentMombotVar("0", "$PLAYER~surroundPassive"));
                if (overwrite)
                {
                    PersistMombotBoolean(false, "$PLAYER~surroundOverwrite");
                    PersistMombotBoolean(true, "$PLAYER~surroundPassive");
                    PersistMombotBoolean(false, "$PLAYER~surroundNormal");
                }
                else if (passive)
                {
                    PersistMombotBoolean(false, "$PLAYER~surroundOverwrite");
                    PersistMombotBoolean(false, "$PLAYER~surroundPassive");
                    PersistMombotBoolean(true, "$PLAYER~surroundNormal");
                }
                else
                {
                    PersistMombotBoolean(true, "$PLAYER~surroundOverwrite");
                    PersistMombotBoolean(false, "$PLAYER~surroundPassive");
                    PersistMombotBoolean(false, "$PLAYER~surroundNormal");
                }

                RenderMombotPreferencesPage();
                return;
            }

            default:
                EndMombotPreferencesMenu();
                return;
        }
    }

    private void HandleMombotGameStatsPreferencesSelection(char selection)
    {
        switch (selection)
        {
            case '?':
                RenderMombotPreferencesPage();
                return;

            case '>':
                _mombotPreferencesPage = MombotPreferencesPage.Hotkeys;
                RenderMombotPreferencesPage();
                return;

            case '<':
                _mombotPreferencesPage = MombotPreferencesPage.General;
                RenderMombotPreferencesPage();
                return;

            default:
                EndMombotPreferencesMenu();
                return;
        }
    }

    private void HandleMombotHotkeyPreferencesSelection(char selection)
    {
        switch (selection)
        {
            case '?':
                RenderMombotPreferencesPage();
                return;

            case '>':
                _mombotPreferencesPage = MombotPreferencesPage.ShipInfo;
                RenderMombotPreferencesPage();
                return;

            case '<':
                _mombotPreferencesPage = MombotPreferencesPage.GameStats;
                RenderMombotPreferencesPage();
                return;
        }

        if (TryGetMombotHotkeySlotFromSelection(selection, out int slot))
        {
            PromptMombotHotkeySelection(slot);
            return;
        }

        EndMombotPreferencesMenu();
    }

    private void HandleMombotShipPreferencesSelection(char selection)
    {
        switch (selection)
        {
            case '?':
                RenderMombotPreferencesPage();
                return;

            case '<':
                _mombotPreferencesPage = MombotPreferencesPage.Hotkeys;
                RenderMombotPreferencesPage();
                return;

            case '>':
                _mombotPreferencesPage = MombotPreferencesPage.PlanetTypes;
                RenderMombotPreferencesPage();
                return;

            case '+':
            {
                int count = LoadMombotShipCatalogEntries().Count;
                _mombotPreferencesShipPageStart = count <= 10 || _mombotPreferencesShipPageStart + 10 >= count ? 1 : _mombotPreferencesShipPageStart + 10;
                RenderMombotPreferencesPage();
                return;
            }
        }

        if (TryGetMombotPagedItemOffset(selection, out int offset))
        {
            ToggleMombotShipDefender(offset);
            return;
        }

        EndMombotPreferencesMenu();
    }

    private void HandleMombotPlanetTypePreferencesSelection(char selection)
    {
        switch (selection)
        {
            case '?':
                RenderMombotPreferencesPage();
                return;

            case '<':
                _mombotPreferencesPage = MombotPreferencesPage.ShipInfo;
                RenderMombotPreferencesPage();
                return;

            case '>':
                _mombotPreferencesPage = MombotPreferencesPage.PlanetList;
                RenderMombotPreferencesPage();
                return;

            case '+':
            {
                int count = LoadMombotPlanetCatalogEntries().Count;
                _mombotPreferencesPlanetTypePageStart = count <= 10 || _mombotPreferencesPlanetTypePageStart + 10 > count ? 1 : _mombotPreferencesPlanetTypePageStart + 10;
                RenderMombotPreferencesPage();
                return;
            }

            case 'K':
                BeginMombotPreferencesInput(
                    "Keeper Planet Slot (0-9)",
                    value =>
                    {
                        if (string.IsNullOrEmpty(value))
                            return;
                        char key = char.ToUpperInvariant(value[0]);
                        if (TryGetMombotPagedItemOffset(key, out int toggleOffset))
                            ToggleMombotPlanetKeeper(toggleOffset);
                    },
                    captureSingleKey: true);
                return;
        }

        if (TryGetMombotPagedItemOffset(selection, out int offset))
        {
            PromptMombotPlanetTypeEdit(offset);
            return;
        }

        EndMombotPreferencesMenu();
    }

    private void HandleMombotPlanetListPreferencesSelection(char selection)
    {
        switch (selection)
        {
            case '?':
                RenderMombotPreferencesPage();
                return;

            case '<':
                _mombotPreferencesPage = MombotPreferencesPage.PlanetTypes;
                RenderMombotPreferencesPage();
                return;

            case '>':
                _mombotPreferencesPage = MombotPreferencesPage.TraderList;
                RenderMombotPreferencesPage();
                return;

            case '+':
                _mombotPreferencesPlanetListCursor = _mombotPreferencesPlanetListHasMore ? _mombotPreferencesPlanetListNextCursor : 2;
                RenderMombotPreferencesPage();
                return;

            default:
                EndMombotPreferencesMenu();
                return;
        }
    }

    private void HandleMombotTraderListPreferencesSelection(char selection)
    {
        switch (selection)
        {
            case '?':
                RenderMombotPreferencesPage();
                return;

            case '<':
                _mombotPreferencesPage = MombotPreferencesPage.PlanetList;
                RenderMombotPreferencesPage();
                return;

            case '>':
                _mombotPreferencesPage = MombotPreferencesPage.General;
                RenderMombotPreferencesPage();
                return;

            case '+':
                _mombotPreferencesTraderListCursor = _mombotPreferencesTraderListHasMore ? _mombotPreferencesTraderListNextCursor : 2;
                RenderMombotPreferencesPage();
                return;

            default:
                EndMombotPreferencesMenu();
                return;
        }
    }

    private void RenderMombotPreferencesPage()
    {
        if (!_mombotPreferencesOpen)
            return;

        var body = new System.Text.StringBuilder();
        body.Append("\x1b[2J\x1b[H");

        switch (_mombotPreferencesPage)
        {
            case MombotPreferencesPage.General:
                BuildMombotGeneralPreferencesPage(body);
                break;

            case MombotPreferencesPage.GameStats:
                BuildMombotGameStatsPreferencesPage(body);
                break;

            case MombotPreferencesPage.Hotkeys:
                BuildMombotHotkeyPreferencesPage(body);
                break;

            case MombotPreferencesPage.ShipInfo:
                BuildMombotShipPreferencesPage(body);
                break;

            case MombotPreferencesPage.PlanetTypes:
                BuildMombotPlanetTypePreferencesPage(body);
                break;

            case MombotPreferencesPage.PlanetList:
                BuildMombotPlanetListPreferencesPage(body);
                break;

            case MombotPreferencesPage.TraderList:
                BuildMombotTraderListPreferencesPage(body);
                break;
        }

        _parser.Feed(body.ToString());
        _buffer.Dirty = true;
        RedrawMombotPrompt();
    }

    private void BuildMombotGeneralPreferencesPage(System.Text.StringBuilder body)
    {
        string botName = ReadCurrentMombotVar("mombot", "$SWITCHBOARD~BOT_NAME", "$SWITCHBOARD~bot_name", "$bot~bot_name", "$bot_name");
        string loginPassword = ReadCurrentMombotVar(string.Empty, "$BOT~PASSWORD", "$bot~password", "$password");
        string botPassword = ReadCurrentMombotVar(string.Empty, "$BOT~BOT_PASSWORD", "$bot~bot_password", "$bot_password");
        string loginName = ReadCurrentMombotVar(string.Empty, "$BOT~USERNAME", "$bot~username", "$username");
        string turnLimit = ReadCurrentMombotVar("0", "$BOT~BOT_TURN_LIMIT", "$bot~bot_turn_limit", "$bot_turn_limit");
        string captureMode = IsMombotTruthy(ReadCurrentMombotVar("0", "$PLAYER~defenderCapping"))
            ? "Using defense"
            : IsMombotTruthy(ReadCurrentMombotVar("0", "$PLAYER~offenseCapping", "$offenseCapping"))
                ? "Using offense"
                : "Don't attack";
        string figType = IsMombotTruthy(ReadCurrentMombotVar("0", "$PLAYER~dropOffensive"))
            ? "Offensive"
            : IsMombotTruthy(ReadCurrentMombotVar("0", "$PLAYER~dropToll"))
                ? "Toll"
                : "Defensive";
        string avoidPlanets = IsMombotTruthy(ReadCurrentMombotVar("0", "$PLAYER~surroundAvoidShieldedOnly"))
            ? "Shielded"
            : IsMombotTruthy(ReadCurrentMombotVar("0", "$PLAYER~surroundAvoidAllPlanets"))
                ? "All"
                : "None";
        string surroundType = IsMombotTruthy(ReadCurrentMombotVar("0", "$PLAYER~surroundOverwrite"))
            ? "All Sectors"
            : IsMombotTruthy(ReadCurrentMombotVar("0", "$PLAYER~surroundPassive"))
                ? "Passive"
                : "Normal";
        string alarmList = ReadCurrentMombotVar(string.Empty, "$BOT~alarm_list", "$bot~alarm_list", "$alarm_list");
        string stardockDisplay = ReadCurrentMombotSectorVar(FormatMombotSector(_sessionDb?.DBHeader.StarDock), "$STARDOCK", "$MAP~STARDOCK", "$MAP~stardock", "$BOT~STARDOCK");
        string backdoorDisplay = ReadCurrentMombotSectorVar("0", "$MAP~BACKDOOR", "$MAP~backdoor");
        string rylosDisplay = ReadCurrentMombotSectorVar(FormatMombotSector(_sessionDb?.DBHeader.Rylos), "$MAP~RYLOS", "$MAP~rylos", "$BOT~RYLOS");
        string alphaDisplay = ReadCurrentMombotSectorVar(FormatMombotSector(_sessionDb?.DBHeader.AlphaCentauri), "$MAP~ALPHA_CENTAURI", "$MAP~alpha_centauri", "$BOT~ALPHA_CENTAURI");
        string homeDisplay = ReadCurrentMombotSectorVar("0", "$MAP~HOME_SECTOR", "$MAP~home_sector", "$BOT~HOME_SECTOR");

        int totalWidth = GetMombotPreferencesLayoutWidth();
        int columnGap = totalWidth >= 104 ? 6 : 4;
        int columnWidth = Math.Max(36, (totalWidth - columnGap) / 2);

        List<MombotPreferencesDisplayCell> leftColumn =
        [
            BuildMombotPreferencesSectionCell("General Info", columnWidth),
            BuildMombotPreferencesKeyValueCell("C", "Login Name:", loginName),
            BuildMombotPreferencesKeyValueCell("P", "Login Password", loginPassword),
            BuildMombotPreferencesKeyValueCell("N", "Bot Name", botName),
            BuildMombotPreferencesKeyValueCell("Z", "Bot Password", botPassword),
            BuildMombotPreferencesKeyValueCell("G", "Game Letter:", ReadCurrentMombotVar(string.Empty, "$BOT~LETTER", "$bot~letter", "$letter")),
            BuildMombotPreferencesKeyValueCell("E", "Banner Interval:", ReadCurrentMombotVar("5760", "$BOT~echoInterval", "$echoInterval") + " Minutes"),
            BuildMombotPreferencesKeyValueCell("1", "Turn Limit:", turnLimit),
            BuildMombotPreferencesKeyValueCell("0", "MSL/Busted Prompt", FormatMombotBoolDisplay(ReadCurrentMombotVar("0", "$BOT~command_prompt_extras", "$command_prompt_extras"))),
            BuildMombotPreferencesKeyValueCell("V", "Silent Mode:", FormatMombotBoolDisplay(ReadCurrentMombotVar("0", "$BOT~silent_running", "$bot~silent_running", "$silent_running"))),
            BuildMombotPreferencesSectionCell("Capture Options", columnWidth),
            BuildMombotPreferencesKeyValueCell("2", "Alien Ships:", captureMode),
            BuildMombotPreferencesSectionCell("Current Ship Stats", columnWidth),
            BuildMombotPreferencesStatCell("Offensive Odds:", ReadCurrentMombotVar("0", "$SHIP~SHIP_OFFENSIVE_ODDS")),
            BuildMombotPreferencesStatCell("Max Attack:", ReadCurrentMombotVar("0", "$SHIP~SHIP_MAX_ATTACK")),
            BuildMombotPreferencesStatCell("Max Fighters:", ReadCurrentMombotVar("0", "$SHIP~SHIP_FIGHTERS_MAX")),
        ];

        List<MombotPreferencesDisplayCell> rightColumn =
        [
            BuildMombotPreferencesSectionCell("Gridding/Attack Options", columnWidth),
            BuildMombotPreferencesKeyValueCell("3", "Figs to drop:", ReadCurrentMombotVar("0", "$PLAYER~surroundFigs", "$PLAYER~SURROUNDFIGS")),
            BuildMombotPreferencesKeyValueCell("4", "Limps to drop:", ReadCurrentMombotVar("0", "$PLAYER~surroundLimp", "$PLAYER~SURROUNDLIMP")),
            BuildMombotPreferencesKeyValueCell("5", "Armids to drop:", ReadCurrentMombotVar("0", "$PLAYER~surroundMine", "$PLAYER~SURROUNDMINE")),
            BuildMombotPreferencesKeyValueCell("6", "Fig Type:", figType),
            BuildMombotPreferencesKeyValueCell("7", "Auto Kill Mode?", FormatMombotBoolDisplay(ReadCurrentMombotVar("0", "$bot~autoattack", "$BOT~autoattack", "$autoattack"))),
            BuildMombotPreferencesKeyValueCell("8", "Avoid Planets?", avoidPlanets),
            BuildMombotPreferencesKeyValueCell("9", "Surround type?", surroundType),
            BuildMombotPreferencesKeyValueCell("K", "Surround HKILL?", FormatMombotBoolDisplay(ReadCurrentMombotVar("0", "$PLAYER~surround_before_hkill"))),
            BuildMombotPreferencesKeyValueCell("J", "Alarm List", string.IsNullOrWhiteSpace(alarmList) ? "None" : "Active"),
            BuildMombotPreferencesSectionCell("Location Variables", columnWidth),
            BuildMombotPreferencesLocationCell("S", "Stardock", "S", FormatMombotDefinedSectorDisplay(stardockDisplay)),
            BuildMombotPreferencesLocationCell("B", "Backdoor", "B", FormatMombotDefinedSectorDisplay(backdoorDisplay)),
            BuildMombotPreferencesLocationCell("R", "Rylos", "R", FormatMombotDefinedSectorDisplay(rylosDisplay)),
            BuildMombotPreferencesLocationCell("A", "Alpha", "A", FormatMombotDefinedSectorDisplay(alphaDisplay)),
            BuildMombotPreferencesLocationCell("H", "Home Sector", "H", FormatMombotDefinedSectorDisplay(homeDisplay)),
            BuildMombotPreferencesLocationCell("X", "Safe Ship", "X", FormatMombotDefinedSectorDisplay(ReadCurrentMombotVar("0", "$BOT~SAFE_SHIP", "$BOT~safe_ship", "$bot~safe_ship", "$safe_ship"))),
            BuildMombotPreferencesLocationCell("L", "Safe Planet", "L", FormatMombotDefinedSectorDisplay(ReadCurrentMombotVar("0", "$BOT~SAFE_PLANET", "$BOT~safe_planet", "$bot~safe_planet", "$safe_planet"))),
        ];

        body.Append("\r\n");
        AppendMombotPreferencesTwoColumnCells(body, leftColumn, rightColumn, columnWidth, columnGap);
        body.Append("\r\n");
        AppendMombotPreferencesStyledFooter(body, "[<] Trader List", "Game Stats [>]", totalWidth: totalWidth);
    }

    private void BuildMombotGameStatsPreferencesPage(System.Text.StringBuilder body)
    {
        string serverMaxCommands = ReadCurrentMombotVar("0", "$GAME~MAX_COMMANDS", "$MAX_COMMANDS");

        int totalWidth = GetMombotPreferencesLayoutWidth();
        int columnGap = totalWidth >= 104 ? 6 : 4;
        int columnWidth = Math.Max(36, (totalWidth - columnGap) / 2);

        List<MombotPreferencesDisplayCell> leftColumn =
        [
            BuildMombotPreferencesSectionCell("Hardware / Dock Costs", columnWidth),
            BuildMombotPreferencesStatCell("Atomic Detonators:", ReadCurrentMombotVar("0", "$GAME~ATOMIC_COST", "$ATOMIC_COST"), 24),
            BuildMombotPreferencesStatCell("Marker Beacons:", ReadCurrentMombotVar("0", "$GAME~BEACON_COST", "$BEACON_COST"), 24),
            BuildMombotPreferencesStatCell("Corbomite Devices:", ReadCurrentMombotVar("0", "$GAME~CORBO_COST", "$CORBO_COST"), 24),
            BuildMombotPreferencesStatCell("Cloaking Devices:", ReadCurrentMombotVar("0", "$GAME~CLOAK_COST", "$CLOAK_COST"), 24),
            BuildMombotPreferencesStatCell("Subspace Ether:", ReadCurrentMombotVar("0", "$GAME~PROBE_COST", "$PROBE_COST"), 24),
            BuildMombotPreferencesStatCell("Planet Scanners:", ReadCurrentMombotVar("0", "$GAME~PLANET_SCANNER_COST", "$PLANET_SCANNER_COST"), 24),
            BuildMombotPreferencesStatCell("Limpet Mines:", ReadCurrentMombotVar("0", "$GAME~LIMPET_COST", "$LIMPET_REMOVAL_COST"), 24),
            BuildMombotPreferencesStatCell("Space Mines:", ReadCurrentMombotVar("0", "$GAME~ARMID_COST", "$ARMID_COST"), 24),
            BuildMombotPreferencesStatCell("Photon Missiles:", IsMombotTruthy(ReadCurrentMombotVar("0", "$GAME~PHOTONS_ENABLED", "$PHOTONS_ENABLED")) ? ReadCurrentMombotVar("0", "$GAME~PHOTON_COST", "$PHOTON_COST") : "Disabled", 24),
            BuildMombotPreferencesSectionCell("Scanner / Ship Costs", columnWidth),
            BuildMombotPreferencesStatCell("Holographic Scan:", ReadCurrentMombotVar("0", "$GAME~HOLO_COST", "$HOLO_COST"), 24),
            BuildMombotPreferencesStatCell("Density Scan:", ReadCurrentMombotVar("0", "$GAME~DENSITY_COST", "$DENSITY_COST"), 24),
            BuildMombotPreferencesStatCell("Mine Disruptors:", ReadCurrentMombotVar("0", "$GAME~DISRUPTOR_COST", "$DISRUPTOR_COST"), 24),
            BuildMombotPreferencesStatCell("Genesis Torps:", ReadCurrentMombotVar("0", "$GAME~GENESIS_COST", "$GENESIS_COST"), 24),
            BuildMombotPreferencesStatCell("TransWarp I:", ReadCurrentMombotVar("0", "$GAME~TWARPI_COST", "$TWARPI_COST"), 24),
            BuildMombotPreferencesStatCell("TransWarp II:", ReadCurrentMombotVar("0", "$GAME~TWARPII_COST", "$TWARPII_COST"), 24),
            BuildMombotPreferencesStatCell("Psychic Probes:", ReadCurrentMombotVar("0", "$GAME~PSYCHIC_COST", "$PSYCHIC_COST"), 24),
            BuildMombotPreferencesStatCell("Limpet Removal:", ReadCurrentMombotVar("0", "$GAME~LIMPET_REMOVAL_COST", "$LIMPET_REMOVAL_COST"), 24),
        ];

        List<MombotPreferencesDisplayCell> rightColumn =
        [
            BuildMombotPreferencesSectionCell("Server / Trade Rules", columnWidth),
            BuildMombotPreferencesStatCell("Server Max Cmds:", serverMaxCommands == "0" ? "Unlimited" : serverMaxCommands, 24),
            BuildMombotPreferencesStatCell("Gold Enabled:", FormatMombotBoolDisplay(ReadCurrentMombotVar("0", "$GAME~goldEnabled", "$goldEnabled")), 24),
            BuildMombotPreferencesStatCell("MBBS Mode:", FormatMombotBoolDisplay(ReadCurrentMombotVar("0", "$GAME~mbbs", "$mbbs")), 24),
            BuildMombotPreferencesStatCell("Multiple Photons:", IsMombotTruthy(ReadCurrentMombotVar("0", "$GAME~PHOTONS_ENABLED", "$PHOTONS_ENABLED")) ? FormatMombotBoolDisplay(ReadCurrentMombotVar("0", "$GAME~MULTIPLE_PHOTONS", "$MULTIPLE_PHOTONS")) : "Disabled", 24),
            BuildMombotPreferencesStatCell("Colonists / Day:", ReadCurrentMombotVar("0", "$GAME~colonist_regen", "$colonist_regen"), 24),
            BuildMombotPreferencesStatCell("Planet Trade:", ReadCurrentMombotVar("0", "$GAME~ptradesetting", "$ptradesetting") + "%", 24),
            BuildMombotPreferencesStatCell("Steal Factor:", ReadCurrentMombotVar("0", "$GAME~STEAL_FACTOR", "$steal_factor"), 24),
            BuildMombotPreferencesStatCell("Rob Factor:", ReadCurrentMombotVar("0", "$GAME~rob_factor", "$rob_factor"), 24),
            BuildMombotPreferencesStatCell("Bust Clear Days:", ReadCurrentMombotVar("0", "$GAME~CLEAR_BUST_DAYS", "$CLEAR_BUST_DAYS"), 24),
            BuildMombotPreferencesSectionCell("Port / Universe Rules", columnWidth),
            BuildMombotPreferencesStatCell("Port Maximum:", ReadCurrentMombotVar("0", "$GAME~PORT_MAX", "$port_max"), 24),
            BuildMombotPreferencesStatCell("Port Prod Rate:", ReadCurrentMombotVar("0", "$GAME~PRODUCTION_RATE", "$PRODUCTION_RATE") + "%", 24),
            BuildMombotPreferencesStatCell("Port Regen Max:", ReadCurrentMombotVar("0", "$GAME~PRODUCTION_REGEN", "$PRODUCTION_REGEN") + "%", 24),
            BuildMombotPreferencesStatCell("Nav Haz Loss:", ReadCurrentMombotVar("0", "$GAME~DEBRIS_LOSS", "$DEBRIS_LOSS") + "%", 24),
            BuildMombotPreferencesStatCell("Radiation Life:", ReadCurrentMombotVar("0", "$GAME~RADIATION_LIFETIME", "$RADIATION_LIFETIME"), 24),
        ];

        body.Append("\r\n");
        AppendMombotPreferencesTwoColumnCells(body, leftColumn, rightColumn, columnWidth, columnGap);
        body.Append("\r\n");
        AppendMombotPreferencesStyledFooter(body, "[<] Preferences", "Hot Keys [>]", totalWidth: totalWidth);
    }

    private void BuildMombotHotkeyPreferencesPage(System.Text.StringBuilder body)
    {
        MombotHotkeyConfigData config = LoadMombotHotkeyConfigData();
        int totalWidth = GetMombotPreferencesLayoutWidth();
        int columnGap = totalWidth >= 104 ? 6 : 4;
        int columnWidth = Math.Max(36, (totalWidth - columnGap) / 2);

        List<MombotPreferencesDisplayCell> leftColumn =
        [
            BuildMombotPreferencesSectionCell("Standard Hot Keys", columnWidth),
        ];

        List<MombotPreferencesDisplayCell> rightColumn =
        [
            BuildMombotPreferencesSectionCell("Custom Hot Keys", columnWidth),
        ];

        for (int slot = 1; slot <= 17; slot++)
        {
            string title = GetMombotHotkeySlotTitle(slot, config.CustomCommands[Math.Min(slot - 1, config.CustomCommands.Length - 1)]);
            string keyValue = slot <= config.CustomKeys.Length ? config.CustomKeys[slot - 1] : "0";
            leftColumn.Add(BuildMombotPreferencesHotkeyCell(GetMombotHotkeySlotLabel(slot), title, FormatMombotHotkeyDisplay(keyValue)));
        }

        for (int slot = 18; slot <= 33; slot++)
        {
            string title = GetMombotHotkeySlotTitle(slot, config.CustomCommands[Math.Min(slot - 1, config.CustomCommands.Length - 1)]);
            string keyValue = slot <= config.CustomKeys.Length ? config.CustomKeys[slot - 1] : "0";
            rightColumn.Add(BuildMombotPreferencesHotkeyCell(GetMombotHotkeySlotLabel(slot), title, FormatMombotHotkeyDisplay(keyValue)));
        }

        body.Append("\r\n");
        AppendMombotPreferencesTwoColumnCells(body, leftColumn, rightColumn, columnWidth, columnGap);
        body.Append("\r\n");
        AppendMombotPreferencesStyledFooter(body, "[<] Game Stats", "Ship Info [>]", "Choose a slot to rebind, any other key exits", totalWidth);
    }

    private void BuildMombotShipPreferencesPage(System.Text.StringBuilder body)
    {
        AppendMombotPreferencesHeader(body, "Preferences", "Known Ship Information");

        List<MombotShipCatalogEntry> ships = LoadMombotShipCatalogEntries();
        if (ships.Count == 0)
        {
            body.Append("No ship catalog file is available.\r\n");
        }
        else
        {
            int start = Math.Clamp(_mombotPreferencesShipPageStart, 1, ships.Count);
            int count = Math.Min(10, ships.Count - start + 1);
            for (int offset = 0; offset < count; offset++)
            {
                MombotShipCatalogEntry ship = ships[start + offset - 1];
                string label = offset.ToString();
                string value = $"Def {ship.DefOdds} Off {ship.OffOdds} TPW {ship.Tpw} Bonus {(ship.Defender ? "Yes" : "No")} Shields {ship.Shields} Figs {ship.MaxFighters}";
                AppendMombotPreferencesEntry(body, label, ship.Name, value);
            }
        }

        AppendMombotPreferencesFooter(body, "[>] Planet Types", "[<] Hot Keys", "[+] More Ships, 0-9 toggles defender, any other key exits");
    }

    private void BuildMombotPlanetTypePreferencesPage(System.Text.StringBuilder body)
    {
        AppendMombotPreferencesHeader(body, "Preferences", "Planet Type Information");

        List<MombotPlanetCatalogEntry> planets = LoadMombotPlanetCatalogEntries();
        if (planets.Count == 0)
        {
            body.Append("No planet catalog file is available.\r\n");
        }
        else
        {
            int start = Math.Clamp(_mombotPreferencesPlanetTypePageStart, 1, planets.Count);
            int count = Math.Min(10, planets.Count - start + 1);
            for (int offset = 0; offset < count; offset++)
            {
                MombotPlanetCatalogEntry planet = planets[start + offset - 1];
                string label = offset.ToString();
                string value = $"Fuel {planet.FuelMin}-{planet.FuelMax} Org {planet.OrgMin}-{planet.OrgMax} Eq {planet.EquipMin}-{planet.EquipMax} Keeper {(planet.Keeper ? "Yes" : "No")}";
                AppendMombotPreferencesEntry(body, label, planet.Name, value);
            }
        }

        AppendMombotPreferencesFooter(body, "[>] Planet List", "[<] Ship Info", "[+] More Planets, [K] toggle keeper, 0-9 edits, any other key exits");
    }

    private void BuildMombotPlanetListPreferencesPage(System.Text.StringBuilder body)
    {
        AppendMombotPreferencesHeader(body, "Preferences", "Known Planet List");

        List<string> lines = CollectMombotPlanetListPage(_mombotPreferencesPlanetListCursor, out int nextCursor, out bool hasMore);
        _mombotPreferencesPlanetListNextCursor = nextCursor;
        _mombotPreferencesPlanetListHasMore = hasMore;

        if (lines.Count == 0)
            body.Append("[End of List]\r\n");
        else
            foreach (string line in lines)
                body.Append(line).Append("\r\n");

        AppendMombotPreferencesFooter(body, "[>] Trader List", "[<] Planet Types", hasMore ? "[+] More Planets, any other key exits" : "Any other key exits");
    }

    private void BuildMombotTraderListPreferencesPage(System.Text.StringBuilder body)
    {
        AppendMombotPreferencesHeader(body, "Preferences", "Trader List");

        List<string> lines = CollectMombotTraderListPage(_mombotPreferencesTraderListCursor, out int nextCursor, out bool hasMore);
        _mombotPreferencesTraderListNextCursor = nextCursor;
        _mombotPreferencesTraderListHasMore = hasMore;

        if (lines.Count == 0)
            body.Append("[End of List]\r\n");
        else
            foreach (string line in lines)
                body.Append(line).Append("\r\n");

        AppendMombotPreferencesFooter(body, "[>] Preferences", "[<] Planet List", hasMore ? "[+] More Traders, any other key exits" : "Any other key exits");
    }

    private enum MombotCountZeroBehavior
    {
        KeepZero,
        TreatAsUndefined,
    }

    private void PromptMombotCountPreference(
        string prompt,
        int minValue,
        int maxValue,
        MombotCountZeroBehavior zeroBehavior = MombotCountZeroBehavior.KeepZero,
        params string[] names)
    {
        BeginMombotPreferencesInput(
            prompt,
            value =>
            {
                if (!int.TryParse(value.Trim(), out int count))
                    return;

                if (count < minValue || count > maxValue)
                    return;

                if (count == 0 && zeroBehavior == MombotCountZeroBehavior.TreatAsUndefined)
                {
                    PersistMombotVars("0", names);
                    return;
                }

                PersistMombotVars(count.ToString(), names);
            },
            ReadCurrentMombotVar(minValue.ToString(), names));
    }

    private enum ResetMombotSpecialSector
    {
        None,
        Stardock,
        Rylos,
        Alpha,
    }

    private void PromptMombotSectorPreference(string prompt, bool allowZeroReset, ResetMombotSpecialSector resetType, params string[] names)
    {
        string currentValue = resetType switch
        {
            ResetMombotSpecialSector.Stardock => ReadCurrentMombotSectorVar(
                FormatMombotSector(_sessionDb?.DBHeader.StarDock),
                "$STARDOCK",
                "$MAP~STARDOCK",
                "$MAP~stardock",
                "$BOT~STARDOCK"),
            ResetMombotSpecialSector.Rylos => ReadCurrentMombotSectorVar(
                FormatMombotSector(_sessionDb?.DBHeader.Rylos),
                "$MAP~RYLOS",
                "$MAP~rylos",
                "$BOT~RYLOS"),
            ResetMombotSpecialSector.Alpha => ReadCurrentMombotSectorVar(
                FormatMombotSector(_sessionDb?.DBHeader.AlphaCentauri),
                "$MAP~ALPHA_CENTAURI",
                "$MAP~alpha_centauri",
                "$BOT~ALPHA_CENTAURI"),
            _ => ReadCurrentMombotSectorVar("0", names),
        };

        BeginMombotPreferencesInput(
            prompt,
            value =>
            {
                if (!int.TryParse(value.Trim(), out int sector))
                    return;

                int maxSector = _sessionDb?.DBHeader.Sectors ?? Math.Max(1, _state.Sector);
                if (sector == 0 && allowZeroReset)
                {
                    string resetValue = resetType switch
                    {
                        ResetMombotSpecialSector.Stardock => FormatMombotSector(_sessionDb?.DBHeader.StarDock),
                        ResetMombotSpecialSector.Rylos => FormatMombotSector(_sessionDb?.DBHeader.Rylos),
                        ResetMombotSpecialSector.Alpha => FormatMombotSector(_sessionDb?.DBHeader.AlphaCentauri),
                        _ => "0",
                    };
                    PersistMombotVars(resetValue, names);
                    return;
                }

                if (sector >= 1 && sector <= maxSector)
                    PersistMombotVars(sector.ToString(), names);
            },
            currentValue);
    }

    private void PersistMombotSurroundPlanetAvoidance(bool allPlanets, bool shieldedOnly, bool none)
    {
        PersistMombotBoolean(shieldedOnly, "$PLAYER~surroundAvoidShieldedOnly");
        PersistMombotBoolean(allPlanets, "$PLAYER~surroundAvoidAllPlanets");
        PersistMombotBoolean(none, "$PLAYER~surroundDontAvoid");
    }

    private void ToggleMombotBooleanPreference(params string[] names)
    {
        bool current = IsMombotTruthy(ReadCurrentMombotVar("0", names));
        PersistMombotBoolean(!current, names);
        RenderMombotPreferencesPage();
    }

    private void PromptMombotHotkeySelection(int slot)
    {
        _mombotPreferencesHotkeySlot = slot;
        MombotHotkeyConfigData config = LoadMombotHotkeyConfigData();
        string slotName = GetMombotHotkeySlotTitle(slot, config.CustomCommands[slot - 1]);
        BeginMombotPreferencesInput(
            $"New Hotkey For {slotName}",
            value => CompleteMombotHotkeySelection(slot, value),
            captureSingleKey: true);
    }

    private void CompleteMombotHotkeySelection(int slot, string value)
    {
        if (string.IsNullOrEmpty(value))
            return;

        char selectedKey = value[0];
        int lower = char.ToLowerInvariant(selectedKey);
        int upper = char.ToUpperInvariant(selectedKey);
        if ((lower >= '0' && lower <= '9') || selectedKey == '?')
        {
            PublishMombotLocalMessage("Hotkeys cannot use digits or '?'.");
            return;
        }

        MombotHotkeyConfigData config = LoadMombotHotkeyConfigData();
        string[] hotkeys = config.Hotkeys.ToArray();
        string[] customKeys = config.CustomKeys.ToArray();
        string[] customCommands = config.CustomCommands.ToArray();
        string slotValue = slot.ToString();

        if (!CanBindMombotHotkeyCode(hotkeys, lower, slotValue) || !CanBindMombotHotkeyCode(hotkeys, upper, slotValue))
        {
            PublishMombotLocalMessage("Hot key already bound to another function.");
            return;
        }

        string existingKey = customKeys[slot - 1];
        if (!string.IsNullOrEmpty(existingKey))
        {
            int existingLower = char.ToLowerInvariant(existingKey[0]);
            int existingUpper = char.ToUpperInvariant(existingKey[0]);
            ClearMombotHotkeyCode(hotkeys, existingLower, slotValue);
            ClearMombotHotkeyCode(hotkeys, existingUpper, slotValue);
        }

        SetMombotHotkeyCode(hotkeys, lower, slotValue);
        SetMombotHotkeyCode(hotkeys, upper, slotValue);
        customKeys[slot - 1] = selectedKey.ToString();

        if (slot > 17)
        {
            string currentCommand = customCommands[slot - 1] == "0" ? string.Empty : customCommands[slot - 1];
            BeginMombotPreferencesInput(
                $"Command For {GetMombotHotkeySlotLabel(slot)}",
                command =>
                {
                    customCommands[slot - 1] = string.IsNullOrWhiteSpace(command) ? "0" : command.Trim();
                    WriteMombotHotkeyConfig(new MombotHotkeyConfigData(hotkeys, customKeys, customCommands));
                },
                currentCommand,
                blankSubmitBehavior: MombotPreferencesBlankSubmitBehavior.Submit);
            return;
        }

        WriteMombotHotkeyConfig(new MombotHotkeyConfigData(hotkeys, customKeys, customCommands));
    }

    private void ToggleMombotShipDefender(int pageOffset)
    {
        List<MombotShipCatalogEntry> ships = LoadMombotShipCatalogEntries();
        int index = _mombotPreferencesShipPageStart + pageOffset - 1;
        if (index < 0 || index >= ships.Count)
            return;

        MombotShipCatalogEntry ship = ships[index];
        ships[index] = ship with { Defender = !ship.Defender };
        WriteMombotShipCatalogEntries(ships);
        RenderMombotPreferencesPage();
    }

    private void ToggleMombotPlanetKeeper(int pageOffset)
    {
        List<MombotPlanetCatalogEntry> planets = LoadMombotPlanetCatalogEntries();
        int index = _mombotPreferencesPlanetTypePageStart + pageOffset - 1;
        if (index < 0 || index >= planets.Count)
            return;

        MombotPlanetCatalogEntry planet = planets[index];
        planets[index] = planet with { Keeper = !planet.Keeper };
        WriteMombotPlanetCatalogEntries(planets);
        RenderMombotPreferencesPage();
    }

    private void PromptMombotPlanetTypeEdit(int pageOffset)
    {
        List<MombotPlanetCatalogEntry> planets = LoadMombotPlanetCatalogEntries();
        int index = _mombotPreferencesPlanetTypePageStart + pageOffset - 1;
        if (index < 0 || index >= planets.Count)
            return;

        MombotPlanetCatalogEntry original = planets[index];
        string[] values =
        {
            original.FuelMin,
            original.FuelMax,
            original.OrgMin,
            original.OrgMax,
            original.EquipMin,
            original.EquipMax,
        };

        string[] prompts =
        {
            $"Min Fuel For {original.Name}",
            $"Max Fuel For {original.Name}",
            $"Min Organics For {original.Name}",
            $"Max Organics For {original.Name}",
            $"Min Equipment For {original.Name}",
            $"Max Equipment For {original.Name}",
        };

        void PromptField(int fieldIndex)
        {
            if (fieldIndex >= values.Length)
            {
                BeginMombotPreferencesInput(
                    $"Keeper Planet {original.Name} (Y/N)",
                    keeper =>
                    {
                        bool isKeeper = keeper.Length > 0 && char.ToUpperInvariant(keeper[0]) == 'Y';
                        planets[index] = original with
                        {
                            FuelMin = values[0],
                            FuelMax = values[1],
                            OrgMin = values[2],
                            OrgMax = values[3],
                            EquipMin = values[4],
                            EquipMax = values[5],
                            Keeper = isKeeper,
                        };
                        WriteMombotPlanetCatalogEntries(planets);
                    },
                    captureSingleKey: true);
                return;
            }

            BeginMombotPreferencesInput(
                prompts[fieldIndex],
                response =>
                {
                    if (!int.TryParse(response.Trim(), out _))
                        return;
                    values[fieldIndex] = response.Trim();
                    PromptField(fieldIndex + 1);
                },
                values[fieldIndex]);
        }

        PromptField(0);
    }

    private List<string> CollectMombotPlanetListPage(int startSector, out int nextCursor, out bool hasMore)
    {
        var results = new List<string>();
        int sectors = _sessionDb?.DBHeader.Sectors ?? 0;
        if (_sessionDb == null || sectors <= 0)
        {
            nextCursor = 2;
            hasMore = false;
            return results;
        }

        int index = Math.Max(2, startSector);
        for (; index <= sectors && results.Count < 3; index++)
        {
            Core.SectorData? sector = _sessionDb.GetSector(index);
            if (sector == null || sector.PlanetNames.Count == 0 || IsMombotBubbleSector(sector))
                continue;

            results.Add($"Sector {sector.Number}: {string.Join(", ", sector.PlanetNames)}");
        }

        hasMore = false;
        nextCursor = 2;
        for (int probe = index; probe <= sectors; probe++)
        {
            Core.SectorData? sector = _sessionDb.GetSector(probe);
            if (sector != null && sector.PlanetNames.Count > 0 && !IsMombotBubbleSector(sector))
            {
                hasMore = true;
                nextCursor = probe;
                break;
            }
        }

        return results;
    }

    private List<string> CollectMombotTraderListPage(int startSector, out int nextCursor, out bool hasMore)
    {
        var results = new List<string>();
        int sectors = _sessionDb?.DBHeader.Sectors ?? 0;
        if (_sessionDb == null || sectors <= 0)
        {
            nextCursor = 2;
            hasMore = false;
            return results;
        }

        int index = Math.Max(2, startSector);
        for (; index <= sectors && results.Count < 3; index++)
        {
            Core.SectorData? sector = _sessionDb.GetSector(index);
            if (sector == null || sector.Traders.Count == 0)
                continue;

            string traders = string.Join(", ", sector.Traders.Select(trader => trader.Name));
            results.Add($"Sector {sector.Number}: {traders}");
        }

        hasMore = false;
        nextCursor = 2;
        for (int probe = index; probe <= sectors; probe++)
        {
            Core.SectorData? sector = _sessionDb.GetSector(probe);
            if (sector != null && sector.Traders.Count > 0)
            {
                hasMore = true;
                nextCursor = probe;
                break;
            }
        }

        return results;
    }

    private List<MombotShipCatalogEntry> LoadMombotShipCatalogEntries()
    {
        string filePath = ResolveMombotCurrentFilePath("$SHIP~cap_file");
        var ships = new List<MombotShipCatalogEntry>();
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            return ships;

        foreach (string line in File.ReadLines(filePath))
        {
            if (TryParseMombotCatalogLine(line, 9, out string[] fields, out string name))
            {
                ships.Add(new MombotShipCatalogEntry(
                    name,
                    fields[0],
                    fields[1],
                    fields[2],
                    fields[3],
                    fields[4],
                    fields[5],
                    fields[6],
                    fields[7],
                    IsMombotTruthy(fields[8])));
            }
        }

        return ships;
    }

    private void WriteMombotShipCatalogEntries(IReadOnlyList<MombotShipCatalogEntry> ships)
    {
        string capFile = ResolveMombotCurrentFilePath("$SHIP~cap_file");
        if (string.IsNullOrWhiteSpace(capFile))
            return;

        string? directory = Path.GetDirectoryName(capFile);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        File.WriteAllLines(
            capFile,
            ships.Select(ship => $"{ship.Shields} {ship.DefOdds} {ship.OffOdds} {ship.Cost} {ship.MaxHolds} {ship.MaxFighters} {ship.InitHolds} {ship.Tpw} {(ship.Defender ? "1" : "0")} {ship.Name}"));

        string bonusFile = Path.Combine(Path.GetDirectoryName(capFile) ?? string.Empty, "dbonus-ships.cfg");
        File.WriteAllLines(bonusFile, ships.Where(ship => ship.Defender).Select(ship => ship.Name));
    }

    private List<MombotPlanetCatalogEntry> LoadMombotPlanetCatalogEntries()
    {
        string filePath = ResolveMombotCurrentFilePath("$PLANET~planet_file");
        var planets = new List<MombotPlanetCatalogEntry>();
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            return planets;

        foreach (string line in File.ReadLines(filePath))
        {
            if (TryParseMombotCatalogLine(line, 7, out string[] fields, out string name))
            {
                planets.Add(new MombotPlanetCatalogEntry(
                    name,
                    fields[0],
                    fields[1],
                    fields[2],
                    fields[3],
                    fields[4],
                    fields[5],
                    IsMombotTruthy(fields[6])));
            }
        }

        return planets;
    }

    private void WriteMombotPlanetCatalogEntries(IReadOnlyList<MombotPlanetCatalogEntry> planets)
    {
        string planetFile = ResolveMombotCurrentFilePath("$PLANET~planet_file");
        if (string.IsNullOrWhiteSpace(planetFile))
            return;

        string? directory = Path.GetDirectoryName(planetFile);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        File.WriteAllLines(
            planetFile,
            planets.Select(planet => $"{planet.FuelMin} {planet.FuelMax} {planet.OrgMin} {planet.OrgMax} {planet.EquipMin} {planet.EquipMax} {(planet.Keeper ? "1" : "0")}  {planet.Name}"));
    }

    private MombotHotkeyConfigData LoadMombotHotkeyConfigData()
    {
        string filePath = ResolveMombotCurrentFilePath("$mombot_config_file");
        if (!string.IsNullOrWhiteSpace(filePath) &&
            TryLoadMombotHotkeyConfigFromFile(filePath, out MombotHotkeyConfigData? loaded) &&
            loaded != null)
        {
            return loaded;
        }

        string? directory = string.IsNullOrWhiteSpace(filePath) ? null : Path.GetDirectoryName(filePath);
        if (!string.IsNullOrWhiteSpace(directory) &&
            TryLoadLegacyMombotHotkeyConfig(
                Path.Combine(directory, "custom_keys.cfg"),
                Path.Combine(directory, "custom_commands.cfg"),
                out MombotHotkeyConfigData? migrated) &&
            migrated != null)
        {
            if (!string.IsNullOrWhiteSpace(filePath))
                WriteMombotHotkeyConfig(migrated);
            return migrated;
        }

        return BuildDefaultMombotHotkeyConfigData();
    }

    private void WriteMombotHotkeyConfig(MombotHotkeyConfigData config)
    {
        string filePath = ResolveMombotCurrentFilePath("$mombot_config_file");
        if (string.IsNullOrWhiteSpace(filePath))
            return;

        WriteMombotHotkeyConfigFile(filePath, config);
        string? directory = Path.GetDirectoryName(filePath);
        if (string.IsNullOrWhiteSpace(directory))
            return;

        foreach (string legacyName in new[] { "hotkeys.cfg", "custom_keys.cfg", "custom_commands.cfg" })
        {
            string legacyPath = Path.Combine(directory, legacyName);
            try
            {
                if (!string.Equals(Path.GetFullPath(legacyPath), Path.GetFullPath(filePath), StringComparison.OrdinalIgnoreCase) &&
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

    private static bool TryParseMombotCatalogLine(string line, int fixedFieldCount, out string[] fields, out string trailingName)
    {
        fields = Array.Empty<string>();
        trailingName = string.Empty;
        if (string.IsNullOrWhiteSpace(line))
            return false;

        string[] tokens = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length < fixedFieldCount + 1)
            return false;

        fields = tokens.Take(fixedFieldCount).ToArray();
        trailingName = string.Join(" ", tokens.Skip(fixedFieldCount));
        return !string.IsNullOrWhiteSpace(trailingName);
    }

    private static bool TryGetMombotHotkeySlotFromSelection(char selection, out int slot)
    {
        const string options = "1234567890ABCDEFGHIJKLMNOPRSTUVWX";
        int index = options.IndexOf(selection);
        slot = index + 1;
        return index >= 0;
    }

    private static bool TryGetMombotPagedItemOffset(char selection, out int offset)
    {
        if (selection >= '0' && selection <= '9')
        {
            offset = selection == '0' ? 0 : selection - '0';
            return true;
        }

        offset = -1;
        return false;
    }

    private static string GetMombotHotkeySlotLabel(int slot)
    {
        const string options = "1234567890ABCDEFGHIJKLMNOPRSTUVWX";
        return slot >= 1 && slot <= options.Length ? options[slot - 1].ToString() : slot.ToString();
    }

    private static string GetMombotHotkeySlotTitle(int slot, string command)
    {
        string[] builtIns =
        {
            "Auto Kill",
            "Auto Capture",
            "Auto Refurb",
            "Surround",
            "Holo-Torp",
            "Transwarp Drive",
            "Planet Macros",
            "Quick Script Loading",
            "Dny Holo Kill",
            "Stop Current Mode",
            "Dock Macros",
            "Exit Enter",
            "Mow",
            "Fast Foton",
            "Clear Sector",
            "Preferences",
            "LS Dock Shopper",
        };

        if (slot >= 1 && slot <= builtIns.Length)
            return builtIns[slot - 1];

        return string.IsNullOrWhiteSpace(command) || command == "0"
            ? $"Custom Hotkey {slot - 17}"
            : command;
    }

    private static string GetMombotPreferencesPageTitle(MombotPreferencesPage page)
    {
        return page switch
        {
            MombotPreferencesPage.General => "***Preferences",
            MombotPreferencesPage.GameStats => "***Game Stats",
            MombotPreferencesPage.Hotkeys => "***Hot Keys",
            MombotPreferencesPage.ShipInfo => "***Ship Info",
            MombotPreferencesPage.PlanetTypes => "***Planet Types",
            MombotPreferencesPage.PlanetList => "***Planet List",
            MombotPreferencesPage.TraderList => "***Trader List",
            _ => "***Preferences",
        };
    }

    private int GetMombotPreferencesLayoutWidth()
    {
        int columns = _buffer.Columns > 0 ? _buffer.Columns : 110;
        return Math.Clamp(columns - 2, 78, 110);
    }

    private readonly record struct MombotPreferencesDisplayCell(string Text, int VisibleWidth);

    private static void AppendMombotPreferencesHeader(System.Text.StringBuilder body, string title, string subtitle)
    {
        body.Append("\x1b[1;33mMombot ").Append(title).Append("\x1b[0m");
        if (!string.IsNullOrWhiteSpace(subtitle))
            body.Append(" - ").Append(subtitle);
        body.Append("\r\n\r\n");
    }

    private static MombotPreferencesDisplayCell BuildMombotPreferencesSectionCell(string title, int width)
    {
        int visibleWidth = Math.Max(width, title.Length);
        int pad = Math.Max(0, visibleWidth - title.Length);
        int leftPad = pad / 2;
        int rightPad = pad - leftPad;
        string text = "\x1b[1;36m" + new string(' ', leftPad) + title + new string(' ', rightPad) + "\x1b[0m";
        return new MombotPreferencesDisplayCell(text, visibleWidth);
    }

    private static MombotPreferencesDisplayCell BuildMombotPreferencesKeyValueCell(string key, string label, string value, int labelWidth = 18)
    {
        string safeValue = FormatMombotPreferencesValue(value);
        string paddedLabel = label.PadRight(labelWidth);
        string text = "\x1b[1;35m<\x1b[1;32m" + key + "\x1b[1;35m>\x1b[0m " +
            "\x1b[1;37m" + paddedLabel + "\x1b[0m " +
            "\x1b[1;33m" + safeValue + "\x1b[0m";
        int visibleWidth = 3 + 1 + labelWidth + 1 + safeValue.Length;
        return new MombotPreferencesDisplayCell(text, visibleWidth);
    }

    private static MombotPreferencesDisplayCell BuildMombotPreferencesLocationCell(string key, string label, string shortLabel, string value)
    {
        string safeValue = FormatMombotPreferencesValue(value);
        string paddedLabel = (label + ":").PadRight(12);
        string paddedShortLabel = ("(" + shortLabel + ")").PadRight(4);
        string text = "\x1b[1;35m<\x1b[1;32m" + key + "\x1b[1;35m>\x1b[0m " +
            "\x1b[1;37m" + paddedLabel + "\x1b[0m " +
            "\x1b[1;37m" + paddedShortLabel + "\x1b[0m " +
            "\x1b[1;33m" + safeValue + "\x1b[0m";
        int visibleWidth = 3 + 1 + 12 + 1 + 4 + 1 + safeValue.Length;
        return new MombotPreferencesDisplayCell(text, visibleWidth);
    }

    private static MombotPreferencesDisplayCell BuildMombotPreferencesStatCell(string label, string value, int labelWidth = 18)
    {
        string safeValue = FormatMombotPreferencesValue(value);
        string paddedLabel = label.PadRight(labelWidth);
        string text = "\x1b[1;37m" + paddedLabel + "\x1b[0m " +
            "\x1b[1;33m" + safeValue + "\x1b[0m";
        int visibleWidth = labelWidth + 1 + safeValue.Length;
        return new MombotPreferencesDisplayCell(text, visibleWidth);
    }

    private static MombotPreferencesDisplayCell BuildMombotPreferencesHotkeyCell(string key, string title, string binding, int titleWidth = 24)
    {
        string safeBinding = FormatMombotPreferencesValue(binding);
        string paddedTitle = title.PadRight(titleWidth);
        string text = "\x1b[1;35m<\x1b[1;32m" + key + "\x1b[1;35m>\x1b[0m " +
            "\x1b[1;37m" + paddedTitle + "\x1b[0m " +
            "\x1b[1;33m" + safeBinding + "\x1b[0m";
        int visibleWidth = 3 + 1 + titleWidth + 1 + safeBinding.Length;
        return new MombotPreferencesDisplayCell(text, visibleWidth);
    }

    private static void AppendMombotPreferencesTwoColumnCells(
        System.Text.StringBuilder body,
        IReadOnlyList<MombotPreferencesDisplayCell> leftColumn,
        IReadOnlyList<MombotPreferencesDisplayCell> rightColumn,
        int columnWidth,
        int columnGap)
    {
        int rows = Math.Max(leftColumn.Count, rightColumn.Count);
        for (int i = 0; i < rows; i++)
        {
            MombotPreferencesDisplayCell left = i < leftColumn.Count
                ? leftColumn[i]
                : new MombotPreferencesDisplayCell(string.Empty, 0);
            MombotPreferencesDisplayCell right = i < rightColumn.Count
                ? rightColumn[i]
                : new MombotPreferencesDisplayCell(string.Empty, 0);

            body.Append(left.Text);
            int spacing = Math.Max(columnGap, columnWidth - left.VisibleWidth + columnGap);
            body.Append(' ', spacing);
            body.Append(right.Text);
            body.Append("\r\n");
        }
    }

    private static void AppendMombotPreferencesStyledFooter(System.Text.StringBuilder body, string leftHint, string rightHint, string miscHint = "Any other key exits", int totalWidth = 110)
    {
        string leftText = FormatMombotPreferencesNavHint(leftHint);
        string rightText = FormatMombotPreferencesNavHint(rightHint);
        int visibleWidth = leftHint.Length + rightHint.Length;
        int spacing = Math.Max(6, totalWidth - visibleWidth);

        body.Append(leftText)
            .Append(' ', spacing)
            .Append(rightText)
            .Append("\r\n")
            .Append("\x1b[0;37m")
            .Append(miscHint)
            .Append("\x1b[0m\r\n");
    }

    private static string FormatMombotPreferencesNavHint(string text)
    {
        return text
            .Replace("[<]", "\x1b[1;35m[\x1b[1;32m<\x1b[1;35m]\x1b[0m")
            .Replace("[>]", "\x1b[1;35m[\x1b[1;32m>\x1b[1;35m]\x1b[0m");
    }

    private static string FormatMombotPreferencesValue(string value)
        => string.IsNullOrWhiteSpace(value) ? "(empty)" : value.Trim();

    private static void AppendMombotPreferencesEntry(System.Text.StringBuilder body, string key, string label, string value)
    {
        body.Append('[').Append(key).Append("] ")
            .Append(label.PadRight(24))
            .Append(value)
            .Append("\r\n");
    }

    private static void AppendMombotPreferencesFooter(System.Text.StringBuilder body, string nextHint, string prevHint, string miscHint)
    {
        body.Append("\r\n")
            .Append(nextHint)
            .Append("   ")
            .Append(prevHint)
            .Append("\r\n")
            .Append(miscHint)
            .Append("\r\n");
    }

    private static string MaskMombotSecret(string value)
        => string.IsNullOrWhiteSpace(value) ? "(empty)" : new string('*', Math.Max(4, value.Trim().Length));

    private static string FormatMombotBoolDisplay(string value)
        => IsMombotTruthy(value) ? "Yes" : "No";

    private static string FormatMombotDefinedSectorDisplay(string value)
        => int.TryParse(value, out int sector) && sector > 0 ? sector.ToString() : "Not Defined";

    private static string FormatMombotHotkeyDisplay(string value)
    {
        if (string.IsNullOrEmpty(value) || value == "0")
            return "Undefined";

        return value[0] switch
        {
            '\t' => "TAB-TAB",
            '\r' => "TAB-Enter",
            '\b' => "TAB-Backspace",
            ' ' => "TAB-Spacebar",
            _ => "TAB-" + value,
        };
    }

    private static bool IsMombotTruthy(string value)
    {
        string normalized = (value ?? string.Empty).Trim();
        return string.Equals(normalized, "1", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(normalized, "true", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(normalized, "yes", StringComparison.OrdinalIgnoreCase);
    }

    private static void PersistMombotVars(string value, params string[] names)
    {
        foreach (string name in names.Where(static name => !string.IsNullOrWhiteSpace(name)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            Core.ScriptRef.SetCurrentGameVar(name, value);
            Core.ScriptRef.OnVariableSaved?.Invoke(name, value);
        }
    }

    private static void PersistMombotBoolean(bool enabled, params string[] names)
        => PersistMombotVars(enabled ? "1" : "0", names);

    private static bool CanBindMombotHotkeyCode(IReadOnlyList<string> hotkeys, int charCode, string slotValue)
    {
        if (charCode <= 0 || charCode > hotkeys.Count)
            return false;

        string existing = hotkeys[charCode - 1];
        return string.IsNullOrWhiteSpace(existing) || existing == "0" || existing == slotValue;
    }

    private static void SetMombotHotkeyCode(IList<string> hotkeys, int charCode, string slotValue)
    {
        if (charCode >= 1 && charCode <= hotkeys.Count)
            hotkeys[charCode - 1] = slotValue;
    }

    private static void ClearMombotHotkeyCode(IList<string> hotkeys, int charCode, string slotValue)
    {
        if (charCode >= 1 && charCode <= hotkeys.Count && hotkeys[charCode - 1] == slotValue)
            hotkeys[charCode - 1] = "0";
    }

    private static bool IsMombotBubbleSector(Core.SectorData sector)
        => sector.Variables.TryGetValue("BUBBLE", out string? value) && IsMombotTruthy(value);

    private string BuildMombotMacroHelpLine()
    {
        if (_mombotMacroContext is not { } context)
            return "mombot grid: H=holo D=density S=surround X=xenter Esc=cancel";

        string line = "mombot grid: H=holo D=density S=surround X=xenter";
        if (context.AdjacentSectors.Count > 0)
        {
            string sectorKeys = string.Join(" ", context.AdjacentSectors
                .Take(10)
                .Select((sector, index) => $"{((index + 1) % 10)}={sector}"));
            line += " " + sectorKeys;
        }

        return line + " Esc=cancel";
    }

    private bool TryHandleMombotMacroKey(byte value)
    {
        if (_mombotMacroContext is not { } context)
        {
            EndMombotMacroPrompt();
            return true;
        }

        if (value >= (byte)'0' && value <= (byte)'9')
        {
            int index = value == (byte)'0' ? 9 : value - (byte)'1';
            if (index >= 0 && index < context.AdjacentSectors.Count)
            {
                int sector = context.AdjacentSectors[index];
                _ = ExecuteMombotMacroActionAsync(async macroContext =>
                {
                    if (macroContext.Surface == MombotPromptSurface.Citadel)
                        await ExecuteMombotUiCommandAsync($"pgrid {sector} scan");
                    else
                        await SendMombotServerMacroAsync(BuildMombotMoveMacro(sector));
                });
                return true;
            }
        }

        switch (char.ToUpperInvariant((char)value))
        {
            case 'H':
                _ = ExecuteMombotMacroActionAsync(macroContext =>
                    SendMombotServerMacroAsync(BuildMombotScanMacro(holo: true, macroContext)));
                return true;

            case 'D':
                _ = ExecuteMombotMacroActionAsync(macroContext =>
                    SendMombotServerMacroAsync(BuildMombotScanMacro(holo: false, macroContext)));
                return true;

            case 'S':
                _ = ExecuteMombotMacroActionAsync(_ => ExecuteMombotUiCommandAsync("surround"));
                return true;

            case 'X':
                _ = ExecuteMombotMacroActionAsync(_ => ExecuteMombotUiCommandAsync("xenter"));
                return true;
        }

        return false;
    }

    private async Task ExecuteMombotMacroActionAsync(Func<MombotGridContext, Task> action)
    {
        MombotGridContext? context = _mombotMacroContext;
        if (context == null)
        {
            _mombotMacroPromptOpen = false;
            _mombotMacroContext = null;
            RedrawMombotPrompt();
            return;
        }

        await action(context);
        if (!_mombotPromptOpen)
            return;

        if (_gameInstance == null || !_gameInstance.IsConnected)
        {
            _mombotMacroPromptOpen = false;
            _mombotMacroContext = null;
            RedrawMombotPrompt();
            return;
        }

        MombotGridContext refreshedContext = BuildMombotGridContext();
        if (refreshedContext.Surface != MombotPromptSurface.Command &&
            refreshedContext.Surface != MombotPromptSurface.Citadel)
        {
            _mombotMacroPromptOpen = false;
            _mombotMacroContext = null;
            RedrawMombotPrompt();
            return;
        }

        _mombotMacroPromptOpen = true;
        _mombotMacroContext = refreshedContext;
    }

    private void PublishMombotLocalMessage(string message)
    {
        if (_gameInstance != null)
            _gameInstance.ClientMessage("\r\n" + message + "\r\n");
        else
            _parser.Feed("\r\n" + message + "\r\n");

        if (_mombotPromptOpen || _mombotHotkeyPromptOpen || _mombotScriptPromptOpen || _mombotPreferencesOpen)
            RedrawMombotPrompt();
        else
            FocusActiveTerminal();

        _buffer.Dirty = true;
    }

    private bool TryInterceptMombotCommandPrompt(byte[] bytes)
    {
        if (_gameInstance == null ||
            !_mombot.Enabled ||
            _mombotPromptOpen ||
            _mombotHotkeyPromptOpen ||
            _mombotScriptPromptOpen ||
            _gameInstance.IsProxyMenuActive)
        {
            return false;
        }

        if (bytes.Length != 1 || bytes[0] != (byte)'>')
            return false;

        Core.ModInterpreter? interpreter = CurrentInterpreter;
        if (interpreter == null ||
            interpreter.HasKeypressInputWaiting ||
            interpreter.IsAnyScriptWaitingForInput())
        {
            return false;
        }

        MombotPromptSurface surface = GetMombotPromptSurface();
        if (_gameInstance.IsConnected && surface == MombotPromptSurface.Unknown)
            return false;

        BeginMombotPrompt();
        return true;
    }

    private bool TryInterceptMombotHotkeyAccess(byte[] bytes)
    {
        if (_gameInstance == null ||
            !_mombot.Enabled ||
            _mombotHotkeyPromptOpen ||
            _mombotScriptPromptOpen ||
            _gameInstance.IsProxyMenuActive)
        {
            return false;
        }

        if (bytes.Length != 1 || bytes[0] != 0x09)
            return false;

        Core.ModInterpreter? interpreter = CurrentInterpreter;
        if (interpreter == null ||
            interpreter.HasKeypressInputWaiting ||
            interpreter.IsAnyScriptWaitingForInput())
        {
            return false;
        }

        BeginMombotHotkeyPrompt();
        return true;
    }

    private MombotPromptSurface GetMombotPromptSurface()
    {
        string promptVar = Core.ScriptRef.GetCurrentGameVar("$PLAYER~CURRENT_PROMPT", string.Empty);
        if (string.Equals(promptVar, "Command", StringComparison.OrdinalIgnoreCase))
            return MombotPromptSurface.Command;
        if (string.Equals(promptVar, "Citadel", StringComparison.OrdinalIgnoreCase))
            return MombotPromptSurface.Citadel;
        if (string.Equals(promptVar, "Planet", StringComparison.OrdinalIgnoreCase))
            return MombotPromptSurface.Planet;
        if (string.Equals(promptVar, "Computer", StringComparison.OrdinalIgnoreCase))
            return MombotPromptSurface.Computer;

        string currentLine = Core.ScriptRef.GetCurrentLine().Trim();
        string currentAnsi = Core.ScriptRef.GetCurrentAnsiLine();
        if (currentLine.StartsWith("Command [TL=", StringComparison.OrdinalIgnoreCase))
            return MombotPromptSurface.Command;
        if (currentLine.StartsWith("Planet command (", StringComparison.OrdinalIgnoreCase))
            return MombotPromptSurface.Planet;
        if (currentLine.StartsWith("Computer command [TL=", StringComparison.OrdinalIgnoreCase))
            return MombotPromptSurface.Computer;
        if (currentLine.Contains("Citadel", StringComparison.OrdinalIgnoreCase) ||
            currentLine.Contains("<Enter Citadel>", StringComparison.OrdinalIgnoreCase) ||
            currentAnsi.Contains("Citadel", StringComparison.OrdinalIgnoreCase))
        {
            return MombotPromptSurface.Citadel;
        }

        return MombotPromptSurface.Unknown;
    }

    private MombotGridContext BuildMombotGridContext()
    {
        int currentSector = Core.ScriptRef.GetCurrentSector();
        IReadOnlyList<int> adjacentSectors = _sessionDb?.GetSector(currentSector)?.Warp
            .Where(warp => warp > 0)
            .Select(warp => (int)warp)
            .Distinct()
            .ToArray()
            ?? Array.Empty<int>();

        return new MombotGridContext(
            GetMombotPromptSurface(),
            currentSector,
            adjacentSectors,
            ParseGameVarInt(Core.ScriptRef.GetCurrentGameVar("$PLANET~PLANET", "0")),
            _gameInstance?.IsConnected == true,
            _state.Photon);
    }

    private static int ParseGameVarInt(string value)
        => int.TryParse(value, out int parsed) ? parsed : 0;

    private async Task ExecuteMombotHotkeySelectionAsync(string commandOrAction)
    {
        if (string.IsNullOrWhiteSpace(commandOrAction))
        {
            EndMombotHotkeyPrompt();
            return;
        }

        EndMombotHotkeyPrompt();

        if (commandOrAction.StartsWith(":", StringComparison.Ordinal))
        {
            await ExecuteMombotHotkeyActionAsync(commandOrAction);
            return;
        }

        await ExecuteMombotHotkeyCommandAsync(commandOrAction);
    }

    private async Task ExecuteMombotHotkeyActionAsync(string actionRef)
    {
        string normalized = actionRef.Trim().ToLowerInvariant();
        switch (normalized)
        {
            case ":user_interface~script_access":
                BeginMombotScriptPrompt();
                return;

            case ":menus~preferencesmenu":
                BeginMombotPreferencesMenu();
                return;

            case ":internal_commands~twarpswitch":
                ResetMombotPromptState();
                _parser.Feed("\r\x1b[K");
                _buffer.Dirty = true;
                BeginMombotPrompt("twarp ");
                return;

            case ":internal_commands~mowswitch":
                ResetMombotPromptState();
                _parser.Feed("\r\x1b[K");
                _buffer.Dirty = true;
                BeginMombotPrompt("mow ", NormalizeMombotMowHotkeyCommand);
                return;

            case ":internal_commands~stopmodules":
                await ExecuteMombotHotkeyCommandAsync("stopmodules");
                EnsureEmbeddedMombotClientAudible();
                ApplyMombotExecutionRefresh();
                return;

            case ":internal_commands~autocap":
            case ":internal_commands~autocapture":
                await ExecuteMombotHotkeyCommandAsync("cap");
                return;

            case ":internal_commands~autokill":
                await ExecuteMombotHotkeyCommandAsync("kill furb silent");
                return;

            case ":internal_commands~autorefurb":
                await ExecuteMombotHotkeyCommandAsync("refurb");
                return;

            case ":internal_commands~hkill":
            case ":holo_kill":
                await ExecuteMombotHotkeyCommandAsync("hkill");
                return;

            case ":internal_commands~htorp":
            case ":holotorp":
                await ExecuteMombotHotkeyCommandAsync("htorp");
                return;

            case ":internal_commands~surround":
                await ExecuteMombotHotkeyCommandAsync("surround");
                return;

            case ":internal_commands~xenter":
            case ":internal_commands~exit":
                await ExecuteMombotHotkeyCommandAsync("xenter");
                return;

            case ":internal_commands~clear":
                await ExecuteMombotHotkeyCommandAsync("clear");
                return;

            case ":internal_commands~kit":
                await ExecuteMombotHotkeyCommandAsync("macro_kit");
                return;

            case ":internal_commands~dock_shopper":
                await ExecuteMombotHotkeyCommandAsync("dock_shopper");
                return;

            case ":internal_commands~fotonswitch":
                await ExecuteMombotHotkeyCommandAsync(ResolveMombotPhotonHotkeyCommand());
                return;
        }

        await ExecuteMombotHotkeyInternalActionAsync(actionRef);
    }

    private string ResolveMombotPhotonHotkeyCommand()
    {
        string mode = Core.ScriptRef.GetCurrentGameVar("$BOT~MODE", string.Empty);
        return string.Equals(mode, "Foton", StringComparison.OrdinalIgnoreCase)
            ? "foton off"
            : "foton on p";
    }

    private async Task ExecuteMombotHotkeyCommandAsync(string command)
    {
        ResetMombotPromptState();
        _parser.Feed("\r\x1b[K");
        _buffer.Dirty = true;
        await ExecuteMombotUiCommandAsync(command);
    }

    private async Task ExecuteMombotHotkeyInternalActionAsync(string actionRef)
    {
        ResetMombotPromptState();
        _parser.Feed("\r\x1b[K");
        _buffer.Dirty = true;
        PublishMombotLocalMessage($"Mombot could not execute hotkey action {actionRef}: no native mapping is defined for this action.");
        ApplyMombotExecutionRefresh();
        await Task.CompletedTask;
    }

    private async Task ExecuteMombotHotkeyScriptAsync(int slot)
    {
        IReadOnlyList<MombotHotkeyScriptEntry> scripts = _mombotHotkeyScripts.Count > 0
            ? _mombotHotkeyScripts
            : LoadMombotHotkeyScripts();

        MombotHotkeyScriptEntry? selected = scripts.FirstOrDefault(entry => entry.Slot == slot);
        if (selected == null)
        {
            ResetMombotPromptState();
            _parser.Feed("\r\x1b[K");
            _buffer.Dirty = true;
            PublishMombotLocalMessage($"No Mombot hotkey script is configured for slot {slot % 10}.");
            return;
        }

        string scriptPath = selected.LoadReference;
        string resolvedPath = ResolveMombotFilePath(scriptPath);

        ResetMombotPromptState();
        _parser.Feed("\r\x1b[K");
        _buffer.Dirty = true;

        if (!File.Exists(resolvedPath))
        {
            PublishMombotLocalMessage(
                $"{scriptPath} does not exist in the configured Mombot script path. Check {ReadCurrentMombotVar("hotkey_scripts.cfg", "$SCRIPT_FILE")}.");
            return;
        }

        if (!_mombot.TryLoadScript(scriptPath, out string? error))
        {
            PublishMombotLocalMessage($"Mombot could not load {scriptPath}: {error}");
            return;
        }

        PublishMombotLocalMessage($"Mombot loaded script {selected.DisplayName} ({scriptPath}).");
        ApplyMombotExecutionRefresh();
        await Task.CompletedTask;
    }

    private bool TryResolveMombotHotkeyCommand(byte keyByte, out string? commandOrAction)
    {
        commandOrAction = null;

        MombotHotkeyConfigData config = LoadMombotHotkeyConfigData();
        IReadOnlyList<string> hotkeys = config.Hotkeys;
        if (keyByte == 0 || keyByte > hotkeys.Count)
            return false;

        string slotValue = hotkeys[keyByte - 1].Trim();
        if (!int.TryParse(slotValue, out int slot) || slot <= 0)
            return false;

        if (slot > config.CustomCommands.Length)
            return false;

        string entry = config.CustomCommands[slot - 1].Trim();
        if (string.IsNullOrWhiteSpace(entry) || entry == "0")
            return false;

        commandOrAction = entry;
        return true;
    }

    private IReadOnlyList<MombotHotkeyScriptEntry> LoadMombotHotkeyScripts()
    {
        string filePath = ResolveMombotCurrentFilePath("$SCRIPT_FILE");
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            return Array.Empty<MombotHotkeyScriptEntry>();

        var scripts = new List<MombotHotkeyScriptEntry>();
        try
        {
            int slot = 1;
            foreach (string rawLine in File.ReadLines(filePath))
            {
                if (slot > 10)
                    break;

                string line = rawLine.Trim();
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                int quoteIndex = line.IndexOf('"');
                if (quoteIndex <= 0)
                    continue;

                string loadReference = NormalizeMombotHotkeyScriptReference(line[..quoteIndex].Trim());
                string displayName = line[quoteIndex..].Trim().Trim('"').Trim();
                if (string.IsNullOrWhiteSpace(loadReference))
                    continue;

                scripts.Add(new MombotHotkeyScriptEntry(
                    slot,
                    loadReference,
                    string.IsNullOrWhiteSpace(displayName) ? Path.GetFileNameWithoutExtension(loadReference) : displayName));
                slot++;
            }
        }
        catch
        {
            return Array.Empty<MombotHotkeyScriptEntry>();
        }

        return scripts;
    }

    private string NormalizeMombotHotkeyScriptReference(string loadReference)
    {
        if (string.IsNullOrWhiteSpace(loadReference))
            return string.Empty;

        string normalized = loadReference.Trim().Replace('\\', '/');
        string directPath = ResolveMombotFilePath(normalized);
        if (File.Exists(directPath))
            return normalized;

        if (!normalized.StartsWith("scripts/", StringComparison.OrdinalIgnoreCase))
        {
            string prefixed = "scripts/" + normalized.TrimStart('/');
            if (File.Exists(ResolveMombotFilePath(prefixed)))
                return prefixed;
        }

        return normalized;
    }

    private string ResolveMombotCurrentFilePath(string currentVarName)
    {
        string relativePath = ReadCurrentMombotVar(string.Empty, currentVarName);
        return string.IsNullOrWhiteSpace(relativePath)
            ? string.Empty
            : ResolveMombotFilePath(relativePath);
    }

    private string ResolveMombotFilePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return string.Empty;

        string normalized = path.Replace('/', Path.DirectorySeparatorChar);
        if (Path.IsPathRooted(normalized))
            return Path.GetFullPath(normalized);

        string programDir = CurrentInterpreter?.ProgramDir ?? GetEffectiveProxyProgramDir(GetEffectiveProxyScriptDirectory());
        return Path.GetFullPath(Path.Combine(programDir, normalized));
    }

    private void PublishMombotScriptPromptList(IReadOnlyList<MombotHotkeyScriptEntry> scripts)
    {
        if (scripts.Count == 0)
            return;

        _parser.Feed("\r\x1b[K");
        foreach (MombotHotkeyScriptEntry script in scripts)
        {
            string slotLabel = script.Slot == 10 ? "0" : script.Slot.ToString();
            _parser.Feed($"\r\n\x1b[1;33m{slotLabel})\x1b[0m {script.DisplayName}");
        }

        _parser.Feed("\r\n");
        _buffer.Dirty = true;
        FocusActiveTerminal();
    }

    private string BuildMombotScanMacro(bool holo, MombotGridContext context)
    {
        string macro = context.Surface == MombotPromptSurface.Citadel ? "q q z n " : string.Empty;
        macro += holo ? "szhzn* " : "sdz* ";

        if (context.Surface == MombotPromptSurface.Citadel && context.PlanetNumber > 0)
            macro += $"l {context.PlanetNumber}*  c  ";

        return macro;
    }

    private string BuildMombotMoveMacro(int sector)
    {
        int starDock = _sessionDb?.DBHeader.StarDock ?? 0;
        string macro = $"m {sector}*";

        if (sector <= 10 || sector == starDock)
            return macro;

        int shipMaxAttack = ParseGameVarInt(Core.ScriptRef.GetCurrentGameVar("$SHIP~SHIP_MAX_ATTACK", "0"));
        int attackCount = shipMaxAttack > 0
            ? Math.Min(_state.Fighters, shipMaxAttack)
            : 0;
        if (attackCount > 0)
            macro += $"za{attackCount}* * ";

        int surroundFigs = ParseGameVarInt(Core.ScriptRef.GetCurrentGameVar("$PLAYER~SURROUNDFIGS", "0"));
        if (surroundFigs > 0)
            macro += $"f  z  {surroundFigs}* z  c  d  *  ";

        int surroundLimp = ParseGameVarInt(Core.ScriptRef.GetCurrentGameVar("$PLAYER~SURROUNDLIMP", "0"));
        if (surroundLimp > 0)
            macro += $"  H  2  Z  {surroundLimp}*  Z C  *  ";

        int surroundMine = ParseGameVarInt(Core.ScriptRef.GetCurrentGameVar("$PLAYER~SURROUNDMINE", "0"));
        if (surroundMine > 0)
            macro += $"  H  1  Z  {surroundMine}*  Z C  *  ";

        return macro;
    }

    private void RememberMombotHistory(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return;

        string trimmed = input.Trim();
        if (_mombotCommandHistory.Count > 0 &&
            string.Equals(_mombotCommandHistory[^1], trimmed, StringComparison.Ordinal))
        {
            return;
        }

        _mombotCommandHistory.Add(trimmed);
        if (_mombotCommandHistory.Count > 50)
            _mombotCommandHistory.RemoveAt(0);
    }

    private void ApplyMombotExecutionRefresh()
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(ApplyMombotExecutionRefresh, DispatcherPriority.Normal);
            return;
        }

        RefreshMombotUi();
        UpdateTemporaryMacroControls();
        RefreshStatusBar();
        RebuildProxyMenu();
        _buffer.Dirty = true;
        FocusActiveTerminal();
    }

    private void ExecuteMombotLocalInput(string input)
    {
        RecordMombotCommandHistory(input);

        (string promptAnsi, string promptPlain) = CaptureCurrentGamePromptSnapshot();
        int promptVersionBefore = _mombotObservedGamePromptVersion;

        _mombot.TryExecuteLocalInput(input, out IReadOnlyList<MTC.mombot.mombotDispatchResult> results);
        ApplyMombotExecutionRefresh();
        _ = RestoreCurrentGamePromptAfterMombotCommandAsync(results, promptAnsi, promptPlain, promptVersionBefore);
    }

    private void RecordMombotCommandHistory(string input)
    {
        string trimmed = (input ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
            return;

        RememberMombotHistory(trimmed);
        string existing = ReadCurrentMombotVar(
            string.Empty,
            "$BOT~HISTORYSTRING",
            "$HISTORYSTRING");

        string updated = trimmed + "<<|HS|>>" + existing;
        PersistMombotVars(
            updated,
            "$BOT~HISTORYSTRING",
            "$HISTORYSTRING");
    }

    private void EnsureMombotCommandHistoryLoaded()
    {
        if (_mombotCommandHistory.Count > 0)
            return;

        string history = ReadCurrentMombotVar(
            string.Empty,
            "$BOT~HISTORYSTRING",
            "$HISTORYSTRING");

        if (string.IsNullOrWhiteSpace(history))
            return;

        foreach (string entry in history.Split("<<|HS|>>", StringSplitOptions.RemoveEmptyEntries))
            RememberMombotHistory(entry);
    }

    private (string PromptAnsi, string PromptPlain) CaptureCurrentGamePromptSnapshot()
    {
        string promptAnsi = Core.ScriptRef.GetCurrentAnsiLine() ?? string.Empty;
        string promptPlainSource = Core.ScriptRef.GetCurrentLine();
        if (string.IsNullOrWhiteSpace(promptPlainSource))
            promptPlainSource = promptAnsi;

        return (promptAnsi, Core.AnsiCodes.NormalizeTerminalText(promptPlainSource).TrimEnd());
    }

    private async Task RestoreCurrentGamePromptAfterMombotCommandAsync(
        IReadOnlyList<MTC.mombot.mombotDispatchResult> results,
        string promptAnsi,
        string promptPlain,
        int promptVersionBefore)
    {
        if (string.IsNullOrWhiteSpace(promptPlain))
            return;

        string[] pendingScriptReferences = results
            .Where(result => result.Kind == MTC.mombot.mombotDispatchKind.Script &&
                             !string.IsNullOrWhiteSpace(result.ScriptReference))
            .Select(result => result.ScriptReference!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        int restoreTicket = ++_mombotPromptRestoreTicket;
        DateTime restoreDeadlineUtc = DateTime.UtcNow.AddHours(8);
        while (DateTime.UtcNow < restoreDeadlineUtc)
        {
            await Task.Delay(100).ConfigureAwait(false);

            if (restoreTicket != _mombotPromptRestoreTicket)
                return;

            if (_gameInstance == null ||
                !_gameInstance.IsConnected ||
                _gameInstance.IsProxyMenuActive ||
                !_mombot.Enabled)
            {
                return;
            }

            if (pendingScriptReferences.Any(IsMombotScriptStillRunning))
                continue;

            long lastServerOutputUtcTicks = Interlocked.Read(ref _mombotLastServerOutputUtcTicks);
            if (lastServerOutputUtcTicks > 0)
            {
                DateTime lastServerOutputUtc = new(lastServerOutputUtcTicks, DateTimeKind.Utc);
                if ((DateTime.UtcNow - lastServerOutputUtc) < TimeSpan.FromMilliseconds(300))
                    continue;
            }

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                string candidatePromptAnsi = promptAnsi;
                string candidatePromptPlain = promptPlain;

                if (restoreTicket != _mombotPromptRestoreTicket)
                    return;

                if (_mombotObservedGamePromptVersion != promptVersionBefore &&
                    !string.IsNullOrWhiteSpace(_mombotLastObservedGamePromptPlain))
                {
                    candidatePromptAnsi = _mombotLastObservedGamePromptAnsi;
                    candidatePromptPlain = _mombotLastObservedGamePromptPlain;
                }

                if (string.IsNullOrWhiteSpace(candidatePromptPlain))
                    return;

                if (HasNonBotScriptsRunning())
                    return;

                if (IsTerminalCurrentLineEquivalentTo(candidatePromptPlain))
                    return;

                if (!IsTerminalCurrentLineBlank())
                    return;

                AppendCurrentGamePrompt(candidatePromptAnsi, candidatePromptPlain);
            });
            return;
        }
    }

    private bool IsMombotScriptStillRunning(string scriptReference)
    {
        Core.ModInterpreter? interpreter = CurrentInterpreter;
        if (interpreter == null || string.IsNullOrWhiteSpace(scriptReference))
            return false;

        string normalizedReference = scriptReference.Replace('\\', '/').Trim();
        string normalizedLeaf = Path.GetFileName(normalizedReference);

        return Core.ProxyGameOperations
            .GetRunningScripts(interpreter)
            .Any(script =>
            {
                string runningReference = (script.Reference ?? string.Empty).Replace('\\', '/').Trim();
                string runningLeaf = Path.GetFileName(runningReference);
                return runningReference.EndsWith(normalizedReference, StringComparison.OrdinalIgnoreCase) ||
                       string.Equals(runningLeaf, normalizedLeaf, StringComparison.OrdinalIgnoreCase) ||
                       string.Equals(script.Name, scriptReference, StringComparison.OrdinalIgnoreCase);
            });
    }

    private bool HasNonBotScriptsRunning()
    {
        Core.ModInterpreter? interpreter = CurrentInterpreter;
        if (interpreter == null)
            return false;

        return Core.ProxyGameOperations
            .GetRunningScripts(interpreter)
            .Any(script => !script.IsBot);
    }

    private bool IsTerminalCurrentLineEquivalentTo(string promptPlain)
    {
        if (string.IsNullOrWhiteSpace(promptPlain))
            return false;

        string currentRowText = ReadTerminalRowText(_buffer.CursorRow);
        return string.Equals(
            Core.AnsiCodes.NormalizeTerminalText(currentRowText).TrimEnd(),
            Core.AnsiCodes.NormalizeTerminalText(promptPlain).TrimEnd(),
            StringComparison.OrdinalIgnoreCase);
    }

    private bool IsTerminalCurrentLineBlank()
    {
        string currentRowText = ReadTerminalRowText(_buffer.CursorRow);
        return string.IsNullOrWhiteSpace(
            Core.AnsiCodes.NormalizeTerminalText(currentRowText).Trim());
    }

    private bool HasActiveServerInputPromptOnCurrentLine()
    {
        string currentRowText = Core.AnsiCodes.NormalizeTerminalText(ReadTerminalRowText(_buffer.CursorRow)).TrimEnd();
        if (string.IsNullOrWhiteSpace(currentRowText))
            return false;

        if (TryGetMombotPromptNameFromLine(currentRowText, out _))
            return true;

        string lower = currentRowText.ToLowerInvariant();
        if (lower.Contains("selection (? for menu):", StringComparison.Ordinal) ||
            lower.Contains("enter your choice", StringComparison.Ordinal) ||
            lower.Contains("land on which planet <q to abort>", StringComparison.Ordinal) ||
            lower.Contains("choose which ship to beam to", StringComparison.Ordinal) ||
            lower.Contains("how many holds of ", StringComparison.Ordinal) ||
            lower.Contains("how many fighters do you want", StringComparison.Ordinal) ||
            lower.Contains("to which sector [", StringComparison.Ordinal) ||
            lower.Contains("do you want to ", StringComparison.Ordinal))
        {
            return true;
        }

        return lower.EndsWith("?") ||
               lower.EndsWith("]") ||
               lower.EndsWith(":");
    }

    private bool TryRestoreLatestObservedGamePrompt()
    {
        if (_gameInstance == null ||
            !_gameInstance.IsConnected ||
            _gameInstance.IsProxyMenuActive ||
            string.IsNullOrWhiteSpace(_mombotLastObservedGamePromptPlain) ||
            HasNonBotScriptsRunning())
        {
            return false;
        }

        if (IsTerminalCurrentLineEquivalentTo(_mombotLastObservedGamePromptPlain))
        {
            FocusActiveTerminal();
            return true;
        }

        if (HasActiveServerInputPromptOnCurrentLine())
            return false;

        AppendCurrentGamePrompt(_mombotLastObservedGamePromptAnsi, _mombotLastObservedGamePromptPlain);
        return true;
    }

    private string ReadTerminalRowText(int row)
    {
        if (row < 0 || row >= _buffer.Rows)
            return string.Empty;

        char[] chars = new char[_buffer.Columns];
        for (int col = 0; col < _buffer.Columns; col++)
            chars[col] = _buffer[row, col].Char;

        return new string(chars).TrimEnd();
    }

    private void AppendCurrentGamePrompt(string promptAnsi, string promptPlain)
    {
        string promptText = string.IsNullOrWhiteSpace(promptAnsi) ? promptPlain : promptAnsi;
        if (string.IsNullOrWhiteSpace(promptText))
            return;

        string currentRowText = ReadTerminalRowText(_buffer.CursorRow);
        bool needsNewLine = _buffer.CursorCol != 0 || !string.IsNullOrWhiteSpace(currentRowText);
        if (needsNewLine)
            _parser.Feed("\r\n");

        _parser.Feed(promptText);
        _buffer.Dirty = true;
        FocusActiveTerminal();
    }

    private async Task SendMombotServerMacroAsync(string macro)
    {
        if (_gameInstance == null || !_gameInstance.IsConnected)
        {
            PublishMombotLocalMessage("This Mombot action requires an active game connection.");
            return;
        }

        if (string.IsNullOrWhiteSpace(macro))
            return;

        await _gameInstance.SendToServerAsync(System.Text.Encoding.ASCII.GetBytes(TranslateMombotBurstText(macro)));
        FocusActiveTerminal();
    }

    private static string TranslateMombotBurstText(string text)
        => text.Replace("*", "\r", StringComparison.Ordinal);

    private async Task ExecuteMombotUiCommandAsync(string input)
    {
        await Task.Yield();

        if (_gameInstance == null)
        {
            PublishMombotLocalMessage("Mombot controls are only available while the embedded proxy is running.");
            return;
        }

        if (!_mombot.Enabled && !string.Equals(input, "bot", StringComparison.OrdinalIgnoreCase))
        {
            PublishMombotLocalMessage("Enable Mombot first.");
            return;
        }

        if (string.Equals(input.Trim(), "refresh", StringComparison.OrdinalIgnoreCase))
            _mombotStartupDataGatherPending = false;

        ExecuteMombotLocalInput(input);
    }

    private Task ShowMombotCommandPromptAsync(string initialValue = "")
    {
        BeginMombotPrompt(initialValue);
        return Task.CompletedTask;
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
        var reloadItem = new MenuItem { Header = "_Reload All Scripts" };
        reloadItem.Click += (_, _) => RebuildScriptsMenu();

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
                    var cmd = $"$ss {relPath}\r\n";
                    _termCtrl.SendInput?.Invoke(System.Text.Encoding.Latin1.GetBytes(cmd));
                };
                target.Add(item);
            }
        }
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
        string previousConfigPath = _currentProfilePath ?? AppPaths.TwxproxyGameConfigFileFor(previousGameName);
        string previousDatabasePath = _embeddedGameConfig?.DatabasePath ?? string.Empty;
        string previousHost = _embeddedGameConfig?.Host ?? _state.Host;
        int previousPort = _embeddedGameConfig?.Port > 0 ? _embeddedGameConfig.Port : _state.Port;
        if (string.IsNullOrWhiteSpace(previousDatabasePath))
            previousDatabasePath = AppPaths.TwxproxyDatabasePathForGame(previousGameName);

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
        string oldDefaultDatabasePath = AppPaths.TwxproxyDatabasePathForGame(previousGameName);
        if (!string.Equals(previousGameName, resolvedGameName, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(previousDatabasePath, oldDefaultDatabasePath, StringComparison.OrdinalIgnoreCase))
        {
            targetDatabasePath = AppPaths.TwxproxyDatabasePathForGame(resolvedGameName);
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
            string.IsNullOrWhiteSpace(targetDatabasePath) ? AppPaths.TwxproxyDatabasePathForGame(resolvedGameName) : targetDatabasePath,
            _embeddedGameConfig);
        await SaveEmbeddedGameConfigAsync(resolvedGameName, config);

        string newConfigPath = AppPaths.TwxproxyGameConfigFileFor(resolvedGameName);
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
        ApplyDebugLoggingPreferences();
        AddToRecentAndSave(newConfigPath);
        await SyncEmbeddedProxySettingsAsync(previousHost, previousPort);

        _parser.Feed($"\x1b[1;36m[Connection settings updated]\x1b[0m\r\n");
        _buffer.Dirty = true;
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
        _gameInstance.SetNativeHaggleEnabled(gameConfig.NativeHaggleEnabled);
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
        string path = AppPaths.TwxproxyGameConfigFileFor(gameName);
        EmbeddedGameConfig config = BuildEmbeddedGameConfigFromProfile(
            newProfile,
            AppPaths.TwxproxyDatabasePathForGame(gameName));
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
        string path = AppPaths.TwxproxyGameConfigFileFor(gameName);
        string targetDatabasePath = AppPaths.TwxproxyDatabasePathForGame(gameName);
        string currentDatabasePath = _embeddedGameConfig?.DatabasePath ?? AppPaths.TwxproxyDatabasePathForGame(DeriveGameName());
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
