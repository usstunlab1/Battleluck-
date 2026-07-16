namespace BattleLuck.Core.Validation;

using BattleLuck.Utilities;

public static class PrefabValidator
{
    public static IReadOnlyList<string> Validate(string modeId, ModeConfig config)
    {
        var issues = new List<string>();
        foreach (var prefab in config.Prefabs)
        {
            if (prefab.Guid == 0 && string.IsNullOrWhiteSpace(prefab.PrefabName))
                issues.Add($"Prefab '{prefab.Name}' is missing both guid and prefabName in mode '{modeId}'.");
        }

        foreach (var zone in config.Zones.Zones)
        {
            var walls = zone.Boundary?.Walls;
            if (walls?.Enabled == true && walls.SpawnWalls)
                ValidateReference(walls.WallPrefab, $"zone '{zone.Name}' wall prefab");
            if (walls?.Enabled == true && walls.SpawnFloors)
                ValidateReference(walls.FloorPrefab, $"zone '{zone.Name}' floor prefab");
            if (zone.MovingPlatform?.Enabled == true)
                ValidateReference(zone.MovingPlatform.TilePrefab, $"zone '{zone.Name}' platform tile prefab");

            if (zone.LootCrates?.Enabled == true && zone.LootCrates.CrateTypes != null)
            {
                ValidateReference(zone.LootCrates.ContainerPrefab, $"zone '{zone.Name}' loot container prefab");
                if (zone.LootCrates.LockedUntilKills < 0)
                    issues.Add($"Zone '{zone.Name}' has negative lockedUntilKills in mode '{modeId}'.");
                if (zone.LootCrates.WinnerOnly && zone.LootCrates.LockedUntilKills <= 0)
                    issues.Add($"Zone '{zone.Name}' winner chest must declare a positive lockedUntilKills in mode '{modeId}'.");

                foreach (var crate in zone.LootCrates.CrateTypes)
                {
                    ValidateReference(crate.Prefab, $"zone '{zone.Name}' loot crate '{crate.Type}' prefab");
                    if (crate.Amount <= 0)
                        issues.Add($"Zone '{zone.Name}' loot crate '{crate.Type}' amount must be positive in mode '{modeId}'.");
                }
            }
        }

        return issues;

        void ValidateReference(string? value, string label)
        {
            if (string.IsNullOrWhiteSpace(value))
                return;
            // Named prefabs cannot be authoritatively checked until the server
            // world and PrefabCollectionSystem are available. Startup config
            // loading happens before that point, so only reject an explicit
            // zero hash here and defer names to ValidateLive.
            if (int.TryParse(value, out var hash) && hash == 0)
                issues.Add($"Invalid {label} '{value}' in mode '{modeId}'.");
        }
    }

    public static IReadOnlyList<string> ValidateLive(string modeId, ModeConfig config)
    {
        var issues = new List<string>();

        foreach (var zone in config.Zones.Zones)
        {
            var walls = zone.Boundary?.Walls;
            if (walls?.Enabled == true && walls.SpawnWalls)
                ValidateReference(walls.WallPrefab, $"zone '{zone.Name}' wall prefab", requireSafeTileModel: true);
            if (walls?.Enabled == true && walls.SpawnFloors)
                ValidateReference(walls.FloorPrefab, $"zone '{zone.Name}' floor prefab", requireSafeTileModel: true);
            if (zone.MovingPlatform?.Enabled == true)
                ValidateReference(zone.MovingPlatform.TilePrefab, $"zone '{zone.Name}' platform tile prefab", requireSafeTileModel: true);

            if (zone.LootCrates?.Enabled == true)
            {
                ValidateReference(zone.LootCrates.ContainerPrefab, $"zone '{zone.Name}' loot container prefab");
                foreach (var crate in zone.LootCrates.CrateTypes)
                    ValidateReference(crate.Prefab, $"zone '{zone.Name}' loot reward '{crate.Type}' prefab");
            }
        }

        return issues;

        void ValidateReference(string? value, string label, bool requireSafeTileModel = false)
        {
            if (string.IsNullOrWhiteSpace(value))
                return;

            PrefabGUID? guid = int.TryParse(value, out var hash)
                ? new PrefabGUID(hash)
                : PrefabHelper.GetLivePrefabGuid(value);

            if (!guid.HasValue || guid.Value.GuidHash == 0 || !PrefabHelper.ValidatePrefab(guid.Value))
                issues.Add($"Live prefab resolution failed for {label} '{value}' in mode '{modeId}'.");
            else if (requireSafeTileModel && !EventTileSafety.TryResolveSafeTileModelPrefab(guid.Value, out _, out var tileError))
                issues.Add($"Unsafe {label} '{value}' in mode '{modeId}': {tileError}.");
            else
                BattleLuckPlugin.LogInfo($"[PrefabValidator] Live resolved {modeId} {label}: {value} -> {guid.Value.GuidHash}.");
        }
    }
}
