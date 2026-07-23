using BattleLuck.Models;
using ProjectM.CastleBuilding;

namespace BattleLuck.Services.Castles;

// ─────────────────────────────────────────────────────────────────────────────
// CastleObjectResolver
//
// Resolves a stable CastleObjectKey into a live Unity Entity after the
// world has loaded. Unity ECS entity index/version values are RUNTIME
// HANDLES — they are not stable across server restarts, world reloads, or
// entity recycling, so they MUST NOT be persisted to JSON.
//
// V Rising domain alignment (per the verdict's review):
//   - A castle HEART is a BuildTileCastle (a placeable tile that anchors
//     a territory). It owns all linked BuildTileObjects (floors, walls,
//     stairs, doors, storage chests, coffins, etc.).
//   - Storage is a BuildTileObject with a chest prefab; the storage
//     inventory lives on the linked entity but the OWNER is the
//     heart's owner (not the chest's last user).
//   - The resolver must be able to:
//       * Find the heart for any linked build-tile object.
//       * Find all build-tile objects linked to a given heart.
//       * Re-derive the heart's owner (UserOwner or LastUserOwner).
//   The service is the authority on owner identity; the resolver only
//   looks up live entities and hearts.
//
// Cache is in-memory only. It is rebuilt on world load.
// ─────────────────────────────────────────────────────────────────────────────

public sealed class CastleObjectResolver
{
    readonly object _sync = new();
    readonly Dictionary<string, Entity> _byKey = new();
    readonly Dictionary<int, List<Entity>> _byPrefab = new();
    readonly Dictionary<Entity, List<Entity>> _objectsByHeart = new();
    bool _worldReady;

    public void MarkWorldReady(bool ready) { lock (_sync) _worldReady = ready; }
    public bool IsWorldReady { get { lock (_sync) return _worldReady; } }

    /// <summary>
    /// Try to find the live Entity for a stable CastleObjectKey. Returns false
    /// when the world is not ready or no matching entity could be located.
    /// </summary>
    public bool TryResolve(CastleObjectKey key, out Entity entity)
    {
        entity = default;
        if (key == null || !key.IsValid()) return false;
        if (!IsWorldReady || !VRisingCore.IsReady) return false;

        var cacheKey = MakeCacheKey(key);
        lock (_sync)
        {
            if (_byKey.TryGetValue(cacheKey, out var cached) && cached.Exists() && VRisingCore.EntityManager.Exists(cached))
            {
                entity = cached;
                return true;
            }

            if (_byPrefab.TryGetValue(key.ObjectPrefabHash, out var candidates) && candidates.Count > 0)
            {
                var best = FindBestCandidate(candidates, key);
                if (best.Exists())
                {
                    _byKey[cacheKey] = best;
                    entity = best;
                    return true;
                }
            }

            var scanned = ScanWorldForPrefab(key.ObjectPrefabHash);
            if (scanned.Count > 0)
            {
                _byPrefab[key.ObjectPrefabHash] = scanned;
                var best = FindBestCandidate(scanned, key);
                if (best.Exists())
                {
                    _byKey[cacheKey] = best;
                    entity = best;
                    return true;
                }
            }
        }
        return false;
    }

    /// <summary>
    /// Drop a specific entity from the cache (e.g. when a chest is destroyed).
    /// </summary>
    public void Invalidate(Entity entity)
    {
        if (!entity.Exists()) return;
        lock (_sync)
        {
            var stale = new List<string>();
            foreach (var pair in _byKey)
            {
                if (pair.Value == entity) stale.Add(pair.Key);
            }
            foreach (var k in stale) _byKey.Remove(k);

            foreach (var pair in _byPrefab)
            {
                pair.Value.RemoveAll(e => e == entity);
            }

            // Drop heart-bucket entries that point to this entity.
            var staleHearts = new List<Entity>();
            foreach (var pair in _objectsByHeart)
            {
                pair.Value.RemoveAll(e => e == entity);
                if (pair.Value.Count == 0) staleHearts.Add(pair.Key);
            }
            foreach (var h in staleHearts) _objectsByHeart.Remove(h);
        }
    }

    public void InvalidateAll()
    {
        lock (_sync)
        {
            _byKey.Clear();
            _byPrefab.Clear();
            _objectsByHeart.Clear();
        }
    }

    /// <summary>
    /// Re-derive the current owner of the targeted entity from the castle
    /// heart. Returns 0 when the entity is not linked to a heart owned by
    /// any player. The service uses this value (not the persisted
    /// OwnerSteamId) to make authorization decisions.
    /// </summary>
    public ulong ResolveOwner(CastleObjectKey key, Entity entity)
    {
        var heart = ResolveCastleHeart(entity);
        if (heart == Entity.Null) return 0;
        return ResolveOwnerOfHeart(heart);
    }

    /// <summary>
    /// Resolve the castle heart for any linked build-tile object.
    /// Returns Entity.Null when the object is not linked to a heart.
    /// </summary>
    public Entity ResolveCastleHeart(Entity entity)
    {
        if (!entity.Exists() || !VRisingCore.IsReady) return Entity.Null;
        try
        {
            if (!entity.Has<CastleHeartConnection>()) return Entity.Null;
            return entity.Read<CastleHeartConnection>().CastleHeartEntity.GetEntityOnServer();
        }
        catch
        {
            return Entity.Null;
        }
    }

    /// <summary>
    /// Resolve the owner SteamId of a castle heart entity. Uses UserOwner
    /// first, then falls back to the heart's LastUserOwner.
    /// </summary>
    public ulong ResolveOwnerOfHeart(Entity heart)
    {
        if (!heart.Exists() || !VRisingCore.IsReady) return 0;
        try
        {
            if (heart.Has<UserOwner>())
            {
                var userEntity = heart.Read<UserOwner>().Owner.GetEntityOnServer();
                if (userEntity.Exists()) return userEntity.GetSteamId();
            }
            if (heart.Has<CastleHeart>())
            {
                var lastUser = heart.Read<CastleHeart>().LastUserOwner.GetEntityOnServer();
                if (lastUser.Exists()) return lastUser.GetSteamId();
            }
        }
        catch
        {
            // Resolution must never throw. A failure means "owner unknown" -> deny.
        }
        return 0;
    }

    /// <summary>
    /// Enumerate all linked BuildTileObjects for a given castle heart.
    /// Used by territory-wide apply. The result is a snapshot; the caller
    /// must not assume it is up-to-date after a world tick.
    /// </summary>
    public IReadOnlyList<Entity> EnumerateObjectsLinkedToHeart(Entity heart)
    {
        if (!heart.Exists()) return Array.Empty<Entity>();
        lock (_sync)
        {
            if (_objectsByHeart.TryGetValue(heart, out var cached))
                return cached.ToList();
        }

        // Cache miss. Scan for entities with CastleHeartConnection pointing at this heart.
        var results = new List<Entity>();
        if (!VRisingCore.IsReady) return results;
        try
        {
            var em = VRisingCore.EntityManager;
            var query = em.CreateEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<CastleHeartConnection>() }
            });
            var entities = query.ToEntityArray(Unity.Collections.Allocator.Temp);
            try
            {
                foreach (var e in entities)
                {
                    if (!em.Exists(e)) continue;
                    if (!e.Has<CastleHeartConnection>()) continue;
                    if (e.Read<CastleHeartConnection>().CastleHeartEntity.GetEntityOnServer() != heart) continue;
                    results.Add(e);
                }
            }
            finally
            {
                entities.Dispose();
                query.Dispose();
            }
        }
        catch
        {
            // Defensive: a bad query must not crash the resolver.
        }

        lock (_sync) _objectsByHeart[heart] = results;
        return results.ToList();
    }

    public IReadOnlyList<CastleObjectKey> DiscoverOwnedObjects(ulong ownerSteamId)
    {
        if (ownerSteamId == 0 || !IsWorldReady || !VRisingCore.IsReady)
            return Array.Empty<CastleObjectKey>();

        var discovered = new Dictionary<string, CastleObjectKey>(StringComparer.Ordinal);
        var em = VRisingCore.EntityManager;
        var query = em.CreateEntityQuery(ComponentType.ReadOnly<CastleHeart>());
        var hearts = query.ToEntityArray(Unity.Collections.Allocator.Temp);
        try
        {
            foreach (var heart in hearts)
            {
                if (!em.Exists(heart) || ResolveOwnerOfHeart(heart) != ownerSteamId)
                    continue;

                foreach (var castleObject in EnumerateObjectsLinkedToHeart(heart))
                {
                    var key = BuildKeyFromEntity(castleObject);
                    if (key == null || key.OwnerSteamId != ownerSteamId || !key.IsValid())
                        continue;
                    discovered[MakeCacheKey(key)] = key;
                }
            }
        }
        finally
        {
            hearts.Dispose();
            query.Dispose();
        }

        return discovered.Values.ToList();
    }

    /// <summary>
    /// Build a stable CastleObjectKey from a live entity by combining its
    /// prefab, position, heart, and resolved owner. Returns null when the
    /// entity lacks the components needed to be a castle object.
    /// </summary>
    public CastleObjectKey? BuildKeyFromEntity(Entity entity)
    {
        if (!entity.Exists() || !VRisingCore.IsReady) return null;
        try
        {
            if (!entity.Has<PrefabGUID>() || !entity.Has<Translation>()) return null;
            var em = VRisingCore.EntityManager;
            var guid = entity.Read<PrefabGUID>();
            var pos = entity.Read<Translation>().Value;

            int heartHash = 0;
            ulong owner = 0;
            var heart = ResolveCastleHeart(entity);
            if (heart.Exists())
            {
                if (heart.Has<PrefabGUID>()) heartHash = heart.Read<PrefabGUID>().GuidHash;
                owner = ResolveOwnerOfHeart(heart);
            }
            return new CastleObjectKey
            {
                OwnerSteamId = owner,
                CastleHeartPrefabHash = heartHash,
                ObjectPrefabHash = guid.GuidHash,
                MapIndex = -1,
                LocalPosition = new QuantizedPosition { X = pos.x, Y = pos.y, Z = pos.z }
            };
        }
        catch
        {
            return null;
        }
    }

    // ── Internals ─────────────────────────────────────────────────────────

    static string MakeCacheKey(CastleObjectKey k) =>
        $"{k.OwnerSteamId}:{k.CastleHeartPrefabHash}:{k.ObjectPrefabHash}:{k.MapIndex}:{k.LocalPosition.X:F2},{k.LocalPosition.Y:F2},{k.LocalPosition.Z:F2}";

    static Entity FindBestCandidate(List<Entity> candidates, CastleObjectKey key)
    {
        Entity best = Entity.Null;
        float bestDistance = float.MaxValue;
        foreach (var candidate in candidates)
        {
            if (!candidate.Exists() || !candidate.Has<Translation>()) continue;
            var pos = candidate.Read<Translation>().Value;
            var dx = pos.x - key.LocalPosition.X;
            var dy = pos.y - key.LocalPosition.Y;
            var dz = pos.z - key.LocalPosition.Z;
            var d2 = dx * dx + dy * dy + dz * dz;
            if (d2 < bestDistance)
            {
                bestDistance = d2;
                best = candidate;
            }
        }
        return best;
    }

    List<Entity> ScanWorldForPrefab(int prefabHash)
    {
        var results = new List<Entity>();
        if (!VRisingCore.IsReady) return results;
        try
        {
            var em = VRisingCore.EntityManager;
            var query = em.CreateEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<PrefabGUID>() }
            });
            var entities = query.ToEntityArray(Unity.Collections.Allocator.Temp);
            try
            {
                foreach (var e in entities)
                {
                    if (!em.Exists(e)) continue;
                    var guid = e.Read<PrefabGUID>();
                    if (guid.GuidHash == prefabHash) results.Add(e);
                }
            }
            finally
            {
                entities.Dispose();
                query.Dispose();
            }
        }
        catch
        {
            // Defensive: a bad query must not crash the resolver.
        }
        return results;
    }
}
