using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace MTC;

public sealed class MacroSettingsDialog : Window
{
    private static readonly IBrush BgPanel = new SolidColorBrush(Color.FromRgb(30, 30, 30));
    private static readonly IBrush BgInput = new SolidColorBrush(Color.FromRgb(20, 20, 20));
    private static readonly IBrush BdInput = new SolidColorBrush(Color.FromRgb(70, 70, 70));
    private static readonly IBrush FgNormal = new SolidColorBrush(Color.FromRgb(170, 170, 170));
    private static readonly IBrush FgLabel = new SolidColorBrush(Color.FromRgb(200, 200, 200));
    private static readonly IBrush FgError = new SolidColorBrush(Color.FromRgb(255, 128, 128));
    private static readonly IBrush BgButton = new SolidColorBrush(Color.FromRgb(55, 55, 55));
    private static readonly IBrush AccentBorder = new SolidColorBrush(Color.FromRgb(64, 144, 255));

    private sealed class MacroRowState
    {
        public required Border Border { get; init; }
        public required ComboBox HotkeyCombo { get; init; }
        public required TextBox MacroTextBox { get; init; }
    }

    private readonly List<MacroRowState> _rows = [];
    private readonly StackPanel _rowsPanel = new() { Spacing = 8 };
    private readonly TextBlock _errorText = new()
    {
        Foreground = FgError,
        IsVisible = false,
        TextWrapping = TextWrapping.Wrap,
    };

    private MacroRowState? _selectedRow;

    public IReadOnlyList<AppPreferences.MacroBinding> Result { get; private set; } = Array.Empty<AppPreferences.MacroBinding>();

    public MacroSettingsDialog(IReadOnlyList<AppPreferences.MacroBinding> defaults)
    {
        Title = "Macros";
        Width = 720;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = BgPanel;

        var btnAdd = new Button
        {
            Content = "+",
            Width = 34,
            Background = BgButton,
            Foreground = FgNormal,
            FontWeight = FontWeight.Bold,
        };

        var btnRemove = new Button
        {
            Content = "-",
            Width = 34,
            Background = BgButton,
            Foreground = FgNormal,
            FontWeight = FontWeight.Bold,
            Margin = new Thickness(8, 0, 0, 0),
        };

        btnAdd.Click += (_, _) =>
        {
            AddRow(new AppPreferences.MacroBinding { Hotkey = GetNextAvailableHotkey(), Macro = string.Empty });
            ClearError();
        };

        btnRemove.Click += (_, _) =>
        {
            RemoveSelectedRow();
            ClearError();
        };

        var controlsRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 0,
            Children = { btnAdd, btnRemove },
        };

        var headerRow = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("130,*"),
            Margin = new Thickness(0, 2, 0, 4),
        };
        headerRow.Children.Add(new TextBlock
        {
            Text = "Hotkey",
            Foreground = FgLabel,
            FontWeight = FontWeight.Bold,
            Margin = new Thickness(12, 0, 0, 0),
        });
        var macroHeader = new TextBlock
        {
            Text = "Macro",
            Foreground = FgLabel,
            FontWeight = FontWeight.Bold,
        };
        Grid.SetColumn(macroHeader, 1);
        headerRow.Children.Add(macroHeader);

        var hintText = new TextBlock
        {
            Text = "'*' sends a carriage return.",
            Foreground = FgNormal,
            Margin = new Thickness(0, 0, 0, 8),
        };

        foreach (AppPreferences.MacroBinding binding in defaults)
            AddRow(new AppPreferences.MacroBinding { Hotkey = NormalizeHotkey(binding.Hotkey), Macro = binding.Macro ?? string.Empty });

        if (_rows.Count == 0)
            AddRow(new AppPreferences.MacroBinding { Hotkey = "F1", Macro = string.Empty });

        var rowsScroll = new ScrollViewer
        {
            Content = _rowsPanel,
            MaxHeight = 360,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        };

        var btnSave = new Button
        {
            Content = "Save",
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

        btnSave.Click += (_, _) =>
        {
            if (!TryBuildResult(out IReadOnlyList<AppPreferences.MacroBinding> bindings, out string? error))
            {
                ShowError(error ?? "Unable to save macros.");
                return;
            }

            Result = bindings;
            Close(true);
        };

        btnCancel.Click += (_, _) => Close(false);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 12, 0, 0),
            Children = { btnSave, btnCancel },
        };

        Content = new StackPanel
        {
            Margin = new Thickness(16),
            Spacing = 6,
            Children =
            {
                controlsRow,
                headerRow,
                hintText,
                rowsScroll,
                _errorText,
                buttons,
            },
        };
    }

    private void AddRow(AppPreferences.MacroBinding binding)
    {
        var hotkeyCombo = new ComboBox
        {
            ItemsSource = TerminalControl.SupportedMacroHotkeys,
            SelectedItem = NormalizeHotkey(binding.Hotkey),
            Width = 110,
            Background = BgInput,
            Foreground = FgNormal,
            BorderBrush = BdInput,
            HorizontalAlignment = HorizontalAlignment.Left,
        };

        var macroTextBox = new TextBox
        {
            Text = binding.Macro ?? string.Empty,
            Watermark = "Macro text",
            Background = BgInput,
            Foreground = FgNormal,
            BorderBrush = BdInput,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };

        var rowGrid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("130,*"),
            ColumnSpacing = 10,
        };
        rowGrid.Children.Add(hotkeyCombo);
        Grid.SetColumn(macroTextBox, 1);
        rowGrid.Children.Add(macroTextBox);

        var border = new Border
        {
            BorderBrush = BdInput,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(8),
            Child = rowGrid,
        };

        var state = new MacroRowState
        {
            Border = border,
            HotkeyCombo = hotkeyCombo,
            MacroTextBox = macroTextBox,
        };

        border.PointerPressed += (_, _) => SelectRow(state);
        hotkeyCombo.GotFocus += (_, _) => SelectRow(state);
        macroTextBox.GotFocus += (_, _) => SelectRow(state);
        hotkeyCombo.SelectionChanged += (_, _) => ClearError();
        macroTextBox.GetObservable(TextBox.TextProperty).Subscribe(_ => ClearError());

        _rows.Add(state);
        _rowsPanel.Children.Add(border);
        SelectRow(state);
    }

    private void RemoveSelectedRow()
    {
        MacroRowState? target = _selectedRow ?? _rows.LastOrDefault();
        if (target == null)
            return;

        int index = _rows.IndexOf(target);
        if (index < 0)
            return;

        _rows.RemoveAt(index);
        _rowsPanel.Children.Remove(target.Border);

        if (_rows.Count == 0)
        {
            _selectedRow = null;
            return;
        }

        SelectRow(_rows[Math.Min(index, _rows.Count - 1)]);
    }

    private void SelectRow(MacroRowState state)
    {
        _selectedRow = state;
        foreach (MacroRowState row in _rows)
        {
            bool selected = ReferenceEquals(row, state);
            row.Border.BorderBrush = selected ? AccentBorder : BdInput;
            row.Border.BorderThickness = selected ? new Thickness(2) : new Thickness(1);
        }
    }

    private bool TryBuildResult(out IReadOnlyList<AppPreferences.MacroBinding> bindings, out string? error)
    {
        var result = new List<AppPreferences.MacroBinding>();
        var seenHotkeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (MacroRowState row in _rows)
        {
            string macro = row.MacroTextBox.Text ?? string.Empty;
            if (string.IsNullOrWhiteSpace(macro))
                continue;

            string hotkey = NormalizeHotkey(row.HotkeyCombo.SelectedItem as string);
            if (!seenHotkeys.Add(hotkey))
            {
                bindings = Array.Empty<AppPreferences.MacroBinding>();
                error = $"Hotkey {hotkey} is assigned more than once.";
                return false;
            }

            result.Add(new AppPreferences.MacroBinding
            {
                Hotkey = hotkey,
                Macro = macro,
            });
        }

        bindings = result;
        error = null;
        return true;
    }

    private string GetNextAvailableHotkey()
    {
        HashSet<string> usedHotkeys = _rows
            .Select(row => NormalizeHotkey(row.HotkeyCombo.SelectedItem as string))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return TerminalControl.SupportedMacroHotkeys.FirstOrDefault(hotkey => !usedHotkeys.Contains(hotkey)) ?? "F1";
    }

    private void ShowError(string message)
    {
        _errorText.Text = message;
        _errorText.IsVisible = true;
    }

    private void ClearError()
    {
        _errorText.Text = string.Empty;
        _errorText.IsVisible = false;
    }

    private static string NormalizeHotkey(string? hotkey)
    {
        string candidate = string.IsNullOrWhiteSpace(hotkey) ? "F1" : hotkey.Trim().ToUpperInvariant();
        return TerminalControl.SupportedMacroHotkeys.Contains(candidate, StringComparer.OrdinalIgnoreCase)
            ? candidate
            : "F1";
    }
}
