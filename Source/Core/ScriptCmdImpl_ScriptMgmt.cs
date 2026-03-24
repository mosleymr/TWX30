/*
Copyright (C) 2005  Remco Mulder, 2026 Matt Mosley

This program is free software; you can redistribute it and/or modify
it under the terms of the GNU General Public License as published by
the Free Software Foundation; either version 2 of the License, or
(at your option) any later version.
*/

using System;
using System.Collections.Generic;
using System.Linq;

namespace TWXProxy.Core
{
    public partial class ScriptRef
    {
        // Reference to the active script interpreter
        // TODO: This should be injected or accessed through a service locator
        private static ModInterpreter? _activeInterpreter;

        #region Script Management Command Implementation

        private static CmdAction CmdLoadScript_Impl(object script, CmdParam[] parameters)
        {
            // CMD: load <filename>
            // Load and execute a script file
            var interpreter = GlobalModules.TWXInterpreter as ModInterpreter;
            if (interpreter == null)
            {
                GlobalModules.DebugLog("[LOAD] No active interpreter\n");
                return CmdAction.None;
            }

            string filename = parameters[0].Value;
            GlobalModules.DebugLog($"[LOAD] Loading script '{filename}'\n");
            
            try
            {
                interpreter.Load(filename, false);
            }
            catch (Exception ex)
            {
                GlobalModules.DebugLog($"[LOAD] Failed: {ex.Message}\n");
            }
            
            return CmdAction.None;
        }

        private static CmdAction CmdUnloadScript_Impl(object script, CmdParam[] parameters)
        {
            // CMD: stop <filename>
            // Stop a running script by filename
            var interpreter = GlobalModules.TWXInterpreter as ModInterpreter;
            if (interpreter == null)
            {
                GlobalModules.DebugLog("[STOP] No active interpreter\n");
                return CmdAction.None;
            }

            string filename = parameters[0].Value;
            GlobalModules.DebugLog($"[STOP] Stopping script '{filename}'\n");
            
            try
            {
                // Find script by filename and stop it
                for (int i = interpreter.Count - 1; i >= 0; i--)
                {
                    var scriptObj = interpreter.GetScript(i);
                    if (scriptObj != null && Path.GetFileName(scriptObj.ScriptName).Equals(Path.GetFileName(filename), StringComparison.OrdinalIgnoreCase))
                    {
                        GlobalModules.DebugLog($"[STOP] Found and stopping script at index {i}: '{scriptObj.ScriptName}'\n");
                        interpreter.Stop(i);
                    }
                }
            }
            catch (Exception ex)
            {
                GlobalModules.DebugLog($"[STOP] Failed: {ex.Message}\n");
            }
            
            return CmdAction.None;
        }

        private static CmdAction CmdIsScriptLoaded_Impl(object script, CmdParam[] parameters)
        {
            // CMD: isscriptloaded var <filename>
            // Check if a script is currently loaded
            
            if (parameters[0] is VarParam varParam)
            {
                string filename = parameters[1].Value;
                bool loaded = false;
                
                if (_activeInterpreter != null)
                {
                    for (int i = 0; i < _activeInterpreter.Count; i++)
                    {
                        var scriptObj = _activeInterpreter.GetScript(i);
                        if (scriptObj != null && scriptObj.ScriptName.Equals(filename, StringComparison.OrdinalIgnoreCase))
                        {
                            loaded = true;
                            break;
                        }
                    }
                }
                
                varParam.Value = loaded ? "1" : "0";
            }
            
            return CmdAction.None;
        }

        private static CmdAction CmdPauseScript_Impl(object script, CmdParam[] parameters)
        {
            // CMD: pausescript <filename>
            // Pause a running script
            
            if (_activeInterpreter == null)
            {
                Console.WriteLine("[Script] PAUSESCRIPT: No active interpreter");
                return CmdAction.None;
            }

            string filename = parameters[0].Value;
            
            try
            {
                for (int i = 0; i < _activeInterpreter.Count; i++)
                {
                    var scriptObj = _activeInterpreter.GetScript(i);
                    if (scriptObj != null && scriptObj.ScriptName.Equals(filename, StringComparison.OrdinalIgnoreCase))
                    {
                        scriptObj.Pause();
                        Console.WriteLine($"[Script] PAUSESCRIPT: Paused {filename}");
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Script] PAUSESCRIPT failed: {ex.Message}");
            }
            
            return CmdAction.None;
        }

        private static CmdAction CmdResumeScript_Impl(object script, CmdParam[] parameters)
        {
            // CMD: resumescript <filename>
            // Resume a paused script
            
            if (_activeInterpreter == null)
            {
                Console.WriteLine("[Script] RESUMESCRIPT: No active interpreter");
                return CmdAction.None;
            }

            string filename = parameters[0].Value;
            
            try
            {
                for (int i = 0; i < _activeInterpreter.Count; i++)
                {
                    var scriptObj = _activeInterpreter.GetScript(i);
                    if (scriptObj != null && scriptObj.ScriptName.Equals(filename, StringComparison.OrdinalIgnoreCase))
                    {
                        scriptObj.Resume();
                        Console.WriteLine($"[Script] RESUMESCRIPT: Resumed {filename}");
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Script] RESUMESCRIPT failed: {ex.Message}");
            }
            
            return CmdAction.None;
        }

        private static CmdAction CmdGetLoadedScripts_Impl(object script, CmdParam[] parameters)
        {
            // CMD: getloadedscripts var
            // Get list of all currently loaded scripts
            
            if (parameters[0] is VarParam varParam)
            {
                var scriptNames = new List<string>();
                
                if (_activeInterpreter != null)
                {
                    for (int i = 0; i < _activeInterpreter.Count; i++)
                    {
                        var scriptObj = _activeInterpreter.GetScript(i);
                        if (scriptObj != null && !string.IsNullOrEmpty(scriptObj.ScriptName))
                        {
                            scriptNames.Add(scriptObj.ScriptName);
                        }
                    }
                }
                
                varParam.SetArrayFromStrings(scriptNames);
            }
            
            return CmdAction.None;
        }

        private static CmdAction CmdStopAllScripts_Impl(object script, CmdParam[] parameters)
        {
            // CMD: stopallscripts
            // Stop all running scripts (except system scripts)
            
            if (_activeInterpreter == null)
            {
                Console.WriteLine("[Script] STOPALLSCRIPTS: No active interpreter");
                return CmdAction.None;
            }

            try
            {
                _activeInterpreter.StopAll(false); // false = don't stop system scripts
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Script] STOPALLSCRIPTS failed: {ex.Message}");
            }
            
            return CmdAction.None;
        }

        private static CmdAction CmdGetScriptName_Impl(object script, CmdParam[] parameters)
        {
            // CMD: getscriptname var
            // Get the name of the current script
            
            if (parameters[0] is VarParam varParam)
            {
                if (script is Script scriptObj)
                {
                    varParam.Value = scriptObj.ScriptName ?? string.Empty;
                }
                else
                {
                    varParam.Value = string.Empty;
                }
            }
            
            return CmdAction.None;
        }

        #endregion

        #region Script Management Helper

        /// <summary>
        /// Set the active script interpreter for script management commands
        /// This should be called when the interpreter is initialized
        /// </summary>
        public static void SetActiveInterpreter(ModInterpreter? interpreter)
        {
            _activeInterpreter = interpreter;
        }

        #endregion
    }
}
