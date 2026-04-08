using TWXP.Models;
using TWXProxy.Core;

namespace TWXP.Services;

public interface IProxyService
{
    event EventHandler<GameStatusChangedEventArgs>? StatusChanged;
    
    Task<bool> StartGameAsync(GameConfig config);
    Task StopGameAsync(string gameId);
    Task ResetGameAsync(string gameId);
    GameStatus GetGameStatus(string gameId);
    Task ConnectAutoStartGamesAsync(IEnumerable<GameConfig> configs);
    Task<IReadOnlyList<RunningScriptInfo>> GetRunningScriptsAsync(string gameId);
    Task LoadScriptAsync(string gameId, string scriptPath);
    Task SwitchBotAsync(string gameId, string botName);
    Task StopScriptAsync(string gameId, int scriptId);
    Task StopAllScriptsAsync(string gameId, bool includeSystemScripts);
    Task<HistorySnapshot> GetHistoryAsync(string gameId);
    Task ClearHistoryAsync(string gameId, HistoryType? type = null);
    Task ExportWarpsAsync(string gameId, string outputPath);
    Task<int> ImportWarpsAsync(string gameId, string inputPath);
    Task ExportBubblesAsync(string gameId, string outputPath);
    Task ExportDeadendsAsync(string gameId, string outputPath);
    Task ExportTwxAsync(string gameId, string outputPath);
    Task<TwxImportResult> ImportTwxAsync(string gameId, string inputPath, bool keepRecent);
    Task<bool> BeginLogPlaybackAsync(string gameId, string capturePath);
}

public class GameStatusChangedEventArgs : EventArgs
{
    public string GameId { get; set; } = string.Empty;
    public GameStatus Status { get; set; }
    public string? Message { get; set; }
}

public class ProxyService : IProxyService
{
    private readonly Dictionary<string, ProxyGameInstance> _runningGames = new();
    private readonly IGameConfigService _configService;

    public ProxyService(IGameConfigService configService)
    {
        _configService = configService;
    }

    public event EventHandler<GameStatusChangedEventArgs>? StatusChanged;

    public async Task<bool> StartGameAsync(GameConfig config)
    {
        if (_runningGames.ContainsKey(config.Id))
            return false;

        try
        {
            NotifyStatusChanged(config.Id, GameStatus.Starting);

            // Switch debug log to per-game file before anything else logs for this game
            AppPaths.EnsureDirectories();
            TWXProxy.Core.GlobalModules.DebugLogPath = AppPaths.DebugLogPathForGame(config.Name);
            TWXProxy.Core.GlobalModules.InitializeDebugLog();

            // Create script interpreter for this game
            var interpreter = new TWXProxy.Core.ModInterpreter();
            
            // Get script directory from config, default to "scripts" in app directory
            string scriptDirectory = string.IsNullOrWhiteSpace(config.ScriptDirectory)
                ? AppPaths.DefaultScriptDir
                : config.ScriptDirectory;
            Directory.CreateDirectory(scriptDirectory);
            
            // Set the program directory for the interpreter (used for relative paths).
            // In the Pascal version the install dir (e.g. C:\TWXProxy) contained the
            // "scripts" sub-folder, so ProgramDir = parent of scriptDirectory.
            string programDir = Path.GetDirectoryName(scriptDirectory)
                ?? (OperatingSystem.IsWindows()
                    ? TWXProxy.Core.WindowsInstallInfo.GetInstalledProgramDirOrDefault()
                    : AppContext.BaseDirectory);
            interpreter.ProgramDir = programDir;
            GlobalModules.ProgramDir = programDir;
            interpreter.ScriptDirectory = scriptDirectory;
            
            // Initialize the menu manager for this game
            GlobalModules.TWXMenu = new MenuManager();
            Console.WriteLine("[ProxyService] Initialized MenuManager");
            
            // Create the actual network game instance
            var gameInstance = new TWXProxy.Core.GameInstance(
                config.Name,
                config.Host,
                config.Port,
                config.ListenPort,
                config.CommandChar,
                interpreter,
                scriptDirectory
            );
            gameInstance.Logger.LogDirectory = AppPaths.LogsDir;
            gameInstance.Logger.SetLogIdentity(config.Name);
            gameInstance.AutoReconnect = config.AutoReconnect;
            gameInstance.ReconnectDelayMs = Math.Max(1, config.ReconnectDelaySeconds) * 1000;
            gameInstance.LocalEcho = config.LocalEcho;
            gameInstance.AcceptExternal = config.AcceptExternal;
            gameInstance.AllowLerkers = config.AllowLerkers;
            gameInstance.ExternalAddress = config.ExternalAddress ?? string.Empty;
            gameInstance.BroadCastMsgs = config.BroadcastMessages;
            gameInstance.Logger.LogEnabled = config.LogEnabled;
            gameInstance.Logger.LogData = config.LogEnabled;
            gameInstance.Logger.LogANSI = config.LogAnsi;
            gameInstance.Logger.BinaryLogs = config.LogBinary;
            gameInstance.Logger.NotifyPlayCuts = config.NotifyPlayCuts;
            gameInstance.Logger.MaxPlayDelay = config.MaxPlayDelay;
            gameInstance.SetNativeHaggleEnabled(config.NativeHaggleEnabled);
            gameInstance.SetNativeHaggleMode(config.NativeHaggleMode);
            gameInstance.NativeHaggleChanged += enabled =>
            {
                if (config.NativeHaggleEnabled == enabled)
                    return;

                config.NativeHaggleEnabled = enabled;
                _ = _configService.SaveConfigAsync(config);
            };
            
            // Create and wire up a file-backed database so sector/port data is
            // persisted across sessions.  The autosave timer in ModDatabase writes
            // to disk every 60 s; CloseDatabase() does a final save on shutdown.
            var sessionDb = new TWXProxy.Core.ModDatabase();
            try
            {
                // Use the explicit DatabasePath from config only if it is an
                // absolute path — a relative path would be resolved against the
                // app's working directory (unpredictable on Mac Catalyst) and
                // silently land in the wrong place or get bundled into the .app.
                string sharedDbPath = AppPaths.DatabasePathForGame(config.Name);
                string legacyDbPath = AppPaths.LegacyDatabasePathForGame(config.Name);
                bool hasAbsoluteConfigPath = !string.IsNullOrWhiteSpace(config.DatabasePath)
                    && Path.IsPathRooted(config.DatabasePath);
                bool usesLegacyDefaultPath = hasAbsoluteConfigPath
                    && PathsEqual(config.DatabasePath, legacyDbPath);

                string dbPath = hasAbsoluteConfigPath && !usesLegacyDefaultPath
                    ? config.DatabasePath
                    : sharedDbPath;

                Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);
                if (!File.Exists(dbPath) && File.Exists(legacyDbPath) && !PathsEqual(dbPath, legacyDbPath))
                {
                    File.Copy(legacyDbPath, dbPath, overwrite: false);
                    TWXProxy.Core.GlobalModules.DebugLog(
                        $"[ProxyService] Migrated legacy database '{legacyDbPath}' -> '{dbPath}'\n");
                }

                TWXProxy.Core.GlobalModules.DebugLog($"[ProxyService] AppDataDir={AppPaths.AppDataDir}\n");
                TWXProxy.Core.GlobalModules.DebugLog($"[ProxyService] DatabaseDir={AppPaths.DatabaseDir}\n");
                TWXProxy.Core.GlobalModules.DebugLog($"[ProxyService] dbPath={dbPath}\n");
                TWXProxy.Core.GlobalModules.DebugLog($"[ProxyService] dbPath exists={File.Exists(dbPath)}\n");

                if (File.Exists(dbPath))
                {
                    sessionDb.OpenDatabase(dbPath);
                    sessionDb.UseCache = config.UseCache;
                    // Sync all runtime-owned header fields, including login automation settings.
                    var header = sessionDb.DBHeader;
                    var updates = BuildHeader(config);
                    bool headerDirty = header.Sectors != updates.Sectors ||
                                       header.Address != updates.Address ||
                                       header.ServerPort != updates.ServerPort ||
                                       header.ListenPort != updates.ListenPort ||
                                       header.CommandChar != updates.CommandChar ||
                                       header.Description != updates.Description ||
                                       header.UseLogin != updates.UseLogin ||
                                       header.UseRLogin != updates.UseRLogin ||
                                       header.LoginScript != updates.LoginScript ||
                                       header.LoginName != updates.LoginName ||
                                       header.Password != updates.Password ||
                                       header.Game != updates.Game;
                    header.Sectors = updates.Sectors;
                    header.Address = updates.Address;
                    header.ServerPort = updates.ServerPort;
                    header.ListenPort = updates.ListenPort;
                    header.CommandChar = updates.CommandChar;
                    header.Description = updates.Description;
                    header.UseLogin = updates.UseLogin;
                    header.UseRLogin = updates.UseRLogin;
                    header.LoginScript = updates.LoginScript;
                    header.LoginName = updates.LoginName;
                    header.Password = updates.Password;
                    header.Game = updates.Game;
                    sessionDb.ReplaceHeader(header);
                    if (headerDirty)
                        sessionDb.SaveDatabase();
                    TWXProxy.Core.GlobalModules.DebugLog($"[ProxyService] Opened existing database: {dbPath}\n");
                }
                else
                {
                    sessionDb.CreateDatabase(dbPath, BuildHeader(config));
                    sessionDb.UseCache = config.UseCache;
                    TWXProxy.Core.GlobalModules.DebugLog($"[ProxyService] Created new database: {dbPath} ({config.Sectors} sectors)\n");
                }

                gameInstance.Logger.SetLogIdentity(dbPath);
            }
            catch (Exception dbEx)
            {
                TWXProxy.Core.GlobalModules.DebugLog($"[ProxyService] DATABASE ERROR: {dbEx}\n");
            }
            TWXProxy.Core.ScriptRef.SetActiveDatabase(sessionDb);
            TWXProxy.Core.GlobalModules.DebugLog($"[ProxyService] Database ready for {config.Name}\n");
            TWXProxy.Core.GlobalModules.FlushDebugLog();

            // Load previously saved variables, but exclude session-startup flags.
            // $gfile_chk controls auto-connect; $doRelog controls the relog machine.
            // Both should always start as '0' so the user must press Z each session.
            var varsToLoad = new Dictionary<string, string>(config.Variables);
            varsToLoad.Remove("$gfile_chk");
            varsToLoad.Remove("$doRelog");
            TWXProxy.Core.ScriptRef.LoadVarsForGame(varsToLoad);

            // When savevar is called, persist the value into the game's data file,
            // but skip the session-startup flags so they never survive across launches.
            TWXProxy.Core.ScriptRef.OnVariableSaved = (varName, value) =>
            {
                if (string.Equals(varName, "$gfile_chk", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(varName, "$doRelog",   StringComparison.OrdinalIgnoreCase))
                    return;
                config.Variables[varName] = value;
                _ = _configService.SaveConfigAsync(config);
            };

            // Create proxy instance early so we can reference it in event handlers
            var proxyInstance = new ProxyGameInstance
            {
                Config = config,
                GameInstance = gameInstance,
                Interpreter = interpreter,
                Database = sessionDb,
                Status = GameStatus.Running,
                ServerLineBuffer = new System.Text.StringBuilder()
            };
            
            // Hook up server data handler to set CURRENTLINE/CURRENTANSILINE
            gameInstance.ServerDataReceived += (sender, e) =>
            {
                // Process server data for line detection
                string text = e.Text;
                
                // Raw-byte hex dump — helps diagnose ANSI/encoding issues
                var hexDump = string.Join(" ", e.Data.Select(b => b.ToString("X2")));
                TWXProxy.Core.GlobalModules.DebugLog($"[ProxyService] RAW {e.Data.Length}B: {hexDump}\n");
                
                // Add to line buffer
                proxyInstance.ServerLineBuffer.Append(text);
                
                // Extract and process all complete lines
                string buffered = proxyInstance.ServerLineBuffer.ToString();
                int searchPos = 0;
                int lastProcessedPos = 0;
                
                while (searchPos < buffered.Length)
                {
                    // TW2002 sends "text\r ESC[0m \n next-line-text..." — split on \r (CR),
                    // matching Pascal's ProcessLine behavior.  After a \r, the bytes up to the
                    // next \r form the next line's content; any \n (LF) bytes in that content
                    // are stripped.  This means the ESC[0m reset that TW2002 places between
                    // \r and \n becomes the START of the next line's CURRENTANSILINE, which is
                    // exactly what the Pascal bytecode's getwordpos needles expect.
                    int crPos = buffered.IndexOf('\r', searchPos);
                    
                    if (crPos == -1)
                    {
                        // No complete \r-terminated line — keep remainder in buffer (prompt/partial)
                        string remainder = buffered.Substring(lastProcessedPos);
                        proxyInstance.ServerLineBuffer.Clear();
                        proxyInstance.ServerLineBuffer.Append(remainder);
                        
                        // Set the partial line as CURRENTLINE and fire triggers on it
                        if (!string.IsNullOrEmpty(remainder))
                        {
                            string remainderForAnsi = TWXProxy.Core.AnsiCodes.PrepareScriptAnsiText(remainder);
                            string scriptRemainder = TWXProxy.Core.AnsiCodes.PrepareScriptText(remainder);
                            string strippedRemainder = TWXProxy.Core.AnsiCodes.NormalizeTerminalText(
                                TWXProxy.Core.AnsiCodes.StripANSI(remainderForAnsi).TrimEnd('\r'));
                            TWXProxy.Core.GlobalModules.GlobalAutoRecorder.ProcessPrompt(strippedRemainder);
                            if (TWXProxy.Core.GlobalModules.GlobalAutoRecorder.CurrentSector > 0)
                                TWXProxy.Core.ScriptRef.SetCurrentSector(TWXProxy.Core.GlobalModules.GlobalAutoRecorder.CurrentSector);
                            TWXProxy.Core.ScriptRef.SetCurrentAnsiLine(remainderForAnsi);
                            TWXProxy.Core.ScriptRef.SetCurrentLine(scriptRemainder);

                            // Fire triggers for partial lines (prompts) too
                            if (TWXProxy.Core.GlobalModules.TWXInterpreter is TWXProxy.Core.ModInterpreter interpreter)
                            {
                                TWXProxy.Core.GlobalModules.DebugLog($"[ProxyService] Processing partial line (prompt): '{strippedRemainder}'\n");
                                // Restore CURRENTLINE to the actual prompt before firing
                                TWXProxy.Core.ScriptRef.SetCurrentLine(scriptRemainder);
                                TWXProxy.Core.GlobalModules.DebugLog($"[ProxyService] Calling Text Event on prompt...\n");
                                // Pascal ProcessPrompt calls TextEvent(CurrentLine) only — no TextLineEvent for partial prompts.
                                // Pascal does NOT call ActivateTriggers after a prompt — only after a full \r-terminated
                                // line (inside ProcessLine). Re-enabling triggers here was causing TextLineTriggers
                                // registered during a prompt handler to fire on the next full line's TextLineEvent
                                // instead of waiting for the line after that.
                                interpreter.TextEvent(scriptRemainder, false);
                                gameInstance.ProcessNativeHaggleLine(strippedRemainder);
                            }
                            else
                            {
                                gameInstance.ProcessNativeHaggleLine(strippedRemainder);
                            }
                        }
                        break;
                    }
                    
                    // Extract the line content from lastProcessedPos up to (but not including) the \r.
                    // The segment may begin with \n and/or ESC[0m bytes carried over from the previous
                    // line's "text\r ESC[0m \n" terminator — strip \n (LF) bytes to concatenate them.
                    int lineStart = lastProcessedPos;
                    int lineLength = crPos - lastProcessedPos;
                    string rawLine = buffered.Substring(lineStart, lineLength);
                    string line = TWXProxy.Core.AnsiCodes.PrepareScriptAnsiText(rawLine);
                    string scriptLine = TWXProxy.Core.AnsiCodes.PrepareScriptText(rawLine);
                    
                    // Pascal fires ProcessLine (and thus TextLineEvent) on every \r, including blank lines.
                    // A blank \r\n line must reach TextLineEvent("") — e.g. PlayerInfo's :line handler
                    // exits when a blank line arrives after the status bar data.  Only skip AutoRecorder.
                    {
                        // Set CURRENTANSILINE (with ANSI codes; \n already stripped above)
                        TWXProxy.Core.ScriptRef.SetCurrentAnsiLine(line);
                        
                        // Set CURRENTLINE (stripped of ANSI codes)
                        string strippedLine = TWXProxy.Core.AnsiCodes.NormalizeTerminalText(
                            TWXProxy.Core.AnsiCodes.StripANSI(line).TrimEnd('\r'));
                        TWXProxy.Core.ScriptRef.SetCurrentLine(strippedLine);
                        
                        TWXProxy.Core.GlobalModules.DebugLog($"[ProxyService] Processing line: '{strippedLine}'\n");
                        gameInstance.FeedShipStatusLine(strippedLine);
                        
                        // Update sector database from game text before firing script triggers (non-blank only)
                        if (!string.IsNullOrEmpty(strippedLine))
                        {
                            TWXProxy.Core.GlobalModules.GlobalAutoRecorder.RecordLine(strippedLine);
                            if (TWXProxy.Core.GlobalModules.GlobalAutoRecorder.CurrentSector > 0)
                                TWXProxy.Core.ScriptRef.SetCurrentSector(TWXProxy.Core.GlobalModules.GlobalAutoRecorder.CurrentSector);
                        }

                        gameInstance.History.ProcessLine(strippedLine);

                        // Fire text triggers and text line triggers for scripts (all lines, including blank)
                        if (TWXProxy.Core.GlobalModules.TWXInterpreter is TWXProxy.Core.ModInterpreter interpreter)
                        {
                            TWXProxy.Core.ScriptRef.SetCurrentLine(scriptLine);

                            // Pascal dispatch order for complete lines: TextLineEvent first, then TextEvent.
                            // (Pascal ProcessLine calls TextLineEvent, then ProcessPrompt calls TextEvent with the same line.)
                            TWXProxy.Core.GlobalModules.DebugLog($"[ProxyService] Calling TextLineEvent...\n");
                            interpreter.TextLineEvent(scriptLine, false);

                            TWXProxy.Core.GlobalModules.DebugLog($"[ProxyService] Calling TextEvent...\n");
                            // TextEvent fires on complete lines too (matches Pascal ProcessPrompt calling TextEvent with CurrentLine)
                            interpreter.TextEvent(scriptLine, false);
                            
                            // Re-enable triggers for next line (they get disabled when they fire to prevent double-triggering)
                            TWXProxy.Core.GlobalModules.DebugLog($"[ProxyService] Re-activating triggers\n");
                            interpreter.ActivateTriggers();

                            gameInstance.ProcessNativeHaggleLine(strippedLine);
                        }
                        else
                        {
                            gameInstance.ProcessNativeHaggleLine(strippedLine);
                        }
                    }
                    
                    // Move past the \r (the \n that follows, if any, will be part of the next
                    // line's content and will be stripped by the .Replace("\n","") above)
                    searchPos = crPos + 1;
                    lastProcessedPos = searchPos;
                }
                
                // If we processed all lines, clear the buffer
                if (lastProcessedPos >= buffered.Length)
                {
                    proxyInstance.ServerLineBuffer.Clear();
                }
            };
            
            // Hook up event handlers
            gameInstance.Connected += (sender, e) =>
            {
                NotifyStatusChanged(config.Id, GameStatus.Running, "Connected to server");
            };
            
            gameInstance.Disconnected += (sender, e) =>
            {
                // Don't change status - still running and accepting connections
                System.Diagnostics.Debug.WriteLine($"[{config.Name}] Server disconnected: {e.Reason}");
                // Fire 'Connection Lost' program event so scripts can react (re-register their triggers, etc.)
                if (TWXProxy.Core.GlobalModules.TWXInterpreter is TWXProxy.Core.ModInterpreter interpD)
                {
                    TWXProxy.Core.GlobalModules.DebugLog($"[ProxyService] Firing ProgramEvent 'Connection Lost'\n");
                    interpD.ProgramEvent("Connection Lost", "", false);
                }
            };
            
            // Hook up clear input buffer handler
            gameInstance.ClearInputBufferRequested += (sender, e) =>
            {
                proxyInstance.InputBuffer = string.Empty;
                Console.WriteLine($"[ProxyService] InputBuffer cleared for GETINPUT");
            };
            
            // Hook up local data handler to process GETINPUT responses
            // The event fires byte-by-byte, so we accumulate into lines
            gameInstance.LocalDataReceived += (sender, e) =>
            {
                string text = e.Text;
                Console.WriteLine($"[ProxyService] LocalDataReceived: {e.Data.Length} bytes, byte={e.Data[0]}, text='{text}'");
                
                // Handle backspace (8) or DEL (127)
                if (e.Data.Length == 1 && (e.Data[0] == 8 || e.Data[0] == 127))
                {
                    if (proxyInstance.InputBuffer.Length > 0)
                    {
                        proxyInstance.InputBuffer = proxyInstance.InputBuffer.Substring(0, proxyInstance.InputBuffer.Length - 1);
                        Console.WriteLine($"[ProxyService] Backspace - buffer now: '{proxyInstance.InputBuffer}'");
                    }
                    return;
                }
                
                // Accumulate characters
                proxyInstance.InputBuffer += text;
                
                // Keypress mode: fire immediately on any single printable character
                // (empty-prompt getInput — e.g. menu choices — expects no Enter key).
                if (TWXProxy.Core.GlobalModules.TWXInterpreter is TWXProxy.Core.ModInterpreter interpKP
                    && interpKP.HasKeypressInputWaiting
                    && proxyInstance.InputBuffer.Length > 0)
                {
                    string key = proxyInstance.InputBuffer;
                    proxyInstance.InputBuffer = string.Empty;
                    Console.WriteLine($"[ProxyService] Keypress mode - firing LocalInputEvent immediately: '{key}'");
                    interpKP.LocalInputEvent(key);
                    return;
                }
                
                // In connected mode with no getConsoleInput waiting: discard the buffer.
                // Characters typed by the user are forwarded directly to the game server
                // by Network.cs and don't need to accumulate here.  Allowing them to pile
                // up causes a stale LIE dump (waiting=False) when '\r' eventually arrives.
                if (gameInstance.IsConnected
                    && TWXProxy.Core.GlobalModules.TWXInterpreter is TWXProxy.Core.ModInterpreter interpConn
                    && !interpConn.IsAnyScriptWaitingForInput())
                {
                    proxyInstance.InputBuffer = string.Empty;
                    return;
                }
                
                // Check if we have a complete line (ended with \r or \n)
                if (proxyInstance.InputBuffer.Contains('\r') || proxyInstance.InputBuffer.Contains('\n'))
                {
                    // Extract the line
                    string line = proxyInstance.InputBuffer.TrimEnd('\r', '\n');
                    proxyInstance.InputBuffer = string.Empty; // Clear buffer
                    
                    if (!string.IsNullOrEmpty(line))
                    {
                        Console.WriteLine($"[ProxyService] Complete line received: '{line}', passing to interpreter");
                        interpreter.LocalInputEvent(line);
                    }
                }
            };
            
            // Start the game instance (starts listening, waits for $c to connect to server)
            await gameInstance.StartAsync();
            
            // Set this as the active game instance for script commands
            TWXProxy.Core.ScriptRef.SetActiveGameInstance(gameInstance);
            Console.WriteLine($"[ProxyService] Set active game instance for {config.Name}");

            proxyInstance.ModuleHost = await ExpansionModuleHost.CreateAsync(new ExpansionModuleHostOptions
            {
                HostTargets = ExpansionHostTargets.Twxp,
                HostName = "TWXP",
                GameName = config.Name,
                ProgramDir = programDir,
                ScriptDirectory = scriptDirectory,
                ModuleDataRootDirectory = AppPaths.ModuleDataDir,
                ModuleDirectories = new[]
                {
                    AppPaths.ModulesDir,
                    AppPaths.SharedModulesDir,
                    Path.Combine(programDir, "modules"),
                },
                GameInstance = gameInstance,
                Interpreter = interpreter,
                Database = sessionDb,
            });

            // Add to running games collection
            _runningGames[config.Id] = proxyInstance;
            
            NotifyStatusChanged(config.Id, GameStatus.Running);
            return true;
        }
        catch (Exception ex)
        {
            NotifyStatusChanged(config.Id, GameStatus.Error, ex.Message);
            return false;
        }
    }

    public async Task StopGameAsync(string gameId)
    {
        if (_runningGames.TryGetValue(gameId, out var instance))
        {
            NotifyStatusChanged(gameId, GameStatus.Stopped);
            
            // Clear the active game instance for script commands
            TWXProxy.Core.ScriptRef.SetActiveGameInstance(null);

            // Close the database — this stops the autosave timer and does a
            // final synchronous write to disk before we clear the reference.
            instance.Database.CloseDatabase();
            TWXProxy.Core.ScriptRef.SetActiveDatabase(null);
            Console.WriteLine($"[ProxyService] Saved and closed database for {instance.Config.Name}");
            
            // Clear the menu manager
            GlobalModules.TWXMenu = null;
            
            // Stop the game instance (stops listening, closes connections)
            await instance.GameInstance.StopAsync();
            if (instance.ModuleHost != null)
                await instance.ModuleHost.DisposeAsync();
            instance.GameInstance.Dispose();
            
            _runningGames.Remove(gameId);
        }
    }

    public async Task ResetGameAsync(string gameId)
    {
        if (_runningGames.TryGetValue(gameId, out var instance))
        {
            await StopGameAsync(gameId);
            await Task.Delay(100);
            await StartGameAsync(instance.Config);
        }
    }

    public GameStatus GetGameStatus(string gameId)
    {
        return _runningGames.TryGetValue(gameId, out var instance)
            ? instance.Status
            : GameStatus.Stopped;
    }

    public async Task ConnectAutoStartGamesAsync(IEnumerable<GameConfig> configs)
    {
        var autoStartConfigs = configs.Where(c => c.AutoConnect);
        foreach (var config in autoStartConfigs)
        {
            await StartGameAsync(config);
        }
    }

    public Task<IReadOnlyList<RunningScriptInfo>> GetRunningScriptsAsync(string gameId)
    {
        if (_runningGames.TryGetValue(gameId, out var instance))
            return Task.FromResult(ProxyGameOperations.GetRunningScripts(instance.Interpreter));

        return Task.FromResult<IReadOnlyList<RunningScriptInfo>>(Array.Empty<RunningScriptInfo>());
    }

    public Task LoadScriptAsync(string gameId, string scriptPath)
    {
        if (!_runningGames.TryGetValue(gameId, out var instance))
            throw new InvalidOperationException("Game is not running.");

        ProxyGameOperations.LoadScript(instance.Interpreter, scriptPath);
        return Task.CompletedTask;
    }

    public Task SwitchBotAsync(string gameId, string botName)
    {
        if (!_runningGames.TryGetValue(gameId, out var instance))
            throw new InvalidOperationException("Game is not running.");

        if (string.IsNullOrWhiteSpace(botName))
            throw new InvalidOperationException("Bot name is required.");

        instance.Interpreter.SwitchBot(string.Empty, botName, stopBotScripts: true);
        return Task.CompletedTask;
    }

    public Task StopScriptAsync(string gameId, int scriptId)
    {
        if (!_runningGames.TryGetValue(gameId, out var instance))
            throw new InvalidOperationException("Game is not running.");

        if (!ProxyGameOperations.StopScriptById(instance.Interpreter, scriptId))
            throw new InvalidOperationException($"Script {scriptId} was not found.");

        return Task.CompletedTask;
    }

    public Task StopAllScriptsAsync(string gameId, bool includeSystemScripts)
    {
        if (!_runningGames.TryGetValue(gameId, out var instance))
            throw new InvalidOperationException("Game is not running.");

        ProxyGameOperations.StopAllScripts(instance.Interpreter, includeSystemScripts);
        return Task.CompletedTask;
    }

    public Task<HistorySnapshot> GetHistoryAsync(string gameId)
    {
        if (_runningGames.TryGetValue(gameId, out var instance))
            return Task.FromResult(instance.GameInstance.History.GetSnapshot());

        return Task.FromResult(new HistorySnapshot(Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>()));
    }

    public Task ClearHistoryAsync(string gameId, HistoryType? type = null)
    {
        if (!_runningGames.TryGetValue(gameId, out var instance))
            throw new InvalidOperationException("Game is not running.");

        instance.GameInstance.History.Clear(type);
        return Task.CompletedTask;
    }

    public Task ExportWarpsAsync(string gameId, string outputPath)
    {
        return WithDatabaseAsync(gameId, database =>
        {
            ProxyGameOperations.ExportWarps(database, outputPath);
            return Task.CompletedTask;
        });
    }

    public async Task<int> ImportWarpsAsync(string gameId, string inputPath)
    {
        int imported = 0;
        await WithDatabaseAsync(gameId, database =>
        {
            imported = ProxyGameOperations.ImportWarps(database, inputPath);
            return Task.CompletedTask;
        });
        return imported;
    }

    public async Task ExportBubblesAsync(string gameId, string outputPath)
    {
        int bubbleSize = await GetBubbleSizeAsync(gameId);
        await WithDatabaseAsync(gameId, database =>
        {
            ProxyGameOperations.ExportBubbles(database, outputPath, bubbleSize);
            return Task.CompletedTask;
        });
    }

    public Task ExportDeadendsAsync(string gameId, string outputPath)
    {
        return WithDatabaseAsync(gameId, database =>
        {
            ProxyGameOperations.ExportDeadends(database, outputPath);
            return Task.CompletedTask;
        });
    }

    public Task ExportTwxAsync(string gameId, string outputPath)
    {
        return WithDatabaseAsync(gameId, database =>
        {
            ProxyGameOperations.ExportTwx(database, outputPath);
            return Task.CompletedTask;
        });
    }

    public Task<TwxImportResult> ImportTwxAsync(string gameId, string inputPath, bool keepRecent)
    {
        return WithDatabaseAsync(gameId, database =>
        {
            return Task.FromResult(ProxyGameOperations.ImportTwx(database, inputPath, keepRecent));
        });
    }

    public Task<bool> BeginLogPlaybackAsync(string gameId, string capturePath)
    {
        if (!_runningGames.TryGetValue(gameId, out var instance))
            throw new InvalidOperationException("Game is not running.");

        return Task.FromResult(instance.GameInstance.Logger.BeginPlayLog(capturePath));
    }

    private void NotifyStatusChanged(string gameId, GameStatus status, string? message = null)
    {
        if (_runningGames.TryGetValue(gameId, out var instance))
        {
            instance.Status = status;
        }

        StatusChanged?.Invoke(this, new GameStatusChangedEventArgs
        {
            GameId = gameId,
            Status = status,
            Message = message
        });
    }

    private static TWXProxy.Core.DataHeader BuildHeader(GameConfig config) => new()
    {
        ProgramName  = "TWX PROXY",
        Sectors      = config.Sectors,
        Address      = config.Host,
        ServerPort   = (ushort)config.Port,
        ListenPort   = (ushort)config.ListenPort,
        CommandChar  = config.CommandChar,
        Description  = config.Name,
        UseLogin     = config.UseLogin,
        UseRLogin    = config.UseRLogin,
        LoginScript  = string.IsNullOrWhiteSpace(config.LoginScript) ? "0_Login.cts" : config.LoginScript,
        LoginName    = config.LoginName ?? string.Empty,
        Password     = config.Password ?? string.Empty,
        Game         = string.IsNullOrWhiteSpace(config.GameLetter) ? '\0' : char.ToUpperInvariant(config.GameLetter[0]),
    };

    private static bool PathsEqual(string left, string right)
    {
        return string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), StringComparison.OrdinalIgnoreCase);
    }

    private async Task<GameConfig> GetRequiredConfigAsync(string gameId)
    {
        var config = await _configService.GetConfigAsync(gameId);
        if (config == null)
            throw new InvalidOperationException($"Game '{gameId}' was not found.");
        return config;
    }

    private async Task WithDatabaseAsync(string gameId, Func<TWXProxy.Core.ModDatabase, Task> action)
    {
        if (_runningGames.TryGetValue(gameId, out var running))
        {
            await action(running.Database);
            return;
        }

        GameConfig config = await GetRequiredConfigAsync(gameId);
        using var database = OpenDetachedDatabase(config);
        await action(database);
        database.SaveDatabase();
    }

    private ModDatabase OpenDetachedDatabase(GameConfig config)
    {
        string sharedDbPath = AppPaths.DatabasePathForGame(config.Name);
        string legacyDbPath = AppPaths.LegacyDatabasePathForGame(config.Name);
        bool hasAbsoluteConfigPath = !string.IsNullOrWhiteSpace(config.DatabasePath)
            && Path.IsPathRooted(config.DatabasePath);
        bool usesLegacyDefaultPath = hasAbsoluteConfigPath
            && PathsEqual(config.DatabasePath, legacyDbPath);

        string dbPath = hasAbsoluteConfigPath && !usesLegacyDefaultPath
            ? config.DatabasePath
            : sharedDbPath;

        Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);

        var database = new ModDatabase();
        if (File.Exists(dbPath))
        {
            database.OpenDatabase(dbPath);
            database.UseCache = config.UseCache;
            var header = database.DBHeader;
            var updates = BuildHeader(config);
            bool headerDirty = header.Sectors != updates.Sectors ||
                               header.Address != updates.Address ||
                               header.ServerPort != updates.ServerPort ||
                               header.ListenPort != updates.ListenPort ||
                               header.CommandChar != updates.CommandChar ||
                               header.Description != updates.Description ||
                               header.UseLogin != updates.UseLogin ||
                               header.UseRLogin != updates.UseRLogin ||
                               header.LoginScript != updates.LoginScript ||
                               header.LoginName != updates.LoginName ||
                               header.Password != updates.Password ||
                               header.Game != updates.Game;
            header.Sectors = updates.Sectors;
            header.Address = updates.Address;
            header.ServerPort = updates.ServerPort;
            header.ListenPort = updates.ListenPort;
            header.CommandChar = updates.CommandChar;
            header.Description = updates.Description;
            header.UseLogin = updates.UseLogin;
            header.UseRLogin = updates.UseRLogin;
            header.LoginScript = updates.LoginScript;
            header.LoginName = updates.LoginName;
            header.Password = updates.Password;
            header.Game = updates.Game;
            database.ReplaceHeader(header);
            if (headerDirty)
                database.SaveDatabase();
        }
        else
        {
            database.CreateDatabase(dbPath, BuildHeader(config));
            database.UseCache = config.UseCache;
        }

        return database;
    }

    private async Task<int> GetBubbleSizeAsync(string gameId)
    {
        if (_runningGames.TryGetValue(gameId, out var running))
            return Math.Max(1, running.Config.BubbleSize);

        return Math.Max(1, (await GetRequiredConfigAsync(gameId)).BubbleSize);
    }

    private class ProxyGameInstance
    {
        public required GameConfig Config { get; init; }
        public required TWXProxy.Core.GameInstance GameInstance { get; init; }
        public required TWXProxy.Core.ModInterpreter Interpreter { get; init; }
        public required TWXProxy.Core.ModDatabase Database { get; init; }
        public ExpansionModuleHost? ModuleHost { get; set; }
        public GameStatus Status { get; set; }
        public string InputBuffer { get; set; } = string.Empty;
        public required System.Text.StringBuilder ServerLineBuffer { get; init; }
    }
}
