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
using System.Linq;
using System.Text;
using TWXProxy.Core;

namespace TWXD
{
    public class ScriptDecompiler
    {
        // Original TWX command order (from Pascal TWX 2.x)
        private static readonly string[] OriginalCommandNames = new[]
        {
            "ADD", "ADDMENU", "AND", "BRANCH", "CLIENTMESSAGE", "CLOSEMENU", "CONNECT",
            "CUTTEXT", "DELETE", "DISCONNECT", "DIVIDE", "ECHO", "FILEEXISTS", "GETCHARCODE",
            "GETCONSOLEINPUT", "GETCOURSE", "GETDATE", "GETDISTANCE", "GETINPUT", "GETLENGTH",
            "GETMENUVALUE", "GETOUTTEXT", "GETRND", "GETSECTOR", "GETSECTORPARAMETER", "GETTEXT",
            "GETTIME", "GOSUB", "GOTO", "GETWORD", "GETWORDPOS", "HALT", "ISEQUAL", "ISGREATER",
            "ISGREATEREQUAL", "ISLESSER", "ISLESSEREQUAL", "ISNOTEQUAL", "ISNUMBER", "KILLWINDOW",
            "KILLALLTRIGGERS", "KILLTRIGGER", "LOAD", "LOADVAR", "LOGGING", "LOWERCASE", "MERGETEXT",
            "MULTIPLY", "OPENMENU", "OR", "PAUSE", "PROCESSIN", "PROCESSOUT", "READ", "RENAME",
            "REPLACETEXT", "REQRECORDING", "RETURN", "ROUND", "SAVEVAR", "SEND", "SETARRAY",
            "SETDELAYTRIGGER", "SETEVENTTRIGGER", "SETMENUHELP", "SETMENUVALUE", "SETMENUOPTIONS",
            "SETPRECISION", "SETPROGVAR", "SETSECTORPARAMETER", "SETTEXTLINETRIGGER", "SETTEXTOUTTRIGGER",
            "SETTEXTTRIGGER", "SETVAR", "SETWINDOWCONTENTS", "SOUND", "STOP", "STRIPTEXT", "SUBTRACT",
            "SYS_CHECK", "SYS_FAIL", "SYS_KILL", "SYS_NOAUTH", "SYS_NOP", "SYS_SHOWMSG", "SYSTEMSCRIPT",
            "UPPERCASE", "XOR", "WAITFOR", "WINDOW", "WRITE", "GETTIMER", "READTOARRAY", "CLEARALLAVOIDS",
            "CLEARAVOID", "GETALLCOURSES", "GETFILELIST", "GETNEARESTWARPS", "GETSCRIPTVERSION",
            "LISTACTIVESCRIPTS", "LISTAVOIDS", "LISTSECTORPARAMETERS", "SETAVOID", "CUTLENGTHS", "FORMAT",
            "GETDIRLIST", "GETWORDCOUNT", "MAKEDIR", "PADLEFT", "PADRIGHT", "REMOVEDIR", "SETMENUKEY",
            "SPLITTEXT", "TRIM", "TRUNCATE", "GETDEAFCLIENTS", "SILENCECLIENTS", "SAVEGLOBAL", "LOADGLOBAL",
            "CLEARGLOBALS",
            // TWX30 additions (IDs 120+)
            "WAITON", "ECHOEX", "DIAGLOG", "DIAGMODE", "DIREXISTS", "SORT", "FIND", "FINDALL",
            "STRIPANSI", "STOPALL", "LABELEXISTS", "REQVERSION", "LISTGLOBALS", "ADDQUICKTEXT",
            "CLEARQUICKTEXT", "SAVEHELP", "COPYDATABASE", "CREATEDATABASE", "DELETEDATABASE",
            "EDITDATABASE", "LISTDATABASES", "OPENDATABASE", "CLOSEDATABASE", "RESETDATABASE",
            "SWITCHBOT", "GETBOTLIST", "SETDEAFCLIENTS", "OPENINSTANCE", "CLOSEINSTANCE", "LIBCMD",
            "SETAUTOTRIGGER", "SETAUTOTEXTTRIGGER", "STARTTIMER", "STOPTIMER", "MODULUS", "CONCAT",
            // TWX27 commands missing from original port (IDs 156+)
            "GETDATETIME", "DATETIMEDIFF", "DATETIMETOSTR", "CENTER", "REPEAT",
        };

        // Original TWX sysconst order (from Pascal TWX 2.x)
        private static readonly string[] OriginalSysConstNames = new[]
        {
            "ANSI_0", "ANSI_1", "ANSI_2", "ANSI_3", "ANSI_4", "ANSI_5", "ANSI_6", "ANSI_7",
            "ANSI_8", "ANSI_9", "ANSI_10", "ANSI_11", "ANSI_12", "ANSI_13", "ANSI_14", "ANSI_15",
            "CONNECTED", "CURRENTANSILINE", "CURRENTLINE", "DATE", "FALSE", "GAME", "GAMENAME",
            "LICENSENAME", "LOGINNAME", "PASSWORD", "PORT.CLASS", "PORT.BUYFUEL", "PORT.BUYORG",
            "PORT.BUYEQUIP", "PORT.EXISTS", "PORT.FUEL", "PORT.NAME", "PORT.ORG", "PORT.EQUIP",
            "PORT.PERCENTFUEL", "PORT.PERCENTORG", "PORT.PERCENTEQUIP", "SECTOR.ANOMOLY",
            "SECTOR.BACKDOORCOUNT", "SECTOR.BACKDOORS", "SECTOR.DENSITY", "SECTOR.EXPLORED",
            "SECTOR.FIGS.OWNER", "SECTOR.FIGS.QUANTITY", "SECTOR.LIMPETS.OWNER",
            "SECTOR.LIMPETS.QUANTITY", "SECTOR.MINES.OWNER", "SECTOR.MINES.QUANTITY",
            "SECTOR.NAVHAZ", "SECTOR.PLANETCOUNT", "SECTOR.PLANETS", "SECTOR.SHIPCOUNT",
            "SECTOR.SHIPS", "SECTOR.TRADERCOUNT", "SECTOR.TRADERS", "SECTOR.UPDATED",
            "SECTOR.WARPCOUNT", "SECTOR.WARPS", "SECTOR.WARPSIN", "SECTOR.WARPINCOUNT",
            "SECTORS", "STARDOCK", "TIME", "TRUE",
            // Added in 2.04
            "ALPHACENTAURI", "CURRENTSECTOR", "RYLOS",
            // Added in 2.04a
            "PORT.BUILDTIME", "PORT.UPDATED", "RAWPACKET", "SECTOR.BEACON",
            "SECTOR.CONSTELLATION", "SECTOR.FIGS.TYPE", "SECTOR.ANOMALY"
        };
        
        private ScriptRef _scriptRef;
        private byte[] _code = Array.Empty<byte>();
        private List<CmdParam> _paramList = new List<CmdParam>();
        private List<ScriptLabel> _labelList = new List<ScriptLabel>();
        private List<string> _includeScriptList = new List<string>();
        private string _description = string.Empty;
        private int _codeSize;
        private int _codePos;

        public ScriptDecompiler(ScriptRef scriptRef)
        {
            _scriptRef = scriptRef;
        }

        public void LoadFromFile(string filename)
        {
            // Delegate entirely to ScriptCmp so that header parsing, version
            // validation, param/label/include reading is identical in both
            // TWXP and twxd — no duplicated CTS-reading logic.
            var cmp = new ScriptCmp(_scriptRef);
            cmp.LoadFromFile(filename);

            _code             = cmp.Code;
            _codeSize         = cmp.CodeSize;
            _paramList        = cmp.ParamList;
            _labelList        = cmp.LabelList;
            _includeScriptList = cmp.IncludeScriptList;
            _description      = cmp.DescriptionText;
        }

        private string ApplyEncryption(string text, int key)
        {
            var result = new StringBuilder();
            foreach (char c in text)
            {
                result.Append((char)(c ^ key));
            }
            return result.ToString();
        }

        public void DecompileToFile(string filename)
        {
            using (var output = new StreamWriter(filename))
            {
                // Add description as comments
                if (!string.IsNullOrEmpty(_description))
                {
                    foreach (var line in _description.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None))
                    {
                        output.WriteLine($"# {line}");
                    }
                    output.WriteLine();
                }

                var branchLabels = new HashSet<string>();
                var tempVars = new Dictionary<string, string>(); // Track $$1, $$2, etc.
                var seenLabels = new HashSet<string>(); // Track which labels we've already processed
                int indent = 0;
                bool pendingElse = false;    // forward GOTO to internal numeric label was just seen
                bool lineelse = false;       // else-start label passed; next BRANCH → elseif, next real cmd → else
                bool lastWasLoopLabel = false; // WHILE-start internal label seen; next BRANCH → while
                bool branchend = false;      // just emitted 'end'; next internal numeric label is EndLabel, skip it
                string pendingElseLabel = ""; // the ConLabel consumed by pendingElse; ELSEIF must remove it from branchLabels
                
                _codePos = 0;
                
                while (_codePos < _codeSize)
                {
                    int commandStart = _codePos;
                    
                    // Read script ID (1 byte)
                    if (_codePos >= _codeSize) break;
                    byte scriptID = _code[_codePos++];
                    
                    // Read line number (2 bytes)
                    if (_codePos + 2 > _codeSize) break;
                    ushort lineNum = BitConverter.ToUInt16(_code, _codePos);
                    _codePos += 2;
                    
                    // Read command ID (2 bytes)
                    if (_codePos + 2 > _codeSize) break;
                    ushort cmdID = BitConverter.ToUInt16(_code, _codePos);
                    _codePos += 2;
                    
                    // Check for labels at this bytecode position
                    foreach (var label in _labelList)
                    {
                        if (label.Location == commandStart)
                        {
                            string labelName = label.Name;
                            seenLabels.Add(labelName);

                            // Internal numeric label (branch targets, loop markers, end labels)
                            bool isNumericLabel = labelName.StartsWith(":") && labelName.Length > 1 &&
                                                  labelName.Substring(1).All(char.IsDigit);

                            if (isNumericLabel)
                            {
                                if (pendingElse)
                                {
                                    // A forward GOTO to this label was seen → else/elseif body starts here.
                                    // pendingElse always takes priority over branchend/branchLabels so
                                    // that a while 'end' at the same position doesn't shadow the else-start.
                                    // DO NOT remove this label from branchLabels — for ELSE it will be
                                    // encountered again at the HandleEnd position where it emits 'end'.
                                    // For ELSEIF, the BRANCH handler will clean it up from branchLabels.
                                    pendingElse = false;
                                    lineelse = true;
                                    pendingElseLabel = labelName;
                                    lastWasLoopLabel = false;
                                }
                                else if (branchLabels.Contains(labelName))
                                {
                                    // ConLabel reached at HandleEnd position — close this block.
                                    branchLabels.Remove(labelName);
                                    if (lineelse)
                                    {
                                        indent--;
                                        output.WriteLine($"{Indent(indent)}else");
                                        indent++;
                                        lineelse = false;
                                        pendingElseLabel = "";
                                    }
                                    indent--;
                                    output.WriteLine($"{Indent(indent)}end");
                                    branchend = true; // next label at this position is EndLabel, skip it
                                    lastWasLoopLabel = false;
                                }
                                else if (branchend)
                                {
                                    // This is the EndLabel that pairs with the ConLabel that just
                                    // emitted 'end'. Silently consume it.
                                    branchend = false;
                                    lastWasLoopLabel = false;
                                }
                                else
                                {
                                    // Not a branch-close or else-start: this is a WHILE loop start.
                                    lastWasLoopLabel = true;
                                }
                            }
                            else
                            {
                                // User-defined label
                                branchend = false;
                                output.WriteLine($":{labelName}");
                            }
                        }
                    }
                    
                    // Get command name
                    string cmdName;
                    if (cmdID >= 0 && cmdID < OriginalCommandNames.Length)
                    {
                        cmdName = OriginalCommandNames[cmdID];
                    }
                    else
                    {
                        cmdName = $"UNKNOWN_{cmdID}";
                    }

                    var parts = new List<string> { cmdName };
                    
                    // Read parameters until PARAM_CMD (0) marker
                    int paramCount = 0;
                    while (_codePos < _codeSize)
                    {
                        byte paramType = _code[_codePos];
                        if (paramType == ScriptConstants.PARAM_CMD)
                        {
                            _codePos++; // Skip end marker
                            break;
                        }
                        
                        string param = DecompileParameter();
                        if (!string.IsNullOrEmpty(param))
                        {
                            parts.Add(param);
                            paramCount++;
                        }
                    }
                    
                    // Handle macro expansion and control flow reconstruction
                    bool skipOutput = false;
                    
                    // Comparison operators
                    if (cmdName == "ISEQUAL" || cmdName == "ISNOTEQUAL" || cmdName == "ISGREATER" || 
                        cmdName == "ISGREATEREQUAL" || cmdName == "ISLESSER" || cmdName == "ISLESSEREQUAL")
                    {
                        if (paramCount >= 3)
                        {
                            string target = parts[1];
                            string left = parts[2];
                            string right = parts[3];
                            
                            string op = cmdName switch
                            {
                                "ISEQUAL" => " = ",
                                "ISNOTEQUAL" => " <> ",
                                "ISGREATER" => " > ",
                                "ISGREATEREQUAL" => " >= ",
                                "ISLESSER" => " < ",
                                "ISLESSEREQUAL" => " <= ",
                                _ => " ? "
                            };
                            
                            // Store for later expansion
                            if (target.StartsWith("$$"))
                            {
                                string expandedLeft = ExpandTempVars(left, tempVars);
                                string expandedRight = ExpandTempVars(right, tempVars);
                                tempVars[target] = $"({expandedLeft}{op}{expandedRight})";
                                skipOutput = true;
                            }
                        }
                    }
                    // Arithmetic operators
                    else if (cmdName == "ADD" || cmdName == "SUBTRACT" || cmdName == "MULTIPLY" || cmdName == "DIVIDE")
                    {
                        if (paramCount >= 3)
                        {
                            string target = parts[1];
                            string left = parts[2];
                            string right = parts[3];
                            
                            string op = cmdName switch
                            {
                                "ADD" => " + ",
                                "SUBTRACT" => " - ",
                                "MULTIPLY" => " * ",
                                "DIVIDE" => " / ",
                                _ => " ? "
                            };
                            
                            if (target.StartsWith("$$"))
                            {
                                string expandedLeft = ExpandTempVars(left, tempVars);
                                string expandedRight = ExpandTempVars(right, tempVars);
                                tempVars[target] = $"({expandedLeft}{op}{expandedRight})";
                                skipOutput = true;
                            }
                        }
                    }
                    // Boolean operators: AND/OR are 2-param accumulator instructions.
                    // Compiled as: SETVAR $$cond $$left  then  OR $$cond $$right
                    // So parts[1]=target/accumulator (already holds left), parts[2]=right.
                    else if (cmdName == "AND" || cmdName == "OR")
                    {
                        if (paramCount >= 2)
                        {
                            string target = parts[1];
                            string right = parts[2];
                            
                            string op = cmdName == "AND" ? " and " : " or ";
                            
                            if (target.StartsWith("$$"))
                            {
                                string expandedLeft = ExpandTempVars(target, tempVars);
                                string expandedRight = ExpandTempVars(right, tempVars);
                                tempVars[target] = $"({expandedLeft}{op}{expandedRight})";
                                skipOutput = true;
                            }
                        }
                    }
                    // SETVAR for temp variables
                    else if (cmdName == "SETVAR" && paramCount >= 2)
                    {
                        string target = parts[1];
                        string value = parts[2];
                        
                        if (target.StartsWith("$$"))
                        {
                            tempVars[target] = ExpandTempVars(value, tempVars);
                            skipOutput = true;
                        }
                    }
                    // MERGETEXT: dest = src1 + src2
                    // When dest is a $$N temp var, accumulate into tempVars so the
                    // consuming SEND/ECHO/etc expands it inline.  For real-var dests
                    // fall through to regular output (with $$N params already expanded).
                    else if (cmdName == "MERGETEXT" && paramCount >= 3)
                    {
                        string src1 = parts[1];
                        string src2 = parts[2];
                        string dest = parts[3];

                        if (dest.StartsWith("$$"))
                        {
                            string exp1 = ExpandTempVars(src1, tempVars);
                            string exp2 = ExpandTempVars(src2, tempVars);
                            tempVars[dest] = $"{exp1} {exp2}";
                            skipOutput = true;
                        }
                    }
                    // BRANCH
                    else if (cmdName == "BRANCH" && paramCount >= 2)
                    {
                        string condition = parts[1];
                        string label = parts[2].Trim('"', ':');
                        string fullLabel = ":" + label;
                        
                        // Expand temporary variables
                        condition = ExpandTempVars(condition, tempVars);
                        
                        // Clear condition temp vars so they don't pollute subsequent instructions
                        tempVars.Clear();
                        
                        bool isWhileLoop = lastWasLoopLabel;
                        lastWasLoopLabel = false;
                        
                        if (lineelse)
                        {
                            // ELSEIF: the false-jump target goes into branchLabels so it emits
                            // 'end' when reached at HandleEnd position.
                            // The old ConLabel (pendingElseLabel) was left in branchLabels by
                            // the pendingElse handler, but HandleEnd for ELSEIF uses the NEW
                            // ConLabel, not the old one. Remove the stale old ConLabel now.
                            if (!string.IsNullOrEmpty(pendingElseLabel))
                            {
                                branchLabels.Remove(pendingElseLabel);
                                pendingElseLabel = "";
                            }
                            branchLabels.Add(fullLabel);
                            indent--;
                            output.WriteLine($"{Indent(indent)}elseif {condition}");
                            indent++;
                            lineelse = false;
                        }
                        else if (isWhileLoop)
                        {
                            branchLabels.Add(fullLabel);
                            output.WriteLine($"{Indent(indent)}while {condition}");
                            indent++;
                        }
                        else
                        {
                            branchLabels.Add(fullLabel);
                            output.WriteLine($"{Indent(indent)}if {condition}");
                            indent++;
                        }
                        skipOutput = true;
                    }
                    // GOTO
                    else if (cmdName == "GOTO" && paramCount >= 1)
                    {
                        string label = parts[1].Trim('"', ':');
                        string fullLabel = ":" + label; // Labels are stored with : prefix
                        
                        // Check if it's an internal numeric label (all digits)
                        bool isInternalLabel = label.Length > 0 && label.All(char.IsDigit);
                        
                        // Check if this is a backward jump (loop)
                        bool isBackwardJump = seenLabels.Contains(fullLabel);
                        
                        if (isInternalLabel)
                        {
                            if (isBackwardJump)
                            {
                                // Backward jump to internal label = while loop back, suppress
                                skipOutput = true;
                                pendingElse = false;
                                lineelse = false;
                            }
                            else
                            {
                                // Forward jump to internal label = else/elseif body coming
                                pendingElse = true;
                                skipOutput = true;
                            }
                        }
                        else
                        {
                            // User goto — emit with : prefix
                            output.WriteLine($"{Indent(indent)}goto :{label}");
                            skipOutput = true;
                        }
                    }
                    
                    // Write regular command
                    if (!skipOutput)
                    {
                        // If else is pending, emit it now before the first real command of the else body.
                        // This is done here (after skipOutput check) so that temp-var condition
                        // commands compiled for ELSEIF don't trigger a premature `else` output.
                        if (lineelse)
                        {
                            // ELSE case: the old ConLabel (pendingElseLabel) stays in branchLabels
                            // because HandleEnd for ELSE emits it a 2nd time to close the block.
                            // Just clear the saved label; do NOT remove it from branchLabels.
                            pendingElseLabel = "";
                            indent--;
                            output.WriteLine($"{Indent(indent)}else");
                            indent++;
                            lineelse = false;
                        }
                        lastWasLoopLabel = false; // a real command ran — not immediately after loop label

                        // Expand temp vars in parameters
                        for (int i = 1; i < parts.Count; i++)
                        {
                            parts[i] = ExpandTempVars(parts[i], tempVars);
                        }

                        // Trigger name (parts[1]) is always an unquoted identifier
                        if (parts.Count >= 2 && (
                            cmdName == "KILLTRIGGER" ||
                            cmdName == "SETTEXTTRIGGER" ||
                            cmdName == "SETTEXTOUTTRIGGER" ||
                            cmdName == "SETTEXTLINETRIGGER" ||
                            cmdName == "SETEVENTTRIGGER" ||
                            cmdName == "SETDELAYTRIGGER"))
                        {
                            parts[1] = parts[1].Trim('"');
                        }
                        
                        output.WriteLine($"{Indent(indent)}{string.Join(" ", parts)}");
                    }
                }
                
                // Close any remaining open blocks
                while (indent > 0)
                {
                    indent--;
                    output.WriteLine($"{Indent(indent)}end");
                }
            }
        }
        
        private string ExpandTempVars(string text, Dictionary<string, string> tempVars)
        {
            // Replace $$1, $$2, etc. with their stored values
            foreach (var kvp in tempVars)
            {
                if (text.Contains(kvp.Key))
                {
                    text = text.Replace(kvp.Key, kvp.Value);
                }
            }
            return text;
        }
        
        private string Indent(int level)
        {
            return new string(' ', level * 2);
        }

        private string DecompileCommand()
        {
            if (_codePos >= _codeSize)
                return string.Empty;

            // Read command type (should be PARAM_CMD)
            byte cmdType = _code[_codePos++];
            if (cmdType != ScriptConstants.PARAM_CMD)
            {
                // Not a command marker, skip
                return string.Empty;
            }

            // Read command ID
            byte cmdID = _code[_codePos++];

            // Look up command name
            string cmdName = _scriptRef.GetCommandName(cmdID);
            if (string.IsNullOrEmpty(cmdName))
            {
                cmdName = $"UNKNOWN_{cmdID}";
            }

            var parts = new List<string> { cmdName };

            // Read parameters until we hit the next command or end of code
            while (_codePos < _codeSize && _code[_codePos] != ScriptConstants.PARAM_CMD)
            {
                string param = DecompileParameter();
                if (!string.IsNullOrEmpty(param))
                {
                    parts.Add(param);
                }
            }

            return string.Join(" ", parts);
        }

        private string DecompileParameter()
        {
            if (_codePos >= _codeSize)
                return string.Empty;

            byte paramType = _code[_codePos++];

            switch (paramType)
            {
                case ScriptConstants.PARAM_VAR:
                    {
                        // Variable reference $var
                        if (_codePos + 4 > _codeSize)
                            return "$ERROR_VAR";
                            
                        int paramIndex = BitConverter.ToInt32(_code, _codePos);
                        _codePos += 4;

                        // Read array indexes
                        string indexes = ReadArrayIndexes();

                        if (paramIndex >= 0 && paramIndex < _paramList.Count && _paramList[paramIndex] is VarParam varParam)
                        {
                            string name = varParam.Name;
                            // Check if name already has $ prefix
                            if (!name.StartsWith("$"))
                                name = "$" + name;

                            // Pascal source uses $ARRAY[$IDX].FIELD notation, but the compiler
                            // stores the variable as name="ARRAY.FIELD" with index [$IDX].  
                            // Reconstruct the original notation: move indexes before the first dot.
                            if (!string.IsNullOrEmpty(indexes))
                            {
                                int dotPos = name.IndexOf('.', 1); // skip leading '$'
                                if (dotPos > 0)
                                {
                                    string basePart = name.Substring(0, dotPos); // "$ARRAY"
                                    string suffix   = name.Substring(dotPos);    // ".FIELD"
                                    return basePart + indexes + suffix;
                                }
                            }

                            return name + indexes;
                        }
                        return $"$VAR_{paramIndex}{indexes}";
                    }

                case ScriptConstants.PARAM_PROGVAR:
                    {
                        // Program variable %var
                        if (_codePos + 4 > _codeSize)
                            return "%ERROR_VAR";
                            
                        int paramIndex = BitConverter.ToInt32(_code, _codePos);
                        _codePos += 4;

                        // Read array indexes
                        string indexes = ReadArrayIndexes();

                        if (paramIndex >= 0 && paramIndex < _paramList.Count && _paramList[paramIndex] is VarParam varParam)
                        {
                            string name = varParam.Name;
                            // Check if name already has % prefix
                            if (!name.StartsWith("%"))
                                name = "%" + name;
                            return name + indexes;
                        }
                        return $"%VAR_{paramIndex}{indexes}";
                    }

                case ScriptConstants.PARAM_CONST:
                    {
                        // Constant value (string or number) - NO INDEX COUNT BYTE
                        if (_codePos + 4 > _codeSize)
                            return "\"ERROR_CONST\"";
                            
                        int paramIndex = BitConverter.ToInt32(_code, _codePos);
                        _codePos += 4;

                        if (paramIndex >= 0 && paramIndex < _paramList.Count)
                        {
                            var param = _paramList[paramIndex];
                            string value = param is VarParam vp ? vp.Value : ((CmdParam)param).Value;

                            // Escape special characters
                            value = EscapeSpecialChars(value);

                            // Check if it needs quotes (contains spaces or special chars)
                            if (NeedsQuotes(value))
                            {
                                return $"\"{value}\"";
                            }
                            return value;
                        }
                        return $"CONST_{paramIndex}";
                    }

                case ScriptConstants.PARAM_SYSCONST:
                    {
                        // System constant
                        if (_codePos + 2 > _codeSize)
                            return "ERROR_SYSCONST";
                            
                        ushort constID = BitConverter.ToUInt16(_code, _codePos);
                        _codePos += 2;

                        // Read array indexes
                        string indexes = ReadArrayIndexes();

                        // Use original TWX sysconst order
                        string constName;
                        if (constID >= 0 && constID < OriginalSysConstNames.Length)
                        {
                            constName = OriginalSysConstNames[constID];
                        }
                        else
                        {
                            constName = $"SYSCONST_{constID}";
                        }
                        
                        return constName + indexes;
                    }

                case ScriptConstants.PARAM_CHAR:
                    {
                        // Character code: 1 byte (the char code itself, NOT a param list ref)
                        if (_codePos >= _codeSize)
                            return "#ERROR_CHAR";
                        byte charCode = _code[_codePos++];
                        return $"#{charCode}";
                    }

                default:
                    // Unknown parameter type
                    return $"UNKNOWN_PARAM_{paramType}";
            }
        }

        private string ReadArrayIndexes()
        {
            // Read array indexes from bytecode.
            // Each index is a full nested parameter — call DecompileParameter() recursively.
            if (_codePos >= _codeSize)
                return "";

            byte indexCount = _code[_codePos++];
            if (indexCount == 0)
                return "";

            var result = new StringBuilder();
            for (int i = 0; i < indexCount; i++)
            {
                string indexValue = DecompileParameter();
                result.Append($"[{indexValue}]");
            }
            return result.ToString();
        }

        private string EscapeSpecialChars(string value)
        {
            // Replace special characters with TWX script representations
            var result = new StringBuilder();
            foreach (char c in value)
            {
                switch (c)
                {
                    case '\r':
                        result.Append('*');
                        break;
                    case '\n':
                        // Line feed - typically part of CRLF, skip if preceded by CR
                        if (result.Length > 0 && result[result.Length - 1] != '*')
                            result.Append('*');
                        break;
                    case '\t':
                        result.Append("\\t");
                        break;
                    default:
                        result.Append(c);
                        break;
                }
            }
            return result.ToString();
        }

        private static readonly HashSet<string> _sysConstNames =
            new HashSet<string>(OriginalSysConstNames, StringComparer.OrdinalIgnoreCase);

        private bool NeedsQuotes(string value)
        {
            if (string.IsNullOrEmpty(value))
                return true;

            // Variable, program-var, char-literal, or label reference — no quotes
            if (value[0] == '$' || value[0] == '%' || value[0] == '#' || value[0] == ':')
                return false;

            // Starts with a space — must quote
            if (value[0] == ' ')
                return true;

            // Positive integer (not a float, not negative) — no quotes, matching reference decompiler.
            // Negative integers are quoted (e.g. "-1") so the leading minus isn't mistaken for subtraction.
            if (int.TryParse(value, out int iv))
                return iv < 0;

            // System constant name passed as a string literal (e.g. TRUE, FALSE, CONNECTED) — no quotes.
            // These appear in conditions like IF $X = TRUE where no quotes are needed.
            if (_sysConstNames.Contains(value))
                return false;

            // Array-indexed sysconst base name (e.g. "SECTOR.WARPS[x]") — no quotes if base is known.
            if (value.Contains('[') && value.Contains(']'))
            {
                string baseName = value.Substring(0, value.IndexOf('['));
                if (_sysConstNames.Contains(baseName))
                    return false;
            }

            // Everything else (strings, single letters like "Y"/"N", keyword args like "OFF") — quote.
            return true;
        }
    }
}
