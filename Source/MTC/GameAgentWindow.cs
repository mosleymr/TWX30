using System;
using System.Linq;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace MTC;

internal sealed class GameAgentWindow : Window
{
    private readonly Func<GameAgentContextSnapshot> _getContext;
    private readonly TextBox _conversationBox;
    private readonly TextBox _inputBox;
    private readonly TextBox _contextBox;
    private readonly TextBlock _statusText;

    public GameAgentWindow(Func<GameAgentContextSnapshot> getContext)
    {
        _getContext = getContext;

        Title = "Game Agent";
        Width = 1050;
        Height = 720;
        MinWidth = 780;
        MinHeight = 520;
        Background = new SolidColorBrush(Color.FromRgb(0x07, 0x12, 0x17));
        FontFamily = new FontFamily("Cascadia Code, Menlo, Consolas, Courier New, monospace");

        _conversationBox = BuildReadOnlyBox("The game agent conversation will appear here.");
        _contextBox = BuildReadOnlyBox("Live game context will appear here.");
        _contextBox.MinWidth = 310;

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
        _inputBox.KeyDown += OnInputKeyDown;

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
        };

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
            Children = { refreshButton, clearButton, askButton },
        };

        var bottomRow = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            Margin = new Thickness(0, 8, 0, 0),
        };
        bottomRow.Children.Add(_statusText);
        Grid.SetColumn(buttons, 1);
        bottomRow.Children.Add(buttons);

        var mainGrid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,330"),
            RowDefinitions = new RowDefinitions("*,Auto,Auto"),
            Margin = new Thickness(14),
            ColumnSpacing = 12,
            RowSpacing = 10,
        };

        mainGrid.Children.Add(WrapPanel("Conversation", _conversationBox).WithColumn(0).WithRow(0));
        Control contextPanel = WrapPanel("Live Context", _contextBox).WithColumn(1).WithRow(0);
        Grid.SetRowSpan(contextPanel, 3);
        mainGrid.Children.Add(contextPanel);
        mainGrid.Children.Add(_inputBox.WithColumn(0).WithRow(1));
        mainGrid.Children.Add(bottomRow.WithColumn(0).WithRow(2));

        Content = mainGrid;
        _conversationBox.Text = BuildWelcomeMessage();

        Opened += (_, _) =>
        {
            RefreshContext();
            _inputBox.Focus();
        };
    }

    public void RefreshContext()
    {
        GameAgentContextSnapshot context = _getContext();
        _contextBox.Text = BuildContextText(context);
        _statusText.Text = $"Watching {context.GameName}; {context.RecentEvents.Count} recent event(s) loaded.";
    }

    private void OnInputKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            e.Handled = true;
            Submit();
        }
    }

    private void Submit()
    {
        string prompt = _inputBox.Text?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(prompt))
            return;

        _inputBox.Text = string.Empty;
        RefreshContext();

        GameAgentContextSnapshot context = _getContext();
        AppendConversation("You", prompt);
        AppendConversation("Agent", BuildLocalObserverReply(prompt, context));
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

    private static string BuildLocalObserverReply(string prompt, GameAgentContextSnapshot context)
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
            return "I do not see an obvious risk from the current sidebar state. This is still a shallow check; deeper risk detection will come from event classifiers.";

        var sb = new StringBuilder("Potential risks:\n");
        if (notConnected)
            sb.AppendLine("- The client is not connected.");
        if (lowShields)
            sb.AppendLine($"- Shields look low: {context.Shields:N0}.");
        if (lowFighters)
            sb.AppendLine($"- Fighters look low: {context.Fighters:N0}.");
        return sb.ToString().TrimEnd();
    }

    private static string BuildContextText(GameAgentContextSnapshot context)
    {
        var sb = new StringBuilder();
        sb.AppendLine(BuildStatusReply(context));
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

    private static string Display(string? value)
        => string.IsNullOrWhiteSpace(value) ? "-" : value.Trim();

    private static string Display(int value)
        => value > 0 ? value.ToString() : "-";
}
