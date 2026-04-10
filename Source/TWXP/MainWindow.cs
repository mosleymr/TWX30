using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using TWXProxy.Core;
using MenuItem = Avalonia.Controls.MenuItem;

namespace TWXP;

public sealed class MainWindow : Window
{
    private static readonly IBrush StatusStoppedBrush = Brushes.Gray;
    private static readonly IBrush StatusStartingBrush = Brushes.Orange;
    private static readonly IBrush StatusRunningBrush = Brushes.LimeGreen;
    private static readonly IBrush StatusPausedBrush = Brushes.Goldenrod;
    private static readonly IBrush StatusErrorBrush = Brushes.IndianRed;
    private static readonly IBrush SelectedRowBrush = new SolidColorBrush(Color.FromRgb(0x2b, 0x36, 0x46));
    private static readonly IBrush RowBrush = new SolidColorBrush(Color.FromRgb(0x1f, 0x24, 0x2c));

    private readonly IGameConfigService _configService;
    private readonly IProxyService _proxyService;

    private readonly TextBlock _programDirectoryText;
    private readonly TextBlock _scriptsDirectoryText;
    private readonly TextBlock _selectedGameText;
    private readonly StackPanel _gamesPanel;
    private readonly MenuItem _proxyMenu;
    private readonly Button _startButton;
    private readonly Button _stopButton;
    private readonly Button _editButton;
    private readonly Button _removeButton;

    private List<GameConfig> _games = new();
    private GameConfig? _selectedGame;
    private bool _initialized;

    public MainWindow()
    {
        _configService = new GameConfigService();
        _proxyService = new ProxyService(_configService);
        _proxyService.StatusChanged += OnProxyStatusChanged;

        Title = "TWX Proxy";
        Width = 1180;
        Height = 760;
        MinWidth = 860;
        MinHeight = 580;

        var fileMenu = new MenuItem { Header = "_File" };
        fileMenu.ItemsSource = BuildFileMenuItems();

        _proxyMenu = new MenuItem { Header = "_Proxy" };

        var menuBar = new Menu
        {
            ItemsSource = new object[] { fileMenu, _proxyMenu },
        };
        DockPanel.SetDock(menuBar, Dock.Top);

        var programDirButton = new Button { Content = "Program Dir…" };
        programDirButton.Click += async (_, _) => await SelectProgramDirectoryAsync();
        _programDirectoryText = new TextBlock
        {
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
        };

        var scriptsDirButton = new Button { Content = "Scripts Dir…" };
        scriptsDirButton.Click += async (_, _) => await SelectScriptsDirectoryAsync();
        _scriptsDirectoryText = new TextBlock
        {
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
        };

        var addGameButton = new Button { Content = "Add Game…" };
        addGameButton.Click += async (_, _) => await AddGameAsync();

        var loadGameButton = new Button { Content = "Load Game…" };
        loadGameButton.Click += async (_, _) => await LoadGameAsync();

        var refreshButton = new Button { Content = "Refresh" };
        refreshButton.Click += async (_, _) => await LoadConfigsAsync();

        _selectedGameText = new TextBlock
        {
            FontWeight = FontWeight.Bold,
            VerticalAlignment = VerticalAlignment.Center,
        };

        _startButton = new Button { Content = "Start", MinWidth = 90 };
        _startButton.Click += async (_, _) => await StartSelectedGameAsync();

        _stopButton = new Button { Content = "Stop", MinWidth = 90 };
        _stopButton.Click += async (_, _) => await StopSelectedGameAsync();

        _editButton = new Button { Content = "Edit", MinWidth = 90 };
        _editButton.Click += async (_, _) => await EditSelectedGameAsync();

        _removeButton = new Button { Content = "Remove", MinWidth = 90 };
        _removeButton.Click += async (_, _) => await RemoveSelectedGameAsync();

        var toolbar = new StackPanel
        {
            Orientation = Orientation.Vertical,
            Spacing = 10,
            Margin = new Thickness(12),
            Children =
            {
                new WrapPanel
                {
                    Orientation = Orientation.Horizontal,
                    ItemHeight = 32,
                    ItemWidth = double.NaN,
                    HorizontalAlignment = HorizontalAlignment.Left,
                    Children =
                    {
                        programDirButton,
                        _programDirectoryText,
                    },
                },
                new WrapPanel
                {
                    Orientation = Orientation.Horizontal,
                    ItemHeight = 32,
                    ItemWidth = double.NaN,
                    HorizontalAlignment = HorizontalAlignment.Left,
                    Children =
                    {
                        scriptsDirButton,
                        _scriptsDirectoryText,
                    },
                },
                new WrapPanel
                {
                    Orientation = Orientation.Horizontal,
                    ItemHeight = 32,
                    ItemWidth = double.NaN,
                    HorizontalAlignment = HorizontalAlignment.Left,
                    Children =
                    {
                        addGameButton,
                        loadGameButton,
                        refreshButton,
                        _selectedGameText,
                        _startButton,
                        _stopButton,
                        _editButton,
                        _removeButton,
                    },
                },
            },
        };
        DockPanel.SetDock(toolbar, Dock.Top);

        _gamesPanel = new StackPanel
        {
            Spacing = 10,
            Margin = new Thickness(12),
        };

        var scroll = new ScrollViewer
        {
            Content = _gamesPanel,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        };

        Content = new DockPanel
        {
            LastChildFill = true,
            Children =
            {
                menuBar,
                toolbar,
                scroll,
            },
        };

        Opened += async (_, _) =>
        {
            if (_initialized)
                return;

            _initialized = true;
            await InitializeAsync();
        };

        Closed += (_, _) => _ = ShutdownAsync();
    }

    private async Task InitializeAsync()
    {
        if (!SharedPaths.HasStoredProgramDir())
        {
            string? programDirectory = await PickDirectoryAsync(
                "Select TWX Program Directory",
                SharedPaths.GetDefaultProgramDir());
            if (!string.IsNullOrWhiteSpace(programDirectory))
                await _configService.SetProgramDirectoryAsync(programDirectory);
        }

        await LoadConfigsAsync();
        await _proxyService.ConnectAutoStartGamesAsync(_games);
        await LoadConfigsAsync(_selectedGame?.Id);
    }

    private async Task ShutdownAsync()
    {
        List<GameConfig> runningGames = _games
            .Where(game => game.Status == GameStatus.Running || game.Status == GameStatus.Starting)
            .ToList();

        foreach (GameConfig game in runningGames)
        {
            try
            {
                await _proxyService.StopGameAsync(game.Id);
            }
            catch
            {
            }
        }
    }

    private async Task LoadConfigsAsync(string? preferredSelectionId = null)
    {
        string? selectedId = preferredSelectionId ?? _selectedGame?.Id;
        var configs = await _configService.LoadConfigsAsync();
        _games = configs.ToList();
        foreach (GameConfig config in _games)
            config.Status = _proxyService.GetGameStatus(config.Id);

        _selectedGame = _games.FirstOrDefault(game => game.Id == selectedId) ?? _games.FirstOrDefault();
        _programDirectoryText.Text = FormatDirectoryDisplay("Program Directory", await _configService.GetProgramDirectoryAsync());
        _scriptsDirectoryText.Text = FormatDirectoryDisplay("Scripts Directory", await _configService.GetScriptsDirectoryAsync());
        RefreshGamesPanel();
        RefreshSelectedGameUi();
        RebuildProxyMenu();
    }

    private void RefreshGamesPanel()
    {
        _gamesPanel.Children.Clear();
        if (_games.Count == 0)
        {
            _gamesPanel.Children.Add(new TextBlock
            {
                Text = "No games configured. Use Add Game or Load Game to get started.",
                TextWrapping = TextWrapping.Wrap,
                Opacity = 0.8,
            });
            return;
        }

        foreach (GameConfig game in _games)
        {
            var nameText = new TextBlock
            {
                Text = game.Name,
                FontSize = 18,
                FontWeight = FontWeight.Bold,
            };
            var statusText = new TextBlock
            {
                Text = GetStatusText(game.Status),
                Foreground = GetStatusBrush(game.Status),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(10, 0, 0, 0),
            };

            var titleRow = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Children = { nameText, statusText },
            };

            var detailText = new TextBlock
            {
                Text = $"{game.Host}:{game.Port}  listen {game.ListenPort}  sectors {game.Sectors}  scripts {GetScriptDirectory(game)}",
                TextWrapping = TextWrapping.Wrap,
                Opacity = 0.8,
            };

            var startButton = new Button
            {
                Content = "Start",
                IsEnabled = game.Status is GameStatus.Stopped or GameStatus.Error,
                MinWidth = 80,
            };
            startButton.Click += async (_, _) =>
            {
                SelectGame(game);
                await StartSelectedGameAsync();
            };

            var stopButton = new Button
            {
                Content = "Stop",
                IsEnabled = game.Status is GameStatus.Running or GameStatus.Starting,
                MinWidth = 80,
            };
            stopButton.Click += async (_, _) =>
            {
                SelectGame(game);
                await StopSelectedGameAsync();
            };

            var editButton = new Button { Content = "Edit", MinWidth = 80 };
            editButton.Click += async (_, _) =>
            {
                SelectGame(game);
                await EditSelectedGameAsync();
            };

            var removeButton = new Button { Content = "Remove", MinWidth = 80 };
            removeButton.Click += async (_, _) =>
            {
                SelectGame(game);
                await RemoveSelectedGameAsync();
            };

            var buttonRow = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                Margin = new Thickness(0, 10, 0, 0),
                Children = { startButton, stopButton, editButton, removeButton },
            };

            var border = new Border
            {
                Background = ReferenceEquals(game, _selectedGame) ? SelectedRowBrush : RowBrush,
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(14),
                Child = new StackPanel
                {
                    Spacing = 6,
                    Children = { titleRow, detailText, buttonRow },
                },
            };
            border.PointerPressed += (_, _) => SelectGame(game);

            _gamesPanel.Children.Add(border);
        }
    }

    private void SelectGame(GameConfig game)
    {
        _selectedGame = game;
        RefreshGamesPanel();
        RefreshSelectedGameUi();
        RebuildProxyMenu();
    }

    private void RefreshSelectedGameUi()
    {
        if (_selectedGame == null)
        {
            _selectedGameText.Text = "No game selected";
            _startButton.IsEnabled = false;
            _stopButton.IsEnabled = false;
            _editButton.IsEnabled = false;
            _removeButton.IsEnabled = false;
            return;
        }

        _selectedGameText.Text = $"Selected: {_selectedGame.Name}";
        _startButton.IsEnabled = _selectedGame.Status is GameStatus.Stopped or GameStatus.Error;
        _stopButton.IsEnabled = _selectedGame.Status is GameStatus.Running or GameStatus.Starting;
        _editButton.IsEnabled = true;
        _removeButton.IsEnabled = true;
    }

    private void RebuildProxyMenu()
    {
        _proxyMenu.ItemsSource = BuildProxyMenuItems();
    }

    private List<object> BuildFileMenuItems()
    {
        var addGame = new MenuItem { Header = "_Add Game…" };
        addGame.Click += async (_, _) => await AddGameAsync();

        var loadGame = new MenuItem { Header = "_Load Game…" };
        loadGame.Click += async (_, _) => await LoadGameAsync();

        var selectProgramDirectory = new MenuItem { Header = "_Program Directory…" };
        selectProgramDirectory.Click += async (_, _) => await SelectProgramDirectoryAsync();

        var selectScriptsDirectory = new MenuItem { Header = "_Scripts Directory…" };
        selectScriptsDirectory.Click += async (_, _) => await SelectScriptsDirectoryAsync();

        var refresh = new MenuItem { Header = "_Refresh" };
        refresh.Click += async (_, _) => await LoadConfigsAsync();

        var quit = new MenuItem { Header = "_Quit" };
        quit.Click += (_, _) => Close();

        return new List<object>
        {
            addGame,
            loadGame,
            new Separator(),
            selectProgramDirectory,
            selectScriptsDirectory,
            refresh,
            new Separator(),
            quit,
        };
    }

    private List<object> BuildProxyMenuItems()
    {
        GameConfig? selected = _selectedGame;
        bool hasSelection = selected != null;
        bool isRunning = hasSelection && selected!.Status == GameStatus.Running;

        var items = new List<object>
        {
            new MenuItem
            {
                Header = hasSelection ? EscapeMenuHeaderText($"Current Game: {selected!.Name}") : "No game selected",
                IsEnabled = false,
            },
            new Separator(),
        };

        var editItem = new MenuItem { Header = "_Edit Selected Game…", IsEnabled = hasSelection };
        editItem.Click += async (_, _) => await EditSelectedGameAsync();
        items.Add(editItem);

        var startItem = new MenuItem
        {
            Header = hasSelection ? EscapeMenuHeaderText($"_Start {selected!.Name}") : "_Start Selected Game",
            IsEnabled = hasSelection && selected!.Status is GameStatus.Stopped or GameStatus.Error,
        };
        startItem.Click += async (_, _) => await StartSelectedGameAsync();
        items.Add(startItem);

        var stopItem = new MenuItem
        {
            Header = hasSelection ? EscapeMenuHeaderText($"S_top {selected!.Name}") : "S_top Selected Game",
            IsEnabled = hasSelection && selected!.Status is GameStatus.Running or GameStatus.Starting,
        };
        stopItem.Click += async (_, _) => await StopSelectedGameAsync();
        items.Add(stopItem);

        var resetItem = new MenuItem { Header = "_Reset Selected Proxy", IsEnabled = hasSelection };
        resetItem.Click += async (_, _) => await ResetSelectedGameAsync();
        items.Add(resetItem);

        items.Add(new Separator());
        items.Add(BuildScriptsSubMenu(selected, isRunning));
        items.Add(BuildExportSubMenu(selected, hasSelection));
        items.Add(BuildImportSubMenu(selected, hasSelection));
        items.Add(BuildLoggingSubMenu(selected, isRunning));
        items.Add(BuildQuickSubMenu(selected, isRunning));
        items.Add(BuildBotSubMenu(selected, isRunning));
        return items;
    }

    private MenuItem BuildScriptsSubMenu(GameConfig? selected, bool isRunning)
    {
        var loadItem = new MenuItem { Header = "_Load Script…", IsEnabled = isRunning };
        loadItem.Click += async (_, _) => await LoadScriptAsync(selected);

        var stopNonSystem = new MenuItem { Header = "Stop All _Non-System", IsEnabled = isRunning };
        stopNonSystem.Click += async (_, _) => await StopAllScriptsAsync(selected, includeSystemScripts: false);

        var stopAll = new MenuItem { Header = "Stop _All Scripts", IsEnabled = isRunning };
        stopAll.Click += async (_, _) => await StopAllScriptsAsync(selected, includeSystemScripts: true);

        var runningScripts = new MenuItem { Header = "Stop _Script", IsEnabled = isRunning };
        runningScripts.ItemsSource = new List<object> { new MenuItem { Header = "No active scripts", IsEnabled = false } };
        if (selected != null && isRunning)
        {
            runningScripts.SubmenuOpened += async (_, _) => await PopulateRunningScriptsAsync(selected, runningScripts);
        }

        return new MenuItem
        {
            Header = "_Scripts",
            IsEnabled = selected != null,
            ItemsSource = new List<object> { loadItem, stopNonSystem, stopAll, runningScripts },
        };
    }

    private async Task PopulateRunningScriptsAsync(GameConfig game, MenuItem menu)
    {
        try
        {
            IReadOnlyList<RunningScriptInfo> scripts = await _proxyService.GetRunningScriptsAsync(game.Id);
            Dispatcher.UIThread.Post(() =>
            {
                if (scripts.Count == 0)
                {
                    menu.ItemsSource = new List<object> { new MenuItem { Header = "No active scripts", IsEnabled = false } };
                    return;
                }

                menu.ItemsSource = scripts.Select(script =>
                {
                    int scriptId = script.Id;
                    var item = new MenuItem
                    {
                        Header = EscapeMenuHeaderText(script.IsSystemScript ? $"{script.Name} (system)" : script.Name),
                    };
                    item.Click += async (_, _) => await StopScriptAsync(game, scriptId);
                    return (object)item;
                }).ToList();
            });
        }
        catch
        {
            Dispatcher.UIThread.Post(() =>
            {
                menu.ItemsSource = new List<object> { new MenuItem { Header = "Unable to query scripts", IsEnabled = false } };
            });
        }
    }

    private MenuItem BuildExportSubMenu(GameConfig? selected, bool hasSelection)
    {
        var exportWarps = new MenuItem { Header = "Export _Warps" };
        exportWarps.Click += async (_, _) => await ExportAsync(selected, "warpspec", "txt", _proxyService.ExportWarpsAsync);

        var exportBubbles = new MenuItem { Header = "Export _Bubbles" };
        exportBubbles.Click += async (_, _) => await ExportAsync(selected, "bubbles", "txt", _proxyService.ExportBubblesAsync);

        var exportDeadends = new MenuItem { Header = "Export _Deadends" };
        exportDeadends.Click += async (_, _) => await ExportAsync(selected, "deadends", "txt", _proxyService.ExportDeadendsAsync);

        var exportTwx = new MenuItem { Header = "Export _TWX" };
        exportTwx.Click += async (_, _) => await ExportAsync(selected, selected?.Name ?? "game", "twx", _proxyService.ExportTwxAsync);

        return new MenuItem
        {
            Header = "_Export",
            IsEnabled = hasSelection,
            ItemsSource = new List<object> { exportWarps, exportBubbles, exportDeadends, exportTwx },
        };
    }

    private MenuItem BuildImportSubMenu(GameConfig? selected, bool hasSelection)
    {
        var importWarps = new MenuItem { Header = "Import _Warps" };
        importWarps.Click += async (_, _) => await ImportWarpsAsync(selected);

        var importTwx = new MenuItem { Header = "Import T_WX" };
        importTwx.Click += async (_, _) => await ImportTwxAsync(selected);

        return new MenuItem
        {
            Header = "_Import",
            IsEnabled = hasSelection,
            ItemsSource = new List<object> { importWarps, importTwx },
        };
    }

    private MenuItem BuildLoggingSubMenu(GameConfig? selected, bool isRunning)
    {
        var playCapture = new MenuItem { Header = "_Play Capture…", IsEnabled = isRunning };
        playCapture.Click += async (_, _) => await PlayCaptureAsync(selected);

        var history = new MenuItem { Header = "_History…", IsEnabled = isRunning };
        history.Click += async (_, _) => await ShowHistoryAsync(selected);

        var debugLog = new MenuItem { Header = "Show _Debug Log Path", IsEnabled = selected != null };
        debugLog.Click += async (_, _) => await ShowDebugLogPathAsync(selected);

        return new MenuItem
        {
            Header = "_Logging",
            IsEnabled = selected != null,
            ItemsSource = new List<object> { playCapture, history, debugLog },
        };
    }

    private MenuItem BuildQuickSubMenu(GameConfig? selected, bool isRunning)
    {
        if (selected == null)
        {
            return new MenuItem
            {
                Header = "_Quick",
                IsEnabled = false,
                ItemsSource = new List<object> { new MenuItem { Header = "No game selected", IsEnabled = false } },
            };
        }

        IReadOnlyList<QuickLoadGroup> groups = ProxyMenuCatalog.BuildQuickLoadGroups(GetProgramDir(selected), GetScriptDirectory(selected));
        if (groups.Count == 0)
        {
            return new MenuItem
            {
                Header = "_Quick",
                IsEnabled = false,
                ItemsSource = new List<object> { new MenuItem { Header = "No quick-load scripts found", IsEnabled = false } },
            };
        }

        var items = new List<object>();
        foreach (QuickLoadGroup group in groups)
        {
            var groupMenu = new MenuItem { Header = EscapeMenuHeaderText(group.Name), IsEnabled = isRunning };
            groupMenu.ItemsSource = group.Entries.Select(entry =>
            {
                string relativePath = entry.RelativePath;
                var item = new MenuItem { Header = EscapeMenuHeaderText(entry.DisplayName), IsEnabled = isRunning };
                item.Click += async (_, _) => await LoadQuickScriptAsync(selected, relativePath);
                return (object)item;
            }).ToList();
            items.Add(groupMenu);
        }

        return new MenuItem
        {
            Header = "_Quick",
            IsEnabled = isRunning,
            ItemsSource = items,
        };
    }

    private MenuItem BuildBotSubMenu(GameConfig? selected, bool isRunning)
    {
        if (selected == null)
        {
            return new MenuItem
            {
                Header = "_Bots",
                IsEnabled = false,
                ItemsSource = new List<object> { new MenuItem { Header = "No game selected", IsEnabled = false } },
            };
        }

        IReadOnlyList<BotConfig> bots = ProxyMenuCatalog.LoadBotConfigs(GetProgramDir(selected), GetScriptDirectory(selected));
        if (bots.Count == 0)
        {
            return new MenuItem
            {
                Header = "_Bots",
                IsEnabled = false,
                ItemsSource = new List<object> { new MenuItem { Header = "No bots configured", IsEnabled = false } },
            };
        }

        var items = new List<object>();
        foreach (BotConfig bot in bots)
        {
            string botName = bot.Name;
            var item = new MenuItem
            {
                Header = EscapeMenuHeaderText(string.IsNullOrWhiteSpace(bot.Description) ? bot.Name : $"{bot.Name} - {bot.Description}"),
                IsEnabled = isRunning,
            };
            item.Click += async (_, _) => await SwitchBotAsync(selected, botName);
            items.Add(item);
        }

        return new MenuItem
        {
            Header = "_Bots",
            IsEnabled = isRunning,
            ItemsSource = items,
        };
    }

    private async Task AddGameAsync()
    {
        var newConfig = new GameConfig
        {
            Name = $"Game {_games.Count + 1}",
            ScriptDirectory = await _configService.GetScriptsDirectoryAsync(),
        };

        await OpenGameEditorAsync(newConfig, isNew: true);
    }

    private async Task EditSelectedGameAsync()
    {
        if (_selectedGame == null)
            return;

        await OpenGameEditorAsync(_selectedGame, isNew: false);
    }

    private async Task OpenGameEditorAsync(GameConfig config, bool isNew)
    {
        var dialog = new GameConfigWindow(config, isNew);
        bool accepted = await dialog.ShowDialog<bool>(this);
        if (!accepted)
            return;

        if (dialog.DeleteRequested)
        {
            await DeleteGameAsync(config);
            return;
        }

        if (dialog.ResultConfig == null)
            return;

        await _configService.SaveConfigAsync(dialog.ResultConfig);
        await LoadConfigsAsync(dialog.ResultConfig.Id);
    }

    private async Task LoadGameAsync()
    {
        string? fullPath = await PickSingleFileAsync("Select TWX Game File (.json)", new[] { "*.json", "*.twx" }, AppPaths.GamesDir);
        if (string.IsNullOrWhiteSpace(fullPath))
            return;

        GameConfig? config = null;

        if (fullPath.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                string json = await File.ReadAllTextAsync(fullPath);
                config = JsonSerializer.Deserialize(json, GameConfigJsonContext.Default.GameConfig);
                if (config != null)
                    config.GameDataFilePath = fullPath;
            }
            catch (Exception ex)
            {
                await DialogHelpers.ShowMessageAsync(this, "Load Game Failed", ex.Message);
                return;
            }
        }

        config ??= new GameConfig
        {
            Name = Path.GetFileNameWithoutExtension(fullPath),
            DatabasePath = fullPath,
        };

        await _configService.SaveConfigAsync(config);
        await LoadConfigsAsync(config.Id);
    }

    private async Task SelectProgramDirectoryAsync()
    {
        string? selectedDirectory = await PickDirectoryAsync(
            "Select TWX Program Directory",
            await _configService.GetProgramDirectoryAsync());
        if (string.IsNullOrWhiteSpace(selectedDirectory))
            return;

        await _configService.SetProgramDirectoryAsync(selectedDirectory);
        await LoadConfigsAsync();
    }

    private async Task SelectScriptsDirectoryAsync()
    {
        string? selectedDirectory = await PickDirectoryAsync(
            "Select Scripts Directory",
            await _configService.GetScriptsDirectoryAsync());
        if (string.IsNullOrWhiteSpace(selectedDirectory))
            return;

        await _configService.SetScriptsDirectoryAsync(selectedDirectory);
        await LoadConfigsAsync(_selectedGame?.Id);
    }

    private async Task StartSelectedGameAsync()
    {
        if (_selectedGame == null)
            return;

        bool started = await _proxyService.StartGameAsync(_selectedGame);
        if (!started)
            await DialogHelpers.ShowMessageAsync(this, "Start Game", "This game is already running.");

        await LoadConfigsAsync(_selectedGame.Id);
    }

    private async Task StopSelectedGameAsync()
    {
        if (_selectedGame == null)
            return;

        await _proxyService.StopGameAsync(_selectedGame.Id);
        await LoadConfigsAsync(_selectedGame.Id);
    }

    private async Task ResetSelectedGameAsync()
    {
        if (_selectedGame == null)
            return;

        try
        {
            await _proxyService.ResetGameAsync(_selectedGame.Id);
        }
        catch (Exception ex)
        {
            await DialogHelpers.ShowMessageAsync(this, "Proxy Reset Failed", ex.Message);
        }

        await LoadConfigsAsync(_selectedGame.Id);
    }

    private async Task RemoveSelectedGameAsync()
    {
        if (_selectedGame == null)
            return;

        bool confirmed = await DialogHelpers.ShowConfirmAsync(
            this,
            "Remove Game",
            $"Remove '{_selectedGame.Name}' from the list? The game data file will not be deleted.",
            "Remove",
            "Cancel");

        if (!confirmed)
            return;

        if (_selectedGame.Status is GameStatus.Running or GameStatus.Starting)
            await _proxyService.StopGameAsync(_selectedGame.Id);

        await _configService.RemoveConfigAsync(_selectedGame.Id);
        await LoadConfigsAsync();
    }

    private async Task DeleteGameAsync(GameConfig config)
    {
        if (config.Status is GameStatus.Running or GameStatus.Starting)
            await _proxyService.StopGameAsync(config.Id);

        await _configService.DeleteConfigAsync(config.Id);
        await LoadConfigsAsync();
    }

    private async Task LoadScriptAsync(GameConfig? selected)
    {
        if (selected == null)
            return;

        try
        {
            string? scriptPath = await PickSingleFileAsync(
                "Select a TWX script (.ts or .cts)",
                new[] { "*.ts", "*.cts" },
                GetScriptDirectory(selected));
            if (string.IsNullOrWhiteSpace(scriptPath))
                return;

            string scriptRoot = GetScriptDirectory(selected);
            if (Path.IsPathRooted(scriptPath) && Path.IsPathRooted(scriptRoot))
            {
                string fullRoot = Path.GetFullPath(scriptRoot)
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
                string fullScript = Path.GetFullPath(scriptPath);
                if (fullScript.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase))
                    scriptPath = Path.GetRelativePath(scriptRoot, scriptPath).Replace('\\', '/');
            }

            await _proxyService.LoadScriptAsync(selected.Id, scriptPath);
        }
        catch (Exception ex)
        {
            await DialogHelpers.ShowMessageAsync(this, "Load Script Failed", ex.Message);
        }
        finally
        {
            RebuildProxyMenu();
        }
    }

    private async Task LoadQuickScriptAsync(GameConfig? selected, string relativePath)
    {
        if (selected == null)
            return;

        try
        {
            await _proxyService.LoadScriptAsync(selected.Id, relativePath);
        }
        catch (Exception ex)
        {
            await DialogHelpers.ShowMessageAsync(this, "Quick Load Failed", ex.Message);
        }
        finally
        {
            RebuildProxyMenu();
        }
    }

    private async Task SwitchBotAsync(GameConfig? selected, string botName)
    {
        if (selected == null)
            return;

        try
        {
            await _proxyService.SwitchBotAsync(selected.Id, botName);
        }
        catch (Exception ex)
        {
            await DialogHelpers.ShowMessageAsync(this, "Switch Bot Failed", ex.Message);
        }
        finally
        {
            RebuildProxyMenu();
        }
    }

    private async Task StopAllScriptsAsync(GameConfig? selected, bool includeSystemScripts)
    {
        if (selected == null)
            return;

        try
        {
            await _proxyService.StopAllScriptsAsync(selected.Id, includeSystemScripts);
        }
        catch (Exception ex)
        {
            await DialogHelpers.ShowMessageAsync(this, "Stop Scripts Failed", ex.Message);
        }
        finally
        {
            RebuildProxyMenu();
        }
    }

    private async Task StopScriptAsync(GameConfig game, int scriptId)
    {
        try
        {
            await _proxyService.StopScriptAsync(game.Id, scriptId);
        }
        catch (Exception ex)
        {
            await DialogHelpers.ShowMessageAsync(this, "Stop Script Failed", ex.Message);
        }
        finally
        {
            RebuildProxyMenu();
        }
    }

    private async Task ExportAsync(GameConfig? selected, string baseName, string extension, Func<string, string, Task> action)
    {
        if (selected == null)
            return;

        string outputPath = BuildDefaultExportPath(selected, baseName, extension);
        try
        {
            await action(selected.Id, outputPath);
            await DialogHelpers.ShowMessageAsync(this, "Export Complete", $"Saved to:\n{outputPath}");
        }
        catch (Exception ex)
        {
            await DialogHelpers.ShowMessageAsync(this, "Export Failed", ex.Message);
        }
    }

    private async Task ImportWarpsAsync(GameConfig? selected)
    {
        if (selected == null)
            return;

        try
        {
            string? inputPath = await PickSingleFileAsync("Select a warp import file", Array.Empty<string>(), AppPaths.GamesDir);
            if (string.IsNullOrWhiteSpace(inputPath))
                return;

            int imported = await _proxyService.ImportWarpsAsync(selected.Id, inputPath);
            await DialogHelpers.ShowMessageAsync(this, "Warp Import Complete", $"Imported {imported} sector rows.");
        }
        catch (Exception ex)
        {
            await DialogHelpers.ShowMessageAsync(this, "Warp Import Failed", ex.Message);
        }
    }

    private async Task ImportTwxAsync(GameConfig? selected)
    {
        if (selected == null)
            return;

        try
        {
            string? inputPath = await PickSingleFileAsync("Select a TWX database export", new[] { "*.twx" }, AppPaths.GamesDir);
            if (string.IsNullOrWhiteSpace(inputPath))
                return;

            bool keepRecent = await DialogHelpers.ShowConfirmAsync(
                this,
                "Import TWX",
                "Keep newer data already stored in the selected game when conflicts are found?",
                "Keep Newer Data",
                "Overwrite");

            TwxImportResult importResult = await _proxyService.ImportTwxAsync(selected.Id, inputPath, keepRecent);
            string message = importResult.WasTruncated || importResult.SkippedInvalidWarps > 0
                ? $"Imported {importResult.ImportedSectorRecords} of {importResult.ExpectedSectorRecords} sector records."
                : "Import completed successfully.";

            if (importResult.WasTruncated)
                message += " The file ended before all header-declared sector records were present.";
            if (importResult.SkippedInvalidWarps > 0)
                message += $" Skipped {importResult.SkippedInvalidWarps} out-of-range warp entries.";

            await DialogHelpers.ShowMessageAsync(this, "TWX Import Complete", message);
        }
        catch (Exception ex)
        {
            await DialogHelpers.ShowMessageAsync(this, "TWX Import Failed", ex.Message);
        }
    }

    private async Task PlayCaptureAsync(GameConfig? selected)
    {
        if (selected == null)
            return;

        try
        {
            string? capturePath = await PickSingleFileAsync("Select a capture file", new[] { "*.cap" }, AppPaths.LogsDir);
            if (string.IsNullOrWhiteSpace(capturePath))
                return;

            bool started = await _proxyService.BeginLogPlaybackAsync(selected.Id, capturePath);
            await DialogHelpers.ShowMessageAsync(
                this,
                started ? "Playback Started" : "Playback Busy",
                started ? "Capture playback has started." : "A capture is already playing or the proxy is not ready.");
        }
        catch (Exception ex)
        {
            await DialogHelpers.ShowMessageAsync(this, "Playback Failed", ex.Message);
        }
    }

    private async Task ShowDebugLogPathAsync(GameConfig? selected)
    {
        if (selected == null)
            return;

        await DialogHelpers.ShowMessageAsync(this, "Game History / Debug Log", AppPaths.DebugLogPathForGame(selected.Name));
    }

    private async Task ShowHistoryAsync(GameConfig? selected)
    {
        if (selected == null)
            return;

        var historyWindow = new HistoryWindow(selected.Id, selected.Name, _proxyService);
        await historyWindow.ShowDialog(this);
    }

    private void OnProxyStatusChanged(object? sender, GameStatusChangedEventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            GameConfig? game = _games.FirstOrDefault(candidate => candidate.Id == e.GameId);
            if (game == null)
                return;

            game.Status = e.Status;
            if (_selectedGame?.Id == e.GameId)
                _selectedGame = game;

            RefreshGamesPanel();
            RefreshSelectedGameUi();
            RebuildProxyMenu();
        });
    }

    private async Task<string?> PickDirectoryAsync(string title, string? startPath)
    {
        IStorageProvider? storage = TopLevel.GetTopLevel(this)?.StorageProvider;
        if (storage == null)
            return null;

        IStorageFolder? startFolder = null;
        if (!string.IsNullOrWhiteSpace(startPath))
            startFolder = await storage.TryGetFolderFromPathAsync(startPath);

        IReadOnlyList<IStorageFolder> folders = await storage.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = title,
            SuggestedStartLocation = startFolder,
            AllowMultiple = false,
        });

        return folders.Count == 0 ? null : folders[0].Path.LocalPath;
    }

    private async Task<string?> PickSingleFileAsync(string title, string[] patterns, string? startPath)
    {
        IStorageProvider? storage = TopLevel.GetTopLevel(this)?.StorageProvider;
        if (storage == null)
            return null;

        IStorageFolder? startFolder = null;
        if (!string.IsNullOrWhiteSpace(startPath))
            startFolder = await storage.TryGetFolderFromPathAsync(startPath);

        List<FilePickerFileType>? filter = null;
        if (patterns.Length > 0)
        {
            filter = new List<FilePickerFileType>
            {
                new FilePickerFileType("Supported Files")
                {
                    Patterns = patterns,
                },
            };
        }

        IReadOnlyList<IStorageFile> files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            SuggestedStartLocation = startFolder,
            FileTypeFilter = filter,
        });

        return files.Count == 0 ? null : files[0].Path.LocalPath;
    }

    private static string GetScriptDirectory(GameConfig game)
    {
        return string.IsNullOrWhiteSpace(game.ScriptDirectory)
            ? GameConfigService.GetDefaultScriptDirectory()
            : game.ScriptDirectory!;
    }

    private static string GetProgramDir(GameConfig game)
    {
        string scriptDirectory = GetScriptDirectory(game).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return Path.GetDirectoryName(scriptDirectory) ?? scriptDirectory;
    }

    private static string BuildDefaultExportPath(GameConfig config, string baseName, string extension)
    {
        string documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        string exportDir = Path.Combine(documents, "TWX Proxy Exports", SharedPaths.SanitizeFileComponent(config.Name));
        Directory.CreateDirectory(exportDir);
        return Path.Combine(exportDir, $"{baseName}.{extension.TrimStart('.')}");
    }

    private static string EscapeMenuHeaderText(string text) => text.Replace("_", "__", StringComparison.Ordinal);

    private static string FormatDirectoryDisplay(string label, string? path)
    {
        return string.IsNullOrWhiteSpace(path)
            ? $"{label}: (default)"
            : $"{label}: {path}";
    }

    private static string GetStatusText(GameStatus status) => status switch
    {
        GameStatus.Stopped => "Stopped",
        GameStatus.Starting => "Starting...",
        GameStatus.Running => "Running",
        GameStatus.Paused => "Paused",
        GameStatus.Error => "Error",
        _ => "Unknown",
    };

    private static IBrush GetStatusBrush(GameStatus status) => status switch
    {
        GameStatus.Stopped => StatusStoppedBrush,
        GameStatus.Starting => StatusStartingBrush,
        GameStatus.Running => StatusRunningBrush,
        GameStatus.Paused => StatusPausedBrush,
        GameStatus.Error => StatusErrorBrush,
        _ => StatusStoppedBrush,
    };
}
