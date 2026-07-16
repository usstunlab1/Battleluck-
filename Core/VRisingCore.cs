using ProjectM;
using ProjectM.Scripting;
using Unity.Entities;

/// <summary>
/// Central static accessor for V Rising ECS singletons.
/// Mirrors Bloodcraft Core.cs pattern — lazy initialization from the server world.
/// SystemService is inlined (Bloodcraft's is a custom class, not a V Rising API).
/// </summary>
public static class VRisingCore
{
    static World? _server;
    static EntityManager? _entityManager;
    static ServerScriptMapper? _serverScriptMapper;
    static DebugEventsSystem? _debugEventsSystem;
    static PrefabCollectionSystem? _prefabCollectionSystem;
    static AdminAuthSystem? _adminAuthSystem;
    static EntityQuery? _onlinePlayersQuery;

    public static bool IsReady => _server != null && _server.IsCreated;

    public static World Server
    {
        get => _server ?? throw new InvalidOperationException("VRisingCore not initialized.");
        private set => _server = value;
    }

    public static EntityManager EntityManager
    {
        get
        {
            _entityManager ??= Server.EntityManager;
            return _entityManager.Value;
        }
    }

    public static ServerScriptMapper ServerScriptMapper
    {
        get
        {
            _serverScriptMapper ??= GetSystem<ServerScriptMapper>();
            return _serverScriptMapper;
        }
    }

    /// <summary>
    /// Fresh reference every call — ServerGameManager is a struct.
    /// Caching a copy causes stale native pointers → AccessViolationException.
    /// </summary>
    public static ServerGameManager ServerGameManager
        => ServerScriptMapper.GetServerGameManager();

    public static DebugEventsSystem DebugEventsSystem
    {
        get
        {
            _debugEventsSystem ??= GetSystem<DebugEventsSystem>();
            return _debugEventsSystem;
        }
    }

    public static PrefabCollectionSystem PrefabCollectionSystem
    {
        get
        {
            _prefabCollectionSystem ??= GetSystem<PrefabCollectionSystem>();
            return _prefabCollectionSystem;
        }
    }

    /// <summary>
    /// Server admin auth system used by admin-console style checks.
    /// </summary>
    public static AdminAuthSystem AdminAuthSystem
    {
        get
        {
            _adminAuthSystem ??= GetSystem<AdminAuthSystem>();
            return _adminAuthSystem;
        }
    }

    /// <summary>
    /// Initialize from the V Rising dedicated server world.
    /// Call once during plugin Load after the server world is available.
    /// </summary>
    public static void Initialize()
    {
        _server = GetServerWorld();
        if (_server == null)
        {
            BattleLuckPlugin.LogWarning("[VRisingCore] Server world not found — will retry on first access.");
            return;
        }

        // Force eager init so downstream callers don't race.
        _ = EntityManager;
        BattleLuckPlugin.LogInfo("[VRisingCore] Initialized successfully.");
    }

    /// <summary>Reset cached references (call on server restart / world teardown).</summary>
    public static void Reset()
    {
        _server = null;
        _entityManager = null;
        _serverScriptMapper = null;
        _debugEventsSystem = null;
        _prefabCollectionSystem = null;
        _adminAuthSystem = null;
        _onlinePlayersQuery = null;
    }

    static T GetSystem<T>() where T : ComponentSystemBase
    {
        return Server.GetExistingSystemManaged<T>()
            ?? throw new InvalidOperationException($"System {typeof(T).Name} not found in server world.");
    }

    static World? GetServerWorld()
    {
        foreach (var world in World.All)
        {
            if (world.Name == "Server")
                return world;
        }
        return null;
    }

    public static Entity GetPrefabEntityByGuid(PrefabGUID guid)
    {
        if (PrefabCollectionSystem._PrefabGuidToEntityMap.TryGetValue(guid, out var entity))
            return entity;
        return Entity.Null;
    }

    /// <summary>Count live entities matching a prefab name pattern via PrefabCollectionSystem.</summary>
    public static int CountEntities(string componentTypeName)
    {
        int count = 0;
        try
        {
            var query = EntityManager.CreateEntityQuery(ComponentType.ReadOnly<PrefabGUID>());
            try
            {
                var entities = query.ToEntityArray(Allocator.Temp);
                try
                {
                    for (int i = 0; i < entities.Length; i++)
                    {
                        if (!entities[i].Exists()) continue;
                        var prefab = entities[i].Read<PrefabGUID>();
                        var name = PrefabHelper.GetName(prefab) ?? "";
                        if (name.IndexOf(componentTypeName, StringComparison.OrdinalIgnoreCase) >= 0)
                            count++;
                    }
                }
                finally
                {
                    entities.Dispose();
                }
            }
            finally
            {
                query.Dispose();
            }
        }
        catch (Exception ex)
        {
            BattleLuckPlugin.LogWarning($"[VRisingCore] CountEntities('{componentTypeName}') failed: {ex.Message}");
        }
        return count;
    }

    /// <summary>Destroy up to <paramref name="max"/> live entities matching a prefab name pattern.</summary>
    public static int DestroyEntities(string componentTypeName, int? max = null)
    {
        int destroyed = 0;
        int deferred = 0;
        try
        {
            var query = EntityManager.CreateEntityQuery(ComponentType.ReadOnly<PrefabGUID>());
            try
            {
                var entities = query.ToEntityArray(Allocator.Temp);
                try
                {
                    for (int i = 0; i < entities.Length; i++)
                    {
                        if (max.HasValue && destroyed >= max.Value) break;
                        if (!entities[i].Exists()) continue;
                        var prefab = entities[i].Read<PrefabGUID>();
                        var name = PrefabHelper.GetName(prefab) ?? "";
                        if (name.IndexOf(componentTypeName, StringComparison.OrdinalIgnoreCase) < 0) continue;
                        try { entities[i].DestroyWithReason(); destroyed++; }
                        catch (Exception ex) when (ex.Message.Contains("in live", StringComparison.OrdinalIgnoreCase))
                        {
                            // Entity is currently being processed by ECS systems - will be cleaned up on next tick
                            deferred++;
                        }
                        catch { }
                    }
                }
                finally
                {
                    entities.Dispose();
                }
            }
            finally
            {
                query.Dispose();
            }
        }
        catch (Exception ex)
        {
            BattleLuckPlugin.LogWarning($"[VRisingCore] DestroyEntities('{componentTypeName}') failed: {ex.Message}");
        }
        if (deferred > 0)
            BattleLuckPlugin.LogInfo($"[VRisingCore] DestroyEntities deferred {deferred} entity(ies) (in live state).");
        return destroyed;
    }

    /// <summary>
    /// Get all online player character entities.
    /// </summary>
    public static List<Entity> GetOnlinePlayers()
    {
        _onlinePlayersQuery ??= EntityManager.CreateEntityQuery(ComponentType.ReadOnly<PlayerCharacter>());
        var entities = _onlinePlayersQuery.Value.ToEntityArray(Allocator.Temp);
        try
        {
            var players = new List<Entity>(entities.Length);
            for (int i = 0; i < entities.Length; i++)
                players.Add(entities[i]);
            return players;
        }
        finally
        {
            entities.Dispose();
        }
    }

    public static Entity GetPlayerEntityBySteamId(ulong steamId)
    {
        var players = GetOnlinePlayers();
        foreach (var player in players)
        {
            if (player.Exists() && player.IsPlayer() && player.GetSteamId() == steamId)
                return player;
        }
        return Entity.Null;
    }
}
