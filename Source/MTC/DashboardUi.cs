using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using Avalonia;
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
            _currentShipType    = s.ShipType;
            _currentShipClass   = s.ShipClass;

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

    private void ObserveComputerShipTypeLine(string line)
    {
        string trimmed = line.Trim();

        if (_awaitingComputerShipTypeLine)
        {
            if (string.IsNullOrWhiteSpace(trimmed))
                return;

            _awaitingComputerShipTypeLine = false;

            if (!LooksLikeComputerShipTypeTitle(trimmed))
                return;

            if (string.Equals(_currentComputerShipType, trimmed, StringComparison.Ordinal))
                return;

            _currentComputerShipType = trimmed;
            Dispatcher.UIThread.Post(RefreshInfoPanels);
            return;
        }

        if (line.StartsWith("Computer command [TL=", StringComparison.OrdinalIgnoreCase) &&
            trimmed.EndsWith(";", StringComparison.Ordinal))
        {
            _awaitingComputerShipTypeLine = true;
        }
        else if (!string.IsNullOrWhiteSpace(trimmed) &&
                 TryGetMombotPromptNameFromLine(trimmed, out _))
        {
            _awaitingComputerShipTypeLine = false;
        }
    }

    private static bool LooksLikeComputerShipTypeTitle(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
            return false;

        if (line.Contains(':', StringComparison.Ordinal) ||
            line.Contains("[TL=", StringComparison.OrdinalIgnoreCase) ||
            line.Contains("<Computer", StringComparison.OrdinalIgnoreCase) ||
            line.Contains("activated", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return true;
    }

    private void ObserveOnlinePlayersLine(string line)
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            string capturedLine = line;
            Dispatcher.UIThread.Post(() => ObserveOnlinePlayersLine(capturedLine));
            return;
        }

        string trimmed = line.Trim();

        Match enteredMatch = OnlinePlayerEnteredGameRegex.Match(trimmed);
        if (enteredMatch.Success)
        {
            AddOnlinePlayer(enteredMatch.Groups[1].Value);
            _capturingOnlinePlayers = false;
            return;
        }

        Match exitedMatch = OnlinePlayerExitedGameRegex.Match(trimmed);
        if (exitedMatch.Success)
        {
            RemoveOnlinePlayer(exitedMatch.Groups[1].Value);
            _capturingOnlinePlayers = false;
            return;
        }

        if (trimmed.Equals("Who's Playing", StringComparison.OrdinalIgnoreCase))
        {
            _capturingOnlinePlayers = true;
            _onlinePlayers.Clear();
            RefreshOnlinePanel();
            return;
        }

        if (!_capturingOnlinePlayers)
            return;

        if (string.IsNullOrWhiteSpace(trimmed))
            return;

        if (TryExtractOnlinePlayerName(trimmed, out string playerName))
        {
            AddOnlinePlayer(playerName);
            return;
        }

        _capturingOnlinePlayers = false;
    }

    private static bool TryExtractOnlinePlayerName(string line, out string playerName)
    {
        playerName = string.Empty;

        Match withCorpMatch = OnlinePlayerLineWithCorpRegex.Match(line);
        if (withCorpMatch.Success)
        {
            playerName = withCorpMatch.Groups[1].Value;
            return true;
        }

        Match withoutCorpMatch = OnlinePlayerLineWithoutCorpRegex.Match(line);
        if (withoutCorpMatch.Success)
        {
            playerName = withoutCorpMatch.Groups[1].Value;
            return true;
        }

        return false;
    }

    private void AddOnlinePlayer(string playerName)
    {
        string normalizedPlayerName = playerName.Trim();
        if (string.IsNullOrWhiteSpace(normalizedPlayerName))
            return;

        if (_onlinePlayers.Any(existing => string.Equals(existing, normalizedPlayerName, StringComparison.OrdinalIgnoreCase)))
            return;

        _onlinePlayers.Add(normalizedPlayerName);
        RefreshOnlinePanel();
    }

    private void RemoveOnlinePlayer(string playerName)
    {
        string normalizedPlayerName = playerName.Trim();
        if (string.IsNullOrWhiteSpace(normalizedPlayerName))
            return;

        int removedCount = _onlinePlayers.RemoveAll(existing =>
            string.Equals(existing, normalizedPlayerName, StringComparison.OrdinalIgnoreCase));
        if (removedCount > 0)
            RefreshOnlinePanel();
    }

    private void RefreshOnlinePanel()
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(RefreshOnlinePanel);
            return;
        }

        _onlinePlayersHost.Children.Clear();
        string currentTraderName = GetCurrentTraderOnlineName();
        List<string> otherOnlinePlayers = _onlinePlayers
            .Where(playerName => !string.IsNullOrWhiteSpace(playerName) &&
                                 !string.Equals(playerName, currentTraderName, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (otherOnlinePlayers.Count == 0)
        {
            _onlinePlayersHost.Children.Add(new TextBlock
            {
                Text = "No one else is online",
                Foreground = HudMuted,
                FontSize = 11,
                FontStyle = FontStyle.Italic,
                TextWrapping = TextWrapping.Wrap,
            });
            return;
        }

        foreach (string playerName in otherOnlinePlayers)
        {
            _onlinePlayersHost.Children.Add(new TextBlock
            {
                Text = playerName,
                Foreground = HudText,
                FontSize = 11,
                FontWeight = FontWeight.SemiBold,
                TextWrapping = TextWrapping.NoWrap,
                TextTrimming = TextTrimming.CharacterEllipsis,
            });
        }
    }

    private string GetCurrentTraderOnlineName()
    {
        if (string.IsNullOrWhiteSpace(_state.TraderName))
            return string.Empty;

        string[] parts = _state.TraderName
            .Trim()
            .Split(' ', StringSplitOptions.RemoveEmptyEntries);

        return parts.Length == 0 ? string.Empty : parts[^1];
    }

    private void ClearOnlinePlayers()
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(ClearOnlinePlayers);
            return;
        }

        _capturingOnlinePlayers = false;
        _onlinePlayers.Clear();
        RefreshOnlinePanel();
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
        _shipInfoHeaderText.Text = GetShipInfoPanelTitle();
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
        RefreshOnlinePanel();
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
        IBrush fightersBrush = GetShipCapacityBrush(_state.Fighters, GetCurrentShipMaxFighters());
        IBrush shieldsBrush = GetShipCapacityBrush(_state.Shields, GetCurrentShipMaxShields());
        _valFighters.Text  = _state.Fighters.ToString("N0");
        _deckValFighters.Text = _valFighters.Text;
        _valFighters.Foreground = fightersBrush;
        _deckValFighters.Foreground = fightersBrush;
        _valShields.Text   = _state.Shields.ToString("N0");
        _deckValShields.Text = _valShields.Text;
        _valShields.Foreground = shieldsBrush;
        _deckValShields.Foreground = shieldsBrush;
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

    private string GetShipInfoPanelTitle()
    {
        if (!string.IsNullOrWhiteSpace(_currentComputerShipType))
            return _currentComputerShipType;

        return "Ship Info";
    }

    private static int? ParsePositiveIntOrNull(string value)
    {
        return int.TryParse(value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed) && parsed > 0
            ? parsed
            : null;
    }

    private int? GetCurrentShipMaxFighters()
    {
        return ParsePositiveIntOrNull(ReadCurrentMombotVar(
            "0",
            "$SHIP~SHIP_FIGHTERS_MAX",
            "$SHIP~ship_fighters_max"));
    }

    private int? GetCurrentShipMaxShields()
    {
        return ParsePositiveIntOrNull(ReadCurrentMombotVar(
            "0",
            "$SHIP~SHIP_SHIELD_MAX",
            "$SHIP~ship_shield_max"));
    }

    private static IBrush GetShipCapacityBrush(int currentValue, int? maxValue)
    {
        if (!maxValue.HasValue || maxValue.Value <= 0)
            return HudText;

        double ratio = Math.Clamp(currentValue / (double)maxValue.Value, 0d, 1d);
        if (ratio < 0.25d)
            return HudAccentWarn;
        if (ratio < 0.5d)
            return HudAccentHot;
        return HudText;
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

}
