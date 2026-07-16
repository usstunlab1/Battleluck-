using System.Text.Json;

namespace BattleLuck.Services.Runtime;

public sealed class ActionRegistry
{
    static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };
    static readonly Dictionary<string, ActionDefinition> _byId = new(StringComparer.OrdinalIgnoreCase);
    static readonly Dictionary<string, SequenceDefinition> _sequences = new(StringComparer.OrdinalIgnoreCase);
    static readonly Dictionary<string, string> _aliases = new(StringComparer.OrdinalIgnoreCase);
    static readonly HashSet<string> _registeredActions = new(StringComparer.OrdinalIgnoreCase);
    static bool _loaded;

    static ActionRegistry()
    {
        EnsureLoaded();
    }

    internal static void EnsureLoaded()
    {
        if (_loaded) return;
        _loaded = true;

        var catalogPath = Path.Combine(ConfigLoader.ConfigRoot, "actions_catalog.json");
        if (!File.Exists(catalogPath)) return;

        try
        {
            var json = File.ReadAllText(catalogPath);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.TryGetProperty("registered", out var registered) && registered.ValueKind == JsonValueKind.Array)
            {
                foreach (var action in registered.EnumerateArray())
                {
                    var value = action.GetString();
                    if (!string.IsNullOrWhiteSpace(value))
                        _registeredActions.Add(value);
                }
            }

            if (root.TryGetProperty("llm_guidance", out var guidance) &&
                guidance.ValueKind == JsonValueKind.Object &&
                guidance.TryGetProperty("legacy_mappings", out var mappings) &&
                mappings.ValueKind == JsonValueKind.Object)
            {
                foreach (var prop in mappings.EnumerateObject())
                {
                    _aliases[prop.Name] = prop.Value.GetString() ?? prop.Name;
                }
            }

            if (root.TryGetProperty("actions", out var actions) && actions.ValueKind == JsonValueKind.Array)
            {
                foreach (var action in actions.EnumerateArray())
                {
                    var def = JsonSerializer.Deserialize<ActionDefinition>(action.GetRawText(), JsonOpts);
                    if (def != null && !string.IsNullOrWhiteSpace(def.ActionId))
                        _byId[def.ActionId] = def;
                }
            }

            if (root.TryGetProperty("sequences", out var sequences) && sequences.ValueKind == JsonValueKind.Array)
            {
                foreach (var seq in sequences.EnumerateArray())
                {
                    var def = JsonSerializer.Deserialize<SequenceDefinition>(seq.GetRawText(), JsonOpts);
                    if (def != null && !string.IsNullOrWhiteSpace(def.SequenceId))
                        _sequences[def.SequenceId] = def;
                }
            }
        }
        catch (Exception ex)
        {
            BattleLuckPlugin.LogWarning($"[ActionRegistry] Failed to load actions_catalog.json: {ex.Message}");
        }
    }

    public static bool IsKnown(string? actionName)
    {
        if (string.IsNullOrWhiteSpace(actionName))
            return false;

        return _registeredActions.Contains(actionName);
    }

    public static string Normalize(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return string.Empty;

        var trimmed = name.Trim();
        return _aliases.TryGetValue(trimmed, out var canonical) ? canonical : trimmed;
    }

    public bool TryGetAction(string actionId, out ActionDefinition? definition)
    {
        EnsureLoaded();
        return _byId.TryGetValue(actionId, out definition);
    }

    public bool TryGetSequence(string sequenceId, out SequenceDefinition? sequence)
    {
        EnsureLoaded();
        return _sequences.TryGetValue(sequenceId, out sequence);
    }

    public IReadOnlyCollection<string> ResolveActionIds(IEnumerable<string> actionIds)
    {
        EnsureLoaded();
        var results = new List<string>();
        foreach (var id in actionIds)
        {
            if (TryResolveToActionString(id, out var actionString))
                results.Add(actionString);
        }
        return results;
    }

    public bool TryResolveToActionString(string reference, out string actionString)
    {
        EnsureLoaded();
        actionString = string.Empty;

        if (string.IsNullOrWhiteSpace(reference))
            return false;

        var trimmed = reference.Trim();

        if (_byId.TryGetValue(trimmed, out var def))
        {
            actionString = BuildActionString(def.Action, def.Params);
            return true;
        }

        if (_sequences.TryGetValue(trimmed, out var seq))
        {
            var firstStep = seq.Steps.FirstOrDefault();
            if (firstStep != null && TryResolveStepToActionString(firstStep, out actionString))
                return true;
        }

        actionString = trimmed;
        return true;
    }

    public bool TryResolveStepToActionString(SequenceStep step, out string actionString)
    {
        EnsureLoaded();
        actionString = string.Empty;

        if (!string.IsNullOrWhiteSpace(step.ActionId) && _byId.TryGetValue(step.ActionId, out var def))
        {
            var mergedParams = new Dictionary<string, JsonElement>(def.Params);
            if (step.Params != null)
            {
                foreach (var kv in step.Params)
                    mergedParams[kv.Key] = kv.Value;
            }
            actionString = BuildActionString(def.Action, mergedParams);
            return true;
        }

        if (!string.IsNullOrWhiteSpace(step.Action))
        {
            actionString = BuildActionString(step.Action, step.Params ?? new Dictionary<string, JsonElement>());
            return true;
        }

        return false;
    }

    public IReadOnlyList<string> ResolveSequence(string sequenceId)
    {
        EnsureLoaded();
        var results = new List<string>();
        if (!_sequences.TryGetValue(sequenceId, out var seq))
            return results;

        foreach (var step in seq.Steps)
        {
            if (TryResolveStepToActionString(step, out var actionString))
                results.Add(actionString);
        }

        return results;
    }

    public IReadOnlyList<SequenceStep> GetSequenceSteps(string sequenceId)
    {
        EnsureLoaded();
        if (!_sequences.TryGetValue(sequenceId, out var seq))
            return Array.Empty<SequenceStep>();
        return seq.Steps;
    }

    public static string BuildActionString(string action, Dictionary<string, JsonElement> parameters)
    {
        if (string.IsNullOrWhiteSpace(action))
            return string.Empty;

        if (parameters == null || parameters.Count == 0)
            return action.Trim();

        var parts = new List<string>();
        foreach (var kv in parameters)
        {
            var value = kv.Value.ValueKind switch
            {
                JsonValueKind.String => kv.Value.GetString() ?? "",
                JsonValueKind.Number => kv.Value.GetRawText(),
                JsonValueKind.True => "true",
                JsonValueKind.False => "false",
                JsonValueKind.Null => "",
                _ => kv.Value.GetRawText()
            };
            if (!string.IsNullOrWhiteSpace(value))
                parts.Add($"{kv.Key}={value}");
        }

        return parts.Count > 0 ? $"{action.Trim()}:{string.Join("|", parts)}" : action.Trim();
    }
}
