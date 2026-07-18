namespace BattleLuck.Models
{
    public class PrefabArchive
    {
        [JsonPropertyName("schemaVersion")]
        public int SchemaVersion { get; set; }

        [JsonPropertyName("generatedAt")]
        public DateTime GeneratedAt { get; set; }

        [JsonPropertyName("prefabCount")]
        public int PrefabCount { get; set; }

        [JsonPropertyName("prefabs")]
        public Dictionary<string, PrefabArchiveEntry> Prefabs { get; set; } = new();
    }

    public class PrefabArchiveEntry
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("category")]
        public string Category { get; set; } = string.Empty;

        [JsonPropertyName("flags")]
        public Dictionary<string, bool> Flags { get; set; } = new();

        [JsonPropertyName("aabbMin")]
        public float[]? AabbMin { get; set; }

        [JsonPropertyName("aabbMax")]
        public float[]? AabbMax { get; set; }
    }
}
