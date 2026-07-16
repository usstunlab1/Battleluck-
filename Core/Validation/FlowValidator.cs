using BattleLuck.Core;
using BattleLuck.Services.Flow;
using System.Globalization;

namespace BattleLuck.Core.Validation;

public static class FlowValidator
{
    public static IReadOnlyList<string> Validate(string modeId, ModeConfig config)
    {
        var issues = new List<string>();
        var enterActions = ResolveActions(config.FlowEnter);
        var exitActions = ResolveActions(config.FlowExit);
        var startActions = ResolveActions(config.Session.Flow.Start);
        var trackingActions = ResolveActions(config.Session.Flow.Tracking);
        var winnerActions = ResolveActions(config.Session.Flow.Winner);
        var endingActions = ResolveActions(config.Session.Flow.Ending);

        ValidatePhase("enter", enterActions, modeId, issues, config, required: true);
        ValidatePhase("exit", exitActions, modeId, issues, config, required: true);
        ValidatePhase("start", startActions, modeId, issues, config, required: false);
        ValidatePhase("tracking", trackingActions, modeId, issues, config, required: false);
        ValidatePhase("winner", winnerActions, modeId, issues, config, required: false);
        ValidatePhase("ending", endingActions, modeId, issues, config, required: false);

        ValidateSnapshotSymmetry(enterActions, exitActions, modeId, issues);
        ValidateKitSymmetry(enterActions, exitActions, modeId, issues);
        ValidateTeleportTargets(enterActions, config, modeId, issues);
        ValidateNoDeprecatedSchematicLoad(enterActions, modeId, issues);
        ValidateNoDeprecatedSchematicLoad(exitActions, modeId, issues, phase: "exit");
        ValidatePvpConsistency(enterActions, config, modeId, issues);
        ValidateBloodTypeConsistency(enterActions, modeId, issues);

        CheckZoneHashReferences(enterActions, config, modeId, issues);

        return issues;
    }

    static List<string> ResolveActions(FlowConfig? flow)
    {
        var actions = new List<string>();
        if (flow?.Flows == null) return actions;
        foreach (var flowDef in flow.Flows.Values)
        {
            if (flowDef.Actions != null)
                actions.AddRange(flowDef.Actions);
        }
        return actions;
    }

    static void ValidatePhase(string phase, List<string> actions, string modeId, List<string> issues, ModeConfig config, bool required)
    {
        if (required && actions.Count == 0)
        {
            issues.Add($"Phase '{phase}' has no actions in mode '{modeId}'.");
            return;
        }

        foreach (var actionString in actions)
        {
            var (actionName, parameters) = FlowActionExecutor.ParseActionString(actionString);
            if (string.IsNullOrWhiteSpace(actionName))
            {
                issues.Add($"Empty action in phase '{phase}' for mode '{modeId}'.");
                continue;
            }

            var normalized = ActionRegistryValidator.NormalizeActionName(actionName);
            if (!ActionRegistryValidator.IsKnown(normalized))
            {
                issues.Add($"Unknown action '{actionName}' in phase '{phase}' for mode '{modeId}'.");
            }

            ValidateActionParameters(actionName, parameters, phase, modeId, issues);
        }
    }

    static void ValidateActionParameters(string actionName, Dictionary<string, string> parameters, string phase, string modeId, List<string> issues)
    {
        var normalized = ActionRegistryValidator.NormalizeActionName(actionName);

        if (normalized is "kit.apply" or "kit.apply_weapons" or "kit.apply_armor" or "inventory.clear_kit")
        {
            if (!parameters.ContainsKey("kitId") && !parameters.ContainsKey("kitId"))
                issues.Add($"Action '{actionName}' in phase '{phase}' is missing 'kitId' parameter in mode '{modeId}'.");
        }

        if (normalized is "blood.change" or "set_blood" or "blood.set")
        {
            if (!parameters.ContainsKey("bloodType"))
                issues.Add($"Action '{actionName}' in phase '{phase}' is missing 'bloodType' parameter in mode '{modeId}'.");
            if (!parameters.ContainsKey("quality") && !parameters.ContainsKey("Quality"))
                issues.Add($"Action '{actionName}' in phase '{phase}' is missing 'quality' parameter in mode '{modeId}'.");
        }

        if (normalized is "teleport" or "player.teleport")
        {
            var hasTarget = parameters.ContainsKey("targetZoneHash") ||
                           parameters.ContainsKey("position") ||
                           parameters.ContainsKey("targetPosition");
            if (!hasTarget)
                issues.Add($"Action '{actionName}' in phase '{phase}' has no teleport target in mode '{modeId}'. Expected 'targetZoneHash' or 'position'.");
        }

        if (normalized is "schematic.load" or "schematic.loadat" or "schematic.loadatpos")
        {
            if (!parameters.ContainsKey("eventName") && !parameters.ContainsKey("schematicName"))
                issues.Add($"Action '{actionName}' in phase '{phase}' is missing 'eventName' parameter in mode '{modeId}'.");
        }

        if (normalized is "buff.apply")
        {
            if (!parameters.ContainsKey("buffPrefab") && !parameters.ContainsKey("buff"))
                issues.Add($"Action '{actionName}' in phase '{phase}' is missing 'buffPrefab' parameter in mode '{modeId}'.");
        }

        if (normalized is "npc.spawn")
        {
            if (!parameters.ContainsKey("prefab") && !parameters.ContainsKey("prefabGuid"))
                issues.Add($"Action '{actionName}' in phase '{phase}' is missing 'prefab' parameter in mode '{modeId}'.");
            if (!parameters.ContainsKey("count"))
                issues.Add($"Action '{actionName}' in phase '{phase}' is missing 'count' parameter in mode '{modeId}'.");
        }

        if (normalized is "spawn.boss" or "boss.spawn")
        {
            if (!parameters.ContainsKey("prefab") && !parameters.ContainsKey("prefabGuid"))
                issues.Add($"Action '{actionName}' in phase '{phase}' is missing 'prefab' parameter in mode '{modeId}'.");
            if (!parameters.ContainsKey("bossId"))
                issues.Add($"Action '{actionName}' in phase '{phase}' is missing 'bossId' parameter in mode '{modeId}'.");
        }

        if (normalized is "snapshot.save" or "snapshot.save_old")
        {
            if (!parameters.ContainsKey("zoneHash"))
                issues.Add($"Action '{actionName}' in phase '{phase}' is missing 'zoneHash' parameter in mode '{modeId}'.");
        }

        if (normalized is "snapshot.restore" or "snapshot.restore_old" or "player.snapshot.restore")
        {
            if (!parameters.ContainsKey("zoneHash"))
                issues.Add($"Action '{actionName}' in phase '{phase}' is missing 'zoneHash' parameter in mode '{modeId}'.");
        }
    }

    static void ValidateSnapshotSymmetry(List<string> enterActions, List<string> exitActions, string modeId, List<string> issues)
    {
        var hasEnterSnapshot = enterActions.Any(a =>
        {
            var (name, _) = FlowActionExecutor.ParseActionString(a);
            return name is "snapshot.save" or "snapshot.save_old";
        });

        var hasExitRestore = exitActions.Any(a =>
        {
            var (name, _) = FlowActionExecutor.ParseActionString(a);
            return name is "snapshot.restore" or "snapshot.restore_old" or "player.snapshot.restore";
        });

        if (hasEnterSnapshot && !hasExitRestore)
        {
            issues.Add($"Mode '{modeId}' saves a snapshot on enter but does not restore it on exit.");
        }

        var hasExitSnapshot = exitActions.Any(a =>
        {
            var (name, _) = FlowActionExecutor.ParseActionString(a);
            return name is "snapshot.save" or "snapshot.save_old";
        });

        if (hasExitSnapshot)
        {
            issues.Add($"Mode '{modeId}' saves a snapshot on exit, which is unusual and may leak state.");
        }
    }

    static void ValidateKitSymmetry(List<string> enterActions, List<string> exitActions, string modeId, List<string> issues)
    {
        var enterKit = ExtractFirstAction(enterActions, "kit.apply");
        var exitKit = ExtractFirstAction(exitActions, "kit.apply");

        if (string.IsNullOrWhiteSpace(enterKit) && exitActions.Any(a => FlowActionExecutor.ParseActionString(a).actionName is "kit.apply"))
        {
            issues.Add($"Mode '{modeId}' applies a kit on exit but not on enter.");
        }

        if (!string.IsNullOrWhiteSpace(enterKit) && string.IsNullOrWhiteSpace(exitKit) && !exitActions.Any(a =>
        {
            var (name, _) = FlowActionExecutor.ParseActionString(a);
            return name is "snapshot.restore" or "snapshot.restore_old" or "player.snapshot.restore";
        }))
        {
            issues.Add($"Mode '{modeId}' applies a kit on enter but not on exit. Players may retain event gear.");
        }
    }

    static void ValidateTeleportTargets(List<string> enterActions, ModeConfig config, string modeId, List<string> issues)
    {
        var teleportActions = enterActions
            .Select(a => FlowActionExecutor.ParseActionString(a))
            .Where(p => p.actionName is "teleport" or "player.teleport" or "teleport.position")
            .ToList();

        if (teleportActions.Count == 0)
        {
            issues.Add($"Mode '{modeId}' enter flow has no teleport action. Players may spawn outside the event zone.");
            return;
        }

        foreach (var (actionName, parameters) in teleportActions)
        {
            if (parameters.TryGetValue("position", out var pos) || parameters.TryGetValue("targetPosition", out pos))
            {
                var parts = pos.Split(',');
                if (parts.Length != 3 || !parts.All(p => float.TryParse(p, NumberStyles.Float, CultureInfo.InvariantCulture, out _)))
                {
                    issues.Add($"Action '{actionName}' has invalid 'position' parameter '{pos}'. Expected 'x,y,z' in mode '{modeId}'.");
                }
            }
            else if (parameters.TryGetValue("targetZoneHash", out var hashStr))
            {
                if (!int.TryParse(hashStr, NumberStyles.Integer, CultureInfo.InvariantCulture, out var zoneHash) || zoneHash <= 0)
                {
                    issues.Add($"Action '{actionName}' has invalid 'targetZoneHash' '{hashStr}' in mode '{modeId}'.");
                    continue;
                }

                var zone = config.Zones.Zones.FirstOrDefault(z => z.Hash == zoneHash);
                if (zone == null)
                {
                    issues.Add($"Action '{actionName}' references zoneHash {zoneHash} which does not exist in mode '{modeId}'.");
                }
                else if (string.IsNullOrWhiteSpace(zone.TeleportSpawn?.ToString()))
                {
                    issues.Add($"Action '{actionName}' references zoneHash {zoneHash} but that zone has no teleportSpawn position in mode '{modeId}'.");
                }
            }
            else
            {
                issues.Add($"Action '{actionName}' is missing teleport target. Expected 'targetZoneHash' or 'position' in mode '{modeId}'.");
            }
        }
    }

    static void ValidateNoDeprecatedSchematicLoad(List<string> actions, string modeId, List<string> issues, string phase = "enter")
    {
        foreach (var actionString in actions)
        {
            var (actionName, _) = FlowActionExecutor.ParseActionString(actionString);
            if (actionName is "schematic.load" or "schematic.loadat" or "schematic.loadatpos")
            {
                issues.Add($"Deprecated schematic action '{actionName}' found in phase '{phase}' for mode '{modeId}'. Remove it - zone geometry should not be spawned from session.json.");
            }
        }
    }

    static void ValidatePvpConsistency(List<string> enterActions, ModeConfig config, string modeId, List<string> issues)
    {
        var hasEnablePvp = enterActions.Any(a =>
        {
            var (name, _) = FlowActionExecutor.ParseActionString(a);
            return name is "enable_pvp" or "pvp.enable";
        });

        var hasDisablePvp = enterActions.Any(a =>
        {
            var (name, _) = FlowActionExecutor.ParseActionString(a);
            return name is "disable_pvp" or "pvp.disable";
        });

        if (hasEnablePvp && hasDisablePvp)
        {
            issues.Add($"Mode '{modeId}' enter flow has both enable_pvp and disable_pvp. These are contradictory.");
        }

        if (config.Rules.EnablePvP && !hasEnablePvp && !hasDisablePvp)
        {
            issues.Add($"Mode '{modeId}' has enablePvP=true in rules but enter flow does not explicitly enable PvP.");
        }

        if (!config.Rules.EnablePvP && hasEnablePvp)
        {
            issues.Add($"Mode '{modeId}' has enablePvP=false in rules but enter flow enables PvP explicitly.");
        }
    }

    static void ValidateBloodTypeConsistency(List<string> enterActions, string modeId, List<string> issues)
    {
        var bloodTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var actionString in enterActions)
        {
            var (actionName, parameters) = FlowActionExecutor.ParseActionString(actionString);
            var normalized = ActionRegistryValidator.NormalizeActionName(actionName);
            if (normalized is "blood.change" or "set_blood" or "blood.set")
            {
                if (parameters.TryGetValue("bloodType", out var bloodType))
                {
                    if (!bloodTypes.Add(bloodType))
                    {
                        issues.Add($"Mode '{modeId}' enter flow sets blood type multiple times to '{bloodType}'. Remove duplicates.");
                    }
                }
            }
        }
    }

    static void CheckZoneHashReferences(List<string> enterActions, ModeConfig config, string modeId, List<string> issues)
    {
        var knownHashes = new HashSet<int>(config.Zones.Zones.Select(z => z.Hash));

        foreach (var actionString in enterActions)
        {
            var (actionName, parameters) = FlowActionExecutor.ParseActionString(actionString);

            if (parameters.TryGetValue("zoneHash", out var zoneHashStr))
            {
                if (int.TryParse(zoneHashStr, NumberStyles.Integer, CultureInfo.InvariantCulture, out var zHash))
                {
                    if (!knownHashes.Contains(zHash))
                        issues.Add($"Action '{actionName}' references zoneHash={zHash} which does not exist in mode '{modeId}'.");
                }
                else
                {
                    issues.Add($"Action '{actionName}' has non-numeric zoneHash '{zoneHashStr}' in mode '{modeId}'.");
                }
            }

            if (parameters.TryGetValue("targetZoneHash", out var targetZoneHashStr))
            {
                if (int.TryParse(targetZoneHashStr, NumberStyles.Integer, CultureInfo.InvariantCulture, out var tzHash))
                {
                    if (!knownHashes.Contains(tzHash))
                        issues.Add($"Action '{actionName}' references targetZoneHash={tzHash} which does not exist in mode '{modeId}'.");
                }
                else
                {
                    issues.Add($"Action '{actionName}' has non-numeric targetZoneHash '{targetZoneHashStr}' in mode '{modeId}'.");
                }
            }
        }
    }

    static string? ExtractFirstAction(List<string> actions, string actionName)
    {
        return actions
            .Select(a => FlowActionExecutor.ParseActionString(a))
            .FirstOrDefault(p => string.Equals(p.actionName, actionName, StringComparison.OrdinalIgnoreCase))
            .actionName;
    }
}
