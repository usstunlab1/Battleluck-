using System.Text.Json.Serialization;

/// <summary>Boss spawning configuration per mode.</summary>
public sealed class BossesConfig
{
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; }

    [JsonPropertyName("spawnTrigger")]
    public string SpawnTrigger { get; set; } = "wave_final";

    [JsonPropertyName("timedSpawnAfterSeconds")]
    public int TimedSpawnAfterSeconds { get; set; } = 180;

    [JsonPropertyName("bosses")]
    public List<BossDefinition> Bosses { get; set; } = new();
}

public sealed class BossDefinition
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("prefab")]
    public string Prefab { get; set; } = "";

    [JsonPropertyName("spawnOffset")]
    public Vec3Config SpawnOffset { get; set; } = new();

    [JsonPropertyName("wave")]
    public int Wave { get; set; }

    [JsonPropertyName("glow")]
    public bool Glow { get; set; }

    [JsonPropertyName("level")]
    public int Level { get; set; }
}

