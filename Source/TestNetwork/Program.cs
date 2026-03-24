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
using System.Threading.Tasks;
using TWXProxy.Core;

class Program
{
    static async Task Main(string[] args)
    {
        Console.WriteLine("TWX30 Network Module Test");
        Console.WriteLine("========================\n");

        // Create network manager
        var manager = new NetworkManager();

        try
        {
            // Example: Connect to a Trade Wars game server
            // Replace with actual server details
            string serverAddress = "twgs.com";  // Example TW game server
            int serverPort = 2002;
            int listenPort = 2602;
            string gameName = "Test Game";

            Console.WriteLine($"Starting game instance: {gameName}");
            Console.WriteLine($"  Server: {serverAddress}:{serverPort}");
            Console.WriteLine($"  Listen: localhost:{listenPort}");
            Console.WriteLine($"  Command character: $");
            Console.WriteLine();

            // Start the game instance
            var game = await manager.StartGameAsync(gameName, serverAddress, serverPort, listenPort);

            // Hook up event handlers to monitor traffic
            game.ServerDataReceived += (sender, e) =>
            {
                Console.WriteLine($"<- Server: {e.Data.Length} bytes");
            };

            game.LocalDataReceived += (sender, e) =>
            {
                Console.WriteLine($"-> Local: {e.Text}");
            };

            game.CommandReceived += (sender, e) =>
            {
                Console.WriteLine($"** Command: {e.Command}");
                
                // Handle some basic commands
                switch (e.Command.ToUpper())
                {
                    case "HELP":
                        _ = game.SendMessageAsync("\r\nAvailable commands:\r\n  HELP - Show this help\r\n  STATUS - Show connection status\r\n  QUIT - Disconnect\r\n\r\n");
                        break;
                    case "STATUS":
                        var status = $"\r\nGame: {game.GameName}\r\nConnected: {game.IsConnected}\r\nRunning: {game.IsRunning}\r\n\r\n";
                        _ = game.SendMessageAsync(status);
                        break;
                    case "QUIT":
                        _ = game.SendMessageAsync("\r\nDisconnecting...\r\n");
                        _ = manager.StopGameAsync(gameName);
                        break;
                    default:
                        _ = game.SendMessageAsync($"\r\nUnknown command: {e.Command}\r\n");
                        break;
                }
            };

            game.Connected += (sender, e) =>
            {
                Console.WriteLine("** Connected to server");
            };

            game.Disconnected += (sender, e) =>
            {
                Console.WriteLine($"** Disconnected: {e.Reason}");
            };

            Console.WriteLine("Game instance started successfully!");
            Console.WriteLine($"Connect your terminal client to localhost:{listenPort}");
            Console.WriteLine("Type $HELP$ for available commands");
            Console.WriteLine("Press Ctrl+C to exit\n");

            // Wait indefinitely until Ctrl+C
            var tcs = new TaskCompletionSource<bool>();
            Console.CancelKeyPress += (sender, e) =>
            {
                e.Cancel = true;
                tcs.TrySetResult(true);
            };

            await tcs.Task;

            Console.WriteLine("\nShutting down...");
            await manager.StopAllGamesAsync();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error: {ex.Message}");
            Console.WriteLine($"Stack trace: {ex.StackTrace}");
            return;
        }
        finally
        {
            manager.Dispose();
        }

        Console.WriteLine("Done!");
    }
}
