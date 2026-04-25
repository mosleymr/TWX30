using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Core = TWXProxy.Core;

namespace MTC;

public class GameInfoWindow : Window
{
    private enum OwnershipFilter
    {
        All,
        Mine,
        Enemy
    }

    private sealed record FighterRow(int Sector, string Owner, int Quantity);
    private sealed record PlanetRow(
        int Sector,
        int? PlanetId,
        string Name,
        string Owner,
        string LevelDisplay,
        int? LevelSort,
        int? Fighters,
        int? FuelOre,
        int? Organics,
        int? Equipment);
    private sealed record PlanetSighting(string Name, bool Shielded);
    private sealed record PortRow(
        int Sector,
        string Name,
        string PortClass,
        int PortClassSort,
        string Mcic,
        int? McicSort);

    private readonly Func<Core.ModDatabase?> _getDb;
    private readonly Func<GameState?> _getState;
    private readonly TextBlock _header;
    private readonly StackPanel _overviewContent;
    private readonly StackPanel _fightersContent;
    private readonly StackPanel _planetsContent;
    private readonly StackPanel _portsContent;

    private OwnershipFilter _fighterFilter = OwnershipFilter.All;
    private OwnershipFilter _planetFilter = OwnershipFilter.All;
    private string _fighterSortColumn = "sector";
    private bool _fighterSortDescending;
    private string _planetSortColumn = "sector";
    private bool _planetSortDescending;
    private string _portSortColumn = "sector";
    private bool _portSortDescending;

    private static readonly IBrush BgWin = new SolidColorBrush(Color.FromRgb(0x00, 0x00, 0x00));
    private static readonly IBrush BgPanel = new SolidColorBrush(Color.FromRgb(0x08, 0x08, 0x08));
    private static readonly IBrush BgRowAlt = new SolidColorBrush(Color.FromRgb(0x11, 0x11, 0x11));
    private static readonly IBrush BgHeader = new SolidColorBrush(Color.FromRgb(0x14, 0x14, 0x14));
    private static readonly IBrush BgActive = new SolidColorBrush(Color.FromRgb(0x14, 0x3f, 0x7a));
    private static readonly IBrush ColBorder = new SolidColorBrush(Color.FromRgb(0x2d, 0x2d, 0x2d));
    private static readonly IBrush ColGreen = new SolidColorBrush(Color.FromRgb(0x00, 0xff, 0x55));
    private static readonly IBrush ColCyan = new SolidColorBrush(Color.FromRgb(0x33, 0xee, 0xff));
    private static readonly IBrush ColYellow = new SolidColorBrush(Color.FromRgb(0xff, 0xee, 0x33));
    private static readonly IBrush ColBlue = new SolidColorBrush(Color.FromRgb(0x44, 0x66, 0xff));
    private static readonly IBrush ColMagenta = new SolidColorBrush(Color.FromRgb(0xff, 0x33, 0xff));
    private static readonly IBrush ColRed = new SolidColorBrush(Color.FromRgb(0xff, 0x44, 0x44));
    private static readonly IBrush ColMuted = new SolidColorBrush(Color.FromRgb(0xaa, 0xaa, 0xaa));
    private static readonly IBrush ColText = new SolidColorBrush(Color.FromRgb(0xe6, 0xe6, 0xe6));

    public GameInfoWindow(Func<Core.ModDatabase?> getDb, Func<GameState?> getState)
    {
        _getDb = getDb;
        _getState = getState;

        Title = "Game Info";
        Width = 1120;
        Height = 760;
        MinWidth = 880;
        MinHeight = 520;
        Background = BgWin;
        FontFamily = new FontFamily("Cascadia Code, Menlo, Consolas, Courier New, monospace");

        var refreshBtn = new Button
        {
            Content = "Refresh",
            Padding = new Thickness(10, 4),
        };
        refreshBtn.Click += (_, _) => RefreshInfo();

        _header = new TextBlock
        {
            Text = "Game database summary",
            Foreground = ColMuted,
            Margin = new Thickness(12, 8, 12, 6),
            FontSize = 13,
        };

        _overviewContent = new StackPanel { Margin = new Thickness(12), Spacing = 3 };
        _fightersContent = new StackPanel { Margin = new Thickness(12), Spacing = 8 };
        _planetsContent = new StackPanel { Margin = new Thickness(12), Spacing = 8 };
        _portsContent = new StackPanel { Margin = new Thickness(12), Spacing = 8 };

        var tabs = new TabControl
        {
            ItemsSource = new object[]
            {
                new TabItem
                {
                    Header = "Overview",
                    Content = new ScrollViewer
                    {
                        Content = _overviewContent,
                        VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
                        HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
                    }
                },
                new TabItem
                {
                    Header = "Fighters",
                    Content = new ScrollViewer
                    {
                        Content = _fightersContent,
                        VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
                        HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
                    }
                },
                new TabItem
                {
                    Header = "Planets",
                    Content = new ScrollViewer
                    {
                        Content = _planetsContent,
                        VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
                        HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
                    }
                },
                new TabItem
                {
                    Header = "Ports",
                    Content = new ScrollViewer
                    {
                        Content = _portsContent,
                        VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
                        HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
                    }
                }
            }
        };

        var toolbar = new DockPanel
        {
            Background = BgPanel,
            LastChildFill = false,
            Children =
            {
                new Border
                {
                    Padding = new Thickness(12, 8, 12, 8),
                    Child = refreshBtn,
                }
            }
        };

        Content = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,Auto,*"),
            Children =
            {
                toolbar,
                _header,
                tabs
            }
        };
        Grid.SetRow(toolbar, 0);
        Grid.SetRow(_header, 1);
        Grid.SetRow(tabs, 2);

        Opened += (_, _) => RefreshInfo();
    }

    private void RefreshInfo()
    {
        _overviewContent.Children.Clear();
        _fightersContent.Children.Clear();
        _planetsContent.Children.Clear();
        _portsContent.Children.Clear();

        Core.ModDatabase? db = _getDb();
        if (db == null)
        {
            _header.Text = "No active database. Connect to a game first.";
            _header.Foreground = ColRed;
            RenderEmptyTab(_overviewContent, "No active database.");
            RenderEmptyTab(_fightersContent, "No active database.");
            RenderEmptyTab(_planetsContent, "No active database.");
            RenderEmptyTab(_portsContent, "No active database.");
            return;
        }

        int totalSectors = db.DBHeader.Sectors > 0 ? db.DBHeader.Sectors : db.MaxSectorSeen;
        if (totalSectors <= 0)
        {
            _header.Text = "Universe size is not known yet.";
            _header.Foreground = ColRed;
            RenderEmptyTab(_overviewContent, "Universe size is not known yet.");
            RenderEmptyTab(_fightersContent, "Universe size is not known yet.");
            RenderEmptyTab(_planetsContent, "Universe size is not known yet.");
            RenderEmptyTab(_portsContent, "Universe size is not known yet.");
            return;
        }

        _header.Text = $"Database: {db.DatabaseName}";
        _header.Foreground = ColMuted;

        RenderOverview(db, totalSectors);
        RenderFighters(db, totalSectors);
        RenderPlanets(db, totalSectors);
        RenderPorts(db, totalSectors);
    }

    private void RenderOverview(Core.ModDatabase db, int totalSectors)
    {
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

        AddOverviewLine("StarDock location:", FormatLocation(stardockSector, stardockName), ColGreen);
        AddOverviewLine("Sol location:", FormatSector(solSector), ColCyan);
        AddOverviewLine("Rylos location:", FormatSector(rylosSector), ColCyan);
        AddOverviewLine("Alpha Centauri location:", FormatSector(alphaSector), ColCyan);
        AddOverviewSpacer();
        AddOverviewLine("Known ports.......:", knownPorts.ToString(), ColYellow);
        AddOverviewLine("Known bubbles.....:", totalBubbles.ToString(), ColBlue);
        AddOverviewLine("Visited sectors...:", $"{visitedSectors} ({FormatPercent(visitedSectors, totalSectors)})", ColCyan);
        AddOverviewLine("Known sectors.....:", $"{knownSectors} ({FormatPercent(knownSectors, totalSectors)})", ColMagenta);
        AddOverviewLine("Number of sectors.:", totalSectors.ToString(), ColRed);
    }

    private void RenderFighters(Core.ModDatabase db, int totalSectors)
    {
        var rows = new List<FighterRow>();
        for (int sectorNumber = 1; sectorNumber <= totalSectors; sectorNumber++)
        {
            Core.SectorData? sector = db.GetSector(sectorNumber);
            if (sector?.Fighters == null || sector.Fighters.Quantity <= 0)
                continue;

            rows.Add(new FighterRow(
                sectorNumber,
                string.IsNullOrWhiteSpace(sector.Fighters.Owner) ? "-" : sector.Fighters.Owner,
                sector.Fighters.Quantity));
        }

        GameState? state = _getState();
        rows = ApplyOwnershipFilter(rows, _fighterFilter, r => r.Owner, state)
            .ToList();

        rows = SortFighters(rows).ToList();

        _fightersContent.Children.Add(BuildFilterBar(
            "Fighter Filter",
            _fighterFilter,
            filter =>
            {
                _fighterFilter = filter;
                RefreshInfo();
            }));

        _fightersContent.Children.Add(BuildTableHeader(
            "100,*,140",
            (HeaderLabel("Sector", _fighterSortColumn == "sector", _fighterSortDescending), false, () => ToggleSort("fighter", "sector")),
            (HeaderLabel("Owner", _fighterSortColumn == "owner", _fighterSortDescending), false, () => ToggleSort("fighter", "owner")),
            (HeaderLabel("Number", _fighterSortColumn == "number", _fighterSortDescending), true, () => ToggleSort("fighter", "number"))));

        if (rows.Count == 0)
        {
            RenderEmptyTab(_fightersContent, "No matching fighter records.");
            return;
        }

        var rowsPanel = new StackPanel { Spacing = 2 };
        for (int i = 0; i < rows.Count; i++)
        {
            FighterRow row = rows[i];
            rowsPanel.Children.Add(BuildDataRow(
                "100,*,140",
                i,
                (row.Sector.ToString(), false),
                (row.Owner, false),
                (row.Quantity.ToString("N0"), true)));
        }

        _fightersContent.Children.Add(rowsPanel);
    }

    private void RenderPlanets(Core.ModDatabase db, int totalSectors)
    {
        var rows = new List<PlanetRow>();
        var sightingsBySector = new Dictionary<int, List<PlanetSighting>>();

        for (int sectorNumber = 1; sectorNumber <= totalSectors; sectorNumber++)
        {
            Core.SectorData? sector = db.GetSector(sectorNumber);
            if (sector == null || sector.PlanetNames.Count == 0)
                continue;

            sightingsBySector[sectorNumber] = sector.PlanetNames
                .Select(ParsePlanetSighting)
                .ToList();
        }

        var planetsBySector = db.GetAllPlanets()
            .Where(planet => planet.LastSector > 0)
            .GroupBy(planet => planet.LastSector)
            .ToDictionary(
                group => group.Key,
                group => group
                    .OrderBy(planet => planet.ObservedOrder > 0 ? planet.ObservedOrder : int.MaxValue)
                    .ThenBy(planet => planet.Id)
                    .ToList());

        var sectorNumbers = new HashSet<int>(sightingsBySector.Keys);
        foreach (int sector in planetsBySector.Keys)
            sectorNumbers.Add(sector);

        foreach (int sectorNumber in sectorNumbers.OrderBy(sector => sector))
        {
            planetsBySector.TryGetValue(sectorNumber, out List<Core.Planet>? knownPlanets);
            sightingsBySector.TryGetValue(sectorNumber, out List<PlanetSighting>? sightings);

            knownPlanets ??= new List<Core.Planet>();
            sightings ??= new List<PlanetSighting>();

            int slotCount = sightings.Count > 0 ? sightings.Count : knownPlanets.Count;
            for (int i = 0; i < slotCount; i++)
            {
                Core.Planet? planet = i < knownPlanets.Count ? knownPlanets[i] : null;
                PlanetSighting? sighting = i < sightings.Count ? sightings[i] : null;

                string name = planet == null || string.IsNullOrWhiteSpace(planet.Name) ? "." : planet.Name;
                if (string.IsNullOrWhiteSpace(name) || name == ".")
                    name = sighting?.Name ?? ".";

                int? levelSort = planet != null && planet.Level > 0 ? planet.Level : null;
                string levelDisplay = planet != null && planet.Level > 0 ? planet.Level.ToString() : "-";
                if (!levelSort.HasValue && ((planet?.Shielded == true) || sighting?.Shielded == true))
                {
                    levelSort = 5;
                    levelDisplay = "5+";
                }

                rows.Add(new PlanetRow(
                    sectorNumber,
                    planet != null && planet.Id > 0 ? planet.Id : null,
                    name,
                    planet == null || string.IsNullOrWhiteSpace(planet.Owner) ? "-" : planet.Owner,
                    levelDisplay,
                    levelSort,
                    planet != null && planet.Fighters >= 0 ? planet.Fighters : null,
                    planet != null && planet.FuelOre >= 0 ? planet.FuelOre : null,
                    planet != null && planet.Organics >= 0 ? planet.Organics : null,
                    planet != null && planet.Equipment >= 0 ? planet.Equipment : null));
            }
        }

        GameState? state = _getState();
        rows = ApplyOwnershipFilter(rows, _planetFilter, r => r.Owner, state)
            .ToList();
        rows = SortPlanets(rows).ToList();

        _planetsContent.Children.Add(BuildFilterBar(
            "Planet Filter",
            _planetFilter,
            filter =>
            {
                _planetFilter = filter;
                RefreshInfo();
            }));

        _planetsContent.Children.Add(BuildTableHeader(
            "80,70,220,160,80,100,90,90,90",
            (HeaderLabel("Sector", _planetSortColumn == "sector", _planetSortDescending), false, () => ToggleSort("planet", "sector")),
            (HeaderLabel("#", _planetSortColumn == "id", _planetSortDescending), true, () => ToggleSort("planet", "id")),
            (HeaderLabel("Name", _planetSortColumn == "name", _planetSortDescending), false, () => ToggleSort("planet", "name")),
            (HeaderLabel("Owner", _planetSortColumn == "owner", _planetSortDescending), false, () => ToggleSort("planet", "owner")),
            (HeaderLabel("Lvl", _planetSortColumn == "level", _planetSortDescending), true, () => ToggleSort("planet", "level")),
            (HeaderLabel("Figs", _planetSortColumn == "fighters", _planetSortDescending), true, () => ToggleSort("planet", "fighters")),
            (HeaderLabel("Ore", _planetSortColumn == "ore", _planetSortDescending), true, () => ToggleSort("planet", "ore")),
            (HeaderLabel("Org", _planetSortColumn == "org", _planetSortDescending), true, () => ToggleSort("planet", "org")),
            (HeaderLabel("Equ", _planetSortColumn == "equ", _planetSortDescending), true, () => ToggleSort("planet", "equ"))));

        if (rows.Count == 0)
        {
            RenderEmptyTab(_planetsContent, "No matching planets.");
            return;
        }

        var rowsPanel = new StackPanel { Spacing = 2 };
        for (int i = 0; i < rows.Count; i++)
        {
            PlanetRow row = rows[i];
            rowsPanel.Children.Add(BuildDataRow(
                "80,70,220,160,80,100,90,90,90",
                i,
                (row.Sector > 0 ? row.Sector.ToString() : "-", true),
                (row.PlanetId.HasValue ? row.PlanetId.Value.ToString() : "-", true),
                (row.Name, false),
                (row.Owner, false),
                (row.LevelDisplay, true),
                (FormatNullable(row.Fighters), true),
                (FormatNullable(row.FuelOre), true),
                (FormatNullable(row.Organics), true),
                (FormatNullable(row.Equipment), true)));
        }

        _planetsContent.Children.Add(rowsPanel);
    }

    private void RenderPorts(Core.ModDatabase db, int totalSectors)
    {
        var rows = new List<PortRow>();
        for (int sectorNumber = 1; sectorNumber <= totalSectors; sectorNumber++)
        {
            Core.SectorData? sector = db.GetSector(sectorNumber);
            Core.Port? port = sector?.SectorPort;
            if (port == null || port.Dead || string.IsNullOrWhiteSpace(port.Name))
                continue;

            (string mcic, int? sortKey) = GetPortMcic(sector!);
            rows.Add(new PortRow(
                sectorNumber,
                port.Name,
                FormatPortClass(port),
                port.ClassIndex,
                mcic,
                sortKey));
        }

        rows = SortPorts(rows).ToList();

        _portsContent.Children.Add(BuildTableHeader(
            "80,260,140,180",
            (HeaderLabel("Sector", _portSortColumn == "sector", _portSortDescending), false, () => ToggleSort("port", "sector")),
            (HeaderLabel("Port Name", _portSortColumn == "name", _portSortDescending), false, () => ToggleSort("port", "name")),
            (HeaderLabel("Port Class", _portSortColumn == "class", _portSortDescending), false, () => ToggleSort("port", "class")),
            (HeaderLabel("MCIC", _portSortColumn == "mcic", _portSortDescending), false, () => ToggleSort("port", "mcic"))));

        if (rows.Count == 0)
        {
            RenderEmptyTab(_portsContent, "No known ports.");
            return;
        }

        var rowsPanel = new StackPanel { Spacing = 2 };
        for (int i = 0; i < rows.Count; i++)
        {
            PortRow row = rows[i];
            rowsPanel.Children.Add(BuildDataRow(
                "80,260,140,180",
                i,
                (row.Sector.ToString(), true),
                (row.Name, false),
                (row.PortClass, false),
                (row.Mcic, false)));
        }

        _portsContent.Children.Add(rowsPanel);
    }

    private IEnumerable<FighterRow> SortFighters(IEnumerable<FighterRow> rows) => _fighterSortColumn switch
    {
        "owner" => _fighterSortDescending
            ? rows.OrderByDescending(r => r.Owner, StringComparer.OrdinalIgnoreCase).ThenByDescending(r => r.Sector)
            : rows.OrderBy(r => r.Owner, StringComparer.OrdinalIgnoreCase).ThenBy(r => r.Sector),
        "number" => _fighterSortDescending
            ? rows.OrderByDescending(r => r.Quantity).ThenByDescending(r => r.Sector)
            : rows.OrderBy(r => r.Quantity).ThenBy(r => r.Sector),
        _ => _fighterSortDescending
            ? rows.OrderByDescending(r => r.Sector)
            : rows.OrderBy(r => r.Sector)
    };

    private IEnumerable<PlanetRow> SortPlanets(IEnumerable<PlanetRow> rows) => _planetSortColumn switch
    {
        "name" => _planetSortDescending
            ? rows.OrderByDescending(r => r.Name, StringComparer.OrdinalIgnoreCase).ThenByDescending(r => r.Sector).ThenByDescending(r => r.PlanetId ?? int.MinValue)
            : rows.OrderBy(r => r.Name, StringComparer.OrdinalIgnoreCase).ThenBy(r => r.Sector).ThenBy(r => r.PlanetId ?? int.MaxValue),
        "owner" => _planetSortDescending
            ? rows.OrderByDescending(r => r.Owner, StringComparer.OrdinalIgnoreCase).ThenByDescending(r => r.Sector).ThenByDescending(r => r.PlanetId ?? int.MinValue)
            : rows.OrderBy(r => r.Owner, StringComparer.OrdinalIgnoreCase).ThenBy(r => r.Sector).ThenBy(r => r.PlanetId ?? int.MaxValue),
        "id" => SortNullable(rows, r => r.PlanetId, _planetSortDescending, r => r.Sector),
        "level" => SortNullable(rows, r => r.LevelSort, _planetSortDescending, r => r.Sector),
        "fighters" => SortNullable(rows, r => r.Fighters, _planetSortDescending, r => r.Sector),
        "ore" => SortNullable(rows, r => r.FuelOre, _planetSortDescending, r => r.Sector),
        "org" => SortNullable(rows, r => r.Organics, _planetSortDescending, r => r.Sector),
        "equ" => SortNullable(rows, r => r.Equipment, _planetSortDescending, r => r.Sector),
        _ => _planetSortDescending
            ? rows.OrderByDescending(r => r.Sector).ThenByDescending(r => r.PlanetId ?? int.MinValue).ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
            : rows.OrderBy(r => r.Sector).ThenBy(r => r.PlanetId ?? int.MaxValue).ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
    };

    private IEnumerable<PortRow> SortPorts(IEnumerable<PortRow> rows) => _portSortColumn switch
    {
        "name" => _portSortDescending
            ? rows.OrderByDescending(r => r.Name, StringComparer.OrdinalIgnoreCase).ThenByDescending(r => r.Sector)
            : rows.OrderBy(r => r.Name, StringComparer.OrdinalIgnoreCase).ThenBy(r => r.Sector),
        "class" => _portSortDescending
            ? rows.OrderByDescending(r => r.PortClassSort).ThenByDescending(r => r.Name, StringComparer.OrdinalIgnoreCase)
            : rows.OrderBy(r => r.PortClassSort).ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase),
        "mcic" => SortNullable(rows, r => r.McicSort, _portSortDescending, r => r.Sector),
        _ => _portSortDescending
            ? rows.OrderByDescending(r => r.Sector)
            : rows.OrderBy(r => r.Sector)
    };

    private static IEnumerable<T> SortNullable<T>(
        IEnumerable<T> rows,
        Func<T, int?> keySelector,
        bool descending,
        Func<T, int> tieBreaker)
    {
        return descending
            ? rows.OrderByDescending(r => keySelector(r).HasValue)
                  .ThenByDescending(r => keySelector(r) ?? int.MinValue)
                  .ThenByDescending(tieBreaker)
            : rows.OrderBy(r => keySelector(r).HasValue ? 0 : 1)
                  .ThenBy(r => keySelector(r) ?? int.MaxValue)
                  .ThenBy(tieBreaker);
    }

    private IEnumerable<T> ApplyOwnershipFilter<T>(
        IEnumerable<T> rows,
        OwnershipFilter filter,
        Func<T, string> ownerSelector,
        GameState? state)
    {
        return filter switch
        {
            OwnershipFilter.Mine => rows.Where(row => IsFriendlyOwner(ownerSelector(row), state)),
            OwnershipFilter.Enemy => rows.Where(row =>
                !string.IsNullOrWhiteSpace(ownerSelector(row)) &&
                ownerSelector(row) != "-" &&
                !IsFriendlyOwner(ownerSelector(row), state)),
            _ => rows
        };
    }

    private static string FormatNullable(int? value) => value.HasValue ? value.Value.ToString("N0") : "-";

    private static PlanetSighting ParsePlanetSighting(string raw)
    {
        string normalized = NormalizePlanetName(raw);
        bool shielded = raw.Contains("(Shielded)", StringComparison.OrdinalIgnoreCase);
        return new PlanetSighting(normalized, shielded);
    }

    private static string NormalizePlanetName(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return ".";

        string normalized = raw.Trim();
        normalized = normalized.Replace("<<<<", string.Empty, StringComparison.Ordinal);
        normalized = normalized.Replace(">>>>", string.Empty, StringComparison.Ordinal);
        normalized = Regex.Replace(normalized, @"\s*\(Shielded\)\s*$", string.Empty, RegexOptions.IgnoreCase);
        normalized = Regex.Replace(normalized, @"^\([A-Z]\)\s*", string.Empty, RegexOptions.IgnoreCase);
        normalized = normalized.Trim();
        return string.IsNullOrWhiteSpace(normalized) ? "." : normalized;
    }

    private static bool IsFriendlyOwner(string owner, GameState? state)
    {
        if (string.IsNullOrWhiteSpace(owner) || owner == "-")
            return false;

        string trimmed = owner.Trim();
        if (trimmed.Equals("belong to your Corp", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Equals("yours", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (state != null)
        {
            if (state.Corp > 0 && trimmed.Contains($"[{state.Corp}]", StringComparison.OrdinalIgnoreCase))
                return true;

            if (!string.IsNullOrWhiteSpace(state.TraderName) &&
                trimmed.Contains(state.TraderName, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private void ToggleSort(string table, string column)
    {
        switch (table)
        {
            case "fighter":
                (_fighterSortColumn, _fighterSortDescending) = ToggleSortState(_fighterSortColumn, _fighterSortDescending, column);
                break;
            case "planet":
                (_planetSortColumn, _planetSortDescending) = ToggleSortState(_planetSortColumn, _planetSortDescending, column);
                break;
            case "port":
                (_portSortColumn, _portSortDescending) = ToggleSortState(_portSortColumn, _portSortDescending, column);
                break;
        }

        RefreshInfo();
    }

    private static (string column, bool descending) ToggleSortState(string currentColumn, bool currentDescending, string nextColumn)
    {
        if (string.Equals(currentColumn, nextColumn, StringComparison.OrdinalIgnoreCase))
            return (currentColumn, !currentDescending);

        return (nextColumn, false);
    }

    private static string HeaderLabel(string label, bool active, bool descending)
    {
        if (!active)
            return label;
        return descending ? $"{label} ▼" : $"{label} ▲";
    }

    private Border BuildFilterBar(string label, OwnershipFilter activeFilter, Action<OwnershipFilter> onChange)
    {
        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Children =
            {
                new TextBlock
                {
                    Text = label,
                    Foreground = ColMuted,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 2, 8, 0),
                },
                BuildFilterButton("All", activeFilter == OwnershipFilter.All, () => onChange(OwnershipFilter.All)),
                BuildFilterButton("Mine", activeFilter == OwnershipFilter.Mine, () => onChange(OwnershipFilter.Mine)),
                BuildFilterButton("Enemy", activeFilter == OwnershipFilter.Enemy, () => onChange(OwnershipFilter.Enemy)),
            }
        };

        return new Border
        {
            Background = BgPanel,
            BorderBrush = ColBorder,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(10, 8),
            Child = row,
        };
    }

    private Button BuildFilterButton(string label, bool active, Action onClick)
    {
        var button = new Button
        {
            Content = label,
            Padding = new Thickness(10, 3),
            Background = active ? BgActive : BgHeader,
            Foreground = active ? Brushes.White : ColMuted,
            BorderBrush = active ? ColBlue : ColBorder,
            BorderThickness = new Thickness(1),
            MinWidth = 70,
        };
        button.Click += (_, _) => onClick();
        return button;
    }

    private Border BuildTableHeader(string columns, params (string text, bool rightAlign, Action onClick)[] headers)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions(columns) };
        for (int i = 0; i < headers.Length; i++)
        {
            var header = headers[i];
            var text = new TextBlock
            {
                Text = header.text,
                Foreground = ColCyan,
                HorizontalAlignment = header.rightAlign ? HorizontalAlignment.Right : HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(6, 4),
            };

            var hitTarget = new Border
            {
                Background = Brushes.Transparent,
                Child = text
            };
            hitTarget.PointerPressed += (_, _) => header.onClick();

            Grid.SetColumn(hitTarget, i);
            grid.Children.Add(hitTarget);
        }

        return new Border
        {
            Background = BgHeader,
            BorderBrush = ColBorder,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Child = grid,
        };
    }

    private Border BuildDataRow(string columns, int index, params (string text, bool rightAlign)[] cells)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions(columns) };
        for (int i = 0; i < cells.Length; i++)
        {
            var cell = cells[i];
            var text = new TextBlock
            {
                Text = cell.text,
                Foreground = ColText,
                TextTrimming = TextTrimming.CharacterEllipsis,
                HorizontalAlignment = cell.rightAlign ? HorizontalAlignment.Right : HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(6, 4),
            };
            Grid.SetColumn(text, i);
            grid.Children.Add(text);
        }

        return new Border
        {
            Background = index % 2 == 0 ? BgPanel : BgRowAlt,
            BorderBrush = ColBorder,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Child = grid,
        };
    }

    private void RenderEmptyTab(Panel panel, string message)
    {
        panel.Children.Add(new TextBlock
        {
            Text = message,
            Foreground = ColMuted,
            Margin = new Thickness(0, 8, 0, 0),
        });
    }

    private void AddOverviewSpacer()
    {
        _overviewContent.Children.Add(new Border { Height = 8 });
    }

    private void AddOverviewLine(string label, string value, IBrush valueColor)
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
        _overviewContent.Children.Add(grid);
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

    private static string FormatPortClass(Core.Port port)
    {
        if (port.ClassIndex == 9)
            return "StarDock";

        if (port.ClassIndex == 0)
            return string.IsNullOrWhiteSpace(port.Name) ? "Special" : port.Name;

        char f = port.BuyProduct.TryGetValue(Core.ProductType.FuelOre, out bool buyFuel) && buyFuel ? 'B' : 'S';
        char o = port.BuyProduct.TryGetValue(Core.ProductType.Organics, out bool buyOrg) && buyOrg ? 'B' : 'S';
        char e = port.BuyProduct.TryGetValue(Core.ProductType.Equipment, out bool buyEquip) && buyEquip ? 'B' : 'S';
        return $"{port.ClassIndex} ({f}{o}{e})";
    }

    private static (string display, int? sortKey) GetPortMcic(Core.SectorData sector)
    {
        var values = new List<(string label, int value)>();
        foreach ((string key, string label) in new[] { ("OREMCIC", "O"), ("ORGMCIC", "G"), ("EQUMCIC", "E") })
        {
            if (sector.Variables.TryGetValue(key, out string? raw) &&
                int.TryParse(raw, out int value))
            {
                values.Add((label, value));
            }
        }

        if (values.Count == 0)
            return ("-", null);

        if (values.Count == 1)
            return (values[0].value.ToString(), values[0].value);

        string display = string.Join(" ", values.Select(v => $"{v.label}:{v.value}"));
        int sortKey = (int)Math.Round(values.Average(v => v.value));
        return (display, sortKey);
    }
}
