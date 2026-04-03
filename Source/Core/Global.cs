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

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace TWXProxy.Core
{
    public class TimerItem
    {
        private string _name;
        private long _startTime;

        public TimerItem(string name)
        {
            _name = name;
            _startTime = Stopwatch.GetTimestamp();
        }

        public string Name
        {
            get { return _name; }
            set { _name = value; }
        }

        public long StartTime
        {
            get { return _startTime; }
            set { _startTime = value; }
        }
    }

    public class GlobalVarItem : IDisposable
    {
        private string _name;
        private string _value;
        private List<string>? _array;
        private int _arrayCount;

        public GlobalVarItem(string name, string value)
        {
            _name = name;
            _value = value;
            _arrayCount = 0;
        }

        public GlobalVarItem(string name, List<string> data)
        {
            _name = name;
            _value = string.Empty;
            _array = data;
            _arrayCount = data.Count;
        }

        public string Name
        {
            get { return _name; }
            set { _name = value; }
        }

        public string Value
        {
            get { return _value; }
            set { _value = value; }
        }

        public List<string>? Data
        {
            get { return _array; }
            set { _array = value; }
        }

        public int ArrayCount
        {
            get { return _arrayCount; }
            set { _arrayCount = value; }
        }

        public void Dispose()
        {
            _array?.Clear();
        }
    }

    // Global module instances - these will be initialized by the application
    public static class GlobalModules
    {
        // Module variables - forward declarations for modules
        // These would be properly typed once the module classes are converted
        public static ITWXMenu? TWXMenu { get; set; }
        public static ITWXDatabase? TWXDatabase { get; set; }
        public static ITWXDatabase? Database => TWXDatabase;
        public static IScriptWindowFactory ScriptWindowFactory { get; set; } = new ConsoleScriptWindowFactory();
        public static IPanelOverlayService? PanelOverlay { get; set; }
        public static object? TWXLog { get; set; }
        public static object? TWXExtractor { get; set; }
        public static object? TWXInterpreter { get; set; }
        public static ITWXServer? TWXServer { get; set; }
        public static ITWXServer? Server => TWXServer;
        public static object? TWXClient { get; set; }
        public static object? TWXBubble { get; set; }
        public static object? TWXGUI { get; set; }
        public static object? PersistenceManager { get; set; }
        public static string ProgramDir { get; set; } = OperatingSystem.IsWindows()
            ? WindowsInstallInfo.GetInstalledProgramDirOrDefault()
            : Environment.CurrentDirectory;

        /// <summary>Auto-recorder that parses game text and updates the sector database.</summary>
        public static AutoRecorder GlobalAutoRecorder { get; } = new AutoRecorder();

        public static List<GlobalVarItem> TWXGlobalVars { get; set; } = new List<GlobalVarItem>();
        public static List<TimerItem> TWXTimers { get; set; } = new List<TimerItem>();

        // Debug configuration
        public static bool DebugMode { get; set; } = true;

        /// <summary>
        /// When false (default), suppresses very high-frequency per-parameter
        /// evaluation logs ([PreEval], [PostEval], [EvaluateArrayIndexes]).
        /// Set to true only when diagnosing deep variable-evaluation bugs.
        /// </summary>
        public static bool VerboseDebugMode { get; set; } = false;

        /// <summary>
        /// When true, logs comparison operation results (ISEQUAL, ISGREATER, etc.) with both
        /// operand values and the result. Useful for tracing conditional logic such as the
        /// haggle derive range-check inner loop. Toggle from a script with: diagmode on/off
        /// </summary>
        public static bool DiagnoseMode { get; set; } = false;

        public static string DebugLogPath { get; set; } = "/tmp/twxp_debug.log";
        private static readonly object _debugLock = new object();
        private static StreamWriter? _debugWriter = null;

        public static void ConfigureDebugLogging(string? debugLogPath, bool enabled, bool verboseEnabled)
        {
            lock (_debugLock)
            {
                if (!string.IsNullOrWhiteSpace(debugLogPath))
                    DebugLogPath = debugLogPath;

                DebugMode = enabled;
                VerboseDebugMode = verboseEnabled;

                _debugWriter?.Dispose();
                _debugWriter = null;

                if (!DebugMode)
                    return;

                try
                {
                    string? directory = Path.GetDirectoryName(DebugLogPath);
                    if (!string.IsNullOrWhiteSpace(directory))
                        Directory.CreateDirectory(directory);

                    _debugWriter = new StreamWriter(DebugLogPath, append: false, System.Text.Encoding.UTF8, bufferSize: 4096)
                    {
                        AutoFlush = true
                    };
                    _debugWriter.WriteLine($"=== TWX Proxy Debug Log Started {DateTime.Now:yyyy-MM-dd HH:mm:ss} ===");
                    _debugWriter.Flush();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[DEBUG LOG INIT ERROR] {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Initialize/clear the debug log file. Call this at application startup.
        /// </summary>
        public static void InitializeDebugLog()
        {
            ConfigureDebugLogging(DebugLogPath, DebugMode, VerboseDebugMode);
        }

        /// <summary>
        /// Flush any buffered debug log entries to disk. Call after pauses / trigger events.
        /// </summary>
        public static void FlushDebugLog()
        {
            if (!DebugMode) return;
            try { lock (_debugLock) { _debugWriter?.Flush(); } }
            catch { /* ignore */ }
        }

        /// <summary>
        /// Write debug message to log file if DebugMode is enabled.
        /// High-frequency per-parameter evaluation messages are gated by VerboseDebugMode.
        /// </summary>
        public static void DebugLog(string message)
        {
            if (!DebugMode) return;

            try
            {
                lock (_debugLock)
                {
                    if (_debugWriter == null)
                    {
                        _debugWriter = new StreamWriter(DebugLogPath, append: true, System.Text.Encoding.UTF8, bufferSize: 4096)
                        {
                            AutoFlush = true
                        };
                    }
                    _debugWriter.Write($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {message}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[DEBUG LOG ERROR] {ex.Message}: {message}");
            }
        }
    }
}
