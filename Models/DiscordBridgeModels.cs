using System.Text.Json.Serialization;

public sealed class DiscordBridgeConfig
{
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; }

    [JsonPropertyName("bindAddress")]
    public string BindAddress { get; set; } = "+";

    [JsonPropertyName("port")]
    public int Port { get; set; } = 25581;

    [JsonPropertyName("applicationId")]
    public string ApplicationId { get; set; } = "";

    [JsonPropertyName("publicKey")]
    public string PublicKey { get; set; } = "";

    [JsonPropertyName("token")]
    public string Token { get; set; } = "";

    [JsonPropertyName("guildId")]
    public string GuildId { get; set; } = "";

    [JsonPropertyName("categoryId")]
    public string CategoryId { get; set; } = "";

    [JsonPropertyName("channels")]
    public DiscordChannels Channels { get; set; } = new();

    [JsonPropertyName("commands")]
    public List<DiscordCommandConfig> Commands { get; set; } = new();

    [JsonPropertyName("playerMappings")]
    public List<PlayerMapping> PlayerMappings { get; set; } = new();
}

public sealed class DiscordChannels
{
    [JsonPropertyName("logs")]
    public string Logs { get; set; } = "";

    [JsonPropertyName("chatvip")]
    public string ChatVip { get; set; } = "";

    [JsonPropertyName("commands")]
    public string Commands { get; set; } = "";

    [JsonPropertyName("cmd")]
    public string Cmd { get; set; } = "";
}

public sealed class DiscordCommandConfig
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("description")]
    public string Description { get; set; } = "";

    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = true;
}

public sealed class PlayerMapping
{
    [JsonPropertyName("discordId")]
    public string DiscordId { get; set; } = "";

    [JsonPropertyName("steamId")]
    public ulong SteamId { get; set; }
}

// Discord API interaction models

public sealed class DiscordInteraction
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("type")]
    public int Type { get; set; }

    [JsonPropertyName("token")]
    public string Token { get; set; } = "";

    [JsonPropertyName("data")]
    public DiscordInteractionData? Data { get; set; }

    [JsonPropertyName("member")]
    public DiscordMember? Member { get; set; }
}

public sealed class DiscordInteractionData
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("options")]
    public List<DiscordOption>? Options { get; set; }
}

public sealed class DiscordOption
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("value")]
    public object? Value { get; set; }
}

public sealed class DiscordMember
{
    [JsonPropertyName("user")]
    public DiscordUser? User { get; set; }
}

public sealed class DiscordUser
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";
}

public sealed class DiscordInteractionResponse
{
    [JsonPropertyName("type")]
    public int Type { get; set; }

    [JsonPropertyName("data")]
    public DiscordResponseData? Data { get; set; }
}

public sealed class DiscordResponseData
{
    [JsonPropertyName("content")]
    public string? Content { get; set; }

    [JsonPropertyName("embeds")]
    public List<DiscordEmbed>? Embeds { get; set; }

    [JsonPropertyName("flags")]
    public int? Flags { get; set; }
}
