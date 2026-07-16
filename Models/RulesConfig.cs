// ── rules.json model ────────────────────────────────────────────────────
// New additive model introduced during Stage A scaffolding for the planned
// per-mode rules.json file. Distinct from SessionConfig.Rules (SessionRules),
// which models the "rules" block embedded inside session.json. Not yet
// referenced by ModeConfig or the loaders; later stages will wire it in.
// Declared in the global namespace to match the rest of the config model graph.

/// <summary>
/// Per-mode rules.json model. Mirrors the gameplay rule fields the AI
/// orchestrator is allowed to generate, kept separate from the embedded
/// session.json rules block so the two can diverge over time.
/// </summary>
public sealed class RulesConfig
{
    [JsonPropertyName("modeId")]
    public string ModeId { get; set; } = "";

    [JsonPropertyName("displayName")]
    public string DisplayName { get; set; } = "";

    [JsonPropertyName("minPlayers")]
    public int MinPlayers { get; set; } = 1;

    [JsonPropertyName("maxPlayers")]
    public int MaxPlayers { get; set; } = 4;

    [JsonPropertyName("enablePvP")]
    public bool EnablePvP { get; set; }

    [JsonPropertyName("enableVBloods")]
    public bool EnableVBloods { get; set; }

    [JsonPropertyName("enableEliteMobs")]
    public bool EnableEliteMobs { get; set; }

    [JsonPropertyName("matchDurationMinutes")]
    public int MatchDurationMinutes { get; set; } = 10;

    [JsonPropertyName("allowLateJoin")]
    public bool AllowLateJoin { get; set; }

    [JsonPropertyName("eliminationMode")]
    public bool EliminationMode { get; set; }

    /// <summary>Maximum deaths allowed before the next death eliminates the player.</summary>
    [JsonPropertyName("maxDeathsPerParticipant")]
    public int MaxDeathsPerParticipant { get; set; } = 3;

    [JsonIgnore]
    public int LivesPerPlayer { get => MaxDeathsPerParticipant; set => MaxDeathsPerParticipant = value; }

    [JsonPropertyName("chestKillsToUnlock")]
    public int ChestKillsToUnlock { get; set; } = 3;

    [JsonPropertyName("rewardGoldPerChest")]
    public int RewardGoldPerChest { get; set; } = 100;

    [JsonPropertyName("spawnRateLimitPerSecond")]
    public int SpawnRateLimitPerSecond { get; set; } = 10;

    [JsonPropertyName("safetyMode")]
    public string SafetyMode { get; set; } = "";

    /// <summary>
    /// Zone entry policy for this mode (e.g. "auto_enter", "ready_check", "manual_only").
    /// </summary>
    [JsonPropertyName("zoneEnterRule")]
    public string ZoneEnterRule { get; set; } = "auto_enter";

    /// <summary>
    /// Action staging behavior for zone entry and start gates.
    /// </summary>
    [JsonPropertyName("actionStaging")]
    public ActionStagingRules ActionStaging { get; set; } = new();

    [JsonPropertyName("eventConsole")]
    public EventConsoleSettings EventConsole { get; set; } = new();

    [JsonPropertyName("techIds")]
    public List<string> TechIds { get; set; } = new();

    // Additional properties referenced by EventDefinitionLoader
    [JsonPropertyName("adminTestMinPlayers")]
    public int? AdminTestMinPlayers { get; set; }

    [JsonPropertyName("allowAdminSoloTest")]
    public bool? AllowAdminSoloTest { get; set; }

    [JsonPropertyName("requireReadyCheck")]
    public bool? RequireReadyCheck { get; set; }

    [JsonPropertyName("restrictGear")]
    public bool? RestrictGear { get; set; }

    [JsonPropertyName("shareLoot")]
    public bool? ShareLoot { get; set; }

    [JsonPropertyName("resetOnExit")]
    public bool? ResetOnExit { get; set; }
}

/// <summary>
/// Controls whether actions are queued/staged before match start and when to release them.
/// </summary>
public sealed class ActionStagingRules
{
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = true;

    [JsonPropertyName("stageOnZoneEnter")]
    public bool StageOnZoneEnter { get; set; } = true;

    [JsonPropertyName("releaseOnMatchStart")]
    public bool ReleaseOnMatchStart { get; set; } = true;
}
