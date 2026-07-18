public sealed class WebhookConfig
{
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; }

    [JsonPropertyName("bindAddress")]
    public string BindAddress { get; set; } = "+";

    [JsonPropertyName("port")]
    public int Port { get; set; } = 25580;

    [JsonPropertyName("secret")]
    public string Secret { get; set; } = "";

    [JsonPropertyName("user")]
    public string User { get; set; } = "";

    [JsonPropertyName("password")]
    public string Password { get; set; } = "";

    [JsonPropertyName("ipWhitelist")]
    public List<string> IpWhitelist { get; set; } = new() { "127.0.0.1" };

    [JsonPropertyName("rateLimitPerSecond")]
    public int RateLimitPerSecond { get; set; } = 10;

    [JsonPropertyName("discord_webhook_url")]
    public string DiscordWebhookUrl { get; set; } = "";

}

public sealed class WebhookRequest
{
    [JsonPropertyName("action")]
    public string Action { get; set; } = "";

    [JsonPropertyName("data")]
    public Dictionary<string, object>? Data { get; set; }
}

public sealed class WebhookResponse
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("message")]
    public string? Message { get; set; }

    [JsonPropertyName("data")]
    public object? Data { get; set; }
}
