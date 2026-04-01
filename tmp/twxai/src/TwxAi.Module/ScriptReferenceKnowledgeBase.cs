using System.Net;
using System.Text.RegularExpressions;
using TWXProxy.Core;

namespace TwxAi.Module;

internal sealed record KnowledgeChunk(
    string Source,
    string Title,
    string Content,
    string SearchText);

internal sealed class ScriptReferenceKnowledgeBase
{
    private static readonly Regex AnchorRegex =
        new(@"<a[^>]*name\s*=\s*[""']?(?<name>[^""'>\s]+)[^>]*>", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex TagRegex =
        new(@"<[^>]+>", RegexOptions.Compiled | RegexOptions.Singleline);

    private static readonly Regex TokenRegex =
        new(@"[a-z0-9_]+", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private readonly List<KnowledgeChunk> _chunks = new();

    public int Count => _chunks.Count;

    public static async Task<ScriptReferenceKnowledgeBase> LoadAsync(
        ExpansionModuleContext context,
        CancellationToken cancellationToken)
    {
        var knowledgeBase = new ScriptReferenceKnowledgeBase();

        string scriptReferencePath = Path.Combine(context.ScriptDirectory, "script.html");
        if (File.Exists(scriptReferencePath))
            await knowledgeBase.AddHtmlAsync(scriptReferencePath, cancellationToken);

        await knowledgeBase.LoadOptionalDirectoryAsync(Path.Combine(context.ModuleDirectory, "knowledge"), cancellationToken);
        await knowledgeBase.LoadOptionalDirectoryAsync(Path.Combine(context.ModuleDataDirectory, "knowledge"), cancellationToken);

        return knowledgeBase;
    }

    public IReadOnlyList<KnowledgeChunk> Search(string query, int maxResults)
    {
        if (string.IsNullOrWhiteSpace(query) || _chunks.Count == 0)
            return Array.Empty<KnowledgeChunk>();

        string loweredQuery = query.ToLowerInvariant();
        string[] tokens = Tokenize(query);

        return _chunks
            .Select(chunk => new
            {
                Chunk = chunk,
                Score = ScoreChunk(chunk, loweredQuery, tokens),
            })
            .Where(entry => entry.Score > 0)
            .OrderByDescending(entry => entry.Score)
            .ThenBy(entry => entry.Chunk.Title, StringComparer.OrdinalIgnoreCase)
            .Take(Math.Max(1, maxResults))
            .Select(entry => entry.Chunk)
            .ToArray();
    }

    private async Task AddHtmlAsync(string path, CancellationToken cancellationToken)
    {
        string html = await File.ReadAllTextAsync(path, cancellationToken);
        MatchCollection matches = AnchorRegex.Matches(html);

        if (matches.Count == 0)
        {
            AddChunk(path, Path.GetFileName(path), StripHtml(html));
            return;
        }

        for (int i = 0; i < matches.Count; i++)
        {
            int start = matches[i].Index;
            int end = i + 1 < matches.Count ? matches[i + 1].Index : html.Length;
            string rawSegment = html[start..end];
            string stripped = StripHtml(rawSegment);
            if (string.IsNullOrWhiteSpace(stripped))
                continue;

            string title = stripped
                .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .FirstOrDefault() ?? matches[i].Groups["name"].Value;
            AddChunk(path, title, stripped);
        }
    }

    private async Task LoadOptionalDirectoryAsync(string directory, CancellationToken cancellationToken)
    {
        if (!Directory.Exists(directory))
            return;

        foreach (string path in Directory.EnumerateFiles(directory, "*.*", SearchOption.TopDirectoryOnly)
                     .Where(IsKnowledgeFile)
                     .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            string extension = Path.GetExtension(path);
            if (extension.Equals(".html", StringComparison.OrdinalIgnoreCase) ||
                extension.Equals(".htm", StringComparison.OrdinalIgnoreCase))
            {
                await AddHtmlAsync(path, cancellationToken);
                continue;
            }

            string text = await File.ReadAllTextAsync(path, cancellationToken);
            AddChunk(path, Path.GetFileNameWithoutExtension(path), text);
        }
    }

    private void AddChunk(string source, string title, string content)
    {
        string normalized = NormalizeWhitespace(content);
        if (string.IsNullOrWhiteSpace(normalized))
            return;

        _chunks.Add(new KnowledgeChunk(
            source,
            string.IsNullOrWhiteSpace(title) ? Path.GetFileName(source) : title.Trim(),
            normalized,
            normalized.ToLowerInvariant()));
    }

    private static bool IsKnowledgeFile(string path)
    {
        string extension = Path.GetExtension(path);
        return extension.Equals(".md", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".txt", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".html", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".htm", StringComparison.OrdinalIgnoreCase);
    }

    private static int ScoreChunk(KnowledgeChunk chunk, string loweredQuery, IReadOnlyList<string> tokens)
    {
        int score = 0;

        if (chunk.Title.Contains(loweredQuery, StringComparison.OrdinalIgnoreCase))
            score += 25;

        foreach (string token in tokens)
        {
            if (token.Length < 2)
                continue;

            if (chunk.Title.Contains(token, StringComparison.OrdinalIgnoreCase))
                score += 8;
            if (chunk.SearchText.Contains(token, StringComparison.Ordinal))
                score += 2;
        }

        return score;
    }

    private static string[] Tokenize(string value)
    {
        return TokenRegex
            .Matches(value.ToLowerInvariant())
            .Select(match => match.Value)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }

    private static string StripHtml(string html)
    {
        string withoutTags = TagRegex.Replace(html, " ");
        string decoded = WebUtility.HtmlDecode(withoutTags);
        return NormalizeWhitespace(AnsiCodes.StripANSI(decoded));
    }

    private static string NormalizeWhitespace(string text)
    {
        return string.Join(
            '\n',
            text.Replace("\r", "\n", StringComparison.Ordinal)
                .Split('\n', StringSplitOptions.TrimEntries)
                .Where(line => !string.IsNullOrWhiteSpace(line)));
    }
}
