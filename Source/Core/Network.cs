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
        private sealed class DeferredLocalOutput
        {
            public byte[] Data { get; init; } = Array.Empty<byte>();
            public bool BroadcastDeaf { get; init; }
        }

        private sealed class ClientSession
        {
            public TcpClient? TcpClient { get; init; }
            public Stream WriteStream { get; init; } = Stream.Null;
            public Stream ReadStream { get; init; } = Stream.Null;
            public bool IsDirect { get; init; }
            public string RemoteAddress { get; set; } = string.Empty;
            public ClientType Type { get; set; } = ClientType.Standard;
            public bool EchoMarks { get; set; }
            public MenuHandler MenuHandler { get; init; } = null!;
            public Task? ReadTask { get; set; }
            public bool IsConnected => IsDirect ? WriteStream != Stream.Null : (TcpClient?.Connected ?? false);
        }

        private sealed class ClientContextScope : IDisposable
        {
            private readonly AsyncLocal<int?> _slot;
            private readonly int? _previous;

            public ClientContextScope(AsyncLocal<int?> slot, int? next)
            {
                _slot = slot;
                _previous = slot.Value;
                _slot.Value = next;
            }

            public void Dispose()
            {
                _slot.Value = _previous;
            }
        }

        private readonly string _gameName;
        private readonly string _serverAddress;
        private readonly int _serverPort;
        private readonly int _listenPort;
        private readonly string _scriptDirectory;
        private char _commandChar;
        private readonly ModInterpreter? _interpreter;
        private readonly List<(string Search, string Replace)> _systemQuickTexts = new();
        private readonly List<(string Search, string Replace)> _userQuickTexts = new();
        private readonly Dictionary<string, BotConfig> _botConfigs = new(StringComparer.OrdinalIgnoreCase);
        private readonly List<BotConfig> _botOrder = new();
        private readonly List<ClientSession> _clients = new();
        private readonly object _clientLock = new();
        private readonly AsyncLocal<int?> _preferredClientIndex = new();
        
        // ITWXServer / IModServer properties
        public bool StreamEnabled { get; set; }
        public bool AllowLerkers { get; set; } = true;
        public string LerkerAddress { get; set; } = string.Empty;
        public bool AcceptExternal { get; set; } = true;
        public string ExternalAddress { get; set; } = string.Empty;
        public bool BroadCastMsgs { get; set; } = true;
        public bool LocalEcho { get; set; } = true;
        public int ClientCount
        {
            get
            {
                lock (_clientLock)
                    return _clients.Count;
            }
        }
        public string ActiveBotName { get; set; } = string.Empty;
        public Func<BotConfig, string, bool>? NativeBotActivator { get; set; }
        public Func<string, bool>? NativeBotStopper { get; set; }
        public Func<string, string?>? NativeBotScriptRedirector { get; set; }
        
        private TcpClient? _serverClient;
        private TcpListener? _localListener;
        private NetworkStream? _serverStream;
        
        private CancellationTokenSource? _cancellationSource;
        private Task? _serverReadTask;
        private Task? _acceptTask;
        
        private bool _isRunning;
        private readonly object _stateLock = new();
        private readonly MenuHandler _directMenuHandler;
        private readonly NativeHaggleEngine _nativeHaggle = new();
        private readonly SemaphoreSlim _nativeHaggleSendLock = new(1, 1);
        private readonly ModLog _log = new();
        private readonly ShipInfoParser _shipInfoParser = new();
        private readonly object _shipStatusLock = new();
        private ShipStatus _currentShipStatus = new();
        private readonly object _deferredLocalOutputLock = new();
        private readonly List<DeferredLocalOutput> _deferredLocalOutput = new();
        private int _serverDataDispatchDepth;
        private int _suppressScriptPipeToggleMessageCount;
        private int _suppressScriptPipeTogglePromptCount;
        
        // Telnet negotiation state
        private bool _telnetNegotiationComplete = false;
        private readonly List<byte> _clientBufferDuringNegotiation = new();
        private readonly object _negotiationLock = new();

        // Auto-reconnect: true by default, set to false by "disconnect disable"
        private bool _autoReconnect = true;
        private int _reconnectDelayMs = 5000;
        private int _reconnectLoopRunning = 0; // Interlocked guard — only one loop at a time
        private int _disconnectHandling = 0;   // Interlocked guard — emit disconnect UI/event once
        public bool AutoReconnect { get => _autoReconnect; set => _autoReconnect = value; }
        public int ReconnectDelayMs
        {
            get => _reconnectDelayMs;
            set => _reconnectDelayMs = Math.Max(1000, value);
        }

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
        public event Action? NativeHaggleStatsChanged;
        public event Action<ShipStatus>? ShipStatusUpdated;

        public string GameName => _gameName;
        public bool IsRunning => _isRunning;
        public bool IsConnected => _serverClient?.Connected ?? false;
        public char CommandChar => _commandChar;
        public bool IsProxyMenuActive
        {
            get
            {
                lock (_clientLock)
                    return _clients.Any(client => client.MenuHandler.IsActive);
            }
        }
        public bool NativeHaggleEnabled => _nativeHaggle.Enabled;
        public string NativeHaggleMode => _nativeHaggle.FirstBidMode;
        public string NativePortHaggleMode => _nativeHaggle.PortHaggleMode;
        public string NativePlanetHaggleMode => _nativeHaggle.PlanetHaggleMode;
        public IReadOnlyList<NativeHaggleModeInfo> NativeHaggleModes => _nativeHaggle.AvailableModes;
        public IReadOnlyList<NativeHaggleModeInfo> NativePortHaggleModes => _nativeHaggle.AvailablePortModes;
        public IReadOnlyList<NativeHaggleModeInfo> NativePlanetHaggleModes => _nativeHaggle.AvailablePlanetModes;
        public int NativeHaggleCompletedCount => _nativeHaggle.CompletedHaggles;
        public int NativeHaggleSuccessfulCount => _nativeHaggle.SuccessfulHaggles;
        public int NativeHaggleGoodCount => _nativeHaggle.GoodRewardCount;
        public int NativeHaggleGreatCount => _nativeHaggle.GreatRewardCount;
        public int NativeHaggleExcellentCount => _nativeHaggle.ExcellentRewardCount;
        public int NativeHaggleSuccessRatePercent => _nativeHaggle.SuccessRatePercent;
        public ModLog Logger => _log;
        public ProxyHistoryBuffer History { get; } = new();
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

        public ShipStatus CurrentShipStatus
        {
            get
            {
                lock (_shipStatusLock)
                    return CloneShipStatus(_currentShipStatus);
            }
        }

        public GameInstance(string gameName, string serverAddress, int serverPort, int listenPort, char commandChar = '$', ModInterpreter? interpreter = null, string? scriptDirectory = null)
        {
            _gameName = gameName;
            _serverAddress = serverAddress;
            _serverPort = serverPort;
            _interpreter = interpreter;
            _scriptDirectory = scriptDirectory ?? GetDefaultScriptDirectory();
            
            // Register this instance as the global TWXServer for script access
            if (_interpreter != null)
            {
                GlobalModules.TWXServer = this;
                GlobalModules.TWXInterpreter = _interpreter;
                ScriptRef.SetActiveInterpreter(_interpreter);
                GlobalModules.DebugLog($"[GameInstance] Registered TWXServer and TWXInterpreter for game {_gameName}\n");
            }
            _listenPort = listenPort;
            _commandChar = commandChar;
            _directMenuHandler = new MenuHandler(this, interpreter, _scriptDirectory, () => 0);
            _nativeHaggle.SetEnabled(true);
            _nativeHaggle.EnabledChanged += enabled => NativeHaggleChanged?.Invoke(enabled);
            _nativeHaggle.StatsChanged += () => NativeHaggleStatsChanged?.Invoke();
            _log.ProgramDir = GlobalModules.ProgramDir;
            _log.SetLogIdentity(gameName);
            _log.SetPlaybackTargets(
                (payload, token) => SendPlaybackToLocalAsync(payload, token),
                message => SendMessageAsync(message).GetAwaiter().GetResult());
            GlobalModules.TWXLog = _log;
            InitializeSystemQuickTexts();
            _shipInfoParser.Updated += status =>
            {
                ShipStatus snapshot = CloneShipStatus(status);
                lock (_shipStatusLock)
                    _currentShipStatus = snapshot;
                ShipStatusUpdated?.Invoke(CloneShipStatus(snapshot));
            };

            string programDir = !string.IsNullOrWhiteSpace(scriptDirectory)
                ? (Path.GetDirectoryName(scriptDirectory) ?? GetDefaultProgramDir())
                : GlobalModules.ProgramDir;
            foreach (var bot in ProxyMenuCatalog.LoadBotConfigs(programDir, scriptDirectory))
                RegisterBotConfig(bot);
        }

        private static string GetDefaultProgramDir()
        {
            if (OperatingSystem.IsWindows())
                return WindowsInstallInfo.GetInstalledProgramDirOrDefault();

            return AppContext.BaseDirectory;
        }

        private static string GetDefaultScriptDirectory()
        {
            return Path.Combine(GetDefaultProgramDir(), "scripts");
        }

        public IDisposable PushClientContext(int clientIndex)
        {
            return new ClientContextScope(_preferredClientIndex, clientIndex >= 0 ? clientIndex : null);
        }

        public void FeedShipStatusLine(string line)
        {
            if (string.IsNullOrWhiteSpace(line))
                return;

            _shipInfoParser.FeedLine(line);
        }

        public void AdjustGenesisTorps(int delta)
        {
            if (delta == 0)
                return;

            _shipInfoParser.ApplyDelta(new ShipStatusDelta
            {
                GenesisTorpsDelta = delta
            });
        }

        public void AdjustAtomicDet(int delta)
        {
            if (delta == 0)
                return;

            _shipInfoParser.ApplyDelta(new ShipStatusDelta
            {
                AtomicDetDelta = delta
            });
        }

        public void ApplyShipStatusDelta(ShipStatusDelta delta)
        {
            if (delta == null || !delta.HasChanges())
                return;

            _shipInfoParser.ApplyDelta(delta);
        }

        private static ShipStatus CloneShipStatus(ShipStatus status) => new()
        {
            TraderName = status.TraderName,
            Experience = status.Experience,
            Alignment = status.Alignment,
            AlignText = status.AlignText,
            TimesBlownUp = status.TimesBlownUp,
            Corp = status.Corp,
            ShipName = status.ShipName,
            ShipType = status.ShipType,
            ShipNumber = status.ShipNumber,
            ShipClass = status.ShipClass,
            CurrentSector = status.CurrentSector,
            Turns = status.Turns,
            TurnsPerWarp = status.TurnsPerWarp,
            TotalHolds = status.TotalHolds,
            FuelOre = status.FuelOre,
            Organics = status.Organics,
            Equipment = status.Equipment,
            Colonists = status.Colonists,
            HoldsEmpty = status.HoldsEmpty,
            Fighters = status.Fighters,
            Shields = status.Shields,
            Photons = status.Photons,
            ArmidMines = status.ArmidMines,
            LimpetMines = status.LimpetMines,
            GenesisTorps = status.GenesisTorps,
            AtomicDet = status.AtomicDet,
            Corbomite = status.Corbomite,
            Cloaks = status.Cloaks,
            Beacons = status.Beacons,
            EtherProbes = status.EtherProbes,
            MineDisruptors = status.MineDisruptors,
            PsychProbe = status.PsychProbe,
            PlanetScanner = status.PlanetScanner,
            LRSType = status.LRSType,
            TransWarp1 = status.TransWarp1,
            TransWarp2 = status.TransWarp2,
            Interdictor = status.Interdictor,
            Credits = status.Credits
        };

        public string GetClientAddress(int index)
        {
            ClientSession? client = GetClientSession(index);
            return client?.RemoteAddress ?? string.Empty;
        }

        public void NotifyScriptLoad()
        {
            _ = SendEchoMarkAsync(2);
        }

        public void NotifyScriptStop()
        {
            _ = SendEchoMarkAsync(3);
            if (_interpreter != null && _interpreter.Count == 0)
            {
                lock (_clientLock)
                {
                    foreach (ClientSession client in _clients)
                    {
                        if (client.Type != ClientType.Rejected)
                            client.Type = ClientType.Standard;
                    }
                }
            }
        }

        private async Task SendEchoMarkAsync(byte mark)
        {
            List<ClientSession> targets;
            lock (_clientLock)
                targets = _clients.Where(client => client.EchoMarks).ToList();

            byte[] payload = new byte[] { 255, mark };
            foreach (ClientSession client in targets)
            {
                try
                {
                    await client.WriteStream.WriteAsync(payload, 0, payload.Length);
                    await client.WriteStream.FlushAsync();
                }
                catch
                {
                    // Ignore stale clients here; disconnect cleanup will remove them.
                }
            }
        }

        private ClientSession? GetClientSession(int index)
        {
            lock (_clientLock)
            {
                if (index < 0 || index >= _clients.Count)
                    return null;
                return _clients[index];
            }
        }

        private int GetClientIndex(ClientSession session)
        {
            lock (_clientLock)
                return _clients.IndexOf(session);
        }

        private IReadOnlyList<ClientSession> GetClientSnapshot()
        {
            lock (_clientLock)
                return _clients.ToList();
        }

        private void AddClientSession(ClientSession session)
        {
            lock (_clientLock)
                _clients.Add(session);
        }

        private void RemoveClientSession(ClientSession session)
        {
            lock (_clientLock)
                _clients.Remove(session);
        }

        private static bool IsPrivateClientAddress(string remoteAddress)
        {
            if (string.IsNullOrWhiteSpace(remoteAddress))
                return false;

            if (remoteAddress.Equals("127.0.0.1", StringComparison.OrdinalIgnoreCase) ||
                remoteAddress.Equals("::1", StringComparison.OrdinalIgnoreCase) ||
                remoteAddress.StartsWith("192.168.", StringComparison.OrdinalIgnoreCase) ||
                remoteAddress.StartsWith("10.", StringComparison.OrdinalIgnoreCase))
                return true;

            if (remoteAddress.StartsWith("172.", StringComparison.OrdinalIgnoreCase))
            {
                string[] parts = remoteAddress.Split('.');
                if (parts.Length > 1 && int.TryParse(parts[1], out int octet))
                    return octet is >= 16 and <= 31;
            }

            return false;
        }

        private static bool AddressMatchesList(string remoteAddress, string addressList)
        {
            if (string.IsNullOrWhiteSpace(remoteAddress) || string.IsNullOrWhiteSpace(addressList))
                return false;

            string[] parts = addressList
                .Split(new[] { ' ', ',', ';', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

            foreach (string part in parts)
            {
                if (part == "*" || part == "*.*.*.*")
                    return true;

                string prefix = part.Replace(".*", string.Empty, StringComparison.OrdinalIgnoreCase);
                if (remoteAddress.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        private bool ShouldAcceptClient(string remoteAddress, out bool localClient, out bool lerker)
        {
            localClient = IsPrivateClientAddress(remoteAddress) || AddressMatchesList(remoteAddress, ExternalAddress);
            lerker = AddressMatchesList(remoteAddress, LerkerAddress);

            return remoteAddress.Equals("127.0.0.1", StringComparison.OrdinalIgnoreCase) ||
                   remoteAddress.Equals("::1", StringComparison.OrdinalIgnoreCase) ||
                   (AcceptExternal && localClient) ||
                   (AllowLerkers && lerker);
        }

        private ClientType DetermineClientType(string remoteAddress, bool localClient)
        {
            if ((localClient && AcceptExternal) ||
                remoteAddress.Equals("127.0.0.1", StringComparison.OrdinalIgnoreCase) ||
                remoteAddress.Equals("::1", StringComparison.OrdinalIgnoreCase))
            {
                return ClientType.Standard;
            }

            return StreamEnabled ? ClientType.Stream : ClientType.Mute;
        }

        private static string DescribeClientType(ClientType type) => type switch
        {
            ClientType.Standard => "STANDARD",
            ClientType.Mute => "VIEW ONLY",
            ClientType.Deaf => "DEAF",
            ClientType.Stream => "STREAMING",
            ClientType.Rejected => "REJECTED",
            _ => type.ToString().ToUpperInvariant()
        };

        private static byte[] ApplyStreamMask(byte[] data)
        {
            byte[] masked = new byte[data.Length];
            Array.Copy(data, masked, data.Length);

            bool inAnsi = false;
            for (int i = 0; i < masked.Length; i++)
            {
                byte b = masked[i];
                if (b == 27)
                {
                    inAnsi = true;
                    continue;
                }

                if (!inAnsi && b >= (byte)'0' && b <= (byte)'9')
                    masked[i] = (byte)'1';

                if ((b >= (byte)'A' && b <= (byte)'Z') || (b >= (byte)'a' && b <= (byte)'z'))
                    inAnsi = false;
            }

            return masked;
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
                if (_acceptTask != null) tasks.Add(_acceptTask);
                tasks.AddRange(GetClientSnapshot().Select(client => client.ReadTask).Where(task => task != null)!);

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
                GlobalModules.GlobalAutoRecorder.ResetState($"game-stop:{_gameName}");

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
            _cancellationSource ??= new CancellationTokenSource();
            lock (_stateLock) { _isRunning = true; }

            var token = _cancellationSource.Token;
            var session = new ClientSession
            {
                IsDirect = true,
                RemoteAddress = "127.0.0.1",
                Type = ClientType.Standard,
                WriteStream = toTerminal,
                ReadStream = fromTerminal,
                MenuHandler = _directMenuHandler,
            };

            AddClientSession(session);
            session.ReadTask = Task.Run(() => ReadFromClientAsync(session, token), token);
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
                GlobalModules.GlobalAutoRecorder.ResetState($"server-connect:{_gameName}");
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

                await SendInitialHandshakeAsync(token);
                _interpreter?.HandleConnectionAccepted();
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

        private DataHeader? GetActiveHeader()
        {
            if (ScriptRef.GetActiveDatabase() is ModDatabase activeDb)
                return activeDb.DBHeader;
            return GlobalModules.TWXDatabase is ModDatabase globalDb ? globalDb.DBHeader : null;
        }

        private async Task SendInitialHandshakeAsync(CancellationToken token)
        {
            if (_serverStream == null || _serverClient?.Connected != true)
                return;

            DataHeader? header = GetActiveHeader();
            byte[] handshake;

            if (header?.UseRLogin == true)
            {
                string loginName = header.LoginName ?? string.Empty;
                handshake = Encoding.ASCII.GetBytes("\0" + loginName + "\0\0\0");
                GlobalModules.DebugLog($"[GameInstance] Sending RLogin handshake for '{loginName}'\n");
            }
            else
            {
                handshake = new byte[] { IAC, DO, 246 };
                GlobalModules.DebugLog("[GameInstance] Sending telnet login handshake\n");
            }

            await _serverStream.WriteAsync(handshake, 0, handshake.Length, token);
            await _serverStream.FlushAsync(token);
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
                    client.NoDelay = true;

                    string remoteAddress = ((IPEndPoint?)client.Client.RemoteEndPoint)?.Address.ToString() ?? "unknown";
                    bool allowed = ShouldAcceptClient(remoteAddress, out bool localClient, out _);
                    NetworkStream stream = client.GetStream();

                    if (!allowed)
                    {
                        byte[] reject = Encoding.ASCII.GetBytes($"\r\nExternal connections are disabled. Goodbye {remoteAddress}!\r\n");
                        await stream.WriteAsync(reject, 0, reject.Length, token);
                        await stream.FlushAsync(token);
                        client.Close();

                        if (BroadCastMsgs)
                            await SendToLocalAsync(Encoding.ASCII.GetBytes($"\r\nRemote connection rejected from: {remoteAddress}\r\n"), broadcastDeaf: true, token: token);
                        continue;
                    }

                    ClientSession? session = null;
                    session = new ClientSession
                    {
                        TcpClient = client,
                        WriteStream = stream,
                        ReadStream = stream,
                        RemoteAddress = remoteAddress,
                        Type = DetermineClientType(remoteAddress, localClient),
                        MenuHandler = new MenuHandler(this, _interpreter, _scriptDirectory, () => GetClientIndex(session!))
                    };

                    AddClientSession(session);
                    Log($"[{_gameName}] Client connected from {remoteAddress} as {DescribeClientType(session.Type)}");

                    await stream.WriteAsync(new byte[] { 255, 251, 1 }, 0, 3, token);
                    await stream.FlushAsync(token);

                    string banner = $"\r\nTWX Proxy Server v{Constants.ProgramVersion}{Constants.ReleaseNumber} ({Constants.ReleaseVersion})\r\n";
                    await stream.WriteAsync(Encoding.ASCII.GetBytes(banner), token);

                    if (session.Type == ClientType.Mute || session.Type == ClientType.Stream)
                    {
                        string viewOnly = "\r\nYou are locked in view only mode\r\n\r\n";
                        await stream.WriteAsync(Encoding.ASCII.GetBytes(viewOnly), token);
                    }
                    else
                    {
                        string prompt = $"\r\nPress {_commandChar} to activate terminal menu\r\n\r\n";
                        await stream.WriteAsync(Encoding.ASCII.GetBytes(prompt), token);
                    }
                    await stream.FlushAsync(token);

                    if (BroadCastMsgs)
                        await SendToLocalAsync(Encoding.ASCII.GetBytes($"\r\nActive connection detected from: {remoteAddress}\r\n"), broadcastDeaf: true, token: token);

                    _interpreter?.ProgramEvent("Client connected", string.Empty, false);
                    session.ReadTask = Task.Run(() => ReadFromClientAsync(session, token), token);
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
                        
                        try
                        {
                            string disconnectText = _autoReconnect && !token.IsCancellationRequested
                                ? $"\r\n[twxp] Server disconnected.  Proxy auto-reconnecting...\r\n"
                                : $"\r\n[twxp] Server disconnected.  Type {_commandChar}c to reconnect.\r\n";
                            await SendToLocalAsync(Encoding.ASCII.GetBytes(disconnectText), broadcastDeaf: true, token: token);
                        }
                        catch (Exception ex)
                        {
                            Log($"[{_gameName}] Could not send disconnect message to clients: {ex.Message}");
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
                        BeginServerDataDispatch();
                        try
                        {
                            ServerDataReceived?.Invoke(this, new DataReceivedEventArgs(cleanData));
                        }
                        finally
                        {
                            EndServerDataDispatch();
                        }
                        _log.RecordServerData(cleanData);
                    }

                    if (cleanData.Length == 0)
                    {
                        Log($"[{_gameName}] -> Only telnet negotiation, nothing to forward");
                    }
                    else
                    {
                        if (!ShouldSuppressScriptPipeToggleOutput(cleanData))
                            await SendToLocalAsync(cleanData, token: token);
                        await FlushDeferredLocalOutputAsync(token);
                        LogVerbose($"[{_gameName}] -> Forwarded {cleanData.Length} bytes to local clients");
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
                
                try
                {
                    string disconnectText = _autoReconnect && !token.IsCancellationRequested
                        ? $"\r\n[twxp] Server disconnected.  Proxy auto-reconnecting...\r\n"
                        : $"\r\n[twxp] Server disconnected.  Type {_commandChar}c to reconnect.\r\n";
                    await SendToLocalAsync(Encoding.ASCII.GetBytes(disconnectText), broadcastDeaf: true, token: token);
                }
                catch (Exception sendEx)
                {
                    Log($"[{_gameName}] Could not send disconnect message to clients: {sendEx.Message}");
                }

                Disconnected?.Invoke(this, new DisconnectEventArgs(ex.Message));
                // Start auto-reconnect if allowed
                if (_autoReconnect && !token.IsCancellationRequested)
                    _ = Task.Run(() => ReconnectLoopAsync(token));
            }
        }

        private async Task ReconnectLoopAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested && _autoReconnect)
            {
                int reconnectDelay = _reconnectDelayMs;
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

        private async Task ReadFromClientAsync(ClientSession session, CancellationToken token)
        {
            var buffer = new byte[8192];
            var commandBuffer = new StringBuilder();
            bool inCommandMode = false;

            try
            {
                while (!token.IsCancellationRequested && session.IsConnected)
                {
                    int bytesRead = await session.ReadStream.ReadAsync(buffer, 0, buffer.Length, token);
                    if (bytesRead == 0)
                        break;

                    for (int i = 0; i < bytesRead; i++)
                    {
                        _log.NotifyUserInput();

                        byte b = buffer[i];
                        char c = (char)b;

                        if (session.Type is ClientType.Mute or ClientType.Stream)
                            continue;

                        if (c == _commandChar)
                        {
                            if (session.MenuHandler.IsActive)
                            {
                                await session.MenuHandler.ExitMenuAsync();
                            }
                            else if (inCommandMode)
                            {
                                string command = commandBuffer.ToString();
                                commandBuffer.Clear();
                                inCommandMode = false;

                                Log($"[{_gameName}] Command from {session.RemoteAddress}: {command}");
                                using var _ = PushClientContext(GetClientIndex(session));
                                await HandleCommandAsync(command);
                            }
                            else
                            {
                                inCommandMode = false;
                                await session.MenuHandler.HandleMenuCommandAsync(c);
                            }
                        }
                        else if (inCommandMode)
                        {
                            commandBuffer.Append(c);
                        }
                        else
                        {
                            bool scriptWaitingForInput = _interpreter?.IsAnyScriptWaitingForInput() ?? false;
                            bool handledByScriptMenu = false;

                            if (!scriptWaitingForInput && GlobalModules.TWXMenu is MenuManager menuMgr && menuMgr.IsMenuOpen())
                                handledByScriptMenu = menuMgr.HandleMenuInput(c);

                            if (!handledByScriptMenu)
                            {
                                bool handled = await session.MenuHandler.HandleInputCharAsync(c);

                                if (!handled && session.MenuHandler.CurrentMenu != MenuState.None && !scriptWaitingForInput)
                                {
                                    await session.MenuHandler.HandleMenuCommandAsync(c);
                                }
                                else if (!handled)
                                {
                                    bool keypressMode = scriptWaitingForInput && (_interpreter?.HasKeypressInputWaiting ?? false);
                                    bool textOutConsumed = false;
                                    bool enteredInputWait = false;
                                    if (_interpreter != null && !scriptWaitingForInput)
                                    {
                                        textOutConsumed = _interpreter.TextOutEvent(c.ToString(), null);
                                        enteredInputWait = _interpreter.IsAnyScriptWaitingForInput();
                                    }

                                    // If a text-out trigger on this exact character just opened a
                                    // GETINPUT/GETCONSOLEINPUT wait, do not also forward that same
                                    // character to the server or reuse it as the pending reply.
                                    bool suppressCurrentCharForNewInputWait = !scriptWaitingForInput && enteredInputWait;

                                    bool sendToServer = !textOutConsumed &&
                                                        !scriptWaitingForInput &&
                                                        !suppressCurrentCharForNewInputWait;
                                    if (sendToServer && _serverStream != null && _serverClient?.Connected == true)
                                    {
                                        bool negotiationInProgress;
                                        lock (_negotiationLock)
                                            negotiationInProgress = !_telnetNegotiationComplete;

                                        if (negotiationInProgress)
                                        {
                                            lock (_negotiationLock)
                                                _clientBufferDuringNegotiation.Add(b);

                                            if (i == 0)
                                                Log($"[{_gameName}] Buffering client data during telnet negotiation");
                                        }
                                        else
                                        {
                                            await _serverStream.WriteAsync(new byte[] { b }, 0, 1, token);
                                            await _serverStream.FlushAsync(token);
                                        }
                                    }
                                    else if (scriptWaitingForInput && !keypressMode)
                                    {
                                        if (b == 8 || b == 127)
                                        {
                                            await session.WriteStream.WriteAsync(new byte[] { 8, 32, 8 }, 0, 3, token);
                                            await session.WriteStream.FlushAsync(token);
                                        }
                                        else if (b != 13 && b != 10)
                                        {
                                            await session.WriteStream.WriteAsync(new byte[] { b }, 0, 1, token);
                                            await session.WriteStream.FlushAsync(token);
                                        }
                                        else if (b == 13)
                                        {
                                            await session.WriteStream.WriteAsync(new byte[] { 13, 10 }, 0, 2, token);
                                            await session.WriteStream.FlushAsync(token);
                                        }
                                    }

                                    if (!textOutConsumed && !suppressCurrentCharForNewInputWait)
                                        LocalDataReceived?.Invoke(this, new DataReceivedEventArgs(new byte[] { b }));
                                    else if (suppressCurrentCharForNewInputWait)
                                        GlobalModules.DebugLog($"[INPUT] Suppressed trigger character {b} ('{c}') after text-out handler opened input wait\n");
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
                Log($"[{_gameName}] Error reading from client {session.RemoteAddress}: {ex.Message}");
            }
            finally
            {
                bool wasRejected = session.Type == ClientType.Rejected;
                RemoveClientSession(session);

                try { session.WriteStream.Close(); } catch { }
                if (!ReferenceEquals(session.ReadStream, session.WriteStream))
                {
                    try { session.ReadStream.Close(); } catch { }
                }
                try { session.TcpClient?.Close(); } catch { }

                if (!wasRejected)
                {
                    IReadOnlyList<ClientSession> remaining = GetClientSnapshot();
                    byte[] notice = Encoding.ASCII.GetBytes($"\r\nConnection lost from: {session.RemoteAddress}\r\n");
                    foreach (ClientSession other in remaining)
                    {
                        if (ReferenceEquals(other, session))
                            continue;
                        try
                        {
                            await other.WriteStream.WriteAsync(notice, 0, notice.Length, token);
                            await other.WriteStream.FlushAsync(token);
                        }
                        catch
                        {
                            // Ignore stale peers here.
                        }
                    }

                    _interpreter?.ProgramEvent("Client disconnected", string.Empty, false);
                }

                Log($"[{_gameName}] Client disconnected: {session.RemoteAddress}");
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
        public async Task SendToLocalAsync(byte[] data, bool broadcastDeaf = false, CancellationToken token = default)
        {
            IReadOnlyList<ClientSession> clients = GetClientSnapshot();
            foreach (ClientSession client in clients)
            {
                if (client.Type == ClientType.Rejected)
                    continue;
                if (!broadcastDeaf && client.Type == ClientType.Deaf)
                    continue;

                byte[] payload = client.Type == ClientType.Stream ? ApplyStreamMask(data) : data;

                try
                {
                    await client.WriteStream.WriteAsync(payload, 0, payload.Length, token);
                    await client.WriteStream.FlushAsync(token);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    Log($"[SendToLocalAsync] Failed sending to client {client.RemoteAddress}: {ex.Message}");
                }
            }
        }

        public async Task SendToClientAsync(int clientIndex, byte[] data, CancellationToken token = default)
        {
            ClientSession? client = GetClientSession(clientIndex);
            if (client == null || client.Type == ClientType.Rejected)
                return;

            byte[] payload = client.Type == ClientType.Stream ? ApplyStreamMask(data) : data;
            await client.WriteStream.WriteAsync(payload, 0, payload.Length, token);
            await client.WriteStream.FlushAsync(token);
        }

        private async Task SendPlaybackToLocalAsync(byte[] data, CancellationToken token)
        {
            await SendToLocalAsync(data, token: token);
        }

        /// <summary>
        /// Send text to the local client
        /// </summary>
        public async Task SendMessageAsync(string message)
        {
            message = ApplyQuickText(message);
            var data = Encoding.ASCII.GetBytes(message);
            if (TryQueueDeferredLocalOutput(data, broadcastDeaf: false))
                return;

            if (_preferredClientIndex.Value is int clientIndex)
                await SendToClientAsync(clientIndex, data);
            else
                await SendToLocalAsync(data);
        }

        public string ApplyQuickText(string text)
        {
            if (string.IsNullOrEmpty(text))
                return string.Empty;

            text = text.Replace("~~", "\u00FF", StringComparison.Ordinal);

            if (text.Contains("~_", StringComparison.Ordinal))
            {
                string botTag = _interpreter?.ActiveBotTag ?? string.Empty;
                int tagLength = _interpreter?.ActiveBotTagLength ?? 0;
                string filler = new string('-', Math.Max(0, 67 - tagLength));
                text = text.Replace("~_", filler + botTag + "--", StringComparison.Ordinal);
            }

            foreach ((string Search, string Replace) in _userQuickTexts)
                text = text.Replace(Search, Replace, StringComparison.Ordinal);

            foreach ((string Search, string Replace) in _systemQuickTexts)
                text = text.Replace(Search, Replace, StringComparison.Ordinal);

            text = text.Replace("\u00FF", "~", StringComparison.Ordinal);
            return text.Replace("^[", "\u001b[", StringComparison.Ordinal);
        }

        private void AddSystemQuickText(string key, string value)
        {
            _systemQuickTexts.Add((key, value));
        }

        private void InitializeSystemQuickTexts()
        {
            AddSystemQuickText("~a", "^[0;30m");
            AddSystemQuickText("~b", "^[0;31m");
            AddSystemQuickText("~c", "^[0;32m");
            AddSystemQuickText("~d", "^[0;33m");
            AddSystemQuickText("~e", "^[0;34m");
            AddSystemQuickText("~f", "^[0;35m");
            AddSystemQuickText("~g", "^[0;36m");
            AddSystemQuickText("~h", "^[0;37m");
            AddSystemQuickText("~A", "^[1;30m");
            AddSystemQuickText("~B", "^[1;31m");
            AddSystemQuickText("~C", "^[1;32m");
            AddSystemQuickText("~D", "^[1;33m");
            AddSystemQuickText("~E", "^[1;34m");
            AddSystemQuickText("~F", "^[1;35m");
            AddSystemQuickText("~G", "^[1;36m");
            AddSystemQuickText("~H", "^[1;37m");
            AddSystemQuickText("~i", "^[40m");
            AddSystemQuickText("~j", "^[41m");
            AddSystemQuickText("~k", "^[42m");
            AddSystemQuickText("~l", "^[43m");
            AddSystemQuickText("~m", "^[44m");
            AddSystemQuickText("~n", "^[45m");
            AddSystemQuickText("~o", "^[46m");
            AddSystemQuickText("~p", "^[47m");
            AddSystemQuickText("~I", "^[5;40m");
            AddSystemQuickText("~J", "^[5;41m");
            AddSystemQuickText("~K", "^[5;42m");
            AddSystemQuickText("~L", "^[5;43m");
            AddSystemQuickText("~M", "^[5;44m");
            AddSystemQuickText("~N", "^[5;45m");
            AddSystemQuickText("~O", "^[5;46m");
            AddSystemQuickText("~P", "^[5;47m");
            AddSystemQuickText("~!", "^[2J^[H");
            AddSystemQuickText("~@", "\r^[0m^[0K");
            AddSystemQuickText("~0", "^[0m");
            AddSystemQuickText("~1", "^[0m^[1;36m");
            AddSystemQuickText("~2", "^[0m^[1;33m");
            AddSystemQuickText("~3", "^[0m^[35m");
            AddSystemQuickText("~4", "^[0m^[1;44m");
            AddSystemQuickText("~5", "^[0m^[32m");
            AddSystemQuickText("~6", "^[0m^[1;5;37m");
            AddSystemQuickText("~7", "^[0m^[1;37m");
            AddSystemQuickText("~8", "^[0m^[1;5;31m");
            AddSystemQuickText("~9", "^[0m^[30;47m");
            AddSystemQuickText("~s", "\u001b[s");
            AddSystemQuickText("~u", "\u001b[u");
            AddSystemQuickText("~-", "---------------------------------------------------------------------");
            AddSystemQuickText("~=", "=====================================================================");
            AddSystemQuickText("~+", "-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-");
        }

        private void RegisterBotConfig(BotConfig config)
        {
            if (string.IsNullOrWhiteSpace(config.Name))
                return;

            if (_botConfigs.TryGetValue(config.Name, out BotConfig? existing))
                _botOrder.Remove(existing);

            _botConfigs[config.Name] = config;
            _botOrder.Add(config);
        }

        public void ReloadBotConfigs(string? programDir, string? scriptDirectory, bool includeNative = false)
        {
            _botConfigs.Clear();
            _botOrder.Clear();

            foreach (BotConfig bot in ProxyMenuCatalog.LoadBotConfigs(programDir, scriptDirectory, includeNative))
                RegisterBotConfig(bot);

            if (!string.IsNullOrWhiteSpace(ActiveBotName) &&
                !_botConfigs.ContainsKey(ActiveBotName) &&
                !_botOrder.Any(bot => string.Equals(bot.Name, ActiveBotName, StringComparison.OrdinalIgnoreCase)))
            {
                ActiveBotName = string.Empty;
            }
        }

        public bool ToggleNativeHaggle()
        {
            return _nativeHaggle.Toggle();
        }

        public void SetNativeHaggleEnabled(bool enabled)
        {
            _nativeHaggle.SetEnabled(enabled);
        }

        public void SetNativeHaggleMode(string? mode)
        {
            _nativeHaggle.SetFirstBidMode(mode);
        }

        public void SetNativePortHaggleMode(string? mode)
        {
            _nativeHaggle.SetPortHaggleMode(mode);
        }

        public void SetNativePlanetHaggleMode(string? mode)
        {
            _nativeHaggle.SetPlanetHaggleMode(mode);
        }

        public void SetNativeHaggleModes(string? portMode, string? planetMode)
        {
            _nativeHaggle.SetPortHaggleMode(portMode);
            _nativeHaggle.SetPlanetHaggleMode(planetMode);
        }

        internal void RegisterNativeHaggleMode(NativeHaggleModeExtension mode)
        {
            _nativeHaggle.RegisterMode(mode);
        }

        internal void UnregisterNativeHaggleMode(string? modeId)
        {
            _nativeHaggle.UnregisterMode(modeId);
        }

        public void SetCommandChar(char commandChar)
        {
            if (!char.IsControl(commandChar))
                _commandChar = commandChar;
        }

        public bool ProcessNativeHaggleLine(string line)
        {
            if (string.IsNullOrWhiteSpace(line))
                return false;

            string? response = _nativeHaggle.HandleLine(line);
            if (!string.IsNullOrEmpty(response))
            {
                SendNativeHaggleResponse(response);
                return true;
            }

            return false;
        }

        public void ObserveScriptSend(string text)
        {
            _nativeHaggle.ObserveScriptSend(text);

            if (text == "|")
            {
                Interlocked.Increment(ref _suppressScriptPipeToggleMessageCount);
                GlobalModules.DebugLog("[MSGTOGGLE] Armed suppression for next script-triggered message toggle response\n");
            }
        }

        private bool ShouldSuppressScriptPipeToggleOutput(byte[] cleanData)
        {
            if (Volatile.Read(ref _suppressScriptPipeToggleMessageCount) <= 0 &&
                Volatile.Read(ref _suppressScriptPipeTogglePromptCount) <= 0)
            {
                return false;
            }

            string text = Encoding.Latin1.GetString(cleanData);
            bool containsToggleMessage =
                text.Contains("Silencing all messages.", StringComparison.Ordinal) ||
                text.Contains("Displaying all messages.", StringComparison.Ordinal);
            bool containsPrompt =
                text.Contains("(?=Help)? :", StringComparison.Ordinal) ||
                text.Contains("Main> ", StringComparison.Ordinal) ||
                text.Contains("Script> ", StringComparison.Ordinal);

            if (containsToggleMessage && Volatile.Read(ref _suppressScriptPipeToggleMessageCount) > 0)
            {
                Interlocked.Decrement(ref _suppressScriptPipeToggleMessageCount);
                if (!containsPrompt)
                    Interlocked.Increment(ref _suppressScriptPipeTogglePromptCount);

                GlobalModules.DebugLog("[MSGTOGGLE] Suppressed local display of script-triggered message toggle response\n");
                return true;
            }

            if (containsPrompt && Volatile.Read(ref _suppressScriptPipeTogglePromptCount) > 0)
            {
                Interlocked.Decrement(ref _suppressScriptPipeTogglePromptCount);
                GlobalModules.DebugLog("[MSGTOGGLE] Suppressed local display of follow-up prompt after script-triggered message toggle\n");
                return true;
            }

            return false;
        }

        private void SendNativeHaggleResponse(string response)
        {
            _nativeHaggleSendLock.Wait();
            try
            {
                if (_serverStream == null || _serverClient?.Connected != true)
                {
                    GlobalModules.DebugLog($"[NativeHaggle] Dropped response '{response}' because the server is not connected.\n");
                    return;
                }

                byte[] data = Encoding.ASCII.GetBytes(response + "\r");
                GlobalModules.DebugLog($"[NativeHaggle] SEND '{response}\\r'\n");
                _serverStream.Write(data, 0, data.Length);
                _serverStream.Flush();
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

            _serverStream?.Close();
            _serverClient?.Close();
            _localListener?.Stop();

            _serverStream = null;
            _serverClient = null;
            _localListener = null;

            IReadOnlyList<ClientSession> clients = GetClientSnapshot();
            foreach (ClientSession client in clients)
            {
                try { client.WriteStream.Close(); } catch { }
                if (!ReferenceEquals(client.ReadStream, client.WriteStream))
                {
                    try { client.ReadStream.Close(); } catch { }
                }
                try { client.TcpClient?.Close(); } catch { }
            }

            lock (_clientLock)
                _clients.Clear();
        }

        #region ITWXServer Implementation

        public void Broadcast(string message)
        {
            byte[] data = Encoding.ASCII.GetBytes(ApplyQuickText(message));
            if (TryQueueDeferredLocalOutput(data, broadcastDeaf: false))
                return;

            SendToLocalAsync(data).Wait();
        }

        public void Broadcast(string message, bool broadcastDeaf)
        {
            byte[] data = Encoding.ASCII.GetBytes(ApplyQuickText(message));
            if (TryQueueDeferredLocalOutput(data, broadcastDeaf))
                return;

            SendToLocalAsync(data, broadcastDeaf: broadcastDeaf).Wait();
        }

        public void ClientMessage(string message)
        {
            byte[] data = Encoding.ASCII.GetBytes(ApplyQuickText(message));
            if (TryQueueDeferredLocalOutput(data, broadcastDeaf: false))
                return;

            SendToLocalAsync(data).Wait();
        }

        private void BeginServerDataDispatch()
        {
            lock (_deferredLocalOutputLock)
                _serverDataDispatchDepth++;
        }

        private void EndServerDataDispatch()
        {
            lock (_deferredLocalOutputLock)
            {
                if (_serverDataDispatchDepth > 0)
                    _serverDataDispatchDepth--;
            }
        }

        private bool TryQueueDeferredLocalOutput(byte[] data, bool broadcastDeaf)
        {
            lock (_deferredLocalOutputLock)
            {
                if (_serverDataDispatchDepth <= 0)
                    return false;

                byte[] copy = new byte[data.Length];
                Buffer.BlockCopy(data, 0, copy, 0, data.Length);
                _deferredLocalOutput.Add(new DeferredLocalOutput
                {
                    Data = copy,
                    BroadcastDeaf = broadcastDeaf
                });
                return true;
            }
        }

        private async Task FlushDeferredLocalOutputAsync(CancellationToken token = default)
        {
            List<DeferredLocalOutput>? pending = null;
            lock (_deferredLocalOutputLock)
            {
                if (_serverDataDispatchDepth > 0 || _deferredLocalOutput.Count == 0)
                    return;

                pending = new List<DeferredLocalOutput>(_deferredLocalOutput);
                _deferredLocalOutput.Clear();
            }

            foreach (DeferredLocalOutput output in pending)
                await SendToLocalAsync(output.Data, output.BroadcastDeaf, token);
        }

        public void AddQuickText(string key, string value)
        {
            if (string.IsNullOrWhiteSpace(key))
                return;

            ClearQuickText(key);
            _userQuickTexts.Add((key, ApplyQuickText(value ?? string.Empty)));
        }

        public void ClearQuickText(string? key = null)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                _userQuickTexts.Clear();
                return;
            }

            _userQuickTexts.RemoveAll(entry => string.Equals(entry.Search, key, StringComparison.Ordinal));
        }

        public ClientType GetClientType(int index)
        {
            return GetClientSession(index)?.Type ?? ClientType.Standard;
        }

        public void SetClientType(int index, ClientType type)
        {
            ClientSession? client = GetClientSession(index);
            if (client != null)
                client.Type = type;
        }

        public void RegisterBot(string botName, string scriptFile, string description = "")
        {
            if (string.IsNullOrWhiteSpace(botName) || string.IsNullOrWhiteSpace(scriptFile))
                return;

            RegisterBotConfig(new BotConfig
            {
                Name = botName,
                ScriptFile = scriptFile.Replace('\\', '/'),
                ScriptFiles = new List<string> { scriptFile.Replace('\\', '/') },
                Description = description ?? string.Empty,
            });
        }

        public void UnregisterBot(string botName)
        {
            if (string.IsNullOrWhiteSpace(botName))
                return;

            BotConfig? config = GetBotConfig(botName);
            if (config == null)
                return;

            _botConfigs.Remove(config.Name);
            _botOrder.Remove(config);
            if (string.Equals(ActiveBotName, config.Name, StringComparison.OrdinalIgnoreCase))
                ActiveBotName = string.Empty;
        }

        public List<string> GetBotList()
        {
            return _botOrder.Select(bot => bot.Name).ToList();
        }

        public BotConfig? GetBotConfig(string botName)
        {
            if (string.IsNullOrWhiteSpace(botName))
                return null;

            string selector = botName.Trim();
            if (selector.StartsWith("bot:", StringComparison.OrdinalIgnoreCase))
                selector = selector["bot:".Length..];

            if (_botConfigs.TryGetValue(selector, out BotConfig? config))
                return config;

            config = _botOrder.FirstOrDefault(bot =>
                string.Equals(bot.Alias, selector, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(bot.Name, selector, StringComparison.OrdinalIgnoreCase));
            if (config != null)
                return config;

            return _botOrder.FirstOrDefault(bot =>
                (!string.IsNullOrWhiteSpace(bot.ScriptFile) &&
                 bot.ScriptFile.Contains(selector, StringComparison.OrdinalIgnoreCase)) ||
                bot.ScriptFiles.Any(script => script.Contains(selector, StringComparison.OrdinalIgnoreCase)));
        }

        public object? GetActiveBot()
        {
            return _interpreter?.GetActiveBot();
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
