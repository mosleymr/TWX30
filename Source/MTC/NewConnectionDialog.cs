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
    private static readonly IBrush Accent = new SolidColorBrush(Color.FromRgb(0, 212, 201));
    private static readonly IBrush AccentHot = new SolidColorBrush(Color.FromRgb(255, 193, 74));
    private static readonly IBrush AccentInk = new SolidColorBrush(Color.FromRgb(8, 26, 30));
    private static readonly IBrush ErrorText = new SolidColorBrush(Color.FromRgb(255, 106, 106));
    private const double FieldLabelWidth = 92;

    public NewConnectionDialog(ConnectionProfile? defaults = null)
    {
        Title = defaults == null ? "New Connection" : "Edit Connection";
        Width = 500;
        SizeToContent = SizeToContent.Height;
        MinHeight = 200;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = BgWin;

        var profile = defaults ?? new ConnectionProfile();
        int initialSectors = profile.Sectors > 0 ? profile.Sectors : ConnectionProfile.DefaultSectors;

        var txtName = CreateTextBox(profile.Name, "rogue_t", width: 250);
        var txtServer = CreateTextBox(profile.Server, "hostname or IP address", width: 250);
        var txtPort = CreateTextBox(profile.Port.ToString(), width: 88);
        var txtSectors = CreateTextBox(initialSectors.ToString(), ConnectionProfile.DefaultSectors.ToString(), width: 108);
        var txtListenPort = CreateTextBox(
            (profile.ListenPort > 0 ? profile.ListenPort : ConnectionProfile.DefaultListenPort).ToString(),
            ConnectionProfile.DefaultListenPort.ToString(),
            width: 88);
        var txtLoginScript = CreateTextBox(string.IsNullOrWhiteSpace(profile.LoginScript) ? "0_Login.cts" : profile.LoginScript, width: 250);
        var txtLoginName = CreateTextBox(profile.LoginName, width: 250);
        var txtPassword = CreateTextBox(profile.Password, width: 250);
        var txtGameLetter = CreateTextBox(profile.GameLetter, width: 88);

        var cboProtocol = new ComboBox
        {
            ItemsSource = new[] { "Telnet", "Rlogin" },
            SelectedIndex = profile.Protocol == TwProtocol.Rlogin ? 1 : 0,
            Width = 86,
            MinHeight = 30,
            FontSize = 13,
            Background = BgInput,
            Foreground = FgText,
            BorderBrush = InnerEdge,
        };

        var chkEmbedded = CreateCheckBox("Run embedded proxy (enables .ts/.cts scripts)", profile.EmbeddedProxy);
        var chkListenForConnections = CreateCheckBox("Listen for connections", profile.ListenForConnections);
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

        var connectionFields = BuildConnectionFieldsGrid(txtName, txtServer, cboProtocol, txtPort, txtSectors);
        var listenPortRow = BuildRow("Listen port:", txtListenPort);
        var loginScriptRow = BuildRow("Login script:", txtLoginScript);
        var loginNameRow = BuildRow("Username:", txtLoginName);
        var passwordRow = BuildRow("Password:", txtPassword);
        var gameLetterRow = BuildRow("Game letter:", txtGameLetter);

        var connectionSection = BuildSection(
            "Game & Server",
            connectionFields);

        var proxySection = BuildSection(
            "Proxy Mode",
            chkEmbedded,
            chkListenForConnections,
            listenPortRow,
            chkStandaloneProxy,
            chkAutoReconnect);

        var loginSection = BuildSection(
            "Login Automation",
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
            chkListenForConnections.IsVisible = embedded;
            listenPortRow.IsVisible = embedded && chkListenForConnections.IsChecked == true;
            loginSection.IsVisible = embedded;
            loginScriptRow.IsVisible = showDetails;
            loginNameRow.IsVisible = showDetails;
            passwordRow.IsVisible = showDetails;
            gameLetterRow.IsVisible = showDetails;

        }

        chkEmbedded.IsCheckedChanged += (_, _) => RefreshModeVisibility();
        chkListenForConnections.IsCheckedChanged += (_, _) => RefreshModeVisibility();
        chkUseLogin.IsCheckedChanged += (_, _) => RefreshModeVisibility();
        chkUseRLogin.IsCheckedChanged += (_, _) => RefreshModeVisibility();
        RefreshModeVisibility();

        WireDialogClipboard(txtName);
        WireDialogClipboard(txtServer);
        WireDialogClipboard(txtPort);
        WireDialogClipboard(txtSectors);
        WireDialogClipboard(txtListenPort);
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

            bool embeddedProxy = chkEmbedded.IsChecked == true;
            bool listenForConnections = embeddedProxy && chkListenForConnections.IsChecked == true;
            int listenPort = profile.ListenPort > 0 ? profile.ListenPort : ConnectionProfile.DefaultListenPort;
            bool listenPortValid = int.TryParse(txtListenPort.Text?.Trim(), out int parsedListenPort) &&
                                   parsedListenPort is >= 1 and <= ushort.MaxValue;
            if (listenPortValid)
                listenPort = parsedListenPort;
            else if (listenForConnections)
            {
                SetValidation("Enter a valid listen port from 1 to 65535.");
                txtListenPort.Focus();
                return;
            }

            Result = new ConnectionProfile
            {
                Name = name,
                Server = server,
                Port = portVal,
                Protocol = cboProtocol.SelectedIndex == 1 ? TwProtocol.Rlogin : TwProtocol.Telnet,
                LocalTwxProxy = chkStandaloneProxy.IsChecked == true,
                EmbeddedProxy = embeddedProxy,
                AutoReconnect = chkAutoReconnect.IsChecked == true,
                ListenForConnections = listenForConnections,
                ListenPort = listenPort,
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
            Padding = new Thickness(14),
            Child = new StackPanel
            {
                Spacing = 10,
                Children =
                {
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
            FontSize = 13,
            MinHeight = 30,
            Padding = new Thickness(8, 4),
        };
    }

    private static CheckBox CreateCheckBox(string text, bool isChecked)
    {
        return new CheckBox
        {
            Content = text,
            IsChecked = isChecked,
            Foreground = FgText,
            FontSize = 13,
            Margin = new Thickness(0, 1, 0, 1),
        };
    }

    private static Border BuildSection(string title, params Control[] children)
    {
        var body = new StackPanel { Spacing = 7 };
        body.Children.Add(new TextBlock
        {
            Text = title,
            Foreground = Accent,
            FontSize = 15,
            FontWeight = FontWeight.SemiBold,
        });

        foreach (Control child in children)
            body.Children.Add(child);

        return new Border
        {
            Background = BgPanel,
            BorderBrush = Edge,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(12),
            Child = body,
        };
    }

    private static Button BuildActionButton(string text, bool primary)
    {
        return new Button
        {
            Content = text,
            MinWidth = 86,
            Padding = new Thickness(12, 6),
            Background = primary ? Accent : BgCardAlt,
            BorderBrush = primary ? AccentHot : InnerEdge,
            Foreground = primary ? AccentInk : FgText,
            FontSize = 13,
            HorizontalContentAlignment = HorizontalAlignment.Center,
        };
    }

    private static Grid BuildRow(string labelText, Control input)
    {
        var grid = new Grid { Margin = new Thickness(0, 1, 0, 1) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(FieldLabelWidth) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var lbl = new TextBlock
        {
            Text = labelText,
            Foreground = FgText,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0),
            FontSize = 13,
            FontWeight = FontWeight.SemiBold,
        };

        Grid.SetColumn(lbl, 0);
        Grid.SetColumn(input, 1);
        grid.Children.Add(lbl);
        grid.Children.Add(input);
        return grid;
    }

    private static Grid BuildConnectionFieldsGrid(
        TextBox txtName,
        TextBox txtServer,
        ComboBox cboProtocol,
        TextBox txtPort,
        TextBox txtSectors)
    {
        var grid = new Grid
        {
            Margin = new Thickness(0, 1, 0, 1),
            RowSpacing = 7,
        };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(FieldLabelWidth) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        for (int i = 0; i < 4; i++)
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        AddConnectionField(grid, 0, "Game name:", txtName, spanInput: true);
        AddConnectionField(grid, 1, "Server:", txtServer, spanInput: true);
        AddConnectionField(grid, 2, "Protocol:", cboProtocol);
        AddSecondaryConnectionField(grid, 2, "Port:", txtPort);
        AddConnectionField(grid, 3, "Sectors:", txtSectors);
        return grid;
    }

    private static void AddConnectionField(Grid grid, int row, string labelText, Control input, bool spanInput = false)
    {
        var label = new TextBlock
        {
            Text = labelText,
            Foreground = FgText,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0),
            FontSize = 13,
            FontWeight = FontWeight.SemiBold,
        };

        input.HorizontalAlignment = HorizontalAlignment.Left;
        Grid.SetRow(label, row);
        Grid.SetColumn(label, 0);
        Grid.SetRow(input, row);
        Grid.SetColumn(input, 1);
        if (spanInput)
            Grid.SetColumnSpan(input, 3);
        grid.Children.Add(label);
        grid.Children.Add(input);
    }

    private static void AddSecondaryConnectionField(Grid grid, int row, string labelText, Control input)
    {
        var label = new TextBlock
        {
            Text = labelText,
            Foreground = FgText,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(18, 0, 6, 0),
            FontSize = 13,
            FontWeight = FontWeight.SemiBold,
        };

        input.HorizontalAlignment = HorizontalAlignment.Left;
        Grid.SetRow(label, row);
        Grid.SetColumn(label, 2);
        Grid.SetRow(input, row);
        Grid.SetColumn(input, 3);
        grid.Children.Add(label);
        grid.Children.Add(input);
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
