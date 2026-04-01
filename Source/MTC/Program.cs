using Avalonia;
using TWXProxy.Core;

// MTC is a GUI application — suppress all Console output so diagnostic
// Console.WriteLine calls in Core do not leak to the terminal.
Console.SetOut(TextWriter.Null);

SharedPaths.EnsureLogDir();
GlobalModules.DebugLogPath = Path.Combine(SharedPaths.LogDir, "mtc_debug.log");
GlobalModules.InitializeDebugLog();

AppBuilder.Configure<MTC.App>()
    .UsePlatformDetect()
    .WithInterFont()
    .LogToTrace()
    .StartWithClassicDesktopLifetime(args);
