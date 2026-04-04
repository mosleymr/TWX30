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

// ANSI Code Constants

namespace TWXProxy.Core
{
    public static class AnsiCodes
    {
        public const string ANSI_0 = "\x1B[0m\x1B[30m";
        public const string ANSI_1 = "\x1B[0m\x1B[34m";
        public const string ANSI_2 = "\x1B[0m\x1B[32m";
        public const string ANSI_3 = "\x1B[0m\x1B[36m";
        public const string ANSI_4 = "\x1B[0m\x1B[31m";
        public const string ANSI_5 = "\x1B[0m\x1B[35m";
        public const string ANSI_6 = "\x1B[0m\x1B[33m";
        public const string ANSI_7 = "\x1B[0m\x1B[37m";
        public const string ANSI_8 = "\x1B[30;1m";
        public const string ANSI_9 = "\x1B[34;1m";
        public const string ANSI_10 = "\x1B[32;1m";
        public const string ANSI_11 = "\x1B[36;1m";
        public const string ANSI_12 = "\x1B[31;1m";
        public const string ANSI_13 = "\x1B[35;1m";
        public const string ANSI_14 = "\x1B[33;1m";
        public const string ANSI_15 = "\x1B[37;1m";
        public const string ANSI_CLEARLINE = "\x1B[K";
        public const string ANSI_MOVEUP = "\x1B[1A";

        private static readonly string[] AnsiCodeArray = new[]
        {
            ANSI_0, ANSI_1, ANSI_2, ANSI_3, ANSI_4, ANSI_5, ANSI_6, ANSI_7,
            ANSI_8, ANSI_9, ANSI_10, ANSI_11, ANSI_12, ANSI_13, ANSI_14, ANSI_15
        };

        public static string GetAnsiCode(int index)
        {
            if (index >= 0 && index < AnsiCodeArray.Length)
                return AnsiCodeArray[index];
            return ANSI_7; // Default
        }

        public static string StripANSI(string text)
        {
            // Remove all ANSI/VT100 CSI escape sequences: ESC [ <params> <letter>
            // This covers SGR colour codes (\x1B[1;33m), cursor movement (\x1B[H,
            // \x1B[2J, \x1B[1;1H), erase (\x1B[K), etc.  TW2002 uses many of these
            // and the old regex only stripped codes ending in 'm', leaving cursor/
            // clear codes intact and mangling text like "Long Range Scan".
            return System.Text.RegularExpressions.Regex.Replace(text, @"\x1B\[[0-9;]*[A-Za-z]", string.Empty);
        }

        public static string NormalizeTerminalText(string text)
        {
            if (string.IsNullOrEmpty(text))
                return string.Empty;

            var builder = new System.Text.StringBuilder(text.Length);
            foreach (char ch in text)
            {
                if (ch == '\b' || ch == (char)127)
                {
                    if (builder.Length > 0)
                        builder.Length--;
                    continue;
                }

                if (ch == '\r' || ch == '\n')
                    continue;

                builder.Append(ch);
            }

            return builder.ToString();
        }

        public static string NormalizeAnsiTerminalText(string text)
        {
            if (string.IsNullOrEmpty(text))
                return string.Empty;

            var builder = new System.Text.StringBuilder(text.Length);
            var visibleCharStarts = new System.Collections.Generic.Stack<int>();

            for (int i = 0; i < text.Length; i++)
            {
                char ch = text[i];

                if (ch == '\x1B')
                {
                    int seqStart = i;
                    builder.Append(ch);

                    if (i + 1 < text.Length)
                    {
                        i++;
                        builder.Append(text[i]);

                        if (text[i] == '[')
                        {
                            while (i + 1 < text.Length)
                            {
                                i++;
                                builder.Append(text[i]);
                                if (char.IsLetter(text[i]))
                                    break;
                            }
                        }
                    }

                    continue;
                }

                if (ch == '\b' || ch == (char)127)
                {
                    if (visibleCharStarts.Count > 0)
                    {
                        int removeAt = visibleCharStarts.Pop();
                        builder.Length = removeAt;
                    }
                    continue;
                }

                if (ch == '\r' || ch == '\n')
                    continue;

                visibleCharStarts.Push(builder.Length);
                builder.Append(ch);
            }

            return builder.ToString();
        }

        public static string PrepareScriptAnsiText(string text)
        {
            if (string.IsNullOrEmpty(text))
                return string.Empty;

            var builder = new System.Text.StringBuilder(text.Length);
            foreach (char ch in text)
            {
                if (ch == '\0' || ch == '\n')
                    continue;

                builder.Append(ch);
            }

            return builder.ToString();
        }

        public static string PrepareScriptText(string text)
        {
            // Pascal exposes inbound script text largely raw: ANSI is stripped, but prompt
            // control bytes such as #145#8 remain available to Text/TextLine triggers.
            // Local typed input still goes through its own normalization path.
            return StripANSI(PrepareScriptAnsiText(text)).TrimEnd('\r');
        }
    }
}
