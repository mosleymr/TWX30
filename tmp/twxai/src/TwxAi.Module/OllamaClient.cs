using System.Net.Http.Json;
using System.Text.Json;

namespace TwxAi.Module;

internal sealed class OllamaClient : IDisposable
{
    private readonly HttpClient _httpClient;

    public OllamaClient(string endpoint)
    {
        string normalized = string.IsNullOrWhiteSpace(endpoint)
            ? "http://127.0.0.1:11434/"
            : endpoint.TrimEnd('/') + "/";
        _httpClient = new HttpClient { BaseAddress = new Uri(normalized, UriKind.Absolute) };
    }

    public async Task<string> ChatAsync(
        string model,
        double temperature,
        IReadOnlyList<(string Role, string Content)> messages,
        CancellationToken cancellationToken)
    {
        var payload = new
        {
            model,
            stream = false,
            options = new
            {
                temperature,
            },
            messages = messages.Select(message => new
            {
                role = NormalizeRole(message.Role),
                content = message.Content,
            }),
        };

        using HttpResponseMessage response =
            await _httpClient.PostAsJsonAsync("api/chat", payload, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using JsonDocument document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

        if (document.RootElement.TryGetProperty("message", out JsonElement messageElement) &&
            messageElement.TryGetProperty("content", out JsonElement contentElement))
        {
            return contentElement.GetString()?.Trim() ?? string.Empty;
        }

        return string.Empty;
    }

    public void Dispose()
    {
        _httpClient.Dispose();
    }

    private static string NormalizeRole(string? role)
    {
        if (string.Equals(role, "assistant", StringComparison.OrdinalIgnoreCase))
            return "assistant";
        if (string.Equals(role, "system", StringComparison.OrdinalIgnoreCase))
            return "system";
        return "user";
    }
}
