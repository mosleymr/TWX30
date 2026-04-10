using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using Avalonia;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using SkiaSharp;
using Avalonia.Controls;
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
    private const string BaseWindowTitle = "Mayhem Tradewars Client v1.0";
    private const double DeckPanelSnapThreshold = 18;
    private const double DeckPanelSnapGap = 18;
    private const double DeckPanelGridSize = 18;

    // ── Core components ────────────────────────────────────────────────────
    private readonly GameState       _state;
    private readonly TerminalBuffer  _buffer;
    private readonly AnsiParser      _parser;
    private readonly TelnetClient    _telnet;
    private TerminalControl _termCtrl = null!;
    private TerminalControl _deckTermCtrl = null!;
    private readonly DispatcherTimer _refreshTimer;    private readonly Core.ShipInfoParser _shipParser = new();
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
    private Window?                        _mombotIntroWindow;
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
    private MenuItem        _aiMenu        = new() { Header = "_AI", IsVisible = false };
    private MenuItem        _fileEdit       = new() { Header = "_Edit Connection…", IsEnabled = false };
    private MenuItem        _fileConnect    = new() { Header = "_Connect",    IsEnabled = false };
    private MenuItem        _fileDisconnect = new() { Header = "_Disconnect", IsEnabled = false };
    private Menu            _menuBar       = new();
    private readonly MenuItem _viewClassicSkin = new() { Header = "_Classic Console" };
    private readonly MenuItem _viewCommandDeckSkin = new() { Header = "_Command Deck" };
    private readonly NativeMenu _nativeAppMenu = new();
    private readonly NativeMenu _nativeDockMenu = new();
    private readonly MTC.mombot.mombotService _mombot = new();
    private readonly Border _shellHost = new();
    private readonly Border _statusBar = new();
    private DockPanel? _rootDock;
    private Canvas? _deckSurface;
    private readonly Dictionary<string, FloatingDeckPanel> _deckPanels = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, DeckPanelState> _deckPanelStates = new(StringComparer.OrdinalIgnoreCase);
    private int _deckNextZIndex = 100;
    private bool _deckPanelsInitialized;
    private bool _suppressDeckPanelStateSync;
    private TacticalMapControl? _tacticalMap;
    private bool _useCommandDeckSkin;
    private bool _nativeAppMenuReady;
    private bool _nativeAppMenuAttached;
    private bool _nativeDockMenuAttached;
    private ToggleSwitch _haggleToggle = null!;
    private ToggleSwitch _deckHaggleToggle = null!;
    private Action<byte[]>? _terminalInputHandler;
    private string? _terminalFontFamilyName;
    private bool _updatingHaggleToggle;
    private bool _mombotPromptOpen;
    private bool _mombotHotkeyPromptOpen;
    private bool _mombotScriptPromptOpen;
    private bool _mombotPreferencesOpen;
    private bool _mombotPreferencesCaptureSingleKey;
    private string _mombotPreferencesInputPrompt = string.Empty;
    private string _mombotPreferencesInputBuffer = string.Empty;
    private Action<string>? _mombotPreferencesInputHandler;
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
    private int _mombotPromptHistoryIndex;
    private MombotPreferencesPage _mombotPreferencesPage;
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
    private Border    _deckScanIndD     = new();
    private Border    _deckScanIndH     = new();
    private Border    _deckScanIndP     = new();

    // ── Status bar text ───────────────────────────────────────────────────
    private TextBlock _statusText = new();
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
    private static readonly IBrush HudText      = new SolidColorBrush(Color.FromRgb(222, 238, 242));
    private static readonly IBrush HudMuted     = new SolidColorBrush(Color.FromRgb(126, 170, 180));
    private static readonly IBrush HudEdge      = new SolidColorBrush(Color.FromRgb(57, 112, 128));
    private static readonly IBrush HudInnerEdge = new SolidColorBrush(Color.FromRgb(23,  81, 94));
    private static readonly IBrush HudAccent    = new SolidColorBrush(Color.FromRgb(0,   212, 201));
    private static readonly IBrush HudAccentInk = new SolidColorBrush(Color.FromRgb(8,  26, 30));
    private static readonly IBrush HudAccentHot = new SolidColorBrush(Color.FromRgb(255, 193, 74));
    private static readonly IBrush HudAccentOk  = new SolidColorBrush(Color.FromRgb(118, 255, 141));
    private static readonly IBrush HudAccentWarn= new SolidColorBrush(Color.FromRgb(255, 112, 112));
    private static readonly IBrush HudStatus    = new SolidColorBrush(Color.FromRgb(11,  20, 28));
    private static readonly FontFamily HudTitleFont = new("Eurostile, Bank Gothic, Bahnschrift, Segoe UI, sans-serif");
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
        _telnet.TextLineReceived += line =>
            Core.GlobalModules.GlobalAutoRecorder.RecordLine(line);

        // Session logging for direct telnet mode is handled through the shared Core logger.
        RefreshSessionLogTarget();
        _telnet.AppDataDecoded   += text  => _sessionLog.RecordServerText(text);

        // Update current sector from the command prompt — fires on every "Command [TL=...]:[N]"
        Core.GlobalModules.GlobalAutoRecorder.CurrentSectorChanged += sn =>
            Dispatcher.UIThread.Post(() =>
            {
                Core.ScriptRef.SetCurrentSector(sn);
                SetMombotCurrentVars(sn.ToString(), "$PLAYER~CURRENT_SECTOR", "$player~current_sector");
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

        Content = BuildLayout();

        ApplyDebugLoggingPreferences();
        RebuildRecentMenu();
        RebuildProxyMenu();
        RebuildScriptsMenu();
        RebuildAiMenu();
        _parser.Feed("\x1b[2J\x1b[H");
        _parser.Feed("\x1b[1;33mMayhem Tradewars Client v1.0\x1b[0m\r\n");
        _parser.Feed("\x1b[37mUse \x1b[1;32mFile \u25b6 New Connection\x1b[0;37m or \x1b[1;32mOpen\x1b[0;37m to select a game, then \x1b[1;32mFile \u25b6 Connect\x1b[0;37m to connect.\x1b[0m\r\n");
        _buffer.Dirty = true;

        // 50 ms refresh – pushes buffer changes to UI
        _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
        _refreshTimer.Tick += (_, _) =>
        {
            _termCtrl.RequestRedraw();
            _deckTermCtrl.RequestRedraw();
        };
        _refreshTimer.Start();

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
            _refreshTimer.Stop();
            _telnet.Disconnect();
            _proxyCts?.Cancel();
            if (_moduleHost != null)
                _ = _moduleHost.DisposeAsync().AsTask();
            foreach (AiAssistantWindow window in _assistantWindows.Values.ToList())
                window.Close();
            _assistantWindows.Clear();
            if (_gameInstance != null) _ = _gameInstance.StopAsync();
            _sessionLog.Dispose();
        };
    }

    private TerminalControl CreateTerminalControl()
    {
        var control = new TerminalControl(_buffer);
        if (_terminalInputHandler != null)
            control.SendInput = _terminalInputHandler;
        control.IsConnected = _state.Connected;
        if (!string.IsNullOrWhiteSpace(_terminalFontFamilyName))
            control.SetFont(_terminalFontFamilyName);
        return control;
    }

    private static ToggleSwitch CreateHaggleToggle()
    {
        return new ToggleSwitch
        {
            OffContent = "Off",
            OnContent = "On",
            IsEnabled = false,
            IsChecked = false,
            Margin = new Thickness(8, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
    }

    private void ApplyCurrentHaggleState(ToggleSwitch toggle)
    {
        bool proxyActive = _gameInstance != null;
        _updatingHaggleToggle = true;
        toggle.IsEnabled = proxyActive;
        toggle.IsChecked = proxyActive && _gameInstance?.NativeHaggleEnabled == true;
        _updatingHaggleToggle = false;
    }

    private void RecreateClassicShellControls()
    {
        _termCtrl = CreateTerminalControl();
        _haggleToggle = CreateHaggleToggle();
        ApplyCurrentHaggleState(_haggleToggle);
        _haggleToggle.IsCheckedChanged += (_, _) => OnHaggleToggleRequested();

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
        _deckHaggleToggle = CreateHaggleToggle();
        ApplyCurrentHaggleState(_deckHaggleToggle);
        _deckHaggleToggle.IsCheckedChanged += (_, _) => OnHaggleToggleRequested();

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
        var dock = new DockPanel { Background = BgWindow };
        _rootDock = dock;

        // ── Menu ──────────────────────────────────────────────────────────
        _menuBar = BuildMenuBar();
        DockPanel.SetDock(_menuBar, Dock.Top);
        dock.Children.Add(_menuBar);

        // ── Status bar ────────────────────────────────────────────────────
        _statusText.Text              = " SD: -  Rylos: -  Alpha: -  [ disconnected ]";
        _statusText.Foreground         = FgStatus;
        _statusText.VerticalAlignment  = VerticalAlignment.Center;
        _statusText.Margin             = new Thickness(8, 0);
        _statusText.FontSize           = 13;

        _statusBar.Background = BgStatus;
        _statusBar.Height = 26;
        _statusBar.Child = _statusText;
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

        // Margin lets the gray BgChrome peek in on all four sides as a frame.
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(200) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(6) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var sidebar = BuildSidebar();
        Grid.SetColumn(sidebar, 0);
        grid.Children.Add(sidebar);

        var termArea = BuildTerminalArea();
        Grid.SetColumn(termArea, 2);
        grid.Children.Add(termArea);

        return grid;
    }

    private Control BuildCommandDeckShell()
    {
        RecreateDeckShellControls();
        _deckPanels.Clear();
        _deckPanelsInitialized = false;
        _suppressDeckPanelStateSync = false;
        _tacticalMap = new TacticalMapControl(
            () => _state.Sector,
            () => _sessionDb)
        {
            MinHeight = 220,
        };

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
        bubbleButton = BuildDeckToggleToolButton("Bubble", () =>
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

        grid.Children.Add(BuildDeckInfoChip("Mode", new TextBlock { Text = "Gameplay feed" }, HudAccent));

        var terminalBorder = new Border
        {
            Margin = new Thickness(0, 12, 0, 0),
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
                        BuildDeckHaggleRow(),
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

        var commander = BuildDeckMetricCard(
            "Commander Link",
            ("Pilot", _deckValName),
            ("Sector", _deckValSector),
            ("Turns", _deckValTurns));
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
        var stack = new StackPanel { Spacing = 8 };
        foreach ((string label, TextBlock value) in rows)
            stack.Children.Add(BuildDeckMetricRow(label, value));

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

    private void ApplySelectedSkin()
    {
        Background = _useCommandDeckSkin ? HudWindow : BgWindow;
        if (_rootDock != null)
            _rootDock.Background = _useCommandDeckSkin ? HudWindow : BgWindow;

        _menuBar.Background = _useCommandDeckSkin ? HudMenu : BgSidebar;
        _menuBar.Foreground = _useCommandDeckSkin ? HudText : FgKey;
        _statusBar.Background = _useCommandDeckSkin ? HudStatus : BgStatus;
        _statusText.Foreground = _useCommandDeckSkin ? HudText : FgStatus;
        _shellHost.Padding = _useCommandDeckSkin
            ? new Thickness(10, 8, 10, 10)
            : new Thickness(6, 4, 6, 4);
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
    }

    private void SetTerminalConnected(bool connected)
    {
        _termCtrl.IsConnected = connected;
        _deckTermCtrl.IsConnected = connected;
    }

    private void SetTerminalFont(string familyName)
    {
        _terminalFontFamilyName = familyName;
        _termCtrl.SetFont(familyName);
        _deckTermCtrl.SetFont(familyName);
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

        var fileMenu = new MenuItem
        {
            Header = "_File",
            Items  = { fileNew, fileEdit, fileOpen, _recentMenu, fileSave, fileSaveAs,
                       new Separator(), fileResetGame,
                       new Separator(), fileNewWin,
                       new Separator(), fileConnect, fileDisconnect,
                       new Separator(), filePrefs,
                       new Separator(), fileQuit },
        };

        var viewClear    = new MenuItem { Header = "_Clear Screen" };
        viewClear.Click += (_, _) => { _buffer.Reset(); _buffer.Dirty = true; };

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

        var viewDbItem = new MenuItem { Header = "_Database..." };
        viewDbItem.Click += (_, _) => OnViewDatabase();

        var viewGameInfoItem = new MenuItem { Header = "_Game Info..." };
        viewGameInfoItem.Click += (_, _) => OnViewGameInfo();

        _viewClassicSkin.Click += (_, _) => SetSkin(useCommandDeckSkin: false);
        _viewCommandDeckSkin.Click += (_, _) => SetSkin(useCommandDeckSkin: true);
        var skinMenu = new MenuItem
        {
            Header = "_Skin",
            Items = { _viewClassicSkin, _viewCommandDeckSkin },
        };

        var viewMenu = new MenuItem
        {
            Header = "_View",
            Items  = { viewClear, viewFont, new Separator(), skinMenu, new Separator(), viewGameInfoItem, viewDbItem },
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

        var menu = new Menu
        {
            Background = BgSidebar,
            Foreground = FgKey,
            Items      = { fileMenu, _scriptsMenu, _proxyMenu, _botMenu, _quickMenu, _aiMenu, mapMenu, viewMenu, helpMenu },
        };

        return menu;
    }

    private void OnViewMap()
    {
        var win = new MapWindow(
            () => _state.Sector,
            () => _sessionDb);
        win.Show();
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
        var win = new GameInfoWindow(() => _sessionDb);
        win.Show();
    }

    // ── Sidebar ────────────────────────────────────────────────────────────

    private Control BuildSidebar()
    {
        var stack = new StackPanel
        {
            Background  = BgSidebar,
            Orientation = Orientation.Vertical,
            Margin      = new Thickness(0, 0, 0, 0),
        };

        // Trader Info
        var traderRows = new (string, TextBlock)[]
        {
            ("Name",    _valName),
            ("Sector",  _valSector),
            ("Turns",   _valTurns),
            ("Exper.",  _valExper),
            ("Alignm.", _valAlignm),
            ("Cred.",   _valCred),
        };
        stack.Children.Add(BuildPanel("Trader Info", traderRows));

        // Holds – total shown in section header, sub-items below
        var holdsRows = new (string, TextBlock)[]
        {
            ("Fuel Ore", _valFuelOre),
            ("Organics", _valOrganics),
            ("Equipmnt", _valEquipment),
            ("Colonsts", _valColonists),
            ("Empty",    _valEmpty),
        };
        stack.Children.Add(BuildPanel("Holds", holdsRows, _valHTotal));

        stack.Children.Add(BuildShipInfoPanel());

        var scroll = new ScrollViewer
        {
            VerticalScrollBarVisibility   = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
            Content = stack,
        };

        // Wrap in a border that gives a raised look against the gray chrome
        var outer = new Border
        {
            Background      = BgSidebar,
            BorderBrush     = BorderColor,
            BorderThickness = new Thickness(1),
            Child           = scroll,
        };

        return outer;
    }

    // ── Expanded Ship Info panel ───────────────────────────────────────────

    private Control BuildShipInfoPanel()
    {
        var panel = new StackPanel { Background = BgPanel, Orientation = Orientation.Vertical, Margin = new Thickness(0, 0, 0, 3) };

        // Title header
        panel.Children.Add(new Border
        {
            Background = BgStatus,
            Child = new TextBlock { Text = "Ship Info", Foreground = FgTitle, FontSize = 14, FontWeight = Avalonia.Media.FontWeight.SemiBold, Margin = new Thickness(6, 4, 4, 4) },
        });
        panel.Children.Add(new Border { Background = BorderColor, Height = 1 });

        // Full-width rows: Fighters, Shields, Turns/Warp
        foreach (var (key, tb) in new (string, TextBlock)[] {
            ("Fighters",   _valFighters),
            ("Shields",    _valShields),
            ("Turns/Warp", _valTrnWarp),
        })
        {
            tb.Text = "-"; tb.Foreground = FgValue; tb.FontSize = 13;
            tb.TextAlignment = TextAlignment.Right; tb.MinWidth = 70;
            var keyTb = new TextBlock { Text = key, Foreground = FgKey, FontSize = 13, VerticalAlignment = VerticalAlignment.Center };
            var row = new Grid { Margin = new Thickness(6, 2, 6, 2) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Grid.SetColumn(keyTb, 0); Grid.SetColumn(tb, 1);
            row.Children.Add(keyTb); row.Children.Add(tb);
            panel.Children.Add(row);
        }

        // Divider before compact equipment rows
        panel.Children.Add(new Border { Background = BorderColor, Height = 1, Margin = new Thickness(0, 2, 0, 1) });

        // Paired rows: two equipment items per line
        foreach (var (k1, v1, k2, v2) in new (string, TextBlock, string, TextBlock)[] {
            ("Ethr", _valEther,     "Bea",  _valBeacon),
            ("Disr", _valDisruptor, "Pho",  _valPhoton),
            ("Arm",  _valArmid,     "Lim",  _valLimpet),
            ("Gen",  _valGenesis,   "Ato",  _valAtomic),
            ("Corb", _valCorbo,     "Clo",  _valCloak),
            ("TW1",  _valTW1,       "TW2",  _valTW2),
        })
            panel.Children.Add(BuildPairedRow(k1, v1, k2, v2));

        // Divider before scanners
        panel.Children.Add(new Border { Background = BorderColor, Height = 1, Margin = new Thickness(0, 2, 0, 1) });

        // Scanner indicators
        panel.Children.Add(BuildScannerRow());
        panel.Children.Add(BuildHaggleRow());
        panel.Children.Add(new Border { Height = 6 });
        return panel;
    }

    private Control BuildPairedRow(string k1, TextBlock v1, string k2, TextBlock v2)
    {
        static Grid MakeHalf(string key, TextBlock valTb)
        {
            valTb.Text = "0"; valTb.Foreground = FgValue; valTb.FontSize = 12;
            valTb.TextAlignment = TextAlignment.Right; valTb.MinWidth = 30;
            var keyTb = new TextBlock { Text = key, Foreground = FgKey, FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
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
        static Border MakeScanInd(string letter) => new Border
        {
            Width = 20, Height = 18, CornerRadius = new CornerRadius(2),
            Background = ScannerInactive, Margin = new Thickness(2, 0),
            Child = new TextBlock
            {
                Text = letter, FontSize = 11, FontWeight = Avalonia.Media.FontWeight.Bold,
                Foreground = ScannerFgInact,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(2, 1),
            },
        };
        _scanIndD = MakeScanInd("D");
        _scanIndH = MakeScanInd("H");
        _scanIndP = MakeScanInd("P");
        var indicators = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        indicators.Children.Add(_scanIndD);
        indicators.Children.Add(_scanIndH);
        indicators.Children.Add(_scanIndP);
        var row = new Grid { Margin = new Thickness(6, 2, 6, 3) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var label = new TextBlock { Text = "Scanners", Foreground = FgKey, FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(label, 0); Grid.SetColumn(indicators, 1);
        row.Children.Add(label); row.Children.Add(indicators);
        return row;
    }

    private Control BuildHaggleRow()
    {
        var row = new Grid { Margin = new Thickness(6, 2, 6, 3) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var label = new TextBlock
        {
            Text = "Haggle",
            Foreground = FgKey,
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(label, 0);
        Grid.SetColumn(_haggleToggle, 1);
        row.Children.Add(label);
        row.Children.Add(_haggleToggle);
        return row;
    }

    private Control BuildDeckScannerRow()
    {
        static Border MakeScanInd(string letter) => new Border
        {
            Width = 24,
            Height = 20,
            CornerRadius = new CornerRadius(6),
            Background = HudHeaderAlt,
            BorderBrush = HudInnerEdge,
            BorderThickness = new Thickness(1),
            Margin = new Thickness(2, 0),
            Child = new TextBlock
            {
                Text = letter,
                FontSize = 11,
                FontWeight = FontWeight.Bold,
                Foreground = HudMuted,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
        };

        _deckScanIndD = MakeScanInd("D");
        _deckScanIndH = MakeScanInd("H");
        _deckScanIndP = MakeScanInd("P");

        var indicators = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
            Children = { _deckScanIndD, _deckScanIndH, _deckScanIndP },
        };

        var row = new Grid { Margin = new Thickness(0, 2, 0, 3) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var label = new TextBlock
        {
            Text = "Scanners",
            Foreground = HudMuted,
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(label, 0);
        Grid.SetColumn(indicators, 1);
        row.Children.Add(label);
        row.Children.Add(indicators);
        return row;
    }

    private Control BuildDeckHaggleRow()
    {
        var row = new Grid { Margin = new Thickness(0, 2, 0, 3) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var label = new TextBlock
        {
            Text = "Haggle",
            Foreground = HudMuted,
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(label, 0);
        Grid.SetColumn(_deckHaggleToggle, 1);
        row.Children.Add(label);
        row.Children.Add(_deckHaggleToggle);
        return row;
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

    /// <summary>Builds a titled info panel containing key/value rows.</summary>
    private Control BuildPanel(string title, (string Key, TextBlock Value)[] rows, TextBlock? headerValue = null)
    {
        var panel = new StackPanel
        {
            Background  = BgPanel,
            Orientation = Orientation.Vertical,
            Margin      = new Thickness(0, 0, 0, 3),
        };

        // Title row – dark background strip to contrast against gray panel body
        if (headerValue != null)
        {
            headerValue.Foreground        = FgValue;
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
                Foreground = FgTitle,
                FontSize   = 14,
                FontWeight = Avalonia.Media.FontWeight.SemiBold,
                Margin     = new Thickness(6, 4, 4, 4),
            };
            Grid.SetColumn(hdrTitle,  0);
            Grid.SetColumn(headerValue, 1);
            hdrGrid.Children.Add(hdrTitle);
            hdrGrid.Children.Add(headerValue);
            panel.Children.Add(new Border { Background = BgStatus, Child = hdrGrid });
        }
        else
        {
            panel.Children.Add(new Border
            {
                Background = BgStatus,
                Child = new TextBlock
                {
                    Text       = title,
                    Foreground = FgTitle,
                    FontSize   = 14,
                    FontWeight = Avalonia.Media.FontWeight.SemiBold,
                    Margin     = new Thickness(6, 4, 4, 4),
                },
            });
        }

        // Separator
        panel.Children.Add(new Border
        {
            Background = BorderColor,
            Height     = 1,
            Margin     = new Thickness(0),
        });

        // Rows
        foreach (var (key, valTb) in rows)
        {
            valTb.Text          = "-";
            valTb.Foreground    = FgValue;
            valTb.FontSize      = 13;
            valTb.TextAlignment = TextAlignment.Right;
            valTb.MinWidth      = 70;

            var keyTb = new TextBlock
            {
                Text              = key,
                Foreground        = FgKey,
                FontSize          = 13,
                VerticalAlignment = VerticalAlignment.Center,
            };

            var row = new Grid { Margin = new Thickness(6, 2, 6, 2) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Grid.SetColumn(keyTb, 0);
            Grid.SetColumn(valTb, 1);
            row.Children.Add(keyTb);
            row.Children.Add(valTb);
            panel.Children.Add(row);
        }

        panel.Children.Add(new Border { Height = 6 }); // bottom padding
        return panel;
    }

    // ── Terminal area ──────────────────────────────────────────────────────

    private Control BuildTerminalArea()
    {
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
            Background          = BgChrome,
            BorderBrush         = BorderHi,
            BorderThickness     = new Thickness(2),
            Padding             = new Thickness(4),
            Child               = inner,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment   = VerticalAlignment.Stretch,
        };
        return outer;
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
            _state.ScannerH     = s.LRSType.Contains("Holo", StringComparison.OrdinalIgnoreCase);
            _state.ScannerD     = !string.IsNullOrEmpty(s.LRSType) && !s.LRSType.Contains("Holo", StringComparison.OrdinalIgnoreCase);
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

    // ── Info panel refresh ─────────────────────────────────────────────────

    private void RefreshInfoPanels()
    {
        string traderName = string.IsNullOrEmpty(_state.TraderName) ? "-" : _state.TraderName;
        _valName.Text      = traderName;
        _deckValName.Text  = traderName;
        _valSector.Text    = _state.Sector.ToString();
        _deckValSector.Text = _valSector.Text;
        _valTurns.Text     = _state.Turns.ToString();
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
        _valHTotal.Text    = _state.HoldsTotal.ToString();
        _deckValHTotal.Text = _valHTotal.Text;
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
        _valTW1.Text       = _state.TranswarpDrive1 > 0 ? _state.TranswarpDrive1.ToString() : "-";
        _deckValTW1.Text   = _valTW1.Text;
        _valTW2.Text       = _state.TranswarpDrive2 > 0 ? _state.TranswarpDrive2.ToString() : "-";
        _deckValTW2.Text   = _valTW2.Text;
        UpdateScanInd(_scanIndD, _state.ScannerD);
        UpdateScanInd(_scanIndH, _state.ScannerH);
        UpdateScanInd(_scanIndP, _state.ScannerP);
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

        RefreshStatusBar();
        _tacticalMap?.InvalidateVisual();
    }

    private void RefreshStatusBar()
    {
        string conn = _state.Connected
            ? $"[ {_state.Host}:{_state.Port} ]"
            : "[ disconnected ]";

        string starDock = "-";
        string rylos = "-";
        string alpha = "-";
        int hagglePct = _gameInstance?.NativeHaggleSuccessRatePercent ?? 0;
        int haggleGood = _gameInstance?.NativeHaggleGoodCount ?? 0;
        int haggleGreat = _gameInstance?.NativeHaggleGreatCount ?? 0;
        int haggleExcellent = _gameInstance?.NativeHaggleExcellentCount ?? 0;
        bool showHagglePct =
            _gameInstance != null &&
            (_gameInstance.NativeHaggleEnabled ||
             haggleGood > 0 ||
             haggleGreat > 0 ||
             haggleExcellent > 0);

        if (_sessionDb != null)
        {
            var header = _sessionDb.DBHeader;

            if (header.StarDock != 0 && header.StarDock != 65535)
                starDock = header.StarDock.ToString();

            if (header.Rylos != 0 && header.Rylos != 65535)
                rylos = header.Rylos.ToString();

            if (header.AlphaCentauri != 0 && header.AlphaCentauri != 65535)
                alpha = header.AlphaCentauri.ToString();
        }

        string haggleText = showHagglePct
            ? $"  Haggle Pct: {hagglePct}% {haggleGood}/{haggleGreat}/{haggleExcellent}"
            : string.Empty;
        bool showBot = _embeddedGameConfig?.Mtc?.mombot != null || _mombot.IsAttached || _gameInstance != null;
        BotRuntimeState botRuntime = GetBotRuntimeState();
        string botText = showBot
            ? $"  Bot: {botRuntime.DisplayName}"
            : string.Empty;

        _statusText.Text =
            $" SD: {starDock,-6}  Rylos: {rylos,-6}  Alpha: {alpha,-6}{haggleText}{botText}  {conn}";

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
        _fileConnect.IsEnabled    = true;
        _fileDisconnect.IsEnabled = false;
        UpdateHaggleToggleState();
        RefreshMombotUi();
        RebuildProxyMenu();
    }

    private void OnHaggleToggleRequested()
    {
        if (_updatingHaggleToggle)
            return;

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
        _haggleToggle.IsEnabled = proxyActive;
        _deckHaggleToggle.IsEnabled = proxyActive;
        if (!proxyActive)
        {
            _updatingHaggleToggle = true;
            _haggleToggle.IsChecked = false;
            _deckHaggleToggle.IsChecked = false;
            _updatingHaggleToggle = false;
        }
    }

    private void ApplyMombotConfigChange(Action<MTC.mombot.mombotConfig> update)
    {
        _embeddedGameConfig ??= new EmbeddedGameConfig();
        _embeddedGameConfig.Mtc ??= new EmbeddedMtcConfig();
        _embeddedGameConfig.Mtc.mombot ??= new MTC.mombot.mombotConfig();

        update(_embeddedGameConfig.Mtc.mombot);
        _embeddedGameConfig.Mtc.mombot.WatcherEnabled = _embeddedGameConfig.Mtc.mombot.Enabled;
        _mombot.ApplyConfig(_embeddedGameConfig.Mtc.mombot);
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

        if (_mombotPromptOpen)
            CancelMombotPrompt();
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
            _updatingHaggleToggle = true;
            _haggleToggle.IsChecked = enabled;
            _deckHaggleToggle.IsChecked = enabled;
            _updatingHaggleToggle = false;
            UpdateHaggleToggleState();
            RefreshMombotUi();
            RefreshStatusBar();
            _buffer.Dirty = true;
        });
    }

    private void OnNativeHaggleStatsChanged()
    {
        Dispatcher.UIThread.Post(() =>
        {
            RefreshMombotUi();
            RefreshStatusBar();
            _buffer.Dirty = true;
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

            Directory.CreateDirectory(Path.GetDirectoryName(config.DatabasePath)!);
            var db = new Core.ModDatabase();
            db.CreateDatabase(config.DatabasePath, resetHeader);
            db.CloseDatabase();

            config.Variables.Clear();
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
        await ShowMessageAsync(
            "About MTC",
            "Mayhem Tradewars Client (MTC)\n" +
            "Version 1.0.0\n\n" +
            "Cross-platform Trade Wars 2002 client\n" +
            "built on TWXProxy Core.\n\n" +
            "Copyright (C) 2026 Matt Mosley\n" +
            "Licensed under GPL v2+");
    }

    private async Task OnPreferencesAsync()
    {
        bool saved = await new PreferencesDialog(_appPrefs).ShowDialog<bool>(this);
        if (!saved)
            return;

        AppPaths.SetConfiguredProgramDir(_appPrefs.ProgramDirectory);
        await ClearScriptDirectoryFromAllGameConfigsAsync();
        RefreshRuntimeScriptDirectoryFromPreferences();
        ApplyDebugLoggingPreferences();
        RebuildScriptsMenu();
    }

    private void ApplyDebugLoggingPreferences()
    {
        string? debugScriptDirectory = CurrentInterpreter?.ScriptDirectory;
        if (string.IsNullOrWhiteSpace(debugScriptDirectory) && !string.IsNullOrWhiteSpace(_appPrefs.ScriptsDirectory))
            debugScriptDirectory = _appPrefs.ScriptsDirectory;

        AppPaths.SetConfiguredProgramDir(_appPrefs.ProgramDirectory);
        string programDir = AppPaths.ProgramDir;
        Core.GlobalModules.ProgramDir = programDir;
        Core.GlobalModules.PreferPreparedVm = _appPrefs.PreparedVmEnabled;
        Core.GlobalModules.EnableVmMetrics = _appPrefs.VmMetricsEnabled;
        AppPaths.EnsureDebugLogDir();
        Core.GlobalModules.ConfigureDebugLogging(
            AppPaths.GetDebugLogPathForGame(GetDebugLogGameName()),
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

    private string GetDebugLogGameName()
    {
        if (!string.IsNullOrWhiteSpace(_embeddedGameName))
            return _embeddedGameName;

        if (!string.IsNullOrWhiteSpace(_state.GameName) || !string.IsNullOrEmpty(_currentProfilePath))
            return DeriveGameName();

        return string.Empty;
    }

    private void RefreshSessionLogTarget(string? scriptDirectory = null)
    {
        string programDir = AppPaths.ProgramDir;
        _sessionLog.ProgramDir = programDir;
        _sessionLog.LogDirectory = AppPaths.GetDebugLogDir();
        _sessionLog.SetLogIdentity(DeriveGameName());
    }

    private void ApplySessionLogSettings(EmbeddedGameConfig? gameConfig)
    {
        if (gameConfig == null)
            return;

        _sessionLog.LogEnabled = gameConfig.LogEnabled;
        _sessionLog.LogData = gameConfig.LogEnabled;
        _sessionLog.LogANSI = gameConfig.LogAnsi;
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
            TranswarpDrive1 = state.TranswarpDrive1,
            TranswarpDrive2 = state.TranswarpDrive2,
            ScannerD = state.ScannerD,
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
            TranswarpDrive1 = _state.TranswarpDrive1,
            TranswarpDrive2 = _state.TranswarpDrive2,
            ScannerD = _state.ScannerD,
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
                ? Core.WindowsInstallInfo.GetDefaultScriptsDirectory()
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
        config.UseLogin = _state.UseLogin;
        config.UseRLogin = _state.UseRLogin;
        config.LoginScript = string.IsNullOrWhiteSpace(_state.LoginScript) ? "0_Login.cts" : _state.LoginScript;
        config.LoginName = _state.LoginName;
        config.Password = _state.Password;
        config.GameLetter = _state.GameLetter;
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
            TranswarpDrive1 = profile.TranswarpDrive1,
            TranswarpDrive2 = profile.TranswarpDrive2,
            ScannerD = profile.ScannerD,
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
        _state.TranswarpDrive1 = p.TranswarpDrive1;
        _state.TranswarpDrive2 = p.TranswarpDrive2;
        _state.ScannerD       = p.ScannerD;
        _state.ScannerH       = p.ScannerH;
        _state.ScannerP       = p.ScannerP;
        SyncMombotRuntimeConfigFromTwxpCfg();
        _mombot.ApplyConfig(_embeddedGameConfig?.Mtc?.mombot);
        UpdateWindowTitle();
        RefreshStatusBar();
        _state.NotifyChanged();
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
            config.Mtc ??= new EmbeddedMtcConfig();
            config.Mtc.State ??= new EmbeddedMtcState();
            return config;
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
        bool configChanged =
            !string.Equals(gameConfig.Name, gameName, StringComparison.Ordinal) ||
            gameConfig.Host != _state.Host ||
            gameConfig.Port != _state.Port ||
            gameConfig.Sectors != _state.Sectors ||
            !string.Equals(gameConfig.DatabasePath, AppPaths.TwxproxyDatabasePathForGame(gameName), StringComparison.OrdinalIgnoreCase) ||
            gameConfig.UseLogin != _state.UseLogin ||
            gameConfig.UseRLogin != _state.UseRLogin ||
            !string.Equals(gameConfig.LoginScript, string.IsNullOrWhiteSpace(_state.LoginScript) ? "0_Login.cts" : _state.LoginScript, StringComparison.Ordinal) ||
            !string.Equals(gameConfig.LoginName, _state.LoginName, StringComparison.Ordinal) ||
            !string.Equals(gameConfig.Password, _state.Password, StringComparison.Ordinal) ||
            !string.Equals(gameConfig.GameLetter, _state.GameLetter, StringComparison.Ordinal);
        gameConfig = BuildEmbeddedGameConfigFromState(gameName, gameConfig);
        gameConfig.DatabasePath = AppPaths.TwxproxyDatabasePathForGame(gameName);
        if (configChanged)
            await SaveEmbeddedGameConfigAsync(gameName, gameConfig);
        _embeddedGameConfig = gameConfig;
        _embeddedGameName = gameName;
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
            Dispatcher.UIThread.Post(() => _ = StartInternalMombotAsync(
                botConfig,
                requestedBotName,
                interactiveOfflinePrompt: false,
                publishMissingGameMessage: false));
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
                        suppressMissingGameMessage: true);
                }
                finally
                {
                    _runtimeStopGate.Release();
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
                    Dispatcher.UIThread.Post(() =>
                    {
                        _sessionLog.RecordServerData(chunk);
                        _parser.Feed(chunk, chunk.Length);
                        if (_mombotPromptOpen)
                            RedrawMombotPrompt();
                        _buffer.Dirty = true;
                    });
                }
            }
            catch (OperationCanceledException) { }
            catch { }
        }, cts.Token);

        // Wire ServerDataReceived → trigger engine + ShipInfoParser + AutoRecorder.
        // Mirrors ProxyService.ServerDataReceived: splits on \r (TW2002 line terminator),
        // fires TextLineEvent / TextEvent / ActivateTriggers for each complete line,
        // and fires TextEvent (only) for partial lines / prompts.
        var serverLineBuf = new System.Text.StringBuilder();
        var rxAnsi  = new System.Text.RegularExpressions.Regex(
            @"\x1B\[[0-9;]*[A-Za-z]",
            System.Text.RegularExpressions.RegexOptions.Compiled);

        gi.ServerDataReceived += (_, e) =>
        {
            serverLineBuf.Append(e.Text);
            string buffered = serverLineBuf.ToString();
            int searchPos = 0;
            int lastProcessedPos = 0;

            while (searchPos < buffered.Length)
            {
                int crPos = buffered.IndexOf('\r', searchPos);

                if (crPos == -1)
                {
                    // No complete line yet — remainder is a partial line / prompt.
                    string remainder = buffered[lastProcessedPos..];
                    serverLineBuf.Clear();
                    serverLineBuf.Append(remainder);

                    if (!string.IsNullOrEmpty(remainder))
                    {
                        string remainderAnsi = Core.AnsiCodes.PrepareScriptAnsiText(remainder);
                        string scriptRemainder = Core.AnsiCodes.PrepareScriptText(remainder);
                        string strippedRemainder = Core.AnsiCodes.NormalizeTerminalText(rxAnsi.Replace(remainderAnsi, string.Empty).TrimEnd('\r'));
                        Core.GlobalModules.GlobalAutoRecorder.ProcessPrompt(strippedRemainder);
                        if (Core.GlobalModules.GlobalAutoRecorder.CurrentSector > 0)
                            Core.ScriptRef.SetCurrentSector(Core.GlobalModules.GlobalAutoRecorder.CurrentSector);
                        if (!gi.IsProxyMenuActive)
                        {
                            Core.ScriptRef.SetCurrentAnsiLine(remainderAnsi);
                            Core.ScriptRef.SetCurrentLine(scriptRemainder);
                            // Partial line / prompt: fire TextEvent only (no TextLineEvent, no ActivateTriggers).
                            interpreter.TextEvent(scriptRemainder, false);
                        }

                        bool nativeHaggleResponded = gi.ProcessNativeHaggleLine(strippedRemainder);
                        if (nativeHaggleResponded)
                        {
                            serverLineBuf.Clear();
                        }
                    }
                    break;
                }

                // Complete \r-terminated line.
                string lineRaw = Core.AnsiCodes.PrepareScriptAnsiText(buffered[lastProcessedPos..crPos]);
                string lineForScript = NormalizeLegacyInterrogLineForScripts(
                    Core.AnsiCodes.PrepareScriptText(buffered[lastProcessedPos..crPos]));
                string lineStripped = Core.AnsiCodes.NormalizeTerminalText(rxAnsi.Replace(lineRaw, string.Empty).TrimEnd('\r'));

                if (!string.IsNullOrEmpty(lineStripped))
                {
                    gi.FeedShipStatusLine(lineStripped);
                    _shipParser.FeedLine(lineStripped);
                    Core.GlobalModules.GlobalAutoRecorder.RecordLine(lineStripped);
                    if (Core.GlobalModules.GlobalAutoRecorder.CurrentSector > 0)
                        Core.ScriptRef.SetCurrentSector(Core.GlobalModules.GlobalAutoRecorder.CurrentSector);
                }

                gi.History.ProcessLine(lineStripped);
                if (!gi.IsProxyMenuActive)
                {
                    Core.ScriptRef.SetCurrentAnsiLine(lineRaw);
                    Core.ScriptRef.SetCurrentLine(lineForScript);

                    // Fire trigger pipeline (all lines including blank — matches Pascal ProcessLine).
                    interpreter.TextLineEvent(lineForScript, false);
                    interpreter.TextEvent(lineForScript, false);
                    interpreter.ActivateTriggers();
                }

                gi.ProcessNativeHaggleLine(lineStripped);
                if (!string.IsNullOrWhiteSpace(lineStripped))
                    SyncMombotPromptStateFromLine(lineStripped);

                if (!string.IsNullOrWhiteSpace(lineStripped) && _mombot.ObserveServerLine(lineStripped))
                {
                    Dispatcher.UIThread.Post(() =>
                    {
                        RefreshMombotUi();
                        RefreshStatusBar();
                        RebuildProxyMenu();
                        _buffer.Dirty = true;
                    });
                }

                searchPos = crPos + 1;
                lastProcessedPos = searchPos;
            }

            if (lastProcessedPos >= buffered.Length)
                serverLineBuf.Clear();
        };

        // Wire Connected / Disconnected events.
        // Note: OnGameConnected() was already called when the proxy started; we only need to
        // update game-connection state (status bar, _state.Connected) here.
        gi.Connected += (_, _) =>
        {
            Dispatcher.UIThread.Post(() =>
            {
                _state.Connected = true;
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
                RefreshStatusBar();
                _buffer.Dirty = true;
            });

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
                if (!string.IsNullOrEmpty(line))
                    interpreter.LocalInputEvent(line);
            }
        };

        _gameInstance = gi;
        ReloadRegisteredBotConfigs();
        SyncMombotRuntimeConfigFromTwxpCfg(gameConfig);
        _mombot.AttachSession(gi, _sessionDb, interpreter, gameConfig.Mtc.mombot);
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

        Core.ScriptRef.SetActiveGameInstance(null);
        Core.ScriptRef.OnVariableSaved = null;  // detach savevar persistence for this game
        _embeddedGameConfig = null;
        _embeddedGameName = null;

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

    private async Task ConnectEmbeddedServerAsync()
    {
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
                    cfg.Mtc ??= new EmbeddedMtcConfig();
                    cfg.Mtc.State ??= new EmbeddedMtcState();
                    return cfg;
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
            UseLogin = _state.UseLogin,
            UseRLogin = _state.UseRLogin,
            LoginScript = string.IsNullOrWhiteSpace(_state.LoginScript) ? "0_Login.cts" : _state.LoginScript,
            LoginName = _state.LoginName,
            Password = _state.Password,
            GameLetter = _state.GameLetter,
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
            string? runtimeNativeHaggleMode = cfg.NativeHaggleMode;
            cfg.NativeHaggleMode = null;
            cfg.Variables = NormalizeEmbeddedVariables(cfg.Variables);
            var json = System.Text.Json.JsonSerializer.Serialize(cfg, _jsonOpts);
            cfg.NativeHaggleMode = runtimeNativeHaggleMode;
            await File.WriteAllTextAsync(path, json);
        }
        catch { }
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
        _currentProfilePath = configPath;
        _embeddedGameConfig = config;
        _embeddedGameName = NormalizeGameName(config.Name);
        ApplyProfile(BuildProfileFromConfig(config));
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
                headerDirty |= header.Address != _state.Host;
                header.Address = _state.Host;
                headerDirty |= header.ServerPort != (ushort)_state.Port;
                header.ServerPort = (ushort)_state.Port;
                headerDirty |= header.ListenPort != (ushort)(_embeddedGameConfig?.ListenPort ?? 2300);
                header.ListenPort = (ushort)(_embeddedGameConfig?.ListenPort ?? 2300);
                headerDirty |= header.CommandChar != (_embeddedGameConfig?.CommandChar ?? '$');
                header.CommandChar = _embeddedGameConfig?.CommandChar ?? '$';
                headerDirty |= header.UseLogin != _state.UseLogin;
                header.UseLogin = _state.UseLogin;
                headerDirty |= header.UseRLogin != _state.UseRLogin;
                header.UseRLogin = _state.UseRLogin;
                headerDirty |= header.LoginScript != (string.IsNullOrWhiteSpace(_state.LoginScript) ? "0_Login.cts" : _state.LoginScript);
                header.LoginScript = string.IsNullOrWhiteSpace(_state.LoginScript) ? "0_Login.cts" : _state.LoginScript;
                headerDirty |= header.LoginName != _state.LoginName;
                header.LoginName = _state.LoginName;
                headerDirty |= header.Password != _state.Password;
                header.Password = _state.Password;
                char gameChar = string.IsNullOrWhiteSpace(_state.GameLetter) ? '\0' : char.ToUpperInvariant(_state.GameLetter[0]);
                headerDirty |= header.Game != gameChar;
                header.Game = gameChar;
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
                    UseLogin   = _state.UseLogin,
                    UseRLogin  = _state.UseRLogin,
                    LoginScript = string.IsNullOrWhiteSpace(_state.LoginScript) ? "0_Login.cts" : _state.LoginScript,
                    LoginName  = _state.LoginName,
                    Password   = _state.Password,
                    Game       = string.IsNullOrWhiteSpace(_state.GameLetter) ? '\0' : char.ToUpperInvariant(_state.GameLetter[0]),
                });
            }

            _sessionDb = db;
            Core.ScriptRef.SetActiveDatabase(db);

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

            foreach (string arg in Environment.GetCommandLineArgs().Skip(1))
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
        stopAll.Click += (_, _) => _ = OnProxyStopAllScriptsAsync(includeSystemScripts: true);
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
        IReadOnlyList<Core.TwxpConfigSection> sections = EnsureNativeBotSectionInTwxpCfg(programDir);
        var storedBots = new List<StoredBotSection>();
        bool foundNative = false;

        foreach (Core.TwxpConfigSection section in sections)
        {
            if (!section.Name.StartsWith("bot:", StringComparison.OrdinalIgnoreCase))
                continue;

            StoredBotSection bot = CreateStoredBotSection(section, programDir, scriptDirectory);
            if (bot.IsNative)
            {
                if (foundNative)
                    continue;

                foundNative = true;
            }

            storedBots.Add(bot);
        }

        if (!foundNative)
        {
            storedBots.Insert(0, CreateStoredBotSection(
                new Core.TwxpConfigSection(
                    Core.ProxyMenuCatalog.NativeMombotSectionName,
                    BuildDefaultNativeBotValues()),
                programDir,
                scriptDirectory));
        }

        return storedBots;
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

        TraceRuntimeStop($"[BotStop] external begin bot='{activeBotName}' lastLoaded='{lastLoadedModule}'");
        ClearMombotRelogState();
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

    private void ClearMombotRelogState()
    {
        Core.ScriptRef.SetCurrentGameVar("$doRelog", "0");
        Core.ScriptRef.SetCurrentGameVar("$BOT~DORELOG", "0");
        Core.ScriptRef.SetCurrentGameVar("$do_not_resuscitate", "0");
        Core.ScriptRef.SetCurrentGameVar("$relogging", "0");
        Core.ScriptRef.SetCurrentGameVar("$connectivity~relogging", "0");
        Core.ScriptRef.SetCurrentGameVar("$relog_message", string.Empty);
        Core.ScriptRef.SetCurrentGameVar("$BOT~LAST_LOADED_MODULE", string.Empty);
        Core.ScriptRef.SetCurrentGameVar("$BOT~MODE", "General");
    }

    private bool ShouldStopNativeMombotAfterDisconnect()
    {
        if (!_mombot.Enabled)
            return false;

        string stopRequested = ReadCurrentMombotVar("0", "$do_not_resuscitate");
        return string.Equals(stopRequested, "true", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(stopRequested, "1", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(stopRequested, "yes", StringComparison.OrdinalIgnoreCase);
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
        var dialog = new BotConfigDialog(
            bot.IsNative ? "Configure MomBot (native)" : $"Configure {bot.DisplayName}",
            defaults,
            bot.IsNative);
        if (!await dialog.ShowDialog<bool>(this) || dialog.Result == null)
        {
            FocusActiveTerminal();
            return;
        }

        if (!TryValidateBotDialogResult(dialog.Result, bot.IsNative, bot.SectionName, out string error, out BotConfigDialogResult normalized))
        {
            await ShowMessageAsync("Bot", error);
            return;
        }

        SaveBotSection(bot, normalized);
        ReloadRegisteredBotConfigs();
        SyncMombotRuntimeConfigFromTwxpCfg();
        if (_mombot.IsAttached)
            _mombot.ApplyConfig(_embeddedGameConfig?.Mtc?.mombot);
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
        return new BotConfigDialogResult(
            Alias: bot.Alias,
            Name: bot.Config.Name,
            Script: bot.Config.ScriptFiles.Count > 0
                ? string.Join(", ", bot.Config.ScriptFiles)
                : bot.Config.ScriptFile,
            Description: bot.Config.Description,
            AutoStart: bot.Config.AutoStart,
            NameVar: bot.Config.NameVar,
            CommsVar: bot.Config.CommsVar,
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
        _gameInstance.ReloadBotConfigs(programDir, scriptDirectory, includeNative: true);
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

        StoredBotSection nativeBot = LoadConfiguredBotSections().First(bot => bot.IsNative);
        targetConfig.Mtc ??= new EmbeddedMtcConfig();
        targetConfig.Mtc.mombot ??= new MTC.mombot.mombotConfig();

        MTC.mombot.mombotConfig runtimeConfig = targetConfig.Mtc.mombot;
        runtimeConfig.AutoStart = nativeBot.Config.AutoStart;
        runtimeConfig.ScriptRoot = GetNativeMombotScriptRoot(nativeBot.Config);
        runtimeConfig.WatcherEnabled = runtimeConfig.Enabled;
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

        Core.BotConfig botConfig = nativeBotConfig ?? LoadConfiguredBotSections().First(bot => bot.IsNative).Config;
        PrimeMombotBootstrapState(botConfig);
        CurrentInterpreter?.ActivateBotContext(botConfig, requestedBotName);
        SyncMombotRuntimeConfigFromTwxpCfg();

        if (_gameInstance.IsConnected)
        {
            SeedMombotRelogVarsFromCurrentState();
            ApplyMombotConfigChange(config => config.Enabled = true);
            LoadMombotStartupScripts();
            ShowMombotStartupBanner(connected: true);
            ShowMombotIntroWindow();
            await SendMombotStartupAnnouncementsAsync();
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
            ApplyMombotConfigChange(config => config.Enabled = true);
            LoadMombotStartupScripts();
            ShowMombotStartupBanner(connected: false);
            ShowMombotIntroWindow();
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
                suppressMissingGameMessage: false);
        }
        finally
        {
            _runtimeStopGate.Release();
        }
    }

    private async Task StopInternalMombotCoreAsync(bool publishStopMessage, bool suppressMissingGameMessage)
    {
        await Task.Yield();

        if (_gameInstance == null)
        {
            if (!suppressMissingGameMessage)
                PublishMombotLocalMessage("Mombot controls are only available while the embedded proxy is running.");
            return;
        }

        CancelMombotPrompt();
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

        TraceRuntimeStop($"[BotStop] native begin root='{scriptRootPath}' lastLoaded='{lastLoadedModule}'");
        ClearMombotRelogState();
        string nativeBotName = LoadConfiguredBotSections().First(bot => bot.IsNative).Config.Name;
        CurrentInterpreter?.ClearActiveBotContext(nativeBotName);

        ApplyMombotConfigChange(config => config.Enabled = false);
        _gameInstance.ActiveBotName = string.Empty;
        int drainedScripts = StopScriptsMatchingTree(
            origin: "native-mombot",
            directScriptPaths: Array.Empty<string>(),
            scriptRootPath: scriptRootPath,
            lastLoadedModule: lastLoadedModule,
            scriptDirectory: scriptDirectory,
            programDir: programDir);

        if (publishStopMessage)
            PublishMombotLocalMessage("Mombot stopped.");
        ApplyMombotExecutionRefresh();
        TraceRuntimeStop($"[BotStop] native complete drained={drainedScripts}");
    }

    private MTC.mombot.mombotRelogDialogResult BuildMombotRelogDefaults()
    {
        string stateLogin = NormalizeMombotValue(_state.LoginName, treatSelfAsEmpty: true);
        string botName = FirstMeaningfulMombotValue(
            Core.ScriptRef.GetCurrentGameVar("$BOT~BOT_NAME", string.Empty),
            Core.ScriptRef.GetCurrentGameVar("$SWITCHBOARD~BOT_NAME", string.Empty),
            Core.ScriptRef.GetCurrentGameVar("$bot_name", string.Empty),
            _mombot.Settings.BotName,
            "mombot");
        string serverName = FirstMeaningfulMombotValue(
            Core.ScriptRef.GetCurrentGameVar("$BOT~SERVERNAME", string.Empty),
            Core.ScriptRef.GetCurrentGameVar("$servername", string.Empty),
            stateLogin);
        string loginName = FirstMeaningfulMombotValue(
            Core.ScriptRef.GetCurrentGameVar("$BOT~USERNAME", string.Empty),
            Core.ScriptRef.GetCurrentGameVar("$username", string.Empty),
            stateLogin);
        string password = FirstMeaningfulMombotValue(
            Core.ScriptRef.GetCurrentGameVar("$BOT~PASSWORD", string.Empty),
            Core.ScriptRef.GetCurrentGameVar("$password", string.Empty),
            _state.Password);
        string gameLetter = FirstMeaningfulMombotValue(
            Core.ScriptRef.GetCurrentGameVar("$BOT~LETTER", string.Empty),
            Core.ScriptRef.GetCurrentGameVar("$letter", string.Empty),
            _state.GameLetter);
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

        MTC.mombot.mombotRelogLoginType loginType = newGameDay1
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

    private void ApplyMombotRelogDialogResult(MTC.mombot.mombotRelogDialogResult result)
    {
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
        string stateLogin = NormalizeMombotValue(_state.LoginName, treatSelfAsEmpty: true);
        string botName = FirstMeaningfulMombotValue(
            Core.ScriptRef.GetCurrentGameVar("$BOT~BOT_NAME", string.Empty),
            Core.ScriptRef.GetCurrentGameVar("$SWITCHBOARD~BOT_NAME", string.Empty),
            Core.ScriptRef.GetCurrentGameVar("$bot_name", string.Empty),
            _mombot.Settings.BotName,
            "mombot");
        string serverName = FirstMeaningfulMombotValue(
            Core.ScriptRef.GetCurrentGameVar("$BOT~SERVERNAME", string.Empty),
            Core.ScriptRef.GetCurrentGameVar("$servername", string.Empty),
            stateLogin);
        string loginName = FirstMeaningfulMombotValue(
            Core.ScriptRef.GetCurrentGameVar("$BOT~USERNAME", string.Empty),
            Core.ScriptRef.GetCurrentGameVar("$username", string.Empty),
            stateLogin);
        string password = FirstMeaningfulMombotValue(
            Core.ScriptRef.GetCurrentGameVar("$BOT~PASSWORD", string.Empty),
            Core.ScriptRef.GetCurrentGameVar("$password", string.Empty),
            _state.Password);
        string gameLetter = FirstMeaningfulMombotValue(
            Core.ScriptRef.GetCurrentGameVar("$BOT~LETTER", string.Empty),
            Core.ScriptRef.GetCurrentGameVar("$letter", string.Empty),
            _state.GameLetter);

        SetMombotCurrentVars(botName, "$BOT~BOT_NAME", "$SWITCHBOARD~BOT_NAME", "$bot_name");
        SetMombotCurrentVars(serverName, "$BOT~SERVERNAME", "$servername");
        SetMombotCurrentVars(loginName, "$BOT~USERNAME", "$username");
        SetMombotCurrentVars(password, "$BOT~PASSWORD", "$password");
        SetMombotCurrentVars(NormalizeGameLetter(gameLetter), "$BOT~LETTER", "$letter");
        SetMombotCurrentVars("1", "$BOT~DORELOG", "$doRelog");
        SetMombotCurrentVars("1", "$BOT~NEWGAMEOLDER", "$newGameOlder");
        SetMombotCurrentVars("0", "$BOT~NEWGAMEDAY1", "$newGameDay1");
        SetMombotCurrentVars("0", "$BOT~ISSHIPDESTROYED");
        SetMombotCurrentVars("General", "$BOT~MODE", "$mode");
        SetMombotCurrentVars(string.Empty, "$BOT~LAST_LOADED_MODULE", "$LAST_LOADED_MODULE");
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

    private void ShowMombotStartupBanner(bool connected)
    {
        string botName = FirstMeaningfulMombotValue(
            Core.ScriptRef.GetCurrentGameVar("$BOT~BOT_NAME", string.Empty),
            Core.ScriptRef.GetCurrentGameVar("$SWITCHBOARD~BOT_NAME", string.Empty),
            Core.ScriptRef.GetCurrentGameVar("$bot_name", string.Empty),
            _mombot.Settings.BotName,
            "mombot");
        string stateLabel = connected ? "online" : "relog armed";
        string version = GetMombotVersionDisplay();
        string banner =
            "\r\n" +
            $"\u001b[1;36m=== Mombot {version} ===\u001b[0m\r\n" +
            $"\u001b[1;33m{botName}\u001b[0m \u001b[1;32m{stateLabel}\u001b[0m\r\n" +
            "\u001b[38;5;245mUse > to open the mombot prompt.\u001b[0m\r\n";

        if (_gameInstance != null)
            _gameInstance.ClientMessage(banner);
        else
            _parser.Feed(banner);

        _buffer.Dirty = true;
    }

    private void ShowMombotIntroWindow()
    {
        if (_mombotIntroWindow is { IsVisible: true })
        {
            _mombotIntroWindow.Activate();
            return;
        }

        var window = new MTC.mombot.mombotIntroWindow();
        _mombotIntroWindow = window;
        window.Closed += (_, _) =>
        {
            if (ReferenceEquals(_mombotIntroWindow, window))
                _mombotIntroWindow = null;
        };
        window.Show(this);
    }

    private async Task SendMombotStartupAnnouncementsAsync()
    {
        if (_gameInstance == null || !_gameInstance.IsConnected)
            return;

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

        if (string.IsNullOrWhiteSpace(loginName) ||
            string.IsNullOrWhiteSpace(gameLetter) ||
            !string.Equals(dorelog, "1", StringComparison.OrdinalIgnoreCase))
        {
            await _gameInstance.SendToServerAsync(System.Text.Encoding.ASCII.GetBytes(
                TranslateMombotBurstText($"'{{{botName}}} - Auto Relog - Not Active*")));
        }
    }

    private void PrimeMombotBootstrapState(Core.BotConfig botConfig)
    {
        string programDir = CurrentInterpreter?.ProgramDir ?? GetEffectiveProxyProgramDir(GetEffectiveProxyScriptDirectory());
        string scriptRoot = GetNativeMombotScriptRoot(botConfig).Trim().Trim('/');
        string scriptRootRelative = GetMombotScriptRootRelative(scriptRoot);
        string majorVersion = "4";
        string minorVersion = "7beta";

        string gameName = _embeddedGameName ?? DeriveGameName();
        string folderRelative = Path.Combine(scriptRoot, "games", gameName).Replace('\\', '/');
        string folderFullPath = Path.Combine(programDir, folderRelative.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(folderFullPath);

        string folderConfigRelative = Path.Combine("scripts", $"mombot{majorVersion}_{minorVersion}.cfg").Replace('\\', '/');
        string folderConfigFullPath = Path.Combine(programDir, folderConfigRelative.Replace('/', Path.DirectorySeparatorChar));
        string hotkeysRelative = Path.Combine(scriptRoot, "hotkeys.cfg").Replace('\\', '/');
        string hotkeysFullPath = Path.Combine(programDir, hotkeysRelative.Replace('/', Path.DirectorySeparatorChar));
        string customKeysRelative = Path.Combine(scriptRoot, "custom_keys.cfg").Replace('\\', '/');
        string customKeysFullPath = Path.Combine(programDir, customKeysRelative.Replace('/', Path.DirectorySeparatorChar));
        string customCommandsRelative = Path.Combine(scriptRoot, "custom_commands.cfg").Replace('\\', '/');
        string customCommandsFullPath = Path.Combine(programDir, customCommandsRelative.Replace('/', Path.DirectorySeparatorChar));
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
        EnsureMombotIndexedConfigFile(hotkeysFullPath, BuildDefaultMombotHotkeyFileLines());
        EnsureMombotIndexedConfigFile(customKeysFullPath, BuildDefaultMombotCustomKeyFileLines());
        EnsureMombotIndexedConfigFile(customCommandsFullPath, BuildDefaultMombotCustomCommandFileLines());

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
        string currentSector = Core.ScriptRef.GetCurrentSector() > 0 ? Core.ScriptRef.GetCurrentSector().ToString() : FormatMombotSector((ushort)_state.Sector);
        string currentPrompt = GetInitialMombotPromptName();

        SetMombotCurrentVars(majorVersion, "$bot~major_version", "$major_version", "$BOT~MAJOR_VERSION");
        SetMombotCurrentVars(minorVersion, "$bot~minor_version", "$minor_version", "$BOT~MINOR_VERSION");
        SetMombotCurrentVars(scriptRootRelative, "$bot~default_bot_directory", "$default_bot_directory");
        SetMombotCurrentVars(scriptRootRelative, "$bot~mombot_directory", "$mombot_directory", "$BOT~MOMBOT_DIRECTORY");
        SetMombotCurrentVars(folderConfigRelative, "$mombot_folder_config");
        SetMombotCurrentVars(hotkeysRelative, "$hotkeys_file");
        SetMombotCurrentVars(customKeysRelative, "$custom_keys_file");
        SetMombotCurrentVars(customCommandsRelative, "$custom_commands_file");
        SetMombotCurrentVars(folderRelative, "$folder");
        SetMombotCurrentVars(gconfigRelative, "$gconfig_file");
        SetMombotCurrentVars(botUsersRelative, "$BOT_USER_FILE");
        SetMombotCurrentVars(ckFigRelative, "$CK_FIG_FILE");
        SetMombotCurrentVars(shipCapRelative, "$SHIP~cap_file", "$SHIP~CAP_FILE", "$ship~cap_file");
        SetMombotCurrentVars(planetFileRelative, "$PLANET~planet_file", "$PLANET~PLANET_FILE", "$planet~planet_file");
        SetMombotCurrentVars(figFileRelative, "$FIG_FILE");
        SetMombotCurrentVars(figCountRelative, "$FIG_COUNT_FILE");
        SetMombotCurrentVars(limpetFileRelative, "$LIMP_FILE");
        SetMombotCurrentVars(limpetCountRelative, "$LIMP_COUNT_FILE");
        SetMombotCurrentVars(armidFileRelative, "$ARMID_FILE");
        SetMombotCurrentVars(armidCountRelative, "$ARMID_COUNT_FILE");
        SetMombotCurrentVars(gameSettingsRelative, "$GAME~GAME_SETTINGS_FILE");
        SetMombotCurrentVars(scriptFileRelative, "$SCRIPT_FILE");
        SetMombotCurrentVars(bustFileRelative, "$BUST_FILE");
        SetMombotCurrentVars(timerFileRelative, "$timer_file");
        SetMombotCurrentVars(mcicFileRelative, "$MCIC_FILE");

        SetMombotCurrentVars(botName, "$BOT~BOT_NAME", "$SWITCHBOARD~BOT_NAME", "$SWITCHBOARD~bot_name", "$bot~bot_name", "$bot_name", "$bot~name");
        SetMombotCurrentVars(teamName, "$BOT~BOT_TEAM_NAME", "$BOT~bot_team_name", "$bot~bot_team_name", "$bot_team_name");
        SetMombotCurrentVars(botPassword, "$BOT~BOT_PASSWORD", "$bot~bot_password", "$bot_password");
        SetMombotCurrentVars(_state.TraderName?.Trim() ?? string.Empty, "$PLAYER~TRADER_NAME");
        SetMombotCurrentVars(currentSector, "$PLAYER~CURRENT_SECTOR", "$player~current_sector");
        SetMombotCurrentVars(currentPrompt, "$PLAYER~CURRENT_PROMPT", "$PLAYER~startingLocation", "$bot~startingLocation");
        SetMombotCurrentVars(string.Empty, "$BOT~COMMAND", "$bot~command", "$command");
        SetMombotCurrentVars(string.Empty, "$BOT~USER_COMMAND_LINE", "$bot~user_command_line", "$USER_COMMAND_LINE", "$user_command_line");
        MirrorMombotCurrentVars(string.Empty, "$BOT~PASSWORD", "$password");
        MirrorMombotCurrentVars(string.Empty, "$BOT~USERNAME", "$username");
        MirrorMombotCurrentVars(string.Empty, "$BOT~SERVERNAME", "$servername");
        MirrorMombotCurrentVars(string.Empty, "$BOT~LETTER", "$letter", "$LETTER");
        MirrorMombotCurrentVars(subspace, "$BOT~SUBSPACE", "$bot~subspace", "$subspace");
        MirrorMombotCurrentVars("General", "$BOT~MODE", "$bot~mode", "$mode");
        MirrorMombotCurrentVars(string.Empty, "$BOT~LAST_LOADED_MODULE", "$LAST_LOADED_MODULE");
        MirrorMombotCurrentVars("0", "$BOT~BOT_TURN_LIMIT", "$bot~bot_turn_limit", "$bot_turn_limit");
        MirrorMombotCurrentVars("0", "$BOT~SAFE_SHIP", "$bot~safe_ship", "$safe_ship");
        MirrorMombotCurrentVars("0", "$BOT~SAFE_PLANET", "$bot~safe_planet", "$safe_planet");
        MirrorMombotCurrentVars("0", "$BOT~BOTISDEAF", "$BOT~botIsDeaf", "$bot~botIsDeaf", "$botIsDeaf");
        MirrorMombotCurrentVars("0", "$BOT~SILENT_RUNNING", "$bot~silent_running", "$silent_running");
        MirrorMombotCurrentVars("0", "$PLAYER~UNLIMITEDGAME", "$PLAYER~unlimitedGame", "$unlimitedGame");
        MirrorMombotCurrentVars("0", "$PLAYER~dropOffensive", "$PLAYER~DROPOFFENSIVE");
        MirrorMombotCurrentVars("0", "$PLAYER~dropToll", "$PLAYER~DROPTOLL");
        MirrorMombotCurrentVars("0", "$do_not_resuscitate");
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
        MirrorMombotCurrentVars(string.Empty, "$historyString");
        MirrorMombotCurrentVars(string.Empty, "$command_prompt_extras");
        MirrorMombotCurrentVars("5760", "$echoInterval");
        MirrorMombotCurrentVars(hadExistingBotConfig ? "1" : "0", "$BOT~DORELOG", "$doRelog");
        MirrorMombotCurrentVars("0", "$BOT~NEWGAMEDAY1", "$newGameDay1");
        MirrorMombotCurrentVars("0", "$BOT~NEWGAMEOLDER", "$newGameOlder");
        MirrorMombotCurrentVars("0", "$BOT~ISSHIPDESTROYED");
        MirrorMombotCurrentVars("0", "$relogging", "$connectivity~relogging");
        MirrorMombotCurrentVars(string.Empty, "$command_caller", "$BOT~COMMAND_CALLER", "$bot~command_caller");
        MirrorMombotCurrentVars("0", "$SWITCHBOARD~SELF_COMMAND", "$switchboard~self_command", "$BOT~SELF_COMMAND", "$bot~self_command", "$self_command");

        string stardock = ReadCurrentMombotVar(FormatMombotSector(_sessionDb?.DBHeader.StarDock), "$MAP~STARDOCK", "$MAP~stardock", "$stardock");
        string rylos = ReadCurrentMombotVar(FormatMombotSector(_sessionDb?.DBHeader.Rylos), "$MAP~RYLOS", "$MAP~rylos", "$rylos");
        string alphaCentauri = ReadCurrentMombotVar(FormatMombotSector(_sessionDb?.DBHeader.AlphaCentauri), "$MAP~ALPHA_CENTAURI", "$MAP~alpha_centauri", "$alpha_centauri");
        MirrorMombotCurrentVars(stardock, "$MAP~STARDOCK", "$MAP~stardock", "$BOT~STARDOCK", "$stardock");
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

    private static void EnsureMombotIndexedConfigFile(string fullPath, IReadOnlyList<string> lines)
    {
        try
        {
            string? directory = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);

            bool rewrite = true;
            if (File.Exists(fullPath))
            {
                string[] existingLines = File.ReadAllLines(fullPath);
                rewrite = existingLines.Length != lines.Count;
            }

            if (rewrite)
                File.WriteAllLines(fullPath, lines);
        }
        catch
        {
        }
    }

    private static IReadOnlyList<string> BuildDefaultMombotHotkeyFileLines()
    {
        string[] lines = Enumerable.Repeat("0", 255).ToArray();
        SetMombotIndexedLine(lines, 76, "9");
        SetMombotIndexedLine(lines, 108, "9");
        SetMombotIndexedLine(lines, 102, "14");
        SetMombotIndexedLine(lines, 70, "14");
        SetMombotIndexedLine(lines, 109, "13");
        SetMombotIndexedLine(lines, 77, "13");
        SetMombotIndexedLine(lines, 104, "5");
        SetMombotIndexedLine(lines, 72, "5");
        SetMombotIndexedLine(lines, 107, "1");
        SetMombotIndexedLine(lines, 75, "1");
        SetMombotIndexedLine(lines, 99, "2");
        SetMombotIndexedLine(lines, 67, "2");
        SetMombotIndexedLine(lines, 98, "17");
        SetMombotIndexedLine(lines, 66, "17");
        SetMombotIndexedLine(lines, 112, "7");
        SetMombotIndexedLine(lines, 80, "7");
        SetMombotIndexedLine(lines, 100, "11");
        SetMombotIndexedLine(lines, 68, "11");
        SetMombotIndexedLine(lines, 116, "6");
        SetMombotIndexedLine(lines, 84, "6");
        SetMombotIndexedLine(lines, 114, "3");
        SetMombotIndexedLine(lines, 82, "3");
        SetMombotIndexedLine(lines, 115, "4");
        SetMombotIndexedLine(lines, 83, "4");
        SetMombotIndexedLine(lines, 120, "12");
        SetMombotIndexedLine(lines, 88, "12");
        SetMombotIndexedLine(lines, 122, "15");
        SetMombotIndexedLine(lines, 90, "15");
        SetMombotIndexedLine(lines, 126, "16");
        SetMombotIndexedLine(lines, 113, "8");
        SetMombotIndexedLine(lines, 81, "8");
        SetMombotIndexedLine(lines, 9, "10");
        return lines;
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

    private static void SetMombotIndexedLine(IList<string> lines, int oneBasedIndex, string value)
    {
        if (oneBasedIndex >= 1 && oneBasedIndex <= lines.Count)
            lines[oneBasedIndex - 1] = value;
    }

    private void SyncMombotPromptStateFromLine(string line)
    {
        if (TryGetMombotPromptNameFromLine(line, out string promptName))
            SetMombotCurrentVars(promptName, "$PLAYER~CURRENT_PROMPT", "$PLAYER~startingLocation", "$bot~startingLocation");
    }

    private string GetInitialMombotPromptName()
    {
        return GetMombotPromptSurface() switch
        {
            MombotPromptSurface.Command => "Command",
            MombotPromptSurface.Citadel => "Citadel",
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

        if (line.StartsWith("Citadel command (", StringComparison.OrdinalIgnoreCase) ||
            line.Contains("<Enter Citadel>", StringComparison.OrdinalIgnoreCase) ||
            line.Contains("Citadel treasury contains", StringComparison.OrdinalIgnoreCase))
        {
            promptName = "Citadel";
            return true;
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

    private static string GetMombotVersionDisplay()
    {
        string major = ReadCurrentMombotVar("4", "$BOT~MAJOR_VERSION", "$bot~major_version", "$major_version");
        string minor = ReadCurrentMombotVar("7beta", "$BOT~MINOR_VERSION", "$bot~minor_version", "$minor_version");
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

    private static string NormalizeGameLetter(string? value)
    {
        string normalized = NormalizeMombotValue(value);
        return string.IsNullOrEmpty(normalized) ? string.Empty : normalized[..1].ToUpperInvariant();
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
        if (TryHandleMombotPromptInput(bytes))
            return;

        if (TryInterceptMombotHotkeyAccess(bytes))
            return;

        if (TryInterceptMombotCommandPrompt(bytes))
            return;

        forward(bytes);
    }

    private bool TryHandleMombotPromptInput(byte[] bytes)
    {
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
                    if (_mombotPromptBuffer.Length > 0)
                    {
                        _mombotPromptBuffer = _mombotPromptBuffer[..^1];
                        changed = true;
                    }
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
                        if (value == (byte)'>' && _mombotPromptBuffer.Length == 0)
                        {
                            BeginMombotMacroPrompt();
                            return true;
                        }

                        _mombotPromptBuffer += (char)value;
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

        if (_mombotHotkeyPromptOpen)
            return;

        _mombotHotkeyPromptOpen = true;
        _mombotScriptPromptOpen = false;
        _mombotPreferencesOpen = false;
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
            FocusActiveTerminal();
        }
    }

    private void BeginMombotPrompt(string initialValue = "")
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

        _mombotPromptOpen = true;
        _mombotPromptBuffer = initialValue;
        _mombotPromptDraft = initialValue;
        _mombotPromptHistoryIndex = _mombotCommandHistory.Count;
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
        RedrawMombotPrompt();
    }

    private void CancelMombotPrompt()
    {
        if (!_mombotPromptOpen)
            return;

        ResetMombotPromptState();
        _parser.Feed("\r\x1b[K");
        _buffer.Dirty = true;
        FocusActiveTerminal();
    }

    private void SubmitMombotPrompt()
    {
        if (!_mombotPromptOpen)
            return;

        string command = _mombotPromptBuffer;
        string prompt = GetMombotPromptPrefix();

        ResetMombotPromptState();

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

        RememberMombotHistory(command);
        _mombot.TryExecuteLocalInput(command, out _);
        ApplyMombotExecutionRefresh();
    }

    private void ResetMombotPromptState()
    {
        _mombotPromptOpen = false;
        _mombotHotkeyPromptOpen = false;
        _mombotScriptPromptOpen = false;
        _mombotPreferencesOpen = false;
        _mombotPreferencesCaptureSingleKey = false;
        _mombotPreferencesInputPrompt = string.Empty;
        _mombotPreferencesInputBuffer = string.Empty;
        _mombotPreferencesInputHandler = null;
        _mombotPreferencesHotkeySlot = 0;
        _mombotMacroPromptOpen = false;
        _mombotMacroContext = null;
        _mombotHotkeyScripts = Array.Empty<MombotHotkeyScriptEntry>();
        _mombotPromptBuffer = string.Empty;
        _mombotPromptDraft = string.Empty;
        _mombotPromptHistoryIndex = _mombotCommandHistory.Count;
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
            _parser.Feed(_mombotPromptBuffer);
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
        ApplyMombotExecutionRefresh();
    }

    private void ClearMombotPreferencesInputState()
    {
        _mombotPreferencesCaptureSingleKey = false;
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

    private void BeginMombotPreferencesInput(string prompt, Action<string> handler, string initialValue = "", bool captureSingleKey = false)
    {
        _mombotPreferencesCaptureSingleKey = captureSingleKey;
        _mombotPreferencesInputPrompt = prompt;
        _mombotPreferencesInputBuffer = captureSingleKey ? string.Empty : initialValue;
        _mombotPreferencesInputHandler = handler;
        RedrawMombotPrompt();
    }

    private void CompleteMombotPreferencesInput(string value)
    {
        Action<string>? handler = _mombotPreferencesInputHandler;
        ClearMombotPreferencesInputState();
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
                    ReadCurrentMombotVar(string.Empty, "$BOT~PASSWORD", "$bot~password", "$password"));
                return;

            case 'Z':
                BeginMombotPreferencesInput(
                    "Bot Password",
                    value => PersistMombotVars(value.Trim(), "$BOT~BOT_PASSWORD", "$bot~bot_password", "$bot_password"),
                    ReadCurrentMombotVar(string.Empty, "$BOT~BOT_PASSWORD", "$bot~bot_password", "$bot_password"));
                return;

            case 'G':
                BeginMombotPreferencesInput(
                    "Game Letter",
                    value => PersistMombotVars(value.Trim().ToUpperInvariant(), "$BOT~LETTER", "$bot~letter", "$letter"),
                    ReadCurrentMombotVar(string.Empty, "$BOT~LETTER", "$bot~letter", "$letter"));
                return;

            case 'C':
                BeginMombotPreferencesInput(
                    "Login Name",
                    value => PersistMombotVars(value.Trim(), "$BOT~USERNAME", "$bot~username", "$username"),
                    ReadCurrentMombotVar(string.Empty, "$BOT~USERNAME", "$bot~username", "$username"));
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
                PromptMombotCountPreference("Surround figs", 0, 50000, "$PLAYER~surroundFigs", "$PLAYER~SURROUNDFIGS");
                return;

            case '4':
                PromptMombotCountPreference("Surround limpets", 0, 250, "$PLAYER~surroundLimp", "$PLAYER~SURROUNDLIMP");
                return;

            case '5':
                PromptMombotCountPreference("Surround armids", 0, 250, "$PLAYER~surroundMine", "$PLAYER~SURROUNDMINE");
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
                    ReadCurrentMombotVar(string.Empty, "$BOT~alarm_list", "$bot~alarm_list", "$alarm_list"));
                return;

            case 'X':
                PromptMombotCountPreference("Safe Ship", 0, int.MaxValue, "$BOT~SAFE_SHIP", "$BOT~safe_ship", "$bot~safe_ship", "$safe_ship");
                return;

            case 'L':
                PromptMombotCountPreference("Safe Planet", 0, int.MaxValue, "$BOT~SAFE_PLANET", "$BOT~safe_planet", "$bot~safe_planet", "$safe_planet");
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
        AppendMombotPreferencesHeader(body, "Preferences", "General");

        string botName = ReadCurrentMombotVar("mombot", "$SWITCHBOARD~BOT_NAME", "$SWITCHBOARD~bot_name", "$bot~bot_name", "$bot_name");
        string loginPassword = ReadCurrentMombotVar(string.Empty, "$BOT~PASSWORD", "$bot~password", "$password");
        string botPassword = ReadCurrentMombotVar(string.Empty, "$BOT~BOT_PASSWORD", "$bot~bot_password", "$bot_password");
        string loginName = ReadCurrentMombotVar(string.Empty, "$BOT~USERNAME", "$bot~username", "$username");
        string turnLimit = IsMombotTruthy(ReadCurrentMombotVar("0", "$PLAYER~UNLIMITEDGAME", "$PLAYER~unlimitedGame", "$unlimitedGame"))
            ? "Unlimited"
            : ReadCurrentMombotVar("0", "$BOT~BOT_TURN_LIMIT", "$bot~bot_turn_limit", "$bot_turn_limit");
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

        AppendMombotPreferencesEntry(body, "C", "Login Name", loginName);
        AppendMombotPreferencesEntry(body, "P", "Game Password", MaskMombotSecret(loginPassword));
        AppendMombotPreferencesEntry(body, "N", "Bot Name", botName);
        AppendMombotPreferencesEntry(body, "Z", "Bot Password", MaskMombotSecret(botPassword));
        AppendMombotPreferencesEntry(body, "G", "Game Letter", ReadCurrentMombotVar(string.Empty, "$BOT~LETTER", "$bot~letter", "$letter"));
        AppendMombotPreferencesEntry(body, "E", "Banner Interval", ReadCurrentMombotVar("5760", "$BOT~echoInterval", "$echoInterval") + " Minutes");
        AppendMombotPreferencesEntry(body, "1", "Turn Limit", turnLimit);
        AppendMombotPreferencesEntry(body, "0", "MSL / Busted Prompt", FormatMombotBoolDisplay(ReadCurrentMombotVar("0", "$BOT~command_prompt_extras", "$command_prompt_extras")));
        AppendMombotPreferencesEntry(body, "V", "Silent Mode", FormatMombotBoolDisplay(ReadCurrentMombotVar("0", "$BOT~silent_running", "$bot~silent_running", "$silent_running")));
        AppendMombotPreferencesEntry(body, "J", "Alarm List", string.IsNullOrWhiteSpace(alarmList) ? "None" : "Active");
        AppendMombotPreferencesEntry(body, "3", "Figs to Drop", ReadCurrentMombotVar("0", "$PLAYER~surroundFigs", "$PLAYER~SURROUNDFIGS"));
        AppendMombotPreferencesEntry(body, "4", "Limpets to Drop", ReadCurrentMombotVar("0", "$PLAYER~surroundLimp", "$PLAYER~SURROUNDLIMP"));
        AppendMombotPreferencesEntry(body, "5", "Armids to Drop", ReadCurrentMombotVar("0", "$PLAYER~surroundMine", "$PLAYER~SURROUNDMINE"));
        AppendMombotPreferencesEntry(body, "6", "Fig Type", figType);
        AppendMombotPreferencesEntry(body, "7", "Auto Kill Mode", FormatMombotBoolDisplay(ReadCurrentMombotVar("0", "$bot~autoattack", "$BOT~autoattack", "$autoattack")));
        AppendMombotPreferencesEntry(body, "8", "Avoid Planets", avoidPlanets);
        AppendMombotPreferencesEntry(body, "9", "Surround Type", surroundType);
        AppendMombotPreferencesEntry(body, "K", "Surround HKILL", FormatMombotBoolDisplay(ReadCurrentMombotVar("0", "$PLAYER~surround_before_hkill")));
        AppendMombotPreferencesEntry(body, "2", "Alien Ships", captureMode);
        AppendMombotPreferencesEntry(body, "S", "Stardock", FormatMombotDefinedSectorDisplay(ReadCurrentMombotVar("0", "$MAP~STARDOCK", "$MAP~stardock", "$BOT~STARDOCK", "$stardock")));
        AppendMombotPreferencesEntry(body, "R", "Rylos", FormatMombotDefinedSectorDisplay(ReadCurrentMombotVar("0", "$MAP~RYLOS", "$MAP~rylos", "$BOT~RYLOS", "$rylos")));
        AppendMombotPreferencesEntry(body, "A", "Alpha", FormatMombotDefinedSectorDisplay(ReadCurrentMombotVar("0", "$MAP~ALPHA_CENTAURI", "$MAP~alpha_centauri", "$BOT~ALPHA_CENTAURI", "$alpha_centauri")));
        AppendMombotPreferencesEntry(body, "H", "Home Sector", FormatMombotDefinedSectorDisplay(ReadCurrentMombotVar("0", "$MAP~HOME_SECTOR", "$MAP~home_sector", "$BOT~HOME_SECTOR", "$home_sector")));
        AppendMombotPreferencesEntry(body, "B", "Backdoor", FormatMombotDefinedSectorDisplay(ReadCurrentMombotVar("0", "$MAP~BACKDOOR", "$MAP~backdoor", "$backdoor")));
        AppendMombotPreferencesEntry(body, "X", "Safe Ship", FormatMombotDefinedSectorDisplay(ReadCurrentMombotVar("0", "$BOT~SAFE_SHIP", "$BOT~safe_ship", "$bot~safe_ship", "$safe_ship")));
        AppendMombotPreferencesEntry(body, "L", "Safe Planet", FormatMombotDefinedSectorDisplay(ReadCurrentMombotVar("0", "$BOT~SAFE_PLANET", "$BOT~safe_planet", "$bot~safe_planet", "$safe_planet")));
        body.Append("\r\n");
        AppendMombotPreferencesEntry(body, "-", "Current Ship Offensive Odds", ReadCurrentMombotVar("0", "$SHIP~SHIP_OFFENSIVE_ODDS"));
        AppendMombotPreferencesEntry(body, "-", "Current Ship Max Attack", ReadCurrentMombotVar("0", "$SHIP~SHIP_MAX_ATTACK"));
        AppendMombotPreferencesEntry(body, "-", "Current Ship Max Fighters", ReadCurrentMombotVar("0", "$SHIP~SHIP_FIGHTERS_MAX"));
        AppendMombotPreferencesFooter(body, "[>] Game Stats", "[<] Trader List", "Any other key exits");
    }

    private void BuildMombotGameStatsPreferencesPage(System.Text.StringBuilder body)
    {
        AppendMombotPreferencesHeader(body, "Preferences", "Game Stats");

        (string Label, string Value)[] items =
        {
            ("Atomic Detonators", ReadCurrentMombotVar("0", "$GAME~ATOMIC_COST", "$ATOMIC_COST")),
            ("Marker Beacons", ReadCurrentMombotVar("0", "$GAME~BEACON_COST", "$BEACON_COST")),
            ("Corbomite Devices", ReadCurrentMombotVar("0", "$GAME~CORBO_COST", "$CORBO_COST")),
            ("Cloaking Devices", ReadCurrentMombotVar("0", "$GAME~CLOAK_COST", "$CLOAK_COST")),
            ("Subspace Ether Probes", ReadCurrentMombotVar("0", "$GAME~PROBE_COST", "$PROBE_COST")),
            ("Planet Scanners", ReadCurrentMombotVar("0", "$GAME~PLANET_SCANNER_COST", "$PLANET_SCANNER_COST")),
            ("Limpet Tracking Mines", ReadCurrentMombotVar("0", "$GAME~LIMPET_COST", "$LIMPET_REMOVAL_COST")),
            ("Space Mines", ReadCurrentMombotVar("0", "$GAME~ARMID_COST", "$ARMID_COST")),
            ("Photon Missiles", IsMombotTruthy(ReadCurrentMombotVar("0", "$GAME~PHOTONS_ENABLED", "$PHOTONS_ENABLED")) ? ReadCurrentMombotVar("0", "$GAME~PHOTON_COST", "$PHOTON_COST") : "Disabled"),
            ("Holographic Scan", ReadCurrentMombotVar("0", "$GAME~HOLO_COST", "$HOLO_COST")),
            ("Density Scan", ReadCurrentMombotVar("0", "$GAME~DENSITY_COST", "$DENSITY_COST")),
            ("Mine Disruptors", ReadCurrentMombotVar("0", "$GAME~DISRUPTOR_COST", "$DISRUPTOR_COST")),
            ("Genesis Torpedoes", ReadCurrentMombotVar("0", "$GAME~GENESIS_COST", "$GENESIS_COST")),
            ("TransWarp I", ReadCurrentMombotVar("0", "$GAME~TWARPI_COST", "$TWARPI_COST")),
            ("TransWarp II", ReadCurrentMombotVar("0", "$GAME~TWARPII_COST", "$TWARPII_COST")),
            ("Psychic Probes", ReadCurrentMombotVar("0", "$GAME~PSYCHIC_COST", "$PSYCHIC_COST")),
            ("Limpet Removal", ReadCurrentMombotVar("0", "$GAME~LIMPET_REMOVAL_COST", "$LIMPET_REMOVAL_COST")),
            ("Server Max Commands", ReadCurrentMombotVar("0", "$GAME~MAX_COMMANDS", "$MAX_COMMANDS") is "0" ? "Unlimited" : ReadCurrentMombotVar("0", "$GAME~MAX_COMMANDS", "$MAX_COMMANDS")),
            ("Gold Enabled", FormatMombotBoolDisplay(ReadCurrentMombotVar("0", "$GAME~goldEnabled", "$goldEnabled"))),
            ("MBBS Mode", FormatMombotBoolDisplay(ReadCurrentMombotVar("0", "$GAME~mbbs", "$mbbs"))),
            ("Multiple Photons", IsMombotTruthy(ReadCurrentMombotVar("0", "$GAME~PHOTONS_ENABLED", "$PHOTONS_ENABLED")) ? FormatMombotBoolDisplay(ReadCurrentMombotVar("0", "$GAME~MULTIPLE_PHOTONS", "$MULTIPLE_PHOTONS")) : "Disabled"),
            ("Colonists Per Day", ReadCurrentMombotVar("0", "$GAME~colonist_regen", "$colonist_regen")),
            ("Planet Trade", ReadCurrentMombotVar("0", "$GAME~ptradesetting", "$ptradesetting") + "%"),
            ("Steal Factor", ReadCurrentMombotVar("0", "$GAME~STEAL_FACTOR", "$steal_factor")),
            ("Rob Factor", ReadCurrentMombotVar("0", "$GAME~rob_factor", "$rob_factor")),
            ("Days To Bust Clear", ReadCurrentMombotVar("0", "$GAME~CLEAR_BUST_DAYS", "$CLEAR_BUST_DAYS")),
            ("Port Maximum", ReadCurrentMombotVar("0", "$GAME~PORT_MAX", "$port_max")),
            ("Port Production Rate", ReadCurrentMombotVar("0", "$GAME~PRODUCTION_RATE", "$PRODUCTION_RATE") + "%"),
            ("Max Port Regen Per Day", ReadCurrentMombotVar("0", "$GAME~PRODUCTION_REGEN", "$PRODUCTION_REGEN") + "%"),
            ("Nav Haz Loss Per Day", ReadCurrentMombotVar("0", "$GAME~DEBRIS_LOSS", "$DEBRIS_LOSS") + "%"),
            ("Radiation Lifetime", ReadCurrentMombotVar("0", "$GAME~RADIATION_LIFETIME", "$RADIATION_LIFETIME")),
        };

        foreach (var item in items)
            AppendMombotPreferencesEntry(body, "-", item.Label, item.Value);

        AppendMombotPreferencesFooter(body, "[>] Hot Keys", "[<] Preferences", "Any other key exits");
    }

    private void BuildMombotHotkeyPreferencesPage(System.Text.StringBuilder body)
    {
        AppendMombotPreferencesHeader(body, "Preferences", "Hot Keys");

        IReadOnlyList<string> customKeys = LoadMombotCustomKeyConfigLines();
        IReadOnlyList<string> customCommands = LoadMombotIndexedConfig("$custom_commands_file", BuildDefaultMombotCustomCommandFileLines());
        string[] slotLabels = "1234567890ABCDEFGHIJKLMNOPRSTUVWX".Select(c => c.ToString()).ToArray();

        for (int slot = 1; slot <= 33; slot++)
        {
            string title = GetMombotHotkeySlotTitle(slot, customCommands[Math.Min(slot - 1, customCommands.Count - 1)]);
            string keyValue = slot <= customKeys.Count ? customKeys[slot - 1] : "0";
            AppendMombotPreferencesEntry(body, slotLabels[slot - 1], title, FormatMombotHotkeyDisplay(keyValue));
        }

        AppendMombotPreferencesFooter(body, "[>] Ship Info", "[<] Game Stats", "Choose a slot to rebind, any other key exits");
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

    private void PromptMombotCountPreference(string prompt, int minValue, int maxValue, params string[] names)
    {
        BeginMombotPreferencesInput(
            prompt,
            value =>
            {
                if (!int.TryParse(value.Trim(), out int count))
                    return;

                if (count < minValue || count > maxValue)
                    return;

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
            ReadCurrentMombotVar("0", names));
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
        string slotName = GetMombotHotkeySlotTitle(slot, LoadMombotIndexedConfig("$custom_commands_file", BuildDefaultMombotCustomCommandFileLines())[slot - 1]);
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

        string[] hotkeys = LoadMombotIndexedConfig("$hotkeys_file", BuildDefaultMombotHotkeyFileLines()).ToArray();
        string[] customKeys = LoadMombotCustomKeyConfigLines().ToArray();
        string[] customCommands = LoadMombotIndexedConfig("$custom_commands_file", BuildDefaultMombotCustomCommandFileLines()).ToArray();
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
                    WriteMombotHotkeyConfigFiles(hotkeys, customKeys, customCommands);
                },
                currentCommand);
            return;
        }

        WriteMombotHotkeyConfigFiles(hotkeys, customKeys, customCommands);
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

    private IReadOnlyList<string> LoadMombotCustomKeyConfigLines()
    {
        string[] merged = BuildDefaultMombotCustomKeyFileLines().ToArray();
        string filePath = ResolveMombotCurrentFilePath("$custom_keys_file");
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            return merged;

        try
        {
            string[] existing = File.ReadAllLines(filePath);
            int count = Math.Min(existing.Length, merged.Length);
            for (int i = 0; i < count; i++)
                merged[i] = existing[i];
        }
        catch
        {
            return merged;
        }

        return merged;
    }

    private void WriteMombotHotkeyConfigFiles(IReadOnlyList<string> hotkeys, IReadOnlyList<string> customKeys, IReadOnlyList<string> customCommands)
    {
        string hotkeysFile = ResolveMombotCurrentFilePath("$hotkeys_file");
        string customKeysFile = ResolveMombotCurrentFilePath("$custom_keys_file");
        string customCommandsFile = ResolveMombotCurrentFilePath("$custom_commands_file");

        foreach (string path in new[] { hotkeysFile, customKeysFile, customCommandsFile })
        {
            string? directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);
        }

        File.WriteAllLines(hotkeysFile, hotkeys);
        File.WriteAllLines(customKeysFile, customKeys);
        File.WriteAllLines(customCommandsFile, customCommands);
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

    private static void AppendMombotPreferencesHeader(System.Text.StringBuilder body, string title, string subtitle)
    {
        body.Append("\x1b[1;33mMombot ").Append(title).Append("\x1b[0m");
        if (!string.IsNullOrWhiteSpace(subtitle))
            body.Append(" - ").Append(subtitle);
        body.Append("\r\n\r\n");
    }

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
        _mombotMacroPromptOpen = false;
        _mombotMacroContext = null;

        if (context == null)
        {
            RedrawMombotPrompt();
            return;
        }

        await action(context);
        if (_mombotPromptOpen)
            RedrawMombotPrompt();
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
        if (string.Equals(promptVar, "Computer", StringComparison.OrdinalIgnoreCase))
            return MombotPromptSurface.Computer;

        string currentLine = Core.ScriptRef.GetCurrentLine().Trim();
        string currentAnsi = Core.ScriptRef.GetCurrentAnsiLine();
        if (currentLine.StartsWith("Command [TL=", StringComparison.OrdinalIgnoreCase))
            return MombotPromptSurface.Command;
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
                BeginMombotPrompt("mow ");
                return;

            case ":internal_commands~fotonswitch":
                await ExecuteMombotHotkeyCommandAsync(
                    string.Equals(Core.ScriptRef.GetCurrentGameVar("$BOT~MODE", "General"), "Foton", StringComparison.OrdinalIgnoreCase)
                        ? "foton off"
                        : "foton on p");
                return;

            case ":internal_commands~autokill":
                await ExecuteMombotHotkeyCommandAsync("kill furb silent");
                return;

            case ":internal_commands~autocap":
                await ExecuteMombotHotkeyCommandAsync("cap");
                return;

            case ":internal_commands~autorefurb":
                await ExecuteMombotHotkeyCommandAsync("refurb");
                return;

            case ":internal_commands~kit":
                await ExecuteMombotHotkeyCommandAsync("macro_kit");
                return;
        }

        string command = actionRef[(actionRef.LastIndexOf('~') + 1)..];
        if (string.Equals(command, "stopModules", StringComparison.OrdinalIgnoreCase))
            command = "stopmodules";

        await ExecuteMombotHotkeyCommandAsync(command);
    }

    private async Task ExecuteMombotHotkeyCommandAsync(string command)
    {
        ResetMombotPromptState();
        _parser.Feed("\r\x1b[K");
        _buffer.Dirty = true;
        await ExecuteMombotUiCommandAsync(command);
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

        IReadOnlyList<string> hotkeys = LoadMombotIndexedConfig(
            "$hotkeys_file",
            BuildDefaultMombotHotkeyFileLines());
        if (keyByte == 0 || keyByte > hotkeys.Count)
            return false;

        string slotValue = hotkeys[keyByte - 1].Trim();
        if (!int.TryParse(slotValue, out int slot) || slot <= 0)
            return false;

        IReadOnlyList<string> commands = LoadMombotIndexedConfig(
            "$custom_commands_file",
            BuildDefaultMombotCustomCommandFileLines());
        if (slot > commands.Count)
            return false;

        string entry = commands[slot - 1].Trim();
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

    private IReadOnlyList<string> LoadMombotIndexedConfig(string currentVarName, IReadOnlyList<string> defaultLines)
    {
        string[] merged = defaultLines.ToArray();
        string filePath = ResolveMombotCurrentFilePath(currentVarName);
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            return merged;

        try
        {
            string[] existing = File.ReadAllLines(filePath);
            int count = Math.Min(existing.Length, merged.Length);
            for (int i = 0; i < count; i++)
            {
                string trimmed = existing[i].Trim();
                if (!string.IsNullOrWhiteSpace(trimmed))
                    merged[i] = trimmed;
            }
        }
        catch
        {
            return merged;
        }

        return merged;
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
        RefreshMombotUi();
        RefreshStatusBar();
        RebuildProxyMenu();
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

        _mombot.TryExecuteLocalInput(input, out _);
        ApplyMombotExecutionRefresh();
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
            int bubbleSize = _embeddedGameConfig?.BubbleSize ?? 25;
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
            var name = Path.GetFileName(p);
            var item = new MenuItem { Header = EscapeMenuHeaderText(name) };
            ToolTip.SetTip(item, p);
            item.Click += (_, _) => _ = OpenRecentAsync(p);
            items.Add(item);
        }
        if (items.Count == 0)
            items.Add(new MenuItem { Header = "(none)", IsEnabled = false });

        _recentMenu.ItemsSource = items;
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

    /// <summary>File > Edit Connection: update the shared game config in-place.</summary>
    private async Task OnEditConnectionAsync()
    {
        string previousGameName = DeriveGameName();
        string previousConfigPath = _currentProfilePath ?? AppPaths.TwxproxyGameConfigFileFor(previousGameName);
        string previousDatabasePath = _embeddedGameConfig?.DatabasePath ?? string.Empty;
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
        AddToRecentAndSave(newConfigPath);
        await SyncEmbeddedProxySettingsAsync();

        _parser.Feed($"\x1b[1;36m[Connection settings updated]\x1b[0m\r\n");
        _buffer.Dirty = true;
    }

    private async Task SyncEmbeddedProxySettingsAsync()
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
        string previousHost = gameConfig.Host;
        int previousPort = gameConfig.Port;

        bool configChanged =
            !string.Equals(originalNativeHaggleMode ?? string.Empty, gameConfig.NativeHaggleMode ?? string.Empty, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(gameConfig.Name, gameName, StringComparison.Ordinal) ||
            gameConfig.Host != _state.Host ||
            gameConfig.Port != _state.Port ||
            gameConfig.Sectors != _state.Sectors ||
            gameConfig.UseLogin != _state.UseLogin ||
            gameConfig.UseRLogin != _state.UseRLogin ||
            !string.Equals(gameConfig.LoginScript, string.IsNullOrWhiteSpace(_state.LoginScript) ? "0_Login.cts" : _state.LoginScript, StringComparison.Ordinal) ||
            !string.Equals(gameConfig.LoginName, _state.LoginName, StringComparison.Ordinal) ||
            !string.Equals(gameConfig.Password, _state.Password, StringComparison.Ordinal) ||
            !string.Equals(gameConfig.GameLetter, _state.GameLetter, StringComparison.Ordinal);

        gameConfig.Name = gameName;
        gameConfig.Host = _state.Host;
        gameConfig.Port = _state.Port;
        gameConfig.Sectors = _state.Sectors;
        gameConfig.UseLogin = _state.UseLogin;
        gameConfig.UseRLogin = _state.UseRLogin;
        gameConfig.LoginScript = string.IsNullOrWhiteSpace(_state.LoginScript) ? "0_Login.cts" : _state.LoginScript;
        gameConfig.LoginName = _state.LoginName;
        gameConfig.Password = _state.Password;
        gameConfig.GameLetter = _state.GameLetter;

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
            headerDirty |= header.UseLogin != _state.UseLogin;
            header.UseLogin = _state.UseLogin;
            headerDirty |= header.UseRLogin != _state.UseRLogin;
            header.UseRLogin = _state.UseRLogin;
            headerDirty |= header.LoginScript != (string.IsNullOrWhiteSpace(_state.LoginScript) ? "0_Login.cts" : _state.LoginScript);
            header.LoginScript = string.IsNullOrWhiteSpace(_state.LoginScript) ? "0_Login.cts" : _state.LoginScript;
            headerDirty |= header.LoginName != _state.LoginName;
            header.LoginName = _state.LoginName;
            headerDirty |= header.Password != _state.Password;
            header.Password = _state.Password;
            char gameChar = string.IsNullOrWhiteSpace(_state.GameLetter) ? '\0' : char.ToUpperInvariant(_state.GameLetter[0]);
            headerDirty |= header.Game != gameChar;
            header.Game = gameChar;
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

        _currentProfilePath = path;
        _embeddedGameConfig = config;
        _embeddedGameName = gameName;
        ApplyProfile(BuildProfileFromConfig(config));
        AddToRecentAndSave(path);
        OnGameSelected();
        _parser.Feed($"\x1b[1;36m[Game loaded: {newProfile.Server}:{newProfile.Port}  —  use File \u25b6 Connect to connect]\x1b[0m\r\n");
        _buffer.Dirty = true;
    }

    /// <summary>File > Open: open or import a shared game JSON or a TWX database.</summary>
    private async Task OnOpenConnectionAsync()
    {
        var storage = TopLevel.GetTopLevel(this)?.StorageProvider;
        if (storage == null) return;

        var home  = await GetHomeFolderAsync(storage);
        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title                  = "Open Game",
            SuggestedStartLocation = home,
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
        _currentProfilePath = path;
        _embeddedGameConfig = config;
        _embeddedGameName = gameName;
        ApplyProfile(BuildProfileFromConfig(config));
        AddToRecentAndSave(path);
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
