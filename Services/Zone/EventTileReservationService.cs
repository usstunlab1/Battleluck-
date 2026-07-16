using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace BattleLuck.Services.Zone;

/// <summary>
/// Cross-controller tile reservations for BattleLuck-spawned geometry.
/// Visual event tiles intentionally lose TilePosition after spawning, so ECS
/// queries alone cannot prevent a later wall, floor, platform, or schematic
/// queue from claiming the same grid coordinate.
/// </summary>
public static class EventTileReservationService
{
    static readonly object Sync = new();
    static readonly Dictionary<long, string> Reservations = new();
    static readonly Dictionary<string, HashSet<long>> ByOwner = new(StringComparer.Ordinal);

    public static bool TryReserve(string owner, int2 tile, out long key, out string? conflictingOwner)
    {
        key = ToKey(tile);
        conflictingOwner = null;
        if (string.IsNullOrWhiteSpace(owner))
            return false;

        lock (Sync)
        {
            if (Reservations.TryGetValue(key, out conflictingOwner))
                return false;

            Reservations[key] = owner;
            if (!ByOwner.TryGetValue(owner, out var keys))
            {
                keys = new HashSet<long>();
                ByOwner[owner] = keys;
            }
            keys.Add(key);
            return true;
        }
    }

    public static bool IsReserved(int2 tile, out string? owner)
    {
        lock (Sync)
            return Reservations.TryGetValue(ToKey(tile), out owner);
    }

    public static void Release(string owner, long key)
    {
        lock (Sync)
        {
            if (!Reservations.TryGetValue(key, out var currentOwner) ||
                !currentOwner.Equals(owner, StringComparison.Ordinal))
            {
                return;
            }

            Reservations.Remove(key);
            if (!ByOwner.TryGetValue(owner, out var keys))
                return;
            keys.Remove(key);
            if (keys.Count == 0)
                ByOwner.Remove(owner);
        }
    }

    public static int ReleaseOwner(string owner)
    {
        lock (Sync)
        {
            if (!ByOwner.Remove(owner, out var keys))
                return 0;
            foreach (var key in keys)
            {
                if (Reservations.TryGetValue(key, out var currentOwner) &&
                    currentOwner.Equals(owner, StringComparison.Ordinal))
                {
                    Reservations.Remove(key);
                }
            }
            return keys.Count;
        }
    }

    public static bool TryReplaceOwnerReservations(
        string owner,
        IEnumerable<int2> tiles,
        out string? conflictingOwner)
    {
        conflictingOwner = null;
        var desired = tiles.Select(ToKey).ToHashSet();
        lock (Sync)
        {
            foreach (var key in desired)
            {
                if (Reservations.TryGetValue(key, out var existing) &&
                    !existing.Equals(owner, StringComparison.Ordinal))
                {
                    conflictingOwner = existing;
                    return false;
                }
            }

            if (ByOwner.TryGetValue(owner, out var previous))
            {
                foreach (var key in previous)
                {
                    if (!desired.Contains(key) &&
                        Reservations.TryGetValue(key, out var existing) &&
                        existing.Equals(owner, StringComparison.Ordinal))
                    {
                        Reservations.Remove(key);
                    }
                }
            }

            ByOwner[owner] = desired;
            foreach (var key in desired)
                Reservations[key] = owner;
            return true;
        }
    }

    public static HashSet<long> SnapshotWorldOccupiedTiles()
    {
        var occupied = new HashSet<long>();
        try
        {
            var em = VRisingCore.EntityManager;
            var query = em.CreateEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<TilePosition>() },
                None = new[] { ComponentType.ReadOnly<PlayerCharacter>() }
            });
            var entities = query.ToEntityArray(Allocator.Temp);
            try
            {
                foreach (var entity in entities)
                {
                    if (em.Exists(entity) && em.HasComponent<TilePosition>(entity))
                        occupied.Add(ToKey(em.GetComponentData<TilePosition>(entity).Tile));
                }
            }
            finally
            {
                entities.Dispose();
                query.Dispose();
            }
        }
        catch (Exception ex)
        {
            BattleLuckPlugin.LogWarning($"[TileReservations] World occupancy scan failed: {ex.Message}");
        }

        return occupied;
    }

    public static long ToKey(int2 tile) => ((long)tile.x << 32) ^ (uint)tile.y;
}
