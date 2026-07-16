using System.Text.Json.Serialization;

/// <summary>
/// Config models for dynamic waypoint movement and glow border scanning.
/// </summary>
public sealed class WaypointConfig
{
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; }

    [JsonPropertyName("points")]
    public List<Vec3Config> Points { get; set; } = new();

    [JsonPropertyName("moveIntervalSec")]
    public float MoveIntervalSec { get; set; } = 5f;

    [JsonPropertyName("moveSpeed")]
    public float MoveSpeed { get; set; } = 8f;

    [JsonPropertyName("loop")]
    public bool Loop { get; set; } = true;

    [JsonPropertyName("radiusShrinkPerWaypoint")]
    public float RadiusShrinkPerWaypoint { get; set; } = 3f;

    [JsonPropertyName("minimumRadius")]
    public float MinimumRadius { get; set; } = 15f;
}

public sealed class GlowBorderConfig
{
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; }

    [JsonPropertyName("scanWidth")]
    public float ScanWidth { get; set; } = 10f;

    [JsonPropertyName("maxGlowEntities")]
    public int MaxGlowEntities { get; set; } = 50;

    [JsonPropertyName("batchSize")]
    public int BatchSize { get; set; } = 10;

    /// <summary>Spawn fire glow entities on zone border. Prefab name.</summary>
    [JsonPropertyName("spawnGlowEntities")]
    public string? SpawnGlowEntities { get; set; }

    /// <summary>Number of glow entities to spawn around border</summary>
    [JsonPropertyName("glowEntityCount")]
    public int GlowEntityCount { get; set; } = 20;

    /// <summary>Distance from zone center to spawn glow entities (0 = use zone radius)</summary>
    [JsonPropertyName("glowRadius")]
    public float GlowRadius { get; set; } = 0f;

    /// <summary>Disable sun damage inside zone (applies Holy buff)</summary>
    [JsonPropertyName("disableSunEffects")]
    public bool DisableSunEffects { get; set; } = false;

    /// <summary>Make player friendly to NPCs (sets faction to friendly team)</summary>
    [JsonPropertyName("npcFriendly")]
    public bool NpcFriendly { get; set; } = false;
}
