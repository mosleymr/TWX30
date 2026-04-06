using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Core = TWXProxy.Core;

namespace MTC;

public class AdvancedProxySettingsDialog : Window
{
    private sealed class HaggleModeOption
    {
        public string Value { get; }
        public string Label { get; }

        public HaggleModeOption(string value, string label)
        {
            Value = value;
            Label = label;
        }

        public override string ToString() => Label;
    }

    private static readonly IBrush BgPanel  = new SolidColorBrush(Color.FromRgb(30, 30, 30));
    private static readonly IBrush BgInput  = new SolidColorBrush(Color.FromRgb(20, 20, 20));
    private static readonly IBrush BdInput  = new SolidColorBrush(Color.FromRgb(70, 70, 70));
    private static readonly IBrush FgNormal = new SolidColorBrush(Color.FromRgb(170, 170, 170));
    private static readonly IBrush BgButton = new SolidColorBrush(Color.FromRgb(55, 55, 55));

    public string SelectedHaggleMode { get; private set; }

    public AdvancedProxySettingsDialog(string currentHaggleMode)
    {
        Title = "Advanced Proxy Settings";
        Width = 520;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = BgPanel;

        var haggleOptions = new List<HaggleModeOption>
        {
            new(Core.NativeHaggleModes.ClampHeuristic, "Clamp Heuristic"),
            new(Core.NativeHaggleModes.BlendHeuristic, "Blend Heuristic"),
            new(Core.NativeHaggleModes.Baseline, "Baseline"),
        };

        var haggleCombo = new ComboBox
        {
            ItemsSource = haggleOptions,
            SelectedItem = haggleOptions.Find(option =>
                string.Equals(option.Value, Core.NativeHaggleModes.Normalize(currentHaggleMode), StringComparison.OrdinalIgnoreCase)),
            Background = BgInput,
            Foreground = FgNormal,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        if (haggleCombo.SelectedItem == null)
            haggleCombo.SelectedIndex = 0;

        var haggleRow = BuildRow("Haggle Mode:", haggleCombo);

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
            SelectedHaggleMode = (haggleCombo.SelectedItem as HaggleModeOption)?.Value ?? Core.NativeHaggleModes.ClampHeuristic;
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

        SelectedHaggleMode = Core.NativeHaggleModes.Normalize(currentHaggleMode);

        Content = new StackPanel
        {
            Margin = new Thickness(16),
            Spacing = 4,
            Children = { haggleRow, buttons },
        };
    }

    private static StackPanel BuildRow(string label, Control input)
    {
        var lbl = new TextBlock
        {
            Text = label,
            Foreground = new SolidColorBrush(Color.FromRgb(200, 200, 200)),
            Margin = new Thickness(0, 0, 0, 3),
        };

        if (input is ComboBox comboBox)
            comboBox.BorderBrush = BdInput;

        return new StackPanel
        {
            Spacing = 2,
            Margin = new Thickness(0, 4, 0, 4),
            Children = { lbl, input },
        };
    }
}
