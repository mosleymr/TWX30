/*
Mock game server for testing TWX30 network proxy
Listens on a port and echoes back everything it receives with a prefix
*/

using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

class MockServer
{
    static async Task Main(string[] args)
    {
        if (args.Length < 1)
        {
            Console.WriteLine("Usage: MockServer <port>");
            Console.WriteLine("Example: MockServer 2002");
            return;
        }

        int port = int.Parse(args[0]);
        
        var listener = new TcpListener(IPAddress.Any, port);
        listener.Start();
        
        Console.WriteLine($"Mock Game Server listening on port {port}");
        Console.WriteLine("Waiting for connections...\n");

        var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (s, e) =>
        {
            e.Cancel = true;
            cts.Cancel();
        };

        try
        {
            while (!cts.Token.IsCancellationRequested)
            {
                var client = await listener.AcceptTcpClientAsync(cts.Token);
                Console.WriteLine($"Client connected: {client.Client.RemoteEndPoint}");
                
                // Handle client in background
                _ = Task.Run(async () => await HandleClientAsync(client, cts.Token));
            }
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine("\nShutting down...");
        }
        finally
        {
            listener.Stop();
        }
    }

    static async Task HandleClientAsync(TcpClient client, CancellationToken token)
    {
        var stream = client.GetStream();
        var buffer = new byte[8192];

        try
        {
            // Send welcome message
            var welcome = "Welcome to Mock Game Server!\r\nType anything and it will be echoed back.\r\n\r\n";
            var welcomeBytes = Encoding.ASCII.GetBytes(welcome);
            await stream.WriteAsync(welcomeBytes, 0, welcomeBytes.Length, token);
            await stream.FlushAsync(token);

            while (!token.IsCancellationRequested)
            {
                int bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length, token);
                
                if (bytesRead == 0)
                {
                    Console.WriteLine("Client disconnected");
                    break;
                }

                var received = Encoding.ASCII.GetString(buffer, 0, bytesRead);
                Console.WriteLine($"Received: {received.Replace("\r", "\\r").Replace("\n", "\\n")}");

                // Echo back with prefix
                var response = $"[SERVER ECHO] {received}";
                var responseBytes = Encoding.ASCII.GetBytes(response);
                await stream.WriteAsync(responseBytes, 0, responseBytes.Length, token);
                await stream.FlushAsync(token);
                
                Console.WriteLine($"Sent: {response.Replace("\r", "\\r").Replace("\n", "\\n")}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error handling client: {ex.Message}");
        }
        finally
        {
            client.Close();
        }
    }
}
