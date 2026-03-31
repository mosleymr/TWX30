/*
Copyright (C) 2005  Remco Mulder

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
using System.IO;
using TWXProxy.Core;

namespace TWXC
{
    class Program
    {
        static string StripFileExtension(string filename)
        {
            int lastDot = filename.LastIndexOf('.');
            if (lastDot > 0)
                return filename.Substring(0, lastDot);
            return filename;
        }

        static string GetUniqueArchiveFilename(string ctsFile)
        {
            if (!File.Exists(ctsFile))
                return ctsFile;

            string baseName = ctsFile;
            int counter = 1;
            while (true)
            {
                string filename = $"{baseName}_{counter}";
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
                Console.WriteLine($"TWXC - TWX Proxy command line script compilation utility v{Constants.ProgramVersion}");
                Console.WriteLine("       (c) Remco Mulder (\"Xide\") 2002-2004");
                Console.WriteLine("       (c) Matt Mosley (\"reaper\") 2026");
                Console.WriteLine();
                Console.WriteLine("Usage: TWXC script [descfile]");
                Console.WriteLine();
                Console.WriteLine("script     - Filename of the script to be compiled, this is usually a .ts file.");
                Console.WriteLine("[descfile] - Optional filename of a description text file to be included in the");
                Console.WriteLine("             compilation.");
                Console.WriteLine("             Description files have no effect on the operation of the script,");
                Console.WriteLine("             but may provide useful information to users.");
                Console.WriteLine();
                Console.WriteLine("If the target .cts file already exists and compilation succeeds,");
                Console.WriteLine("TWXC archives the previous file to .cts_1, .cts_2, etc. and writes");
                Console.WriteLine("the new output to the current .cts filename.");
                Console.WriteLine();
                return;
            }

            var scriptRef = new ScriptRef();
            var scriptCmp = new ScriptCmp(scriptRef);
            bool compileOk = false;
            string fileOut = StripFileExtension(args[0]);
            
            Console.WriteLine($"Compiling script '{fileOut}' ...");

            try
            {
                string descFile = args.Length > 1 ? args[1] : string.Empty;
                scriptCmp.CompileFromFile(args[0], descFile);
                compileOk = true;
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
            }

            if (compileOk)
            {
                string ctsFile = fileOut + ".cts";
                string outputDir = Path.GetDirectoryName(Path.GetFullPath(ctsFile)) ?? Directory.GetCurrentDirectory();
                string tempFile = Path.Combine(outputDir, $".{Path.GetFileName(ctsFile)}.{Guid.NewGuid():N}.tmp");
                string? archivedFile = null;

                try
                {
                    scriptCmp.WriteToFile(tempFile);

                    if (File.Exists(ctsFile))
                    {
                        archivedFile = GetUniqueArchiveFilename(ctsFile);
                        File.Move(ctsFile, archivedFile);
                    }

                    File.Move(tempFile, ctsFile);
                }
                catch
                {
                    try
                    {
                        if (File.Exists(tempFile))
                            File.Delete(tempFile);
                    }
                    catch
                    {
                    }

                    if (archivedFile != null && File.Exists(archivedFile) && !File.Exists(ctsFile))
                    {
                        try
                        {
                            File.Move(archivedFile, ctsFile);
                        }
                        catch
                        {
                        }
                    }

                    throw;
                }

                Console.WriteLine("Compilation successful.");
                Console.WriteLine();
                Console.WriteLine($"Output file: {ctsFile}");
                if (archivedFile != null)
                    Console.WriteLine($"Archived previous output: {archivedFile}");
                Console.WriteLine($"Code Size: {scriptCmp.CodeSize}");
                Console.WriteLine($"Lines: {scriptCmp.LineCount}");
                Console.WriteLine($"Definitions: {scriptCmp.ParamCount}");
                Console.WriteLine($"Commands: {scriptCmp.CmdCount}");
            }

            scriptCmp.Dispose();
        }
    }
}
