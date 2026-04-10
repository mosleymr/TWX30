using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

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
        await EnsureSuccessAsync(response, cancellationToken);

        await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using JsonDocument document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

        if (document.RootElement.TryGetProperty("message", out JsonElement messageElement) &&
            messageElement.TryGetProperty("content", out JsonElement contentElement))
        {
            return contentElement.GetString()?.Trim() ?? string.Empty;
        }

        return string.Empty;
    }

    public async Task<IReadOnlyList<string>> GetInstalledModelsAsync(CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await _httpClient.GetAsync("api/tags", cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);

        await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        TagsResponse? tags = await JsonSerializer.DeserializeAsync<TagsResponse>(stream, cancellationToken: cancellationToken);
        return (tags?.Models ?? [])
            .Select(model => model.Name?.Trim())
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Cast<string>()
            .ToArray();
    }

    public void Dispose()
    {
        _httpClient.Dispose();
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
            return;

        string body = await response.Content.ReadAsStringAsync(cancellationToken);
        string message = TryExtractErrorMessage(body);
        if (string.IsNullOrWhiteSpace(message))
            message = $"HTTP {(int)response.StatusCode} ({response.ReasonPhrase})";
        throw new HttpRequestException(message, null, response.StatusCode);
    }

    private static string TryExtractErrorMessage(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
            return string.Empty;

        try
        {
            using JsonDocument document = JsonDocument.Parse(body);
            if (document.RootElement.TryGetProperty("error", out JsonElement errorElement))
                return errorElement.GetString()?.Trim() ?? string.Empty;
        }
        catch
        {
        }

        return body.Trim();
    }

    private static string NormalizeRole(string? role)
    {
        if (string.Equals(role, "assistant", StringComparison.OrdinalIgnoreCase))
            return "assistant";
        if (string.Equals(role, "system", StringComparison.OrdinalIgnoreCase))
            return "system";
        return "user";
    }

    private sealed class TagsResponse
    {
        [JsonPropertyName("models")]
        public List<TagModel>? Models { get; set; }
    }

    private sealed class TagModel
    {
        [JsonPropertyName("name")]
        public string? Name { get; set; }
    }
}
