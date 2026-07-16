using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using BattleLuck.Models;
using BattleLuck.Services.Flow;

namespace BattleLuck.Core.Validation;

public static class AnalyticsValidator
{
    static readonly string[] _analyticsSpawnActions =
    {
        "spawn.boss", "spawn.wave", "npc.spawn", "entity.spawn",
        "prefab.spawn", "npc.spawn"
    };

    static readonly string[] _analyticsLifecycleActions =
    {
        "snapshot.save", "snapshot.restore", "player.snapshot.restore",
        "schematic.load", "schematic.clear", "schematic.destroy.radius",
        "zone.border.place", "zone.border.remove"
    };

    static readonly string[] _analyticsTeleportActions =
    {
        "teleport", "player.teleport", "teleport.position", "point.set", "point.remove", "point.clear_session", "effect.spawn_at_point"
    };

    static readonly string[] _analyticsRegionActions =
    {
        "shrink.zone", "zone.buff.apply", "zone.buff.remove", "glow.enable", "glow.disable"
    };

    static readonly string[] _analyticsPvPActions =
    {
        "enable_pvp", "disable_pvp", "pvp.enable", "pvp.disable",
        "death.prevent", "death.allow", "player.stun", "player.freeze"
    };

    static readonly string[] _analyticsInventoryActions =
    {
        "inventory.send", "inventory.stash", "inventory.salvage",
        "inventory.clear_all", "inventory.clear_kit", "inventory.count"
    };

    static readonly string[] _analyticsBuffActions =
    {
        "buff.apply", "buff.remove", "player.buff.apply", "player.buff.remove",
        "zone.buff.apply", "zone.buff.remove", "blood.change", "set_blood", "blood.set"
    };

    static readonly HashSet<string> _validBloodTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "Scholar", "Warrior", "Rogue", "Brute", "Worker", "Creature", "Draculin"
    };

    static readonly Regex _guidHashRegex = new(@"^-?\d+$", RegexOptions.Compiled);

    public static IReadOnlyList<string> Validate(string modeId, ModeConfig config)
    {
        var issues = new List<string>();

        ValidateZoneAnalyticsFields(config, modeId, issues);
        ValidateZoneActionCoverage(config, modeId, issues);
        ValidateTeleportWiring(config, modeId, issues);
        ValidateSnapshotWiring(config, modeId, issues);
        ValidateKitWiring(config, modeId, issues);
        ValidateSchematicWiring(config, modeId, issues);
        ValidateResourceReferenceIntegrity(config, modeId, issues);
        ValidateNoSchematicActionsInSessionFlows(config, modeId, issues);
        ValidateBuffActions(config, modeId, issues);
        ValidateBloodTypeActions(config, modeId, issues);
        ValidateBuffSymmetry(config, modeId, issues);

        return issues;
    }

    static void ValidateZoneAnalyticsFields(ModeConfig config, string modeId, List<string> issues)
    {
        if (config.Zones?.Zones == null || config.Zones.Zones.Count == 0)
        {
            issues.Add($"Mode '{modeId}' has no zones defined. Analytics cannot track territory data.");
            return;
        }

        foreach (var zone in config.Zones.Zones)
        {
            if (string.IsNullOrWhiteSpace(zone.Name))
                issues.Add($"Zone hash={zone.Hash} in mode '{modeId}' has no Name. Analytics needs zone names for readable reports.");

            if (string.IsNullOrWhiteSpace(zone.Type))
                issues.Add($"Zone '{zone.Name}' (hash={zone.Hash}) in mode '{modeId}' has no Type. Analytics requires zone type for classification.");

            if (zone.Position == null || IsEmptyVec3(zone.Position))
                issues.Add($"Zone '{zone.Name}' (hash={zone.Hash}) in mode '{modeId}' has no Position. Analytics requires zone center coordinates.");

            if (zone.Center == null || IsEmptyVec3(zone.Center))
                issues.Add($"Zone '{zone.Name}' (hash={zone.Hash}) in mode '{modeId}' has no Center. Analytics requires local center offset.");

            if (zone.Radius <= 0)
                issues.Add($"Zone '{zone.Name}' (hash={zone.Hash}) in mode '{modeId}' has invalid radius={zone.Radius}. Analytics requires positive radius.");

            if (zone.ExitRadius > 0 && zone.ExitRadius < zone.Radius)
                issues.Add($"Zone '{zone.Name}' (hash={zone.Hash}) in mode '{modeId}' has exitRadius < radius. Analytics boundary tracking requires exitRadius >= radius.");

            if (zone.TeleportSpawn == null || IsEmptyVec3(zone.TeleportSpawn))
                issues.Add($"Zone '{zone.Name}' (hash={zone.Hash}) in mode '{modeId}' has no TeleportSpawn. Analytics tracks entry points via teleportSpawn.");

            if (zone.Boundary == null)
                issues.Add($"Zone '{zone.Name}' (hash={zone.Hash}) in mode '{modeId}' has no Boundary config. Analytics cannot classify zone containment policy.");

            if (zone.Boundary?.Walls != null && zone.Boundary.Walls.Enabled)
            {
                if (string.IsNullOrWhiteSpace(zone.Boundary.Walls.WallPrefab))
                    issues.Add($"Zone '{zone.Name}' (hash={zone.Hash}) wall prefab is empty. Analytics tracks wall spawning for territory pressure.");

                if (zone.Boundary.Walls.Spacing <= 0)
                    issues.Add($"Zone '{zone.Name}' (hash={zone.Hash}) wall spacing is invalid. Analytics uses spacing to estimate wall count.");

                if (zone.Boundary.Walls.Height <= 0)
                    issues.Add($"Zone '{zone.Name}' (hash={zone.Hash}) wall height is invalid. Analytics uses height for territory defense scoring.");
            }
        }
    }

    static void ValidateZoneActionCoverage(ModeConfig config, string modeId, List<string> issues)
    {
        var allEnterActions = ResolveAllActions(config, "enter");
        var allExitActions = ResolveAllActions(config, "exit");
        var allStartActions = ResolveAllActions(config, "start");
        var allTrackingActions = ResolveAllActions(config, "tracking");
        var allWinnerActions = ResolveAllActions(config, "winner");
        var allEndingActions = ResolveAllActions(config, "ending");

        if (allEnterActions.Count == 0)
            issues.Add($"Mode '{modeId}' enter flow is empty. Analytics requires at least one enter action to track sessions.");

        if (allExitActions.Count == 0)
            issues.Add($"Mode '{modeId}' exit flow is empty. Analytics requires exit actions to compute session duration and cleanup.");

        var hasSpawnAction = allEnterActions.Concat(allStartActions).Any(a => HasActionPrefix(a, _analyticsSpawnActions));
        var hasLifecycleAction = allEnterActions.Concat(allExitActions).Any(a => HasActionPrefix(a, _analyticsLifecycleActions));
        var hasTeleportAction = allEnterActions.Any(a => HasActionPrefix(a, _analyticsTeleportActions));
        var hasRegionAction = allEnterActions.Concat(allTrackingActions).Any(a => HasActionPrefix(a, _analyticsRegionActions));
        var hasPvPAction = allEnterActions.Concat(allTrackingActions).Any(a => HasActionPrefix(a, _analyticsPvPActions));
        var hasInventoryAction = allEnterActions.Concat(allExitActions).Any(a => HasActionPrefix(a, _analyticsInventoryActions));

        if (!hasSpawnAction && _analyticsSpawnActions.Any(a => !allEnterActions.Contains(a)))
            issues.Add($"Mode '{modeId}' enter flow has no spawn action. Analytics cannot track entity spawn counts.");

        if (!hasLifecycleAction)
            issues.Add($"Mode '{modeId}' flows have no snapshot/schematic lifecycle action. Analytics cannot track object lifetime.");

        if (!hasTeleportAction)
            issues.Add($"Mode '{modeId}' enter flow has no teleport action. Analytics cannot record zone entry points.");

        if (!hasRegionAction)
            issues.Add($"Mode '{modeId}' flows have no region action. Analytics cannot track territory pressure (shrink/buffs/glow).");

        if (config.Rules.EnablePvP && !hasPvPAction)
            issues.Add($"Mode '{modeId}' has enablePvP=true but enter flow has no PvP action. Analytics expects PvP to be explicitly toggled.");

        if (!hasInventoryAction)
            issues.Add($"Mode '{modeId}' flows have no inventory action. Analytics cannot track item flow or currency drain.");
    }

    static void ValidateTeleportWiring(ModeConfig config, string modeId, List<string> issues)
    {
        var enterActions = ResolveAllActions(config, "enter");
        var knownHashes = new HashSet<int>(config.Zones.Zones.Select(z => z.Hash));

        foreach (var actionString in enterActions)
        {
            var (actionName, parameters) = FlowActionExecutor.ParseActionString(actionString);
            var normalized = ActionRegistryValidator.NormalizeActionName(actionName);

            if (!_analyticsTeleportActions.Contains(normalized))
                continue;

            if (parameters.TryGetValue("targetZoneHash", out var hashStr))
            {
                if (!int.TryParse(hashStr, NumberStyles.Integer, CultureInfo.InvariantCulture, out var zoneHash) || zoneHash <= 0)
                {
                    issues.Add($"Teleport action '{actionString}' in mode '{modeId}' has invalid targetZoneHash '{hashStr}'.");
                    continue;
                }

                if (!knownHashes.Contains(zoneHash))
                {
                    issues.Add($"Teleport action '{actionString}' in mode '{modeId}' references zoneHash {zoneHash} which does not exist.");
                    continue;
                }

                var zone = config.Zones.Zones.FirstOrDefault(z => z.Hash == zoneHash);
                if (zone != null && (zone.TeleportSpawn == null || IsEmptyVec3(zone.TeleportSpawn)))
                {
                    issues.Add($"Teleport action '{actionString}' targets zone '{zone.Name}' (hash={zoneHash}) which has no TeleportSpawn position.");
                }
            }
        }
    }

    static void ValidateSnapshotWiring(ModeConfig config, string modeId, List<string> issues)
    {
        var enterActions = ResolveAllActions(config, "enter");
        var exitActions = ResolveAllActions(config, "exit");

        var hasEnterSnapshot = enterActions.Any(a =>
        {
            var (name, _) = FlowActionExecutor.ParseActionString(a);
            return name is "snapshot.save" or "snapshot.save_old" or "snapshot.mark_active";
        });

        var hasExitRestore = exitActions.Any(a =>
        {
            var (name, _) = FlowActionExecutor.ParseActionString(a);
            return name is "snapshot.restore" or "snapshot.restore_old" or "player.snapshot.restore" or "snapshot.clear_active";
        });

        if (hasEnterSnapshot && !hasExitRestore)
            issues.Add($"Mode '{modeId}' saves a snapshot on enter but does not restore or clear it on exit. Analytics session lifecycle is incomplete.");

        var hasExitSnapshot = exitActions.Any(a =>
        {
            var (name, _) = FlowActionExecutor.ParseActionString(a);
            return name is "snapshot.save" or "snapshot.save_old";
        });

        if (hasExitSnapshot)
            issues.Add($"Mode '{modeId}' saves a snapshot on exit. This may create orphaned analytics records.");
    }

    static void ValidateKitWiring(ModeConfig config, string modeId, List<string> issues)
    {
        var enterActions = ResolveAllActions(config, "enter");
        var exitActions = ResolveAllActions(config, "exit");

        var enterKit = enterActions.Select(a => FlowActionExecutor.ParseActionString(a))
            .FirstOrDefault(p => string.Equals(p.actionName, "kit.apply", StringComparison.OrdinalIgnoreCase))
            .actionName;

        var exitKit = exitActions.Select(a => FlowActionExecutor.ParseActionString(a))
            .FirstOrDefault(p => string.Equals(p.actionName, "kit.apply", StringComparison.OrdinalIgnoreCase))
            .actionName;

        if (!string.IsNullOrWhiteSpace(enterKit) && string.IsNullOrWhiteSpace(exitKit))
            issues.Add($"Mode '{modeId}' applies a kit on enter but not on exit. Analytics tracking becomes inaccurate due to lingering event gear.");

        if (string.IsNullOrWhiteSpace(enterKit) && !string.IsNullOrWhiteSpace(exitKit))
            issues.Add($"Mode '{modeId}' applies a kit on exit but not on enter. Analytics cannot correlate kit usage with mode sessions.");
    }

    static void ValidateSchematicWiring(ModeConfig config, string modeId, List<string> issues)
    {
        var allActions = ResolveAllActions(config, "enter")
            .Concat(ResolveAllActions(config, "exit"))
            .Concat(ResolveAllActions(config, "start"))
            .Concat(ResolveAllActions(config, "tracking"))
            .Concat(ResolveAllActions(config, "winner"))
            .Concat(ResolveAllActions(config, "ending"));

        var schematicActions = allActions.Where(a => HasActionPrefix(a, new[] { "schematic.load", "schematic.loadat", "schematic.loadatpos" })).ToList();
        if (schematicActions.Count > 0)
        {
            foreach (var action in schematicActions)
            {
                var (_, parameters) = FlowActionExecutor.ParseActionString(action);
                if (!parameters.ContainsKey("eventName") && !parameters.ContainsKey("schematicName"))
                    issues.Add($"Schematic action '{action}' in mode '{modeId}' is missing eventName/schematicName parameter.");
            }
        }
    }

    static void ValidateResourceReferenceIntegrity(ModeConfig config, string modeId, List<string> issues)
    {
        if (config.KitConfig == null)
        {
            issues.Add($"Mode '{modeId}' has no KitConfig. Analytics cannot validate resource/item references.");
            return;
        }

        if (config.KitConfig.Weapons != null)
        {
            foreach (var weapon in config.KitConfig.Weapons)
                ValidatePrefabReference(weapon.Prefab, "weapon", modeId, issues);
        }

        if (config.KitConfig.Items != null)
        {
            foreach (var item in config.KitConfig.Items)
                ValidatePrefabReference(item.Prefab, "item", modeId, issues);
        }

        if (config.KitConfig.Armors != null)
        {
            ValidateArmorReference(config.KitConfig.Armors.Chest, "chest", modeId, issues);
            ValidateArmorReference(config.KitConfig.Armors.Legs, "legs", modeId, issues);
            ValidateArmorReference(config.KitConfig.Armors.Gloves, "gloves", modeId, issues);
            ValidateArmorReference(config.KitConfig.Armors.Boots, "boots", modeId, issues);
            ValidateArmorReference(config.KitConfig.Armors.Cloak, "cloak", modeId, issues);
            ValidateArmorReference(config.KitConfig.Armors.Headgear, "headgear", modeId, issues);
        }
    }

    static void ValidateNoSchematicActionsInSessionFlows(ModeConfig config, string modeId, List<string> issues)
    {
        var phases = new[]
        {
            ("enter", ResolveAllActions(config, "enter")),
            ("exit", ResolveAllActions(config, "exit")),
            ("start", ResolveAllActions(config, "start")),
            ("tracking", ResolveAllActions(config, "tracking")),
            ("winner", ResolveAllActions(config, "winner")),
            ("ending", ResolveAllActions(config, "ending"))
        };

        foreach (var (phase, actions) in phases)
        {
            foreach (var actionString in actions)
            {
                var (actionName, _) = FlowActionExecutor.ParseActionString(actionString);
                var normalized = ActionRegistryValidator.NormalizeActionName(actionName);
                if (normalized is "schematic.load" or "schematic.loadat" or "schematic.loadatpos")
                {
                    issues.Add($"Deprecated schematic action '{actionName}' found in phase '{phase}' for mode '{modeId}'. Analytics expects zone geometry to be spawned via zone.border.* or build.spawn, not session flows.");
                }
            }
        }
    }

    static List<string> ResolveAllActions(ModeConfig config, string phase)
    {
        var result = new List<string>();
        FlowConfig? flow = phase switch
        {
            "enter" => config.FlowEnter,
            "exit" => config.FlowExit,
            "start" => config.Session?.Flow?.Start,
            "tracking" => config.Session?.Flow?.Tracking,
            "winner" => config.Session?.Flow?.Winner,
            "ending" => config.Session?.Flow?.Ending,
            _ => null
        };

        if (flow?.Flows == null) return result;

        foreach (var flowDef in flow.Flows.Values)
        {
            if (flowDef.Actions != null)
                result.AddRange(flowDef.Actions);
        }

        return result;
    }

    static bool HasActionPrefix(string actionString, string[] prefixes)
    {
        var (name, _) = FlowActionExecutor.ParseActionString(actionString);
        return prefixes.Contains(name, StringComparer.OrdinalIgnoreCase);
    }

    static bool HasActionPrefix(string actionString, string prefix)
    {
        var (name, _) = FlowActionExecutor.ParseActionString(actionString);
        return string.Equals(name, prefix, StringComparison.OrdinalIgnoreCase);
    }

    static bool IsEmptyVec3(Vec3Config? vec)
    {
        if (vec == null) return true;
        return vec.X == 0 && vec.Y == 0 && vec.Z == 0;
    }

    static bool ValidatePrefabReference(string? prefab, string category, string modeId, List<string> issues)
    {
        if (string.IsNullOrWhiteSpace(prefab))
        {
            issues.Add($"Mode '{modeId}' has empty {category} prefab reference.");
            return false;
        }

        if (_guidHashRegex.IsMatch(prefab))
        {
            if (int.TryParse(prefab, out var guidHash) && guidHash != 0)
                return true;

            issues.Add($"Mode '{modeId}' has invalid numeric {category} prefab '{prefab}'.");
            return false;
        }

        return true;
    }

    static void ValidateArmorReference(string? prefab, string slot, string modeId, List<string> issues)
    {
        ValidatePrefabReference(prefab, $"armor_{slot}", modeId, issues);
    }

    static void ValidateBuffActions(ModeConfig config, string modeId, List<string> issues)
    {
        var allActions = ResolveAllActions(config, "enter")
            .Concat(ResolveAllActions(config, "exit"))
            .Concat(ResolveAllActions(config, "start"))
            .Concat(ResolveAllActions(config, "tracking"))
            .Concat(ResolveAllActions(config, "winner"))
            .Concat(ResolveAllActions(config, "ending"));

        foreach (var actionString in allActions)
        {
            var (actionName, parameters) = FlowActionExecutor.ParseActionString(actionString);
            var normalized = ActionRegistryValidator.NormalizeActionName(actionName);

            if (normalized is "buff.apply" or "player.buff.apply")
            {
                if (!parameters.ContainsKey("buffPrefab") && !parameters.ContainsKey("buff"))
                    issues.Add($"Action '{actionName}' in mode '{modeId}' is missing 'buffPrefab' parameter. Analytics cannot track buff applications.");

                if (!parameters.ContainsKey("duration") && !parameters.ContainsKey("durationSeconds"))
                    issues.Add($"Action '{actionName}' in mode '{modeId}' is missing 'duration' parameter. Analytics cannot compute buff uptime.");
            }

            if (normalized is "buff.remove" or "player.buff.remove")
            {
                if (!parameters.ContainsKey("buffPrefab") && !parameters.ContainsKey("buff"))
                    issues.Add($"Action '{actionName}' in mode '{modeId}' is missing 'buffPrefab' parameter. Analytics cannot track buff removals.");
            }

            if (normalized is "zone.buff.apply")
            {
                if (!parameters.ContainsKey("buffPrefab") && !parameters.ContainsKey("buff"))
                    issues.Add($"Action '{actionName}' in mode '{modeId}' is missing 'buffPrefab' parameter. Analytics cannot track zone buffs.");

                if (!parameters.ContainsKey("zoneHash"))
                    issues.Add($"Action '{actionName}' in mode '{modeId}' is missing 'zoneHash' parameter. Analytics cannot attribute zone buffs to territories.");
            }

            if (normalized is "zone.buff.remove")
            {
                if (!parameters.ContainsKey("buffPrefab") && !parameters.ContainsKey("buff"))
                    issues.Add($"Action '{actionName}' in mode '{modeId}' is missing 'buffPrefab' parameter. Analytics cannot track zone buff removals.");
            }
        }
    }

    static void ValidateBloodTypeActions(ModeConfig config, string modeId, List<string> issues)
    {
        var enterActions = ResolveAllActions(config, "enter");

        foreach (var actionString in enterActions)
        {
            var (actionName, parameters) = FlowActionExecutor.ParseActionString(actionString);
            var normalized = ActionRegistryValidator.NormalizeActionName(actionName);

            if (normalized is "blood.change" or "set_blood" or "blood.set")
            {
                if (!parameters.TryGetValue("bloodType", out var bloodType) || string.IsNullOrWhiteSpace(bloodType))
                {
                    issues.Add($"Action '{actionName}' in mode '{modeId}' is missing 'bloodType' parameter. Analytics cannot track blood type distribution.");
                    continue;
                }

                if (!_validBloodTypes.Contains(bloodType))
                    issues.Add($"Action '{actionName}' in mode '{modeId}' has invalid bloodType '{bloodType}'. Expected one of: {string.Join(", ", _validBloodTypes)}.");

                if (!parameters.TryGetValue("quality", out var qualityStr) || !int.TryParse(qualityStr, out var quality))
                {
                    issues.Add($"Action '{actionName}' in mode '{modeId}' is missing or has invalid 'quality' parameter. Analytics requires quality for blood type tracking.");
                    continue;
                }

                if (quality < 0 || quality > 100)
                    issues.Add($"Action '{actionName}' in mode '{modeId}' has invalid blood quality '{quality}'. Expected 0-100.");
            }
        }
    }

    static void ValidateBuffSymmetry(ModeConfig config, string modeId, List<string> issues)
    {
        var enterApply = ResolveAllActions(config, "enter").Count(a =>
        {
            var (name, _) = FlowActionExecutor.ParseActionString(a);
            var normalized = ActionRegistryValidator.NormalizeActionName(name);
            return normalized is "buff.apply" or "player.buff.apply";
        });

        var exitRemove = ResolveAllActions(config, "exit").Count(a =>
        {
            var (name, _) = FlowActionExecutor.ParseActionString(a);
            var normalized = ActionRegistryValidator.NormalizeActionName(name);
            return normalized is "buff.remove" or "player.buff.remove";
        });

        var exitApply = ResolveAllActions(config, "exit").Count(a =>
        {
            var (name, _) = FlowActionExecutor.ParseActionString(a);
            var normalized = ActionRegistryValidator.NormalizeActionName(name);
            return normalized is "buff.apply" or "player.buff.apply";
        });

        var enterRemove = ResolveAllActions(config, "enter").Count(a =>
        {
            var (name, _) = FlowActionExecutor.ParseActionString(a);
            var normalized = ActionRegistryValidator.NormalizeActionName(name);
            return normalized is "buff.remove" or "player.buff.remove";
        });

        if (enterApply > 0 && exitRemove == 0)
            issues.Add($"Mode '{modeId}' applies buffs on enter but has no buff removals on exit. Analytics may record orphaned buff states.");

        if (exitApply > 0 && enterRemove == 0)
            issues.Add($"Mode '{modeId}' applies buffs on exit without prior removal. Analytics may record unexpected buff transitions.");

        if (enterApply > 0 && enterRemove > 0)
            issues.Add($"Mode '{modeId}' both applies and removes buffs on enter. Analytics buff lifecycle is ambiguous.");

        if (exitApply > 0 && exitRemove > 0)
            issues.Add($"Mode '{modeId}' both applies and removes buffs on exit. Analytics buff lifecycle is ambiguous.");
    }
}
