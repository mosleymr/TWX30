using System.Collections.ObjectModel;
using System.Windows.Input;
using TWXP.Models;
using TWXP.Services;
using TWXProxy.Core;

namespace TWXP.ViewModels;

public class MainViewModel : BaseViewModel
{
    private readonly IGameConfigService _configService;
    private readonly IProxyService _proxyService;
    private readonly IDirectoryPickerService _directoryPickerService;
    private GameConfigViewModel? _selectedGame;
    private string _programDirectory = string.Empty;
    private string _scriptsDirectory = string.Empty;

    public ObservableCollection<GameConfigViewModel> Games { get; } = new();
    public GameConfigViewModel? SelectedGame
    {
        get => _selectedGame;
        set => SetProperty(ref _selectedGame, value);
    }

    public string ScriptsDirectory
    {
        get => _scriptsDirectory;
        set
        {
            if (SetProperty(ref _scriptsDirectory, value))
                OnPropertyChanged(nameof(ScriptsDirectoryDisplay));
        }
    }

    public string ScriptsDirectoryDisplay
        => string.IsNullOrWhiteSpace(ScriptsDirectory)
            ? "Scripts Directory: (default)"
            : $"Scripts Directory: {ScriptsDirectory}";

    public string ProgramDirectory
    {
        get => _programDirectory;
        set
        {
            if (SetProperty(ref _programDirectory, value))
                OnPropertyChanged(nameof(ProgramDirectoryDisplay));
        }
    }

    public string ProgramDirectoryDisplay
        => string.IsNullOrWhiteSpace(ProgramDirectory)
            ? "Program Directory: (default)"
            : $"Program Directory: {ProgramDirectory}";

    public ICommand AddGameCommand { get; }
    public ICommand LoadGameCommand { get; }
    public ICommand LoadConfigsCommand { get; }
    public ICommand SelectProgramDirectoryCommand { get; }
    public ICommand SelectScriptsDirectoryCommand { get; }

    public MainViewModel(
        IGameConfigService configService,
        IProxyService proxyService,
        IDirectoryPickerService directoryPickerService)
    {
        _configService = configService;
        _proxyService = proxyService;
        _directoryPickerService = directoryPickerService;

        AddGameCommand = new AsyncRelayCommand(async _ => await AddGameAsync());
        LoadGameCommand = new AsyncRelayCommand(async _ => await LoadGameAsync());
        LoadConfigsCommand = new AsyncRelayCommand(async _ => await LoadConfigsAsync());
        SelectProgramDirectoryCommand = new AsyncRelayCommand(async _ => await SelectProgramDirectoryAsync());
        SelectScriptsDirectoryCommand = new AsyncRelayCommand(async _ => await SelectScriptsDirectoryAsync());

        _proxyService.StatusChanged += OnGameStatusChanged;
        _configService.ProgramDirectoryChanged += OnProgramDirectoryChanged;
        _configService.ScriptsDirectoryChanged += OnScriptsDirectoryChanged;
    }

    public async Task InitializeAsync()
    {
        ProgramDirectory = await _configService.GetProgramDirectoryAsync();
        ScriptsDirectory = await _configService.GetScriptsDirectoryAsync();
        if (!SharedPaths.HasStoredProgramDir())
            await SelectProgramDirectoryAsync();

        await LoadConfigsAsync();
        await _proxyService.ConnectAutoStartGamesAsync(Games.Select(g => g.Config));
    }

    private async Task LoadConfigsAsync()
    {
        if (IsBusy)
            return;

        IsBusy = true;

        try
        {
            Games.Clear();
            var configs = await _configService.LoadConfigsAsync();
            ProgramDirectory = await _configService.GetProgramDirectoryAsync();
            ScriptsDirectory = await _configService.GetScriptsDirectoryAsync();

            foreach (var config in configs)
            {
                // Restore runtime status from ProxyService
                config.Status = _proxyService.GetGameStatus(config.Id);
                
                var vm = new GameConfigViewModel(config, _configService, _proxyService, _directoryPickerService, RemoveGame);
                Games.Add(vm);
            }

            SelectedGame ??= Games.FirstOrDefault();
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task AddGameAsync()
    {
        var newConfig = new GameConfig
        {
            Name = $"Game {Games.Count + 1}",
            ScriptDirectory = ScriptsDirectory
        };

        var vm = new GameConfigViewModel(newConfig, _configService, _proxyService, _directoryPickerService, RemoveGame);
        SelectedGame = vm;
        var page = new GameConfigPage
        {
            BindingContext = vm
        };

        // Open in a new window (popup)
        await Application.Current!.MainPage!.Navigation.PushModalAsync(page);
    }

    private async Task LoadGameAsync()
    {
        try
        {
            System.Diagnostics.Debug.WriteLine("LoadGameAsync called");
            
            var options = new PickOptions
            {
                PickerTitle = "Select TWX Game File (.json)"
            };

            var result = await FilePicker.Default.PickAsync(options);
            System.Diagnostics.Debug.WriteLine($"File picker result: {result?.FileName ?? "null"}");
            
            if (result != null)
            {
                System.Diagnostics.Debug.WriteLine($"Selected file: {result.FullPath}");

                GameConfig? config = null;

                if (result.FileName.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                {
                    // Load an existing GAMENAME.json game data file.
                    try
                    {
                        var json = await File.ReadAllTextAsync(result.FullPath);
                        config = System.Text.Json.JsonSerializer.Deserialize(
                            json, TWXP.Services.GameConfigJsonContext.Default.GameConfig);
                        if (config != null)
                            config.GameDataFilePath = result.FullPath;
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Failed to parse game file: {ex.Message}");
                    }
                }

                // Fallback: create a new game config (e.g. user picked a .twx database).
                config ??= new GameConfig
                {
                    Name = Path.GetFileNameWithoutExtension(result.FileName),
                    DatabasePath = result.FullPath
                };

                await _configService.SaveConfigAsync(config);
                
                // Avoid duplicate entries if the file was already in the list.
                if (Games.All(g => g.Config.Id != config.Id))
                {
                    var vm = new GameConfigViewModel(config, _configService, _proxyService, _directoryPickerService, RemoveGame);
                    Games.Add(vm);
                    SelectedGame = vm;
                }
                
                System.Diagnostics.Debug.WriteLine("Game added to list");
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error loading game: {ex.Message}");
            System.Diagnostics.Debug.WriteLine($"Stack trace: {ex.StackTrace}");
            
            await Application.Current!.MainPage!.DisplayAlert("Error", 
                $"Failed to load game: {ex.Message}", "OK");
        }
    }

    private async Task SelectScriptsDirectoryAsync()
    {
        var selectedDirectory = await _directoryPickerService.PickDirectoryAsync();
        if (string.IsNullOrWhiteSpace(selectedDirectory))
            return;

        await _configService.SetScriptsDirectoryAsync(selectedDirectory);
    }

    private async Task SelectProgramDirectoryAsync()
    {
        var selectedDirectory = await _directoryPickerService.PickDirectoryAsync();
        if (string.IsNullOrWhiteSpace(selectedDirectory))
            return;

        await _configService.SetProgramDirectoryAsync(selectedDirectory);
        await LoadConfigsAsync();
    }

    private void OnGameStatusChanged(object? sender, GameStatusChangedEventArgs e)
    {
        var game = Games.FirstOrDefault(g => g.Config.Id == e.GameId);
        if (game != null)
        {
            game.Status = e.Status;
        }
    }

    private void OnScriptsDirectoryChanged(object? sender, string scriptsDirectory)
    {
        ScriptsDirectory = scriptsDirectory;
        foreach (var game in Games)
            game.ScriptDirectory = scriptsDirectory;
    }

    private void OnProgramDirectoryChanged(object? sender, string programDirectory)
    {
        ProgramDirectory = programDirectory;
    }

    private void RemoveGame(GameConfigViewModel game)
    {
        Games.Remove(game);
        if (ReferenceEquals(SelectedGame, game))
            SelectedGame = Games.FirstOrDefault();
    }
}
