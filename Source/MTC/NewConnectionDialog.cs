using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace MTC;

/// <summary>
/// Dialog for creating or editing a connection profile.
/// Usage: <c>var ok = await new NewConnectionDialog(profile).ShowDialog&lt;bool&gt;(owner);</c>
/// If <c>ok</c> is true, <see cref="Result"/> contains the validated profile.
/// </summary>
public class NewConnectionDialog : Window
{
    /// <summary>Set when the user clicks OK. Contains the validated connection settings.</summary>
    public ConnectionProfile? Result { get; private set; }

    // ── Colors (match MainWindow dark chrome) ─────────────────────────────
    private static readonly IBrush BgPanel  = new SolidColorBrush(Color.FromRgb(30,  30,  30));
    private static readonly IBrush BgInput  = new SolidColorBrush(Color.FromRgb(20,  20,  20));
    private static readonly IBrush BdInput  = new SolidColorBrush(Color.FromRgb(70,  70,  70));
    private static readonly IBrush FgNormal = new SolidColorBrush(Color.FromRgb(170, 170, 170));
    private static readonly IBrush FgLabel  = new SolidColorBrush(Color.FromRgb(200, 200, 200));

    public NewConnectionDialog(ConnectionProfile? defaults = null)
    {
        Title                 = "Connection Settings";
        Width                 = 500;
        SizeToContent         = SizeToContent.Height;
        MinHeight             = 200;
        CanResize             = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background            = BgPanel;

        var profile = defaults ?? new ConnectionProfile();

        // ── Input fields ──────────────────────────────────────────────────
        var txtServer = new TextBox
        {
            Text        = profile.Server,
            Watermark   = "hostname or IP address",
            Background  = BgInput,
            Foreground  = FgNormal,
            BorderBrush = BdInput,
        };

        var txtPort = new TextBox
        {
            Text        = profile.Port.ToString(),
            Width       = 80,
            Background  = BgInput,
            Foreground  = FgNormal,
            BorderBrush = BdInput,
        };

        var cboProtocol = new ComboBox
        {
            ItemsSource   = new[] { "Telnet", "Rlogin" },
            SelectedIndex = profile.Protocol == TwProtocol.Rlogin ? 1 : 0,
            MinWidth      = 100,
        };

        // ── OK / Cancel ────────────────────────────────────────────────────
        var chkEmbedded = new CheckBox
        {
            Content   = "Run embedded proxy (enables .ts/.cts scripts)",
            IsChecked = profile.EmbeddedProxy,
            Foreground = FgNormal,
        };
        var chkAutoReconnect = new CheckBox
        {
            Content   = "Auto-reconnect on disconnect",
            IsChecked = profile.AutoReconnect,
            Foreground = FgNormal,
        };
        var txtSectors = new TextBox
        {
            Text        = profile.Sectors.ToString(),
            Width       = 80,
            Background  = BgInput,
            Foreground  = FgNormal,
            BorderBrush = BdInput,
        };
        var chkUseLogin = new CheckBox
        {
            Content = "Run login script after connect",
            IsChecked = profile.UseLogin,
            Foreground = FgNormal,
        };
        var chkUseRLogin = new CheckBox
        {
            Content = "Use RLogin handshake",
            IsChecked = profile.UseRLogin,
            Foreground = FgNormal,
        };
        var txtLoginScript = new TextBox
        {
            Text = string.IsNullOrWhiteSpace(profile.LoginScript) ? "0_Login.cts" : profile.LoginScript,
            Background = BgInput,
            Foreground = FgNormal,
            BorderBrush = BdInput,
        };
        var txtLoginName = new TextBox
        {
            Text = profile.LoginName,
            Background = BgInput,
            Foreground = FgNormal,
            BorderBrush = BdInput,
        };
        var txtPassword = new TextBox
        {
            Text = profile.Password,
            Background = BgInput,
            Foreground = FgNormal,
            BorderBrush = BdInput,
        };
        var txtGameLetter = new TextBox
        {
            Text = profile.GameLetter,
            Width = 80,
            Background = BgInput,
            Foreground = FgNormal,
            BorderBrush = BdInput,
        };
        var sectorsRow = BuildRow("Sectors:", txtSectors);
        var loginScriptRow = BuildRow("Login script:", txtLoginScript);
        var loginNameRow = BuildRow("Username:", txtLoginName);
        var passwordRow = BuildRow("Password:", txtPassword);
        var gameLetterRow = BuildRow("Game letter:", txtGameLetter);
        sectorsRow.IsVisible     = profile.EmbeddedProxy;   // only relevant for embedded proxy
        chkAutoReconnect.IsVisible = profile.EmbeddedProxy;
        chkUseLogin.IsVisible = profile.EmbeddedProxy;
        chkUseRLogin.IsVisible = profile.EmbeddedProxy;

        void RefreshEmbeddedLoginVisibility()
        {
            bool embedded = chkEmbedded.IsChecked == true;
            bool showDetails = embedded && (chkUseLogin.IsChecked == true || chkUseRLogin.IsChecked == true);
            sectorsRow.IsVisible = embedded;
            chkAutoReconnect.IsVisible = embedded;
            chkUseLogin.IsVisible = embedded;
            chkUseRLogin.IsVisible = embedded;
            loginScriptRow.IsVisible = showDetails;
            loginNameRow.IsVisible = showDetails;
            passwordRow.IsVisible = showDetails;
            gameLetterRow.IsVisible = showDetails;
        }

        chkEmbedded.IsCheckedChanged += (_, _) =>
        {
            RefreshEmbeddedLoginVisibility();
        };
        chkUseLogin.IsCheckedChanged += (_, _) => RefreshEmbeddedLoginVisibility();
        chkUseRLogin.IsCheckedChanged += (_, _) => RefreshEmbeddedLoginVisibility();
        RefreshEmbeddedLoginVisibility();
        var btnOk = new Button
        {
            Content                    = "OK",
            HorizontalAlignment        = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Center,
        };
        var btnCancel = new Button
        {
            Content                    = "Cancel",
            HorizontalAlignment        = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Center,
        };

        btnOk.Click += (_, _) =>
        {
            string server = txtServer.Text?.Trim() ?? string.Empty;
            if (string.IsNullOrEmpty(server))
            {
                txtServer.Focus();
                return;
            }
            if (!int.TryParse(txtPort.Text?.Trim(), out int portVal) || portVal is < 1 or > 65535)
                portVal = 2002;

            if (!int.TryParse(txtSectors.Text?.Trim(), out int sectors) || sectors < 100)
                sectors = 1000;

            Result = new ConnectionProfile
            {
                Server          = server,
                Port            = portVal,
                Protocol        = cboProtocol.SelectedIndex == 1 ? TwProtocol.Rlogin : TwProtocol.Telnet,
                EmbeddedProxy   = chkEmbedded.IsChecked == true,
                AutoReconnect   = chkAutoReconnect.IsChecked == true,
                Sectors         = sectors,
                UseLogin        = chkUseLogin.IsChecked == true,
                UseRLogin       = chkUseRLogin.IsChecked == true,
                LoginScript     = string.IsNullOrWhiteSpace(txtLoginScript.Text) ? "0_Login.cts" : txtLoginScript.Text.Trim(),
                LoginName       = txtLoginName.Text?.Trim() ?? string.Empty,
                Password        = txtPassword.Text ?? string.Empty,
                GameLetter      = string.IsNullOrWhiteSpace(txtGameLetter.Text)
                    ? string.Empty
                    : txtGameLetter.Text.Trim().Substring(0, 1).ToUpperInvariant(),
                LoginSettingsConfigured = chkEmbedded.IsChecked == true,
                // Preserve scrollback setting from the profile being edited
                ScrollbackLines = profile.ScrollbackLines,
            };
            Close(true);
        };

        btnCancel.Click += (_, _) => Close(false);

        var btnRow = new Grid { Margin = new Thickness(0, 12, 0, 0) };
        btnRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        btnRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
        btnRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetColumn(btnOk,     0);
        Grid.SetColumn(btnCancel, 2);
        btnRow.Children.Add(btnOk);
        btnRow.Children.Add(btnCancel);

        // ── Assemble layout ────────────────────────────────────────────────
        Content = new StackPanel
        {
            Margin   = new Thickness(24, 20, 24, 20),
            Spacing  = 8,
            Children =
            {
                BuildRow("Server:",         txtServer),
                BuildRow("Port:",           txtPort),
                BuildRow("Protocol:",       cboProtocol),
                chkEmbedded,
                chkAutoReconnect,
                sectorsRow,
                chkUseLogin,
                chkUseRLogin,
                loginScriptRow,
                loginNameRow,
                passwordRow,
                gameLetterRow,
                btnRow,
            },
        };
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    private static Grid BuildRow(string labelText, Control input)
    {
        const double LabelWidth = 110;
        var grid = new Grid { Margin = new Thickness(0, 2, 0, 2) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(LabelWidth) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var lbl = new TextBlock
        {
            Text              = labelText,
            Foreground        = FgLabel,
            VerticalAlignment = VerticalAlignment.Center,
            Margin            = new Thickness(0, 0, 8, 0),
        };

        Grid.SetColumn(lbl,   0);
        Grid.SetColumn(input, 1);
        grid.Children.Add(lbl);
        grid.Children.Add(input);
        return grid;
    }
}
