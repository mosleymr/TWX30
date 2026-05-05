using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using SkiaSharp;
using Core = TWXProxy.Core;

namespace MTC;

public partial class MainWindow
{
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
        _termCtrl.ViewportSizeChanged += OnClassicTerminalViewportSizeChanged;
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
        _onlinePlayersHost = new() { Spacing = 2 };
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
        _shipInfoHeaderText = new();
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

        _statusTerminalSizeText.Foreground = HudMuted;
        _statusTerminalSizeText.VerticalAlignment = VerticalAlignment.Center;
        _statusTerminalSizeText.HorizontalAlignment = HorizontalAlignment.Right;
        _statusTerminalSizeText.Margin = new Thickness(10, 0, 10, 0);
        _statusTerminalSizeText.FontSize = 12;
        _statusTerminalSizeText.FontFamily = new FontFamily("Cascadia Code, Menlo, Consolas, Courier New, monospace");
        _statusTerminalSizeText.TextAlignment = TextAlignment.Right;
        _statusTerminalSizeText.IsVisible = false;

        _statusBarLayoutRoot.ColumnDefinitions.Clear();
        _statusBarLayoutRoot.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        _statusBarLayoutRoot.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _statusBarLayoutRoot.Children.Clear();
        Grid.SetColumn(_statusBarContent, 0);
        Grid.SetColumn(_statusTerminalSizeText, 1);
        _statusBarLayoutRoot.Children.Add(_statusBarContent);
        _statusBarLayoutRoot.Children.Add(_statusTerminalSizeText);

        _statusBar.Background = BgStatus;
        _statusBar.Height = 34;
        InvalidateStatusBarLayout();
        _statusBar.Child = _statusBarLayoutRoot;
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
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(8) });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        _deckValName.Foreground = HudText;
        _deckValName.FontSize = 15;
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

        return new Viewbox
        {
            Stretch = Stretch.Uniform,
            StretchDirection = StretchDirection.DownOnly,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            Child = new Border
            {
                Padding = new Thickness(2),
                Child = grid,
            },
        };
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

    private void OnClassicTerminalViewportSizeChanged(TerminalControl _, int __, int ___)
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(UpdateClassicTerminalSizeStatus, DispatcherPriority.Background);
            return;
        }

        UpdateClassicTerminalSizeStatus();
    }

    private void UpdateClassicTerminalSizeStatus()
    {
        bool showClassicSize = !_useCommandDeckSkin;
        _statusTerminalSizeText.IsVisible = showClassicSize;
        if (!showClassicSize)
        {
            _statusTerminalSizeText.Text = string.Empty;
            return;
        }

        _statusTerminalSizeText.Text = $"{_termCtrl.Columns}x{_termCtrl.Rows}";
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
        var stack = new StackPanel { Spacing = 6 };
        foreach (Control row in rows)
            stack.Children.Add(row);

        return new Border
        {
            Background = HudFrameAlt,
            BorderBrush = HudInnerEdge,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(14),
            Padding = new Thickness(10),
            MinWidth = 0,
            Child = new StackPanel
            {
                Spacing = 8,
                Children =
                {
                    new TextBlock
                    {
                        Text = title,
                        FontFamily = HudTitleFont,
                        FontSize = 13,
                        FontWeight = FontWeight.SemiBold,
                        Foreground = HudAccent,
                        TextTrimming = TextTrimming.CharacterEllipsis,
                        TextWrapping = TextWrapping.NoWrap,
                        MaxLines = 1,
                    },
                    stack,
                },
            },
        };
    }

    private Control BuildDeckMetricRow(string label, TextBlock value)
    {
        value.Foreground = HudText;
        value.FontSize = 15;
        value.FontWeight = FontWeight.SemiBold;
        value.TextAlignment = TextAlignment.Right;
        value.TextTrimming = TextTrimming.CharacterEllipsis;
        value.TextWrapping = TextWrapping.NoWrap;
        value.MinWidth = 0;
        value.MaxLines = 1;
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
            FontSize = 11,
            TextWrapping = TextWrapping.NoWrap,
            VerticalAlignment = VerticalAlignment.Center,
        });
        row.Children.Add(value);
        return row;
    }

    private Control BuildDeckMetricStretchRow(string label, TextBlock value)
    {
        value.Foreground = HudText;
        value.FontSize = 15;
        value.FontWeight = FontWeight.SemiBold;
        value.TextAlignment = TextAlignment.Right;
        value.TextTrimming = TextTrimming.CharacterEllipsis;
        value.TextWrapping = TextWrapping.NoWrap;
        value.MinWidth = 0;
        value.MaxLines = 1;
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
            FontSize = 11,
            TextWrapping = TextWrapping.NoWrap,
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
        _statusTerminalSizeText.Foreground = HudMuted;
        UpdateTerminalLiveSelector();
        _shellHost.Padding = new Thickness(10, 8, 10, 10);
        _shellHost.Child = null;
        _shellHost.Child = _useCommandDeckSkin
            ? BuildCommandDeckShell()
            : BuildClassicShell();

        UpdateClassicTerminalSizeStatus();

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

        var viewCacheItem = new MenuItem { Header = "_Cache..." };
        viewCacheItem.Click += (_, _) => OnViewCache();
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
            Items  = { viewFont, viewFontSize, skinMenu, _viewCommWindow, _viewShowHaggleDetails, _viewBottomBar, new Separator(), viewCacheItem, viewBubblesItem, viewDbItem, new Separator(), _viewClearRecents },
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
                _statusHaggleFrame,
                _statusCommFrame,
                _statusStopAllFrame,
                _statusMacrosFrame,
                _statusMacroHost,
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

    private void OnViewCache()
    {
        if (_cacheWindow is { IsVisible: true })
        {
            _cacheWindow.Activate();
            return;
        }

        _cacheWindow = new CacheWindow(CaptureCacheWindowSnapshot);
        _cacheWindow.Closed += (_, _) => _cacheWindow = null;
        _cacheWindow.Show(this);
        _cacheWindow.Activate();
    }

    private CacheWindowSnapshot CaptureCacheWindowSnapshot()
    {
        Core.ScriptCacheSnapshot snapshot = Core.ScriptCmp.CaptureCacheSnapshot();
        IReadOnlyList<MTC.mombot.mombotPrewarmModule> prewarmModules = _mombot.GetPrewarmModules();
        var prewarmCommandsByPath = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (MTC.mombot.mombotPrewarmModule module in prewarmModules)
        {
            string fullPath = Path.GetFullPath(module.ScriptPath);
            if (!prewarmCommandsByPath.ContainsKey(fullPath))
                prewarmCommandsByPath[fullPath] = module.CommandName;
        }

        var preloadEntries = new List<CacheDisplayEntry>();
        var vmEntries = new List<CacheDisplayEntry>();

        foreach (Core.ScriptCacheEntrySnapshot entry in snapshot.Entries)
        {
            string fullPath = string.IsNullOrWhiteSpace(entry.ScriptPath)
                ? string.Empty
                : Path.GetFullPath(entry.ScriptPath);

            if (entry.Kind == Core.ScriptCacheKind.Compiled &&
                prewarmCommandsByPath.TryGetValue(fullPath, out string? commandName))
            {
                preloadEntries.Add(new CacheDisplayEntry(
                    commandName,
                    BuildCachePathLabel(entry.ScriptPath),
                    "preload",
                    entry.EstimatedBytes));
                continue;
            }

            vmEntries.Add(new CacheDisplayEntry(
                entry.DisplayName,
                BuildCachePathLabel(entry.ScriptPath),
                entry.Kind == Core.ScriptCacheKind.Source ? "source" : "compiled",
                entry.EstimatedBytes));
        }

        preloadEntries.Sort(static (left, right) => StringComparer.OrdinalIgnoreCase.Compare(left.Name, right.Name));
        vmEntries.Sort(static (left, right) =>
        {
            int byName = StringComparer.OrdinalIgnoreCase.Compare(left.Name, right.Name);
            return byName != 0
                ? byName
                : StringComparer.OrdinalIgnoreCase.Compare(left.Subtitle, right.Subtitle);
        });

        return new CacheWindowSnapshot(
            preloadEntries,
            vmEntries,
            preloadEntries.Sum(static entry => entry.Bytes),
            vmEntries.Sum(static entry => entry.Bytes));
    }

    private static string BuildCachePathLabel(string scriptPath)
    {
        if (string.IsNullOrWhiteSpace(scriptPath))
            return string.Empty;

        string normalized = Path.GetFullPath(scriptPath).Replace('\\', '/');
        string[] parts = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
            return normalized;

        int take = Math.Min(4, parts.Length);
        return string.Join("/", parts[^take..]);
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

        int configuredBubbleMaxSize = Math.Max(1, _embeddedGameConfig?.BubbleSize ?? Core.ModBubble.DefaultMaxBubbleSize);
        int configuredDeadEndMaxSize = Math.Max(1, _embeddedGameConfig?.DeadEndMaxSize ?? Core.ModBubble.DefaultMaxBubbleSize);
        int configuredTunnelMaxSize = Math.Max(1, _embeddedGameConfig?.TunnelMaxSize ?? Core.ModBubble.DefaultMaxBubbleSize);
        int bubbleMaxSize = Math.Min(configuredBubbleMaxSize, FinderPrewarmMaxSize);
        int deadEndMaxSize = Math.Min(configuredDeadEndMaxSize, FinderPrewarmMaxSize);
        int tunnelMaxSize = Math.Min(configuredTunnelMaxSize, FinderPrewarmMaxSize);
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
                string capMessage = configuredBubbleMaxSize != bubbleMaxSize ||
                                    configuredDeadEndMaxSize != deadEndMaxSize ||
                                    configuredTunnelMaxSize != tunnelMaxSize
                    ? $" configured=({configuredBubbleMaxSize},{configuredDeadEndMaxSize},{configuredTunnelMaxSize})"
                    : string.Empty;
                Core.GlobalModules.DebugLog(
                    $"[MTC.FinderPrewarm] start db={db.DatabasePath} bubbleMax={bubbleMaxSize} deadEndMax={deadEndMaxSize} tunnelMax={tunnelMaxSize}{capMessage} allowSeparated={allowSeparatedByGates}\n");
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

}
