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
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace TWXProxy.Core
{
    #region Constants and Enums

    public static class ScriptConstants
    {
        // TWX Proxy 2.02 is version 1
        // TWX Proxy 2.03Beta is version 2
        // TWX Proxy 2.03Final is version 3
        // TWX Proxy 2.04 is version 4
        // TWX Proxy 2.05 is version 5
        // TWX Proxy 2.06 is version 6
        public const int CompiledScriptVersion = 6;

        public const byte PARAM_CMD = 0;
        public const byte PARAM_VAR = 1;        // User variable prefix
        public const byte PARAM_CONST = 2;      // Compiler string constant prefix
        public const byte PARAM_SYSCONST = 3;   // Read only system value
        public const byte PARAM_PROGVAR = 4;    // Program variable
        public const byte PARAM_CHAR = 5;       // Character code

        public const char OP_GREATEREQUAL = (char)230;
        public const char OP_LESSEREQUAL = (char)231;
        public const char OP_AND = (char)232;
        public const char OP_OR = (char)233;
        public const char OP_XOR = (char)234;
        public const char OP_NOT = (char)235;
        public const char OP_NOTEQUAL = (char)236;
        public const char OP_NONE = '\0';
    }

    public enum ParamKind
    {
        Value,
        Variable,
        Expression
    }

    #endregion

    #region Script File Header

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct ScriptFileHeader
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 11)]
        public string ProgramName;
        public ushort Version;
        public int DescSize;
        public int CodeSize;
    }

    #endregion

    #region Condition Struct

    public class ConditionStruct
    {
        public string ConLabel { get; set; } = string.Empty;
        public string EndLabel { get; set; } = string.Empty;
        public bool IsWhile { get; set; }
        public bool AtElse { get; set; }
    }

    #endregion

    #region Script Command Parameter Classes

    // NOTE: CmdParam class is now defined in ScriptCmd.cs

    /// <summary>
    /// TVarParam: A variable within the script. Typically referenced by its ID. Can contain
    /// a list of indexed values in the event of it being used as an array within the script.
    /// </summary>
    public class VarParam : CmdParam
    {
        private string _name = string.Empty;
        private List<VarParam> _vars;
        private int _arraySize;

        public VarParam()
        {
            _vars = new List<VarParam>();
        }

        public override void Dispose()
        {
            // Free all index element variables
            for (int i = _vars.Count - 1; i >= 0; i--)
            {
                _vars[i].Dispose(); // Recursive call
                _vars.RemoveAt(i);
            }
            _name = string.Empty;
            base.Dispose();
        }

        public int AddVar(VarParam newVar)
        {
            // Link up an index element variable
            _vars.Add(newVar);
            return _vars.Count - 1;
        }

        public VarParam GetIndexVar(string[] indexes) => GetIndexVar(indexes, 0);

        private VarParam GetIndexVar(string[] indexes, int offset)
        {
            // Move through the array of index dimensions and return a reference to the
            // variable with the specified name/index.
            // Uses an offset rather than Skip(1).ToArray() to avoid per-call allocations.

            if (indexes == null || offset >= indexes.Length)
                return this; // no more indexes

            // Search the index for a variable with a matching name
            if (_arraySize > 0)
            {
                // Static array - we can look up the variable directly
                if (!int.TryParse(indexes[offset], out int i))
                    i = 0;

                if (i < 1 || i > _arraySize)
                {
                    throw new Exception($"Static array index '{indexes[offset]}' is out of range (must be 1-{_arraySize})");
                }

                return _vars[i - 1].GetIndexVar(indexes, offset + 1);
            }
            else
            {
                // Dynamic array - search for matching name
                string key = indexes[offset];
                foreach (var v in _vars)
                {
                    if (v.Name == key)
                        return v.GetIndexVar(indexes, offset + 1);
                }

                // Variable not found in index - make a new one
                var newVar = new VarParam { Name = key };
                AddVar(newVar);
                return newVar.GetIndexVar(indexes, offset + 1);
            }
        }

        public void SetArray(params int[] dimensions)
        {
            if (dimensions == null || dimensions.Length == 0)
                return;

            _arraySize = dimensions[0];

            // First, delete any existing Vars above ArraySize
            for (int i = _vars.Count - 1; i >= _arraySize; i--)
            {
                _vars[i].Dispose();
                _vars.RemoveAt(i);
            }

            // Build variables up until size limit
            try
            {
                for (int i = 0; i < dimensions[0]; i++)
                {
                    // See if an Item already exists
                    if (i >= _vars.Count)
                    {
                        var newVar = new VarParam { Name = (i + 1).ToString(), Value = "0" };
                        AddVar(newVar);
                    }
                    else
                    {
                        // Clear existing variable
                        _vars[i].Name = (i + 1).ToString();
                        _vars[i].Value = "0";
                    }

                    if (dimensions.Length > 1 || _vars[i]._vars.Count > 0)
                    {
                        // We have sub-dimensions
                        int[] nextDimen = dimensions.Length == 1
                            ? new[] { 0 }
                            : dimensions.Skip(1).ToArray();

                        _vars[i].SetArray(nextDimen);
                    }
                }
            }
            catch
            {
                throw new Exception("Not enough memory to set static array");
            }
        }

        public void SetArrayFromStrings(List<string> strings)
        {
            _arraySize = strings.Count;

            for (int i = _vars.Count - 1; i >= _arraySize; i--)
            {
                _vars[i].Dispose();
                _vars.RemoveAt(i);
            }

            for (int i = 0; i < _arraySize; i++)
            {
                if (i >= _vars.Count)
                {
                    var newVar = new VarParam
                    {
                        Name = (i + 1).ToString(),
                        Value = strings[i]
                    };
                    AddVar(newVar);
                }
                else
                {
                    _vars[i].Name = (i + 1).ToString();
                    _vars[i].Value = strings[i];
                    if (_vars[i]._vars.Count > 0)
                    {
                        _vars[i].SetArray(0);
                    }
                }
            }
        }

        public void Dump(string tab)
        {
            // Broadcast variable details to active telnet connections
            if (Name.Length >= 2 && Name.StartsWith("$$"))
                return; // don't dump system variables

            // TWXServer.AddBuffer($"{tab}{AnsiCodes.ANSI_15}\"{AnsiCodes.ANSI_7}{Name}{AnsiCodes.ANSI_15}\" = \"{AnsiCodes.ANSI_7}{Value}{AnsiCodes.ANSI_15}\"\r\n");

            if (_vars.Count > 0)
            {
                // Dump array contents
                string arrayType = _arraySize > 0 ? "Static" : "Dynamic";
                // TWXServer.AddBuffer($"{tab}{AnsiCodes.ANSI_15}{arrayType} array of \"{Name}\" (size {_vars.Count})\r\n");

                foreach (var v in _vars)
                {
                    v.Dump(tab + "  ");
                }
            }
        }

        public string Name
        {
            get => _name;
            set => _name = value;
        }

        public int ArraySize
        {
            get => _arraySize;
            set => _arraySize = value;
        }

        public List<VarParam> Vars => _vars;
    }

    /// <summary>
    /// ProgVarParam: A parameter that wraps a program variable and automatically 
    /// reads/writes to the global program variable storage.
    /// </summary>
    public class ProgVarParam : CmdParam
    {
        private readonly string _progVarName;
        private readonly ModInterpreter _interpreter;

        public ProgVarParam(string progVarName, ModInterpreter interpreter)
        {
            _progVarName = progVarName;
            _interpreter = interpreter;
        }

        /// <summary>The progvar's identifier name, used as the sector DB key in Pascal 2-param calls.</summary>
        public string Name => _progVarName;

        public override string Value
        {
            get => _interpreter.GetProgVar(_progVarName);
            set => _interpreter.SetProgVar(_progVarName, value);
        }
    }

    #endregion

    #region Script Label

    /// <summary>
    /// TScriptLabel: A jump label within a script.
    /// </summary>
    public class ScriptLabel
    {
        public int Location { get; set; }
        public string Name { get; set; } = string.Empty;
    }

    #endregion

    #region ScriptCmp - Main Compiler Class

    /// <summary>
    /// TScriptCmp: Main script compiler class
    /// </summary>
    public class ScriptCmp : IDisposable
    {
        private Stack<ConditionStruct> _ifStack;
        private List<CmdParam> _paramList;
        private List<ScriptLabel> _labelList;
        private Dictionary<string, int> _labelDict;
        private List<string> _includeScriptList;
        private List<string> _description;
        private string _scriptFile = string.Empty;
        private string _scriptDirectory = string.Empty;
        private byte[] _code = Array.Empty<byte>();
        private int _ifLabelCount;
#pragma warning disable CS0169 // Field is never used
        private int _sysVarCount;
#pragma warning restore CS0169
        private int _waitOnCount;
        private int _lineCount;
        private int _cmdCount;
        private int _codeSize;
        private int _version = ScriptConstants.CompiledScriptVersion;
        private ScriptRef? _scriptRef;

        public ScriptCmp(ScriptRef? scriptRef = null, string scriptDirectory = "")
        {
            _paramList = new List<CmdParam>();
            _labelList = new List<ScriptLabel>();
            _labelDict = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            _description = new List<string>();
            _scriptRef = scriptRef ?? new ScriptRef();
            _scriptDirectory = scriptDirectory;
            _includeScriptList = new List<string>();
            _ifStack = new Stack<ConditionStruct>();
            _lineCount = 0;
            _cmdCount = 0;
        }

        public void Dispose()
        {
            // Free up parameters
            foreach (var param in _paramList)
            {
                param.Dispose();
            }
            _paramList.Clear();

            _labelList.Clear();
            _labelDict.Clear();
            _description.Clear();

            // Free up IF stack
            _ifStack.Clear();

            _code = Array.Empty<byte>();
            _includeScriptList.Clear();
        }

        #region Properties

        public int ParamCount => _paramList.Count;
        public int LabelCount => _labelList.Count;
        public CmdParam GetParam(int index) => _paramList[index];
        public ScriptLabel GetLabel(int index) => _labelList[index];
        public string GetIncludeScript(int index) => _includeScriptList[index];
        public int IncludeScriptCount => _includeScriptList.Count;
        public List<CmdParam> ParamList => _paramList;
        public List<ScriptLabel> LabelList => _labelList;
        public List<string> IncludeScriptList => _includeScriptList;
        public byte[] Code => _code;
        public int CodeSize => _codeSize;
        public int LineCount => _lineCount;
        public int CmdCount => _cmdCount;
        public int Version => _version;
        public ScriptRef? ScriptRef => _scriptRef;
        public string ScriptFile => _scriptFile;
        /// <summary>Description as a single string (lines joined with '\n').</summary>
        public string DescriptionText => _description.Count > 0 ? string.Join("\n", _description) : string.Empty;

        #endregion

        #region Code Generation Methods

        private void AppendCode(byte[] newCode)
        {
            // Write this data to the end of the byte-code
            int oldSize = _code.Length;
            Array.Resize(ref _code, oldSize + newCode.Length);
            Array.Copy(newCode, 0, _code, oldSize, newCode.Length);
            _codeSize = _code.Length;
        }

        private void BuildLabel(string name, int location)
        {
            // Create a new label
            var newLabel = new ScriptLabel
            {
                Name = name,
                Location = location
            };
            _labelList.Add(newLabel);
            // O(1) lookup - first definition wins, matching FirstOrDefault semantics
            if (!_labelDict.ContainsKey(name))
                _labelDict[name] = location;
        }

        /// <summary>
        /// O(1) label lookup. Returns code position, or -1 if not found.
        /// </summary>
        public int FindLabel(string name)
        {
            return _labelDict.TryGetValue(name, out int pos) ? pos : -1;
        }

        public void ExtendName(ref string name, int scriptID)
        {
            // Don't extend system variables (starting with $$)
            if (name.StartsWith("$$"))
                return;
                
            if (!name.Contains('~'))
            {
                if (scriptID > 0 && scriptID <= _includeScriptList.Count)
                    name = _includeScriptList[scriptID - 1] + "~" + name;
            }
            else
            {
                if (!string.IsNullOrEmpty(name) && name[0] == '~')
                {
                    if (name.Length == 1)
                        throw new Exception("Bad name");

                    name = name.Substring(1);
                }
            }
        }

        public void ExtendLabelName(ref string name, int scriptID)
        {
            if (!name.Contains('~') && scriptID > 0 && scriptID < _includeScriptList.Count)
                name = ":" + _includeScriptList[scriptID] + "~" + name.Substring(1);
        }

        private byte IdentifyParam(string paramName)
        {
            // Identify the type of this parameter
            // See if it's a $var, %progVar, #char, [const], or [sysConst]
            if (string.IsNullOrEmpty(paramName))
                return ScriptConstants.PARAM_CONST;

            if (paramName[0] == '$')
                return ScriptConstants.PARAM_VAR;
            else if (paramName[0] == '%')
                return ScriptConstants.PARAM_PROGVAR;
            else if (paramName[0] == '#')
                return ScriptConstants.PARAM_CHAR;
            else
            {
                // Remove indexes from constant name
                int indexLevel = 0;
                StringBuilder constName = new StringBuilder();

                foreach (char c in paramName)
                {
                    if (c == '[')
                        indexLevel++;
                    else if (c == ']')
                        indexLevel--;
                    else if (indexLevel == 0)
                        constName.Append(c);
                }

                // Check for system constant
                string constNameStr = constName.ToString();
                if (_scriptRef is ScriptRef scriptRef)
                {
                    if (scriptRef.FindSysConst(constNameStr) >= 0)
                        return ScriptConstants.PARAM_SYSCONST;
                }

                return ScriptConstants.PARAM_CONST;
            }
        }

        private string ApplyEncryption(string value, byte key)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;

            char[] result = new char[value.Length];
            for (int i = 0; i < value.Length; i++)
            {
                result[i] = (char)((byte)value[i] ^ key);
            }
            return new string(result);
        }

        private bool IsOperator(char c)
        {
            return "+-*/<>=&|!^".Contains(c) ||
                   c == ScriptConstants.OP_GREATEREQUAL ||
                   c == ScriptConstants.OP_LESSEREQUAL ||
                   c == ScriptConstants.OP_AND ||
                   c == ScriptConstants.OP_OR ||
                   c == ScriptConstants.OP_XOR ||
                   c == ScriptConstants.OP_NOT ||
                   c == ScriptConstants.OP_NOTEQUAL;
        }

        #endregion

        #region Compilation Methods

        public void CompileFromFile(string filename, string descFile)
        {
            _scriptFile = filename;

            // Read script file
            var scriptText = new List<string>(File.ReadAllLines(filename));

            // Read description file if provided
            if (!string.IsNullOrEmpty(descFile) && File.Exists(descFile))
            {
                _description.AddRange(File.ReadAllLines(descFile));
            }

            CompileFromStrings(scriptText, filename);
        }

        public void CompileFromStrings(List<string> scriptText, string scriptName)
        {
            _scriptFile = scriptName;
            _lineCount = 0;
            _cmdCount = 0;

            foreach (var line in scriptText)
            {
                _lineCount++;
                try
                {
                    CompileParamLine(line, _lineCount, 0);
                }
                catch (Exception ex)
                {
                    throw new Exception($"Error on line {_lineCount}: {ex.Message}", ex);
                }
            }

            // Check for unclosed IF/WHILE blocks
            if (_ifStack.Count > 0)
            {
                throw new Exception("IF or WHILE block not terminated with END");
            }
        }

        private void CompileParamLine(string line, int lineNumber, byte scriptID)
        {
            // Ignore lines that start with # (comment lines)
            line = line.TrimStart();
            if (line.StartsWith('#'))
                return;

            // Process parenthesized compound expressions before parsing
            line = ProcessParenthesizedExpressions(line, lineNumber, scriptID);
            
            // Process arithmetic expressions in parameters
            line = ProcessArithmeticExpressions(line, lineNumber, scriptID);

            // Parse the line into parameters
            var paramLine = ParseLine(line);

            // Call the overload that processes the parsed line
            CompileParamLine(paramLine, lineNumber, scriptID);
        }

        private void CompileParamLine(List<string> paramLine, int lineNumber, byte scriptID)
        {            if (paramLine.Count == 0)
                return; // Empty line

            string firstParam = paramLine[0];

            // Check for label
            if (firstParam.StartsWith(':'))
            {
                if (paramLine.Count > 1)
                    throw new Exception("Unnecessary parameters after label declaration");

                if (firstParam.Length < 2)
                    throw new Exception("Bad label name");

                string labelName = firstParam.Substring(1);
                if (!labelName.Contains('~') && scriptID > 0 && scriptID <= _includeScriptList.Count)
                    labelName = _includeScriptList[scriptID - 1] + "~" + labelName;

                BuildLabel(labelName, _codeSize);
                return;
            }

            // Check for & concatenation operator (convert to CONCAT command)
            // Handle chained concatenations: SETVAR/CONCAT var val1 & val2 & val3
            if (paramLine.Count > 2 && (paramLine[0].ToUpperInvariant() == "SETVAR" || paramLine[0].ToUpperInvariant() == "CONCAT"))
            {
                // Find all & operators
                var parts = new List<string>();
                int startIdx = paramLine[0].ToUpperInvariant() == "SETVAR" ? 2 : 1; // Skip command and variable for SETVAR
                
                for (int i = startIdx; i < paramLine.Count; i++)
                {
                    if (paramLine[i] == "&")
                        continue; // Skip & operators
                    parts.Add(paramLine[i]);
                }

                // If we found & operators (parts count differs from original)
                if (parts.Count > 1 && parts.Count < (paramLine.Count - startIdx))
                {
                    string varName = paramLine[1];

                    if (paramLine[0].ToUpperInvariant() == "SETVAR")
                    {
                        // Pascal compiles `setVar $a $b&$c` as MERGETEXT which reads all
                        // inputs before writing. Doing SETVAR $a=$b then CONCAT $a+=$c
                        // aliases the destination into the source when $c == $a, giving
                        // 'CC' instead of 'CargoMaster'. Fix: build into a temp var first.
                        string tempVar = $"%_concat{++_tempVarCounter}";
                        RecurseCmd(new[] { "SETVAR", tempVar, parts[0] }, lineNumber, scriptID);
                        for (int i = 1; i < parts.Count; i++)
                            RecurseCmd(new[] { "CONCAT", tempVar, parts[i] }, lineNumber, scriptID);
                        RecurseCmd(new[] { "SETVAR", varName, tempVar }, lineNumber, scriptID);
                    }
                    else
                    {
                        // CONCAT: first part uses CONCAT directly, rest chain on top
                        RecurseCmd(new[] { "CONCAT", varName, parts[0] }, lineNumber, scriptID);
                        for (int i = 1; i < parts.Count; i++)
                            RecurseCmd(new[] { "CONCAT", varName, parts[i] }, lineNumber, scriptID);
                    }
                    
                    return;
                }
            }
            // For other commands with & operators, collapse them into a temp variable
            else if (paramLine.Count > 2)
            {
                bool hasAmpersand = false;
                for (int i = 1; i < paramLine.Count; i++)
                {
                    if (paramLine[i] == "&")
                    {
                        hasAmpersand = true;
                        break;
                    }
                }

                if (hasAmpersand)
                {
                    // Find the parameter with & operator (usually the last one)
                    int firstAmpPos = -1;
                    for (int i = 1; i < paramLine.Count; i++)
                    {
                        if (paramLine[i] == "&")
                        {
                            firstAmpPos = i;
                            break;
                        }
                    }
                    
                    // Create a temp variable and build the concatenated value
                    string tempVar = $"%_concat{++_tempVarCounter}";
                    var parts = new List<string>();

                    // Collect only the parts that form the & chain (stop when the chain breaks).
                    // This prevents parameters that follow the concat expression (like cutText's
                    // dest, start, len) from being incorrectly pulled into the concat temp.
                    int chainEnd = firstAmpPos - 1; // start at the left operand
                    while (chainEnd < paramLine.Count)
                    {
                        parts.Add(paramLine[chainEnd]); // add the operand
                        chainEnd++;
                        if (chainEnd < paramLine.Count && paramLine[chainEnd] == "&")
                            chainEnd++; // skip the & and continue to next operand
                        else
                            break; // no & follows = end of chain
                    }
                    // chainEnd now points to the first parameter after the concat chain

                    // Build concatenation commands
                    RecurseCmd(new[] { "SETVAR", tempVar, parts[0] }, lineNumber, scriptID);
                    for (int i = 1; i < parts.Count; i++)
                    {
                        RecurseCmd(new[] { "CONCAT", tempVar, parts[i] }, lineNumber, scriptID);
                    }

                    // Replace paramLine: keep command + params before concat, concat result,
                    // and any params that follow the concat chain (e.g. cutText dest/start/len).
                    var newParamLine = new List<string> { paramLine[0] };
                    for (int i = 1; i < firstAmpPos - 1; i++)
                    {
                        newParamLine.Add(paramLine[i]);
                    }
                    newParamLine.Add(tempVar);
                    for (int i = chainEnd; i < paramLine.Count; i++)
                    {
                        newParamLine.Add(paramLine[i]);
                    }
                    paramLine = newParamLine;
                }
            }

            // Check for macro commands (IF, WHILE, ELSE, END, etc.)
            if (HandleMacroCommand(paramLine, lineNumber, scriptID))
                return;

            // Regular command - compile it
            CompileCommand(paramLine, lineNumber, scriptID);
        }

        private int _tempVarCounter = 0;

        private string ProcessArithmeticExpressions(string line, int lineNumber, byte scriptID)
        {
            // Process arithmetic expressions like:  setVar $spaces (20 - ($spaces / 2)) - 1
            // Convert to temp variables and arithmetic commands
            
            // Look for patterns with arithmetic operators outside quotes
            bool inQuote = false;
            bool hasArithmeticOp = false;
            
            for (int i = 0; i < line.Length; i++)
            {
                if (line[i] == '"')
                    inQuote = !inQuote;
                else if (!inQuote && (line[i] == '+' || line[i] == '-' || line[i] == '*' || line[i] == '/'))
                {
                    // Check if it's part of an expression (not just a negative number)
                    if (line[i] == '-' && i > 0 && char.IsWhiteSpace(line[i - 1]) && i + 1 < line.Length && char.IsDigit(line[i + 1]))
                        continue; // Skip negative numbers
                    hasArithmeticOp = true;
                    break;
                }
            }
            
            if (!hasArithmeticOp)
                return line; // No arithmetic operators found
            
            // Parse line to find arithmetic expressions
            var words = new List<string>();
            var currentWord = new System.Text.StringBuilder();
            inQuote = false;
            
            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (c == '"')
                {
                    inQuote = !inQuote;
                    currentWord.Append(c);
                }
                else if (!inQuote && char.IsWhiteSpace(c))
                {
                    if (currentWord.Length > 0)
                    {
                        words.Add(currentWord.ToString());
                        currentWord.Clear();
                    }
                }
                else
                {
                    currentWord.Append(c);
                }
            }
            if (currentWord.Length > 0)
                words.Add(currentWord.ToString());
            
            // If first word is setVar, process the expression after the variable name
            if (words.Count > 2 && words[0].ToLowerInvariant() == "setvar")
            {
                string variable = words[1];
                string expression = string.Join(" ", words.Skip(2));
                
                // Check if expression contains operators
                if (ContainsArithmeticOperators(expression))
                {
                    try
                    {
                        // Compile expression to temp variable
                        string tempVar = CompileArithmeticExpressionToCommands(expression, lineNumber, scriptID);
                        
                        // Return modified line
                        return $"setVar {variable} {tempVar}";
                    }
                    catch
                    {
                        // If compilation fails, return original line
                        return line;
                    }
                }
            }
            
            return line;
        }
        
        private bool ContainsArithmeticOperators(string expr)
        {
            bool inQuote = false;
            for (int i = 0; i < expr.Length; i++)
            {
                if (expr[i] == '"')
                    inQuote = !inQuote;
                else if (!inQuote && i > 0 && i < expr.Length - 1)
                {
                    // Check for operators with surrounding context
                    if ((expr[i] == '+' || expr[i] == '*' || expr[i] == '/') ||
                        (expr[i] == '-' && !char.IsDigit(expr[i - 1])))
                    {
                        return true;
                    }
                }
            }
            return false;
        }
        
        private string CompileArithmeticExpressionToCommands(string expr, int lineNumber, byte scriptID)
        {
            expr = expr.Trim();
            
            // Create result temp variable
            string resultVar = $"$$__math{++_ifLabelCount}";
            var resultParam = new VarParam { Name = resultVar, Value = "0" };
            _paramList.Add(resultParam);
            
            // Parse and compile expression
            CompileArithmeticRecursive(expr.Trim(), resultVar, lineNumber, scriptID);
            
            return resultVar;
        }
        
        private void CompileArithmeticRecursive(string expr, string targetVar, int lineNumber, byte scriptID)
        {
            // Remove outer parentheses
            while (expr.StartsWith("(") && expr.EndsWith(")"))
            {
                int depth = 0;
                bool innerMatch = false;
                for (int i = 1; i < expr.Length - 1; i++)
                {
                    if (expr[i] == '(') depth++;
                    else if (expr[i] == ')') depth--;
                    if (depth < 0)
                    {
                        innerMatch = true;
                        break;
                    }
                }
                if (!innerMatch)
                    expr = expr.Substring(1, expr.Length - 2).Trim();
                else
                    break;
            }
            
            // Find lowest precedence operator (+ or - outside parentheses)
            int opPos = FindOperator(expr, new[] { '+', '-' });
            char opChar = '\0';
            
            if (opPos >= 0)
                opChar = expr[opPos];
            else
            {
                // Try * or /
                opPos = FindOperator(expr, new[] { '*', '/' });
                if (opPos >= 0)
                    opChar = expr[opPos];
            }
            
            if(opPos > 0)
            {
                // Split and compile into two-address form:
                //   SETVAR targetVar left     (targetVar = left)
                //   OP     targetVar right    (targetVar op= right)
                // This matches the 2-param signature of ADD/SUBTRACT/MULTIPLY/DIVIDE.
                string left = expr.Substring(0, opPos).Trim();
                string right = expr.Substring(opPos + 1).Trim();
                
                string leftVar = GetOrCreateTempVar(left, lineNumber, scriptID);
                string rightVar = GetOrCreateTempVar(right, lineNumber, scriptID);
                
                string cmd = opChar switch
                {
                    '+' => "ADD",
                    '-' => "SUBTRACT",
                    '*' => "MULTIPLY",
                    '/' => "DIVIDE",
                    _ => throw new Exception($"Unknown operator: {opChar}")
                };
                
                // Copy left into targetVar, then apply op with right
                RecurseCmd(new[] { "SETVAR", targetVar, leftVar }, lineNumber, scriptID);
                RecurseCmd(new[] { cmd, targetVar, rightVar }, lineNumber, scriptID);
            }
            else
            {
                // No operator - simple assignment
                RecurseCmd(new[] { "SETVAR", targetVar, expr }, lineNumber, scriptID);
            }
        }
        
        private int FindOperator(string expr, char[] operators)
        {
            int depth = 0;
            // Scan right-to-left for correct precedence
            for (int i = expr.Length - 1; i >= 0; i--)
            {
                if (expr[i] == ')') depth++;
                else if (expr[i] == '(') depth--;
                else if (depth == 0 && operators.Contains(expr[i]))
                {
                    // Check if it's unary minus
                    if (expr[i] == '-' && i == 0)
                        continue;
                    return i;
                }
            }
            return -1;
        }

        private int FindLogicalOperator(string condition, out string operatorFound, out string commandName)
        {
            // Find 'and' or 'or' at depth 0 (outside parentheses)
            // Returns the position of the operator, or -1 if not found
            operatorFound = "";
            commandName = "";
            
            int depth = 0;
            // Scan from left to right (no precedence between AND/OR, just left-to-right)
            for (int i = 0; i < condition.Length; i++)
            {
                if (condition[i] == '(') depth++;
                else if (condition[i] == ')') depth--;
                else if (depth == 0 && i + 3 <= condition.Length)
                {
                    // Check for 'and' or 'or' as whole words
                    string substr = condition.Substring(i, Math.Min(4, condition.Length - i)).ToLowerInvariant();
                    if ((substr.StartsWith("and ") || (substr == "and" && i + 3 == condition.Length)) &&
                        (i == 0 || !char.IsLetterOrDigit(condition[i - 1])))
                    {
                        operatorFound = condition.Substring(i, 3);
                        commandName = "AND";
                        return i;
                    }
                    if (substr.Length >= 3 && (substr.StartsWith("or ") || (substr.Substring(0, 2) == "or" && i + 2 == condition.Length)) &&
                        (i == 0 || !char.IsLetterOrDigit(condition[i - 1])))
                    {
                        operatorFound = condition.Substring(i, 2);
                        commandName = "OR";
                        return i;
                    }
                }
            }
            return -1;
        }
        
        private string GetOrCreateTempVar(string value, int lineNumber, byte scriptID)
        {
            value = value.Trim();
            
            // If it contains operators, it needs compilation
            if (ContainsArithmeticOperators(value))
            {
                string tempVar = $"$$__math{++_ifLabelCount}";
                var tempParam = new VarParam { Name = tempVar, Value = "0" };
                _paramList.Add(tempParam);
                CompileArithmeticRecursive(value, tempVar, lineNumber, scriptID);
                return tempVar;
            }
            
            // Simple value - return as is
            return value;
        }

        private string ProcessParenthesizedExpressions(string line, int lineNumber, byte scriptID)
        {
            // Find and process all parenthesized expressions
            while (true)
            {
                int openParen = -1;
                int closeParen = -1;
                bool inQuote = false;

                // Find innermost parenthesized expression (not inside quotes)
                for (int i = 0; i < line.Length; i++)
                {
                    if (line[i] == '"')
                        inQuote = !inQuote;
                    else if (!inQuote && line[i] == '(')
                        openParen = i;
                    else if (!inQuote && line[i] == ')' && openParen >= 0)
                    {
                        closeParen = i;
                        break;
                    }
                }

                if (openParen < 0 || closeParen < 0)
                    break; // No more parenthesized expressions

                // Extract the expression inside parentheses
                string expr = line.Substring(openParen + 1, closeParen - openParen - 1).Trim();

                // Check if expression contains & operator (concatenation)
                if (expr.Contains('&'))
                {
                    // Parse the expression to check whether & is the leading operator.
                    // e.g. "val1 & val2 & val3"  → tokens[1] == "&" → pure concat → use temp var.
                    // e.g. "$char = #27&\"[A\"" → tokens[1] == "=" → & is inside an operand →
                    // just strip the parens; RecurseCmd's own & handling will build the concat.
                    var tokens = ParseExpression(expr);

                    bool isConcatExpr = (tokens.Count >= 3 && tokens[1] == "&")
                                     || (tokens.Count == 1);

                    if (isConcatExpr)
                    {
                        // Pure concatenation expression — materialise into a temp variable.
                        string tempVar = $"%_temp{++_tempVarCounter}";

                        if (tokens.Count >= 3)
                        {
                            RecurseCmd(new[] { "SETVAR", tempVar, tokens[0] }, lineNumber, scriptID);
                            RecurseCmd(new[] { "CONCAT", tempVar, tokens[2] }, lineNumber, scriptID);

                            for (int i = 3; i < tokens.Count; i += 2)
                            {
                                if (i < tokens.Count && tokens[i] == "&" && i + 1 < tokens.Count)
                                    RecurseCmd(new[] { "CONCAT", tempVar, tokens[i + 1] }, lineNumber, scriptID);
                            }
                        }
                        else // tokens.Count == 1
                        {
                            RecurseCmd(new[] { "SETVAR", tempVar, tokens[0] }, lineNumber, scriptID);
                        }

                        line = line.Substring(0, openParen) + tempVar + line.Substring(closeParen + 1);
                    }
                    else
                    {
                        // & appears inside a sub-expression (e.g. comparison RHS "#27&\"[A\"").
                        // Drop the parens only — RecurseCmd's & chain logic handles the concat.
                        line = line.Substring(0, openParen) + expr + line.Substring(closeParen + 1);
                    }
                }
                else if (ContainsArithmeticOperators(expr))
                {
                    // Arithmetic expression inside parens: compile to a temp var so that
                    // tokens like ($param + 1) don't bleed into the surrounding command
                    // parameters as raw operators.  e.g. getWord $line $v ($param + 1)
                    // must not become getWord $line $v $param + 1  (5 tokens).
                    string tempVar = CompileArithmeticExpressionToCommands(expr, lineNumber, scriptID);
                    line = line.Substring(0, openParen) + tempVar + line.Substring(closeParen + 1);
                }
                else
                {
                    // No operators — but if the expression contains logical AND/OR it is
                    // a grouping expression for a compound boolean condition.  Stripping
                    // those parens would destroy the explicit precedence the script author
                    // intended (e.g. "(A OR B) AND C" → "A OR B AND C" which evaluates as
                    // "A OR (B AND C)" due to AND-before-OR precedence).  Stop PPE here
                    // so that CompileCondition can see the parens and split correctly.
                    if (FindLogicalOperator(expr, out _, out _) >= 0)
                        break;

                    // No logical operators — safe to remove the parentheses.
                    line = line.Substring(0, openParen) + expr + line.Substring(closeParen + 1);
                }
            }

            return line;
        }

        private List<string> ParseExpression(string expr)
        {
            // Parse an expression and split on & operator (outside of quotes)
            var tokens = new List<string>();
            bool inQuote = false;
            var currentToken = new System.Text.StringBuilder();

            for (int i = 0; i < expr.Length; i++)
            {
                char c = expr[i];

                if (c == '"')
                {
                    inQuote = !inQuote;
                    currentToken.Append(c);
                }
                else if (!inQuote && c == '&')
                {
                    // Split on & operator
                    if (currentToken.Length > 0)
                    {
                        tokens.Add(currentToken.ToString().Trim());
                        currentToken.Clear();
                    }
                    tokens.Add("&");
                }
                else if (!inQuote && char.IsWhiteSpace(c))
                {
                    // Skip whitespace outside quotes (unless it's part of a token)
                    if (currentToken.Length > 0)
                    {
                        currentToken.Append(c);
                    }
                }
                else
                {
                    currentToken.Append(c);
                }
            }

            if (currentToken.Length > 0)
                tokens.Add(currentToken.ToString().Trim());

            return tokens;
        }

        private List<string> ParseLine(string line)
        {
            var result = new List<string>();
            if (string.IsNullOrWhiteSpace(line))
                return result;

            // Remove comments
            int commentPos = line.IndexOf("//");
            if (commentPos >= 0)
                line = line.Substring(0, commentPos);

            line = line.Trim();
            if (string.IsNullOrEmpty(line))
                return result;

            // Parse parameters (handle quoted strings and operators)
            bool inQuote = false;
            var currentParam = new System.Text.StringBuilder();

            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];

                if (c == '"')
                {
                    inQuote = !inQuote;
                    currentParam.Append(c);
                }
                else if (char.IsWhiteSpace(c) && !inQuote)
                {
                    if (currentParam.Length > 0)
                    {
                        result.Add(currentParam.ToString());
                        currentParam.Clear();
                    }
                }
                else if (!inQuote && c == '&')
                {
                    // Split on & operator
                    if (currentParam.Length > 0)
                    {
                        result.Add(currentParam.ToString());
                        currentParam.Clear();
                    }
                    result.Add("&");
                }
                else
                {
                    currentParam.Append(c);
                }
            }

            if (currentParam.Length > 0)
                result.Add(currentParam.ToString());

            return result;
        }

        private bool HandleMacroCommand(List<string> paramLine, int lineNumber, byte scriptID)
        {
            string cmd = paramLine[0].ToUpperInvariant();

            switch (cmd)
            {
                case "WHILE":
                    HandleWhile(paramLine, lineNumber, scriptID);
                    return true;

                case "IF":
                    HandleIf(paramLine, lineNumber, scriptID);
                    return true;

                case "ELSE":
                    HandleElse(paramLine, lineNumber, scriptID);
                    return true;

                case "ELSEIF":
                    HandleElseIf(paramLine, lineNumber, scriptID);
                    return true;

                case "END":
                    HandleEnd(paramLine, lineNumber, scriptID);
                    return true;

                case "INCLUDE":
                    HandleInclude(paramLine);
                    return true;

                case "WAITON":
                case "WAITFOR":
                    HandleWaitOn(paramLine, lineNumber, scriptID);
                    return true;

                default:
                    return false; // Not a macro
            }
        }

        private void HandleWhile(List<string> paramLine, int lineNumber, byte scriptID)
        {
            if (paramLine.Count < 2)
                throw new Exception("No parameters to compare with WHILE macro");

            // Concatenate all parameters after WHILE into a single condition
            string condition = string.Join(" ", paramLine.Skip(1));

            var conStruct = new ConditionStruct
            {
                IsWhile = true,
                ConLabel = "::" + (++_ifLabelCount),
                EndLabel = "::" + (++_ifLabelCount)
            };

            _ifStack.Push(conStruct);
            RecurseCmd(new[] { conStruct.ConLabel }, lineNumber, scriptID);
            
            // Compile the condition to a temporary variable
            string tempVar = CompileCondition(condition, lineNumber, scriptID);
            
            // BRANCH to EndLabel if condition is false
            RecurseCmd(new[] { "BRANCH", tempVar, conStruct.EndLabel }, lineNumber, scriptID);
        }

        private string CompileCondition(string condition, int lineNumber, byte scriptID)
        {
            // Parse condition like "($var = value)" or "($var > 10)"
            // Or compound conditions like "($a > 0) and ($b < 10)"
            // Returns the name of the temporary variable holding the result
            
            condition = condition.Trim();
            
            // First, check for logical operators (and/or) at depth 0
            int logicalOpIndex = FindLogicalOperator(condition, out string logicalOp, out string logicalCmd);
            if (logicalOpIndex >= 0)
            {
                // Split on logical operator and compile each side
                string leftCondition = condition.Substring(0, logicalOpIndex).Trim();
                string rightCondition = condition.Substring(logicalOpIndex + logicalOp.Length).Trim();
                
                // Compile left and right conditions recursively
                string leftVar = CompileCondition(leftCondition, lineNumber, scriptID);
                string rightVar = CompileCondition(rightCondition, lineNumber, scriptID);
                
                // Create temp variable for combined result, initialized to left result
                string tempVarName = $"$$__cond{++_ifLabelCount}";
                var tempVar = new VarParam { Name = tempVarName, Value = "0" };
                _paramList.Add(tempVar);
                
                // Copy left result to temp var
                RecurseCmd(new[] { "SETVAR", tempVarName, leftVar }, lineNumber, scriptID);
                
                // Combine with right result using AND/OR command
                RecurseCmd(new[] { logicalCmd, tempVarName, rightVar }, lineNumber, scriptID);
                
                return tempVarName;
            }
            
            // Remove outer parentheses if present (only for simple comparisons)
            if (condition.StartsWith("(") && condition.EndsWith(")"))
            {
                int depth = 0;
                bool isSimple = true;
                for (int i = 0; i < condition.Length; i++)
                {
                    if (condition[i] == '(') depth++;
                    else if (condition[i] == ')') depth--;
                    if (depth == 0 && i < condition.Length - 1) { isSimple = false; break; }
                }
                if (isSimple)
                {
                    condition = condition.Substring(1, condition.Length - 2).Trim();

                    // After stripping the outer parens the inner content may itself be a
                    // compound logical expression (e.g. "A OR B" from "(A OR B)") that
                    // must be split on the logical operator rather than treated as a bare
                    // comparison.  Re-check now so that explicit grouping is honoured.
                    logicalOpIndex = FindLogicalOperator(condition, out logicalOp, out logicalCmd);
                    if (logicalOpIndex >= 0)
                    {
                        string innerLeft  = condition.Substring(0, logicalOpIndex).Trim();
                        string innerRight = condition.Substring(logicalOpIndex + logicalOp.Length).Trim();
                        string innerLeftVar  = CompileCondition(innerLeft,  lineNumber, scriptID);
                        string innerRightVar = CompileCondition(innerRight, lineNumber, scriptID);
                        string innerTempName = $"$$__cond{++_ifLabelCount}";
                        var innerTempVar = new VarParam { Name = innerTempName, Value = "0" };
                        _paramList.Add(innerTempVar);
                        RecurseCmd(new[] { "SETVAR",   innerTempName, innerLeftVar  }, lineNumber, scriptID);
                        RecurseCmd(new[] { logicalCmd, innerTempName, innerRightVar }, lineNumber, scriptID);
                        return innerTempName;
                    }
                }
            }
            
            // Find comparison operator
            string[] operators = { "<>", "<=", ">=", "=", "<", ">", "!=", "==", "<>" };
            string[] commands = { "ISNOTEQUAL", "ISLESSEREQUAL", "ISGREATEREQUAL", "ISEQUAL", "ISLESSER", "ISGREATER", "ISNOTEQUAL", "ISEQUAL", "ISNOTEQUAL" };
            
            string op = "";
            string cmd = "";
            int opIndex = -1;
            
            for (int i = 0; i < operators.Length; i++)
            {
                opIndex = condition.IndexOf(operators[i]);
                if (opIndex >= 0)
                {
                    op = operators[i];
                    cmd = commands[i];
                    break;
                }
            }
            
            if (opIndex < 0)
            {
                // No operator found - treat as boolean test (non-zero = true)
                // Create temp var and use ISNOTEQUAL to test != 0
                string tempVarName = $"$$__cond{++_ifLabelCount}";
                var tempVar = new VarParam { Name = tempVarName, Value = "0" };
                _paramList.Add(tempVar);
                
                RecurseCmd(new[] { "ISNOTEQUAL", tempVarName, condition.Trim(), "\"0\"" }, lineNumber, scriptID);
                return tempVarName;
            }
            
            // Split on operator
            string left = condition.Substring(0, opIndex).Trim();
            string right = condition.Substring(opIndex + op.Length).Trim();
            
            // Quote only plain literals (PARAM_CONST) - leave sysconsts, variables,
            // progvars, and chars unquoted so they compile correctly.
            if (IdentifyParam(left) == ScriptConstants.PARAM_CONST && !left.StartsWith("\""))
                left = $"\"{left}\"";
            if (IdentifyParam(right) == ScriptConstants.PARAM_CONST && !right.StartsWith("\""))
                right = $"\"{right}\"";
            
            // Create temporary variable for result
            string tempVarName2 = $"$$__cond{++_ifLabelCount}";
            var tempVar2 = new VarParam { Name = tempVarName2, Value = "0" };
            _paramList.Add(tempVar2);
            
            // Compile comparison command
            // ISEQUAL/ISGREATER/etc. take 3 params: result var, left operand, right operand
            RecurseCmd(new[] { cmd, tempVarName2, left, right }, lineNumber, scriptID);
            
            return tempVarName2;
        }

        private void HandleIf(List<string> paramLine, int lineNumber, byte scriptID)
        {
            if (paramLine.Count < 2)
                throw new Exception("No parameters to compare with IF macro");

            // Concatenate all parameters after IF into a single condition
            string condition = string.Join(" ", paramLine.Skip(1));

            var conStruct = new ConditionStruct
            {
                IsWhile = false,
                AtElse = false,
                ConLabel = "::" + (++_ifLabelCount),
                EndLabel = "::" + (++_ifLabelCount)
            };

            _ifStack.Push(conStruct);
            
            // Compile the condition to a temporary variable
            string tempVar = CompileCondition(condition, lineNumber, scriptID);
            
            // BRANCH to ConLabel if condition is false (tempVar != "1")
            RecurseCmd(new[] { "BRANCH", tempVar, conStruct.ConLabel }, lineNumber, scriptID);
        }

        private void HandleElse(List<string> paramLine, int lineNumber, byte scriptID)
        {
            if (paramLine.Count > 1)
                throw new Exception("Unnecessary parameters after ELSE macro");

            if (_ifStack.Count == 0)
                throw new Exception("ELSE without IF");

            var conStruct = _ifStack.Peek();

            if (conStruct.IsWhile)
                throw new Exception("Cannot use ELSE with WHILE");

            if (conStruct.AtElse)
                throw new Exception("IF macro syntax error");

            conStruct.AtElse = true;
            RecurseCmd(new[] { "GOTO", conStruct.EndLabel }, lineNumber, scriptID);
            RecurseCmd(new[] { conStruct.ConLabel }, lineNumber, scriptID);
        }

        private void HandleElseIf(List<string> paramLine, int lineNumber, byte scriptID)
        {
            if (paramLine.Count < 2)
                throw new Exception("No parameters to compare with ELSEIF macro");

            if (_ifStack.Count == 0)
                throw new Exception("ELSEIF without IF");

            var conStruct = _ifStack.Peek();

            if (conStruct.IsWhile)
                throw new Exception("Cannot use ELSEIF with WHILE");

            if (conStruct.AtElse)
                throw new Exception("IF macro syntax error");

            // Concatenate all parameters after ELSEIF into a single condition
            string condition = string.Join(" ", paramLine.Skip(1));

            RecurseCmd(new[] { "GOTO", conStruct.EndLabel }, lineNumber, scriptID);
            RecurseCmd(new[] { conStruct.ConLabel }, lineNumber, scriptID);
            conStruct.ConLabel = "::" + (++_ifLabelCount);
            
            // Compile the condition to a temporary variable
            string tempVar = CompileCondition(condition, lineNumber, scriptID);
            
            // BRANCH to new ConLabel if condition is false
            RecurseCmd(new[] { "BRANCH", tempVar, conStruct.ConLabel }, lineNumber, scriptID);
        }

        private void HandleEnd(List<string> paramLine, int lineNumber, byte scriptID)
        {
            if (paramLine.Count > 1)
                throw new Exception("Unnecessary parameters after END macro");

            if (_ifStack.Count == 0)
                throw new Exception("END without IF");

            var conStruct = _ifStack.Pop();

            if (conStruct.IsWhile)
                RecurseCmd(new[] { "GOTO", conStruct.ConLabel }, lineNumber, scriptID);
            else
                RecurseCmd(new[] { conStruct.ConLabel }, lineNumber, scriptID);

            RecurseCmd(new[] { conStruct.EndLabel }, lineNumber, scriptID);
        }

        private void HandleInclude(List<string> paramLine)
        {
            if (paramLine.Count > 2)
                throw new Exception("Unnecessary parameters after INCLUDE macro");
            if (paramLine.Count < 2)
                throw new Exception("No file name supplied for INCLUDE macro");

            string filename = paramLine[1];
            if (filename.StartsWith('"') && filename.EndsWith('"'))
                filename = filename.Substring(1, filename.Length - 2);

            IncludeFile(filename);
        }

        private void HandleWaitOn(List<string> paramLine, int lineNumber, byte scriptID)
        {
            if (paramLine.Count > 2)
                throw new Exception("Unnecessary parameters after WAITON macro");
            if (paramLine.Count < 2)
                throw new Exception("No wait text supplied for WAITON macro");

            _waitOnCount++;
            string triggerName = "WAITON" + _waitOnCount;
            string labelName = ":WAITON" + _waitOnCount;

            RecurseCmd(new[] { "SETTEXTTRIGGER", triggerName, labelName, paramLine[1] }, lineNumber, scriptID);
            RecurseCmd(new[] { "PAUSE" }, lineNumber, scriptID);
            RecurseCmd(new[] { labelName }, lineNumber, scriptID);
        }

        private void RecurseCmd(string[] cmdLine, int lineNumber, byte scriptID)
        {
            // Convert array to list and compile directly without re-parsing
            var paramList = new List<string>(cmdLine);
            CompileParamLine(paramList, lineNumber, scriptID);
        }

        private void CompileCommand(List<string> paramLine, int lineNumber, byte scriptID)
        {
            if (paramLine.Count == 0)
                return;

            string cmdName = paramLine[0].ToUpperInvariant();

            // Look up command in ScriptRef
            int cmdID = -1;
            if (_scriptRef is ScriptRef scriptRef)
            {
                cmdID = scriptRef.FindCmd(cmdName);
            }

            if (cmdID < 0)
            {
                // Unknown command - might be a label reference for GOTO/GOSUB
                if (!cmdName.StartsWith(':'))
                    throw new Exception($"Unknown command: {cmdName}");
            }

            // Write command header in Pascal format: [ScriptID:1][LineNumber:2][CmdID:2]
            ushort lineWord = (ushort)lineNumber;
            ushort cmdWord = (ushort)(cmdID >= 0 ? cmdID : 255);
            AppendCode(new[] { scriptID });
            AppendCode(BitConverter.GetBytes(lineWord));
            AppendCode(BitConverter.GetBytes(cmdWord));

            // Compile parameters
            for (int i = 1; i < paramLine.Count; i++)
            {
                CompileParameter(paramLine[i], lineNumber, scriptID);
            }

            // Null-terminate the parameter list (Pascal format)
            AppendCode(new byte[] { 0 });

            _cmdCount++;
        }

        private void CompileParameter(string param, int lineNumber, byte scriptID)
        {
            byte paramType = IdentifyParam(param);

            // Write parameter type
            AppendCode(new[] { paramType });

            // Handle different parameter types
            if (paramType == ScriptConstants.PARAM_CONST)
            {
                // String constant - remove quotes
                string value = param;
                if (value.StartsWith('"') && value.EndsWith('"'))
                {
                    value = value.Substring(1, value.Length - 2);
                    value = value.Replace("*", "\r"); // Replace * with CR
                }
                // Check if this is a label reference (starts with :) in an include file
                else if (value.StartsWith(':'))
                {
                    // Strip the colon, apply include prefix if needed, then restore colon
                    string labelName = value.Substring(1);
                    if (!labelName.Contains('~') && scriptID > 0 && scriptID <= _includeScriptList.Count)
                    {
                        labelName = _includeScriptList[scriptID - 1] + "~" + labelName;
                    }
                    value = ":" + labelName;
                }

                var newParam = new CmdParam { Value = value };
                int id = _paramList.Count;
                _paramList.Add(newParam);

                // Write 32-bit parameter ID
                AppendCode(BitConverter.GetBytes(id));
                
                // NOTE: PARAM_CONST does NOT have array indexes in TWX bytecode format
                // Only PARAM_VAR, PARAM_PROGVAR, and PARAM_SYSCONST have array index bytes
            }
            else if (paramType == ScriptConstants.PARAM_VAR)
            {
                // Convert dot notation to bracket index notation:
                // $sector.density  ->  $sector["density"]
                string convertedParam = ConvertDotNotation(param);

                // Variable - strip array indexes to get base name
                string varName = convertedParam;
                
                // Remove array indexes from variable name
                int bracketPos = varName.IndexOf('[');
                if (bracketPos > 0)
                    varName = varName.Substring(0, bracketPos);

                // Extend name for included scripts (skip system variables starting with $$)
                if (scriptID > 0 && scriptID <= _includeScriptList.Count && !varName.Contains('~') && !varName.StartsWith("$$"))
                {
                    varName = "$" + _includeScriptList[scriptID - 1] + "~" + varName.Substring(1);
                }

                // Find or create variable
                int id = FindOrCreateVariable(varName);

                // Write 32-bit variable ID
                AppendCode(BitConverter.GetBytes(id));

                // Write array indexes (pass scriptID so variable names inside indexes are extended)
                WriteArrayIndexes(convertedParam, scriptID);
            }
            else if (paramType == ScriptConstants.PARAM_PROGVAR)
            {
                // Convert dot notation to bracket index notation
                string convertedParam = ConvertDotNotation(param);

                // Program variable - strip array indexes to get base name
                string varName = convertedParam;
                
                // Remove array indexes from variable name
                int bracketPos = varName.IndexOf('[');
                if (bracketPos > 0)
                    varName = varName.Substring(0, bracketPos);

                // Extend name for included scripts (skip system variables starting with %%)
                if (scriptID > 0 && scriptID <= _includeScriptList.Count && !varName.Contains('~') && !varName.StartsWith("%%"))
                {
                    varName = "%" + _includeScriptList[scriptID - 1] + "~" + varName.Substring(1);
                }

                // Find or create variable
                int id = FindOrCreateVariable(varName);

                // Write 32-bit variable ID
                AppendCode(BitConverter.GetBytes(id));

                // Write array indexes (pass scriptID so variable names inside indexes are extended)
                WriteArrayIndexes(convertedParam, scriptID);
            }
            else if (paramType == ScriptConstants.PARAM_SYSCONST)
            {
                // System constant - extract base name without array indexes
                string constName = param;
                
                // Remove array indexes to get base constant name
                int indexLevel = 0;
                StringBuilder baseName = new StringBuilder();
                foreach (char c in constName)
                {
                    if (c == '[')
                        indexLevel++;
                    else if (c == ']')
                        indexLevel--;
                    else if (indexLevel == 0)
                        baseName.Append(c);
                }
                
                string baseConstName = baseName.ToString();
                int constID = -1;

                if (_scriptRef is ScriptRef scriptRef)
                {
                    constID = scriptRef.FindSysConst(baseConstName);
                }

                if (constID < 0)
                    throw new Exception($"Unknown system constant: {baseConstName}");

                // Write 16-bit system constant ID
                AppendCode(BitConverter.GetBytes((ushort)constID));

                // Write array indexes
                WriteArrayIndexes(param, scriptID);
            }
            else if (paramType == ScriptConstants.PARAM_CHAR)
            {
                // Character code (#32, etc.)
                string charStr = param.Substring(1); // Remove #
                if (!int.TryParse(charStr, out int charCode) || charCode < 0 || charCode > 255)
                    throw new Exception($"Invalid character code: {param}");

                // Write 1 byte character code directly (NOT a parameter ID!)
                // Format: PARAM_CHAR type byte + 1 byte char code
                AppendCode(new[] { (byte)charCode });
            }
        }

        private int FindOrCreateVariable(string varName)
        {
            // Search for existing variable - case-insensitive to match Pascal TWX 2.x behaviour
            for (int i = 0; i < _paramList.Count; i++)
            {
                if (_paramList[i] is VarParam varParam &&
                    string.Equals(varParam.Name, varName, StringComparison.OrdinalIgnoreCase))
                    return i;
            }

            // Create new variable
            var newVar = new VarParam { Name = varName, Value = "0" };
            _paramList.Add(newVar);
            return _paramList.Count - 1;
        }

        /// <summary>
        /// Converts dot-notation field access to bracket-index notation so the compiler
        /// stores sub-fields as hierarchical VarParam entries rather than flat names.
        ///   $sector.density          ->  $sector["density"]
        ///   $sector.warp[$i]         ->  $sector["warp"][$i]
        ///   $sector.port.exists      ->  $sector["port"]["exists"]
        /// Only processes $var and %progvar tokens.
        /// </summary>
        private static string ConvertDotNotation(string param)
        {
            if (string.IsNullOrEmpty(param))
                return param;

            // Only convert $ and % variable references
            if (param[0] != '$' && param[0] != '%')
                return param;

            // Find position of first '[' – only convert the prefix before any bracket index
            int firstBracket = param.IndexOf('[');
            string beforeBracket = firstBracket >= 0 ? param.Substring(0, firstBracket) : param;
            string afterBracket  = firstBracket >= 0 ? param.Substring(firstBracket) : "";

            if (!beforeBracket.Contains('.'))
                return param; // no dots, nothing to do

            // Split prefix on dots: $sector.warp  ->  [$sector, warp]
            string[] parts = beforeBracket.Split('.');
            string result = parts[0]; // base $var / %var name
            for (int di = 1; di < parts.Length; di++)
                result += $"[\"{parts[di]}\"]";

            return result + afterBracket;
        }

        private List<string> ExtractArrayIndexes(string param)
        {
            // Extract array indexes from a parameter like VAR[index1][index2]
            // Returns list of index expressions
            var indexes = new List<string>();
            int level = 0;
            StringBuilder currentIndex = new StringBuilder();
            bool inIndex = false;

            foreach (char c in param)
            {
                if (c == '[')
                {
                    if (level == 0)
                    {
                        inIndex = true;
                        currentIndex.Clear();
                    }
                    else
                    {
                        currentIndex.Append(c);
                    }
                    level++;
                }
                else if (c == ']')
                {
                    level--;
                    if (level == 0)
                    {
                        indexes.Add(currentIndex.ToString());
                        inIndex = false;
                    }
                    else
                    {
                        currentIndex.Append(c);
                    }
                }
                else if (inIndex)
                {
                    currentIndex.Append(c);
                }
            }

            return indexes;
        }

        private void WriteArrayIndexes(string param, byte scriptID = 0)
        {
            // Parse and write array indexes from parameter
            var indexes = ExtractArrayIndexes(param);
            
            if (indexes.Count == 0)
            {
                // No indexes
                AppendCode(new byte[] { 0 });
                return;
            }

            // Write index count
            AppendCode(new byte[] { (byte)indexes.Count });

            // Write each index: type byte + data (format matches TWX decompiler ReadIndexValues)
            foreach (string indexExpr in indexes)
            {
                // Each index can be a variable, constant, or sysconst
                byte indexType = IdentifyParam(indexExpr);
                
                // Write type byte first
                AppendCode(new[] { indexType });

                if (indexType == ScriptConstants.PARAM_VAR || indexType == ScriptConstants.PARAM_PROGVAR)
                {
                    // Variable index – extend name for include scripts
                    string indexVarName = indexExpr;
                    int idxBracketPos = indexVarName.IndexOf('[');
                    if (idxBracketPos > 0)
                        indexVarName = indexVarName.Substring(0, idxBracketPos);

                    if (scriptID > 0 && scriptID <= _includeScriptList.Count && !indexVarName.Contains('~'))
                    {
                        if (indexVarName.StartsWith("$") && !indexVarName.StartsWith("$$"))
                            indexVarName = "$" + _includeScriptList[scriptID - 1] + "~" + indexVarName.Substring(1);
                        else if (indexVarName.StartsWith("%") && !indexVarName.StartsWith("%%"))
                            indexVarName = "%" + _includeScriptList[scriptID - 1] + "~" + indexVarName.Substring(1);
                    }

                    int indexId = FindOrCreateVariable(indexVarName);
                    AppendCode(BitConverter.GetBytes(indexId));
                    WriteArrayIndexes(indexExpr, scriptID); // Recursive for nested arrays
                }
                else if (indexType == ScriptConstants.PARAM_SYSCONST)
                {
                    // System constant - write 16-bit ID + recursively write its indexes
                    string constName = indexExpr;
                    int indexLevel = 0;
                    StringBuilder baseName = new StringBuilder();
                    foreach (char c in constName)
                    {
                        if (c == '[') indexLevel++;
                        else if (c == ']') indexLevel--;
                        else if (indexLevel == 0) baseName.Append(c);
                    }
                    
                    int indexId = -1;
                    if (_scriptRef is ScriptRef scriptRef)
                    {
                        indexId = scriptRef.FindSysConst(baseName.ToString());
                    }
                    
                    if (indexId < 0)
                        throw new Exception($"Unknown system constant in array index: {indexExpr}");
                    
                    AppendCode(BitConverter.GetBytes((ushort)indexId));
                    WriteArrayIndexes(indexExpr, scriptID); // Recursive for nested arrays
                }
                else if  (indexType == ScriptConstants.PARAM_CONST)
                {
                    // Constant index - write 32-bit parameter ID
                    string value = indexExpr;
                    if (value.StartsWith('"') && value.EndsWith('"'))
                        value = value.Substring(1, value.Length - 2);
                    
                    var newParam = new CmdParam { Value = value };
                    int indexId = _paramList.Count;
                    _paramList.Add(newParam);
                    AppendCode(BitConverter.GetBytes(indexId));
                }
                else if (indexType == ScriptConstants.PARAM_CHAR)
                {
                    // Character index - write 1 byte
                    if (indexExpr.StartsWith('#'))
                    {
                        byte charCode = byte.Parse(indexExpr.Substring(1));
                        AppendCode(new[] { charCode });
                    }
                }
            }
        }

        private void IncludeFile(string filename)
        {
            // Include another script file
            // Normalize path separators (Windows scripts may use backslashes)
            filename = filename.Replace('\\', Path.DirectorySeparatorChar);
            
            // Automatically append .ts extension if no extension is present
            if (string.IsNullOrEmpty(Path.GetExtension(filename)))
            {
                filename += ".ts";
            }
            
            // If the path is not absolute, resolve it relative to the script directory
            string fullPath = filename;
            
            if (!Path.IsPathRooted(filename))
            {
                // Try relative to script directory first
                if (!string.IsNullOrEmpty(_scriptDirectory))
                {
                    fullPath = Path.Combine(_scriptDirectory, filename);
                }
            }
            
            if (!File.Exists(fullPath))
                throw new FileNotFoundException($"Include file not found: {filename} (searched: {fullPath})");

            var scriptText = new List<string>(File.ReadAllLines(fullPath));
            
            // Get the actual filename from the filesystem (preserves correct case)
            // This is important because include statements may use different case than the actual file
            // e.g., "include\move" should get the actual filename "Move.ts" on case-insensitive filesystems
            // On case-insensitive filesystems, FileInfo.Name returns the name as passed in, not the actual case
            // So we need to query the directory to get the real filename
            string? directory = Path.GetDirectoryName(fullPath);
            string searchFileName = Path.GetFileName(fullPath);
            string actualFileName = searchFileName; // fallback
            
            if (!string.IsNullOrEmpty(directory) && Directory.Exists(directory))
            {
                // Find the actual file with correct case from the directory listing
                var filesInDir = Directory.GetFiles(directory);
                foreach (var file in filesInDir)
                {
                    string fileNameInDir = Path.GetFileName(file);
                    if (string.Equals(fileNameInDir, searchFileName, StringComparison.OrdinalIgnoreCase))
                    {
                        actualFileName = fileNameInDir;
                        break;
                    }
                }
            }
            
            string includeName = Path.GetFileNameWithoutExtension(actualFileName);
            _includeScriptList.Add(includeName);
            
            // Compile the included script with scriptID = index + 1 (0 is main script)
            // For first include at index 0, scriptID = 1
            byte scriptID = (byte)_includeScriptList.Count;
            
            int savedLineCount = _lineCount;
            _lineCount = 0;
            
            foreach (var line in scriptText)
            {
                _lineCount++;
                try
                {
                    CompileParamLine(line, _lineCount, scriptID);
                }
                catch (Exception ex)
                {
                    throw new Exception($"Error in include '{includeName}' line {_lineCount}: {ex.Message}", ex);
                }
            }
            
            _lineCount = savedLineCount;
        }

        #endregion

        #region File I/O Methods

        public void WriteToFile(string filename)
        {
            using (var fs = new FileStream(filename, FileMode.Create, FileAccess.Write))
            using (var writer = new BinaryWriter(fs))
            {
                // Write 24-byte Pascal TScriptFileHeader (matches ReadScriptFileHeader layout):
                //   offset  0    : ShortString length byte (10)
                //   offset  1-10 : "TWX SCRIPT" (10 chars, no null terminator)
                //   offset 11    : 1 alignment pad byte
                //   offset 12-13 : Version (uint16)
                //   offset 14-15 : 2 alignment pad bytes
                //   offset 16-19 : DescSize (int32)
                //   offset 20-23 : CodeSize (int32)
                byte[] descBytes = Encoding.ASCII.GetBytes(string.Join("\r\n", _description));
                writer.Write((byte)10);                                        // ShortString length
                writer.Write(Encoding.ASCII.GetBytes("TWX SCRIPT"));           // 10 chars
                writer.Write((byte)0);                                         // offset 11 pad
                writer.Write((ushort)ScriptConstants.CompiledScriptVersion);   // offset 12-13
                writer.Write((byte)0);                                         // offset 14 pad
                writer.Write((byte)0);                                         // offset 15 pad
                writer.Write(descBytes.Length);                                // offset 16-19 DescSize
                writer.Write(_codeSize);                                       // offset 20-23 CodeSize

                // Write description then code (reader reads them in this order after the header)
                writer.Write(descBytes);
                writer.Write(_code);

                // Write parameters
                foreach (var param in _paramList)
                {
                    if (param is VarParam varParam)
                    {
                        writer.Write((byte)2); // TVarParam
                        string encrypted = ApplyEncryption(varParam.Value, 113);
                        writer.Write(encrypted.Length);
                        writer.Write(Encoding.Latin1.GetBytes(encrypted));

                        encrypted = ApplyEncryption(varParam.Name, 113);
                        writer.Write(encrypted.Length);
                        writer.Write(Encoding.Latin1.GetBytes(encrypted));
                    }
                    else
                    {
                        writer.Write((byte)1); // TCmdParam
                        string encrypted = ApplyEncryption(param.Value, 113);
                        writer.Write(encrypted.Length);
                        writer.Write(Encoding.Latin1.GetBytes(encrypted));
                    }
                }
                writer.Write((byte)0); // End of parameters

                // Write include scripts
                foreach (var include in _includeScriptList)
                {
                    writer.Write(include.Length);
                    writer.Write(Encoding.ASCII.GetBytes(include));
                }
                writer.Write(0); // End of includes

                // Write labels
                foreach (var label in _labelList)
                {
                    writer.Write(label.Location);
                    writer.Write(label.Name.Length);
                    writer.Write(Encoding.ASCII.GetBytes(label.Name));
                }
            }
        }

        /// <summary>
        /// Reads the 24-byte Pascal TScriptFileHeader from a BinaryReader.
        /// Layout (Win32 default alignment, BlockWrite'd verbatim by the Pascal compiler):
        ///   offset  0    : ShortString length byte (always 10 = 0x0A for "TWX SCRIPT")
        ///   offset  1-10 : ShortString characters  ("TWX SCRIPT")
        ///   offset 11    : 1 alignment pad byte
        ///   offset 12-13 : Version   (Word/uint16, little-endian)
        ///   offset 14-15 : 2 alignment pad bytes
        ///   offset 16-19 : DescSize  (Integer/int32, little-endian)
        ///   offset 20-23 : CodeSize  (Integer/int32, little-endian)
        /// Returns (progName, version, descSize, codeSize). Throws on bad magic or short file.
        /// </summary>
        public static (string progName, int version, int descSize, int codeSize) ReadScriptFileHeader(BinaryReader reader)
        {
            byte[] hdr = reader.ReadBytes(24);
            if (hdr.Length < 24)
                throw new Exception("File is too short to be a compiled TWX script");

            byte nameLen = hdr[0];
            string progName = nameLen <= 10
                ? Encoding.ASCII.GetString(hdr, 1, nameLen)
                : string.Empty;
            int version  = BitConverter.ToUInt16(hdr, 12);
            int descSize = BitConverter.ToInt32(hdr, 16);
            int codeSize = BitConverter.ToInt32(hdr, 20);

            if (progName != "TWX SCRIPT")
                throw new Exception("File is not a compiled TWX script");

            return (progName, version, descSize, codeSize);
        }

        public void LoadFromFile(string filename)
        {
            if (_codeSize > 0)
                throw new Exception("Code already exists - cannot load from file");

            using (var fs = new FileStream(filename, FileMode.Open, FileAccess.Read))
            using (var reader = new BinaryReader(fs))
            {
                GlobalModules.DebugLog($"[DEBUG] Loading file: {filename} ({fs.Length} bytes)\n");

                var (progName, version, descSize, codeSize) = ReadScriptFileHeader(reader);

                GlobalModules.DebugLog($"[ScriptCmp.LoadFromFile] File: {filename}\n");
                GlobalModules.DebugLog($"[ScriptCmp.LoadFromFile] File size: {fs.Length} bytes\n");
                GlobalModules.DebugLog($"[ScriptCmp.LoadFromFile] Header string: '{progName}', Version: {version}, DescSize: {descSize}, CodeSize: {codeSize}\n");

                if (version < 2 || version > ScriptConstants.CompiledScriptVersion)
                    throw new Exception($"Script file is an incorrect version ({version}); supported versions are 2-{ScriptConstants.CompiledScriptVersion}");

                _version = version;

                // Read and skip description
                byte[] descBytes = descSize > 0 ? reader.ReadBytes(descSize) : Array.Empty<byte>();
                string description = descSize > 0 ? Encoding.ASCII.GetString(descBytes) : string.Empty;
                if (!string.IsNullOrEmpty(description))
                    _description = new List<string>(description.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None));
                else
                    _description = new List<string>();

                GlobalModules.DebugLog($"[ScriptCmp.LoadFromFile] Stream position before reading code: {fs.Position}\n");

                // Read code
                _code = reader.ReadBytes(codeSize);
                _codeSize = codeSize;
                
                GlobalModules.DebugLog($"[ScriptCmp.LoadFromFile] Read {codeSize} bytes of code, position now: {fs.Position}\n");

                // Read parameters
                byte paramType = reader.ReadByte();
                while (paramType > 0)
                {
                    if (paramType == 1)
                    {
                        // TCmdParam
                        int len = reader.ReadInt32();
                        string val = Encoding.Latin1.GetString(reader.ReadBytes(len));
                        var param = new CmdParam
                        {
                            Value = ApplyEncryption(val, 113)
                        };
                        _paramList.Add(param);
                    }
                    else
                    {
                        // TVarParam
                        int len = reader.ReadInt32();
                        string val = Encoding.Latin1.GetString(reader.ReadBytes(len));
                        var param = new VarParam
                        {
                            Value = ApplyEncryption(val, 113)
                        };

                        len = reader.ReadInt32();
                        val = Encoding.Latin1.GetString(reader.ReadBytes(len));
                        param.Name = ApplyEncryption(val, 113);

                        _paramList.Add(param);
                    }

                    paramType = reader.ReadByte();
                }
                
                GlobalModules.DebugLog($"[ScriptCmp.LoadFromFile] Read {_paramList.Count} parameters, position now: {fs.Position}\n");
                GlobalModules.DebugLog($"[ScriptCmp.LoadFromFile] File length: {fs.Length}, bytes remaining: {fs.Length - fs.Position}\n");

                // Note: the Pascal compiler (CompileValue) deduplicates variables at compile time —
                // when a variable name is seen again, it reuses the existing paramID rather than
                // creating a new entry. Our C# TWXC does the same via FindOrCreateVariable.
                // Therefore a well-formed CTS file already has one entry per unique variable name,
                // and no post-load deduplication is needed. All bytecode references to a given
                // variable share the same paramID pointing to the same single VarParam object.

                // Read include scripts (only if data remains)
                if (fs.Position + 4 <= fs.Length)
                {
                    GlobalModules.DebugLog($"[ScriptCmp.LoadFromFile] Attempting to read includeLen...\n");
                    try
                    {
                        int includeLen = reader.ReadInt32();
                        GlobalModules.DebugLog($"[ScriptCmp.LoadFromFile] Read includeLen: {includeLen}\n");
                        while (includeLen > 0)
                        {
                            string include = Encoding.ASCII.GetString(reader.ReadBytes(includeLen));
                            _includeScriptList.Add(include);
                            includeLen = reader.ReadInt32();
                        }
                    }
                    catch (EndOfStreamException ex)
                    {
                        GlobalModules.DebugLog($"[ScriptCmp.LoadFromFile] Hit EOF while reading includes: {ex.Message}\n");
                        // Continue - includes are optional
                    }
                }
                else
                {
                    GlobalModules.DebugLog($"[ScriptCmp.LoadFromFile] No include scripts section (EOF at {fs.Position})\n");
                }

                // Read labels
                while (fs.Position < fs.Length)
                {
                    int location = reader.ReadInt32();
                    int len = reader.ReadInt32();
                    string name = Encoding.ASCII.GetString(reader.ReadBytes(len));

                    var label = new ScriptLabel
                    {
                        Name = name,
                        Location = location
                    };
                    _labelList.Add(label);
                    // First definition wins - consistent with FirstOrDefault
                    if (!_labelDict.ContainsKey(name))
                        _labelDict[name] = location;
                }
            }

            _scriptFile = filename;
        }

        public void AddParam(CmdParam param)
        {
            _paramList.Add(param);
        }

        #endregion
    }

    #endregion
}
