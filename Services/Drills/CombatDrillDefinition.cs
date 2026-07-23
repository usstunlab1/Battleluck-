using System.Text.Json.Serialization;

namespace BattleLuck.Services.Drills;

/// <summary>
/// Defines a combat drill — a structured NPC behavior pattern that reacts to
/// specific player actions. Drills are referenced by wave schematics and event
/// configurations to create practice scenarios.
/// </summary>
public sealed class CombatDrillDefinition
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("displayName")] public string DisplayName { get; set; } = "";
    [JsonPropertyName("description")] public string Description { get; set; } = "";
    [JsonPropertyName("triggerPattern")] public string TriggerPattern { get; set; } = "";
    [JsonPropertyName("defaultReaction")] public string DefaultReaction { get; set; } = "counter";
    [JsonPropertyName("priority")] public int Priority { get; set; }
    [JsonPropertyName("rules")] public List<DrillReactionRule> Rules { get; set; } = new();
    [JsonPropertyName("enabled")] public bool Enabled { get; set; } = true;
}

/// <summary>
/// A single reaction rule within a combat drill — maps a player action pattern
/// to an NPC reaction behavior.
/// </summary>
public sealed class DrillReactionRule
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("trigger")] public string Trigger { get; set; } = "";
    [JsonPropertyName("reactionMode")] public string ReactionMode { get; set; } = "evade";
    [JsonPropertyName("cooldownSeconds")] public float CooldownSeconds { get; set; } = 3f;
    [JsonPropertyName("durationSeconds")] public float DurationSeconds { get; set; } = 2f;
    [JsonPropertyName("priority")] public int Priority { get; set; }
    [JsonPropertyName("conditional")] public DrillConditional? Conditional { get; set; }
}

/// <summary>
/// Conditional requirements for a drill reaction rule to activate.
/// </summary>
public sealed class DrillConditional
{
    [JsonPropertyName("playerDistanceMin")] public float PlayerDistanceMin { get; set; }
    [JsonPropertyName("playerDistanceMax")] public float PlayerDistanceMax { get; set; } = 50f;
    [JsonPropertyName("playerHealthBelow")] public float PlayerHealthBelow { get; set; } = 1f;
    [JsonPropertyName("playerHealthAbove")] public float PlayerHealthAbove { get; set; }
    [JsonPropertyName("npcHealthBelow")] public float NpcHealthBelow { get; set; } = 1f;
    [JsonPropertyName("npcHealthAbove")] public float NpcHealthAbove { get; set; }
    [JsonPropertyName("weaponCategory")] public string WeaponCategory { get; set; } = "";
    [JsonPropertyName("playerIsCasting")] public bool? PlayerIsCasting { get; set; }
    [JsonPropertyName("playerIsDashing")] public bool? PlayerIsDashing { get; set; }
}