using System.Collections.Specialized;
using System.ComponentModel;
using TWXP.ViewModels;
using TWXP.Services;
using TWXProxy.Core;

namespace TWXP;

public partial class MainPage : ContentPage
{
    private readonly MainViewModel _viewModel;
    private readonly IProxyService _proxyService;

    public MainPage(MainViewModel viewModel, IProxyService proxyService)
    {
        InitializeComponent();
        _viewModel = viewModel;
        _proxyService = proxyService;
        BindingContext = _viewModel;

        // Register the overlay AbsoluteLayout so script window commands can add panels to it.
        GlobalModules.PanelOverlay = new MauiPanelOverlayService(ScriptWindowOverlay);
        GlobalModules.DebugLog("[MainPage] PanelOverlay registered\n");
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        _viewModel.Games.CollectionChanged += OnGamesCollectionChanged;
        _proxyService.StatusChanged += (_, _) => MainThread.BeginInvokeOnMainThread(RebuildMenus);
        RebuildMenus();
    }

    private bool _initialized = false;

    protected override void OnAppearing()
    {
        base.OnAppearing();
        if (!_initialized)
        {
            _initialized = true;
            _ = _viewModel.InitializeAsync();
        }

        RebuildMenus();
    }

    private void OnGamesCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        RebuildMenus();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (string.IsNullOrEmpty(e.PropertyName) || e.PropertyName == nameof(MainViewModel.SelectedGame))
            RebuildMenus();
    }

    private void RebuildMenus()
    {
        MenuBarItems.Clear();
        MenuBarItems.Add(BuildProxyMenuBar());
        RebuildGameContextMenus();
    }

    private MenuBarItem BuildProxyMenuBar()
    {
        var menu = new MenuBarItem { Text = "Proxy" };
        var selected = _viewModel.SelectedGame;
        bool hasSelection = selected != null;
        bool isRunning = hasSelection && selected!.Status == Models.GameStatus.Running;

        var editItem = new MenuFlyoutItem { Text = hasSelection ? $"Edit {selected!.Name}" : "Edit Selected Game", IsEnabled = hasSelection };
        editItem.Clicked += async (_, _) => await EditSelectedGameAsync();
        menu.Add(editItem);

        var startItem = new MenuFlyoutItem { Text = hasSelection ? $"Start {selected!.Name}" : "Start Selected Game", IsEnabled = hasSelection && selected!.CanStart };
        startItem.Clicked += async (_, _) => await StartSelectedGameAsync();
        menu.Add(startItem);

        var stopItem = new MenuFlyoutItem { Text = hasSelection ? $"Stop {selected!.Name}" : "Stop Selected Game", IsEnabled = hasSelection && selected!.CanStop };
        stopItem.Clicked += async (_, _) => await StopSelectedGameAsync();
        menu.Add(stopItem);

        var resetItem = new MenuFlyoutItem { Text = "Reset Selected Proxy", IsEnabled = hasSelection };
        resetItem.Clicked += async (_, _) => await ResetSelectedGameAsync();
        menu.Add(resetItem);
        menu.Add(new MenuFlyoutSeparator());

        menu.Add(BuildScriptsSubMenu(selected, isRunning));
        menu.Add(BuildExportSubMenu(selected, hasSelection));
        menu.Add(BuildImportSubMenu(selected, hasSelection));
        menu.Add(BuildLoggingSubMenu(selected, isRunning));
        menu.Add(BuildQuickSubMenu(selected, isRunning));
        menu.Add(BuildBotSubMenu(selected, isRunning));
        return menu;
    }

    private void RebuildGameContextMenus()
    {
        foreach (var game in _viewModel.Games)
        {
            game.ProxyContextFlyout = BuildGameContextMenu(game);
        }
    }

    private MenuFlyout BuildGameContextMenu(GameConfigViewModel game)
    {
        var context = new MenuFlyout();
        AddGameContextItems(context.Add, game, false);
        return context;
    }

    private void AddGameContextItems(Action<IMenuElement> add, GameConfigViewModel game, bool includeGroups)
    {
        if (includeGroups)
            add(new MenuFlyoutItem { Text = game.StatusText, IsEnabled = false });

        var start = new MenuFlyoutItem { Text = "Start", IsEnabled = game.CanStart };
        start.Clicked += async (_, _) => await RunGameActionAsync(game, () => game.StartCommand.Execute(null));
        add(start);

        var stop = new MenuFlyoutItem { Text = "Stop", IsEnabled = game.CanStop };
        stop.Clicked += async (_, _) => await RunGameActionAsync(game, () => game.StopCommand.Execute(null));
        add(stop);

        var edit = new MenuFlyoutItem { Text = "Edit Settings" };
        edit.Clicked += async (_, _) => await RunGameActionAsync(game, () => game.EditCommand.Execute(null));
        add(edit);

        add(new MenuFlyoutSeparator());
        add(BuildScriptsSubMenu(game, game.Status == Models.GameStatus.Running));
        add(BuildExportSubMenu(game, true));
        add(BuildImportSubMenu(game, true));
        add(BuildLoggingSubMenu(game, game.Status == Models.GameStatus.Running));
        add(BuildQuickSubMenu(game, game.Status == Models.GameStatus.Running));
        add(BuildBotSubMenu(game, game.Status == Models.GameStatus.Running));
    }

    private MenuFlyoutSubItem BuildScriptsSubMenu(GameConfigViewModel? selected, bool isRunning)
    {
        var menu = new MenuFlyoutSubItem { Text = "Scripts", IsEnabled = selected != null };

        var loadItem = new MenuFlyoutItem { Text = "Load Script...", IsEnabled = isRunning };
        loadItem.Clicked += async (_, _) => await LoadScriptAsync(selected);
        menu.Add(loadItem);

        var stopNonSys = new MenuFlyoutItem { Text = "Stop All Non-System", IsEnabled = isRunning };
        stopNonSys.Clicked += async (_, _) => await StopAllScriptsAsync(selected, includeSystemScripts: false);
        menu.Add(stopNonSys);

        var stopAll = new MenuFlyoutItem { Text = "Stop All Scripts", IsEnabled = isRunning };
        stopAll.Clicked += async (_, _) => await StopAllScriptsAsync(selected, includeSystemScripts: true);
        menu.Add(stopAll);

        var runningScripts = new MenuFlyoutSubItem { Text = "Stop Script", IsEnabled = isRunning };
        if (selected != null && isRunning)
        {
            _ = PopulateRunningScriptsAsync(selected, runningScripts);
        }
        else
        {
            runningScripts.Add(new MenuFlyoutItem { Text = "No active scripts", IsEnabled = false });
        }

        menu.Add(runningScripts);
        return menu;
    }

    private async Task PopulateRunningScriptsAsync(GameConfigViewModel game, MenuFlyoutSubItem menu)
    {
        try
        {
            var scripts = await _proxyService.GetRunningScriptsAsync(game.Config.Id);
            MainThread.BeginInvokeOnMainThread(() =>
            {
                menu.Clear();
                if (scripts.Count == 0)
                {
                    menu.Add(new MenuFlyoutItem { Text = "No active scripts", IsEnabled = false });
                    return;
                }

                foreach (var script in scripts)
                {
                    var item = new MenuFlyoutItem
                    {
                        Text = script.IsSystemScript ? $"{script.Name} (system)" : script.Name
                    };
                    int scriptId = script.Id;
                    item.Clicked += async (_, _) => await StopScriptAsync(game, scriptId);
                    menu.Add(item);
                }
            });
        }
        catch
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                menu.Clear();
                menu.Add(new MenuFlyoutItem { Text = "Unable to query scripts", IsEnabled = false });
            });
        }
    }

    private MenuFlyoutSubItem BuildExportSubMenu(GameConfigViewModel? selected, bool hasSelection)
    {
        var menu = new MenuFlyoutSubItem { Text = "Export", IsEnabled = hasSelection };

        var exportWarps = new MenuFlyoutItem { Text = "Export Warps" };
        exportWarps.Clicked += async (_, _) => await ExportAsync(selected, "warpspec", "txt", _proxyService.ExportWarpsAsync);
        menu.Add(exportWarps);

        var exportBubbles = new MenuFlyoutItem { Text = "Export Bubbles" };
        exportBubbles.Clicked += async (_, _) => await ExportAsync(selected, "bubbles", "txt", _proxyService.ExportBubblesAsync);
        menu.Add(exportBubbles);

        var exportDeadends = new MenuFlyoutItem { Text = "Export Deadends" };
        exportDeadends.Clicked += async (_, _) => await ExportAsync(selected, "deadends", "txt", _proxyService.ExportDeadendsAsync);
        menu.Add(exportDeadends);

        var exportTwx = new MenuFlyoutItem { Text = "Export TWX" };
        exportTwx.Clicked += async (_, _) => await ExportAsync(selected, selected?.Name ?? "game", "twx", _proxyService.ExportTwxAsync);
        menu.Add(exportTwx);

        return menu;
    }

    private MenuFlyoutSubItem BuildImportSubMenu(GameConfigViewModel? selected, bool hasSelection)
    {
        var menu = new MenuFlyoutSubItem { Text = "Import", IsEnabled = hasSelection };

        var importWarps = new MenuFlyoutItem { Text = "Import Warps" };
        importWarps.Clicked += async (_, _) => await ImportWarpsAsync(selected);
        menu.Add(importWarps);

        var importTwx = new MenuFlyoutItem { Text = "Import TWX" };
        importTwx.Clicked += async (_, _) => await ImportTwxAsync(selected);
        menu.Add(importTwx);

        return menu;
    }

    private MenuFlyoutSubItem BuildLoggingSubMenu(GameConfigViewModel? selected, bool isRunning)
    {
        var menu = new MenuFlyoutSubItem { Text = "Logging", IsEnabled = selected != null };

        var playCapture = new MenuFlyoutItem { Text = "Play Capture...", IsEnabled = isRunning };
        playCapture.Clicked += async (_, _) => await PlayCaptureAsync(selected);
        menu.Add(playCapture);

        var history = new MenuFlyoutItem { Text = "History...", IsEnabled = isRunning };
        history.Clicked += async (_, _) => await ShowHistoryAsync(selected);
        menu.Add(history);

        var debugLog = new MenuFlyoutItem { Text = "Show Debug Log Path", IsEnabled = selected != null };
        debugLog.Clicked += async (_, _) => await ShowDebugLogPathAsync(selected);
        menu.Add(debugLog);

        return menu;
    }

    private MenuFlyoutSubItem BuildQuickSubMenu(GameConfigViewModel? selected, bool isRunning)
    {
        var menu = new MenuFlyoutSubItem { Text = "Quick", IsEnabled = selected != null && isRunning };
        if (selected == null)
        {
            menu.Add(new MenuFlyoutItem { Text = "No game selected", IsEnabled = false });
            return menu;
        }

        var groups = ProxyMenuCatalog.BuildQuickLoadGroups(GetProgramDir(selected), GetScriptDirectory(selected));
        if (groups.Count == 0)
        {
            menu.Add(new MenuFlyoutItem { Text = "No quick-load scripts found", IsEnabled = false });
            return menu;
        }

        foreach (var group in groups)
        {
            var groupMenu = new MenuFlyoutSubItem { Text = group.Name, IsEnabled = isRunning };
            foreach (var entry in group.Entries)
            {
                string relativePath = entry.RelativePath;
                var item = new MenuFlyoutItem { Text = entry.DisplayName, IsEnabled = isRunning };
                item.Clicked += async (_, _) => await LoadQuickScriptAsync(selected, relativePath);
                groupMenu.Add(item);
            }

            menu.Add(groupMenu);
        }

        return menu;
    }

    private MenuFlyoutSubItem BuildBotSubMenu(GameConfigViewModel? selected, bool isRunning)
    {
        var menu = new MenuFlyoutSubItem { Text = "Bots", IsEnabled = selected != null && isRunning };
        if (selected == null)
        {
            menu.Add(new MenuFlyoutItem { Text = "No game selected", IsEnabled = false });
            return menu;
        }

        var bots = ProxyMenuCatalog.LoadBotConfigs(GetProgramDir(selected), GetScriptDirectory(selected));
        if (bots.Count == 0)
        {
            menu.Add(new MenuFlyoutItem { Text = "No bots configured", IsEnabled = false });
            return menu;
        }

        foreach (var bot in bots)
        {
            string botName = bot.Name;
            var item = new MenuFlyoutItem
            {
                Text = string.IsNullOrWhiteSpace(bot.Description) ? bot.Name : $"{bot.Name} - {bot.Description}",
                IsEnabled = isRunning,
            };
            item.Clicked += async (_, _) => await SwitchBotAsync(selected, botName);
            menu.Add(item);
        }

        return menu;
    }

    private async Task EditSelectedGameAsync()
    {
        var selected = _viewModel.SelectedGame;
        if (selected == null)
            return;

        _viewModel.SelectedGame = selected;
        selected.EditCommand.Execute(null);
    }

    private async Task StartSelectedGameAsync()
    {
        var selected = _viewModel.SelectedGame;
        if (selected == null)
            return;

        selected.StartCommand.Execute(null);
        await Task.CompletedTask;
    }

    private async Task StopSelectedGameAsync()
    {
        var selected = _viewModel.SelectedGame;
        if (selected == null)
            return;

        selected.StopCommand.Execute(null);
        await Task.CompletedTask;
    }

    private async Task ResetSelectedGameAsync()
    {
        var selected = _viewModel.SelectedGame;
        if (selected == null)
            return;

        try
        {
            await _proxyService.ResetGameAsync(selected.Config.Id);
        }
        catch (Exception ex)
        {
            await DisplayAlert("Proxy Reset Failed", ex.Message, "OK");
        }
        finally
        {
            RebuildMenus();
        }
    }

    private async Task LoadScriptAsync(GameConfigViewModel? selected)
    {
        if (selected == null)
            return;

        try
        {
            var result = await FilePicker.Default.PickAsync(new PickOptions
            {
                PickerTitle = "Select a TWX script (.ts or .cts)",
                FileTypes = new FilePickerFileType(new Dictionary<DevicePlatform, IEnumerable<string>>
                {
                    { DevicePlatform.iOS, new[] { "public.data" } },
                    { DevicePlatform.MacCatalyst, new[] { "public.data" } },
                    { DevicePlatform.WinUI, new[] { ".ts", ".cts" } },
                    { DevicePlatform.Android, new[] { "*/*" } }
                })
            });

            if (result == null)
                return;

            string scriptPath = result.FullPath;
            string scriptRoot = string.IsNullOrWhiteSpace(selected.Config.ScriptDirectory)
                ? GameConfigService.GetDefaultScriptDirectory()
                : selected.Config.ScriptDirectory;

            if (Path.IsPathRooted(scriptPath) && Path.IsPathRooted(scriptRoot))
            {
                string fullRoot = Path.GetFullPath(scriptRoot)
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
                string fullScript = Path.GetFullPath(scriptPath);
                if (fullScript.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase))
                    scriptPath = Path.GetRelativePath(scriptRoot, scriptPath).Replace('\\', '/');
            }

            await _proxyService.LoadScriptAsync(selected.Config.Id, scriptPath);
        }
        catch (Exception ex)
        {
            await DisplayAlert("Load Script Failed", ex.Message, "OK");
        }
        finally
        {
            RebuildMenus();
        }
    }

    private async Task LoadQuickScriptAsync(GameConfigViewModel? selected, string relativePath)
    {
        if (selected == null)
            return;

        try
        {
            await _proxyService.LoadScriptAsync(selected.Config.Id, relativePath);
        }
        catch (Exception ex)
        {
            await DisplayAlert("Quick Load Failed", ex.Message, "OK");
        }
        finally
        {
            RebuildMenus();
        }
    }

    private async Task SwitchBotAsync(GameConfigViewModel? selected, string botName)
    {
        if (selected == null)
            return;

        try
        {
            await _proxyService.SwitchBotAsync(selected.Config.Id, botName);
        }
        catch (Exception ex)
        {
            await DisplayAlert("Switch Bot Failed", ex.Message, "OK");
        }
        finally
        {
            RebuildMenus();
        }
    }

    private async Task StopAllScriptsAsync(GameConfigViewModel? selected, bool includeSystemScripts)
    {
        if (selected == null)
            return;

        try
        {
            await _proxyService.StopAllScriptsAsync(selected.Config.Id, includeSystemScripts);
        }
        catch (Exception ex)
        {
            await DisplayAlert("Stop Scripts Failed", ex.Message, "OK");
        }
        finally
        {
            RebuildMenus();
        }
    }

    private async Task StopScriptAsync(GameConfigViewModel? selected, int scriptId)
    {
        if (selected == null)
            return;

        try
        {
            await _proxyService.StopScriptAsync(selected.Config.Id, scriptId);
        }
        catch (Exception ex)
        {
            await DisplayAlert("Stop Script Failed", ex.Message, "OK");
        }
        finally
        {
            RebuildMenus();
        }
    }

    private async Task ExportAsync(
        GameConfigViewModel? selected,
        string baseName,
        string extension,
        Func<string, string, Task> action)
    {
        if (selected == null)
            return;

        string outputPath = BuildDefaultExportPath(selected.Config, baseName, extension);
        try
        {
            await action(selected.Config.Id, outputPath);
            await DisplayAlert("Export Complete", $"Saved to:\n{outputPath}", "OK");
        }
        catch (Exception ex)
        {
            await DisplayAlert("Export Failed", ex.Message, "OK");
        }
    }

    private async Task ImportWarpsAsync(GameConfigViewModel? selected)
    {
        if (selected == null)
            return;

        try
        {
            var result = await FilePicker.Default.PickAsync(new PickOptions
            {
                PickerTitle = "Select a warp import file",
            });

            if (result == null)
                return;

            int imported = await _proxyService.ImportWarpsAsync(selected.Config.Id, result.FullPath);
            await DisplayAlert("Warp Import Complete", $"Imported {imported} sector rows.", "OK");
        }
        catch (Exception ex)
        {
            await DisplayAlert("Warp Import Failed", ex.Message, "OK");
        }
    }

    private async Task ImportTwxAsync(GameConfigViewModel? selected)
    {
        if (selected == null)
            return;

        try
        {
            var result = await FilePicker.Default.PickAsync(new PickOptions
            {
                PickerTitle = "Select a TWX database export",
                FileTypes = new FilePickerFileType(new Dictionary<DevicePlatform, IEnumerable<string>>
                {
                    { DevicePlatform.iOS, new[] { "public.data" } },
                    { DevicePlatform.MacCatalyst, new[] { "public.data" } },
                    { DevicePlatform.WinUI, new[] { ".twx" } },
                    { DevicePlatform.Android, new[] { "*/*" } }
                })
            });

            if (result == null)
                return;

            bool keepRecent = await DisplayAlert(
                "Import TWX",
                "Keep newer data already stored in the selected game when conflicts are found?",
                "Keep Newer Data",
                "Overwrite");

            await _proxyService.ImportTwxAsync(selected.Config.Id, result.FullPath, keepRecent);
            await DisplayAlert("TWX Import Complete", "Import completed successfully.", "OK");
        }
        catch (Exception ex)
        {
            await DisplayAlert("TWX Import Failed", ex.Message, "OK");
        }
    }

    private async Task PlayCaptureAsync(GameConfigViewModel? selected)
    {
        if (selected == null)
            return;

        try
        {
            var result = await FilePicker.Default.PickAsync(new PickOptions
            {
                PickerTitle = "Select a capture file",
                FileTypes = new FilePickerFileType(new Dictionary<DevicePlatform, IEnumerable<string>>
                {
                    { DevicePlatform.iOS, new[] { "public.data" } },
                    { DevicePlatform.MacCatalyst, new[] { "public.data" } },
                    { DevicePlatform.WinUI, new[] { ".cap" } },
                    { DevicePlatform.Android, new[] { "*/*" } }
                })
            });

            if (result == null)
                return;

            bool started = await _proxyService.BeginLogPlaybackAsync(selected.Config.Id, result.FullPath);
            await DisplayAlert(
                started ? "Playback Started" : "Playback Busy",
                started ? "Capture playback has started." : "A capture is already playing or the proxy is not ready.",
                "OK");
        }
        catch (Exception ex)
        {
            await DisplayAlert("Playback Failed", ex.Message, "OK");
        }
    }

    private async Task ShowDebugLogPathAsync(GameConfigViewModel? selected)
    {
        if (selected == null)
            return;

        string path = AppPaths.DebugLogPathForGame(selected.Config.Name);
        await DisplayAlert("Game History / Debug Log", path, "OK");
    }

    private async Task ShowHistoryAsync(GameConfigViewModel? selected)
    {
        if (selected == null)
            return;

        await Navigation.PushModalAsync(new NavigationPage(new HistoryPage(selected.Config.Id, selected.Config.Name, _proxyService)));
    }

    private static string GetScriptDirectory(GameConfigViewModel game)
    {
        return string.IsNullOrWhiteSpace(game.Config.ScriptDirectory)
            ? GameConfigService.GetDefaultScriptDirectory()
            : game.Config.ScriptDirectory;
    }

    private static string GetProgramDir(GameConfigViewModel game)
    {
        string scriptDirectory = GetScriptDirectory(game).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return Path.GetDirectoryName(scriptDirectory) ?? scriptDirectory;
    }

    private static string BuildDefaultExportPath(Models.GameConfig config, string baseName, string extension)
    {
        string documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        string exportDir = Path.Combine(documents, "TWX Proxy Exports", SharedPaths.SanitizeFileComponent(config.Name));
        Directory.CreateDirectory(exportDir);
        return Path.Combine(exportDir, $"{baseName}.{extension.TrimStart('.')}");
    }

    private async Task RunGameActionAsync(GameConfigViewModel game, Action action)
    {
        try
        {
            _viewModel.SelectedGame = game;
            action();
            await Task.CompletedTask;
        }
        finally
        {
            RebuildMenus();
        }
    }
}
