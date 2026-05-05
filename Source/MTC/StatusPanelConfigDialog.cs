using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace MTC;

internal sealed class StatusPanelConfigDialogResult
{
    public List<AppPreferences.StatusPanelSectionPreference> Sections { get; init; } = [];
}

internal sealed class StatusPanelConfigDialog : Window
{
    private static readonly IBrush BgPanel = new SolidColorBrush(Color.FromRgb(9, 36, 44));
    private static readonly IBrush BgSection = new SolidColorBrush(Color.FromRgb(14, 55, 66));
    private static readonly IBrush BgInput = new SolidColorBrush(Color.FromRgb(7, 28, 36));
    private static readonly IBrush BdInput = new SolidColorBrush(Color.FromRgb(0, 116, 138));
    private static readonly IBrush FgText = new SolidColorBrush(Color.FromRgb(222, 238, 242));
    private static readonly IBrush FgMuted = new SolidColorBrush(Color.FromRgb(150, 191, 199));
    private static readonly IBrush Accent = new SolidColorBrush(Color.FromRgb(0, 212, 201));

    private sealed class SectionRow
    {
        public required AppPreferences.StatusPanelSectionPreference Section { get; init; }
        public required CheckBox VisibleCheckBox { get; init; }
    }

    private readonly List<AppPreferences.StatusPanelSectionPreference> _sections;
    private readonly List<SectionRow> _rows = [];
    private readonly StackPanel _rowsHost = new() { Spacing = 8 };

    public StatusPanelConfigDialogResult? Result { get; private set; }

    public StatusPanelConfigDialog(IReadOnlyList<AppPreferences.StatusPanelSectionPreference> sections)
    {
        Title = "Configure Status Panel";
        Width = 620;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = BgPanel;

        _sections = (sections ?? [])
            .Select(static section => new AppPreferences.StatusPanelSectionPreference
            {
                Id = section.Id,
                Visible = section.Visible,
                Order = section.Order,
            })
            .ToList();

        RebuildRows();

        var btnSave = BuildActionButton("Save", Accent);
        btnSave.Click += (_, _) =>
        {
            Result = new StatusPanelConfigDialogResult
            {
                Sections = _rows
                    .Select((row, index) => new AppPreferences.StatusPanelSectionPreference
                    {
                        Id = row.Section.Id,
                        Visible = row.VisibleCheckBox.IsChecked == true,
                        Order = index,
                    })
                    .ToList(),
            };
            Close(true);
        };

        var btnCancel = BuildActionButton("Cancel", BgInput);
        btnCancel.Click += (_, _) => Close(false);

        Content = new Border
        {
            Padding = new Thickness(18),
            Child = new StackPanel
            {
                Spacing = 14,
                Children =
                {
                    new Border
                    {
                        Background = BgSection,
                        BorderBrush = BdInput,
                        BorderThickness = new Thickness(1),
                        CornerRadius = new CornerRadius(10),
                        Padding = new Thickness(14),
                        Child = new StackPanel
                        {
                            Spacing = 10,
                            Children =
                            {
                                new TextBlock
                                {
                                    Text = "Classic status panel layout",
                                    Foreground = Accent,
                                    FontSize = 17,
                                    FontWeight = FontWeight.SemiBold,
                                },
                                new TextBlock
                                {
                                    Text = "Show or hide the left-side Trader, Online, and Ship Info sections, then move them up or down to change their order.",
                                    Foreground = FgMuted,
                                    TextWrapping = TextWrapping.Wrap,
                                },
                                _rowsHost,
                            },
                        },
                    },
                    new TextBlock
                    {
                        Text = "This currently affects the classic sidebar layout. Command Deck stays unchanged for now.",
                        Foreground = FgMuted,
                        TextWrapping = TextWrapping.Wrap,
                    },
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        HorizontalAlignment = HorizontalAlignment.Right,
                        Spacing = 10,
                        Children = { btnSave, btnCancel },
                    },
                },
            },
        };
    }

    private void RebuildRows()
    {
        _rows.Clear();
        _rowsHost.Children.Clear();

        for (int index = 0; index < _sections.Count; index++)
        {
            AppPreferences.StatusPanelSectionPreference section = _sections[index];
            int currentIndex = index;

            var checkBox = new CheckBox
            {
                Content = AppPreferences.GetStatusPanelSectionLabel(section.Id),
                IsChecked = section.Visible,
                Foreground = FgText,
                VerticalAlignment = VerticalAlignment.Center,
            };

            var upButton = BuildActionButton("Up", BgInput);
            upButton.MinWidth = 74;
            upButton.IsEnabled = currentIndex > 0;
            upButton.Click += (_, _) =>
            {
                MoveSection(currentIndex, currentIndex - 1);
            };

            var downButton = BuildActionButton("Down", BgInput);
            downButton.MinWidth = 74;
            downButton.IsEnabled = currentIndex < _sections.Count - 1;
            downButton.Click += (_, _) =>
            {
                MoveSection(currentIndex, currentIndex + 1);
            };

            var row = new Grid
            {
                ColumnDefinitions =
                {
                    new ColumnDefinition(GridLength.Star),
                    new ColumnDefinition(GridLength.Auto),
                    new ColumnDefinition(GridLength.Auto),
                },
                ColumnSpacing = 10,
            };

            Grid.SetColumn(checkBox, 0);
            Grid.SetColumn(upButton, 1);
            Grid.SetColumn(downButton, 2);
            row.Children.Add(checkBox);
            row.Children.Add(upButton);
            row.Children.Add(downButton);

            _rows.Add(new SectionRow
            {
                Section = section,
                VisibleCheckBox = checkBox,
            });

            _rowsHost.Children.Add(new Border
            {
                Background = BgInput,
                BorderBrush = BdInput,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(12, 10),
                Child = row,
            });
        }
    }

    private void MoveSection(int fromIndex, int toIndex)
    {
        if (fromIndex < 0 || fromIndex >= _sections.Count || toIndex < 0 || toIndex >= _sections.Count || fromIndex == toIndex)
            return;

        CaptureVisibleState();
        AppPreferences.StatusPanelSectionPreference section = _sections[fromIndex];
        _sections.RemoveAt(fromIndex);
        _sections.Insert(toIndex, section);
        RebuildRows();
    }

    private void CaptureVisibleState()
    {
        for (int index = 0; index < _rows.Count; index++)
            _sections[index].Visible = _rows[index].VisibleCheckBox.IsChecked == true;
    }

    private static Button BuildActionButton(string text, IBrush background)
    {
        return new Button
        {
            Content = text,
            MinWidth = 100,
            Padding = new Thickness(14, 8),
            Background = background,
            Foreground = FgText,
            BorderBrush = BdInput,
            BorderThickness = new Thickness(1),
        };
    }
}
