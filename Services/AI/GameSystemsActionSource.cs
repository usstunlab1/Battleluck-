namespace BattleLuck.Services.AI;

/// <summary>One catalog-backed capability the planner can propose.</summary>
public sealed class GameSystem
{
    public string Id { get; init; } = "";
    public string Category { get; init; } = "";
    public string Description { get; init; } = "";
    public string ExampleAction { get; init; } = "";
    public List<string> Parameters { get; init; } = new();
}

/// <summary>
/// Exposes only runtime-reachable entries from <c>actions_catalog.json</c> to
/// the planner. The old built-in list advertised unsupported/prohibited actions
/// such as tech and servant mutation, which made plans look valid but fail at
/// execution time.
/// </summary>
public sealed class GameSystemsActionSource
{
    readonly List<GameSystem> _systems = new();
    readonly HashSet<string> _requestedCatalogActions = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Retains compatibility with callers that explicitly add names, while never
    /// allowing an uncataloged name into planning context.
    /// </summary>
    public void AddCatalogActions(IEnumerable<string> actionNames)
    {
        foreach (var actionName in actionNames)
        {
            if (!string.IsNullOrWhiteSpace(actionName))
                _requestedCatalogActions.Add(actionName.Trim());
        }
    }

    public IReadOnlyList<GameSystem> AllSystems
    {
        get
        {
            RefreshCatalogActions();
            return _systems;
        }
    }

    /// <summary>Compact, validated planning context for the LLM.</summary>
    public string GetPlanningContext(int max = 60)
    {
        RefreshCatalogActions();
        var sb = new StringBuilder();
        sb.AppendLine("Available catalog-backed runtime actions. Propose only these; every action is validated and approval-gated by the host:");
        foreach (var system in _systems.Take(Math.Max(1, max)))
            sb.AppendLine($"- {system.Id} [{system.Category}]: {system.Description} (e.g. {system.ExampleAction})");
        return sb.ToString();
    }

    void RefreshCatalogActions()
    {
        var manifest = new ActionManifestService();
        var entries = manifest.Entries.Values
            .Where(entry => entry.HandlerAvailable)
            .Where(entry => _requestedCatalogActions.Count == 0 ||
                            _requestedCatalogActions.Contains(entry.Name) ||
                            entry.Aliases.Any(alias => _requestedCatalogActions.Contains(alias)))
            .OrderBy(entry => entry.Category, StringComparer.OrdinalIgnoreCase)
            .ThenBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        _systems.Clear();
        foreach (var entry in entries)
        {
            var example = entry.Examples.FirstOrDefault() ?? entry.Name;
            var description = string.IsNullOrWhiteSpace(entry.Description)
                ? "Catalog-backed runtime action."
                : entry.Description;
            if (entry.Name.StartsWith("system.", StringComparison.OrdinalIgnoreCase))
                description += " This is a verified reference only; it does not invoke native ECS code.";

            _systems.Add(new GameSystem
            {
                Id = entry.Name,
                Category = string.IsNullOrWhiteSpace(entry.Category) ? "catalog" : entry.Category,
                Description = description,
                ExampleAction = example,
                Parameters = entry.Required.Concat(entry.Optional).Distinct(StringComparer.OrdinalIgnoreCase).ToList()
            });
        }
    }
}
