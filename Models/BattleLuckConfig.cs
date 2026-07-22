using System.Text.Json.Serialization;

namespace BattleLuck.Models;

public sealed class BattleLuckConfig
{
    [JsonPropertyName("schema")]
    public int Schema { get; set; } = 1;

    [JsonPropertyName("events")]
    public EventPlatformSettings Events { get; set; } = new();

    [JsonPropertyName("chat")]
    public ChatPlatformSettings Chat { get; set; } = new();

    [JsonPropertyName("results")]
    public ResultPlatformSettings Results { get; set; } = new();

    [JsonPropertyName("assistant")]
    public AssistantPlatformSettings Assistant { get; set; } = new();

    [JsonPropertyName("backtrace")]
    public BacktraceSettings Backtrace { get; set; } = new();
}

public sealed class EventPlatformSettings
{
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = true;

    [JsonPropertyName("enabled_ids")]
    public List<string> EnabledIds { get; set; } = new() { "bloodbath", "colosseum" };
}

public sealed class ChatPlatformSettings
{
    [JsonPropertyName("prefix")]
    public string Prefix { get; set; } = ".bl";

    [JsonPropertyName("killfeed_scope")]
    public string KillfeedScope { get; set; } = "event";

    [JsonPropertyName("zui_opt_in")]
    public bool ZuiOptIn { get; set; } = true;
}

public sealed class ResultPlatformSettings
{
    [JsonPropertyName("keep")]
    public int Keep { get; set; } = 20;

    [JsonPropertyName("season_id")]
    public string SeasonId { get; set; } = "default";
}

public sealed class AssistantPlatformSettings
{
    [JsonPropertyName("mode")]
    public string Mode { get; set; } = "auto";

    [JsonPropertyName("local_url")]
    public string LocalUrl { get; set; } = "http://127.0.0.1:11434";

    [JsonPropertyName("model")]
    public string Model { get; set; } = "qwen2.5:0.5b";
}
