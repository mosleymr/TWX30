using Avalonia;
using TWXProxy.Core;

// MTC is a GUI application — suppress all Console output so diagnostic
// Console.WriteLine calls in Core do not leak to the terminal.
Console.SetOut(TextWriter.Null);

var prefs = MTC.AppPreferences.Load();
GlobalModules.ProgramDir = MTC.AppPaths.GetEffectiveProgramDir(prefs.ScriptsDirectory);
MTC.AppPaths.EnsureDebugLogDir(prefs.ScriptsDirectory);
GlobalModules.ConfigureDebugLogging(
    MTC.AppPaths.GetDebugLogPath(prefs.ScriptsDirectory),
    prefs.DebugLoggingEnabled,
    prefs.VerboseDebugLogging);

AppBuilder.Configure<MTC.App>()
    .UsePlatformDetect()
    .WithInterFont()
    .LogToTrace()
    .StartWithClassicDesktopLifetime(args);
