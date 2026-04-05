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
using Avalonia.Layout;
using Avalonia.Media;
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
    private MenuItem        _quickMenu     = new() { Header = "_Quick" };
    private MenuItem        _aiMenu        = new() { Header = "_AI", IsVisible = false };
    private MenuItem        _fileEdit       = new() { Header = "_Edit Connection…", IsEnabled = false };
    private MenuItem        _fileConnect    = new() { Header = "_Connect",    IsEnabled = false };
    private MenuItem        _fileDisconnect = new() { Header = "_Disconnect", IsEnabled = false };
    private Menu            _menuBar       = new();
    private readonly NativeMenu _nativeAppMenu = new();
    private readonly NativeMenu _nativeDockMenu = new();
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
        _sessionLog.LogDirectory = AppPaths.LogDir;
        _sessionLog.SetLogIdentity(DeriveGameName());
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
            if (_telnet.IsConnected)
                _telnet.SendRaw(bytes);
            else
                _parser.Feed("\x1b[33m[not connected]\x1b[0m\r\n");
        };

        _haggleToggle.IsCheckedChanged += (_, _) => OnHaggleToggleRequested();

        Content = BuildLayout();

        // Load persisted preferences (recent file list etc.)
        _appPrefs = AppPreferences.Load();
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
        // Root: DockPanel – menu top, status bottom, content fill
        var dock = new DockPanel { Background = BgWindow };

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

        var statusBar = new Border
        {
            Background = BgStatus,
            Height     = 26,
            Child      = _statusText,
        };
        DockPanel.SetDock(statusBar, Dock.Bottom);
        dock.Children.Add(statusBar);

        // ── Centre: sidebar + terminal ────────────────────────────────────
        // Margin lets the gray BgChrome peek in on all four sides as a frame.
        var grid = new Grid { Margin = new Thickness(6, 4, 6, 4) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(200) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(6) });   // gap
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var sidebar  = BuildSidebar();
        Grid.SetColumn(sidebar, 0);
        grid.Children.Add(sidebar);

        var termArea = BuildTerminalArea();
        Grid.SetColumn(termArea, 2);
        grid.Children.Add(termArea);

        dock.Children.Add(grid);
        return dock;
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

        var viewMenu = new MenuItem
        {
            Header = "_View",
            Items  = { viewClear, viewFont, viewDbItem },
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
            Items      = { fileMenu, _scriptsMenu, _proxyMenu, _quickMenu, _aiMenu, mapMenu, viewMenu, helpMenu },
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
        _valName.Text      = string.IsNullOrEmpty(_state.TraderName) ? "-" : _state.TraderName;
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

        RefreshStatusBar();
    }

    private void RefreshStatusBar()
    {
        string conn = _state.Connected
            ? $"[ {_state.Host}:{_state.Port} ]"
            : "[ disconnected ]";

        string starDock = "-";
        string rylos = "-";
        string alpha = "-";

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

        _statusText.Text =
            $" SD: {starDock,-6}  Rylos: {rylos,-6}  Alpha: {alpha,-6}  {conn}";
    }

    // ── Telnet events ──────────────────────────────────────────────────────

    private void OnTelnetConnected()
    {
        _state.Connected = true;
        _sessionLog.SetLogIdentity(DeriveGameName());
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
        RebuildProxyMenu();
    }

    /// <summary>Call when TCP connection is lost / disconnected.</summary>
    private void OnGameDisconnected()
    {
        _fileConnect.IsEnabled    = true;
        _fileDisconnect.IsEnabled = false;
        UpdateHaggleToggleState();
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
        });
    }

    // ── Menu actions ───────────────────────────────────────────────────────

    private async void OnConnect()
    {
        if (_state.EmbeddedProxy)
            await DoConnectEmbeddedAsync();
        else
            DoConnect();
    }

    private void OnDisconnect()
    {
        if (_gameInstance != null)
        {
            _pendingEmbeddedStop = StopEmbeddedAsync();
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
        config.ScriptDirectory = string.IsNullOrWhiteSpace(config.ScriptDirectory)
            ? (!string.IsNullOrWhiteSpace(_appPrefs.ScriptsDirectory) ? _appPrefs.ScriptsDirectory : string.Empty)
            : config.ScriptDirectory;
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
        if (string.IsNullOrWhiteSpace(config.ScriptDirectory) && !string.IsNullOrWhiteSpace(_appPrefs.ScriptsDirectory))
            config.ScriptDirectory = _appPrefs.ScriptsDirectory;
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

        // Open / create the session database using sectors from the game config.
        OpenSessionDatabase(gameName, gameConfig.Sectors, useSharedProxyDatabase: true);

        // Resolve the effective script directory: game config -> app prefs -> platform default.
        // TrimEnd removes any trailing separator so Path.GetDirectoryName returns the
        // true parent folder rather than the same folder (which happens when the stored
        // path ends with '/').
        string effectiveScriptDir = (!string.IsNullOrWhiteSpace(gameConfig.ScriptDirectory)
            ? gameConfig.ScriptDirectory
            : (!string.IsNullOrWhiteSpace(_appPrefs.ScriptsDirectory)
                ? _appPrefs.ScriptsDirectory
                : (OperatingSystem.IsWindows()
                    ? Core.WindowsInstallInfo.GetDefaultScriptsDirectory()
                    : System.Environment.GetFolderPath(System.Environment.SpecialFolder.MyDocuments))))
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

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
        gi.Logger.LogDirectory = AppPaths.LogDir;
        gi.Logger.SetLogIdentity(gameName);
        gi.ReconnectDelayMs = Math.Max(1, gameConfig.ReconnectDelaySeconds) * 1000;
        gi.LocalEcho = gameConfig.LocalEcho;
        gi.AcceptExternal = gameConfig.AcceptExternal;
        gi.AllowLerkers = gameConfig.AllowLerkers;
        gi.ExternalAddress = gameConfig.ExternalAddress ?? string.Empty;
        gi.BroadCastMsgs = gameConfig.BroadcastMessages;
        gi.Logger.LogEnabled = gameConfig.LogEnabled;
        gi.Logger.LogData = gameConfig.LogEnabled;
        gi.Logger.LogANSI = gameConfig.LogAnsi;
        gi.Logger.BinaryLogs = gameConfig.LogBinary;
        gi.Logger.NotifyPlayCuts = gameConfig.NotifyPlayCuts;
        gi.Logger.MaxPlayDelay = gameConfig.MaxPlayDelay;
        gi.SetNativeHaggleEnabled(gameConfig.NativeHaggleEnabled);
        gi.NativeHaggleChanged += OnNativeHaggleChanged;

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
            try { termWriter.Write(bytes, 0, bytes.Length); termWriter.Flush(); }
            catch { }
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
                        _parser.Feed(chunk, chunk.Length);
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
                        interpreter.BeginServerLineTracking();
                        if (!gi.IsProxyMenuActive)
                        {
                            Core.ScriptRef.SetCurrentAnsiLine(remainderAnsi);
                            Core.ScriptRef.SetCurrentLine(scriptRemainder);
                            // Partial line / prompt: fire TextEvent only (no TextLineEvent, no ActivateTriggers).
                            interpreter.TextEvent(scriptRemainder, false);
                        }

                        gi.ProcessNativeHaggleLine(strippedRemainder, interpreter.ConsumeServerLineHandledFlag());
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
                interpreter.BeginServerLineTracking();

                if (!gi.IsProxyMenuActive)
                {
                    Core.ScriptRef.SetCurrentAnsiLine(lineRaw);
                    Core.ScriptRef.SetCurrentLine(lineForScript);

                    // Fire trigger pipeline (all lines including blank — matches Pascal ProcessLine).
                    interpreter.TextLineEvent(lineForScript, false);
                    interpreter.TextEvent(lineForScript, false);
                    interpreter.ActivateTriggers();
                }

                gi.ProcessNativeHaggleLine(lineStripped, interpreter.ConsumeServerLineHandledFlag());

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
        OnGameConnected();   // disable Connect menu item, enable Disconnect
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
        var moduleHost = _moduleHost;
        _moduleHost = null;
        if (gi != null)
            gi.NativeHaggleChanged -= OnNativeHaggleChanged;
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
            if (_telnet.IsConnected) _telnet.SendRaw(bytes);
            else _parser.Feed("\x1b[33m[not connected]\x1b[0m\r\n");
        };

        _state.Connected      = false;
        _termCtrl.IsConnected = false;
        OnGameDisconnected();
        _parser.Feed("\x1b[1;31m[Embedded proxy stopped]\x1b[0m\r\n");
        RefreshStatusBar();
        UpdateHaggleToggleState();
        _buffer.Dirty = true;
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
            ScriptDirectory = !string.IsNullOrWhiteSpace(_appPrefs.ScriptsDirectory) ? _appPrefs.ScriptsDirectory : string.Empty,
            NativeHaggleEnabled = true,
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
            var json = System.Text.Json.JsonSerializer.Serialize(cfg, _jsonOpts);
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
        if (string.IsNullOrWhiteSpace(config.ScriptDirectory) && !string.IsNullOrWhiteSpace(_appPrefs.ScriptsDirectory))
            config.ScriptDirectory = _appPrefs.ScriptsDirectory;
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
            if (!string.IsNullOrWhiteSpace(config.ScriptDirectory))
                importedConfig.ScriptDirectory = config.ScriptDirectory;
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
                header.Address = _state.Host;
                header.ServerPort = (ushort)_state.Port;
                header.ListenPort = (ushort)(_embeddedGameConfig?.ListenPort ?? 2300);
                header.CommandChar = _embeddedGameConfig?.CommandChar ?? '$';
                header.UseLogin = _state.UseLogin;
                header.UseRLogin = _state.UseRLogin;
                header.LoginScript = string.IsNullOrWhiteSpace(_state.LoginScript) ? "0_Login.cts" : _state.LoginScript;
                header.LoginName = _state.LoginName;
                header.Password = _state.Password;
                header.Game = string.IsNullOrWhiteSpace(_state.GameLetter) ? '\0' : char.ToUpperInvariant(_state.GameLetter[0]);
                db.ReplaceHeader(header);
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
                Header = hasGame ? $"Current Game: {gameName}" : "No game selected",
                IsEnabled = false,
            },
            new Separator(),
        };

        var stopNonSystem = new MenuItem { Header = "Stop All _Non-System Scripts", IsEnabled = hasInterpreter };
        stopNonSystem.Click += (_, _) => _ = OnProxyStopAllScriptsAsync(includeSystemScripts: false);
        items.Add(stopNonSystem);

        var stopAll = new MenuItem { Header = "Stop _All Scripts", IsEnabled = hasInterpreter };
        stopAll.Click += (_, _) => _ = OnProxyStopAllScriptsAsync(includeSystemScripts: true);
        items.Add(stopAll);

        var stopScriptMenu = new MenuItem { Header = "Stop _Script", IsEnabled = hasInterpreter };
        stopScriptMenu.ItemsSource = BuildStopScriptItems();
        items.Add(stopScriptMenu);
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

        return items;
    }

    private List<object> BuildStopScriptItems()
    {
        var items = new List<object>();
        var interpreter = CurrentInterpreter;
        if (interpreter == null)
        {
            items.Add(new MenuItem { Header = "No proxy scripts active", IsEnabled = false });
            return items;
        }

        var scripts = Core.ProxyGameOperations.GetRunningScripts(interpreter);
        if (scripts.Count == 0)
        {
            items.Add(new MenuItem { Header = "No active scripts", IsEnabled = false });
            return items;
        }

        foreach (var script in scripts)
        {
            int scriptId = script.Id;
            var item = new MenuItem
            {
                Header = script.IsSystemScript ? $"{script.Name} (system)" : script.Name
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

        var botMenu = new MenuItem { Header = "_Bots", IsEnabled = _gameInstance != null };
        botMenu.ItemsSource = BuildBotMenuItems(_gameInstance != null);
        items.Add(botMenu);

        if (groups.Count > 0)
            items.Add(new Separator());

        foreach (var group in groups)
        {
            var groupMenu = new MenuItem { Header = group.Name };
            var groupItems = new List<object>();
            foreach (var entry in group.Entries)
            {
                string relativePath = entry.RelativePath;
                var item = new MenuItem { Header = entry.DisplayName };
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
                Header = binding.Info.DisplayName,
            };
            item.Click += (_, _) => _ = OpenAiAssistantAsync(moduleId);
            items.Add(item);
        }

        return items;
    }

    private List<object> BuildBotMenuItems(bool enabled)
    {
        var items = new List<object>();
        if (!enabled || _gameInstance == null)
        {
            items.Add(new MenuItem { Header = "Proxy is not running", IsEnabled = false });
            return items;
        }

        var bots = _gameInstance.GetBotList();
        if (bots.Count == 0)
        {
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
            return _appPrefs.ScriptsDirectory;

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
                var sub = new MenuItem { Header = node.Name };
                sub.ItemsSource = subItems;
                target.Add(sub);
            }
            else
            {
                var relPath = node.RelPath;  // capture
                var item    = new MenuItem { Header = node.Name };
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
            var item = new MenuItem { Header = name };
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

        return header.Replace("_", string.Empty);
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
        string previousHost = gameConfig.Host;
        int previousPort = gameConfig.Port;

        bool configChanged =
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
            header.Address = _state.Host;
            header.ServerPort = (ushort)_state.Port;
            header.ListenPort = (ushort)gameConfig.ListenPort;
            header.CommandChar = gameConfig.CommandChar == '\0' ? '$' : gameConfig.CommandChar;
            header.UseLogin = _state.UseLogin;
            header.UseRLogin = _state.UseRLogin;
            header.LoginScript = string.IsNullOrWhiteSpace(_state.LoginScript) ? "0_Login.cts" : _state.LoginScript;
            header.LoginName = _state.LoginName;
            header.Password = _state.Password;
            header.Game = string.IsNullOrWhiteSpace(_state.GameLetter) ? '\0' : char.ToUpperInvariant(_state.GameLetter[0]);
            _sessionDb.ReplaceHeader(header);
            Core.ScriptRef.SetActiveDatabase(_sessionDb);
        }

        if (_gameInstance == null)
            return;

        _gameInstance.AutoReconnect = _state.AutoReconnect;
        _gameInstance.ReconnectDelayMs = Math.Max(1, gameConfig.ReconnectDelaySeconds) * 1000;
        _gameInstance.LocalEcho = gameConfig.LocalEcho;
        _gameInstance.AcceptExternal = gameConfig.AcceptExternal;
        _gameInstance.AllowLerkers = gameConfig.AllowLerkers;
        _gameInstance.ExternalAddress = gameConfig.ExternalAddress ?? string.Empty;
        _gameInstance.BroadCastMsgs = gameConfig.BroadcastMessages;
        _gameInstance.Logger.LogEnabled = gameConfig.LogEnabled;
        _gameInstance.Logger.LogData = gameConfig.LogEnabled;
        _gameInstance.Logger.LogANSI = gameConfig.LogAnsi;
        _gameInstance.Logger.BinaryLogs = gameConfig.LogBinary;
        _gameInstance.Logger.NotifyPlayCuts = gameConfig.NotifyPlayCuts;
        _gameInstance.Logger.MaxPlayDelay = gameConfig.MaxPlayDelay;
        _gameInstance.SetNativeHaggleEnabled(gameConfig.NativeHaggleEnabled);

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
