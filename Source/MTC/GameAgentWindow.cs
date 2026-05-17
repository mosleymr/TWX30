using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;

namespace MTC;

internal sealed class GameAgentWindow : Window
{
    private readonly Func<GameAgentContextSnapshot> _getContext;
    private readonly AppPreferences _preferences;
    private readonly IGameAgentModel _localModel = new LocalObserverGameAgentModel();
    private readonly TextBox _conversationBox;
    private readonly TextBox _inputBox;
    private readonly TextBox _contextBox;
    private readonly ComboBox _integrationCombo;
    private readonly ComboBox _modelCombo;
    private readonly TextBlock _statusText;

    public GameAgentWindow(Func<GameAgentContextSnapshot> getContext, AppPreferences preferences)
    {
        _getContext = getContext;
        _preferences = preferences;

        Title = "Game Agent";
        Width = 1050;
        Height = 720;
        MinWidth = 780;
        MinHeight = 640;
        Background = new SolidColorBrush(Color.FromRgb(0x07, 0x12, 0x17));
        FontFamily = new FontFamily("Cascadia Code, Menlo, Consolas, Courier New, monospace");

        _conversationBox = BuildReadOnlyBox("The game agent conversation will appear here.");
        _contextBox = BuildReadOnlyBox("Live game context will appear here.");
        _contextBox.MinWidth = 260;

        _inputBox = new TextBox
        {
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            MinHeight = 80,
            Watermark = "Ask about the current game. Try: what just happened, status, risks, log path.",
            Background = new SolidColorBrush(Color.FromRgb(0x03, 0x1d, 0x26)),
            Foreground = Brushes.Gainsboro,
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x12, 0x5e, 0x70)),
            Padding = new Thickness(8),
        };
        _inputBox.AddHandler(KeyDownEvent, OnInputKeyDown, RoutingStrategies.Tunnel);

        _integrationCombo = new ComboBox
        {
            ItemsSource = BuildIntegrationChoices(),
            SelectedItem = FindIntegrationChoice(AppPreferences.NormalizeGameAgentProvider(_preferences.GameAgentProvider)),
            Width = 160,
            Background = new SolidColorBrush(Color.FromRgb(0x03, 0x1d, 0x26)),
            Foreground = Brushes.Gainsboro,
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x12, 0x5e, 0x70)),
            Padding = new Thickness(6),
        };
        _integrationCombo.SelectionChanged += (_, _) => UpdateIntegrationControls();

        _modelCombo = new ComboBox
        {
            ItemsSource = new[] { ResolveInitialProviderModel(_preferences, GetSelectedProviderId()) },
            SelectedIndex = 0,
            Width = 220,
            Background = new SolidColorBrush(Color.FromRgb(0x03, 0x1d, 0x26)),
            Foreground = Brushes.Gainsboro,
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x12, 0x5e, 0x70)),
            Padding = new Thickness(6),
        };
        _modelCombo.SelectionChanged += (_, _) => SavePreferences();

        var refreshModelsButton = new Button
        {
            Content = "Models",
            MinWidth = 76,
            Height = 32,
        };
        refreshModelsButton.Click += async (_, _) => await RefreshLmStudioModelsAsync();

        var askButton = new Button
        {
            Content = "Ask",
            MinWidth = 100,
            Height = 32,
        };
        askButton.Click += (_, _) => Submit();

        var refreshButton = new Button
        {
            Content = "Refresh Context",
            MinWidth = 140,
            Height = 32,
        };
        refreshButton.Click += (_, _) => RefreshContext();

        var snapshotButton = new Button
        {
            Content = "Save Snapshot",
            MinWidth = 130,
            Height = 32,
        };
        snapshotButton.Click += (_, _) => SaveSnapshot();

        var sampleButton = new Button
        {
            Content = "Export Sample",
            MinWidth = 130,
            Height = 32,
        };
        sampleButton.Click += (_, _) => ExportTrainingSample();

        var clearButton = new Button
        {
            Content = "Clear",
            MinWidth = 100,
            Height = 32,
        };
        clearButton.Click += (_, _) => _conversationBox.Text = BuildWelcomeMessage();

        _statusText = new TextBlock
        {
            Text = "Observer mode. Commands are not enabled yet.",
            Foreground = new SolidColorBrush(Color.FromRgb(0x8a, 0xb8, 0xc0)),
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
        };

        var buttons = new WrapPanel
        {
            HorizontalAlignment = HorizontalAlignment.Right,
            Children =
            {
                WithControlMargin(refreshButton),
                WithControlMargin(snapshotButton),
                WithControlMargin(sampleButton),
                WithControlMargin(clearButton),
                WithControlMargin(askButton),
            },
        };

        var modelRow = new WrapPanel
        {
            Children =
            {
                WithControlMargin(new TextBlock
                {
                    Text = "Provider",
                    Foreground = new SolidColorBrush(Color.FromRgb(0x8a, 0xb8, 0xc0)),
                    VerticalAlignment = VerticalAlignment.Center,
                }),
                WithControlMargin(_integrationCombo),
                WithControlMargin(refreshModelsButton),
                WithControlMargin(_modelCombo),
            },
        };

        var controlPanel = new StackPanel
        {
            Spacing = 8,
            Children =
            {
                modelRow,
                _inputBox,
                _statusText,
                buttons,
            },
        };

        var leftPane = new Grid
        {
            RowDefinitions = new RowDefinitions("*,Auto"),
            RowSpacing = 10,
        };
        leftPane.Children.Add(WrapPanel("Conversation", _conversationBox).WithRow(0));
        leftPane.Children.Add(controlPanel.WithRow(1));

        var mainGrid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,320"),
            Margin = new Thickness(14),
            ColumnSpacing = 12,
        };

        mainGrid.Children.Add(leftPane.WithColumn(0));
        mainGrid.Children.Add(WrapPanel("Live Context", _contextBox).WithColumn(1));

        Content = mainGrid;
        _conversationBox.Text = BuildWelcomeMessage();
        UpdateIntegrationControls();

        Opened += (_, _) =>
        {
            SafeRefreshContext("Could not load the live game context");
            try
            {
                _inputBox.Focus();
            }
            catch
            {
                // Focus is best-effort during startup; the window should still open.
            }

            _ = RefreshLmStudioModelsAsync();
        };
    }

    public void RefreshContext()
    {
        GameAgentContextSnapshot context = _getContext();
        _contextBox.Text = BuildContextText(context);
        _statusText.Text = $"Watching {context.GameName}; {context.RecentEvents.Count} recent event(s) loaded.";
    }

    private bool SafeRefreshContext(string failurePrefix)
    {
        try
        {
            RefreshContext();
            return true;
        }
        catch (Exception ex)
        {
            _contextBox.Text = $"{failurePrefix}:\n{ex.Message}";
            _statusText.Text = failurePrefix + ".";
            return false;
        }
    }

    private void OnInputKeyDown(object? sender, KeyEventArgs e)
    {
        if ((e.Key == Key.Enter || e.Key == Key.Return) &&
            !e.KeyModifiers.HasFlag(KeyModifiers.Shift))
        {
            e.Handled = true;
            Submit();
        }
    }

    private async void Submit()
    {
        string prompt = _inputBox.Text?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(prompt))
            return;

        _inputBox.Text = string.Empty;
        GameAgentContextSnapshot context;
        try
        {
            context = _getContext();
            _contextBox.Text = BuildContextText(context);
            _statusText.Text = $"Watching {context.GameName}; {context.RecentEvents.Count} recent event(s) loaded.";
        }
        catch (Exception ex)
        {
            AppendConversation("Agent", $"Could not load the live game context:\n{ex.Message}");
            return;
        }

        AppendConversation("You", prompt);
        try
        {
            IGameAgentModel model = BuildActiveModel();
            _statusText.Text = $"Asking {GameAgentProviders.Find(GetSelectedProviderId()).Label}...";
            GameAgentModelReply reply = await model.AskAsync(new GameAgentModelRequest
            {
                Prompt = prompt,
                Context = context,
                MaxContextCharacters = _preferences.GameAgentContextLimitCharacters,
            }, CancellationToken.None);
            AppendConversation("Agent", reply.Content);
            _statusText.Text = reply.UsedExternalModel ? reply.Status : "Local observer model. Commands are disabled.";
        }
        catch (Exception ex)
        {
            AppendConversation("Agent", $"{GameAgentProviders.Find(GetSelectedProviderId()).Label} request failed: {ex.Message}");
        }
    }

    private IGameAgentModel BuildActiveModel()
    {
        SavePreferences();
        string provider = GetSelectedProviderId();
        if (provider == "local")
            return _localModel;

        return GameAgentProviders.BuildModel(BuildProviderConfig(provider), _localModel);
    }

    private async Task RefreshLmStudioModelsAsync()
    {
        string provider = GetSelectedProviderId();
        if (provider == "local")
            return;

        string selected = _modelCombo.SelectedItem?.ToString() ?? string.Empty;
        try
        {
            _statusText.Text = $"Loading {GameAgentProviders.Find(provider).Label} models...";
            IReadOnlyList<string> models = await GameAgentProviders.GetAvailableModelsAsync(
                BuildProviderConfig(provider),
                CancellationToken.None);
            if (models.Count == 0)
            {
                _statusText.Text = $"{GameAgentProviders.Find(provider).Label} returned no models.";
                return;
            }

            _modelCombo.ItemsSource = models;
            _modelCombo.SelectedItem = models.Contains(selected, StringComparer.OrdinalIgnoreCase)
                ? models.First(model => string.Equals(model, selected, StringComparison.OrdinalIgnoreCase))
                : models[0];
            SavePreferences();
            _statusText.Text = $"Loaded {models.Count} {GameAgentProviders.Find(provider).Label} model(s).";
        }
        catch (Exception ex)
        {
            _statusText.Text = $"{GameAgentProviders.Find(provider).Label} model list unavailable: {ex.Message}";
        }
    }

    private string GetSelectedProviderId()
        => _integrationCombo.SelectedItem is IntegrationChoice choice
            ? AppPreferences.NormalizeGameAgentProvider(choice.Id)
            : "lmstudio";

    private void UpdateIntegrationControls()
    {
        string provider = GetSelectedProviderId();
        _modelCombo.IsEnabled = provider != "local";
        string saved = ResolveInitialProviderModel(_preferences, provider);
        _modelCombo.ItemsSource = string.IsNullOrWhiteSpace(saved) ? [] : new[] { saved };
        _modelCombo.SelectedIndex = string.IsNullOrWhiteSpace(saved) ? -1 : 0;
        SavePreferences();
        _statusText.Text = $"{GameAgentProviders.Find(provider).Label} selected. Commands are not enabled yet.";
        if (provider != "local")
            _ = RefreshLmStudioModelsAsync();
    }

    private void SavePreferences()
    {
        try
        {
            string provider = GetSelectedProviderId();
            _preferences.GameAgentProvider = provider;

            string model = _modelCombo.SelectedItem?.ToString()?.Trim() ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(model))
                _preferences.GameAgentProviderModels[provider] = model;

            _preferences.Save();
        }
        catch
        {
            // Best-effort preference persistence.
        }
    }

    private GameAgentProviderConfig BuildProviderConfig(string provider)
    {
        provider = AppPreferences.NormalizeGameAgentProvider(provider);
        return new GameAgentProviderConfig
        {
            Provider = provider,
            Model = _modelCombo.SelectedItem?.ToString() ?? ResolveInitialProviderModel(_preferences, provider),
            Port = provider switch
            {
                "ollama" => _preferences.GameAgentOllamaPort,
                "lmstudio" => _preferences.GameAgentLmStudioPort,
                _ => 0,
            },
            ApiKey = provider switch
            {
                "openai" => _preferences.GameAgentOpenAiApiKey,
                "anthropic" => _preferences.GameAgentAnthropicApiKey,
                _ => string.Empty,
            },
        };
    }

    private static string ResolveInitialProviderModel(AppPreferences preferences, string provider)
    {
        string normalizedProvider = AppPreferences.NormalizeGameAgentProvider(provider);
        if (preferences.GameAgentProviderModels.TryGetValue(normalizedProvider, out string? saved) &&
            !string.IsNullOrWhiteSpace(saved))
        {
            return saved.Trim();
        }

        string? env = provider == "lmstudio"
            ? Environment.GetEnvironmentVariable("MTC_GAME_AGENT_LMSTUDIO_MODEL")
            : null;
        if (!string.IsNullOrWhiteSpace(env))
            return env;

        return provider switch
        {
            "ollama" => "llama3.1",
            "openai" => "gpt-4o-mini",
            "anthropic" => "claude-sonnet-4-5",
            "local" => "local-observer",
            _ => "local-model",
        };
    }

    private static IntegrationChoice FindIntegrationChoice(string provider)
        => BuildIntegrationChoices()
            .FirstOrDefault(choice => string.Equals(choice.Id, provider, StringComparison.OrdinalIgnoreCase))
           ?? new IntegrationChoice("lmstudio", "LM Studio");

    private static IReadOnlyList<IntegrationChoice> BuildIntegrationChoices()
        => GameAgentProviders.Choices
            .Select(choice => new IntegrationChoice(choice.Id, choice.Label))
            .ToArray();

    private sealed class IntegrationChoice
    {
        public IntegrationChoice(string id, string label)
        {
            Id = id;
            Label = label;
        }

        public string Id { get; }
        public string Label { get; }

        public override string ToString()
            => Label;

        public override bool Equals(object? obj)
            => obj is IntegrationChoice other &&
               string.Equals(Id, other.Id, StringComparison.OrdinalIgnoreCase);

        public override int GetHashCode()
            => StringComparer.OrdinalIgnoreCase.GetHashCode(Id);
    }

    private void SaveSnapshot()
    {
        try
        {
            GameAgentContextSnapshot context = _getContext();
            string path = GameAgentRuntime.ExportSnapshot(context);
            _statusText.Text = $"Snapshot saved: {path}";
            AppendConversation("Agent", $"Saved current observer snapshot:\n{path}");
            SafeRefreshContext("Could not refresh the live game context");
        }
        catch (Exception ex)
        {
            _statusText.Text = "Snapshot failed.";
            AppendConversation("Agent", $"Could not save the observer snapshot:\n{ex.Message}");
        }
    }

    private void ExportTrainingSample()
    {
        try
        {
            GameAgentContextSnapshot context = _getContext();
            string path = GameAgentRuntime.ExportTrainingSample(context);
            _statusText.Text = $"Training sample exported: {path}";
            AppendConversation("Agent", $"Exported offline training sample:\n{path}");
            SafeRefreshContext("Could not refresh the live game context");
        }
        catch (Exception ex)
        {
            _statusText.Text = "Training sample export failed.";
            AppendConversation("Agent", $"Could not export the training sample:\n{ex.Message}");
        }
    }

    private static TextBox BuildReadOnlyBox(string watermark)
        => new()
        {
            IsReadOnly = true,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            Watermark = watermark,
            Background = new SolidColorBrush(Color.FromRgb(0x03, 0x1d, 0x26)),
            Foreground = Brushes.Gainsboro,
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x12, 0x5e, 0x70)),
            Padding = new Thickness(8),
        };

    private static T WithControlMargin<T>(T control) where T : Control
    {
        control.Margin = new Thickness(0, 0, 8, 6);
        return control;
    }

    private static Control WrapPanel(string title, Control child)
        => new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0x0b, 0x26, 0x30)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x1b, 0x82, 0x95)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(10),
            Child = new DockPanel
            {
                Children =
                {
                    new TextBlock
                    {
                        Text = title,
                        Foreground = new SolidColorBrush(Color.FromRgb(0x00, 0xd4, 0xc9)),
                        FontSize = 16,
                        FontWeight = FontWeight.SemiBold,
                        Margin = new Thickness(0, 0, 0, 8),
                    }.WithDock(Dock.Top),
                    child,
                }
            }
        };

    private static string BuildWelcomeMessage()
        => "Agent:\nI am in observer mode. I can watch the game stream, record structured training events, and summarize the current situation. Command execution is intentionally disabled until the observer layer proves reliable.\n\n";

    private void AppendConversation(string speaker, string message)
    {
        string existing = _conversationBox.Text ?? string.Empty;
        _conversationBox.Text = existing + $"{speaker}:\n{message.Trim()}\n\n";
        _conversationBox.CaretIndex = _conversationBox.Text.Length;
    }

    internal static string BuildLocalObserverReply(string prompt, GameAgentContextSnapshot context)
    {
        string normalized = prompt.Trim().ToLowerInvariant();
        if (normalized.Contains("status", StringComparison.Ordinal) ||
            normalized.Contains("context", StringComparison.Ordinal) ||
            normalized.Contains("where am i", StringComparison.Ordinal))
        {
            return BuildStatusReply(context);
        }

        if (normalized.Contains("what happened", StringComparison.Ordinal) ||
            normalized.Contains("recent", StringComparison.Ordinal) ||
            normalized.Contains("last", StringComparison.Ordinal))
        {
            return BuildRecentEventsReply(context);
        }

        if (normalized.Contains("log", StringComparison.Ordinal) ||
            normalized.Contains("train", StringComparison.Ordinal) ||
            normalized.Contains("replay", StringComparison.Ordinal))
        {
            return $"Training events are being written as JSONL here:\n{context.EventLogPath}\n\nThat file can be replayed later into a model or test harness.";
        }

        if (normalized.Contains("risk", StringComparison.Ordinal) ||
            normalized.Contains("danger", StringComparison.Ordinal) ||
            normalized.Contains("safe", StringComparison.Ordinal))
        {
            return BuildRiskReply(context);
        }

        if (normalized.Contains("tool", StringComparison.Ordinal) ||
            normalized.Contains("command", StringComparison.Ordinal) ||
            normalized.Contains("script", StringComparison.Ordinal))
        {
            return BuildToolReply(context);
        }

        return "I can currently answer from structured MTC state and recent observed game lines. The next build step is to connect this context to a model/tool loop, then add approval-gated actions like sending commands or starting scripts.";
    }

    private static string BuildStatusReply(GameAgentContextSnapshot context)
        => $"Game: {context.GameName}\n" +
           $"Connected: {(context.Connected ? "yes" : "no")}\n" +
           $"Server: {context.Host}:{context.Port}\n" +
           $"Trader: {Display(context.TraderName)}   Corp: {(context.Corp > 0 ? context.Corp.ToString() : "-")}\n" +
           $"Sector: {Display(context.CurrentSector)}   Prompt: {Display(context.CurrentPrompt)}\n" +
           $"Credits: {context.Credits:N0}   Fighters: {context.Fighters:N0}   Shields: {context.Shields:N0}\n" +
           $"Holds: {context.HoldsEmpty:N0} empty / {context.HoldsTotal:N0} total";

    private static string BuildRecentEventsReply(GameAgentContextSnapshot context)
    {
        var events = context.RecentEvents
            .Where(evt => evt.Kind is GameAgentEventKind.ServerLine or GameAgentEventKind.ServerPrompt or GameAgentEventKind.CurrentSectorChanged or GameAgentEventKind.ShipStatus)
            .TakeLast(18)
            .ToArray();

        if (events.Length == 0)
            return "I do not have recent gameplay events yet.";

        var sb = new StringBuilder();
        sb.AppendLine("Recent gameplay events:");
        foreach (GameAgentEvent evt in events)
        {
            string text = string.IsNullOrWhiteSpace(evt.PlainText)
                ? evt.Kind.ToString()
                : evt.PlainText.Trim();
            if (text.Length > 140)
                text = text[..140] + "...";
            sb.Append(evt.Timestamp.ToLocalTime().ToString("HH:mm:ss"))
              .Append(" [")
              .Append(evt.Kind)
              .Append("] ")
              .AppendLine(text);
        }

        return sb.ToString().TrimEnd();
    }

    private static string BuildRiskReply(GameAgentContextSnapshot context)
    {
        bool lowShields = context.Shields > 0 && context.Shields < 100;
        bool lowFighters = context.Fighters > 0 && context.Fighters < 100;
        bool notConnected = !context.Connected;

        if (!lowShields && !lowFighters && !notConnected)
        {
            if (context.Hazards.Count == 0)
                return "I do not see an obvious risk from the current sidebar state or compact sector snapshot. This is still a shallow check; deeper risk detection will come from event classifiers.";

            return "Potential local hazards:\n" + string.Join("\n", context.Hazards.Select(hazard => "- " + hazard));
        }

        var sb = new StringBuilder("Potential risks:\n");
        if (notConnected)
            sb.AppendLine("- The client is not connected.");
        if (lowShields)
            sb.AppendLine($"- Shields look low: {context.Shields:N0}.");
        if (lowFighters)
            sb.AppendLine($"- Fighters look low: {context.Fighters:N0}.");
        foreach (string hazard in context.Hazards.Take(8))
            sb.Append("- ").AppendLine(hazard);
        return sb.ToString().TrimEnd();
    }

    private static string BuildToolReply(GameAgentContextSnapshot context)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Available observer tools:");
        foreach (GameAgentToolDescriptor tool in GameAgentToolRegistry.DescribeTools())
        {
            sb.Append("- ")
              .Append(tool.Name)
              .Append(tool.CanExecuteGameCommand ? " [disabled action]" : " [observer]")
              .Append(tool.RequiresApproval ? " [approval]" : string.Empty)
              .Append(": ")
              .AppendLine(tool.Description);
        }

        sb.AppendLine();
        sb.AppendLine(GameAgentToolRegistry.ObserveContext(context).Message);
        sb.AppendLine("Command execution remains disabled.");
        return sb.ToString().TrimEnd();
    }

    private static string BuildContextText(GameAgentContextSnapshot context)
    {
        var sb = new StringBuilder();
        sb.AppendLine(BuildStatusReply(context));
        sb.AppendLine();
        AppendBotSnapshot(sb, context);
        AppendScriptSnapshot(sb, context);
        AppendOnlinePlayers(sb, context);
        AppendPromptHistory(sb, context);
        AppendHazards(sb, context);
        AppendSectorSnapshot(sb, "Current sector", context.CurrentSectorDetails);
        if (context.AdjacentSectors.Count > 0)
        {
            sb.AppendLine("Adjacent sectors:");
            foreach (GameAgentSectorSnapshot sector in context.AdjacentSectors)
                sb.Append("  ").AppendLine(FormatSectorOneLine(sector));
            sb.AppendLine();
        }
        sb.AppendLine("Agent tools:");
        foreach (GameAgentToolDescriptor tool in GameAgentToolRegistry.DescribeTools())
            sb.Append("  ").Append(tool.Name).Append(tool.CanExecuteGameCommand ? " (disabled)" : string.Empty).AppendLine();
        sb.AppendLine();
        sb.AppendLine("Event log:");
        sb.AppendLine(string.IsNullOrWhiteSpace(context.EventLogPath) ? "(not created yet)" : context.EventLogPath);
        sb.AppendLine();
        sb.AppendLine("Recent events:");
        foreach (GameAgentEvent evt in context.RecentEvents.TakeLast(24))
        {
            string text = string.IsNullOrWhiteSpace(evt.PlainText) ? evt.Kind.ToString() : evt.PlainText.Trim();
            if (text.Length > 95)
                text = text[..95] + "...";
            sb.Append(evt.Timestamp.ToLocalTime().ToString("HH:mm:ss"))
              .Append(' ')
              .Append(evt.Kind)
              .Append(" | ")
              .AppendLine(text);
        }

        return sb.ToString().TrimEnd();
    }

    private static void AppendBotSnapshot(StringBuilder sb, GameAgentContextSnapshot context)
    {
        GameAgentBotSnapshot bot = context.Bot;
        if (!bot.NativeMombotRunning && string.IsNullOrWhiteSpace(bot.ExternalBotName))
            return;

        sb.AppendLine("Bot:");
        if (bot.NativeMombotRunning)
        {
            sb.Append("  Native Mombot");
            if (!string.IsNullOrWhiteSpace(bot.BotName))
                sb.Append(" ").Append(bot.BotName);
            if (!string.IsNullOrWhiteSpace(bot.Mode))
                sb.Append(" mode=").Append(bot.Mode);
            if (!string.IsNullOrWhiteSpace(bot.LastLoadedModule))
                sb.Append(" module=").Append(bot.LastLoadedModule);
            sb.Append(bot.WatcherAttached ? " watcher=attached" : " watcher=off");
            sb.AppendLine();
        }
        if (!string.IsNullOrWhiteSpace(bot.ExternalBotName))
            sb.Append("  External bot: ").AppendLine(bot.ExternalBotName);
        sb.AppendLine();
    }

    private static void AppendScriptSnapshot(StringBuilder sb, GameAgentContextSnapshot context)
    {
        if (context.RunningScripts.Count == 0)
            return;

        sb.AppendLine("Running scripts:");
        foreach (GameAgentRunningScriptSnapshot script in context.RunningScripts.Take(12))
        {
            sb.Append("  #").Append(script.Id).Append(' ').Append(script.Name);
            if (script.IsBot)
                sb.Append(" [bot]");
            if (script.IsSystemScript)
                sb.Append(" [system]");
            if (script.Paused)
                sb.Append(" [paused]");
            sb.AppendLine();
        }
        sb.AppendLine();
    }

    private static void AppendOnlinePlayers(StringBuilder sb, GameAgentContextSnapshot context)
    {
        if (context.OnlinePlayers.Count == 0)
            return;

        sb.Append("Online: ").AppendLine(string.Join(", ", context.OnlinePlayers.Take(20)));
        sb.AppendLine();
    }

    private static void AppendPromptHistory(StringBuilder sb, GameAgentContextSnapshot context)
    {
        if (context.RecentPrompts.Count == 0)
            return;

        sb.Append("Recent prompts: ").AppendLine(string.Join(" -> ", context.RecentPrompts));
        sb.AppendLine();
    }

    private static void AppendHazards(StringBuilder sb, GameAgentContextSnapshot context)
    {
        if (context.Hazards.Count == 0)
            return;

        sb.AppendLine("Hazards:");
        foreach (string hazard in context.Hazards.Take(10))
            sb.Append("  ").AppendLine(hazard);
        sb.AppendLine();
    }

    private static void AppendSectorSnapshot(StringBuilder sb, string label, GameAgentSectorSnapshot? sector)
    {
        if (sector == null)
            return;

        sb.Append(label).Append(": ").AppendLine(FormatSectorOneLine(sector));
        if (sector.Traders.Count > 0)
            sb.Append("  Traders: ").AppendLine(string.Join("; ", sector.Traders));
        if (sector.Ships.Count > 0)
            sb.Append("  Ships: ").AppendLine(string.Join("; ", sector.Ships));
        if (sector.Planets.Count > 0)
            sb.Append("  Planets: ").AppendLine(string.Join("; ", sector.Planets));
        sb.AppendLine();
    }

    private static string FormatSectorOneLine(GameAgentSectorSnapshot sector)
    {
        var details = new List<string>();
        if (!string.IsNullOrWhiteSpace(sector.Port))
            details.Add("port " + sector.Port);
        if (!string.IsNullOrWhiteSpace(sector.Fighters))
            details.Add("figs " + sector.Fighters);
        if (!string.IsNullOrWhiteSpace(sector.ArmidMines))
            details.Add("armids " + sector.ArmidMines);
        if (!string.IsNullOrWhiteSpace(sector.LimpetMines))
            details.Add("limpets " + sector.LimpetMines);
        if (sector.NavHaz > 0)
            details.Add($"haz {sector.NavHaz}%");
        if (sector.Anomaly)
            details.Add("anom");

        string suffix = details.Count > 0 ? " | " + string.Join(", ", details) : string.Empty;
        return $"{sector.Number} [{sector.Explored}] -> {string.Join(" ", sector.WarpsOut)}{suffix}";
    }

    private static string Display(string? value)
        => string.IsNullOrWhiteSpace(value) ? "-" : value.Trim();

    private static string Display(int value)
        => value > 0 ? value.ToString() : "-";
}
