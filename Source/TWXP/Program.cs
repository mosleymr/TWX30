using Avalonia;
using TWXProxy.Core;
using TWXP;

Console.SetOut(TextWriter.Null);

AppPaths.EnsureDirectories();
GlobalModules.ProgramDir = AppPaths.ProgramDir;
GlobalModules.DebugLogPath = Path.Combine(AppPaths.LogsDir, "twxp_debug.log");
GlobalModules.InitializeDebugLog();

AppDomain.CurrentDomain.UnhandledException += (_, e) =>
{
    try
    {
        GlobalModules.DebugLog($"[UnhandledException] {e.ExceptionObject}\n");
    }
    catch
    {
    }
};

TaskScheduler.UnobservedTaskException += (_, e) =>
{
    try
    {
        GlobalModules.DebugLog($"[UnobservedTaskException] {e.Exception}\n");
    }
    catch
    {
    }
};

AppBuilder.Configure<App>()
    .UsePlatformDetect()
    .WithInterFont()
    .LogToTrace()
    .StartWithClassicDesktopLifetime(args);
