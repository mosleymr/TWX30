using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace MTC.mombot;

internal enum mombotRelogLoginType
{
    NormalRelog,
    ReturnAfterDestroyed,
    NewGameAccountCreation,
}

internal sealed record mombotRelogDialogResult(
    mombotRelogLoginType LoginType,
    string BotName,
    string ServerName,
    string LoginName,
    string Password,
    string GameLetter,
    int DelayMinutes,
    string AfterLoginAction,
    string BotCommand,
    string MacroAfterLogin);

internal sealed class mombotRelogDialog : Window
{
    private sealed class LoginTypeOption
    {
        public mombotRelogLoginType Value { get; }
        public string Label { get; }

        public LoginTypeOption(mombotRelogLoginType value, string label)
        {
            Value = value;
            Label = label;
        }

        public override string ToString() => Label;
    }

    private sealed class AfterLoginOption
    {
        public string Value { get; }
        public string Label { get; }

        public AfterLoginOption(string value, string label)
        {
            Value = value;
            Label = label;
        }

        public override string ToString() => Label;
    }

    private static readonly IBrush BgPanel = new SolidColorBrush(Color.FromRgb(30, 30, 30));
    private static readonly IBrush BgInput = new SolidColorBrush(Color.FromRgb(20, 20, 20));
    private static readonly IBrush BdInput = new SolidColorBrush(Color.FromRgb(70, 70, 70));
    private static readonly IBrush FgNormal = new SolidColorBrush(Color.FromRgb(170, 170, 170));
    private static readonly IBrush FgLabel = new SolidColorBrush(Color.FromRgb(200, 200, 200));
    private static readonly IBrush BgButton = new SolidColorBrush(Color.FromRgb(55, 55, 55));

    public mombotRelogDialogResult? Result { get; private set; }

    public mombotRelogDialog(mombotRelogDialogResult defaults)
    {
        Title = "Mombot Configuration";
        Width = 560;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = BgPanel;

        var loginTypeOptions = new[]
        {
            new LoginTypeOption(mombotRelogLoginType.NormalRelog, "Normal Relog"),
            new LoginTypeOption(mombotRelogLoginType.ReturnAfterDestroyed, "Return after being destroyed"),
            new LoginTypeOption(mombotRelogLoginType.NewGameAccountCreation, "New Game Account Creation"),
        };

        var afterLoginOptions = new[]
        {
            new AfterLoginOption("nothing", "Nothing"),
        };

        var cboLoginType = new ComboBox
        {
            ItemsSource = loginTypeOptions,
            SelectedItem = Array.Find(loginTypeOptions, option => option.Value == defaults.LoginType) ?? loginTypeOptions[0],
            Background = BgInput,
            Foreground = FgNormal,
            BorderBrush = BdInput,
        };

        var txtBotName = BuildTextBox(defaults.BotName, "bot name");
        var txtServerName = BuildTextBox(defaults.ServerName, "server name");
        var txtLoginName = BuildTextBox(defaults.LoginName, "login name");
        var txtPassword = BuildTextBox(defaults.Password, "password");
        var txtGameLetter = BuildTextBox(defaults.GameLetter, "game letter");
        txtGameLetter.MaxLength = 1;
        txtGameLetter.Width = 80;

        var txtDelay = BuildTextBox(defaults.DelayMinutes.ToString(), "0");
        txtDelay.Width = 100;

        var cboAfterLogin = new ComboBox
        {
            ItemsSource = afterLoginOptions,
            SelectedItem = Array.Find(afterLoginOptions, option =>
                string.Equals(option.Value, defaults.AfterLoginAction, StringComparison.OrdinalIgnoreCase)) ?? afterLoginOptions[0],
            Background = BgInput,
            Foreground = FgNormal,
            BorderBrush = BdInput,
        };

        var txtBotCommand = BuildTextBox(defaults.BotCommand, "None");
        var txtMacro = BuildTextBox(defaults.MacroAfterLogin, "None");

        var content = new StackPanel
        {
            Margin = new Thickness(16),
            Spacing = 4,
            Children =
            {
                new TextBlock
                {
                    Text = "Configure login details for this game. These settings are saved per game and used to start native Mombot when the embedded proxy is offline.",
                    TextWrapping = TextWrapping.Wrap,
                    Foreground = FgNormal,
                    Margin = new Thickness(0, 0, 0, 8),
                },
                BuildRow("Type of login:", cboLoginType),
                BuildRow("Bot Name:", txtBotName),
                BuildRow("Server Login:", txtServerName),
                BuildRow("Login Name:", txtLoginName),
                BuildRow("Password:", txtPassword),
                BuildRow("Game Letter:", txtGameLetter),
                BuildRow("Delay (Minutes):", txtDelay),
                BuildRow("After login:", cboAfterLogin),
                BuildRow("Bot command to perform:", txtBotCommand),
                BuildRow("Macro to fire after login:", txtMacro),
            }
        };

        var btnStart = new Button
        {
            Content = "Save and Start",
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

        btnStart.Click += (_, _) =>
        {
            string botName = (txtBotName.Text ?? string.Empty).Trim();
            string serverName = (txtServerName.Text ?? string.Empty).Trim();
            string loginName = (txtLoginName.Text ?? string.Empty).Trim();
            string password = txtPassword.Text ?? string.Empty;
            string gameLetter = NormalizeGameLetter(txtGameLetter.Text);
            int delayMinutes = int.TryParse(txtDelay.Text?.Trim(), out int parsedDelay) && parsedDelay >= 0
                ? parsedDelay
                : 0;

            if (string.IsNullOrWhiteSpace(botName))
            {
                txtBotName.Focus();
                return;
            }

            if (string.IsNullOrWhiteSpace(serverName))
            {
                txtServerName.Focus();
                return;
            }

            if (string.IsNullOrWhiteSpace(loginName))
            {
                txtLoginName.Focus();
                return;
            }

            Result = new mombotRelogDialogResult(
                (cboLoginType.SelectedItem as LoginTypeOption)?.Value ?? mombotRelogLoginType.NormalRelog,
                botName,
                serverName,
                loginName,
                password,
                gameLetter,
                delayMinutes,
                (cboAfterLogin.SelectedItem as AfterLoginOption)?.Value ?? "nothing",
                NormalizeFreeform(txtBotCommand.Text),
                NormalizeFreeform(txtMacro.Text));
            Close(true);
        };

        btnCancel.Click += (_, _) => Close(false);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 12, 0, 0),
            Children = { btnStart, btnCancel },
        };

        content.Children.Add(buttons);
        Content = content;
    }

    private static TextBox BuildTextBox(string? text, string watermark)
    {
        return new TextBox
        {
            Text = text ?? string.Empty,
            Watermark = watermark,
            Background = BgInput,
            Foreground = FgNormal,
            BorderBrush = BdInput,
        };
    }

    private static StackPanel BuildRow(string label, Control input)
    {
        var lbl = new TextBlock
        {
            Text = label,
            Foreground = FgLabel,
            Margin = new Thickness(0, 0, 0, 3),
        };

        return new StackPanel
        {
            Spacing = 2,
            Margin = new Thickness(0, 4, 0, 4),
            Children = { lbl, input },
        };
    }

    private static string NormalizeGameLetter(string? value)
    {
        string trimmed = (value ?? string.Empty).Trim();
        return string.IsNullOrWhiteSpace(trimmed)
            ? string.Empty
            : trimmed[..1].ToUpperInvariant();
    }

    private static string NormalizeFreeform(string? value)
    {
        string trimmed = (value ?? string.Empty).Trim();
        return string.Equals(trimmed, "none", StringComparison.OrdinalIgnoreCase) ? string.Empty : trimmed;
    }
}
