using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Core = TWXProxy.Core;

namespace MTC;

internal sealed class AiAssistantWindow : Window
{
    private readonly Core.IExpansionChatModule _module;
    private readonly TextBox _conversationBox;
    private readonly TextBox _inputBox;
    private readonly TextBlock _statusText;
    private readonly Button _askButton;
    private readonly List<Core.ExpansionChatMessage> _conversation = new();
    private CancellationTokenSource? _requestCts;

    public AiAssistantWindow(Core.IExpansionChatModule module, string gameName)
    {
        _module = module;

        Title = $"{module.ChatTitle} - {gameName}";
        Width = 900;
        Height = 640;
        MinWidth = 680;
        MinHeight = 420;
        Background = new SolidColorBrush(Color.FromRgb(28, 28, 28));

        _conversationBox = new TextBox
        {
            IsReadOnly = true,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            FontFamily = new FontFamily("Cascadia Code, Menlo, Consolas, Courier New, monospace"),
            Background = new SolidColorBrush(Color.FromRgb(18, 18, 18)),
            Foreground = Brushes.Gainsboro,
            BorderBrush = new SolidColorBrush(Color.FromRgb(70, 70, 70)),
            MinHeight = 420,
        };

        _inputBox = new TextBox
        {
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            Watermark = string.IsNullOrWhiteSpace(module.ChatInputPlaceholder)
                ? "Ask a question about the current game or TWX scripting..."
                : module.ChatInputPlaceholder,
            FontFamily = new FontFamily("Cascadia Code, Menlo, Consolas, Courier New, monospace"),
            Background = new SolidColorBrush(Color.FromRgb(24, 24, 24)),
            Foreground = Brushes.Gainsboro,
            BorderBrush = new SolidColorBrush(Color.FromRgb(70, 70, 70)),
            MinHeight = 96,
        };
        _inputBox.KeyDown += OnInputKeyDown;

        _statusText = new TextBlock
        {
            Foreground = new SolidColorBrush(Color.FromRgb(180, 180, 180)),
            Text = "Ready.",
            VerticalAlignment = VerticalAlignment.Center,
        };

        _askButton = new Button
        {
            Content = "Ask",
            MinWidth = 120,
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        _askButton.Click += (_, _) => _ = SubmitAsync();

        var clearButton = new Button
        {
            Content = "Clear",
            MinWidth = 120,
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        clearButton.Click += (_, _) => ClearConversation();

        var buttonRow = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto"),
            Margin = new Thickness(0, 10, 0, 0),
        };
        Grid.SetColumn(_statusText, 0);
        Grid.SetColumn(clearButton, 1);
        Grid.SetColumn(_askButton, 2);
        buttonRow.Children.Add(_statusText);
        buttonRow.Children.Add(clearButton);
        buttonRow.Children.Add(_askButton);

        Content = new DockPanel
        {
            Margin = new Thickness(14),
            Children =
            {
                new StackPanel
                {
                    Spacing = 10,
                    Children =
                    {
                        _conversationBox,
                        _inputBox,
                        buttonRow,
                    }
                }
            }
        };

        if (!string.IsNullOrWhiteSpace(module.ChatWelcomeText))
        {
            AppendMessage("Assistant", module.ChatWelcomeText);
            _conversation.Add(new Core.ExpansionChatMessage
            {
                Role = "assistant",
                Content = module.ChatWelcomeText,
                Timestamp = DateTimeOffset.UtcNow,
            });
        }

        Closed += (_, _) =>
        {
            _requestCts?.Cancel();
            _requestCts?.Dispose();
            _requestCts = null;
        };
    }

    private void OnInputKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter &&
            e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            e.Handled = true;
            _ = SubmitAsync();
        }
    }

    private async Task SubmitAsync()
    {
        string prompt = _inputBox.Text?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(prompt))
            return;

        _inputBox.Text = string.Empty;
        _askButton.IsEnabled = false;
        _inputBox.IsEnabled = false;
        _statusText.Text = "Thinking...";

        var userMessage = new Core.ExpansionChatMessage
        {
            Role = "user",
            Content = prompt,
            Timestamp = DateTimeOffset.UtcNow,
        };
        _conversation.Add(userMessage);
        AppendMessage("You", prompt);

        _requestCts?.Cancel();
        _requestCts?.Dispose();
        _requestCts = new CancellationTokenSource();

        try
        {
            Core.ExpansionChatReply reply = await _module.AskAsync(new Core.ExpansionChatRequest
            {
                Prompt = prompt,
                Conversation = _conversation.ToList(),
            }, _requestCts.Token);

            string content = string.IsNullOrWhiteSpace(reply.Content)
                ? "(no response)"
                : reply.Content.Trim();

            _conversation.Add(new Core.ExpansionChatMessage
            {
                Role = "assistant",
                Content = content,
                Timestamp = DateTimeOffset.UtcNow,
            });
            AppendMessage(_module.DisplayName, content);
            _statusText.Text = string.IsNullOrWhiteSpace(reply.Status)
                ? (reply.IsError ? "The assistant reported an error." : "Ready.")
                : reply.Status;
        }
        catch (OperationCanceledException)
        {
            _statusText.Text = "Canceled.";
        }
        catch (Exception ex)
        {
            AppendMessage(_module.DisplayName, $"Error: {ex.Message}");
            _conversation.Add(new Core.ExpansionChatMessage
            {
                Role = "assistant",
                Content = $"Error: {ex.Message}",
                Timestamp = DateTimeOffset.UtcNow,
            });
            _statusText.Text = "The assistant failed to answer.";
        }
        finally
        {
            _askButton.IsEnabled = true;
            _inputBox.IsEnabled = true;
            _inputBox.Focus();
        }
    }

    private void ClearConversation()
    {
        _conversation.Clear();
        _conversationBox.Text = string.Empty;
        if (!string.IsNullOrWhiteSpace(_module.ChatWelcomeText))
        {
            AppendMessage("Assistant", _module.ChatWelcomeText);
            _conversation.Add(new Core.ExpansionChatMessage
            {
                Role = "assistant",
                Content = _module.ChatWelcomeText,
                Timestamp = DateTimeOffset.UtcNow,
            });
        }
        _statusText.Text = "Ready.";
    }

    private void AppendMessage(string speaker, string message)
    {
        string existing = _conversationBox.Text ?? string.Empty;
        string block = $"{speaker}:{Environment.NewLine}{message.Trim()}{Environment.NewLine}{Environment.NewLine}";
        _conversationBox.Text = existing + block;
        _conversationBox.CaretIndex = _conversationBox.Text.Length;
    }
}
