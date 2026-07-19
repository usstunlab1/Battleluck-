using System.Globalization;
using System.Text.Json;

namespace BattleLuck.Services.Runtime;

/// <summary>
/// Validates explicit ECS references in declarative event files against the
/// checked-in KindredExtract component/system reference lists. These exports
/// are candidates, not proof that the target server has the type/prefab. A
/// live PrefabHelper lookup is still attempted for prefabs, and every failure
/// includes the source file and JSONPath. The separate sequence UUID catalog
/// admits only entries explicitly verified in-game.
/// </summary>
public static class EventReferenceAllowlistValidator
{
    static readonly string[] SystemProperties = { "systemType", "systemName" };
    static readonly string[] ComponentProperties = { "componentType", "componentName" };
    static readonly string[] PrefabProperties =
    {
        "prefab", "prefabName", "wallPrefab", "floorPrefab", "tilePrefab",
        "containerPrefab", "sequencePrefab", "buffPrefab", "itemPrefab",
        "abilityPrefab", "weaponPrefab", "npcPrefab", "bossPrefab"
    };

    public static EventReferenceValidationResult Validate(IReadOnlyDictionary<string, string> files)
    {
        var result = new EventReferenceValidationResult();
        var allowlists = LoadAllowlists();

        foreach (var file in new[] { "event.json", "zones.json", "kits.json" })
        {
            if (!files.TryGetValue(file, out var text) || string.IsNullOrWhiteSpace(text))
                continue;

            try
            {
                using var document = JsonDocument.Parse(text);
                Visit(document.RootElement, "$", file, allowlists, result);
            }
            catch (JsonException)
            {
                // EJSONPARSE is emitted by EventSchemaValidator; do not mask it.
            }
        }

        if (files.TryGetValue("prompt.txt", out var prompt))
            ValidateMarkers(prompt, "prompt.txt", "$", result);

        return result;
    }

    static void Visit(
        JsonElement element,
        string path,
        string file,
        AllowlistSet allowlists,
        EventReferenceValidationResult result)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    var childPath = path + "." + property.Name;
                    if (property.Value.ValueKind == JsonValueKind.String)
                    {
                        var value = property.Value.GetString()?.Trim() ?? "";
                        if (Matches(property.Name, SystemProperties))
                            ValidateExact(value, allowlists.Systems, "system", file, childPath, result);
                        else if (Matches(property.Name, ComponentProperties))
                            ValidateExact(value, allowlists.Components, "component", file, childPath, result);
                        else if (IsPrefabProperty(property.Name))
                            ValidatePrefab(value, file, childPath, allowlists, result);

                        ValidateActionParameters(value, file, childPath, allowlists, result);
                        ValidateMarkers(value, file, childPath, result);
                    }

                    Visit(property.Value, childPath, file, allowlists, result);
                }
                break;

            case JsonValueKind.Array:
                var index = 0;
                foreach (var item in element.EnumerateArray())
                {
                    Visit(item, $"{path}[{index}]", file, allowlists, result);
                    index++;
                }
                break;
        }
    }

    static void ValidateActionParameters(
        string value,
        string file,
        string path,
        AllowlistSet allowlists,
        EventReferenceValidationResult result)
    {
        // Flow actions use key=value|key=value notation. Only inspect explicit
        // system/component/prefab keys; prose and prompts remain untouched.
        if (!value.Contains('=', StringComparison.Ordinal))
            return;

        foreach (var token in value.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var separator = token.IndexOf('=');
            if (separator <= 0 || separator == token.Length - 1)
                continue;

            var key = token[..separator].Trim();
            var parameter = token[(separator + 1)..].Trim();
            if (Matches(key, SystemProperties))
                ValidateExact(parameter, allowlists.Systems, "system", file, path + "." + key, result);
            else if (Matches(key, ComponentProperties))
                ValidateExact(parameter, allowlists.Components, "component", file, path + "." + key, result);
            else if (IsPrefabProperty(key))
                ValidatePrefab(parameter, file, path + "." + key, allowlists, result);
        }
    }

    static void ValidateExact(
        string value,
        HashSet<string> allowlist,
        string kind,
        string file,
        string path,
        EventReferenceValidationResult result)
    {
        if (string.IsNullOrWhiteSpace(value) || allowlist.Contains(value))
            return;

        result.Errors.Add($"E_IDS: {file} {path} references unknown {kind} '{value}'.");
    }

    static void ValidatePrefab(
        string value,
        string file,
        string path,
        AllowlistSet allowlists,
        EventReferenceValidationResult result)
    {
        if (string.IsNullOrWhiteSpace(value) || int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var hash) && hash != 0)
            return;

        if (allowlists.Prefabs.Contains(value))
            return;

        try
        {
            if (PrefabHelper.TryGetPrefabGuid(value, out var guid) && guid.GuidHash != 0)
                return;
            if (VRisingCore.IsReady && PrefabHelper.GetLivePrefabGuid(value) is { GuidHash: not 0 })
                return;
        }
        catch
        {
            // Runtime prefab catalog is not ready; leave a deterministic E_IDS.
        }

        result.Errors.Add($"E_IDS: {file} {path} references unknown prefab '{value}'.");
    }

    static void ValidateMarkers(string value, string file, string path, EventReferenceValidationResult result)
    {
        foreach (var token in value.Split(new[] { '|', ' ', '\r', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries))
        {
            if (!token.StartsWith("tick:", StringComparison.OrdinalIgnoreCase) &&
                !token.StartsWith("wait:", StringComparison.OrdinalIgnoreCase))
                continue;

            var raw = token[(token.IndexOf(':') + 1)..].Trim();
            if (!double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds) ||
                double.IsNaN(seconds) || double.IsInfinity(seconds) || seconds < 0)
            {
                result.Errors.Add($"E_TICK: {file} {path} contains invalid timing marker '{token}'.");
            }
        }
    }

    static bool IsPrefabProperty(string name) =>
        PrefabProperties.Contains(name, StringComparer.OrdinalIgnoreCase) ||
        name.EndsWith("Prefab", StringComparison.OrdinalIgnoreCase);

    static bool Matches(string name, IReadOnlyCollection<string> values) =>
        values.Contains(name, StringComparer.OrdinalIgnoreCase);

    static AllowlistSet LoadAllowlists()
    {
        var root = Path.Combine(ConfigLoader.ConfigRoot, "audit", "systems", "allowlists");
        if (!Directory.Exists(root))
        {
            root = Path.Combine(AppContext.BaseDirectory, "docs", "audit", "systems", "allowlists");
            if (!Directory.Exists(root))
                root = Path.Combine(Directory.GetCurrentDirectory(), "docs", "audit", "systems", "allowlists");
        }

        return new AllowlistSet(
            ReadEntries(Path.Combine(root, "systems.json")),
            ReadEntries(Path.Combine(root, "components.json")),
            ReadEntries(Path.Combine(root, "prefabs.json")));
    }

    static HashSet<string> ReadEntries(string path)
    {
        var entries = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            if (File.Exists(path))
            {
                using var document = JsonDocument.Parse(File.ReadAllText(path));
                if (document.RootElement.TryGetProperty("entries", out var jsonEntries) &&
                    jsonEntries.ValueKind == JsonValueKind.Array)
                {
                    foreach (var entry in jsonEntries.EnumerateArray())
                    {
                        if (entry.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(entry.GetString()))
                            entries.Add(entry.GetString()!);
                    }
                }
            }
        }
        catch
        {
            // Fall through to the line-oriented export. A malformed JSON
            // sidecar must not hide a valid pinned allowlist.
        }

        if (entries.Count == 0)
        {
            var textPath = Path.Combine(
                Path.GetDirectoryName(path) ?? "",
                Path.GetFileNameWithoutExtension(path) + ".allowlist.txt");
            try
            {
                if (File.Exists(textPath))
                {
                    foreach (var line in File.ReadLines(textPath))
                    {
                        var value = line.Trim();
                        if (value.Length > 0 && !value.StartsWith("#", StringComparison.Ordinal))
                            entries.Add(value);
                    }
                }
            }
            catch
            {
                // A missing/unreadable optional export remains an empty set.
            }
        }

        return entries;
    }

    sealed record AllowlistSet(
        HashSet<string> Systems,
        HashSet<string> Components,
        HashSet<string> Prefabs);
}

public sealed class EventReferenceValidationResult
{
    public List<string> Errors { get; } = new();
    public bool Success => Errors.Count == 0;
}
