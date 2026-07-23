using BattleLuck.Utilities;
using Stunlock.Core;

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

    public static IReadOnlyList<string> ValidateLive(string modeId, ModeConfig config)
    {
        var issues = new List<string>();
        var kit = config.KitConfig;

        foreach (var weapon in kit.Weapons)
            ValidateLiveReference(weapon.Prefab, "weapon");
        foreach (var item in kit.Items)
            ValidateLiveReference(item.Prefab, "item");

        if (kit.Armors != null)
        {
            ValidateLiveReference(kit.Armors.Chest, "armor chest", optional: true);
            ValidateLiveReference(kit.Armors.Legs, "armor legs", optional: true);
            ValidateLiveReference(kit.Armors.Gloves, "armor gloves", optional: true);
            ValidateLiveReference(kit.Armors.Boots, "armor boots", optional: true);
            ValidateLiveReference(kit.Armors.Cloak, "armor cloak", optional: true);
            ValidateLiveReference(kit.Armors.Headgear, "armor headgear", optional: true);
            ValidateLiveReference(kit.Armors.MagicSource, "magic source", optional: true);
            ValidateLiveReference(kit.Armors.Bag, "bag", optional: true);
        }

        return issues;

        void ValidateLiveReference(string? value, string label, bool optional = false)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                if (!optional)
                    issues.Add($"Empty {label} prefab reference in mode '{modeId}'.");
                return;
            }

            PrefabGUID? guid = int.TryParse(value, out var hash)
                ? new PrefabGUID(hash)
                : PrefabHelper.GetLivePrefabGuid(value);
            if (!guid.HasValue || guid.Value.GuidHash == 0 || !PrefabHelper.ValidatePrefab(guid.Value))
                issues.Add($"Live prefab resolution failed for {label} '{value}' in mode '{modeId}'.");
        }
    }

    static bool ValidatePrefabReference(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        if (int.TryParse(value, out var guidHash))
            return guidHash != 0;

        // Named prefabs cannot be resolved authoritatively until the server
        // world and PrefabCollectionSystem are ready.
        return true;
    }
}
