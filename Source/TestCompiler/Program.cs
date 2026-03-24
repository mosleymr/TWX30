using System;
using System.Collections.Generic;
using System.IO;
using TWXProxy.Core;

namespace TestCompiler
{
    class Program
    {
        static void Main(string[] args)
        {
            string scriptFile = args.Length > 0 ? args[0] : "test_compile.ts";
            
            if (!File.Exists(scriptFile))
            {
                Console.WriteLine($"Error: Script file '{scriptFile}' not found.");
                return;
            }
            
            Console.WriteLine($"Testing compilation of: {scriptFile}");
            Console.WriteLine();
            
            try
            {
                // Create a mock TWX server for output
                GlobalModules.TWXServer = new MockTWXServer();
                
                // Create interpreter and load script
                var interpreter = new ModInterpreter();
                interpreter.ProgramDir = Directory.GetCurrentDirectory();
                interpreter.ScriptDirectory = Path.GetDirectoryName(scriptFile) ?? Directory.GetCurrentDirectory();
                
                Console.WriteLine("Loading and compiling script...");
                interpreter.Load(scriptFile, false);
                
                Console.WriteLine();
                Console.WriteLine("✓ Compilation successful!");
                Console.WriteLine($"Script count: {interpreter.Count}");
                
                if (interpreter.Count > 0)
                {
                    var script = interpreter.GetScript(0);
                    Console.WriteLine($"Script paused: {script?.Paused}");
                    
                    if (script?.Compiler != null)
                    {
                        Console.WriteLine($"Code size: {script.Compiler.CodeSize}");
                        Console.WriteLine($"Line count: {script.Compiler.LineCount}");
                        Console.WriteLine($"Parameters: {script.Compiler.ParamCount}");
                        Console.WriteLine($"Commands: {script.Compiler.CmdCount}");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"✗ Error: {ex.Message}");
                Console.WriteLine();
                Console.WriteLine($"Stack trace:\n{ex.StackTrace}");
                Environment.Exit(1);
            }
        }
    }
    
    class MockTWXServer : ITWXServer
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
