using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using TWXProxy.Core;

namespace MTC;

// ── Popup Window ─────────────────────────────────────────────────────────────

/// <summary>
/// A standalone Avalonia Window opened by the TWX script WINDOW command.
/// Content is set via SETWINDOWCONTENTS and uses '*' as the line separator.
/// </summary>
internal class ScriptPopupWindow : Window
{
    private readonly ScrollViewer _scroll;
    private readonly StackPanel   _lines;

    private static readonly IBrush BgWin  = new SolidColorBrush(Color.FromRgb(0x08, 0x0c, 0x18));
    private static readonly IBrush FgText = new SolidColorBrush(Color.FromRgb(0xd8, 0xe8, 0xff));
    private static readonly IBrush FgMute = new SolidColorBrush(Color.FromRgb(0x78, 0x88, 0xaa));

    public ScriptPopupWindow(string name, string title, int width, int height, bool onTop)
    {
        Title      = string.IsNullOrWhiteSpace(title) ? name : title;
        Width      = Math.Max(width,  120);
        Height     = Math.Max(height, 80);
        MinWidth   = 120;
        MinHeight  = 60;
        Background = BgWin;
        Topmost    = onTop;
        // Don't show in taskbar — these are script auxiliary windows
        ShowInTaskbar = false;
        CanResize     = true;

        FontFamily = new FontFamily("Cascadia Code, Menlo, Consolas, Courier New, monospace");
        FontSize   = 12;

        _lines = new StackPanel { Orientation = Orientation.Vertical, Margin = new Thickness(6, 4, 6, 4) };
        _scroll = new ScrollViewer
        {
            Content            = _lines,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility   = ScrollBarVisibility.Auto,
        };

        Content = _scroll;
    }

    /// <summary>
    /// Replace all displayed lines. Content uses '*' as the line separator
    /// (the TWX script convention: "line1*line2*line3").
    /// Leading/trailing '*' produce blank lines and are preserved.
    /// </summary>
    public void SetContent(string rawContent)
    {
        var parts = rawContent.Split('*');
        _lines.Children.Clear();
        foreach (var part in parts)
        {
            _lines.Children.Add(new TextBlock
            {
                Text       = part,
                Foreground = string.IsNullOrWhiteSpace(part) ? FgMute : FgText,
                TextWrapping = TextWrapping.NoWrap,
                Margin     = new Thickness(0, 1, 0, 1),
            });
        }
        _scroll.ScrollToEnd();
    }
}

// ── IScriptWindow wrapper ─────────────────────────────────────────────────────

/// <summary>
/// Wraps a <see cref="ScriptPopupWindow"/> as an <see cref="IScriptWindow"/>.
/// All Avalonia calls are marshalled to the UI thread.
/// </summary>
internal sealed class AvaloniaScriptWindow : IScriptWindow
{
    private ScriptPopupWindow? _popup;
    private string _textContent = string.Empty;
    private bool   _isVisible;
    private bool   _disposed;

    private readonly string _name;
    private readonly string _title;
    private readonly int    _width;
    private readonly int    _height;
    private readonly bool   _onTop;

    public AvaloniaScriptWindow(string name, string title, int width, int height, bool onTop)
    {
        _name   = name;
        _title  = title;
        _width  = width;
        _height = height;
        _onTop  = onTop;
    }

    public string Name        => _name;
    public string Title       => _title;
    public int    Width       => _width;
    public int    Height      => _height;
    public bool   OnTop       => _onTop;
    public bool   IsVisible   => _isVisible;

    public string TextContent
    {
        get => _textContent;
        set
        {
            _textContent = value;
            if (_isVisible && _popup is not null)
                Dispatcher.UIThread.Post(() => _popup?.SetContent(value));
        }
    }

    public void Show()
    {
        if (_disposed || _isVisible) return;
        _isVisible = true;
        Dispatcher.UIThread.Post(() =>
        {
            if (_popup is null)
            {
                _popup = new ScriptPopupWindow(_name, _title, _width, _height, _onTop);
                _popup.Closed += (_, _) =>
                {
                    _popup    = null;
                    _isVisible = false;
                };
            }
            if (!string.IsNullOrEmpty(_textContent))
                _popup.SetContent(_textContent);
            _popup.Show();
        });
    }

    public void Hide()
    {
        if (!_isVisible) return;
        _isVisible = false;
        Dispatcher.UIThread.Post(() => { _popup?.Close(); _popup = null; });
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Hide();
    }
}

// ── Factory ───────────────────────────────────────────────────────────────────

/// <summary>
/// Registered on <see cref="GlobalModules.ScriptWindowFactory"/> at startup.
/// Creates real Avalonia popup windows for script WINDOW commands.
/// </summary>
public sealed class AvaloniaScriptWindowFactory : IScriptWindowFactory
{
    public IScriptWindow CreateWindow(string name, string title, int width, int height, bool onTop = false)
        => new AvaloniaScriptWindow(name, title, width, height, onTop);
}
