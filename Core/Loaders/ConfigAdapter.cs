namespace BattleLuck.Core.Loaders;

/// <summary>
/// Adapter for resolving a mode's effective configuration from the
/// canonical event-only configuration root: config/BattleLuck/events/.
///
/// Supported source layout:
///   1. events/<modeId>.json        (unified single-file event definition)
///
/// Legacy mode folders are no longer consulted.
/// Results are cached in memory for O(1) access after the one-time startup load.
/// </summary>
public static class ConfigAdapter
{
    static readonly object CacheLock = new();
    static readonly Dictionary<string, ModeConfig> _effectiveCache = new(StringComparer.OrdinalIgnoreCase);

    public enum ModeSourceKind
    {
        None,
        EventsUnified
    }

    public sealed record ModeSource(string ModeId, ModeSourceKind Kind, string Path);

    public static string EventsRoot => Path.Combine(ConfigLoader.ConfigRoot, "events");

    /// <summary>Available config sources for a mode, in precedence order.</summary>
    public static IReadOnlyList<ModeSource> ResolveSources(string modeId)
    {
        var sources = new List<ModeSource>();
        var unified = Path.Combine(EventsRoot, $"{modeId}.json");

        if (File.Exists(unified))
        {
            sources.Add(new ModeSource(modeId, ModeSourceKind.EventsUnified, unified));
            BattleLuckPlugin.Log?.LogDebug($"[ConfigAdapter] Config source chosen for {modeId}: {unified}");
        }

        return sources;
    }

    /// <summary>
    /// Resolves the event-only config base path for a mode.
    /// No legacy folders are consulted.
    /// </summary>
    public static string ResolveBaseDirectory(string modeId)
    {
        return Path.Combine(EventsRoot, modeId);
    }

    /// <summary>
    /// Parses the YAML-frontmatter + narrative body of an Events prompt.txt.
    /// Format is deliberately tolerant: a leading '---' block of 'key: value' and
    /// '- list' entries, followed by a free-text body.
    /// </summary>
    public static EventPromptDefinition ParsePromptFrontmatter(string text)
    {
        var result = new EventPromptDefinition();
        if (string.IsNullOrWhiteSpace(text))
            return result;

        var lines = text.Replace("\r\n", "\n").Split('\n');
        var inFrontmatter = false;
        var frontmatterEnded = false;
        var body = new StringBuilder();
        List<string>? currentList = null;

        foreach (var raw in lines)
        {
            var line = raw.TrimEnd();

            if (!frontmatterEnded)
            {
                if (line == "---")
                {
                    if (!inFrontmatter)
                    {
                        inFrontmatter = true;
                        continue;
                    }
                    frontmatterEnded = true;
                    currentList = null;
                    continue;
                }

                if (!inFrontmatter)
                    continue;

                if (line.StartsWith("- ", StringComparison.Ordinal))
                {
                    var item = line.Substring(2).Trim();
                    if (currentList != null && item.Length > 0)
                        currentList.Add(item);
                    continue;
                }

                var colon = line.IndexOf(':');
                if (colon <= 0)
                    continue;

                var key = line.Substring(0, colon).Trim();
                var value = line.Substring(colon + 1).Trim();
                currentList = null;

                switch (key.ToLowerInvariant())
                {
                    case "eventid":
                        result.EventId = value;
                        break;
                    case "allowedactions":
                        currentList = result.AllowedActions;
                        break;
                    case "blockedactions":
                        currentList = result.BlockedActions;
                        break;
                    case "allowedtechs":
                        currentList = result.AllowedTechs;
                        break;
                }
                continue;
            }

            body.AppendLine(raw);
        }

        result.Body = body.ToString().Trim();
        return result;
    }

    static T? LoadJson<T>(string path) where T : class
    {
        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<T>(json, ConfigLoader.JsonOptions);
        }
        catch (Exception ex)
        {
            BattleLuckPlugin.LogWarning($"[ConfigAdapter] Failed to load {path}: {ex.Message}");
            return null;
        }
    }

    public static void Invalidate(string modeId)
    {
        lock (CacheLock)
        {
            _effectiveCache.Remove(modeId);
        }
    }

    public static void InvalidateAll()
    {
        lock (CacheLock)
        {
            _effectiveCache.Clear();
        }
    }
}
