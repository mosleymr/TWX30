/*
Copyright (C) 2005  Remco Mulder, 2026 Matt Mosley

This program is free software; you can redistribute it and/or modify
it under the terms of the GNU General Public License as published by
the Free Software Foundation; either version 2 of the License, or
(at your option) any later version.
*/

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace TWXProxy.Core
{
    public partial class ScriptRef
    {
        // Variable persistence storage (per-script)
        private static readonly Dictionary<string, Dictionary<string, string>> _scriptVars = new();
        
        // Global variable storage (shared across all scripts)
        private static readonly Dictionary<string, string> _globalVars = new();
        
        // Program variables (internal proxy settings)
        private static readonly Dictionary<string, string> _progVars = new();

        // Per-game variable storage: loaded from GameConfig.Variables at game start;
        // updated on every savevar call and flushed to disk via OnVariableSaved.
        private static readonly Dictionary<string, string> _currentGameVars = new();

        /// <summary>
        /// Called whenever savevar persists a value. The delegate (set by
        /// ProxyService) writes the key/value into GameConfig.Variables and
        /// saves the game data file.
        /// </summary>
        public static Action<string, string>? OnVariableSaved;
        
        // Persistence file paths
        private static string _variablePersistencePath = "variables.json";
        private static string _globalVarsPath = "globals.json";
        private static string _progVarsPath = "progvars.json";
        private static string _credentialsPath = Path.Combine(AppContext.BaseDirectory, "credentials.json");

        // Login credentials (username/password/game letter) – persisted separately
        private static readonly Dictionary<string, string> _credentials = new();

        #region Variable Persistence Implementation

        private static CmdAction CmdLoadVar_Impl(object script, CmdParam[] parameters)
        {
            // CMD: loadvar var
            // Load variable value from persistent storage
            if (parameters[0] is VarParam varParam)
            {
                string scriptId = GetScriptId(script);
                string varName = varParam.Name;
                
                if (_scriptVars.TryGetValue(scriptId, out var vars) &&
                    vars.TryGetValue(varName, out var value))
                {
                    varParam.Value = value;
                    GlobalModules.DebugLog($"[LOADVAR] scriptId='{scriptId}' var='{varName}' source='script-cache' value='{value}'\n");
                }
                else if (_currentGameVars.TryGetValue(varName, out var gameValue))
                {
                    // Fallback to per-game persistent storage loaded at game start.
                    varParam.Value = gameValue;
                    // Prime the per-script cache so subsequent in-memory lookups hit.
                    if (!_scriptVars.ContainsKey(scriptId))
                        _scriptVars[scriptId] = new Dictionary<string, string>();
                    _scriptVars[scriptId][varName] = gameValue;
                    GlobalModules.DebugLog($"[LOADVAR] scriptId='{scriptId}' var='{varName}' source='game-cache' value='{gameValue}'\n");
                }
                else
                {
                    GlobalModules.DebugLog($"[LOADVAR] scriptId='{scriptId}' var='{varName}' source='miss' value='{varParam.Value}'\n");
                }
            }
            return CmdAction.None;
        }

        private static CmdAction CmdSaveVar_Impl(object script, CmdParam[] parameters)
        {
            // CMD: savevar var
            // Save variable value to persistent storage
            if (parameters[0] is VarParam varParam)
            {
                string scriptId = GetScriptId(script);
                string varName = varParam.Name;
                string value = varParam.Value;
                
                if (!_scriptVars.ContainsKey(scriptId))
                    _scriptVars[scriptId] = new Dictionary<string, string>();
                
                _scriptVars[scriptId][varName] = value;
                GlobalModules.DebugLog($"[SAVEVAR] scriptId='{scriptId}' var='{varName}' value='{value}'\n");

                // Persist to the per-game data file via ProxyService callback.
                _currentGameVars[varName] = value;
                OnVariableSaved?.Invoke(varName, value);
            }
            return CmdAction.None;
        }

        private static CmdAction CmdLoadGlobal_Impl(object script, CmdParam[] parameters)
        {
            // CMD: loadglobal <name> var
            // Load global variable (shared across all scripts)
            string name = parameters[0].Value;
            
            if (_globalVars.TryGetValue(name, out var value) && parameters[1] is VarParam varParam)
            {
                varParam.Value = value;
            }
            return CmdAction.None;
        }

        private static CmdAction CmdSaveGlobal_Impl(object script, CmdParam[] parameters)
        {
            // CMD: saveglobal <name> var
            // Save global variable (shared across all scripts)
            string name = parameters[0].Value;
            
            if (parameters[1] is VarParam varParam)
            {
                _globalVars[name] = varParam.Value;
            }
            
            return CmdAction.None;
        }

        private static CmdAction CmdClearGlobals_Impl(object script, CmdParam[] parameters)
        {
            // CMD: clearglobals
            // Clear all global variables
            _globalVars.Clear();
            return CmdAction.None;
        }

        private static CmdAction CmdListGlobals_Impl(object script, CmdParam[] parameters)
        {
            // CMD: listglobals var <pattern>
            // List global variables matching pattern
            if (parameters[0] is VarParam varParam)
            {
                string pattern = parameters.Length > 1 ? parameters[1].Value : "*";
                var matchingVars = _globalVars.Keys
                    .Where(key => MatchesPattern(key, pattern))
                    .ToList();
                
                varParam.SetArrayFromStrings(matchingVars);
            }
            return CmdAction.None;
        }

        private static CmdAction CmdSetProgVar_Impl(object script, CmdParam[] parameters)
        {
            // CMD: setprogvar <name> <value>
            // Set program variable (internal proxy settings)
            string name = parameters[0].Value;
            string value = parameters[1].Value;
            
            GlobalModules.DebugLog($"[SETPROGVAR] '{name}' = '{value}'\n");
            _progVars[name] = value;
            return CmdAction.None;
        }

        private static string GetScriptId(object script)
        {
            // Extract script identifier from Script object
            if (script is Script scriptObj)
            {
                return scriptObj.PersistenceId ?? scriptObj.ScriptName ?? script.GetHashCode().ToString();
            }
            return script.GetHashCode().ToString();
        }

        private static bool MatchesPattern(string text, string pattern)
        {
            // Simple wildcard matching (* and ?)
            if (pattern == "*") return true;
            
            var regexPattern = "^" + System.Text.RegularExpressions.Regex.Escape(pattern)
                .Replace("\\*", ".*")
                .Replace("\\?", ".") + "$";
            
            return System.Text.RegularExpressions.Regex.IsMatch(text, regexPattern, 
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        }

        /// <summary>
        /// Get a program variable value by name
        /// </summary>
        public static string GetProgVar(string name)
        {
            if (_progVars.TryGetValue(name, out var value))
                return value;
            return "0"; // Match Pascal TCmdParam.Create default of '0'
        }

        /// <summary>
        /// Set a program variable value by name
        /// </summary>
        public static void SetProgVar(string name, string value)
        {
            _progVars[name] = value;
        }

        /// <summary>
        /// Load a game's persisted variables into the current-game cache.
        /// Call this when a game starts so loadvar can retrieve previously saved values.
        /// </summary>
        public static void LoadVarsForGame(Dictionary<string, string> variables)
        {
            _currentGameVars.Clear();
            foreach (var kvp in variables)
                _currentGameVars[kvp.Key] = kvp.Value;
        }

        /// <summary>
        /// Inject or override a value in the current game's in-memory loadvar cache
        /// without persisting it to disk.
        /// </summary>
        public static void SetCurrentGameVar(string name, string value)
        {
            if (string.IsNullOrWhiteSpace(name))
                return;

            _currentGameVars[name] = value;
        }

        /// <summary>
        /// Remove the in-memory variable cache for a single script.
        /// Call this whenever a script is stopped or killed so its vars don't
        /// bleed across into a fresh run of the same (or another) script.
        /// </summary>
        public static void ClearVarsForScript(string scriptId)
        {
            if (!string.IsNullOrEmpty(scriptId))
            {
                bool existed = _scriptVars.TryGetValue(scriptId, out var vars);
                int count = existed ? vars!.Count : 0;
                _scriptVars.Remove(scriptId);
                GlobalModules.DebugLog($"[VARCACHE] ClearVarsForScript scriptId='{scriptId}' existed={existed} count={count}\n");
            }
        }

        /// <summary>
        /// Remove the in-memory variable cache for all scripts.
        /// Call this when the proxy itself is stopped so no stale vars survive
        /// into the next session.
        /// </summary>
        public static void ClearAllScriptVars()
        {
            _scriptVars.Clear();
        }

        #endregion

        #region Persistence File Management

        /// <summary>
        /// Set the directory path where variable files are stored
        /// </summary>
        public static void SetPersistencePath(string path)
        {
            _variablePersistencePath = Path.Combine(path, "variables.json");
            _globalVarsPath = Path.Combine(path, "globals.json");
            _progVarsPath = Path.Combine(path, "progvars.json");
            _credentialsPath = Path.Combine(path, "credentials.json");
        }

        /// <summary>
        /// Track a login credential (username, password, or game letter) and persist to disk.
        /// </summary>
        public static void TrackCredential(string name, string value)
        {
            _credentials[name] = value;
            SaveCredentialsFile();
        }

        /// <summary>
        /// Get a tracked login credential value.
        /// </summary>
        public static string GetCredential(string name)
        {
            return _credentials.TryGetValue(name, out var val) ? val : string.Empty;
        }

        /// <summary>
        /// Save credentials to disk.
        /// </summary>
        public static void SaveCredentialsFile()
        {
            try
            {
                var dir = Path.GetDirectoryName(_credentialsPath);
                if (!string.IsNullOrEmpty(dir))
                    Directory.CreateDirectory(dir);
                var json = JsonSerializer.Serialize(_credentials, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_credentialsPath, json);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[VarPersistence] Error saving credentials: {ex.Message}");
            }
        }

        /// <summary>
        /// Load credentials from disk.
        /// </summary>
        public static void LoadCredentialsFile()
        {
            try
            {
                if (File.Exists(_credentialsPath))
                {
                    var json = File.ReadAllText(_credentialsPath);
                    var loaded = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
                    if (loaded != null)
                    {
                        _credentials.Clear();
                        foreach (var kvp in loaded)
                            _credentials[kvp.Key] = kvp.Value;
                        Console.WriteLine($"[VarPersistence] Loaded {_credentials.Count} credentials from {_credentialsPath}");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[VarPersistence] Error loading credentials: {ex.Message}");
            }
        }

        /// <summary>
        /// Load all persisted variables from disk
        /// </summary>
        public static void LoadPersistedVariables()
        {
            try
            {
                // Load script variables
                if (File.Exists(_variablePersistencePath))
                {
                    var json = File.ReadAllText(_variablePersistencePath);
                    var loaded = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, string>>>(json);
                    if (loaded != null)
                    {
                        _scriptVars.Clear();
                        foreach (var kvp in loaded)
                        {
                            _scriptVars[kvp.Key] = kvp.Value;
                        }
                    }
                }

                // Load global variables
                if (File.Exists(_globalVarsPath))
                {
                    var json = File.ReadAllText(_globalVarsPath);
                    var loaded = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
                    if (loaded != null)
                    {
                        _globalVars.Clear();
                        foreach (var kvp in loaded)
                        {
                            _globalVars[kvp.Key] = kvp.Value;
                        }
                    }
                }

                // Load program variables
                if (File.Exists(_progVarsPath))
                {
                    var json = File.ReadAllText(_progVarsPath);
                    var loaded = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
                    if (loaded != null)
                    {
                        _progVars.Clear();
                        foreach (var kvp in loaded)
                        {
                            _progVars[kvp.Key] = kvp.Value;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[VarPersistence] Error loading variables: {ex.Message}");
            }
        }

        /// <summary>
        /// Save all persisted variables to disk
        /// </summary>
        public static void SavePersistedVariables()
        {
            try
            {
                var options = new JsonSerializerOptions { WriteIndented = true };

                // Save script variables
                var scriptJson = JsonSerializer.Serialize(_scriptVars, options);
                File.WriteAllText(_variablePersistencePath, scriptJson);

                // Save global variables
                var globalJson = JsonSerializer.Serialize(_globalVars, options);
                File.WriteAllText(_globalVarsPath, globalJson);

                // Save program variables
                var progJson = JsonSerializer.Serialize(_progVars, options);
                File.WriteAllText(_progVarsPath, progJson);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[VarPersistence] Error saving variables: {ex.Message}");
            }
        }

        #endregion
    }
}
