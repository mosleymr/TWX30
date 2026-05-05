using Avalonia;
using TWXProxy.Core;

if (MTC.UnixAutoDetach.TryRelaunchDetached(args))
    return;

// MTC is a GUI application — suppress all Console output so diagnostic
// Console.WriteLine calls in Core do not leak to the terminal.
Console.SetOut(TextWriter.Null);

var prefs = MTC.AppPreferences.Load();
MTC.AppPaths.SetConfiguredProgramDir(prefs.ProgramDirectory);
GlobalModules.ProgramDir = MTC.AppPaths.ProgramDir;
GlobalModules.PreferPreparedVm = prefs.PreparedVmEnabled;
GlobalModules.EnableVmMetrics = prefs.VmMetricsEnabled;
MTC.AppPaths.EnsureDebugLogDir();
MTC.AppPaths.ResetStartupDebugLogs();
GlobalModules.ConfigureDebugLogging(
    MTC.AppPaths.GetDebugLogPath(),
    prefs.DebugLoggingEnabled,
    prefs.VerboseDebugLogging,
    prefs.TriggerDebugLogging);
GlobalModules.ConfigureHaggleDebugLogging(
    MTC.AppPaths.GetPortHaggleDebugLogPath(),
    prefs.DebugPortHaggleEnabled,
    MTC.AppPaths.GetPlanetHaggleDebugLogPath(),
    prefs.DebugPlanetHaggleEnabled);

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

AppBuilder.Configure<MTC.App>()
    .UsePlatformDetect()
    .WithInterFont()
    .LogToTrace()
    .StartWithClassicDesktopLifetime(args);
