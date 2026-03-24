using TWXP.Models;
using TWXP.ViewModels;
using TWXP.Services;

namespace TWXP;

public partial class GameConfigPage : ContentPage
{
    private GameConfigViewModel? _viewModel;

    public GameConfigPage()
    {
        InitializeComponent();
    }

    protected override void OnBindingContextChanged()
    {
        base.OnBindingContextChanged();
        _viewModel = BindingContext as GameConfigViewModel;
    }

    private async void OnCancelClicked(object sender, EventArgs e)
    {
        await Navigation.PopModalAsync();
    }
}
