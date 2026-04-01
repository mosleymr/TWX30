using Avalonia;
using TWXProxy.Core;

// MTC is a GUI application — suppress all Console output so diagnostic
// Console.WriteLine calls in Core do not leak to the terminal.
Console.SetOut(TextWriter.Null);

var prefs = MTC.AppPreferences.Load();
SharedPaths.EnsureLogDir();
GlobalModules.ConfigureDebugLogging(
    Path.Combine(SharedPaths.LogDir, "mtc_debug.log"),
    prefs.DebugLoggingEnabled,
    prefs.VerboseDebugLogging);

AppBuilder.Configure<MTC.App>()
    .UsePlatformDetect()
    .WithInterFont()
    .LogToTrace()
    .StartWithClassicDesktopLifetime(args);
