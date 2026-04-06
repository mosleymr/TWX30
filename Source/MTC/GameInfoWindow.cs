using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Core = TWXProxy.Core;

namespace MTC;

public class GameInfoWindow : Window
{
    private readonly Func<Core.ModDatabase?> _getDb;
    private readonly TextBlock _header;
    private readonly StackPanel _content;

    private static readonly IBrush BgWin = new SolidColorBrush(Color.FromRgb(0x00, 0x00, 0x00));
    private static readonly IBrush BgPanel = new SolidColorBrush(Color.FromRgb(0x08, 0x08, 0x08));
    private static readonly IBrush ColGreen = new SolidColorBrush(Color.FromRgb(0x00, 0xff, 0x55));
    private static readonly IBrush ColCyan = new SolidColorBrush(Color.FromRgb(0x33, 0xee, 0xff));
    private static readonly IBrush ColYellow = new SolidColorBrush(Color.FromRgb(0xff, 0xee, 0x33));
    private static readonly IBrush ColBlue = new SolidColorBrush(Color.FromRgb(0x44, 0x66, 0xff));
    private static readonly IBrush ColMagenta = new SolidColorBrush(Color.FromRgb(0xff, 0x33, 0xff));
    private static readonly IBrush ColRed = new SolidColorBrush(Color.FromRgb(0xff, 0x44, 0x44));
    private static readonly IBrush ColMuted = new SolidColorBrush(Color.FromRgb(0xaa, 0xaa, 0xaa));

    public GameInfoWindow(Func<Core.ModDatabase?> getDb)
    {
        _getDb = getDb;

        Title = "Game Info";
        Width = 560;
        Height = 420;
        MinWidth = 420;
        MinHeight = 260;
        Background = BgWin;
        FontFamily = new FontFamily("Cascadia Code, Menlo, Consolas, Courier New, monospace");

        var refreshBtn = new Button
        {
            Content = "Refresh",
            Padding = new Thickness(10, 4),
            Margin = new Thickness(0, 0, 0, 0),
        };
        refreshBtn.Click += (_, _) => RefreshInfo();

        _header = new TextBlock
        {
            Text = "Game database summary",
            Foreground = ColMuted,
            Margin = new Thickness(12, 8, 12, 6),
            FontSize = 13,
        };

        _content = new StackPanel
        {
            Margin = new Thickness(12, 4, 12, 12),
            Spacing = 3,
        };

        var scroll = new ScrollViewer
        {
            Content = _content,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
        };

        var toolbar = new DockPanel
        {
            Background = BgPanel,
            LastChildFill = false,
            Margin = new Thickness(0, 0, 0, 0),
            Children =
            {
                new Border
                {
                    Padding = new Thickness(12, 8, 12, 8),
                    Child = refreshBtn,
                }
            }
        };

        var layout = new DockPanel();
        DockPanel.SetDock(toolbar, Dock.Top);
        DockPanel.SetDock(_header, Dock.Top);
        layout.Children.Add(toolbar);
        layout.Children.Add(_header);
        layout.Children.Add(scroll);
        Content = layout;

        Opened += (_, _) => RefreshInfo();
    }

    private void RefreshInfo()
    {
        _content.Children.Clear();

        Core.ModDatabase? db = _getDb();
        if (db == null)
        {
            _header.Text = "No active database. Connect to a game first.";
            _header.Foreground = ColRed;
            return;
        }

        int totalSectors = db.DBHeader.Sectors > 0 ? db.DBHeader.Sectors : db.MaxSectorSeen;
        if (totalSectors <= 0)
        {
            _header.Text = "Universe size is not known yet.";
            _header.Foreground = ColRed;
            return;
        }

        int knownPorts = 0;
        int visitedSectors = 0;
        int knownSectors = 0;
        int solSector = 0;
        int rylosSector = 0;
        int alphaSector = 0;

        for (int sectorNumber = 1; sectorNumber <= totalSectors; sectorNumber++)
        {
            Core.SectorData? sector = db.GetSector(sectorNumber);
            if (sector == null)
                continue;

            if (sector.Explored != Core.ExploreType.No)
                knownSectors++;
            if (sector.Explored == Core.ExploreType.Yes)
                visitedSectors++;

            if (sector.SectorPort == null || string.IsNullOrWhiteSpace(sector.SectorPort.Name))
                continue;

            knownPorts++;

            if (sector.SectorPort.ClassIndex == 0)
            {
                if (string.Equals(sector.SectorPort.Name, "Sol", StringComparison.OrdinalIgnoreCase))
                    solSector = sectorNumber;
                else if (string.Equals(sector.SectorPort.Name, "Rylos", StringComparison.OrdinalIgnoreCase))
                    rylosSector = sectorNumber;
                else if (string.Equals(sector.SectorPort.Name, "Alpha Centauri", StringComparison.OrdinalIgnoreCase))
                    alphaSector = sectorNumber;
            }
        }

        if (rylosSector == 0 && db.DBHeader.Rylos != 0 && db.DBHeader.Rylos != 65535)
            rylosSector = db.DBHeader.Rylos;
        if (alphaSector == 0 && db.DBHeader.AlphaCentauri != 0 && db.DBHeader.AlphaCentauri != 65535)
            alphaSector = db.DBHeader.AlphaCentauri;

        int stardockSector = (db.DBHeader.StarDock != 0 && db.DBHeader.StarDock != 65535) ? db.DBHeader.StarDock : 0;
        string stardockName = "-";
        if (stardockSector > 0)
            stardockName = db.GetSector(stardockSector)?.SectorPort?.Name ?? "StarDock";

        Core.ITWXDatabase? previousDb = Core.GlobalModules.TWXDatabase;
        Core.GlobalModules.TWXDatabase = db;
        var bubbleModule = Core.GlobalModules.TWXBubble as Core.ModBubble ?? new Core.ModBubble();
        if (Core.GlobalModules.TWXBubble == null)
            Core.GlobalModules.TWXBubble = bubbleModule;
        (int totalBubbles, _, _) = bubbleModule.GetBubbleCounts();
        Core.GlobalModules.TWXDatabase = previousDb;

        _header.Text = $"Database: {db.DatabaseName}";
        _header.Foreground = ColMuted;

        AddLine("StarDock location:", FormatLocation(stardockSector, stardockName), ColGreen);
        AddLine("Sol location:", FormatSector(solSector), ColCyan);
        AddLine("Rylos location:", FormatSector(rylosSector), ColCyan);
        AddLine("Alpha Centauri location:", FormatSector(alphaSector), ColCyan);
        AddSpacer();
        AddLine("Known ports.......:", knownPorts.ToString(), ColYellow);
        AddLine("Known bubbles.....:", totalBubbles.ToString(), ColBlue);
        AddLine("Visited sectors...:", $"{visitedSectors} ({FormatPercent(visitedSectors, totalSectors)})", ColCyan);
        AddLine("Known sectors.....:", $"{knownSectors} ({FormatPercent(knownSectors, totalSectors)})", ColMagenta);
        AddLine("Number of sectors.:", totalSectors.ToString(), ColRed);
    }

    private void AddSpacer()
    {
        _content.Children.Add(new Border { Height = 8 });
    }

    private void AddLine(string label, string value, IBrush valueColor)
    {
        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("260,*"),
        };

        var labelBlock = new TextBlock
        {
            Text = label,
            Foreground = valueColor,
            FontSize = 14,
        };

        var valueBlock = new TextBlock
        {
            Text = value,
            Foreground = valueColor,
            FontSize = 14,
        };

        Grid.SetColumn(labelBlock, 0);
        Grid.SetColumn(valueBlock, 1);
        grid.Children.Add(labelBlock);
        grid.Children.Add(valueBlock);
        _content.Children.Add(grid);
    }

    private static string FormatLocation(int sector, string name)
    {
        if (sector <= 0)
            return "-";
        return string.IsNullOrWhiteSpace(name) || name == "-"
            ? sector.ToString()
            : $"{sector} ({name})";
    }

    private static string FormatSector(int sector) => sector > 0 ? sector.ToString() : "-";

    private static string FormatPercent(int value, int total)
    {
        if (total <= 0)
            return "0.00%";
        return $"{(value * 100.0 / total):0.00}%";
    }
}
