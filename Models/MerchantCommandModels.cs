using System.Text.Json.Serialization;

namespace BattleLuck.Models;

public sealed class MerchantServantActionConfig
{
    [JsonPropertyName("merchants")]
    public MerchantCommandConfig Merchants { get; set; } = new();

    [JsonPropertyName("servants")]
    public List<NumberedServantAction> Servants { get; set; } = new();
}

public sealed class NumberedServantAction
{
    [JsonPropertyName("number")]
    public int Number { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("action")]
    public string Action { get; set; } = "";

    [JsonPropertyName("prefab")]
    public string Prefab { get; set; } = "";

    [JsonPropertyName("prefabGuid")]
    public int? PrefabGuid { get; set; }
}

public sealed class MerchantCommandConfig
{
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = true;

    [JsonPropertyName("scanIntervalSeconds")]
    public float ScanIntervalSeconds { get; set; } = 2f;

    [JsonPropertyName("consumeItem")]
    public bool ConsumeItem { get; set; } = true;

    [JsonPropertyName("defaultDurationSeconds")]
    public int DefaultDurationSeconds { get; set; } = 300;

    [JsonPropertyName("defaultCooldownSeconds")]
    public int DefaultCooldownSeconds { get; set; } = 10;

    [JsonPropertyName("listings")]
    public List<MerchantCommandListing> Listings { get; set; } = new();
}

public sealed class MerchantCommandListing
{
    [JsonPropertyName("number")]
    public int Number { get; set; }

    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = true;

    [JsonPropertyName("displayName")]
    public string DisplayName { get; set; } = "";

    [JsonPropertyName("kind")]
    public string Kind { get; set; } = "custom";

    [JsonPropertyName("itemPrefab")]
    public string ItemPrefab { get; set; } = "";

    [JsonPropertyName("itemGuid")]
    public int? ItemGuid { get; set; }

    [JsonPropertyName("adminOnly")]
    public bool AdminOnly { get; set; }

    [JsonPropertyName("consumeItem")]
    public bool? ConsumeItem { get; set; }

    [JsonPropertyName("cooldownSeconds")]
    public int? CooldownSeconds { get; set; }

    [JsonPropertyName("durationSeconds")]
    public int? DurationSeconds { get; set; }

    [JsonPropertyName("bossPrefab")]
    public string BossPrefab { get; set; } = "";

    [JsonPropertyName("bossId")]
    public string BossId { get; set; } = "";

    [JsonPropertyName("level")]
    public int Level { get; set; } = 80;

    [JsonPropertyName("homeRadius")]
    public float HomeRadius { get; set; } = 40f;

    [JsonPropertyName("followRange")]
    public float FollowRange { get; set; } = 6f;

    [JsonPropertyName("leashRange")]
    public float LeashRange { get; set; } = 80f;

    [JsonPropertyName("protectTeam")]
    public bool ProtectTeam { get; set; } = true;

    [JsonPropertyName("neutralUntilStart")]
    public bool NeutralUntilStart { get; set; }

    [JsonPropertyName("teleportPosition")]
    public Vec3Config TeleportPosition { get; set; } = new();

    [JsonPropertyName("actions")]
    public List<string> Actions { get; set; } = new();
}
