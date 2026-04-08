using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace MTC;

/// <summary>
/// Floating in-console window used by the command-deck skin.
/// Supports dragging, minimizing, and optional closing.
/// </summary>
public sealed class FloatingDeckPanel : Border
{
    private readonly Border _bodyHost;
    private readonly Button _minButton;
    private readonly Button? _closeButton;
    private readonly double _bodyHeight;
    private bool _isDragging;
    private bool _isClosed;
    private bool _isMinimized;
    private Point _dragStartPointer;
    private double _dragStartLeft;
    private double _dragStartTop;

    public string PanelId { get; }
    public bool CanClose { get; }
    public bool IsMinimized => _isMinimized;
    public bool IsClosed => _isClosed;

    public event Action<FloatingDeckPanel>? Activated;
    public event Action<FloatingDeckPanel>? StateChanged;

    public FloatingDeckPanel(
        string panelId,
        string title,
        string tag,
        Control body,
        double width,
        double bodyHeight,
        bool canClose,
        IBrush frameBrush,
        IBrush frameAltBrush,
        IBrush headerBrush,
        IBrush headerAltBrush,
        IBrush edgeBrush,
        IBrush innerEdgeBrush,
        IBrush titleBrush,
        IBrush mutedBrush,
        FontFamily titleFont)
    {
        PanelId = panelId;
        CanClose = canClose;
        _bodyHeight = bodyHeight;

        Width = width;
        Background = frameBrush;
        BorderBrush = edgeBrush;
        BorderThickness = new Thickness(1.4);
        CornerRadius = new CornerRadius(18);
        Padding = new Thickness(2);

        _bodyHost = new Border
        {
            Padding = new Thickness(14),
            Height = bodyHeight,
            Child = body,
        };

        var titleText = new TextBlock
        {
            Text = title,
            FontFamily = titleFont,
            FontSize = 16,
            FontWeight = FontWeight.Bold,
            Foreground = titleBrush,
            VerticalAlignment = VerticalAlignment.Center,
        };

        var tagBadge = new Border
        {
            Background = headerAltBrush,
            BorderBrush = innerEdgeBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(8, 3),
            Margin = new Thickness(0, 0, 8, 0),
            Child = new TextBlock
            {
                Text = tag,
                Foreground = mutedBrush,
                FontSize = 10,
                FontWeight = FontWeight.SemiBold,
                VerticalAlignment = VerticalAlignment.Center,
            },
        };

        _minButton = BuildHeaderButton("—", mutedBrush);
        _minButton.Click += (_, _) =>
        {
            SetMinimized(!_isMinimized);
            Activated?.Invoke(this);
        };

        if (canClose)
        {
            _closeButton = BuildHeaderButton("✕", mutedBrush);
            _closeButton.Click += (_, _) =>
            {
                SetClosed(true);
                Activated?.Invoke(this);
            };
        }

        var headerRight = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            Children = { tagBadge, _minButton },
        };
        if (_closeButton != null)
            headerRight.Children.Add(_closeButton);

        var titleBar = new Border
        {
            Background = headerBrush,
            CornerRadius = new CornerRadius(14, 14, 0, 0),
            Padding = new Thickness(14, 10),
            Child = new Grid
            {
                ColumnDefinitions =
                {
                    new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
                    new ColumnDefinition { Width = GridLength.Auto },
                },
                Children =
                {
                    titleText,
                    headerRight,
                },
            },
        };
        Grid.SetColumn(headerRight, 1);

        titleBar.PointerPressed += OnTitleBarPointerPressed;
        titleBar.PointerMoved += OnTitleBarPointerMoved;
        titleBar.PointerReleased += OnTitleBarPointerReleased;
        titleBar.DoubleTapped += (_, _) =>
        {
            SetMinimized(!_isMinimized);
            Activated?.Invoke(this);
        };
        PointerPressed += (_, _) => Activated?.Invoke(this);

        var contentGrid = new Grid();
        contentGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        contentGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        contentGrid.Children.Add(titleBar);
        Grid.SetRow(_bodyHost, 1);
        contentGrid.Children.Add(_bodyHost);

        Child = new Border
        {
            Background = frameAltBrush,
            BorderBrush = innerEdgeBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(16),
            Child = contentGrid,
        };
    }

    public void MoveTo(double left, double top)
    {
        if (Parent is Control host)
        {
            double panelWidth = Bounds.Width > 1 ? Bounds.Width : Width;
            double panelHeight = Bounds.Height > 1 ? Bounds.Height : (_isMinimized ? 52 : _bodyHeight + 56);
            double maxLeft = Math.Max(0, host.Bounds.Width - panelWidth);
            double maxTop = Math.Max(0, host.Bounds.Height - panelHeight);
            left = Math.Clamp(left, 0, maxLeft);
            top = Math.Clamp(top, 0, maxTop);
        }

        Canvas.SetLeft(this, left);
        Canvas.SetTop(this, top);
        StateChanged?.Invoke(this);
    }

    public void SetMinimized(bool minimized)
    {
        _isMinimized = minimized;
        _bodyHost.IsVisible = !minimized;
        _bodyHost.Height = minimized ? 0 : _bodyHeight;
        _bodyHost.Margin = minimized ? new Thickness(0) : new Thickness(0);
        _minButton.Content = minimized ? "▢" : "—";
        StateChanged?.Invoke(this);
    }

    public void SetClosed(bool closed)
    {
        if (closed && !CanClose)
            return;

        _isClosed = closed;
        IsVisible = !closed;
        StateChanged?.Invoke(this);
    }

    public void Restore(double left, double top)
    {
        if (_isClosed)
            _isClosed = false;

        IsVisible = true;
        if (_isMinimized)
            SetMinimized(false);

        MoveTo(left, top);
        Activated?.Invoke(this);
    }

    public (double Left, double Top) GetPosition()
    {
        double left = Canvas.GetLeft(this);
        double top = Canvas.GetTop(this);
        return (double.IsNaN(left) ? 0 : left, double.IsNaN(top) ? 0 : top);
    }

    private static Button BuildHeaderButton(string label, IBrush foreground)
    {
        return new Button
        {
            Content = label,
            Width = 22,
            Height = 22,
            Padding = new Thickness(0),
            Background = Brushes.Transparent,
            BorderBrush = Brushes.Transparent,
            Foreground = foreground,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
    }

    private void OnTitleBarPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.Source is Button || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed || Parent is not Control host)
            return;

        Activated?.Invoke(this);
        _isDragging = true;
        _dragStartPointer = e.GetPosition(host);
        (_dragStartLeft, _dragStartTop) = GetPosition();
        e.Pointer.Capture((IInputElement?)sender);
        e.Handled = true;
    }

    private void OnTitleBarPointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_isDragging || Parent is not Control host)
            return;

        Point current = e.GetPosition(host);
        MoveTo(
            _dragStartLeft + (current.X - _dragStartPointer.X),
            _dragStartTop + (current.Y - _dragStartPointer.Y));
        e.Handled = true;
    }

    private void OnTitleBarPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!_isDragging)
            return;

        _isDragging = false;
        e.Pointer.Capture(null);
        StateChanged?.Invoke(this);
        e.Handled = true;
    }
}
