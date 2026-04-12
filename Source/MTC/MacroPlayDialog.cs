using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace MTC;

public sealed class MacroPlayDialog : Window
{
    private static readonly IBrush BgPanel = new SolidColorBrush(Color.FromRgb(30, 30, 30));
    private static readonly IBrush BgInput = new SolidColorBrush(Color.FromRgb(20, 20, 20));
    private static readonly IBrush BdInput = new SolidColorBrush(Color.FromRgb(70, 70, 70));
    private static readonly IBrush FgNormal = new SolidColorBrush(Color.FromRgb(170, 170, 170));
    private static readonly IBrush FgLabel = new SolidColorBrush(Color.FromRgb(200, 200, 200));
    private static readonly IBrush FgError = new SolidColorBrush(Color.FromRgb(255, 128, 128));
    private static readonly IBrush BgButton = new SolidColorBrush(Color.FromRgb(55, 55, 55));

    private readonly Func<string, string?>? _macroValidator;
    private readonly TextBox _macroTextBox;
    private readonly TextBox _countTextBox;
    private readonly TextBlock _errorText;

    public int PlayCount { get; private set; } = 1;
    public string MacroText { get; private set; } = string.Empty;

    public MacroPlayDialog(string macro, Func<string, string?>? macroValidator = null)
    {
        _macroValidator = macroValidator;
        Title = "Play Macro";
        Width = 640;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = BgPanel;

        var macroLabel = new TextBlock
        {
            Text = "Macro",
            Foreground = FgLabel,
            FontWeight = FontWeight.Bold,
        };

        _macroTextBox = new TextBox
        {
            Text = macro,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            MinHeight = 90,
            Background = BgInput,
            Foreground = FgNormal,
            BorderBrush = BdInput,
        };

        var countLabel = new TextBlock
        {
            Text = "Times to play (1-1000)",
            Foreground = FgLabel,
            FontWeight = FontWeight.Bold,
            Margin = new Thickness(0, 8, 0, 0),
        };

        _countTextBox = new TextBox
        {
            Text = "1",
            Width = 90,
            Background = BgInput,
            Foreground = FgNormal,
            BorderBrush = BdInput,
            HorizontalAlignment = HorizontalAlignment.Left,
        };

        _errorText = new TextBlock
        {
            Foreground = FgError,
            IsVisible = false,
            TextWrapping = TextWrapping.Wrap,
        };

        var btnGo = new Button
        {
            Content = "Go",
            MinWidth = 80,
            Background = BgButton,
            Foreground = FgNormal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 0, 8, 0),
        };

        var btnCancel = new Button
        {
            Content = "Cancel",
            MinWidth = 80,
            Background = BgButton,
            Foreground = FgNormal,
        };

        btnGo.Click += (_, _) => TryAccept();
        btnCancel.Click += (_, _) => Close(false);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 12, 0, 0),
            Children = { btnGo, btnCancel },
        };

        Content = new StackPanel
        {
            Margin = new Thickness(16),
            Spacing = 6,
            Children =
            {
                macroLabel,
                _macroTextBox,
                countLabel,
                _countTextBox,
                _errorText,
                buttons,
            },
        };

        Opened += (_, _) => FocusCountTextBox();
        Activated += (_, _) => FocusCountTextBox();

        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                Close(false);
                e.Handled = true;
            }
            else if (e.Key == Key.Enter || e.Key == Key.Return)
            {
                TryAccept();
                e.Handled = true;
            }
        };
    }

    private void TryAccept()
    {
        _errorText.Text = string.Empty;
        _errorText.IsVisible = false;

        if (!int.TryParse((_countTextBox.Text ?? string.Empty).Trim(), out int count) || count < 1 || count > 1000)
        {
            _errorText.Text = "Enter a whole number from 1 to 1000.";
            _errorText.IsVisible = true;
            return;
        }

        string macroText = _macroTextBox.Text ?? string.Empty;
        string? macroError = _macroValidator?.Invoke(macroText);
        if (!string.IsNullOrWhiteSpace(macroError))
        {
            _errorText.Text = macroError;
            _errorText.IsVisible = true;
            return;
        }

        MacroText = macroText;
        PlayCount = count;
        Close(true);
    }

    private void FocusCountTextBox()
    {
        Dispatcher.UIThread.Post(() =>
        {
            Activate();
            _countTextBox.Focus(NavigationMethod.Tab);
            _countTextBox.SelectAll();
        }, DispatcherPriority.Input);
    }
}
