using System.Text;

namespace MTC;

/// <summary>
/// VT100/ANSI escape-sequence parser.  Feed raw bytes (after telnet has
/// stripped IAC sequences) in via <see cref="Feed(byte[],int)"/> and the
/// parser writes decoded characters + attributes directly into the
/// <see cref="TerminalBuffer"/>.
/// </summary>
public class AnsiParser
{
    private static readonly char[] Cp437Glyphs = BuildCp437GlyphTable();

    // ── Parser state machine ───────────────────────────────────────────────
    private enum State
    {
        Ground,         // normal text
        Escape,         // received ESC
        CsiEntry,       // received ESC [
        CsiParam,       // accumulating CSI parameter bytes
        OscString,      // inside OSC (ESC ]) – ignored but consumed
        DcsEntry,       // inside DCS (ESC P) – ignored
        SosEntry,       // inside SOS/PM/APC (ESC X / ^ / _) – ignored
    }

    private readonly TerminalBuffer _buf;
    private State    _state      = State.Ground;
    private string   _csiParam   = "";
    private char     _csiIntermediate = '\0';
    private byte?    _pendingUtf8Latin1Lead;

    // Saved cursor
    private int _savedRow, _savedCol;

    // Attribute state
    private bool _bold;
    private int  _fgIndex = 7;
    private int  _bgIndex = 0;
    private bool _fgIs256;
    // 256/true-color accumulation state
    private bool _nextIsFgColor, _nextIsBgColor;
    private int  _colorStage; // 0=waiting for type, 1=waiting for index

    public AnsiParser(TerminalBuffer buffer)
    {
        _buf = buffer;
        ApplyAttributes();
    }

    // ── Public feed API ────────────────────────────────────────────────────

    public void Feed(byte[] data, int length)
    {
        for (int i = 0; i < length; i++)
        {
            byte b = data[i];

            if (_pendingUtf8Latin1Lead is byte lead)
            {
                _pendingUtf8Latin1Lead = null;
                if (TryDecodeUtf8Latin1Byte(lead, b, out byte decoded))
                {
                    ProcessByte(decoded);
                    continue;
                }

                ProcessByte(lead);
            }

            if (IsUtf8Latin1Lead(b))
            {
                if (i + 1 >= length)
                {
                    _pendingUtf8Latin1Lead = b;
                    continue;
                }

                if (TryDecodeUtf8Latin1Byte(b, data[i + 1], out byte decoded))
                {
                    ProcessByte(decoded);
                    i++;
                    continue;
                }
            }

            ProcessByte(b);
        }
    }

    public void Feed(string text)
    {
        FlushPendingUtf8Latin1Lead();
        foreach (char c in text)
            ProcessByte((byte)c);
    }

    private void FlushPendingUtf8Latin1Lead()
    {
        if (_pendingUtf8Latin1Lead is not byte lead)
            return;

        _pendingUtf8Latin1Lead = null;
        ProcessByte(lead);
    }

    private static bool IsUtf8Latin1Lead(byte b)
        => b is 0xC2 or 0xC3;

    private static bool TryDecodeUtf8Latin1Byte(byte lead, byte trail, out byte value)
    {
        value = 0;
        if (!IsUtf8Latin1Lead(lead) || trail < 0x80 || trail > 0xBF)
            return false;

        int codePoint = ((lead & 0x1F) << 6) | (trail & 0x3F);
        if (codePoint < 0x80 || codePoint > 0xFF)
            return false;

        value = (byte)codePoint;
        return true;
    }

    // ── Main dispatch ──────────────────────────────────────────────────────

    private void ProcessByte(byte b)
    {
        char c = (char)b;

        switch (_state)
        {
            case State.Ground:
                if (b == 0x1B) { _state = State.Escape; return; }
                HandleControlChar(b);
                break;

            case State.Escape:
                _state = State.Ground;
                switch (b)
                {
                    case (byte)'[': _state = State.CsiEntry; _csiParam = ""; _csiIntermediate = '\0'; break;
                    case (byte)']': _state = State.OscString; break;
                    case (byte)'P': _state = State.DcsEntry;  break;
                    case (byte)'X':
                    case (byte)'^':
                    case (byte)'_': _state = State.SosEntry; break;
                    case (byte)'7': SaveCursor();    break;
                    case (byte)'8': RestoreCursor(); break;
                    case (byte)'c': _buf.Reset();    ApplyAttributes(); break;
                    case (byte)'M': _buf.ScrollDown(); break;  // reverse index
                    default: break; // ignore unrecognised
                }
                break;

            case State.CsiEntry:
            case State.CsiParam:
                _state = State.CsiParam;
                if (b >= 0x30 && b <= 0x3F)          // parameter / subparam bytes
                {
                    _csiParam += c;
                }
                else if (b >= 0x20 && b <= 0x2F)     // intermediate bytes
                {
                    _csiIntermediate = c;
                }
                else if (b >= 0x40 && b <= 0x7E)     // final byte → dispatch
                {
                    DispatchCsi(_csiParam, _csiIntermediate, c);
                    _state = State.Ground;
                }
                break;

            case State.OscString:
                if (b == 0x07 || b == 0x1B) _state = State.Ground;  // BEL or ESC terminates
                break;

            case State.DcsEntry:
            case State.SosEntry:
                if (b == 0x1B) _state = State.Escape;  // ESC \ terminates (ST)
                break;
        }
    }

    private void HandleControlChar(byte b)
    {
        switch (b)
        {
            case 0x00: break;          // NUL – ignore
            case 0x07: break;          // BEL – ignore
            case 0x08: _buf.BackSpace(); break;
            case 0x09: _buf.Tab();       break;
            case 0x0A:                 // LF
            case 0x0B:                 // VT
            case 0x0C: _buf.LineFeed(); break;  // FF
            case 0x0D: _buf.CarriageReturn(); break;
            default:
                if (b >= 0x20) _buf.WriteChar(Cp437Glyphs[b]);  // printable DOS/ANSI glyph
                break;
        }
    }

    private static char[] BuildCp437GlyphTable()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var encoding = Encoding.GetEncoding(437);
        var table = new char[256];

        for (int i = 0; i < table.Length; i++)
            table[i] = encoding.GetChars([(byte)i])[0];

        return table;
    }

    // ── CSI dispatch ───────────────────────────────────────────────────────

    private void DispatchCsi(string param, char intermediate, char finalChar)
    {
        int[] ps = ParseParams(param);

        switch (finalChar)
        {
            // Cursor movement
            case 'A': _buf.MoveCursorRelative(-(P(ps, 0, 1)), 0); break;
            case 'B': _buf.MoveCursorRelative( P(ps, 0, 1),  0); break;
            case 'C': _buf.MoveCursorRelative(0,  P(ps, 0, 1)); break;
            case 'D': _buf.MoveCursorRelative(0, -P(ps, 0, 1)); break;
            case 'E': _buf.SetCursor(_buf.CursorRow + P(ps, 0, 1), 0); break;
            case 'F': _buf.SetCursor(_buf.CursorRow - P(ps, 0, 1), 0); break;
            case 'G': _buf.SetCursor(_buf.CursorRow, P(ps, 0, 1) - 1); break;

            // Cursor position  CSI row ; col H  (1-based)
            case 'H':
            case 'f':
                _buf.SetCursor(P(ps, 0, 1) - 1, P(ps, 1, 1) - 1);
                break;

            // Erase in display
            case 'J':
                switch (P(ps, 0, 0))
                {
                    case 0: _buf.EraseDisplay(_buf.CursorRow + 1); _buf.EraseLine(_buf.CursorRow, _buf.CursorCol, _buf.Columns - 1); break;
                    case 1: _buf.EraseDisplay(0, _buf.CursorRow - 1); _buf.EraseLine(_buf.CursorRow, 0, _buf.CursorCol); break;
                    case 2:
                    case 3: _buf.EraseDisplay(); break;
                }
                break;

            // Erase in line
            case 'K':
                switch (P(ps, 0, 0))
                {
                    case 0: _buf.EraseLine(_buf.CursorRow, _buf.CursorCol, _buf.Columns - 1); break;
                    case 1: _buf.EraseLine(_buf.CursorRow, 0, _buf.CursorCol); break;
                    case 2: _buf.EraseLine(_buf.CursorRow, 0, _buf.Columns - 1); break;
                }
                break;

            // Scroll up / down
            case 'S': _buf.ScrollUp(P(ps, 0, 1));   break;
            case 'T': _buf.ScrollDown(P(ps, 0, 1));  break;

            // Insert / delete characters
            case '@': _buf.InsertChars(P(ps, 0, 1)); break;
            case 'P': _buf.DeleteChars(P(ps, 0, 1)); break;

            // Insert / delete lines
            case 'L':
                for (int i = 0; i < P(ps, 0, 1); i++) _buf.ScrollDown();
                break;
            case 'M':
                for (int i = 0; i < P(ps, 0, 1); i++) _buf.ScrollUp();
                break;

            // Scroll region   CSI top ; bottom r  (1-based)
            case 'r':
                _buf.SetScrollRegion(P(ps, 0, 1) - 1, P(ps, 1, _buf.Rows) - 1);
                _buf.SetCursor(0, 0);
                break;

            // Save / restore cursor (ANSI extension)
            case 's': SaveCursor();    break;
            case 'u': RestoreCursor(); break;

            // SGR – Select Graphic Rendition
            case 'm': ApplySgr(ps); break;

            // Show/hide cursor (private mode with ?)
            case 'h':
            case 'l':
                if (param.StartsWith('?') && P(ps, 0, 0) == 25)
                    _buf.CursorVisible = (finalChar == 'h');
                break;

            // Device Attributes / reports – respond with nothing (client-side only)
            default: break;
        }
    }

    // ── SGR ────────────────────────────────────────────────────────────────

    private void ApplySgr(int[] ps)
    {
        if (ps.Length == 0) { ResetAttributes(); return; }

        int i = 0;
        while (i < ps.Length)
        {
            int p = ps[i];

            // 256-color / truecolor continuation
            if (_nextIsFgColor || _nextIsBgColor)
            {
                if (_colorStage == 0)
                {
                    // p should be 5 (256-color) or 2 (truecolor)
                    if (p == 5) { _colorStage = 1; i++; continue; }
                    // truecolor (38;2;r;g;b) – consume next 3
                    if (p == 2 && i + 3 < ps.Length)
                    {
                        var tc = new TermColor((byte)ps[i + 1], (byte)ps[i + 2], (byte)ps[i + 3]);
                        if (_nextIsFgColor) _buf.CurrentFg = tc;
                        else               _buf.CurrentBg = tc;
                        i += 4;
                        _nextIsFgColor = _nextIsBgColor = false; _colorStage = 0;
                        continue;
                    }
                    _nextIsFgColor = _nextIsBgColor = false; _colorStage = 0;
                }
                else if (_colorStage == 1)
                {
                    var c256 = AnsiColor.ToColor(p);
                    if (_nextIsFgColor) _buf.CurrentFg = c256;
                    else               _buf.CurrentBg = c256;
                    _nextIsFgColor = _nextIsBgColor = false; _colorStage = 0;
                    i++; continue;
                }
            }

            switch (p)
            {
                case 0:  ResetAttributes(); break;
                case 1:  _bold = true;  AdjustBold(); break;
                case 2:  _bold = false; AdjustBold(); break;  // dim
                case 22: _bold = false; AdjustBold(); break;
                case 5:  // slow blink
                case 6:  // rapid blink (treat same as slow)
                    _buf.CurrentBlink = true; break;
                case 25: _buf.CurrentBlink = false; break;
                // 3/4/7/8 – italic/underline/reverse/conceal – mostly ignore for TW
                case 7:  // reverse video
                    (_buf.CurrentFg, _buf.CurrentBg) = (_buf.CurrentBg, _buf.CurrentFg);
                    break;
                case 27: ApplyAttributes(); break;   // reverse off

                // Standard fg 30-37
                case int n when n >= 30 && n <= 37:
                    _fgIndex = n - 30 + (_bold ? 8 : 0);
                    _fgIs256 = false;
                    _buf.CurrentFg = AnsiColor.ToColor(_fgIndex);
                    break;

                // 256-color fg
                case 38:
                    _nextIsFgColor = true; _colorStage = 0;
                    break;

                // Default fg
                case 39:
                    _fgIndex = 7; _fgIs256 = false;
                    _buf.CurrentFg = AnsiColor.ToColor(7);
                    break;

                // Standard bg 40-47
                case int n when n >= 40 && n <= 47:
                    _bgIndex = n - 40;
                    _buf.CurrentBg = AnsiColor.ToColor(_bgIndex);
                    break;

                // 256-color bg
                case 48:
                    _nextIsBgColor = true; _colorStage = 0;
                    break;

                // Default bg
                case 49:
                    _bgIndex = 0;
                    _buf.CurrentBg = AnsiColor.ToColor(0);
                    break;

                // Bright fg 90-97
                case int n when n >= 90 && n <= 97:
                    _fgIndex = n - 90 + 8;
                    _fgIs256 = false;
                    _buf.CurrentFg = AnsiColor.ToColor(_fgIndex);
                    break;

                // Bright bg 100-107
                case int n when n >= 100 && n <= 107:
                    _bgIndex = n - 100 + 8;
                    _buf.CurrentBg = AnsiColor.ToColor(_bgIndex);
                    break;
            }
            i++;
        }
    }

    private void ResetAttributes()
    {
        _bold = false;
        _fgIndex = 7; _bgIndex = 0;
        _fgIs256 = false;
        _nextIsFgColor = _nextIsBgColor = false;
        _buf.CurrentBlink = false;
        ApplyAttributes();
    }

    private void ApplyAttributes()
    {
        _buf.CurrentFg = AnsiColor.ToColor(_bold ? Math.Min(_fgIndex | 8, 15) : _fgIndex);
        _buf.CurrentBg = AnsiColor.ToColor(_bgIndex);
    }

    private void AdjustBold()
    {
        if (!_fgIs256)
            _buf.CurrentFg = AnsiColor.ToColor(_bold ? Math.Min(_fgIndex | 8, 15) : (_fgIndex & 7));
    }

    // ── Cursor save/restore ────────────────────────────────────────────────

    private void SaveCursor()
    {
        _savedRow = _buf.CursorRow;
        _savedCol = _buf.CursorCol;
    }

    private void RestoreCursor() => _buf.SetCursor(_savedRow, _savedCol);

    // ── Helpers ────────────────────────────────────────────────────────────

    private static int[] ParseParams(string s)
    {
        if (string.IsNullOrEmpty(s)) return [0];
        var parts = s.TrimStart('?').Split(';');
        var result = new int[parts.Length];
        for (int i = 0; i < parts.Length; i++)
            int.TryParse(parts[i], out result[i]);
        return result;
    }

    /// <summary>Returns parameter at <paramref name="idx"/> or <paramref name="def"/> if missing / zero.</summary>
    private static int P(int[] ps, int idx, int def)
        => (idx < ps.Length && ps[idx] != 0) ? ps[idx] : def;
}
