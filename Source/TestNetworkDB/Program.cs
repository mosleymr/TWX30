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
using System.IO;
using System.Text;
using System.Threading.Tasks;
using TWXProxy.Core;

class Program
{
    static async Task Main(string[] args)
    {
        Console.WriteLine("TWX30 Network + Database Integration Test");
        Console.WriteLine("==========================================\n");

        // Parse command line arguments
        if (args.Length < 4)
        {
            Console.WriteLine("Usage: TestNetworkDB <game-name> <server-address> <server-port> <listen-port>");
            Console.WriteLine("Example: TestNetworkDB \"Test Game\" twgs.com 2002 2602");
            Console.WriteLine("\nThis test demonstrates the network module with database-style configuration.");
            return;
        }

        string gameName = args[0];
        string serverAddress = args[1];
        int serverPort = int.Parse(args[2]);
        int listenPort = int.Parse(args[3]);

        var manager = new NetworkManager();

        try
        {
            // Display configuration (mimicking what would be loaded from database)
            Console.WriteLine("Game Configuration:");
            Console.WriteLine($"  Name: {gameName}");
            Console.WriteLine($"  Server: {serverAddress}:{serverPort}");
            Console.WriteLine($"  Listen Port: {listenPort}");
            Console.WriteLine();

            // Start network
            Console.WriteLine("Starting network connection...");
            var game = await manager.StartGameAsync(gameName, serverAddress, serverPort, listenPort);

            // Don't auto-connect - wait for $c command from client
            Console.WriteLine("Network ready. Waiting for client to connect...");
            Console.WriteLine("Client should type $c to connect to server");

            // Hook up event handlers (these would connect to Database event handlers)
            game.ServerDataReceived += (sender, e) =>
            {
                // In full integration, this would call Database.OnServerDataReceived
                // which would update sector data, parse port reports, etc.
                Console.WriteLine($"<- Server: {e.Data.Length} bytes");
            };

            game.LocalDataReceived += (sender, e) =>
            {
                // In full integration, this would call Database.OnLocalDataReceived
                // for script processing, command interception, etc.
                var text = Encoding.ASCII.GetString(e.Data);
                Console.WriteLine($"-> Local: {text.TrimEnd()}");
            };

            game.CommandReceived += (sender, e) =>
            {
                Console.WriteLine($"** Command: {e.Command}");
                
                // Handle commands (in full integration, these would be Database methods)
                // Note: 'c' is handled internally by GameInstance for connection
                switch (e.Command.ToUpper())
                {
                    case "C":
                    case "CONNECT":
                        // Built-in command - handled by GameInstance
                        break;
                    case "HELP":
                        _ = game.SendMessageAsync("\\r\\nTWX Proxy Commands:\\r\\n  $c - Connect to server\\r\\n  HELP - Show this help\\r\\n  STATUS - Show connection status\\r\\n  SAVE - Save database\\r\\n  QUIT - Disconnect\\r\\n\\r\\n");
                        break;
                    case "STATUS":
                        var status = $"\\r\\nGame: {game.GameName}\\r\\nConnected: {game.IsConnected}\\r\\nRunning: {game.IsRunning}\\r\\n\\r\\n";
                        _ = game.SendMessageAsync(status);
                        break;
                    case "SAVE":
                        // In full integration: await database.SaveDatabase();
                        _ = game.SendMessageAsync("\\r\\nDatabase saved.\\r\\n");
                        break;
                    case "QUIT":
                        _ = game.SendMessageAsync("\\r\\nDisconnecting...\\r\\n");
                        _ = manager.StopGameAsync(gameName);
                        break;
                    default:
                        _ = game.SendMessageAsync($"\\r\\nUnknown command: {e.Command}\\r\\nType $HELP$ for available commands\\r\\n");
                        break;
                }
            };

            game.Connected += (sender, e) =>
            {
                Console.WriteLine("** Connected to server");
                // In full integration: Database.OnConnected would update status
            };

            game.Disconnected += (sender, e) =>
            {
                Console.WriteLine($"** Disconnected: {e.Reason}");
                // In full integration: Database.OnDisconnected would handle reconnection
            };

            Console.WriteLine($"✓ Network started successfully!");
            Console.WriteLine($"✓ Listening on localhost:{listenPort}");
            Console.WriteLine($"✓ Connect your terminal client to localhost:{listenPort}");
            Console.WriteLine("\\nType $HELP$ for available commands");
            Console.WriteLine("Press Ctrl+C to exit\\n");

            Console.WriteLine($"Status: Connected={game.IsConnected}, Active={game.IsRunning}");
            Console.WriteLine();

            // Wait for Ctrl+C
            var tcs = new TaskCompletionSource<bool>();
            Console.CancelKeyPress += (sender, e) =>
            {
                e.Cancel = true;
                tcs.TrySetResult(true);
            };

            await tcs.Task;

            Console.WriteLine("\\nShutting down...");
            await manager.StopAllGamesAsync();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"ERROR: {ex.Message}");
            Console.WriteLine($"Stack trace: {ex.StackTrace}");
        }
        finally
        {
            manager.Dispose();
        }

        Console.WriteLine("Done!");
    }
}
