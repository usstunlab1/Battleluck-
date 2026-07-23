using ProjectM;
using Stunlock.Core;
using Unity.Entities;
using Unity.Mathematics;

/// <summary>
/// Spawns enemies, bosses, and VBloods for PvE game modes.
/// Uses UnitSpawnerUpdateSystem.SpawnUnit with callback pattern from VAMP SpawnService.
/// Also supports direct InstantiateEntityImmediate for immediate spawns.
/// </summary>
public sealed class SpawnController
{
    readonly Dictionary<Entity, SpawnedUnit> _tracked = new();

    // ── Common enemy / boss prefab GUIDs ────────────────────────────────

// Regular enemies (varying difficulty)
    public static readonly PrefabGUID Skeleton_Mage = new(-539289064);
    public static readonly PrefabGUID Skeleton_Warrior = new(-1584807109);
    public static readonly PrefabGUID Skeleton_Archer = new(-1340402506);
    public static readonly PrefabGUID Ghoul = new(-1508186605);
    public static readonly PrefabGUID Bandit_Thug = new(1458281806);
    public static readonly PrefabGUID Bandit_Hunter = new(-1000550829);
    public static readonly PrefabGUID Militia_Guard = new(-1101895538);
    public static readonly PrefabGUID Militia_Devoted = new(1820387430);
    public static readonly PrefabGUID Church_Paladin = new(-1791316508);
    public static readonly PrefabGUID Vampire_Cultist = new(-707081968);

    // Elite / mini-boss enemies
    public static readonly PrefabGUID Bandit_Bomber = new(-1090756563);
    public static readonly PrefabGUID Church_Captain = new(1090737596);
    public static readonly PrefabGUID Bear_Dire = new(-1391546585);
    public static readonly PrefabGUID Forest_Wolf = new(1446249180);

    // VBlood bosses (world bosses)
    public static readonly PrefabGUID VBlood_Errol = new(-484556888);
    public static readonly PrefabGUID VBlood_Grayson = new(1106149033);
    public static readonly PrefabGUID VBlood_Putrid = new(-1905691330);
    public static readonly PrefabGUID VBlood_Keely = new(-1065970933);
    public static readonly PrefabGUID VBlood_Nicholaus = new(153390636);
    public static readonly PrefabGUID VBlood_Quincey = new(-680831417);
    public static readonly PrefabGUID VBlood_Jade = new(-1968372384);
    public static readonly PrefabGUID VBlood_Octavian = new(1688478381);
    public static readonly PrefabGUID VBlood_Dracula = new(-327335305);

    // Wave difficulty tiers (low → high)
    public static readonly List<PrefabGUID> Tier1Enemies = new() { Skeleton_Warrior, Skeleton_Archer, Skeleton_Mage };
    public static readonly List<PrefabGUID> Tier2Enemies = new() { Ghoul, Bandit_Thug, Bandit_Hunter };
    public static readonly List<PrefabGUID> Tier3Enemies = new() { Militia_Guard, Militia_Devoted, Church_Paladin };
    public static readonly List<PrefabGUID> Tier4Enemies = new() { Bandit_Bomber, Church_Captain, Vampire_Cultist };
    public static readonly List<PrefabGUID> EliteEnemies = new() { Bear_Dire, Forest_Wolf, Church_Captain };

    /// <summary>
    /// Spawn a unit using UnitSpawnerUpdateSystem (proper game spawn with AI/pathfinding).
    /// The callback fires once the entity is fully initialized by the game engine.
    /// </summary>
    public void SpawnWithCallback(PrefabGUID prefab, float3 position, float duration = 0f, Action<Entity>? postActions = null)
    {
        if (!IsValidSpawnPrefab(prefab))
        {
            BattleLuckPlugin.LogWarning($"[SpawnController] Skipped invalid spawn prefab {prefab.GuidHash} at ({position.x:F0}, {position.y:F0}, {position.z:F0}).");
            return;
        }

        var usus = VRisingCore.Server.GetExistingSystemManaged<UnitSpawnerUpdateSystem>();
        if (usus == null)
        {
            BattleLuckPlugin.LogWarning($"[SpawnController] UnitSpawnerUpdateSystem not available - cannot spawn prefab {prefab.GuidHash} at ({position.x:F0}, {position.y:F0}, {position.z:F0}).");
            return;
        }

        var durationKey = UnitSpawnerPatch.NextKey();

        usus.SpawnUnit(Entity.Null, prefab, position, 1, 1, 1, durationKey);

        UnitSpawnerPatch.PostActions[durationKey] = (duration, entity =>
        {
            // Apply standard post-spawn fixes
            ApplyPostSpawnFixes(entity);

            // Track the entity
            _tracked[entity] = new SpawnedUnit
            {
                Entity = entity,
                Prefab = prefab,
                SpawnPosition = position,
                SpawnedAtUtc = DateTime.UtcNow
            };

            BattleLuckPlugin.LogInfo($"[SpawnController] Unit spawned via callback: {prefab.GuidHash} at ({position.x:F0}, {position.y:F0}, {position.z:F0})");

            // Execute additional post-spawn actions
            postActions?.Invoke(entity);
        });
    }

    /// <summary>
    /// Spawn a unit immediately using InstantiateEntityImmediate (no AI initialization).
    /// Use SpawnWithCallback for full NPC spawns with AI/pathfinding.
    /// </summary>
    public Entity SpawnImmediate(PrefabGUID prefab, float3 position, Entity? owner = null)
    {
        if (!IsValidSpawnPrefab(prefab))
        {
            BattleLuckPlugin.LogWarning($"[SpawnController] Skipped invalid immediate spawn prefab {prefab.GuidHash} at ({position.x:F0}, {position.y:F0}, {position.z:F0}).");
            return Entity.Null;
        }

        var ownerEntity = owner ?? Entity.Null;
        var entity = EntityExtensions.SpawnUnit(prefab, ownerEntity, position);

        if (entity.Exists())
        {
            _tracked[entity] = new SpawnedUnit
            {
                Entity = entity,
                Prefab = prefab,
                SpawnPosition = position,
                SpawnedAtUtc = DateTime.UtcNow
            };
        }

        return entity;
    }

    /// <summary>Spawn a wave of enemies with proper AI using UnitSpawnerUpdateSystem.</summary>
    public void SpawnWave(List<PrefabGUID> prefabs, int count, float3 center, float spread = 5f, Action<List<Entity>>? onWaveComplete = null, int? seed = null)
    {
        var validPrefabs = prefabs.Where(IsValidSpawnPrefab).ToList();
        if (validPrefabs.Count != prefabs.Count)
        {
            var invalid = prefabs.Where(p => !IsValidSpawnPrefab(p)).Select(p => p.GuidHash.ToString()).Distinct();
            BattleLuckPlugin.LogWarning($"[SpawnController] Wave skipped invalid prefab(s): {string.Join(", ", invalid)}.");
        }

        if (validPrefabs.Count == 0)
        {
            if (IsValidSpawnPrefab(Skeleton_Warrior))
                validPrefabs.Add(Skeleton_Warrior);
            else
            {
                BattleLuckPlugin.LogWarning("[SpawnController] Wave canceled: no valid enemy prefabs available.");
                onWaveComplete?.Invoke(new List<Entity>());
                return;
            }
        }

        // Use seeded RNG for deterministic replay when seed is provided.
        // Without a seed, uses time-based seed for variety.
        var rng = seed.HasValue ? new System.Random(seed.Value) : new System.Random();
        int spawned = 0;
        var entities = new List<Entity>();

        for (int i = 0; i < count; i++)
        {
            var prefab = validPrefabs[rng.Next(validPrefabs.Count)];
            var offset = new float3(
                (float)(rng.NextDouble() * 2 - 1) * spread,
                0,
                (float)(rng.NextDouble() * 2 - 1) * spread
            );
            var pos = center + offset;
            int capturedIndex = i;

            SpawnWithCallback(prefab, pos, duration: 0f, entity =>
            {
                entities.Add(entity);
                spawned++;
                if (spawned >= count)
                {
                    BattleLuckPlugin.LogInfo($"[SpawnController] Wave complete: {entities.Count}/{count} units.");
                    onWaveComplete?.Invoke(entities);
                }
            });
        }
    }

    /// <summary>Spawn a boss with proper AI using UnitSpawnerUpdateSystem.</summary>
    public void SpawnBoss(PrefabGUID bossPrefab, float3 position, Action<Entity>? onSpawned = null)
    {
        SpawnWithCallback(bossPrefab, position, duration: 0f, entity =>
        {
            BattleLuckPlugin.LogInfo($"[SpawnController] Boss spawned: {bossPrefab.GuidHash} at ({position.x:F0}, {position.y:F0}, {position.z:F0})");
            onSpawned?.Invoke(entity);
        });
    }

    /// <summary>Spawn a boss with level scaling applied.</summary>
    public void SpawnBoss(PrefabGUID bossPrefab, float3 position, int level, Action<Entity>? onSpawned = null)
    {
        SpawnWithCallback(bossPrefab, position, duration: 0f, entity =>
        {
            ApplyLevelScaling(entity, level);
            BattleLuckPlugin.LogInfo($"[SpawnController] Boss spawned: {bossPrefab.GuidHash} at ({position.x:F0}, {position.y:F0}, {position.z:F0}) with level {level}");
            onSpawned?.Invoke(entity);
        });
    }

    /// <summary>Spawn an NPC (servant) with proper AI using UnitSpawnerUpdateSystem.</summary>
    public void SpawnNPC(PrefabGUID npcPrefab, float3 position, Action<Entity>? onSpawned = null)
    {
        SpawnWithCallback(npcPrefab, position, duration: 0f, entity =>
        {
            BattleLuckPlugin.LogInfo($"[SpawnController] NPC spawned: {npcPrefab.GuidHash} at ({position.x:F0}, {position.y:F0}, {position.z:F0})");
            onSpawned?.Invoke(entity);
        });
    }

    /// <summary>Apply level scaling to a spawned boss entity.</summary>
    public void ApplyLevelScaling(Entity entity, int level)
    {
        if (level <= 0) return;

        var em = VRisingCore.EntityManager;

        // Scale boss lifetime duration based on level for extended fight duration
        if (em.HasComponent<LifeTime>(entity))
        {
            var lifeTime = em.GetComponentData<LifeTime>(entity);
            lifeTime.Duration = Math.Max(lifeTime.Duration, level * 60f);
            em.SetComponentData(entity, lifeTime);
        }
    }

    /// <summary>Apply standard post-spawn fixes (prevent disable, remove drops, remove convertable).</summary>
    static void ApplyPostSpawnFixes(Entity entity)
    {
        var em = VRisingCore.EntityManager;

        // Prevent entity from being disabled when no players nearby
        if (!em.HasComponent<CanPreventDisableWhenNoPlayersInRange>(entity))
            em.AddComponent<CanPreventDisableWhenNoPlayersInRange>(entity);
        entity.With((ref CanPreventDisableWhenNoPlayersInRange c) => c.CanDisable = new ModifiableBool(false));

        // Remove drop tables
        // DropTableBuffer not available in this build - skipped

        // Remove convertability
        if (entity.Has<ServantConvertable>())
            em.RemoveComponent<ServantConvertable>(entity);
        if (entity.Has<CharmSource>())
            em.RemoveComponent<CharmSource>(entity);
    }

    /// <summary>Get the appropriate enemy tier for a wave number.</summary>
    public static List<PrefabGUID> GetEnemiesForWave(int waveNumber)
    {
        var candidates = waveNumber switch
        {
            <= 2 => Tier1Enemies,
            <= 4 => Tier2Enemies,
            <= 6 => Tier3Enemies,
            <= 8 => Tier4Enemies,
            _ => EliteEnemies
        };

        var valid = candidates.Where(IsValidSpawnPrefab).ToList();
        return valid.Count > 0 ? valid : new List<PrefabGUID> { Skeleton_Warrior };
    }

    static bool IsValidSpawnPrefab(PrefabGUID prefab)
    {
        if (prefab == PrefabGUID.Empty || prefab.GuidHash == 0)
            return false;

        try
        {
            return PrefabHelper.ValidatePrefab(prefab);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Record that a tracked entity was killed. Returns true if it was tracked.</summary>
    public bool RecordKill(Entity entity)
    {
        return _tracked.Remove(entity);
    }

    public bool RecordKill(Entity entity, out SpawnedUnit unit)
    {
        if (_tracked.TryGetValue(entity, out var tracked))
        {
            unit = tracked;
            _tracked.Remove(entity);
            return true;
        }

        unit = new SpawnedUnit();
        return false;
    }

    public bool TryRespawn(SpawnedUnit unit)
    {
        if (unit.LivesRemaining <= 1)
            return false;

        var remaining = unit.LivesRemaining - 1;
        SpawnWithCallback(unit.Prefab, unit.SpawnPosition, postActions: entity =>
        {
            if (_tracked.TryGetValue(entity, out var respawned))
                respawned.LivesRemaining = remaining;
        });
        return true;
    }

    /// <summary>Destroy all tracked spawned entities.</summary>
    public void DespawnAll()
    {
        int destroyed = 0;
        int deferred = 0;
        foreach (var kv in _tracked)
        {
            if (kv.Key.Exists())
            {
                try
                {
                    kv.Key.DestroyWithReason();
                    destroyed++;
                }
                catch (Exception ex) when (ex.Message.Contains("in live", StringComparison.OrdinalIgnoreCase))
                {
                    // Entity is currently being processed by ECS systems - will be cleaned up on next tick
                    // The Disabled component added by DestroyWithReason will prevent further processing
                    deferred++;
                }
                catch (Exception ex)
                {
                    BattleLuckPlugin.LogWarning($"[SpawnController] Failed to destroy entity {kv.Key}: {ex.Message}");
                }
            }
        }
        _tracked.Clear();
        BattleLuckPlugin.LogInfo($"[SpawnController] Despawned {destroyed} tracked units, {deferred} deferred (in live state).");
    }

    /// <summary>Number of currently tracked (alive) spawned units.</summary>
    public int AliveCount => _tracked.Count(kv => kv.Key.Exists());

    /// <summary>Get all tracked entities.</summary>
    public IReadOnlyDictionary<Entity, SpawnedUnit> TrackedUnits => _tracked;

    public void Reset()
    {
        DespawnAll();
    }
}

public sealed class SpawnedUnit
{
    public Entity Entity { get; set; }
    public PrefabGUID Prefab { get; set; }
    public float3 SpawnPosition { get; set; }
    public DateTime SpawnedAtUtc { get; set; }
    public int LivesRemaining { get; set; } = 3;
}
