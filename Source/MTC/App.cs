using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using TWXProxy.Core;

namespace MTC;

public class App : Application
{
    public override void Initialize()
    {
        Styles.Add(new FluentTheme());
        RequestedThemeVariant = ThemeVariant.Dark;

        // Replace the console-stub factory with the real Avalonia popup factory
        // so that script WINDOW / SETWINDOWCONTENTS / KILLWINDOW commands open
        // actual popup windows.
        GlobalModules.ScriptWindowFactory = new AvaloniaScriptWindowFactory();
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow();
            if (OperatingSystem.IsMacOS())
                MacDockIcon.SetFromAsset();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
