# TWX30 Network + Database Integration

This document describes how the Network module integrates with Database.cs to provide game proxy functionality.

## Architecture Overview

```
LocalClient <--> TcpListener(ListenPort) <--> GameInstance <--> TcpClient <--> GameServer
                                               |
                                               v
                                          Database
                                          (stores sectors, processes traffic)
```

## Integration Components

### 1. Network Module (`Network.cs`)

#### `GameInstance` Class
Manages a single game connection with:
- **Server Connection**: TcpClient connecting to game server
- **Local Listener**: TcpListener accepting local client connections  
- **Traffic Pass-Through**: Bidirectional data flow between local and server
- **Command Mode**: Detects `$COMMAND$` syntax and intercepts commands
- **Events**: Hooks for script processing

#### `NetworkManager` Class
Manages multiple game instances:
- Thread-safe dictionary of active games
- Start/stop/get operations for individual games
- Bulk operations (stop all games)

### 2. Database Integration (`Database.cs`)

#### Fields Added to `ModDatabase`
```csharp
private NetworkManager? _networkManager;
private GameInstance? _gameInstance;
```

#### Properties Added
```csharp
public bool IsNetworkActive => _gameInstance?.IsRunning ?? false;
public bool IsConnected => _gameInstance?.IsConnected ?? false;
public GameInstance? GameInstance => _gameInstance;
```

#### Methods Added

**`StartNetworkAsync()`**
- Reads configuration from `DataHeader` (Address, ServerPort, ListenPort)
- Initializes `NetworkManager` if needed
- Starts `GameInstance` with database name and settings
- Hooks up event handlers for traffic processing

**`StopNetworkAsync()`**
- Unhooks event handlers
- Stops game instance
- Cleans up resources

**`SendMessageAsync(string message)`**
- Sends text to local client

**`SendToServerAsync(byte[] data)`**
- Sends raw data to game server

**`SendToLocalAsync(byte[] data)`**
- Sends raw data to local client

#### Event Handlers

**`OnServerDataReceived()`**
- Called when data received from game server
- Hook point for script processing to parse sector updates, port reports, etc.
- Updates database in real-time

**`OnLocalDataReceived()`**
- Called when data received from local client
- Hook point for script processing to intercept commands, add macros, etc.

**`OnCommandReceived()`**
- Called when `$COMMAND$` detected
- Processes TWX Proxy commands (STATUS, SAVE, RELOAD, SCRIPT, etc.)

**`OnConnected()`**
- Called when connection to server established

**`OnDisconnected()`**
- Called when connection lost
- Can trigger auto-reconnect logic

## Usage Example

### Basic Usage

```csharp
var database = new ModDatabase();

// Open database (loads game configuration)
database.OpenDatabase("myGame.xdb");

// Start network using database configuration
await database.StartNetworkAsync();

// Database is now proxying traffic and updating in real-time
Console.WriteLine($"Connected: {database.IsConnected}");
Console.WriteLine($"Active: {database.IsNetworkActive}");

// Send a message to the local client
await database.SendMessageAsync("Welcome to TWX Proxy!\r\n");

// Stop when done
await database.StopNetworkAsync();
database.Dispose();
```

### Managing Multiple Games

```csharp
var manager = new NetworkManager();

// Start multiple games
var game1 = await manager.StartGameAsync("Game1", "server1.com", 2002, 2602);
var game2 = await manager.StartGameAsync("Game2", "server2.com", 2003, 2603);

// Each game proxies independently
game1.CommandReceived += (s, e) => Console.WriteLine($"Game1: {e.Command}");
game2.CommandReceived += (s, e) => Console.WriteLine($"Game2: {e.Command}");

// Stop all games
await manager.StopAllGamesAsync();
```

## Database Configuration

The `DataHeader` class contains network configuration:

```csharp
public class DataHeader
{
    public string Address { get; set; }        // Server address (e.g., "twgs.com")
    public ushort ServerPort { get; set; }     // Server port (e.g., 2002)
    public ushort ListenPort { get; set; }     // Local listen port (e.g., 2602)
    public string Description { get; set; }    // Game description
    public string LoginName { get; set; }      // Auto-login username
    public string Password { get; set; }       // Auto-login password
    public string LoginScript { get; set; }    // Login script commands
    // ... other fields
}
```

## Command Mode

Commands are delimited by `$` characters:
- User types: `$HELP$`
- GameInstance detects command, extracts `HELP`
- Fires `CommandReceived` event with command text
- Command is **not** sent to server
- All other traffic passes through normally

## Script Processing Hooks

Scripts can intercept traffic through events:

```csharp
database.GameInstance.ServerDataReceived += (sender, e) =>
{
    var text = e.Text; // ASCII text from server
    // Parse sector data, update database
    // Modify data before sending to client
};

database.GameInstance.LocalDataReceived += (sender, e) =>
{
    var text = e.Text; // ASCII text from client
    // Intercept commands, add shortcuts
    // Auto-complete, macros, etc.
};
```

## Testing

### TestNetworkDB
Demonstrates network module with database-style configuration.

**Usage:**
```bash
cd TWX30/Source/TestNetworkDB
dotnet run "Game Name" server.com 2002 2602
```

**Example:**
```bash
dotnet run "Test Game" twgs.com 2002 2602
```

Then connect with terminal client:
```bash
telnet localhost 2602
```

Available commands:
- `$HELP$` - Show help
- `$STATUS$` - Show connection status  
- `$SAVE$` - Save database (placeholder)
- `$QUIT$` - Disconnect

### TestNetwork
Basic network module test without database.

**Usage:**
```bash
cd TWX30/Source/TestNetwork  
dotnet run
```

## Migration from TWX26

TWX26 used Delphi's TServerSocket/TClientSocket components. TWX30 uses:

| TWX26 | TWX30 |
|-------|-------|
| TModServer (TServerSocket) | GameInstance (TcpListener) |
| TModClient (TClientSocket) | GameInstance (TcpClient) |
| TTelnetSocket | Network stream handling |
| ProcessTelnet() | Event handlers |
| Broadcast() | SendToLocalAsync() |
| Send() | SendToServerAsync() |

## Future Enhancements

1. **Auto-Reconnect**: Detect server disconnection and retry
2. **Multi-Client Support**: Allow multiple local clients per game
3. **Telnet Protocol**: Full IAC/WILL/WONT/DO/DONT negotiation
4. **Traffic Logging**: Record all traffic for debugging
5. **Script Engine**: Integrate scripting language for automation
6. **Compression**: Support MCCP (Mud Client Compression Protocol)
7. **SSL/TLS**: Encrypted connections to game servers

## Architecture Notes

- **Async/Await**: All network operations are async for responsiveness
- **Thread-Safe**: ConcurrentDictionary and locks protect shared state
- **Event-Driven**: Scripts hook into traffic flow via events
- **Resource Management**: Proper IDisposable implementation
- **Cancellation**: CancellationToken support for clean shutdown
