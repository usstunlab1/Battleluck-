using System.Text.Json.Serialization;
using Stunlock.Core;

namespace BattleLuck.Models;

public sealed class AdaptiveDrillCatalog
{
    [JsonPropertyName("version")] public int Version { get; set; } = 1;
    [JsonPropertyName("events")] public Dictionary<string, AdaptiveEventCatalog> Events { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class AdaptiveEventCatalog
{
    [JsonPropertyName("enabled")] public bool Enabled { get; set; } = true;
    [JsonPropertyName("maximumNpcCount")] public int MaximumNpcCount { get; set; } = 8;
    [JsonPropertyName("baseThreat")] public float BaseThreat { get; set; } = 12;
    [JsonPropertyName("npcs")] public List<AdaptiveNpcCatalogEntry> Npcs { get; set; } = new();
    [JsonPropertyName("drills")] public List<CombatDrillDefinition> Drills { get; set; } = new();
}

public sealed class AdaptiveNpcCatalogEntry
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("prefab")] public string Prefab { get; set; } = "";
    [JsonPropertyName("threatCost")] public float ThreatCost { get; set; } = 5;
    [JsonPropertyName("minimumStrength")] public float MinimumStrength { get; set; }
    [JsonPropertyName("maximumStrength")] public float MaximumStrength { get; set; } = 999;
    [JsonPropertyName("behavior")] public string Behavior { get; set; } = "attack";
}

public sealed class CombatDrillDefinition
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("reaction")] public string Reaction { get; set; } = "counter";
}

public sealed class PlayerCombatProfile
{
    [JsonPropertyName("steamId")] public ulong SteamId { get; init; }
    [JsonPropertyName("level")] public int Level { get; init; }
    [JsonPropertyName("healthRatio")] public float HealthRatio { get; init; }
    [JsonPropertyName("combatStrength")] public float CombatStrength { get; init; }

    public PlayerCombatProfile(ulong steamId, int level, float healthRatio, float combatStrength)
    {
        SteamId = steamId;
        Level = level;
        HealthRatio = healthRatio;
        CombatStrength = combatStrength;
    }
}

public sealed class EventParticipantProfile
{
    [JsonPropertyName("players")] public IReadOnlyList<PlayerCombatProfile> Players { get; init; } = Array.Empty<PlayerCombatProfile>();
    [JsonPropertyName("averageStrength")] public float AverageStrength { get; init; }
    public int PlayerCount => Players.Count;
    public float AverageCombatStrength => AverageStrength;
    public float PeakCombatStrength => Players.Count > 0 ? Players.Max(p => p.CombatStrength) : 0;

    public EventParticipantProfile(IReadOnlyList<PlayerCombatProfile> players, float averageStrength)
    {
        Players = players;
        AverageStrength = averageStrength;
    }
}

public sealed class SpawnNpcPlan
{
    [JsonPropertyName("catalogId")] public string CatalogId { get; init; } = "";
    [JsonPropertyName("prefab")] public string Prefab { get; init; } = "";
    [JsonPropertyName("count")] public int Count { get; init; }
    [JsonPropertyName("behavior")] public string Behavior { get; init; } = "";
    public PrefabGUID PrefabGuid { get; set; }
    public string BehaviorProfileId { get; set; } = "";
    public string SpawnZoneId { get; set; } = "";
    public float HealthScale { get; set; } = 1f;
    public float DamageScale { get; set; } = 1f;

    public SpawnNpcPlan(string catalogId, string prefab, int count, string behavior)
    {
        CatalogId = catalogId;
        Prefab = prefab;
        Count = count;
        Behavior = behavior;
    }
}

public sealed class AdaptiveSpawnPlan
{
    [JsonPropertyName("eventId")] public string EventId { get; init; } = "";
    [JsonPropertyName("threatBudget")] public float ThreatBudget { get; init; }
    [JsonPropertyName("npcs")] public IReadOnlyList<SpawnNpcPlan> Npcs { get; init; } = Array.Empty<SpawnNpcPlan>();
    public IReadOnlyList<SpawnWavePlan> Waves { get; set; } = Array.Empty<SpawnWavePlan>();
    public RewardBudget? RewardBudget { get; set; }
    public SpawnSafetyLimits? SafetyLimits { get; set; }
    public float CalculatedDifficulty { get; set; } = 1f;

    public AdaptiveSpawnPlan(string eventId, float threatBudget, IReadOnlyList<SpawnNpcPlan> npcs)
    {
        EventId = eventId;
        ThreatBudget = threatBudget;
        Npcs = npcs;
    }
}