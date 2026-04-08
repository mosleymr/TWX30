namespace MTC;

/// <summary>
/// Lightweight RGB color value used by the terminal cell grid.
/// No dependency on any UI framework.
/// </summary>
public readonly record struct TermColor(byte R, byte G, byte B)
{
    public static readonly TermColor Black     = new(  0,   0,   0);
    public static readonly TermColor LightGray = new(170, 170, 170);
}

/// <summary>
/// A single character cell in the virtual terminal screen.
/// </summary>
public struct TerminalCell
{
    public char      Char;
    public TermColor Foreground;
    public TermColor Background;
    public bool      Blink;

    public static readonly TerminalCell Default = new()
    {
        Char       = ' ',
        Foreground = AnsiColor.ToColor(7),   // light gray
        Background = AnsiColor.ToColor(0),   // black
        Blink      = false,
    };
}

/// <summary>
/// Maps the 16 standard ANSI/VT100 color indices plus 256-color palette
/// to <see cref="TermColor"/> RGB values.
/// </summary>
public static class AnsiColor
{
    // Classic 16-color CGA/ANSI palette
    private static readonly TermColor[] Palette16 =
    [
        new(  0,   0,   0),  //  0 Black
        new(170,   0,   0),  //  1 Dark Red
        new(  0, 170,   0),  //  2 Dark Green
        new(170, 170,   0),  //  3 Dark Yellow (brown)
        new(  0,   0, 170),  //  4 Dark Blue
        new(170,   0, 170),  //  5 Dark Magenta
        new(  0, 170, 170),  //  6 Dark Cyan
        new(170, 170, 170),  //  7 Light Gray
        new( 85,  85,  85),  //  8 Dark Gray
        new(255,  85,  85),  //  9 Bright Red
        new( 85, 255,  85),  // 10 Bright Green
        new(255, 255,  85),  // 11 Bright Yellow
        new( 85,  85, 255),  // 12 Bright Blue
        new(255,  85, 255),  // 13 Bright Magenta
        new( 85, 255, 255),  // 14 Bright Cyan
        new(255, 255, 255),  // 15 White
    ];

    public static TermColor ToColor(int index)
    {
        if (index >= 0 && index < 16)
            return Palette16[index];

        // 256-color cube (indices 16-231)
        if (index >= 16 && index <= 231)
        {
            int i = index - 16;
            int b = i % 6;
            int g = (i / 6) % 6;
            int r = i / 36;
            return new TermColor(
                (byte)(r == 0 ? 0 : 55 + r * 40),
                (byte)(g == 0 ? 0 : 55 + g * 40),
                (byte)(b == 0 ? 0 : 55 + b * 40));
        }

        // Grayscale ramp (indices 232-255)
        if (index >= 232 && index <= 255)
        {
            byte v = (byte)(8 + (index - 232) * 10);
            return new TermColor(v, v, v);
        }

        return Palette16[7];
    }
}

/// <summary>
/// Virtual terminal screen buffer supporting an NxM grid of colored cells,
/// cursor tracking, scrolling, and erase operations.
/// </summary>
public class TerminalBuffer
{
    public int Columns { get; private set; }
    public int Rows    { get; private set; }

    private TerminalCell[,] _cells;
    private TerminalCell[,]? _resizeBackupCells;
    private int _resizeBackupColumns;
    private int _resizeBackupRows;
    private bool _suppressResizeBackupInvalidation;

    // ── Scrollback buffer ──────────────────────────────────────────────────
    /// <summary>Maximum number of lines retained in the off-screen scrollback buffer.</summary>
    public int ScrollbackLines { get; set; } = 2000;

    // Lines ordered oldest → newest.  Capped at ScrollbackLines entries.
    private readonly List<TerminalCell[]> _scrollback = [];

    /// <summary>Number of lines currently held in the scrollback buffer.</summary>
    public int ScrollbackCount => _scrollback.Count;

    /// <summary>
    /// Returns the cells for scrollback line <paramref name="index"/> (0 = oldest).
    /// The returned array may be shorter than <see cref="Columns"/> if the terminal
    /// was wider when the line was captured; callers must bounds-check.
    /// </summary>
    public TerminalCell[] GetScrollbackLine(int index) => _scrollback[index];

    public int  CursorCol    { get; set; }
    public int  CursorRow    { get; set; }
    public bool CursorVisible { get; set; } = true;

    // Scroll region (inclusive, 0-based)
    public int ScrollTop    { get; private set; }
    public int ScrollBottom { get; private set; }

    // Current attribute for new writes
    public TermColor CurrentFg    { get; set; } = AnsiColor.ToColor(7);
    public TermColor CurrentBg    { get; set; } = AnsiColor.ToColor(0);
    public bool      CurrentBlink { get; set; } = false;

    // Dirty flag – TerminalView checks this to know when to redraw
    public bool Dirty { get; set; } = true;

    public TerminalBuffer(int columns = 80, int rows = 24)
    {
        Columns      = columns;
        Rows         = rows;
        _cells       = new TerminalCell[rows, columns];
        ScrollTop    = 0;
        ScrollBottom = rows - 1;
        Reset();
    }

    // ── Cell access ────────────────────────────────────────────────────────

    public TerminalCell this[int row, int col] => _cells[row, col];

    public void SetCell(int row, int col, char ch, TermColor fg, TermColor bg)
    {
        InvalidateResizeBackup();
        if (row < 0 || row >= Rows || col < 0 || col >= Columns) return;
        _cells[row, col] = new TerminalCell { Char = ch, Foreground = fg, Background = bg };
        Dirty = true;
    }

    /// <summary>Writes a character at the current cursor position and advances.</summary>
    public void WriteChar(char ch)
    {
        if (CursorCol >= Columns) LineFeed();   // wrap

        SetCell(CursorRow, CursorCol, ch, CurrentFg, CurrentBg);
        _cells[CursorRow, CursorCol].Blink = CurrentBlink;
        CursorCol++;
        if (CursorCol >= Columns)
        {
            CursorCol = 0;
            LineFeed();
        }
    }

    // ── Cursor movement ────────────────────────────────────────────────────

    public void SetCursor(int row, int col)
    {
        CursorRow = Math.Clamp(row, 0, Rows - 1);
        CursorCol = Math.Clamp(col, 0, Columns - 1);
    }

    public void MoveCursorRelative(int dRow, int dCol)
        => SetCursor(CursorRow + dRow, CursorCol + dCol);

    public void CarriageReturn()  => CursorCol = 0;

    public void LineFeed()
    {
        if (CursorRow >= ScrollBottom)
            ScrollUp();
        else
            CursorRow++;
    }

    public void BackSpace()
    {
        if (CursorCol > 0) CursorCol--;
    }

    public void Tab()
    {
        int next = ((CursorCol / 8) + 1) * 8;
        CursorCol = Math.Min(next, Columns - 1);
    }

    // ── Scroll operations ──────────────────────────────────────────────────

    public void SetScrollRegion(int top, int bottom)
    {
        ScrollTop    = Math.Clamp(top, 0, Rows - 1);
        ScrollBottom = Math.Clamp(bottom, 0, Rows - 1);
    }

    public void ScrollUp(int lines = 1)
    {
        InvalidateResizeBackup();
        for (int n = 0; n < lines; n++)
        {
            // Save the departing top line to the scrollback buffer (only when
            // the scroll region covers the full viewport, matching xterm behaviour).
            if (ScrollbackLines > 0 && ScrollTop == 0)
            {
                var saved = new TerminalCell[Columns];
                for (int c = 0; c < Columns; c++)
                    saved[c] = _cells[ScrollTop, c];
                _scrollback.Add(saved);
                if (_scrollback.Count > ScrollbackLines)
                    _scrollback.RemoveAt(0);
            }

            for (int r = ScrollTop; r < ScrollBottom; r++)
                for (int c = 0; c < Columns; c++)
                    _cells[r, c] = _cells[r + 1, c];
            EraseLine(ScrollBottom, 0, Columns - 1);
        }
        Dirty = true;
    }

    public void ScrollDown(int lines = 1)
    {
        InvalidateResizeBackup();
        for (int n = 0; n < lines; n++)
        {
            for (int r = ScrollBottom; r > ScrollTop; r--)
                for (int c = 0; c < Columns; c++)
                    _cells[r, c] = _cells[r - 1, c];
            EraseLine(ScrollTop, 0, Columns - 1);
        }
        Dirty = true;
    }

    // ── Erase operations ───────────────────────────────────────────────────

    public void EraseLine(int row, int fromCol, int toCol)
    {
        InvalidateResizeBackup();
        for (int c = fromCol; c <= toCol && c < Columns; c++)
            _cells[row, c] = new TerminalCell { Char = ' ', Foreground = CurrentFg, Background = CurrentBg };
        Dirty = true;
    }

    public void EraseDisplay(int fromRow = 0) => EraseDisplay(fromRow, Rows - 1);
    public void EraseDisplay(int fromRow, int toRow)
    {
        for (int r = fromRow; r <= toRow && r < Rows; r++)
            EraseLine(r, 0, Columns - 1);
    }

    // ── Insert / Delete ────────────────────────────────────────────────────

    public void InsertChars(int count)
    {
        InvalidateResizeBackup();
        for (int c = Columns - 1; c >= CursorCol + count; c--)
            _cells[CursorRow, c] = _cells[CursorRow, c - count];
        EraseLine(CursorRow, CursorCol, CursorCol + count - 1);
    }

    public void DeleteChars(int count)
    {
        InvalidateResizeBackup();
        for (int c = CursorCol; c < Columns - count; c++)
            _cells[CursorRow, c] = _cells[CursorRow, c + count];
        EraseLine(CursorRow, Columns - count, Columns - 1);
    }

    // ── Resize ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Resize the terminal grid, preserving as much existing content as fits.
    /// Scroll region is reset to the full new height.
    /// </summary>
    public void Resize(int columns, int rows)
    {
        if (columns == Columns && rows == Rows) return;
        columns = Math.Max(10, columns);
        rows    = Math.Max(3,  rows);

        bool shrinking = columns < Columns || rows < Rows;
        bool growing = columns > Columns || rows > Rows;

        if (shrinking && _resizeBackupCells == null)
            SaveResizeBackup();

        TerminalCell[,] sourceCells = _cells;
        int sourceColumns = Columns;
        int sourceRows = Rows;
        bool restoreFromBackup = growing && _resizeBackupCells != null;
        if (restoreFromBackup)
        {
            sourceCells = _resizeBackupCells!;
            sourceColumns = _resizeBackupColumns;
            sourceRows = _resizeBackupRows;
        }

        var newCells = new TerminalCell[rows, columns];
        for (int r = 0; r < rows; r++)
            for (int c = 0; c < columns; c++)
                newCells[r, c] = TerminalCell.Default;

        int copyRows = Math.Min(rows, sourceRows);
        int copyCols = Math.Min(columns, sourceColumns);
        for (int r = 0; r < copyRows; r++)
            for (int c = 0; c < copyCols; c++)
                newCells[r, c] = sourceCells[r, c];

        _suppressResizeBackupInvalidation = true;
        _cells       = newCells;
        Columns      = columns;
        Rows         = rows;
        ScrollTop    = 0;
        ScrollBottom = rows - 1;
        CursorCol    = Math.Clamp(CursorCol, 0, columns - 1);
        CursorRow    = Math.Clamp(CursorRow, 0, rows    - 1);
        _suppressResizeBackupInvalidation = false;

        if (_resizeBackupCells != null &&
            columns >= _resizeBackupColumns &&
            rows >= _resizeBackupRows)
        {
            ClearResizeBackup();
        }

        Dirty        = true;
    }

    // ── Full reset ─────────────────────────────────────────────────────────

    public void Reset()
    {
        InvalidateResizeBackup();
        for (int r = 0; r < Rows; r++)
            for (int c = 0; c < Columns; c++)
                _cells[r, c] = TerminalCell.Default;
        CursorCol    = 0;
        CursorRow    = 0;
        ScrollTop    = 0;
        ScrollBottom = Rows - 1;
        CurrentFg    = AnsiColor.ToColor(7);
        CurrentBg    = AnsiColor.ToColor(0);
        // Intentionally do NOT clear _scrollback here — a terminal reset (ESC c)
        // from the server should not destroy the session scroll history.
        Dirty        = true;
    }

    private void SaveResizeBackup()
    {
        _resizeBackupCells = CloneCells(_cells, Rows, Columns);
        _resizeBackupColumns = Columns;
        _resizeBackupRows = Rows;
    }

    private static TerminalCell[,] CloneCells(TerminalCell[,] cells, int rows, int columns)
    {
        var clone = new TerminalCell[rows, columns];
        for (int r = 0; r < rows; r++)
            for (int c = 0; c < columns; c++)
                clone[r, c] = cells[r, c];
        return clone;
    }

    private void ClearResizeBackup()
    {
        _resizeBackupCells = null;
        _resizeBackupColumns = 0;
        _resizeBackupRows = 0;
    }

    private void InvalidateResizeBackup()
    {
        if (_suppressResizeBackupInvalidation)
            return;

        ClearResizeBackup();
    }
}
