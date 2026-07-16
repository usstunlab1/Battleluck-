namespace BattleLuck.Models;

/// <summary>
/// Compact server-only event console settings. Rendering uses native server
/// system messages, so no client mod or custom wire protocol is required.
/// </summary>
public sealed class EventConsoleSettings
{
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = true;

    [JsonPropertyName("autoShow")]
    public bool AutoShow { get; set; } = true;

    [JsonPropertyName("refreshSeconds")]
    public float RefreshSeconds { get; set; } = 15f;

    [JsonPropertyName("topPlayers")]
    public int TopPlayers { get; set; } = 4;

    [JsonPropertyName("recentActions")]
    public int RecentActions { get; set; } = 3;

    [JsonPropertyName("maxNameLength")]
    public int MaxNameLength { get; set; } = 12;
}
