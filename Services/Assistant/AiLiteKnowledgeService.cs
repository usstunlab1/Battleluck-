using System.Reflection;
using System.Text.Json;

namespace BattleLuck.Services.Assistant;

public sealed class AiLiteKnowledgeService
{
    sealed class KnowledgeDocument { public int Schema { get; set; } public List<KnowledgeEntry> Entries { get; set; } = new(); }
    sealed class KnowledgeEntry { public string Id { get; set; } = ""; public List<string> Terms { get; set; } = new(); public string Answer { get; set; } = ""; }

    readonly IReadOnlyList<KnowledgeEntry> _entries;

    public AiLiteKnowledgeService() => _entries = LoadEntries();

    public string Answer(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return "Ask about events, results, configuration, local AI, or NPC simulation with .ai request <text>.";

        var tokens = Tokenize(query);
        var best = _entries.Select(entry => new
        {
            Entry = entry,
            Score = entry.Terms.Sum(term => ScoreTerm(term, tokens, query))
        }).OrderByDescending(match => match.Score).ThenBy(match => match.Entry.Id, StringComparer.Ordinal).FirstOrDefault();

        return best == null || best.Score <= 0
            ? "I could not match that safely. Ask with .ai request <text> about events, results, configuration, local AI, or NPC simulation."
            : best.Entry.Answer;
    }

    static int ScoreTerm(string term, ISet<string> tokens, string query)
    {
        var normalized = term.Trim().ToLowerInvariant();
        if (tokens.Contains(normalized)) return 4;
        if (query.Contains(normalized, StringComparison.OrdinalIgnoreCase)) return 2;
        return tokens.Any(token => Levenshtein(token, normalized) <= 1) ? 1 : 0;
    }

    static HashSet<string> Tokenize(string value) => value.ToLowerInvariant()
        .Split(new[] { ' ', '\t', '\r', '\n', '.', ',', ':', ';', '?', '!', '/', '\\', '-', '_' },
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToHashSet(StringComparer.OrdinalIgnoreCase);

    static IReadOnlyList<KnowledgeEntry> LoadEntries()
    {
        try
        {
            var assembly = typeof(AiLiteKnowledgeService).Assembly;
            var resource = assembly.GetManifestResourceNames().FirstOrDefault(name =>
                name.EndsWith("config.BattleLuck.knowledge.ai-lite.json", StringComparison.OrdinalIgnoreCase));
            if (resource == null) return Array.Empty<KnowledgeEntry>();
            using var stream = assembly.GetManifestResourceStream(resource);
            if (stream == null) return Array.Empty<KnowledgeEntry>();
            var document = JsonSerializer.Deserialize<KnowledgeDocument>(stream, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
            return document?.Entries.Where(entry => !string.IsNullOrWhiteSpace(entry.Answer)).ToArray()
                   ?? Array.Empty<KnowledgeEntry>();
        }
        catch { return Array.Empty<KnowledgeEntry>(); }
    }

    static int Levenshtein(string left, string right)
    {
        if (Math.Abs(left.Length - right.Length) > 1) return 2;
        var previous = Enumerable.Range(0, right.Length + 1).ToArray();
        for (var i = 1; i <= left.Length; i++)
        {
            var current = new int[right.Length + 1]; current[0] = i;
            for (var j = 1; j <= right.Length; j++)
                current[j] = Math.Min(Math.Min(current[j - 1] + 1, previous[j] + 1),
                    previous[j - 1] + (left[i - 1] == right[j - 1] ? 0 : 1));
            previous = current;
        }
        return previous[right.Length];
    }
}
