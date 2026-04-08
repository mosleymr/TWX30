using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
    // ── Core components ────────────────────────────────────────────────────
    private readonly GameState       _state;
    private readonly TerminalBuffer  _buffer;
    private readonly AnsiParser      _parser;
    private readonly TelnetClient    _telnet;
    private readonly TerminalControl _termCtrl;
    private readonly DispatcherTimer _refreshTimer;    private readonly Core.ShipInfoParser _shipParser = new();
    // ── Current saved profile path (null = not yet saved) ──────────────────
    private string?         _currentProfilePath;
    private AppPreferences  _appPrefs = new();
    private Core.ModDatabase?              _sessionDb;
    private Core.GameInstance?             _gameInstance;   // non-null only in embedded proxy mode
    private Core.ExpansionModuleHost?      _moduleHost;
    private CancellationTokenSource?       _proxyCts;       // cancels the pipe-reader task
    private Task                           _pendingEmbeddedStop = Task.CompletedTask; // tracks in-flight StopEmbeddedAsync
    private readonly Core.ModLog           _sessionLog = new();
    private EmbeddedGameConfig?            _embeddedGameConfig;
    private string?                        _embeddedGameName;
    private readonly Dictionary<string, AiAssistantWindow> _assistantWindows = new(StringComparer.OrdinalIgnoreCase);
    private static readonly System.Text.Json.JsonSerializerOptions _jsonOpts = new()
    {
        WriteIndented             = true,
        PropertyNameCaseInsensitive = true,
    };
    private MenuItem        _recentMenu    = new() { Header = "_Recent" };
    private MenuItem        _proxyMenu     = new() { Header = "_Proxy" };
    private MenuItem        _scriptsMenu   = new() { Header = "_Scripts" };
    private MenuItem        _botMenu       = new() { Header = "_mbot" };
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
    private readonly MTC.mbot.mbotService _mbot = new();
    private readonly Border _shellHost = new();
    private readonly Border _statusBar = new();
    private DockPanel? _rootDock;
    private Canvas? _deckSurface;
    private readonly Dictionary<string, FloatingDeckPanel> _deckPanels = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, DeckPanelState> _deckPanelStates = new(StringComparer.OrdinalIgnoreCase);
    private int _deckNextZIndex = 100;
    private TacticalMapControl? _tacticalMap;
    private bool _useCommandDeckSkin;
    private bool _nativeAppMenuReady;
    private bool _nativeAppMenuAttached;
    private bool _nativeDockMenuAttached;
    private readonly ToggleSwitch _haggleToggle = new()
    {
        OffContent = "Off",
        OnContent = "On",
        IsEnabled = false,
        IsChecked = false,
        Margin = new Thickness(8, 0, 0, 0),
        VerticalAlignment = VerticalAlignment.Center,
    };
    private bool _updatingHaggleToggle;
    private bool _mbotPromptOpen;
    private bool _mbotMacroPromptOpen;
    private MbotGridContext? _mbotMacroContext;
    private readonly List<string> _mbotCommandHistory = [];
    private string _mbotPromptBuffer = string.Empty;
    private string _mbotPromptDraft = string.Empty;
    private int _mbotPromptHistoryIndex;
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

    // ── Status bar text ───────────────────────────────────────────────────
    private TextBlock _statusText = new();
    private TextBlock _hudHeaderSector = new();
    private TextBlock _hudHeaderConnection = new();
    private TextBlock _hudShipName = new();
    private TextBlock _hudShipSubtitle = new();
    private TextBlock _hudStarDock = new();
    private TextBlock _hudRylos = new();
    private TextBlock _hudAlpha = new();
    private TextBlock _hudUniverse = new();

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
    private static readonly IBrush HudText      = new SolidColorBrush(Color.FromRgb(222, 238, 242));
    private static readonly IBrush HudMuted     = new SolidColorBrush(Color.FromRgb(126, 170, 180));
    private static readonly IBrush HudEdge      = new SolidColorBrush(Color.FromRgb(57, 112, 128));
    private static readonly IBrush HudInnerEdge = new SolidColorBrush(Color.FromRgb(23,  81, 94));
    private static readonly IBrush HudAccent    = new SolidColorBrush(Color.FromRgb(0,   212, 201));
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
        public required double Width { get; init; }
        public required double BodyHeight { get; init; }
        public required int ZIndex { get; set; }
        public bool Closed { get; set; }
        public bool Minimized { get; set; }
    }

    // ── Constructor ────────────────────────────────────────────────────────
    public MainWindow()
    {
        Title          = "Mayhem Tradewars Client v1.0";
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
        _termCtrl = new TerminalControl(_buffer);
        _telnet   = new TelnetClient(_buffer, _parser);

        _telnet.Connected    += OnTelnetConnected;
        _telnet.Disconnected += OnTelnetDisconnected;
        _telnet.Error        += OnTelnetError;

        // Ship status: feed every server line through the parser
        _telnet.TextLineReceived += _shipParser.FeedLine;
        _shipParser.Updated      += OnShipStatusUpdated;

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

        _state.Changed += () => Dispatcher.UIThread.Post(RefreshInfoPanels);

        // Wire keyboard → telnet
        _termCtrl.SendInput = bytes =>
        {
            RouteTerminalInput(bytes, SendToTelnet);
        };

        // Load persisted preferences (recent file list etc.) before the first shell build
        // so we don't compose the visual tree twice on startup.
        _appPrefs = AppPreferences.Load();
        _useCommandDeckSkin = _appPrefs.CommandDeckSkinEnabled;

        _haggleToggle.IsCheckedChanged += (_, _) => OnHaggleToggleRequested();
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
        _refreshTimer.Tick += (_, _) => _termCtrl.RequestRedraw();
        _refreshTimer.Start();

        Opened += (_, _) =>
        {
            _nativeAppMenuReady = true;
            _nativeAppMenuAttached = false;
            _nativeDockMenuAttached = false;
            RefreshNativeAppMenu();
            RefreshNativeDockMenu();
        };
        Activated += (_, _) => _termCtrl.Focus();
        Closed    += (_, _) =>
        {
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

        ApplySelectedSkin();
        return dock;
    }

    private Control BuildClassicShell()
    {
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
        _deckPanels.Clear();
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
        _deckSurface.SizeChanged += (_, _) => ClampDeckPanelsToSurface();

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

        _deckSurface.Children.Add(panel);
        _deckPanels[panelId] = panel;

        panel.ZIndex = state.ZIndex;
        panel.MoveTo(state.Left, state.Top);
        if (state.Minimized)
            panel.SetMinimized(true);
        if (state.Closed)
            panel.SetClosed(true);
    }

    private DeckPanelState GetOrCreateDeckPanelState(string panelId)
    {
        if (_deckPanelStates.TryGetValue(panelId, out DeckPanelState? state))
            return state;

        state = CreateDefaultDeckPanelState(panelId);
        _deckPanelStates[panelId] = state;
        _deckNextZIndex = Math.Max(_deckNextZIndex, state.ZIndex + 1);
        return state;
    }

    private DeckPanelState CreateDefaultDeckPanelState(string panelId)
    {
        double surfaceWidth = Math.Max(860, (Bounds.Width > 100 ? Bounds.Width : Width) - 64);
        double surfaceHeight = Math.Max(460, (Bounds.Height > 100 ? Bounds.Height : Height) - 170);

        return panelId switch
        {
            "map" => new DeckPanelState
            {
                PanelId = panelId,
                Left = 18,
                Top = 18,
                Width = Math.Min(460, Math.Max(380, surfaceWidth * 0.40)),
                BodyHeight = Math.Min(290, Math.Max(230, surfaceHeight * 0.48)),
                ZIndex = 110,
            },
            "console" => new DeckPanelState
            {
                PanelId = panelId,
                Left = Math.Max(260, surfaceWidth * 0.36),
                Top = 18,
                Width = Math.Min(660, Math.Max(520, surfaceWidth * 0.58)),
                BodyHeight = Math.Min(330, Math.Max(250, surfaceHeight * 0.56)),
                ZIndex = 120,
            },
            "ship" => new DeckPanelState
            {
                PanelId = panelId,
                Left = 26,
                Top = Math.Max(170, surfaceHeight * 0.44),
                Width = 340,
                BodyHeight = Math.Min(290, Math.Max(230, surfaceHeight * 0.42)),
                ZIndex = 130,
            },
            "intel" => new DeckPanelState
            {
                PanelId = panelId,
                Left = Math.Max(260, surfaceWidth * 0.33),
                Top = Math.Max(210, surfaceHeight * 0.50),
                Width = Math.Min(450, Math.Max(380, surfaceWidth * 0.40)),
                BodyHeight = Math.Min(250, Math.Max(210, surfaceHeight * 0.34)),
                ZIndex = 140,
            },
            "logo" => new DeckPanelState
            {
                PanelId = panelId,
                Left = Math.Max(620, surfaceWidth - 314),
                Top = Math.Max(220, surfaceHeight * 0.54),
                Width = 280,
                BodyHeight = Math.Min(220, Math.Max(180, surfaceHeight * 0.28)),
                ZIndex = 150,
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
        if (!_deckPanelStates.TryGetValue(panel.PanelId, out DeckPanelState? state))
            return;

        (state.Left, state.Top) = panel.GetPosition();
        state.Minimized = panel.IsMinimized;
        state.Closed = panel.IsClosed;
        state.ZIndex = panel.ZIndex;
    }

    private void ShowDeckPanel(string panelId)
    {
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
        if (_useCommandDeckSkin)
            ApplySelectedSkin();
    }

    private void ClampDeckPanelsToSurface()
    {
        if (_deckSurface == null)
            return;

        foreach (FloatingDeckPanel panel in _deckPanels.Values)
        {
            if (!panel.IsVisible)
                continue;

            (double left, double top) = panel.GetPosition();
            panel.MoveTo(left, top);
        }
    }

    private Control BuildDeckBanner()
    {
        _hudHeaderSector.FontFamily = HudTitleFont;
        _hudHeaderSector.FontSize = 22;
        _hudHeaderSector.FontWeight = FontWeight.SemiBold;
        _hudHeaderSector.Foreground = HudAccentHot;
        _hudHeaderSector.Text = "SECTOR ---";

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
                BuildDeckInfoChip("Sector", _hudHeaderSector, HudAccentHot),
                BuildDeckInfoChip("Link", _hudHeaderConnection, HudAccent),
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
        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        grid.Children.Add(BuildDeckInfoChip("Mode", new TextBlock { Text = "Live overlay" }, HudAccentHot));

        var mapBorder = new Border
        {
            Margin = new Thickness(0, 12, 0, 0),
            BorderBrush = HudInnerEdge,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(14),
            ClipToBounds = true,
            Child = _tacticalMap!,
        };
        Grid.SetRow(mapBorder, 1);
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
                Child = _termCtrl,
            },
        };
        Grid.SetRow(terminalBorder, 1);
        grid.Children.Add(terminalBorder);

        return grid;
    }

    private Control BuildDeckShipPanel()
    {
        _hudShipName.FontFamily = HudTitleFont;
        _hudShipName.FontSize = 22;
        _hudShipName.FontWeight = FontWeight.Bold;
        _hudShipName.Foreground = HudAccentOk;
        _hudShipName.Text = "-";

        _hudShipSubtitle.Foreground = HudMuted;
        _hudShipSubtitle.FontSize = 12;
        _hudShipSubtitle.Text = "Independent captain";

        var badgeGrid = new Grid { Margin = new Thickness(0, 8, 0, 0) };
        badgeGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        badgeGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
        badgeGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        badgeGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
        badgeGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var fightersBadge = BuildDeckStatBadge("FTR", _valFighters, HudAccentOk);
        Grid.SetColumn(fightersBadge, 0);
        badgeGrid.Children.Add(fightersBadge);

        var shieldsBadge = BuildDeckStatBadge("SHD", _valShields, HudAccent);
        Grid.SetColumn(shieldsBadge, 2);
        badgeGrid.Children.Add(shieldsBadge);

        var holdsBadge = BuildDeckStatBadge("HLD", _valHTotal, HudAccentHot);
        Grid.SetColumn(holdsBadge, 4);
        badgeGrid.Children.Add(holdsBadge);

        var devices = new WrapPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            ItemWidth = 88,
            Margin = new Thickness(0, 6, 0, 0),
        };
        devices.Children.Add(BuildDeckDeviceChip("ETH", _valEther, HudAccent));
        devices.Children.Add(BuildDeckDeviceChip("BEA", _valBeacon, HudAccent));
        devices.Children.Add(BuildDeckDeviceChip("DIS", _valDisruptor, HudAccent));
        devices.Children.Add(BuildDeckDeviceChip("PHO", _valPhoton, HudAccentHot));
        devices.Children.Add(BuildDeckDeviceChip("ARM", _valArmid, HudAccentWarn));
        devices.Children.Add(BuildDeckDeviceChip("LIM", _valLimpet, HudAccentWarn));
        devices.Children.Add(BuildDeckDeviceChip("GEN", _valGenesis, HudAccentOk));
        devices.Children.Add(BuildDeckDeviceChip("ATO", _valAtomic, HudAccentHot));
        devices.Children.Add(BuildDeckDeviceChip("COR", _valCorbo, HudAccent));
        devices.Children.Add(BuildDeckDeviceChip("CLK", _valCloak, HudAccentOk));

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
            Children = { _hudShipName, _hudShipSubtitle },
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
                        BuildDeckMetricRow("Fuel Ore", _valFuelOre),
                        BuildDeckMetricRow("Organics", _valOrganics),
                        BuildDeckMetricRow("Equipment", _valEquipment),
                        BuildDeckMetricRow("Colonists", _valColonists),
                        BuildDeckMetricRow("Empty", _valEmpty),
                    },
                }),
                BuildDeckSection("Device Rack", devices),
                BuildDeckSection("Aux Systems", new StackPanel
                {
                    Spacing = 4,
                    Children =
                    {
                        BuildScannerRow(),
                        BuildHaggleRow(),
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
            ("Pilot", _valName),
            ("Sector", _valSector),
            ("Turns", _valTurns));
        Grid.SetRow(commander, 0);
        Grid.SetColumn(commander, 0);
        grid.Children.Add(commander);

        var economy = BuildDeckMetricCard(
            "Economy",
            ("Credits", _valCred),
            ("Experience", _valExper),
            ("Alignment", _valAlignm));
        Grid.SetRow(economy, 0);
        Grid.SetColumn(economy, 2);
        grid.Children.Add(economy);

        var routes = BuildDeckMetricCard(
            "Route Markers",
            ("StarDock", _hudStarDock),
            ("Rylos", _hudRylos),
            ("Alpha", _hudAlpha),
            ("Universe", _hudUniverse));
        Grid.SetRow(routes, 2);
        Grid.SetColumn(routes, 0);
        grid.Children.Add(routes);

        var drives = BuildDeckMetricCard(
            "Drive Core",
            ("Turns/Warp", _valTrnWarp),
            ("TW-I", _valTW1),
            ("TW-II", _valTW2));
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
    }

    private void SetSkin(bool useCommandDeckSkin)
    {
        if (_useCommandDeckSkin == useCommandDeckSkin && _shellHost.Child != null)
            return;

        _useCommandDeckSkin = useCommandDeckSkin;
        _appPrefs.CommandDeckSkinEnabled = useCommandDeckSkin;
        _appPrefs.Save();
        ApplySelectedSkin();
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
        fileNewWin.Click += (_, _) => new MainWindow().Show();

        var fileEdit = _fileEdit;
        _fileEdit.Click += (_, _) => _ = OnEditConnectionAsync();

        var fileOpen    = new MenuItem { Header = "_Open…" };
        fileOpen.Click += (_, _) => _ = OnOpenConnectionAsync();

        var fileSave    = new MenuItem { Header = "_Save" };
        fileSave.Click += (_, _) => _ = OnSaveConnectionAsync(saveAs: false);

        var fileSaveAs    = new MenuItem { Header = "Save _As…" };
        fileSaveAs.Click += (_, _) => _ = OnSaveConnectionAsync(saveAs: true);

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
                item.Click += (_, _) => _termCtrl.SetFont(fname);
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

    private static void UpdateScanInd(Border b, bool active)
    {
        b.Background = active ? ScannerActive : ScannerInactive;
        if (b.Child is TextBlock tb)
            tb.Foreground = active ? Brushes.Black : ScannerFgInact;
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
    // ── Info panel refresh ─────────────────────────────────────────────────

    private void RefreshInfoPanels()
    {
        string traderName = string.IsNullOrEmpty(_state.TraderName) ? "-" : _state.TraderName;
        _valName.Text      = traderName;
        _valSector.Text    = _state.Sector.ToString();
        _valTurns.Text     = _state.Turns.ToString();
        _valExper.Text     = _state.Experience.ToString("N0");
        int alignVal = int.TryParse(_state.Alignment, out int av) ? av : 0;
        _valAlignm.Text    = alignVal.ToString("N0");
        _valAlignm.Foreground = alignVal >= 1000
            ? new SolidColorBrush(Color.FromRgb(100, 180, 255))      // blue  (≥ 1,000)
            : alignVal < 0
                ? new SolidColorBrush(Color.FromRgb(255, 80, 80))    // red   (negative)
                : new SolidColorBrush(Color.FromRgb(255, 255, 255));  // white (0–999)
        _valCred.Text      = _state.Credits.ToString("N0");
        _valHTotal.Text    = _state.HoldsTotal.ToString();
        _valFuelOre.Text   = _state.FuelOre.ToString();
        _valOrganics.Text  = _state.Organics.ToString();
        _valEquipment.Text = _state.Equipment.ToString();
        _valColonists.Text = _state.Colonists.ToString();
        _valEmpty.Text     = _state.HoldsEmpty.ToString();
        _valFighters.Text  = _state.Fighters.ToString("N0");
        _valShields.Text   = _state.Shields.ToString("N0");
        _valTrnWarp.Text   = _state.TurnsPerWarp.ToString();
        _valEther.Text     = _state.Etheral.ToString();
        _valBeacon.Text    = _state.Beacon.ToString();
        _valDisruptor.Text = _state.Disruptor.ToString();
        _valPhoton.Text    = _state.Photon.ToString();
        _valArmid.Text     = _state.Armor.ToString();
        _valLimpet.Text    = _state.Limpet.ToString();
        _valGenesis.Text   = _state.Genesis.ToString();
        _valAtomic.Text    = _state.Atomic.ToString();
        _valCorbo.Text     = _state.Corbomite.ToString();
        _valCloak.Text     = _state.Cloak.ToString();
        _valTW1.Text       = _state.TranswarpDrive1 > 0 ? _state.TranswarpDrive1.ToString() : "-";
        _valTW2.Text       = _state.TranswarpDrive2 > 0 ? _state.TranswarpDrive2.ToString() : "-";
        UpdateScanInd(_scanIndD, _state.ScannerD);
        UpdateScanInd(_scanIndH, _state.ScannerH);
        UpdateScanInd(_scanIndP, _state.ScannerP);

        _hudHeaderSector.Text = _state.Sector > 0 ? _state.Sector.ToString("N0") : "---";
        _hudShipName.Text = string.IsNullOrWhiteSpace(_state.ShipName) || _state.ShipName == "-"
            ? "Unassigned Hull"
            : _state.ShipName;
        string captainText = traderName == "-" ? "Independent captain" : $"Capt. {traderName}";
        string corpText = _state.Corp > 0 ? $"Corp {_state.Corp}" : "Free trader";
        if (!string.IsNullOrWhiteSpace(_state.GameName))
            _hudShipSubtitle.Text = $"{captainText}  //  {corpText}  //  {_state.GameName}";
        else
            _hudShipSubtitle.Text = $"{captainText}  //  {corpText}";

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
        bool showMbot = _embeddedGameConfig?.Mtc?.mbot != null || _mbot.IsAttached;
        MTC.mbot.mbotStatusSnapshot mbotSnapshot = _mbot.GetStatusSnapshot();
        string mbotText = showMbot
            ? $"  mbot: {(mbotSnapshot.Enabled ? "on" : "off")}"
            : string.Empty;

        _statusText.Text =
            $" SD: {starDock,-6}  Rylos: {rylos,-6}  Alpha: {alpha,-6}{haggleText}{mbotText}  {conn}";

        _hudHeaderConnection.Text = _state.Connected
            ? $"{_state.Host}:{_state.Port}"
            : "OFFLINE";
        _hudHeaderConnection.Foreground = _state.Connected ? HudAccentOk : HudAccentWarn;
        _hudStarDock.Text = starDock;
        _hudRylos.Text = rylos;
        _hudAlpha.Text = alpha;
        int universeCount = _sessionDb?.DBHeader.Sectors > 0
            ? _sessionDb.DBHeader.Sectors
            : _state.Sectors;
        _hudUniverse.Text = universeCount > 0 ? universeCount.ToString("N0") : "-";
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
            _termCtrl.IsConnected = true;
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
            _termCtrl.IsConnected = false;
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
        RefreshMbotUi();
        RebuildProxyMenu();
    }

    /// <summary>Call when TCP connection is lost / disconnected.</summary>
    private void OnGameDisconnected()
    {
        _fileConnect.IsEnabled    = true;
        _fileDisconnect.IsEnabled = false;
        UpdateHaggleToggleState();
        RefreshMbotUi();
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
        Dispatcher.UIThread.Post(() => _termCtrl.Focus(), DispatcherPriority.Input);
    }

    private void UpdateHaggleToggleState()
    {
        bool proxyActive = _gameInstance != null;
        _haggleToggle.IsEnabled = proxyActive;
        if (!proxyActive)
        {
            _updatingHaggleToggle = true;
            _haggleToggle.IsChecked = false;
            _updatingHaggleToggle = false;
        }
    }

    private void ApplyMbotConfigChange(Action<MTC.mbot.mbotConfig> update)
    {
        _embeddedGameConfig ??= new EmbeddedGameConfig();
        _embeddedGameConfig.Mtc ??= new EmbeddedMtcConfig();
        _embeddedGameConfig.Mtc.mbot ??= new MTC.mbot.mbotConfig();

        update(_embeddedGameConfig.Mtc.mbot);
        _embeddedGameConfig.Mtc.mbot.WatcherEnabled = _embeddedGameConfig.Mtc.mbot.Enabled;
        _mbot.ApplyConfig(_embeddedGameConfig.Mtc.mbot);
        RefreshStatusBar();
        RebuildProxyMenu();
        _ = SaveCurrentGameConfigAsync();
    }

    private void RefreshMbotUi()
    {
        if (_mbot.Enabled)
            return;

        if (_mbotPromptOpen)
            CancelMbotPrompt();
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
            _updatingHaggleToggle = false;
            UpdateHaggleToggleState();
            RefreshMbotUi();
            RefreshStatusBar();
            _buffer.Dirty = true;
        });
    }

    private void OnNativeHaggleStatsChanged()
    {
        Dispatcher.UIThread.Post(() =>
        {
            RefreshMbotUi();
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
        string scriptDirectory = GetEffectiveProxyScriptDirectory();
        string programDir = GetEffectiveProxyProgramDir(scriptDirectory);

        return Core.NativeHaggleModeDiscovery.DiscoverFromDirectories(new[]
        {
            AppPaths.ModulesDir,
            AppPaths.SharedModulesDir,
            Path.Combine(programDir, "modules"),
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

        string programDir = AppPaths.GetEffectiveProgramDir(debugScriptDirectory);
        Core.GlobalModules.ProgramDir = programDir;
        Core.GlobalModules.PreferPreparedVm = _appPrefs.PreparedVmEnabled;
        Core.GlobalModules.EnableVmMetrics = _appPrefs.VmMetricsEnabled;
        AppPaths.EnsureDebugLogDir(debugScriptDirectory);
        Core.GlobalModules.ConfigureDebugLogging(
            AppPaths.GetDebugLogPath(debugScriptDirectory),
            _appPrefs.DebugLoggingEnabled,
            _appPrefs.VerboseDebugLogging);
        RefreshSessionLogTarget(debugScriptDirectory);
        if (_gameInstance != null)
            _gameInstance.Logger.LogDirectory = AppPaths.GetDebugLogDir(debugScriptDirectory);
    }

    private void RefreshSessionLogTarget(string? scriptDirectory = null)
    {
        string programDir = AppPaths.GetEffectiveProgramDir(scriptDirectory);
        _sessionLog.ProgramDir = programDir;
        _sessionLog.LogDirectory = AppPaths.GetDebugLogDir(scriptDirectory);
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
        config.Variables ??= new Dictionary<string, string>();
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
        config.Variables ??= new Dictionary<string, string>();
        return config;
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
        _mbot.ApplyConfig(_embeddedGameConfig?.Mtc?.mbot);
        RefreshStatusBar();
        _state.NotifyChanged();
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
        ApplySessionLogSettings(gameConfig);

        // Open / create the session database using sectors from the game config.
        OpenSessionDatabase(gameName, gameConfig.Sectors, useSharedProxyDatabase: true);

        // Resolve the effective script directory from the MTC-wide preference first,
        // then fall back to older per-game state only when no app-level setting exists.
        string effectiveScriptDir = ResolveEffectiveScriptDirectory(gameConfig.ScriptDirectory);

        // Create the script interpreter.
        // ProgramDir = parent of the scripts folder (matches ProxyService behaviour).
        string programDir = Path.GetDirectoryName(effectiveScriptDir) ?? effectiveScriptDir;
        var interpreter = new Core.ModInterpreter();
        interpreter.ScriptDirectory = effectiveScriptDir;
        interpreter.ProgramDir      = programDir;
        Core.GlobalModules.ProgramDir = programDir;  // shared global used by some script commands
        ApplyDebugLoggingPreferences();

        // Embedded mode needs a live menu manager so OPENMENU pauses and displays
        // configuration menus (same behavior as TWXP ProxyService startup).
        Core.GlobalModules.TWXMenu = new Core.MenuManager();

        // Load previously saved variables (excluding session-startup flags).
        var varsToLoad = new System.Collections.Generic.Dictionary<string, string>(gameConfig.Variables);
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
        _termCtrl.SendInput = bytes =>
        {
            RouteTerminalInput(bytes, data =>
            {
                try { termWriter.Write(data, 0, data.Length); termWriter.Flush(); }
                catch { }
            });
        };

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
                        if (_mbotPromptOpen)
                            RedrawMbotPrompt();
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
                string lineForScript = Core.AnsiCodes.PrepareScriptText(buffered[lastProcessedPos..crPos]);
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
                if (!string.IsNullOrWhiteSpace(lineStripped) && _mbot.ObserveServerLine(lineStripped))
                {
                    Dispatcher.UIThread.Post(() =>
                    {
                        RefreshMbotUi();
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
        gameConfig.Mtc.mbot.Enabled = false;
        gameConfig.Mtc.mbot.AutoStart = false;
        _mbot.AttachSession(gi, _sessionDb, interpreter, gameConfig.Mtc.mbot);
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
                AppPaths.SharedModulesDir,
                Path.Combine(programDir, "modules"),
            },
            GameInstance = gi,
            Interpreter = interpreter,
            Database = _sessionDb,
        });

        // The proxy is now running. Scripts can execute and communicate with the user
        // before any server connection is made. The server connection is triggered by
        // the $c command (typed by the user or called from a script via the connect command).
        _termCtrl.IsConnected = true;
        OnGameDisconnected();   // proxy is live, but the game server is not connected yet
        _parser.Feed($"\x1b[1;32m[Embedded proxy ready — type \x1b[1;33m$c\x1b[1;32m to connect to {_state.Host}:{_state.Port}, or start a script]\x1b[0m\r\n");
        _buffer.Dirty = true;
    }

    /// <summary>Stops the embedded <see cref="Core.GameInstance"/> and restores normal state.
    /// Must be awaited (not fire-and-forget) from DoConnectEmbeddedAsync to avoid races.</summary>
    private async Task StopEmbeddedAsync()
    {
        _proxyCts?.Cancel();
        _proxyCts = null;

        var gi = _gameInstance;
        _gameInstance = null;
        _mbot.DetachSession();
        var moduleHost = _moduleHost;
        _moduleHost = null;
        if (gi != null)
            gi.NativeHaggleChanged -= OnNativeHaggleChanged;
        if (gi != null)
            gi.NativeHaggleStatsChanged -= OnNativeHaggleStatsChanged;
        if (gi != null)
            gi.ShipStatusUpdated -= OnShipStatusUpdated;
        if (gi != null)
            await gi.StopAsync();  // no ConfigureAwait(false) — continuation returns to UI thread
        if (moduleHost != null)
            await moduleHost.DisposeAsync();

        Core.ScriptRef.SetActiveGameInstance(null);
        Core.ScriptRef.OnVariableSaved = null;  // detach savevar persistence for this game
        _embeddedGameConfig = null;
        _embeddedGameName = null;

        try { _sessionDb?.CloseDatabase(); } catch { }
        _sessionDb = null;
        Core.ScriptRef.SetActiveDatabase(null);

        // Restore default keyboard → telnet wiring (runs on UI thread, no Dispatcher.Post needed).
        _termCtrl.SendInput = bytes =>
        {
            RouteTerminalInput(bytes, SendToTelnet);
        };

        _state.Connected      = false;
        _termCtrl.IsConnected = false;
        OnGameDisconnected();
        _parser.Feed("\x1b[1;31m[Embedded proxy stopped]\x1b[0m\r\n");
        RefreshStatusBar();
        UpdateHaggleToggleState();
        _buffer.Dirty = true;
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
            importedConfig.Variables = config.Variables ?? new Dictionary<string, string>();
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

        var mbotMenu = new MenuItem { Header = "_mbot", IsEnabled = _embeddedGameConfig?.Mtc?.mbot != null || _gameInstance != null };
        mbotMenu.ItemsSource = BuildMbotMenuItems();
        items.Add(mbotMenu);
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

    private List<object> BuildMbotMenuItems()
    {
        var items = new List<object>();
        MTC.mbot.mbotConfig? config = _embeddedGameConfig?.Mtc?.mbot;
        bool proxyActive = _gameInstance != null;
        MTC.mbot.mbotStatusSnapshot snapshot = _mbot.GetStatusSnapshot();

        if (config == null && !proxyActive)
        {
            items.Add(new MenuItem { Header = "No embedded game loaded", IsEnabled = false });
            return items;
        }

        config ??= snapshot.IsAttached ? _mbot.Config : new MTC.mbot.mbotConfig();

        items.Add(new MenuItem
        {
            Header = $"Status: {(snapshot.Enabled ? "Running" : "Stopped")} / mode {snapshot.Mode}",
            IsEnabled = false,
        });

        var start = new MenuItem { Header = "_Start", IsEnabled = proxyActive && !snapshot.Enabled };
        start.Click += (_, _) => _ = StartInternalMbotAsync();
        items.Add(start);

        var stop = new MenuItem { Header = "S_top", IsEnabled = proxyActive && snapshot.Enabled };
        stop.Click += (_, _) => _ = StopInternalMbotAsync();
        items.Add(stop);
        items.Add(new Separator());

        var runCommand = new MenuItem { Header = "Open _Prompt", IsEnabled = proxyActive && snapshot.Enabled };
        runCommand.Click += (_, _) => _ = ShowMbotCommandPromptAsync();
        items.Add(runCommand);

        var showStatus = new MenuItem { Header = "Show _Status", IsEnabled = proxyActive };
        showStatus.Click += (_, _) => _ = ExecuteMbotUiCommandAsync("bot");
        items.Add(showStatus);

        var refresh = new MenuItem { Header = "_Refresh Context", IsEnabled = proxyActive && snapshot.Enabled };
        refresh.Click += (_, _) => _ = ExecuteMbotUiCommandAsync("refresh");
        items.Add(refresh);

        var listAll = new MenuItem { Header = "_List Active Scripts", IsEnabled = proxyActive && snapshot.Enabled };
        listAll.Click += (_, _) => _ = ExecuteMbotUiCommandAsync("listall");
        items.Add(listAll);

        var stopCurrent = new MenuItem { Header = "S_top Current Module", IsEnabled = proxyActive && snapshot.Enabled };
        stopCurrent.Click += (_, _) => _ = ExecuteMbotUiCommandAsync("stop");
        items.Add(stopCurrent);

        var stopAll = new MenuItem { Header = "Stop _All Bot Scripts", IsEnabled = proxyActive && snapshot.Enabled };
        stopAll.Click += (_, _) => _ = ExecuteMbotUiCommandAsync("stopall");
        items.Add(stopAll);

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

        return items;
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
        return BuildMbotMenuItems();
    }

    private List<object> BuildBotMenuItems(bool enabled, bool includeStatusMessage = true)
    {
        var items = new List<object>();
        if (!enabled || _gameInstance == null)
        {
            if (includeStatusMessage)
                items.Add(new MenuItem { Header = "Proxy is not running", IsEnabled = false });
            return items;
        }

        var bots = _gameInstance.GetBotList();
        if (bots.Count == 0)
        {
            if (includeStatusMessage)
                items.Add(new MenuItem { Header = "No bots configured", IsEnabled = false });
            return items;
        }

        foreach (string botName in bots)
        {
            Core.BotConfig? botConfig = _gameInstance.GetBotConfig(botName);
            string caption = botName;
            if (_gameInstance.ActiveBotName.Equals(botName, StringComparison.OrdinalIgnoreCase))
                caption += " (active)";

            var item = new MenuItem
            {
                Header = caption,
            };
            item.Click += (_, _) => _ = SwitchBotAsync(botName);
            items.Add(item);
        }

        return items;
    }

    private async Task StartInternalMbotAsync()
    {
        await Task.Yield();

        if (_gameInstance == null)
        {
            PublishMbotLocalMessage("mbot controls are only available while the embedded proxy is running.");
            return;
        }

        if (_gameInstance.IsConnected)
        {
            SeedMbotRelogVarsFromCurrentState();
            ApplyMbotConfigChange(config => config.Enabled = true);
            LoadMbotStartupScripts();
            ShowMbotStartupBanner(connected: true);
            await SendMbotStartupAnnouncementsAsync();
            ApplyMbotExecutionRefresh();
        }
        else
        {
            var dialog = new MTC.mbot.mbotRelogDialog(BuildMbotRelogDefaults());
            if (!await dialog.ShowDialog<bool>(this) || dialog.Result == null)
            {
                _termCtrl.Focus();
                return;
            }

            ApplyMbotRelogDialogResult(dialog.Result);
            ApplyMbotConfigChange(config => config.Enabled = true);
            ShowMbotStartupBanner(connected: false);
            await ExecuteMbotUiCommandAsync("relog");
        }

        _termCtrl.Focus();
    }

    private async Task StopInternalMbotAsync()
    {
        await Task.Yield();

        if (_gameInstance == null)
        {
            PublishMbotLocalMessage("mbot controls are only available while the embedded proxy is running.");
            return;
        }

        CancelMbotPrompt();
        string scriptRoot = (_mbot.Config.ScriptRoot ?? string.Empty)
            .Replace('\\', '/')
            .Trim()
            .Trim('/');
        string lastLoadedModule = Core.ScriptRef.GetCurrentGameVar("$BOT~LAST_LOADED_MODULE", string.Empty);
        foreach (var script in _mbot.GetRunningScripts())
        {
            if (script.IsSystemScript)
                continue;

            string name = script.Name ?? string.Empty;
            string normalizedName = name.Replace('\\', '/');
            bool underMbotRoot = !string.IsNullOrWhiteSpace(scriptRoot) &&
                                 (normalizedName.StartsWith(scriptRoot + "/", StringComparison.OrdinalIgnoreCase) ||
                                  normalizedName.Contains("/" + scriptRoot + "/", StringComparison.OrdinalIgnoreCase));
            bool isLastLoaded = !string.IsNullOrWhiteSpace(lastLoadedModule) &&
                                string.Equals(name, lastLoadedModule, StringComparison.OrdinalIgnoreCase);

            if (underMbotRoot || isLastLoaded)
                _mbot.StopScriptByName(name);
        }

        ApplyMbotConfigChange(config => config.Enabled = false);
        Core.ScriptRef.SetCurrentGameVar("$doRelog", "0");
        Core.ScriptRef.SetCurrentGameVar("$BOT~DORELOG", "0");
        Core.ScriptRef.SetCurrentGameVar("$relogging", "0");
        Core.ScriptRef.SetCurrentGameVar("$connectivity~relogging", "0");
        Core.ScriptRef.SetCurrentGameVar("$relog_message", string.Empty);
        Core.ScriptRef.SetCurrentGameVar("$BOT~LAST_LOADED_MODULE", string.Empty);
        Core.ScriptRef.SetCurrentGameVar("$BOT~MODE", "General");
        PublishMbotLocalMessage("mbot stopped.");
        ApplyMbotExecutionRefresh();
    }

    private MTC.mbot.mbotRelogDialogResult BuildMbotRelogDefaults()
    {
        string stateLogin = NormalizeMbotValue(_state.LoginName, treatSelfAsEmpty: true);
        string botName = FirstMeaningfulMbotValue(
            Core.ScriptRef.GetCurrentGameVar("$BOT~BOT_NAME", string.Empty),
            Core.ScriptRef.GetCurrentGameVar("$SWITCHBOARD~BOT_NAME", string.Empty),
            Core.ScriptRef.GetCurrentGameVar("$bot_name", string.Empty),
            _mbot.Settings.BotName,
            "mbot");
        string serverName = FirstMeaningfulMbotValue(
            Core.ScriptRef.GetCurrentGameVar("$BOT~SERVERNAME", string.Empty),
            Core.ScriptRef.GetCurrentGameVar("$servername", string.Empty),
            stateLogin);
        string loginName = FirstMeaningfulMbotValue(
            Core.ScriptRef.GetCurrentGameVar("$BOT~USERNAME", string.Empty),
            Core.ScriptRef.GetCurrentGameVar("$username", string.Empty),
            stateLogin);
        string password = FirstMeaningfulMbotValue(
            Core.ScriptRef.GetCurrentGameVar("$BOT~PASSWORD", string.Empty),
            Core.ScriptRef.GetCurrentGameVar("$password", string.Empty),
            _state.Password);
        string gameLetter = FirstMeaningfulMbotValue(
            Core.ScriptRef.GetCurrentGameVar("$BOT~LETTER", string.Empty),
            Core.ScriptRef.GetCurrentGameVar("$letter", string.Empty),
            _state.GameLetter);
        string delayValue = FirstMeaningfulMbotValue(
            Core.ScriptRef.GetCurrentGameVar("$BOT~STARTGAMEDELAY", string.Empty),
            Core.ScriptRef.GetCurrentGameVar("$startGameDelay", string.Empty),
            "0");
        int delayMinutes = int.TryParse(delayValue, out int parsedDelay) && parsedDelay >= 0 ? parsedDelay : 0;
        string botCommand = NormalizeMbotValue(Core.ScriptRef.GetCurrentGameVar("$command_to_issue", string.Empty));
        string startMacro = FirstMeaningfulMbotValue(
            Core.ScriptRef.GetCurrentGameVar("$BOT~STARTMACRO", string.Empty),
            Core.ScriptRef.GetCurrentGameVar("$bot~startMacro", string.Empty),
            Core.ScriptRef.GetCurrentGameVar("$startMacro", string.Empty));

        bool newGameDay1 = string.Equals(Core.ScriptRef.GetCurrentGameVar("$BOT~NEWGAMEDAY1", "0"), "1", StringComparison.OrdinalIgnoreCase) ||
                           string.Equals(Core.ScriptRef.GetCurrentGameVar("$BOT~NEWGAMEDAY1", "false"), "true", StringComparison.OrdinalIgnoreCase);
        bool newGameOlder = string.Equals(Core.ScriptRef.GetCurrentGameVar("$BOT~NEWGAMEOLDER", "0"), "1", StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(Core.ScriptRef.GetCurrentGameVar("$BOT~NEWGAMEOLDER", "false"), "true", StringComparison.OrdinalIgnoreCase);

        MTC.mbot.mbotRelogLoginType loginType = newGameDay1
            ? MTC.mbot.mbotRelogLoginType.NewGameAccountCreation
            : newGameOlder
                ? MTC.mbot.mbotRelogLoginType.NormalRelog
                : MTC.mbot.mbotRelogLoginType.ReturnAfterDestroyed;

        return new MTC.mbot.mbotRelogDialogResult(
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

    private void ApplyMbotRelogDialogResult(MTC.mbot.mbotRelogDialogResult result)
    {
        SetMbotCurrentVars(result.BotName, "$BOT~BOT_NAME", "$SWITCHBOARD~BOT_NAME", "$bot_name");
        SetMbotCurrentVars(
            FirstMeaningfulMbotValue(
                Core.ScriptRef.GetCurrentGameVar("$BOT~BOT_TEAM_NAME", string.Empty),
                Core.ScriptRef.GetCurrentGameVar("$bot_team_name", string.Empty),
                result.BotName),
            "$BOT~BOT_TEAM_NAME",
            "$bot_team_name");
        SetMbotCurrentVars(result.ServerName, "$BOT~SERVERNAME", "$servername");
        SetMbotCurrentVars(result.LoginName, "$BOT~USERNAME", "$username");
        SetMbotCurrentVars(result.Password, "$BOT~PASSWORD", "$password");
        SetMbotCurrentVars(NormalizeGameLetter(result.GameLetter), "$BOT~LETTER", "$letter");
        SetMbotCurrentVars(result.DelayMinutes.ToString(), "$BOT~STARTGAMEDELAY", "$startGameDelay");
        SetMbotCurrentVars(result.BotCommand, "$command_to_issue");
        SetMbotCurrentVars(result.MacroAfterLogin, "$BOT~STARTMACRO", "$bot~startMacro", "$startMacro");
        SetMbotCurrentVars("General", "$BOT~MODE", "$mode");
        SetMbotCurrentVars(string.Empty, "$BOT~LAST_LOADED_MODULE", "$LAST_LOADED_MODULE");
        SetMbotCurrentVars("1", "$BOT~DORELOG", "$doRelog");

        switch (result.LoginType)
        {
            case MTC.mbot.mbotRelogLoginType.NewGameAccountCreation:
                SetMbotCurrentVars("1", "$BOT~NEWGAMEDAY1", "$newGameDay1");
                SetMbotCurrentVars("0", "$BOT~NEWGAMEOLDER", "$newGameOlder");
                SetMbotCurrentVars("0", "$BOT~ISSHIPDESTROYED");
                break;
            case MTC.mbot.mbotRelogLoginType.ReturnAfterDestroyed:
                SetMbotCurrentVars("0", "$BOT~NEWGAMEDAY1", "$newGameDay1");
                SetMbotCurrentVars("0", "$BOT~NEWGAMEOLDER", "$newGameOlder");
                SetMbotCurrentVars("1", "$BOT~ISSHIPDESTROYED");
                break;
            default:
                SetMbotCurrentVars("0", "$BOT~NEWGAMEDAY1", "$newGameDay1");
                SetMbotCurrentVars("1", "$BOT~NEWGAMEOLDER", "$newGameOlder");
                SetMbotCurrentVars("0", "$BOT~ISSHIPDESTROYED");
                break;
        }

        string relogMessage = $"{result.BotName} connected and ready.*";
        SetMbotCurrentVars(relogMessage, "$relog_message");
    }

    private void SeedMbotRelogVarsFromCurrentState()
    {
        string stateLogin = NormalizeMbotValue(_state.LoginName, treatSelfAsEmpty: true);
        string botName = FirstMeaningfulMbotValue(
            Core.ScriptRef.GetCurrentGameVar("$BOT~BOT_NAME", string.Empty),
            Core.ScriptRef.GetCurrentGameVar("$SWITCHBOARD~BOT_NAME", string.Empty),
            Core.ScriptRef.GetCurrentGameVar("$bot_name", string.Empty),
            _mbot.Settings.BotName,
            "mbot");
        string serverName = FirstMeaningfulMbotValue(
            Core.ScriptRef.GetCurrentGameVar("$BOT~SERVERNAME", string.Empty),
            Core.ScriptRef.GetCurrentGameVar("$servername", string.Empty),
            stateLogin);
        string loginName = FirstMeaningfulMbotValue(
            Core.ScriptRef.GetCurrentGameVar("$BOT~USERNAME", string.Empty),
            Core.ScriptRef.GetCurrentGameVar("$username", string.Empty),
            stateLogin);
        string password = FirstMeaningfulMbotValue(
            Core.ScriptRef.GetCurrentGameVar("$BOT~PASSWORD", string.Empty),
            Core.ScriptRef.GetCurrentGameVar("$password", string.Empty),
            _state.Password);
        string gameLetter = FirstMeaningfulMbotValue(
            Core.ScriptRef.GetCurrentGameVar("$BOT~LETTER", string.Empty),
            Core.ScriptRef.GetCurrentGameVar("$letter", string.Empty),
            _state.GameLetter);

        SetMbotCurrentVars(botName, "$BOT~BOT_NAME", "$SWITCHBOARD~BOT_NAME", "$bot_name");
        SetMbotCurrentVars(serverName, "$BOT~SERVERNAME", "$servername");
        SetMbotCurrentVars(loginName, "$BOT~USERNAME", "$username");
        SetMbotCurrentVars(password, "$BOT~PASSWORD", "$password");
        SetMbotCurrentVars(NormalizeGameLetter(gameLetter), "$BOT~LETTER", "$letter");
        SetMbotCurrentVars("1", "$BOT~DORELOG", "$doRelog");
        SetMbotCurrentVars("1", "$BOT~NEWGAMEOLDER", "$newGameOlder");
        SetMbotCurrentVars("0", "$BOT~NEWGAMEDAY1", "$newGameDay1");
        SetMbotCurrentVars("0", "$BOT~ISSHIPDESTROYED");
        SetMbotCurrentVars("General", "$BOT~MODE", "$mode");
        SetMbotCurrentVars(string.Empty, "$BOT~LAST_LOADED_MODULE", "$LAST_LOADED_MODULE");
    }

    private void LoadMbotStartupScripts()
    {
        foreach (string startupScript in _mbot.GetStartupScriptReferences())
        {
            _mbot.StopScriptByName(startupScript);
            if (!_mbot.TryLoadScript(startupScript, out string? error))
                PublishMbotLocalMessage($"mbot: failed to load startup '{startupScript}': {error}");
        }
    }

    private void ShowMbotStartupBanner(bool connected)
    {
        string botName = FirstMeaningfulMbotValue(
            Core.ScriptRef.GetCurrentGameVar("$BOT~BOT_NAME", string.Empty),
            Core.ScriptRef.GetCurrentGameVar("$SWITCHBOARD~BOT_NAME", string.Empty),
            Core.ScriptRef.GetCurrentGameVar("$bot_name", string.Empty),
            _mbot.Settings.BotName,
            "mbot");
        string stateLabel = connected ? "online" : "relog armed";
        string banner =
            "\r\n" +
            "\u001b[1;36m=== mbot 1.0 ===\u001b[0m\r\n" +
            $"\u001b[1;33m{botName}\u001b[0m \u001b[1;32m{stateLabel}\u001b[0m\r\n" +
            "\u001b[38;5;245mUse > to open the mbot prompt.\u001b[0m\r\n";

        if (_gameInstance != null)
            _gameInstance.ClientMessage(banner);
        else
            _parser.Feed(banner);

        _buffer.Dirty = true;
    }

    private async Task SendMbotStartupAnnouncementsAsync()
    {
        if (_gameInstance == null || !_gameInstance.IsConnected)
            return;

        string botName = FirstMeaningfulMbotValue(
            Core.ScriptRef.GetCurrentGameVar("$BOT~BOT_NAME", string.Empty),
            Core.ScriptRef.GetCurrentGameVar("$SWITCHBOARD~BOT_NAME", string.Empty),
            Core.ScriptRef.GetCurrentGameVar("$bot_name", string.Empty),
            _mbot.Settings.BotName,
            "mbot");
        string loginName = FirstMeaningfulMbotValue(
            Core.ScriptRef.GetCurrentGameVar("$BOT~USERNAME", string.Empty),
            Core.ScriptRef.GetCurrentGameVar("$username", string.Empty));
        string gameLetter = FirstMeaningfulMbotValue(
            Core.ScriptRef.GetCurrentGameVar("$BOT~LETTER", string.Empty),
            Core.ScriptRef.GetCurrentGameVar("$letter", string.Empty));
        string dorelog = FirstMeaningfulMbotValue(
            Core.ScriptRef.GetCurrentGameVar("$BOT~DORELOG", string.Empty),
            Core.ScriptRef.GetCurrentGameVar("$doRelog", string.Empty),
            "0");

        await _gameInstance.SendToServerAsync(System.Text.Encoding.ASCII.GetBytes(
            $"'{{{botName}}} - is ACTIVE: Version - 1.0 - type \"{botName} help\" for command list*"));
        await _gameInstance.SendToServerAsync(System.Text.Encoding.ASCII.GetBytes(
            $"'{{{botName}}} - to login - send a corporate memo*"));

        if (string.IsNullOrWhiteSpace(loginName) ||
            string.IsNullOrWhiteSpace(gameLetter) ||
            !string.Equals(dorelog, "1", StringComparison.OrdinalIgnoreCase))
        {
            await _gameInstance.SendToServerAsync(System.Text.Encoding.ASCII.GetBytes(
                $"'{{{botName}}} - Auto Relog - Not Active*"));
        }
    }

    private static void SetMbotCurrentVars(string value, params string[] names)
    {
        foreach (string name in names)
            Core.ScriptRef.SetCurrentGameVar(name, value);
    }

    private static string FirstMeaningfulMbotValue(params string?[] candidates)
    {
        foreach (string? candidate in candidates)
        {
            string normalized = NormalizeMbotValue(candidate, treatSelfAsEmpty: true);
            if (!string.IsNullOrEmpty(normalized))
                return normalized;
        }

        return string.Empty;
    }

    private static string NormalizeMbotValue(string? value, bool treatSelfAsEmpty = false)
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
        string normalized = NormalizeMbotValue(value);
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

        if (OperatingSystem.IsWindows())
            return Core.WindowsInstallInfo.GetDefaultScriptsDirectory();

        return Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
    }

    private static string GetEffectiveProxyProgramDir(string scriptDirectory)
    {
        string trimmed = scriptDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return Path.GetDirectoryName(trimmed) ?? trimmed;
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
        _termCtrl.Focus();
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
        _termCtrl.Focus();
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
        _termCtrl.Focus();
    }

    private enum MbotPromptSurface
    {
        Unknown,
        Command,
        Citadel,
        Computer,
    }

    private sealed record MbotGridContext(
        MbotPromptSurface Surface,
        int CurrentSector,
        IReadOnlyList<int> AdjacentSectors,
        int PlanetNumber,
        bool Connected,
        int PhotonCount);

    private void SendToTelnet(byte[] bytes)
    {
        if (_telnet.IsConnected)
            _telnet.SendRaw(bytes);
        else
            _parser.Feed("\x1b[33m[not connected]\x1b[0m\r\n");
    }

    private void RouteTerminalInput(byte[] bytes, Action<byte[]> forward)
    {
        if (TryHandleMbotPromptInput(bytes))
            return;

        if (TryInterceptMbotCommandPrompt(bytes))
            return;

        forward(bytes);
    }

    private bool TryHandleMbotPromptInput(byte[] bytes)
    {
        if (!_mbotPromptOpen)
            return false;

        if (_mbotMacroPromptOpen)
            return TryHandleMbotMacroPromptInput(bytes);

        if (bytes.Length == 0)
            return true;

        if (MatchesMbotPromptSequence(bytes, 'A'))
        {
            RecallMbotPromptHistory(-1);
            return true;
        }

        if (MatchesMbotPromptSequence(bytes, 'B'))
        {
            RecallMbotPromptHistory(1);
            return true;
        }

        if (bytes.Length == 1 && bytes[0] == 0x1B)
        {
            CancelMbotPrompt();
            return true;
        }

        bool changed = false;
        foreach (byte value in bytes)
        {
            switch (value)
            {
                case 0x08:
                case 0x7F:
                    if (_mbotPromptBuffer.Length > 0)
                    {
                        _mbotPromptBuffer = _mbotPromptBuffer[..^1];
                        changed = true;
                    }
                    break;

                case 0x0D:
                case 0x0A:
                    SubmitMbotPrompt();
                    return true;

                case 0x09:
                    BeginMbotMacroPrompt();
                    return true;

                default:
                    if (value >= 0x20)
                    {
                        _mbotPromptBuffer += (char)value;
                        changed = true;
                    }
                    break;
            }
        }

        if (changed)
        {
            _mbotPromptHistoryIndex = _mbotCommandHistory.Count;
            _mbotPromptDraft = _mbotPromptBuffer;
            RedrawMbotPrompt();
        }

        return true;
    }

    private bool TryHandleMbotMacroPromptInput(byte[] bytes)
    {
        if (!_mbotMacroPromptOpen)
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
                    EndMbotMacroPrompt();
                    return true;

                case 0x09:
                    _ = ExecuteMbotMacroActionAsync(_ => ExecuteMbotUiCommandAsync("stopmodules"));
                    return true;

                case (byte)'?':
                    PublishMbotLocalMessage(BuildMbotMacroHelpLine());
                    return true;

                default:
                    if (TryHandleMbotMacroKey(value))
                        return true;
                    break;
            }
        }

        PublishMbotLocalMessage(BuildMbotMacroHelpLine());
        return true;
    }

    private static bool MatchesMbotPromptSequence(byte[] bytes, char finalChar)
    {
        return bytes.Length == 3 &&
            bytes[0] == 0x1B &&
            bytes[1] == (byte)'[' &&
            bytes[2] == (byte)finalChar;
    }

    private void BeginMbotPrompt(string initialValue = "")
    {
        if (_gameInstance == null)
        {
            PublishMbotLocalMessage("mbot commands are only available while the embedded proxy is running.");
            return;
        }

        if (!_mbot.Enabled)
        {
            PublishMbotLocalMessage("Enable mbot first.");
            return;
        }

        if (_mbotPromptOpen)
            return;

        _mbotPromptOpen = true;
        _mbotPromptBuffer = initialValue;
        _mbotPromptDraft = initialValue;
        _mbotPromptHistoryIndex = _mbotCommandHistory.Count;
        _mbotMacroPromptOpen = false;
        _mbotMacroContext = null;
        RedrawMbotPrompt();
    }

    private void BeginMbotMacroPrompt()
    {
        if (!_mbotPromptOpen || _mbotMacroPromptOpen)
            return;

        if (_gameInstance == null || !_gameInstance.IsConnected)
        {
            PublishMbotLocalMessage("mbot macros need an active game connection.");
            return;
        }

        MbotGridContext context = BuildMbotGridContext();
        if (context.Surface != MbotPromptSurface.Command &&
            context.Surface != MbotPromptSurface.Citadel)
        {
            PublishMbotLocalMessage("mbot macros are available from command or citadel prompts.");
            return;
        }

        _mbotMacroContext = context;
        _mbotMacroPromptOpen = true;
        RedrawMbotPrompt();
    }

    private void EndMbotMacroPrompt()
    {
        _mbotMacroPromptOpen = false;
        _mbotMacroContext = null;
        RedrawMbotPrompt();
    }

    private void RecallMbotPromptHistory(int delta)
    {
        if (!_mbotPromptOpen || _mbotCommandHistory.Count == 0)
            return;

        int count = _mbotCommandHistory.Count;
        if (_mbotPromptHistoryIndex == count)
            _mbotPromptDraft = _mbotPromptBuffer;

        _mbotPromptHistoryIndex = Math.Clamp(_mbotPromptHistoryIndex + delta, 0, count);
        _mbotPromptBuffer = _mbotPromptHistoryIndex >= count
            ? _mbotPromptDraft
            : _mbotCommandHistory[_mbotPromptHistoryIndex];
        RedrawMbotPrompt();
    }

    private void CancelMbotPrompt()
    {
        if (!_mbotPromptOpen)
            return;

        ResetMbotPromptState();
        _parser.Feed("\r\x1b[K");
        _buffer.Dirty = true;
        _termCtrl.Focus();
    }

    private void SubmitMbotPrompt()
    {
        if (!_mbotPromptOpen)
            return;

        string command = _mbotPromptBuffer;
        string prompt = GetMbotPromptPrefix();

        ResetMbotPromptState();

        if (string.IsNullOrWhiteSpace(command))
        {
            _parser.Feed("\r\x1b[K");
            _buffer.Dirty = true;
            _termCtrl.Focus();
            return;
        }

        _parser.Feed("\r\x1b[K");
        _parser.Feed(prompt);
        _parser.Feed(command);
        _parser.Feed("\r\n");

        RememberMbotHistory(command);
        _mbot.TryExecuteLocalInput(command, out _);
        ApplyMbotExecutionRefresh();
    }

    private void ResetMbotPromptState()
    {
        _mbotPromptOpen = false;
        _mbotMacroPromptOpen = false;
        _mbotMacroContext = null;
        _mbotPromptBuffer = string.Empty;
        _mbotPromptDraft = string.Empty;
        _mbotPromptHistoryIndex = _mbotCommandHistory.Count;
    }

    private void RedrawMbotPrompt()
    {
        if (!_mbotPromptOpen)
            return;

        _parser.Feed("\r\x1b[K");
        _parser.Feed(_mbotMacroPromptOpen ? GetMbotMacroPromptPrefix() : GetMbotPromptPrefix());
        if (!_mbotMacroPromptOpen && _mbotPromptBuffer.Length > 0)
            _parser.Feed(_mbotPromptBuffer);
        _buffer.Dirty = true;
        _termCtrl.Focus();
    }

    private string GetMbotPromptPrefix()
    {
        MTC.mbot.mbotStatusSnapshot snapshot = _mbot.GetStatusSnapshot();
        string mode = string.IsNullOrWhiteSpace(snapshot.Mode) ? "General" : snapshot.Mode;
        string botName = string.IsNullOrWhiteSpace(snapshot.BotName) ? "mbot" : snapshot.BotName;
        return $"\x1b[1;34m{{{mode}}}\x1b[0;37m {botName}\x1b[1;32m>\x1b[0m ";
    }

    private string GetMbotMacroPromptPrefix()
    {
        string options = "Tab=Reset H=Holo D=Dens S=Surround X=Xenter";
        if (_mbotMacroContext is { AdjacentSectors.Count: > 0 } context)
        {
            string sectorKeys = string.Join(" ", context.AdjacentSectors
                .Take(10)
                .Select((sector, index) => $"{((index + 1) % 10)}={sector}"));
            options += " " + sectorKeys;
        }

        return $"\x1b[1;33m{{{options}}}\x1b[0;37m mbot\x1b[1;32m>\x1b[0m ";
    }

    private string BuildMbotMacroHelpLine()
    {
        if (_mbotMacroContext is not { } context)
            return "mbot macros: Tab=reset H=holo D=density S=surround X=xenter Esc=cancel";

        string line = "mbot macros: Tab=reset H=holo D=density S=surround X=xenter";
        if (context.AdjacentSectors.Count > 0)
        {
            string sectorKeys = string.Join(" ", context.AdjacentSectors
                .Take(10)
                .Select((sector, index) => $"{((index + 1) % 10)}={sector}"));
            line += " " + sectorKeys;
        }

        return line + " Esc=cancel";
    }

    private bool TryHandleMbotMacroKey(byte value)
    {
        if (_mbotMacroContext is not { } context)
        {
            EndMbotMacroPrompt();
            return true;
        }

        if (value >= (byte)'0' && value <= (byte)'9')
        {
            int index = value == (byte)'0' ? 9 : value - (byte)'1';
            if (index >= 0 && index < context.AdjacentSectors.Count)
            {
                int sector = context.AdjacentSectors[index];
                _ = ExecuteMbotMacroActionAsync(async macroContext =>
                {
                    if (macroContext.Surface == MbotPromptSurface.Citadel)
                        await ExecuteMbotUiCommandAsync($"pgrid {sector} scan");
                    else
                        await SendMbotServerMacroAsync(BuildMbotMoveMacro(sector));
                });
                return true;
            }
        }

        switch (char.ToUpperInvariant((char)value))
        {
            case 'H':
                _ = ExecuteMbotMacroActionAsync(macroContext =>
                    SendMbotServerMacroAsync(BuildMbotScanMacro(holo: true, macroContext)));
                return true;

            case 'D':
                _ = ExecuteMbotMacroActionAsync(macroContext =>
                    SendMbotServerMacroAsync(BuildMbotScanMacro(holo: false, macroContext)));
                return true;

            case 'S':
                _ = ExecuteMbotMacroActionAsync(_ => ExecuteMbotUiCommandAsync("surround"));
                return true;

            case 'X':
                _ = ExecuteMbotMacroActionAsync(_ => ExecuteMbotUiCommandAsync("xenter"));
                return true;
        }

        return false;
    }

    private async Task ExecuteMbotMacroActionAsync(Func<MbotGridContext, Task> action)
    {
        MbotGridContext? context = _mbotMacroContext;
        _mbotMacroPromptOpen = false;
        _mbotMacroContext = null;

        if (context == null)
        {
            RedrawMbotPrompt();
            return;
        }

        await action(context);
        if (_mbotPromptOpen)
            RedrawMbotPrompt();
    }

    private void PublishMbotLocalMessage(string message)
    {
        if (_gameInstance != null)
            _gameInstance.ClientMessage("\r\n" + message + "\r\n");
        else
            _parser.Feed("\r\n" + message + "\r\n");

        if (_mbotPromptOpen)
            RedrawMbotPrompt();
        else
            _termCtrl.Focus();

        _buffer.Dirty = true;
    }

    private bool TryInterceptMbotCommandPrompt(byte[] bytes)
    {
        if (_gameInstance == null ||
            !_mbot.Enabled ||
            _mbotPromptOpen ||
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

        MbotPromptSurface surface = GetMbotPromptSurface();
        if (_gameInstance.IsConnected && surface == MbotPromptSurface.Unknown)
            return false;

        BeginMbotPrompt();
        return true;
    }

    private MbotPromptSurface GetMbotPromptSurface()
    {
        string promptVar = Core.ScriptRef.GetCurrentGameVar("$PLAYER~CURRENT_PROMPT", string.Empty);
        if (string.Equals(promptVar, "Command", StringComparison.OrdinalIgnoreCase))
            return MbotPromptSurface.Command;
        if (string.Equals(promptVar, "Citadel", StringComparison.OrdinalIgnoreCase))
            return MbotPromptSurface.Citadel;
        if (string.Equals(promptVar, "Computer", StringComparison.OrdinalIgnoreCase))
            return MbotPromptSurface.Computer;

        string currentLine = Core.ScriptRef.GetCurrentLine().Trim();
        string currentAnsi = Core.ScriptRef.GetCurrentAnsiLine();
        if (currentLine.StartsWith("Command [TL=", StringComparison.OrdinalIgnoreCase))
            return MbotPromptSurface.Command;
        if (currentLine.StartsWith("Computer command [TL=", StringComparison.OrdinalIgnoreCase))
            return MbotPromptSurface.Computer;
        if (currentLine.Contains("Citadel", StringComparison.OrdinalIgnoreCase) ||
            currentLine.Contains("<Enter Citadel>", StringComparison.OrdinalIgnoreCase) ||
            currentAnsi.Contains("Citadel", StringComparison.OrdinalIgnoreCase))
        {
            return MbotPromptSurface.Citadel;
        }

        return MbotPromptSurface.Unknown;
    }

    private MbotGridContext BuildMbotGridContext()
    {
        int currentSector = Core.ScriptRef.GetCurrentSector();
        IReadOnlyList<int> adjacentSectors = _sessionDb?.GetSector(currentSector)?.Warp
            .Where(warp => warp > 0)
            .Select(warp => (int)warp)
            .Distinct()
            .ToArray()
            ?? Array.Empty<int>();

        return new MbotGridContext(
            GetMbotPromptSurface(),
            currentSector,
            adjacentSectors,
            ParseGameVarInt(Core.ScriptRef.GetCurrentGameVar("$PLANET~PLANET", "0")),
            _gameInstance?.IsConnected == true,
            _state.Photon);
    }

    private static int ParseGameVarInt(string value)
        => int.TryParse(value, out int parsed) ? parsed : 0;

    private string BuildMbotScanMacro(bool holo, MbotGridContext context)
    {
        string macro = context.Surface == MbotPromptSurface.Citadel ? "q q z n " : string.Empty;
        macro += holo ? "szhzn* " : "sdz* ";

        if (context.Surface == MbotPromptSurface.Citadel && context.PlanetNumber > 0)
            macro += $"l {context.PlanetNumber}*  c  ";

        return macro;
    }

    private string BuildMbotMoveMacro(int sector)
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

    private void RememberMbotHistory(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return;

        string trimmed = input.Trim();
        if (_mbotCommandHistory.Count > 0 &&
            string.Equals(_mbotCommandHistory[^1], trimmed, StringComparison.Ordinal))
        {
            return;
        }

        _mbotCommandHistory.Add(trimmed);
        if (_mbotCommandHistory.Count > 50)
            _mbotCommandHistory.RemoveAt(0);
    }

    private void ApplyMbotExecutionRefresh()
    {
        RefreshMbotUi();
        RefreshStatusBar();
        RebuildProxyMenu();
        _buffer.Dirty = true;
        _termCtrl.Focus();
    }

    private async Task SendMbotServerMacroAsync(string macro)
    {
        if (_gameInstance == null || !_gameInstance.IsConnected)
        {
            PublishMbotLocalMessage("This mbot action requires an active game connection.");
            return;
        }

        if (string.IsNullOrWhiteSpace(macro))
            return;

        await _gameInstance.SendToServerAsync(System.Text.Encoding.ASCII.GetBytes(macro));
        _termCtrl.Focus();
    }

    private async Task ExecuteMbotUiCommandAsync(string input)
    {
        await Task.Yield();

        if (_gameInstance == null)
        {
            PublishMbotLocalMessage("mbot controls are only available while the embedded proxy is running.");
            return;
        }

        if (!_mbot.Enabled && !string.Equals(input, "bot", StringComparison.OrdinalIgnoreCase))
        {
            PublishMbotLocalMessage("Enable mbot first.");
            return;
        }

        _mbot.TryExecuteLocalInput(input, out _);
        ApplyMbotExecutionRefresh();
    }

    private Task ShowMbotCommandPromptAsync(string initialValue = "")
    {
        BeginMbotPrompt(initialValue);
        return Task.CompletedTask;
    }

    private async Task ShowMbotGridMenuAsync(bool photonMode = false)
    {
        if (_gameInstance == null)
        {
            await ShowMessageAsync("mbot", "mbot commands are only available while the embedded proxy is running.");
            return;
        }

        if (!_mbot.Enabled)
        {
            await ShowMessageAsync("mbot", "Enable mbot first.");
            return;
        }

        MbotGridContext context = BuildMbotGridContext();
        if (!context.Connected)
        {
            await ShowMessageAsync("mbot", "The grid menu needs an active game connection.");
            return;
        }

        if (context.Surface != MbotPromptSurface.Command && context.Surface != MbotPromptSurface.Citadel)
        {
            await ShowMessageAsync("mbot", "The grid menu is only available from command or citadel prompts.");
            return;
        }

        string? action = null;
        string surfaceLabel = context.Surface == MbotPromptSurface.Citadel ? "Citadel" : "Command";
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
                : (context.Surface == MbotPromptSurface.Citadel ? $"PGrid {sector}" : $"Move {sector}");
            AddActionButton(verb, (photonMode ? "photon:" : "move:") + sector);
        }

        var closeBtn = new Button { Content = "Close", MinWidth = 96 };
        var dlg = new Window
        {
            Title = photonMode ? "mbot Photon Menu" : "mbot Grid Menu",
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
            _termCtrl.Focus();
            return;
        }

        if (string.Equals(action, "scan:holo", StringComparison.Ordinal))
        {
            await SendMbotServerMacroAsync(BuildMbotScanMacro(holo: true, context));
            return;
        }

        if (string.Equals(action, "scan:density", StringComparison.Ordinal))
        {
            await SendMbotServerMacroAsync(BuildMbotScanMacro(holo: false, context));
            return;
        }

        if (string.Equals(action, "cmd:surround", StringComparison.Ordinal))
        {
            await ExecuteMbotUiCommandAsync("surround");
            return;
        }

        if (string.Equals(action, "menu:photon", StringComparison.Ordinal))
        {
            await ShowMbotGridMenuAsync(photonMode: true);
            return;
        }

        if (action.StartsWith("photon:", StringComparison.Ordinal) &&
            int.TryParse(action["photon:".Length..], out int photonSector))
        {
            await ExecuteMbotUiCommandAsync($"photon {photonSector}");
            return;
        }

        if (action.StartsWith("move:", StringComparison.Ordinal) &&
            int.TryParse(action["move:".Length..], out int moveSector))
        {
            if (context.Surface == MbotPromptSurface.Citadel)
                await ExecuteMbotUiCommandAsync($"pgrid {moveSector} scan");
            else
                await SendMbotServerMacroAsync(BuildMbotMoveMacro(moveSector));
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
        _termCtrl.Focus();
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
        _termCtrl.Focus();
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
            _termCtrl.Focus();
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
            _termCtrl.Focus();
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
            _termCtrl.Focus();
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
            _termCtrl.Focus();
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
            _termCtrl.Focus();
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
            _termCtrl.Focus();
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
