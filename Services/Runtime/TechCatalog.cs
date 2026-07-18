namespace BattleLuck.Services.Runtime;

/// <summary>Manages tech catalog loading and resolution.</summary>
public sealed class TechCatalog
{
    Dictionary<string, TechDefinition> _techIndex = new(StringComparer.OrdinalIgnoreCase);
    List<string> _stackGroups = new();

    public void Load(List<TechDefinition> techs, List<string> stackGroups)
    {
        _techIndex.Clear();
        _stackGroups.Clear();

        if (techs != null)
        {
            foreach (var tech in techs)
            {
                if (!string.IsNullOrWhiteSpace(tech.TechId))
                    _techIndex[tech.TechId] = tech;
            }
        }

        if (stackGroups != null)
            _stackGroups.AddRange(stackGroups);

        BattleLuckPlugin.LogInfo($"[TechCatalog] Loaded {_techIndex.Count} tech(s), {_stackGroups.Count} stack group(s).");
    }

    public bool TryGetTech(string techId, out TechDefinition? tech)
    {
        return _techIndex.TryGetValue(techId, out tech);
    }

    public IReadOnlyDictionary<string, TechDefinition> AllTechs => _techIndex;
    public IReadOnlyList<string> StackGroups => _stackGroups;
}

/// <summary>Resolves tech conflicts per session and enforces stack group constraints.</summary>
public sealed class TechResolver
{
    readonly TechCatalog _catalog;

    public TechResolver(TechCatalog catalog)
    {
        _catalog = catalog;
    }

    public (bool Success, SessionTechState? State, string? Error) Resolve(List<string> requestedTechIds)
    {
        var result = new SessionTechState();
        var processed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var errors = new List<string>();

        // Sort by priority descending (higher priority wins conflicts)
        var sorted = requestedTechIds
            .Where(id => _catalog.TryGetTech(id, out _))
            // Guard against possible null value returned via out var 't'
            .OrderByDescending(id => _catalog.TryGetTech(id, out var t) && t != null ? t.Priority : 0)
            .ToList();

        foreach (var techId in sorted)
        {
            if (!_catalog.TryGetTech(techId, out var tech) || tech == null)
            {
                errors.Add($"Tech '{techId}' not found in catalog.");
                continue;
            }

            if (!tech.Enabled)
            {
                errors.Add($"Tech '{techId}' is disabled.");
                continue;
            }

            // Check for duplicate
            if (processed.Contains(techId))
            {
                errors.Add($"Duplicate tech: '{techId}'");
                continue;
            }

            // Check conflicts with already-active techs in the same stack group
            var conflict = result.ActiveTechs.Values.FirstOrDefault(
                active => active.StackGroup.Equals(tech.StackGroup, StringComparison.OrdinalIgnoreCase));

            if (conflict != null && conflict.TechId != techId)
            {
                var handled = HandleConflict(tech, conflict, result, errors);
                if (!handled)
                    continue;
            }

            result.ActiveTechs[techId] = tech;
            processed.Add(techId);
        }

        if (errors.Count > 0)
            result.LastResolvedError = string.Join("; ", errors.Take(3));

        return (result.ActiveTechs.Count > 0, result, result.LastResolvedError);
    }

    bool HandleConflict(TechDefinition incoming, TechDefinition active, SessionTechState state, List<string> errors)
    {
        var mode = incoming.ConflictMode ?? "Reject";

        if (mode.Equals("Reject", StringComparison.OrdinalIgnoreCase))
        {
            errors.Add($"Tech '{incoming.TechId}' conflicts with active tech '{active.TechId}' (stack group: {incoming.StackGroup}). Rejected.");
            return false;
        }

        if (mode.Equals("ReplaceLowerPriority", StringComparison.OrdinalIgnoreCase))
        {
            if (incoming.Priority > active.Priority)
            {
                state.ActiveTechs.Remove(active.TechId);
                BattleLuckPlugin.LogInfo($"[TechResolver] Tech '{active.TechId}' (priority {active.Priority}) replaced by '{incoming.TechId}' (priority {incoming.Priority}).");
                return true;
            }
            else
            {
                errors.Add($"Tech '{incoming.TechId}' (priority {incoming.Priority}) cannot replace '{active.TechId}' (priority {active.Priority}). Rejected.");
                return false;
            }
        }

        if (mode.Equals("Suspend", StringComparison.OrdinalIgnoreCase))
        {
            state.ActiveTechs.Remove(active.TechId);
            state.SuspendedTechs[active.TechId] = active;
            BattleLuckPlugin.LogInfo($"[TechResolver] Tech '{active.TechId}' suspended for incoming tech '{incoming.TechId}'.");
            return true;
        }

        errors.Add($"Unknown conflict mode: {mode}");
        return false;
    }
}
