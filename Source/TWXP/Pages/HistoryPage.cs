using TWXP.Services;
using TWXProxy.Core;

namespace TWXP;

internal sealed class HistoryPage : ContentPage
{
    private readonly IProxyService _proxyService;
    private readonly string _gameId;
    private readonly Picker _channelPicker;
    private readonly Editor _viewer;
    private IDispatcherTimer? _refreshTimer;

    public HistoryPage(string gameId, string gameName, IProxyService proxyService)
    {
        _proxyService = proxyService;
        _gameId = gameId;

        Title = $"History - {gameName}";

        _channelPicker = new Picker
        {
            Title = "Channel",
            ItemsSource = new[] { "Messages", "Fighters", "Computer" },
            SelectedIndex = 0,
        };
        _channelPicker.SelectedIndexChanged += async (_, _) => await RefreshAsync();

        _viewer = new Editor
        {
            IsReadOnly = true,
            AutoSize = EditorAutoSizeOption.Disabled,
            HeightRequest = 440,
            FontFamily = "Menlo",
        };

        var refreshButton = new Button { Text = "Refresh" };
        refreshButton.Clicked += async (_, _) => await RefreshAsync();

        var clearButton = new Button { Text = "Clear Current" };
        clearButton.Clicked += async (_, _) => await ClearCurrentAsync();

        var closeButton = new Button { Text = "Close" };
        closeButton.Clicked += async (_, _) => await Navigation.PopModalAsync();

        Content = new VerticalStackLayout
        {
            Padding = 16,
            Spacing = 12,
            Children =
            {
                _channelPicker,
                _viewer,
                new HorizontalStackLayout
                {
                    Spacing = 10,
                    Children =
                    {
                        refreshButton,
                        clearButton,
                        closeButton,
                    }
                }
            }
        };
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await RefreshAsync();

        _refreshTimer ??= Dispatcher.CreateTimer();
        _refreshTimer.Interval = TimeSpan.FromMilliseconds(750);
        _refreshTimer.Tick += async (_, _) => await RefreshAsync();
        _refreshTimer.Start();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _refreshTimer?.Stop();
    }

    private async Task RefreshAsync()
    {
        HistorySnapshot snapshot = await _proxyService.GetHistoryAsync(_gameId);
        _viewer.Text = _channelPicker.SelectedIndex switch
        {
            1 => string.Join(Environment.NewLine, snapshot.Fighters),
            2 => string.Join(Environment.NewLine, snapshot.Computer),
            _ => string.Join(Environment.NewLine, snapshot.Messages),
        };
    }

    private async Task ClearCurrentAsync()
    {
        HistoryType type = _channelPicker.SelectedIndex switch
        {
            1 => HistoryType.Fighter,
            2 => HistoryType.Computer,
            _ => HistoryType.Msg,
        };

        await _proxyService.ClearHistoryAsync(_gameId, type);
        await RefreshAsync();
    }
}
