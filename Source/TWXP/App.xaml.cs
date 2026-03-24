using TWXProxy.Core;
using TWXP.Services;

namespace TWXP;

public partial class App : Application
{
    public App()
    {
        InitializeComponent();

        // Clear/initialize debug log file at application startup
        GlobalModules.InitializeDebugLog();

        // Initialize debug logging
        GlobalModules.DebugLog("Application initialization complete\n");
        GlobalModules.DebugLog($"Debug log path: {GlobalModules.DebugLogPath}\n");

        // Initialize MAUI window factory for script windows
        GlobalModules.ScriptWindowFactory = new MauiScriptWindowFactory();

        GlobalModules.DebugLog("ScriptWindowFactory initialized\n");

        MainPage = new AppShell();
    }
}
