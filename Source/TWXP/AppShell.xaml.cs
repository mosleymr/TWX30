namespace TWXP;

public partial class AppShell : Shell
{
    public AppShell()
    {
        InitializeComponent();
        
        Routing.RegisterRoute(nameof(GameConfigPage), typeof(GameConfigPage));
    }
}
