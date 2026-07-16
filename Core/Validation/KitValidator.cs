using BattleLuck.Utilities;

namespace BattleLuck.Core.Validation;

public static class KitValidator
{
    public static IReadOnlyList<string> Validate(string modeId, ModeConfig config)
    {
        var issues = new List<string>();
        var kit = config.KitConfig;

        foreach (var weapon in kit.Weapons)
        {
            if (!ValidatePrefabReference(weapon.Prefab))
                issues.Add($"Invalid weapon prefab '{weapon.Prefab}' in mode '{modeId}'.");
        }

        foreach (var item in kit.Items)
        {
            if (!ValidatePrefabReference(item.Prefab))
                issues.Add($"Invalid item prefab '{item.Prefab}' in mode '{modeId}'.");
        }

        if (kit.Armors != null)
        {
            ValidateArmor(kit.Armors.Chest, "chest");
            ValidateArmor(kit.Armors.Legs, "legs");
            ValidateArmor(kit.Armors.Gloves, "gloves");
            ValidateArmor(kit.Armors.Boots, "boots");
            ValidateArmor(kit.Armors.Cloak, "cloak");
            ValidateArmor(kit.Armors.Headgear, "headgear");
            ValidateArmor(kit.Armors.MagicSource, "magicSource");
            ValidateArmor(kit.Armors.Bag, "bag");
        }

        return issues;

        void ValidateArmor(string? value, string slot)
        {
            if (string.IsNullOrWhiteSpace(value))
                return;
            if (!ValidatePrefabReference(value))
                issues.Add($"Invalid armor prefab '{value}' for slot '{slot}' in mode '{modeId}'.");
        }
    }

    static bool ValidatePrefabReference(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        if (int.TryParse(value, out var guidHash))
            return guidHash != 0;

        return PrefabHelper.TryGetPrefabGuid(value, out _);
    }
}
