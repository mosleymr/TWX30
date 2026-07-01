using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

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

    /// <summary>True when the profile was created from the Auto Setup flow and should start native Mombot.</summary>
    public bool AutoSetupRequested { get; private set; }

    private static readonly IBrush BgWin = new SolidColorBrush(Color.FromRgb(8, 14, 20));
    private static readonly IBrush BgPanel = new SolidColorBrush(Color.FromRgb(14, 33, 42));
    private static readonly IBrush BgCard = new SolidColorBrush(Color.FromRgb(16, 53, 67));
    private static readonly IBrush BgCardAlt = new SolidColorBrush(Color.FromRgb(10, 43, 53));
    private static readonly IBrush BgInput = new SolidColorBrush(Color.FromRgb(7, 28, 36));
    private static readonly IBrush Edge = new SolidColorBrush(Color.FromRgb(57, 112, 128));
    private static readonly IBrush InnerEdge = new SolidColorBrush(Color.FromRgb(23, 81, 94));
    private static readonly IBrush FgText = new SolidColorBrush(Color.FromRgb(222, 238, 242));
    private static readonly IBrush FgMuted = new SolidColorBrush(Color.FromRgb(142, 195, 205));
    private static readonly IBrush Accent = new SolidColorBrush(Color.FromRgb(0, 212, 201));
    private static readonly IBrush AccentHot = new SolidColorBrush(Color.FromRgb(255, 193, 74));
    private static readonly IBrush AccentInk = new SolidColorBrush(Color.FromRgb(8, 26, 30));
    private static readonly IBrush ErrorText = new SolidColorBrush(Color.FromRgb(255, 106, 106));
    private static readonly IBrush WarnText = new SolidColorBrush(Color.FromRgb(255, 226, 88));
    private static readonly IBrush BadText = new SolidColorBrush(Color.FromRgb(255, 95, 95));
    private const double FieldLabelWidth = 92;

    private readonly bool _allowAutoSetup;
    private bool _autoSetupLoaded;
    private CancellationTokenSource? _autoLoadCts;
    private StackPanel? _serverListPanel;
    private TextBlock? _autoStatusText;
    private TextBlock? _autoValidationText;
    private TextBlock? _selectedGameText;
    private TextBox? _autoUsernameBox;
    private TextBox? _autoPasswordBox;
    private TwcrawlGameSummary? _selectedAutoGame;

    public NewConnectionDialog(ConnectionProfile? defaults = null, bool allowAutoSetup = true)
    {
        _allowAutoSetup = allowAutoSetup && defaults == null;
        Title = defaults == null ? "New Connection" : "Edit Connection";
        Width = _allowAutoSetup ? 1080 : 500;
        Height = _allowAutoSetup ? 760 : double.NaN;
        SizeToContent = _allowAutoSetup ? SizeToContent.Manual : SizeToContent.Height;
        MinHeight = 200;
        MinWidth = _allowAutoSetup ? 920 : 500;
        CanResize = _allowAutoSetup;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = BgWin;

        var profile = defaults ?? new ConnectionProfile();
        Control manualSetup = BuildManualSetup(profile);

        Control content = _allowAutoSetup
            ? BuildTabbedContent(manualSetup, BuildAutoSetup())
            : manualSetup;

        Content = new Border
        {
            Padding = new Thickness(14),
            Child = content,
        };

        if (_allowAutoSetup)
        {
            Opened += async (_, _) =>
            {
                if (_autoSetupLoaded)
                    return;
                _autoSetupLoaded = true;
                await ReloadAutoServersAsync();
            };
            Closed += (_, _) => _autoLoadCts?.Cancel();
        }
    }

    private static Control BuildTabbedContent(Control manualSetup, Control autoSetup)
    {
        return new TabControl
        {
            ItemsSource = new[]
            {
                new TabItem { Header = "Manual Setup", Content = manualSetup },
                new TabItem { Header = "Auto Setup (New!)", Content = autoSetup },
            },
        };
    }

    private Control BuildManualSetup(ConnectionProfile profile)
    {
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

        var validationText = BuildValidationText();

        var connectionFields = BuildConnectionFieldsGrid(txtName, txtServer, cboProtocol, txtPort, txtSectors);
        var listenPortRow = BuildRow("Listen port:", txtListenPort);
        var loginScriptRow = BuildRow("Login script:", txtLoginScript);
        var loginNameRow = BuildRow("Username:", txtLoginName);
        var passwordRow = BuildRow("Password:", txtPassword);
        var gameLetterRow = BuildRow("Game letter:", txtGameLetter);

        var connectionSection = BuildSection("Game & Server", connectionFields);
        var proxySection = BuildSection("Proxy Mode", chkEmbedded, chkListenForConnections, listenPortRow, chkStandaloneProxy, chkAutoReconnect);
        var loginSection = BuildSection("Login Automation", chkUseLogin, chkUseRLogin, loginScriptRow, loginNameRow, passwordRow, gameLetterRow);

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
                ScrollbackLines = profile.ScrollbackLines,
            };
            AutoSetupRequested = false;
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

        return new StackPanel
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
        };
    }

    private Control BuildAutoSetup()
    {
        _autoStatusText = new TextBlock
        {
            Text = "Loading active TradeWars servers from twcrawl...",
            Foreground = FgMuted,
            FontSize = 13,
        };
        _autoValidationText = BuildValidationText();
        _selectedGameText = new TextBlock
        {
            Text = "Select a game above, then enter your first-login account information.",
            Foreground = FgMuted,
            FontSize = 13,
            TextWrapping = TextWrapping.Wrap,
        };
        _autoUsernameBox = CreateTextBox(string.Empty, "username", width: 210);
        _autoPasswordBox = CreateTextBox(string.Empty, "password", width: 210);
        _autoPasswordBox.PasswordChar = '*';
        WireDialogClipboard(_autoUsernameBox);
        WireDialogClipboard(_autoPasswordBox);

        var reloadButton = BuildActionButton("Reload", primary: false);
        reloadButton.Click += async (_, _) => await ReloadAutoServersAsync();

        _serverListPanel = new StackPanel { Spacing = 8 };
        var serverScroll = new ScrollViewer
        {
            Content = _serverListPanel,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
        };

        var goButton = BuildActionButton("GO", primary: true);
        goButton.Click += (_, _) => SubmitAutoSetup();

        var header = new Grid { ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) } };
        Grid.SetColumn(_autoStatusText, 0);
        Grid.SetColumn(reloadButton, 1);
        header.Children.Add(_autoStatusText);
        header.Children.Add(reloadButton);

        var credentialsGrid = new Grid
        {
            ColumnSpacing = 10,
            RowSpacing = 6,
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto),
            },
        };
        credentialsGrid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        credentialsGrid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

        AddInlineLabel(credentialsGrid, 0, 0, "Username");
        AddInlineControl(credentialsGrid, 0, 1, _autoUsernameBox);
        AddInlineLabel(credentialsGrid, 0, 2, "Password");
        AddInlineControl(credentialsGrid, 0, 3, _autoPasswordBox);
        Grid.SetRow(goButton, 0);
        Grid.SetColumn(goButton, 5);
        credentialsGrid.Children.Add(goButton);
        Grid.SetRow(_selectedGameText, 1);
        Grid.SetColumn(_selectedGameText, 0);
        Grid.SetColumnSpan(_selectedGameText, 6);
        credentialsGrid.Children.Add(_selectedGameText);

        var bottom = BuildSection(
            "Create and Login",
            credentialsGrid,
            _autoValidationText);

        var grid = new Grid
        {
            RowSpacing = 10,
            RowDefinitions =
            {
                new RowDefinition(GridLength.Auto),
                new RowDefinition(new GridLength(1, GridUnitType.Star)),
                new RowDefinition(GridLength.Auto),
            },
        };
        Grid.SetRow(header, 0);
        Grid.SetRow(serverScroll, 1);
        Grid.SetRow(bottom, 2);
        grid.Children.Add(header);
        grid.Children.Add(serverScroll);
        grid.Children.Add(bottom);

        return grid;
    }

    private async Task ReloadAutoServersAsync()
    {
        _autoLoadCts?.Cancel();
        _autoLoadCts = new CancellationTokenSource();
        CancellationToken token = _autoLoadCts.Token;

        if (_serverListPanel == null || _autoStatusText == null)
            return;

        _serverListPanel.Children.Clear();
        _autoStatusText.Text = "Loading active TradeWars servers from twcrawl...";
        _autoStatusText.Foreground = FgMuted;
        SetAutoValidation(null);

        try
        {
            IReadOnlyList<TwcrawlServerSummary> servers = await TwcrawlDiscoveryClient.FetchActiveServersAsync(token);
            if (token.IsCancellationRequested)
                return;

            _serverListPanel.Children.Clear();
            foreach (TwcrawlServerSummary server in servers)
                _serverListPanel.Children.Add(BuildServerCard(server));

            _autoStatusText.Text = servers.Count == 0
                ? "No active servers were reported by twcrawl."
                : $"Active servers: {servers.Count}. Expand a server and select a game.";
            _autoStatusText.Foreground = servers.Count == 0 ? WarnText : FgMuted;
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _autoStatusText.Text = "Unable to load twcrawl data.";
            _autoStatusText.Foreground = BadText;
            SetAutoValidation(ex.Message);
        }
    }

    private Control BuildServerCard(TwcrawlServerSummary server)
    {
        var gamesPanel = new StackPanel { Spacing = 4 };
        gamesPanel.Children.Add(BuildGameHeaderRow());
        foreach (TwcrawlGameSummary game in server.GameList)
            gamesPanel.Children.Add(BuildGameRow(game));

        return new Expander
        {
            Header = BuildServerHeader(server),
            Content = new Border
            {
                Background = BgPanel,
                BorderBrush = InnerEdge,
                BorderThickness = new Thickness(1, 0, 1, 1),
                CornerRadius = new CornerRadius(0, 0, 8, 8),
                Padding = new Thickness(10, 6),
                Child = gamesPanel,
            },
            Background = BgCardAlt,
            BorderBrush = Edge,
            Foreground = FgText,
            IsExpanded = false,
        };
    }

    private static Control BuildServerHeader(TwcrawlServerSummary server)
    {
        var grid = new Grid
        {
            ColumnSpacing = 12,
            ColumnDefinitions =
            {
                new ColumnDefinition(new GridLength(2.2, GridUnitType.Star)),
                new ColumnDefinition(new GridLength(1.4, GridUnitType.Star)),
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Auto),
            },
        };

        AddHeaderText(grid, 0, server.Name, FgText, FontWeight.SemiBold);
        AddHeaderText(grid, 1, server.Telnet, FgMuted, FontWeight.Normal);
        AddHeaderText(grid, 2, $"BigBang {EmptyDash(server.BigBang)}", FgMuted, FontWeight.Normal);
        AddHeaderText(grid, 3, $"{server.Games} games", FgMuted, FontWeight.Normal);
        AddHeaderText(grid, 4, $"{server.Players} players", FgMuted, FontWeight.Normal);
        return grid;
    }

    private Control BuildGameHeaderRow()
    {
        var grid = BuildGameRowGrid();
        AddGameHeader(grid, 0, "Game");
        AddGameHeader(grid, 1, "Days");
        AddGameHeader(grid, 2, "Time");
        AddGameHeader(grid, 3, "Turns");
        AddGameHeader(grid, 4, "Sectors");
        AddGameHeader(grid, 5, "Players");
        AddGameHeader(grid, 6, "Warnings");
        AddGameHeader(grid, 7, "");
        AddGameHeader(grid, 8, "");
        return grid;
    }

    private Control BuildGameRow(TwcrawlGameSummary game)
    {
        var grid = BuildGameRowGrid();

        AddGameText(grid, 0, $"{game.Letter} - {game.Name}", FgText, FontWeight.SemiBold, HorizontalAlignment.Left);
        AddGameText(grid, 1, game.DaysOpen?.ToString() ?? "-", FgMuted, FontWeight.Normal);
        AddGameText(grid, 2, EmptyDash(game.Time), FgMuted, FontWeight.Normal);
        AddGameText(grid, 3, EmptyDash(game.Turns), FgMuted, FontWeight.Normal);
        AddGameText(grid, 4, game.Sectors.ToString("N0"), FgMuted, FontWeight.Normal);
        AddGameText(grid, 5, game.Players.ToString("N0"), FgMuted, FontWeight.Normal);

        var warningPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 4,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        AddWarningLabel(warningPanel, "LATENCY", TwcrawlDiscoveryClient.ClassifyLatency(game.Latency));
        AddWarningLabel(warningPanel, "MOVE DELAY", TwcrawlDiscoveryClient.ClassifyShipDelay(game.ShipDelay));
        Grid.SetColumn(warningPanel, 6);
        grid.Children.Add(warningPanel);

        var details = BuildSmallButton("View Details");
        details.Click += async (_, _) => await OpenGameDetailsAsync(game);
        Grid.SetColumn(details, 7);
        grid.Children.Add(details);

        var select = BuildSmallButton("Select Game", primary: true);
        select.Click += (_, _) => SelectAutoGame(game);
        Grid.SetColumn(select, 8);
        grid.Children.Add(select);

        return new Border
        {
            Background = BgCardAlt,
            BorderBrush = InnerEdge,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(8, 5),
            Child = grid,
        };
    }

    private static Grid BuildGameRowGrid()
    {
        return new Grid
        {
            ColumnSpacing = 10,
            ColumnDefinitions =
            {
                new ColumnDefinition(new GridLength(2.4, GridUnitType.Star)),
                new ColumnDefinition(new GridLength(58)),
                new ColumnDefinition(new GridLength(95)),
                new ColumnDefinition(new GridLength(100)),
                new ColumnDefinition(new GridLength(78)),
                new ColumnDefinition(new GridLength(65)),
                new ColumnDefinition(new GridLength(132)),
                new ColumnDefinition(new GridLength(90)),
                new ColumnDefinition(new GridLength(96)),
            },
        };
    }

    private void SelectAutoGame(TwcrawlGameSummary game)
    {
        _selectedAutoGame = game;
        string suggestedName = TwcrawlDiscoveryClient.BuildSuggestedGameName(game);
        if (_selectedGameText != null)
        {
            _selectedGameText.Text =
                $"Selected {game.ServerName} game {game.Letter}: {game.Name}. Game will be saved as '{suggestedName}' and opened through the embedded proxy.";
            _selectedGameText.Foreground = FgText;
        }

        SetAutoValidation(null);
        Dispatcher.UIThread.Post(() => _autoUsernameBox?.Focus(), DispatcherPriority.Input);
    }

    private void SubmitAutoSetup()
    {
        SetAutoValidation(null);
        if (_selectedAutoGame == null)
        {
            SetAutoValidation("Select a game first.");
            return;
        }

        string username = _autoUsernameBox?.Text?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(username))
        {
            SetAutoValidation("Enter the username to create or log into.");
            _autoUsernameBox?.Focus();
            return;
        }

        string password = _autoPasswordBox?.Text ?? string.Empty;
        if (string.IsNullOrEmpty(password))
        {
            SetAutoValidation("Enter the password for this game.");
            _autoPasswordBox?.Focus();
            return;
        }

        if (!TwcrawlDiscoveryClient.TryParseTelnetEndpoint(_selectedAutoGame.ServerTelnet, out string host, out int port))
        {
            SetAutoValidation($"Unable to parse telnet endpoint '{_selectedAutoGame.ServerTelnet}'.");
            return;
        }

        Result = new ConnectionProfile
        {
            Name = TwcrawlDiscoveryClient.BuildSuggestedGameName(_selectedAutoGame),
            Server = host,
            Port = port,
            Protocol = TwProtocol.Telnet,
            LocalTwxProxy = true,
            EmbeddedProxy = true,
            AutoReconnect = false,
            ListenForConnections = false,
            ListenPort = ConnectionProfile.DefaultListenPort,
            Sectors = _selectedAutoGame.Sectors > 0 ? _selectedAutoGame.Sectors : ConnectionProfile.DefaultSectors,
            UseLogin = false,
            UseRLogin = false,
            LoginScript = "0_Login.cts",
            LoginName = username,
            Password = password,
            GameLetter = _selectedAutoGame.Letter,
            LoginSettingsConfigured = true,
        };
        AutoSetupRequested = true;
        Close(true);
    }

    private async Task OpenGameDetailsAsync(TwcrawlGameSummary game)
    {
        Uri uri = TwcrawlDiscoveryClient.BuildDetailsUri(game);
        try
        {
            var launcher = TopLevel.GetTopLevel(this)?.Launcher;
            if (launcher != null && await launcher.LaunchUriAsync(uri))
                return;
        }
        catch
        {
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = uri.ToString(),
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            SetAutoValidation($"Unable to open details page: {ex.Message}");
        }
    }

    private void SetAutoValidation(string? message)
    {
        if (_autoValidationText == null)
            return;

        _autoValidationText.Text = message ?? string.Empty;
        _autoValidationText.IsVisible = !string.IsNullOrWhiteSpace(message);
    }

    private static TextBlock BuildValidationText()
    {
        return new TextBlock
        {
            Foreground = ErrorText,
            FontSize = 12,
            IsVisible = false,
            TextWrapping = TextWrapping.Wrap,
        };
    }

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

    private static Button BuildSmallButton(string text, bool primary = false)
    {
        return new Button
        {
            Content = text,
            MinWidth = 76,
            Padding = new Thickness(8, 4),
            Background = primary ? Accent : BgCard,
            BorderBrush = primary ? AccentHot : InnerEdge,
            Foreground = primary ? AccentInk : FgText,
            FontSize = 12,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
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

    private static void AddInlineLabel(Grid grid, int row, int column, string text)
    {
        var label = new TextBlock
        {
            Text = text,
            Foreground = FgText,
            VerticalAlignment = VerticalAlignment.Center,
            FontSize = 13,
            FontWeight = FontWeight.SemiBold,
        };
        Grid.SetRow(label, row);
        Grid.SetColumn(label, column);
        grid.Children.Add(label);
    }

    private static void AddInlineControl(Grid grid, int row, int column, Control control)
    {
        Grid.SetRow(control, row);
        Grid.SetColumn(control, column);
        grid.Children.Add(control);
    }

    private static void AddHeaderText(Grid grid, int column, string text, IBrush foreground, FontWeight weight)
    {
        var block = new TextBlock
        {
            Text = text,
            Foreground = foreground,
            FontWeight = weight,
            FontSize = 13,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(block, column);
        grid.Children.Add(block);
    }

    private static void AddGameHeader(Grid grid, int column, string text)
    {
        AddGameText(
            grid,
            column,
            text,
            FgMuted,
            FontWeight.SemiBold,
            column == 0 ? HorizontalAlignment.Left : HorizontalAlignment.Center);
    }

    private static void AddGameText(
        Grid grid,
        int column,
        string text,
        IBrush foreground,
        FontWeight weight,
        HorizontalAlignment alignment = HorizontalAlignment.Center)
    {
        var block = new TextBlock
        {
            Text = text,
            Foreground = foreground,
            FontWeight = weight,
            FontSize = 12,
            TextTrimming = TextTrimming.CharacterEllipsis,
            HorizontalAlignment = alignment,
            TextAlignment = alignment == HorizontalAlignment.Left ? TextAlignment.Left : TextAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(block, column);
        grid.Children.Add(block);
    }

    private static void AddWarningLabel(StackPanel panel, string label, TwcrawlQuality quality)
    {
        if (quality is not (TwcrawlQuality.Warn or TwcrawlQuality.Bad))
            return;

        panel.Children.Add(new Border
        {
            Background = quality == TwcrawlQuality.Bad
                ? new SolidColorBrush(Color.FromRgb(74, 12, 18))
                : new SolidColorBrush(Color.FromRgb(70, 58, 8)),
            BorderBrush = quality == TwcrawlQuality.Bad ? BadText : WarnText,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(4, 0),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock
            {
                Text = label,
                Foreground = quality == TwcrawlQuality.Bad ? BadText : WarnText,
                FontSize = 9,
                FontWeight = FontWeight.Bold,
                LineHeight = 10,
                VerticalAlignment = VerticalAlignment.Center,
            },
        });
    }

    private static string EmptyDash(string value)
        => string.IsNullOrWhiteSpace(value) ? "-" : value;

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
