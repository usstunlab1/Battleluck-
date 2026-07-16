using System.Text.Json.Serialization;

/// <summary>
/// Data models for kit.json — ArenaBuilds-style build schema.
/// Each mode has one kit.json defining the full loadout applied on zone entry.
/// </summary>

public sealed class KitConfig
{
    [JsonPropertyName("settings")]
    public KitSettings Settings { get; set; } = new();

    [JsonPropertyName("blood")]
    public BloodConfig? Blood { get; set; }

    [JsonPropertyName("armors")]
    public ArmorsConfig? Armors { get; set; }

    [JsonPropertyName("weapons")]
    public List<WeaponConfig> Weapons { get; set; } = new();

    [JsonPropertyName("items")]
    public List<ItemConfig> Items { get; set; } = new();

    [JsonPropertyName("abilities")]
    public AbilitiesKitConfig? Abilities { get; set; }

    [JsonPropertyName("passiveSpells")]
    public List<PassiveSpellConfig> PassiveSpells { get; set; } = new();
}

public sealed class KitSettings
{
    [JsonPropertyName("gearLevel")]
    public float GearLevel { get; set; } = 90f;

    [JsonPropertyName("healOnApply")]
    public bool HealOnApply { get; set; } = true;

    [JsonPropertyName("clearInventoryFirst")]
    public bool ClearInventoryFirst { get; set; } = true;
}

public sealed class BloodConfig
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "Warrior";

    [JsonPropertyName("quality")]
    public float Quality { get; set; } = 100f;

    [JsonPropertyName("value")]
    public float Value { get; set; } = 10f;
}

public sealed class ArmorsConfig
{
    [JsonPropertyName("chest")]
    public string? Chest { get; set; }

    [JsonPropertyName("legs")]
    public string? Legs { get; set; }

    [JsonPropertyName("gloves")]
    public string? Gloves { get; set; }

    [JsonPropertyName("boots")]
    public string? Boots { get; set; }

    [JsonPropertyName("cloak")]
    public string? Cloak { get; set; }

    [JsonPropertyName("headgear")]
    public string? Headgear { get; set; }

    [JsonPropertyName("magicSource")]
    public string? MagicSource { get; set; }

    [JsonPropertyName("bag")]
    public string? Bag { get; set; }
}

public sealed class WeaponConfig
{
    [JsonPropertyName("prefab")]
    public string Prefab { get; set; } = "";

    [JsonPropertyName("amount")]
    public int Amount { get; set; } = 1;

    [JsonPropertyName("isLegendary")]
    public bool IsLegendary { get; set; }

    [JsonPropertyName("legendaryTier")]
    public int LegendaryTier { get; set; }
}

public sealed class ItemConfig
{
    [JsonPropertyName("prefab")]
    public string Prefab { get; set; } = "";

    [JsonPropertyName("amount")]
    public int Amount { get; set; } = 1;
}

public sealed class AbilitiesKitConfig
{
    [JsonPropertyName("travel")]
    public AbilityKitSlot? Travel { get; set; }

    [JsonPropertyName("spell1")]
    public AbilityKitSlot? Spell1 { get; set; }

    [JsonPropertyName("spell2")]
    public AbilityKitSlot? Spell2 { get; set; }

    [JsonPropertyName("ultimate")]
    public AbilityKitSlot? Ultimate { get; set; }

    [JsonPropertyName("counter")]
    public AbilityKitSlot? Counter { get; set; }

    [JsonPropertyName("veil")]
    public AbilityKitSlot? Veil { get; set; }
}

public sealed class AbilityKitSlot
{
    [JsonPropertyName("prefab")]
    public string Prefab { get; set; } = "";
}

public sealed class PassiveSpellConfig
{
    [JsonPropertyName("slot")]
    public int Slot { get; set; }

    [JsonPropertyName("prefab")]
    public string Prefab { get; set; } = "";
}
