using System.Text.Json;

namespace TwxAi.Module;

internal sealed class TwxAiConfig
{
    public string Endpoint { get; set; } =
        (Environment.GetEnvironmentVariable("OLLAMA_HOST") ?? "http://127.0.0.1:11434").TrimEnd('/');

    public string Model { get; set; } =
        Environment.GetEnvironmentVariable("TWXAI_OLLAMA_MODEL") ?? "llama3.2";

    public int MaxReferenceSnippets { get; set; } = 6;
    public int MaxRecentGameplayLines { get; set; } = 60;
    public int MaxConversationMessages { get; set; } = 12;
    public double Temperature { get; set; } = 0.2;
    public string SystemPrompt { get; set; } =
        "You are a Tradewars 2002 and TWX Proxy assistant. Answer using the supplied TWX scripting reference and live gameplay context when possible. If the answer is uncertain, say what you do and do not know. Prefer practical, concrete explanations.";

    public static async Task<TwxAiConfig> LoadOrCreateAsync(string path, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? Directory.GetCurrentDirectory());

        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true,
        };

        if (File.Exists(path))
        {
            await using FileStream stream = File.OpenRead(path);
            TwxAiConfig? config = await JsonSerializer.DeserializeAsync<TwxAiConfig>(stream, options, cancellationToken);
            return config ?? new TwxAiConfig();
        }

        var created = new TwxAiConfig();
        await using (FileStream stream = File.Create(path))
        {
            await JsonSerializer.SerializeAsync(stream, created, options, cancellationToken);
        }

        return created;
    }

    public async Task SaveAsync(string path, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? Directory.GetCurrentDirectory());

        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true,
        };

        await using FileStream stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, this, options, cancellationToken);
    }
}
