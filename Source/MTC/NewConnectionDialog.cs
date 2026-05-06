using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
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

    // Deck-style colors used by the left sidebar, map, cache, and status dialogs.
    private static readonly IBrush BgWin = new SolidColorBrush(Color.FromRgb(8, 14, 20));
    private static readonly IBrush BgPanel = new SolidColorBrush(Color.FromRgb(14, 33, 42));
    private static readonly IBrush BgCard = new SolidColorBrush(Color.FromRgb(16, 53, 67));
    private static readonly IBrush BgCardAlt = new SolidColorBrush(Color.FromRgb(10, 43, 53));
    private static readonly IBrush BgInput = new SolidColorBrush(Color.FromRgb(7, 28, 36));
    private static readonly IBrush Edge = new SolidColorBrush(Color.FromRgb(57, 112, 128));
    private static readonly IBrush InnerEdge = new SolidColorBrush(Color.FromRgb(23, 81, 94));
    private static readonly IBrush FgText = new SolidColorBrush(Color.FromRgb(222, 238, 242));
    private static readonly IBrush FgMuted = new SolidColorBrush(Color.FromRgb(126, 170, 180));
    private static readonly IBrush Accent = new SolidColorBrush(Color.FromRgb(0, 212, 201));
    private static readonly IBrush AccentHot = new SolidColorBrush(Color.FromRgb(255, 193, 74));
    private static readonly IBrush AccentInk = new SolidColorBrush(Color.FromRgb(8, 26, 30));
    private static readonly IBrush ErrorText = new SolidColorBrush(Color.FromRgb(255, 106, 106));

    public NewConnectionDialog(ConnectionProfile? defaults = null)
    {
        Title = defaults == null ? "New Connection" : "Edit Connection";
        Width = 640;
        SizeToContent = SizeToContent.Height;
        MinHeight = 200;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = BgWin;

        var profile = defaults ?? new ConnectionProfile();
        int initialSectors = profile.Sectors > 0 ? profile.Sectors : 1000;

        var txtName = CreateTextBox(profile.Name, "rogue_t");
        var txtServer = CreateTextBox(profile.Server, "hostname or IP address");
        var txtPort = CreateTextBox(profile.Port.ToString(), width: 96);
        var txtSectors = CreateTextBox(initialSectors.ToString(), "1000", width: 120);
        var txtLoginScript = CreateTextBox(string.IsNullOrWhiteSpace(profile.LoginScript) ? "0_Login.cts" : profile.LoginScript);
        var txtLoginName = CreateTextBox(profile.LoginName);
        var txtPassword = CreateTextBox(profile.Password);
        var txtGameLetter = CreateTextBox(profile.GameLetter, width: 96);

        var cboProtocol = new ComboBox
        {
            ItemsSource = new[] { "Telnet", "Rlogin" },
            SelectedIndex = profile.Protocol == TwProtocol.Rlogin ? 1 : 0,
            MinWidth = 120,
            Background = BgInput,
            Foreground = FgText,
            BorderBrush = InnerEdge,
        };

        var chkEmbedded = CreateCheckBox("Run embedded proxy (enables .ts/.cts scripts)", profile.EmbeddedProxy);
        var chkStandaloneProxy = CreateCheckBox("Connect to standalone TWX proxy on this machine", profile.LocalTwxProxy);
        var chkAutoReconnect = CreateCheckBox("Auto-reconnect on disconnect", profile.AutoReconnect);
        var chkUseLogin = CreateCheckBox("Run login script after connect", profile.UseLogin);
        var chkUseRLogin = CreateCheckBox("Use RLogin handshake", profile.UseRLogin);

        var validationText = new TextBlock
        {
            Foreground = ErrorText,
            FontSize = 12,
            IsVisible = false,
            TextWrapping = TextWrapping.Wrap,
        };

        var sectorHint = new TextBlock
        {
            Foreground = FgMuted,
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(132, -4, 0, 2),
        };

        var sectorsRow = BuildRow("Sectors:", txtSectors);
        var loginScriptRow = BuildRow("Login script:", txtLoginScript);
        var loginNameRow = BuildRow("Username:", txtLoginName);
        var passwordRow = BuildRow("Password:", txtPassword);
        var gameLetterRow = BuildRow("Game letter:", txtGameLetter);

        var connectionSection = BuildSection(
            "Connection",
            "Name the game and tell MTC where to connect.",
            BuildRow("Game name:", txtName),
            BuildRow("Server:", txtServer),
            BuildRow("Port:", txtPort),
            BuildRow("Protocol:", cboProtocol),
            sectorsRow,
            sectorHint);

        var proxySection = BuildSection(
            "Proxy Mode",
            "Embedded mode runs the native proxy inside MTC. Standalone mode can connect to an external proxy.",
            chkEmbedded,
            chkStandaloneProxy,
            chkAutoReconnect);

        var loginSection = BuildSection(
            "Login Automation",
            "Optional embedded-proxy login helpers.",
            chkUseLogin,
            chkUseRLogin,
            loginScriptRow,
            loginNameRow,
            passwordRow,
            gameLetterRow);

        void SetValidation(string? message)
        {
            validationText.Text = message ?? string.Empty;
            validationText.IsVisible = !string.IsNullOrWhiteSpace(message);
        }

        void RefreshModeVisibility()
        {
            bool embedded = chkEmbedded.IsChecked == true;
            bool showDetails = embedded && (chkUseLogin.IsChecked == true || chkUseRLogin.IsChecked == true);

            chkStandaloneProxy.IsVisible = !embedded;
            chkAutoReconnect.IsVisible = embedded;
            loginSection.IsVisible = embedded;
            loginScriptRow.IsVisible = showDetails;
            loginNameRow.IsVisible = showDetails;
            passwordRow.IsVisible = showDetails;
            gameLetterRow.IsVisible = showDetails;

            sectorHint.Text = embedded
                ? "Used when the embedded proxy creates or resizes the shared TWX database."
                : "Used when MTC creates or resizes its local standalone database for this game.";
        }

        chkEmbedded.IsCheckedChanged += (_, _) => RefreshModeVisibility();
        chkUseLogin.IsCheckedChanged += (_, _) => RefreshModeVisibility();
        chkUseRLogin.IsCheckedChanged += (_, _) => RefreshModeVisibility();
        RefreshModeVisibility();

        WireDialogClipboard(txtName);
        WireDialogClipboard(txtServer);
        WireDialogClipboard(txtPort);
        WireDialogClipboard(txtSectors);
        WireDialogClipboard(txtLoginScript);
        WireDialogClipboard(txtLoginName);
        WireDialogClipboard(txtPassword);
        WireDialogClipboard(txtGameLetter);

        var btnOk = BuildActionButton("Save", primary: true);
        var btnCancel = BuildActionButton("Cancel", primary: false);

        btnOk.Click += (_, _) =>
        {
            SetValidation(null);

            string name = txtName.Text?.Trim() ?? string.Empty;
            if (string.IsNullOrEmpty(name))
            {
                SetValidation("Enter a game name.");
                txtName.Focus();
                return;
            }

            string server = txtServer.Text?.Trim() ?? string.Empty;
            if (string.IsNullOrEmpty(server))
            {
                SetValidation("Enter the game server host name or IP address.");
                txtServer.Focus();
                return;
            }

            if (!int.TryParse(txtPort.Text?.Trim(), out int portVal) || portVal is < 1 or > 65535)
            {
                SetValidation("Enter a valid TCP port from 1 to 65535.");
                txtPort.Focus();
                return;
            }

            if (!int.TryParse(txtSectors.Text?.Trim(), out int sectors) || sectors is < 100 or > ushort.MaxValue)
            {
                SetValidation($"Enter a sector count from 100 to {ushort.MaxValue:N0}.");
                txtSectors.Focus();
                return;
            }

            Result = new ConnectionProfile
            {
                Name = name,
                Server = server,
                Port = portVal,
                Protocol = cboProtocol.SelectedIndex == 1 ? TwProtocol.Rlogin : TwProtocol.Telnet,
                LocalTwxProxy = chkStandaloneProxy.IsChecked == true,
                EmbeddedProxy = chkEmbedded.IsChecked == true,
                AutoReconnect = chkAutoReconnect.IsChecked == true,
                Sectors = sectors,
                UseLogin = chkUseLogin.IsChecked == true,
                UseRLogin = chkUseRLogin.IsChecked == true,
                LoginScript = string.IsNullOrWhiteSpace(txtLoginScript.Text) ? "0_Login.cts" : txtLoginScript.Text.Trim(),
                LoginName = txtLoginName.Text?.Trim() ?? string.Empty,
                Password = txtPassword.Text ?? string.Empty,
                GameLetter = string.IsNullOrWhiteSpace(txtGameLetter.Text)
                    ? string.Empty
                    : txtGameLetter.Text.Trim().Substring(0, 1).ToUpperInvariant(),
                LoginSettingsConfigured = chkEmbedded.IsChecked == true,
                // Preserve scrollback setting from the profile being edited.
                ScrollbackLines = profile.ScrollbackLines,
            };
            Close(true);
        };

        btnCancel.Click += (_, _) => Close(false);

        var btnRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 10,
            Children = { btnCancel, btnOk },
        };

        Content = new Border
        {
            Padding = new Thickness(18),
            Child = new StackPanel
            {
                Spacing = 14,
                Children =
                {
                    new TextBlock
                    {
                        Text = defaults == null ? "Create Connection" : "Edit Connection",
                        Foreground = Accent,
                        FontSize = 22,
                        FontWeight = FontWeight.Bold,
                    },
                    new TextBlock
                    {
                        Text = "Connection settings are stored with the game so the database, sidebar, and script runtime stay aligned.",
                        Foreground = FgMuted,
                        TextWrapping = TextWrapping.Wrap,
                    },
                    connectionSection,
                    proxySection,
                    loginSection,
                    validationText,
                    btnRow,
                },
            },
        };
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    private static TextBox CreateTextBox(string? text, string? watermark = null, double width = double.NaN)
    {
        return new TextBox
        {
            Text = text ?? string.Empty,
            Watermark = watermark,
            Width = double.IsNaN(width) ? double.NaN : width,
            Background = BgInput,
            Foreground = FgText,
            BorderBrush = InnerEdge,
            CaretBrush = Accent,
        };
    }

    private static CheckBox CreateCheckBox(string text, bool isChecked)
    {
        return new CheckBox
        {
            Content = text,
            IsChecked = isChecked,
            Foreground = FgText,
            Margin = new Thickness(0, 2, 0, 2),
        };
    }

    private static Border BuildSection(string title, string subtitle, params Control[] children)
    {
        var body = new StackPanel { Spacing = 8 };
        body.Children.Add(new TextBlock
        {
            Text = title,
            Foreground = Accent,
            FontSize = 16,
            FontWeight = FontWeight.SemiBold,
        });
        body.Children.Add(new TextBlock
        {
            Text = subtitle,
            Foreground = FgMuted,
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
        });

        foreach (Control child in children)
            body.Children.Add(child);

        return new Border
        {
            Background = BgPanel,
            BorderBrush = Edge,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(14),
            Child = body,
        };
    }

    private static Button BuildActionButton(string text, bool primary)
    {
        return new Button
        {
            Content = text,
            MinWidth = 96,
            Padding = new Thickness(14, 7),
            Background = primary ? Accent : BgCardAlt,
            BorderBrush = primary ? AccentHot : InnerEdge,
            Foreground = primary ? AccentInk : FgText,
            HorizontalContentAlignment = HorizontalAlignment.Center,
        };
    }

    private static Grid BuildRow(string labelText, Control input)
    {
        const double LabelWidth = 124;
        var grid = new Grid { Margin = new Thickness(0, 2, 0, 2) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(LabelWidth) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var lbl = new TextBlock
        {
            Text = labelText,
            Foreground = FgText,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0),
            FontWeight = FontWeight.SemiBold,
        };

        Grid.SetColumn(lbl, 0);
        Grid.SetColumn(input, 1);
        grid.Children.Add(lbl);
        grid.Children.Add(input);
        return grid;
    }

    private static void WireDialogClipboard(TextBox textBox)
    {
        textBox.KeyDown += async (_, e) =>
        {
            if (!e.KeyModifiers.HasFlag(KeyModifiers.Control))
                return;

            switch (e.Key)
            {
                case Key.A:
                {
                    string current = textBox.Text ?? string.Empty;
                    textBox.SelectionStart = 0;
                    textBox.SelectionEnd = current.Length;
                    textBox.CaretIndex = current.Length;
                    e.Handled = true;
                    break;
                }

                case Key.C:
                {
                    string selected = textBox.SelectedText ?? string.Empty;
                    if (selected.Length > 0)
                        await ClipboardHelper.TrySetTextAsync(textBox, selected);
                    e.Handled = true;
                    break;
                }

                case Key.X:
                {
                    string selected = textBox.SelectedText ?? string.Empty;
                    if (selected.Length > 0)
                    {
                        if (await ClipboardHelper.TrySetTextAsync(textBox, selected))
                            ReplaceSelection(textBox, string.Empty);
                    }
                    e.Handled = true;
                    break;
                }

                case Key.V:
                {
                    var clipboard = TopLevel.GetTopLevel(textBox)?.Clipboard;
                    if (clipboard != null)
                    {
                        string? pasted = await ClipboardExtensions.TryGetTextAsync(clipboard);
                        if (!string.IsNullOrEmpty(pasted))
                            ReplaceSelection(textBox, pasted);
                    }
                    e.Handled = true;
                    break;
                }
            }
        };
    }

    private static void ReplaceSelection(TextBox textBox, string replacement)
    {
        string current = textBox.Text ?? string.Empty;
        int start = Math.Min(textBox.SelectionStart, textBox.SelectionEnd);
        int end = Math.Max(textBox.SelectionStart, textBox.SelectionEnd);
        start = Math.Clamp(start, 0, current.Length);
        end = Math.Clamp(end, 0, current.Length);

        string updated = current.Substring(0, start) + replacement + current.Substring(end);
        int caret = start + replacement.Length;
        textBox.Text = updated;
        textBox.SelectionStart = caret;
        textBox.SelectionEnd = caret;
        textBox.CaretIndex = caret;
    }
}
