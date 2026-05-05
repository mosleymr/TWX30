/*
Copyright (C) 2005  Remco Mulder, 2026 Matt Mosley

This program is free software; you can redistribute it and/or modify
it under the terms of the GNU General Public License as published by
the Free Software Foundation; either version 2 of the License, or
(at your option) any later version.
*/

using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace TWXProxy.Core
{
    public partial class ScriptRef
    {
        // Reference to the active game instance
        // TODO: This should be injected or accessed through a service locator
        private static GameInstance? _activeGameInstance;

        #region Network Command Implementation

        private static CmdAction CmdConnect_Impl(object script, CmdParam[] parameters)
        {
            // CMD: connect
            // Initiate connection to the game server
            GlobalModules.DebugLog($"[CONNECT] called, gameInstance={((_activeGameInstance == null) ? "NULL" : "set")}, isConnected={_activeGameInstance?.IsConnected}\n");
            
            if (_activeGameInstance == null)
            {
                GlobalModules.DebugLog($"[CONNECT] ERROR: No active game instance\n");
                Console.WriteLine("[Script] CONNECT: No active game instance");
                return CmdAction.None;
            }

            if (_activeGameInstance.IsConnected)
            {
                GlobalModules.DebugLog($"[CONNECT] Already connected, skipping\n");
                Console.WriteLine($"[Script] CONNECT: Already connected to server");
                return CmdAction.None;
            }

            GlobalModules.DebugLog($"[CONNECT] Firing async ConnectToServerAsync\n");
            GlobalModules.FlushDebugLog();
            try
            {
                // Run connection asynchronously - scripts don't wait for completion
                Task.Run(async () =>
                {
                    try
                    {
                        GlobalModules.DebugLog($"[CONNECT] ConnectToServerAsync starting\n");
                        await _activeGameInstance.ConnectToServerAsync();
                        GlobalModules.DebugLog($"[CONNECT] ConnectToServerAsync completed successfully\n");
                        GlobalModules.FlushDebugLog();
                    }
                    catch (Exception ex)
                    {
                        GlobalModules.DebugLog($"[CONNECT] FAILED: {ex.Message}\n");
                        GlobalModules.FlushDebugLog();
                        Console.WriteLine($"[Script] CONNECT failed: {ex.Message}");
                    }
                });
            }
            catch (Exception ex)
            {
                GlobalModules.DebugLog($"[CONNECT] OUTER ERROR: {ex.Message}\n");
                Console.WriteLine($"[Script] CONNECT error: {ex.Message}");
            }
            
            return CmdAction.None;
        }

        private static CmdAction CmdDisconnect_Impl(object script, CmdParam[] parameters)
        {
            // CMD: disconnect [disable]
            // Close connection to the game server
            // Optional parameter to disable auto-reconnect
            
            if (_activeGameInstance == null)
            {
                Console.WriteLine("[Script] DISCONNECT: No active game instance");
                return CmdAction.None;
            }

            if (!_activeGameInstance.IsConnected)
            {
                Console.WriteLine("[Script] DISCONNECT: Not connected to server");
                return CmdAction.None;
            }

            try
            {
                // The optional "disable" parameter disables auto-reconnect (TWX 2.x behavior)
                if (parameters.Length > 0 && parameters[0].Value.Equals("1", StringComparison.OrdinalIgnoreCase))
                    _activeGameInstance.AutoReconnect = false;

                // Disconnect from server only — do NOT call StopAsync() which would
                // shut down the entire proxy (listener, all tasks, local connection).
                GlobalModules.DebugLog($"[Script.DISCONNECT] DISCONNECT command executed!\n{System.Environment.StackTrace}\n");
                GlobalModules.FlushDebugLog();
                Task.Run(async () =>
                {
                    try
                    {
                        await _activeGameInstance.DisconnectFromServerAsync();
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[Script] DISCONNECT failed: {ex.Message}");
                    }
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Script] DISCONNECT error: {ex.Message}");
            }
            
            return CmdAction.None;
        }

        private static CmdAction CmdProcessIn_Impl(object script, CmdParam[] parameters)
        {
            // CMD: processin <processType> <text>
            // TWX27 semantics:
            //   processType=1 -> process globally for all scripts
            //   processType=0 -> process locally for the current script only
            // Older C# builds accidentally implemented the reversed shape
            // `processin <text> <force>`, so accept that form too for safety.
            string text;
            bool globalProcess;

            if (parameters.Length >= 2
                && TryConvertBoolean(parameters[0], out bool processType)
                && !TryConvertBoolean(parameters[1], out _))
            {
                globalProcess = processType;
                text = parameters[1].Value;
            }
            else
            {
                text = parameters[0].Value;
                globalProcess = parameters.Length > 1 && TryConvertBoolean(parameters[1], out bool legacyForce)
                    ? legacyForce
                    : false;
            }

            if (script is not Script currentScript)
            {
                Console.WriteLine("[Script] PROCESSIN: Script instance unavailable");
                return CmdAction.None;
            }

            try
            {
                if (globalProcess)
                {
                    currentScript.Controller.TextEvent(text, true);
                    currentScript.Controller.TextLineEvent(text, true);
                }
                else
                {
                    currentScript.TextEvent(text, true);
                    currentScript.TextLineEvent(text, true);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Script] PROCESSIN error: {ex.Message}");
            }

            return CmdAction.None;
        }

        private static bool TryConvertBoolean(CmdParam parameter, out bool value)
        {
            try
            {
                ConvertToBoolean(parameter, out value);
                return true;
            }
            catch
            {
                value = false;
                return false;
            }
        }

        private static CmdAction CmdProcessOut_Impl(object script, CmdParam[] parameters)
        {
            // CMD: processout <text>
            // Inject data into the outgoing client data stream
            
            if (_activeGameInstance == null)
            {
                Console.WriteLine("[Script] PROCESSOUT: No active game instance");
                return CmdAction.None;
            }

            if (!_activeGameInstance.IsConnected)
            {
                Console.WriteLine("[Script] PROCESSOUT: Not connected to server");
                return CmdAction.None;
            }

            string text = parameters[0].Value;

            try
            {
                // Add CRLF if not present
                if (!text.EndsWith("\r\n") && !text.EndsWith("\n"))
                {
                    text += "\r\n";
                }

                // Send to server as if local client typed it
                var data = Encoding.ASCII.GetBytes(text);
                
                Task.Run(async () =>
                {
                    try
                    {
                        await _activeGameInstance.SendToServerAsync(data);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[Script] PROCESSOUT failed: {ex.Message}");
                    }
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Script] PROCESSOUT error: {ex.Message}");
            }
            
            return CmdAction.None;
        }

        private static CmdAction CmdAutoHaggle_Impl(object script, CmdParam[] parameters)
        {
            if (_activeGameInstance == null)
            {
                Console.WriteLine("[Script] AUTOHAGGLE: No active game instance");
                return CmdAction.None;
            }

            string mode = parameters.Length > 0 ? parameters[0].Value.Trim() : string.Empty;
            if (string.IsNullOrWhiteSpace(mode))
            {
                Console.WriteLine("[Script] AUTOHAGGLE: Expected 'on' or 'off'");
                return CmdAction.None;
            }

            bool? enabled = mode.ToLowerInvariant() switch
            {
                "on" => true,
                "1" => true,
                "true" => true,
                "yes" => true,
                "off" => false,
                "0" => false,
                "false" => false,
                "no" => false,
                _ => null
            };

            if (enabled == null)
            {
                Console.WriteLine($"[Script] AUTOHAGGLE: Unknown mode '{mode}'");
                return CmdAction.None;
            }

            _activeGameInstance.SetNativeHaggleEnabled(enabled.Value, NativeHaggleChangeSource.Script);
            GlobalModules.DebugLog($"[AUTOHAGGLE] Script set native haggle {(enabled.Value ? "ON" : "OFF")}\n");
            return CmdAction.None;
        }

        private static CmdAction CmdQuikStats_Impl(object script, CmdParam[] parameters)
        {
            if (_activeGameInstance == null)
            {
                Console.WriteLine("[Script] QUIKSTATS: No active game instance");
                return CmdAction.None;
            }

            if (!_activeGameInstance.IsConnected)
            {
                Console.WriteLine("[Script] QUIKSTATS: Not connected to server");
                return CmdAction.None;
            }

            using var updateReceived = new ManualResetEventSlim(false);
            Action<ShipStatus>? statusHandler = null;
            statusHandler = _ =>
            {
                updateReceived.Set();
            };
            static bool IsSlashTerminalLine(string line)
            {
                if (string.IsNullOrEmpty(line) || !line.Contains('\u00B3'))
                    return false;

                return (line.Contains("Aln ", StringComparison.Ordinal) && line.Contains("Exp ", StringComparison.Ordinal)) ||
                       line.Contains("Ship ", StringComparison.Ordinal) ||
                       (line.Contains("Exp ", StringComparison.Ordinal) && line.Contains("Corp ", StringComparison.Ordinal));
            }
            EventHandler<DataReceivedEventArgs>? serverHandler = null;
            var serverLineBuf = new StringBuilder();
            var serverAnsiLineBuf = new StringBuilder();
            bool serverScriptInAnsi = false;

            serverHandler = (_, e) =>
            {
                if (_activeGameInstance == null || updateReceived.IsSet)
                    return;

                string ansiChunk = AnsiCodes.PrepareScriptAnsiText(e.Text);
                string plainChunk = AnsiCodes.StripANSIStateful(ansiChunk, ref serverScriptInAnsi);

                if (ansiChunk.Length == 0 && plainChunk.Length == 0)
                    return;

                serverLineBuf.Append(plainChunk);
                serverAnsiLineBuf.Append(ansiChunk);

                string buffered = serverLineBuf.ToString();
                string bufferedAnsi = serverAnsiLineBuf.ToString();
                int searchPos = 0;
                int lastProcessedPos = 0;
                int ansiSearchPos = 0;
                int lastAnsiProcessedPos = 0;

                while (searchPos < buffered.Length)
                {
                    int crPos = buffered.IndexOf('\r', searchPos);
                    if (crPos == -1)
                    {
                        string remainder = buffered[lastProcessedPos..];
                        string remainderAnsi = bufferedAnsi[lastAnsiProcessedPos..];

                        serverLineBuf.Clear();
                        serverLineBuf.Append(remainder);
                        serverAnsiLineBuf.Clear();
                        serverAnsiLineBuf.Append(remainderAnsi);

                        if (!string.IsNullOrWhiteSpace(remainder))
                        {
                            string strippedRemainder = AnsiCodes.NormalizeTerminalText(remainder.TrimEnd('\r'));
                            if (!string.IsNullOrWhiteSpace(strippedRemainder))
                            {
                                _activeGameInstance.FeedShipStatusLine(strippedRemainder);
                                if (IsSlashTerminalLine(strippedRemainder))
                                    updateReceived.Set();
                            }
                        }

                        return;
                    }

                    int ansiCrPos = bufferedAnsi.IndexOf('\r', ansiSearchPos);
                    if (ansiCrPos == -1)
                        break;

                    string lineForScript = buffered[lastProcessedPos..crPos];
                    string lineStripped = AnsiCodes.NormalizeTerminalText(lineForScript);
                    if (!string.IsNullOrWhiteSpace(lineStripped))
                    {
                        _activeGameInstance.FeedShipStatusLine(lineStripped);
                        if (IsSlashTerminalLine(lineStripped))
                            updateReceived.Set();
                    }

                    searchPos = crPos + 1;
                    lastProcessedPos = searchPos;
                    ansiSearchPos = ansiCrPos + 1;
                    lastAnsiProcessedPos = ansiSearchPos;
                }

                if (lastProcessedPos >= buffered.Length)
                {
                    serverLineBuf.Clear();
                    serverAnsiLineBuf.Clear();
                }
            };

            try
            {
                _activeGameInstance.ShipStatusUpdated += statusHandler;
                _activeGameInstance.ServerDataReceived += serverHandler;
                _activeGameInstance.SendToServerAsync(Encoding.ASCII.GetBytes("/")).GetAwaiter().GetResult();

                if (!updateReceived.Wait(3000))
                    GlobalModules.DebugLog("[QUIKSTATS] Timed out waiting for ship status refresh after '/'\n");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Script] QUIKSTATS error: {ex.Message}");
            }
            finally
            {
                if (statusHandler != null)
                    _activeGameInstance.ShipStatusUpdated -= statusHandler;
                if (serverHandler != null)
                    _activeGameInstance.ServerDataReceived -= serverHandler;
            }

            return CmdAction.None;
        }

        #endregion

        #region Network Access Helper

        /// <summary>
        /// Set the active game instance for script commands
        /// This should be called when a game instance is started
        /// </summary>
        public static void SetActiveGameInstance(GameInstance? gameInstance)
        {
            _activeGameInstance = gameInstance;
        }

        #endregion
    }
}
