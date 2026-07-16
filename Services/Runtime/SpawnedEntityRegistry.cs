using Unity.Entities;

namespace BattleLuck.Services.Runtime;

public sealed record SpawnedEntityRecord(
    string EventId,
    string SessionId,
    string Group,
    string Kind,
    Entity Entity);

public sealed class SpawnedEntityRegistry
{
    readonly Dictionary<string, List<SpawnedEntityRecord>> _bySession = new(StringComparer.OrdinalIgnoreCase);

    public void Register(string eventId, string sessionId, string group, string kind, Entity entity)
    {
        if (!entity.Exists())
            return;

        if (entity.IsPlayer())
        {
            BattleLuckPlugin.LogWarning($"[SpawnedEntityRegistry] Refusing to register player entity for event cleanup ({eventId}/{sessionId}).");
            return;
        }

        if (IsProtectedCastleAnchor(entity))
        {
            BattleLuckPlugin.LogWarning($"[SpawnedEntityRegistry] Refusing to register castle anchor for event cleanup ({eventId}/{sessionId}).");
            return;
        }

        if (!_bySession.TryGetValue(sessionId, out var records))
        {
            records = new List<SpawnedEntityRecord>();
            _bySession[sessionId] = records;
        }

        records.Add(new SpawnedEntityRecord(eventId, sessionId, group, kind, entity));
    }

    public int ClearSession(string sessionId, string? group = null)
    {
        if (!_bySession.TryGetValue(sessionId, out var records))
            return 0;

        var remaining = new List<SpawnedEntityRecord>();
        var destroyed = 0;
        foreach (var record in records)
        {
            if (!string.IsNullOrWhiteSpace(group) && !record.Group.Equals(group, StringComparison.OrdinalIgnoreCase))
            {
                remaining.Add(record);
                continue;
            }

            try
            {
                if (record.Entity.Exists() && !record.Entity.IsPlayer() && !IsProtectedCastleAnchor(record.Entity))
                {
                    record.Entity.Destroy();
                    destroyed++;
                }
            }
            catch (Exception ex)
            {
                BattleLuckPlugin.LogWarning($"[SpawnedEntityRegistry] Failed to destroy {record.Kind}/{record.Group}: {ex.Message}");
            }
        }

        if (remaining.Count == 0) _bySession.Remove(sessionId);
        else _bySession[sessionId] = remaining;
        return destroyed;
    }

    public SpawnedEntityRegistrySnapshot Snapshot(string sessionId)
    {
        if (!_bySession.TryGetValue(sessionId, out var records))
            return new SpawnedEntityRegistrySnapshot(sessionId, 0, 0, new(), new());

        var alive = records.Where(r => r.Entity.Exists() && !r.Entity.IsPlayer()).ToList();
        return new SpawnedEntityRegistrySnapshot(
            sessionId,
            records.Count,
            alive.Count,
            alive.GroupBy(r => string.IsNullOrWhiteSpace(r.Kind) ? "unknown" : r.Kind, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase),
            alive.GroupBy(r => string.IsNullOrWhiteSpace(r.Group) ? "ungrouped" : r.Group, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase));
    }

    static bool IsProtectedCastleAnchor(Entity entity)
    {
        if (!entity.Exists()) return false;
        // CastleHeart and CastleTerritory types not available in this build - skipped
        var prefab = entity.Has<PrefabGUID>() ? entity.Read<PrefabGUID>() : PrefabGUID.Empty;
        return prefab.GuidHash is -485435268 or -1994745735;
    }

    public IReadOnlyList<SpawnedEntityRegistrySnapshot> SnapshotAll() =>
        _bySession.Keys.Select(Snapshot).ToList();
}

public sealed record SpawnedEntityRegistrySnapshot(
    string SessionId,
    int TotalRegistered,
    int AliveNonPlayers,
    Dictionary<string, int> AliveByKind,
    Dictionary<string, int> AliveByGroup);
