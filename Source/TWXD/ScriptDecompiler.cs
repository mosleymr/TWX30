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
        private static readonly HashSet<string> BareScriptConsts = new(StringComparer.Ordinal)
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
            "SECTORS", "STARDOCK", "TIME", "TRUE", "ALPHACENTAURI", "CURRENTSECTOR", "RYLOS",
            "PORT.BUILDTIME", "PORT.UPDATED", "RAWPACKET", "SECTOR.BEACON",
            "SECTOR.CONSTELLATION", "SECTOR.FIGS.TYPE", "SECTOR.ANOMALY", "EOF"
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
            using (var output = new StreamWriter(filename, false, Encoding.Latin1))
            {
                int outputLine = 0;

                void WriteTrackedLine(string text = "")
                {
                    output.WriteLine(text);
                    outputLine++;
                }

                void PadToLine(int targetLine)
                {
                    while (targetLine > 0 && outputLine + 1 < targetLine)
                        WriteTrackedLine();
                }

                // Add description as comments
                if (!string.IsNullOrEmpty(_description))
                {
                    foreach (var line in _description.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None))
                    {
                        WriteTrackedLine($"# {line}");
                    }
                    WriteTrackedLine();
                }

                var branchLabels = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var whileLabels = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var waitOnLabels = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var tempVars = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                int indent = 0;
                bool lineGoto = false;
                bool lineElse = false;
                int pendingElseLine = -1;
                bool whileLoop = false;
                bool branchEnd = false;
                bool waitOn = false;
                int lastLine = -1;
                byte lastScript = byte.MaxValue;

                void ProcessLabelsAt(int location)
                {
                    foreach (var label in _labelList)
                    {
                        if (label.Location != location)
                            continue;

                        string labelName = label.Name;
                        bool isNumericLabel = labelName.StartsWith(":") && labelName.Length > 1 &&
                                              labelName.Substring(1).All(char.IsDigit);

                        if ((waitOn && labelName.Contains("WAITON", StringComparison.OrdinalIgnoreCase)) ||
                            waitOnLabels.Contains(labelName))
                        {
                            continue;
                        }

                        if (!isNumericLabel)
                        {
                            branchEnd = false;
                            if (lineElse)
                            {
                                PadToLine(pendingElseLine);
                                indent--;
                                WriteTrackedLine($"{Indent(indent)}else");
                                indent++;
                                lineElse = false;
                                pendingElseLine = -1;
                            }
                            WriteTrackedLine($"{Indent(indent)}:{labelName}");
                        }
                        else if (lineGoto && !whileLabels.Contains(labelName))
                        {
                            lineGoto = false;
                            lineElse = true;
                        }
                        else if (branchLabels.Contains(labelName))
                        {
                            branchLabels.Remove(labelName);
                            branchEnd = true;

                            if (lineElse)
                            {
                                indent--;
                                WriteTrackedLine($"{Indent(indent)}else");
                                indent++;
                                lineElse = false;
                            }

                            indent--;
                            WriteTrackedLine($"{Indent(indent)}end");
                        }
                        else if (branchEnd)
                        {
                            branchEnd = false;
                        }
                        else
                        {
                            whileLabels.Add(labelName);
                            branchEnd = false;
                            whileLoop = true;
                        }
                    }
                }
                
                _codePos = 0;
                ProcessLabelsAt(0);

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

                        if (lastLine != -1 && (lineNum != lastLine || scriptID != lastScript))
                        {
                            if (lineElse)
                            {
                            PadToLine(pendingElseLine);
                            indent--;
                            WriteTrackedLine($"{Indent(indent)}else");
                            indent++;
                            lineElse = false;
                            pendingElseLine = -1;
                        }

                        lineGoto = false;
                        branchEnd = false;
                        waitOn = false;
                    }

                    lastLine = lineNum;
                    lastScript = scriptID;

                    // Get command name
                    string cmdName;
                    cmdName = _scriptRef.GetCommandName(cmdID);
                    if (string.IsNullOrEmpty(cmdName))
                        cmdName = $"UNKNOWN_{cmdID}";

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
                            if (target.StartsWith("$$", StringComparison.Ordinal))
                            {
                                string expandedLeft = ExpandTempVars(left, tempVars);
                                string expandedRight = ExpandTempVars(right, tempVars);
                                tempVars[target] = $"({expandedLeft}{op}{expandedRight})";
                                skipOutput = true;
                            }
                        }
                    }
                    // Arithmetic operators: ADD/SUBTRACT/etc are 2-param accumulator ops.
                    else if (cmdName == "ADD" || cmdName == "SUBTRACT" || cmdName == "MULTIPLY" || cmdName == "DIVIDE")
                    {
                        if (paramCount >= 2)
                        {
                            string target = parts[1];
                            string right = parts[2];
                            
                            string op = cmdName switch
                            {
                                "ADD" => " + ",
                                "SUBTRACT" => " - ",
                                "MULTIPLY" => " * ",
                                "DIVIDE" => " / ",
                                _ => " ? "
                            };
                            
                            if (target.StartsWith("$$", StringComparison.Ordinal))
                            {
                                string expandedLeft = ExpandTempVars(target, tempVars);
                                string expandedRight = ExpandTempVars(right, tempVars);
                                tempVars[target] = $"({expandedLeft}{op}{expandedRight})";
                                skipOutput = true;
                            }
                        }
                    }
                    // Boolean operators: AND/OR are 2-param accumulator instructions.
                    // Compiled as: SETVAR $$cond $$left  then  OR $$cond $$right
                    else if (cmdName == "AND" || cmdName == "OR")
                    {
                        if (paramCount >= 2)
                        {
                            string target = parts[1];
                            string right = parts[2];
                            
                            if (target.StartsWith("$$", StringComparison.Ordinal))
                            {
                                string expandedLeft = ExpandTempVars(target, tempVars);
                                string expandedRight = ExpandTempVars(right, tempVars);

                                if (cmdName == "AND")
                                {
                                    if (expandedLeft.Contains(" or ", StringComparison.Ordinal))
                                        expandedLeft = $"({expandedLeft})";
                                    if (expandedRight.Contains(" or ", StringComparison.Ordinal))
                                        expandedRight = $"({expandedRight})";
                                    tempVars[target] = $"({expandedLeft} and {expandedRight})";
                                }
                                else
                                {
                                    tempVars[target] = $"{expandedLeft} or {expandedRight}";
                                }

                                skipOutput = true;
                            }
                        }
                    }
                    // SETVAR for temp variables
                    else if (cmdName == "SETVAR" && paramCount >= 2)
                    {
                        string target = parts[1];
                        string value = parts[2];
                        
                        if (target.StartsWith("$$", StringComparison.Ordinal))
                        {
                            tempVars[target] = ExpandTempVars(value, tempVars);
                            skipOutput = true;
                        }
                    }
                    // MERGETEXT: dest = src1 + src2
                    else if (cmdName == "MERGETEXT" && paramCount >= 3)
                    {
                        string src1 = parts[1];
                        string src2 = parts[2];
                        string dest = parts[3];

                        if (dest.StartsWith("$$", StringComparison.Ordinal))
                        {
                            string exp1 = ExpandTempVars(src1, tempVars);
                            string exp2 = ExpandTempVars(src2, tempVars);
                            tempVars[dest] = $"{exp1}&{exp2}";
                            skipOutput = true;
                        }
                    }
                    // BRANCH
                    else if (cmdName == "BRANCH" && paramCount >= 2)
                    {
                        string condition = ExpandTempVars(parts[1], tempVars);
                        string label = NormalizeLabelReference(parts[2]);

                        tempVars.Clear();
                        branchLabels.Add(label);

                        if (lineElse)
                        {
                            PadToLine(lineNum);
                            indent--;
                            WriteTrackedLine($"{Indent(indent)}elseif {FormatCondition(condition)}");
                            indent++;
                            lineElse = false;
                            pendingElseLine = -1;
                        }
                        else if (whileLoop)
                        {
                            PadToLine(lineNum);
                            WriteTrackedLine($"{Indent(indent)}while {FormatCondition(condition)}");
                            indent++;
                            whileLoop = false;
                        }
                        else
                        {
                            PadToLine(lineNum);
                            WriteTrackedLine($"{Indent(indent)}if {FormatCondition(condition)}");
                            indent++;
                        }

                        skipOutput = true;
                    }
                    // GOTO / GOSUB
                    else if ((cmdName == "GOTO" || cmdName == "GOSUB") && paramCount >= 1)
                    {
                        string labelExpr = ExpandTempVars(parts[1], tempVars);
                        labelExpr = Unquote(labelExpr);
                        if (labelExpr.StartsWith("::", StringComparison.Ordinal))
                            labelExpr = labelExpr.Substring(1);

                        bool isInternalLabel = IsInternalNumericLabel(labelExpr);

                        if (cmdName == "GOTO" && isInternalLabel)
                        {
                            if (whileLabels.Contains(labelExpr))
                                whileLabels.Remove(labelExpr);
                            else
                            {
                                lineGoto = true;
                                pendingElseLine = lineNum;
                            }

                            skipOutput = true;
                        }
                        else
                        {
                            if (!labelExpr.Contains('&', StringComparison.Ordinal) &&
                                !labelExpr.StartsWith("$", StringComparison.Ordinal) &&
                                !labelExpr.StartsWith("%", StringComparison.Ordinal) &&
                                !labelExpr.StartsWith(":", StringComparison.Ordinal))
                            {
                                labelExpr = ":" + labelExpr;
                            }

                            PadToLine(lineNum);
                            WriteTrackedLine($"{Indent(indent)}{cmdName.ToLowerInvariant()} {labelExpr}");
                            skipOutput = true;
                        }
                    }
                    else if (IsTriggerCommand(cmdName) && paramCount >= 2)
                    {
                        parts[1] = Unquote(parts[1]);
                        parts[2] = NormalizeLabelReference(parts[2]);

                        if (parts[1].Contains("WAITON", StringComparison.OrdinalIgnoreCase) &&
                            parts[2].Contains("WAITON", StringComparison.OrdinalIgnoreCase) &&
                            parts.Count >= 4)
                        {
                            waitOn = true;
                            waitOnLabels.Add(parts[2]);
                            string waitText = ExpandTempVars(parts[3], tempVars);
                            PadToLine(lineNum);
                            WriteTrackedLine($"{Indent(indent)}waiton {waitText}");
                            skipOutput = true;
                        }
                    }
                    
                    // Write regular command
                    if (!skipOutput)
                    {
                        if (waitOn && cmdName == "PAUSE")
                        {
                        }
                        else
                        {
                            // If else is still pending and a real command appears on this line,
                            // emit it now before the first command in the else body.
                            if (lineElse)
                            {
                                PadToLine(pendingElseLine);
                                indent--;
                                WriteTrackedLine($"{Indent(indent)}else");
                                indent++;
                                lineElse = false;
                                pendingElseLine = -1;
                            }

                            // Expand temp vars in parameters
                            for (int i = 1; i < parts.Count; i++)
                            {
                                parts[i] = ExpandTempVars(parts[i], tempVars);
                            }

                            if (cmdName == "KILLTRIGGER" && parts.Count >= 2)
                            {
                                parts[1] = Unquote(parts[1]);
                            }
                            else if (IsTriggerCommand(cmdName) && parts.Count >= 3)
                            {
                                parts[1] = Unquote(parts[1]);
                                parts[2] = NormalizeLabelReference(parts[2]);
                            }

                            if (cmdName == "GETCONSOLEINPUT" && parts.Count >= 3 && parts[2] == "\"SINGLEKEY\"")
                            {
                                parts[2] = "SINGLEKEY";
                            }

                            if (cmdName == "OPENMENU" && parts.Count >= 2 &&
                                parts[1].StartsWith("\"TWX_", StringComparison.Ordinal) &&
                                parts[1].EndsWith("\"", StringComparison.Ordinal))
                            {
                                parts[1] = Unquote(parts[1]);
                            }
                        
                            parts[0] = cmdName.ToLowerInvariant();
                            PadToLine(lineNum);
                            WriteTrackedLine($"{Indent(indent)}{string.Join(" ", parts)}");
                        }
                    }

                    ProcessLabelsAt(_codePos);
                }

                if (lineElse)
                {
                    PadToLine(pendingElseLine);
                    indent--;
                    WriteTrackedLine($"{Indent(indent)}else");
                    indent++;
                    pendingElseLine = -1;
                }

                // Close any remaining open blocks
                while (indent > 0)
                {
                    indent--;
                    WriteTrackedLine($"{Indent(indent)}end");
                }
            }
        }
        
        private string ExpandTempVars(string text, Dictionary<string, string> tempVars)
        {
            if (string.IsNullOrEmpty(text) || !text.Contains("$$", StringComparison.Ordinal))
                return text;

            string expanded = text;

            for (int pass = 0; pass < 32; pass++)
            {
                var sb = new StringBuilder();
                bool changed = false;

                for (int i = 0; i < expanded.Length; i++)
                {
                    if (expanded[i] == '$' && i + 2 < expanded.Length && expanded[i + 1] == '$' &&
                        char.IsDigit(expanded[i + 2]))
                    {
                        int j = i + 2;
                        while (j < expanded.Length && char.IsDigit(expanded[j]))
                            j++;

                        string key = expanded.Substring(i, j - i);
                        if (tempVars.TryGetValue(key, out string? replacement))
                        {
                            sb.Append(replacement);
                            i = j - 1;
                            changed = true;
                            continue;
                        }
                    }

                    sb.Append(expanded[i]);
                }

                string next = sb.ToString();
                if (!changed || next == expanded)
                    return next;

                expanded = next;
            }

            return expanded;
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
                        constName = _scriptRef.GetSysConstName(constID);
                        if (string.IsNullOrEmpty(constName))
                            constName = $"SYSCONST_{constID}";
                        
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

        private static bool IsTriggerCommand(string cmdName)
        {
            return cmdName == "SETTEXTTRIGGER" ||
                   cmdName == "SETTEXTOUTTRIGGER" ||
                   cmdName == "SETTEXTLINETRIGGER" ||
                   cmdName == "SETEVENTTRIGGER" ||
                   cmdName == "SETDELAYTRIGGER";
        }

        private static string Unquote(string input)
        {
            if (input.Length >= 2 && input[0] == '"' && input[input.Length - 1] == '"')
                return input.Substring(1, input.Length - 2);
            return input;
        }

        private static bool IsInternalNumericLabel(string label)
        {
            return label.StartsWith(":", StringComparison.Ordinal) &&
                   label.Length > 1 &&
                   label.Substring(1).All(char.IsDigit);
        }

        private static bool IsBareLabelReference(string value)
        {
            return value.StartsWith(":", StringComparison.Ordinal) &&
                   value.Length > 1 &&
                   !char.IsWhiteSpace(value[1]) &&
                   value.IndexOfAny(new[] { ' ', '\t', '"' }) < 0;
        }

        private static string NormalizeLabelReference(string label)
        {
            string value = Unquote(label);
            if (value.StartsWith("::", StringComparison.Ordinal))
                value = value.Substring(1);
            if (!value.StartsWith(":", StringComparison.Ordinal))
                value = ":" + value;
            return value;
        }

        private static string FormatCondition(string condition)
        {
            if (string.IsNullOrEmpty(condition))
                return condition;

            if (!condition.StartsWith("(", StringComparison.Ordinal))
                return $"({condition})";

            if (condition.Contains(" or ", StringComparison.Ordinal) &&
                !condition.Contains(" and ", StringComparison.Ordinal))
                return $"({condition})";

            return condition;
        }

        private bool NeedsQuotes(string value)
        {
            if (string.IsNullOrEmpty(value))
                return true;

            // Variable or program-var — no quotes
            if ((value[0] == '$' || value[0] == '%') &&
                value.Length > 1 &&
                (char.IsLetterOrDigit(value[1]) || value[1] == '$' || value[1] == '_'))
                return false;

            // Char literal: #13, #32, etc. — no quotes only if followed by digits
            if (value[0] == '#' && value.Length > 1 && value.Substring(1).All(char.IsDigit))
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
            if (BareScriptConsts.Contains(value))
                return false;

            // Array-indexed sysconst base name (e.g. "SECTOR.WARPS[x]") — no quotes if base is known.
            if (value.Contains('[') && value.Contains(']'))
            {
                string baseName = value.Substring(0, value.IndexOf('['));
                if (BareScriptConsts.Contains(baseName))
                    return false;
            }

            // Everything else (strings, single letters like "Y"/"N", keyword args like "OFF") — quote.
            return true;
        }
    }
}
