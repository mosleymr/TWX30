using System.Text;
using TWXProxy.Core;

namespace TwxAi.Module;

public sealed class TwxAiAssistantModule : IExpansionChatModule, IDisposable
{
    private readonly object _sync = new();
    private readonly List<string> _recentGameplay = new();
    private readonly StringBuilder _incomingBuffer = new();

    private ExpansionModuleContext? _context;
    private TwxAiConfig? _config;
    private OllamaClient? _ollama;
    private ScriptReferenceKnowledgeBase? _knowledgeBase;
    private string _transcriptPath = string.Empty;

    public string Id => "twxai-ollama";
    public string DisplayName => "TWX AI Assistant";
    public ExpansionHostTargets SupportedHosts => ExpansionHostTargets.Any;
    public string ChatTitle => "TWX AI Assistant";
    public string ChatWelcomeText =>
        "Ask about TWX scripting, the current game state, or recent gameplay. I ground answers from script.html and the live session transcript.";
    public string ChatInputPlaceholder =>
        "Ask about script commands, gameplay, bot behavior, or what just happened...";

    public async Task InitializeAsync(ExpansionModuleContext context, CancellationToken cancellationToken)
    {
        _context = context;

        Directory.CreateDirectory(context.ModuleDataDirectory);
        Directory.CreateDirectory(Path.Combine(context.ModuleDataDirectory, "knowledge"));

        _config = await TwxAiConfig.LoadOrCreateAsync(
            Path.Combine(context.ModuleDataDirectory, "twxai.json"),
            cancellationToken);
        _knowledgeBase = await ScriptReferenceKnowledgeBase.LoadAsync(context, cancellationToken);
        _ollama = new OllamaClient(_config.Endpoint);
        _transcriptPath = Path.Combine(context.ModuleDataDirectory, "gameplay.log");

        SeedRecentGameplay();

        context.GameInstance.Connected += OnConnected;
        context.GameInstance.Disconnected += OnDisconnected;
        context.GameInstance.ServerDataReceived += OnServerDataReceived;

        context.Log($"AI assistant initialized. Model={_config.Model}, knowledgeChunks={_knowledgeBase.Count}");
    }

    public Task ShutdownAsync(CancellationToken cancellationToken)
    {
        if (_context != null)
        {
            _context.GameInstance.Connected -= OnConnected;
            _context.GameInstance.Disconnected -= OnDisconnected;
            _context.GameInstance.ServerDataReceived -= OnServerDataReceived;
            _context.Log("AI assistant shut down.");
            _context = null;
        }

        Dispose();
        return Task.CompletedTask;
    }

    public async Task<ExpansionChatReply> AskAsync(ExpansionChatRequest request, CancellationToken cancellationToken)
    {
        if (_context == null || _config == null || _ollama == null)
        {
            return new ExpansionChatReply
            {
                Content = "The AI assistant has not finished initializing yet.",
                IsError = true,
                Status = "Not initialized",
            };
        }

        string prompt = request.Prompt?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(prompt))
        {
            return new ExpansionChatReply
            {
                Content = "Please enter a question first.",
                IsError = true,
                Status = "Empty prompt",
            };
        }

        try
        {
            var referenceChunks = _knowledgeBase?.Search(prompt, _config.MaxReferenceSnippets) ?? Array.Empty<KnowledgeChunk>();
            List<string> recentGameplay = GetRecentGameplay(_config.MaxRecentGameplayLines);
            List<(string Role, string Content)> messages = BuildMessages(request, referenceChunks, recentGameplay);

            string answer = await _ollama.ChatAsync(
                _config.Model,
                _config.Temperature,
                messages,
                cancellationToken);

            if (string.IsNullOrWhiteSpace(answer))
                answer = "I did not receive a response from Ollama.";

            return new ExpansionChatReply
            {
                Content = answer.Trim(),
                Status = $"Model {_config.Model} · {referenceChunks.Count} refs · {recentGameplay.Count} gameplay lines",
            };
        }
        catch (Exception ex)
        {
            _context.Log($"Ask failed: {ex.Message}");
            return new ExpansionChatReply
            {
                Content = $"I could not reach the local model. {ex.Message}",
                IsError = true,
                Status = $"Ollama error at {_config.Endpoint}",
            };
        }
    }

    public void Dispose()
    {
        _ollama?.Dispose();
        _ollama = null;
    }

    private void OnConnected(object? sender, EventArgs e)
    {
        RecordGameplayLine("[connected]");
    }

    private void OnDisconnected(object? sender, DisconnectEventArgs e)
    {
        RecordGameplayLine($"[disconnected] {e.Reason}");
    }

    private void OnServerDataReceived(object? sender, DataReceivedEventArgs e)
    {
        string text = AnsiCodes.StripANSI(e.Text).Replace("\0", string.Empty, StringComparison.Ordinal);
        if (string.IsNullOrEmpty(text))
            return;

        lock (_sync)
        {
            _incomingBuffer.Append(text);
            string buffered = _incomingBuffer.ToString();
            int start = 0;

            while (true)
            {
                int newline = buffered.IndexOf('\n', start);
                if (newline < 0)
                    break;

                string line = buffered[start..newline].Replace("\r", string.Empty, StringComparison.Ordinal).Trim();
                if (!string.IsNullOrWhiteSpace(line))
                    RecordGameplayLineInternal(line);

                start = newline + 1;
            }

            _incomingBuffer.Clear();
            if (start < buffered.Length)
                _incomingBuffer.Append(buffered[start..]);
        }
    }

    private List<(string Role, string Content)> BuildMessages(
        ExpansionChatRequest request,
        IReadOnlyList<KnowledgeChunk> referenceChunks,
        IReadOnlyList<string> recentGameplay)
    {
        var messages = new List<(string Role, string Content)>();
        string promptHeader = BuildSystemContext(referenceChunks, recentGameplay);
        messages.Add(("system", promptHeader));

        IEnumerable<ExpansionChatMessage> conversation = request.Conversation
            .Where(message => !string.IsNullOrWhiteSpace(message.Content))
            .TakeLast(Math.Max(1, _config?.MaxConversationMessages ?? 12));

        foreach (ExpansionChatMessage message in conversation)
        {
            string role = string.Equals(message.Role, "assistant", StringComparison.OrdinalIgnoreCase)
                ? "assistant"
                : "user";
            messages.Add((role, message.Content.Trim()));
        }

        if (!conversation.Any())
            messages.Add(("user", request.Prompt.Trim()));

        return messages;
    }

    private string BuildSystemContext(
        IReadOnlyList<KnowledgeChunk> referenceChunks,
        IReadOnlyList<string> recentGameplay)
    {
        var builder = new StringBuilder();
        builder.AppendLine(_config?.SystemPrompt ?? string.Empty);
        builder.AppendLine();

        if (_context != null)
        {
            builder.AppendLine($"Host: {_context.HostName}");
            builder.AppendLine($"Game: {_context.GameName}");
            builder.AppendLine($"Scripts: {_context.ScriptDirectory}");
            builder.AppendLine();
        }

        if (referenceChunks.Count > 0)
        {
            builder.AppendLine("Reference material:");
            builder.AppendLine();
            foreach (KnowledgeChunk chunk in referenceChunks)
            {
                builder.AppendLine($"[{chunk.Title}]");
                builder.AppendLine(chunk.Content);
                builder.AppendLine();
            }
        }

        if (recentGameplay.Count > 0)
        {
            builder.AppendLine("Recent gameplay:");
            builder.AppendLine();
            foreach (string line in recentGameplay)
                builder.AppendLine(line);
            builder.AppendLine();
        }

        builder.AppendLine("Answer clearly and keep Tradewars/TWX terminology accurate.");
        return builder.ToString().Trim();
    }

    private List<string> GetRecentGameplay(int maxLines)
    {
        lock (_sync)
        {
            int skip = Math.Max(0, _recentGameplay.Count - Math.Max(1, maxLines));
            return _recentGameplay.Skip(skip).ToList();
        }
    }

    private void SeedRecentGameplay()
    {
        if (string.IsNullOrWhiteSpace(_transcriptPath) || !File.Exists(_transcriptPath))
            return;

        string[] lines = File.ReadAllLines(_transcriptPath);
        int keep = Math.Max(1, _config?.MaxRecentGameplayLines ?? 60);
        foreach (string line in lines.TakeLast(keep))
        {
            string trimmed = line.Trim();
            if (!string.IsNullOrWhiteSpace(trimmed))
                _recentGameplay.Add(trimmed);
        }
    }

    private void RecordGameplayLine(string line)
    {
        lock (_sync)
        {
            RecordGameplayLineInternal(line);
        }
    }

    private void RecordGameplayLineInternal(string line)
    {
        string trimmed = line.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
            return;

        _recentGameplay.Add(trimmed);
        int maxLines = Math.Max(20, _config?.MaxRecentGameplayLines ?? 60);
        if (_recentGameplay.Count > maxLines * 4)
            _recentGameplay.RemoveRange(0, _recentGameplay.Count - (maxLines * 4));

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_transcriptPath) ?? Directory.GetCurrentDirectory());
            File.AppendAllText(
                _transcriptPath,
                $"{DateTimeOffset.Now:O}\t{trimmed}{Environment.NewLine}",
                Encoding.UTF8);
        }
        catch
        {
            // Best-effort transcript logging only.
        }
    }
}
