using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Core = TWXProxy.Core;

namespace MTC;

public sealed class DataMiningWindow : Window
{
    private enum NumericOperator
    {
        GreaterThan,
        GreaterThanOrEqual,
        Equal,
        LessThanOrEqual,
        LessThan,
        NotEqual
    }

    private enum ProductIntent
    {
        Any,
        Buy,
        Sell
    }

    private enum PortSearchMode
    {
        Single,
        Pair
    }

    private enum SectorDisplayLineKind
    {
        Generic,
        Beacon,
        Sector,
        Port,
        Planet,
        Trader,
        Ship,
        Fighter,
        Mine,
        Warp
    }

    private sealed record OwnerContext(string TraderName, int Corp);
    private sealed record NumericCriteria(bool Enabled, NumericOperator Operator, int Value);
    private sealed record TextCriteria(bool Enabled, string Text);
    private sealed record SectorCriteria(
        NumericCriteria Fighters,
        TextCriteria FighterOwner,
        bool UseFighterType,
        Core.FighterType FighterType,
        NumericCriteria Armids,
        TextCriteria ArmidOwner,
        NumericCriteria Limpets,
        TextCriteria LimpetOwner,
        NumericCriteria Density,
        NumericCriteria NavHaz,
        NumericCriteria Warps,
        bool Planets,
        bool TradersAndAliens,
        bool Ships,
        bool Anomaly,
        bool Hostile,
        bool Friendly,
        bool Visited,
        bool Explored,
        bool Backdoor,
        bool Unexplored,
        bool UsePortType,
        string PortType,
        TextCriteria SpaceName,
        TextCriteria PortName,
        TextCriteria Beacon,
        TextCriteria SectorNote);

    private sealed record ProductCriteria(
        ProductIntent Intent,
        int MinAmount,
        int MaxAmount,
        int MinPercent,
        int MaxPercent,
        int MinMcic,
        int MaxMcic);

    private sealed record PortCriteria(
        PortSearchMode Mode,
        bool UseMaxDistance,
        int MaxDistance,
        bool FighterSearch,
        IReadOnlyDictionary<Core.ProductType, ProductCriteria> Primary,
        IReadOnlyDictionary<Core.ProductType, ProductCriteria> Secondary);

    private sealed record SectorResult(int Sector, string Display);
    private sealed record PortResult(int Sector, string Display, int? Distance);
    private sealed record PortPairResult(int SectorA, int SectorB, string Display, int? Distance);

    private sealed class FinderResultItem
    {
        public required string Label { get; init; }
        public required string Detail { get; init; }
        public required IReadOnlyList<int> Sectors { get; init; }

        public override string ToString() => Label;
    }

    private sealed class NumericFilterControls
    {
        public CheckBox Enabled { get; init; } = null!;
        public ComboBox Operator { get; init; } = null!;
        public TextBox Value { get; init; } = null!;
    }

    private sealed class TextFilterControls
    {
        public CheckBox Enabled { get; init; } = null!;
        public TextBox Text { get; init; } = null!;
    }

    private sealed class SectorQueryControls
    {
        public NumericFilterControls Fighters { get; init; } = null!;
        public TextFilterControls FighterOwner { get; init; } = null!;
        public CheckBox UseFighterType { get; init; } = null!;
        public ComboBox FighterType { get; init; } = null!;
        public NumericFilterControls Armids { get; init; } = null!;
        public TextFilterControls ArmidOwner { get; init; } = null!;
        public NumericFilterControls Limpets { get; init; } = null!;
        public TextFilterControls LimpetOwner { get; init; } = null!;
        public NumericFilterControls Density { get; init; } = null!;
        public NumericFilterControls NavHaz { get; init; } = null!;
        public NumericFilterControls Warps { get; init; } = null!;
        public CheckBox Planets { get; init; } = null!;
        public CheckBox TradersAndAliens { get; init; } = null!;
        public CheckBox Ships { get; init; } = null!;
        public CheckBox Anomaly { get; init; } = null!;
        public CheckBox Hostile { get; init; } = null!;
        public CheckBox Friendly { get; init; } = null!;
        public CheckBox Visited { get; init; } = null!;
        public CheckBox Explored { get; init; } = null!;
        public CheckBox Backdoor { get; init; } = null!;
        public CheckBox Unexplored { get; init; } = null!;
        public CheckBox UsePortType { get; init; } = null!;
        public ComboBox PortType { get; init; } = null!;
        public TextFilterControls SpaceName { get; init; } = null!;
        public TextFilterControls PortName { get; init; } = null!;
        public TextFilterControls Beacon { get; init; } = null!;
        public TextFilterControls SectorNote { get; init; } = null!;
    }

    private sealed class ProductFilterControls
    {
        public ComboBox Intent { get; init; } = null!;
        public TextBox MinAmount { get; init; } = null!;
        public TextBox MaxAmount { get; init; } = null!;
        public TextBox MinPercent { get; init; } = null!;
        public TextBox MaxPercent { get; init; } = null!;
        public TextBox MinMcic { get; init; } = null!;
        public TextBox MaxMcic { get; init; } = null!;
    }

    private sealed class PortQueryControls
    {
        public RadioButton SinglePorts { get; init; } = null!;
        public RadioButton PortPairs { get; init; } = null!;
        public CheckBox MaxDistanceEnabled { get; init; } = null!;
        public TextBox MaxDistance { get; init; } = null!;
        public CheckBox FighterSearch { get; init; } = null!;
        public Border SecondaryPanel { get; init; } = null!;
        public Dictionary<Core.ProductType, ProductFilterControls> Primary { get; init; } = new();
        public Dictionary<Core.ProductType, ProductFilterControls> Secondary { get; init; } = new();
    }

    private readonly Func<Core.ModDatabase?> _getDb;
    private readonly Func<int> _getCurrentSector;
    private readonly Func<GameState?> _getState;
    private readonly TabControl _modeTabs = new();
    private readonly TextBlock _resultHeader = new();
    private readonly TextBlock _previewHeader = new();
    private readonly StackPanel _resultsHost = new();
    private readonly ListBox _resultList = new();
    private readonly Button _searchButton = new();
    private readonly Button _copyButton = new();
    private readonly List<SectorQueryControls> _sectorQueries = new();
    private readonly List<PortQueryControls> _portQueries = new();
    private TacticalMapControl? _previewMap;
    private string _currentResultText = string.Empty;
    private int _selectedPreviewSector;

    private const int MaxDisplayResults = 250;
    private const int MaxPairResults = 1000;
    private const int MaxPairEvaluations = 750000;
    private const double HeaderFontSize = 13;
    private const double SectionFontSize = 9;
    private const double BodyFontSize = 8.3;
    private const double SmallFontSize = 7.4;
    private const double ControlHeight = 19;

    private static readonly FontFamily MonoFont = new("Cascadia Code, Menlo, Consolas, Courier New, monospace");
    private static readonly IBrush BgWin = new SolidColorBrush(Color.FromRgb(0x07, 0x12, 0x17));
    private static readonly IBrush BgPanel = new SolidColorBrush(Color.FromRgb(0x0b, 0x26, 0x30));
    private static readonly IBrush BgCard = new SolidColorBrush(Color.FromRgb(0x08, 0x33, 0x3c));
    private static readonly IBrush BgCardAlt = new SolidColorBrush(Color.FromRgb(0x03, 0x1d, 0x26));
    private static readonly IBrush BgRow = new SolidColorBrush(Color.FromRgb(0x09, 0x20, 0x29));
    private static readonly IBrush BgRowAlt = new SolidColorBrush(Color.FromRgb(0x0d, 0x2b, 0x34));
    private static readonly IBrush Edge = new SolidColorBrush(Color.FromRgb(0x1b, 0x82, 0x95));
    private static readonly IBrush InnerEdge = new SolidColorBrush(Color.FromRgb(0x12, 0x5e, 0x70));
    private static readonly IBrush ColText = new SolidColorBrush(Color.FromRgb(0xe0, 0xf4, 0xf8));
    private static readonly IBrush ColMuted = new SolidColorBrush(Color.FromRgb(0x8a, 0xb8, 0xc0));
    private static readonly IBrush ColAccent = new SolidColorBrush(Color.FromRgb(0x00, 0xd4, 0xc9));
    private static readonly IBrush ColCyan = new SolidColorBrush(Color.FromRgb(0x33, 0xee, 0xff));
    private static readonly IBrush ColGreen = new SolidColorBrush(Color.FromRgb(0x00, 0xff, 0x66));
    private static readonly IBrush ColYellow = new SolidColorBrush(Color.FromRgb(0xff, 0xee, 0x44));
    private static readonly IBrush ColOrange = new SolidColorBrush(Color.FromRgb(0xff, 0xaa, 0x44));
    private static readonly IBrush ColRed = new SolidColorBrush(Color.FromRgb(0xff, 0x66, 0x66));
    private static readonly IBrush ColMagenta = new SolidColorBrush(Color.FromRgb(0xff, 0x33, 0xff));
    private static readonly IBrush ColBlue = new SolidColorBrush(Color.FromRgb(0x33, 0x4d, 0xff));
    private static readonly IBrush ColWhite = new SolidColorBrush(Color.FromRgb(0xff, 0xff, 0xff));

    private static readonly string[] OperatorLabels = [">", ">=", "=", "<=", "<", "!="];
    private static readonly string[] ProductIntentLabels = ["Any", "Buy", "Sell"];
    private static readonly string[] PortTypeLabels =
    [
        "Any",
        "No port",
        "Any port",
        "Class 0",
        "Class 1 (BBS)",
        "Class 2 (BSB)",
        "Class 3 (SBB)",
        "Class 4 (SSB)",
        "Class 5 (SBS)",
        "Class 6 (BSS)",
        "Class 7 (SSS)",
        "Class 8 (BBB)",
        "Class 9 (StarDock)",
        "BBS",
        "BSB",
        "SBB",
        "SSB",
        "SBS",
        "BSS",
        "SSS",
        "BBB"
    ];

    public DataMiningWindow(
        Func<Core.ModDatabase?> getDb,
        Func<int> getCurrentSector,
        Func<GameState?> getState)
    {
        _getDb = getDb;
        _getCurrentSector = getCurrentSector;
        _getState = getState;

        Title = "Find";
        Width = 1640;
        Height = 540;
        MinWidth = 1180;
        MinHeight = 460;
        Background = BgWin;
        FontFamily = MonoFont;
        FontSize = BodyFontSize;

        _searchButton.Content = "Search";
        _searchButton.Height = 28;
        _searchButton.Padding = new Thickness(16, 4);
        StyleActionButton(_searchButton, primary: true);
        _searchButton.Click += async (_, _) => await RunSearchAsync();

        var resetButton = new Button
        {
            Content = "Reset",
            Height = 28,
            Padding = new Thickness(16, 4)
        };
        StyleActionButton(resetButton, primary: false);
        resetButton.Click += (_, _) => ResetActiveQuery();

        _copyButton.Content = "Copy";
        _copyButton.Height = 28;
        _copyButton.Padding = new Thickness(16, 4);
        _copyButton.IsEnabled = false;
        StyleActionButton(_copyButton, primary: false);
        _copyButton.Click += async (_, _) => await CopyResultsAsync();

        var closeButton = new Button
        {
            Content = "Close",
            Height = 28,
            Padding = new Thickness(16, 4)
        };
        StyleActionButton(closeButton, primary: false);
        closeButton.Click += (_, _) => Close();

        _resultHeader.Text = "Build a search on the left, then click Search.";
        _resultHeader.Foreground = ColMuted;
        _resultHeader.FontSize = BodyFontSize;
        _resultHeader.TextWrapping = TextWrapping.Wrap;
        _resultHeader.Margin = new Thickness(0, 0, 0, 6);
        _modeTabs.FontSize = BodyFontSize;
        _resultList.Background = BgCardAlt;
        _resultList.Foreground = ColText;
        _resultList.BorderBrush = InnerEdge;
        _resultList.FontFamily = MonoFont;
        _resultList.FontSize = BodyFontSize;
        _resultList.SelectionMode = SelectionMode.Single;
        _resultList.SelectionChanged += (_, _) =>
        {
            if (_resultList.SelectedItem is FinderResultItem item)
                SelectResultItem(item);
        };

        _previewHeader.Text = "Select a result to preview it.";
        _previewHeader.Foreground = ColMuted;
        _previewHeader.FontSize = BodyFontSize;
        _previewHeader.TextWrapping = TextWrapping.Wrap;
        _previewHeader.Margin = new Thickness(0, 0, 0, 6);

        var split = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("660,6,390,6,*"),
            Children =
            {
                BuildCriteriaPane(),
                new GridSplitter
                {
                    Width = 6,
                    Background = Brushes.Transparent,
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    VerticalAlignment = VerticalAlignment.Stretch,
                }.WithColumn(1),
                BuildResultListPane().WithColumn(2),
                new GridSplitter
                {
                    Width = 6,
                    Background = Brushes.Transparent,
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    VerticalAlignment = VerticalAlignment.Stretch,
                }.WithColumn(3),
                BuildPreviewPane().WithColumn(4),
            }
        };

        var footer = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,Auto,*,Auto,Auto"),
            ColumnSpacing = 8,
            Margin = new Thickness(0, 8, 0, 0),
            Children =
            {
                _searchButton,
                resetButton.WithColumn(1),
                _copyButton.WithColumn(3),
                closeButton.WithColumn(4),
            }
        };

        Content = new Border
        {
            Background = BgWin,
            Padding = new Thickness(8),
            Child = new Grid
            {
                RowDefinitions = new RowDefinitions("*,Auto"),
                Children =
                {
                    split,
                    footer.WithRow(1),
                }
            }
        };
    }

    private Control BuildCriteriaPane()
    {
        _modeTabs.ItemsSource = new object[]
        {
            new TabItem { Header = BuildTabHeader("Sectors", emphasized: true), Content = BuildSectorQuery() },
            new TabItem { Header = BuildTabHeader("Ports", emphasized: true), Content = BuildPortQuery() },
        };

        return new Border
        {
            Background = BgPanel,
            BorderBrush = Edge,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            MinWidth = 620,
            Padding = new Thickness(8),
            Child = new DockPanel
            {
                Children =
                {
                    new StackPanel
                    {
                        Spacing = 2,
                        Children =
                        {
                            new TextBlock
                            {
                                Text = "Data Finder",
                                Foreground = ColAccent,
                                FontSize = HeaderFontSize,
                                FontWeight = FontWeight.Bold,
                            },
                            new TextBlock
                            {
                                Text = "Search the active TWX database for sectors, ports, and port pairs.",
                                Foreground = ColMuted,
                                FontSize = SmallFontSize,
                                TextWrapping = TextWrapping.Wrap,
                                Margin = new Thickness(0, 0, 0, 8),
                            }
                        }
                    }.WithDock(Dock.Top),
                    _modeTabs,
                }
            }
        };
    }

    private static TextBlock BuildTabHeader(string text, bool emphasized = false)
        => new()
        {
            Text = text,
            Foreground = emphasized ? ColText : ColMuted,
            FontSize = emphasized ? SectionFontSize : BodyFontSize,
            FontWeight = emphasized ? FontWeight.SemiBold : FontWeight.Normal,
        };

    private Control BuildResultListPane()
    {
        var detailScroll = new ScrollViewer
        {
            Content = _resultsHost,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
        };

        return new Border
        {
            Background = BgPanel,
            BorderBrush = Edge,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(8),
            Child = new Grid
            {
                RowDefinitions = new RowDefinitions("Auto,*,*"),
                RowSpacing = 8,
                Children =
                {
                    _resultHeader,
                    _resultList.WithRow(1),
                    new Border
                    {
                        Background = BgCardAlt,
                        BorderBrush = InnerEdge,
                        BorderThickness = new Thickness(1),
                        CornerRadius = new CornerRadius(10),
                        Padding = new Thickness(8),
                        Child = new DockPanel
                        {
                            Children =
                            {
                                _previewHeader.WithDock(Dock.Top),
                                detailScroll,
                            }
                        }
                    }.WithRow(2),
                }
            }
        };
    }

    private Control BuildPreviewPane()
    {
        _previewMap = new TacticalMapControl(
            () => _selectedPreviewSector > 0 ? _selectedPreviewSector : Math.Max(1, _getCurrentSector()),
            _getDb,
            _getState);
        _previewMap.SetViewMode(TacticalMapViewMode.Bubble);
        _previewMap.SetPreviewSelection(null, legendText: "Find preview");

        return new Border
        {
            Background = BgPanel,
            BorderBrush = Edge,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(8),
            Child = new Border
            {
                Background = BgCardAlt,
                BorderBrush = InnerEdge,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Child = _previewMap,
            },
        };
    }

    private Control BuildSectorQuery()
    {
        string defaultOwner = _getState()?.TraderName ?? string.Empty;

        var query = new SectorQueryControls
        {
            Fighters = BuildNumericFilter("Fighters", 0, enabled: false),
            FighterOwner = BuildTextFilter("Owner", defaultOwner, enabled: false, width: 96),
            UseFighterType = BuildCheckBox("Type"),
            FighterType = BuildCombo(["Toll", "Defensive", "Offensive"], "Toll", 74),
            Armids = BuildNumericFilter("Armid mines", 0, enabled: false),
            ArmidOwner = BuildTextFilter("Owner", defaultOwner, enabled: false, width: 96),
            Limpets = BuildNumericFilter("Limpet mines", 0, enabled: false),
            LimpetOwner = BuildTextFilter("Owner", defaultOwner, enabled: false, width: 96),
            Density = BuildNumericFilter("Density", 0, enabled: false),
            NavHaz = BuildNumericFilter("NavHaz", 0, enabled: false),
            Warps = BuildNumericFilter("Warps", 0, enabled: false),
            Planets = BuildCheckBox("Planets"),
            TradersAndAliens = BuildCheckBox("Traders & Aliens"),
            Ships = BuildCheckBox("Ships"),
            Anomaly = BuildCheckBox("Anomaly"),
            Hostile = BuildCheckBox("Hostile"),
            Friendly = BuildCheckBox("Friendly"),
            Visited = BuildCheckBox("Visited"),
            Explored = BuildCheckBox("Explored"),
            Backdoor = BuildCheckBox("Backdoor"),
            Unexplored = BuildCheckBox("Unexplored"),
            UsePortType = BuildCheckBox("Port type"),
            PortType = BuildCombo(PortTypeLabels, "No port", 112),
            SpaceName = BuildTextFilter("Space name", string.Empty, enabled: false, width: 160),
            PortName = BuildTextFilter("Port name", string.Empty, enabled: false, width: 160),
            Beacon = BuildTextFilter("Beacon text", string.Empty, enabled: false, width: 160),
            SectorNote = BuildTextFilter("Sector note", string.Empty, enabled: false, width: 160),
        };

        _sectorQueries.Add(query);

        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,*"),
            RowDefinitions = new RowDefinitions("Auto,Auto"),
            ColumnSpacing = 6,
            RowSpacing = 6,
        };

        AddGridChild(grid, BuildSectionCard("Deployments", new StackPanel
        {
            Spacing = 3,
            Children =
            {
                BuildSectorObjectRow(query.Fighters, query.FighterOwner, query.UseFighterType, query.FighterType),
                BuildSectorObjectRow(query.Armids, query.ArmidOwner, null, null),
                BuildSectorObjectRow(query.Limpets, query.LimpetOwner, null, null),
            }
        }), 0, 0, 2);

        AddGridChild(grid, BuildSectionCard("Topology", new StackPanel
        {
            Spacing = 3,
            Children =
            {
                BuildSectorNumericRow(query.Density),
                BuildSectorNumericRow(query.NavHaz),
                BuildSectorNumericRow(query.Warps),
                BuildPortTypeRow(query.UsePortType, query.PortType),
            }
        }), 1, 0);

        AddGridChild(grid, BuildSectionCard("Flags", BuildFlagGrid(
            query.Planets,
            query.TradersAndAliens,
            query.Ships,
            query.Anomaly,
            query.Hostile,
            query.Friendly,
            query.Visited,
            query.Explored,
            query.Backdoor,
            query.Unexplored)), 1, 1);

        return new ScrollViewer
        {
            Content = grid,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Margin = new Thickness(0, 4, 0, 0),
        };
    }

    private Control BuildPortQuery()
    {
        var primary = BuildProductControls(defaultsOnly: false);
        var secondary = BuildProductControls(defaultsOnly: true);
        var primaryPanel = BuildPortProductPanel("Port settings", primary, enabled: true);
        var secondaryPanel = BuildPortProductPanel("Second port settings", secondary, enabled: false);

        var single = new RadioButton { Content = "Single ports", GroupName = "port-mode", IsChecked = true };
        var pair = new RadioButton { Content = "Port pairs", GroupName = "port-mode" };
        StyleCheck(single);
        StyleCheck(pair);

        var query = new PortQueryControls
        {
            SinglePorts = single,
            PortPairs = pair,
            MaxDistanceEnabled = BuildCheckBox("Max distance"),
            MaxDistance = BuildTextBox("10", 78),
            FighterSearch = BuildCheckBox("Fighter search"),
            SecondaryPanel = secondaryPanel,
            Primary = primary,
            Secondary = secondary,
        };
        query.MaxDistanceEnabled.IsChecked = true;
        _portQueries.Add(query);

        void UpdatePairMode()
        {
            bool pairMode = query.PortPairs.IsChecked == true;
            SetPanelEnabled(query.SecondaryPanel, pairMode);
        }

        single.IsCheckedChanged += (_, _) => UpdatePairMode();
        pair.IsCheckedChanged += (_, _) => UpdatePairMode();
        UpdatePairMode();

        var header = new Border
        {
            Background = BgCard,
            BorderBrush = InnerEdge,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(5),
            Child = new StackPanel
            {
                Spacing = 4,
                Children =
                {
                    new Grid
                    {
                        ColumnDefinitions = new ColumnDefinitions("24,62,84,78,74,48,94,*"),
                        ColumnSpacing = 6,
                        ClipToBounds = true,
                        Children =
                        {
                            BuildSearchGlyph(),
                            new TextBlock
                            {
                                Text = "Search for:",
                                Foreground = ColText,
                                VerticalAlignment = VerticalAlignment.Center,
                                FontSize = BodyFontSize,
                            }.WithColumn(1),
                            single.WithColumn(2),
                            pair.WithColumn(3),
                            query.MaxDistanceEnabled.WithColumn(4),
                            query.MaxDistance.WithColumn(5),
                            query.FighterSearch.WithColumn(6),
                        }
                    },
                }
            }
        };

        return new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = new StackPanel
            {
                Spacing = 6,
                Margin = new Thickness(0, 4, 0, 0),
                Children =
                {
                    header,
                    primaryPanel,
                    secondaryPanel,
                }
            }
        };
    }

    private async Task RunSearchAsync()
    {
        Core.ModDatabase? db = _getDb();
        if (db == null)
        {
            ShowMessage("No active database. Connect to or open a game first.", ColRed);
            return;
        }

        int totalSectors = db.DBHeader.Sectors > 0 ? db.DBHeader.Sectors : db.MaxSectorSeen;
        if (totalSectors <= 0)
        {
            ShowMessage("Universe size is not known yet.", ColRed);
            return;
        }

        _searchButton.IsEnabled = false;
        _copyButton.IsEnabled = false;
        _currentResultText = string.Empty;
        ShowMessage("Searching...", ColYellow);

        try
        {
            OwnerContext ownerContext = CaptureOwnerContext();
            bool sectorMode = _modeTabs.SelectedIndex <= 0;
            if (sectorMode)
            {
                int index = 0;
                SectorCriteria criteria = CaptureSectorCriteria(_sectorQueries[index]);
                List<SectorResult> results = await Task.Run(() => FindSectors(db, totalSectors, criteria, ownerContext));
                RenderSectorResults(results, totalSectors);
            }
            else
            {
                int index = 0;
                PortCriteria criteria = CapturePortCriteria(_portQueries[index]);
                int currentSector = Math.Max(1, _getCurrentSector());
                if (criteria.Mode == PortSearchMode.Single)
                {
                    List<PortResult> results = await Task.Run(() => FindPorts(db, totalSectors, currentSector, criteria));
                    RenderPortResults(results, totalSectors);
                }
                else
                {
                    List<PortPairResult> results = await Task.Run(() => FindPortPairs(db, totalSectors, currentSector, criteria));
                    RenderPortPairResults(results, totalSectors);
                }
            }
        }
        catch (Exception ex)
        {
            ShowMessage($"Search failed: {ex.Message}", ColRed);
        }
        finally
        {
            _searchButton.IsEnabled = true;
            _copyButton.IsEnabled = !string.IsNullOrWhiteSpace(_currentResultText);
        }
    }

    private List<SectorResult> FindSectors(Core.ModDatabase db, int totalSectors, SectorCriteria criteria, OwnerContext ownerContext)
    {
        var results = new List<SectorResult>();
        for (int sectorNumber = 1; sectorNumber <= totalSectors; sectorNumber++)
        {
            Core.SectorData? sector = db.GetSector(sectorNumber);
            if (sector == null)
            {
                if (criteria.Unexplored)
                    results.Add(new SectorResult(sectorNumber, SectorScanFormatter.FormatSectorTooltip(sectorNumber, null, db)));
                continue;
            }

            if (!MatchesSector(db, sectorNumber, sector, criteria, ownerContext))
                continue;

            results.Add(new SectorResult(
                sectorNumber,
                SectorScanFormatter.FormatSectorTooltip(sectorNumber, sector, db)));
        }

        return results;
    }

    private List<PortResult> FindPorts(Core.ModDatabase db, int totalSectors, int currentSector, PortCriteria criteria)
    {
        var results = new List<PortResult>();
        for (int sectorNumber = 1; sectorNumber <= totalSectors; sectorNumber++)
        {
            Core.SectorData? sector = db.GetSector(sectorNumber);
            Core.Port? port = sector?.SectorPort;
            if (!IsActivePort(port))
                continue;

            if (criteria.FighterSearch && sector!.Fighters.Quantity <= 0)
                continue;

            if (!MatchesPortProducts(sector!, port!, criteria.Primary, unknownMcicPassesDefault: true))
                continue;

            int? distance = null;
            if (criteria.UseMaxDistance)
            {
                distance = GetDistance(db, currentSector, sectorNumber);
                if (!distance.HasValue || distance.Value > criteria.MaxDistance)
                    continue;
            }

            results.Add(new PortResult(
                sectorNumber,
                FormatPortResultLine(sectorNumber, sector!, port!, distance),
                distance));
        }

        return results
            .OrderBy(r => r.Distance ?? int.MaxValue)
            .ThenBy(r => r.Sector)
            .ToList();
    }

    private List<PortPairResult> FindPortPairs(Core.ModDatabase db, int totalSectors, int currentSector, PortCriteria criteria)
    {
        var primary = new List<(int Sector, Core.SectorData Data, Core.Port Port)>();
        var secondary = new List<(int Sector, Core.SectorData Data, Core.Port Port)>();

        for (int sectorNumber = 1; sectorNumber <= totalSectors; sectorNumber++)
        {
            Core.SectorData? sector = db.GetSector(sectorNumber);
            Core.Port? port = sector?.SectorPort;
            if (!IsActivePort(port))
                continue;

            if (criteria.FighterSearch && sector!.Fighters.Quantity <= 0)
                continue;

            if (MatchesPortProducts(sector!, port!, criteria.Primary, unknownMcicPassesDefault: true))
                primary.Add((sectorNumber, sector!, port!));

            if (MatchesPortProducts(sector!, port!, criteria.Secondary, unknownMcicPassesDefault: true))
                secondary.Add((sectorNumber, sector!, port!));
        }

        var results = new List<PortPairResult>();
        int evaluations = 0;
        foreach ((int sectorA, Core.SectorData dataA, Core.Port portA) in primary)
        {
            foreach ((int sectorB, Core.SectorData dataB, Core.Port portB) in secondary)
            {
                if (sectorA == sectorB)
                    continue;

                evaluations++;
                if (evaluations > MaxPairEvaluations || results.Count >= MaxPairResults)
                    return SortPortPairs(results);

                int? distance = null;
                if (criteria.UseMaxDistance)
                {
                    distance = GetDistance(db, sectorA, sectorB);
                    if (!distance.HasValue || distance.Value > criteria.MaxDistance)
                        continue;
                }

                results.Add(new PortPairResult(
                    sectorA,
                    sectorB,
                    FormatPortPairResultLine(sectorA, dataA, portA, sectorB, dataB, portB, distance),
                    distance));
            }
        }

        return SortPortPairs(results);
    }

    private static List<PortPairResult> SortPortPairs(List<PortPairResult> results)
        => results
            .OrderBy(r => r.Distance ?? int.MaxValue)
            .ThenBy(r => r.SectorA)
            .ThenBy(r => r.SectorB)
            .ToList();

    private bool MatchesSector(
        Core.ModDatabase db,
        int sectorNumber,
        Core.SectorData sector,
        SectorCriteria criteria,
        OwnerContext ownerContext)
    {
        if (!MatchesNumeric(sector.Fighters.Quantity, criteria.Fighters))
            return false;
        if (!MatchesText(sector.Fighters.Owner, criteria.FighterOwner))
            return false;
        if (criteria.UseFighterType && sector.Fighters.FigType != criteria.FighterType)
            return false;

        if (!MatchesNumeric(sector.MinesArmid.Quantity, criteria.Armids))
            return false;
        if (!MatchesText(sector.MinesArmid.Owner, criteria.ArmidOwner))
            return false;

        if (!MatchesNumeric(sector.MinesLimpet.Quantity, criteria.Limpets))
            return false;
        if (!MatchesText(sector.MinesLimpet.Owner, criteria.LimpetOwner))
            return false;

        if (!MatchesNumeric(sector.Density, criteria.Density))
            return false;
        if (!MatchesNumeric(sector.NavHaz, criteria.NavHaz))
            return false;
        if (!MatchesNumeric(sector.Warp.Count(w => w > 0), criteria.Warps))
            return false;

        if (criteria.Planets && db.GetPlanetNamesInSector(sectorNumber).Count == 0)
            return false;
        if (criteria.TradersAndAliens && sector.Traders.Count == 0)
            return false;
        if (criteria.Ships && sector.Ships.Count == 0)
            return false;
        if (criteria.Anomaly && !sector.Anomaly)
            return false;
        if (criteria.Visited && sector.Explored != Core.ExploreType.Yes)
            return false;
        if (criteria.Explored && sector.Explored == Core.ExploreType.No)
            return false;
        if (criteria.Unexplored && sector.Explored != Core.ExploreType.No)
            return false;
        if (criteria.Backdoor && !HasBackdoor(sector))
            return false;
        if (criteria.Hostile && !HasHostileData(db, sectorNumber, sector, ownerContext))
            return false;
        if (criteria.Friendly && !HasFriendlyData(db, sectorNumber, sector, ownerContext))
            return false;
        if (criteria.UsePortType && !MatchesPortType(sector.SectorPort, criteria.PortType))
            return false;
        if (!MatchesText($"{sector.Constellation} {sector.SectorName}", criteria.SpaceName))
            return false;
        if (!MatchesText(sector.SectorPort?.Name ?? string.Empty, criteria.PortName))
            return false;
        if (!MatchesText(sector.Beacon, criteria.Beacon))
            return false;
        if (!MatchesSectorNote(sector, criteria.SectorNote))
            return false;

        return true;
    }

    private static bool MatchesPortProducts(
        Core.SectorData sector,
        Core.Port port,
        IReadOnlyDictionary<Core.ProductType, ProductCriteria> criteria,
        bool unknownMcicPassesDefault)
    {
        foreach ((Core.ProductType product, ProductCriteria filter) in criteria)
        {
            bool buys = port.BuyProduct.GetValueOrDefault(product);
            if (filter.Intent == ProductIntent.Buy && !buys)
                return false;
            if (filter.Intent == ProductIntent.Sell && buys)
                return false;

            int amount = port.ProductAmount.GetValueOrDefault(product);
            if (amount < filter.MinAmount || amount > filter.MaxAmount)
                return false;

            int percent = port.ProductPercent.GetValueOrDefault(product);
            if (percent < filter.MinPercent || percent > filter.MaxPercent)
                return false;

            if (!MatchesMcic(sector, product, filter.MinMcic, filter.MaxMcic, unknownMcicPassesDefault))
                return false;
        }

        return true;
    }

    private static bool MatchesMcic(Core.SectorData sector, Core.ProductType product, int min, int max, bool unknownPassesDefault)
    {
        (int defaultMin, int defaultMax) = DefaultMcicRange(product);
        bool defaultRange = min == defaultMin && max == defaultMax;
        if (!TryGetMcic(sector, product, out int mcic))
            return unknownPassesDefault && defaultRange;

        return mcic >= min && mcic <= max;
    }

    private static bool TryGetMcic(Core.SectorData sector, Core.ProductType product, out int value)
    {
        foreach (string key in McicKeys(product))
        {
            if (sector.Variables.TryGetValue(key, out string? raw) &&
                int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
            {
                return true;
            }
        }

        string minKey = ProductKey(product) + "-";
        string maxKey = ProductKey(product) + "+";
        if (sector.Variables.TryGetValue(minKey, out string? rawMin) &&
            sector.Variables.TryGetValue(maxKey, out string? rawMax) &&
            int.TryParse(rawMin, NumberStyles.Integer, CultureInfo.InvariantCulture, out int min) &&
            int.TryParse(rawMax, NumberStyles.Integer, CultureInfo.InvariantCulture, out int max))
        {
            value = (int)Math.Round((min + max) / 2.0);
            return true;
        }

        value = 0;
        return false;
    }

    private void RenderSectorResults(IReadOnlyList<SectorResult> results, int totalSectors)
    {
        _resultHeader.Text = $"{results.Count:N0} sector(s) matched out of {totalSectors:N0}.";
        _resultHeader.Foreground = results.Count == 0 ? ColOrange : ColGreen;
        _currentResultText = string.Join(Environment.NewLine + Environment.NewLine, results.Select(r => r.Display));

        if (results.Count == 0)
        {
            AddEmptyResult("No sectors matched this query.");
            return;
        }

        IReadOnlyList<FinderResultItem> items = results
            .Take(MaxDisplayResults)
            .Select(result => new FinderResultItem
            {
                Label = $"Sector {result.Sector,5}  {ExtractSectorSummary(result.Display)}",
                Detail = result.Display,
                Sectors = [result.Sector],
            })
            .ToArray();
        PopulateResultList(items, results.Count, MaxDisplayResults);
    }

    private void RenderPortResults(IReadOnlyList<PortResult> results, int totalSectors)
    {
        _resultHeader.Text = $"{results.Count:N0} port(s) matched out of {totalSectors:N0} sectors.";
        _resultHeader.Foreground = results.Count == 0 ? ColOrange : ColGreen;
        _currentResultText = string.Join(Environment.NewLine, results.Select(r => r.Display));

        if (results.Count == 0)
        {
            AddEmptyResult("No ports matched this query.");
            return;
        }

        IReadOnlyList<FinderResultItem> items = results
            .Take(MaxDisplayResults)
            .Select(result => new FinderResultItem
            {
                Label = result.Display,
                Detail = BuildSectorDetail(result.Sector, result.Display),
                Sectors = [result.Sector],
            })
            .ToArray();
        PopulateResultList(items, results.Count, MaxDisplayResults);
    }

    private void RenderPortPairResults(IReadOnlyList<PortPairResult> results, int totalSectors)
    {
        _resultHeader.Text = $"{results.Count:N0} port pair(s) matched. Pair searches are capped at {MaxPairResults:N0} displayed candidates.";
        _resultHeader.Foreground = results.Count == 0 ? ColOrange : ColGreen;
        _currentResultText = string.Join(Environment.NewLine, results.Select(r => r.Display));

        if (results.Count == 0)
        {
            AddEmptyResult("No port pairs matched this query.");
            return;
        }

        IReadOnlyList<FinderResultItem> items = results
            .Take(MaxDisplayResults)
            .Select(result => new FinderResultItem
            {
                Label = result.Display,
                Detail = BuildPortPairDetail(result),
                Sectors = [result.SectorA, result.SectorB],
            })
            .ToArray();
        PopulateResultList(items, results.Count, MaxDisplayResults);
    }

    private void PopulateResultList(IReadOnlyList<FinderResultItem> items, int total, int displayedLimit)
    {
        _resultList.ItemsSource = null;
        _resultList.ItemsSource = items;
        if (total > displayedLimit)
            _resultHeader.Text += $" Showing first {displayedLimit:N0}.";

        if (items.Count > 0)
            _resultList.SelectedIndex = 0;
    }

    private void SelectResultItem(FinderResultItem item)
    {
        int center = item.Sectors.FirstOrDefault(sector => sector > 0);
        _selectedPreviewSector = center;
        _previewHeader.Text = item.Label;
        _previewHeader.Foreground = ColAccent;
        _resultsHost.Children.Clear();
        _resultsHost.Children.Add(BuildColorizedSectorDetailBlock(item.Detail));

        _previewMap?.SetPreviewSelection(
            item.Sectors,
            center,
            surroundingDepth: 1,
            legendText: "Selected result",
            limitHighlightedSectors: false,
            zoomControlsDepth: true);
        if (center > 0)
            _previewMap?.CenterOnSector(center);
    }

    private SectorCriteria CaptureSectorCriteria(SectorQueryControls query)
        => new(
            CaptureNumeric(query.Fighters),
            CaptureText(query.FighterOwner),
            query.UseFighterType.IsChecked == true,
            ParseFighterType(query.FighterType.SelectedItem?.ToString()),
            CaptureNumeric(query.Armids),
            CaptureText(query.ArmidOwner),
            CaptureNumeric(query.Limpets),
            CaptureText(query.LimpetOwner),
            CaptureNumeric(query.Density),
            CaptureNumeric(query.NavHaz),
            CaptureNumeric(query.Warps),
            query.Planets.IsChecked == true,
            query.TradersAndAliens.IsChecked == true,
            query.Ships.IsChecked == true,
            query.Anomaly.IsChecked == true,
            query.Hostile.IsChecked == true,
            query.Friendly.IsChecked == true,
            query.Visited.IsChecked == true,
            query.Explored.IsChecked == true,
            query.Backdoor.IsChecked == true,
            query.Unexplored.IsChecked == true,
            query.UsePortType.IsChecked == true,
            query.PortType.SelectedItem?.ToString() ?? "Any",
            CaptureText(query.SpaceName),
            CaptureText(query.PortName),
            CaptureText(query.Beacon),
            CaptureText(query.SectorNote));

    private PortCriteria CapturePortCriteria(PortQueryControls query)
        => new(
            query.PortPairs.IsChecked == true ? PortSearchMode.Pair : PortSearchMode.Single,
            query.MaxDistanceEnabled.IsChecked == true,
            ParseInt(query.MaxDistance.Text, 10, 0, 100000),
            query.FighterSearch.IsChecked == true,
            CaptureProducts(query.Primary),
            CaptureProducts(query.Secondary));

    private static IReadOnlyDictionary<Core.ProductType, ProductCriteria> CaptureProducts(Dictionary<Core.ProductType, ProductFilterControls> controls)
        => controls.ToDictionary(
            pair => pair.Key,
            pair =>
            {
                (int defaultMin, int defaultMax) = DefaultMcicRange(pair.Key);
                return new ProductCriteria(
                    ParseProductIntent(pair.Value.Intent.SelectedItem?.ToString()),
                    ParseInt(pair.Value.MinAmount.Text, 0, 0, 65535),
                    ParseInt(pair.Value.MaxAmount.Text, 65535, 0, 65535),
                    ParseInt(pair.Value.MinPercent.Text, 0, 0, 100),
                    ParseInt(pair.Value.MaxPercent.Text, 100, 0, 100),
                    ParseInt(pair.Value.MinMcic.Text, defaultMin, -1000, 1000),
                    ParseInt(pair.Value.MaxMcic.Text, defaultMax, -1000, 1000));
            });

    private static NumericCriteria CaptureNumeric(NumericFilterControls controls)
        => new(
            controls.Enabled.IsChecked == true,
            ParseOperator(controls.Operator.SelectedItem?.ToString()),
            ParseInt(controls.Value.Text, 0, int.MinValue, int.MaxValue));

    private static TextCriteria CaptureText(TextFilterControls controls)
        => new(controls.Enabled.IsChecked == true, controls.Text.Text?.Trim() ?? string.Empty);

    private void ResetActiveQuery()
    {
        if (_modeTabs.SelectedIndex <= 0)
        {
            int index = 0;
            ResetSectorQuery(_sectorQueries[index]);
        }
        else
        {
            int index = 0;
            ResetPortQuery(_portQueries[index]);
        }
    }

    private void ResetSectorQuery(SectorQueryControls query)
    {
        ResetNumeric(query.Fighters, 0);
        ResetText(query.FighterOwner, _getState()?.TraderName ?? string.Empty);
        query.UseFighterType.IsChecked = false;
        query.FighterType.SelectedItem = "Toll";
        ResetNumeric(query.Armids, 0);
        ResetText(query.ArmidOwner, _getState()?.TraderName ?? string.Empty);
        ResetNumeric(query.Limpets, 0);
        ResetText(query.LimpetOwner, _getState()?.TraderName ?? string.Empty);
        ResetNumeric(query.Density, 0);
        ResetNumeric(query.NavHaz, 0);
        ResetNumeric(query.Warps, 0);
        foreach (CheckBox box in new[] { query.Planets, query.TradersAndAliens, query.Ships, query.Anomaly, query.Hostile, query.Friendly, query.Visited, query.Explored, query.Backdoor, query.Unexplored, query.UsePortType })
            box.IsChecked = false;
        query.PortType.SelectedItem = "No port";
        ResetText(query.SpaceName, string.Empty);
        ResetText(query.PortName, string.Empty);
        ResetText(query.Beacon, string.Empty);
        ResetText(query.SectorNote, string.Empty);
    }

    private static void ResetPortQuery(PortQueryControls query)
    {
        query.SinglePorts.IsChecked = true;
        query.PortPairs.IsChecked = false;
        query.MaxDistanceEnabled.IsChecked = true;
        query.MaxDistance.Text = "10";
        query.FighterSearch.IsChecked = false;
        ResetProductControls(query.Primary);
        ResetProductControls(query.Secondary);
        SetPanelEnabled(query.SecondaryPanel, false);
    }

    private static void ResetProductControls(Dictionary<Core.ProductType, ProductFilterControls> controls)
    {
        foreach ((Core.ProductType product, ProductFilterControls control) in controls)
        {
            (int minMcic, int maxMcic) = DefaultMcicRange(product);
            control.Intent.SelectedItem = "Any";
            control.MinAmount.Text = "0";
            control.MaxAmount.Text = "65535";
            control.MinPercent.Text = "0";
            control.MaxPercent.Text = "100";
            control.MinMcic.Text = minMcic.ToString(CultureInfo.InvariantCulture);
            control.MaxMcic.Text = maxMcic.ToString(CultureInfo.InvariantCulture);
        }
    }

    private async Task CopyResultsAsync()
    {
        if (string.IsNullOrWhiteSpace(_currentResultText))
            return;

        bool copied = await ClipboardHelper.TrySetTextAsync(this, _currentResultText);
        _resultHeader.Text = copied ? "Results copied to clipboard." : "Unable to copy results on this platform.";
        _resultHeader.Foreground = copied ? ColGreen : ColOrange;
    }

    private OwnerContext CaptureOwnerContext()
    {
        GameState? state = _getState();
        return new OwnerContext(state?.TraderName ?? string.Empty, state?.Corp ?? 0);
    }

    private static bool MatchesNumeric(int actual, NumericCriteria criteria)
    {
        if (!criteria.Enabled)
            return true;

        return criteria.Operator switch
        {
            NumericOperator.GreaterThan => actual > criteria.Value,
            NumericOperator.GreaterThanOrEqual => actual >= criteria.Value,
            NumericOperator.Equal => actual == criteria.Value,
            NumericOperator.LessThanOrEqual => actual <= criteria.Value,
            NumericOperator.LessThan => actual < criteria.Value,
            NumericOperator.NotEqual => actual != criteria.Value,
            _ => true
        };
    }

    private static bool MatchesText(string? actual, TextCriteria criteria)
    {
        if (!criteria.Enabled)
            return true;
        if (string.IsNullOrWhiteSpace(criteria.Text))
            return true;
        return actual?.IndexOf(criteria.Text, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static bool MatchesSectorNote(Core.SectorData sector, TextCriteria criteria)
    {
        if (!criteria.Enabled || string.IsNullOrWhiteSpace(criteria.Text))
            return true;

        return sector.Variables.Any(pair =>
            pair.Key.IndexOf("NOTE", StringComparison.OrdinalIgnoreCase) >= 0 &&
            pair.Value.IndexOf(criteria.Text, StringComparison.OrdinalIgnoreCase) >= 0);
    }

    private static bool HasBackdoor(Core.SectorData sector)
    {
        HashSet<ushort> outbound = sector.Warp.Where(w => w > 0).ToHashSet();
        return sector.WarpsIn.Any(warpIn => warpIn > 0 && !outbound.Contains(warpIn));
    }

    private static bool HasFriendlyData(Core.ModDatabase db, int sectorNumber, Core.SectorData sector, OwnerContext context)
        => HasOwnerMatch(db, sectorNumber, sector, context, friendly: true);

    private static bool HasHostileData(Core.ModDatabase db, int sectorNumber, Core.SectorData sector, OwnerContext context)
        => HasOwnerMatch(db, sectorNumber, sector, context, friendly: false);

    private static bool HasOwnerMatch(Core.ModDatabase db, int sectorNumber, Core.SectorData sector, OwnerContext context, bool friendly)
    {
        bool Match(string? owner) => friendly ? IsFriendlyOwner(owner, context) : IsEnemyOwner(owner, context);

        if (Match(sector.Fighters.Owner) || Match(sector.MinesArmid.Owner) || Match(sector.MinesLimpet.Owner))
            return true;
        if (sector.Ships.Any(ship => Match(ship.Owner)))
            return true;
        if (sector.Traders.Any(trader => Match($"{trader.Name} {trader.DisplayLabel}")))
            return true;
        if (db.GetPlanetsInSector(sectorNumber).Any(planet => Match(planet.Owner)))
            return true;

        return false;
    }

    private static bool IsFriendlyOwner(string? owner, OwnerContext context)
    {
        if (string.IsNullOrWhiteSpace(owner) || owner == "-")
            return false;

        string trimmed = owner.Trim();
        if (trimmed.Equals("belong to your Corp", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Equals("yours", StringComparison.OrdinalIgnoreCase))
            return true;

        if (context.Corp > 0 && trimmed.Contains($"[{context.Corp}]", StringComparison.OrdinalIgnoreCase))
            return true;

        return !string.IsNullOrWhiteSpace(context.TraderName) &&
               trimmed.Contains(context.TraderName, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsEnemyOwner(string? owner, OwnerContext context)
    {
        if (string.IsNullOrWhiteSpace(owner) || owner == "-")
            return false;

        string trimmed = owner.Trim();
        if (trimmed.Equals("unknown", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Equals("none", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Equals("unowned", StringComparison.OrdinalIgnoreCase))
            return false;

        return !IsFriendlyOwner(trimmed, context);
    }

    private static bool MatchesPortType(Core.Port? port, string portType)
    {
        string value = portType.Trim();
        bool active = IsActivePort(port);

        if (value.Equals("Any", StringComparison.OrdinalIgnoreCase))
            return true;
        if (value.Equals("No port", StringComparison.OrdinalIgnoreCase))
            return !active;
        if (value.Equals("Any port", StringComparison.OrdinalIgnoreCase))
            return active;
        if (!active)
            return false;
        if (value.StartsWith("Class ", StringComparison.OrdinalIgnoreCase) &&
            int.TryParse(value.Split(' ', StringSplitOptions.RemoveEmptyEntries).ElementAtOrDefault(1), out int classIndex))
            return port!.ClassIndex == classIndex;

        return GetPortCode(port!).Equals(value, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsActivePort(Core.Port? port)
        => port != null && !port.Dead && !string.IsNullOrWhiteSpace(port.Name);

    private static int? GetDistance(Core.ModDatabase db, int from, int to)
    {
        if (from <= 0 || to <= 0)
            return null;
        if (from == to)
            return 0;

        List<int> route = db.CalculateBidirectionalShortestPath(from, to);
        return route.Count == 0 ? null : Math.Max(0, route.Count - 1);
    }

    private static string FormatPortResultLine(int sectorNumber, Core.SectorData sector, Core.Port port, int? distance)
    {
        string dist = distance.HasValue ? distance.Value.ToString(CultureInfo.InvariantCulture) : "-";
        return string.Join(" | ",
            sectorNumber.ToString(CultureInfo.InvariantCulture).PadLeft(6),
            dist.PadLeft(4),
            Truncate(port.Name, 28).PadRight(28),
            FormatPortClass(port).PadRight(13),
            FormatProduct(port, Core.ProductType.FuelOre).PadRight(14),
            FormatProduct(port, Core.ProductType.Organics).PadRight(14),
            FormatProduct(port, Core.ProductType.Equipment).PadRight(14),
            FormatMcicSummary(sector));
    }

    private static string FormatPortPairResultLine(
        int sectorA,
        Core.SectorData dataA,
        Core.Port portA,
        int sectorB,
        Core.SectorData dataB,
        Core.Port portB,
        int? distance)
    {
        string dist = distance.HasValue ? distance.Value.ToString(CultureInfo.InvariantCulture) : "-";
        return string.Join(" | ",
            sectorA.ToString(CultureInfo.InvariantCulture).PadLeft(6),
            sectorB.ToString(CultureInfo.InvariantCulture).PadLeft(6),
            dist.PadLeft(4),
            $"{Truncate(portA.Name, 20)} [{FormatPortClass(portA)}] <-> {Truncate(portB.Name, 20)} [{FormatPortClass(portB)}]");
    }

    private string BuildSectorDetail(int sectorNumber, string prefix)
    {
        Core.ModDatabase? db = _getDb();
        Core.SectorData? sector = db?.GetSector(sectorNumber);
        string sectorDisplay = SectorScanFormatter.FormatSectorTooltip(sectorNumber, sector, db);
        return string.IsNullOrWhiteSpace(prefix)
            ? sectorDisplay
            : $"{prefix}{Environment.NewLine}{Environment.NewLine}{sectorDisplay}";
    }

    private string BuildPortPairDetail(PortPairResult result)
        => string.Join(
            Environment.NewLine + Environment.NewLine,
            result.Display,
            BuildSectorDetail(result.SectorA, string.Empty),
            BuildSectorDetail(result.SectorB, string.Empty));

    private static string ExtractSectorSummary(string display)
    {
        string firstLine = display
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault() ?? string.Empty;
        int marker = firstLine.IndexOf(':', StringComparison.Ordinal);
        if (marker >= 0 && marker + 1 < firstLine.Length)
            firstLine = firstLine[(marker + 1)..].Trim();

        return Truncate(firstLine, 44);
    }

    private static string FormatProduct(Core.Port port, Core.ProductType product)
    {
        string side = port.BuyProduct.GetValueOrDefault(product) ? "B" : "S";
        int amount = port.ProductAmount.GetValueOrDefault(product);
        int percent = port.ProductPercent.GetValueOrDefault(product);
        return $"{side} {amount:N0}/{percent}%";
    }

    private static string FormatPortClass(Core.Port port)
    {
        if (port.ClassIndex == 9)
            return "StarDock";
        if (port.ClassIndex == 0)
            return string.IsNullOrWhiteSpace(port.Name) ? "Special" : "Class 0";
        return $"{port.ClassIndex} ({GetPortCode(port)})";
    }

    private static string FormatMcicSummary(Core.SectorData sector)
    {
        var values = new List<string>();
        foreach (Core.ProductType product in Enum.GetValues<Core.ProductType>())
        {
            if (TryGetMcic(sector, product, out int mcic))
                values.Add($"{ProductShortLabel(product)}:{mcic}");
        }

        return values.Count == 0 ? "-" : string.Join(" ", values);
    }

    private static string GetPortCode(Core.Port port)
    {
        char fuel = port.BuyProduct.GetValueOrDefault(Core.ProductType.FuelOre) ? 'B' : 'S';
        char org = port.BuyProduct.GetValueOrDefault(Core.ProductType.Organics) ? 'B' : 'S';
        char equip = port.BuyProduct.GetValueOrDefault(Core.ProductType.Equipment) ? 'B' : 'S';
        return new string([fuel, org, equip]);
    }

    private static string ProductShortLabel(Core.ProductType product)
        => product switch
        {
            Core.ProductType.FuelOre => "O",
            Core.ProductType.Organics => "G",
            _ => "E"
        };

    private static string ProductKey(Core.ProductType product)
        => product switch
        {
            Core.ProductType.FuelOre => "FUEL",
            Core.ProductType.Organics => "ORGANICS",
            _ => "EQUIPMENT"
        };

    private static IEnumerable<string> McicKeys(Core.ProductType product)
    {
        yield return product switch
        {
            Core.ProductType.FuelOre => "OREMCIC",
            Core.ProductType.Organics => "ORGMCIC",
            _ => "EQUMCIC"
        };
    }

    private static (int Min, int Max) DefaultMcicRange(Core.ProductType product)
        => product switch
        {
            Core.ProductType.FuelOre => (-90, 90),
            Core.ProductType.Organics => (-75, 75),
            _ => (-65, 65)
        };

    private static string Truncate(string value, int max)
    {
        if (value.Length <= max)
            return value;
        return value[..Math.Max(0, max - 1)] + "~";
    }

    private static NumericOperator ParseOperator(string? label)
        => label switch
        {
            ">=" => NumericOperator.GreaterThanOrEqual,
            "=" => NumericOperator.Equal,
            "<=" => NumericOperator.LessThanOrEqual,
            "<" => NumericOperator.LessThan,
            "!=" => NumericOperator.NotEqual,
            _ => NumericOperator.GreaterThan
        };

    private static ProductIntent ParseProductIntent(string? label)
        => label switch
        {
            "Buy" => ProductIntent.Buy,
            "Sell" => ProductIntent.Sell,
            _ => ProductIntent.Any
        };

    private static Core.FighterType ParseFighterType(string? label)
        => label switch
        {
            "Defensive" => Core.FighterType.Defensive,
            "Offensive" => Core.FighterType.Offensive,
            _ => Core.FighterType.Toll
        };

    private static int ParseInt(string? value, int fallback, int min, int max)
    {
        if (!int.TryParse((value ?? string.Empty).Replace(",", string.Empty, StringComparison.Ordinal).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int result))
            return fallback;
        return Math.Clamp(result, min, max);
    }

    private NumericFilterControls BuildNumericFilter(string label, int value, bool enabled)
    {
        var controls = new NumericFilterControls
        {
            Enabled = BuildCheckBox(label),
            Operator = BuildCombo(OperatorLabels, ">", 36),
            Value = BuildTextBox(value.ToString(CultureInfo.InvariantCulture), 58),
        };
        controls.Enabled.IsChecked = enabled;
        return controls;
    }

    private TextFilterControls BuildTextFilter(string label, string text, bool enabled, double width)
    {
        var controls = new TextFilterControls
        {
            Enabled = BuildCheckBox(label),
            Text = BuildTextBox(text, width),
        };
        controls.Enabled.IsChecked = enabled;
        return controls;
    }

    private Control BuildSectorObjectRow(
        NumericFilterControls numeric,
        TextFilterControls owner,
        CheckBox? useType,
        ComboBox? type)
    {
        owner.Text.Width = double.NaN;
        owner.Text.MinWidth = 0;
        owner.Text.HorizontalAlignment = HorizontalAlignment.Stretch;

        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("86,36,58,Auto,*"),
            RowDefinitions = new RowDefinitions("Auto,Auto"),
            ColumnSpacing = 4,
            RowSpacing = 2,
            ClipToBounds = true,
        };

        AddGridChild(grid, numeric.Enabled, 0, 0);
        AddGridChild(grid, numeric.Operator, 0, 1);
        AddGridChild(grid, numeric.Value, 0, 2);

        if (useType != null && type != null)
        {
            type.MinWidth = 0;
            type.HorizontalAlignment = HorizontalAlignment.Stretch;
            AddGridChild(grid, useType, 0, 3);
            AddGridChild(grid, type, 0, 4);
        }

        AddGridChild(grid, owner.Enabled, 1, 0);
        AddGridChild(grid, owner.Text, 1, 1, 4);
        return grid;
    }

    private Control BuildSectorNumericRow(NumericFilterControls numeric)
    {
        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("86,36,58,*"),
            ColumnSpacing = 4,
            ClipToBounds = true,
        };
        AddGridChild(grid, numeric.Enabled, 0, 0);
        AddGridChild(grid, numeric.Operator, 0, 1);
        AddGridChild(grid, numeric.Value, 0, 2);
        return grid;
    }

    private static Control BuildFlagGrid(params CheckBox[] boxes)
    {
        int rowCount = Math.Max(1, (boxes.Length + 1) / 2);
        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,*"),
            RowDefinitions = new RowDefinitions(string.Join(",", Enumerable.Repeat("Auto", rowCount))),
            RowSpacing = 2,
            ColumnSpacing = 6,
            ClipToBounds = true,
        };

        for (int i = 0; i < boxes.Length; i++)
        {
            boxes[i].HorizontalAlignment = HorizontalAlignment.Stretch;
            boxes[i].MinWidth = 0;
            Grid.SetRow(boxes[i], i / 2);
            Grid.SetColumn(boxes[i], i % 2);
            grid.Children.Add(boxes[i]);
        }

        return grid;
    }

    private static Control BuildTextRow(TextFilterControls controls)
    {
        controls.Text.Width = double.NaN;
        controls.Text.MinWidth = 0;
        controls.Text.HorizontalAlignment = HorizontalAlignment.Stretch;

        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("86,*"),
            ColumnSpacing = 4,
            ClipToBounds = true,
        };
        AddGridChild(grid, controls.Enabled, 0, 0);
        AddGridChild(grid, controls.Text, 0, 1);
        return grid;
    }

    private static Control BuildPortTypeRow(CheckBox enabled, ComboBox portType)
    {
        portType.Width = double.NaN;
        portType.MinWidth = 0;
        portType.HorizontalAlignment = HorizontalAlignment.Stretch;

        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("86,*"),
            ColumnSpacing = 4,
            ClipToBounds = true,
        };
        AddGridChild(grid, enabled, 0, 0);
        AddGridChild(grid, portType, 0, 1);
        return grid;
    }

    private static void AddGridChild(Grid grid, Control control, int row, int column, int columnSpan = 1)
    {
        Grid.SetRow(control, row);
        Grid.SetColumn(control, column);
        if (columnSpan > 1)
            Grid.SetColumnSpan(control, columnSpan);
        grid.Children.Add(control);
    }

    private Dictionary<Core.ProductType, ProductFilterControls> BuildProductControls(bool defaultsOnly)
    {
        var controls = new Dictionary<Core.ProductType, ProductFilterControls>();
        foreach (Core.ProductType product in Enum.GetValues<Core.ProductType>())
        {
            (int minMcic, int maxMcic) = DefaultMcicRange(product);
            controls[product] = new ProductFilterControls
            {
                Intent = BuildCombo(ProductIntentLabels, "Any", 52),
                MinAmount = BuildTextBox("0", 44),
                MaxAmount = BuildTextBox("65535", 48),
                MinPercent = BuildTextBox("0", 32),
                MaxPercent = BuildTextBox("100", 34),
                MinMcic = BuildTextBox(minMcic.ToString(CultureInfo.InvariantCulture), 36),
                MaxMcic = BuildTextBox(maxMcic.ToString(CultureInfo.InvariantCulture), 36),
            };
        }

        return controls;
    }

    private Border BuildPortProductPanel(string title, Dictionary<Core.ProductType, ProductFilterControls> controls, bool enabled)
    {
        var stack = new StackPanel
        {
            Spacing = 3,
            Children =
            {
                BuildProductHeaderRow(),
                BuildProductFilterRow("Fuel Ore", controls[Core.ProductType.FuelOre]),
                BuildProductFilterRow("Organics", controls[Core.ProductType.Organics]),
                BuildProductFilterRow("Equipment", controls[Core.ProductType.Equipment]),
            }
        };

        var panel = BuildSectionCard(title, stack);
        SetPanelEnabled(panel, enabled);
        return panel;
    }

    private static Control BuildProductHeaderRow()
        => new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("62,52,44,48,32,34,36,36,*"),
            ColumnSpacing = 3,
            ClipToBounds = true,
            Children =
            {
                BuildTinyHeader("Product"),
                BuildTinyHeader("B/S").WithColumn(1),
                BuildTinyHeader("Min").WithColumn(2),
                BuildTinyHeader("Max").WithColumn(3),
                BuildTinyHeader("%-").WithColumn(4),
                BuildTinyHeader("%+").WithColumn(5),
                BuildTinyHeader("M-").WithColumn(6),
                BuildTinyHeader("M+").WithColumn(7),
            }
        };

    private static Control BuildProductFilterRow(string label, ProductFilterControls controls)
    {
        var row = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("62,52,44,48,32,34,36,36,*"),
            ColumnSpacing = 3,
            ClipToBounds = true,
        };
        AddGridChild(row, BuildMiniLabel(label), 0, 0);
        AddGridChild(row, controls.Intent, 0, 1);
        AddGridChild(row, controls.MinAmount, 0, 2);
        AddGridChild(row, controls.MaxAmount, 0, 3);
        AddGridChild(row, controls.MinPercent, 0, 4);
        AddGridChild(row, controls.MaxPercent, 0, 5);
        AddGridChild(row, controls.MinMcic, 0, 6);
        AddGridChild(row, controls.MaxMcic, 0, 7);

        return new Border
        {
            Background = BgRowAlt,
            BorderBrush = InnerEdge,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(3),
            Child = row,
        };
    }

    private static TextBlock BuildTinyHeader(string text)
        => new()
        {
            Text = text,
            Foreground = ColMuted,
            FontSize = SmallFontSize,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };

    private static TextBlock BuildMiniLabel(string text)
        => new()
        {
            Text = text,
            Foreground = ColText,
            FontSize = BodyFontSize,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };

    private Border BuildSectionCard(string title, Control child)
        => new()
        {
            Background = BgCard,
            BorderBrush = InnerEdge,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(5),
            Child = new StackPanel
            {
                Spacing = 3,
                Children =
                {
                    new TextBlock
                    {
                        Text = title,
                        Foreground = ColAccent,
                        FontSize = SectionFontSize,
                        FontWeight = FontWeight.SemiBold,
                    },
                    child,
                }
            }
        };

    private CheckBox BuildCheckBox(string text)
    {
        var box = new CheckBox
        {
            Content = new TextBlock
            {
                Text = text,
                Foreground = ColText,
                FontSize = BodyFontSize,
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center,
            },
            Foreground = ColText,
            FontSize = BodyFontSize,
            MinHeight = ControlHeight,
            MinWidth = 0,
            ClipToBounds = true,
            VerticalAlignment = VerticalAlignment.Center,
        };
        StyleCheck(box);
        return box;
    }

    private static void StyleCheck(ToggleButton button)
    {
        button.Foreground = ColText;
        button.FontSize = BodyFontSize;
        button.MinHeight = ControlHeight;
        button.MinWidth = 0;
        button.ClipToBounds = true;
        button.VerticalAlignment = VerticalAlignment.Center;
    }

    private ComboBox BuildCombo(IEnumerable<string> items, string selected, double width)
    {
        var combo = new ComboBox
        {
            ItemsSource = items.ToArray(),
            SelectedItem = selected,
            Width = width,
            Height = ControlHeight,
            Background = BgCardAlt,
            BorderBrush = InnerEdge,
            Foreground = ColText,
            FontSize = BodyFontSize,
            Padding = new Thickness(4, 0),
            MinWidth = 0,
        };
        return combo;
    }

    private TextBox BuildTextBox(string text, double width)
    {
        var box = new TextBox
        {
            Text = text,
            Width = width,
            Height = ControlHeight,
            Background = BgCardAlt,
            BorderBrush = InnerEdge,
            BorderThickness = new Thickness(1),
            Foreground = ColText,
            CaretBrush = ColAccent,
            FontSize = BodyFontSize,
            VerticalContentAlignment = VerticalAlignment.Center,
            Padding = new Thickness(4, 0),
            MinWidth = 0,
        };
        box.KeyDown += async (_, e) =>
        {
            if (e.Key != Key.Enter)
                return;
            e.Handled = true;
            await RunSearchAsync();
        };
        return box;
    }

    private static void ResetNumeric(NumericFilterControls controls, int value)
    {
        controls.Enabled.IsChecked = false;
        controls.Operator.SelectedItem = ">";
        controls.Value.Text = value.ToString(CultureInfo.InvariantCulture);
    }

    private static void ResetText(TextFilterControls controls, string value)
    {
        controls.Enabled.IsChecked = false;
        controls.Text.Text = value;
    }

    private static void SetPanelEnabled(Control control, bool enabled)
    {
        control.IsEnabled = enabled;
        control.Opacity = enabled ? 1.0 : 0.45;
    }

    private static void StyleActionButton(Button button, bool primary)
    {
        button.Background = primary ? ColAccent : BgCardAlt;
        button.Foreground = primary ? BgWin : ColText;
        button.FontSize = BodyFontSize;
        button.BorderBrush = primary ? ColAccent : InnerEdge;
        button.BorderThickness = new Thickness(1);
        button.CornerRadius = new CornerRadius(8);
    }

    private static Control BuildSearchGlyph()
        => new Border
        {
            Width = 24,
            Height = 24,
            Background = BgCardAlt,
            BorderBrush = InnerEdge,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Child = new TextBlock
            {
                Text = "?",
                Foreground = ColAccent,
                FontSize = 14,
                FontWeight = FontWeight.Bold,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            }
        };

    private Control BuildSectorSummaryLine(IReadOnlyList<int> sectors)
    {
        string text = string.Join(", ", sectors.Take(500).Select(s => s.ToString(CultureInfo.InvariantCulture)));
        if (sectors.Count > 500)
            text += $", ... ({sectors.Count - 500:N0} more)";

        return new Border
        {
            Background = BgCardAlt,
            BorderBrush = InnerEdge,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(10),
            Margin = new Thickness(0, 0, 0, 8),
            Child = new TextBlock
            {
                Text = text,
                Foreground = ColCyan,
                FontFamily = MonoFont,
                FontSize = BodyFontSize,
                TextWrapping = TextWrapping.Wrap,
            }
        };
    }

    private static TextBlock BuildColorizedSectorDetailBlock(string text)
    {
        var block = new TextBlock
        {
            Foreground = ColText,
            FontFamily = MonoFont,
            FontSize = BodyFontSize,
            TextWrapping = TextWrapping.NoWrap,
        };

        string[] lines = text
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n');

        for (int index = 0; index < lines.Length; index++)
        {
            AppendColorizedSectorLine(block, lines[index]);
            if (index < lines.Length - 1)
                block.Inlines?.Add(new LineBreak());
        }

        return block;
    }

    private static void AppendColorizedSectorLine(TextBlock block, string line)
    {
        if (string.IsNullOrEmpty(line))
            return;

        if (!TrySplitSectorDisplayLabel(line, out string prefix, out string content, out SectorDisplayLineKind kind))
        {
            AppendGenericTwText(block, line);
            return;
        }

        int colonIndex = prefix.IndexOf(':', StringComparison.Ordinal);
        if (colonIndex >= 0)
        {
            AppendRun(block, prefix[..colonIndex], GetSectorDisplayLabelBrush(kind));
            AppendRun(block, ":", ColYellow);
            if (colonIndex + 1 < prefix.Length)
                AppendRun(block, prefix[(colonIndex + 1)..], ColText);
        }
        else
        {
            AppendRun(block, prefix, GetSectorDisplayLabelBrush(kind));
        }

        switch (kind)
        {
            case SectorDisplayLineKind.Sector:
                AppendSectorLineContent(block, content);
                break;
            case SectorDisplayLineKind.Port:
                AppendPortLineContent(block, content);
                break;
            case SectorDisplayLineKind.Planet:
                AppendPlanetLineContent(block, content);
                break;
            case SectorDisplayLineKind.Fighter:
                AppendFighterLineContent(block, content);
                break;
            case SectorDisplayLineKind.Mine:
                AppendMineLineContent(block, content);
                break;
            case SectorDisplayLineKind.Warp:
                AppendWarpLineContent(block, content);
                break;
            case SectorDisplayLineKind.Trader:
            case SectorDisplayLineKind.Ship:
                AppendTraderShipLineContent(block, content);
                break;
            case SectorDisplayLineKind.Beacon:
                AppendRun(block, content, ColYellow);
                break;
            default:
                AppendGenericTwText(block, content);
                break;
        }
    }

    private static bool TrySplitSectorDisplayLabel(
        string line,
        out string prefix,
        out string content,
        out SectorDisplayLineKind kind)
    {
        prefix = string.Empty;
        content = line;
        kind = SectorDisplayLineKind.Generic;

        int colonIndex = line.IndexOf(':', StringComparison.Ordinal);
        if (colonIndex < 0)
            return false;

        string label = line[..colonIndex].Trim();
        kind = label switch
        {
            "Beacon" => SectorDisplayLineKind.Beacon,
            "Sector" => SectorDisplayLineKind.Sector,
            "Ports" => SectorDisplayLineKind.Port,
            "Planets" => SectorDisplayLineKind.Planet,
            "Federals" => SectorDisplayLineKind.Trader,
            "Jem'Hada" => SectorDisplayLineKind.Trader,
            "Traders" => SectorDisplayLineKind.Trader,
            "Ships" => SectorDisplayLineKind.Ship,
            "Fighters" => SectorDisplayLineKind.Fighter,
            "Mines" => SectorDisplayLineKind.Mine,
            "Warps to Sector(s)" => SectorDisplayLineKind.Warp,
            "" when line[..colonIndex].All(char.IsWhiteSpace) => SectorDisplayLineKind.Generic,
            _ => SectorDisplayLineKind.Generic,
        };

        if (kind == SectorDisplayLineKind.Generic && label.Length > 0)
            return false;

        int contentStart = colonIndex + 1;
        while (contentStart < line.Length && line[contentStart] == ' ')
            contentStart++;

        prefix = line[..contentStart];
        content = line[contentStart..];
        return true;
    }

    private static IBrush GetSectorDisplayLabelBrush(SectorDisplayLineKind kind)
        => kind switch
        {
            SectorDisplayLineKind.Sector => ColGreen,
            SectorDisplayLineKind.Warp => ColGreen,
            SectorDisplayLineKind.Trader => ColYellow,
            SectorDisplayLineKind.Ship => ColYellow,
            _ => ColMagenta,
        };

    private static void AppendSectorLineContent(TextBlock block, string content)
    {
        int marker = content.IndexOf(" in ", StringComparison.Ordinal);
        if (marker < 0)
        {
            AppendGenericTwText(block, content);
            return;
        }

        AppendRun(block, content[..marker], ColCyan);
        AppendRun(block, " in ", ColGreen);
        AppendRun(block, content[(marker + 4)..], ColBlue);
    }

    private static void AppendPortLineContent(TextBlock block, string content)
    {
        const string classMarker = ", Class ";
        int marker = content.IndexOf(classMarker, StringComparison.Ordinal);
        if (marker < 0)
        {
            AppendGenericTwText(block, content);
            return;
        }

        AppendRun(block, content[..marker], ColCyan);
        AppendRun(block, classMarker, ColMagenta);

        int typeStart = content.IndexOf('(', marker + classMarker.Length);
        if (typeStart < 0)
        {
            AppendRun(block, content[(marker + classMarker.Length)..], ColCyan);
            return;
        }

        AppendRun(block, content[(marker + classMarker.Length)..typeStart], ColCyan);
        AppendPortTypeToken(block, content[typeStart..]);
    }

    private static void AppendPortTypeToken(TextBlock block, string text)
    {
        foreach (char ch in text)
        {
            IBrush brush = char.ToUpperInvariant(ch) switch
            {
                'B' => ColGreen,
                'S' => ColCyan,
                _ => ColMagenta,
            };
            AppendRun(block, ch.ToString(), brush);
        }
    }

    private static void AppendPlanetLineContent(TextBlock block, string content)
    {
        for (int index = 0; index < content.Length;)
        {
            if (content[index..].StartsWith("<<<<", StringComparison.Ordinal))
            {
                AppendRun(block, "<<<<", ColRed);
                index += 4;
                continue;
            }

            if (content[index..].StartsWith(">>>>", StringComparison.Ordinal))
            {
                AppendRun(block, ">>>>", ColRed);
                index += 4;
                continue;
            }

            if (content[index] == '(' && TryReadDelimited(content, index, '(', ')', out string parenthetical))
            {
                AppendRun(block, parenthetical, ColGreen);
                index += parenthetical.Length;
                continue;
            }

            if (content[index] == '.')
            {
                AppendRun(block, ".", ColBlue);
                index++;
                continue;
            }

            int next = FindNextSpecial(content, index, '<', '>', '(', '.');
            AppendRun(block, content[index..next], ColGreen);
            index = next;
        }
    }

    private static void AppendFighterLineContent(TextBlock block, string content)
    {
        for (int index = 0; index < content.Length;)
        {
            if (char.IsDigit(content[index]))
            {
                int next = ReadNumberToken(content, index);
                AppendRun(block, content[index..next], ColCyan);
                index = next;
                continue;
            }

            if (content[index] == '(' && TryReadDelimited(content, index, '(', ')', out string parenthetical))
            {
                AppendRun(block, parenthetical, ColMagenta);
                index += parenthetical.Length;
                continue;
            }

            if (content[index] == '[' && TryReadDelimited(content, index, '[', ']', out string bracketed))
            {
                AppendRun(block, bracketed, ColYellow);
                index += bracketed.Length;
                continue;
            }

            AppendRun(block, content[index].ToString(), ColMagenta);
            index++;
        }
    }

    private static void AppendMineLineContent(TextBlock block, string content)
    {
        for (int index = 0; index < content.Length;)
        {
            if (char.IsDigit(content[index]))
            {
                int next = ReadNumberToken(content, index);
                AppendRun(block, content[index..next], ColCyan);
                index = next;
                continue;
            }

            if (content[index] == '(' && TryReadDelimited(content, index, '(', ')', out string parenthetical))
            {
                AppendRun(block, parenthetical, parenthetical.Contains("belong", StringComparison.OrdinalIgnoreCase) ? ColGreen : ColMagenta);
                index += parenthetical.Length;
                continue;
            }

            AppendRun(block, content[index].ToString(), ColMagenta);
            index++;
        }
    }

    private static void AppendWarpLineContent(TextBlock block, string content)
    {
        for (int index = 0; index < content.Length;)
        {
            if (content[index] == '(' && TryReadDelimited(content, index, '(', ')', out string parenthetical))
            {
                AppendRun(block, parenthetical, ColRed);
                index += parenthetical.Length;
                continue;
            }

            if (char.IsDigit(content[index]))
            {
                int next = ReadNumberToken(content, index);
                AppendRun(block, content[index..next], ColCyan);
                index = next;
                continue;
            }

            AppendRun(block, content[index].ToString(), content[index] == '-' ? ColGreen : ColText);
            index++;
        }
    }

    private static void AppendTraderShipLineContent(TextBlock block, string content)
    {
        for (int index = 0; index < content.Length;)
        {
            if (char.IsDigit(content[index]))
            {
                int next = ReadNumberToken(content, index);
                AppendRun(block, content[index..next], ColCyan);
                index = next;
                continue;
            }

            if (content[index] == '[' && TryReadDelimited(content, index, '[', ']', out string bracketed))
            {
                AppendRun(block, bracketed, ColBlue);
                index += bracketed.Length;
                continue;
            }

            if (content[index] == '(' && TryReadDelimited(content, index, '(', ')', out string parenthetical))
            {
                AppendRun(block, parenthetical, ColRed);
                index += parenthetical.Length;
                continue;
            }

            AppendRun(block, content[index].ToString(), content.StartsWith("in ", StringComparison.OrdinalIgnoreCase) ? ColGreen : ColWhite);
            index++;
        }
    }

    private static void AppendGenericTwText(TextBlock block, string text)
    {
        for (int index = 0; index < text.Length;)
        {
            if (char.IsDigit(text[index]))
            {
                int next = ReadNumberToken(text, index);
                AppendRun(block, text[index..next], ColCyan);
                index = next;
                continue;
            }

            if (text[index] == '[' && TryReadDelimited(text, index, '[', ']', out string bracketed))
            {
                AppendRun(block, bracketed, ColBlue);
                index += bracketed.Length;
                continue;
            }

            if (text[index] == '(' && TryReadDelimited(text, index, '(', ')', out string parenthetical))
            {
                AppendRun(block, parenthetical, ColMagenta);
                index += parenthetical.Length;
                continue;
            }

            AppendRun(block, text[index].ToString(), ColText);
            index++;
        }
    }

    private static bool TryReadDelimited(string text, int start, char open, char close, out string token)
    {
        token = string.Empty;
        int end = text.IndexOf(close, start + 1);
        if (end < 0)
            return false;

        token = text[start..(end + 1)];
        return true;
    }

    private static int ReadNumberToken(string text, int start)
    {
        int index = start;
        while (index < text.Length && (char.IsDigit(text[index]) || text[index] == ','))
            index++;
        return index;
    }

    private static int FindNextSpecial(string text, int start, params char[] specials)
    {
        int index = start;
        while (index < text.Length && !specials.Contains(text[index]))
            index++;
        return index;
    }

    private static void AppendRun(TextBlock block, string text, IBrush brush)
    {
        if (string.IsNullOrEmpty(text))
            return;

        block.Inlines?.Add(new Run(text) { Foreground = brush });
    }

    private Control BuildPreformattedCard(string text, string title)
        => new Border
        {
            Background = BgCardAlt,
            BorderBrush = InnerEdge,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(10),
            Margin = new Thickness(0, 0, 0, 8),
            Child = new StackPanel
            {
                Spacing = 6,
                Children =
                {
                    new TextBlock
                    {
                        Text = title,
                        Foreground = ColAccent,
                        FontWeight = FontWeight.SemiBold,
                        FontSize = BodyFontSize,
                    },
                    new TextBlock
                    {
                        Text = text,
                        Foreground = ColText,
                        FontFamily = MonoFont,
                        FontSize = BodyFontSize,
                        TextWrapping = TextWrapping.NoWrap,
                    }
                }
            }
        };

    private static Control BuildTableHeader(params string[] columns)
        => BuildResultLine(string.Join(" | ", columns.Select(c => c.PadRight(Math.Max(6, c.Length)))), header: true);

    private static Control BuildResultLine(string text, bool header = false)
        => new Border
        {
            Background = header ? BgCard : BgRow,
            BorderBrush = InnerEdge,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(8, 5),
            Margin = new Thickness(0, 0, 0, 3),
            Child = new TextBlock
            {
                Text = text,
                Foreground = header ? ColAccent : ColText,
                FontFamily = MonoFont,
                FontSize = BodyFontSize,
                TextWrapping = TextWrapping.NoWrap,
            }
        };

    private Control BuildLimitNotice(int total, int displayed)
        => new Border
        {
            Background = BgCard,
            BorderBrush = Edge,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(10),
            Margin = new Thickness(0, 4, 0, 0),
            Child = new TextBlock
            {
                Text = $"Showing first {displayed:N0} of {total:N0} results. Use Copy for the full displayed result text.",
                Foreground = ColYellow,
                FontSize = BodyFontSize,
                TextWrapping = TextWrapping.Wrap,
            }
        };

    private void AddEmptyResult(string message)
    {
        _resultList.ItemsSource = null;
        _selectedPreviewSector = 0;
        _previewMap?.SetPreviewSelection(null, legendText: "Find preview");
        _previewMap?.FollowLiveSector();
        _previewHeader.Text = message;
        _previewHeader.Foreground = ColMuted;
        _resultsHost.Children.Clear();
        _resultsHost.Children.Add(new TextBlock
        {
            Text = message,
            Foreground = ColMuted,
            FontSize = BodyFontSize,
            Margin = new Thickness(4),
        });
    }

    private void ShowMessage(string message, IBrush brush)
    {
        _resultHeader.Text = message;
        _resultHeader.Foreground = brush;
        AddEmptyResult(message);
    }
}
