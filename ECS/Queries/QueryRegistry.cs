namespace BattleLuck.ECS.Queries;

/// <summary>
/// Small compile-safe query registry for the installed V Rising/Unity.Entities surface.
/// It keeps only queries that are known to exist across the pinned server assemblies.
/// </summary>
public static class QueryRegistry
{
    static readonly Dictionary<string, EntityQuery> _cachedQueries = new(StringComparer.OrdinalIgnoreCase);
    static EntityManager _entityManager;
    static bool _initialized;

    public static void Initialize(EntityManager entityManager)
    {
        if (_initialized)
            return;

        _entityManager = entityManager;
        RegisterQuery("Players", new QueryDefinition
        {
            Name = "Players",
            All = new[] { ComponentType.ReadOnly<ProjectM.PlayerCharacter>() }
        });
        RegisterQuery("BuffEntities", new QueryDefinition
        {
            Name = "BuffEntities",
            All = new[] { ComponentType.ReadOnly<ProjectM.Buff>() }
        });
        _initialized = true;
        BattleLuckPlugin.LogInfo("[QueryRegistry] Initialized compile-safe core queries.");
    }

    public static void RegisterQuery(string name, QueryDefinition definition)
    {
        if (!_initialized && _entityManager.Equals(default(EntityManager)))
            return;

        if (_cachedQueries.ContainsKey(name))
            return;

        var components = (definition.All ?? Array.Empty<ComponentType>())
            .Concat(definition.Any ?? Array.Empty<ComponentType>())
            .ToArray();

        if (components.Length == 0)
            return;

        _cachedQueries[name] = _entityManager.CreateEntityQuery(components);
    }

    public static EntityQuery GetQuery(string name)
    {
        if (_cachedQueries.TryGetValue(name, out var query))
            return query;
        throw new InvalidOperationException($"Query '{name}' not registered in QueryRegistry");
    }

    public static bool HasQuery(string name) => _cachedQueries.ContainsKey(name);

    public static void Shutdown()
    {
        foreach (var query in _cachedQueries.Values)
        {
            try { query.Dispose(); } catch { }
        }
        _cachedQueries.Clear();
        _initialized = false;
    }
}

public struct SessionTag
{
    public FixedString64Bytes SessionId;
    public FixedString64Bytes ModeId;
    public int ZoneHash;
}

public struct ArenaUnitTag { }

public struct SpawnedByBattleluck { }
