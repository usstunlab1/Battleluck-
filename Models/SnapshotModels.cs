/// <summary>
/// Versioned snapshot contract — captures full entity state per player.
/// 13-category storage contract: position, health, energy, blood, equipment levels,
/// equipment slots, inventory, weapons, abilities, jewels, passives, buffs, and
/// progression. RestoreSnapshot currently replays the native-supported categories
/// and retains energy/jewel/progression fields for forward-compatible snapshots.
/// </summary>
public sealed class PlayerSnapshot
{
    public int Version { get; set; } = 2;
    public string GameVersion { get; set; } = "";
    public string PlayerId { get; set; } = "";
    public string Name { get; set; } = "";
    public DateTime Timestamp { get; set; }
    public int ZoneHash { get; set; }
    /// <summary>Managed event session id that owns this snapshot, when captured at event entry.</summary>
    public string EventRunId { get; set; } = "";
    /// <summary>Mode id associated with EventRunId; empty for non-event snapshots.</summary>
    public string EventModeId { get; set; } = "";

    public Vec3Snapshot Position { get; set; } = new();
    public HealthSnapshot Health { get; set; } = new();
    public EnergySnapshot Energy { get; set; } = new();
    public BloodSnapshot Blood { get; set; } = new();
    public EquipmentLevelsSnapshot EquipmentLevels { get; set; } = new();
    public EquipmentSlotsSnapshot Equipment { get; set; } = new();
    public List<InventoryItemSnapshot> Inventory { get; set; } = new();
    public List<WeaponSnapshot> Weapons { get; set; } = new();
    public AbilitiesSnapshot Abilities { get; set; } = new();
    public List<JewelSnapshot> Jewels { get; set; } = new();
    public List<PassiveSlotSnapshot> Passives { get; set; } = new();
    public List<BuffSnapshot> Buffs { get; set; } = new();
    public ProgressionSnapshot Progression { get; set; } = new();
}

public sealed class Vec3Snapshot
{
    public float X { get; set; }
    public float Y { get; set; }
    public float Z { get; set; }
}

public sealed class HealthSnapshot
{
    public float Current { get; set; }
    public float Max { get; set; }
}

public sealed class EnergySnapshot
{
    public float Current { get; set; }
    public float Max { get; set; }
}

public sealed class BloodSnapshot
{
    public string Type { get; set; } = "";
    public int TypeGuid { get; set; }
    public float Quality { get; set; }
    public float Value { get; set; }
    public string? SecondaryType { get; set; }
    public float SecondaryQuality { get; set; }
    public int SecondaryBuffIndex { get; set; } = -1;
}

public sealed class EquipmentLevelsSnapshot
{
    public float Weapon { get; set; }
    public float Armor { get; set; }
    public float Spell { get; set; }
}

public sealed class EquipmentSlotSnapshot
{
    public string Prefab { get; set; } = "";
    public int Guid { get; set; }
}

public sealed class EquipmentSlotsSnapshot
{
    public EquipmentSlotSnapshot? Weapon { get; set; }
    public EquipmentSlotSnapshot? Chest { get; set; }
    public EquipmentSlotSnapshot? Legs { get; set; }
    public EquipmentSlotSnapshot? Boots { get; set; }
    public EquipmentSlotSnapshot? Gloves { get; set; }
    public EquipmentSlotSnapshot? Head { get; set; }
    public EquipmentSlotSnapshot? Cloak { get; set; }
    public EquipmentSlotSnapshot? MagicSource { get; set; }
    public EquipmentSlotSnapshot? Bag { get; set; }
}

public sealed class InventoryItemSnapshot
{
    public string Prefab { get; set; } = "";
    public int Guid { get; set; }
    public int Amount { get; set; }
    public int Slot { get; set; }
}

public sealed class WeaponSnapshot
{
    public string Prefab { get; set; } = "";
    public int Guid { get; set; }
    public int Slot { get; set; }
    public bool IsLegendary { get; set; }
    public int LegendaryTier { get; set; }
    public SpellModSnapshot? InfuseSpellMod { get; set; }
    public List<SpellModSnapshot> SpellMods { get; set; } = new();
    public List<StatModSnapshot> StatMods { get; set; } = new();
}

public sealed class SpellModSnapshot
{
    public int Guid { get; set; }
    public float Power { get; set; }
}

public sealed class StatModSnapshot
{
    public string Type { get; set; } = "";
    public float Value { get; set; }
}

public sealed class AbilitiesSnapshot
{
    public AbilitySlotSnapshot? Primary { get; set; }
    public AbilitySlotSnapshot? Veil { get; set; }
    public AbilitySlotSnapshot? Travel { get; set; }
    public AbilitySlotSnapshot? Counter { get; set; }
    public AbilitySlotSnapshot? Spell1 { get; set; }
    public AbilitySlotSnapshot? Spell2 { get; set; }
    public AbilitySlotSnapshot? Ultimate { get; set; }
    public List<AbilitySlotSnapshot> Slots { get; set; } = new();
}

public sealed class AbilitySlotSnapshot
{
    public int Slot { get; set; }
    public string Prefab { get; set; } = "";
    public int Guid { get; set; }
    public bool CopyCooldown { get; set; } = true;
    public int Priority { get; set; }
}

public sealed class JewelSnapshot
{
    public string AbilitySlot { get; set; } = "";
    public string Prefab { get; set; } = "";
    public int Guid { get; set; }
    public SpellModSnapshot? SpellMod { get; set; }
}

public sealed class PassiveSlotSnapshot
{
    public int Slot { get; set; }
    public string Prefab { get; set; } = "";
    public int Guid { get; set; }
}

public sealed class BuffSnapshot
{
    public string Prefab { get; set; } = "";
    public int Guid { get; set; }
    public int Stacks { get; set; }
    public float RemainingDuration { get; set; }
}

public sealed class ProgressionSnapshot
{
    public List<int> UnlockedVBloods { get; set; } = new();
    public List<int> UnlockedRecipes { get; set; } = new();
    public List<int> UnlockedBlueprints { get; set; } = new();
    public List<int> UnlockedShapeshifts { get; set; } = new();
    public List<int> UnlockedWaypoints { get; set; } = new();
    public List<int> ResearchProgress { get; set; } = new();
}
