/*
Copyright (C) 2026  Matt Mosley

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
*/

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace TWXProxy.Core
{
    /// <summary>
    /// Represents a single game instance with server and local connections
    /// </summary>
    public class GameInstance : IDisposable, ITWXServer
    {
        private readonly string _gameName;
        private readonly string _serverAddress;
        private readonly int _serverPort;
        private readonly int _listenPort;
        private char _commandChar;
        private readonly ModInterpreter? _interpreter;
        
        // ITWXServer / IModServer properties
        public bool AllowLerkers { get; set; } = true;
        public bool AcceptExternal { get; set; } = true;
        public string ExternalAddress { get; set; } = string.Empty;
        public bool BroadCastMsgs { get; set; } = true;
        public bool LocalEcho { get; set; } = true;
        public int ClientCount => 1; // Single client for now
        public string ActiveBotName { get; set; } = string.Empty;
        
        private TcpClient? _serverClient;
        private TcpListener? _localListener;
        private TcpClient? _localClient;
        
        private NetworkStream? _serverStream;
        private Stream? _localStream;      // write-to-terminal (or full-duplex NetworkStream in TCP mode)
        private Stream? _localReadStream;  // non-null only in direct (embedded) mode — separate read stream
        private bool _directMode;          // true when ConnectDirectClient() was used instead of a TCP listener
        
        private CancellationTokenSource? _cancellationSource;
        private Task? _serverReadTask;
        private Task? _localReadTask;
        private Task? _acceptTask;
        
        private bool _isRunning;

        // True when there is a live "local client" — whether TCP or in-process pipe.
        private bool LocalIsConnected => _directMode ? (_localStream != null) : (_localClient?.Connected == true);
        private bool _inCommandMode;
        private readonly object _stateLock = new();
        private readonly MenuHandler _menuHandler;
        private readonly NativeHaggleEngine _nativeHaggle = new();
        private readonly SemaphoreSlim _nativeHaggleSendLock = new(1, 1);
        private readonly ModLog _log = new();
        
        // Telnet negotiation state
        private bool _telnetNegotiationComplete = false;
        private readonly List<byte> _clientBufferDuringNegotiation = new();
        private readonly object _negotiationLock = new();

        // Auto-reconnect: true by default, set to false by "disconnect disable"
        private bool _autoReconnect = true;
        private int _reconnectLoopRunning = 0; // Interlocked guard — only one loop at a time
        private int _disconnectHandling = 0;   // Interlocked guard — emit disconnect UI/event once
        public bool AutoReconnect { get => _autoReconnect; set => _autoReconnect = value; }

        /// <summary>
        /// When false, suppresses all Console.WriteLine diagnostic output.
        /// Set to false in embedded (direct) mode to keep the console clean.
        /// </summary>
        public bool Verbose { get; set; } = true;


        // Log: important events (connect/disconnect/errors) → always written to DebugLog.
        // LogVerbose: high-frequency traffic (byte counts) → Console only when Verbose=true.
        private void Log(string message) { GlobalModules.DebugLog(message + "\n"); if (Verbose) Console.WriteLine(message); }
        private void LogVerbose(string message) { if (Verbose) Console.WriteLine(message); }
        
        // Telnet protocol constants
        private const byte IAC = 255;  // Interpret As Command
        private const byte DONT = 254;
        private const byte DO = 253;
        private const byte WONT = 252;
        private const byte WILL = 251;
        private const byte SB = 250;   // Subnegotiation Begin
        private const byte SE = 240;   // Subnegotiation End
        
        // Events for script processing hooks
        public event EventHandler<DataReceivedEventArgs>? ServerDataReceived;
        public event EventHandler<DataReceivedEventArgs>? LocalDataReceived;
        public event EventHandler<CommandEventArgs>? CommandReceived;
        public event EventHandler? Connected;
        public event EventHandler<DisconnectEventArgs>? Disconnected;
        public event EventHandler? ClearInputBufferRequested;
        public event Action<bool>? NativeHaggleChanged;

        public string GameName => _gameName;
        public bool IsRunning => _isRunning;
        public bool IsConnected => _serverClient?.Connected ?? false;
        public char CommandChar => _commandChar;
        public bool IsProxyMenuActive => _menuHandler.IsActive;
        public bool NativeHaggleEnabled => _nativeHaggle.Enabled;
        public ModLog Logger => _log;
        public bool LogDataEnabled
        {
            get => _log.LogData;
            set => _log.LogData = value;
        }
        public bool LogAnsiEnabled
        {
            get => _log.LogANSI;
            set => _log.LogANSI = value;
        }

        public GameInstance(string gameName, string serverAddress, int serverPort, int listenPort, char commandChar = '$', ModInterpreter? interpreter = null, string? scriptDirectory = null)
        {
            _gameName = gameName;
            _serverAddress = serverAddress;
            _serverPort = serverPort;
            _interpreter = interpreter;
            
            // Register this instance as the global TWXServer for script access
            if (_interpreter != null)
            {
                GlobalModules.TWXServer = this;
                GlobalModules.TWXInterpreter = _interpreter;
                GlobalModules.DebugLog($"[GameInstance] Registered TWXServer and TWXInterpreter for game {_gameName}\n");
            }
            _listenPort = listenPort;
            _commandChar = commandChar;
            _menuHandler = new MenuHandler(this, interpreter, scriptDirectory);
            _nativeHaggle.SetEnabled(true);
            _nativeHaggle.EnabledChanged += enabled => NativeHaggleChanged?.Invoke(enabled);
            _log.ProgramDir = GlobalModules.ProgramDir;
            _log.SetLogIdentity(gameName);
            _log.SetPlaybackTargets(
                (payload, token) => SendPlaybackToLocalAsync(payload, token),
                message => SendMessageAsync(message).GetAwaiter().GetResult());
            GlobalModules.TWXLog = _log;
        }

        /// <summary>
        /// Start the game instance - connect to server and start listening
        /// </summary>
        public async Task StartAsync()
        {
            lock (_stateLock)
            {
                if (_isRunning)
                {
                    throw new InvalidOperationException($"Game instance {_gameName} is already running");
                }
                _isRunning = true;
            }

            try
            {
                _cancellationSource = new CancellationTokenSource();
                var token = _cancellationSource.Token;

                // Start listening for local connections on all interfaces
                _localListener = new TcpListener(IPAddress.Any, _listenPort);
                _localListener.Start();
                Log($"[{_gameName}] Listening on 0.0.0.0:{_listenPort}");
                Log($"[{_gameName}] Type $c to connect to server");

                // Start accepting local connections
                _acceptTask = Task.Run(async () => await AcceptLocalConnectionsAsync(token), token);

                // NOTE: Server connection is now manual - triggered by $c command
            }
            catch (Exception ex)
            {
                Log($"[{_gameName}] Error starting: {ex.Message}");
                await StopAsync();
                throw;
            }
        }

        /// <summary>
        /// Stop the game instance and close all connections
        /// </summary>
        public async Task StopAsync()
        {
            GlobalModules.DebugLog($"[Network] StopAsync called for {_gameName}\n{System.Environment.StackTrace}\n");
            GlobalModules.FlushDebugLog();
            lock (_stateLock)
            {
                if (!_isRunning)
                    return;
                _isRunning = false;
            }

            try
            {
                _cancellationSource?.Cancel();

                // Wait for tasks to complete
                var tasks = new List<Task>();
                if (_serverReadTask != null) tasks.Add(_serverReadTask);
                if (_localReadTask != null) tasks.Add(_localReadTask);
                if (_acceptTask != null) tasks.Add(_acceptTask);

                await Task.WhenAll(tasks.Where(t => t != null));
            }
            catch (OperationCanceledException)
            {
                // Expected when cancelling
            }
            catch (Exception ex)
            {
                Log($"[{_gameName}] Error stopping: {ex.Message}");
            }
            finally
            {
                // Stop all scripts (including bots/system scripts) before closing connections.
                // This disposes every script's triggers and timers so they don't fire against
                // a dead connection and don't leak into the next session.
                _interpreter?.StopAll(true);

                // Wipe the entire in-memory script-var cache so no savevar values
                // survive into the next proxy session.
                ScriptRef.ClearAllScriptVars();

                CloseConnections();
                Log($"[{_gameName}] Stopped");
            }
        }

        /// <summary>
        /// Attach MTC's own terminal streams as the "local client" without opening a TCP listener.
        /// <paramref name="toTerminal"/> is where GameInstance writes game output (MTC reads from the other end).
        /// <paramref name="fromTerminal"/> is where MTC writes keystrokes (GameInstance reads from it).
        /// Call this before <see cref="ConnectToServerAsync"/>.
        /// </summary>
        public void ConnectDirectClient(Stream toTerminal, Stream fromTerminal)
        {
            _localStream     = toTerminal;
            _localReadStream = fromTerminal;
            _directMode      = true;

            _cancellationSource ??= new CancellationTokenSource();
            lock (_stateLock) { _isRunning = true; }

            var token = _cancellationSource.Token;
            _localReadTask = Task.Run(() => ReadFromLocalAsync(token), token);
        }

        /// <summary>
        /// Manually connect to the game server
        /// </summary>
        public async Task ConnectToServerAsync()
        {
            if (_serverClient?.Connected == true)
            {
                Log($"[{_gameName}] Already connected to server");
                return;
            }

            if (_cancellationSource == null)
            {
                throw new InvalidOperationException("Game instance is not running");
            }

            var token = _cancellationSource.Token;

            try
            {
                _serverClient = new TcpClient();
                await _serverClient.ConnectAsync(_serverAddress, _serverPort, token);
                _serverStream = _serverClient.GetStream();

                Log($"[{_gameName}] Connected to {_serverAddress}:{_serverPort}");
                
                // Reset telnet negotiation state
                lock (_negotiationLock)
                {
                    _telnetNegotiationComplete = false;
                    _clientBufferDuringNegotiation.Clear();
                }

                System.Threading.Interlocked.Exchange(ref _disconnectHandling, 0);
                
                Connected?.Invoke(this, EventArgs.Empty);

                // Start reading from server
                _serverReadTask = Task.Run(async () => await ReadFromServerAsync(token), token);
            }
            catch (Exception ex)
            {
                Log($"[{_gameName}] Failed to connect to server: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Manually disconnect from the game server
        /// </summary>
        public async Task DisconnectFromServerAsync()
        {
            GlobalModules.DebugLog($"[Network] DisconnectFromServerAsync called for {_gameName}\n");
            GlobalModules.FlushDebugLog();
            if (_serverClient?.Connected != true)
            {
                Log($"[{_gameName}] Not connected to server");
                return;
            }

            try
            {
                if (System.Threading.Interlocked.CompareExchange(ref _disconnectHandling, 1, 0) != 0)
                {
                    Log($"[{_gameName}] Disconnect already in progress");
                    return;
                }

                Log($"[{_gameName}] Disconnecting from server");
                
                // Close the server connection
                _serverStream?.Close();
                _serverClient?.Close();
                _serverStream = null;
                _serverClient = null;
                
                // Reset telnet negotiation state
                lock (_negotiationLock)
                {
                    _telnetNegotiationComplete = false;
                    _clientBufferDuringNegotiation.Clear();
                }
                
                // Send disconnect message to client
                await SendToLocalAsync(Encoding.ASCII.GetBytes($"\r\n[twxp] Disconnected from server.  Type {_commandChar}c to reconnect.\r\n"));
                
                Disconnected?.Invoke(this, new DisconnectEventArgs("User requested disconnect"));
            }
            catch (Exception ex)
            {
                Log($"[{_gameName}] Error disconnecting: {ex.Message}");
                throw;
            }
        }

        private async Task AcceptLocalConnectionsAsync(CancellationToken token)
        {
            try
            {
                while (!token.IsCancellationRequested && _localListener != null)
                {
                    var client = await _localListener.AcceptTcpClientAsync(token);
                    
                    // If we already have a local client, close the old one
                    if (_localClient != null)
                    {
                        Log($"[{_gameName}] Replacing existing local connection");
                        _localClient.Close();
                    }

                    _localClient = client;
                    _localClient.NoDelay = true; // Disable Nagle algorithm for immediate transmission
                    _localStream = client.GetStream();
                    Log($"[{_gameName}] Local client connected");

                    // Send telnet WILL ECHO to tell client not to echo locally
                    // IAC (255) WILL (251) ECHO (1)
                    await _localStream.WriteAsync(new byte[] { 255, 251, 1 }, 0, 3, token);
                    await _localStream.FlushAsync(token);
                    Log($"[{_gameName}] Sent telnet WILL ECHO to client");

                    // Start reading from local client
                    _localReadTask = Task.Run(async () => await ReadFromLocalAsync(token), token);
                }
            }
            catch (OperationCanceledException)
            {
                // Expected during shutdown
            }
            catch (Exception ex)
            {
                Log($"[{_gameName}] Error accepting connections: {ex.Message}");
            }
        }

        private async Task ReadFromServerAsync(CancellationToken token)
        {
            var buffer = new byte[8192];
            
            try
            {
                while (!token.IsCancellationRequested && _serverStream != null)
                {
                    int bytesRead = await _serverStream.ReadAsync(buffer, 0, buffer.Length, token);
                    
                    if (bytesRead == 0)
                    {
                        Log($"[{_gameName}] Server disconnected");

                        if (System.Threading.Interlocked.CompareExchange(ref _disconnectHandling, 1, 0) != 0)
                            break;
                        
                        // Notify local client if connected
                        if (_localStream != null && LocalIsConnected)
                        {
                            try
                            {
                                string disconnectText = _autoReconnect && !token.IsCancellationRequested
                                    ? $"\r\n[twxp] Server disconnected.  Proxy auto-reconnecting...\r\n"
                                    : $"\r\n[twxp] Server disconnected.  Type {_commandChar}c to reconnect.\r\n";
                                var message = Encoding.ASCII.GetBytes(disconnectText);
                                await _localStream.WriteAsync(message, 0, message.Length, token);
                                await _localStream.FlushAsync(token);
                            }
                            catch (Exception ex)
                            {
                                Log($"[{_gameName}] Could not send disconnect message to client: {ex.Message}");
                            }
                        }
                        
                        // Clean up server connection
                        _serverStream?.Close();
                        _serverClient?.Close();
                        _serverStream = null;
                        _serverClient = null;
                        
                        // Reset telnet negotiation state
                        lock (_negotiationLock)
                        {
                            _telnetNegotiationComplete = false;
                            _clientBufferDuringNegotiation.Clear();
                        }
                        
                        Disconnected?.Invoke(this, new DisconnectEventArgs("Server closed connection"));
                        // Start auto-reconnect if allowed
                        if (_autoReconnect && !token.IsCancellationRequested)
                            _ = Task.Run(() => ReconnectLoopAsync(token));
                        break;
                    }

                    var data = new byte[bytesRead];
                    Array.Copy(buffer, data, bytesRead);

                    LogVerbose($"[{_gameName}] Server -> Local: {bytesRead} bytes");

                    // Process telnet protocol and get cleaned data
                    var (cleanData, telnetResponses) = ProcessTelnetFromServer(data);
                    
                    // Send telnet responses back to server if needed
                    if (telnetResponses.Count > 0 && _serverStream != null)
                    {
                        var responses = telnetResponses.ToArray();
                        await _serverStream.WriteAsync(responses, 0, responses.Length, token);
                        await _serverStream.FlushAsync(token);
                        LogVerbose($"[{_gameName}] -> Sent {responses.Length} bytes telnet negotiation to server");
                    }

                    // Check if telnet negotiation is complete (first clean data from server)
                    if (!_telnetNegotiationComplete && cleanData.Length > 0)
                    {
                        byte[]? bufferedData = null;
                        
                        lock (_negotiationLock)
                        {
                            _telnetNegotiationComplete = true;
                            Log($"[{_gameName}] Telnet negotiation complete");
                            
                            // Get any buffered client data that was waiting
                            if (_clientBufferDuringNegotiation.Count > 0)
                            {
                                LogVerbose($"[{_gameName}] Sending {_clientBufferDuringNegotiation.Count} buffered bytes from client");
                                bufferedData = _clientBufferDuringNegotiation.ToArray();
                                _clientBufferDuringNegotiation.Clear();
                            }
                        }
                        
                        // Send buffered data outside the lock
                        if (bufferedData != null && _serverStream != null)
                        {
                            await _serverStream.WriteAsync(bufferedData, 0, bufferedData.Length, token);
                            await _serverStream.FlushAsync(token);
                        }
                    }

                    // Raise event for script processing (with cleaned data)
                    if (cleanData.Length > 0)
                    {
                        ServerDataReceived?.Invoke(this, new DataReceivedEventArgs(cleanData));
                        _log.RecordServerData(cleanData);
                    }

                    // Pass through cleaned data to local client
                    if (cleanData.Length > 0 && _localStream != null && LocalIsConnected)
                    {
                        await _localStream.WriteAsync(cleanData, 0, cleanData.Length, token);
                        await _localStream.FlushAsync(token);
                        LogVerbose($"[{_gameName}] -> Forwarded {cleanData.Length} bytes to local client");
                    }
                    else if (cleanData.Length == 0)
                    {
                        Log($"[{_gameName}] -> Only telnet negotiation, nothing to forward");
                    }
                    else
                    {
                        Log($"[{_gameName}] -> No local client connected, data discarded");
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Expected during shutdown
            }
            catch (Exception ex)
            {
                Log($"[{_gameName}] Error reading from server: {ex.Message}");

                if (System.Threading.Interlocked.CompareExchange(ref _disconnectHandling, 1, 0) != 0)
                    return;
                
                // Clean up server connection
                _serverStream?.Close();
                _serverClient?.Close();
                _serverStream = null;
                _serverClient = null;
                
                // Reset telnet negotiation state
                lock (_negotiationLock)
                {
                    _telnetNegotiationComplete = false;
                    _clientBufferDuringNegotiation.Clear();
                }
                
                if (_localStream != null && LocalIsConnected)
                {
                    try
                    {
                        string disconnectText = _autoReconnect && !token.IsCancellationRequested
                            ? $"\r\n[twxp] Server disconnected.  Proxy auto-reconnecting...\r\n"
                            : $"\r\n[twxp] Server disconnected.  Type {_commandChar}c to reconnect.\r\n";
                        var message = Encoding.ASCII.GetBytes(disconnectText);
                        await _localStream.WriteAsync(message, 0, message.Length, token);
                        await _localStream.FlushAsync(token);
                    }
                    catch (Exception sendEx)
                    {
                        Log($"[{_gameName}] Could not send disconnect message to client: {sendEx.Message}");
                    }
                }

                Disconnected?.Invoke(this, new DisconnectEventArgs(ex.Message));
                // Start auto-reconnect if allowed
                if (_autoReconnect && !token.IsCancellationRequested)
                    _ = Task.Run(() => ReconnectLoopAsync(token));
            }
        }

        private async Task ReconnectLoopAsync(CancellationToken token)
        {
            const int reconnectDelay = 5000; // 5 seconds between attempts
            while (!token.IsCancellationRequested && _autoReconnect)
            {
                try { await Task.Delay(reconnectDelay, token); } catch (OperationCanceledException) { return; }
                if (token.IsCancellationRequested) return;
                if (_serverClient?.Connected == true) return; // already reconnected

                try
                {
                    Log($"[{_gameName}] Auto-reconnect attempt...");
                    GlobalModules.DebugLog($"[AutoReconnect] Connecting to {_serverAddress}:{_serverPort}\n");
                    await ConnectToServerAsync();
                    GlobalModules.DebugLog($"[AutoReconnect] Connected successfully\n");
                    GlobalModules.FlushDebugLog();
                    Log($"[{_gameName}] Auto-reconnect succeeded");
                    return;
                }
                catch (Exception ex)
                {
                    Log($"[{_gameName}] Auto-reconnect failed: {ex.Message}, retrying in {reconnectDelay / 1000}s...");
                    GlobalModules.DebugLog($"[AutoReconnect] Failed: {ex.Message}, retrying...\n");
                }
            }
            System.Threading.Interlocked.Exchange(ref _reconnectLoopRunning, 0);
        }

        /// <summary>
        /// Start the reconnect loop if not already running. Safe to call from any thread.
        /// </summary>
        public void StartReconnectIfNeeded()
        {
            if (!_autoReconnect || _serverClient?.Connected == true) return;
            if (_cancellationSource == null) return;
            // Only start if no loop is currently running
            if (System.Threading.Interlocked.CompareExchange(ref _reconnectLoopRunning, 1, 0) == 0)
            {
                GlobalModules.DebugLog($"[AutoReconnect] StartReconnectIfNeeded: launching reconnect loop\n");
                GlobalModules.FlushDebugLog();
                _ = Task.Run(() => ReconnectLoopAsync(_cancellationSource.Token));
            }
        }

        private async Task ReadFromLocalAsync(CancellationToken token)
        {
            var buffer = new byte[8192];
            var commandBuffer = new StringBuilder();

            try
            {
                // In direct mode _localReadStream is the read half; in TCP mode _localStream is full-duplex.
                var readStream = _localReadStream ?? _localStream;

                while (!token.IsCancellationRequested && readStream != null)
                {
                    int bytesRead = await readStream.ReadAsync(buffer, 0, buffer.Length, token);
                    
                    if (bytesRead == 0)
                    {
                        Log($"[{_gameName}] Local client disconnected");
                        _localClient?.Close();
                        _localClient = null;
                        _localStream = null;
                        _localReadStream = null;
                        break;
                    }

                    // Process each byte immediately for instant response
                    for (int i = 0; i < bytesRead; i++)
                    {
                        _log.NotifyUserInput();

                        byte b = buffer[i];
                        char c = (char)b;
                        
                        if (c == _commandChar)
                        {
                            // $ while in the proxy menu immediately exits the proxy menu.
                            if (_menuHandler.IsActive)
                            {
                                await _menuHandler.ExitMenuAsync();
                            }
                            else if (_inCommandMode)
                            {
                                // End command mode - execute command
                                var command = commandBuffer.ToString();
                                commandBuffer.Clear();
                                _inCommandMode = false;

                                Log($"[{_gameName}] Command: {command}");
                                await HandleCommandAsync(command);
                            }
                            else
                            {
                                // Enter menu mode with main menu
                                _inCommandMode = false;
                                await _menuHandler.HandleMenuCommandAsync(c);
                            }
                        }
                        else if (_inCommandMode)
                        {
                            // Old-style $COMMAND$ syntax - accumulate
                            commandBuffer.Append(c);
                        }
                        else
                        {
                            // Check if script is waiting for input FIRST (GETINPUT takes priority over menus)
                            bool scriptWaitingForInput = _interpreter?.IsAnyScriptWaitingForInput() ?? false;
                            bool handledByScriptMenu = false;
                            
                            // [LocalChar] logging removed — too high-frequency for the debug log.
                            
                            if (!scriptWaitingForInput)
                            {
                                // Check if a script menu is open (only if GETINPUT not active)
                                if (GlobalModules.TWXMenu is MenuManager menuMgr && menuMgr.IsMenuOpen())
                                {
                                    handledByScriptMenu = menuMgr.HandleMenuInput(c);
                                }
                            }
                            
                            if (!handledByScriptMenu)
                            {
                                // Try input collection (it handles skipping \n after \r even when InputMode is None)
                                bool handled = await _menuHandler.HandleInputCharAsync(c);
                                
                                if (!handled && _menuHandler.CurrentMenu != MenuState.None && !scriptWaitingForInput)
                                {
                                    // In menu mode - single character commands.
                                    // Skip when a script is waiting for input: those chars belong
                                    // to the script (getinput / getconsoleinput), not the proxy menu.
                                    await _menuHandler.HandleMenuCommandAsync(c);
                                }
                                else if (!handled)
                                {
                                    bool keypressMode = scriptWaitingForInput && (_interpreter?.HasKeypressInputWaiting ?? false);

                                    // Pascal ProcessOutBound equivalent: fire TextOutEvent BEFORE
                                    // deciding whether to forward to the server.  If a TextOut
                                    // trigger consumes the character (returns true) the character
                                    // must NOT be sent to the game — the script is handling it
                                    // (e.g. the bot's character-by-character command prompt).
                                    // Only do this when no GETINPUT is active; in that case the
                                    // char is raw input data destined for the waiting script.
                                    bool textOutConsumed = false;
                                    if (_interpreter != null && !scriptWaitingForInput)
                                        textOutConsumed = _interpreter.TextOutEvent(c.ToString(), null);

                                    // Send to server only when no trigger consumed the char
                                    // and no script is waiting for input.  When GETINPUT is
                                    // active the char belongs to the script buffer, not the server.
                                    bool sendToServer = !textOutConsumed && !scriptWaitingForInput;

                                    if (sendToServer && _serverStream != null && _serverClient?.Connected == true)
                                    {
                                        // Check if telnet negotiation is still in progress
                                        bool negotiationInProgress;
                                        lock (_negotiationLock)
                                        {
                                            negotiationInProgress = !_telnetNegotiationComplete;
                                        }
                                        
                                        if (negotiationInProgress)
                                        {
                                            // Buffer data during telnet negotiation
                                            lock (_negotiationLock)
                                            {
                                                _clientBufferDuringNegotiation.Add(b);
                                            }
                                            if (i == 0)
                                            {
                                                Log($"[{_gameName}] Buffering client data during telnet negotiation");
                                            }
                                        }
                                        else
                                        {
                                            // Send immediately after negotiation complete
                                            await _serverStream.WriteAsync(new byte[] { b }, 0, 1, token);
                                            await _serverStream.FlushAsync(token);
                                        }
                                    }
                                    else if (scriptWaitingForInput && !keypressMode)
                                    {
                                        // Full-line GETINPUT (blocking, non-keypress): echo chars locally
                                        // so the user can see what they type before pressing Enter.
                                        if (_localStream != null)
                                        {
                                            if (b == 8 || b == 127) // Backspace or DEL
                                            {
                                                // Echo backspace sequence: BS + SPACE + BS
                                                await _localStream.WriteAsync(new byte[] { 8, 32, 8 }, 0, 3, token);
                                                await _localStream.FlushAsync(token);
                                            }
                                            else if (b != 13 && b != 10) // Don't echo CR/LF yet
                                            {
                                                await _localStream.WriteAsync(new byte[] { b }, 0, 1, token);
                                                await _localStream.FlushAsync(token);
                                            }
                                            else if (b == 13) // CR - echo newline
                                            {
                                                await _localStream.WriteAsync(new byte[] { 13, 10 }, 0, 2, token);
                                                await _localStream.FlushAsync(token);
                                            }
                                        }
                                    }
                                
                                // Raise event for input buffering / keypress / line-assembly
                                // (ProxyService subscriber handles those cases).
                                // Do NOT raise if TextOut consumed the char — the TextOut trigger handler
                                // may have set _waitingForInput (e.g. getConsoleInput SINGLEKEY), and
                                // raising LocalDataReceived would incorrectly deliver the triggering
                                // character as the waiting script's input.
                                if (!textOutConsumed)
                                    LocalDataReceived?.Invoke(this, new DataReceivedEventArgs(new byte[] { b }));
                            }
                            }
                        }
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Expected during shutdown
            }
            catch (Exception ex)
            {
                Log($"[{_gameName}] Error reading from local: {ex.Message}");
            }
        }

        /// <summary>
        /// Process telnet protocol sequences from server
        /// Returns cleaned data (without telnet commands) and responses to send back
        /// </summary>
        private (byte[] cleanData, List<byte> responses) ProcessTelnetFromServer(byte[] data)
        {
            var cleanData = new List<byte>();
            var responses = new List<byte>();
            
            int i = 0;
            while (i < data.Length)
            {
                if (data[i] == IAC && i + 1 < data.Length)
                {
                    byte command = data[i + 1];
                    
                    if (command == IAC)
                    {
                        // Escaped IAC (255 255 means literal 255)
                        cleanData.Add(IAC);
                        i += 2;
                    }
                    else if (command == DO && i + 2 < data.Length)
                    {
                        // Server wants us to DO something
                        byte option = data[i + 2];
                        Log($"[{_gameName}] Telnet: Server DO {option}");
                        // Respond with WONT (we don't support options)
                        responses.Add(IAC);
                        responses.Add(WONT);
                        responses.Add(option);
                        i += 3;
                    }
                    else if (command == DONT && i + 2 < data.Length)
                    {
                        // Server wants us to NOT do something
                        byte option = data[i + 2];
                        Log($"[{_gameName}] Telnet: Server DONT {option}");
                        // Acknowledge with WONT
                        responses.Add(IAC);
                        responses.Add(WONT);
                        responses.Add(option);
                        i += 3;
                    }
                    else if (command == WILL && i + 2 < data.Length)
                    {
                        // Server will do something
                        byte option = data[i + 2];
                        Log($"[{_gameName}] Telnet: Server WILL {option}");
                        // Accept with DO (or reject with DONT if we don't want it)
                        responses.Add(IAC);
                        responses.Add(DO);
                        responses.Add(option);
                        i += 3;
                    }
                    else if (command == WONT && i + 2 < data.Length)
                    {
                        // Server won't do something
                        byte option = data[i + 2];
                        Log($"[{_gameName}] Telnet: Server WONT {option}");
                        // Acknowledge with DONT
                        responses.Add(IAC);
                        responses.Add(DONT);
                        responses.Add(option);
                        i += 3;
                    }
                    else if (command == SB)
                    {
                        // Subnegotiation - skip until SE
                        Log($"[{_gameName}] Telnet: Subnegotiation Begin");
                        i += 2;
                        while (i < data.Length - 1)
                        {
                            if (data[i] == IAC && data[i + 1] == SE)
                            {
                                Log($"[{_gameName}] Telnet: Subnegotiation End");
                                i += 2;
                                break;
                            }
                            i++;
                        }
                    }
                    else
                    {
                        // Unknown IAC command, skip it
                        Log($"[{_gameName}] Telnet: Unknown command {command}");
                        i += 2;
                    }
                }
                else
                {
                    // Regular data
                    cleanData.Add(data[i]);
                    i++;
                }
            }
            
            return (cleanData.ToArray(), responses);
        }

        /// <summary>
        /// Handle command execution
        /// </summary>
        private async Task HandleCommandAsync(string command)
        {
            // Fire event for external handling
            CommandReceived?.Invoke(this, new CommandEventArgs(command));

            // Handle built-in commands
            switch (command.ToLower())
            {
                case "c":
                case "connect":
                    // Check if already connected - if so, disconnect
                    if (_serverClient?.Connected == true)
                    {
                        await DisconnectFromServerAsync();
                    }
                    else
                    {
                        // Not connected, so connect
                        try
                        {
                            await SendToLocalAsync(Encoding.ASCII.GetBytes("\r\nConnecting to server...\r\n"));
                            await ConnectToServerAsync();
                            await SendToLocalAsync(Encoding.ASCII.GetBytes("Connected!\r\n"));
                        }
                        catch (Exception ex)
                        {
                            await SendToLocalAsync(Encoding.ASCII.GetBytes($"\r\nConnection failed: {ex.Message}\r\n"));
                        }
                    }
                    break;
            }
        }

        /// <summary>
        /// Send data to the server
        /// </summary>
        public async Task SendToServerAsync(byte[] data)
        {
            if (_serverStream != null && _serverClient?.Connected == true)
            {
                await _serverStream.WriteAsync(data, 0, data.Length);
                await _serverStream.FlushAsync();
            }
        }

        /// <summary>
        /// Send data to the local client
        /// </summary>
        public async Task SendToLocalAsync(byte[] data)
        {
            if (_localStream != null && LocalIsConnected)
            {
                await _localStream.WriteAsync(data, 0, data.Length);
                await _localStream.FlushAsync();
            }
            else
            {
                Log($"[SendToLocalAsync] Cannot send - localStream null: {_localStream == null}, localIsConnected: {LocalIsConnected}");
            }
        }

        private async Task SendPlaybackToLocalAsync(byte[] data, CancellationToken token)
        {
            if (_localStream == null || !LocalIsConnected)
                return;

            try
            {
                await _localStream.WriteAsync(data, 0, data.Length, token);
                await _localStream.FlushAsync(token);
            }
            catch (OperationCanceledException)
            {
                // Playback was cancelled.
            }
        }

        /// <summary>
        /// Send text to the local client
        /// </summary>
        public async Task SendMessageAsync(string message)
        {
            var data = Encoding.ASCII.GetBytes(message);
            await SendToLocalAsync(data);
        }

        public bool ToggleNativeHaggle()
        {
            return _nativeHaggle.Toggle();
        }

        public void SetNativeHaggleEnabled(bool enabled)
        {
            _nativeHaggle.SetEnabled(enabled);
        }

        public void SetCommandChar(char commandChar)
        {
            if (!char.IsControl(commandChar))
                _commandChar = commandChar;
        }

        public void ProcessNativeHaggleLine(string line)
        {
            if (string.IsNullOrWhiteSpace(line))
                return;

            string? response = _nativeHaggle.HandleLine(line);
            if (!string.IsNullOrEmpty(response))
                _ = SendNativeHaggleResponseAsync(response);
        }

        private async Task SendNativeHaggleResponseAsync(string response)
        {
            await _nativeHaggleSendLock.WaitAsync();
            try
            {
                if (_serverStream == null || _serverClient?.Connected != true)
                {
                    GlobalModules.DebugLog($"[NativeHaggle] Dropped response '{response}' because the server is not connected.\n");
                    return;
                }

                byte[] data = Encoding.ASCII.GetBytes(response + "\r");
                GlobalModules.DebugLog($"[NativeHaggle] SEND '{response}\\r'\n");
                await _serverStream.WriteAsync(data, 0, data.Length);
                await _serverStream.FlushAsync();
            }
            catch (Exception ex)
            {
                GlobalModules.DebugLog($"[NativeHaggle] SEND FAILED '{response}': {ex.Message}\n");
            }
            finally
            {
                _nativeHaggleSendLock.Release();
            }
        }

        /// <summary>
        /// Request input buffer to be cleared (for GETINPUT)
        /// </summary>
        public void ClearInputBuffer()
        {
            ClearInputBufferRequested?.Invoke(this, EventArgs.Empty);
        }

        private void CloseConnections()
        {
            _log.CloseLog();

            _localStream?.Close();
            _localReadStream?.Close();
            _serverStream?.Close();
            _localClient?.Close();
            _serverClient?.Close();
            _localListener?.Stop();

            _localStream = null;
            _localReadStream = null;
            _serverStream = null;
            _localClient = null;
            _serverClient = null;
            _localListener = null;
            _directMode = false;
        }

        #region ITWXServer Implementation

        public void Broadcast(string message)
        {
            // Send message to client (currently single client)
            SendMessageAsync(message).Wait();
        }

        public void ClientMessage(string message)
        {
            // Send message to client
            SendMessageAsync(message).Wait();
        }

        public void AddQuickText(string key, string value)
        {
            // Quick text not implemented yet
        }

        public void ClearQuickText(string? key = null)
        {
            // Quick text not implemented yet
        }

        public ClientType GetClientType(int index)
        {
            return ClientType.Standard; // Default for now
        }

        public void SetClientType(int index, ClientType type)
        {
            // Client type management not implemented yet
        }

        public void RegisterBot(string botName, string scriptFile, string description = "")
        {
            // Bot registration not implemented yet
        }

        public void UnregisterBot(string botName)
        {
            // Bot unregistration not implemented yet
        }

        public List<string> GetBotList()
        {
            return new List<string>(); // Empty for now
        }

        public BotConfig? GetBotConfig(string botName)
        {
            return null; // Not implemented yet
        }

        public object? GetActiveBot()
        {
            return null; // Not implemented yet
        }

        #endregion

        public void Dispose()
        {
            // Unregister from GlobalModules if we're the current TWXServer
            if (GlobalModules.TWXServer == this)
            {
                GlobalModules.TWXServer = null;
            }
            
            StopAsync().Wait();
            _cancellationSource?.Dispose();
            _nativeHaggleSendLock.Dispose();
            _log.Dispose();
        }
    }

    /// <summary>
    /// Event args for data received events
    /// </summary>
    public class DataReceivedEventArgs : EventArgs
    {
        public byte[] Data { get; }
        // Use Latin1 (ISO-8859-1) to preserve bytes 128-255 as char n.
        // Pascal TWX used 8-bit strings where byte 179 = char 179, so scripts
        // that use #179 (the ³ status-bar separator) must see the same value.
        public string Text => Encoding.Latin1.GetString(Data);

        public DataReceivedEventArgs(byte[] data)
        {
            Data = data;
        }
    }

    /// <summary>
    /// Event args for command events
    /// </summary>
    public class CommandEventArgs : EventArgs
    {
        public string Command { get; }

        public CommandEventArgs(string command)
        {
            Command = command;
        }
    }

    /// <summary>
    /// Event args for disconnect events
    /// </summary>
    public class DisconnectEventArgs : EventArgs
    {
        public string Reason { get; }

        public DisconnectEventArgs(string reason)
        {
            Reason = reason;
        }
    }

    /// <summary>
    /// Manages multiple game instances
    /// </summary>
    public class NetworkManager : IDisposable
    {
        private readonly ConcurrentDictionary<string, GameInstance> _gameInstances;
        private readonly object _managementLock = new();

        public NetworkManager()
        {
            _gameInstances = new ConcurrentDictionary<string, GameInstance>();
        }

        /// <summary>
        /// Create and start a game instance
        /// </summary>
        public async Task<GameInstance> StartGameAsync(string gameName, string serverAddress, int serverPort, int listenPort, char commandChar = '$', ModInterpreter? interpreter = null, string? scriptDirectory = null)
        {
            lock (_managementLock)
            {
                if (_gameInstances.ContainsKey(gameName))
                {
                    throw new InvalidOperationException($"Game instance {gameName} is already running");
                }
            }

            var instance = new GameInstance(gameName, serverAddress, serverPort, listenPort, commandChar, interpreter, scriptDirectory);
            
            if (_gameInstances.TryAdd(gameName, instance))
            {
                try
                {
                    await instance.StartAsync();
                    Console.WriteLine($"Started game instance: {gameName}");
                    return instance;
                }
                catch
                {
                    _gameInstances.TryRemove(gameName, out _);
                    throw;
                }
            }

            throw new InvalidOperationException($"Failed to add game instance {gameName}");
        }

        /// <summary>
        /// Stop a game instance
        /// </summary>
        public async Task StopGameAsync(string gameName)
        {
            if (_gameInstances.TryRemove(gameName, out var instance))
            {
                await instance.StopAsync();
                instance.Dispose();
                Console.WriteLine($"Stopped game instance: {gameName}");
            }
        }

        /// <summary>
        /// Get a game instance by name
        /// </summary>
        public GameInstance? GetGame(string gameName)
        {
            _gameInstances.TryGetValue(gameName, out var instance);
            return instance;
        }

        /// <summary>
        /// Get all running game instances
        /// </summary>
        public IEnumerable<GameInstance> GetAllGames()
        {
            return _gameInstances.Values;
        }

        /// <summary>
        /// Stop all game instances
        /// </summary>
        public async Task StopAllGamesAsync()
        {
            var tasks = _gameInstances.Values.Select(g => g.StopAsync()).ToList();
            await Task.WhenAll(tasks);
            
            foreach (var instance in _gameInstances.Values)
            {
                instance.Dispose();
            }
            
            _gameInstances.Clear();
            Console.WriteLine("Stopped all game instances");
        }

        public void Dispose()
        {
            StopAllGamesAsync().Wait();
        }
    }
}
