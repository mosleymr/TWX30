using TWXP.ViewModels;
using TWXP.Services;
using TWXProxy.Core;

namespace TWXP;

public partial class MainPage : ContentPage
{
    private readonly MainViewModel _viewModel;

    public MainPage(MainViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        BindingContext = _viewModel;

        // Register the overlay AbsoluteLayout so script window commands can add panels to it.
        GlobalModules.PanelOverlay = new MauiPanelOverlayService(ScriptWindowOverlay);
        GlobalModules.DebugLog("[MainPage] PanelOverlay registered\n");
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
    }
}
