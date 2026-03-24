using Avalonia;
using TWXProxy.Core;

// MTC is a GUI application — suppress all Console output so diagnostic
// Console.WriteLine calls in Core do not leak to the terminal.
Console.SetOut(TextWriter.Null);

GlobalModules.DebugLogPath = "/tmp/mtc_debug.log";
GlobalModules.InitializeDebugLog();

AppBuilder.Configure<MTC.App>()
    .UsePlatformDetect()
    .WithInterFont()
    .LogToTrace()
    .StartWithClassicDesktopLifetime(args);
