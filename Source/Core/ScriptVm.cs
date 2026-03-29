using System;
using System.Collections.Generic;

namespace TWXProxy.Core
{
    public sealed class PreparedScriptProgram
    {
        private readonly Dictionary<int, int> _instructionIndexByOffset;

        public PreparedScriptProgram(
            PreparedInstruction[] instructions,
            Dictionary<int, int> instructionIndexByOffset,
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
            return _instructionIndexByOffset.TryGetValue(rawOffset, out instructionIndex);
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
    }

    public sealed class PreparedParam
    {
        public byte ParamType { get; init; }
        public int ParamId { get; init; } = -1;
        public ushort SysConstId { get; init; }
        public byte CharCode { get; init; }
        public ScriptSysConst? SysConst { get; init; }
        public PreparedParam[] Indexes { get; init; } = Array.Empty<PreparedParam>();
    }

    internal static class PreparedScriptDecoder
    {
        public static PreparedScriptProgram Decode(ScriptCmp cmp)
        {
            byte[] code = cmp.Code;
            var instructions = new List<PreparedInstruction>();
            var instructionIndexByOffset = new Dictionary<int, int>();
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
                        Params = parameters.ToArray()
                    };
                }

                instructionIndexByOffset[rawOffset] = instructions.Count;
                instructions.Add(instruction);
            }

            return new PreparedScriptProgram(instructions.ToArray(), instructionIndexByOffset, code.Length);
        }

        private static PreparedParam DecodeParam(byte[] code, ref int codePos, ScriptCmp cmp)
        {
            if (codePos >= code.Length)
                throw new Exception("Unexpected end of bytecode while decoding parameter");

            byte paramType = code[codePos++];
            return paramType switch
            {
                ScriptConstants.PARAM_CONST => new PreparedParam
                {
                    ParamType = paramType,
                    ParamId = ReadInt32(code, ref codePos)
                },
                ScriptConstants.PARAM_VAR => new PreparedParam
                {
                    ParamType = paramType,
                    ParamId = ReadInt32(code, ref codePos),
                    Indexes = DecodeIndexes(code, ref codePos, cmp)
                },
                ScriptConstants.PARAM_PROGVAR => new PreparedParam
                {
                    ParamType = paramType,
                    ParamId = ReadInt32(code, ref codePos),
                    Indexes = DecodeIndexes(code, ref codePos, cmp)
                },
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
                    CharCode = ReadByte(code, ref codePos)
                },
                _ => throw new Exception($"Unknown parameter type {paramType} at byte offset {codePos - 1}")
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
