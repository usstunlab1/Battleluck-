using System.Text.Json.Serialization;

/// <summary>Config for the PvP transformation special item.</summary>
public sealed class SpecialItemConfig
{
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; }

    [JsonPropertyName("itemName")]
    public string ItemName { get; set; } = "PvP Transformation Token";

    [JsonPropertyName("prefab")]
    public string Prefab { get; set; } = "Item_Consumable_PvPToken";

    [JsonPropertyName("adminOnlyDeletable")]
    public bool AdminOnlyDeletable { get; set; } = true;

    [JsonPropertyName("onAcquire")]
    public OnAcquireConfig OnAcquire { get; set; } = new();
}

public sealed class OnAcquireConfig
{
    [JsonPropertyName("renamePrefix")]
    public string RenamePrefix { get; set; } = "[pvp]";

    [JsonPropertyName("changeBloodType")]
    public string ChangeBloodType { get; set; } = "Warrior";

    [JsonPropertyName("changeBloodQuality")]
    public float ChangeBloodQuality { get; set; } = 100f;

    [JsonPropertyName("applyKit")]
    public bool ApplyKit { get; set; } = true;

    [JsonPropertyName("kitMode")]
    public string KitMode { get; set; } = "bloodbath";
}
