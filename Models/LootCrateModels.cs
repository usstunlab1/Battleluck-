/// <summary>Config for loot crate spawning on the battle platform.</summary>
public sealed class LootCrateConfig
{
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; }

    [JsonPropertyName("spawnIntervalSec")]
    public float SpawnIntervalSec { get; set; } = 15f;

    [JsonPropertyName("maxActiveCrates")]
    public int MaxActiveCrates { get; set; } = 3;

    [JsonPropertyName("despawnAfterSec")]
    public float DespawnAfterSec { get; set; } = 10f;

    [JsonPropertyName("spawnAtCenter")]
    public bool SpawnAtCenter { get; set; } = true;

    [JsonPropertyName("lockedUntilKills")]
    public int LockedUntilKills { get; set; } = 3;

    [JsonPropertyName("winnerOnly")]
    public bool WinnerOnly { get; set; } = true;

    [JsonPropertyName("containerPrefab")]
    public string ContainerPrefab { get; set; } = "Chain_Container_WorldChest_Iron_01";

    [JsonPropertyName("crateTypes")]
    public List<CrateTypeConfig> CrateTypes { get; set; } = new();
}

public sealed class CrateTypeConfig
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "";

    [JsonPropertyName("weight")]
    public int Weight { get; set; }

    [JsonPropertyName("prefab")]
    public string Prefab { get; set; } = "";

    [JsonPropertyName("amount")]
    public int Amount { get; set; } = 1;
}

public sealed class CrateInstance
{
    public Entity Entity { get; set; }
    public Entity GlowEntity { get; set; }
    public CrateTypeConfig Type { get; set; } = new();
    public DateTime SpawnedAt { get; set; }
    public float3 Position { get; set; }
}
