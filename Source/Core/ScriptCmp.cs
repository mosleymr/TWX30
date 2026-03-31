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
                if (key.Length >= 2 && key[0] == '"' && key[^1] == '"')
                    key = key.Substring(1, key.Length - 2);
                foreach (var v in _vars)
                {
                    if (string.Equals(v.Name, key, StringComparison.OrdinalIgnoreCase))
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

            GlobalModules.Server?.Broadcast(
                $"{tab}{AnsiCodes.ANSI_15}\"{AnsiCodes.ANSI_7}{Name}{AnsiCodes.ANSI_15}\" = \"{AnsiCodes.ANSI_7}{Value}{AnsiCodes.ANSI_15}\"\r\n");

            if (_vars.Count > 0)
            {
                // Dump array contents
                string arrayType = _arraySize > 0 ? "Static" : "Dynamic";
                GlobalModules.Server?.Broadcast(
                    $"{tab}{AnsiCodes.ANSI_15}{arrayType} array of \"{AnsiCodes.ANSI_7}{Name}{AnsiCodes.ANSI_15}\" (size {_vars.Count})\r\n");

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
        private string _rootScriptDirectory = string.Empty;
        private byte[] _code = Array.Empty<byte>();
        private int _ifLabelCount;
        private int _sysVarCount; // Resets per source line; temp vars reuse names for deduplication
        private int _waitOnCount;
        private int _lineCount;
        private int _cmdCount;
        private int _codeSize;
        private int _version = ScriptConstants.CompiledScriptVersion;
        private ScriptRef? _scriptRef;
        private PreparedScriptProgram? _preparedProgram;

        public ScriptCmp(ScriptRef? scriptRef = null, string scriptDirectory = "")
        {
            _paramList = new List<CmdParam>();
            _labelList = new List<ScriptLabel>();
            _labelDict = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            _description = new List<string>();
            _scriptRef = scriptRef ?? new ScriptRef();
            _scriptDirectory = string.IsNullOrWhiteSpace(scriptDirectory)
                ? string.Empty
                : Path.GetFullPath(scriptDirectory);
            _rootScriptDirectory = _scriptDirectory;
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
            _preparedProgram = null;
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
        public PreparedScriptProgram? PreparedProgram => _preparedProgram;
        public int PreparedInstructionCount => _preparedProgram?.Instructions.Length ?? 0;
        /// <summary>Description as a single string (lines joined with '\n').</summary>
        public string DescriptionText => _description.Count > 0 ? string.Join("\n", _description) : string.Empty;

        public PreparedScriptProgram? PrepareForExecution()
        {
            if (_preparedProgram != null)
                return _preparedProgram;

            try
            {
                _preparedProgram = PreparedScriptDecoder.Decode(this);
            }
            catch (Exception ex)
            {
                GlobalModules.DebugLog($"[ScriptCmp.PrepareForExecution] Failed for '{_scriptFile}': {ex.Message}\n");
                _preparedProgram = null;
            }

            return _preparedProgram;
        }

        public VarParam GetOrCreateRuntimeVar(string varName)
        {
            int id = FindOrCreateVariable(varName);
            return (VarParam)_paramList[id];
        }

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

        public string GetScriptNamespace(int scriptID)
        {
            if (scriptID > 0 && scriptID < _includeScriptList.Count)
                return _includeScriptList[scriptID];

            return string.Empty;
        }

        public string QualifyLabelReference(string name, int scriptID)
        {
            if (string.IsNullOrEmpty(name))
                return name;

            // Pascal bytecode preserves label references exactly as written in source.
            // Include-local refs such as ":BUY", ":246", and "::308" stay local in the
            // compiled parameter table and are resolved at runtime using ExecScriptID.
            // Only already-qualified cross-script refs carry a namespace in source.
            bool hasColon = name.StartsWith(':');
            string labelName = hasColon ? name.Substring(1) : name;
            if (string.IsNullOrEmpty(labelName))
                return name;

            if (!hasColon)
                return name;

            return name;
        }

        public string StripLocalLabelReference(string name, int scriptID)
        {
            if (string.IsNullOrEmpty(name))
                return name;

            bool hasColon = name.StartsWith(':');
            string labelName = hasColon ? name.Substring(1) : name;
            string scriptNamespace = GetScriptNamespace(scriptID);
            if (string.IsNullOrEmpty(scriptNamespace))
                return name;

            string prefix = scriptNamespace + "~";
            if (!labelName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return name;

            string stripped = labelName.Substring(prefix.Length);
            return hasColon ? ":" + stripped : stripped;
        }

        public void ExtendName(ref string name, int scriptID)
        {
            // Don't extend system variables (starting with $$)
            if (name.StartsWith("$$"))
                return;
                
            if (!name.Contains('~'))
            {
                if (scriptID > 0 && scriptID < _includeScriptList.Count)
                {
                    if (name.Length > 1 && (name[0] == '$' || name[0] == '%') && char.IsDigit(name[1]))
                        name = name[0] + _includeScriptList[scriptID] + "~" + name;
                    else
                        name = _includeScriptList[scriptID] + "~" + name;
                }
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
            return "+-*/%<>=&|!^".Contains(c) ||
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
            _preparedProgram = null;
            _scriptFile = filename;
            _scriptDirectory = Path.GetDirectoryName(Path.GetFullPath(filename)) ?? string.Empty;
            _rootScriptDirectory = _scriptDirectory;
            _ifLabelCount = 0;
            _waitOnCount = 0;
            _lineCount = 0;
            _cmdCount = 0;
            _includeScriptList.Clear();

            // Read script file
            var scriptText = LoadSourceLines(filename);

            // Read description file if provided
            if (!string.IsNullOrEmpty(descFile) && File.Exists(descFile))
            {
                _description.AddRange(File.ReadAllLines(descFile, Encoding.Latin1));
            }

            CompileFromStrings(scriptText, Path.GetFileName(filename));
        }

        public void CompileFromStrings(List<string> scriptText, string scriptName)
        {
            _scriptFile = scriptName;
            string includeName = Path.GetFileName(scriptName).ToUpperInvariant();
            byte scriptID = (byte)_includeScriptList.Count;
            _includeScriptList.Add(includeName);
            int localLineCount = 0;

            foreach (var line in scriptText)
            {
                _lineCount++;
                localLineCount++;
                try
                {
                    CompileParamLine(line, localLineCount, scriptID);
                }
                catch (Exception ex)
                {
                    throw new Exception($"Error on line {localLineCount}: {ex.Message}", ex);
                }
            }

            // Check for unclosed IF/WHILE blocks
            if (_ifStack.Count > 0)
            {
                throw new Exception("IF or WHILE block not terminated with END");
            }
        }

        // ── Pascal preprocessing ────────────────────────────────────────────────────
        // Replaces AND/OR/XOR word tokens (outside quotes) with single sentinel chars.
        // Called with a trailing space already appended (as Pascal does).
        private static string ConvertOps(string line)
        {
            bool inQuote = false;
            var result = new StringBuilder();
            var token = new StringBuilder();
            foreach (char c in line)
            {
                if (c == '"') inQuote = !inQuote;
                if (c == ' ')
                {
                    if (!inQuote)
                    {
                        string up = token.ToString().ToUpperInvariant();
                        if      (up == "AND") token.Clear().Append(ScriptConstants.OP_AND);
                        else if (up == "OR")  token.Clear().Append(ScriptConstants.OP_OR);
                        else if (up == "XOR") token.Clear().Append(ScriptConstants.OP_XOR);
                    }
                    result.Append(token).Append(' ');
                    token.Clear();
                }
                else token.Append(c);
            }
            return result.ToString();
        }

        // Replaces >=, <=, <> with single sentinel chars (outside quotes).
        private static string ConvertConditions(string line)
        {
            bool inQuote = false;
            var result = new StringBuilder();
            char last = '\0';
            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (c == '"')
                {
                    inQuote = !inQuote;
                    result.Append(c);
                }
                else if (c == '=' && !inQuote)
                {
                    if      (last == '>') result.Append(ScriptConstants.OP_GREATEREQUAL);
                    else if (last == '<') result.Append(ScriptConstants.OP_LESSEREQUAL);
                    else                  result.Append('=');
                }
                else if ((c != '>' && c != '<') || inQuote)
                {
                    if ((last == '>' || last == '<') && !inQuote)
                        result.Append(last); // flush deferred > or <
                    result.Append(c);
                }
                else if (last == '<' && c == '>')
                {
                    result.Append(ScriptConstants.OP_NOTEQUAL);
                    c = '\0'; // squash so last doesn't become '>'
                }
                // else: c is standalone '>' or '<' not yet resolved — defer via last
                last = c;
            }
            return result.ToString();
        }

        private void CompileParamLine(string line, int lineNumber, byte scriptID)
        {
            // Reset per-line temp-var counter (matching Pascal's SysVarCount := 0 per source line).
            _sysVarCount = 0;

            line = line.TrimStart();
            if (line.Length == 0 || line[0] == '#')
                return;

            // Pascal preprocessing: convert AND/OR/XOR word tokens and >=/<=/<> to single
            // sentinel chars so the operator-linking tokenizer can accumulate them into tokens.
            // Append trailing space as Pascal does (ConvertOps relies on it to flush last word).
            string processed = ConvertConditions(ConvertOps(line + " "));

            // ── Operator-linking tokenizer (matches Pascal's CompileFromStrings loop) ──────
            // Operator chars are accumulated INTO the current token via the Linked flag.
            // Whitespace does NOT split the current token when Linked is true.
            // This means $a OR $b → after ConvertOps → $a OP_OR $b → one token "$aOP_OR$b".
            var paramLine = new List<string>();
            var sb = new StringBuilder();
            bool inQuote = false;
            bool linked = false;
            char last = ' ';

            for (int i = 0; i < processed.Length; i++)
            {
                char c = processed[i];

                // # at the very start of a fresh first token = comment line
                if (c == '#' && sb.Length == 0 && paramLine.Count == 0)
                    break;

                // // inline comment — remove the first '/' from the token buffer and stop
                if (c == '/' && last == '/' && !inQuote)
                {
                    if (sb.Length > 0) sb.Remove(sb.Length - 1, 1);
                    linked = false;
                    break;
                }

                if (!inQuote && IsOperator(c))
                {
                    if (linked)
                        throw new Exception($"Operation syntax error at line {lineNumber}");
                    linked = true;
                    sb.Append(c);
                }
                else if ((c != ' ' && c != '\t') || inQuote)
                {
                    // Flush completed token when whitespace precedes a non-linked, non-quoted char
                    if ((last == ' ' || last == '\t') && !linked && !inQuote && sb.Length > 0)
                    {
                        paramLine.Add(sb.ToString());
                        sb.Clear();
                    }
                    sb.Append(inQuote ? c : char.ToUpperInvariant(c));
                    if (c == '"') inQuote = !inQuote;
                    linked = false;
                }

                last = c;
            }

            if (sb.Length > 0)
                paramLine.Add(sb.ToString());

            CompileParamLine(paramLine, lineNumber, scriptID);
        }

        private void CompileParamLine(List<string> paramLine, int lineNumber, byte scriptID)
        {
            if (paramLine.Count == 0)
                return;

            string firstParam = paramLine[0];

            // Label declaration
            if (firstParam.StartsWith(':'))
            {
                if (paramLine.Count > 1)
                    throw new Exception("Unnecessary parameters after label declaration");
                if (firstParam.Length < 2)
                    throw new Exception("Bad label name");

                string labelName = firstParam.Substring(1);
                if (!labelName.Contains('~') && scriptID > 0 && scriptID < _includeScriptList.Count)
                {
                    // Preserve Pascal's distinction between source local labels
                    // (:76 -> HAGGLE~76) and compiler-generated flow labels
                    // (::11 -> HAGGLE~:11) by keeping the local label text as-is.
                    labelName = _includeScriptList[scriptID] + "~" + labelName;
                }

                BuildLabel(labelName, _codeSize);
                return;
            }

            // Macro commands (IF, WHILE, ELSE, ELSEIF, END, INCLUDE, WAITON)
            if (HandleMacroCommand(paramLine, lineNumber, scriptID))
                return;

            // Regular command — expressions in each token are compiled by CompileParam
            CompileCommand(paramLine, lineNumber, scriptID);
        }




        // ── Operator group constants for BreakDown (matching Pascal precedence) ─────────────
        // Group1 = lowest priority (split first) — comparisons, logical, concat
        // Group2 = medium priority — additive
        // Group3 = highest priority (split last) — multiplicative
        private static readonly string OpsGroup1 =
            "=<>&" + ScriptConstants.OP_GREATEREQUAL + ScriptConstants.OP_LESSEREQUAL
                   + ScriptConstants.OP_AND + ScriptConstants.OP_OR + ScriptConstants.OP_XOR
                   + ScriptConstants.OP_NOTEQUAL;
        private const string OpsGroup2 = "+-";
        private const string OpsGroup3 = "*/%";

        // ── Binary expression tree node ──────────────────────────────────────────────────────
        private sealed class ExprNode
        {
            public char      Op        { get; set; } = ScriptConstants.OP_NONE;
            public string?   LeafValue { get; set; }
            public ExprNode? Left      { get; set; }
            public ExprNode? Right     { get; set; }
        }

        /// <summary>
        /// Splits <paramref name="eq"/> on the first character in <paramref name="ops"/>
        /// found at bracket-depth 0, outside quotes.  Returns false when no match is found.
        /// </summary>
        private static bool SplitOperator(string eq, string ops,
            out string v1, out string v2, out char op)
        {
            v1 = v2 = "";
            op = ScriptConstants.OP_NONE;
            bool inQ = false;
            int depth = 0;
            for (int i = 0; i < eq.Length; i++)
            {
                char c = eq[i];
                if (c == '"') { inQ = !inQ; continue; }
                if (inQ) continue;
                if (c == '(') { depth++; continue; }
                if (c == ')') { depth--; continue; }
                if (depth > 0) continue;
                if (ops.IndexOf(c) >= 0)
                {
                    if (i == 0)
                        throw new Exception($"SplitOperator: operator '{(int)c}' at position 0 in \"{eq}\"");
                    if (i == eq.Length - 1)
                        throw new Exception($"SplitOperator: operator '{(int)c}' at end of \"{eq}\"");
                    v1 = eq.Substring(0, i);
                    v2 = eq.Substring(i + 1);
                    op = c;
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Recursively decomposes an expression string into an operator-precedence binary tree,
        /// matching Pascal's <c>BreakDown</c> function.
        /// </summary>
        private static ExprNode BreakDown(string eq)
        {
            eq = eq.Trim();
            // Strip matched outer parentheses (while the whole expression is wrapped)
            while (eq.StartsWith("(") && eq.EndsWith(")"))
            {
                int depth = 0;
                bool isOuter = true;
                for (int i = 0; i < eq.Length; i++)
                {
                    if (eq[i] == '(') depth++;
                    else if (eq[i] == ')') depth--;
                    if (depth == 0 && i < eq.Length - 1) { isOuter = false; break; }
                }
                if (!isOuter) break;
                eq = eq.Substring(1, eq.Length - 2).Trim();
            }

            if (SplitOperator(eq, OpsGroup1, out string v1, out string v2, out char opFound) ||
                SplitOperator(eq, OpsGroup2, out v1, out v2, out opFound) ||
                SplitOperator(eq, OpsGroup3, out v1, out v2, out opFound))
            {
                return new ExprNode { Op = opFound, Left = BreakDown(v1), Right = BreakDown(v2) };
            }

            return new ExprNode { Op = ScriptConstants.OP_NONE, LeafValue = eq };
        }

        /// <summary>
        /// Recursively emits opcodes for the expression tree rooted at <paramref name="node"/>.
        /// Returns the name of the variable or literal that holds the result.
        /// Matches Pascal's <c>CompileTree</c>.
        /// </summary>
        private string CompileTree(ExprNode node, int lineNumber, byte scriptID)
        {
            _sysVarCount++;
            string result = scriptID > 0 ? "$" + _sysVarCount : "$$" + _sysVarCount;

            if (node.Op == ScriptConstants.OP_NONE)
                return node.LeafValue!;

            string v1 = CompileTree(node.Left!,  lineNumber, scriptID);
            string v2 = CompileTree(node.Right!, lineNumber, scriptID);

            char op = node.Op;
            if (op == '&')
            {
                // String concatenation -> MERGETEXT src1 src2 dest
                RecurseCmd(new[] { "MERGETEXT", v1, v2, result }, lineNumber, scriptID);
            }
            else if (op == '=')
                RecurseCmd(new[] { "ISEQUAL",        result, v1, v2 }, lineNumber, scriptID);
            else if (op == '>')
                RecurseCmd(new[] { "ISGREATER",      result, v1, v2 }, lineNumber, scriptID);
            else if (op == '<')
                RecurseCmd(new[] { "ISLESSER",       result, v1, v2 }, lineNumber, scriptID);
            else if (op == ScriptConstants.OP_GREATEREQUAL)
                RecurseCmd(new[] { "ISGREATEREQUAL", result, v1, v2 }, lineNumber, scriptID);
            else if (op == ScriptConstants.OP_LESSEREQUAL)
                RecurseCmd(new[] { "ISLESSEREQUAL",  result, v1, v2 }, lineNumber, scriptID);
            else if (op == ScriptConstants.OP_NOTEQUAL)
                RecurseCmd(new[] { "ISNOTEQUAL",     result, v1, v2 }, lineNumber, scriptID);
            else if (op == ScriptConstants.OP_AND)
            {
                RecurseCmd(new[] { "SETVAR", result, v1 }, lineNumber, scriptID);
                RecurseCmd(new[] { "AND",    result, v2 }, lineNumber, scriptID);
            }
            else if (op == ScriptConstants.OP_OR)
            {
                RecurseCmd(new[] { "SETVAR", result, v1 }, lineNumber, scriptID);
                RecurseCmd(new[] { "OR",     result, v2 }, lineNumber, scriptID);
            }
            else if (op == ScriptConstants.OP_XOR)
            {
                RecurseCmd(new[] { "SETVAR", result, v1 }, lineNumber, scriptID);
                RecurseCmd(new[] { "XOR",    result, v2 }, lineNumber, scriptID);
            }
            else if (op == '+')
            {
                RecurseCmd(new[] { "SETVAR", result, v1 }, lineNumber, scriptID);
                RecurseCmd(new[] { "ADD",    result, v2 }, lineNumber, scriptID);
            }
            else if (op == '-')
            {
                RecurseCmd(new[] { "SETVAR",   result, v1 }, lineNumber, scriptID);
                RecurseCmd(new[] { "SUBTRACT", result, v2 }, lineNumber, scriptID);
            }
            else if (op == '*')
            {
                RecurseCmd(new[] { "SETVAR",   result, v1 }, lineNumber, scriptID);
                RecurseCmd(new[] { "MULTIPLY", result, v2 }, lineNumber, scriptID);
            }
            else if (op == '/')
            {
                RecurseCmd(new[] { "SETVAR",  result, v1 }, lineNumber, scriptID);
                RecurseCmd(new[] { "DIVIDE",  result, v2 }, lineNumber, scriptID);
            }
            else if (op == '%')
            {
                RecurseCmd(new[] { "SETVAR",  result, v1 }, lineNumber, scriptID);
                RecurseCmd(new[] { "MODULUS", result, v2 }, lineNumber, scriptID);
            }
            else
            {
                throw new Exception($"CompileTree: unknown operator code {(int)op}");
            }

            return result;
        }

        /// <summary>
        /// Compiles <paramref name="param"/> through the expression tree and returns the
        /// variable name or literal that holds its value.  If it is already a leaf
        /// (no operators), it is returned as-is.  Matches Pascal's inline use of
        /// BreakDown + CompileTree before the final CompileParameter call.
        /// </summary>
        private string CompileParamToVar(string param, int lineNumber, byte scriptID)
        {
            ExprNode root = BreakDown(param.Trim());
            if (root.Op == ScriptConstants.OP_NONE)
                return root.LeafValue!;
            return CompileTree(root, lineNumber, scriptID);
        }

        /// <summary>
        /// Full Pascal-style parameter compile: run through BreakDown/CompileTree then
        /// emit <see cref="CompileParameter"/> for the resulting variable or literal.
        /// </summary>
        private void CompileParam(string param, int lineNumber, byte scriptID)
        {
            string result = CompileParamToVar(param, lineNumber, scriptID);
            CompileParameter(result, lineNumber, scriptID, null);
        }

        private void CompileParam(string param, int lineNumber, byte scriptID, List<byte> cmdCode)
        {
            string result = CompileParamToVar(param, lineNumber, scriptID);
            CompileParameter(result, lineNumber, scriptID, cmdCode);
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
                    HandleWaitOn(paramLine, lineNumber, scriptID);
                    return true;

                default:
                    return false;
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
            string tempVar = CompileParamToVar(condition, lineNumber, scriptID);

            // BRANCH to EndLabel if condition is false
            RecurseCmd(new[] { "BRANCH", tempVar, conStruct.EndLabel }, lineNumber, scriptID);
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
            string tempVar = CompileParamToVar(condition, lineNumber, scriptID);

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
            string tempVar = CompileParamToVar(condition, lineNumber, scriptID);

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

            // Build the command in a local buffer first. Recursive expression compilation
            // emits temp opcodes directly into the global stream, and Pascal appends the
            // parent command only after those helper opcodes have been written.
            var cmdCode = new List<byte>();
            ushort lineWord = (ushort)lineNumber;
            ushort cmdWord = (ushort)(cmdID >= 0 ? cmdID : 255);
            cmdCode.Add(scriptID);
            cmdCode.AddRange(BitConverter.GetBytes(lineWord));
            cmdCode.AddRange(BitConverter.GetBytes(cmdWord));

            // Compile parameters — each token is run through the expression tree compiler
            for (int i = 1; i < paramLine.Count; i++)
            {
                string param = CanonicalizeTriggerName(cmdName, i - 1, paramLine[i]);
                CompileParam(param, lineNumber, scriptID, cmdCode);
            }

            // Null-terminate the parameter list (Pascal format)
            cmdCode.Add(0);
            AppendCode(cmdCode.ToArray());

            _cmdCount++;
        }

        private static string CanonicalizeTriggerName(string cmdName, int paramIndex, string param)
        {
            if (string.IsNullOrEmpty(param))
                return param;

            // Pascal canonicalizes a couple of legacy handwritten include trigger names
            // into library sysconsts before emission. These show up in the original Pack2
            // sources as ANSI / line, but in compiled bytecode and decompiled output as
            // LIBSILENT / LIBMULTILINE.
            bool isTriggerNameParam =
                paramIndex == 0 &&
                (cmdName == "SETTEXTLINETRIGGER" ||
                 cmdName == "SETTEXTTRIGGER" ||
                 cmdName == "SETTEXTOUTTRIGGER" ||
                 cmdName == "SETDELAYTRIGGER" ||
                 cmdName == "KILLTRIGGER");

            if (!isTriggerNameParam)
                return param;

            return param.ToUpperInvariant() switch
            {
                "LINE" => "LIBMULTILINE",
                "ANSI" => "LIBSILENT",
                _ => param
            };
        }

        private void WriteCodeByte(List<byte>? cmdCode, byte value)
        {
            if (cmdCode != null)
                cmdCode.Add(value);
            else
                AppendCode(new[] { value });
        }

        private void WriteCodeBytes(List<byte>? cmdCode, byte[] bytes)
        {
            if (cmdCode != null)
                cmdCode.AddRange(bytes);
            else
                AppendCode(bytes);
        }

        private void CompileParameter(string param, int lineNumber, byte scriptID, List<byte>? cmdCode)
        {
            byte paramType = IdentifyParam(param);

            // Write parameter type
            WriteCodeByte(cmdCode, paramType);

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
                else
                {
                    // Bare identifier (trigger name, numeric literal, etc.) — match Pascal's
                    // implicit ToUpperCase behaviour for unquoted string constants.
                    if (value.StartsWith(':'))
                        value = QualifyLabelReference(value, scriptID);
                    value = value.ToUpperInvariant();
                }

                var newParam = new CmdParam { Value = value };
                int id = _paramList.Count;
                _paramList.Add(newParam);

                // Write 32-bit parameter ID
                WriteCodeBytes(cmdCode, BitConverter.GetBytes(id));
                
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

                // Match Pascal: all include-local $vars are namespaced, including
                // explicit $$-prefixed variables such as $$FILETEST.
                if (scriptID > 0 && scriptID < _includeScriptList.Count && !varName.Contains('~'))
                {
                    varName = varName.Length > 1 && char.IsDigit(varName[1])
                        ? "$" + _includeScriptList[scriptID] + "~" + varName
                        : "$" + _includeScriptList[scriptID] + "~" + varName.Substring(1);
                }

                // Find or create variable
                int id = FindOrCreateVariable(varName);

                // Write 32-bit variable ID
                WriteCodeBytes(cmdCode, BitConverter.GetBytes(id));

                // Write array indexes (pass scriptID so variable names inside indexes are extended)
                WriteArrayIndexes(convertedParam, lineNumber, scriptID, cmdCode);
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
                if (scriptID > 0 && scriptID < _includeScriptList.Count && !varName.Contains('~') && !varName.StartsWith("%%"))
                {
                    varName = varName.Length > 1 && char.IsDigit(varName[1])
                        ? "%" + _includeScriptList[scriptID] + "~" + varName
                        : "%" + _includeScriptList[scriptID] + "~" + varName.Substring(1);
                }

                // Find or create variable
                int id = FindOrCreateVariable(varName);

                // Write 32-bit variable ID
                WriteCodeBytes(cmdCode, BitConverter.GetBytes(id));

                // Write array indexes (pass scriptID so variable names inside indexes are extended)
                WriteArrayIndexes(convertedParam, lineNumber, scriptID, cmdCode);
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
                WriteCodeBytes(cmdCode, BitConverter.GetBytes((ushort)constID));

                // Write array indexes
                WriteArrayIndexes(param, lineNumber, scriptID, cmdCode);
            }
            else if (paramType == ScriptConstants.PARAM_CHAR)
            {
                // Character code (#32, etc.)
                string charStr = param.Substring(1); // Remove #
                if (!int.TryParse(charStr, out int charCode) || charCode < 0 || charCode > 255)
                    throw new Exception($"Invalid character code: {param}");

                // Write 1 byte character code directly (NOT a parameter ID!)
                // Format: PARAM_CHAR type byte + 1 byte char code
                WriteCodeByte(cmdCode, (byte)charCode);
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

        private string ConvertDotNotation(string param)
        {
            // Pascal keeps dotted $ / % names flat in the parameter table.
            // Decompiler output relies on that for exact round-tripping.
            return param;
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

        private void WriteArrayIndexes(string param, int lineNumber, byte scriptID = 0, List<byte>? cmdCode = null)
        {
            // Parse and write array indexes from parameter
            var indexes = ExtractArrayIndexes(param);
            
            if (indexes.Count == 0)
            {
                // No indexes
                WriteCodeByte(cmdCode, 0);
                return;
            }

            // Write index count
            WriteCodeByte(cmdCode, (byte)indexes.Count);

            // Write each index: type byte + data (format matches TWX decompiler ReadIndexValues)
            foreach (string indexExpr in indexes)
            {
                // Pre-compile any expression in the index (e.g. ($emptyShipCount+1) → $$1).
                // For simple variable/constant leaves this is a no-op.
                string compiledIndex = CompileParamToVar(indexExpr, lineNumber, scriptID);

                // Each index can be a variable, constant, or sysconst
                byte indexType = IdentifyParam(compiledIndex);
                
                // Write type byte first
                WriteCodeByte(cmdCode, indexType);

                if (indexType == ScriptConstants.PARAM_VAR || indexType == ScriptConstants.PARAM_PROGVAR)
                {
                    // Variable index – extend name for include scripts
                    string indexVarName = compiledIndex;
                    int idxBracketPos = indexVarName.IndexOf('[');
                    if (idxBracketPos > 0)
                        indexVarName = indexVarName.Substring(0, idxBracketPos);

                    if (scriptID > 0 && scriptID < _includeScriptList.Count && !indexVarName.Contains('~'))
                    {
                        if (indexVarName.StartsWith("$"))
                            indexVarName = indexVarName.Length > 1 && char.IsDigit(indexVarName[1])
                                ? "$" + _includeScriptList[scriptID] + "~" + indexVarName
                                : "$" + _includeScriptList[scriptID] + "~" + indexVarName.Substring(1);
                        else if (indexVarName.StartsWith("%") && !indexVarName.StartsWith("%%"))
                            indexVarName = indexVarName.Length > 1 && char.IsDigit(indexVarName[1])
                                ? "%" + _includeScriptList[scriptID] + "~" + indexVarName
                                : "%" + _includeScriptList[scriptID] + "~" + indexVarName.Substring(1);
                    }

                    int indexId = FindOrCreateVariable(indexVarName);
                    WriteCodeBytes(cmdCode, BitConverter.GetBytes(indexId));
                    WriteArrayIndexes(compiledIndex, lineNumber, scriptID, cmdCode); // Recursive for nested arrays
                }
                else if (indexType == ScriptConstants.PARAM_SYSCONST)
                {
                    // System constant - write 16-bit ID + recursively write its indexes
                    string constName = compiledIndex;
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
                        throw new Exception($"Unknown system constant in array index: {compiledIndex}");
                    
                    WriteCodeBytes(cmdCode, BitConverter.GetBytes((ushort)indexId));
                    WriteArrayIndexes(compiledIndex, lineNumber, scriptID, cmdCode); // Recursive for nested arrays
                }
                else if  (indexType == ScriptConstants.PARAM_CONST)
                {
                    // Constant index - write 32-bit parameter ID
                    string value = compiledIndex;
                    if (value.StartsWith('"') && value.EndsWith('"'))
                        value = value.Substring(1, value.Length - 2);
                    
                    var newParam = new CmdParam { Value = value };
                    int indexId = _paramList.Count;
                    _paramList.Add(newParam);
                    WriteCodeBytes(cmdCode, BitConverter.GetBytes(indexId));
                }
                else if (indexType == ScriptConstants.PARAM_CHAR)
                {
                    // Character index - write 1 byte
                    if (compiledIndex.StartsWith('#'))
                    {
                        byte charCode = byte.Parse(compiledIndex.Substring(1));
                        WriteCodeByte(cmdCode, charCode);
                    }
                }
            }
        }

        private static void AddScriptPathCandidates(List<string> candidates, HashSet<string> seen, string baseDirectory, string filename)
        {
            if (string.IsNullOrWhiteSpace(baseDirectory))
                return;

            string basePath = Path.GetFullPath(baseDirectory);
            string target = Path.Combine(basePath, filename);
            string extension = Path.GetExtension(target);

            void AddCandidate(string path)
            {
                string fullPath = Path.GetFullPath(path);
                if (seen.Add(fullPath))
                    candidates.Add(fullPath);
            }

            if (!string.IsNullOrEmpty(extension))
            {
                AddCandidate(target);
                return;
            }

            AddCandidate(target + ".ts");
            AddCandidate(target + ".cts");
            AddCandidate(target + ".inc");
        }

        private string ResolveIncludePath(string filename)
        {
            filename = filename.Replace('\\', Path.DirectorySeparatorChar);
            string searchRoot = !string.IsNullOrWhiteSpace(_rootScriptDirectory)
                ? _rootScriptDirectory
                : Path.GetFullPath(Directory.GetCurrentDirectory());

            var candidates = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (Path.IsPathRooted(filename))
            {
                AddScriptPathCandidates(candidates, seen, Path.GetDirectoryName(filename) ?? string.Empty, Path.GetFileName(filename));
            }
            else
            {
                // Match Pascal FetchScript behavior as closely as practical:
                // start at the script root, then walk upward so scripts kept in
                // nested folders can still resolve sibling "include/" trees.
                string? probeRoot = searchRoot;
                while (!string.IsNullOrWhiteSpace(probeRoot))
                {
                    AddScriptPathCandidates(candidates, seen, probeRoot, filename);
                    AddScriptPathCandidates(candidates, seen, Path.Combine(probeRoot, "scripts"), filename);

                    string? parent = Directory.GetParent(probeRoot)?.FullName;
                    if (string.IsNullOrWhiteSpace(parent)
                        || string.Equals(parent, probeRoot, StringComparison.OrdinalIgnoreCase))
                    {
                        break;
                    }

                    probeRoot = parent;
                }

                // Final compatibility fallback: resolve relative to the current
                // include file directory for decompiled or ad-hoc layouts.
                AddScriptPathCandidates(candidates, seen, _scriptDirectory, filename);
            }

            foreach (string candidate in candidates)
            {
                if (File.Exists(candidate))
                    return candidate;
            }

            string searched = candidates.Count > 0
                ? string.Join(", ", candidates)
                : filename;
            throw new FileNotFoundException($"Include file not found: {filename} (searched: {searched})");
        }

        private void IncludeFile(string filename)
        {
            string fullPath = ResolveIncludePath(filename);

            var scriptText = LoadSourceLines(fullPath);
            
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
            
            string includeName = Path.GetFileNameWithoutExtension(actualFileName).ToUpperInvariant();
            string previousScriptDirectory = _scriptDirectory;
            string previousScriptFile = _scriptFile;
            _scriptDirectory = directory ?? previousScriptDirectory;

            try
            {
                CompileFromStrings(scriptText, includeName);
            }
            catch (Exception ex)
            {
                throw new Exception($"Error in include '{includeName}': {ex.Message}", ex);
            }
            finally
            {
                _scriptDirectory = previousScriptDirectory;
                _scriptFile = previousScriptFile;
            }
        }

        #endregion

        #region File I/O Methods

        private static List<string> SplitSourceLines(string text)
        {
            var lines = new List<string>();
            using var reader = new StringReader(text);
            string? line;
            while ((line = reader.ReadLine()) != null)
                lines.Add(line);

            return lines;
        }

        private static string LoadSourceText(string filename)
        {
            byte[] bytes = File.ReadAllBytes(filename);

            if (LegacyScriptEncryption.IsEncryptedIncludePath(filename))
                return LegacyScriptEncryption.DecryptToString(bytes);

            if (!LegacyScriptEncryption.LooksLikePlainText(bytes)
                && LegacyScriptEncryption.TryDecryptToString(bytes, out string decrypted)
                && LegacyScriptEncryption.LooksLikeScriptText(decrypted))
            {
                return decrypted;
            }

            return Encoding.Latin1.GetString(bytes);
        }

        private static List<string> LoadSourceLines(string filename)
        {
            return SplitSourceLines(LoadSourceText(filename));
        }

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
        public static (string progName, int version, int descSize, int codeSize, int headerSize) ReadScriptFileHeader(BinaryReader reader)
        {
            long startPosition = reader.BaseStream.Position;
            byte[] hdr = reader.ReadBytes(24);
            if (hdr.Length < 21)
                throw new Exception("File is too short to be a compiled TWX script");

            reader.BaseStream.Seek(startPosition, SeekOrigin.Begin);

            if (hdr[0] == 10 && Encoding.ASCII.GetString(hdr, 1, 10) == "TWX SCRIPT")
            {
                reader.BaseStream.Seek(startPosition + 24, SeekOrigin.Begin);
                return (
                    "TWX SCRIPT",
                    BitConverter.ToUInt16(hdr, 12),
                    BitConverter.ToInt32(hdr, 16),
                    BitConverter.ToInt32(hdr, 20),
                    24);
            }

            if (Encoding.ASCII.GetString(hdr, 0, 10) == "TWX SCRIPT" && hdr[10] == 0)
            {
                // Older compiled artifacts store a null-terminated header string
                // instead of the Pascal shortstring byte length.
                reader.BaseStream.Seek(startPosition + 21, SeekOrigin.Begin);
                return (
                    "TWX SCRIPT",
                    BitConverter.ToUInt16(hdr, 11),
                    BitConverter.ToInt32(hdr, 13),
                    BitConverter.ToInt32(hdr, 17),
                    21);
            }

            throw new Exception("File is not a compiled TWX script");
        }

        public void LoadFromFile(string filename)
        {
            _preparedProgram = null;
            if (_codeSize > 0)
                throw new Exception("Code already exists - cannot load from file");

            using (var fs = new FileStream(filename, FileMode.Open, FileAccess.Read))
            using (var reader = new BinaryReader(fs))
            {
                GlobalModules.DebugLog($"[DEBUG] Loading file: {filename} ({fs.Length} bytes)\n");

                var (progName, version, descSize, codeSize, headerSize) = ReadScriptFileHeader(reader);

                GlobalModules.DebugLog($"[ScriptCmp.LoadFromFile] File: {filename}\n");
                GlobalModules.DebugLog($"[ScriptCmp.LoadFromFile] File size: {fs.Length} bytes\n");
                GlobalModules.DebugLog($"[ScriptCmp.LoadFromFile] Header string: '{progName}', Version: {version}, DescSize: {descSize}, CodeSize: {codeSize}, HeaderSize: {headerSize}\n");

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
