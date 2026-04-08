using System.Windows.Input;
using TWXP.Models;
using TWXP.Services;

namespace TWXP.ViewModels;

public class GameConfigViewModel : BaseViewModel
{
    private readonly IGameConfigService _configService;
    private readonly IProxyService _proxyService;
    private readonly IDirectoryPickerService _directoryPickerService;
    private readonly Action<GameConfigViewModel>? _onRemove;
    private GameStatus _status;
    private MenuFlyout? _proxyContextFlyout;

    public GameConfig Config { get; }

    public string Name
    {
        get => Config.Name;
        set
        {
            if (Config.Name != value)
            {
                Config.Name = value;
                OnPropertyChanged();
                
                // Automatically set database path based on game name
                if (!string.IsNullOrWhiteSpace(value))
                {
                    var safeFileName = string.Join("_", value.Split(Path.GetInvalidFileNameChars()));
                    Config.DatabasePath = $"{safeFileName}.twx";
                    OnPropertyChanged(nameof(DatabasePath));
                }
            }
        }
    }

    public string Host
    {
        get => Config.Host;
        set
        {
            if (Config.Host != value)
            {
                Config.Host = value;
                OnPropertyChanged();
            }
        }
    }

    public int Port
    {
        get => Config.Port;
        set
        {
            if (Config.Port != value)
            {
                Config.Port = value;
                OnPropertyChanged();
            }
        }
    }

    public int ListenPort
    {
        get => Config.ListenPort;
        set
        {
            if (Config.ListenPort != value)
            {
                Config.ListenPort = value;
                OnPropertyChanged();
            }
        }
    }

    public string CommandKey
    {
        get => Config.CommandChar.ToString();
        set
        {
            char next = string.IsNullOrWhiteSpace(value) ? '$' : value.Trim()[0];
            if (Config.CommandChar != next)
            {
                Config.CommandChar = next;
                OnPropertyChanged();
            }
        }
    }

    public int Sectors
    {
        get => Config.Sectors;
        set
        {
            if (Config.Sectors != value)
            {
                Config.Sectors = value;
                OnPropertyChanged();
            }
        }
    }

    public string DatabasePath
    {
        get => Config.DatabasePath;
        set
        {
            if (Config.DatabasePath != value)
            {
                Config.DatabasePath = value;
                OnPropertyChanged();
            }
        }
    }

    public string ScriptDirectory
    {
        get => Config.ScriptDirectory ?? string.Empty;
        set
        {
            if (Config.ScriptDirectory != value)
            {
                Config.ScriptDirectory = value;
                OnPropertyChanged();
            }
        }
    }

    public bool AutoConnect
    {
        get => Config.AutoConnect;
        set
        {
            if (Config.AutoConnect != value)
            {
                Config.AutoConnect = value;
                OnPropertyChanged();
            }
        }
    }

    public bool AutoReconnect
    {
        get => Config.AutoReconnect;
        set
        {
            if (Config.AutoReconnect != value)
            {
                Config.AutoReconnect = value;
                OnPropertyChanged();
            }
        }
    }

    public int ReconnectDelaySeconds
    {
        get => Config.ReconnectDelaySeconds;
        set
        {
            int next = Math.Max(1, value);
            if (Config.ReconnectDelaySeconds != next)
            {
                Config.ReconnectDelaySeconds = next;
                OnPropertyChanged();
            }
        }
    }

    public bool NativeHaggleEnabled
    {
        get => Config.NativeHaggleEnabled;
        set
        {
            if (Config.NativeHaggleEnabled != value)
            {
                Config.NativeHaggleEnabled = value;
                OnPropertyChanged();
            }
        }
    }

    public bool UseCache
    {
        get => Config.UseCache;
        set
        {
            if (Config.UseCache != value)
            {
                Config.UseCache = value;
                OnPropertyChanged();
            }
        }
    }

    public int BubbleSize
    {
        get => Config.BubbleSize;
        set
        {
            int next = Math.Max(1, value);
            if (Config.BubbleSize != next)
            {
                Config.BubbleSize = next;
                OnPropertyChanged();
            }
        }
    }

    public bool LocalEcho
    {
        get => Config.LocalEcho;
        set
        {
            if (Config.LocalEcho != value)
            {
                Config.LocalEcho = value;
                OnPropertyChanged();
            }
        }
    }

    public bool LogEnabled
    {
        get => Config.LogEnabled;
        set
        {
            if (Config.LogEnabled != value)
            {
                Config.LogEnabled = value;
                OnPropertyChanged();
            }
        }
    }

    public bool LogAnsi
    {
        get => Config.LogAnsi;
        set
        {
            if (Config.LogAnsi != value)
            {
                Config.LogAnsi = value;
                OnPropertyChanged();
            }
        }
    }

    public bool LogBinary
    {
        get => Config.LogBinary;
        set
        {
            if (Config.LogBinary != value)
            {
                Config.LogBinary = value;
                OnPropertyChanged();
            }
        }
    }

    public bool NotifyPlayCuts
    {
        get => Config.NotifyPlayCuts;
        set
        {
            if (Config.NotifyPlayCuts != value)
            {
                Config.NotifyPlayCuts = value;
                OnPropertyChanged();
            }
        }
    }

    public int MaxPlayDelay
    {
        get => Config.MaxPlayDelay;
        set
        {
            int next = Math.Max(1, value);
            if (Config.MaxPlayDelay != next)
            {
                Config.MaxPlayDelay = next;
                OnPropertyChanged();
            }
        }
    }

    public bool AcceptExternal
    {
        get => Config.AcceptExternal;
        set
        {
            if (Config.AcceptExternal != value)
            {
                Config.AcceptExternal = value;
                OnPropertyChanged();
            }
        }
    }

    public bool AllowLerkers
    {
        get => Config.AllowLerkers;
        set
        {
            if (Config.AllowLerkers != value)
            {
                Config.AllowLerkers = value;
                OnPropertyChanged();
            }
        }
    }

    public bool BroadcastMessages
    {
        get => Config.BroadcastMessages;
        set
        {
            if (Config.BroadcastMessages != value)
            {
                Config.BroadcastMessages = value;
                OnPropertyChanged();
            }
        }
    }

    public bool StreamingMode
    {
        get => Config.StreamingMode;
        set
        {
            if (Config.StreamingMode != value)
            {
                Config.StreamingMode = value;
                OnPropertyChanged();
            }
        }
    }

    public string ExternalAddress
    {
        get => Config.ExternalAddress;
        set
        {
            string next = value?.Trim() ?? string.Empty;
            if (Config.ExternalAddress != next)
            {
                Config.ExternalAddress = next;
                OnPropertyChanged();
            }
        }
    }

    public string LerkerAddress
    {
        get => Config.LerkerAddress;
        set
        {
            string next = value?.Trim() ?? string.Empty;
            if (Config.LerkerAddress != next)
            {
                Config.LerkerAddress = next;
                OnPropertyChanged();
            }
        }
    }

    public bool UseLogin
    {
        get => Config.UseLogin;
        set
        {
            if (Config.UseLogin != value)
            {
                Config.UseLogin = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(ShowLoginDetails));
            }
        }
    }

    public bool UseRLogin
    {
        get => Config.UseRLogin;
        set
        {
            if (Config.UseRLogin != value)
            {
                Config.UseRLogin = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(ShowLoginDetails));
            }
        }
    }

    public string LoginScript
    {
        get => Config.LoginScript;
        set
        {
            string next = string.IsNullOrWhiteSpace(value) ? "0_Login.cts" : value.Trim();
            if (Config.LoginScript != next)
            {
                Config.LoginScript = next;
                OnPropertyChanged();
            }
        }
    }

    public string LoginName
    {
        get => Config.LoginName;
        set
        {
            if (Config.LoginName != value)
            {
                Config.LoginName = value;
                OnPropertyChanged();
            }
        }
    }

    public string Password
    {
        get => Config.Password;
        set
        {
            if (Config.Password != value)
            {
                Config.Password = value;
                OnPropertyChanged();
            }
        }
    }

    public string GameLetter
    {
        get => Config.GameLetter;
        set
        {
            string next = string.IsNullOrWhiteSpace(value)
                ? string.Empty
                : value.Trim().Substring(0, 1).ToUpperInvariant();
            if (Config.GameLetter != next)
            {
                Config.GameLetter = next;
                OnPropertyChanged();
            }
        }
    }

    public bool ShowLoginDetails => UseLogin || UseRLogin;

    public GameStatus Status
    {
        get => _status;
        set
        {
            if (SetProperty(ref _status, value))
            {
                OnPropertyChanged(nameof(StatusText));
                OnPropertyChanged(nameof(StatusColor));
                OnPropertyChanged(nameof(CanStart));
                OnPropertyChanged(nameof(CanStop));
            }
        }
    }

    public string StatusText => Status switch
    {
        GameStatus.Stopped => "Stopped",
        GameStatus.Starting => "Starting...",
        GameStatus.Running => "Running",
        GameStatus.Paused => "Paused",
        GameStatus.Error => "Error",
        _ => "Unknown"
    };

    public Color StatusColor => Status switch
    {
        GameStatus.Stopped => Colors.Gray,
        GameStatus.Starting => Colors.Orange,
        GameStatus.Running => Colors.Green,
        GameStatus.Paused => Colors.Yellow,
        GameStatus.Error => Colors.Red,
        _ => Colors.Gray
    };

    public bool CanStart => Status == GameStatus.Stopped || Status == GameStatus.Error;
    public bool CanStop => Status == GameStatus.Running || Status == GameStatus.Starting;

    public MenuFlyout? ProxyContextFlyout
    {
        get => _proxyContextFlyout;
        set => SetProperty(ref _proxyContextFlyout, value);
    }

    public ICommand StartCommand { get; }
    public ICommand StopCommand { get; }
    public ICommand EditCommand { get; }
    public ICommand RemoveCommand { get; }
    public ICommand DeleteCommand { get; }
    public ICommand SaveCommand { get; }
    public ICommand SelectScriptDirectoryCommand { get; }

    public GameConfigViewModel(
        GameConfig config,
        IGameConfigService configService,
        IProxyService proxyService,
        IDirectoryPickerService directoryPickerService,
        Action<GameConfigViewModel>? onRemove = null)
    {
        Config = config;
        _configService = configService;
        _proxyService = proxyService;
        _directoryPickerService = directoryPickerService;
        _onRemove = onRemove;
        _status = config.Status;

        StartCommand = new AsyncRelayCommand(async _ => await StartAsync(), _ => CanStart);
        StopCommand = new AsyncRelayCommand(async _ => await StopAsync(), _ => CanStop);
        EditCommand = new AsyncRelayCommand(async _ => await EditAsync());
        RemoveCommand = new AsyncRelayCommand(async _ => await RemoveAsync());
        DeleteCommand = new AsyncRelayCommand(async _ => await DeleteAsync());
        SaveCommand = new AsyncRelayCommand(async _ => await SaveAsync());
        SelectScriptDirectoryCommand = new AsyncRelayCommand(async _ => await SelectScriptDirectoryAsync());
    }

    private async Task SelectScriptDirectoryAsync()
    {
        var selectedDirectory = await _directoryPickerService.PickDirectoryAsync();
        System.Diagnostics.Debug.WriteLine($"[GameConfig] Picker returned: {selectedDirectory ?? "<null>"}");
        if (!string.IsNullOrWhiteSpace(selectedDirectory))
        {
            ScriptDirectory = selectedDirectory;
        }
    }

    private async Task StartAsync()
    {
        await _proxyService.StartGameAsync(Config);
    }

    private async Task StopAsync()
    {
        await _proxyService.StopGameAsync(Config.Id);
    }

    private async Task RemoveAsync()
    {
        bool confirm = await Application.Current!.MainPage!.DisplayAlert(
            "Remove Game",
            $"Remove '{Name}' from the list? The game data file will NOT be deleted and can be re-loaded later.",
            "Remove",
            "Cancel");

        if (confirm)
        {
            if (Status == GameStatus.Running || Status == GameStatus.Starting)
                await _proxyService.StopGameAsync(Config.Id);

            // Remove from registry only — game data file is kept on disk.
            await _configService.RemoveConfigAsync(Config.Id);

            _onRemove?.Invoke(this);
        }
    }

    private async Task EditAsync()
    {
        // Create a new page with this view model as the binding context
        var page = new GameConfigPage
        {
            BindingContext = this
        };

        await Application.Current!.MainPage!.Navigation.PushModalAsync(page);
    }

    private async Task DeleteAsync()
    {
        bool confirm = await Application.Current!.MainPage!.DisplayAlert(
            "Remove Game",
            $"Remove '{Name}' from the list? The game data file will NOT be deleted.",
            "Remove",
            "Cancel");

        if (confirm)
        {
            if (Status == GameStatus.Running || Status == GameStatus.Starting)
                await _proxyService.StopGameAsync(Config.Id);

            // Remove from registry and delete the game data file.
            await _configService.DeleteConfigAsync(Config.Id);

            if (Application.Current?.MainPage?.Navigation?.ModalStack.Count > 0)
                await Application.Current.MainPage.Navigation.PopModalAsync();

            _onRemove?.Invoke(this);
        }
    }

    private async Task SaveAsync()
    {
        System.Diagnostics.Debug.WriteLine($"[GameConfig] Saving ScriptDirectory: {Config.ScriptDirectory}");
        await _configService.SaveConfigAsync(Config);
        
        // Close the modal
        if (Application.Current?.MainPage?.Navigation?.ModalStack.Count > 0)
        {
            await Application.Current.MainPage.Navigation.PopModalAsync();
        }
    }
}
