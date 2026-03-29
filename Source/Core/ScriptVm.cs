using System;
using System.Collections.Generic;
using System.Globalization;

namespace TWXProxy.Core
{
    public sealed class PreparedScriptProgram
    {
        private readonly int[] _instructionIndexByOffset;

        public PreparedScriptProgram(
            PreparedInstruction[] instructions,
            int[] instructionIndexByOffset,
            int codeLength)
        {
            Instructions = instructions;
            _instructionIndexByOffset = instructionIndexByOffset;
            CodeLength = codeLength;
        }

        public PreparedInstruction[] Instructions { get; }
        public int CodeLength { get; }

        public bool TryGetInstructionIndex(int rawOffset, out int instructionIndex)
        {
            if (rawOffset < 0 || rawOffset >= _instructionIndexByOffset.Length)
            {
                instructionIndex = -1;
                return false;
            }

            instructionIndex = _instructionIndexByOffset[rawOffset];
            return instructionIndex >= 0;
        }
    }

    public sealed class PreparedInstruction
    {
        public int RawOffset { get; init; }
        public int RawEndOffset { get; init; }
        public byte ScriptId { get; init; }
        public ushort LineNumber { get; init; }
        public ushort CommandId { get; init; }
        public bool IsLabel { get; init; }
        public ScriptCmd? Command { get; init; }
        public PreparedParam[] Params { get; init; } = Array.Empty<PreparedParam>();
        public int NextInstructionIndex { get; set; } = -1;
        public CmdParam[]? RuntimeDispatchParams { get; set; }
        public int[] DynamicParamIndexes { get; init; } = Array.Empty<int>();
        public bool DirectParamsInitialized { get; set; }
    }

    public sealed class PreparedParam
    {
        public byte ParamType { get; init; }
        public int ParamId { get; init; } = -1;
        public ushort SysConstId { get; init; }
        public byte CharCode { get; init; }
        public ScriptSysConst? SysConst { get; init; }
        public PreparedParam[] Indexes { get; init; } = Array.Empty<PreparedParam>();
        public CmdParam? CompiledParam { get; init; }
        public string LiteralValue { get; init; } = string.Empty;
        public string ProgVarName { get; init; } = string.Empty;
        public bool HasArithmeticExpression { get; init; }
        public VarParam? ArithmeticBaseVar { get; init; }
        public char ArithmeticOperator { get; init; }
        public double ArithmeticRightValue { get; init; }
        public CmdParam? RuntimeParam { get; set; }
        public bool IsDirectReference { get; init; }
    }

    internal static class PreparedScriptDecoder
    {
        public static PreparedScriptProgram Decode(ScriptCmp cmp)
        {
            byte[] code = cmp.Code;
            var instructions = new List<PreparedInstruction>();
            var instructionIndexByOffset = new int[code.Length + 1];
            Array.Fill(instructionIndexByOffset, -1);
            int codePos = 0;

            while (codePos < code.Length)
            {
                int rawOffset = codePos;

                if (codePos + 5 > code.Length)
                    throw new Exception($"Truncated instruction header at byte offset {codePos}");

                byte scriptId = code[codePos++];
                ushort lineNumber = BitConverter.ToUInt16(code, codePos);
                codePos += 2;
                ushort commandId = BitConverter.ToUInt16(code, codePos);
                codePos += 2;

                PreparedInstruction instruction;
                if (commandId == 255)
                {
                    if (codePos + 4 > code.Length)
                        throw new Exception($"Truncated label payload at byte offset {codePos}");

                    codePos += 4;
                    instruction = new PreparedInstruction
                    {
                        RawOffset = rawOffset,
                        RawEndOffset = codePos,
                        ScriptId = scriptId,
                        LineNumber = lineNumber,
                        CommandId = commandId,
                        IsLabel = true
                    };
                }
                else
                {
                    var parameters = new List<PreparedParam>();
                    while (codePos < code.Length)
                    {
                        if (code[codePos] == 0)
                        {
                            codePos++;
                            break;
                        }

                        parameters.Add(DecodeParam(code, ref codePos, cmp));
                    }

                    ScriptCmd? command = null;
                    if (cmp.ScriptRef != null && commandId < cmp.ScriptRef.CmdCount)
                        command = cmp.ScriptRef.GetCmd(commandId);

                    instruction = new PreparedInstruction
                    {
                        RawOffset = rawOffset,
                        RawEndOffset = codePos,
                        ScriptId = scriptId,
                        LineNumber = lineNumber,
                        CommandId = commandId,
                        Command = command,
                        Params = parameters.ToArray(),
                        DynamicParamIndexes = BuildDynamicParamIndexes(parameters)
                    };
                }

                instructionIndexByOffset[rawOffset] = instructions.Count;
                instructions.Add(instruction);
            }

            instructionIndexByOffset[code.Length] = instructions.Count;

            var preparedInstructions = instructions.ToArray();
            for (int i = 0; i < preparedInstructions.Length; i++)
                preparedInstructions[i].NextInstructionIndex = i + 1;

            return new PreparedScriptProgram(preparedInstructions, instructionIndexByOffset, code.Length);
        }

        private static PreparedParam DecodeParam(byte[] code, ref int codePos, ScriptCmp cmp)
        {
            if (codePos >= code.Length)
                throw new Exception("Unexpected end of bytecode while decoding parameter");

            byte paramType = code[codePos++];
            return paramType switch
            {
                ScriptConstants.PARAM_CONST => DecodeConstParam(code, ref codePos, cmp, paramType),
                ScriptConstants.PARAM_VAR => DecodeVarParam(code, ref codePos, cmp, paramType),
                ScriptConstants.PARAM_PROGVAR => DecodeProgVarParam(code, ref codePos, cmp, paramType),
                ScriptConstants.PARAM_SYSCONST => new PreparedParam
                {
                    ParamType = paramType,
                    SysConstId = ReadUInt16(code, ref codePos),
                    SysConst = TryGetSysConst(cmp, ReadUInt16ValueFromCurrentParam(code, codePos - 2)),
                    Indexes = DecodeIndexes(code, ref codePos, cmp)
                },
                ScriptConstants.PARAM_CHAR => new PreparedParam
                {
                    ParamType = paramType,
                    CharCode = ReadByte(code, ref codePos),
                    LiteralValue = ((char)code[codePos - 1]).ToString()
                },
                _ => throw new Exception($"Unknown parameter type {paramType} at byte offset {codePos - 1}")
            };
        }

        private static PreparedParam DecodeConstParam(byte[] code, ref int codePos, ScriptCmp cmp, byte paramType)
        {
            int paramId = ReadInt32(code, ref codePos);
            CmdParam? compiledParam = TryGetParam(cmp, paramId);
            return new PreparedParam
            {
                ParamType = paramType,
                ParamId = paramId,
                CompiledParam = compiledParam,
                LiteralValue = compiledParam?.Value ?? string.Empty,
                IsDirectReference = false
            };
        }

        private static PreparedParam DecodeVarParam(byte[] code, ref int codePos, ScriptCmp cmp, byte paramType)
        {
            int paramId = ReadInt32(code, ref codePos);
            CmdParam? compiledParam = TryGetParam(cmp, paramId);
            VarParam? varParam = compiledParam as VarParam;
            PreparedParam[] indexes = DecodeIndexes(code, ref codePos, cmp);

            bool hasArithmeticExpression = false;
            VarParam? arithmeticBaseVar = null;
            char arithmeticOperator = '\0';
            double arithmeticRightValue = 0;

            if (varParam != null && TryDecodeArithmetic(varParam.Name, cmp, out arithmeticBaseVar, out arithmeticOperator, out arithmeticRightValue))
                hasArithmeticExpression = true;

            return new PreparedParam
            {
                ParamType = paramType,
                ParamId = paramId,
                CompiledParam = compiledParam,
                Indexes = indexes,
                HasArithmeticExpression = hasArithmeticExpression,
                ArithmeticBaseVar = arithmeticBaseVar,
                ArithmeticOperator = arithmeticOperator,
                ArithmeticRightValue = arithmeticRightValue,
                IsDirectReference = compiledParam != null && indexes.Length == 0 && !hasArithmeticExpression
            };
        }

        private static PreparedParam DecodeProgVarParam(byte[] code, ref int codePos, ScriptCmp cmp, byte paramType)
        {
            int paramId = ReadInt32(code, ref codePos);
            CmdParam? compiledParam = TryGetParam(cmp, paramId);
            string progVarName = compiledParam is VarParam vp
                ? vp.Name
                : compiledParam?.Value ?? string.Empty;

            PreparedParam[] indexes = DecodeIndexes(code, ref codePos, cmp);

            return new PreparedParam
            {
                ParamType = paramType,
                ParamId = paramId,
                CompiledParam = compiledParam,
                ProgVarName = progVarName,
                Indexes = indexes,
                IsDirectReference = !string.IsNullOrEmpty(progVarName) && indexes.Length == 0
            };
        }

        private static PreparedParam[] DecodeIndexes(byte[] code, ref int codePos, ScriptCmp cmp)
        {
            if (codePos >= code.Length)
                return Array.Empty<PreparedParam>();

            byte indexCount = code[codePos++];
            if (indexCount == 0)
                return Array.Empty<PreparedParam>();

            var indexes = new PreparedParam[indexCount];
            for (int i = 0; i < indexCount; i++)
                indexes[i] = DecodeParam(code, ref codePos, cmp);

            return indexes;
        }

        private static CmdParam? TryGetParam(ScriptCmp cmp, int paramId)
        {
            return paramId >= 0 && paramId < cmp.ParamList.Count
                ? cmp.ParamList[paramId]
                : null;
        }

        private static int[] BuildDynamicParamIndexes(List<PreparedParam> parameters)
        {
            if (parameters.Count == 0)
                return Array.Empty<int>();

            var dynamicIndexes = new List<int>(parameters.Count);
            for (int i = 0; i < parameters.Count; i++)
            {
                if (!parameters[i].IsDirectReference)
                    dynamicIndexes.Add(i);
            }

            return dynamicIndexes.Count == 0
                ? Array.Empty<int>()
                : dynamicIndexes.ToArray();
        }

        private static bool TryDecodeArithmetic(
            string varName,
            ScriptCmp cmp,
            out VarParam? baseVar,
            out char arithmeticOperator,
            out double arithmeticRightValue)
        {
            baseVar = null;
            arithmeticOperator = '\0';
            arithmeticRightValue = 0;

            int opIdx = FindArithmeticOperator(varName);
            if (opIdx < 0)
                return false;

            string baseVarName = varName.Substring(0, opIdx);
            arithmeticOperator = varName[opIdx];
            string rhsStr = varName.Substring(opIdx + 1);

            if (!double.TryParse(rhsStr, NumberStyles.Float, CultureInfo.InvariantCulture, out arithmeticRightValue))
                return false;

            foreach (CmdParam param in cmp.ParamList)
            {
                if (param is VarParam vp &&
                    string.Equals(vp.Name, baseVarName, StringComparison.OrdinalIgnoreCase))
                {
                    baseVar = vp;
                    return true;
                }
            }

            return false;
        }

        private static int FindArithmeticOperator(string varName)
        {
            if (string.IsNullOrEmpty(varName) || varName[0] != '$')
                return -1;

            for (int i = 2; i < varName.Length; i++)
            {
                char c = varName[i];
                if (c == '+' || c == '-' || c == '*' || c == '/')
                    return i;

                if (!(char.IsLetterOrDigit(c) || c == '_'))
                    return -1;
            }

            return -1;
        }

        private static ScriptSysConst? TryGetSysConst(ScriptCmp cmp, ushort sysConstId)
        {
            if (cmp.ScriptRef == null)
                return null;

            return sysConstId < cmp.ScriptRef.SysConstCount
                ? cmp.ScriptRef.GetSysConst(sysConstId)
                : null;
        }

        private static int ReadInt32(byte[] code, ref int codePos)
        {
            if (codePos + 4 > code.Length)
                throw new Exception($"Unexpected end of bytecode while reading Int32 at offset {codePos}");

            int value = BitConverter.ToInt32(code, codePos);
            codePos += 4;
            return value;
        }

        private static ushort ReadUInt16(byte[] code, ref int codePos)
        {
            if (codePos + 2 > code.Length)
                throw new Exception($"Unexpected end of bytecode while reading UInt16 at offset {codePos}");

            ushort value = BitConverter.ToUInt16(code, codePos);
            codePos += 2;
            return value;
        }

        private static ushort ReadUInt16ValueFromCurrentParam(byte[] code, int codePos)
        {
            if (codePos + 2 > code.Length)
                throw new Exception($"Unexpected end of bytecode while reading UInt16 at offset {codePos}");

            return BitConverter.ToUInt16(code, codePos);
        }

        private static byte ReadByte(byte[] code, ref int codePos)
        {
            if (codePos >= code.Length)
                throw new Exception($"Unexpected end of bytecode while reading byte at offset {codePos}");

            return code[codePos++];
        }
    }
}
