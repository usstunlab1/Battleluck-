using ProjectM;
using ProjectM.CastleBuilding;
using Stunlock.Core;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

/// <summary>
/// Zone-wide cleanup for BattleLuck session / mode teardown.
///
/// On session exit, end of a mode, or when a session becomes empty, this
/// service removes only entities owned by BattleLuck's session controllers
/// and strips transient BattleLuck buffs from players in the event zone.
/// Untracked world buildings and entities are never swept.
/// </summary>
public sealed class SessionCleanupService
{
    public sealed class CleanupReport
    {
        public int DestroyedEntities;
        public int DestroyedWalls;
        public int DestroyedFloors;
        public int DestroyedPlatforms;
        public int DestroyedSpawned;
        public int DestroyedNpcs;
        public int DestroyedItems;
        public int DestroyedProjectiles;
        public int DestroyedTraps;
        public int StrippedBuffs;
        public int PlayersAffected;
        public int TotalAffected;
        public long ElapsedMilliseconds;
    }

    // Well-known transient buff prefabs to strip from players on cleanup.
    // These are buffs the plugin itself applies (DOT, slow, zone indicator, etc).
    static readonly PrefabGUID[] TransientBuffsToStrip =
    {
        Prefabs.Buff_General_Ignite,
        Prefabs.Buff_General_Slow,
        Prefabs.Buff_General_Freeze,
        Prefabs.Buff_General_Stun,
        Prefabs.Buff_InCombat,
        Prefabs.Buff_General_Holy_T01,
        Prefabs.Buff_SunDamageDebuff,
        Prefabs.Buff_General_Garlic_Area
    };

    static readonly string[] TransientBuffNamesToStrip =
    {
        "Buff_General_HolyAreaProtection",
        "Buff_General_GarlicAreaProtection",
        "Buff_InCombat",
        "Buff_General_Slow",
        "Buff_General_Stun"
    };

    EntityQuery _wallsFloorsQ;
    EntityQuery _spawnedQ;
    EntityQuery _npcQ;
    EntityQuery _itemQ;
    EntityQuery _projectileQ;
    EntityQuery _platformTileQ;
    bool _init;

    /// <summary>
    /// Clear BattleLuck-tracked session entities and transient player buffs.
    /// Untracked world entities are preserved.
    /// </summary>
    public CleanupReport CleanupZone(float3 center, float radius, int? zoneHash = null)
    {
        var report = new CleanupReport();
        if (!VRisingCore.IsReady) return report;

        // Refuse to run with bogus parameters — better a no-op than a
        // 200-unit world-origin sweep that would nuke the central area.
        if (radius <= 0f) return report;
        if (math.lengthsq(center) <= 0.0001f) return report;

        float r = radius;
        float rSq = r * r;

        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            DespawnTracked(report, zoneHash);

            StripPlayerBuffsInZone(center, rSq, report);

            report.TotalAffected = report.DestroyedEntities + report.StrippedBuffs;
            sw.Stop();
            report.ElapsedMilliseconds = sw.ElapsedMilliseconds;
            LogReport(report, r, center);
        }
        catch (Exception ex)
        {
            BattleLuckPlugin.LogWarning($"[SessionCleanup] CleanupZone failed: {ex.ToString()}");
        }
        return report;
    }

    /// <summary>
    /// Sweep the entire server world for transient non-player entities and
    /// destroy them. Used on plugin unload / global reset.
    /// </summary>
    public CleanupReport CleanupAllNonPlayer()
    {
        var report = new CleanupReport();
        if (!VRisingCore.IsReady) return report;
        try
        {
            Ensure();
            float rSq = float.MaxValue;
            float3 c = float3.zero;
            Sweep(_spawnedQ, c, rSq, null, report, n => report.DestroyedSpawned += n);
            Sweep(_npcQ, c, rSq, IsNpcFilter, report, n => report.DestroyedNpcs += n);
            Sweep(_itemQ, c, rSq, null, report, n => report.DestroyedItems += n);
            Sweep(_projectileQ, c, rSq, null, report, n => report.DestroyedProjectiles += n);
            report.TotalAffected = report.DestroyedEntities;
        }
        catch (Exception ex)
        {
            BattleLuckPlugin.LogWarning($"[SessionCleanup] CleanupAllNonPlayer failed: {ex.ToString()}");
        }
        return report;
    }

    // ── Tracked resource despawn (registry-aware) ─────────────────────────

    void DespawnTracked(CleanupReport report, int? zoneHash)
    {
        try
        {
            var s = BattleLuckPlugin.Session;
            if (s == null || !zoneHash.HasValue ||
                !s.ActiveSessions.TryGetValue(zoneHash.Value, out var a) || a == null)
                return;

            try { report.DestroyedSpawned += a.Spawner?.AliveCount ?? 0; a.Spawner?.DespawnAll(); } catch { }
            try { report.DestroyedWalls += a.Border?.WallCount ?? 0; a.Border?.DespawnWalls(); } catch { }
            try { report.DestroyedFloors += a.Border?.FloorCount ?? 0; a.Border?.DespawnFloors(); } catch { }
            try { report.DestroyedPlatforms += a.Platform?.TileCount ?? 0; a.Platform?.DespawnPlatform(); } catch { }
            try { a.BorderDot?.Reset(); } catch { }
        }
        catch (Exception ex) { BattleLuckPlugin.LogWarning($"[SessionCleanup] Tracked sweep failed: {ex.Message}"); }
    }

    // ── Query setup ────────────────────────────────────────────────────────

    /// <summary>
    /// Reset cached ECS queries so the next <see cref="Ensure()"/> call rebuilds them.
    /// Call after the server world changes (e.g. scene reload, plugin re-init).
    /// </summary>
    public void RebuildQueries()
    {
        _init = false;
        _wallsFloorsQ = default;
        _spawnedQ = default;
        _npcQ = default;
        _itemQ = default;
        _projectileQ = default;
        _platformTileQ = default;
        BattleLuckPlugin.LogInfo("[SessionCleanup] Queries invalidated — will rebuild on next use.");
    }

    void Ensure()
    {
        if (_init) return;
        try
        {
            var em = VRisingCore.EntityManager;
            EnsureInner(em);
        }
        catch (Exception ex)
        {
            // Failed to build queries — leave _init = false so callers
            // can no-op. Better than throwing into a session teardown.
            BattleLuckPlugin.LogWarning($"[SessionCleanup] Ensure() failed: {ex.Message}");
            _init = false;
        }
    }

    void EnsureInner(EntityManager em)
    {
        _wallsFloorsQ = em.CreateEntityQuery(new EntityQueryDesc
        {
            All = new[] { ComponentType.ReadOnly<Translation>(), ComponentType.ReadOnly<PrefabGUID>() },
            Any = new[] { ComponentType.ReadOnly<EditableTileModel>(), ComponentType.ReadOnly<TilePosition>() },
            None = new[] { ComponentType.ReadOnly<PlayerCharacter>() }
        });

        _spawnedQ = em.CreateEntityQuery(new EntityQueryDesc
        {
            All = new[]
            {
                ComponentType.ReadOnly<Translation>(),
                ComponentType.ReadOnly<PrefabGUID>(),
                ComponentType.ReadOnly<CanPreventDisableWhenNoPlayersInRange>()
            },
            None = new[] { ComponentType.ReadOnly<PlayerCharacter>() }
        });

        _npcQ = em.CreateEntityQuery(new EntityQueryDesc
        {
            All = new[] { ComponentType.ReadOnly<Translation>(), ComponentType.ReadOnly<PrefabGUID>() },
            Any = new[] { ComponentType.ReadOnly<UnitLevel>(), ComponentType.ReadOnly<UnitStats>() },
            None = new[] { ComponentType.ReadOnly<PlayerCharacter>() }
        });

        _itemQ = em.CreateEntityQuery(new EntityQueryDesc
        {
            All = new[] { ComponentType.ReadOnly<Translation>(), ComponentType.ReadOnly<ItemPickup>() },
            None = new[] { ComponentType.ReadOnly<PlayerCharacter>() }
        });

        _projectileQ = em.CreateEntityQuery(new EntityQueryDesc
        {
            All = new[] { ComponentType.ReadOnly<Translation>(), ComponentType.ReadOnly<PrefabGUID>() },
            Any = new[] { ComponentType.ReadOnly<Projectile>() },
            None = new[] { ComponentType.ReadOnly<PlayerCharacter>() }
        });

        // NOTE: dedicated trap query omitted. V Rising exposes trap entities
        // through many component shapes (TrapSpike, BearTrap, Spikes, etc.),
        // and the defensive NPC sweep below already removes any trap-like
        // entity that has a PrefabGUID inside the zone.

        _platformTileQ = em.CreateEntityQuery(new EntityQueryDesc
        {
            All = new[] { ComponentType.ReadOnly<Translation>(), ComponentType.ReadOnly<TilePosition>() },
            None = new[] { ComponentType.ReadOnly<PlayerCharacter>() }
        });

        _init = true;
    }

    // ── Sweep helpers ─────────────────────────────────────────────────────

    /// <summary>
    /// Single-pass sweep for an EntityQuery. Destroys every entity that
    /// passes the XZ radius check and (optionally) the include predicate,
    /// bumping <paramref name="onDestroyed"/> for every successful destroy.
    /// </summary>
    void Sweep(
        EntityQuery q,
        float3 center,
        float rSq,
        Func<Entity, IncludeAction>? include,
        CleanupReport report,
        Action<int> onDestroyed)
    {
        var em = VRisingCore.EntityManager;
        var arr = q.ToEntityArray(Allocator.Temp);
        int n = 0;
        try
        {
            for (int i = 0; i < arr.Length; i++)
            {
                var e = arr[i];
                if (!em.Exists(e)) continue;
                if (IsProtectedCastleAnchor(e, em)) continue;
                if (!WithinXZ(e, em, center, rSq)) continue;
                if (include != null)
                {
                    var act = include(e);
                    if (act == IncludeAction.Skip) continue;
                    if (act == IncludeAction.CountAsOther) continue; // let a later sweep count it
                }
                try
                {
                    e.DestroyWithReason();
                    n++;
                    report.DestroyedEntities++;
                }
                catch { }
            }
        }
        finally { arr.Dispose(); }
        if (n > 0) onDestroyed(n);
    }

    /// <summary>
    /// Walls/floors use two counters — walls go to one bucket, floors to
    /// another, depending on the entity's component set. We split the
    /// walk into walls and floors via a single sweep.
    /// </summary>
    void Sweep(
        EntityQuery q,
        float3 center,
        float rSq,
        Func<Entity, bool> isFloorSelector,
        CleanupReport report,
        Action<int> onWallDestroyed,
        Action<int> onFloorDestroyed)
    {
        var em = VRisingCore.EntityManager;
        var arr = q.ToEntityArray(Allocator.Temp);
        int walls = 0, floors = 0;
        try
        {
            for (int i = 0; i < arr.Length; i++)
            {
                var e = arr[i];
                if (!em.Exists(e)) continue;
                if (IsProtectedCastleAnchor(e, em)) continue;
                if (!WithinXZ(e, em, center, rSq)) continue;
                try
                {
                    bool isFloor = isFloorSelector(e);
                    e.DestroyWithReason();
                    if (isFloor) floors++;
                    else walls++;
                    report.DestroyedEntities++;
                }
                catch { }
            }
        }
        finally { arr.Dispose(); }
        if (walls > 0) onWallDestroyed(walls);
        if (floors > 0) onFloorDestroyed(floors);
    }

    enum IncludeAction { Include, Skip, CountAsOther }

    // ── Entity classification ─────────────────────────────────────────────

    bool IsWall(Entity e)
    {
        var em = VRisingCore.EntityManager;
        return em.HasComponent<EditableTileModel>(e) && !em.HasComponent<TilePosition>(e);
    }

    IncludeAction IsPlatformFilter(Entity e)
    {
        var em = VRisingCore.EntityManager;
        if (IsProtectedCastleAnchor(e, em)) return IncludeAction.Skip;
        if (em.HasComponent<EditableTileModel>(e)) return IncludeAction.Skip; // wall — handled elsewhere
        if (em.HasComponent<TilePosition>(e)) return IncludeAction.Include;
        return IncludeAction.Skip;
    }

    IncludeAction IsNpcFilter(Entity e)
    {
        var em = VRisingCore.EntityManager;
        if (IsProtectedCastleAnchor(e, em)) return IncludeAction.Skip;
        if (em.HasComponent<CanPreventDisableWhenNoPlayersInRange>(e)) return IncludeAction.Skip; // spawned-sweep handles
        if (em.HasComponent<ItemPickup>(e)) return IncludeAction.Skip;
        if (em.HasComponent<Projectile>(e)) return IncludeAction.Skip;
        if (em.HasComponent<TilePosition>(e)) return IncludeAction.Skip;
        return IncludeAction.Include;
    }

    static bool IsProtectedCastleAnchor(Entity e, EntityManager em)
    {
        if (CastleTileOwnershipService.IsPermanentCastleEntity(e)) return true;
        if (em.HasComponent<CastleHeartConnection>(e)) return true;
        var prefab = em.HasComponent<PrefabGUID>(e) ? em.GetComponentData<PrefabGUID>(e) : PrefabGUID.Empty;
        return prefab.GuidHash is -485435268 or -1994745735;
    }

    static bool WithinXZ(Entity e, EntityManager em, float3 center, float rSq)
    {
        if (!em.HasComponent<Translation>(e)) return false;
        var p = em.GetComponentData<Translation>(e).Value;
        float dx = p.x - center.x;
        float dz = p.z - center.z;
        return (dx * dx + dz * dz) <= rSq;
    }

    // ── Player buff strip ─────────────────────────────────────────────────

    void StripPlayerBuffsInZone(float3 center, float rSq, CleanupReport report)
    {
        var em = VRisingCore.EntityManager;
        var players = VRisingCore.GetOnlinePlayers();
        try
        {
            foreach (var player in players)
            {
                if (!player.Exists() || !player.IsPlayer()) continue;
                if (!WithinXZ(player, em, center, rSq)) continue;

                report.PlayersAffected++;
                int stripped = 0;
                foreach (var buffPrefab in GetTransientBuffsToStrip())
                {
                    try
                    {
                        if (buffPrefab == PrefabGUID.Empty) continue;
                        player.TryRemoveBuff(buffPrefab);
                        stripped++;
                    }
                    catch { }
                }
                report.StrippedBuffs += stripped;
            }
        }
        catch { }
    }

    static IEnumerable<PrefabGUID> GetTransientBuffsToStrip()
    {
        var seen = new HashSet<int>();
        foreach (var buff in TransientBuffsToStrip)
        {
            if (buff == PrefabGUID.Empty || !seen.Add(buff.GuidHash))
                continue;
            yield return buff;
        }

        foreach (var name in TransientBuffNamesToStrip)
        {
            if (!PrefabHelper.TryGetValidPrefabGuidDeep(name, out var guid))
                continue;
            if (guid == PrefabGUID.Empty || !seen.Add(guid.GuidHash))
                continue;
            yield return guid;
        }
    }

    // ── Logging ───────────────────────────────────────────────────────────

    static void LogReport(CleanupReport r, float rUsed, float3 center)
    {
        BattleLuckPlugin.LogInfo(
            $"[SessionCleanup] zone=({center.x:F0},{center.z:F0}) r={rUsed:F0} → " +
            $"walls={r.DestroyedWalls} floors={r.DestroyedFloors} " +
            $"platforms={r.DestroyedPlatforms} spawned={r.DestroyedSpawned} " +
            $"npcs={r.DestroyedNpcs} items={r.DestroyedItems} " +
            $"projectiles={r.DestroyedProjectiles} traps={r.DestroyedTraps} " +
            $"buffs={r.StrippedBuffs} playersAffected={r.PlayersAffected} " +
            $"total={r.TotalAffected} elapsed={r.ElapsedMilliseconds}ms.");
    }
}
