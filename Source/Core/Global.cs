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
            : AppContext.BaseDirectory;

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
        public static bool PortHaggleDebugMode { get; set; } = false;
        public static bool PlanetHaggleDebugMode { get; set; } = false;

        /// <summary>
        /// When true, logs comparison operation results (ISEQUAL, ISGREATER, etc.) with both
        /// operand values and the result. Useful for tracing conditional logic such as the
        /// haggle derive range-check inner loop. Toggle from a script with: diagmode on/off
        /// </summary>
        public static bool DiagnoseMode { get; set; } = false;

        /// <summary>
        /// Enables lightweight VM timing/counter summaries for script load and execute paths.
        /// These summaries are written through the shared log path even when normal debug logging is off.
        /// </summary>
        public static bool EnableVmMetrics { get; set; } = false;

        /// <summary>
        /// When true, newly created Script instances prefer the prepared VM path.
        /// Defaults to false so existing runtime behavior is unchanged unless explicitly enabled.
        /// </summary>
        public static bool PreferPreparedVm { get; set; } = false;

        /// <summary>
        /// Enables the conservative in-memory source compile cache for .ts loads.
        /// Cache keys are validated against the full discovered include/dependency set.
        /// </summary>
        public static bool EnableSourceScriptCache { get; set; } = true;

        public static string DebugLogPath { get; set; } = "/tmp/twxp_debug.log";
        public static string PortHaggleDebugLogPath { get; set; } = "/tmp/twxp_haggle_debug.log";
        public static string PlanetHaggleDebugLogPath { get; set; } = "/tmp/twxp_neg_debug.log";
        private static readonly object _debugLock = new object();
        private static StreamWriter? _debugWriter = null;
        private static StreamWriter? _portHaggleWriter = null;
        private static StreamWriter? _planetHaggleWriter = null;

        private static bool LogWriterEnabled => DebugMode || EnableVmMetrics;

        private static void EnsureLogWriter(bool resetFile)
        {
            if (!LogWriterEnabled)
                return;

            string? directory = Path.GetDirectoryName(DebugLogPath);
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);

            if (_debugWriter != null && !resetFile)
                return;

            _debugWriter?.Dispose();
            _debugWriter = new StreamWriter(DebugLogPath, append: !resetFile, System.Text.Encoding.UTF8, bufferSize: 4096)
            {
                AutoFlush = true
            };

            if (resetFile)
            {
                _debugWriter.WriteLine($"=== TWX Proxy Debug Log Started {DateTime.Now:yyyy-MM-dd HH:mm:ss} ===");
                _debugWriter.Flush();
            }
        }

        private static void WriteLogMessage(string message)
        {
            lock (_debugLock)
            {
                EnsureLogWriter(resetFile: false);
                _debugWriter?.Write($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {message}");
            }
        }

        private static StreamWriter? CreateAppendWriter(string path, string header)
        {
            string? directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);

            bool fileExists = File.Exists(path);
            bool writeHeader = !fileExists || new FileInfo(path).Length == 0;
            var writer = new StreamWriter(path, append: true, System.Text.Encoding.UTF8, bufferSize: 4096)
            {
                AutoFlush = true
            };

            if (writeHeader)
                writer.WriteLine(header);
            else
                writer.WriteLine($"=== {header.Trim('=',' ')} {DateTime.Now:yyyy-MM-dd HH:mm:ss} ===");

            writer.Flush();
            return writer;
        }

        private static void EnsureTradeDebugWriters()
        {
            if (PortHaggleDebugMode && _portHaggleWriter == null)
            {
                _portHaggleWriter = CreateAppendWriter(
                    PortHaggleDebugLogPath,
                    $"=== Port Haggle Debug Log Started {DateTime.Now:yyyy-MM-dd HH:mm:ss} ===");
            }

            if (PlanetHaggleDebugMode && _planetHaggleWriter == null)
            {
                _planetHaggleWriter = CreateAppendWriter(
                    PlanetHaggleDebugLogPath,
                    $"=== Planet Haggle Debug Log Started {DateTime.Now:yyyy-MM-dd HH:mm:ss} ===");
            }
        }

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

                if (!LogWriterEnabled)
                    return;

                try
                {
                    EnsureLogWriter(resetFile: true);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[DEBUG LOG INIT ERROR] {ex.Message}");
                }
            }
        }

        public static void ConfigureHaggleDebugLogging(
            string? portHaggleDebugLogPath,
            bool portEnabled,
            string? planetHaggleDebugLogPath,
            bool planetEnabled)
        {
            lock (_debugLock)
            {
                if (!string.IsNullOrWhiteSpace(portHaggleDebugLogPath))
                    PortHaggleDebugLogPath = portHaggleDebugLogPath;
                if (!string.IsNullOrWhiteSpace(planetHaggleDebugLogPath))
                    PlanetHaggleDebugLogPath = planetHaggleDebugLogPath;

                bool portChanged = PortHaggleDebugMode != portEnabled;
                bool planetChanged = PlanetHaggleDebugMode != planetEnabled;
                PortHaggleDebugMode = portEnabled;
                PlanetHaggleDebugMode = planetEnabled;

                if (!PortHaggleDebugMode || portChanged)
                {
                    _portHaggleWriter?.Dispose();
                    _portHaggleWriter = null;
                }

                if (!PlanetHaggleDebugMode || planetChanged)
                {
                    _planetHaggleWriter?.Dispose();
                    _planetHaggleWriter = null;
                }

                try
                {
                    EnsureTradeDebugWriters();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[HAGGLE LOG INIT ERROR] {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Initialize/clear the debug log file. Call this at application startup.
        /// </summary>
        public static void InitializeDebugLog()
        {
            ConfigureDebugLogging(DebugLogPath, DebugMode, VerboseDebugMode);
            ConfigureHaggleDebugLogging(PortHaggleDebugLogPath, PortHaggleDebugMode, PlanetHaggleDebugLogPath, PlanetHaggleDebugMode);
        }

        /// <summary>
        /// Flush any buffered debug log entries to disk. Call after pauses / trigger events.
        /// </summary>
        public static void FlushDebugLog()
        {
            if (!LogWriterEnabled && !PortHaggleDebugMode && !PlanetHaggleDebugMode) return;
            try
            {
                lock (_debugLock)
                {
                    _debugWriter?.Flush();
                    _portHaggleWriter?.Flush();
                    _planetHaggleWriter?.Flush();
                }
            }
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
                WriteLogMessage(message);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[DEBUG LOG ERROR] {ex.Message}: {message}");
            }
        }

        /// <summary>
        /// Write VM-specific metric output to the shared log path when VM metrics are enabled,
        /// without requiring full debug logging.
        /// </summary>
        public static void VmMetricLog(string message)
        {
            if (!EnableVmMetrics) return;

            try
            {
                WriteLogMessage(message);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[VM METRIC LOG ERROR] {ex.Message}: {message}");
            }
        }

        public static void PortHaggleDebug(string message)
        {
            if (!PortHaggleDebugMode) return;

            try
            {
                lock (_debugLock)
                {
                    EnsureTradeDebugWriters();
                    _portHaggleWriter?.Write($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {message}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[PORT HAGGLE LOG ERROR] {ex.Message}: {message}");
            }
        }

        public static void PlanetHaggleDebug(string message)
        {
            if (!PlanetHaggleDebugMode) return;

            try
            {
                lock (_debugLock)
                {
                    EnsureTradeDebugWriters();
                    _planetHaggleWriter?.Write($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {message}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[PLANET HAGGLE LOG ERROR] {ex.Message}: {message}");
            }
        }
    }
}
