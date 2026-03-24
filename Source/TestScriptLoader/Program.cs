using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using TWXProxy.Core;

namespace TestScriptLoader
{
    class Program
    {
        static void Main(string[] args)
        {
            if (args.Length == 0)
            {
                Console.WriteLine("Usage: TestScriptLoader <script.cts>");
                Console.WriteLine();
                Console.WriteLine("Example: TestScriptLoader /path/to/script.cts");
                return;
            }

            string scriptPath = args[0];
            
            if (!File.Exists(scriptPath))
            {
                Console.WriteLine($"Error: File not found: {scriptPath}");
                return;
            }

            Console.WriteLine($"Testing script: {scriptPath}");
            Console.WriteLine($"File size: {new FileInfo(scriptPath).Length} bytes");
            Console.WriteLine();

            // Examine raw file header
            ExamineFileHeader(scriptPath);
            Console.WriteLine();

            // Try loading with ModInterpreter
            TestScriptLoad(scriptPath);
        }

        static void ExamineFileHeader(string filename)
        {
            Console.WriteLine("=== Raw File Header Examination ===");
            
            using (var fs = new FileStream(filename, FileMode.Open, FileAccess.Read))
            using (var reader = new BinaryReader(fs))
            {
                // Read first 32 bytes
                int bytesToRead = Math.Min(32, (int)fs.Length);
                byte[] headerBytes = reader.ReadBytes(bytesToRead);
                
                Console.WriteLine($"First {bytesToRead} bytes (hex):");
                for (int i = 0; i < headerBytes.Length; i++)
                {
                    if (i > 0 && i % 16 == 0)
                        Console.WriteLine();
                    Console.Write($"{headerBytes[i]:X2} ");
                }
                Console.WriteLine();
                Console.WriteLine();

                // Check for leading newline
                fs.Seek(0, SeekOrigin.Begin);
                byte firstByte = (byte)fs.ReadByte();
                
                if (firstByte == 0x0A || firstByte == 0x0D)
                {
                    Console.WriteLine($"Leading byte: 0x{firstByte:X2} (line break)");
                }
                else
                {
                    Console.WriteLine($"Leading byte: 0x{firstByte:X2} ('{(char)firstByte}')");
                    fs.Seek(0, SeekOrigin.Begin);
                }

                // Read header
                byte[] header = reader.ReadBytes(11);
                string progName = Encoding.ASCII.GetString(header).TrimEnd('\0');
                Console.WriteLine($"Header (11 bytes): '{progName}'");
                
                // Read version
                ushort version = reader.ReadUInt16();
                Console.WriteLine($"Version: {version}");
                
                // Read sizes
                int descSize = reader.ReadInt32();
                int codeSize = reader.ReadInt32();
                Console.WriteLine($"Desc Size: {descSize}");
                Console.WriteLine($"Code Size: {codeSize}");
                Console.WriteLine($"Stream position: {fs.Position}");
            }
        }

        static void TestScriptLoad(string filename)
        {
            Console.WriteLine("=== Testing ModInterpreter Load ===");
            
            // Set up a mock TWXServer to capture debug messages
            GlobalModules.TWXServer = new MockTWXServer();
            
            try
            {
                var interpreter = new ModInterpreter();
                interpreter.ProgramDir = AppContext.BaseDirectory;
                interpreter.ScriptDirectory = Path.GetDirectoryName(filename) ?? AppContext.BaseDirectory;
                
                Console.WriteLine($"Loading script...");
                interpreter.Load(filename, false);
                
                Console.WriteLine($"Success! Script loaded.");
                Console.WriteLine($"Script count: {interpreter.Count}");
                
                if (interpreter.Count > 0)
                {
                    var script = interpreter.GetScript(0);
                    if (script != null)
                    {
                        Console.WriteLine($"Script name: {script.ScriptName}");
                        Console.WriteLine($"Script paused: {script.Paused}");
                        
                        // Try executing
                        Console.WriteLine();
                        Console.WriteLine("Attempting to execute script...");
                        bool completed = script.Execute();
                        Console.WriteLine($"Execute() returned: {completed}");
                        
                        // Keep resuming paused scripts to allow multiple PAUSE/resume cycles
                        int resumeAttempts = 0;
                        while (script.Paused && resumeAttempts < 10)
                        {
                            resumeAttempts++;
                            Console.WriteLine();
                            Console.WriteLine($"Script paused (attempt {resumeAttempts}/10). Resuming...");
                            script.Resume();
                            completed = script.Execute();
                            Console.WriteLine($"Execute() returned: {completed}");
                            
                            if (script.Paused)
                            {
                                Console.WriteLine("Script paused again, waiting for delay timer (2 seconds)...");
                                System.Threading.Thread.Sleep(2000);
                            }
                        }
                        
                        if (script.Paused)
                        {
                            Console.WriteLine($"Script still paused after {resumeAttempts} resume attempts.");
                        }
                        else
                        {
                            Console.WriteLine("Script completed successfully.");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
                Console.WriteLine($"Stack trace: {ex.StackTrace}");
            }
        }
    }

    // Mock implementation of ITWXServer to capture debug messages
    public class MockTWXServer : ITWXServer
    {
        public void Broadcast(string message) => Console.Write(message);
        public void ClientMessage(string message) => Console.Write(message);
        public bool SendText(string text) => true;
        public void ClearText(int left, int top, int right, int bottom) { }
        public void Display(int left, int top, string text) { }
        public void Reposition(int left, int top, int right, int bottom) { }
        public void Show() { }
        public void ShowMenu() { }
        public void Hide() { }
        public void Update() { }
        public void ClearMsgBuffer() { }
        public int ProcessLines(int numberOfLines) => numberOfLines;
        public string ProcessText(string text) => text;
        public bool ScriptRunning() => false;
        public void UpdateMenu() { }
        
        // ITWXServer specific
        public void AddQuickText(string key, string value) { }
        public void ClearQuickText(string? key = null) { }
        public int ClientCount => 0;
        public ClientType GetClientType(int index) => ClientType.Standard;
        public void SetClientType(int index, ClientType type) { }
        public void RegisterBot(string botName, string scriptFile, string description = "") { }
        public void UnregisterBot(string botName) { }
        public List<string> GetBotList() => new List<string>();
        public BotConfig? GetBotConfig(string botName) => null;
        public string ActiveBotName { get; set; } = "";
        public object? GetActiveBot() => null;
        
        // IModServer properties
        public bool AllowLerkers { get; set; }
        public bool AcceptExternal { get; set; }
        public string ExternalAddress { get; set; } = "";
        public bool BroadCastMsgs { get; set; }
        public bool LocalEcho { get; set; }
    }
}
