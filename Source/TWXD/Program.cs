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

For source notes please refer to Notes.txt
For license terms please refer to GPL.txt.

These files should be stored in the root of the compression you 
received this source in.
*/

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using TWXProxy.Core;

namespace TWXD
{
    class Program
    {
        static string GetUniqueFilename(string baseName)
        {
            // If .ts doesn't exist, use it
            string filename = baseName + ".ts";
            if (!File.Exists(filename))
                return filename;

            // Otherwise try .ts_1, .ts_2, etc.
            int counter = 1;
            while (true)
            {
                filename = $"{baseName}.ts_{counter}";
                if (!File.Exists(filename))
                    return filename;
                counter++;
            }
        }

        static void Main(string[] args)
        {
            if (args.Length == 0)
            {
                // Display program description
                Console.WriteLine($"TWXD - TWX Proxy decompilation utility v{Constants.ProgramVersion}");
                Console.WriteLine("       (c) Matt Mosley (\"reaper\") 2026");
                Console.WriteLine();
                Console.WriteLine("Usage: TWXD script.cts");
                Console.WriteLine();
                Console.WriteLine("script.cts - Filename of the compiled script to be decompiled.");
                Console.WriteLine();
                Console.WriteLine("The decompiler will create a .ts file with the decompiled script.");
                Console.WriteLine("If the .ts file exists, it will use .ts_1, .ts_2, etc.");
                Console.WriteLine();
                return;
            }

            string inputFile = args[0];
            if (!File.Exists(inputFile))
            {
                Console.WriteLine($"Error: File '{inputFile}' not found.");
                return;
            }

            // Remove .cts extension if present
            string baseName = inputFile;
            if (baseName.EndsWith(".cts", StringComparison.OrdinalIgnoreCase))
                baseName = baseName.Substring(0, baseName.Length - 4);

            string outputFile = GetUniqueFilename(baseName);

            Console.WriteLine($"Decompiling '{inputFile}' to '{outputFile}' ...");

            try
            {
                var scriptRef = new ScriptRef();
                var decompiler = new ScriptDecompiler(scriptRef);
                
                decompiler.LoadFromFile(inputFile);
                decompiler.DecompileToFile(outputFile);

                Console.WriteLine("Decompilation successful.");
                Console.WriteLine();
                Console.WriteLine($"Output file: {outputFile}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
                
                // Delete output file on error if it was created
                if (File.Exists(outputFile))
                {
                    try
                    {
                        File.Delete(outputFile);
                    }
                    catch
                    {
                        // Ignore deletion errors
                    }
                }
            }
        }
    }
}
