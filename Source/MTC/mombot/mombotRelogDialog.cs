using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
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

    private static readonly IBrush BgWindow = new SolidColorBrush(Color.FromRgb(0x03, 0x0F, 0x17));
    private static readonly IBrush BgPanel = new SolidColorBrush(Color.FromRgb(0x06, 0x37, 0x41));
    private static readonly IBrush BgInput = new SolidColorBrush(Color.FromRgb(0x02, 0x16, 0x20));
    private static readonly IBrush BgButton = new SolidColorBrush(Color.FromRgb(0x04, 0x4A, 0x56));
    private static readonly IBrush BgPrimaryButton = new SolidColorBrush(Color.FromRgb(0x00, 0xD8, 0xCB));
    private static readonly IBrush Border = new SolidColorBrush(Color.FromRgb(0x08, 0x91, 0xA4));
    private static readonly IBrush InputBorder = new SolidColorBrush(Color.FromRgb(0x0B, 0x79, 0x8B));
    private static readonly IBrush FgHeader = new SolidColorBrush(Color.FromRgb(0x00, 0xF2, 0xE7));
    private static readonly IBrush FgNormal = new SolidColorBrush(Color.FromRgb(0xD7, 0xF3, 0xF6));
    private static readonly IBrush FgMuted = new SolidColorBrush(Color.FromRgb(0x8D, 0xC1, 0xC8));
    private static readonly IBrush FgDark = new SolidColorBrush(Color.FromRgb(0x01, 0x12, 0x18));
    private static readonly FontFamily MonoFont = new("Cascadia Code, Menlo, Consolas, Courier New, monospace");

    public mombotRelogDialogResult? Result { get; private set; }

    public mombotRelogDialog(mombotRelogDialogResult defaults)
    {
        Title = "Native MomBot Login";
        Width = 620;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = BgWindow;

        var loginTypeOptions = new[]
        {
            new LoginTypeOption(mombotRelogLoginType.NewGameAccountCreation, "New Game Account Creation"),
            new LoginTypeOption(mombotRelogLoginType.NormalRelog, "Normal Relog"),
            new LoginTypeOption(mombotRelogLoginType.ReturnAfterDestroyed, "Return after being destroyed"),
        };

        var afterLoginOptions = new[]
        {
            new AfterLoginOption("nothing", "Nothing"),
            new AfterLoginOption("command", "Run Command"),
            new AfterLoginOption("macro", "Fire Macro"),
            new AfterLoginOption("terra", "Land on Terra"),
        };

        string defaultLoginName = NormalizeFreeform(defaults.LoginName);
        string defaultServerName = defaultLoginName;
        string defaultGameLetter = NormalizeGameLetter(defaults.GameLetter);

        var txtLoginName = BuildTextBox(defaultLoginName, "login name");
        var txtPassword = BuildTextBox(defaults.Password, "password");
        txtPassword.PasswordChar = '*';
        var txtBotName = BuildTextBox(defaults.BotName, "mombot");
        txtBotName.Width = 110;
        txtBotName.HorizontalAlignment = HorizontalAlignment.Left;
        var txtServerName = BuildTextBox(defaultServerName, "server name");
        var txtDelay = BuildTextBox(defaults.DelayMinutes.ToString(), "0");
        txtDelay.Width = 110;
        txtDelay.HorizontalAlignment = HorizontalAlignment.Left;

        string[] gameLetters = Enumerable.Range('A', 26)
            .Select(value => ((char)value).ToString())
            .ToArray();
        var cboGameLetter = BuildComboBox(gameLetters);
        cboGameLetter.Width = 110;
        cboGameLetter.HorizontalAlignment = HorizontalAlignment.Left;
        cboGameLetter.SelectedItem = gameLetters.FirstOrDefault(letter =>
            string.Equals(letter, defaultGameLetter, StringComparison.OrdinalIgnoreCase));

        var cboLoginType = BuildComboBox(loginTypeOptions);
        cboLoginType.SelectedItem = loginTypeOptions.FirstOrDefault(option => option.Value == defaults.LoginType) ?? loginTypeOptions[0];

        var cboAfterLogin = BuildComboBox(afterLoginOptions);
        cboAfterLogin.SelectedItem = afterLoginOptions.FirstOrDefault(option =>
            string.Equals(option.Value, defaults.AfterLoginAction, StringComparison.OrdinalIgnoreCase)) ?? afterLoginOptions[0];

        var txtBotCommand = BuildTextBox(defaults.BotCommand, "bot command");
        var txtMacro = BuildTextBox(defaults.MacroAfterLogin, "macro");
        Control commandCell = BuildFullWidthCell("Bot command to perform", txtBotCommand);
        Control macroCell = BuildFullWidthCell("Macro to fire", txtMacro);

        bool syncingServerName = false;
        bool serverNameEdited = false;

        txtLoginName.TextChanged += (_, _) =>
        {
            if (serverNameEdited)
                return;

            syncingServerName = true;
            txtServerName.Text = txtLoginName.Text ?? string.Empty;
            syncingServerName = false;
        };

        txtServerName.TextChanged += (_, _) =>
        {
            if (syncingServerName)
                return;

            if (txtServerName.IsKeyboardFocusWithin)
                serverNameEdited = true;
        };

        void RefreshAfterLoginFields()
        {
            string selectedAction = (cboAfterLogin.SelectedItem as AfterLoginOption)?.Value ?? "nothing";
            commandCell.IsVisible = string.Equals(selectedAction, "command", StringComparison.OrdinalIgnoreCase);
            macroCell.IsVisible = string.Equals(selectedAction, "macro", StringComparison.OrdinalIgnoreCase);
        }

        cboAfterLogin.SelectionChanged += (_, _) => RefreshAfterLoginFields();
        RefreshAfterLoginFields();

        var btnSave = BuildButton("Save", primary: true);
        var btnCancel = BuildButton("Cancel", primary: false);

        btnSave.Click += (_, _) =>
        {
            string botName = NormalizeFreeform(txtBotName.Text);
            string serverName = NormalizeFreeform(txtServerName.Text);
            string loginName = NormalizeFreeform(txtLoginName.Text);
            string password = txtPassword.Text ?? string.Empty;
            string gameLetter = NormalizeGameLetter(cboGameLetter.SelectedItem as string);
            int delayMinutes = int.TryParse(txtDelay.Text?.Trim(), out int parsedDelay) && parsedDelay >= 0
                ? parsedDelay
                : 0;
            string afterLoginAction = (cboAfterLogin.SelectedItem as AfterLoginOption)?.Value ?? "nothing";

            if (string.IsNullOrWhiteSpace(loginName))
            {
                txtLoginName.Focus();
                return;
            }

            if (string.IsNullOrWhiteSpace(password))
            {
                txtPassword.Focus();
                return;
            }

            if (string.IsNullOrWhiteSpace(gameLetter))
            {
                cboGameLetter.Focus();
                return;
            }

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

            Result = new mombotRelogDialogResult(
                (cboLoginType.SelectedItem as LoginTypeOption)?.Value ?? mombotRelogLoginType.NewGameAccountCreation,
                botName,
                serverName,
                loginName,
                password,
                gameLetter,
                delayMinutes,
                afterLoginAction,
                string.Equals(afterLoginAction, "command", StringComparison.OrdinalIgnoreCase)
                    ? NormalizeFreeform(txtBotCommand.Text)
                    : string.Empty,
                string.Equals(afterLoginAction, "terra", StringComparison.OrdinalIgnoreCase)
                    ? "pt"
                    : string.Equals(afterLoginAction, "macro", StringComparison.OrdinalIgnoreCase)
                    ? NormalizeFreeform(txtMacro.Text)
                    : string.Empty);
            Close(true);
        };

        btnCancel.Click += (_, _) => Close(false);

        var buttonRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 10,
            Children = { btnCancel, btnSave },
        };

        Content = new Border
        {
            Background = BgWindow,
            Padding = new Thickness(18),
            Child = new Border
            {
                Background = BgPanel,
                BorderBrush = Border,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(14),
                Padding = new Thickness(18),
                Child = new StackPanel
                {
                    Spacing = 14,
                    Children =
                    {
                        BuildFullWidthCell("Login type", cboLoginType),
                        BuildPairRow(
                            BuildFieldCell("Login name", txtLoginName),
                            BuildFieldCell("Server name", txtServerName)),
                        BuildPairRow(
                            BuildFieldCell("Password", txtPassword),
                            BuildFieldCell("Game letter", cboGameLetter)),
                        BuildPairRow(
                            BuildFieldCell("Bot name", txtBotName),
                            BuildFieldCell("Delay", txtDelay)),
                        BuildFullWidthCell("After login", cboAfterLogin),
                        commandCell,
                        macroCell,
                        buttonRow,
                    },
                },
            },
        };

        txtLoginName.AttachedToVisualTree += (_, _) => txtLoginName.Focus();
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                e.Handled = true;
                Close(false);
            }
        };
    }

    private static TextBox BuildTextBox(string? text, string watermark)
    {
        return new TextBox
        {
            Text = text ?? string.Empty,
            Watermark = watermark,
            MinWidth = 0,
            FontFamily = MonoFont,
            FontSize = 15,
            Background = BgInput,
            Foreground = FgNormal,
            BorderBrush = InputBorder,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
    }

    private static ComboBox BuildComboBox(System.Collections.IEnumerable items)
    {
        return new ComboBox
        {
            ItemsSource = items,
            MinWidth = 0,
            FontSize = 15,
            Background = BgInput,
            Foreground = FgNormal,
            BorderBrush = InputBorder,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
    }

    private static Button BuildButton(string label, bool primary)
    {
        return new Button
        {
            Content = label,
            MinWidth = 96,
            Background = primary ? BgPrimaryButton : BgButton,
            Foreground = primary ? FgDark : FgNormal,
            BorderBrush = primary ? BgPrimaryButton : InputBorder,
            HorizontalContentAlignment = HorizontalAlignment.Center,
        };
    }

    private static Control BuildFieldCell(string label, Control input)
    {
        return new StackPanel
        {
            Spacing = 6,
            Children =
            {
                new TextBlock
                {
                    Text = label,
                    Foreground = FgMuted,
                    FontWeight = FontWeight.SemiBold,
                },
                input,
            },
        };
    }

    private static Control BuildFullWidthCell(string label, Control input)
    {
        return new StackPanel
        {
            Spacing = 6,
            Children =
            {
                new TextBlock
                {
                    Text = label,
                    Foreground = FgMuted,
                    FontWeight = FontWeight.SemiBold,
                },
                input,
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
