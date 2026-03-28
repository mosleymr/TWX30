using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using SkiaSharp;
using Avalonia.Controls;
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
    private CancellationTokenSource?       _proxyCts;       // cancels the pipe-reader task
    private Task                           _pendingEmbeddedStop = Task.CompletedTask; // tracks in-flight StopEmbeddedAsync
    private readonly SessionLogger         _sessionLog = new();
    private static readonly System.Text.Json.JsonSerializerOptions _jsonOpts = new()
    {
        WriteIndented             = true,
        PropertyNameCaseInsensitive = true,
    };
    private MenuItem        _recentMenu    = new() { Header = "_Recent" };
    private MenuItem        _scriptsMenu   = new() { Header = "_Scripts" };
    private MenuItem        _fileEdit       = new() { Header = "_Edit Connection…", IsEnabled = false };
    private MenuItem        _fileConnect    = new() { Header = "_Connect",    IsEnabled = false };
    private MenuItem        _fileDisconnect = new() { Header = "_Disconnect", IsEnabled = false };
    private Menu            _menuBar       = new();
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

        // Session logging: capture all raw server output and client sends.
        _telnet.AppDataDecoded   += text  => _sessionLog.LogFromServer(text);

        // Update current sector from the command prompt — fires on every "Command [TL=...]:[N]"
        Core.GlobalModules.GlobalAutoRecorder.CurrentSectorChanged += sn =>
            Dispatcher.UIThread.Post(() =>
            {
                if (_state.Sector != sn)
                {
                    _state.Sector = sn;
                    _state.NotifyChanged();
                }
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

        Content = BuildLayout();

        // Load persisted preferences (recent file list etc.)
        _appPrefs = AppPreferences.Load();
        RebuildRecentMenu();
        RebuildScriptsMenu();
        _parser.Feed("\x1b[2J\x1b[H");
        _parser.Feed("\x1b[1;33mMayhem Tradewars Client v1.0\x1b[0m\r\n");
        _parser.Feed("\x1b[37mUse \x1b[1;32mFile \u25b6 New Connection\x1b[0;37m or \x1b[1;32mOpen\x1b[0;37m to select a game, then \x1b[1;32mFile \u25b6 Connect\x1b[0;37m to connect.\x1b[0m\r\n");
        _buffer.Dirty = true;

        // 50 ms refresh – pushes buffer changes to UI
        _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
        _refreshTimer.Tick += (_, _) => _termCtrl.RequestRedraw();
        _refreshTimer.Start();

        Activated += (_, _) => _termCtrl.Focus();
        Closed    += (_, _) => { _refreshTimer.Stop(); _telnet.Disconnect(); _proxyCts?.Cancel(); if (_gameInstance != null) _ = _gameInstance.StopAsync(); _sessionLog.Dispose(); };
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
        _statusText.Text              = " Sect: -  Turns: -  Cred: -  [ disconnected ]";
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
            Items      = { fileMenu, _scriptsMenu, mapMenu, viewMenu, helpMenu },
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
                var savePath = _currentProfilePath;
                try { BuildProfileFromState().SaveXml(savePath); }
                catch { /* best-effort */ }
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
        _statusText.Text =
            $" Sect: {_state.Sector,-6}  Turns: {_state.Turns,-8:N0}  Cred: {_state.Credits,-14:N0}  {conn}";
    }

    // ── Telnet events ──────────────────────────────────────────────────────

    private void OnTelnetConnected()
    {
        _state.Connected = true;
        _sessionLog.Open(DeriveGameName());
        // Open (or create) the sector database for this game connection
        OpenSessionDatabase();
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
        _sessionLog.Close();
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
        string name = !string.IsNullOrEmpty(_currentProfilePath)
            ? Path.GetFileNameWithoutExtension(_currentProfilePath)
            : $"{_state.Host}_{_state.Port}";
        name = string.Concat(name.Split(Path.GetInvalidFileNameChars()));
        return string.IsNullOrWhiteSpace(name) ? "game" : name;
    }

    /// <summary>Call after a profile is applied (game selected) to enable Connect.</summary>
    private void OnGameSelected()
    {
        _fileEdit.IsEnabled       = true;
        _fileConnect.IsEnabled    = true;
        _fileDisconnect.IsEnabled = false;
    }

    /// <summary>Call when TCP connection is established.</summary>
    private void OnGameConnected()
    {
        _fileConnect.IsEnabled    = false;
        _fileDisconnect.IsEnabled = true;
    }

    /// <summary>Call when TCP connection is lost / disconnected.</summary>
    private void OnGameDisconnected()
    {
        _fileConnect.IsEnabled    = true;
        _fileDisconnect.IsEnabled = false;
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
        await new PreferencesDialog(_appPrefs).ShowDialog<bool>(this);
        // Rebuild scripts menu in case the directory changed.
        RebuildScriptsMenu();
    }

    // ── Connection profile helpers ──────────────────────────────────────────

    /// <summary>Builds a <see cref="ConnectionProfile"/> from the current live state.</summary>
    private ConnectionProfile BuildProfileFromState() => new ConnectionProfile
    {
        // Connection
        Server          = _state.Host,
        Port            = _state.Port,
        Protocol        = _state.Protocol,
        LocalTwxProxy   = _state.LocalTwxProxy,
        TwxProxyDbPath  = _state.TwxProxyDbPath,
        EmbeddedProxy   = _state.EmbeddedProxy,
        Sectors         = _state.Sectors,
        AutoReconnect   = _state.AutoReconnect,
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

    /// <summary>Applies a profile to GameState and the terminal buffer.</summary>
    private void ApplyProfile(ConnectionProfile p)
    {
        // Connection
        _state.Host           = p.Server;
        _state.Port           = p.Port;
        _state.Protocol       = p.Protocol;
        _state.LocalTwxProxy  = p.LocalTwxProxy;
        _state.TwxProxyDbPath = p.TwxProxyDbPath;
        _state.EmbeddedProxy   = p.EmbeddedProxy;
        _state.Sectors         = p.Sectors;
        _state.AutoReconnect   = p.AutoReconnect;
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
        string gameName = !string.IsNullOrEmpty(_currentProfilePath)
            ? System.IO.Path.GetFileNameWithoutExtension(_currentProfilePath)
            : $"{_state.Host}_{_state.Port}";
        gameName = string.Concat(gameName.Split(System.IO.Path.GetInvalidFileNameChars()));
        if (string.IsNullOrWhiteSpace(gameName)) gameName = "game";

        // Open session log for this connection.
        _sessionLog.Open(gameName);

        // Load (or create) the shared TWXP game config JSON.
        // This gives us the persisted variable state and the authoritative sector count.
        var gameConfig = await LoadOrCreateEmbeddedGameConfigAsync(gameName);

        // Open / create the session database using sectors from the game config.
        OpenSessionDatabase(gameName, gameConfig.Sectors);

        // Resolve the effective script directory: app prefs → ~/Documents.
        // TrimEnd removes any trailing separator so Path.GetDirectoryName returns the
        // true parent folder rather than the same folder (which happens when the stored
        // path ends with '/').
        string effectiveScriptDir = (!string.IsNullOrWhiteSpace(_appPrefs.ScriptsDirectory)
            ? _appPrefs.ScriptsDirectory
            : System.Environment.GetFolderPath(System.Environment.SpecialFolder.MyDocuments))
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        // Create the script interpreter.
        // ProgramDir = parent of the scripts folder (matches ProxyService behaviour).
        string programDir = Path.GetDirectoryName(effectiveScriptDir) ?? effectiveScriptDir;
        var interpreter = new Core.ModInterpreter();
        interpreter.ScriptDirectory = effectiveScriptDir;
        interpreter.ProgramDir      = programDir;
        Core.GlobalModules.ProgramDir = programDir;  // shared global used by some script commands

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
            listenPort: 0,
            commandChar: '$',
            interpreter: interpreter,
            scriptDirectory: effectiveScriptDir)
        {
            Verbose       = false,          // suppress diagnostic Console.WriteLine in embedded mode
            AutoReconnect = _state.AutoReconnect,
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
            // Session log captures only true server-originated text in embedded mode.
            _sessionLog.LogFromServer(e.Text);

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
                        string remainderAnsi   = remainder.Replace("\n", "");
                        string strippedRemainder = rxAnsi.Replace(remainderAnsi, string.Empty).TrimEnd('\r');
                        _shipParser.FeedLine(strippedRemainder);
                        Core.GlobalModules.GlobalAutoRecorder.RecordLine(strippedRemainder);
                        if (!gi.IsProxyMenuActive)
                        {
                            Core.ScriptRef.SetCurrentAnsiLine(remainderAnsi);
                            Core.ScriptRef.SetCurrentLine(strippedRemainder);
                            // Partial line / prompt: fire TextEvent only (no TextLineEvent, no ActivateTriggers).
                            interpreter.TextEvent(strippedRemainder, false);
                        }
                    }
                    break;
                }

                // Complete \r-terminated line.
                string lineRaw     = buffered[lastProcessedPos..crPos].Replace("\n", "");
                string lineStripped = rxAnsi.Replace(lineRaw, string.Empty).TrimEnd('\r');

                if (!string.IsNullOrEmpty(lineStripped))
                {
                    _shipParser.FeedLine(lineStripped);
                    Core.GlobalModules.GlobalAutoRecorder.RecordLine(lineStripped);
                }

                if (!gi.IsProxyMenuActive)
                {
                    Core.ScriptRef.SetCurrentAnsiLine(lineRaw);
                    Core.ScriptRef.SetCurrentLine(lineStripped);

                    // Fire trigger pipeline (all lines including blank — matches Pascal ProcessLine).
                    interpreter.TextLineEvent(lineStripped, false);
                    interpreter.TextEvent(lineStripped, false);
                    interpreter.ActivateTriggers();
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
            // Fire the Pascal 'Connection accepted' program event so scripts that
            // registered setEventTrigger handlers can respond (e.g. login sequence).
            interpreter.ProgramEvent("Connection accepted", "", false);
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
                _parser.Feed("\x1b[1;33m[Server disconnected — proxy auto-reconnecting…]\x1b[0m\r\n");
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
        if (gi != null)
            await gi.StopAsync();  // no ConfigureAwait(false) — continuation returns to UI thread

        Core.ScriptRef.SetActiveGameInstance(null);
        Core.ScriptRef.OnVariableSaved = null;  // detach savevar persistence for this game

        try { _sessionDb?.CloseDatabase(); } catch { }
        _sessionDb = null;
        Core.ScriptRef.SetActiveDatabase(null);

        _sessionLog.Close();
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
                if (cfg != null) return cfg;
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

    /// <summary>
    /// Opens or creates the sector database for the current connection.
    /// Named after the profile file (if saved) or the host:port string.
    /// Stored in ~/Library/MTC/databases/&lt;name&gt;.xdb.
    /// </summary>
    private void OpenSessionDatabase(string? gameName = null, int sectors = 0)
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

            AppPaths.EnsureDirectories();
            string dbPath = AppPaths.DatabasePathForGame(gameName);

            var db = new Core.ModDatabase();
            if (File.Exists(dbPath))
            {
                db.OpenDatabase(dbPath);
                db.UpdateHeader(new Core.DataHeader
                {
                    Address    = _state.Host,
                    ServerPort = (ushort)_state.Port,
                });
            }
            else
            {
                db.CreateDatabase(dbPath, new Core.DataHeader
                {
                    Address    = _state.Host,
                    ServerPort = (ushort)_state.Port,
                    Sectors    = sectors,
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
    }

    /// <summary>Opens a recently used .mtc file directly (no file picker, no connect).</summary>
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

        ConnectionProfile profile;
        try   { profile = ConnectionProfile.LoadXml(path); }
        catch (Exception ex) { await ShowMessageAsync("Load Error", ex.Message); return; }

        _currentProfilePath = path;
        AddToRecentAndSave(path);
        ApplyProfile(profile);
        OnGameSelected();
        if (profile.EmbeddedProxy && _gameInstance == null)
        {
            // Auto-start the embedded proxy (but not the server connection) when
            // selecting a game that uses the embedded proxy from the recent list.
            await DoConnectEmbeddedAsync();
        }
        else
        {
            _parser.Feed($"\x1b[1;36m[Game loaded: {profile.Server}:{profile.Port}  —  use File \u25b6 Connect to connect]\x1b[0m\r\n");
            _buffer.Dirty = true;
        }
    }

    /// <summary>File > New Connection: dialog → save picker → save XML → apply (no connect).</summary>
    private async Task OnEditConnectionAsync()
    {
        var dlg = new NewConnectionDialog(BuildProfileFromState());
        if (!await dlg.ShowDialog<bool>(this) || dlg.Result == null) return;

        ApplyProfile(dlg.Result);

        // Auto-save in place if a profile file is already loaded.
        if (!string.IsNullOrEmpty(_currentProfilePath))
        {
            try { dlg.Result.SaveXml(_currentProfilePath); }
            catch { /* best-effort */ }
        }

        _parser.Feed($"\x1b[1;36m[Connection settings updated]\x1b[0m\r\n");
        _buffer.Dirty = true;
    }

    private async Task OnNewConnectionAsync()
    {
        var dlg = new NewConnectionDialog();
        if (!await dlg.ShowDialog<bool>(this) || dlg.Result == null) return;

        var path = await PickSavePath(Path.GetFileName(_currentProfilePath));
        if (path == null) return;

        try   { dlg.Result.SaveXml(path); }
        catch (Exception ex) { await ShowMessageAsync("Save Error", ex.Message); return; }

        _currentProfilePath = path;
        ApplyProfile(dlg.Result);
        AddToRecentAndSave(path);
        OnGameSelected();
        _parser.Feed($"\x1b[1;36m[Game loaded: {dlg.Result.Server}:{dlg.Result.Port}  —  use File \u25b6 Connect to connect]\x1b[0m\r\n");
        _buffer.Dirty = true;
    }

    /// <summary>File > Open: file picker → load XML → apply (no connect).</summary>
    private async Task OnOpenConnectionAsync()
    {
        var storage = TopLevel.GetTopLevel(this)?.StorageProvider;
        if (storage == null) return;

        var home  = await GetHomeFolderAsync(storage);
        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title                  = "Open MTC Connection",
            SuggestedStartLocation = home,
            AllowMultiple          = false,
            FileTypeFilter         =
            [
                new FilePickerFileType("MTC Connection") { Patterns = ["*.mtc"] },
                new FilePickerFileType("All Files")      { Patterns = ["*"]     },
            ],
        });
        if (files.Count == 0) return;

        string path = files[0].Path.LocalPath;
        ConnectionProfile profile;
        try   { profile = ConnectionProfile.LoadXml(path); }
        catch (Exception ex) { await ShowMessageAsync("Load Error", ex.Message); return; }

        _currentProfilePath = path;
        ApplyProfile(profile);
        AddToRecentAndSave(path);
        OnGameSelected();
        _parser.Feed($"\x1b[1;36m[Game loaded: {profile.Server}:{profile.Port}  —  use File \u25b6 Connect to connect]\x1b[0m\r\n");
        _buffer.Dirty = true;
    }

    /// <summary>File > Save / Save As: save current connection settings to XML.</summary>
    private async Task OnSaveConnectionAsync(bool saveAs = false)
    {
        string? path = _currentProfilePath;
        if (saveAs || path == null)
        {
            path = await PickSavePath(Path.GetFileName(_currentProfilePath));
            if (path == null) return;
        }

        try   { BuildProfileFromState().SaveXml(path); }
        catch (Exception ex) { await ShowMessageAsync("Save Error", ex.Message); return; }

        _currentProfilePath = path;
        AddToRecentAndSave(path);
    }

    /// <summary>Opens the save-file picker for .mtc files, returns chosen path or null.</summary>
    private async Task<string?> PickSavePath(string? suggestedName = null)
    {
        var storage = TopLevel.GetTopLevel(this)?.StorageProvider;
        if (storage == null) return null;

        var home = await GetHomeFolderAsync(storage);
        var file = await storage.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title                  = "Save MTC Connection",
            SuggestedStartLocation = home,
            SuggestedFileName      = suggestedName ?? "connection",
            DefaultExtension       = "mtc",
            FileTypeChoices        =
            [
                new FilePickerFileType("MTC Connection") { Patterns = ["*.mtc"] },
            ],
        });
        return file?.Path.LocalPath;
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
}
