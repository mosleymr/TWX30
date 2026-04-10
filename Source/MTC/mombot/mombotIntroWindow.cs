using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace MTC.mombot;

internal sealed class mombotIntroWindow : Window
{
    private static readonly IBrush BgWindow = Brushes.White;
    private static readonly IBrush BgButton = new SolidColorBrush(Color.FromRgb(0xF0, 0xF0, 0xF0));
    private static readonly IBrush FgBody = Brushes.Black;
    private static readonly IBrush FgTitle = new SolidColorBrush(Color.FromRgb(0xC4, 0x18, 0x18));
    private static readonly IBrush Border = new SolidColorBrush(Color.FromRgb(0xB8, 0xB8, 0xB8));
    private static readonly FontFamily MonoFont = new("Cascadia Code, Menlo, Consolas, Courier New, monospace");

    private const string TitleArt = @"
 /$$      /$$ /$$                 /$$         /$$$/$$$
| $$$    /$$$|__/                | $$        /$$_/_  $$
| $$$$  /$$$$ /$$ /$$$$$$$   /$$$$$$$       /$$/   \  $$ /$$    /$$/$$$$$$   /$$$$$$
| $$ $$/$$ $$| $$| $$__  $$ /$$__  $$      | $$     | $$|  $$  /$$/$$__  $$ /$$__  $$
| $$  $$$| $$| $$| $$  \ $$| $$  | $$      | $$     | $$ \  $$/$$/ $$$$$$$$| $$  \__/
| $$\  $ | $$| $$| $$  | $$| $$  | $$      |  $$    /$$/  \  $$$/| $$_____/| $$
| $$ \/  | $$| $$| $$  | $$|  $$$$$$$       \  $$$/$$$/    \  $/ |  $$$$$$$| $$
|__/     |__/|__/|__/  |__/ \_______/        \___/___/      \_/   \_______/|__/



       /$$      /$$             /$$     /$$
      | $$$    /$$$            | $$    | $$
      | $$$$  /$$$$  /$$$$$$  /$$$$$$ /$$$$$$    /$$$$$$   /$$$$$$
      | $$ $$/$$ $$ |____  $$|_  $$_/|_  $$_/   /$$__  $$ /$$__  $$
      | $$  $$$| $$  /$$$$$$$  | $$    | $$    | $$$$$$$$| $$  \__/
      | $$\  $ | $$ /$$__  $$  | $$ /$$| $$ /$$| $$_____/| $$
      | $$ \/  | $$|  $$$$$$$  |  $$$$/|  $$$$/|  $$$$$$$| $$
      |__/     |__/ \_______/   \___/   \___/   \_______/|__/



                 /$$$$$$$              /$$
                | $$__  $$            | $$
                | $$  \ $$  /$$$$$$  /$$$$$$
                | $$$$$$$  /$$__  $$|_  $$_/
                | $$__  $$| $$  \ $$  | $$
                | $$  \ $$| $$  | $$  | $$ /$$
                | $$$$$$$/|  $$$$$$/  |  $$$$/
                |_______/  \______/    \___/";

    private const string CreditsText = @"
       Created by: The Bounty Hunter, Mind Dagger, Lonestar, and Hammer
                    Testing by: Misbehavin and DaCreeper


       Credits: Oz, Zentock, SupG, Dynarri, Cherokee, Alexio, Xide,
                Phx, Rincrast, Voltron, Traitor, Parrothead,
                PSI, Elder Prophet, Caretaker, Deign


       Version: 4.7beta";

    public mombotIntroWindow()
    {
        Title = "Mombot Credits";
        Width = 1120;
        Height = 900;
        MinWidth = 900;
        MinHeight = 680;
        Background = BgWindow;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var closeButton = new Button
        {
            Content = "Close",
            MinWidth = 110,
            HorizontalAlignment = HorizontalAlignment.Center,
            Background = BgButton,
            Foreground = FgBody,
            BorderBrush = Border,
        };
        closeButton.Click += (_, _) => Close();

        var artBlock = BuildViewer(TitleArt.Trim('\r', '\n'), FgTitle, 13.5);
        var creditsBlock = BuildViewer(CreditsText.Trim('\r', '\n'), FgBody, 15);

        Content = new ScrollViewer
        {
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            Content = new StackPanel
            {
                Margin = new Thickness(18),
                Spacing = 12,
                Children =
                {
                    new Border
                    {
                        Background = BgWindow,
                        BorderBrush = Border,
                        BorderThickness = new Thickness(1),
                        Padding = new Thickness(14),
                        Child = artBlock,
                    },
                    new Border
                    {
                        Background = BgWindow,
                        BorderBrush = Border,
                        BorderThickness = new Thickness(1),
                        Padding = new Thickness(14),
                        Child = creditsBlock,
                    },
                    closeButton,
                },
            },
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
}
