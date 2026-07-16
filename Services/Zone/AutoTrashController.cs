using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

/// <summary>
/// Automatically destroys items dropped on the ground inside active mode zones.
/// Queries for ItemPickup entities each tick and removes those within zone boundaries.
/// </summary>
public sealed class AutoTrashController
{
    readonly Dictionary<int, ZoneDefinition> _activeZones = new();
    EntityQuery _itemPickupQuery;
    bool _initialized;
    bool _enabled = true;

    DateTime _lastSweep = DateTime.UtcNow;
    const int SweepIntervalMs = 1000; // check every 1 second
    int _totalTrashed;
    readonly List<PendingDrop> _pendingDrops = new();

    readonly record struct PendingDrop(PrefabGUID ItemHash, int Amount, Entity ItemEntity, float3 Position, DateTime SeenAtUtc);

    public bool Enabled
    {
        get => _enabled;
        set => _enabled = value;
    }

    public int TotalTrashed => _totalTrashed;

    public void Initialize()
    {
        var em = VRisingCore.EntityManager;
        _itemPickupQuery = em.CreateEntityQuery(
            ComponentType.ReadOnly<ItemPickup>(),
            ComponentType.ReadOnly<Translation>()
        );
        _initialized = true;
        BattleLuckPlugin.LogInfo("[AutoTrash] Initialized.");
    }

    /// <summary>
    /// Register a zone as active — items dropped here will be destroyed.
    /// </summary>
    public void RegisterZone(int zoneHash, ZoneDefinition zone)
    {
        _activeZones[zoneHash] = zone;
    }

    /// <summary>
    /// Unregister a zone — items will no longer be auto-trashed there.
    /// </summary>
    public void UnregisterZone(int zoneHash)
    {
        _activeZones.Remove(zoneHash);
    }

    public void ClearZones() => _activeZones.Clear();

    public void HandleDrop(PrefabGUID itemHash, int amount, Entity itemEntity, float3 position)
    {
        if (!_enabled || _activeZones.Count == 0)
            return;

        if (!IsInsideActiveZone(position, out var zoneHash))
            return;

        _pendingDrops.Add(new PendingDrop(itemHash, amount, itemEntity, position, DateTime.UtcNow));
        BattleLuckPlugin.LogInfo($"[AutoTrash] Drop observed in zone {zoneHash}: {PrefabHelper.GetLivePrefabName(itemHash) ?? itemHash.GuidHash.ToString()} x{amount} at {position}.");
    }

    /// <summary>
    /// Called each server tick. Sweeps for dropped items inside active zones and destroys them.
    /// </summary>
    public void Tick()
    {
        if (!_enabled || !_initialized || _activeZones.Count == 0) return;

        var now = DateTime.UtcNow;
        if ((now - _lastSweep).TotalMilliseconds < SweepIntervalMs) return;
        _lastSweep = now;

        var em = VRisingCore.EntityManager;
        ProcessPendingDrops(em);

        var entities = _itemPickupQuery.ToEntityArray(Allocator.Temp);

        try
        {
            for (int i = 0; i < entities.Length; i++)
            {
                var entity = entities[i];
                if (!em.Exists(entity)) continue;

                float3 pos;
                if (em.HasComponent<Translation>(entity))
                    pos = em.GetComponentData<Translation>(entity).Value;
                else
                    continue;

                if (IsInsideActiveZone(pos, out _))
                {
                    em.DestroyEntity(entity);
                    _totalTrashed++;
                }
            }
        }
        finally
        {
            entities.Dispose();
        }
    }

    void ProcessPendingDrops(EntityManager em)
    {
        if (_pendingDrops.Count == 0)
            return;

        var now = DateTime.UtcNow;
        var remaining = new List<PendingDrop>();
        foreach (var drop in _pendingDrops)
        {
            var destroyed = false;
            try
            {
                if (drop.ItemEntity.Exists() && drop.ItemEntity.Has<ItemPickup>())
                {
                    em.DestroyEntity(drop.ItemEntity);
                    destroyed = true;
                }
                else
                {
                    destroyed = DestroyNearbyPickup(em, drop);
                }
            }
            catch (Exception ex)
            {
                BattleLuckPlugin.LogWarning($"[AutoTrash] Pending drop cleanup failed: {ex.Message}");
            }

            if (destroyed)
            {
                _totalTrashed++;
                continue;
            }

            if ((now - drop.SeenAtUtc).TotalSeconds < 5)
                remaining.Add(drop);
        }

        _pendingDrops.Clear();
        _pendingDrops.AddRange(remaining);
    }

    bool DestroyNearbyPickup(EntityManager em, PendingDrop drop)
    {
        var entities = _itemPickupQuery.ToEntityArray(Allocator.Temp);
        try
        {
            for (var i = 0; i < entities.Length; i++)
            {
                var entity = entities[i];
                if (!entity.Exists() || !entity.Has<Translation>())
                    continue;

                var pos = entity.Read<Translation>().Value;
                if (math.distance(new float2(pos.x, pos.z), new float2(drop.Position.x, drop.Position.z)) > 4f)
                    continue;

                if (drop.ItemHash != PrefabGUID.Empty && entity.Has<PrefabGUID>() && entity.Read<PrefabGUID>() != drop.ItemHash)
                    continue;

                em.DestroyEntity(entity);
                return true;
            }
        }
        finally
        {
            entities.Dispose();
        }

        return false;
    }

    bool IsInsideActiveZone(float3 position, out int zoneHash)
    {
        foreach (var kv in _activeZones)
        {
            var zone = kv.Value;
            var zoneCenter = new float3(zone.Position.X, zone.Position.Y, zone.Position.Z);
            var dist = math.distance(new float2(position.x, position.z), new float2(zoneCenter.x, zoneCenter.z));
            if (dist <= zone.Radius)
            {
                zoneHash = kv.Key;
                return true;
            }
        }

        zoneHash = 0;
        return false;
    }
}
