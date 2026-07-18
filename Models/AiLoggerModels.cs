public sealed class AiLoggerConfig
{
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = true;

    [JsonPropertyName("providers")]
    public ProviderConfig Providers { get; set; } = new();

    [JsonPropertyName("discord")]
    public DiscordConfig Discord { get; set; } = new();

    [JsonPropertyName("buffer")]
    public BufferConfig Buffer { get; set; } = new();

    [JsonPropertyName("prompts")]
    public PromptConfig Prompts { get; set; } = new();

    public bool HasAnyProvider => Providers.Azure.Enabled || Providers.Gemini.Enabled || Providers.SuperuserSidecar.Enabled;
}

public sealed class ProviderConfig
{
    [JsonPropertyName("azure")]
    public AzureConfig Azure { get; set; } = new();

    [JsonPropertyName("gemini")]
    public GeminiConfig Gemini { get; set; } = new();

    [JsonPropertyName("superuserSidecar")]
    public SuperuserSidecarConfig SuperuserSidecar { get; set; } = new();
}

public sealed class AzureConfig
{
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = false;

    [JsonPropertyName("endpoint")]
    public string Endpoint { get; set; } = "";

    [JsonPropertyName("deployment")]
    public string Deployment { get; set; } = "gpt-4o-mini";

    [JsonIgnore]
    public string ApiKey { get; set; } = "";
}

public sealed class GeminiConfig
{
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = false;

    [JsonPropertyName("model")]
    public string Model { get; set; } = "gemini-2.5-flash";

    [JsonIgnore]
    public string ApiKey { get; set; } = "";
}

public sealed class SuperuserSidecarConfig
{
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = false;

    [JsonPropertyName("url")]
    public string Url { get; set; } = "http://localhost:3000";

    [JsonIgnore]
    public string ApiKey { get; set; } = "";
}

public sealed class DiscordConfig
{
    [JsonPropertyName("webhookUrl")]
    public string WebhookUrl { get; set; } = "";

    [JsonPropertyName("serverId")]
    public string ServerId { get; set; } = "";

    [JsonPropertyName("channelId")]
    public string ChannelId { get; set; } = "";

    public bool IsConfigured => !string.IsNullOrWhiteSpace(WebhookUrl);
}

public sealed class BufferConfig
{
    [JsonPropertyName("flushIntervalSec")]
    public int FlushIntervalSec { get; set; } = 60;

    [JsonPropertyName("maxSize")]
    public int MaxSize { get; set; } = 100;
}

public sealed class PromptConfig
{
    [JsonPropertyName("system")]
    public string System { get; set; } = "You are a game event summarizer for a V Rising PvP arena mod called BattleLuck. Summarize the following game events into a brief, engaging narrative suitable for a Discord channel. Use 1-3 sentences.";
}

public sealed class GameEventEntry
{
    [JsonPropertyName("timestamp")]
    public DateTime Timestamp { get; set; }

    [JsonPropertyName("type")]
    public string Type { get; set; } = "";

    [JsonPropertyName("details")]
    public string Details { get; set; } = "";
}

public sealed class DiscordEmbed
{
    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("color")]
    public int Color { get; set; } = 0x5865F2;

    [JsonPropertyName("fields")]
    public List<EmbedField>? Fields { get; set; }

    [JsonPropertyName("timestamp")]
    public string? Timestamp { get; set; }
}

public sealed class EmbedField
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("value")]
    public string Value { get; set; } = "";

    [JsonPropertyName("inline")]
    public bool Inline { get; set; }
}

public sealed class DiscordWebhookPayload
{
    [JsonPropertyName("content")]
    public string? Content { get; set; }

    [JsonPropertyName("embeds")]
    public List<DiscordEmbed>? Embeds { get; set; }
}
