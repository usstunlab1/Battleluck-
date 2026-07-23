using System.Text.Json.Serialization;

namespace BattleLuck.Models;

// ── Participant Analysis ─────────────────────────────────────────────────────

public sealed class EventParticipantProfile
{
    [JsonPropertyName("playerCount")] public int PlayerCount { get; init; }
    [JsonPropertyName("averageLevel")] public float AverageLevel { get; init; }
    [JsonPropertyName("minimumLevel")] public float MinimumLevel { get; init; }
    [JsonPropertyName("maximumLevel")] public float MaximumLevel { get; init; }
    [JsonPropertyName("averageCombatStrength")] public float AverageCombatStrength { get; init; }
    [JsonPropertyName("peakCombatStrength")] public float PeakCombatStrength { get; init; }
    [JsonPropertyName("players")] public IReadOnlyList<PlayerCombatProfile> Players { get; init; } = Array.Empty<PlayerCombatProfile>();
}

public sealed class PlayerCombatProfile
{
    [JsonPropertyName("steamId")] public ulong SteamId { get; init; }
    [JsonPropertyName("equipmentLevel")] public int EquipmentLevel { get; init; }
    [JsonPropertyName("maximumHealth")] public float MaximumHealth { get; init; }
    [JsonPropertyName("currentHealthRatio")] public float CurrentHealthRatio { get; init; }
    [JsonPropertyName("equippedWeapon")] public PrefabGUID EquippedWeapon { get; init; }
    [JsonPropertyName("weaponCategory")] public WeaponCategory WeaponCategory { get; init; }
    [JsonPropertyName("estimatedDamageRating")] public float EstimatedDamageRating { get; init; }
    [JsonPropertyName("estimatedDefenseRating")] public float EstimatedDefenseRating { get; init; }
    [JsonPropertyName("combatStrength")] public float CombatStrength { get; init; }
}

// ── Threat Budget ────────────────────────────────────────────────────────────

public sealed class ThreatBudget
{
    [JsonPropertyName("totalBudget")] public float TotalBudget { get; init; }
    [JsonPropertyName("eliteBudget")] public float EliteBudget { get; init; }
    [JsonPropertyName("standardBudget")] public float StandardBudget { get; init; }
    [JsonPropertyName("supportBudget")] public float SupportBudget { get; init; }
    [JsonPropertyName("maximumNpcCount")] public int MaximumNpcCount { get; init; }
}

// ── Spawn Plan ───────────────────────────────────────────────────────────────

public sealed class AdaptiveSpawnPlan
{
    [JsonPropertyName("eventId")] public string EventId { get; init; } = "";
    [JsonPropertyName("calculatedDifficulty")] public float CalculatedDifficulty { get; init; }
    [JsonPropertyName("waves")] public IReadOnlyList<SpawnWavePlan> Waves { get; init; } = Array.Empty<SpawnWavePlan>();
    [JsonPropertyName("rewardBudget")] public RewardBudget RewardBudget { get; init; } = new();
    [JsonPropertyName("safetyLimits")] public SpawnSafetyLimits SafetyLimits { get; init; } = new();
}

public sealed class SpawnWavePlan
{
    [JsonPropertyName("waveIndex")] public int WaveIndex { get; init; }
    [JsonPropertyName("threatBudget")] public float ThreatBudget { get; init; }
    [JsonPropertyName("npcs")] public IReadOnlyList<SpawnNpcPlan> Npcs { get; init; } = Array.Empty<SpawnNpcPlan>();
    [JsonPropertyName("startDelaySeconds")] public float StartDelaySeconds { get; init; }
    [JsonPropertyName("completionCondition")] public string CompletionCondition { get; init; } = "all_defeated";
    [JsonPropertyName("drillId")] public string DrillId { get; init; } = "";
}

public sealed class SpawnNpcPlan
{
    [JsonPropertyName("catalogId")] public string CatalogId { get; init; } = "";
    [JsonPropertyName("prefabGuid")] public PrefabGUID PrefabGuid { get; init; }
    [JsonPropertyName("count")] public int Count { get; init; } = 1;
    [JsonPropertyName("behaviorProfileId")] public string BehaviorProfileId { get; init; } = "attack";
    [JsonPropertyName("spawnZoneId")] public string SpawnZoneId { get; init; } = "";
    [JsonPropertyName("healthScale")] public float HealthScale { get; init; } = 1f;
    [JsonPropertyName("damageScale")] public float DamageScale { get; init; } = 1f;
}

public sealed class SpawnSafetyLimits
{
    [JsonPropertyName("maximumTotalNpcs")] public int MaximumTotalNpcs { get; init; } = 32;
    [JsonPropertyName("maximumNpcsPerWave")] public int MaximumNpcsPerWave { get; init; } = 16;
    [JsonPropertyName("maximumElitesPerWave")] public int MaximumElitesPerWave { get; init; } = 4;
    [JsonPropertyName("maximumBossesPerEvent")] public int MaximumBossesPerEvent { get; init; } = 1;
    [JsonPropertyName("minimumSpawnSpacing")] public float MinimumSpawnSpacing { get; init; } = 3f;
}

// ── Event Catalog ────────────────────────────────────────────────────────────

public sealed class EventCatalogContext
{
    [JsonPropertyName("eventId")] public string EventId { get; init; } = "";
    [JsonPropertyName("npcs")] public IReadOnlyDictionary<string, NpcCatalogEntry> Npcs { get; init; } = new Dictionary<string, NpcCatalogEntry>();
    [JsonPropertyName("waves")] public IReadOnlyDictionary<string, WaveSchematic> Waves { get; init; } = new Dictionary<string, WaveSchematic>();
    [JsonPropertyName("rewards")] public IReadOnlyDictionary<string, RewardProfile> Rewards { get; init; } = new Dictionary<string, RewardProfile>();
    [JsonPropertyName("drills")] public IReadOnlyDictionary<string, CombatDrillDefinition> Drills { get; init; } = new Dictionary<string, CombatDrillDefinition>();
}

public sealed class NpcCatalogEntry
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("prefabHash")] public int PrefabHash { get; set; }
    [JsonPropertyName("prefabName")] public string PrefabName { get; set; } = "";
    [JsonPropertyName("threatCost")] public float ThreatCost { get; set; } = 8f;
    [JsonPropertyName("minimumRecommendedStrength")] public float MinimumRecommendedStrength { get; set; }
    [JsonPropertyName("maximumRecommendedStrength")] public float MaximumRecommendedStrength { get; set; } = 999f;
    [JsonPropertyName("roles")] public List<string> Roles { get; set; } = new();
    [JsonPropertyName("allowedEvents")] public List<string> AllowedEvents { get; set; } = new();
    [JsonPropertyName("rewardProfile")] public string RewardProfile { get; set; } = "basic_combat";
    [JsonPropertyName("isElite")] public bool IsElite { get; set; }
    [JsonPropertyName("isBoss")] public bool IsBoss { get; set; }
    [JsonPropertyName("defaultBehavior")] public string DefaultBehavior { get; set; } = "attack";
}

public sealed class WaveSchematic
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("npcIds")] public List<string> NpcIds { get; set; } = new();
    [JsonPropertyName("threatMultiplier")] public float ThreatMultiplier { get; set; } = 1f;
    [JsonPropertyName("spawnDelaySeconds")] public float SpawnDelaySeconds { get; set; }
    [JsonPropertyName("completionCondition")] public string CompletionCondition { get; set; } = "all_defeated";
    [JsonPropertyName("drillId")] public string DrillId { get; set; } = "";
}

public sealed class RewardProfile
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("itemPrefabs")] public List<int> ItemPrefabs { get; set; } = new();
    [JsonPropertyName("itemCount")] public int ItemCount { get; set; } = 1;
    [JsonPropertyName("estimatedValue")] public float EstimatedValue { get; set; } = 10f;
    [JsonPropertyName("eventDifficultyTier")] public int EventDifficultyTier { get; set; } = 1;
    [JsonPropertyName("minimumCombatStrength")] public float MinimumCombatStrength { get; set; }
    [JsonPropertyName("maximumCombatStrength")] public float MaximumCombatStrength { get; set; } = 999f;
}

// ── Reward Budget ────────────────────────────────────────────────────────────

public sealed class RewardBudget
{
    [JsonPropertyName("maximumItemsPerPlayer")] public int MaximumItemsPerPlayer { get; init; } = 3;
    [JsonPropertyName("maximumItemsTotal")] public int MaximumItemsTotal { get; init; } = 20;
    [JsonPropertyName("maximumValuePerPlayer")] public float MaximumValuePerPlayer { get; init; } = 100f;
    [JsonPropertyName("allowedItems")] public IReadOnlySet<PrefabGUID> AllowedItems { get; init; } = new HashSet<PrefabGUID>();
    [JsonPropertyName("blockedItems")] public IReadOnlySet<PrefabGUID> BlockedItems { get; init; } = new HashSet<PrefabGUID>();
}

// ── Player Observation ───────────────────────────────────────────────────────

public sealed class PlayerObservation
{
    [JsonPropertyName("steamId")] public ulong SteamId { get; init; }
    [JsonPropertyName("position")] public float3 Position { get; init; }
    [JsonPropertyName("velocity")] public float3 Velocity { get; init; }
    [JsonPropertyName("distanceToNpc")] public float DistanceToNpc { get; init; }
    [JsonPropertyName("healthRatio")] public float HealthRatio { get; init; }
    [JsonPropertyName("equippedWeapon")] public PrefabGUID EquippedWeapon { get; init; }
    [JsonPropertyName("weaponCategory")] public WeaponCategory WeaponCategory { get; init; }
    [JsonPropertyName("isInCombat")] public bool IsInCombat { get; init; }
    [JsonPropertyName("isCasting")] public bool IsCasting { get; init; }
    [JsonPropertyName("isDashing")] public bool IsDashing { get; init; }
    [JsonPropertyName("isMovingTowardNpc")] public bool IsMovingTowardNpc { get; init; }
    [JsonPropertyName("isMovingAwayFromNpc")] public bool IsMovingAwayFromNpc { get; init; }
    [JsonPropertyName("activeBuffs")] public IReadOnlySet<PrefabGUID> ActiveBuffs { get; init; } = new HashSet<PrefabGUID>();
    [JsonPropertyName("recentAbilityEffects")] public IReadOnlySet<PrefabGUID> RecentAbilityEffects { get; init; } = new HashSet<PrefabGUID>();
}

// ── Offset Follow Configuration ──────────────────────────────────────────────

public sealed class OffsetFollowConfig
{
    [JsonPropertyName("forwardOffset")] public float ForwardOffset { get; init; }
    [JsonPropertyName("sideOffset")] public float SideOffset { get; init; }
    [JsonPropertyName("positionTolerance")] public float PositionTolerance { get; init; } = 1.5f;
    [JsonPropertyName("pathUpdateIntervalSeconds")] public float PathUpdateIntervalSeconds { get; init; } = 0.3f;
    [JsonPropertyName("maximumFollowDistance")] public float MaximumFollowDistance { get; init; } = 40f;
    [JsonPropertyName("minimumMovementSpeed")] public float MinimumMovementSpeed { get; init; } = 2f;
    [JsonPropertyName("maximumMovementSpeed")] public float MaximumMovementSpeed { get; init; } = 12f;
    [JsonPropertyName("stuckTimeoutSeconds")] public float StuckTimeoutSeconds { get; init; } = 5f;
    [JsonPropertyName("followGain")] public float FollowGain { get; init; } = 2.5f;
}

// ── Adaptive NPC Session ─────────────────────────────────────────────────────

public enum AdaptiveNpcMode
{
    Idle,
    Follow,
    Chase,
    Attack,
    KeepDistance,
    Evade,
    Flank,
    Counter,
    Retreat,
    HoldPosition,
    Patrol,
    OffsetFollow
}

public sealed class AdaptiveNpcSession
{
    [JsonPropertyName("npcId")] public string NpcId { get; init; } = "";
    [JsonPropertyName("sessionId")] public string SessionId { get; init; } = "";
    public Entity NpcEntity { get; set; }
    public Entity ObservedPlayerEntity { get; set; }
    [JsonPropertyName("observedPlayerSteamId")] public ulong ObservedPlayerSteamId { get; init; }
    [JsonPropertyName("currentMode")] public AdaptiveNpcMode CurrentMode { get; set; } = AdaptiveNpcMode.Idle;
    [JsonPropertyName("desiredOffset")] public float3 DesiredOffset { get; set; }
    [JsonPropertyName("followConfig")] public OffsetFollowConfig FollowConfig { get; set; } = new();
    [JsonPropertyName("homePosition")] public float3 HomePosition { get; set; }
    [JsonPropertyName("leashRange")] public float LeashRange { get; set; } = 80f;
    [JsonPropertyName("preferredCombatDistance")] public float PreferredCombatDistance { get; set; } = 8f;
    [JsonPropertyName("createdAtUtc")] public DateTime CreatedAtUtc { get; init; } = DateTime.UtcNow;
    [JsonPropertyName("lastPathUpdateTime")] public float LastPathUpdateTime { get; set; }
    [JsonPropertyName("stuckTimer")] public float StuckTimer { get; set; }
    [JsonPropertyName("lastPosition")] public float3 LastPosition { get; set; }
    [JsonPropertyName("isStuck")] public bool IsStuck { get; set; }
}

// ── Player Position Sample (for delayed path following) ──────────────────────

public readonly record struct PlayerPositionSample(
    float Time,
    float3 Position,
    quaternion Rotation);

// ── Adaptive Scaling Configuration ───────────────────────────────────────────

public sealed class AdaptiveScalingConfig
{
    [JsonPropertyName("enabled")] public bool Enabled { get; set; } = true;
    [JsonPropertyName("minimumDifficultyMultiplier")] public float MinimumDifficultyMultiplier { get; set; } = 0.75f;
    [JsonPropertyName("maximumDifficultyMultiplier")] public float MaximumDifficultyMultiplier { get; set; } = 1.5f;
    [JsonPropertyName("maximumNpcCount")] public int MaximumNpcCount { get; set; } = 20;
    [JsonPropertyName("allowElitePromotion")] public bool AllowElitePromotion { get; set; } = true;
    [JsonPropertyName("allowBossPromotion")] public bool AllowBossPromotion { get; set; }
}

// ── Event Adaptive Configuration (top-level event config) ────────────────────

public sealed class EventAdaptiveConfig
{
    [JsonPropertyName("enabled")] public bool Enabled { get; set; } = true;
    [JsonPropertyName("maximumNpcCount")] public int MaximumNpcCount { get; set; } = 16;
    [JsonPropertyName("baseThreat")] public float BaseThreat { get; set; } = 20f;
    [JsonPropertyName("npcs")] public List<NpcCatalogEntry> Npcs { get; set; } = new();
    [JsonPropertyName("waves")] public List<WaveSchematic> Waves { get; set; } = new();
    [JsonPropertyName("rewards")] public EventRewardConfig Rewards { get; set; } = new();
    [JsonPropertyName("adaptiveScaling")] public AdaptiveScalingConfig AdaptiveScaling { get; set; } = new();
    [JsonPropertyName("drills")] public List<CombatDrillDefinition> Drills { get; set; } = new();
}

public sealed class EventRewardConfig
{
    [JsonPropertyName("enabled")] public bool Enabled { get; set; } = true;
    [JsonPropertyName("maximumItemsPerPlayer")] public int MaximumItemsPerPlayer { get; set; } = 3;
    [JsonPropertyName("maximumTotalItemValue")] public float MaximumTotalItemValue { get; set; } = 100f;
    [JsonPropertyName("allowedRewardProfiles")] public List<string> AllowedRewardProfiles { get; set; } = new();
    [JsonPropertyName("blockedItemPrefabs")] public List<int> BlockedItemPrefabs { get; set; } = new();
    [JsonPropertyName("allowNpcNativeDrops")] public bool AllowNpcNativeDrops { get; set; }
}

// ── Adaptive Event Catalog (root JSON structure) ─────────────────────────────

public sealed class AdaptiveEventCatalogRoot
{
    [JsonPropertyName("version")] public int Version { get; set; } = 1;
    [JsonPropertyName("events")] public Dictionary<string, EventAdaptiveConfig> Events { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    [JsonPropertyName("npcDefinitions")] public List<NpcCatalogEntry> NpcDefinitions { get; set; } = new();
    [JsonPropertyName("rewardProfiles")] public List<RewardProfile> RewardProfiles { get; set; } = new();
}

// ── Wave Performance Tracking (for dynamic adaptation) ───────────────────────

public sealed class WavePerformanceData
{
    [JsonPropertyName("waveIndex")] public int WaveIndex { get; init; }
    [JsonPropertyName("clearTimeSeconds")] public float ClearTimeSeconds { get; set; }
    [JsonPropertyName("averagePlayerHealthRemaining")] public float AveragePlayerHealthRemaining { get; set; } = 1f;
    [JsonPropertyName("playerDeaths")] public int PlayerDeaths { get; set; }
    [JsonPropertyName("playerEliminations")] public int PlayerEliminations { get; set; }
    [JsonPropertyName("damageDealtToNpcs")] public float DamageDealtToNpcs { get; set; }
    [JsonPropertyName("damageTakenByPlayers")] public float DamageTakenByPlayers { get; set; }
    [JsonPropertyName("npcCountAlive")] public int NpcCountAlive { get; set; }
    [JsonPropertyName("playersRetreated")] public int PlayersRetreated { get; set; }
    [JsonPropertyName("rangedDamageRatio")] public float RangedDamageRatio { get; set; }
    [JsonPropertyName("meleeDamageRatio")] public float MeleeDamageRatio { get; set; }
    [JsonPropertyName("carryPlayerSteamId")] public ulong CarryPlayerSteamId { get; set; }
    [JsonPropertyName("carryPlayerDamageShare")] public float CarryPlayerDamageShare { get; set; }
}

// ── Adaptive Difficulty Adjustment ───────────────────────────────────────────

public sealed class DifficultyAdjustment
{
    [JsonPropertyName("difficultyMultiplier")] public float DifficultyMultiplier { get; set; } = 1f;
    [JsonPropertyName("npcCountAdjustment")] public int NpcCountAdjustment { get; set; }
    [JsonPropertyName("promoteToElite")] public bool PromoteToElite { get; set; }
    [JsonPropertyName("promoteToBoss")] public bool PromoteToBoss { get; set; }
    [JsonPropertyName("healthMultiplier")] public float HealthMultiplier { get; set; } = 1f;
    [JsonPropertyName("damageMultiplier")] public float DamageMultiplier { get; set; } = 1f;
    [JsonPropertyName("spawnDelayAdjustment")] public float SpawnDelayAdjustment { get; set; }
}