using TWXProxy.Core;

var tests = new (string Name, Action Body)[]
{
    ("Destroyed star port notice clears PORT.EXISTS in current sector", DestroyedStarPortNoticeClearsPortExists),
    ("Destroyed port display clears PORT.EXISTS and keeps dead marker", DestroyedPortDisplayClearsPortExists),
    ("Setting BUSTED stamps current bust date", SettingBustedStampsCurrentBustDate),
    ("AutoRecorder sudden bust records dated bust", AutoRecorderSuddenBustRecordsDatedBust),
    ("AutoRecorder fake bust phrase records fake bust", AutoRecorderFakeBustPhraseRecordsFakeBust),
    ("AutoRecorder busted announcement preserves dated bust fields", AutoRecorderBustedAnnouncementPreservesDatedBustFields),
    ("ClearBustsBefore clears only previous dated busts", ClearBustsBeforeClearsOnlyPreviousDatedBusts),
    ("Ship status parser publishes latest slash sector and fighters", ShipStatusParserPublishesLatestSlashSectorAndFighters),
    ("Script constants use latest slash ship status", ScriptConstantsUseLatestSlashShipStatus),
    ("AutoRecorder prompt restores current sector after holo sector display", AutoRecorderPromptRestoresCurrentSectorAfterHoloSectorDisplay),
    ("Disabled debug categories do not construct interpolated messages", DisabledDebugCategoriesSkipInterpolation),
    ("Disabled debug categories allocate no interpolated messages", DisabledDebugCategoriesAllocateNothing),
    ("Comma-formatted TWGS values are numeric", CommaFormattedTwxValuesAreNumeric),
    ("Nested script loads preserve configured script root", NestedScriptLoadsPreserveConfiguredScriptRoot),
    ("Delay-triggered halt removes script and emits stop event", DelayTriggeredHaltRemovesScriptAndEmitsStopEvent),
    ("Prompt probe rearms line trigger after partial prompt handler", PromptProbeRearmsLineTriggerAfterPartialPromptHandler),
    ("Prompt probe fires only once across partial and complete views of one line", PromptProbeFiresOnlyOncePerLine),
    ("Distinct prompt probes can fire on one unterminated line", DistinctPromptProbesFireOnOneLine),
    ("Sector parameter scans count as watchdog activity", SectorParameterScansCountAsWatchdogActivity),
    ("Top-level return terminates without a script error", TopLevelReturnTerminatesWithoutScriptError),
    ("Game file lock inspection reports stale PID", GameFileLockInspectionReportsStalePid),
    ("Game file lock stale removal deletes lock", GameFileLockStaleRemovalDeletesLock),
};

int failed = 0;
foreach ((string name, Action body) in tests)
{
    try
    {
        body();
        Console.WriteLine($"PASS {name}");
    }
    catch (Exception ex)
    {
        failed++;
        Console.Error.WriteLine($"FAIL {name}: {ex.Message}");
    }
}

return failed == 0 ? 0 : 1;

static void DestroyedStarPortNoticeClearsPortExists()
{
    using var fixture = DatabaseFixture.Create();
    SeedLivePort(fixture.Database, 3554);

    var recorder = new AutoRecorder();
    recorder.RecordLine("Command [TL=00:00:00]:[3554] (?=Help)? : ");
    recorder.RecordLine("You destroyed the Star Port!");

    AssertDestroyedPort(fixture.Database, 3554);
}

static void DestroyedPortDisplayClearsPortExists()
{
    using var fixture = DatabaseFixture.Create();
    SeedLivePort(fixture.Database, 3554);

    var recorder = new AutoRecorder();
    recorder.RecordLine("Sector  : 3554 in uncharted space.");
    recorder.RecordLine("Ports   : Scanners indicate massive debris and heavy");

    AssertDestroyedPort(fixture.Database, 3554);
}

static void SeedLivePort(ModDatabase database, int sectorNumber)
{
    SectorData sector = database.GetSector(sectorNumber)
        ?? throw new InvalidOperationException($"Sector {sectorNumber} was not created.");

    sector.SectorPort = new Port
    {
        Name = "Existing Port",
        ClassIndex = 4,
        Dead = false,
        Update = DateTime.Now.AddDays(-1),
    };
    database.SaveSector(sector);
}

static void AssertDestroyedPort(ModDatabase database, int sectorNumber)
{
    SectorData sector = database.GetSector(sectorNumber)
        ?? throw new InvalidOperationException($"Sector {sectorNumber} was not found.");

    if (sector.SectorPort == null)
        throw new InvalidOperationException("Expected a dead port marker, got no port record.");

    if (!sector.SectorPort.Dead)
        throw new InvalidOperationException("Expected destroyed port to be marked dead.");

    if (!string.IsNullOrEmpty(sector.SectorPort.Name))
        throw new InvalidOperationException($"Expected PORT.EXISTS false via empty port name, got '{sector.SectorPort.Name}'.");

    if (sector.SectorPort.ClassIndex != 0)
        throw new InvalidOperationException($"Expected port class to be cleared, got {sector.SectorPort.ClassIndex}.");
}

static void SettingBustedStampsCurrentBustDate()
{
    using var fixture = DatabaseFixture.Create();

    fixture.Database.SetSectorVar(1234, DatabaseConstants.BustParameterName, "true");

    string busted = fixture.Database.GetSectorVar(1234, DatabaseConstants.BustParameterName);
    if (!string.Equals(busted, "true", StringComparison.OrdinalIgnoreCase))
        throw new InvalidOperationException($"Expected BUSTED=true, got '{busted}'.");

    string bustDate = fixture.Database.GetSectorVar(1234, DatabaseConstants.BustDateParameterName);
    string today = DateTime.Now.ToString("yyyy-MM-dd");
    if (!string.Equals(bustDate, today, StringComparison.Ordinal))
        throw new InvalidOperationException($"Expected BUSTDATE={today}, got '{bustDate}'.");
}

static void AutoRecorderSuddenBustRecordsDatedBust()
{
    using var fixture = DatabaseFixture.Create();

    var recorder = new AutoRecorder();
    recorder.RecordLine("Command [TL=00:00:00]:[2468] (?=Help)? : ");
    recorder.RecordLine("Suddenly you're Busted!");

    string busted = fixture.Database.GetSectorVar(2468, DatabaseConstants.BustParameterName);
    if (busted != "1")
        throw new InvalidOperationException($"Expected BUSTED=1, got '{busted}'.");

    string bustDate = fixture.Database.GetSectorVar(2468, DatabaseConstants.BustDateParameterName);
    string today = DateTime.Now.ToString("yyyy-MM-dd");
    if (!string.Equals(bustDate, today, StringComparison.Ordinal))
        throw new InvalidOperationException($"Expected BUSTDATE={today}, got '{bustDate}'.");
}

static void AutoRecorderFakeBustPhraseRecordsFakeBust()
{
    using var fixture = DatabaseFixture.Create();

    var recorder = new AutoRecorder();
    recorder.RecordLine("Command [TL=00:00:00]:[2468] (?=Help)? : ");
    recorder.RecordLine("(You suddenly remember that you were caught stealing here before)");

    string busted = fixture.Database.GetSectorVar(2468, DatabaseConstants.BustParameterName);
    if (busted != "1")
        throw new InvalidOperationException($"Expected BUSTED=1, got '{busted}'.");

    string fakeBust = fixture.Database.GetSectorVar(2468, DatabaseConstants.FakeBustParameterName);
    if (fakeBust != "1")
        throw new InvalidOperationException($"Expected FAKEBUST=1, got '{fakeBust}'.");
}

static void AutoRecorderBustedAnnouncementPreservesDatedBustFields()
{
    using var fixture = DatabaseFixture.Create();

    string today = DateTime.Now.ToString("yyyy-MM-dd");
    fixture.Database.SetSectorVar(3210, DatabaseConstants.BustParameterName, "1");
    fixture.Database.SetSectorVar(3210, DatabaseConstants.FakeBustParameterName, "1");
    fixture.Database.SetSectorVar(3210, DatabaseConstants.BustDateParameterName, today);

    var recorder = new AutoRecorder();
    recorder.RecordLine("R <SS>[Busted:3210]<SS>");

    if (fixture.Database.GetSectorVar(3210, DatabaseConstants.BustParameterName) != "1" ||
        fixture.Database.GetSectorVar(3210, DatabaseConstants.FakeBustParameterName) != "1" ||
        fixture.Database.GetSectorVar(3210, DatabaseConstants.BustDateParameterName) != today)
    {
        throw new InvalidOperationException("Expected subspace busted announcement to preserve MTC dated bust fields.");
    }
}

static void ClearBustsBeforeClearsOnlyPreviousDatedBusts()
{
    using var fixture = DatabaseFixture.Create();

    fixture.Database.SetSectorVar(101, DatabaseConstants.BustParameterName, "1");
    fixture.Database.SetSectorVar(101, DatabaseConstants.FakeBustParameterName, "1");
    fixture.Database.SetSectorVar(101, DatabaseConstants.BustDateParameterName, DateTime.Now.AddDays(-1).ToString("yyyy-MM-dd"));

    fixture.Database.SetSectorVar(102, DatabaseConstants.BustParameterName, "1");
    fixture.Database.SetSectorVar(102, DatabaseConstants.FakeBustParameterName, "1");
    fixture.Database.SetSectorVar(102, DatabaseConstants.BustDateParameterName, DateTime.Now.ToString("yyyy-MM-dd"));

    fixture.Database.SetSectorVar(103, DatabaseConstants.BustParameterName, "1");
    fixture.Database.SetSectorVar(103, DatabaseConstants.BustDateParameterName, string.Empty);

    int cleared = fixture.Database.ClearBustsBefore(DateTime.Now);
    if (cleared != 1)
        throw new InvalidOperationException($"Expected to clear 1 old bust, cleared {cleared}.");

    if (!string.IsNullOrEmpty(fixture.Database.GetSectorVar(101, DatabaseConstants.BustParameterName)) ||
        !string.IsNullOrEmpty(fixture.Database.GetSectorVar(101, DatabaseConstants.FakeBustParameterName)) ||
        !string.IsNullOrEmpty(fixture.Database.GetSectorVar(101, DatabaseConstants.BustDateParameterName)))
    {
        throw new InvalidOperationException("Expected previous-day bust fields to be cleared.");
    }

    if (fixture.Database.GetSectorVar(102, DatabaseConstants.BustParameterName) != "1")
        throw new InvalidOperationException("Expected today's dated bust to remain.");

    if (fixture.Database.GetSectorVar(103, DatabaseConstants.BustParameterName) != "1")
        throw new InvalidOperationException("Expected undated bust to remain.");
}

static void ShipStatusParserPublishesLatestSlashSectorAndFighters()
{
    var parser = new ShipInfoParser();
    ShipStatus? last = null;
    parser.Updated += status => last = CloneStatus(status);

    FeedLoggedSlashStatusToParser(parser, sector: 12016, fighters: 253480);
    FeedLoggedSlashStatusToParser(parser, sector: 8822, fighters: 253471);

    if (last == null)
        throw new InvalidOperationException("Expected slash parser to publish a ship status update.");

    if (last.CurrentSector != 8822)
        throw new InvalidOperationException($"Expected current sector 8822, got {last.CurrentSector}.");

    if (last.Fighters != 253471)
        throw new InvalidOperationException($"Expected fighters 253471, got {last.Fighters}.");
}

static void ScriptConstantsUseLatestSlashShipStatus()
{
    var context = new TwxRuntimeContext("script-constants-slash-status-test");
    using var scope = GlobalModules.UseRuntimeContext(context);
    using var game = new GameInstance(
        "script-constants-slash-status-test",
        "127.0.0.1",
        0,
        0,
        runtimeContext: context);
    ScriptRef.SetActiveGameInstance(context, game);

    try
    {
        FeedLoggedSlashStatusToGame(game, sector: 12016, fighters: 253480);
        FeedLoggedSlashStatusToGame(game, sector: 8822, fighters: 253471);

        var scriptRef = new ScriptRef();
        string currentFighters = ReadSysConst(scriptRef, "CURRENTFIGHTERS");

        if (currentFighters != "253471")
            throw new InvalidOperationException($"Expected CURRENTFIGHTERS=253471, got {currentFighters}.");
    }
    finally
    {
        ScriptRef.SetActiveGameInstance(context, null);
    }
}

static void AutoRecorderPromptRestoresCurrentSectorAfterHoloSectorDisplay()
{
    using var fixture = DatabaseFixture.Create();

    var recorder = GlobalModules.CurrentContext.AutoRecorder;
    recorder.ResetState("core-test");
    recorder.RecordLine("Command [TL=00:00:00]:[12016] (?=Help)? : ");
    recorder.RecordLine("Sector  : 12016 in uncharted space.");
    recorder.RecordLine("Sector  : 13592 in uncharted space.");
    recorder.RecordLine("Sector  : 8822 in uncharted space.");
    recorder.ProcessPrompt("Command [TL=00:00:00]:[8822] (?=Help)? : ");

    if (recorder.CurrentSector != 8822)
        throw new InvalidOperationException($"Expected AutoRecorder current sector 8822, got {recorder.CurrentSector}.");

    if (ScriptRef.GetCurrentSector() != 8822)
        throw new InvalidOperationException($"Expected CURRENTSECTOR source 8822, got {ScriptRef.GetCurrentSector()}.");
}

static void FeedLoggedSlashStatusToParser(ShipInfoParser parser, int sector, int fighters)
{
    parser.FeedLine($" Sect {sector}\u00B3Turns 0\u00B3Creds 37,710,864\u00B3Figs {fighters:N0}\u00B3Shlds 0\u00B3Hlds 230\u00B3Ore 230");
    parser.FeedLine(" Org 0\u00B3Equ 0\u00B3Col 0\u00B3Phot 0\u00B3Armd 255\u00B3Lmpt 255\u00B3GTorp 15\u00B3TWarp 2\u00B3Clks 0\u00B3Beacns 0");
    parser.FeedLine(" AtmDt 15\u00B3Crbo 14,000\u00B3EPrb 0\u00B3MDis 40\u00B3PsPrb No\u00B3PlScn Yes\u00B3LRS Holo");
    parser.FeedLine(" Aln -2,943,163\u00B3Exp 2,385,446\u00B3Corp 5\u00B3Ship 75 Some Ship");
}

static void FeedLoggedSlashStatusToGame(GameInstance game, int sector, int fighters)
{
    game.FeedShipStatusLine($" Sect {sector}\u00B3Turns 0\u00B3Creds 37,710,864\u00B3Figs {fighters:N0}\u00B3Shlds 0\u00B3Hlds 230\u00B3Ore 230");
    game.FeedShipStatusLine(" Org 0\u00B3Equ 0\u00B3Col 0\u00B3Phot 0\u00B3Armd 255\u00B3Lmpt 255\u00B3GTorp 15\u00B3TWarp 2\u00B3Clks 0\u00B3Beacns 0");
    game.FeedShipStatusLine(" AtmDt 15\u00B3Crbo 14,000\u00B3EPrb 0\u00B3MDis 40\u00B3PsPrb No\u00B3PlScn Yes\u00B3LRS Holo");
    game.FeedShipStatusLine(" Aln -2,943,163\u00B3Exp 2,385,446\u00B3Corp 5\u00B3Ship 75 Some Ship");
}

static string ReadSysConst(ScriptRef scriptRef, string name)
{
    int index = scriptRef.FindSysConst(name);
    if (index < 0)
        throw new InvalidOperationException($"System constant {name} was not found.");

    return scriptRef.GetSysConst(index).Read(Array.Empty<string>());
}

static ShipStatus CloneStatus(ShipStatus status) => new()
{
    CurrentSector = status.CurrentSector,
    Fighters = status.Fighters,
};

static void DisabledDebugCategoriesSkipInterpolation()
{
    TwxRuntimeContext context = GlobalModules.CurrentContext;
    bool originalDebug = context.DebugMode;
    bool originalTrigger = context.TriggerDebugMode;
    bool originalScriptTrace = context.ScriptTraceDebugMode;
    bool originalPersistence = context.VariablePersistenceDebugMode;
    bool originalAutoRecorder = context.AutoRecorderDebugMode;
    bool originalPortHaggle = GlobalModules.PortHaggleDebugMode;
    bool originalPlanetHaggle = GlobalModules.PlanetHaggleDebugMode;

    try
    {
        context.DebugMode = false;
        context.TriggerDebugMode = false;
        context.ScriptTraceDebugMode = false;
        context.VariablePersistenceDebugMode = false;
        context.AutoRecorderDebugMode = false;
        GlobalModules.PortHaggleDebugMode = false;
        GlobalModules.PlanetHaggleDebugMode = false;

        var probe = new InterpolationProbe();
        GlobalModules.DebugLog($"debug {probe}");
        GlobalModules.TriggerDebugLog($"trigger {probe}");
        GlobalModules.ScriptTraceDebugLog($"trace {probe}");
        GlobalModules.VariablePersistenceDebugLog($"persistence {probe}");
        GlobalModules.AutoRecorderDebugLog($"recorder {probe}");
        GlobalModules.PortHaggleDebug($"port {probe}");
        GlobalModules.PlanetHaggleDebug($"planet {probe}");

        if (probe.FormatCount != 0)
            throw new InvalidOperationException($"Disabled logging formatted {probe.FormatCount} values.");
    }
    finally
    {
        context.DebugMode = originalDebug;
        context.TriggerDebugMode = originalTrigger;
        context.ScriptTraceDebugMode = originalScriptTrace;
        context.VariablePersistenceDebugMode = originalPersistence;
        context.AutoRecorderDebugMode = originalAutoRecorder;
        GlobalModules.PortHaggleDebugMode = originalPortHaggle;
        GlobalModules.PlanetHaggleDebugMode = originalPlanetHaggle;
    }
}

static void DisabledDebugCategoriesAllocateNothing()
{
    TwxRuntimeContext context = GlobalModules.CurrentContext;
    bool originalDebug = context.DebugMode;
    bool originalTrigger = context.TriggerDebugMode;
    bool originalScriptTrace = context.ScriptTraceDebugMode;
    bool originalPersistence = context.VariablePersistenceDebugMode;
    bool originalAutoRecorder = context.AutoRecorderDebugMode;

    try
    {
        context.DebugMode = false;
        context.TriggerDebugMode = false;
        context.ScriptTraceDebugMode = false;
        context.VariablePersistenceDebugMode = false;
        context.AutoRecorderDebugMode = false;

        LogDisabledIteration(0);
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 10_000; i++)
            LogDisabledIteration(i);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        if (allocated != 0)
            throw new InvalidOperationException($"Disabled interpolated logging allocated {allocated} bytes.");
    }
    finally
    {
        context.DebugMode = originalDebug;
        context.TriggerDebugMode = originalTrigger;
        context.ScriptTraceDebugMode = originalScriptTrace;
        context.VariablePersistenceDebugMode = originalPersistence;
        context.AutoRecorderDebugMode = originalAutoRecorder;
    }
}

static void LogDisabledIteration(int value)
{
    GlobalModules.DebugLog($"debug {value}");
    GlobalModules.TriggerDebugLog($"trigger {value}");
    GlobalModules.ScriptTraceDebugLog($"trace {value}");
    GlobalModules.VariablePersistenceDebugLog($"persistence {value}");
    GlobalModules.AutoRecorderDebugLog($"recorder {value}");
}

static void CommaFormattedTwxValuesAreNumeric()
{
    var integer = new CmdParam { Value = "1,000" };
    if (integer.DecValue != 1000d)
        throw new InvalidOperationException($"Expected 1,000 to coerce to 1000, got {integer.DecValue}.");

    var signedDecimal = new CmdParam { Value = "-2,943,163.5" };
    if (signedDecimal.DecValue != -2943163.5d)
    {
        throw new InvalidOperationException(
            $"Expected -2,943,163.5 to coerce to -2943163.5, got {signedDecimal.DecValue}.");
    }
}

static void NestedScriptLoadsPreserveConfiguredScriptRoot()
{
    string originalDirectory = Directory.GetCurrentDirectory();
    string directory = Path.Combine(Path.GetTempPath(), "twx-script-root-tests", Guid.NewGuid().ToString("N"));
    string botDirectory = Path.Combine(directory, "mombot");
    string moduleDirectory = Path.Combine(botDirectory, "Modes", "Resource");
    Directory.CreateDirectory(moduleDirectory);
    File.WriteAllText(Path.Combine(botDirectory, "mombot.ts"), "pause\n");
    File.WriteAllText(Path.Combine(moduleDirectory, "colo.ts"), "halt\n");

    try
    {
        using var interpreter = new ModInterpreter
        {
            ProgramDir = directory,
            ScriptDirectory = directory,
        };

        interpreter.Load("scripts/mombot/mombot.ts", silent: true);

        if (!string.Equals(interpreter.ScriptDirectory, directory, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Expected configured script root '{directory}', got '{interpreter.ScriptDirectory}'.");
        }

        string expected = Path.Combine(moduleDirectory, "colo.ts");
        string actual = interpreter.ResolveScriptPath("scripts/mombot/Modes/Resource/colo.ts");
        if (!string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Expected nested module '{expected}', got '{actual}'.");
    }
    finally
    {
        Directory.SetCurrentDirectory(originalDirectory);
        try { Directory.Delete(directory, recursive: true); } catch { }
    }
}

static void DelayTriggeredHaltRemovesScriptAndEmitsStopEvent()
{
    string directory = Path.Combine(Path.GetTempPath(), "twx-delay-stop-tests", Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(directory);
    string resultPath = Path.Combine(directory, "stopped.txt");
    string listenerPath = Path.Combine(directory, "listener.ts");
    string delayedPath = Path.Combine(directory, "delayed.ts");
    File.WriteAllText(listenerPath, $$"""
setvar $result_file "{{resultPath}}"
seteventtrigger stopped :stopped "SCRIPT STOPPED"
pause

:stopped
settextlinetrigger ready :ready "READY"
pause

:ready
write $result_file "stopped"
halt
""");
    File.WriteAllText(delayedPath, """
setdelaytrigger done :done 25
pause

:done
halt
""");

    try
    {
        using var interpreter = new ModInterpreter
        {
            ProgramDir = directory,
            ScriptDirectory = directory,
        };

        interpreter.Load(listenerPath, silent: true);
        interpreter.Load(delayedPath, silent: true);

        DateTime deadline = DateTime.UtcNow.AddSeconds(2);
        while (interpreter.Count != 1 && DateTime.UtcNow < deadline)
            Thread.Sleep(10);

        if (interpreter.Count != 1)
            throw new InvalidOperationException($"Expected the delayed script to stop, found {interpreter.Count} scripts.");

        interpreter.DispatchCompleteLine("READY", "READY", forceTrigger: false);

        while (!File.Exists(resultPath) && DateTime.UtcNow < deadline)
            Thread.Sleep(10);

        if (!File.Exists(resultPath))
            throw new InvalidOperationException("Expected SCRIPT STOPPED listener to run after the delayed halt.");

        if (interpreter.Count != 0)
            throw new InvalidOperationException($"Expected both scripts to be removed, found {interpreter.Count} running.");
    }
    finally
    {
        try { Directory.Delete(directory, recursive: true); } catch { }
    }
}

static void PromptProbeRearmsLineTriggerAfterPartialPromptHandler()
{
    string directory = Path.Combine(Path.GetTempPath(), "twx-prompt-probe-tests", Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(directory);
    string resultPath = Path.Combine(directory, "marker.txt");
    string scriptPath = Path.Combine(directory, "marker.ts");
    File.WriteAllText(scriptPath, $$"""
setvar $result_file "{{resultPath}}"
settexttrigger prompt :prompt "Command prompt"
pause

:prompt
settextlinetrigger marker :marker #145&#8
pause

:marker
write $result_file "marker"
halt
""");

    try
    {
        using var interpreter = new ModInterpreter
        {
            ProgramDir = directory,
            ScriptDirectory = directory,
        };

        interpreter.Load(scriptPath, silent: true);
        interpreter.DispatchPartialLine("Command prompt", "Command prompt", forceTrigger: false);
        interpreter.DispatchCompleteLine(
            "Command prompt \u0091\b/",
            "Command prompt \u0091\b/",
            forceTrigger: false);

        if (!File.Exists(resultPath))
            throw new InvalidOperationException("Expected the marker line trigger to fire after the partial prompt handler.");

        if (interpreter.Count != 0)
            throw new InvalidOperationException($"Expected the marker script to stop, found {interpreter.Count} running.");
    }
    finally
    {
        try { Directory.Delete(directory, recursive: true); } catch { }
    }
}

static void PromptProbeFiresOnlyOncePerLine()
{
    string directory = Path.Combine(Path.GetTempPath(), "twx-prompt-probe-tests", Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(directory);
    string resultPath = Path.Combine(directory, "marker.txt");
    string scriptPath = Path.Combine(directory, "marker.ts");
    File.WriteAllText(scriptPath, $$"""
setvar $result_file "{{resultPath}}"
settexttrigger prompt :first #145&#8
pause

:first
settexttrigger prompt :second #145&#8
pause

:second
write $result_file currentline
halt
""");

    try
    {
        using var interpreter = new ModInterpreter
        {
            ProgramDir = directory,
            ScriptDirectory = directory,
        };

        interpreter.Load(scriptPath, silent: true);
        interpreter.DispatchPartialLine(
            "Planet command (?=help) [D] \u0091\b",
            "Planet command (?=help) [D] \u0091\b",
            forceTrigger: false);
        interpreter.DispatchCompleteLine(
            "Planet command (?=help) [D] \u0091\bC",
            "Planet command (?=help) [D] \u0091\bC",
            forceTrigger: false);

        if (File.Exists(resultPath))
            throw new InvalidOperationException("The rearmed probe matched the marker already consumed on the same line.");

        interpreter.DispatchCompleteLine(
            "Citadel command (?=help) \u0091\b",
            "Citadel command (?=help) \u0091\b",
            forceTrigger: false);

        if (!File.Exists(resultPath))
            throw new InvalidOperationException("Expected the rearmed probe to match the next prompt line.");

        if (interpreter.Count != 0)
            throw new InvalidOperationException($"Expected the marker script to stop, found {interpreter.Count} running.");
    }
    finally
    {
        try { Directory.Delete(directory, recursive: true); } catch { }
    }
}

static void DistinctPromptProbesFireOnOneLine()
{
    string directory = Path.Combine(Path.GetTempPath(), "twx-prompt-probe-tests", Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(directory);
    string resultPath = Path.Combine(directory, "marker.txt");
    string scriptPath = Path.Combine(directory, "marker.ts");
    File.WriteAllText(scriptPath, $$"""
setvar $result_file "{{resultPath}}"
settexttrigger prompt :first #145&#8
pause

:first
settexttrigger prompt :second #145&#8
pause

:second
write $result_file currentline
halt
""");

    try
    {
        using var interpreter = new ModInterpreter
        {
            ProgramDir = directory,
            ScriptDirectory = directory,
        };

        interpreter.Load(scriptPath, silent: true);
        interpreter.DispatchPartialLine(
            "Planet command (?=help) [D] \u0091\b",
            "Planet command (?=help) [D] \u0091\b",
            forceTrigger: false);
        interpreter.DispatchPartialLine(
            "Planet command (?=help) [D] \u0091\b\u0091\b",
            "Planet command (?=help) [D] \u0091\b\u0091\b",
            forceTrigger: false);

        if (!File.Exists(resultPath))
            throw new InvalidOperationException("Expected the second distinct probe to fire on the same partial line.");

        if (interpreter.Count != 0)
            throw new InvalidOperationException($"Expected the marker script to stop, found {interpreter.Count} running.");
    }
    finally
    {
        try { Directory.Delete(directory, recursive: true); } catch { }
    }
}

static void SectorParameterScansCountAsWatchdogActivity()
{
    Directory.SetCurrentDirectory(AppContext.BaseDirectory);
    using var fixture = DatabaseFixture.Create();
    string directory = Path.Combine(Path.GetTempPath(), "twx-sector-parameter-watchdog-tests", Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(directory);
    string resultPath = Path.Combine(directory, "complete.txt");
    string scriptPath = Path.Combine(directory, "sector-parameter-scan.ts");
    File.WriteAllText(scriptPath, $$"""
setvar $i 1
while ($i <= sectors)
    setvar $repeat 1
    while ($repeat <= 100)
        setvar $scratch $i
        add $repeat 1
    end
    setsectorparameter $i "FIGSEC" false
    add $i 1
end
write "{{resultPath}}" "complete"
halt
""");

    try
    {
        using var interpreter = new ModInterpreter
        {
            ProgramDir = directory,
            ScriptDirectory = directory,
        };
        ScriptRef.SetActiveDatabase(interpreter.RuntimeContext, fixture.Database);

        interpreter.Load(scriptPath, silent: true);

        if (!File.Exists(resultPath))
            throw new InvalidOperationException("Expected the long sector parameter scan to complete.");
        if (interpreter.Count != 0)
            throw new InvalidOperationException($"Expected the scan script to stop, found {interpreter.Count} running.");
    }
    finally
    {
        try { Directory.Delete(directory, recursive: true); } catch { }
    }
}

static void TopLevelReturnTerminatesWithoutScriptError()
{
    string directory = Path.Combine(Path.GetTempPath(), "twx-top-level-return-tests", Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(directory);
    string scriptPath = Path.Combine(directory, "top-level-return.ts");
    File.WriteAllText(scriptPath, "goto :done\n\n:done\nreturn\n");

    TextWriter originalOut = Console.Out;
    using var output = new StringWriter();
    try
    {
        Console.SetOut(output);
        using var interpreter = new ModInterpreter
        {
            ProgramDir = directory,
            ScriptDirectory = directory,
        };

        interpreter.Load(scriptPath, silent: true);

        if (interpreter.Count != 0)
            throw new InvalidOperationException($"Expected top-level return to stop the script, found {interpreter.Count} running.");
        if (output.ToString().Contains("Return without gosub", StringComparison.Ordinal))
            throw new InvalidOperationException("Top-level return emitted a script error.");
    }
    finally
    {
        Console.SetOut(originalOut);
        try { Directory.Delete(directory, recursive: true); } catch { }
    }
}

static void GameFileLockInspectionReportsStalePid()
{
    using var fixture = LockFixture.Create();
    GameFileLock.Info info = GameFileLock.TryInspect(fixture.LockFilePath)
        ?? throw new InvalidOperationException("Expected lock metadata to be readable.");

    if (info.Pid != int.MaxValue)
        throw new InvalidOperationException($"Expected stale PID {int.MaxValue}, got {info.Pid}.");

    if (info.IsProcessRunning)
        throw new InvalidOperationException("Expected fake PID to be reported as not running.");

    if (!string.Equals(Path.GetFullPath(fixture.ConfigPath), info.ConfigPath, StringComparison.OrdinalIgnoreCase))
        throw new InvalidOperationException("Expected configPath metadata to round-trip.");
}

static void GameFileLockStaleRemovalDeletesLock()
{
    using var fixture = LockFixture.Create();
    if (!GameFileLock.TryRemoveIfStale(fixture.LockFilePath))
        throw new InvalidOperationException("Expected stale lock deletion to return true.");

    if (File.Exists(fixture.LockFilePath))
        throw new InvalidOperationException("Expected stale lock file to be deleted.");
}

sealed class InterpolationProbe
{
    public int FormatCount { get; private set; }

    public override string ToString()
    {
        FormatCount++;
        return "probe";
    }
}

sealed class DatabaseFixture : IDisposable
{
    private readonly string _directory;

    private DatabaseFixture(string directory, ModDatabase database)
    {
        _directory = directory;
        Database = database;
    }

    public ModDatabase Database { get; }

    public static DatabaseFixture Create(int sectors = 5000)
    {
        string directory = Path.Combine(Path.GetTempPath(), "twx-core-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);

        var database = new ModDatabase();
        database.CreateDatabase(Path.Combine(directory, "game.xdb"), new DataHeader
        {
            Sectors = sectors,
            CommandChar = '$',
        });
        ScriptRef.SetActiveDatabase(database);

        return new DatabaseFixture(directory, database);
    }

    public void Dispose()
    {
        ScriptRef.SetActiveDatabase(null);
        try { Database.CloseDatabase(); } catch { }
        try { Directory.Delete(_directory, recursive: true); } catch { }
    }
}

sealed class LockFixture : IDisposable
{
    private readonly string _directory;

    private LockFixture(string directory, string configPath, string lockFilePath)
    {
        _directory = directory;
        ConfigPath = configPath;
        LockFilePath = lockFilePath;
    }

    public string ConfigPath { get; }
    public string LockFilePath { get; }

    public static LockFixture Create()
    {
        string directory = Path.Combine(Path.GetTempPath(), "twx-core-lock-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string configPath = Path.Combine(directory, "game.json");
        string databasePath = Path.Combine(directory, "game.xdb");
        string lockFilePath = GameFileLock.GetLockFilePath(configPath);

        File.WriteAllText(configPath, "{}");
        File.WriteAllText(lockFilePath, """
{
  "owner": "test",
  "pid": 2147483647,
  "processName": "missing-process",
  "configPath": "__CONFIG__",
  "databasePath": "__DATABASE__",
  "acquiredUtc": "2026-08-08T00:00:00.0000000+00:00"
}
""".Replace("__CONFIG__", EscapeJson(configPath), StringComparison.Ordinal)
   .Replace("__DATABASE__", EscapeJson(databasePath), StringComparison.Ordinal));

        return new LockFixture(directory, configPath, lockFilePath);
    }

    private static string EscapeJson(string value)
        => value.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal);

    public void Dispose()
    {
        try { Directory.Delete(_directory, recursive: true); } catch { }
    }
}
