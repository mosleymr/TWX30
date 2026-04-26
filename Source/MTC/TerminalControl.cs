using System.Globalization;
using System.Linq;
using System.Text;
using System.Diagnostics;
using System.Threading;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.Input.Platform;

namespace MTC;

/// <summary>
/// Custom Avalonia <see cref="Control"/> that renders the contents of a
/// <see cref="TerminalBuffer"/> cell-by-cell with full ANSI color support.
/// Handles keyboard input and forwards raw bytes to the telnet connection.
/// </summary>
public class TerminalControl : Control
{
    private sealed class CachedRenderRun
    {
        public required double X { get; init; }
        public required double Width { get; init; }
        public required TermColor Background { get; init; }
        public required bool Blink { get; init; }
        public FormattedText? Text { get; init; }
    }

    private sealed class CachedRenderRow
    {
        public required CachedRenderRun[] Runs { get; init; }
    }

    private static readonly (string Name, byte[] Bytes)[] MacroHotkeyDefinitions =
    [
        ("F1", [0x1B, (byte)'O', (byte)'P']),
        ("F2", [0x1B, (byte)'O', (byte)'Q']),
        ("F3", [0x1B, (byte)'O', (byte)'R']),
        ("F4", [0x1B, (byte)'O', (byte)'S']),
        ("F5", [0x1B, (byte)'[', (byte)'1', (byte)'5', (byte)'~']),
        ("F6", [0x1B, (byte)'[', (byte)'1', (byte)'7', (byte)'~']),
        ("F7", [0x1B, (byte)'[', (byte)'1', (byte)'8', (byte)'~']),
        ("F8", [0x1B, (byte)'[', (byte)'1', (byte)'9', (byte)'~']),
        ("F9", [0x1B, (byte)'[', (byte)'2', (byte)'0', (byte)'~']),
        ("F10", [0x1B, (byte)'[', (byte)'2', (byte)'1', (byte)'~']),
        ("F11", [0x1B, (byte)'[', (byte)'2', (byte)'3', (byte)'~']),
    ];

    private readonly TerminalBuffer _buffer;
    private readonly DispatcherTimer _cursorTimer;
    private readonly DispatcherTimer _windowMoveTimer;
    private bool _cursorOn = true;
    private bool _dirtySubscriptionActive;
    private bool _hostWindowMoving;
    private int _redrawQueued;
    private CachedRenderRow[] _visibleRowCache = [];
    private long _visibleRowCacheDirtyVersion = -1;
    private int _visibleRowCacheTopDocumentRow = int.MinValue;
    private int _visibleRowCacheColumns = -1;
    private int _visibleRowCacheRows = -1;
    private double _visibleRowCacheFontSize = -1;
    private string _visibleRowCacheFontKey = string.Empty;

    // Monospace font metrics – measured once at construction time (or on font change)
    private double _charWidth;
    private double _lineHeight;

    private FontFamily _fontFamily =
        new("Cascadia Code, Menlo, Consolas, Courier New, monospace");

    public const double DefaultFontSize = 14.0;
    private double _fontSize = DefaultFontSize;
    private Typeface _typeFace;

    // Brush cache – one SolidColorBrush per unique TermColor
    private readonly Dictionary<TermColor, SolidColorBrush> _brushCache = [];

    // ── Selection state ────────────────────────────────────────────────────
    // All coordinates are absolute document positions: scrollback rows first,
    // then the currently visible live buffer rows.
    private (int Row, int Col) _selAnchor;
    private (int Row, int Col) _selCurrent;
    private bool _hasSelection;
    /// <summary>Lines scrolled above the live view; 0 = showing live bottom.</summary>
    private int    _scrollOffset;
    /// <summary>Fractional scroll accumulator for smooth-scroll trackpads.</summary>
    private double _scrollAccumulator;
    private long   _scrollGenerationSeen;

    private static readonly SolidColorBrush SelectionBrush =
        new(Color.FromArgb(100, 51, 153, 255));  // translucent blue

    /// <summary>
    /// Set by the owner to forward key bytes to the server.
    /// </summary>
    public Action<byte[]>? SendInput { get; set; }

    public static IReadOnlyList<string> SupportedMacroHotkeys { get; } =
        MacroHotkeyDefinitions.Select(definition => definition.Name).ToArray();

    /// <summary>
    /// When false, all keyboard input is silently swallowed (no bytes sent, no messages printed).
    /// Set to true when a connection is established, false when disconnected.
    /// </summary>
    public bool IsConnected { get; set; }

    public TerminalControl(TerminalBuffer buffer)
    {
        _buffer   = buffer;
        Focusable = true;
        _typeFace = new Typeface(_fontFamily);
        _scrollGenerationSeen = _buffer.ScrollbackGeneration;

        MeasureFont();

        // Size is driven by the parent container; ArrangeOverride updates the
        // buffer dimensions to match whatever pixel space we actually receive.
        HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch;
        VerticalAlignment   = Avalonia.Layout.VerticalAlignment.Stretch;

        _cursorTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(530) };
        _cursorTimer.Tick += (_, _) =>
        {
            if (_hostWindowMoving)
                return;

            _cursorOn = !_cursorOn;
            InvalidateVisual();
        };
        _cursorTimer.Start();

        _windowMoveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(160) };
        _windowMoveTimer.Tick += (_, _) =>
        {
            _windowMoveTimer.Stop();
            if (!_hostWindowMoving)
                return;

            _hostWindowMoving = false;
            InvalidateVisual();
        };

        AttachedToVisualTree += (_, _) => EnsureDirtySubscription();
        DetachedFromVisualTree += (_, _) => RemoveDirtySubscription();

        // ── Right-click context menu ─────────────────────────────────────
        var copyItem  = new MenuItem { Header = "Copy" };
        var pasteItem = new MenuItem { Header = "Paste" };

        copyItem.Click  += (_, _) => _ = CopySelectionAsync();
        pasteItem.Click += (_, _) => _ = PasteFromClipboardAsync();

        var ctxMenu = new ContextMenu();
        ctxMenu.ItemsSource = new[] { copyItem, pasteItem };
        ctxMenu.Opening += (_, _) => copyItem.IsEnabled = _hasSelection;
        ContextMenu = ctxMenu;
    }

    private void EnsureDirtySubscription()
    {
        if (_dirtySubscriptionActive)
            return;

        _buffer.DirtyRaised += OnBufferDirtyRaised;
        _dirtySubscriptionActive = true;
    }

    private void RemoveDirtySubscription()
    {
        if (!_dirtySubscriptionActive)
            return;

        _buffer.DirtyRaised -= OnBufferDirtyRaised;
        _dirtySubscriptionActive = false;
    }

    private void OnBufferDirtyRaised() => RequestRedraw();

    // ── Layout ─────────────────────────────────────────────────────────────

    private void MeasureFont()
    {
        var probe = new FormattedText(
            "W",
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            _typeFace,
            _fontSize,
            Brushes.White);
        _charWidth  = probe.Width;
        _lineHeight = probe.Height > 0 ? probe.Height : _fontSize * 1.3;
    }

    /// <summary>Change the terminal font. Can be called from the UI thread at any time.</summary>
    public void SetFont(string familyName)
    {
        _fontFamily = new FontFamily(familyName);
        _typeFace   = new Typeface(_fontFamily);
        MeasureFont();
        InvalidateVisibleRowCache();
        // Force a buffer resize on the next arrange pass
        _buffer.Resize(_buffer.Columns, _buffer.Rows);
        InvalidateMeasure();
        InvalidateVisual();
    }

    public void SetFontSize(double fontSize)
    {
        double normalized = Math.Clamp(fontSize, 8.0, 40.0);
        if (Math.Abs(_fontSize - normalized) < 0.01)
            return;

        _fontSize = normalized;
        MeasureFont();
        InvalidateVisibleRowCache();
        _buffer.Resize(_buffer.Columns, _buffer.Rows);
        InvalidateMeasure();
        InvalidateVisual();
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        // Compute how many characters fit in the available pixel area and
        // resize the backing buffer so the terminal always fills the window.
        int newCols = Math.Max(10, (int)(finalSize.Width  / _charWidth));
        int newRows = Math.Max(3,  (int)(finalSize.Height / _lineHeight));
        if (newCols != _buffer.Columns || newRows != _buffer.Rows)
        {
            InvalidateVisibleRowCache();
            _buffer.Resize(newCols, newRows);
        }
        return finalSize;
    }

    // ── Rendering ──────────────────────────────────────────────────────────

    /// <summary>
    /// Returns the cell to display at render row/col, routing through the
    /// scrollback buffer when <paramref name="scrollOff"/> &gt; 0.
    /// </summary>
    private TerminalCell GetDisplayCell(int renderRow, int col, int scrollOff)
    {
        int abs = GetViewportTopDocumentRow(scrollOff) + renderRow;
        return GetDocumentCell(abs, col);
    }

    private TerminalCell GetDocumentCell(int absRow, int col)
    {
        int sc = _buffer.ScrollbackCount;
        if (absRow < 0)
            return TerminalCell.Default;

        if (absRow < sc)
        {
            var line = _buffer.GetScrollbackLine(absRow);
            return col < line.Length ? line[col] : TerminalCell.Default;
        }

        int liveRow = absRow - sc;
        return (liveRow >= 0 && liveRow < _buffer.Rows)
            ? _buffer[liveRow, col]
            : TerminalCell.Default;
    }

    private int GetViewportTopDocumentRow(int scrollOff)
        => Math.Max(0, _buffer.ScrollbackCount - scrollOff);

    public override void Render(DrawingContext ctx)
    {
        SyncScrollAnchorToLatest();
        long renderedDirtyVersion = _buffer.DirtyVersion;
        bool cacheUpToDate = EnsureVisibleRowCache(_scrollOffset, renderedDirtyVersion);

        // Solid black background
        ctx.FillRectangle(Brushes.Black, new Rect(Bounds.Size));

        for (int row = 0; row < _visibleRowCache.Length; row++)
        {
            double y = row * _lineHeight;
            foreach (CachedRenderRun run in _visibleRowCache[row].Runs)
            {
                if (run.Background != TermColor.Black)
                    ctx.FillRectangle(GetBrush(run.Background), new Rect(run.X, y, run.Width, _lineHeight));

                if (run.Text != null && (!run.Blink || _cursorOn))
                    ctx.DrawText(run.Text, new Point(run.X, y));
            }
        }

        // Block cursor – suppressed when scrolled back
        if (_scrollOffset == 0 && _buffer.CursorVisible && _cursorOn)
        {
            double cx = _buffer.CursorCol * _charWidth;
            double cy = _buffer.CursorRow * _lineHeight;
            ctx.FillRectangle(
                new SolidColorBrush(Color.FromArgb(180, 220, 220, 220)),
                new Rect(cx, cy, _charWidth, _lineHeight));
        }

        // Selection highlight – drawn on top of text as a translucent overlay
        if (_hasSelection)
            RenderSelection(ctx);

        // Scrollback indicator bar – appears at top when scrolled back
        if (_scrollOffset > 0)
        {
            ctx.FillRectangle(
                new SolidColorBrush(Color.FromArgb(210, 0, 0, 0)),
                new Rect(0, 0, Bounds.Width, _lineHeight));
            var indFt = new FormattedText(
                $"  ── SCROLLBACK  {_scrollOffset} of {_buffer.ScrollbackCount}  " +
                 " (scroll down to return to live) ──",
                CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                _typeFace,
                _fontSize * 0.85,
                GetBrush(new TermColor(0, 200, 200)));
            ctx.DrawText(indFt, new Point(4, (_lineHeight - indFt.Height) / 2));
        }

        if (cacheUpToDate)
            _buffer.AcknowledgeDirty(renderedDirtyVersion);
        Interlocked.Exchange(ref _redrawQueued, 0);
        if (_buffer.Dirty && !_hostWindowMoving)
            RequestRedraw();
    }

    private void RenderSelection(DrawingContext ctx)
    {
        var (startR, startC, endR, endC) = NormalizedSelection();
        int topDocumentRow = GetViewportTopDocumentRow(_scrollOffset);
        int bottomDocumentRow = topDocumentRow + _buffer.Rows - 1;
        int visibleStart = Math.Max(startR, topDocumentRow);
        int visibleEnd = Math.Min(endR, bottomDocumentRow);

        for (int row = visibleStart; row <= visibleEnd; row++)
        {
            int colFrom = row == startR ? startC : 0;
            int colTo   = row == endR   ? endC   : _buffer.Columns - 1;
            colFrom = Math.Clamp(colFrom, 0, _buffer.Columns - 1);
            colTo   = Math.Clamp(colTo,   0, _buffer.Columns - 1);

            double x = colFrom * _charWidth;
            double y = (row - topDocumentRow) * _lineHeight;
            double w = (colTo - colFrom + 1) * _charWidth;
            ctx.FillRectangle(SelectionBrush, new Rect(x, y, w, _lineHeight));
        }
    }

    /// <summary>Call from any thread to schedule a repaint.</summary>
    public void RequestRedraw()
    {
        if (!_buffer.Dirty || Interlocked.Exchange(ref _redrawQueued, 1) != 0)
            return;

        Dispatcher.UIThread.Post(() =>
        {
            bool scrollAdjusted = SyncScrollAnchorToLatest();
            if (_buffer.Dirty || scrollAdjusted)
            {
                InvalidateVisual();
                return;
            }

            Interlocked.Exchange(ref _redrawQueued, 0);
        }, DispatcherPriority.Render);
    }

    public void NotifyHostWindowPositionChanged()
    {
        _hostWindowMoving = true;
        _windowMoveTimer.Stop();
        _windowMoveTimer.Start();
    }

    // ── Mouse wheel scrollback ─────────────────────────────────────────────

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        SyncScrollAnchorToLatest();

        // Accumulate fractional deltas so smooth-scroll trackpads work correctly.
        // Positive Delta.Y (scroll forward/up) = scroll back into history.
        _scrollAccumulator += e.Delta.Y * 3;
        int delta = (int)_scrollAccumulator;
        _scrollAccumulator -= delta;  // keep remainder

        if (delta != 0)
        {
            _scrollOffset = Math.Clamp(
                _scrollOffset + delta, 0, _buffer.ScrollbackCount);

            if (e.Pointer.Captured == this)
            {
                var pos = PixelToDocumentCell(e.GetPosition(this));
                if (pos.Row > _selAnchor.Row && pos.Col == 0 && pos.Row > 0)
                    pos = (pos.Row - 1, _buffer.Columns - 1);
                _selCurrent = pos;
                _hasSelection = _selCurrent != _selAnchor;
            }

            InvalidateVisual();
        }
        e.Handled = true;
        base.OnPointerWheelChanged(e);
    }

    // ── Keyboard input ─────────────────────────────────────────────────────

    /// <summary>
    /// Resets scroll-back to the live view, then forwards bytes to the server.
    /// Use this instead of calling <see cref="SendInput"/> directly.
    /// </summary>
    private void SendBytes(byte[] bytes)
    {
        SyncScrollAnchorToLatest();
        if (_scrollOffset != 0) { _scrollOffset = 0; InvalidateVisual(); }
        SendInput?.Invoke(bytes);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        bool primaryModifier = e.KeyModifiers.HasFlag(KeyModifiers.Control) ||
                               e.KeyModifiers.HasFlag(KeyModifiers.Meta);

        // Ctrl+C: copy selection if active — works even when disconnected
        if (primaryModifier && e.Key == Key.C)
        {
            if (_hasSelection)
            {
                _ = CopySelectionAsync();
                e.Handled = true;
                return;
            }
            // Fall through: no selection → sends 0x03 to server below
        }

        // Ctrl+V: paste from clipboard — only when connected
        if (primaryModifier && e.Key == Key.V)
        {
            if (IsConnected)
                _ = PasteFromClipboardAsync();
            e.Handled = true;
            return;
        }

        // Windows-style clipboard shortcuts
        if (e.Key == Key.Insert && e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            if (_hasSelection)
                _ = CopySelectionAsync();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Insert && e.KeyModifiers.HasFlag(KeyModifiers.Shift))
        {
            if (IsConnected)
                _ = PasteFromClipboardAsync();
            e.Handled = true;
            return;
        }

        // When not connected, swallow all other keypresses silently
        if (!IsConnected)
        {
            e.Handled = true;
            return;
        }

        byte[]? bytes = SpecialKeyToBytes(e);
        if (bytes != null)
        {
            SendBytes(bytes);
            e.Handled = true;
            return;
        }
        base.OnKeyDown(e);
    }

    protected override void OnTextInput(TextInputEventArgs e)
    {
        if (!IsConnected)
        {
            e.Handled = true;
            base.OnTextInput(e);
            return;
        }
        if (!string.IsNullOrEmpty(e.Text))
        {
            // Filter out control characters — those are handled exclusively by
            // OnKeyDown (e.g. Backspace fires both OnKeyDown and OnTextInput on
            // Windows; letting both through sends duplicate bytes to the server).
            var text = e.Text;
            if (text.Length == 1 && (text[0] < '\x20' || text[0] == '\x7F'))
            {
                e.Handled = true;
                base.OnTextInput(e);
                return;
            }
            SendBytes(Encoding.Latin1.GetBytes(text));
            e.Handled = true;
        }
        base.OnTextInput(e);
    }

    /// <summary>
    /// Maps special / control keys to VT100 byte sequences.
    /// Printable characters are handled by <see cref="OnTextInput"/>.
    /// </summary>
    private static byte[]? SpecialKeyToBytes(KeyEventArgs e)
    {
        bool ctrl = e.KeyModifiers.HasFlag(KeyModifiers.Control);

        // Ctrl + letter → control code 0x01-0x1A
        if (ctrl && !e.KeyModifiers.HasFlag(KeyModifiers.Alt))
        {
            byte? cc = e.Key switch
            {
                Key.A => 1,  Key.B => 2,  Key.C => 3,  Key.D => 4,
                Key.E => 5,  Key.F => 6,  Key.G => 7,  Key.H => 8,
                Key.I => 9,  Key.J => 10, Key.K => 11, Key.L => 12,
                Key.M => 13, Key.N => 14, Key.O => 15, Key.P => 16,
                Key.Q => 17, Key.R => 18, Key.S => 19, Key.T => 20,
                Key.U => 21, Key.V => 22, Key.W => 23, Key.X => 24,
                Key.Y => 25, Key.Z => 26,
                _ => null,
            };
            if (cc != null) return [cc.Value];
        }

        return e.Key switch
        {
            Key.Return   => "\r"u8.ToArray(),
            Key.Back     => [0x08],   // BS (Ctrl+H) – expected by TW2002/TWXProxy
            Key.Escape   => [0x1B],
            Key.Tab      => [0x09],
            Key.Delete   => [0x1B, (byte)'[', (byte)'3', (byte)'~'],
            Key.Up       => [0x1B, (byte)'[', (byte)'A'],
            Key.Down     => [0x1B, (byte)'[', (byte)'B'],
            Key.Right    => [0x1B, (byte)'[', (byte)'C'],
            Key.Left     => [0x1B, (byte)'[', (byte)'D'],
            Key.Home     => [0x1B, (byte)'[', (byte)'H'],
            Key.End      => [0x1B, (byte)'[', (byte)'F'],
            Key.PageUp   => [0x1B, (byte)'[', (byte)'5', (byte)'~'],
            Key.PageDown => [0x1B, (byte)'[', (byte)'6', (byte)'~'],
            Key.F1       => [0x1B, (byte)'O', (byte)'P'],
            Key.F2       => [0x1B, (byte)'O', (byte)'Q'],
            Key.F3       => [0x1B, (byte)'O', (byte)'R'],
            Key.F4       => [0x1B, (byte)'O', (byte)'S'],
            Key.F5       => [0x1B, (byte)'[', (byte)'1', (byte)'5', (byte)'~'],
            Key.F6       => [0x1B, (byte)'[', (byte)'1', (byte)'7', (byte)'~'],
            Key.F7       => [0x1B, (byte)'[', (byte)'1', (byte)'8', (byte)'~'],
            Key.F8       => [0x1B, (byte)'[', (byte)'1', (byte)'9', (byte)'~'],
            Key.F9       => [0x1B, (byte)'[', (byte)'2', (byte)'0', (byte)'~'],
            Key.F10      => [0x1B, (byte)'[', (byte)'2', (byte)'1', (byte)'~'],
            Key.F11      => [0x1B, (byte)'[', (byte)'2', (byte)'3', (byte)'~'],
            Key.F12      => [0x1B, (byte)'[', (byte)'2', (byte)'4', (byte)'~'],
            _            => null,
        };
    }

    internal static bool TryGetMacroHotkeyName(ReadOnlySpan<byte> bytes, out string hotkey)
    {
        foreach ((string name, byte[] definitionBytes) in MacroHotkeyDefinitions)
        {
            if (bytes.SequenceEqual(definitionBytes))
            {
                hotkey = name;
                return true;
            }
        }

        hotkey = string.Empty;
        return false;
    }

    // ── Mouse selection ────────────────────────────────────────────────────

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        var props = e.GetCurrentPoint(this).Properties;
        // Use PointerUpdateKind ("button just went down") not IsLeftButtonPressed
        // ("button is held now") — the latter can be true during a right-click event
        // on some platforms, causing the selection anchor to jump.
        if (props.PointerUpdateKind == PointerUpdateKind.LeftButtonPressed)
        {
            Focus();
            _selAnchor   = _selCurrent = PixelToDocumentCell(e.GetPosition(this));
            _hasSelection = false;
            e.Pointer.Capture(this);
            InvalidateVisual();
            e.Handled = true;
        }
        // Right-click opens the context menu (Avalonia handles it automatically)
        base.OnPointerPressed(e);
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        if (e.Pointer.Captured == this)
        {
            var pos = PixelToDocumentCell(e.GetPosition(this));
            // When dragging downward, snap col=0 of the next row back to the
            // end of the previous row so that dragging down selects whole lines.
            if (pos.Row > _selAnchor.Row && pos.Col == 0 && pos.Row > 0)
                pos = (pos.Row - 1, _buffer.Columns - 1);
            _selCurrent = pos;
            _hasSelection = _selCurrent != _selAnchor;
            InvalidateVisual();
        }
        base.OnPointerMoved(e);
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        var props = e.GetCurrentPoint(this).Properties;
        // Only finalise the selection for the LEFT button — right-button-up
        // events (and any synthetic releases caused by a ContextMenu popup
        // grabbing focus) must not move _selCurrent.
        if (e.Pointer.Captured == this &&
            props.PointerUpdateKind == PointerUpdateKind.LeftButtonReleased)
        {
            var pos = PixelToDocumentCell(e.GetPosition(this));
            // Same line-end snap as OnPointerMoved
            if (pos.Row > _selAnchor.Row && pos.Col == 0 && pos.Row > 0)
                pos = (pos.Row - 1, _buffer.Columns - 1);
            _selCurrent = pos;
            _hasSelection = _selCurrent != _selAnchor;
            e.Pointer.Capture(null);
            InvalidateVisual();
        }
        base.OnPointerReleased(e);
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        // Capture was taken away externally (e.g. ContextMenu popup appearing).
        // Do NOT touch _selCurrent / _hasSelection — just let Avalonia clean up.
        base.OnPointerCaptureLost(e);
    }

    // ── Clipboard ─────────────────────────────────────────────────────────

    private async Task CopySelectionAsync()
    {
        if (!_hasSelection) return;
        string text = GetSelectedText();
        if (text.Length == 0)
            return;

        await TrySetClipboardTextAsync(text);
    }

    private async Task PasteFromClipboardAsync()
    {
        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard == null) return;
        string? text = await ClipboardExtensions.TryGetTextAsync(clipboard);
        if (!string.IsNullOrEmpty(text))
            SendBytes(Encoding.Latin1.GetBytes(text));
    }

    private async Task<bool> TrySetClipboardTextAsync(string text)
    {
        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard != null)
        {
            try
            {
                await clipboard.SetTextAsync(text);
                string? roundTrip = await ClipboardExtensions.TryGetTextAsync(clipboard);
                if (ClipboardTextMatches(roundTrip, text))
                    return true;
            }
            catch
            {
                // Fall through to platform fallback below.
            }
        }

        if (OperatingSystem.IsWindows())
            return await TrySetWindowsClipboardFallbackAsync(text);

        return false;
    }

    private static bool ClipboardTextMatches(string? actual, string expected)
    {
        if (string.IsNullOrEmpty(actual))
            return false;

        return NormalizeClipboardText(actual) == NormalizeClipboardText(expected);
    }

    private static string NormalizeClipboardText(string text)
        => text.Replace("\r\n", "\n").Replace('\r', '\n');

    private static async Task<bool> TrySetWindowsClipboardFallbackAsync(string text)
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = "/c clip",
                UseShellExecute = false,
                RedirectStandardInput = true,
                CreateNoWindow = true,
                StandardInputEncoding = Encoding.Unicode,
            };

            using var process = new Process { StartInfo = startInfo };
            if (!process.Start())
                return false;

            string clipboardText = text.Replace("\n", "\r\n");
            await process.StandardInput.WriteAsync(clipboardText);
            await process.StandardInput.FlushAsync();
            process.StandardInput.Close();
            await process.WaitForExitAsync();
            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    // ── Selection helpers ─────────────────────────────────────────────────

    /// <summary>Convert a pixel position to a viewport-relative (row, col) cell coordinate.</summary>
    private (int Row, int Col) PixelToViewportCell(Point p)
    {
        int col = Math.Clamp((int)(p.X / _charWidth),  0, _buffer.Columns - 1);
        int row = Math.Clamp((int)(p.Y / _lineHeight), 0, _buffer.Rows    - 1);
        return (row, col);
    }

    /// <summary>Convert a pixel position to an absolute document (row, col) coordinate.</summary>
    private (int Row, int Col) PixelToDocumentCell(Point p)
    {
        var (row, col) = PixelToViewportCell(p);
        return (GetViewportTopDocumentRow(_scrollOffset) + row, col);
    }

    /// <summary>Returns (startRow, startCol, endRow, endCol) with start &lt;= end.</summary>
    private (int SR, int SC, int ER, int EC) NormalizedSelection()
    {
        var a = _selAnchor;
        var b = _selCurrent;
        // Compare in document order
        bool aFirst = a.Row < b.Row || (a.Row == b.Row && a.Col <= b.Col);
        return aFirst
            ? (a.Row, a.Col, b.Row, b.Col)
            : (b.Row, b.Col, a.Row, a.Col);
    }

    /// <summary>Extract the selected region from the buffer as plain text.</summary>
    private string GetSelectedText()
    {
        var (startR, startC, endR, endC) = NormalizedSelection();
        var sb = new StringBuilder();

        for (int row = startR; row <= endR; row++)
        {
            int colFrom = row == startR ? startC : 0;
            int colTo   = row == endR   ? endC   : _buffer.Columns - 1;

            var line = new StringBuilder();
            for (int c = colFrom; c <= colTo && c < _buffer.Columns; c++)
                line.Append(GetDocumentCell(row, c).Char);

            // Trim trailing spaces from each line (matches typical terminal copy behaviour)
            string lineStr = line.ToString().TrimEnd();

            if (sb.Length > 0) sb.Append('\n');
            sb.Append(lineStr);
        }

        return sb.ToString();
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    private SolidColorBrush GetBrush(TermColor c)
    {
        if (!_brushCache.TryGetValue(c, out var brush))
        {
            brush = new SolidColorBrush(Color.FromRgb(c.R, c.G, c.B));
            _brushCache[c] = brush;
        }
        return brush;
    }

    private void InvalidateVisibleRowCache()
    {
        _visibleRowCache = [];
        _visibleRowCacheDirtyVersion = -1;
        _visibleRowCacheTopDocumentRow = int.MinValue;
        _visibleRowCacheColumns = -1;
        _visibleRowCacheRows = -1;
        _visibleRowCacheFontSize = -1;
        _visibleRowCacheFontKey = string.Empty;
    }

    private bool EnsureVisibleRowCache(int scrollOff, long dirtyVersion)
    {
        int topDocumentRow = GetViewportTopDocumentRow(scrollOff);
        string fontKey = _fontFamily.ToString();
        bool viewportMatches =
            _visibleRowCache.Length == _buffer.Rows &&
            _visibleRowCacheTopDocumentRow == topDocumentRow &&
            _visibleRowCacheColumns == _buffer.Columns &&
            _visibleRowCacheRows == _buffer.Rows &&
            Math.Abs(_visibleRowCacheFontSize - _fontSize) < 0.01 &&
            string.Equals(_visibleRowCacheFontKey, fontKey, StringComparison.Ordinal);

        if (_hostWindowMoving && viewportMatches)
            return _visibleRowCacheDirtyVersion == dirtyVersion;

        if (viewportMatches && _visibleRowCacheDirtyVersion == dirtyVersion)
            return true;

        var rows = new CachedRenderRow[_buffer.Rows];
        var runBuilder = new StringBuilder(_buffer.Columns);

        for (int row = 0; row < _buffer.Rows; row++)
        {
            int col = 0;
            var runs = new List<CachedRenderRun>();

            while (col < _buffer.Columns)
            {
                var cell = GetDisplayCell(row, col, scrollOff);
                var fg = cell.Foreground;
                var bg = cell.Background;
                bool blink = cell.Blink;

                int end = col + 1;
                while (end < _buffer.Columns)
                {
                    var next = GetDisplayCell(row, end, scrollOff);
                    if (next.Foreground != fg || next.Background != bg || next.Blink != blink)
                        break;

                    end++;
                }

                runBuilder.Clear();
                bool hasVisibleGlyph = false;
                for (int i = col; i < end; i++)
                {
                    char ch = GetDisplayCell(row, i, scrollOff).Char;
                    runBuilder.Append(ch);
                    hasVisibleGlyph |= ch != ' ';
                }

                FormattedText? ft = null;
                if (hasVisibleGlyph)
                {
                    ft = new FormattedText(
                        runBuilder.ToString(),
                        CultureInfo.InvariantCulture,
                        FlowDirection.LeftToRight,
                        _typeFace,
                        _fontSize,
                        GetBrush(fg));
                }

                runs.Add(new CachedRenderRun
                {
                    X = col * _charWidth,
                    Width = (end - col) * _charWidth,
                    Background = bg,
                    Blink = blink,
                    Text = ft,
                });

                col = end;
            }

            rows[row] = new CachedRenderRow { Runs = runs.ToArray() };
        }

        _visibleRowCache = rows;
        _visibleRowCacheDirtyVersion = dirtyVersion;
        _visibleRowCacheTopDocumentRow = topDocumentRow;
        _visibleRowCacheColumns = _buffer.Columns;
        _visibleRowCacheRows = _buffer.Rows;
        _visibleRowCacheFontSize = _fontSize;
        _visibleRowCacheFontKey = fontKey;
        return true;
    }

    private bool SyncScrollAnchorToLatest()
    {
        long latestGeneration = _buffer.ScrollbackGeneration;
        long delta = latestGeneration - _scrollGenerationSeen;
        _scrollGenerationSeen = latestGeneration;
        if (delta <= 0 || _scrollOffset == 0)
            return false;

        int maxOffset = _buffer.ScrollbackCount;
        int nextOffset = (int)Math.Clamp((long)_scrollOffset + delta, 0L, (long)maxOffset);
        int appliedDelta = nextOffset - _scrollOffset;
        if (appliedDelta == 0)
            return false;

        _scrollOffset = nextOffset;

        return true;
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _cursorTimer.Stop();
        _windowMoveTimer.Stop();
        base.OnDetachedFromVisualTree(e);
    }
}
