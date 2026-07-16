// ── session.json model ──────────────────────────────────────────────────
// Extracted from Core/ConfigLoader.cs (Stage A). Declared in the global
// namespace to match the rest of the config model graph. The remaining
// helper types it depends on (DelayConfig) continue to live alongside the
// other config models.

/// <summary>Top-level model for a mode's session.json.</summary>
public sealed class SessionConfig
{
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = true;

    [JsonPropertyName("displayName")]
    public string DisplayName { get; set; } = "";

    [JsonPropertyName("description")]
    public string Description { get; set; } = "";

    [JsonPropertyName("useEcs")]
    public bool UseEcs { get; set; } = true;

    [JsonPropertyName("startDelay")]
    public DelayConfig StartDelay { get; set; } = new();

    [JsonPropertyName("rules")]
    public SessionRules Rules { get; set; } = new();

    [JsonPropertyName("flow")]
    public SessionFlowConfig Flow { get; set; } = new();
}

public sealed class SessionFlowConfig
{
    [JsonPropertyName("enter")]
    public FlowConfig Enter { get; set; } = new();

    [JsonPropertyName("start")]
    public FlowConfig Start { get; set; } = new();

    [JsonPropertyName("tracking")]
    public FlowConfig Tracking { get; set; } = new();

    [JsonPropertyName("winner")]
    public FlowConfig Winner { get; set; } = new();

    [JsonPropertyName("ending")]
    public FlowConfig Ending { get; set; } = new();

    [JsonPropertyName("exit")]
    public FlowConfig Exit { get; set; } = new();
}

public sealed class SessionRules
{
    [JsonPropertyName("minPlayers")]
    public int MinPlayers { get; set; } = 1;

    [JsonPropertyName("adminTestMinPlayers")]
    public int AdminTestMinPlayers { get; set; } = 1;

    [JsonPropertyName("allowAdminSoloTest")]
    public bool AllowAdminSoloTest { get; set; }

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

    [JsonPropertyName("requireReadyCheck")]
    public bool RequireReadyCheck { get; set; }

    [JsonPropertyName("restrictGear")]
    public bool RestrictGear { get; set; }

    [JsonPropertyName("shareLoot")]
    public bool ShareLoot { get; set; }

    [JsonPropertyName("resetOnExit")]
    public bool ResetOnExit { get; set; } = true;

    [JsonPropertyName("eliminationMode")]
    public bool EliminationMode { get; set; }

    /// <summary>Immediate in-arena respawns allowed before the next death eliminates the player.</summary>
    [JsonPropertyName("livesPerPlayer")]
    public int LivesPerPlayer { get; set; } = 3;

    [JsonPropertyName("zoneEnterRule")]
    public string ZoneEnterRule { get; set; } = "auto_enter";

    [JsonPropertyName("actionStaging")]
    public ActionStagingRules ActionStaging { get; set; } = new();

    [JsonPropertyName("eventConsole")]
    public EventConsoleSettings EventConsole { get; set; } = new();

    [JsonPropertyName("techIds")]
    public List<string> TechIds { get; set; } = new();
}
