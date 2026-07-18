namespace BattleLuck.Models;

public sealed class AiGroupProjectMSnapshot
{
    [JsonPropertyName("captured_utc")]
    public DateTime CapturedUtc { get; set; } = DateTime.UtcNow;

    [JsonPropertyName("source_system")]
    public string SourceSystem { get; set; } = "";

    [JsonPropertyName("active_session")]
    public string ActiveSession { get; set; } = "none";

    [JsonPropertyName("aggro_consumer_count")]
    public int AggroConsumerCount { get; set; }

    [JsonPropertyName("online_player_count")]
    public int OnlinePlayerCount { get; set; }

    [JsonPropertyName("units")]
    public List<AiGroupUnitSnapshot> Units { get; set; } = new();

    [JsonPropertyName("players")]
    public List<AiGroupPlayerSnapshot> Players { get; set; } = new();
}

public sealed class AiGroupUnitSnapshot
{
    [JsonPropertyName("entity")]
    public string Entity { get; set; } = "";

    [JsonPropertyName("prefab")]
    public string Prefab { get; set; } = "";

    [JsonPropertyName("position")]
    public string Position { get; set; } = "";

    [JsonPropertyName("level")]
    public int Level { get; set; }

    [JsonPropertyName("health")]
    public string Health { get; set; } = "";

    [JsonPropertyName("aggro_target")]
    public string AggroTarget { get; set; } = "none";

    [JsonPropertyName("aggro_reason")]
    public string AggroReason { get; set; } = "";
}

public sealed class AiGroupPlayerSnapshot
{
    [JsonPropertyName("steam_id")]
    public ulong SteamId { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("entity")]
    public string Entity { get; set; } = "";

    [JsonPropertyName("position")]
    public string Position { get; set; } = "";

    [JsonPropertyName("health")]
    public string Health { get; set; } = "";
}

public sealed class AiGroupProjectMDirective
{
    [JsonPropertyName("directive")]
    public string Directive { get; set; } = "observe";

    [JsonPropertyName("reason")]
    public string Reason { get; set; } = "";

    [JsonPropertyName("action")]
    public string Action { get; set; } = "";

    [JsonPropertyName("confidence")]
    public float Confidence { get; set; }

    [JsonPropertyName("cooldown_seconds")]
    public int CooldownSeconds { get; set; } = 15;

    [JsonPropertyName("target")]
    public string Target { get; set; } = "";
}

public sealed class AiProjectOrderResult
{
    [JsonPropertyName("project_id")]
    public string ProjectId { get; set; } = "";

    [JsonPropertyName("summary")]
    public string Summary { get; set; } = "";

    [JsonPropertyName("recommended_actions")]
    public List<string> RecommendedActions { get; set; } = new();

    [JsonPropertyName("risk")]
    public string Risk { get; set; } = "low";
}

public sealed class AiActionModernizationReview
{
    [JsonPropertyName("summary")]
    public string Summary { get; set; } = "";

    [JsonPropertyName("canonical_actions")]
    public List<string> CanonicalActions { get; set; } = new();

    [JsonPropertyName("legacy_actions")]
    public List<string> LegacyActions { get; set; } = new();

    [JsonPropertyName("llm_recommendations")]
    public List<string> LlmRecommendations { get; set; } = new();

    [JsonPropertyName("config_policy_suggestions")]
    public List<string> ConfigPolicySuggestions { get; set; } = new();
}
