// Models/ManifestConfig.cs
// Canonical mode-manifest schema.
//
// Canonical keys:  modeId, displayName

namespace BattleLuck.Models;

/// <summary>
/// Minimal mode manifest.  Loaded from manifest.json in each mode folder.
/// </summary>
public sealed class ManifestConfig
{
    [JsonPropertyName("modeId")]
    public string? ModeId { get; set; }

    [JsonPropertyName("displayName")]
    public string? DisplayName { get; set; }

    [JsonPropertyName("version")]
    public int Version { get; set; } = 1;

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("author")]
    public string? Author { get; set; }

    [JsonPropertyName("minPlayers")]
    public int MinPlayers { get; set; } = 2;

    [JsonPropertyName("maxPlayers")]
    public int MaxPlayers { get; set; } = 32;

    [JsonPropertyName("tags")]
    public List<string> Tags { get; set; } = new();

    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = true;

    public bool IsValid() =>
        !string.IsNullOrWhiteSpace(ModeId) && !string.IsNullOrWhiteSpace(DisplayName);

    public static JsonSerializerOptions DefaultOptions => _options;

    public string Serialize() => JsonSerializer.Serialize(this, _options);

    private static readonly JsonSerializerOptions _options = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };
}
