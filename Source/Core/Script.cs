/*
Copyright (C) 2005  Remco Mulder

This program is free software; you can redistribute it and/or modify
it under the terms of the GNU General Public License as published by
the Free Software Foundation; either version 2 of the License, or
(at your option) any later version.

This program is distributed in the hope that it will be useful,
but WITHOUT ANY WARRANTY; without even the implied warranty of
MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
GNU General Public License for more details.

You should have received a copy of the GNU General Public License
along with this program; if not, write to the Free Software
Foundation, Inc., 59 Temple Place, Suite 330, Boston, MA  02111-1307  USA

For source notes please refer to Notes.txt
For license terms please refer to GPL.txt.

These files should be stored in the root of the compression you 
received this source in.
*/

// This unit controls scripts

/*
***************** Notes:

Standard compiled command specification:
  ScriptID:Byte|LineNumber:Word|CmdID:Word|Params|0:Byte

  'Params' is a series of command parameters specified as:
  Type:Byte|Value

  'Type:Byte' can be one of the following:
    - PARAM_VAR : User variable prefix (see below)
    - PARAM_CONST : Compiler string constant prefix
    - PARAM_SYSCONST : Read only system value
    - PARAM_PROGVAR : Program variable
    - PARAM_CHAR : Character code

  'Value' can be one of the following:
    - A 16-bit listed system constant reference (from TModInterpreter.SysConsts[]) type PARAM_SYSCONST
    - A 32-bit listed variable/constant reference (from TScript.Params[]) type PARAM_CONCAT
    - A 32-bit global-listed program variable reference type PARAM_VAR/PARAM_CONST
    - An 8-bit character code type PARAM_CHAR

  If 'Type:Byte' is equal to PARAM_VAR or PARAM_SYSCONST, the full parameter is instead
  specified as:
  PARAM_VAR:Byte|Ref:Word/Integer|IndexCount:Byte

  The IndexCount is the number of values the variable or sysconst has been indexed by.  These
  values are specified using the above methods and can therefore be indexed in the
  same way.  Any variable not indexed must have an IndexCount of zero.

  I.e. compiled indexed variable:

  PARAM_VAR:Byte|VarRef:Integer|1:Byte|PARAM_CONST:Byte|ConstRef:Integer
*/

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Timers;
using Timer = System.Timers.Timer;

namespace TWXProxy.Core
{
    #region Trigger Types and Classes

    public enum TriggerType
    {
        Text,
        TextLine,
        TextOut,
        Delay,
        Event,
        TextAuto
    }

    public enum PauseReason
    {
        None,
        Command,      // From script 'pause' command - wait for trigger/event
        OpenMenu,     // From OPENMENU - menu handler can unpause
        Input,        // From GETINPUT - wait for user input
        Auth          // From authentication - wait for auth complete
    }

    public class DelayTimer : Timer
    {
        public Trigger? DelayTrigger { get; set; }
    }

    public class Trigger
    {
        public string Name { get; set; } = string.Empty;
        public string Value { get; set; } = string.Empty;
        public string LabelName { get; set; } = string.Empty;
        public string Response { get; set; } = string.Empty;
        public string Param { get; set; } = string.Empty;
        public int LifeCycle { get; set; }
        public DelayTimer? Timer { get; set; }
    }

    #endregion

    #region ModInterpreter - Script Manager

    /// <summary>
    /// TModInterpreter: Encapsulation for all script interpretation within the program.
    /// </summary>
    public class ModInterpreter : TWXModule, ITWXGlobals, IObserver
    {
        private List<Script> _scriptList;
        private object? _scriptMenu;
        private ScriptRef? _scriptRef;
        private List<string> _autoRun;
        private Timer _tmrTime;
        private int _timerEventCount;
        private string _lastScript = string.Empty;
        private string _activeBot = string.Empty;
        private string _activeBotScript = string.Empty;
        private string _activeBotNameVar = string.Empty;
        private string _activeCommsVar = string.Empty;
        private string _activeLoginScript = string.Empty;
        private string _activeBotTag = string.Empty;
#pragma warning disable CS0649 // Field is never assigned to
        private int _activeBotTagLength;
#pragma warning restore CS0649
        private string _programDir = string.Empty;
        private string _scriptDirectory = string.Empty;

        public ModInterpreter(IPersistenceController? persistenceController = null)
            : base(persistenceController)
        {
            _scriptList = new List<Script>();
            _autoRun = new List<string>();
            _tmrTime = new Timer(1000);
            _tmrTime.Elapsed += OnTimerElapsed;
            _tmrTime.Enabled = false;
            _scriptRef = new ScriptRef();
        }

        public override void Dispose()
        {
            StopAll(true);
            _scriptList.Clear();
            _tmrTime?.Dispose();
            base.Dispose();
        }

        public override void StateValuesLoaded()
        {
            // This is called when all modules have been fully initialised
            // Load up our auto run scripts
            foreach (var script in _autoRun)
            {
                Load(script, false);
            }
        }

        private void OnTimerElapsed(object? sender, ElapsedEventArgs e)
        {
            ProgramEvent("Time hit", DateTime.Now.ToShortTimeString(), true);
        }

        public void CountTimerEvent()
        {
            _timerEventCount++;
            _tmrTime.Enabled = true;
        }

        public void UnCountTimerEvent()
        {
            _timerEventCount--;
            System.Diagnostics.Debug.Assert(_timerEventCount >= 0, "Timer uncount without count");

            if (_timerEventCount <= 0)
                _tmrTime.Enabled = false;
        }

        /// <summary>
        /// Normalises path separators (Windows '\' → OS separator) then walks each
        /// directory component with a case-insensitive search so scripts written for
        /// Windows work unchanged on macOS/Linux.  Returns the best-matching path it
        /// can find; if nothing matches the normalised path is returned as-is so the
        /// caller gets a meaningful FileNotFoundException.
        /// </summary>
        private static string ResolveFilePath(string filename, string baseDir)
        {
            // Normalise all backslashes to the OS separator
            filename = filename.Replace('\\', Path.DirectorySeparatorChar);

            // If already absolute and exists, we're done
            if (Path.IsPathRooted(filename) && File.Exists(filename))
                return filename;

            // Make absolute relative to baseDir when not rooted
            string candidate = Path.IsPathRooted(filename)
                ? filename
                : Path.Combine(baseDir, filename);

            if (File.Exists(candidate))
                return candidate;

            // Case-insensitive walk: resolve each path segment independently
            string[] parts = candidate.Replace('/', Path.DirectorySeparatorChar)
                                       .Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);

            // Reconstruct from the root
            string current = Path.IsPathRooted(candidate)
                ? candidate.Substring(0, candidate.IndexOf(parts[0], StringComparison.Ordinal))
                : string.Empty;
            if (string.IsNullOrEmpty(current)) current = Path.GetPathRoot(candidate) ?? string.Empty;

            foreach (string part in parts)
            {
                if (!Directory.Exists(current))
                    return candidate; // can't traverse further, give up

                // Find a child (file or directory) matching this segment case-insensitively
                string? match = Directory.EnumerateFileSystemEntries(current)
                    .FirstOrDefault(e => string.Equals(Path.GetFileName(e), part, StringComparison.OrdinalIgnoreCase));

                current = match ?? Path.Combine(current, part);
            }

            return current;
        }

        public void Load(string filename, bool silent)
        {
            GlobalModules.DebugLog($"[ModInterpreter.Load] Starting load of '{filename}', silent={silent}\n");

            // Normalise separators and resolve case-insensitively so scripts written
            // on Windows (with backslashes and arbitrary casing) work on macOS/Linux.
            filename = ResolveFilePath(filename, _programDir);
            _scriptDirectory = ResolveScriptRoot(filename, _scriptDirectory);

            // MB - Stop script if it is already running
            int i = 0;
            while (i < Count)
            {
                // TODO: Replace with proper type once ScriptCmp is converted
                // if (_scriptList[i].Cmp?.ScriptFile == filename)
                //     Stop(i);
                // else
                    i++;
            }

            GlobalModules.DebugLog($"[ModInterpreter.Load] Program dir: {_programDir}\n");
            Directory.SetCurrentDirectory(_programDir);
            var script = new Script(this);
            script.Silent = silent;
            _scriptList.Add(script);

            _lastScript = filename;
            bool error = true;

            // Allow reconnect after manual disconnect when launching a script
            // TWXClient.UserDisconnect = false;

            string extension = Path.GetExtension(filename).ToUpperInvariant();
            GlobalModules.DebugLog($"[ModInterpreter.Load] Extension: '{extension}'\n");
            
            if (extension == ".CTS" || extension == ".TWX")
            {
                if (!silent)
                {
                    // TWXServer.ClientMessage("Loading script: " + AnsiCodes.ANSI_7 + filename);
                }

                try
                {
                    GlobalModules.DebugLog($"[ModInterpreter.Load] Calling script.GetFromFile for .CTS file\n");
                    script.GetFromFile(filename, false);
                    GlobalModules.DebugLog($"[ModInterpreter.Load] GetFromFile completed successfully\n");
                    error = false;
                }
                catch (Exception ex)
                {
                    GlobalModules.TWXServer?.ClientMessage($"\r\n[ERROR] Loading script failed: {ex.Message}\r\n");
                    Console.WriteLine($"[ERROR] Loading script failed: {ex.Message}\r\n{ex.StackTrace}");
                    Stop(Count - 1);
                    throw; // Rethrow so caller knows it failed
                }
            }
            else
            {
                if (!silent)
                {
                    // TWXServer.ClientMessage("Loading and compiling script: " + AnsiCodes.ANSI_7 + filename);
                }

                _lastScript = filename;

                try
                {
                    GlobalModules.DebugLog($"[ModInterpreter.Load] Calling script.GetFromFile for .TS file (compile=true)\n");
                    script.GetFromFile(filename, true);
                    GlobalModules.DebugLog($"[ModInterpreter.Load] GetFromFile completed successfully\n");
                    error = false;
                }
                catch (Exception ex)
                {
                    GlobalModules.TWXServer?.ClientMessage($"\r\n[ERROR] Compiling script failed: {ex.Message}\r\n");
                    Console.WriteLine($"[ERROR] Compiling script failed: {ex.Message}\r\n{ex.StackTrace}");
                    // TWXServer.Broadcast($"\r\n{AnsiCodes.ANSI_15}Script compilation error: {AnsiCodes.ANSI_7}{ex.Message}\r\n\r\n");
                    Stop(Count - 1);
                }
            }

            GlobalModules.DebugLog($"[ModInterpreter.Load] Error flag: {error}\n");
            
            if (!error)
            {
                ProgramEvent("SCRIPT LOADED", filename, true);
                // TWXServer.NotifyScriptLoad();

                // Add menu option for script
                // TWXGUI.AddScriptMenu(script);

                try
                {
                    var server = GlobalModules.TWXServer;
                    GlobalModules.DebugLog($"[DEBUG] About to execute script. TWXServer is {(server == null ? "NULL" : "set")}\n");
                    Console.WriteLine($"[ModInterpreter] Executing script: {filename}, TWXServer={server != null}");
                    
                    // Keep executing the script until it actually pauses or completes
                    // This handles cases where the script returns from Execute() but should continue
                    bool completed = false;
                    int iterations = 0;
                    const int maxIterations = 1000; // Safety limit to prevent infinite loops
                    
                    Console.WriteLine($"[ModInterpreter] Starting execution loop, script.Paused={script.Paused}");
                    GlobalModules.DebugLog($"[DEBUG] Starting execution loop, script.Paused={script.Paused}\n");
                    
                    while (!completed && iterations < maxIterations)
                    {
                        Console.WriteLine($"[ModInterpreter] Loop iteration {iterations + 1}, calling Execute()...");
                        completed = script.Execute();
                        iterations++;
                        
                        Console.WriteLine($"[ModInterpreter] Execute() returned: {completed}, script.Paused={script.Paused}");
                        GlobalModules.DebugLog($"[DEBUG] Iteration {iterations}: completed={completed}, paused={script.Paused}\n");
                        
                        // If script is paused (waiting for input, trigger, etc.), stop looping
                        if (script.Paused)
                        {
                            Console.WriteLine($"[ModInterpreter] Script paused after {iterations} iterations");
                            GlobalModules.DebugLog($"[DEBUG] Script PAUSED after {iterations} iterations\n");
                            break;
                        }
                        
                        // If Execute() returned true (completed), we're done
                        if (completed)
                        {
                            Console.WriteLine($"[ModInterpreter] Script completed after {iterations} iterations");
                            GlobalModules.DebugLog($"[DEBUG] Script COMPLETED after {iterations} iterations\n");
                            break;
                        }
                        
                        // Otherwise, the script has more work to do - continue looping
                        Console.WriteLine($"[ModInterpreter] Script iteration {iterations} - continuing...");
                        GlobalModules.DebugLog($"[DEBUG] Continuing to iteration {iterations + 1}...\n");
                    }
                    
                    if (iterations >= maxIterations)
                    {
                        Console.WriteLine($"[ModInterpreter] WARNING: Script hit max iterations ({maxIterations}) - may be in infinite loop");
                        server?.ClientMessage($"\r\n[WARNING] Script hit execution limit - check for infinite loops\r\n");
                    }
                    
                    GlobalModules.DebugLog($"[DEBUG] Script execution {(completed ? "completed" : "paused")}.\n");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[ModInterpreter] Script execution error: {ex.Message}\r\n{ex.StackTrace}");
                    GlobalModules.TWXServer?.ClientMessage($"\r\nScript execution error: {ex.Message}\r\n");
                }
            }
        }

        private static string ResolveScriptRoot(string filename, string currentScriptDirectory)
        {
            if (!string.IsNullOrWhiteSpace(currentScriptDirectory) &&
                Directory.Exists(currentScriptDirectory) &&
                Path.GetFileName(currentScriptDirectory).Equals("scripts", StringComparison.OrdinalIgnoreCase))
            {
                return Path.GetFullPath(currentScriptDirectory);
            }

            string fileDirectory = Path.GetDirectoryName(Path.GetFullPath(filename)) ?? Directory.GetCurrentDirectory();
            string? candidate = fileDirectory;

            while (!string.IsNullOrEmpty(candidate))
            {
                if (Directory.Exists(Path.Combine(candidate, "include")) ||
                    Path.GetFileName(candidate).Equals("scripts", StringComparison.OrdinalIgnoreCase))
                {
                    return candidate;
                }

                string? parent = Path.GetDirectoryName(candidate);
                if (string.IsNullOrEmpty(parent) || string.Equals(parent, candidate, StringComparison.Ordinal))
                    break;
                candidate = parent;
            }

            if (!string.IsNullOrWhiteSpace(currentScriptDirectory) && Directory.Exists(currentScriptDirectory))
                return Path.GetFullPath(currentScriptDirectory);

            return fileDirectory;
        }

        public void Stop(int index)
        {
            var script = _scriptList[index];
            // Match Pascal: ProgramEvent('SCRIPT STOPPED', Script.Cmp.ScriptFile, TRUE)
            string scriptName = script.Compiler?.ScriptFile ?? string.Empty;

            GlobalModules.DebugLog($"[Script.Stop] Stopping script[{index}]='{scriptName}', remaining after={_scriptList.Count - 1}\n");
            GlobalModules.FlushDebugLog();

            // Broadcast termination message
            if (!script.Silent)
            {
                // TWXServer.Broadcast($"\r\n{AnsiCodes.ANSI_15}Script terminated: {AnsiCodes.ANSI_7}{scriptName}\r\n\r\n");
            }

            // Remove stop menu option from interface
            // TWXGUI.RemoveScriptMenu(script);

            // Free the script
            script.Dispose();

            // Remove script from list
            _scriptList.RemoveAt(index);

            GlobalModules.DebugLog($"[Script.Stop] Script removed, _scriptList.Count={_scriptList.Count}, firing ProgramEvent SCRIPT STOPPED\n");
            GlobalModules.FlushDebugLog();

            // Trigger program event
            ProgramEvent("SCRIPT STOPPED", scriptName, true);

            // TWXServer.NotifyScriptStop();
        }

        public void StopByHandle(Script script)
        {
            int index = _scriptList.IndexOf(script);
            if (index >= 0)
                Stop(index);
        }

        public void StopAll(bool stopSysScripts)
        {
            // Terminate all scripts
            int i = 0;

            while (i < _scriptList.Count)
            {
                if (stopSysScripts || !_scriptList[i].System)
                    Stop(i);
                else
                    i++;
            }
        }

        public void SwitchBot(string scriptName, string botName, bool stopBotScripts)
        {
            // Switch to a different bot configuration - loads the bot script if not running
            var server = GlobalModules.TWXServer;
            if (server != null)
            {
                var botConfig = server.GetBotConfig(botName);
                if (botConfig != null)
                {
                    // Stop the currently active bot if requested
                    if (stopBotScripts)
                    {
                        var currentBot = FindBot(_activeBot);
                        if (currentBot != null)
                        {
                            StopBot(_activeBot);
                        }
                    }
                    
                    // Update active bot information
                    _activeBot = botName;
                    _activeBotScript = !string.IsNullOrEmpty(scriptName) ? scriptName : botConfig.ScriptFile;
                    
                    // Set the active bot on the server
                    server.ActiveBotName = botName;
                    
                    // Start the new bot if it's not already running
                    var existingBot = FindBot(botName);
                    if (existingBot == null)
                    {
                        StartBot(botName, _activeBotScript);
                    }
                    
                    Console.WriteLine($"[SwitchBot] Switched to bot '{botName}' with script '{_activeBotScript}'");
                }
                else
                {
                    Console.WriteLine($"[SwitchBot] Bot '{botName}' not found");
                }
            }
            else
            {
                Console.WriteLine("[SwitchBot] Server not available");
            }
        }

        public void StartBot(string botName, string scriptFile)
        {
            // Load and start a bot script as a system script
            if (string.IsNullOrEmpty(scriptFile))
            {
                Console.WriteLine($"[StartBot] Error: No script file specified for bot '{botName}'");
                return;
            }

            // Check if bot is already running
            var existingBot = FindBot(botName);
            if (existingBot != null)
            {
                Console.WriteLine($"[StartBot] Bot '{botName}' is already running");
                return;
            }

            // Load the script as a system/bot script
            Directory.SetCurrentDirectory(_programDir);
            var script = new Script(this);
            script.System = true;
            script.Silent = true; // Bots run silently by default
            script.IsBot = true;
            script.BotName = botName;
            _scriptList.Add(script);

            try
            {
                string extension = Path.GetExtension(scriptFile).ToUpperInvariant();
                bool compile = extension == ".TS";
                
                script.GetFromFile(scriptFile, compile);
                
                Console.WriteLine($"[StartBot] Started bot '{botName}' from script '{scriptFile}'");
                
                // Execute the bot script
                script.Execute();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[StartBot] Error loading bot '{botName}': {ex.Message}");
                _scriptList.Remove(script);
                script.Dispose();
            }
        }

        public void StopBot(string botName)
        {
            // Stop a running bot script — route through Stop(index) so that triggers/timers
            // are disposed and ProgramEvent("SCRIPT STOPPED") fires for any listening scripts.
            var bot = FindBot(botName);
            if (bot != null)
            {
                Console.WriteLine($"[StopBot] Stopping bot '{botName}'");
                int index = _scriptList.IndexOf(bot);
                if (index >= 0)
                    Stop(index);
                else
                    bot.Dispose(); // already removed from list somehow — clean up directly
            }
            else
            {
                Console.WriteLine($"[StopBot] Bot '{botName}' not found");
            }
        }

        public Script? FindBot(string botName)
        {
            // Find a running bot by name
            return _scriptList.FirstOrDefault(s => s.IsBot && s.BotName.Equals(botName, StringComparison.OrdinalIgnoreCase));
        }

        public Script? GetActiveBot()
        {
            // Get the currently active bot script
            if (!string.IsNullOrEmpty(_activeBot))
            {
                return FindBot(_activeBot);
            }
            return null;
        }

        public void StartAllBots()
        {
            // Auto-start all registered bots that are configured to auto-start
            var server = GlobalModules.TWXServer;
            if (server != null)
            {
                var botList = server.GetBotList();
                foreach (var botName in botList)
                {
                    var botConfig = server.GetBotConfig(botName);
                    if (botConfig?.AutoStart == true)
                    {
                        StartBot(botName, botConfig.ScriptFile);
                    }
                }
            }
        }

        public List<Script> GetRunningBots()
        {
            // Get all currently running bot scripts
            return _scriptList.Where(s => s.IsBot).ToList();
        }

        public bool IsAnyScriptWaitingForInput()
        {
            // Check if any script is waiting for user input (GETINPUT)
            return _scriptList.Any(s => s.WaitingForInput);
        }

        public void ProgramEvent(string eventName, string matchText, bool exclusive)
        {
            // Trigger all matching program events in active scripts
            eventName = eventName.ToUpperInvariant();
            int i = 0;

            while (i < _scriptList.Count)
            {
                if (!_scriptList[i].ProgramEvent(eventName, matchText, exclusive))
                    i++;
            }
        }

        public bool LocalInputEvent(string inputText)
        {
            // Handle input from local client - pass to all scripts waiting for input.
            // Returns true if any script was waiting for input and consumed the text.
            bool consumed = false;
            int i = 0;
            while (i < _scriptList.Count)
            {
                if (_scriptList[i].WaitingForInput)
                {
                    _scriptList[i].LocalInputEvent(inputText);
                    consumed = true;
                    break; // only one script gets the input
                }
                i++;
            }
            return consumed;
        }

        /// <summary>
        /// Returns true if any running script is in keypress-input-waiting mode
        /// (getInput with empty prompt — expects a single character without Enter).
        /// ProxyService uses this to fire LocalInputEvent immediately on each char.
        /// </summary>
        public bool HasKeypressInputWaiting =>
            _scriptList.Any(s => s.WaitingForInput && s.KeypressMode);

        public bool TextOutEvent(string text, Script? startScript)
        {
            GlobalModules.DebugLog($"[ModInterpreter.TextOutEvent] Text='{text}', scriptCount={_scriptList.Count}\n");
            // Trigger matching text out triggers in active scripts
            int i = 0;

            // Find starting script
            if (startScript != null)
            {
                while (i < _scriptList.Count)
                {
                    if (_scriptList[i] == startScript)
                    {
                        i++;
                        break;
                    }
                    i++;
                }
            }

            bool result = false;

            // Loop through scripts and trigger off any text out triggers
            while (i < _scriptList.Count)
            {
                bool handled = false;
                if (!_scriptList[i].TextOutEvent(text, ref handled))
                    i++;

                if (handled)
                {
                    result = true;
                    break;
                }
            }

            return result;
        }

        public void TextEvent(string text, bool forceTrigger)
        {
            // Trigger matching text triggers in active scripts
            // [ModInterpreter.TextEvent] per-line logging removed — too high-frequency.
            int i = 0;

            while (i < _scriptList.Count)
            {
                if (!_scriptList[i].TextEvent(text, forceTrigger))
                    i++;
            }
        }

        public void TextLineEvent(string text, bool forceTrigger)
        {
            // Trigger matching textline triggers in active scripts
            if (GlobalModules.VerboseDebugMode)
                GlobalModules.DebugLog($"[ModInterpreter.TextLineEvent] Text='{text}', scriptCount={_scriptList.Count}\n");
            int i = 0;

            while (i < _scriptList.Count)
            {
                if (!_scriptList[i].TextLineEvent(text, forceTrigger))
                    i++;
            }
        }

        public void AutoTextEvent(string text, bool forceTrigger)
        {
            // Trigger matching auto text triggers in active scripts
            int i = 0;

            while (i < _scriptList.Count)
            {
                if (!_scriptList[i].AutoTextEvent(text, forceTrigger))
                    i++;
            }
        }

        public bool EventActive(string eventName)
        {
            // Check if any scripts hold matching event triggers
            foreach (var script in _scriptList)
            {
                if (script.EventActive(eventName))
                    return true;
            }
            return false;
        }

        public void ActivateTriggers()
        {
            // All text related triggers are deactivated for the rest of the line after they activate.
            // This is to prevent double triggering. Turn them back on.
            // Pascal TWX does not resume script execution here; it only re-enables triggers.
            foreach (var script in _scriptList)
            {
                script.TriggersActive = true;
            }

            // Clean up scripts that have fully terminated: no more code to run, not paused,
            // and no pending triggers or waitFor.  This handles scripts that exited via
            // "halt" (CmdAction.Stop sets _codePos = code.Length → HasCode = false) as well
            // as scripts that ran to the natural end of their bytecode.
            for (int i = _scriptList.Count - 1; i >= 0; i--)
            {
                var s = _scriptList[i];
                if (!s.HasCode && !s.Paused && !s.HasActiveTriggers)
                {
                    GlobalModules.DebugLog($"[ActivateTriggers] Cleaning up terminated script '{s.ScriptName}' (HasCode=false, no triggers)\n");
                    Stop(i);
                }
            }
        }

        public void DumpVars(string searchName)
        {
            if (string.IsNullOrEmpty(searchName))
            {
                // TWXServer.ClientMessage("Dumping all script variables");
            }
            else
            {
                // TWXServer.ClientMessage($"Dumping all script variables containing '{searchName}'");
            }

            // Dump variables in all scripts
            foreach (var script in _scriptList)
            {
                script.DumpVars(searchName);
            }

            // TWXServer.ClientMessage("Variable Dump Complete.");
        }

        public void DumpTriggers()
        {
            // Dump triggers in all scripts
            foreach (var script in _scriptList)
            {
                script.DumpTriggers();
            }
        }

        // Properties
        public int Count => _scriptList.Count;
        public string LastScript => _lastScript;
        public string ActiveBot => _activeBot;
        public string ActiveBotDir => _activeBotScript.Replace(_programDir, string.Empty, StringComparison.OrdinalIgnoreCase);
        public string ActiveBotScript => _activeBotScript;
        public string ActiveBotName => GetActiveBotName();
        public string ActiveBotTag => _activeBotTag;
        public int ActiveBotTagLength => _activeBotTagLength;
        public bool ActiveLoginDisabled => _activeLoginScript.Equals("disabled", StringComparison.OrdinalIgnoreCase);
        public string ActiveLoginScript => ActiveLoginDisabled ? string.Empty : _activeLoginScript;
        public object? ScriptMenu { get => _scriptMenu; set => _scriptMenu = value; }
        public ScriptRef? ScriptRef => _scriptRef;
        public string ProgramDir { get => _programDir; set => _programDir = value; }
        public string ScriptDirectory { get => _scriptDirectory; set => _scriptDirectory = value; }
        public Script this[int index] => _scriptList[index];
        public List<string> AutoRun => _autoRun;
        public string AutoRunText
        {
            get => string.Join(Environment.NewLine, _autoRun);
            set => _autoRun = new List<string>(value.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries));
        }

        public Script? GetScript(int index)
        {
            if (index >= 0 && index < _scriptList.Count)
                return _scriptList[index];
            return null;
        }

        public string GetProgVar(string progVarName)
        {
            // Program variables are stored in a static dictionary in ScriptRef
            // They are global proxy settings accessible by name
            return ScriptRef.GetProgVar(progVarName);
        }

        public void SetProgVar(string progVarName, string value)
        {
            // Set program variable in the static dictionary
            ScriptRef.SetProgVar(progVarName, value);
        }

        private string GetActiveBotName()
        {
            // Get active bot name from the running bot script
            var bot = GetActiveBot();
            if (bot != null)
            {
                return bot.BotName;
            }
            return _activeBot;
        }

        // IObserver implementation
        public void Notify(NotificationType noteType)
        {
            // Handle notifications if needed
        }
    }

    #endregion

    #region Script - Individual Script Instance

    /// <summary>
    /// TScript: A physical script in memory. Controlled by TModInterpreter - do not construct
    /// from outside TModInterpreter class.
    /// </summary>
    public class Script : IObserver, IDisposable
    {
        private int _codePos;
        private List<IScriptWindow> _windowList;
        private List<object> _menuItemList;
        private Dictionary<TriggerType, List<Trigger>> _triggers;
        private bool _waitingForAuth;
        private bool _waitForActive;
        private bool _waitingForInput;
        private bool _keypressMode;         // true when waiting for a single keypress (empty prompt)
        private CmdParam? _inputVarParam;
#pragma warning disable CS0169 // Field is never used
        private bool _silent;
        private bool _system;
#pragma warning restore CS0169
        private bool _paused;
        private PauseReason _pausedReason = PauseReason.None;
        private int _lastCodePos = -1;
        private int _loopCounter = 0;
        private DateTime _lastLoopCheck = DateTime.MinValue;
        private const int MAX_LOOP_ITERATIONS = 50;  // Max iterations before warning
        private bool _enableVariableDebug = false;  // Toggle for [VAR] output
        private string _waitText = string.Empty;
        private string _outText = string.Empty;
        private ScriptCmp? _cmp;
        private ModInterpreter _owner;
#pragma warning disable CS0169 // Field is never used
        private int _decimalPrecision;
#pragma warning restore CS0169
#pragma warning disable CS0649 // Field is never assigned to
        private int _execScriptID;
#pragma warning restore CS0649
        private Stack<int> _subStack;
        private List<CmdParam> _cmdParams;
        private string _libCmdName = string.Empty;
        private List<string> _libCmdLoaded;
        public Script(ModInterpreter owner)
        {
            _owner = owner;
            _cmp = new ScriptCmp(owner.ScriptRef, owner.ScriptDirectory);
            _waitForActive = false;
            _waitingForInput = false;
            _keypressMode = false;
            _inputVarParam = null;
            _subStack = new Stack<int>();
            _cmdParams = new List<CmdParam>(20);
            _windowList = new List<IScriptWindow>();
            _menuItemList = new List<object>();
            _libCmdLoaded = new List<string>();

            _triggers = new Dictionary<TriggerType, List<Trigger>>();
            foreach (TriggerType triggerType in Enum.GetValues(typeof(TriggerType)))
            {
                _triggers[triggerType] = new List<Trigger>();
            }
        }

        public void Dispose()
        {
            string triggerSummary = string.Join(", ",
                Enum.GetValues(typeof(TriggerType)).Cast<TriggerType>()
                    .Select(triggerType => $"{triggerType}={_triggers[triggerType].Count}"));
            GlobalModules.DebugLog(
                $"[Script.Dispose] script='{ScriptName}' persistenceId='{PersistenceId}' " +
                $"paused={_paused} pauseReason={_pausedReason} waitForActive={_waitForActive} " +
                $"waitText='{_waitText}' waitingInput={_waitingForInput} subDepth={_subStack.Count} " +
                $"triggers=[{triggerSummary}]\n");

            // Free up menu items
            while (_menuItemList.Count > 0)
            {
                // if (_menuItemList[0] == TWXMenu.CurrentMenu)
                //     TWXMenu.CloseMenu(false);

                _menuItemList.RemoveAt(0);
            }

            // Free up script windows
            while (_windowList.Count > 0)
            {
                _windowList[0].Dispose();
                _windowList.RemoveAt(0);
            }

            // Free up trigger lists
            foreach (var triggerType in Enum.GetValues(typeof(TriggerType)).Cast<TriggerType>())
            {
                while (_triggers[triggerType].Count > 0)
                {
                    FreeTrigger(_triggers[triggerType][0]);
                    _triggers[triggerType].RemoveAt(0);
                }
            }

            // Free up sub stack
            _subStack.Clear();

            _cmdParams.Clear();

            // Clear this script's in-memory var cache so stale savevar values
            // don't carry over if the same script is reloaded in this session.
            ScriptRef.ClearVarsForScript(PersistenceId);

            GlobalModules.DebugLog($"[Script.Dispose] script='{ScriptName}' dispose complete\n");
        }

        public void Notify(NotificationType noteType)
        {
            if (_waitingForAuth)
            {
                switch (noteType)
                {
                    case NotificationType.AuthenticationDone:
                        _waitingForAuth = false;
                        Execute();
                        break;
                    case NotificationType.AuthenticationFailed:
                        _waitingForAuth = false;
                        SelfTerminate();
                        break;
                }
            }
        }

        private void SelfTerminate()
        {
            // Send a notification message to self to start termination
            _owner.StopByHandle(this);
        }

        public void GetFromFile(string filename, bool compile)
        {
            if (_cmp == null)
                throw new Exception("Script compiler not initialized");

            if (compile)
            {
                _cmp.CompileFromFile(filename, string.Empty);
                CompileLib();
            }
            else
            {
                _cmp.LoadFromFile(filename);
                CompileLib();
            }

            _codePos = 0; // always start at beginning of script
            TriggersActive = true; // Enable triggers when script loads
        }

        private void FreeTrigger(Trigger trigger)
        {
            trigger.Timer?.Dispose();
            trigger.Timer = null;
        }

        private bool CheckTriggers(List<Trigger> triggerList, string text, bool textOutTrigger, bool forceTrigger, ref bool handled)
        {
            // Check through triggers for matches with Text
            bool result = false;
            handled = false;

            if (GlobalModules.VerboseDebugMode)
                GlobalModules.DebugLog($"[CheckTriggers] Text='{text}', triggerCount={triggerList.Count}, TriggersActive={TriggersActive}, Locked={Locked}, textOut={textOutTrigger}, force={forceTrigger}\n");

            if ((!textOutTrigger && !forceTrigger && !TriggersActive) || Locked)
            {
                if (GlobalModules.VerboseDebugMode)
                    GlobalModules.DebugLog($"[CheckTriggers] BLOCKED: TriggersActive={TriggersActive}, Locked={Locked}\n");
                return false; // triggers are not enabled or locked in stasis (waiting on menu?)
            }

            int i = 0;

            while (i < triggerList.Count)
            {
                if (GlobalModules.VerboseDebugMode)
                    GlobalModules.DebugLog($"[CheckTriggers] Checking trigger {i}: value='{triggerList[i].Value}', label='{triggerList[i].LabelName}'\n");
                if (text.Contains(triggerList[i].Value) || string.IsNullOrEmpty(triggerList[i].Value))
                {
                    GlobalModules.DebugLog($"[CheckTriggers] MATCH! Trigger {i} matched\n");
                    // Save trigger values
                    int lifeCycle = triggerList[i].LifeCycle;
                    string labelName = triggerList[i].LabelName;
                    string response = triggerList[i].Response;

                    // New lifecycle option
                    if (lifeCycle > 0)
                    {
                        triggerList[i].LifeCycle = lifeCycle - 1;
                        if (triggerList[i].LifeCycle == 0)
                        {
                            handled = true;
                            FreeTrigger(triggerList[i]);
                            triggerList.RemoveAt(i);
                        }
                    }

                    if (!string.IsNullOrEmpty(response))
                    {
                        // Handle autotrigger response - send text to game server
                        // Convert * to CRLF (Trade Wars convention)
                        string output = response.Replace("*", "\r\n");
                        
                        // Send to game server via ModDatabase (cast to concrete type for SendToServerAsync)
                        if (GlobalModules.TWXDatabase is ModDatabase database)
                        {
                            // Convert string to bytes (Latin1 encoding to preserve high-byte chars like chr(145))
                            byte[] data = Encoding.Latin1.GetBytes(output);
                            
                            // Blocking send to preserve send order
                            database.SendToServerAsync(data).GetAwaiter().GetResult();
                        }
                        
                        return result;
                    }
                    else
                    {
                        try
                        {
                            // Set CURRENTLINE to the text that triggered this handler
                            // This allows the handler to parse the triggering line
                            GlobalModules.DebugLog($"[CheckTriggers] Setting CURRENTLINE to '{text}'\n");
                            ScriptRef.SetCurrentLine(text);
                            GlobalModules.DebugLog($"[CheckTriggers] Calling GotoLabel('{labelName}')\n");
                            
                            GotoLabel(labelName);
                            GlobalModules.DebugLog($"[CheckTriggers] GotoLabel succeeded, result={result}\n");
                        }
                        catch (Exception ex)
                        {
                            // Script is not in execution - error handling for gotos outside execute loop
                            GlobalModules.DebugLog($"[CheckTriggers] GotoLabel FAILED: {ex.Message}\n");
                            // TWXServer.Broadcast($"{AnsiCodes.ANSI_15}Script run-time error (trigger activation): {AnsiCodes.ANSI_7}{ex.Message}\r\n");
                            SelfTerminate();
                            result = true;
                        }
                    }

                    if (!result)
                    {
                        GlobalModules.DebugLog($"[CheckTriggers] Calling Execute() to run handler\n");
                        TriggersActive = false;

                        // Save the current pause state before running the handler.
                        // We must clear _paused so that Execute() can actually run the handler
                        // code (it returns immediately when _paused is true).
                        bool wasPaused = _paused;
                        PauseReason savedPauseReason = _pausedReason;
                        _paused = false;
                        _pausedReason = PauseReason.None;

                        // For PERSISTENT triggers (LifeCycle == 0) that fire while the outer
                        // code is mid-waitOn, the handler must NOT permanently overwrite the
                        // instruction pointer or the waitFor state.  The handler's variable
                        // side-effects are still useful, but the outer execution position must
                        // be restored afterwards so that the outer waitOn resumes correctly.
                        // NOTE: SetTextLineTrigger uses LifeCycle = 1 (one-shot) to match the
                        // original Pascal TWX where all triggers are removed before firing.
                        // This path is only relevant for any future LifeCycle = 0 triggers.
                        bool outerWaitActive = _waitForActive;   // true when mid-waitOn
                        int  savedCodePos    = _codePos;
                        string savedWaitText = _waitText;

                        if (Execute())
                        {
                            GlobalModules.DebugLog($"[CheckTriggers] Execute() returned true (script terminated)\n");
                            result = true; // script was self-terminated
                        }
                        else
                        {
                            GlobalModules.DebugLog($"[CheckTriggers] Execute() returned false (handler completed)\n");

                            bool handlerPaused = _paused; // did the handler itself pause (e.g. its own waitOn)?

                            if (handlerPaused && lifeCycle == 0 && outerWaitActive)
                            {
                                // Persistent trigger (LifeCycle==0) fired while the outer script
                                // was mid-waitOn.  Restore the pre-trigger IP and waitFor state
                                // so the outer waitOn resumes from the correct bytecode spot.
                                GlobalModules.DebugLog($"[CheckTriggers] Persistent trigger fired mid-waitOn; restoring outer IP and waitFor state\n");
                                _codePos      = savedCodePos;
                                _waitForActive = outerWaitActive;  // true
                                _waitText      = savedWaitText;
                                _paused        = true;
                                _pausedReason  = savedPauseReason;
                            }
                            else if (handlerPaused)
                            {
                                // Handler registered its own waitOn/pause and paused.
                                // Leave the handler's pause as the effective state.
                                GlobalModules.DebugLog($"[CheckTriggers] Handler paused itself (new waitOn); leaving handler pause in place\n");
                            }
                            else if (wasPaused)
                            {
                                // Handler completed without pausing.
                                if (lifeCycle > 0)
                                {
                                    // One-shot trigger (waitOn): the outer pause is consumed by
                                    // this trigger firing.  Script continues from here.
                                    GlobalModules.DebugLog($"[CheckTriggers] One-shot trigger satisfied outer pause — script continues\n");
                                    // _paused already false — nothing to do
                                }
                                else
                                {
                                    // Persistent trigger (setTextLineTrigger): handler ran to
                                    // completion without setting its own waitOn.  Restore the
                                    // outer pause so the main script keeps waiting.
                                    GlobalModules.DebugLog($"[CheckTriggers] Persistent trigger handler done; restoring outer pause\n");
                                    _paused = true;
                                    _pausedReason = savedPauseReason;
                                }
                            }
                            // If !wasPaused: script was not paused before, nothing to restore.
                        }
                    }

                    if (result)
                    {
                        if (GlobalModules.VerboseDebugMode)
                            GlobalModules.DebugLog($"[CheckTriggers] Returning true from CheckTriggers\n");
                        return result;
                    }

                    break;
                }
                else
                {
                    i++;
                }
            }

            if (GlobalModules.VerboseDebugMode)
                GlobalModules.DebugLog($"[CheckTriggers] Finished checking all triggers, returning {result}\n");
            return result;
        }

        private bool TriggerExists(string name)
        {
            // Check through all trigger lists to see if this trigger name is in use
            foreach (var triggerType in Enum.GetValues(typeof(TriggerType)).Cast<TriggerType>())
            {
                var triggerList = _triggers[triggerType];
                if (triggerList.Any(t => t.Name == name))
                    return true;
            }
            return false;
        }

        public bool TextOutEvent(string text, ref bool handled)
        {
            _outText = text;
            // Check through textOut triggers for matches with text
            return CheckTriggers(_triggers[TriggerType.TextOut], text, true, false, ref handled);
        }

        public bool TextLineEvent(string text, bool forceTrigger)
        {
            // Pascal TextLineEvent only checks TextLine triggers — no waitFor/waitOn check here.
            // (waitFor/waitOn is handled exclusively in TextEvent.)
            // Check through lineTriggers for matches with Text
            bool handled = false;
            return CheckTriggers(_triggers[TriggerType.TextLine], text, false, forceTrigger, ref handled);
        }

        public bool AutoTextEvent(string text, bool forceTrigger)
        {
            // Check through autoTriggers for matches with Text
            bool handled = false;
            return CheckTriggers(_triggers[TriggerType.TextAuto], text, false, forceTrigger, ref handled);
        }

        public bool TextEvent(string text, bool forceTrigger)
        {
            // [Script.TextEvent] per-line logging removed — too high-frequency.
            // Check waitfor
            if (_waitForActive)
            {
                if (text.Contains(_waitText))
                {
                    TriggersActive = false;
                    _waitForActive = false;
                    // Unpause if paused (WAITFOR/WAITON paused the script)
                    if (_paused)
                    {
                        _paused = false;
                        _pausedReason = PauseReason.None;
                    }
                    return Execute();
                }
            }

            // Check through textTriggers for matches with Text
            bool handled = false;
            return CheckTriggers(_triggers[TriggerType.Text], text, false, forceTrigger, ref handled);
        }

        public bool ProgramEvent(string eventName, string matchText, bool exclusive)
        {
            // SetEventTrigger stores Value as ToUpperInvariant(), so match case-insensitively
            string eventNameUpper = eventName.ToUpperInvariant();

            // Check through EventTriggers for matches with Text
            if (eventNameUpper == "SCRIPT STOPPED")
            {
                int eventCount = _triggers[TriggerType.Event].Count(t => t.Value == "SCRIPT STOPPED");
                GlobalModules.DebugLog($"[Script.ProgramEvent] Script='{ScriptName}' received SCRIPT STOPPED, eventTriggerCount={eventCount}\n");
                GlobalModules.FlushDebugLog();
            }

            GlobalModules.DebugLog($"[Script.ProgramEvent] Script='{ScriptName}' event='{eventNameUpper}' triggerCount={_triggers[TriggerType.Event].Count}\n");
            GlobalModules.FlushDebugLog();

            bool result = false;
            int i = 0;

            while (i < _triggers[TriggerType.Event].Count)
            {
                var trigger = _triggers[TriggerType.Event][i];

                bool matchFound = trigger.Value == eventNameUpper &&
                    ((!exclusive && matchText.Contains(trigger.Param)) ||
                     (exclusive && trigger.Param == matchText) ||
                     string.IsNullOrEmpty(matchText) ||
                     string.IsNullOrEmpty(trigger.Param));

                if (matchFound)
                {
                    GlobalModules.DebugLog($"[Script.ProgramEvent] MATCHED trigger='{trigger.Name}' label='{trigger.LabelName}'\n");
                    GlobalModules.FlushDebugLog();

                    // Remove this trigger and enact it
                    string labelName = trigger.LabelName;
                    FreeTrigger(_triggers[TriggerType.Event][i]);
                    _triggers[TriggerType.Event].RemoveAt(i);

                    try
                    {
                        GotoLabel(labelName);
                    }
                    catch (Exception)
                    {
                        // Script is not in execution - error handling for gotos outside execute loop
                        SelfTerminate();
                        result = true;
                    }

                    if (!result)
                    {
                        TriggersActive = false;

                        // Clear _paused so Execute() can actually run the handler
                        // (Execute returns immediately when _paused is true).
                        bool wasPaused = _paused;
                        PauseReason savedPauseReason = _pausedReason;
                        _paused = false;
                        _pausedReason = PauseReason.None;

                        if (Execute())
                        {
                            result = true; // script was self-terminated
                        }
                        else if (wasPaused && !_paused)
                        {
                            // Handler ran to completion without re-pausing; restore original pause
                            // so the outer PAUSE loop continues where it left off.
                            _paused = true;
                            _pausedReason = savedPauseReason;
                        }
                    }

                    break;
                }
                else
                {
                    i++;
                }
            }

            return result;
        }

        public bool EventActive(string eventName)
        {
            // Check for events matching this event name
            return _triggers[TriggerType.Event].Any(t => t.Value == eventName);
        }

        private void DelayTimerEvent(object? sender, ElapsedEventArgs e)
        {
            if (sender is not DelayTimer timer || timer.DelayTrigger == null)
                return;

            string labelName = timer.DelayTrigger.LabelName;
            bool term = false;

            // Remove the trigger and its timer
            _triggers[TriggerType.Delay].Remove(timer.DelayTrigger);
            FreeTrigger(timer.DelayTrigger);

            try
            {
                GotoLabel(labelName);
            }
            catch (Exception)
            {
                // Script is not in execution - error handling for gotos outside execute loop
                // TWXServer.Broadcast($"{AnsiCodes.ANSI_15}Script run-time error (delay trigger activation): {AnsiCodes.ANSI_7}{ex.Message}\r\n");
                SelfTerminate();
                term = true;
            }

            if (!term)
            {
                // Unpause the script so delay trigger can resume execution
                _paused = false;
                Execute();
            }
        }

        private Trigger CreateTrigger(string name, string labelName, string value)
        {
            if (_cmp != null)
            {
                _cmp.ExtendName(ref name, _execScriptID);
                _cmp.ExtendLabelName(ref labelName, _execScriptID);
            }

            if (TriggerExists(name))
                throw new Exception($"Trigger already exists: '{name}'");

            return new Trigger
            {
                Name = name,
                LabelName = labelName,
                Response = string.Empty,
                Value = value,
                Timer = null,
                LifeCycle = 1 // default lifecycle is single response / no repeat
            };
        }

        public void SetAutoTrigger(string name, string value, string response, int lifeCycle)
        {
            if (TriggerExists(name))
                throw new Exception($"Trigger already exists: '{name}'");

            var trigger = new Trigger
            {
                Name = name,
                LabelName = string.Empty,
                Response = response,
                Value = value,
                Timer = null,
                LifeCycle = lifeCycle
            };

            _triggers[TriggerType.TextAuto].Add(trigger);
        }

        public void SetTextLineTrigger(string name, string labelName, string value)
        {
            KillTrigger(name); // upsert: replace any existing trigger with this name
            var trigger = CreateTrigger(name, labelName, value);
            // LifeCycle = 1 (one-shot, default from CreateTrigger): matches Pascal behavior where
            // all triggers are unconditionally removed before their handler runs.  "Persistence"
            // in scripts like InfoQuick's :line loop is achieved by the handler re-registering
            // the trigger itself, exactly as in the original Pascal TWX.
            _triggers[TriggerType.TextLine].Add(trigger);
        }

        public void SetTextOutTrigger(string name, string labelName, string value)
        {
            KillTrigger(name); // upsert: replace any existing trigger with this name
            var trigger = CreateTrigger(name, labelName, value);
            // LifeCycle = 1 (one-shot): matches Pascal behavior.
            _triggers[TriggerType.TextOut].Add(trigger);
        }

        public void SetTextTrigger(string name, string labelName, string value)
        {
            // Upsert: remove any existing trigger with this name first.
            // In the original Pascal TWX, re-registering a waitOn is valid and simply
            // replaces the old entry (e.g. when a persistent handler re-issues the same waitOn).
            KillTrigger(name);
            _triggers[TriggerType.Text].Add(CreateTrigger(name, labelName, value));
        }

        public void SetEventTrigger(string name, string labelName, string value, string param)
        {
            if (_cmp != null)
            {
                _cmp.ExtendName(ref name, _execScriptID);
                _cmp.ExtendLabelName(ref labelName, _execScriptID);
            }

            if (TriggerExists(name))
                throw new Exception($"Trigger already exists: '{name}'");

            GlobalModules.DebugLog($"[SETEVENTTRIGGER] name='{name}' label='{labelName}' event='{value}' param='{param}'\n");

            var trigger = new Trigger
            {
                Name = name,
                LabelName = labelName,
                Response = string.Empty,
                Value = value.ToUpperInvariant(),
                Param = param,
                Timer = null,
                LifeCycle = 1
            };

            if (trigger.Value == "TIME HIT")
                _owner.CountTimerEvent();

            _triggers[TriggerType.Event].Add(trigger);
        }

        public void SetDelayTrigger(string name, string labelName, int value)
        {
            if (_cmp != null)
            {
                _cmp.ExtendName(ref name, _execScriptID);
                _cmp.ExtendLabelName(ref labelName, _execScriptID);
            }

            if (TriggerExists(name))
                throw new Exception($"Trigger already exists: '{name}'");

            var trigger = new Trigger
            {
                Name = name,
                LabelName = labelName,
                Response = string.Empty,
                Value = string.Empty,
                Param = string.Empty,
                LifeCycle = 1
            };

            var timer = new DelayTimer
            {
                Interval = value,
                DelayTrigger = trigger,
                AutoReset = false
            };

            timer.Elapsed += DelayTimerEvent;
            trigger.Timer = timer;
            timer.Start();

            _triggers[TriggerType.Delay].Add(trigger);
        }

        public void KillTrigger(string name)
        {
            if (_cmp != null)
                _cmp.ExtendName(ref name, _execScriptID);

            // Remove trigger by name from all trigger lists
            foreach (var triggerType in Enum.GetValues(typeof(TriggerType)).Cast<TriggerType>())
            {
                var triggerList = _triggers[triggerType];
                for (int i = 0; i < triggerList.Count; i++)
                {
                    if (triggerList[i].Name == name)
                    {
                        if (triggerList[i].Value == "TIME HIT")
                            _owner.UnCountTimerEvent();

                        FreeTrigger(triggerList[i]);
                        triggerList.RemoveAt(i);
                        return;
                    }
                }
            }
        }

        public void KillAllTriggers()
        {
            // Remove all triggers
            foreach (var triggerType in Enum.GetValues(typeof(TriggerType)).Cast<TriggerType>())
            {
                var triggerList = _triggers[triggerType];
                while (triggerList.Count > 0)
                {
                    if (triggerList[0].Value == "TIME HIT")
                        _owner.UnCountTimerEvent();

                    FreeTrigger(triggerList[0]);
                    triggerList.RemoveAt(0);
                }
            }
        }

        /// <summary>
        /// Pascal TWX compiler convention: a VarParam named "$foo+1" or "$foo-2" represents
        /// an arithmetic expression on $foo.  The Pascal interpreter evaluated these inline;
        /// our C# interpreter must do the same.  Finds the operator position in the name,
        /// returning -1 if the name is a plain variable name.
        /// </summary>
        private static int FindArithOpInVarName(string name)
        {
            // Name always starts with '$'; operator must be preceded by at least one identifier char.
            if (string.IsNullOrEmpty(name) || name[0] != '$')
                return -1;
            for (int i = 2; i < name.Length; i++)
            {
                char c = name[i];
                if (c == '+' || c == '-' || c == '*' || c == '/')
                    return i;
                if (!(char.IsLetterOrDigit(c) || c == '_'))
                    return -1;   // unexpected char before any operator
            }
            return -1;
        }

        /// <summary>
        /// If <paramref name="param"/> is a VarParam whose name encodes an arithmetic
        /// expression (Pascal TWX convention, e.g. "$commandLength+1"), evaluate it
        /// now using the current value of the base variable in the ParamList and return
        /// a fresh read-only CmdParam holding the result.  Otherwise returns null.
        /// </summary>
        private CmdParam? EvaluateArithExprVar(VarParam param)
        {
            int opIdx = FindArithOpInVarName(param.Name);
            if (opIdx < 0) return null;

            string baseVarName = param.Name.Substring(0, opIdx);
            char op            = param.Name[opIdx];
            string rhsStr      = param.Name.Substring(opIdx + 1);

            if (!double.TryParse(rhsStr, NumberStyles.Float,
                                 CultureInfo.InvariantCulture, out double rhs))
                return null;

            // Find the base VarParam in the compiled parameter list.
            VarParam? baseVar = null;
            foreach (var p in (_cmp?.ParamList ?? Enumerable.Empty<CmdParam>()))
            {
                if (p is VarParam vp &&
                    string.Equals(vp.Name, baseVarName, StringComparison.OrdinalIgnoreCase))
                { baseVar = vp; break; }
            }
            if (baseVar == null) return null;

            double.TryParse(baseVar.Value,
                            NumberStyles.Float,
                            CultureInfo.InvariantCulture,
                            out double baseVal);

            double result = op switch
            {
                '+' => baseVal + rhs,
                '-' => baseVal - rhs,
                '*' => baseVal * rhs,
                '/' when rhs != 0 => baseVal / rhs,
                _   => baseVal
            };

            GlobalModules.DebugLog(
                $"[ArithVar] '{param.Name}': {baseVarName}={baseVal} {op} {rhs} = {result}\n");

            long intResult = (long)result;
            string valStr = (result == intResult)
                ? intResult.ToString()
                : result.ToString("R", CultureInfo.InvariantCulture);
            return new CmdParam { Value = valStr };
        }

        private void SkipArrayIndexes(byte[] code, ref int codePos)
        {
            // Read index count
            if (codePos >= code.Length)
                return;
            
            byte indexCount = code[codePos++];
            
            // For each index, read: type byte + data (format matches TWX decompiler)
            for (int i = 0; i < indexCount; i++)
            {
                if (codePos >= code.Length)
                    return;
                
                byte indexType = code[codePos++];
                
                if (indexType == ScriptConstants.PARAM_CONST)
                {
                    // 32-bit parameter ID
                    codePos += 4;
                }
                else if (indexType == ScriptConstants.PARAM_VAR || indexType == ScriptConstants.PARAM_PROGVAR)
                {
                    // 32-bit variable ID + recursively skip its indexes
                    codePos += 4;
                    SkipArrayIndexes(code, ref codePos);
                }
                else if (indexType == ScriptConstants.PARAM_SYSCONST)
                {
                    // 16-bit sysconst ID + recursively skip its indexes
                    codePos += 2;
                    SkipArrayIndexes(code, ref codePos);
                }
                else if (indexType == ScriptConstants.PARAM_CHAR)
                {
                    // 1-byte character code
                    codePos += 1;
                }
            }
        }

        private CmdParam EvaluateArrayIndexes(byte[] code, ref int codePos, CmdParam baseParam)
        {
            var server = GlobalModules.TWXServer;
            
            // Read index count
            if (codePos >= code.Length)
                return baseParam;
            
            byte indexCount = code[codePos++];
            
            if (GlobalModules.VerboseDebugMode) GlobalModules.DebugLog($"[EvaluateArrayIndexes] indexCount={indexCount}, baseParam type={baseParam.GetType().Name}\n");
            
            if (indexCount == 0)
                return baseParam;  // Not an array reference
            
            // Only VarParam supports arrays
            if (!(baseParam is VarParam varParam))
            {
                // Not an array type, but we still need to skip the index bytecode
                for (int i = 0; i < indexCount; i++)
                {
                    if (codePos >= code.Length)
                        break;
                    
                    byte indexType = code[codePos++];
                    
                    if (indexType == ScriptConstants.PARAM_CONST)
                        codePos += 4;
                    else if (indexType == ScriptConstants.PARAM_VAR || indexType == ScriptConstants.PARAM_PROGVAR)
                    {
                        codePos += 4;
                        SkipArrayIndexes(code, ref codePos);
                    }
                    else if (indexType == ScriptConstants.PARAM_SYSCONST)
                    {
                        codePos += 2;
                        SkipArrayIndexes(code, ref codePos);
                    }
                    else if (indexType == ScriptConstants.PARAM_CHAR)
                        codePos += 1;
                }
                return baseParam;
            }
            
            // Evaluate each index dimension
            var indexes = new List<string>();
            
            for (int i = 0; i < indexCount; i++)
            {
                if (codePos >= code.Length)
                    return baseParam;
                
                byte indexType = code[codePos++];
                
                string indexValue = "";
                
                if (GlobalModules.VerboseDebugMode) GlobalModules.DebugLog($"[EvalIndex {i}] indexType={indexType}\n");
                
                if (indexType == ScriptConstants.PARAM_CONST)
                {
                    // 32-bit parameter ID
                    int paramID = BitConverter.ToInt32(code, codePos);
                    codePos += 4;
                    
                    if (paramID >= 0 && paramID < (_cmp?.ParamList.Count ?? 0))
                    {
                        indexValue = _cmp!.ParamList[paramID].Value;
                        if (indexValue.Length >= 2 && indexValue[0] == '"' && indexValue[^1] == '"')
                            indexValue = indexValue.Substring(1, indexValue.Length - 2);
                        if (GlobalModules.VerboseDebugMode) GlobalModules.DebugLog($"[EvalIndex {i}] CONST paramID={paramID}, value='{indexValue}'\n");
                    }
                }
                else if (indexType == ScriptConstants.PARAM_VAR)
                {
                    // 32-bit variable ID
                    int paramID = BitConverter.ToInt32(code, codePos);
                    codePos += 4;
                    
                    if (paramID >= 0 && paramID < (_cmp?.ParamList.Count ?? 0))
                    {
                        var indexParam = _cmp!.ParamList[paramID];
                        if (GlobalModules.VerboseDebugMode) GlobalModules.DebugLog($"[EvalIndex {i}] VAR paramID={paramID}, before recursion value='{indexParam.Value}'\n");
                        // Check for Pascal arithmetic expression in var name (e.g. "$courseLength+1")
                        if (indexParam is VarParam vpIdx)
                        {
                            var arithResult = EvaluateArithExprVar(vpIdx);
                            if (arithResult != null)
                            {
                                SkipArrayIndexes(code, ref codePos);
                                indexValue = arithResult.Value;
                                if (GlobalModules.VerboseDebugMode) GlobalModules.DebugLog($"[EvalIndex {i}] ArithExpr result='{indexValue}'\n");
                                indexes.Add(indexValue);
                                continue;
                            }
                        }
                        // Recursively evaluate if this index is itself an array
                        indexParam = EvaluateArrayIndexes(code, ref codePos, indexParam);
                        indexValue = indexParam.Value;
                        if (indexValue.Length >= 2 && indexValue[0] == '"' && indexValue[^1] == '"')
                            indexValue = indexValue.Substring(1, indexValue.Length - 2);
                        if (GlobalModules.VerboseDebugMode) GlobalModules.DebugLog($"[EvalIndex {i}] VAR after recursion value='{indexValue}'\n");
                    }
                    else
                    {
                        // Skip nested indexes if param not found
                        SkipArrayIndexes(code, ref codePos);
                    }
                }
                else if (indexType == ScriptConstants.PARAM_PROGVAR)
                {
                    // 32-bit parameter ID
                    int paramID = BitConverter.ToInt32(code, codePos);
                    codePos += 4;
                    
                    if (paramID >= 0 && paramID < (_cmp?.ParamList.Count ?? 0))
                    {
                        var nameParam = _cmp!.ParamList[paramID];
                        string progVarName = nameParam is VarParam vp ? vp.Name : nameParam.Value;
                        var progVarParam = new ProgVarParam(progVarName, _owner);
                        // Recursively evaluate if this index is itself an array
                        progVarParam = EvaluateArrayIndexes(code, ref codePos, progVarParam) as ProgVarParam ?? progVarParam;
                        indexValue = progVarParam.Value;
                        if (indexValue.Length >= 2 && indexValue[0] == '"' && indexValue[^1] == '"')
                            indexValue = indexValue.Substring(1, indexValue.Length - 2);
                    }
                    else
                    {
                        // Skip nested indexes if param not found
                        SkipArrayIndexes(code, ref codePos);
                    }
                }
                else if (indexType == ScriptConstants.PARAM_SYSCONST)
                {
                    // 16-bit sysconst ID
                    ushort constID = BitConverter.ToUInt16(code, codePos);
                    codePos += 2;
                    
                    // Evaluate sysconst (skip nested indexes)
                    SkipArrayIndexes(code, ref codePos);
                    
                    if (_owner.ScriptRef != null)
                    {
                        var scriptRef = _owner.ScriptRef!;
                        var sysConst = scriptRef.GetSysConst(constID);
                        if (sysConst != null)
                        {
                            indexValue = sysConst.Read(Array.Empty<string>());
                        }
                    }
                }
                else if (indexType == ScriptConstants.PARAM_CHAR)
                {
                    // 1-byte character code
                    byte charCode = code[codePos++];
                    indexValue = ((char)charCode).ToString();
                }
                
                indexes.Add(indexValue);
            }
            
            // Get the indexed array element
            try
            {
                if (GlobalModules.VerboseDebugMode) GlobalModules.DebugLog($"[GetIndexVar] Getting element with indexes: [{string.Join(", ", indexes)}]\n");
                
                var result = varParam.GetIndexVar(indexes.ToArray());

                string traceName = varParam.Name ?? string.Empty;
                bool traceSectorLookup =
                    traceName.IndexOf("CURSECTOR", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    traceName.IndexOf("THISSECTOR", StringComparison.OrdinalIgnoreCase) >= 0;
                if (traceSectorLookup && indexes.Count > 0)
                {
                    GlobalModules.DebugLog(
                        $"[ARRAY TRACE] base='{traceName}' indexes=[{string.Join(", ", indexes)}] " +
                        $"resultName='{(result as VarParam)?.Name ?? result.Value}' resultValue='{result.Value}'\n");
                }
                
                if (GlobalModules.VerboseDebugMode && result is VarParam resultVp)
                {
                    GlobalModules.DebugLog($"[GetIndexVar] Result: '{resultVp.Name}' = '{resultVp.Value}'\n");
                }
                
                return result;
            }
            catch (Exception ex)
            {
                GlobalModules.TWXServer?.ClientMessage($"[Array Access Error] {ex.Message}\r\n");
                return baseParam;  // Return base param if index fails
            }
        }

        /// <summary>
        /// Reads the array-index section that follows a PARAM_SYSCONST in the bytecode,
        /// evaluates each index expression, and returns the resulting string values.
        /// Replaces the old SkipArrayIndexes call for sysconsts so that indexed sysconsts
        /// like PORT.CLASS[$CurSector] can receive the actual evaluated index values.
        /// </summary>
        private string[] EvaluateSysConstIndexes(byte[] code, ref int codePos)
        {
            if (codePos >= code.Length)
                return Array.Empty<string>();

            byte indexCount = code[codePos++];
            if (indexCount == 0)
                return Array.Empty<string>();

            var indexes = new List<string>(indexCount);

            for (int i = 0; i < indexCount; i++)
            {
                if (codePos >= code.Length)
                    break;

                byte indexType = code[codePos++];
                string indexValue = "";

                if (indexType == ScriptConstants.PARAM_CONST)
                {
                    int paramID = BitConverter.ToInt32(code, codePos);
                    codePos += 4;
                    if (paramID >= 0 && paramID < _cmp!.ParamList.Count)
                        indexValue = _cmp.ParamList[paramID].Value;
                }
                else if (indexType == ScriptConstants.PARAM_VAR)
                {
                    int paramID = BitConverter.ToInt32(code, codePos);
                    codePos += 4;
                    if (paramID >= 0 && paramID < _cmp!.ParamList.Count)
                    {
                        var param = _cmp.ParamList[paramID];
                        // Check for Pascal arithmetic expression in var name (e.g. "$step+1")
                        if (param is VarParam vpIdx)
                        {
                            var arithResult = EvaluateArithExprVar(vpIdx);
                            if (arithResult != null)
                            {
                                SkipArrayIndexes(code, ref codePos);
                                indexValue = arithResult.Value;
                                indexes.Add(indexValue);
                                continue;
                            }
                        }
                        param = EvaluateArrayIndexes(code, ref codePos, param);
                        indexValue = param.Value;
                    }
                    else
                        SkipArrayIndexes(code, ref codePos);
                }
                else if (indexType == ScriptConstants.PARAM_PROGVAR)
                {
                    int paramID = BitConverter.ToInt32(code, codePos);
                    codePos += 4;
                    if (paramID >= 0 && paramID < _cmp!.ParamList.Count)
                    {
                        var nameParam = _cmp.ParamList[paramID];
                        string progVarName = nameParam is VarParam vp ? vp.Name : nameParam.Value;
                        var progVar = new ProgVarParam(progVarName, _owner);
                        var evalResult = EvaluateArrayIndexes(code, ref codePos, progVar);
                        indexValue = evalResult.Value;
                    }
                    else
                        SkipArrayIndexes(code, ref codePos);
                }
                else if (indexType == ScriptConstants.PARAM_SYSCONST)
                {
                    ushort innerConstID = BitConverter.ToUInt16(code, codePos);
                    codePos += 2;
                    SkipArrayIndexes(code, ref codePos);   // nested sysconst – don't recurse infinitely
                    if (_owner.ScriptRef != null)
                    {
                        var scriptRef2 = _owner.ScriptRef!;
                        var innerConst = scriptRef2.GetSysConst(innerConstID);
                        if (innerConst != null)
                            indexValue = innerConst.Read(Array.Empty<string>());
                    }
                }
                else if (indexType == ScriptConstants.PARAM_CHAR)
                {
                    if (codePos < code.Length)
                        indexValue = ((char)code[codePos++]).ToString();
                }

                indexes.Add(indexValue);
            }

            return indexes.ToArray();
        }

        public bool Execute()
        {
            var server = GlobalModules.TWXServer;
            
            // Loop detection: check if we're repeatedly hitting the same code position
            if (_codePos == _lastCodePos && (DateTime.Now - _lastLoopCheck).TotalMilliseconds < 100)
            {
                _loopCounter++;
                if (_loopCounter >= MAX_LOOP_ITERATIONS)
                {
                    server?.ClientMessage($"\r\n*** WARNING: Possible script loop detected at position {_codePos} ***\r\n");
                    server?.ClientMessage($"*** Script has executed the same position {_loopCounter} times in rapid succession ***\r\n");
                    server?.ClientMessage($"*** This may indicate an endless loop (e.g., relog attempts with no server) ***\r\n");
                    server?.ClientMessage($"*** Script will continue, but consider stopping it if stuck ***\r\n\r\n");
                    _loopCounter = 0;  // Reset counter after warning
                }
            }
            else
            {
                _loopCounter = 0;  // Reset if we've moved or enough time passed
            }
            _lastCodePos = _codePos;
            _lastLoopCheck = DateTime.Now;

            // Don't execute if paused
            if (_paused)
            {
                return false;
            }

            if (_cmp == null)
            {
                return true; // No script loaded
            }

            byte[] code = _cmp.Code;
            
            if (code.Length == 0 || _codePos >= code.Length)
            {
                return true; // No code or reached end
            }

            try
            {
                // All Pascal TWX versions (2–6) use the same bytecode format:
                // ScriptID:Byte|LineNumber:Word|CmdID:Word|Params|0:Byte
                bool isOldFormat = true;
                
                // Execute bytecode until we hit a pause, stop, or end
                while (_codePos < code.Length)
                {
                    
                    ushort cmdID;
                    
                    if (isOldFormat)
                    {
                        // Old format: ScriptID + LineNumber + CmdID
                        if (_codePos + 5 > code.Length)
                            return true; // End of script
                        
                        byte scriptID = code[_codePos++];
                        _execScriptID = scriptID;
                        ushort lineNumber = BitConverter.ToUInt16(code, _codePos);
                        _codePos += 2;
                        cmdID = BitConverter.ToUInt16(code, _codePos);
                        _codePos += 2;
                        
                        // [Execute] per-instruction logging removed — too high-frequency.
                    }
                    else
                    {
                        // New format: PARAM_CMD + CmdID
                        byte paramType = code[_codePos++];
                        if (paramType != ScriptConstants.PARAM_CMD)
                            throw new Exception($"Expected command at position {_codePos - 1}, got param type {paramType}");

                        if (_codePos >= code.Length)
                            return true; // End of script

                        cmdID = code[_codePos++];
                    }
                    
                    // Check for label (cmdID = 255)
                    if (cmdID == 255)
                    {
                        // Skip label ID
                        if (_codePos + 4 > code.Length)
                            return true;
                        _codePos += 4;
                        continue;
                    }

                    // Record where this command's params start (for error diagnostics)
                    int cmdStartPos = _codePos;

                    // Read parameters
                    _cmdParams.Clear();
                    int paramCount = 0;
                    while (_codePos < code.Length)
                    {
                        byte paramType = code[_codePos];
                        
                        // For old format, params are terminated by 0 byte, not PARAM_CMD
                        if (isOldFormat && paramType == 0)
                        {
                            _codePos++; // Skip terminator
                            break;
                        }
                        
                        if (paramType == ScriptConstants.PARAM_CMD)
                            break; // Next command

                        _codePos++;
                        paramCount++;
                        
                        // All versions use ParamList references; only command header format differs
                        if (paramType == ScriptConstants.PARAM_CONST || paramType == ScriptConstants.PARAM_VAR)
                        {
                            // Read 32-bit parameter ID
                            if (_codePos + 4 > code.Length)
                                return true;
                            
                            int paramID = BitConverter.ToInt32(code, _codePos);
                            _codePos += 4;

                            // For older file versions, parameters might be embedded differently
                            // Create a placeholder if parameter doesn't exist
                            if (paramID < 0 || paramID >= _cmp.ParamList.Count)
                            {
                                server?.ClientMessage($"Warning: Parameter ID {paramID} not found in list (count={_cmp.ParamList.Count}), using empty string\r\n");
                                Console.WriteLine($"Warning: Parameter ID {paramID} not found in ParamList (count={_cmp.ParamList.Count})");
                                
                                // Skip array index data if it's a VAR type
                                if (paramType == ScriptConstants.PARAM_VAR)
                                {
                                    SkipArrayIndexes(code, ref _codePos);
                                }
                                
                                var placeholder = new CmdParam { Value = "" };
                                _cmdParams.Add(placeholder);
                            }
                            else
                            {
                                CmdParam param = _cmp.ParamList[paramID];

                                // Evaluate array subscripts if this is a variable
                                if (paramType == ScriptConstants.PARAM_VAR)
                                {
                                    // Pascal TWX convention: variable names like "$foo+1" represent
                                    // arithmetic expressions; evaluate them against the base var now.
                                    if (param is VarParam vpArith)
                                    {
                                        var arithResult = EvaluateArithExprVar(vpArith);
                                        if (arithResult != null)
                                        {
                                            // Skip the (zero) index count bytes and add the computed value.
                                            SkipArrayIndexes(code, ref _codePos);
                                            _cmdParams.Add(arithResult);
                                            goto nextParam;
                                        }
                                    }

                                    if (GlobalModules.VerboseDebugMode && param is VarParam vp)
                                    {
                                        GlobalModules.DebugLog($"[PreEval] VAR param '{vp.Name}' = '{vp.Value}', paramID={paramID}, objID={param.GetHashCode():X8}, about to eval array indexes\n");
                                    }

                                    param = EvaluateArrayIndexes(code, ref _codePos, param);
                                    
                                    if (GlobalModules.VerboseDebugMode && param is VarParam vp2)
                                    {
                                        GlobalModules.DebugLog($"[PostEval] VAR param '{vp2.Name}' = '{vp2.Value}', paramID={paramID}, objID={param.GetHashCode():X8}\n");
                                    }
                                }

                                if (paramType == ScriptConstants.PARAM_CONST)
                                {
                                    // Pascal passes per-command parameter values, so mutating a
                                    // constant parameter during execution must not rewrite the
                                    // shared compiled ParamList entry for later commands.
                                    _cmdParams.Add(new CmdParam { Value = param.Value });
                                }
                                else
                                {
                                    _cmdParams.Add(param);
                                }
                                
                                // Debug: show variable names for PARAM_VAR (only if enabled)
                                if (_enableVariableDebug && paramType == ScriptConstants.PARAM_VAR && param is VarParam varParam)
                                {
                                    server?.ClientMessage($"  [VAR {paramID}] = {varParam.Name} (value='{varParam.Value}')\r\n");
                                }
                            }
                        }
                        else if (paramType == ScriptConstants.PARAM_SYSCONST)
                        {
                            // System constant - read 2-byte ID and lookup
                            if (_codePos + 2 > code.Length)
                                return true;
                            
                            ushort constID = BitConverter.ToUInt16(code, _codePos);
                            _codePos += 2;
                            
                            // Evaluate array indexes so indexed sysconsts like PORT.CLASS[$CurSector]
                            // receive the actual runtime index values.
                            string[] sysConstIndexValues = EvaluateSysConstIndexes(code, ref _codePos);
                            
                            if (_owner.ScriptRef == null)
                                throw new Exception("ScriptRef not initialized");
                            
                            var scriptRef3 = _owner.ScriptRef!;
                            var sysConst = scriptRef3.GetSysConst(constID);
                            if (sysConst == null)
                                throw new Exception($"System constant {constID} not found");
                            
                            string constValue = sysConst.Read(sysConstIndexValues);
                            var constParam = new CmdParam { Value = constValue };
                            _cmdParams.Add(constParam);
                        }
                        else if (paramType == ScriptConstants.PARAM_PROGVAR)
                        {
                            // Program variable - read from ParamList like other params
                            if (_codePos + 4 > code.Length)
                                return true;
                            
                            int paramID = BitConverter.ToInt32(code, _codePos);
                            _codePos += 4;

                            // Skip array index data using proper format (type + data per index)
                            SkipArrayIndexes(code, ref _codePos);

                            if (_owner.ScriptRef == null)
                                throw new Exception("ScriptRef not initialized");

                            // Get the parameter containing the program variable name
                            string progVarName = "";
                            if (paramID >= 0 && paramID < _cmp.ParamList.Count)
                            {
                                var param = _cmp.ParamList[paramID];
                                progVarName = param is VarParam vp ? vp.Name : param.Value;
                            }

                            // Create a ProgVarParam that reads/writes to storage automatically
                            var progVarParam = new ProgVarParam(progVarName, _owner);
                            _cmdParams.Add(progVarParam);
                        }
                        else if (paramType == ScriptConstants.PARAM_CHAR)
                        {
                            if (_codePos >= code.Length)
                                return true;
                            
                            byte charCode = code[_codePos++];
                            var charParam = new CmdParam { Value = ((char)charCode).ToString() };
                            _cmdParams.Add(charParam);
                        }
                        else
                        {
                            // Unknown parameter type - this indicates either:
                            // 1. Unimplemented parameter type
                            // 2. Bytecode misalignment 
                            // 3. Corrupted script file
                            server?.ClientMessage($"ERROR: Unknown parameter type {paramType} (0x{paramType:X2}, ASCII:'{(char)paramType}') at position {_codePos-1}\r\n");
                            server?.ClientMessage($"  Current command ID: {cmdID}\r\n");
                            server?.ClientMessage($"  Parameters read so far: {paramCount-1}\r\n");
                            
                            int prevStart = Math.Max(0, _codePos - 17);
                            int prevLen = Math.Min(16, _codePos - 1 - prevStart);
                            if (prevLen > 0)
                                server?.ClientMessage($"  Previous {prevLen} bytes: {BitConverter.ToString(code, prevStart, prevLen)}\r\n");
                            
                            int nextLen = Math.Min(16, code.Length - _codePos);
                            if (nextLen > 0)
                                server?.ClientMessage($"  Next {nextLen} bytes: {BitConverter.ToString(code, _codePos, nextLen)}\r\n");
                            
                            // This is a fatal error - we cannot safely continue
                            throw new Exception($"Unknown parameter type {paramType} (0x{paramType:X2}, ASCII:'{(char)paramType}') at position {_codePos-1}. Cannot safely parse remaining bytecode.");
                        }
                        nextParam: ;  // jump target used by arithmetic-expression variable evaluation
                    }

                    if (_owner.ScriptRef == null)
                        throw new Exception("ScriptRef not initialized");

                    // Execute command (with bounds checking for compatibility)
                    if (cmdID >= _owner.ScriptRef.CmdCount)
                    {
                        server?.ClientMessage($"Warning: Command ID {cmdID} not found (max={_owner.ScriptRef.CmdCount-1}), skipping\r\n");
                        continue; // Skip unknown commands for version compatibility
                    }
                    
                    var cmd = _owner.ScriptRef.GetCmd(cmdID);

                    if (cmd.Name == "GOTO" && _cmdParams.Count > 0)
                    {
                        string ns = _cmp?.GetScriptNamespace(_execScriptID) ?? string.Empty;
                        GlobalModules.DebugLog($"[Execute/GOTO] raw='{_cmdParams[0].Value}' execScriptID={_execScriptID} ns='{ns}' codePos={_codePos}\n");
                    }
                    
                    // Validate minimum parameter count before executing
                    if (_cmdParams.Count < cmd.MinParams)
                    {
                        var _dbgSb = new System.Text.StringBuilder();
                        _dbgSb.AppendLine($"ERROR: Command '{cmd.Name}' (ID={cmdID}) requires at least {cmd.MinParams} parameters, but only {_cmdParams.Count} were provided.");
                        int dumpStart = Math.Max(0, cmdStartPos - 20);
                        int dumpLen = Math.Min(40, code.Length - dumpStart);
                        _dbgSb.AppendLine($"  Code pos: cmdStart={cmdStartPos}, current={_codePos}, total={code.Length}");
                        _dbgSb.AppendLine($"  Bytes [{dumpStart}..{dumpStart+dumpLen-1}]: {BitConverter.ToString(code, dumpStart, dumpLen)}");
                        for (int pi = 0; pi < _cmdParams.Count; pi++)
                            _dbgSb.AppendLine($"  Param[{pi}] = '{_cmdParams[pi].Value}'");
                        if (_codePos < code.Length)
                        {
                            int nextLen = Math.Min(10, code.Length - _codePos);
                            _dbgSb.AppendLine($"  Next {nextLen} bytes at {_codePos}: {BitConverter.ToString(code, _codePos, nextLen)}");
                        }
                        File.AppendAllText("/tmp/twxproxy_debug.log", _dbgSb.ToString());
                        continue;
                    }
                    
                    // Handle RETURN before dispatch - stack/flow is maintained by Script itself.
                    if (cmd.Name == "RETURN")
                    {
                        GlobalModules.DebugLog($"[Execute] RETURN - popping substack (depth={_subStack.Count}, codePos={_codePos})\n");
                        Return();
                        GlobalModules.DebugLog($"[Execute] RETURN completed, new codePos={_codePos}, continuing execution\n");
                        continue;
                    }
                    
                    if (cmd.OnCmd == null)
                        throw new Exception($"Command {cmd.Name} has no handler");
                    
                    if (GlobalModules.VerboseDebugMode)
                        GlobalModules.DebugLog($"[Dispatch] CMD={cmd.Name} paramCount={_cmdParams.Count}\n");
                    CmdAction action;
                    try
                    {
                        action = cmd.OnCmd(this, _cmdParams.ToArray());
                    }
                    catch (Exception ex)
                    {
                        GlobalModules.DebugLog($"[Dispatch] EXCEPTION in {cmd.Name}: {ex.Message}\n");
                        throw new ScriptException($"Error executing command '{cmd.Name}': {ex.Message}", ex);
                    }

                    // Handle command actions
                    switch (action)
                    {
                        case CmdAction.Stop:
                            // Fast-forward _codePos to end so that HasCode becomes false.
                            // This prevents ActivateTriggers from calling Execute() again
                            // on the remaining bytecode after a "halt" command — which was
                            // the cause of the "No safe options!" infinite loop where halt
                            // fired but ActivateTriggers immediately re-ran the code after
                            // halt (send bestWarp, goto :Move, re-register triggers, loop).
                            _codePos = code.Length;
                            return true; // Terminate script
                        
                        case CmdAction.Pause:
                            _paused = true;
                            _pausedReason = PauseReason.Command;
                            GlobalModules.DebugLog($"[PAUSED_BY] cmd='{cmd.Name}' id={cmdID}\n");
                            GlobalModules.FlushDebugLog();
                            return false; // Pause execution
                        
                        case CmdAction.Auth:
                            _waitingForAuth = true;
                            _pausedReason = PauseReason.Auth;
                            return false; // Wait for authentication
                        
                        case CmdAction.None:
                        default:
                            // Continue execution
                            break;
                    }
                }

                GlobalModules.FlushDebugLog();
                return true; // Reached end of script
            }
            catch (ScriptException ex)
            {
                // Script-level error — log and terminate the script gracefully.
                // Do NOT re-throw: propagating a ScriptException out of Execute() causes it
                // to bubble up through TextEvent → Network.ReadFromServerAsync, where it is
                // mistaken for a server I/O error and fires a spurious Disconnected event.
                string msg = $"[Script.Execute] Script exception at pos {_codePos}: {ex.Message}";
                Console.WriteLine(msg);
                GlobalModules.DebugLog(msg + "\n");
                if (ex.InnerException != null)
                {
                    string innerMsg = $"[Script.Execute] Inner: {ex.InnerException.Message}";
                    Console.WriteLine(innerMsg);
                    GlobalModules.DebugLog(innerMsg + "\n");
                }
                GlobalModules.TWXServer?.ClientMessage($"\r\n[Script error] {ex.Message}\r\n");
                _codePos = code.Length; // terminate this script
                return true;
            }
            catch (Exception ex)
            {
                // Unexpected exception — log, notify, and terminate the script gracefully.
                string msg = $"[Script.Execute] Unexpected exception at pos {_codePos}: {ex.Message}";
                Console.WriteLine(msg);
                Console.WriteLine($"[Script.Execute] Stack trace: {ex.StackTrace}");
                GlobalModules.DebugLog(msg + "\n");
                GlobalModules.TWXServer?.ClientMessage($"\r\n[Script error] {ex.Message}\r\n");
                _codePos = code.Length; // terminate this script
                return true;
            }
        }

        private string NormalizeLabelLookup(string label)
        {
            if (_cmp == null)
                return label;

            string normalized = label ?? string.Empty;

            if (normalized.StartsWith(':'))
            {
                if (normalized.Length < 2)
                    throw new ScriptException($"Bad goto label '{label}'");

                normalized = normalized.Substring(1);
            }
            else if (string.IsNullOrEmpty(normalized))
            {
                throw new ScriptException($"Bad goto label '{label}'");
            }

            normalized = normalized.ToUpperInvariant();
            _cmp.ExtendName(ref normalized, _execScriptID);

            return normalized;
        }

        public void GotoLabel(string label)
        {
            if (GlobalModules.VerboseDebugMode)
                GlobalModules.DebugLog($"[GotoLabel] Attempting to goto label '{label}'\n");

            if (_cmp == null)
                throw new Exception("No script loaded");

            string normalized = NormalizeLabelLookup(label);
            int labelPos = _cmp.FindLabel(normalized);

            if (labelPos < 0)
            {
                GlobalModules.DebugLog($"[GotoLabel] ERROR: Label '{label}' normalized='{normalized}' execScriptID={_execScriptID} not found in script\n");

                string ns = _cmp.GetScriptNamespace(_execScriptID);
                if (!string.IsNullOrEmpty(ns))
                {
                    string prefix = ns + "~";
                    var nsLabels = _cmp.LabelList
                        .Where(l => l.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                        .Select(l => l.Name)
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .Take(20)
                        .ToList();

                    GlobalModules.DebugLog($"[GotoLabel] Namespace '{ns}' sample labels ({nsLabels.Count}): {string.Join(", ", nsLabels)}\n");
                }

                string suffix = normalized;
                int tilde = normalized.IndexOf('~');
                if (tilde >= 0 && tilde + 1 < normalized.Length)
                    suffix = normalized.Substring(tilde + 1);

                var suffixLabels = _cmp.LabelList
                    .Where(l => l.Name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                    .Select(l => l.Name)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Take(20)
                    .ToList();
                GlobalModules.DebugLog($"[GotoLabel] Suffix '{suffix}' sample labels ({suffixLabels.Count}): {string.Join(", ", suffixLabels)}\n");

                throw new ScriptException($"Label '{label}' not found");
            }

            if (GlobalModules.VerboseDebugMode)
                GlobalModules.DebugLog($"[GotoLabel] Found label '{normalized}' at position {labelPos}\n");
            _codePos = labelPos;
        }

        public bool LabelExists(string label)
        {
            if (_cmp == null)
                return false;

            try
            {
                string normalized = NormalizeLabelLookup(label);
                return _cmp.FindLabel(normalized) >= 0;
            }
            catch (ScriptException)
            {
                return false;
            }
        }

        public void Gosub(string labelName)
        {
            GlobalModules.DebugLog($"[GOSUB] Called with label '{labelName}'\n");
            // Push current position and jump to label
            _subStack.Push(_codePos);
            GotoLabel(labelName);
        }

        public void GosubFromMenu(string labelName)
        {
            // When calling GOSUB from menu (not from bytecode), we push a safe return position
            // that will cause Execute() to stop immediately when RETURN is hit
            if (_cmp != null && _cmp.Code != null)
            {
                _subStack.Push(_cmp.Code.Length); // Push end-of-bytecode position
            }
            else
            {
                _subStack.Push(_codePos); // Fallback to current position
            }
            GotoLabel(labelName);
        }

        public void Return()
        {
            if (_subStack.Count == 0)
                throw new ScriptException("Return without gosub");

            int returnPos = _subStack.Pop();
            GlobalModules.DebugLog($"[RETURN] Restoring codePos={returnPos}, remainingDepth={_subStack.Count}\n");
            _codePos = returnPos;
        }

        public void DumpVars(string searchName)
        {
            // Dump script variables (would require variable storage implementation)
            // Placeholder
        }

        public void DumpTriggers()
        {
            // Dump all triggers
            foreach (var triggerType in Enum.GetValues(typeof(TriggerType)).Cast<TriggerType>())
            {
                var triggerList = _triggers[triggerType];
                if (triggerList.Count > 0)
                {
                    // TWXServer.ClientMessage($"Triggers ({triggerType}):");
                    foreach (var trigger in triggerList)
                    {
                        // TWXServer.ClientMessage($"  {trigger.Name} = {trigger.Value}");
                    }
                }
            }
        }

        public void SetVariable(string varName, string value, string index)
        {
            // Set a script variable (would require variable storage implementation)
            // Placeholder
        }

        public void InputCompleted(string inputText, object varParam)
        {
            // Input has just been completed - set the variable and resume execution
            if (varParam is CmdParam param)
            {
                param.Value = inputText;
            }

            // Unlock script and resume execution
            Locked = false;
            Execute();
        }

        /// <summary>
        /// Find a top-level named VarParam by name and set its value.
        /// Used to inject state (e.g. $doRelog=1) from calling code.
        /// </summary>
        public bool SetScriptVar(string varName, string value)
        {
            if (_cmp == null) return false;
            foreach (var param in _cmp.ParamList)
            {
                if (param is VarParam vp && vp.Name == varName && vp.Vars.Count == 0)
                {
                    string old = vp.Value;
                    vp.Value = value;
                    GlobalModules.DebugLog($"[FORCEVAR] {varName}: '{old}' -> '{value}'\n");
                    return true;
                }
            }
            return false;
        }

        public bool LocalInputEvent(string text)
        {
            // Handle input from local client (for GETINPUT command)
            string pname = (_inputVarParam is ProgVarParam pvp) ? $"PV:{pvp.Name}" 
                         : (_inputVarParam is VarParam ivp) ? $"V:{ivp.Name}" : "null";
            GlobalModules.DebugLog($"[LIE] waiting={_waitingForInput} kpm={_keypressMode} var={pname} text='{text.TrimEnd('\r', '\n')}'\n");
            if (_waitingForInput && _inputVarParam != null)
            {
                Console.WriteLine($"[Script.LocalInputEvent] Received input: '{text}', storing and resuming");
                
                // Store the input in the variable
                string trimmed = text.TrimEnd('\r', '\n');
                _inputVarParam.Value = trimmed;

                // When a credential variable is set via line-input (not keypress), persist it.
                if (!_keypressMode && _inputVarParam is VarParam credVp)
                {
                    string credName = credVp.Name;
                    if (credName == "$username" || credName == "$password" || credName == "$letter")
                    {
                        ScriptRef.TrackCredential(credName, trimmed);
                        GlobalModules.DebugLog($"[CRED] Saved {credName}='{trimmed}'\n");
                    }
                }

                // When Z is pressed in single-key mode (config menu), force $doRelog=1
                // so the Z handler's ::5258 check succeeds and jumps to the connect path.
                if (trimmed.Equals("Z", StringComparison.OrdinalIgnoreCase) && _keypressMode)
                {
                    SetScriptVar("$doRelog", "1");
                    GlobalModules.DebugLog("[Z-KEY] Set $doRelog='1' to trigger connect\n");
                }
                
                // Clear waiting state
                _waitingForInput = false;
                _inputVarParam = null;
                
                // Unpause the script before resuming
                _paused = false;
                
                // Resume script execution with continuation loop
                GlobalModules.DebugLog("[LocalInputEvent] Resuming script after input\n");
                bool completed = false;
                int iterations = 0;
                const int maxIterations = 1000;
                
                while (!completed && iterations < maxIterations)
                {
                    completed = Execute();
                    iterations++;
                    
                    if (_paused || completed)
                        break;
                }
                
                // After execution completes, check if a menu is still open
                // If so, we need to re-pause the script and redisplay the menu
                if (!completed && GlobalModules.TWXMenu is MenuManager menuMgr && menuMgr.IsMenuOpen())
                {
                    Console.WriteLine($"[Script.LocalInputEvent] Menu still open after handler, re-pausing and redisplaying");
                    _paused = true;
                    menuMgr.RedisplayCurrentMenu();
                }
                
                return completed;
            }
            
            Console.WriteLine($"[Script.LocalInputEvent] Not waiting for input or inputVarParam is null");
            return false;
        }

        public void AddMenu(object menuItem)
        {
            _menuItemList.Add(menuItem);
        }

        public void AddWindow(IScriptWindow window)
        {
            _windowList.Add(window);
        }

        public void RemoveWindow(IScriptWindow window)
        {
            window.Dispose();
            _windowList.Remove(window);
        }

        public IScriptWindow? FindWindow(string windowName)
        {
            // Find window by name
            foreach (var window in _windowList)
            {
                if (window.Name.Equals(windowName, StringComparison.OrdinalIgnoreCase))
                {
                    return window;
                }
            }
            return null;
        }

        public void CompileLib()
        {
            // Compile library commands (complex implementation)
            // Placeholder
        }

        public void GetLibCmd(List<string> scriptText, string command)
        {
            // Get library command implementation (complex implementation)
            // Placeholder
        }

        // Properties
        public bool System { get; set; }
        public bool IsBot { get; set; }
        public string BotName { get; set; } = string.Empty;
        public bool TriggersActive { get; set; }
        public bool Locked { get; set; }
        // These properties wrap the private backing fields used by TextLineEvent/TextEvent,
        // so that CmdWaitFor/CmdWaitOn (in ScriptCmdImpl.cs) and the text-matching loop
        // both operate on the same state.
        public bool WaitForActive
        {
            get => _waitForActive;
            set => _waitForActive = value;
        }
        public string WaitText
        {
            get => _waitText;
            set => _waitText = value;
        }

        public void SetWaitingForInput(CmdParam varParam, bool keypressMode = false)
        {
            _waitingForInput = true;
            _keypressMode = keypressMode;
            _inputVarParam = varParam;
        }
        public string OutText => _outText;
        public bool Silent { get; set; }
        public bool Paused { get => _paused; set => _paused = value; }
        public PauseReason PausedReason { get => _pausedReason; set => _pausedReason = value; }
        public bool WaitingForInput => _waitingForInput;
        public bool KeypressMode => _keypressMode;
        public int SubStackDepth => _subStack.Count;
        public ModInterpreter Controller => _owner;
        public int ExecScript => _execScriptID;
        public object? Cmp => _cmp;
        public ScriptCmp? Compiler => _cmp;
        /// <summary>True when a script is loaded and has not yet run past end-of-bytecode.</summary>
        public bool HasCode => _cmp != null && _cmp.Code.Length > 0 && _codePos < _cmp.Code.Length;

        /// <summary>
        /// True when the script has any registered triggers or an active waitFor/waitOn.
        /// Used by ActivateTriggers to decide whether a code-exhausted script should be
        /// kept alive (it still needs to respond to events) or cleaned up.
        /// </summary>
        public bool HasActiveTriggers =>
            _waitForActive ||
            _triggers.Values.Any(list => list.Count > 0);
        public int DecimalPrecision { get; set; }
        public string ProgramDir => _owner.ProgramDir;
        public string ScriptName => Path.GetFileName(_cmp?.ScriptFile ?? string.Empty);
        public string PersistenceId
        {
            get
            {
                if (_cmp != null && _cmp.IncludeScriptCount > 0)
                    return _cmp.GetIncludeScript(0);

                return ScriptName;
            }
        }

        /// <summary>
        /// Pause the script execution
        /// </summary>
        public void Pause()
        {
            _paused = true;
        }

        /// <summary>
        /// Resume the script execution
        /// </summary>
        public void Resume()
        {
            _paused = false;
            // If script was waiting for something, try to execute again
            if (!_waitForActive && !_waitingForAuth)
            {
                Execute();
            }
        }
    }

    #endregion
}
