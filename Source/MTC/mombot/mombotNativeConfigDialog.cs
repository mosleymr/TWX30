using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace MTC.mombot;

internal sealed class mombotNativeConfigDialog : Window
{
    private static readonly IBrush BgWindow = new SolidColorBrush(Color.FromRgb(30, 30, 30));
    private static readonly IBrush BgHeader = Brushes.Black;
    private static readonly IBrush BgPanel = new SolidColorBrush(Color.FromRgb(30, 30, 30));
    private static readonly IBrush BgInput = new SolidColorBrush(Color.FromRgb(20, 20, 20));
    private static readonly IBrush BgButton = new SolidColorBrush(Color.FromRgb(55, 55, 55));
    private static readonly IBrush Border = new SolidColorBrush(Color.FromRgb(0xC8, 0xC8, 0xC8));
    private static readonly IBrush InputBorder = new SolidColorBrush(Color.FromRgb(70, 70, 70));
    private static readonly IBrush FgHeaderBody = Brushes.White;
    private static readonly IBrush FgNormal = new SolidColorBrush(Color.FromRgb(170, 170, 170));
    private static readonly IBrush FgLabel = new SolidColorBrush(Color.FromRgb(200, 200, 200));
    private static readonly FontFamily MonoFont = new("Cascadia Code, Menlo, Consolas, Courier New, monospace");

    private const string CreditsText = @"
Created by: The Bounty Hunter, Mind Dagger, Lonestar, and Hammer
Testing by: Misbehavin and DaCreeper

Credits: Oz, Zentock, SupG, Dynarri, Cherokee, Alexio, Xide,
Phx, Rincrast, Voltron, Traitor, Parrothead,
PSI, Elder Prophet, Caretaker, Deign

Native twx3 integration by Shadow

Version: 5.0.0";

    public global::MTC.BotConfigDialogResult? Result { get; private set; }

    public mombotNativeConfigDialog(string title, global::MTC.BotConfigDialogResult defaults)
    {
        Title = title;
        Width = 980;
        Height = 820;
        MinWidth = 920;
        MinHeight = 720;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = BgWindow;

        var txtName = BuildTextBox(defaults.Name, "MomBot");
        var txtDescription = BuildTextBox(defaults.Description, "Built-in native Mombot runtime");
        txtName.IsEnabled = false;
        txtDescription.IsEnabled = false;
        var txtNameVar = BuildTextBox(defaults.NameVar, "BotName");
        var txtCommsVar = BuildTextBox(defaults.CommsVar, "BotComms");
        var txtLoginScript = BuildTextBox(defaults.LoginScript, "disabled");
        var txtTheme = BuildTextBox(defaults.Theme, "7|[MOMBOT]|~D|~G|~B|~C");
        txtTheme.MinWidth = 0;

        var chkAutoStart = new CheckBox
        {
            Content = "Auto Start",
            IsChecked = defaults.AutoStart,
            Foreground = FgNormal,
            VerticalAlignment = VerticalAlignment.Center,
        };

        var btnSave = new Button
        {
            Content = "Save",
            MinWidth = 88,
            Background = BgButton,
            Foreground = FgNormal,
            BorderBrush = InputBorder,
            Margin = new Thickness(0, 0, 10, 0),
        };

        var btnCancel = new Button
        {
            Content = "Cancel",
            MinWidth = 88,
            Background = BgButton,
            Foreground = FgNormal,
            BorderBrush = InputBorder,
        };

        btnSave.Click += (_, _) =>
        {
            Result = new global::MTC.BotConfigDialogResult(
                defaults.Alias,
                defaults.Name,
                defaults.Script,
                defaults.Description,
                chkAutoStart.IsChecked == true,
                txtNameVar.Text?.Trim() ?? string.Empty,
                txtCommsVar.Text?.Trim() ?? string.Empty,
                txtLoginScript.Text?.Trim() ?? string.Empty,
                txtTheme.Text?.Trim() ?? string.Empty);
            Close(true);
        };

        btnCancel.Click += (_, _) => Close(false);

        Control nameCell = BuildFieldCell("Name", txtName);
        nameCell.IsEnabled = false;
        Control descriptionCell = BuildFieldCell("Description", txtDescription);
        descriptionCell.IsEnabled = false;

        var titleImage = BuildHeaderImage();
        titleImage.VerticalAlignment = VerticalAlignment.Top;

        var creditsBlock = BuildViewer(CreditsText.Trim('\r', '\n'), FgHeaderBody, 10.5);
        creditsBlock.TextWrapping = TextWrapping.NoWrap;
        creditsBlock.VerticalAlignment = VerticalAlignment.Top;
        creditsBlock.Margin = new Thickness(10, 2, 0, 0);

        var titleHeader = new StackPanel
        {
            Spacing = 6,
            Children =
            {
                titleImage,
                creditsBlock,
            },
        };

        Content = new ScrollViewer
        {
            Background = BgPanel,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = new StackPanel
            {
                Margin = new Thickness(10),
                Spacing = 0,
                Children =
                {
                    new Border
                    {
                        Background = BgHeader,
                        BorderBrush = Border,
                        BorderThickness = new Thickness(1),
                        Padding = new Thickness(12, 10, 12, 10),
                        Child = titleHeader,
                    },
                    new Border
                    {
                        Background = BgPanel,
                        BorderBrush = InputBorder,
                        BorderThickness = new Thickness(1),
                        Padding = new Thickness(12),
                        Child = new StackPanel
                        {
                            Spacing = 10,
                            Children =
                            {
                                new TextBlock
                                {
                                    Text = "Alias and script path are managed by the native runtime and are shown here for reference only.",
                                    Foreground = FgNormal,
                                    TextWrapping = TextWrapping.Wrap,
                                },
                                BuildPairRow(nameCell, descriptionCell),
                                BuildPairRow(
                                    BuildFieldCell("Name Var", txtNameVar),
                                    BuildFieldCell("Comms Var", txtCommsVar)),
                                BuildPairRow(
                                    BuildFieldCell("Startup", chkAutoStart),
                                    BuildFieldCell("Login Script", txtLoginScript)),
                                BuildFullWidthCell("Theme", txtTheme),
                                new StackPanel
                                {
                                    Orientation = Orientation.Horizontal,
                                    HorizontalAlignment = HorizontalAlignment.Right,
                                    Margin = new Thickness(0, 4, 0, 0),
                                    Children = { btnSave, btnCancel },
                                },
                            },
                        },
                    },
                },
            },
        };

        txtName.AttachedToVisualTree += (_, _) => txtName.Focus();
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                e.Handled = true;
                Close(false);
            }
        };
    }

    private static TextBox BuildTextBox(string? value, string watermark)
    {
        return new TextBox
        {
            Text = value ?? string.Empty,
            Watermark = watermark,
            MinWidth = 200,
            Background = BgInput,
            Foreground = FgNormal,
            BorderBrush = InputBorder,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
    }

    private static TextBlock BuildViewer(string text, IBrush foreground, double fontSize)
    {
        return new TextBlock
        {
            Text = text,
            TextWrapping = TextWrapping.NoWrap,
            FontFamily = MonoFont,
            FontSize = fontSize,
            Foreground = foreground,
        };
    }

    private static Image BuildHeaderImage()
    {
        using var stream = AssetLoader.Open(new Uri("avares://MTC/mombot/mombot.png"));
        var bitmap = new Bitmap(stream);

        return new Image
        {
            Source = bitmap,
            Stretch = Stretch.Uniform,
            HorizontalAlignment = HorizontalAlignment.Left,
            Width = 900,
            MaxWidth = 900,
        };
    }

    private static Control BuildFieldCell(string label, Control editor)
    {
        return new StackPanel
        {
            Spacing = 6,
            Children =
            {
                new TextBlock
                {
                    Text = label,
                    Foreground = FgLabel,
                    FontWeight = FontWeight.SemiBold,
                },
                editor,
            },
        };
    }

    private static Control BuildPairRow(Control left, Control right)
    {
        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,*"),
            ColumnSpacing = 14,
        };

        Grid.SetColumn(left, 0);
        Grid.SetColumn(right, 1);
        grid.Children.Add(left);
        grid.Children.Add(right);
        return grid;
    }

    private static Control BuildFullWidthCell(string label, Control editor)
    {
        return new StackPanel
        {
            Spacing = 6,
            Children =
            {
                new TextBlock
                {
                    Text = label,
                    Foreground = FgLabel,
                    FontWeight = FontWeight.SemiBold,
                },
                editor,
            },
        };
    }
}
