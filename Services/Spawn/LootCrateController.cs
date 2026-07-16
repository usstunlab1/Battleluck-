using Unity.Entities;
using Unity.Mathematics;
using Stunlock.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using BattleLuck.Services.Flow;
/// <summary>
/// Tracked ECS loot chest spawner. Chests may stay locked until a configured kill
/// threshold and can be awarded only to the resolved winner at match end.
/// </summary>
public sealed class LootCrateController
{
    readonly List<CrateInstance> _activeCrates = new();
    readonly System.Random _rng = new();
    LootCrateConfig? _config;
    float _spawnTimer;
    bool _configured;

    public int ActiveCount => _activeCrates.Count;
    public int LockedUntilKills => Math.Max(0, _config?.LockedUntilKills ?? 0);
    public bool WinnerOnly => _config?.WinnerOnly == true;

    public void Configure(LootCrateConfig? config)
    {
        if (config == null || !config.Enabled || config.CrateTypes.Count == 0)
        {
            _configured = false;
            return;
        }
        _config = config;
        _spawnTimer = config.SpawnIntervalSec;
        _configured = true;
    }

    /// <summary>
    /// Tick crate spawning, despawning, and proximity collection.
    /// centerPos = current arena center. gridHalfExtent = platform half-size for spawn bounds.
    /// Returns list of collected crate types for the caller to apply effects.
    /// </summary>
    public List<(Entity player, CrateTypeConfig crate)> Tick(
        float3 centerPos, float gridHalfExtent, float deltaSeconds,
        IEnumerable<Entity> players, string sessionId, Func<ulong, bool>? canCollect = null)
    {
        var collected = new List<(Entity, CrateTypeConfig)>();
        if (!_configured || _config == null) return collected;

        // Despawn expired crates
        DespawnExpired();

        // Spawn timer
        _spawnTimer -= deltaSeconds;
        if (_spawnTimer <= 0 && _activeCrates.Count < _config.MaxActiveCrates)
        {
            _spawnTimer = _config.SpawnIntervalSec;
            SpawnCrate(centerPos, gridHalfExtent, sessionId);
        }

        // Proximity collection
        var em = VRisingCore.EntityManager;
        foreach (var player in players)
        {
            try
            {
                if (!em.Exists(player)) continue;
                var steamId = player.GetSteamId();
                if (canCollect != null && !canCollect(steamId)) continue;
                var playerPos = player.GetPosition();

                for (int i = _activeCrates.Count - 1; i >= 0; i--)
                {
                    var crate = _activeCrates[i];
                    float dist = math.distance(playerPos.xz, crate.Position.xz);
                    if (dist < 1.5f)
                    {
                        collected.Add((player, crate.Type));
                        DestroyCrate(crate);
                        _activeCrates.RemoveAt(i);

                        GameEvents.OnCrateCollected?.Invoke(new CrateCollectedEvent
                        {
                            SessionId = sessionId,
                            SteamId = steamId,
                            CrateId = crate.Type.Type,
                            LootTable = crate.Type.Prefab
                        });
                    }
                }
            }
            catch { /* skip unreadable players */ }
        }

        return collected;
    }

    public int GrantAllTo(Entity recipient)
    {
        if (!recipient.Exists() || !recipient.IsPlayer()) return 0;

        var granted = 0;
        foreach (var crate in _activeCrates.ToList())
        {
            var item = PrefabHelper.GetPrefabGuidDeep(crate.Type.Prefab);
            var amount = Math.Max(1, crate.Type.Amount);
            if (item.HasValue && recipient.TryGiveItem(item.Value, amount))
                granted += amount;
        }
        return granted;
    }

    public List<(PrefabGUID Item, int Amount)> GetAllRewards()
    {
        var rewards = new List<(PrefabGUID Item, int Amount)>();
        foreach (var crate in _activeCrates)
        {
            var item = PrefabHelper.GetPrefabGuidDeep(crate.Type.Prefab);
            if (item.HasValue)
                rewards.Add((item.Value, Math.Max(1, crate.Type.Amount)));
        }
        return rewards;
    }

    /// <summary>Sync crate positions after platform moves.</summary>
    public void SyncCratePositions(float3 centerDelta)
    {
        if (math.lengthsq(centerDelta.xz) < 0.0001f) return;
        var em = VRisingCore.EntityManager;
        foreach (var crate in _activeCrates)
        {
            crate.Position += centerDelta;
            try
            {
                if (em.Exists(crate.Entity))
                    crate.Entity.SetPosition(crate.Position);
            }
            catch { /* best-effort */ }
        }
    }

    public void ClearAllCrates()
    {
        foreach (var crate in _activeCrates)
            DestroyCrate(crate);
        _activeCrates.Clear();
        _spawnTimer = _config?.SpawnIntervalSec ?? 15f;
    }

    void SpawnCrate(float3 center, float halfExtent, string sessionId)
    {
        if (_config == null) return;

        var type = SelectWeightedRandom();
        if (type == null) return;

        halfExtent = Math.Max(2f, Math.Abs(halfExtent));
        var centerAngle = _activeCrates.Count * (Math.PI * 2d / Math.Max(1, _config.MaxActiveCrates));
        float rx = _config.SpawnAtCenter ? (float)Math.Cos(centerAngle) * 2.5f : (float)(_rng.NextDouble() * 2 - 1) * halfExtent * 0.8f;
        float rz = _config.SpawnAtCenter ? (float)Math.Sin(centerAngle) * 2.5f : (float)(_rng.NextDouble() * 2 - 1) * halfExtent * 0.8f;
        var pos = new float3(center.x + rx, center.y + 0.5f, center.z + rz);

        try
        {
            var spawned = SchematicLoader.SpawnPrefabAt(
                _config.ContainerPrefab,
                pos,
                kind: "loot_chest",
                trackingGroup: string.IsNullOrWhiteSpace(sessionId) ? "event_loot" : sessionId);
            if (spawned.Success && spawned.Value != null)
            {
                _activeCrates.Add(new CrateInstance
                {
                    Entity = spawned.Value.Entity,
                    Type = type,
                    SpawnedAt = DateTime.UtcNow,
                    Position = pos
                });
            }
            else
                BattleLuckPlugin.LogWarning($"[LootCrate] Container spawn failed: {spawned.Error}");
        }
        catch (Exception ex)
        {
            BattleLuckPlugin.LogWarning($"[LootCrate] Spawn failed: {ex.Message}");
        }
    }

    CrateTypeConfig? SelectWeightedRandom()
    {
        if (_config == null || _config.CrateTypes.Count == 0) return null;

        int totalWeight = 0;
        foreach (var ct in _config.CrateTypes) totalWeight += ct.Weight;
        if (totalWeight <= 0) return null;

        int roll = _rng.Next(totalWeight);
        int cumulative = 0;
        foreach (var ct in _config.CrateTypes)
        {
            cumulative += ct.Weight;
            if (roll < cumulative) return ct;
        }
        return _config.CrateTypes[^1];
    }

    void DespawnExpired()
    {
        if (_config == null) return;
        var now = DateTime.UtcNow;
        for (int i = _activeCrates.Count - 1; i >= 0; i--)
        {
            if ((now - _activeCrates[i].SpawnedAt).TotalSeconds >= _config.DespawnAfterSec)
            {
                DestroyCrate(_activeCrates[i]);
                _activeCrates.RemoveAt(i);
            }
        }

        // FIFO cleanup if over max
        while (_config != null && _activeCrates.Count > _config.MaxActiveCrates)
        {
            DestroyCrate(_activeCrates[0]);
            _activeCrates.RemoveAt(0);
        }
    }

    void DestroyCrate(CrateInstance crate)
    {
        try
        {
            var ecb = EcbHelper.GetEcb();
            if (!ecb.Equals(default(EntityCommandBuffer)))
            {
                if (crate.Entity != Entity.Null)
                    ecb.DestroyEntity(crate.Entity);
                if (crate.GlowEntity != Entity.Null)
                    ecb.DestroyEntity(crate.GlowEntity);
            }
        }
        catch { /* best-effort */ }
    }
}
