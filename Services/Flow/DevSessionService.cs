using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using BattleLuck.Models;
using BattleLuck.Utilities;
using BattleLuck.Services.Flow;
using ProjectM;
using Stunlock.Core;
using Unity.Entities;
using Unity.Mathematics;

namespace BattleLuck.Services;

/// <summary>
/// Manages dev mode sessions — isolated sandbox with full action access.
/// Players enter with .dev.enter and exit with .dev.exit.
/// </summary>
public sealed class DevSessionService
{
    /// <summary>Special zone hash identifying the dev arena.</summary>
    public const int DevZoneHash = -999;

    /// <summary>Center of the dev arena.</summary>
    public static readonly float3 DevArenaCenter = new(-3000f, 5f, -3000f);

    /// <summary>Radius of the dev arena (for border wall, etc.).</summary>
    public const float DevArenaRadius = 40f;

    /// <summary>Timeout for dev sessions (30 minutes).</summary>
    public static readonly TimeSpan SessionTimeout = TimeSpan.FromMinutes(30);

    readonly PlayerStateController _playerState;
    readonly FlowController _flow;

    readonly Dictionary<ulong, DateTime> _activeSessions = new();
    readonly Dictionary<ulong, List<Entity>> _spawnedEntities = new();
    readonly object _lock = new();

    public bool IsDevSession(ulong steamId)
    {
        lock (_lock)
        {
            return _activeSessions.ContainsKey(steamId);
        }
    }

    public IReadOnlyCollection<ulong> ActiveSessionIds
    {
        get
        {
            lock (_lock)
            {
                return _activeSessions.Keys.ToList().AsReadOnly();
            }
        }
    }

    public DevSessionService(PlayerStateController playerState, FlowController flow)
    {
        _playerState = playerState;
        _flow = flow;
    }

    /// <summary>
    /// Enter dev mode: save snapshot, teleport to dev arena, grant full access.
    /// </summary>
    public OperationResult EnterDevMode(Entity player, ulong steamId)
    {
        lock (_lock)
        {
            if (_activeSessions.ContainsKey(steamId))
                return OperationResult.Ok(); // Already in dev mode

            // Save current state
            _playerState.SaveSnapshot(player, 0);

            // Teleport to dev arena center
            player.SetPosition(DevArenaCenter);

            // Track session
            _activeSessions[steamId] = DateTime.UtcNow;
            _spawnedEntities[steamId] = new List<Entity>();

            BattleLuckLogger.Info($"[DevSession] Player {steamId} entered dev mode at {DevArenaCenter}");

            return OperationResult.Ok();
        }
    }

    /// <summary>
    /// Exit dev mode: cleanup spawned entities, restore snapshot, teleport back.
    /// </summary>
    public OperationResult ExitDevMode(Entity player, ulong steamId)
    {
        lock (_lock)
        {
            if (!_activeSessions.Remove(steamId))
                return OperationResult.Fail("You are not in dev mode.");

            // Cleanup spawned entities
            if (_spawnedEntities.TryGetValue(steamId, out var entities))
            {
                foreach (var entity in entities)
                {
                    try
                    {
                        if (entity.Exists())
                            entity.Destroy();
                    }
                    catch { }
                }
                entities.Clear();
                _spawnedEntities.Remove(steamId);
            }

            // Restore snapshot
            _playerState.RestoreSnapshot(player, 0);

            BattleLuckLogger.Info($"[DevSession] Player {steamId} exited dev mode");

            return OperationResult.Ok();
        }
    }

    /// <summary>
    /// Execute an action in dev mode context.
    /// Supports both string format (teleport:position=100|0|200) and JSON format.
    /// </summary>
    public OperationResult ExecuteDevAction(Entity player, ulong steamId, string action)
    {
        lock (_lock)
        {
            if (!_activeSessions.ContainsKey(steamId))
                return OperationResult.Fail("You must enter dev mode first. Use .dev.enter");

            // Refresh timeout
            _activeSessions[steamId] = DateTime.UtcNow;
        }

        try
        {
            if (IsUnsafeDevAction(action, out var reason))
                return OperationResult.Fail(reason);

            var context = new FlowActionContext
            {
                PlayerCharacter = player,
                ZoneHash = DevZoneHash,
                PlayerState = _playerState,
                GameContext = new GameModeContext { SessionId = $"dev_{steamId}" },
            };

            var executor = new FlowActionExecutor(_playerState);
            var result = ExecuteAction(action, player, executor, context);

            if (result.Success)
            {
                BattleLuckLogger.Info($"[DevSession] Action '{action}' OK for {steamId}");
            }
            else
            {
                BattleLuckLogger.Error($"[DevSession] Action '{action}' failed for {steamId}: {result.Error}");
            }

            return result;
        }
        catch (Exception ex)
        {
            BattleLuckLogger.Error($"[DevSession] ExecuteDevAction error for {steamId}: {ex.Message}");
            return OperationResult.Fail($"Dev action failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Execute action supporting both string and JSON formats.
    /// JSON format: {"action":"EntityCreate","prefabName":"CHAR_Hero","position":{"x":100,"y":0,"z":200}}
    /// String format: teleport:position=100|0|200
    /// </summary>
    private static OperationResult ExecuteAction(string action, Entity player, FlowActionExecutor executor, FlowActionContext context)
    {
        if (string.IsNullOrWhiteSpace(action))
            return OperationResult.Fail("Empty action.");

        // Try JSON format first
        if (action.TrimStart().StartsWith("{"))
        {
            return ExecuteJsonAction(action, player, executor, context);
        }

        // Fall back to string format
        return executor.Execute(action, context);
    }

    private static OperationResult ExecuteJsonAction(string json, Entity player, FlowActionExecutor executor, FlowActionContext context)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (!root.TryGetProperty("action", out var actionProp))
                return OperationResult.Fail("JSON action missing 'action' field.");

            var jsonActionName = actionProp.GetString();
            if (string.IsNullOrWhiteSpace(jsonActionName))
                return OperationResult.Fail("JSON action has empty action name.");

            // Map JSON action name to string action name
            var actionName = MapJsonActionName(jsonActionName);

            var parameters = JsonToParameters(root, jsonActionName);
            var actionString = BuildActionString(actionName, parameters);

            return executor.Execute(actionString, context);
        }
        catch (JsonException ex)
        {
            return OperationResult.Fail($"Invalid JSON: {ex.Message}");
        }
        catch (Exception ex)
        {
            return OperationResult.Fail($"JSON action error: {ex.Message}");
        }
    }

    private static string MapJsonActionName(string jsonName)
    {
        // Convert JSON action names to string action names
        return jsonName.ToLowerInvariant() switch
        {
            "snapshotsave" or "snapshot.save" => "snapshot.save",
            "snapshotrestore" or "snapshot.restore" => "snapshot.restore",
            "kitapply" or "kit.apply" => "kit.apply",
            "teleportplayer" or "teleport" => "teleport.position",
            "entitycreate" => "npc.spawn",
            "entitydestroy" => "wall.destroy", // Uses similar tracking/destruction logic
            "componentadd" => "player.buff.apply", // Map to buff apply as fallback
            "transformsetposition" => "teleport.position",
            "healthmodify" => "entity.damage", // Will need to negate for heal
            "healthset" => "entity.heal",
            "componentdestroy" => "player.buff.remove",
            "buffaddstack" => "player.buff.apply",
            "heal" => "heal",
            "buffclear" or "buff.clear" => "buff.clear_all",
            "inventorysend" or "inventory.send" => "inventory.send",
            "inventoryclearkit" or "inventory.clear_kit" => "inventory.clear_kit",
            "enablepvp" => "enable_pvp",
            "disablepvp" => "disable_pvp",
            "setblood" or "bloodchange" => "blood.change",
            "playerstun" => "player.stun",
            "spawnwave" => "spawn.wave",
            "spawnboss" or "bossspawn" => "spawn.boss",
            "spawnstructure" or "tileplace" => "structure.spawn",
            "npcspawn" or "spawnnpc" => "npc.spawn",
            "npcfollow" => "npc.follow",
            "npcaggro" => "npc.aggro",
            "npcgoto" => "npc.goto",
            "npcgotopos" => "npc.goto.pos",
            "npchold" => "npc.hold",
            "npcrelease" => "npc.release",
            "npcspeed" => "npc.speed",
            "npcrename" => "npc.rename",
            "abilitysetslot" => "ability.set_slot",
            "modestart" => "mode.start",
            "modeend" => "mode.end",
            "dooropen" => "door.open",
            "doorclose" => "door.close",
            "doorlock" => "door.lock",
            "doorunlock" => "door.unlock",
            "bossfollowtarget" or "bossfollow" => "boss.follow_target",
            "bossclearfollow" => "boss.clear_follow",
            "trapplace" => "trap.place",
            "traptrigger" => "trap.trigger",
            "trapremove" => "trap.remove",
            "mountsummon" => "mount.summon",
            "mountdismiss" => "mount.dismiss",
            "zonebuffapply" => "zone.buff.apply",
            "zonebuffremove" => "zone.buff.remove",
            "playerbuffapply" => "player.buff.apply",
            "playerbuffremove" => "player.buff.remove",
            "wallbuild" => "wall.build",
            "walldestroy" => "wall.destroy",
            "floorplace" => "floor.place",
            "buildspawn" => "build.spawn",
            "buildsearch" => "build.search",
            "sequenceplay" => "sequence.play",
            "sequencepersist" => "sequence.persist",
            "sequencestop" => "sequence.stop",
            "glowenable" => "glow.enable",
            "glowdisable" => "glow.disable",
            "revivegrant" => "revive.grant",
            "reviveconsume" => "revive.consume",
            "revivereset" => "revive.reset",
            "objectivecapture" => "objective.capture",
            "objectivecomplete" => "objective.complete",
            "objectivereset" => "objective.reset",
            "shrinkzone" => "shrink.zone",
            "shrinkzonestop" => "shrink.stop",
            "playerdowngrade" => "player.downgrade",
            "playerupgrade" => "player.upgrade",
            "equiprestrict" => "equip.restrict",
            "equipunrestrict" => "equip.unrestrict",
            "autotrashclear" => "autotrash.clear",
            "autotrashset" => "autotrash.set",
            "aibossaggro" => "ai.boss.aggro",
            "aibossdeaggro" => "ai.boss.deaggro",
            "aisetbehavior" => "ai.set_behavior",
            "aispawngroup" => "ai.spawn_group",
            "entitydamage" => "entity.damage",
            "entityheal" => "entity.heal",
            "timerstart" => "timer.start",
            "timerstop" => "timer.stop",
            "scoreadd" => "score.add",
            "scorereset" => "score.reset",
            "conditioncheck" => "condition.check",
            "pointset" => "point.set",
            "pointremove" => "point.remove",
            "factionset" => "faction.set",
            "factionclear" => "faction.clear",
            "deathprevent" => "death.prevent",
            "deathallow" => "death.allow",
"zoneborderplace" => "zone.border.place",
             "zoneborderremove" => "zone.border.remove",
             "zoneborderplaceall" => "zone.border.place_all",
             "zoneborderremoveall" => "zone.border.remove_all",
            "autoteleport" => "auto.teleport",
            "autofly" => "auto.fly",
            "schematicload" => "schematic.load",
            "schematicloadat" => "schematic.loadat",
            "schematicloadatpos" => "schematic.loadatpos",
            "schematicclear" => "schematic.clear",
            "schematicclearradius" => "schematic.clear.radius",
            "schematicdestroyradius" => "schematic.destroy.radius",
            _ => jsonName
        };
    }

    private static Dictionary<string, string> JsonToParameters(JsonElement root, string jsonActionName)
    {
        var parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var prop in root.EnumerateObject())
        {
            if (prop.NameEquals("action"))
                continue;

            var value = prop.Value;
            if (value.ValueKind == JsonValueKind.Object)
            {
                // Handle quat rotations: {"x":0,"y":0,"z":0,"w":1}
                if (value.TryGetProperty("x", out var x) && value.TryGetProperty("y", out var y) && value.TryGetProperty("z", out var z) && value.TryGetProperty("w", out var w))
                {
                    parameters[prop.Name] = $"{x.GetSingle()},{y.GetSingle()},{z.GetSingle()},{w.GetSingle()}";
                }
                // Handle float3 positions: {"x":100,"y":0,"z":200}
                else if (value.TryGetProperty("x", out x) && value.TryGetProperty("y", out y) && value.TryGetProperty("z", out z))
                {
                    parameters[prop.Name] = $"{x.GetSingle()} {y.GetSingle()} {z.GetSingle()}";
                }
                else
                {
                    parameters[prop.Name] = value.GetRawText();
                }
            }
            else if (value.ValueKind == JsonValueKind.Number)
            {
                if (value.TryGetInt32(out var intVal))
                    parameters[prop.Name] = intVal.ToString();
                else if (value.TryGetSingle(out var floatVal))
                    parameters[prop.Name] = floatVal.ToString(System.Globalization.CultureInfo.InvariantCulture);
            }
            else if (value.ValueKind == JsonValueKind.True || value.ValueKind == JsonValueKind.False)
            {
                parameters[prop.Name] = value.GetBoolean().ToString().ToLowerInvariant();
            }
            else if (value.ValueKind == JsonValueKind.String)
            {
                parameters[prop.Name] = value.GetString() ?? string.Empty;
            }
        }

        // Map JSON field names to parameter names expected by string format
        var mapped = MapParameterNames(parameters, jsonActionName);
        return mapped;
    }

    private static Dictionary<string, string> MapParameterNames(Dictionary<string, string> parameters, string jsonActionName)
    {
        var mapped = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var kvp in parameters)
        {
            var paramName = kvp.Key;
            var value = kvp.Value;

            // Map JSON field names (camelCase) to string parameter names (lowercase)
            var targetName = paramName.ToLowerInvariant() switch
            {
                "prefabname" => "prefab",
                "buffprefab" => "buffprefab",
                "abilityprefab" => "abilityprefab",
                "targetposition" => "position",
                "targetentity" => "targetentity",
                "traptype" => "trapname",
                "targetzonehash" => "zonehash",
                "durationseconds" => "duration",
                "maxlives" => "count",
                "rewardpoints" => "points",
                "sequenceprefab" => "sequencePrefab",
                "markerid" => "markerId",
                "objectiveid" => "objectiveId",
                "timerid" => "timerId",
                "aifollow" => "aiFollow",
                "aicontrolled" => "aiControlled",
                _ => paramName
            };

            mapped[targetName] = value;
        }

        return mapped;
    }

    private static string BuildActionString(string actionName, Dictionary<string, string> parameters)
    {
        if (parameters.Count == 0)
            return actionName;

        var paramStr = string.Join("|", parameters.Select(kvp => $"{kvp.Key}={kvp.Value}"));
        return $"{actionName}:{paramStr}";
    }

    /// <summary>
    /// Track a spawned entity for cleanup on exit.
    /// </summary>
    public void TrackSpawnedEntity(ulong steamId, Entity entity)
    {
        lock (_lock)
        {
            if (_spawnedEntities.TryGetValue(steamId, out var entities))
            {
                entities.Add(entity);
            }
        }
    }

    /// <summary>
    /// Execute all supported actions in dev mode context.
    /// Used for testing/debugging — runs each action with minimal defaults.
    /// </summary>
    public List<OperationResult> ExecuteAllActions(Entity player, ulong steamId)
    {
        var results = new List<OperationResult>();
        
        lock (_lock)
        {
            if (!_activeSessions.ContainsKey(steamId))
            {
                results.Add(OperationResult.Fail("You must enter dev mode first. Use .dev.enter"));
                return results;
            }
            _activeSessions[steamId] = DateTime.UtcNow;
        }

        var context = new FlowActionContext
        {
            PlayerCharacter = player,
            ZoneHash = DevZoneHash,
            PlayerState = _playerState,
            GameContext = new GameModeContext { SessionId = $"dev_{steamId}" },
        };

        var executor = new FlowActionExecutor(_playerState);

        var samples = LoadCatalogSamples();
        foreach (var actionName in FlowActionExecutor.SupportedActions)
        {
            try
            {
                var sampleAction = ResolveDevSample(actionName, samples);
                var result = executor.Execute(sampleAction, context);
                results.Add(result);
            }
            catch (Exception ex)
            {
                var failResult = OperationResult.Fail($"Dev action failed: {ex.Message}");
                results.Add(failResult);
            }
        }

        return results;
    }

    /// <summary>
    /// Execute an effective enter/exit flow inside the isolated dev-session context.
    /// This deliberately uses the flow runner rather than publishing zone/session events.
    /// </summary>
    public OperationResult ExecuteDevFlow(Entity player, ulong steamId, string modeId, string flowType)
    {
        lock (_lock)
        {
            if (!_activeSessions.ContainsKey(steamId))
                return OperationResult.Fail("You must enter dev mode first. Use .dev.enter");

            _activeSessions[steamId] = DateTime.UtcNow;
        }

        if (string.IsNullOrWhiteSpace(modeId))
            return OperationResult.Fail("Mode id is required.");

        var parsedFlowType = flowType.Equals("enter", StringComparison.OrdinalIgnoreCase)
            ? FlowType.Enter
            : flowType.Equals("exit", StringComparison.OrdinalIgnoreCase)
                ? FlowType.Exit
                : (FlowType?)null;
        if (parsedFlowType == null)
            return OperationResult.Fail("Flow type must be 'enter' or 'exit'.");

        try
        {
            var config = ConfigLoader.Load(modeId);
            var flow = FlowOverrideManager.Instance.GetEffectiveFlow(modeId, parsedFlowType.Value);
            var zone = new ZoneDefinition
            {
                Name = $"dev:{modeId}",
                Type = "dev",
                Hash = DevZoneHash,
                Center = new Vec3Config { X = DevArenaCenter.x, Y = DevArenaCenter.y, Z = DevArenaCenter.z },
                Radius = DevArenaRadius,
                ExitRadius = DevArenaRadius
            };
            var context = new GameModeContext { SessionId = $"dev_{steamId}_{modeId}_{flowType.ToLowerInvariant()}" };
            return _flow.ExecuteFlow(flow, config, player, zone, context, rollbackOnFailure: true);
        }
        catch (Exception ex)
        {
            BattleLuckLogger.Error($"[DevSession] Flow test {modeId}/{flowType} failed for {steamId}: {ex.Message}");
            return OperationResult.Fail($"Dev flow failed: {ex.Message}");
        }
    }

    static bool IsUnsafeDevAction(string action, out string reason)
    {
        reason = "";
        var name = action.Split(':', 2)[0].Trim().ToLowerInvariant();
        var lower = action.ToLowerInvariant();

        if (name is "boss.add_servant" or "boss.command_servants" or "boss.spawn_servants" or
            "shrink.zone" or "entity.spawn" or "spawn.wave")
        {
            reason = $"Dev action '{name}' is disabled on live servers because it can destabilize the process.";
            return true;
        }

        if (lower.Contains("realcastle=true") || lower.Contains("roommode=castle") || lower.Contains("ownedcastle=true"))
        {
            reason = "Real castle build actions are disabled in dev mode on live servers.";
            return true;
        }

        return false;
    }

    static Dictionary<string, string> LoadCatalogSamples()
    {
        var samples = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var path = Path.Combine(ConfigLoader.ConfigRoot, "actions_catalog.json");
            if (!File.Exists(path))
                return samples;

            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            if (!doc.RootElement.TryGetProperty("examples", out var examples) || examples.ValueKind != JsonValueKind.Object)
                return samples;

            foreach (var category in examples.EnumerateObject())
            {
                if (category.Value.ValueKind != JsonValueKind.Array)
                    continue;

                foreach (var item in category.Value.EnumerateArray())
                {
                    var action = item.GetString();
                    if (string.IsNullOrWhiteSpace(action))
                        continue;

                    var name = action.Split(':', 2)[0].Trim();
                    if (!samples.ContainsKey(name))
                        samples[name] = action;
                }
            }
        }
        catch (Exception ex)
        {
            BattleLuckLogger.Warning($"[DevSession] Could not load action samples: {ex.Message}");
        }

        return samples;
    }

    static string ResolveDevSample(string actionName, Dictionary<string, string> samples)
    {
        if (samples.TryGetValue(actionName, out var sample))
            return NormalizeDevSample(sample);

        return actionName switch
        {
            "inventory.send" => "inventory.send:itemId=-257494203|amount=1",
            "ability.set_slot" => "ability.set_slot:slot=5|abilityPrefab=AB_Blood_Shadowbolt",
            "player.buff.apply" => "player.buff.apply:buffPrefab=Buff_General_Slow|duration=2",
            "player.buff.remove" => "player.buff.remove:buffPrefab=Buff_General_Slow",
            "zone.buff.apply" => "zone.buff.apply:buffPrefab=Buff_General_Slow|duration=2",
            "zone.buff.remove" => "zone.buff.remove:buffPrefab=Buff_General_Slow",
            "buff.apply" => "buff.apply:buff=Buff_General_Slow|duration=2",
            "buff.remove" => "buff.remove:buff=Buff_General_Slow",
            "prefab.query" => "prefab.query:prefab=Buff_General_Slow",
            "prefab.grant" => "prefab.grant:prefab=Item_Resource_Stone|amount=1",
            "prefab.spawn" => "prefab.spawn:prefab=TM_Castle_Floor_Tier02_Stone|position=-3000,5,-3000|roomMode=visual",
            "structure.spawn" => "structure.spawn:prefab=TM_Castle_Floor_Tier02_Stone|position=-3000,5,-3000|roomMode=visual",
            "tile.place" => "tile.place:prefab=TM_Castle_Floor_Tier02_Stone|position=-3000,5,-3000|roomMode=visual",
            "floor.place" => "floor.place:floorType=TM_Castle_Floor_Tier02_Stone|position=-3000,5,-3000|roomMode=visual",
            "build.search" => "build.search:filter=castle floor",
            "build.spawn" => "build.spawn:prefab=TM_Castle_Floor_Tier02_Stone|position=-3000,5,-3000|group=dev_build",
            "wall.build" => "wall.build:wallType=TM_Castle_Wall_Tier02_Stone|position=-3000,5,-3000",
            "teleport.position" => "teleport.position:position=-3000,5,-3000",
            "point.set" => "point.set:pointId=dev",
            "point.remove" => "point.remove:pointId=dev",
            "objective.capture" => "objective.capture:objectiveId=dev|teamId=1",
            "objective.complete" => "objective.complete:objectiveId=dev|rewardPoints=1",
            "timer.start" => "timer.start:timerId=dev|duration=2",
            "timer.stop" => "timer.stop:timerId=dev",
            "npc.spawn" or "spawn.npc" => "npc.spawn:prefab=CHAR_Undead_SkeletonSoldier_Armored_Dunley|count=1|npcId=dev_npc",
            "npc.follow" => "npc.follow:npcId=dev_npc|target=self",
            "npc.aggro" => "npc.aggro:npcId=dev_npc|target=self",
            "npc.goto" or "npc.goto.pos" => "npc.goto:npcId=dev_npc|position=-3000,5,-3000",
            "npc.hold" or "npc.stay" => "npc.hold:npcId=dev_npc|radius=5",
            "npc.release" => "npc.release:npcId=dev_npc",
            "npc.speed" => "npc.speed:npcId=dev_npc|speed=9",
            "npc.rename" => "npc.rename:npcId=dev_npc|name=Dev NPC",
            "npc.despawn" => "npc.despawn:npcId=dev_npc",
            "schematic.load" => "schematic.load:eventName=castle_design_template|radius=20|clearOld=true|spawnItems=false",
            "schematic.loadat" => "schematic.loadat:eventName=castle_design_template|position=-3000,5,-3000|clearRadius=0|spawnItems=false",
            "schematic.loadatpos" => "schematic.loadatpos:eventName=castle_design_template|position=-3000,5,-3000|clearRadius=0|spawnItems=false",
            "schematic.clear" => "schematic.clear:eventName=castle_design_template",
            "schematic.clear.radius" => "schematic.clear.radius:position=-3000,5,-3000|radius=20",
            "schematic.destroy.radius" => "schematic.destroy.radius:position=-3000,5,-3000|radius=20|includeItems=false",
            "blood.change" or "blood.set" or "set_blood" => "blood.change:bloodType=Warrior|quality=100",
            _ => actionName
        };
    }

    static string NormalizeDevSample(string sample)
    {
        return sample
            .Replace("5000,0,5000", "-2000,5,-2800", StringComparison.OrdinalIgnoreCase)
            .Replace("-2000,5,-2800", "-3000,5,-3000", StringComparison.OrdinalIgnoreCase)
            .Replace("duration=300", "duration=2", StringComparison.OrdinalIgnoreCase)
            .Replace("duration=120", "duration=2", StringComparison.OrdinalIgnoreCase)
            .Replace("duration=60", "duration=2", StringComparison.OrdinalIgnoreCase)
            .Replace("durationSeconds=30", "durationSeconds=2", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Check and cleanup expired dev sessions.
    /// </summary>
    public void Tick()
    {
        lock (_lock)
        {
            var now = DateTime.UtcNow;
            var expired = _activeSessions
                .Where(kvp => now - kvp.Value > SessionTimeout)
                .Select(kvp => kvp.Key)
                .ToList();

            foreach (var steamId in expired)
            {
                _activeSessions.Remove(steamId);
                _spawnedEntities.Remove(steamId);
                BattleLuckLogger.Warning($"[DevSession] Session for {steamId} expired (timeout {SessionTimeout.TotalMinutes}min)");
            }
        }
    }
}
