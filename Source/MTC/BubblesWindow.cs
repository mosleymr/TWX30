using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Core = TWXProxy.Core;

namespace MTC;

public class BubblesWindow : Window
{
    private sealed record BubbleRow(
        Core.BubbleInfo Bubble,
        int? DistToSd,
        int? DistToSol);

    private enum BubbleSortMode
    {
        Door,
        Sectors,
        Depth,
        DistToSd,
        DistToSol,
    }

    private readonly Func<Core.ModDatabase?> _getDb;
    private readonly Action<int>? _setMaxBubbleSize;
    private readonly TextBlock _header;
    private readonly TextBlock _summary;
    private readonly StackPanel _content;
    private readonly TextBox _bubbleSizeBox;
    private Button? _sortSectorsButton;
    private Button? _sortDepthButton;
    private Button? _sortDistToSdButton;
    private Button? _sortDistToSolButton;
    private BubbleSortMode _sortMode = BubbleSortMode.Door;
    private bool _sortDescending = true;
    private int _currentBubbleSize;

    private static readonly IBrush BgWin = new SolidColorBrush(Color.FromRgb(8, 14, 20));
    private static readonly IBrush BgPanel = new SolidColorBrush(Color.FromRgb(14, 33, 42));
    private static readonly IBrush BgCard = new SolidColorBrush(Color.FromRgb(16, 53, 67));
    private static readonly IBrush BgCardAlt = new SolidColorBrush(Color.FromRgb(10, 43, 53));
    private static readonly IBrush Edge = new SolidColorBrush(Color.FromRgb(57, 112, 128));
    private static readonly IBrush InnerEdge = new SolidColorBrush(Color.FromRgb(23, 81, 94));
    private static readonly IBrush ColText = new SolidColorBrush(Color.FromRgb(222, 238, 242));
    private static readonly IBrush ColMuted = new SolidColorBrush(Color.FromRgb(126, 170, 180));
    private static readonly IBrush ColAccent = new SolidColorBrush(Color.FromRgb(0, 212, 201));
    private static readonly IBrush ColWarn = new SolidColorBrush(Color.FromRgb(255, 193, 74));
    private static readonly IBrush ColError = new SolidColorBrush(Color.FromRgb(255, 106, 106));
    private static readonly IBrush ColSuccess = new SolidColorBrush(Color.FromRgb(116, 239, 164));

    public BubblesWindow(Func<Core.ModDatabase?> getDb, Func<int> getMaxBubbleSize, Action<int>? setMaxBubbleSize = null)
    {
        _getDb = getDb;
        _setMaxBubbleSize = setMaxBubbleSize;
        _currentBubbleSize = Math.Max(1, getMaxBubbleSize());

        Title = "Bubble List";
        Width = 980;
        Height = 640;
        MinWidth = 760;
        MinHeight = 360;
        Background = BgWin;
        FontFamily = new FontFamily("Cascadia Code, Menlo, Consolas, Courier New, monospace");

        var refreshButton = new Button
        {
            Content = "Refresh",
            Padding = new Thickness(12, 5),
            Height = 34,
            VerticalAlignment = VerticalAlignment.Center,
        };
        StyleActionButton(refreshButton, primary: true);
        refreshButton.Click += async (_, _) => await RefreshFromInputAsync();

        _bubbleSizeBox = new TextBox
        {
            Text = _currentBubbleSize.ToString(),
            Width = 72,
            Height = 34,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
        };
        StyleInputBox(_bubbleSizeBox);

        var bubbleSizeLabel = new TextBlock
        {
            Text = "Max Size",
            Foreground = ColMuted,
            FontSize = 12,
            FontWeight = FontWeight.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
        };

        var actionsPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            VerticalAlignment = VerticalAlignment.Center,
            Children = { bubbleSizeLabel, _bubbleSizeBox, refreshButton },
        };

        _header = new TextBlock
        {
            Text = "Bubble list",
            Foreground = ColText,
            FontSize = 18,
            FontWeight = FontWeight.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
        };

        _summary = new TextBlock
        {
            Text = string.Empty,
            Foreground = ColMuted,
            FontSize = 13,
            Margin = new Thickness(0, 6, 0, 0),
            TextWrapping = TextWrapping.Wrap,
        };

        var headerStack = new StackPanel
        {
            Orientation = Orientation.Vertical,
            Spacing = 0,
            Children = { _header, _summary },
        };

        var refreshHost = new Border
        {
            Child = actionsPanel,
            VerticalAlignment = VerticalAlignment.Top,
        };
        DockPanel.SetDock(refreshHost, Dock.Right);

        var toolbar = new DockPanel
        {
            Background = BgPanel,
            LastChildFill = true,
            Margin = new Thickness(12, 12, 12, 8),
            Children =
            {
                refreshHost,
                headerStack,
            },
        };

        var columnHeader = BuildColumnHeader();

        _content = new StackPanel
        {
            Spacing = 8,
            Margin = new Thickness(12, 0, 12, 12),
        };

        var scroll = new ScrollViewer
        {
            Content = _content,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
        };

        var layout = new DockPanel();
        DockPanel.SetDock(toolbar, Dock.Top);
        DockPanel.SetDock(columnHeader, Dock.Top);
        layout.Children.Add(toolbar);
        layout.Children.Add(columnHeader);
        layout.Children.Add(scroll);
        Content = layout;

        Opened += async (_, _) => await RefreshAsync();
    }

    private Control BuildColumnHeader()
    {
        var grid = new Grid
        {
            Margin = new Thickness(12, 0, 12, 8),
            ColumnDefinitions = new ColumnDefinitions("120,84,84,94,94,*,Auto"),
            ColumnSpacing = 12,
        };

        AddHeaderCell(grid, "Door", 0);
        _sortSectorsButton = AddSortHeaderCell(grid, "Sectors", 1, BubbleSortMode.Sectors);
        _sortDepthButton = AddSortHeaderCell(grid, "Depth", 2, BubbleSortMode.Depth);
        _sortDistToSdButton = AddSortHeaderCell(grid, "Dist to SD", 3, BubbleSortMode.DistToSd);
        _sortDistToSolButton = AddSortHeaderCell(grid, "Dist to Sol", 4, BubbleSortMode.DistToSol);
        AddHeaderCell(grid, "Sector List", 5);
        AddHeaderCell(grid, string.Empty, 6);
        UpdateSortButtons();

        return new Border
        {
            Background = BgCardAlt,
            BorderBrush = InnerEdge,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Child = new Border
            {
                Padding = new Thickness(12, 10),
                Child = grid,
            },
        };
    }

    private static void AddHeaderCell(Grid grid, string text, int column)
    {
        var block = new TextBlock
        {
            Text = text,
            Foreground = ColMuted,
            FontSize = 12,
            FontWeight = FontWeight.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(block, column);
        grid.Children.Add(block);
    }

    private Button AddSortHeaderCell(Grid grid, string text, int column, BubbleSortMode mode)
    {
        var button = new Button
        {
            Padding = new Thickness(0),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = ColMuted,
            FontSize = 12,
            FontWeight = FontWeight.SemiBold,
        };
        button.Click += async (_, _) =>
        {
            OnSortClicked(mode);
            await RefreshAsync();
        };
        Grid.SetColumn(button, column);
        grid.Children.Add(button);
        return button;
    }

    private void OnSortClicked(BubbleSortMode mode)
    {
        if (_sortMode == mode)
        {
            _sortDescending = !_sortDescending;
        }
        else
        {
            _sortMode = mode;
            _sortDescending = true;
        }
    }

    private void UpdateSortButtons()
    {
        if (_sortSectorsButton != null)
            _sortSectorsButton.Content = BuildSortLabel("Sectors", BubbleSortMode.Sectors);

        if (_sortDepthButton != null)
            _sortDepthButton.Content = BuildSortLabel("Depth", BubbleSortMode.Depth);

        if (_sortDistToSdButton != null)
            _sortDistToSdButton.Content = BuildSortLabel("Dist to SD", BubbleSortMode.DistToSd);

        if (_sortDistToSolButton != null)
            _sortDistToSolButton.Content = BuildSortLabel("Dist to Sol", BubbleSortMode.DistToSol);
    }

    private Control BuildSortLabel(string label, BubbleSortMode mode)
    {
        string displayLabel = mode switch
        {
            BubbleSortMode.DistToSd => "Dist to\nSD",
            BubbleSortMode.DistToSol => "Dist to\nSol",
            _ => label,
        };

        string text = _sortMode != mode
            ? $"{displayLabel} ▲▼"
            : _sortDescending
                ? $"{displayLabel} ▼"
                : $"{displayLabel} ▲";

        return new TextBlock
        {
            Text = text,
            Foreground = ColMuted,
            FontSize = 12,
            FontWeight = FontWeight.SemiBold,
            TextWrapping = TextWrapping.Wrap,
            TextAlignment = TextAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
        };
    }

    private async Task RefreshAsync()
    {
        _content.Children.Clear();
        UpdateSortButtons();

        Core.ModDatabase? db = _getDb();
        if (db == null)
        {
            _header.Text = "Bubble list";
            _summary.Text = "No active database. Connect to or open a game first.";
            _summary.Foreground = ColError;
            return;
        }

        IReadOnlyList<Core.BubbleInfo> allBubbles = Core.ProxyGameOperations.GetBubbles(db, _currentBubbleSize);
        int stardockSector = ResolveStardockSector(db);
        int solSector = ResolveSolSector(db);
        List<BubbleRow> solidBubbles = SortBubbles(
            allBubbles
                .Where(bubble => !bubble.Gapped)
                .Select(bubble => BuildBubbleRow(db, bubble, stardockSector, solSector)))
            .ToList();

        _header.Text = $"Bubble list for {db.DatabaseName}";
        _summary.Text = $"{solidBubbles.Count} solid bubble(s) found.";
        _summary.Foreground = solidBubbles.Count == 0 ? ColWarn : ColMuted;

        if (solidBubbles.Count == 0)
        {
            _content.Children.Add(new Border
            {
                Background = BgPanel,
                BorderBrush = Edge,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(14, 12),
                Child = new TextBlock
                {
                    Text = "No bubbles to display yet.",
                    Foreground = ColText,
                    FontSize = 14,
                },
            });
            return;
        }

        foreach (BubbleRow bubble in solidBubbles)
            _content.Children.Add(BuildBubbleRowCard(bubble));

        await Task.CompletedTask;
    }

    private async Task RefreshFromInputAsync()
    {
        if (!TryApplyBubbleSizeInput())
            return;

        await RefreshAsync();
    }

    private bool TryApplyBubbleSizeInput()
    {
        string raw = (_bubbleSizeBox.Text ?? string.Empty).Trim();
        if (!int.TryParse(raw, out int bubbleSize) || bubbleSize < 1)
        {
            _summary.Text = "Max size must be a whole number greater than zero.";
            _summary.Foreground = ColError;
            return false;
        }

        _currentBubbleSize = bubbleSize;
        _bubbleSizeBox.Text = bubbleSize.ToString();
        _setMaxBubbleSize?.Invoke(bubbleSize);
        return true;
    }

    private IOrderedEnumerable<BubbleRow> SortBubbles(IEnumerable<BubbleRow> bubbles)
    {
        return _sortMode switch
        {
            BubbleSortMode.Sectors => _sortDescending
                ? bubbles.OrderByDescending(bubble => bubble.Bubble.Size).ThenBy(bubble => bubble.Bubble.Gate)
                : bubbles.OrderBy(bubble => bubble.Bubble.Size).ThenBy(bubble => bubble.Bubble.Gate),
            BubbleSortMode.Depth => _sortDescending
                ? bubbles.OrderByDescending(bubble => bubble.Bubble.MaxDepth).ThenBy(bubble => bubble.Bubble.Gate)
                : bubbles.OrderBy(bubble => bubble.Bubble.MaxDepth).ThenBy(bubble => bubble.Bubble.Gate),
            BubbleSortMode.DistToSd => SortByNullableDistance(bubbles, bubble => bubble.DistToSd),
            BubbleSortMode.DistToSol => SortByNullableDistance(bubbles, bubble => bubble.DistToSol),
            _ => bubbles.OrderBy(bubble => bubble.Bubble.Gate),
        };
    }

    private IOrderedEnumerable<BubbleRow> SortByNullableDistance(
        IEnumerable<BubbleRow> bubbles,
        Func<BubbleRow, int?> selector)
    {
        return _sortDescending
            ? bubbles.OrderBy(bubble => selector(bubble).HasValue ? 0 : 1)
                .ThenByDescending(bubble => selector(bubble) ?? int.MinValue)
                .ThenBy(bubble => bubble.Bubble.Gate)
            : bubbles.OrderBy(bubble => selector(bubble).HasValue ? 0 : 1)
                .ThenBy(bubble => selector(bubble) ?? int.MaxValue)
                .ThenBy(bubble => bubble.Bubble.Gate);
    }

    private static BubbleRow BuildBubbleRow(Core.ModDatabase db, Core.BubbleInfo bubble, int stardockSector, int solSector)
    {
        int? distToSd = stardockSector > 0 ? NormalizeDistance(db.GetDistance(bubble.Gate, stardockSector)) : null;
        int? distToSol = solSector > 0 ? NormalizeDistance(db.GetDistance(bubble.Gate, solSector)) : null;
        return new BubbleRow(bubble, distToSd, distToSol);
    }

    private static int? NormalizeDistance(int distance) => distance >= 0 ? distance : null;

    private static int ResolveStardockSector(Core.ModDatabase db)
    {
        int sector = db.DBHeader.StarDock;
        return sector == 0 || sector == 65535 ? 0 : sector;
    }

    private static int ResolveSolSector(Core.ModDatabase db)
    {
        for (int sectorNumber = 1; sectorNumber <= db.DBHeader.Sectors; sectorNumber++)
        {
            Core.SectorData? sector = db.GetSector(sectorNumber);
            if (sector?.SectorPort == null || sector.SectorPort.ClassIndex != 0)
                continue;

            if (string.Equals(sector.SectorPort.Name, "Sol", StringComparison.OrdinalIgnoreCase))
                return sectorNumber;
        }

        return db.DBHeader.Sectors > 0 ? 1 : 0;
    }

    private static string FormatDistance(int? distance) => distance?.ToString() ?? string.Empty;

    private Control BuildBubbleRowCard(BubbleRow bubble)
    {
        string sectorList = string.Join(" ", bubble.Bubble.Sectors);

        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("120,84,84,94,94,*,Auto"),
            ColumnSpacing = 12,
        };

        AddValueCell(grid, bubble.Bubble.Gate.ToString(), 0, ColAccent, bold: true);
        AddValueCell(grid, bubble.Bubble.Size.ToString(), 1, ColText);
        AddValueCell(grid, bubble.Bubble.MaxDepth.ToString(), 2, ColText);
        AddValueCell(grid, FormatDistance(bubble.DistToSd), 3, ColText);
        AddValueCell(grid, FormatDistance(bubble.DistToSol), 4, ColText);
        AddValueCell(grid, sectorList, 5, ColMuted, wrap: true);

        var copyButton = new Button
        {
            Content = "Copy",
            Padding = new Thickness(10, 4),
            Height = 30,
            VerticalAlignment = VerticalAlignment.Center,
        };
        StyleActionButton(copyButton, primary: false);
        copyButton.Click += async (_, _) => await CopyBubbleAsync(copyButton, sectorList);
        Grid.SetColumn(copyButton, 6);
        grid.Children.Add(copyButton);

        return new Border
        {
            Background = BgCard,
            BorderBrush = Edge,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Child = new Border
            {
                Background = BgCardAlt,
                BorderBrush = InnerEdge,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(9),
                Padding = new Thickness(12, 10),
                Child = grid,
            },
        };
    }

    private static void AddValueCell(Grid grid, string text, int column, IBrush foreground, bool bold = false, bool wrap = false)
    {
        var block = new TextBlock
        {
            Text = text,
            Foreground = foreground,
            FontSize = 13,
            FontWeight = bold ? FontWeight.SemiBold : FontWeight.Normal,
            VerticalAlignment = wrap ? VerticalAlignment.Top : VerticalAlignment.Center,
            TextWrapping = wrap ? TextWrapping.Wrap : TextWrapping.NoWrap,
        };
        Grid.SetColumn(block, column);
        grid.Children.Add(block);
    }

    private async Task CopyBubbleAsync(Button button, string sectorList)
    {
        string previous = button.Content?.ToString() ?? "Copy";
        try
        {
            var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
            if (clipboard == null)
                return;

            await clipboard.SetTextAsync(sectorList);
            button.Content = "Copied";
            button.Foreground = ColSuccess;
            await Task.Delay(900);
        }
        finally
        {
            button.Content = previous;
            button.ClearValue(Button.ForegroundProperty);
        }
    }

    private static void StyleActionButton(Button button, bool primary)
    {
        button.Background = primary ? ColAccent : BgCardAlt;
        button.Foreground = primary ? BgWin : ColText;
        button.BorderBrush = primary ? ColAccent : InnerEdge;
        button.BorderThickness = new Thickness(1);
        button.CornerRadius = new CornerRadius(8);
    }

    private static void StyleInputBox(TextBox textBox)
    {
        textBox.Background = BgCardAlt;
        textBox.BorderBrush = InnerEdge;
        textBox.BorderThickness = new Thickness(1);
        textBox.Foreground = ColText;
        textBox.CaretBrush = ColAccent;
    }
}
