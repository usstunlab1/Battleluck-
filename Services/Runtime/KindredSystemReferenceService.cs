using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace BattleLuck.Services.Runtime;

public sealed record KindredSystemMatch(
    string Name,
    string Kind,
    string Family,
    int Score,
    string Detail);

public sealed class KindredSystemReferenceService
{
    const int MaxItemsPerKind = 5000;
    readonly string _referencePath;
    readonly object _lock = new();
    DateTime _loadedWriteTimeUtc;
    List<KindredSystemMatch> _items = new();

    public KindredSystemReferenceService(string? referencePath = null)
    {
        _referencePath = referencePath ?? Path.Combine(
            ConfigLoader.ConfigRoot,
            "kindredextract-reference.json");

        if (File.Exists(_referencePath))
            return;

        _referencePath = Path.Combine(
            AppContext.BaseDirectory,
            "docs",
            "reference",
            "kindredextract-reference.json");

        if (!File.Exists(_referencePath))
        {
            var cwdPath = Path.Combine(
                Directory.GetCurrentDirectory(),
                "docs",
                "reference",
                "kindredextract-reference.json");
            if (File.Exists(cwdPath))
                _referencePath = cwdPath;
        }
    }

    public IReadOnlyList<KindredSystemMatch> Search(string description, int limit = 8)
    {
        EnsureLoaded();

        var terms = Tokenize(description).ToArray();
        if (terms.Length == 0)
            return Array.Empty<KindredSystemMatch>();

        lock (_lock)
        {
            return _items
                .Select(item => item with { Score = Score(item, terms) })
                .Where(item => item.Score > 0)
                .OrderByDescending(item => item.Score)
                .ThenBy(item => item.Kind)
                .ThenBy(item => item.Name)
                .Take(Math.Clamp(limit, 1, 20))
                .ToList();
        }
    }

    /// <summary>
    /// Resolves an exact ECS system type from the checked-in KindredExtract
    /// inventory. Live registration deliberately uses an exact match so a
    /// typo or an LLM-invented type cannot become a runtime capability.
    /// </summary>
    public bool TryGetSystem(string systemType, out KindredSystemMatch match)
    {
        match = default!;
        if (string.IsNullOrWhiteSpace(systemType))
            return false;

        EnsureLoaded();
        var requested = systemType.Trim();
        lock (_lock)
        {
            var found = _items.FirstOrDefault(item =>
                item.Kind.Equals("system", StringComparison.OrdinalIgnoreCase) &&
                item.Name.Equals(requested, StringComparison.OrdinalIgnoreCase));
            if (found == null)
                return false;

            match = found;
            return true;
        }
    }

    public string BuildAiPrompt(string description, IReadOnlyList<KindredSystemMatch> matches)
    {
        var sb = new StringBuilder();
        sb.AppendLine("You are BattleLuck's V Rising modding assistant.");
        sb.AppendLine("An admin described the game system they want. Explain which ProjectM/ECS/Unity/EOS/network system or component names are most relevant.");
        sb.AppendLine("Do not invent names. Use only the candidates below. Keep it short and actionable.");
        sb.AppendLine();
        sb.AppendLine($"Admin description: {description}");
        sb.AppendLine();
        sb.AppendLine("Candidates:");
        foreach (var match in matches.Take(10))
            sb.AppendLine($"- {match.Name} | kind={match.Kind} | family={match.Family} | detail={match.Detail}");
        sb.AppendLine();
        sb.AppendLine("Return: best match first, why it fits, and what BattleLuck code should inspect next.");
        return sb.ToString();
    }

    public string FormatMatches(IReadOnlyList<KindredSystemMatch> matches)
    {
        if (matches.Count == 0)
            return "No matching ECS/ProjectM/Unity system found in the KindredExtract reference.";

        var sb = new StringBuilder();
        foreach (var match in matches.Take(8))
            sb.AppendLine($"{match.Name} [{match.Kind}/{match.Family}] score={match.Score}");
        return sb.ToString().TrimEnd();
    }

    void EnsureLoaded()
    {
        if (!File.Exists(_referencePath))
            return;

        var writeTime = File.GetLastWriteTimeUtc(_referencePath);
        lock (_lock)
        {
            if (_items.Count > 0 && writeTime == _loadedWriteTimeUtc)
                return;

            _items = LoadItems(_referencePath);
            _loadedWriteTimeUtc = writeTime;
        }
    }

    static List<KindredSystemMatch> LoadItems(string path)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        var root = doc.RootElement;
        var items = new List<KindredSystemMatch>();

        if (root.TryGetProperty("references", out var refs) && refs.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in refs.EnumerateArray())
            {
                var name = Text(item, "name");
                if (string.IsNullOrWhiteSpace(name)) continue;
                var family = Text(item, "family", "Assembly");
                var hint = Text(item, "hintPath");
                items.Add(new KindredSystemMatch(name, "assembly", family, 0, hint));
            }
        }

        AddStringArray(root, "systemTypes", "system", items);
        AddStringArray(root, "componentExtractorTypes", "component", items);

        return items
            .GroupBy(i => $"{i.Kind}:{i.Name}", StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToList();
    }

    static void AddStringArray(JsonElement root, string property, string kind, List<KindredSystemMatch> items)
    {
        if (!root.TryGetProperty(property, out var array) || array.ValueKind != JsonValueKind.Array)
            return;

        var count = 0;
        foreach (var value in array.EnumerateArray())
        {
            if (++count > MaxItemsPerKind)
                break;

            var name = value.GetString();
            if (string.IsNullOrWhiteSpace(name))
                continue;

            items.Add(new KindredSystemMatch(
                name,
                kind,
                InferFamily(name),
                0,
                ShortName(name)));
        }
    }

    static int Score(KindredSystemMatch item, string[] terms)
    {
        var haystack = $"{item.Name} {item.Kind} {item.Family} {item.Detail}".ToLowerInvariant();
        var name = item.Name.ToLowerInvariant();
        var score = 0;

        foreach (var term in terms)
        {
            if (name.Equals(term, StringComparison.OrdinalIgnoreCase))
                score += 50;
            else if (name.Contains(term))
                score += 20;
            else if (haystack.Contains(term))
                score += 8;
        }

        if (terms.Any(t => t is "eos" or "epic") && haystack.Contains("eos"))
            score += 40;
        if (terms.Any(t => t is "network" or "connect" or "disconnect" or "player") && haystack.Contains("network"))
            score += 20;
        if (terms.Any(t => t is "castle" or "territory" or "building") && haystack.Contains("castle"))
            score += 25;
        if (terms.Any(t => t is "boss" or "v blood" or "vblood") && haystack.Contains("vblood"))
            score += 25;
        if (terms.Any(t => t is "prefab" or "guid" or "spawn") && haystack.Contains("prefab"))
            score += 20;

        return score;
    }

    static IEnumerable<string> Tokenize(string input)
    {
        foreach (Match match in Regex.Matches(input.ToLowerInvariant(), "[a-z0-9_.]+"))
        {
            var token = match.Value.Trim();
            if (token.Length < 3)
                continue;
            if (token is "the" or "and" or "for" or "with" or "that" or "this" or "want" or "system")
                continue;
            yield return token;
        }
    }

    static string Text(JsonElement item, string property, string fallback = "")
    {
        return item.TryGetProperty(property, out var value) ? value.GetString() ?? fallback : fallback;
    }

    static string InferFamily(string name)
    {
        if (name.StartsWith("ProjectM.Network.", StringComparison.OrdinalIgnoreCase)) return "ProjectM.Network";
        if (name.StartsWith("ProjectM.", StringComparison.OrdinalIgnoreCase)) return "ProjectM";
        if (name.StartsWith("Unity.", StringComparison.OrdinalIgnoreCase)) return "Unity";
        if (name.StartsWith("Stunlock.", StringComparison.OrdinalIgnoreCase)) return "Stunlock";
        if (name.Contains("Eos", StringComparison.OrdinalIgnoreCase)) return "EOS";
        return "ECS";
    }

    static string ShortName(string name)
    {
        var index = name.LastIndexOf('.');
        return index >= 0 && index < name.Length - 1 ? name[(index + 1)..] : name;
    }
}
