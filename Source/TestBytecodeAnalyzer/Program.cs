using System;
using System.IO;
using TWXProxy.Core;

namespace TestBytecodeAnalyzer
{
    class Program
    {
        static void Main(string[] args)
        {
            string scriptFile = args.Length > 0 ? args[0] : "test_simple_setvar.ts";
            
            if (!File.Exists(scriptFile))
            {
                Console.WriteLine($"Error: Script file '{scriptFile}' not found.");
                return;
            }
            
            Console.WriteLine($"Analyzing compilation of: {scriptFile}");
            Console.WriteLine();
            
            try
            {
                var scriptRef = new ScriptRef();
                var scriptCmp = new ScriptCmp(scriptRef);
                
                // Compile
                scriptCmp.CompileFromFile(scriptFile, string.Empty);
                
                Console.WriteLine($"✓ Compilation successful!");
                Console.WriteLine($"Code size: {scriptCmp.CodeSize}");
                Console.WriteLine($"Parameters: {scriptCmp.ParamCount}");
                Console.WriteLine($"Commands: {scriptCmp.CmdCount}");
                Console.WriteLine();
                
                // List all parameters
                Console.WriteLine("Parameter List:");
                for (int i = 0; i < scriptCmp.ParamCount; i++)
                {
                    var param = scriptCmp.GetParam(i);
                    string paramInfo = $"  [{i}] {param.GetType().Name}";
                    if (param is VarParam vp)
                    {
                        paramInfo += $" Name='{vp.Name}' Value='{vp.Value}'";
                    }
                    else
                    {
                        paramInfo += $" Value='{param.Value}'";
                    }
                    Console.WriteLine(paramInfo);
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
}
