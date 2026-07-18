/// <summary>Config for the 5x5 moving battle platform (Bloodbath only).</summary>
public sealed class MovingPlatformConfig
{
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; }

    [JsonPropertyName("tilePrefab")]
    public string TilePrefab { get; set; } = "TM_Castle_Floor_Tier02_Stone";

    [JsonPropertyName("gridSize")]
    public int GridSize { get; set; } = 5;

    [JsonPropertyName("tileSpacing")]
    public float TileSpacing { get; set; } = 2.5f;

    [JsonPropertyName("heightOffset")]
    public float HeightOffset { get; set; } = 0.1f;

    [JsonPropertyName("glowEdge")]
    public bool GlowEdge { get; set; } = true;

    [JsonPropertyName("totalTiles")]
    public int TotalTiles { get; set; } = 25;
}

/// <summary>Tracks a single spawned platform tile.</summary>
public sealed class PlatformTileInfo
{
    public Unity.Entities.Entity Entity { get; set; }
    public Unity.Mathematics.float3 LocalOffset { get; set; }
    public bool IsEdge { get; set; }
}
