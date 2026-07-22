using BattleLuck.Models;
using BattleLuck.Utilities;
using BattleLuck.ECS.Actions.Components;
using BattleLuck.Commands.Converters;
using BattleLuck.Services.Runtime;
using BattleLuck.Services.AI;
using BattleLuck.Services.Npc;
using BattleLuck.Services.Logistics;
using BattleLuck.Services.Progression;
using ProjectM;
using Stunlock.Core;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Collections;
using System.Text.Json;
using System.Diagnostics;
using System.Linq;
using JetBrains.Annotations;
using ServantType = BattleLuck.Models.ServantType;
using ServantFaction = BattleLuck.Models.ServantFaction;
using ServantCommand = BattleLuck.Models.ServantCommand;
using ServantFormation = BattleLuck.Models.ServantFormation;

namespace BattleLuck.Services.Flow
{

    public sealed class FlowActionContext
    {
        public Entity PlayerCharacter { get; init; }
        public int ZoneHash { get; init; }
        public PlayerStateController PlayerState { get; init; } = null!;
        public GameModeRegistry? Registry { get; init; }
        public ModeConfig? Config { get; init; }
        public ZoneDefinition? Zone { get; init; }
        public GameModeContext? GameContext { get; init; }
        public string ModeId => Config?.Rules?.ModeId ?? GameContext?.ModeId ?? "";
    }

public sealed class FlowActionExecutor : IActionRuntime
    {
    static readonly StringComparer KeyComparer = StringComparer.OrdinalIgnoreCase;
    static readonly bool EventGeometryMutationsEnabled = true;
    static readonly bool EventVisualFloorMutationsEnabled = true;
    static readonly bool EventBuildFreeActionsEnabled = true;
    static readonly bool EventBossServantMutationsEnabled = true;
    static readonly HashSet<string> StrictlyBlockedNativeMutations = new(StringComparer.OrdinalIgnoreCase)
    {
        "tech.apply", "progression.unlock.all_vbloods", "progression.unlock.all_research",
        "progression.unlock_gear", "progression.set_tier"
    };
    public static readonly HashSet<string> NonBlockingEnterActions = new(StringComparer.OrdinalIgnoreCase)
    {
        "announce",
        "send_message",
        "notify",
        "notification",
        "visual.set_state",
        "progression.unlock.all_vbloods",
        "progression.unlock.all_research",
        "system.find",
        "system.search"
    };
    readonly PlayerStateController _playerState;
    readonly GameModeRegistry? _registry;

    public FlowActionExecutor(PlayerStateController playerState, GameModeRegistry? registry = null)
    {
        _playerState = playerState;
        _registry = registry;
    }

    // All supported action names — loaded exclusively from actions_catalog.json at runtime.
    // To add a new action: register it in actions_catalog.json under the appropriate category,
    // then add a case to ExecuteParsed. No hardcoded list here.
    static List<string>? _actionNames;

    public static IReadOnlyCollection<string> SupportedActions
    {
        get
        {
            if (_actionNames == null) ReloadActionNames();
            return _actionNames!;
        }
    }

    public static ActionManifestService Registry { get; } = ActionManifestService.Instance;

    /// <summary>
    /// Shared singleton for command callers that don't need a dedicated PlayerStateController.
    /// Uses a default PlayerStateController; commands that create their own state should
    /// pass it via FlowActionContext.PlayerState.
    /// </summary>
    public static FlowActionExecutor Shared { get; } = new(new PlayerStateController(), BattleLuckPlugin.GameModes);

    private static readonly ActionLogger _actionLogger = new("FlowActionExecutor");
    public static ActionLogger Logger => _actionLogger;

    /// <summary>Reload action name list from actions_catalog.json. Safe to call at runtime.</summary>
    public static void ReloadActionNames()
    {
        var names = new List<string>();
        try
        {
            var path = System.IO.Path.Combine(ConfigLoader.ConfigRoot, "actions_catalog.json");
            if (!System.IO.File.Exists(path))
            {
                BattleLuckLogger.Warning("[FlowActionExecutor] actions_catalog.json not found — no actions registered.");
                _actionNames = names;
                return;
            }
            var json = System.IO.File.ReadAllText(path);
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("registered", out var registered))
            {
                foreach (var entry in registered.EnumerateArray())
                {
                    var name = entry.GetString();
                    if (!string.IsNullOrWhiteSpace(name))
                        names.Add(name!);
                }
            }
        }
        catch (Exception ex)
        {
            BattleLuckLogger.Warning($"[FlowActionExecutor] Failed to load actions_catalog.json: {ex.Message}");
        }
        _actionNames = names;
        BattleLuckPlugin.LogInfo($"[FlowActionExecutor] Loaded {_actionNames.Count} registered actions from catalog.");
        // ActionManifestService loads catalog data in its constructor.
    }

    /// <summary>Load the runtime_inject list for a specific event — hot-reloaded each tick check.</summary>
    public static List<string> GetRuntimeInject(string eventId)
    {
        var actions = new List<string>();
        try
        {
            var path = System.IO.Path.Combine(ConfigLoader.ConfigRoot, "actions_catalog.json");
            if (!System.IO.File.Exists(path)) return actions;
            var json = System.IO.File.ReadAllText(path);
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("runtime_inject", out var inject)
                && inject.TryGetProperty(eventId, out var eventActions)
                && eventActions.ValueKind == JsonValueKind.Array)
            {
                foreach (var entry in eventActions.EnumerateArray())
                {
                    var a = entry.GetString();
                    if (!string.IsNullOrWhiteSpace(a)) actions.Add(a!);
                }
            }
        }
        catch (Exception ex)
        {
            BattleLuckLogger.Warning($"[FlowActionExecutor] Failed to read runtime_inject for '{eventId}': {ex.Message}");
        }
        return actions;
    }

    public OperationResult Execute(string actionString, FlowActionContext context)
    {
        if (!context.PlayerCharacter.Exists())
        {
            _actionLogger.LogError("Execute", "Player entity does not exist");
            return OperationResult.FailWithHelp(
                "Player entity does not exist.",
                "Entity not found",
                "Reconnect to the server to restore your character entity.");
        }

        var stopwatch = Stopwatch.StartNew();
        var (actionName, parameters) = ParseActionString(actionString);
        _actionLogger.LogParse(actionString, actionName, parameters);

        if (Registry.TryGetAction(actionName, out var definition) && definition != null && !string.IsNullOrWhiteSpace(definition.Action))
        {
            _actionLogger.LogValidation(actionName, definition.Name ?? "", true);
            var resolved = ActionManifestService.BuildActionString(definition.Action, definition.Params);
            if (!string.IsNullOrWhiteSpace(resolved))
            {
                var (resolvedName, resolvedParams) = ParseActionString(resolved);
                (resolvedName, resolvedParams) = ActionParameterConverter.Normalize(resolvedName, resolvedParams);
                foreach (var kv in parameters)
                    resolvedParams[kv.Key] = kv.Value;
                actionName = resolvedName;
                parameters = resolvedParams;
            }
        }

        (actionName, parameters) = ActionParameterConverter.Normalize(actionName, parameters);
        if (string.IsNullOrWhiteSpace(actionName))
        {
            _actionLogger.LogError(actionName, "Empty action after normalization");
            return OperationResult.FailWithHelp(
                "Empty action.",
                "Invalid action",
                "Use a valid action string: actionName:key=value|key2=value2");
        }

        if (!IsRegisteredAction(actionName))
        {
            _actionLogger.LogValidation(actionName, "", false, "Action not registered in catalog");
            return OperationResult.FailWithHelp(
                $"Action '{actionName}' is not registered in actions_catalog.json.",
                "Action not cataloged",
                "Add the action to actions_catalog.json before using it in flows, events, AI, or live commands.");
        }

        if (StrictlyBlockedNativeMutations.Contains(actionName))
        {
            _actionLogger.LogValidation(actionName, "", false, "Strict server-stability profile");
            return OperationResult.FailWithHelp(
                $"Action '{actionName}' is disabled by the strict server-stability profile.",
                "Native mutation blocked",
                "Use logical zones, player state, scoring, announcements, and non-construction event actions only.");
        }

        _actionLogger.LogActionExecution(actionName, "", parameters, inProgress: true);

        try
        {
            stopwatch.Start();
            var result = ExecuteParsed(actionName, parameters, context);
            stopwatch.Stop();

            _actionLogger.LogActionResult(
                actionName,
                result.Success,
                result.Success ? "OK" : result.Error,
                result.Success ? null : result.Error,
                stopwatch.ElapsedMilliseconds);

            return result;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _actionLogger.LogError(actionName, ex.Message, ex);
            BattleLuckPlugin.LogError($"[FlowActionExecutor] {actionName} failed: {ex.Message}");
            return OperationResult.FailWithHelp(ex.Message, "Action execution error", $"Check the action '{actionName}' parameters are valid.");
        }
    }

    public OperationResult Execute(EventActionDefinition action, FlowActionContext context)
    {
        var actionString = action.ToActionString();
        if (string.IsNullOrWhiteSpace(actionString))
            return OperationResult.FailWithHelp(
                "Empty structured action.",
                "Invalid action",
                "Use either { \"action\": \"name:key=value\" } or { \"type\": \"name\", \"params\": { ... } }.");

        return Execute(actionString, context);
    }

    // ── IActionRuntime implementation ──────────────────────────────────────────
    // Bridges the canonical runtime pipeline (RuntimeActionIntent / RuntimeActionContext)
    // onto the existing string-based flow engine so every action request funnels
    // through the single IActionRuntime entry point.

    RuntimeActionReport IActionRuntime.Execute(RuntimeActionIntent intent, RuntimeActionContext context)
    {
        var flowContext = ToFlowContext(context);
        var result = Execute(FormatIntent(intent), flowContext);
        return ToReport(intent, result);
    }

    async Task<RuntimeActionReport> IActionRuntime.ExecuteAsync(
        RuntimeActionIntent intent,
        RuntimeActionContext context,
        CancellationToken cancellationToken)
    {
        // Execution is synchronous; satisfy the async contract via the thread pool.
        return await Task.Run(() => ((IActionRuntime)this).Execute(intent, context), cancellationToken)
            .ConfigureAwait(false);
    }

    async Task<RuntimeActionReport> IActionRuntime.ValidateOnlyAsync(
        RuntimeActionIntent intent,
        RuntimeActionContext context,
        CancellationToken cancellationToken)
    {
        return await Task.Run(() => ValidateOnly(intent, context), cancellationToken)
            .ConfigureAwait(false);
    }

    bool IActionRuntime.IsKnownAction(string actionName) => IsRegisteredAction(actionName);

    static string FormatIntent(RuntimeActionIntent intent)
    {
        if (intent.Parameters == null || intent.Parameters.Count == 0)
            return intent.ActionName;

        var parts = new List<string>(intent.Parameters.Count);
        foreach (var kvp in intent.Parameters)
            parts.Add($"{kvp.Key}={kvp.Value}");

        return $"{intent.ActionName}:{string.Join("|", parts)}";
    }

    static FlowActionContext ToFlowContext(RuntimeActionContext context)
    {
        return new FlowActionContext
        {
            PlayerCharacter = context.PlayerCharacter,
            ZoneHash = context.ZoneHash,
            PlayerState = context.PlayerState!,
            Registry = context.Registry,
            Config = context.ModeConfig,
            Zone = context.Zone,
            GameContext = context.SessionContext,
        };
    }

    static RuntimeActionReport ToReport(RuntimeActionIntent intent, OperationResult result)
    {
        return new RuntimeActionReport
        {
            Intent = intent,
            ExecutionStatus = result.Success ? ExecutionStatus.Succeeded : ExecutionStatus.Failed,
            Summary = result.Error,
            Error = result.Error,
        };
    }

    RuntimeActionReport ValidateOnly(RuntimeActionIntent intent, RuntimeActionContext context)
    {
        var flowContext = ToFlowContext(context);
        var (actionName, parameters) = ParseActionString(FormatIntent(intent));
        (actionName, parameters) = ActionParameterConverter.Normalize(actionName, parameters);

        if (string.IsNullOrWhiteSpace(actionName) || !IsRegisteredAction(actionName))
        {
            return new RuntimeActionReport
            {
                Intent = intent,
                ExecutionStatus = ExecutionStatus.DeniedByValidation,
                Validation = ValidationStatus.Rejected,
                Error = string.IsNullOrWhiteSpace(actionName)
                    ? "Empty action."
                    : $"Action '{actionName}' is not registered in actions_catalog.json.",
            };
        }

        return new RuntimeActionReport
        {
            Intent = intent,
            ExecutionStatus = ExecutionStatus.Skipped,
            Validation = ValidationStatus.Allowed,
            Summary = "Validation passed (action not executed).",
        };
    }

    // ── Canonical entry point for deprecated callers (commands) ────────────────────
    // Keeps the convenient (string action, FlowActionContext) signature that
    // commands already use, but funnels through IActionRuntime so every request
    // goes through the single pipeline. Behaviour is identical to
    // Execute(string, FlowActionContext).

    public OperationResult ExecuteViaRuntime(string actionString, FlowActionContext context)
    {
        var intent = new RuntimeActionIntent { ActionName = actionString };
        var report = ((IActionRuntime)this).Execute(intent, ToRuntimeContext(context));
        return report.IsSuccess
            ? OperationResult.Ok()
            : OperationResult.FailWithHelp(report.Error ?? "Action failed.", report.Summary ?? "Execution error", null);
    }

    static RuntimeActionContext ToRuntimeContext(FlowActionContext context)
    {
        return new RuntimeActionContext
        {
            PlayerCharacter = context.PlayerCharacter,
            ZoneHash = context.ZoneHash,
            PlayerState = context.PlayerState,
            Registry = context.Registry,
            ModeConfig = context.Config,
            Zone = context.Zone,
            SessionContext = context.GameContext,
        };
    }

    public OperationResult ExecuteFlow(FlowConfig flowConfig, FlowActionContext context, bool rollbackOnFailure)
    {
        if (flowConfig.ExecutionOrder.Count == 0 && flowConfig.Flows.Count == 0)
            return OperationResult.Ok();

        var order = flowConfig.ExecutionOrder!.Count > 0
            ? flowConfig.ExecutionOrder!
            : flowConfig.Flows!.Keys.ToList();

        var snapshotRestored = false;
        foreach (var flowName in order)
        {
            if (!flowConfig.Flows!.TryGetValue(flowName, out var flow))
                return OperationResult.FailWithHelp(
                    $"Flow '{flowName}' not found.",
                    "Flow not found",
                    $"Check that '{flowName}' exists in the session configuration.");

            var actions = flow.Actions ?? new List<string>();
            var actionCount = actions.Count;
            BattleLuckPlugin.LogInfo($"[FlowActionExecutor] Executing flow '{flowName}' with {actionCount} actions.");

            var actionIndex = 0;
            foreach (var action in actions)
            {
                actionIndex++;
                var (flowActionName, flowActionParams) = ParseActionString(action);
                (flowActionName, _) = ActionParameterConverter.Normalize(flowActionName, flowActionParams);
                if (snapshotRestored && IsPostRestoreLoadoutMutation(flowActionName))
                {
                    BattleLuckPlugin.LogWarning($"[FlowActionExecutor] [{flowName}] skipped post-restore loadout mutation '{action}' to preserve the original player snapshot.");
                    continue;
                }

                BattleLuckPlugin.LogInfo($"[FlowActionExecutor] [{flowName}] action {actionIndex}/{actionCount}: {action}");
                var result = Execute(action, context);
                if (result.Success)
                {
                    if (IsSnapshotRestoreAction(flowActionName))
                        snapshotRestored = true;
                    BattleLuckPlugin.LogInfo($"[FlowActionExecutor] [{flowName}] action {actionIndex}/{actionCount} succeeded.");
                    continue;
                }

                if (rollbackOnFailure && IsNonBlockingEnterAction(action))
                {
                    BattleLuckPlugin.LogWarning($"[FlowActionExecutor] [{flowName}] non-critical enter action failed and was skipped: {action} | {result.Error}");
                    continue;
                }

                if (!result.Success)
                {
                    if (rollbackOnFailure)
                        TryRestore(context.PlayerCharacter, context.ZoneHash, context);
                    return OperationResult.FailWithHelp(
                        $"Action '{action}' failed: {result.Error}",
                        "Flow action failed",
                        result.Troubleshooting);
                }
            }
        }

        return OperationResult.Ok();
    }

    static bool IsSnapshotRestoreAction(string actionName) =>
        actionName is "player.snapshot.restore" or "snapshot.restore" or "snapshot.restore_old";

    static bool IsPostRestoreLoadoutMutation(string actionName) =>
        actionName is "kit.apply" or "kit.apply_weapons" or "kit.apply_armor" or
            "inventory.clear_kit" or "inventory.clear_all" or
            "ability.set_slot" or
            "level.set_max" or "set_blood" or "blood.set" or "blood.change";

    static bool IsNonBlockingEnterAction(string actionString)
    {
        var (actionName, parameters) = ParseActionString(actionString);
        (actionName, _) = ActionParameterConverter.Normalize(actionName, parameters);
        return NonBlockingEnterActions.Contains(actionName);
    }

    private static List<Stunlock.Core.PrefabGUID> ParsePrefabList(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return new List<Stunlock.Core.PrefabGUID>();
        return input.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(s => int.TryParse(s, out var id) ? new Stunlock.Core.PrefabGUID(id) : (Stunlock.Core.PrefabGUID?)null)
            .Where(id => id.HasValue)
            .Select(id => id!.Value)
            .ToList();
    }

    OperationResult ExecuteParsed(string actionName, Dictionary<string, string> p, FlowActionContext c)
    {
        var player = c.PlayerCharacter;
        var ctx = c.GameContext;
        var zoneHash = Int(p, "zoneHash", c.ZoneHash);

        switch (actionName)
        {
            case "event.toggleenter":
                return ToggleEventEntry(player, p, c);
            case "team.cornerteleport":
                return TeamCornerTeleport(player, p, c);
            case "chest.spawn":
                return SpawnChest(player, p, c);
            case "team.swap":
                return SwapTeam(player, p, c);
            case "event.finalize":
                return FinalizeEvent(player, p, c);
            case "snapshot.save":
            case "snapshot.save_old":
                var saveState = c.PlayerState ?? _playerState;
                if (IsEntryPrepared(c))
                    saveState.SaveSnapshotIfMissing(player, zoneHash);
                else
                    saveState.SaveSnapshot(player, zoneHash);
                return OperationResult.Ok();
            case "player.snapshot.restore":
            case "snapshot.restore":
            case "snapshot.restore_old":
                return (c.PlayerState ?? _playerState).RestoreSnapshot(player, zoneHash)
                    ? OperationResult.Ok()
                    : OperationResult.Fail("No snapshot found.");
            case "snapshot.clear":
                (c.PlayerState ?? _playerState).ClearSnapshot(player.GetSteamId());
                return OperationResult.Ok();
            case "snapshot.mark_active":
                ctx?.Players.Add(player.GetSteamId());
                return OperationResult.Ok();
            case "snapshot.clear_active":
                ctx?.Players.Remove(player.GetSteamId());
                return OperationResult.Ok();
            case "state.clear_old":
            case "state.clear_zone":
                BattleLuckPlugin.LogInfo($"[FlowActionExecutor] {actionName} is compatibility no-op; snapshots are preserved for restore.");
                return OperationResult.Ok();

            case "kit.apply":
                return BattleLuckPlugin.PlayerLoadouts?.Apply(player, Text(p, "kitId", Text(p, "modeId", c.Config?.KitId ?? "bloodbath")))
                    ?? OperationResult.Fail("Player loadout service is not initialized.");
            case "kit.apply_weapons":
                KitController.ApplyWeaponsKit(player);
                return OperationResult.Ok();
            case "kit.apply_armor":
                KitController.ApplyArmorKit(player);
                return OperationResult.Ok();
            case "inventory.clear_kit":
                ClearKitItems(player, Text(p, "kitId", c.Config?.KitId ?? "bloodbath"));
                return OperationResult.Ok();
            case "level.set_max":
                KitController.SetMaxLevel(player);
                return OperationResult.Ok();
            case "heal":
                player.HealToFull();
                return OperationResult.Ok();
            case "buff.clear_all":
                ClearKnownBuffs(player);
                return OperationResult.Ok();
            case "ability.set_slot":
                return SetAbilitySlot(player, p);

            case "enable_pvp":
            case "pvp.enable":
                player.SetTeam(c.ZoneHash + (int)(player.GetSteamId() % 1000));
                return OperationResult.Ok();
            case "disable_pvp":
            case "pvp.disable":
                player.SetTeam(0);
                return OperationResult.Ok();
            case "team.autobalance":
            case "team.auto_assign":
                return c.GameContext != null
                    ? TeamBalancer.AssignTeams(c.GameContext)
                    : OperationResult.Fail("team.autobalance requires an active session context.");
            case "set_blood":
            case "blood.set":
            case "blood.change":
                return ChangeBlood(player, p, c);
            case "player.stun":
                return ApplyTimedBuff(player, Prefabs.Buff_General_Stun, EffectDuration(p, c, 3f, "durationSeconds", "duration"));

            case "teleport":
            case "player.teleport":
                return Teleport(player, p, c);
            case "teleport.position":
                if (!TryParseFloat3(Required(p, "position"), out var position))
                    return OperationResult.Fail("Invalid position.");
                return BattleLuckPlugin.Teleports?.Teleport(player, position)
                    ?? OperationResult.Fail("Teleport service is not initialized.");
            case "notify":
            case "send_message":
            case "notification":
            case "announce":
                return Announce(player, p, c);

            case "inventory.send":
                return SendInventory(player, p);

            case "spawn.wave":
            case "spawnwave":
                return SpawnWave(player, p, c);
            case "spawn.boss":
            case "boss.spawn":
                return SpawnBoss(player, p, c);
            case "npc.spawn":
            case "spawn.npc":
                return SpawnNpcGroup(player, p, c);
            case "npc.follow":
            case "npc.aggro":
            case "npc.goto":
            case "npc.goto.pos":
            case "npc.hold":
            case "npc.stay":
            case "npc.release":
            case "npc.despawn":
            case "npc.team":
            case "npc.faction":
            case "npc.speed":
            case "npc.rename":
            case "npc.patrol":
            case "npc.guard":
            case "npc.flee":
            case "npc.wander":
            case "npc.formation":
                return NpcControl(actionName, player, p, c);
            case "ai.boss.aggro":
                return NpcControl("npc.aggro", player, p, c);
            case "ai.boss.deaggro":
            case "boss.clear_follow":
                return NpcControl("npc.release", player, p, c);
            case "boss.follow":
            case "boss.follow_target":
                return NpcControl("npc.follow", player, p, c);
            case "boss.goto":
            case "boss.goto.pos":
                return NpcControl("npc.goto", player, p, c);
            case "boss.return_home":
                return ReturnNpcHome(player, p, c);
            case "ai.set_behavior":
                return SetNpcBehavior(player, p, c);
            case "ai.spawn_group":
            case "boss.add_servant":
            case "boss.spawn_servants":
                return SpawnNpcGroup(player, p, c);
            case "boss.remove_servant":
            case "boss.command_servants":
                return OperationResult.Fail($"{actionName} was removed; target controlled entities with npc.despawn/npc.* actions.");
            case "prefab.spawn":
                return SpawnStructure(player, p, c, "prefab", allowRealCastleGeometry: false);
            case "build.search":
                return BuildSearch(p);
            case "palette.add":
            case "palette.remove":
            case "palette.clear":
            case "palette.next":
            case "palette.prev":
            case "palette.list":
            case "palette.current":
                return Palette(actionName, player, p);
            case "prefab.grant":
                return ForEachTargetPlayer(player, c, p, target => GrantPrefab(target, p));
            case "prefab.query":
                return QueryPrefab(player, p);
            case "merchant.run":
                return MerchantRun(player, p);
            case "merchant.grant":
                return MerchantGrant(player, p);
            case "mount.summon":
                return SummonMount(player, p, c);
            case "mount.dismiss":
                return DestroyTracked(c, "mounts");
            case "mount.slowdown":
                return ApplyTimedBuff(player, Prefabs.Buff_General_Slow, EffectDuration(p, c, 8f, "duration", "durationSeconds"));

            case "zone.buff.apply":
                return ZoneBuff(c, p, apply: true);
            case "zone.buff.remove":
                return ZoneBuff(c, p, apply: false);
            case "player.buff.apply":
                return ForEachTargetPlayer(player, c, p, target => PlayerBuff(target, p, c, apply: true));
            case "player.buff.remove":
                return ForEachTargetPlayer(player, c, p, target => PlayerBuff(target, p, c, apply: false));
            case "trap.place":
                return PlaceTrap(player, p, c);
            case "trap.trigger":
                return TriggerTraps(player, p, c);
            case "trap.remove":
                return RemoveTraps(c, p);
            case "sequence.play":
            case "sequence.persist":
                return PlaySequence(player, p);
            case "sequence.stop":
                return OperationResult.Ok();
            case "sequence.custom.play":
            case "sequence.custom.run":
            case "sequence.custom.execute":
            case "sequence.custom.preview":
                return ExecuteCustomSequence(actionName, p, c);
            case "sequence.step.run":
            case "sequence.step.execute":
                return RunSequenceStep(player, p, c);
            case "sequence.step.skip":
                return SkipSequenceStep(player, p, c);
            case "sequence.step.retry":
                return RetrySequenceStep(player, p, c);
            case "buff.apply":
                return ApplyNamedBuff(player, p, c);
            case "buff.remove":
                return RemoveNamedBuff(player, p);
            case "glow.enable":
                return Glow(player, p, c, true);
            case "glow.disable":
                return Glow(player, p, c, false);
            case "schematic.load":
            case "schematic.loadat":
            case "schematic.loadatpos":
                return LoadSchematic(player, p, c);
            case "schematic.clear":
                return ClearSchematic(p);
            case "schematic.clear.radius":
                return ClearSchematicRadius(player, p, destroyWorld: false);
            case "schematic.destroy.radius":
                return ClearSchematicRadius(player, p, destroyWorld: true);
            case "auto.teleport":
                return Teleport(player, p, c);
            case "auto.fly":
                return AutoFly(player, p);

            case "revive.grant":
                foreach (var target in TargetPlayers(player, c, p))
                    Revives(ctx)[target.GetSteamId()] = Int(p, "maxLives", 1);
                return OperationResult.Ok();
            case "revive.consume":
                return ConsumeRevive(player, p, c);
            case "revive.reset":
                foreach (var target in TargetPlayers(player, c, p))
                    Revives(ctx).Remove(target.GetSteamId());
                return OperationResult.Ok();
            case "objective.capture":
                return CaptureObjective(p, c);
            case "objective.complete":
                ctx?.Scores.AddPlayerScore(player.GetSteamId(), Int(p, "rewardPoints", 0));
                return OperationResult.Ok();
            case "objective.deliver":
                return DeliverObjective(player, p, c);
            case "objective.reset":
                ctx?.State.Remove("objectives");
                return OperationResult.Ok();
            case "clan.task.add":
            case "clan.task.update":
            case "clan.task.progress":
            case "clan.task.cancel":
            case "clan.task.complete":
            case "clan.task.list":
            case "clan.task.reward":
                return ClanTaskAction(actionName, player, p, c);
            case "shrink.zone":
                StartManualShrink(c, p);
                DispatchShrinkZone(c, apply: true, p);
                return OperationResult.Ok();
            case "shrink.stop":
                c.GameContext?.State.Remove("manualShrink");
                DispatchShrinkZone(c, apply: false, p);
                return OperationResult.Ok();
            case "player.downgrade":
            case "player.upgrade":
                player.SetEquipmentLevel(Int(p, "gearLevel", 80), Int(p, "gearLevel", 80), Int(p, "gearLevel", 80));
                return OperationResult.Ok();
            case "equip.restrict":
                State(c, "equipment")["maxGearLevel"] = Int(p, "maxGearLevel", 80);
                return OperationResult.Ok();
            case "equip.unrestrict":
                State(c, "equipment").Remove("maxGearLevel");
                return OperationResult.Ok();
            case "autotrash.clear":
            case "autotrash.set":
                State(c, "autotrash")[actionName] = string.Join(",", p.Select(kv => $"{kv.Key}={kv.Value}"));
                return OperationResult.Ok();
            case "entity.damage":
                player.DealDamagePercent(Float(p, "damage", 10f) / 100f);
                return OperationResult.Ok();
            case "entity.heal":
                player.HealToFull();
                return OperationResult.Ok();
            case "entity.damage_percent":
                player.DealDamagePercent(Float(p, "percent", Float(p, "damage", 10f)) / 100f);
                return OperationResult.Ok();
            case "entity.heal_percent":
                HealPercent(player, Float(p, "percent", 25f) / 100f);
                return OperationResult.Ok();
            case "timer.start":
                Timers(ctx)[Text(p, "timerId", "default")] = DateTime.UtcNow.AddSeconds(Float(p, "duration", 30f));
                return OperationResult.Ok();
            case "timer.stop":
                Timers(ctx).Remove(Text(p, "timerId", "default"));
                return OperationResult.Ok();
            case "score.add":
                var points = Int(p, "points", 1);
                if (ctx != null && OptionalTeamId(p) is { } scoringTeam)
                    ctx.Scores.AddTeamScore(scoringTeam, points);
                else
                    ctx?.Scores.AddPlayerScore(player.GetSteamId(), points);
                return OperationResult.Ok();
            case "score.reset":
                ctx?.Scores.SetPlayerScore(player.GetSteamId(), 0);
                return OperationResult.Ok();
            case "condition.check":
                return OperationResult.Ok();
            case "point.set":
                SpatialPoints(ctx)[RequiredAny(p, "pointId", "id")] = ResolvePosition(player, p, c);
                return OperationResult.Ok();
            case "point.remove":
                return SpatialPoints(ctx).Remove(RequiredAny(p, "pointId", "id"))
                    ? OperationResult.Ok()
                    : OperationResult.Fail("Spatial point was not found.");
            case "point.clear_session":
                SpatialPoints(ctx).Clear();
                return OperationResult.Ok();
            case "effect.spawn_at_point":
                return SpawnEffectAtPoint(p, c);
            case "faction.set":
                player.SetTeam(Int(p, "teamId", 0));
                if (PrefabHelper.GetPrefabGuidDeep(Text(p, "factionId", "")) is { } faction)
                    player.SetFaction(faction);
                return OperationResult.Ok();
            case "faction.clear":
                player.SetTeam(0);
                return OperationResult.Ok();
            case "death.prevent":
                return BattleLuckPlugin.DeathPrevention?.Arm(
                    player,
                    Int(p, "initialCharges", Int(p, "charges", 1)),
                    Float(p, "activeWindowSeconds", Float(p, "duration", 0f)),
                    Float(p, "triggerCooldownSeconds", Float(p, "cooldown", 0f)),
                    Text(p, "onTriggeredSequenceId", Text(p, "sequenceId", "")))
                    ?? OperationResult.Fail("Death prevention service is not initialized.");
            case "death.allow":
                BattleLuckPlugin.DeathPrevention?.Disarm(player.GetSteamId());
                return OperationResult.Ok();
            case "progression.unlock.all_vbloods":
                return ForEachTargetPlayer(player, c, p, target => BattleLuckPlugin.Progression?.UnlockAllVBloods(target)
                    ?? OperationResult.Fail("Player progression service is not initialized."));
            case "progression.unlock.all_research":
                return ForEachTargetPlayer(player, c, p, target => BattleLuckPlugin.Progression?.UnlockAllResearch(target)
                    ?? OperationResult.Fail("Player progression service is not initialized."));
            case "progression.set_tier":
                return SetProgressionTier(player, p, c);
            case "progression.unlock_gear":
                return UnlockGear(player, p);
            case "mode.start":
            case "warevent_start":
            {
                var session = BattleLuckPlugin.Session;
                if (session == null)
                    return OperationResult.Fail("Session controller is not initialized.");

                var modeId = Text(p, "modeId", c.Config?.ModeId ?? "");
                if (!string.IsNullOrWhiteSpace(modeId))
                {
                    // Admin/war-event style: explicitly start by mode id.
                    session.ForceStart(modeId, player, skipEnterActions: Bool(p, "skipEnterActions", true));
                    BattleLuckPlugin.LogInfo($"[FlowActionExecutor] mode.start requested for mode '{modeId}'.");
                    return OperationResult.Ok();
                }

                // Flow-style: start the session that this player is already in.
                var result = session.ForceStartForPlayer(player.GetSteamId());
                if (!result.Success)
                    return OperationResult.Fail(result.Error ?? result.UserMessage ?? "Unable to force-start session.");

                BattleLuckPlugin.LogInfo($"[FlowActionExecutor] mode.start requested for player {player.GetSteamId()}.");
                return OperationResult.Ok();
            }
            case "mode.end":
            case "warevent_end":
            {
                var session = BattleLuckPlugin.Session;
                if (session == null)
                    return OperationResult.Fail("Session controller is not initialized.");

                var modeId = Text(p, "modeId", c.Config?.ModeId ?? "");
                if (!string.IsNullOrWhiteSpace(modeId))
                    session.ForceEndByModeId(modeId);

                if (ctx != null)
                    ctx.State["result"] = Text(p, "reason", "flow_mode_end");

                BattleLuckPlugin.LogInfo($"[FlowActionExecutor] mode.end requested for mode '{modeId}'.");
                return OperationResult.Ok();
            }
            case "tech.apply":
            {
                var techIds = ParseStringList(Text(p, "techIds", ""));
                if (techIds == null || techIds.Count == 0)
                    return OperationResult.Fail("tech.apply requires 'techIds' list.");

                var runtimeCatalog = ConfigLoader.LoadTechCatalog();
                var resolver = new Services.Runtime.TechResolver(runtimeCatalog);
                var (success, state, error) = resolver.Resolve(techIds.ToList());
                if (!success || state == null)
                    return OperationResult.Fail(error ?? "Tech resolution failed.");

                if (ctx != null)
                    ctx.TechState = state;

                BattleLuckPlugin.LogInfo($"[FlowActionExecutor] Applied {state.ActiveTechs.Count} tech(s) via tech.apply.");
                return OperationResult.Ok();
            }
            case "system.find":
            case "system.search":
                return FindSystemReference(player, p, c, useAi: actionName.Equals("system.find", StringComparison.OrdinalIgnoreCase));
            case "system.register":
                return RegisterLiveSystem(player, p, c);


            case "inventory.clear_all":
                return ClearInventory(player);
            case "inventory.count":
                return CountInventory(player, p);
            case "inventory.stash":
                return LogisticsController.Stash(player, Text(p, "container", ""), ParsePrefabList(Text(p, "itemIds", "")));
            case "inventory.salvage":
                return LogisticsController.Salvage(player, ParsePrefabList(Text(p, "itemIds", "")), Bool(p, "salvageAll", false));
            case "inventory.pull":
                return LogisticsController.Pull(player, ParsePrefabList(Text(p, "itemIds", "")));
            case "inventory.craftpull":
                return LogisticsController.CraftPull(player);
            case "inventory.sort":
                return LogisticsController.Sort(player);
            case "inventory.emptytrash":
                return LogisticsController.EmptyTrash(player);
            case "logistics.setting":
                return LogisticsController.SetGlobalSetting(Required(p, "setting"), Bool(p, "enabled", true));
            case "config.reload":
            case "event.reload":
                return ConfigController.Reload(Text(p, "modeId", c.ModeId));
            case "config.set_rule":
                return ConfigController.SetRule(Text(p, "modeId", c.ModeId), Required(p, "rule"), Required(p, "value"));
            case "config.set_metadata":
                return ConfigController.SetMetadata(Text(p, "modeId", c.ModeId), Required(p, "key"), Required(p, "value"));
            case "event.set_prompt":
                return ConfigController.SetPrompt(Text(p, "modeId", ""), Required(p, "prompt"));
            case "horse.mutate":
                p["mountType"] = Text(p, "horseType", Text(p, "mountType", "horse"));
                return SummonMount(player, p, c);
            case "horse.dismiss":
                return DestroyTracked(c, "mounts");
            case "player.freeze":
                return ApplyTimedBuff(player, Prefabs.Buff_General_Freeze, Float(p, "durationSeconds", 5f));
            case "player.unfreeze":
                player.TryRemoveBuff(Prefabs.Buff_General_Freeze);
                return OperationResult.Ok();
            case "camera.shake":
            case "camera.fade":
                return RecordBestEffort(c, "camera", actionName, p, "Camera client effect requested.");
            case "sound.play":
            case "sound.stop":
                return RecordBestEffort(c, "sound", actionName, p, "Sound client effect requested.");
            case "entity.spawn":
                return EntitySpawn(player, p, c);
            case "entity.destroy":
                return EntityDestroy(p);
            case "entity.count":
                return EntityCount(player, p, c);
            case "entity.query":
                return EntityQuery(p);
            case "entity.validate":
                return EntityValidate(p);

            case string s when s.StartsWith("system."):
                if (!LiveSystemRegistryService.TryGet(s, out var systemRegistration))
                {
                    return OperationResult.Fail(
                        $"System action '{s}' is not registered. Use system.register with an exact ProjectM or Unity system type first.");
                }

                BattleLuckPlugin.LogInfo(
                    $"[FlowActionExecutor] Registered {systemRegistration.Runtime} system '{systemRegistration.SystemType}' requested as '{s}'. Native ECS execution is intentionally not auto-created.");
                State(c, "systems")[s] = systemRegistration.SystemType;
                return OperationResult.Ok();
            default:
                return OperationResult.Fail($"Unknown action '{actionName}'.");
        }
    }

    OperationResult ApplyKit(Entity player, string kitId)
    {
        var result = KitController.ApplyKit(player, kitId);
        return result.Success ? OperationResult.Ok() : OperationResult.Fail(result.Error ?? "kit.apply failed");
    }

    OperationResult SetAbilitySlot(Entity player, Dictionary<string, string> p)
    {
        var prefab = PrefabHelper.GetPrefabGuidDeep(Required(p, "abilityPrefab"));
        if (!prefab.HasValue)
            return OperationResult.Fail("Unknown ability prefab.");

        var slot = AbilitySlot(p, "slot", 5);
        if (slot is < 5 or > 7)
            return OperationResult.Fail("ability.set_slot only supports Q/E/R slots (5, 6, 7). Travel/T and base combat slots are managed by kit config.");

        AbilityController.SetSpellOnSlot(player, slot, prefab.Value);
        return OperationResult.Ok();
    }

    OperationResult Teleport(Entity player, Dictionary<string, string> p, FlowActionContext c)
    {
        if (p.TryGetValue("position", out var posText) && TryParseFloat3(posText, out var pos))
        {
            return BattleLuckPlugin.Teleports?.Teleport(player, pos)
                ?? OperationResult.Fail("Teleport service is not initialized.");
        }

        var targetHash = Int(p, "targetZoneHash", c.ZoneHash);

        // Dev zone: teleport to dev arena center
        if (targetHash == DevSessionService.DevZoneHash)
        {
            return BattleLuckPlugin.Teleports?.Teleport(player, DevSessionService.DevArenaCenter)
                ?? OperationResult.Fail("Teleport service is not initialized.");
        }

        foreach (var modeId in RegisteredModes(c))
        {
            var config = ConfigLoader.Load(modeId);
            var zone = config.Zones.Zones.FirstOrDefault(z => z.Hash == targetHash);
            if (zone != null)
            {
                return BattleLuckPlugin.Teleports?.Teleport(player, zone.TeleportSpawn.ToFloat3())
                    ?? OperationResult.Fail("Teleport service is not initialized.");
            }
        }

        if (c.Zone != null)
        {
            return BattleLuckPlugin.Teleports?.Teleport(player, c.Zone.TeleportSpawn.ToFloat3())
                ?? OperationResult.Fail("Teleport service is not initialized.");
        }

        return OperationResult.Fail($"Zone {targetHash} not found.");
    }

    OperationResult SpawnEffectAtPoint(Dictionary<string, string> p, FlowActionContext c)
    {
        var pointId = RequiredAny(p, "pointId", "id");
        if (!SpatialPoints(c.GameContext).TryGetValue(pointId, out var position))
            return OperationResult.Fail($"Spatial point '{pointId}' was not found.");

        var prefab = RequiredAny(p, "prefab", "effectPrefab");
        var group = c.GameContext?.SessionId ?? "spatial_effects";
        var spawned = SchematicLoader.SpawnPrefabAt(prefab, position, Float(p, "rotation", 0f), "effect", group);
        if (!spawned.Success || spawned.Value == null)
            return OperationResult.Fail(spawned.Error ?? "Effect spawn failed.");
        Track(c, "effects", spawned.Value.Entity);
        return OperationResult.Ok();
    }

    OperationResult Announce(Entity player, Dictionary<string, string> p, FlowActionContext c)
    {
        var message = Text(p, "message", Text(p, "text", ""));
        if (string.IsNullOrWhiteSpace(message))
            return OperationResult.Fail("announce/notify requires message.");

        message = ExpandAnnouncementTokens(message, c);
        var title = Text(p, "title", Text(p, "phase", ""));
        title = ExpandAnnouncementTokens(title, c);
        var color = Text(p, "color", Text(p, "rgb", Text(p, "hex", "")));
        var level = NotificationHelper.ParseLevel(Text(p, "level", Text(p, "type", "")));
        var formatted = NotificationHelper.FormatAnnouncement(message, color, title, level);
        var scope = Text(p, "scope", Text(p, "target", "session"));

        if (scope.Equals("self", StringComparison.OrdinalIgnoreCase) ||
            scope.Equals("player", StringComparison.OrdinalIgnoreCase))
        {
            if (FlowController.TryGetUser(player, out var user))
                NotificationHelper.NotifyPlayerRaw(user, formatted);
            return OperationResult.Ok();
        }

        if (scope.Equals("admin", StringComparison.OrdinalIgnoreCase) ||
            scope.Equals("admins", StringComparison.OrdinalIgnoreCase))
        {
            NotificationHelper.NotifyAdminsRaw(formatted);
            return OperationResult.Ok();
        }

        NotificationHelper.NotifyAllRaw(formatted);
        return OperationResult.Ok();
    }

    OperationResult FindSystemReference(Entity player, Dictionary<string, string> p, FlowActionContext c, bool useAi)
    {
        var query = Text(p, "query", Text(p, "description", Text(p, "text", Text(p, "system", "")))).Trim();
        if (string.IsNullOrWhiteSpace(query))
            return OperationResult.Fail("system.find requires query/description/text.");

        var limit = Math.Clamp(Int(p, "limit", 5), 1, 10);
        var service = new KindredSystemReferenceService();
        var matches = service.Search(query, limit);
        var localResult = service.FormatMatches(matches);
        var scope = Text(p, "scope", Text(p, "target", "admin"));

        if (!useAi || BattleLuckPlugin.AIAssistant == null || matches.Count == 0)
        {
            SendSystemReferenceResult(player, localResult, scope);
            return OperationResult.Ok();
        }

        var steamId = player.GetSteamId();
        var prompt = service.BuildAiPrompt(query, matches);
        _ = Task.Run(async () =>
        {
            try
            {
                var response = await BattleLuckPlugin.AIAssistant.HandleDirectQuery(steamId, prompt, source: "event-system-reference");
                var text = string.IsNullOrWhiteSpace(response)
                    ? localResult
                    : BattleLuckPlugin.AIAssistant.FormatInGameResponse(query, response);
                MainThreadDispatcher.Enqueue(() => SendSystemReferenceResult(player, text, scope));
            }
            catch (Exception ex)
            {
                BattleLuckPlugin.LogWarning($"[FlowActionExecutor] system.find AI lookup failed: {ex.Message}");
                MainThreadDispatcher.Enqueue(() => SendSystemReferenceResult(player, localResult, scope));
            }
        });

        return OperationResult.Ok();
    }

    OperationResult RegisterLiveSystem(Entity player, Dictionary<string, string> p, FlowActionContext c)
    {
        var systemType = Text(p, "systemType", Text(p, "system", Text(p, "type", "")));
        var runtime = Text(p, "runtime", Text(p, "family", ""));
        var alias = Text(p, "alias", Text(p, "action", ""));
        var description = Text(p, "description", Text(p, "note", ""));
        var requestedBy = player.GetSteamId().ToString(System.Globalization.CultureInfo.InvariantCulture);

        var registration = LiveSystemRegistryService.Register(systemType, runtime, alias, description, requestedBy);
        if (!registration.Success || registration.Value == null)
            return OperationResult.Fail(registration.Error ?? "Failed to register live system reference.");

        var value = registration.Value;
        State(c, "systems")[value.Action] = value.SystemType;
        SendSystemReferenceResult(
            player,
            $"Registered {value.Runtime} system: {value.SystemType}\nLive action: {value.Action}\nThis records a verified reference; it does not create or patch a native ECS system.",
            Text(p, "scope", Text(p, "target", "admin")));
        return OperationResult.Ok();
    }

    static void SendSystemReferenceResult(Entity player, string message, string scope)
    {
        var formatted = NotificationHelper.FormatAnnouncement(
            message,
            "#66d9ff",
            "System Reference",
            NotificationHelper.ParseLevel("info"));
        if (scope.Equals("self", StringComparison.OrdinalIgnoreCase) ||
            scope.Equals("player", StringComparison.OrdinalIgnoreCase))
        {
            if (FlowController.TryGetUser(player, out var user))
                NotificationHelper.NotifyPlayerRaw(user, formatted);
            return;
        }

        if (scope.Equals("all", StringComparison.OrdinalIgnoreCase) ||
            scope.Equals("server", StringComparison.OrdinalIgnoreCase))
        {
            NotificationHelper.NotifyAllRaw(formatted);
            return;
        }

        NotificationHelper.NotifyAdminsRaw(formatted);
    }

    static string ExpandAnnouncementTokens(string value, FlowActionContext c)
    {
        if (string.IsNullOrWhiteSpace(value))
            return value;

        var ctx = c.GameContext;
        var zoneName = c.Zone?.Name ?? c.ZoneHash.ToString();
        return value
            .Replace("{mode}", c.Config?.ModeId ?? ctx?.ModeId ?? "event", StringComparison.OrdinalIgnoreCase)
            .Replace("{zone}", zoneName, StringComparison.OrdinalIgnoreCase)
            .Replace("{zoneHash}", c.ZoneHash.ToString(), StringComparison.OrdinalIgnoreCase)
            .Replace("{players}", (ctx?.Players.Count ?? 0).ToString(), StringComparison.OrdinalIgnoreCase)
            .Replace("{session}", ctx?.SessionId ?? "", StringComparison.OrdinalIgnoreCase)
            .Replace("{timeLimit}", (ctx?.TimeLimitSeconds ?? 0).ToString(), StringComparison.OrdinalIgnoreCase);
    }

    OperationResult SendInventory(Entity player, Dictionary<string, string> p)
    {
        if (!int.TryParse(Required(p, "itemId"), out var guid))
            return OperationResult.Fail("Invalid itemId.");
        var amount = Int(p, "amount", 1);
        return player.TrySendItemTo(new PrefabGUID(guid), amount)
            ? OperationResult.Ok()
            : OperationResult.Fail("Item could not be sent.");
    }

    OperationResult SpawnWave(Entity player, Dictionary<string, string> p, FlowActionContext c)
    {
        var count = Int(p, "count", 3);
        var waveId = Int(p, "waveId", 1);
        Spawner(c).SpawnWave(GetWavePrefabs(waveId), count, ResolvePosition(player, p, c));
        return OperationResult.Ok();
    }

    OperationResult MerchantRun(Entity player, Dictionary<string, string> p)
    {
        var service = BattleLuckPlugin.MerchantCommands;
        if (service == null)
            return OperationResult.Fail("Merchant command service is not initialized.");
        if (!FlowController.TryGetUser(player, out var user))
            return OperationResult.Fail("Player user is not available.");

        var id = Text(p, "listingId", Text(p, "uuid", Text(p, "id", "")));
        if (string.IsNullOrWhiteSpace(id))
            return OperationResult.Fail("merchant.run requires listingId/uuid/id.");

        return service.ExecuteListing(player, user, id, Bool(p, "consumeInventoryItem", Bool(p, "consume", false)));
    }

    OperationResult MerchantGrant(Entity player, Dictionary<string, string> p)
    {
        var service = BattleLuckPlugin.MerchantCommands;
        if (service == null)
            return OperationResult.Fail("Merchant command service is not initialized.");

        var id = Text(p, "listingId", Text(p, "uuid", Text(p, "id", "")));
        if (string.IsNullOrWhiteSpace(id))
            return OperationResult.Fail("merchant.grant requires listingId/uuid/id.");

        return service.GrantToken(player, id, Int(p, "amount", 1));
    }

    OperationResult SpawnBoss(Entity player, Dictionary<string, string> p, FlowActionContext c)
    {
        var prefabName = Required(p, "prefab");
        var prefab = ResolvePrefab(prefabName);
        if (!prefab.HasValue)
            return OperationResult.Fail("Unknown boss prefab.");

        var spawnPos = ResolvePosition(player, p, c);
        var bossId = Text(p, "bossId", Text(p, "id", ""));
        var sessionId = c.GameContext?.SessionId ?? "_flow_";
        var homeRadius = Float(p, "homeRadius", 40f);
        var teamId = OptionalTeamId(p);
        var behavior = ParseNpcControlMode(Text(p, "behavior", "idle"));

        void OnSpawned(Entity entity)
        {
            Track(c, "spawned", entity);
            Track(c, "bosses", entity);

            if (entity == Entity.Null || !entity.Exists())
                return;

            var registered = BattleLuckPlugin.NpcService?.RegisterNpc(
                sessionId,
                string.IsNullOrWhiteSpace(bossId) ? null : bossId,
                prefabName,
                prefab.Value,
                entity,
                spawnPos,
                homeRadius,
                preventDisable: true);

            if (registered?.Success == true && registered.Value != null)
            {
                var entry = registered.Value;
                if (teamId.HasValue)
                    BattleLuckPlugin.NpcService?.SetTeam(entry.NpcId, teamId.Value);
                ApplyInitialNpcMode(BattleLuckPlugin.NpcService!, entry, behavior, player, homeRadius);
            }
        }

        Spawner(c).SpawnBoss(prefab.Value, spawnPos, OnSpawned);

        return OperationResult.Ok();
    }

    OperationResult SpawnNpcGroup(Entity player, Dictionary<string, string> p, FlowActionContext c)
    {
        var prefabName = Required(p, "prefab");
        var prefab = ResolvePrefab(prefabName);
        if (!prefab.HasValue)
            return OperationResult.Fail("Unknown NPC prefab.");
        var count = Int(p, "count", 1);
        var center = ResolvePosition(player, p, c);
        var formation = Text(p, "formation", "line");
        var idPrefix = Text(p, "npcId", Text(p, "id", Text(p, "group", "")));
        var homeRadius = Float(p, "homeRadius", 35f);
        var sessionId = c.GameContext?.SessionId ?? "_flow_";

        for (var i = 0; i < count; i++)
        {
            var index = i;
            var offset = FormationOffset(i, count, formation);
            var spawnPos = center + offset;
            var npcId = string.IsNullOrWhiteSpace(idPrefix)
                ? null
                : count == 1 ? idPrefix : $"{idPrefix}_{index + 1}";

            Spawner(c).SpawnNPC(prefab.Value, spawnPos, entity =>
            {
                Track(c, "spawned", entity);
                Track(c, "npcs", entity);
                var result = BattleLuckPlugin.NpcService?.RegisterNpc(sessionId, npcId, prefabName, prefab.Value, entity, spawnPos, homeRadius);
                if (result?.Success == false)
                    BattleLuckPlugin.LogWarning($"[FlowActionExecutor] NPC register failed: {result.Error}");
            });
        }
        return OperationResult.Ok();
    }

    OperationResult NpcControl(string actionName, Entity player, Dictionary<string, string> p, FlowActionContext c)
    {
        var service = BattleLuckPlugin.NpcService;
        if (service == null)
            return OperationResult.Fail("NPC control service is not initialized.");

        var sessionId = c.GameContext?.SessionId ?? "_flow_";
        var selection = ResolveNpcControlEntries(service, sessionId, actionName, player, p, c);
        if (!selection.Success || selection.Value == null || selection.Value.Count == 0)
            return OperationResult.Fail(selection.Error ?? "No NPCs matched the selector.");

        var primaryNpcId = selection.Value[0].NpcId;
        var audit = NpcActionAuditor.Start(actionName, primaryNpcId, "Flow", player.GetSteamId().ToString(), sessionId);
        audit.Parameters = p.ToDictionary(kv => kv.Key, kv => (object)kv.Value);
        audit = NpcActionAuditor.RecordPre(audit, service);

        var successes = 0;
        var failures = new List<string>();
        foreach (var entry in selection.Value)
        {
            var result = ApplyNpcControlAction(service, actionName, entry.NpcId, player, p, c);
            if (result.Success)
                successes++;
            else
                failures.Add($"{entry.NpcId}: {result.Error}");
        }

        var allExecuted = failures.Count == 0;
        audit.NewMode = service.TryGet(primaryNpcId, out var afterEntry) ? (BattleLuck.Models.NpcBehaviorMode?)afterEntry.Mode : null;
        audit = NpcActionAuditor.RecordPost(audit, service, allExecuted, failures.Count > 0 ? string.Join("; ", failures.Take(3)) : null);

        if (successes == 0)
            return OperationResult.Fail(string.Join("; ", failures.Take(3)));
        if (failures.Count > 0)
            BattleLuckPlugin.LogWarning($"[FlowActionExecutor] {actionName} applied to {successes} NPC(s), failed {failures.Count}: {string.Join("; ", failures.Take(3))}");

        BattleLuckPlugin.LogInfo($"[FlowActionExecutor] {actionName} applied to {successes} NPC(s).");
        return OperationResult.Ok();
    }

    OperationResult ApplyNpcControlAction(NpcControlService service, string actionName, string npcId, Entity player, Dictionary<string, string> p, FlowActionContext c)
    {
        switch (actionName)
        {
            case "npc.follow":
                return service.Follow(
                    npcId,
                    ResolveNpcTarget(player, p),
                    Float(p, "followRange", Float(p, "range", 6f)),
                    Float(p, "leashRange", 80f));
            case "npc.aggro":
                return service.Aggro(
                    npcId,
                    ResolveNpcTarget(player, p),
                    Float(p, "aggroRange", Float(p, "range", 3f)),
                    Float(p, "leashRange", 80f));
            case "npc.goto":
            case "npc.goto.pos":
                return service.GoTo(npcId, ResolveNpcGotoPosition(player, p, c), Float(p, "arrivalRange", 2f));
            case "npc.hold":
            case "npc.stay":
                return service.Hold(npcId, Float(p, "holdRadius", Float(p, "homeRadius", Float(p, "radius", 8f))));
            case "npc.release":
                return service.Release(npcId);
            case "npc.despawn":
                return service.Despawn(npcId);
            case "npc.team":
                return service.SetTeam(npcId, Int(p, "teamId", 0));
            case "npc.faction":
                var factionName = Text(p, "factionPrefab", Text(p, "factionId", ""));
                var faction = ResolvePrefab(factionName) ?? PrefabGUID.Empty;
                return faction == PrefabGUID.Empty
                    ? OperationResult.Fail("Unknown faction prefab.")
                    : service.SetFaction(npcId, faction);
            case "npc.speed":
                return service.SetSpeed(npcId, Float(p, "speed", 9f));
            case "npc.rename":
                return service.Rename(npcId, Text(p, "name", Text(p, "displayName", npcId)));
            case "npc.patrol":
            {
                var waypoints = ParsePatrolWaypoints(p);
                return waypoints.Count == 0
                    ? OperationResult.Fail("npc.patrol requires waypoints. Use waypoints=x1,y1,z1;x2,y2,z2 or waypoint.1=x,y,z.")
                    : service.Patrol(npcId, waypoints);
            }
            case "npc.guard":
            {
                var guardPos = ResolvePosition(player, p, c);
                var detection = Float(p, "detectionRadius", Float(p, "range", 15f));
                var chase = Float(p, "chaseRange", 25f);
                var returnRadius = Float(p, "returnRadius", 35f);
                var target = ResolveNpcTarget(player, p);
                var config = new BattleLuck.Models.NpcGuardPost
                {
                    Position = guardPos,
                    TargetEntity = target,
                    DetectionRadius = detection,
                    ChaseRange = chase,
                    ReturnRadius = returnRadius
                };
                return service.Guard(npcId, config);
            }
            case "npc.flee":
            {
                var from = ResolveNpcTarget(player, p);
                var fromPos = ResolvePosition(player, p, c);
                var safe = Float(p, "safeDistance", 20f);
                var duration = Float(p, "duration", 10f);
                var speedMult = Float(p, "speedMultiplier", 1.5f);
                var config = new BattleLuck.Models.NpcFleeConfig
                {
                    FromEntity = from,
                    FromPosition = fromPos,
                    SafeDistance = safe,
                    DurationSeconds = duration,
                    FleeSpeedMultiplier = speedMult
                };
                return service.Flee(npcId, config);
            }
            case "npc.wander":
            {
                var radius = Float(p, "radius", 15f);
                var minPause = Float(p, "minPause", 2f);
                var maxPause = Float(p, "maxPause", 6f);
                var config = new BattleLuck.Models.NpcWanderConfig
                {
                    Radius = radius,
                    MinPauseSeconds = minPause,
                    MaxPauseSeconds = maxPause
                };
                return service.Wander(npcId, config);
            }
            case "npc.formation":
            {
                var center = ResolvePosition(player, p, c);
                var slots = ParseFormationSlots(p);
                var leader = Text(p, "leaderId", Text(p, "leader", ""));
                return service.Formation(npcId, slots, string.IsNullOrWhiteSpace(leader) ? null : leader, center);
            }
            default:
                return OperationResult.Fail($"Unknown NPC control action '{actionName}'.");
        }
    }

    OperationResult<List<ControlledNpcEntry>> ResolveNpcControlEntries(
        NpcControlService service,
        string sessionId,
        string actionName,
        Entity player,
        Dictionary<string, string> p,
        FlowActionContext c)
    {
        var selector = Text(p, "npcId", Text(p, "bossId", Text(p, "id", Text(p, "selector", Text(p, "prefab", ""))))).Trim();
        var limit = Math.Clamp(Int(p, "limit", Int(p, "max", Bool(p, "all", false) ? 50 : 1)), 1, 100);

        if (selector.Equals("all", StringComparison.OrdinalIgnoreCase))
        {
            var all = service.List(sessionId).Take(limit).ToList();
            return all.Count > 0
                ? OperationResult<List<ControlledNpcEntry>>.Ok(all)
                : OperationResult<List<ControlledNpcEntry>>.Fail($"No tracked NPCs exist for session '{sessionId}'.");
        }

        if (TryResolveNpcAreaSelector(selector, actionName, player, p, c, out var center, out var radius))
        {
            var rows = FindNpcEntitiesNear(center, radius, limit);
            var entries = new List<ControlledNpcEntry>();
            foreach (var row in rows)
            {
                if (TryGetOrRegisterNpc(service, sessionId, row.Entity, out var entry, out _))
                    entries.Add(entry);
            }

            return entries.Count > 0
                ? OperationResult<List<ControlledNpcEntry>>.Ok(entries)
                : OperationResult<List<ControlledNpcEntry>>.Fail($"No NPCs found within {radius:F1}m of ({center.x:F1},{center.y:F1},{center.z:F1}).");
        }

        if (string.IsNullOrWhiteSpace(selector) ||
            selector.Equals("last", StringComparison.OrdinalIgnoreCase) ||
            selector.Equals("self", StringComparison.OrdinalIgnoreCase))
        {
            var latest = service.GetLatest(sessionId);
            return latest != null
                ? OperationResult<List<ControlledNpcEntry>>.Ok(new List<ControlledNpcEntry> { latest })
                : OperationResult<List<ControlledNpcEntry>>.Fail("No NPC id was provided and no tracked NPC exists for this session.");
        }

        if (service.TryGet(selector, out var exact))
            return OperationResult<List<ControlledNpcEntry>>.Ok(new List<ControlledNpcEntry> { exact });

        var matches = service.List(sessionId)
            .Where(e =>
                e.NpcId.Contains(selector, StringComparison.OrdinalIgnoreCase) ||
                e.DisplayName.Contains(selector, StringComparison.OrdinalIgnoreCase) ||
                e.PrefabName.Contains(selector, StringComparison.OrdinalIgnoreCase))
            .Take(limit)
            .ToList();
        if (matches.Count > 0)
            return OperationResult<List<ControlledNpcEntry>>.Ok(matches);

        if (TryResolveEntityReference(selector, out var entity) &&
            IsNpcLike(entity) &&
            TryGetOrRegisterNpc(service, sessionId, entity, out var registered, out var registerError))
        {
            return OperationResult<List<ControlledNpcEntry>>.Ok(new List<ControlledNpcEntry> { registered });
        }

        return OperationResult<List<ControlledNpcEntry>>.Fail($"NPC selector '{selector}' was not found. Use npcId, near, near:radius, pos:x,y,z:radius, or position/radius.");
    }

    static bool TryResolveNpcAreaSelector(
        string selector,
        string actionName,
        Entity player,
        Dictionary<string, string> p,
        FlowActionContext c,
        out float3 center,
        out float radius)
    {
        center = player.GetPosition();
        radius = 35f;

        if (selector.StartsWith("near:", StringComparison.OrdinalIgnoreCase))
        {
            radius = Math.Clamp(ParseSelectorRadius(selector[5..], p, actionName, hasExplicitCenter: false), 1f, 250f);
            return true;
        }

        if (selector.StartsWith("pos:", StringComparison.OrdinalIgnoreCase))
        {
            var payload = selector[4..];
            var parts = payload.Split(':', 2, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length > 0 && TryParseFloat3(parts[0], out center))
            {
                radius = Math.Clamp(parts.Length > 1 ? ParseSelectorRadius(parts[1], p, actionName, hasExplicitCenter: true) : SelectionRadius(p, actionName, true), 1f, 250f);
                return true;
            }
        }

        var areaSelector =
            selector.Equals("near", StringComparison.OrdinalIgnoreCase) ||
            selector.Equals("area", StringComparison.OrdinalIgnoreCase) ||
            selector.Equals("radius", StringComparison.OrdinalIgnoreCase);
        var hasExplicitCenter = TryResolveNpcSelectionCenter(player, p, actionName, out center);
        if (areaSelector || hasExplicitCenter)
        {
            radius = Math.Clamp(SelectionRadius(p, actionName, hasExplicitCenter), 1f, 250f);
            return true;
        }

        return false;
    }

    static bool TryResolveNpcSelectionCenter(Entity player, Dictionary<string, string> p, string actionName, out float3 center)
    {
        foreach (var key in new[] { "selectPosition", "selectorPosition", "sourcePosition", "center", "origin" })
        {
            if (p.TryGetValue(key, out var text) && TryParseFloat3(text, out center))
                return true;
        }

        if (!actionName.Equals("npc.goto", StringComparison.OrdinalIgnoreCase) &&
            !actionName.Equals("npc.goto.pos", StringComparison.OrdinalIgnoreCase) &&
            p.TryGetValue("position", out var positionText) &&
            TryParseFloat3(positionText, out center))
        {
            return true;
        }

        center = player.GetPosition();
        return false;
    }

    static float SelectionRadius(Dictionary<string, string> p, string actionName, bool hasExplicitCenter)
    {
        if (p.TryGetValue("selectRadius", out var sr) && float.TryParse(sr, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var parsed))
            return parsed;
        if (p.TryGetValue("selectorRadius", out sr) && float.TryParse(sr, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out parsed))
            return parsed;
        if (p.TryGetValue("npcRadius", out sr) && float.TryParse(sr, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out parsed))
            return parsed;
        if ((hasExplicitCenter || !actionName.Equals("npc.hold", StringComparison.OrdinalIgnoreCase)) &&
            p.TryGetValue("radius", out sr) &&
            float.TryParse(sr, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out parsed))
            return parsed;
        return 35f;
    }

    static float ParseSelectorRadius(string text, Dictionary<string, string> p, string actionName, bool hasExplicitCenter)
        => float.TryParse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var radius)
            ? radius
            : SelectionRadius(p, actionName, hasExplicitCenter);

    static float3 ResolveNpcGotoPosition(Entity player, Dictionary<string, string> p, FlowActionContext c)
    {
        foreach (var key in new[] { "destination", "targetPosition", "gotoPosition" })
        {
            if (p.TryGetValue(key, out var text) && TryParseFloat3(text, out var value))
                return value;
        }

        return ResolvePosition(player, p, c);
    }

    static bool TryGetOrRegisterNpc(NpcControlService service, string sessionId, Entity entity, out ControlledNpcEntry entry, out string error)
    {
        entry = null!;
        error = "";
        if (service.TryGetByEntity(entity, out entry))
            return true;

        var prefab = entity.GetPrefabGuid();
        var name = PrefabHelper.GetLivePrefabName(prefab) ?? PrefabHelper.GetName(prefab) ?? prefab.GuidHash.ToString();
        var result = service.RegisterNpc(sessionId, null, name, prefab, entity, entity.GetPosition());
        if (result.Success && result.Value != null)
        {
            entry = result.Value;
            return true;
        }

        error = result.Error ?? "NPC registration failed.";
        return false;
    }

    static List<(Entity Entity, float Distance)> FindNpcEntitiesNear(float3 center, float radius, int limit)
    {
        var em = VRisingCore.EntityManager;
        var query = em.CreateEntityQuery(new EntityQueryDesc
        {
            All = new[] { ComponentType.ReadOnly<Translation>(), ComponentType.ReadOnly<PrefabGUID>() },
            Any = new[] { ComponentType.ReadOnly<UnitLevel>(), ComponentType.ReadOnly<UnitStats>(), ComponentType.ReadOnly<Aggroable>() },
            None = new[] { ComponentType.ReadOnly<PlayerCharacter>() }
        });

        var entities = query.ToEntityArray(Allocator.Temp);
        var rows = new List<(Entity Entity, float Distance)>();
        try
        {
            foreach (var entity in entities)
            {
                if (!IsNpcLike(entity))
                    continue;

                var distance = math.distance(center, entity.GetPosition());
                if (distance <= radius)
                    rows.Add((entity, distance));
            }
        }
        finally
        {
            if (entities.IsCreated)
                entities.Dispose();
            query.Dispose();
        }

        return rows.OrderBy(r => r.Distance).Take(limit).ToList();
    }

    static bool IsNpcLike(Entity entity)
        => entity.Exists()
           && !entity.Has<PlayerCharacter>()
           && entity.Has<Translation>()
           && entity.Has<PrefabGUID>()
            && (entity.Has<UnitLevel>() || entity.Has<UnitStats>() || entity.Has<Aggroable>());

    static List<BattleLuck.Models.NpcPatrolWaypoint> ParsePatrolWaypoints(Dictionary<string, string> p)
    {
        var waypoints = new List<BattleLuck.Models.NpcPatrolWaypoint>();
        if (p.TryGetValue("waypoints", out var wpText))
        {
            foreach (var segment in wpText.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (TryParseFloat3(segment, out var pos))
                    waypoints.Add(new BattleLuck.Models.NpcPatrolWaypoint { Position = pos });
            }
        }

        for (var i = 1; i <= 20; i++)
        {
            var key = $"waypoint.{i}";
            if (p.TryGetValue(key, out var wp) && TryParseFloat3(wp, out var ppos))
                waypoints.Add(new BattleLuck.Models.NpcPatrolWaypoint { Position = ppos });
        }

        return waypoints;
    }

    static List<BattleLuck.Models.NpcFormationSlot> ParseFormationSlots(Dictionary<string, string> p)
    {
        var slots = new List<BattleLuck.Models.NpcFormationSlot>();
        for (var i = 1; i <= 20; i++)
        {
            var key = $"slot.{i}";
            if (!p.TryGetValue(key, out var slotText))
                continue;

            var parts = slotText.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var npcId = parts.Length > 0 ? parts[0] : "";
            var offset = parts.Length > 3 && TryParseFloat3(string.Join(",", parts.Skip(1).Take(3)), out var parsedOffset)
                ? parsedOffset
                : new float3(0f, 0f, 0f);
            slots.Add(new BattleLuck.Models.NpcFormationSlot { NpcId = npcId, Offset = offset, Priority = i });
        }

        if (slots.Count == 0 && p.TryGetValue("formation", out var fmt))
        {
            var fmtLower = fmt.ToLowerInvariant();
            if (fmtLower.Contains("line"))
            {
                for (var i = 0; i < 5; i++)
                    slots.Add(new BattleLuck.Models.NpcFormationSlot { NpcId = "", Offset = new float3(i * 3f, 0f, 0f), Priority = i });
            }
            else if (fmtLower.Contains("circle"))
            {
                for (var i = 0; i < 5; i++)
                {
                    var angle = (float)(i / 5.0 * System.Math.PI * 2);
                    slots.Add(new BattleLuck.Models.NpcFormationSlot { NpcId = "", Offset = new float3(math.cos(angle) * 5f, 0f, math.sin(angle) * 5f), Priority = i });
                }
            }
        }

        return slots;
    }

    static bool TryResolveEntityReference(string selector, out Entity entity)
    {
        entity = Entity.Null;
        var parts = selector.Split(':', 2, StringSplitOptions.TrimEntries);
        if (parts.Length == 2 &&
            int.TryParse(parts[0], out var index) &&
            int.TryParse(parts[1], out var version))
        {
            entity = new Entity { Index = index, Version = version };
            return entity.Exists();
        }

        if (!int.TryParse(selector, out var entityIndex))
            return false;

        var em = VRisingCore.EntityManager;
        var query = em.CreateEntityQuery(ComponentType.ReadOnly<Translation>(), ComponentType.ReadOnly<PrefabGUID>());
        var entities = query.ToEntityArray(Allocator.Temp);
        try
        {
            foreach (var candidate in entities)
            {
                if (candidate.Index == entityIndex && candidate.Exists())
                {
                    entity = candidate;
                    return true;
                }
            }
        }
        finally
        {
            if (entities.IsCreated)
                entities.Dispose();
            query.Dispose();
        }

        return false;
    }

    OperationResult SpawnStructure(Entity player, Dictionary<string, string> p, FlowActionContext c, string key, bool allowRealCastleGeometry)
    {
        var prefabName = Required(p, key);
        var prefab = ResolvePrefab(prefabName);
        if (!prefab.HasValue)
            return OperationResult.Fail("Unknown structure prefab.");
        var realCastle = IsRealCastleSpawn(p);
        if (realCastle && !allowRealCastleGeometry)
            return OperationResult.Fail("realCastle/ownedCastle is limited to floor.place, tile.place, and wall.build.");
        if (realCastle && !CastleTileOwnershipService.IsOwnedCastleGeometryPrefabName(prefabName))
            return OperationResult.Fail($"realCastle/ownedCastle only supports floor/tile/wall prefabs; '{prefabName}' is not allowed.");
        if (realCastle && !CastleTileOwnershipService.TryEnsureAnchorForCurrentAdmin(out var anchorError))
            return OperationResult.Fail(anchorError);

        var position = ResolvePosition(player, p, c);
        var entity = EntityExtensions.SpawnUnit(prefab.Value, Entity.Null, position);
        if (entity.Exists())
        {
            if (realCastle)
            {
                if (!CastleTileOwnershipService.TryStampOwnedTile(entity, position, prefabName, out var warning))
                {
                    try { entity.DestroyWithReason(); } catch { }
                    return OperationResult.Fail($"Castle ownership stamp failed: {warning}");
                }
            }
            else
            {
                ProtectEventObject(entity);
            }

            if (!realCastle && ShouldSanitizeStructureSpawn(prefabName, p))
                EventTileSafety.StripRoomGraphComponents(VRisingCore.EntityManager, entity, stripTileGrid: true);
            if (!realCastle)
                Track(c, "structures", entity);
        }
        return entity.Exists() ? OperationResult.Ok() : OperationResult.Fail("Spawn failed.");
    }

    OperationResult BuildSpawn(Entity player, Dictionary<string, string> p, FlowActionContext c)
    {
        var prefabName = Text(p, "prefab", Text(p, "tile", Text(p, "name", "")));
        if (prefabName.Equals("palette", StringComparison.OrdinalIgnoreCase) ||
            prefabName.Equals("current", StringComparison.OrdinalIgnoreCase))
        {
            var current = BuildPaletteService.Current(player.GetSteamId());
            if (!current.Success || current.Value == null)
                return OperationResult.Fail(current.Error ?? "Palette is empty.");

            prefabName = current.Value.Prefab;
        }

        if (string.IsNullOrWhiteSpace(prefabName))
            return OperationResult.Fail("build.spawn requires prefab/tile/name.");

        if (KindredSpawnSafety.IsSpawnBanned(prefabName, out var bannedReason))
            return OperationResult.Fail($"build.spawn blocked: {bannedReason}");

        var position = ResolvePosition(player, p, c);
        var trackingGroup = Text(p, "group", c.GameContext?.SessionId ?? "manual_build");
        var realCastle = IsRealCastleSpawn(p);
        if (realCastle)
            return OperationResult.Fail("realCastle/ownedCastle is limited to floor.place, tile.place, and wall.build. Use visual build.spawn for tracked non-owned objects.");
        var kind = Text(p, "kind", ClassifyBuildKind(prefabName));

        var result = SchematicLoader.SpawnPrefabAt(
            prefabName,
            position,
            Float(p, "rotation", Float(p, "rotationDegrees", 0f)),
            kind,
            trackingGroup);

        if (!result.Success || result.Value == null)
            return OperationResult.Fail(result.Error ?? "build.spawn failed.");

        if (result.Value.Entity.Exists())
        {
            Track(c, "structures", result.Value.Entity);
        }

        return OperationResult.Ok();
    }

    OperationResult BuildSearch(Dictionary<string, string> p)
    {
        var filter = Text(p, "filter", Text(p, "search", Text(p, "query", "")));
        if (string.IsNullOrWhiteSpace(filter))
            return OperationResult.Fail("build.search requires filter/search/query.");

        PrefabHelper.ScanLivePrefabs();
        var count = PrefabHelper.FindLive(filter).Count(kv => LooksLikeBuildPrefab(kv.Key));
        BattleLuckPlugin.LogInfo($"[FlowActionExecutor] build.search '{filter}' matched {count} build prefab(s).");
        return OperationResult.Ok();
    }

    OperationResult Palette(string actionName, Entity player, Dictionary<string, string> p)
    {
        var ownerId = player.GetSteamId();
        var search = Text(p, "search", Text(p, "filter", Text(p, "query", Text(p, "prefab", ""))));

        switch (actionName)
        {
            case "palette.add":
                {
                    var result = BuildPaletteService.Add(ownerId, search);
                    if (!result.Success || result.Value == null)
                        return OperationResult.Fail(result.Error ?? "palette.add failed.");

                    BattleLuckPlugin.LogInfo($"[FlowActionExecutor] palette.add {result.Value.Prefab}={result.Value.PrefabGuid}.");
                    return OperationResult.Ok();
                }
            case "palette.remove":
                return BuildPaletteService.Remove(ownerId, search);
            case "palette.clear":
                return BuildPaletteService.Clear(ownerId);
            case "palette.next":
                return BuildPaletteService.Cycle(ownerId, 1).Success ? OperationResult.Ok() : OperationResult.Fail("Palette is empty.");
            case "palette.prev":
                return BuildPaletteService.Cycle(ownerId, -1).Success ? OperationResult.Ok() : OperationResult.Fail("Palette is empty.");
            case "palette.list":
                {
                    var entries = BuildPaletteService.List(ownerId);
                    BattleLuckPlugin.LogInfo($"[FlowActionExecutor] palette.list count={entries.Count}: {string.Join(", ", entries.Take(8).Select(e => e.Prefab))}");
                    return OperationResult.Ok();
                }
            case "palette.current":
                {
                    var current = BuildPaletteService.Current(ownerId);
                    if (!current.Success || current.Value == null)
                        return OperationResult.Fail(current.Error ?? "Palette is empty.");

                    BattleLuckPlugin.LogInfo($"[FlowActionExecutor] palette.current {current.Value.Prefab}={current.Value.PrefabGuid}.");
                    return OperationResult.Ok();
                }
            default:
                return OperationResult.Fail($"Unknown palette action '{actionName}'.");
        }
    }

    OperationResult SetBuildFree(FlowActionContext c, bool enableFreeBuild)
    {
        if (enableFreeBuild)
        {
            if (!EventBuildFreeActionsEnabled)
            {
                BattleLuckPlugin.LogWarning("[FlowActionExecutor] build.free skipped: event free-build/build-queue geometry is disabled for server stability.");
                return OperationResult.Ok();
            }

            global::BuildingRestrictionController.DisableRestrictions();
            c.GameContext?.State.TryAdd("freeBuildEnabled", true);
            BattleLuckPlugin.LogInfo("[FlowActionExecutor] build.free enabled free-build restrictions bypass for event setup.");
        }
        else
        {
            global::BuildingRestrictionController.EnableRestrictions();
            c.GameContext?.State.Remove("freeBuildEnabled");
            BattleLuckPlugin.LogInfo("[FlowActionExecutor] build.disablefreebuild restored normal build restrictions.");
        }

        return OperationResult.Ok();
    }

    static string ClassifyBuildKind(string prefabName)
    {
        if (prefabName.Contains("Floor", StringComparison.OrdinalIgnoreCase)) return "floor";
        if (prefabName.Contains("Wall", StringComparison.OrdinalIgnoreCase)) return "wall";
        if (prefabName.Contains("Door", StringComparison.OrdinalIgnoreCase)) return "door";
        if (prefabName.Contains("Gate", StringComparison.OrdinalIgnoreCase)) return "gate";
        if (prefabName.Contains("Carpet", StringComparison.OrdinalIgnoreCase)) return "carpet";
        if (prefabName.Contains("Tile", StringComparison.OrdinalIgnoreCase)) return "tile";
        return "prefab";
    }

    static bool LooksLikeBuildPrefab(string prefabName) =>
        prefabName.Contains("TM_", StringComparison.OrdinalIgnoreCase) ||
        prefabName.Contains("Castle", StringComparison.OrdinalIgnoreCase) ||
        prefabName.Contains("Floor", StringComparison.OrdinalIgnoreCase) ||
        prefabName.Contains("Wall", StringComparison.OrdinalIgnoreCase) ||
        prefabName.Contains("Tile", StringComparison.OrdinalIgnoreCase) ||
        prefabName.Contains("Door", StringComparison.OrdinalIgnoreCase) ||
        prefabName.Contains("Gate", StringComparison.OrdinalIgnoreCase) ||
        prefabName.Contains("Carpet", StringComparison.OrdinalIgnoreCase);

    static bool ShouldSanitizeStructureSpawn(string prefabName, Dictionary<string, string> p)
    {
        if (IsRealCastleSpawn(p))
            return false;

        return prefabName.Contains("Castle", StringComparison.OrdinalIgnoreCase) ||
               prefabName.Contains("Floor", StringComparison.OrdinalIgnoreCase) ||
               prefabName.Contains("Wall", StringComparison.OrdinalIgnoreCase) ||
               prefabName.Contains("Tile", StringComparison.OrdinalIgnoreCase);
    }

    static bool IsRealCastleSpawn(Dictionary<string, string> p)
    {
        var roomMode = Text(p, "roomMode", Text(p, "floorMode", Text(p, "mode", "visual")));
        return Bool(p, "realCastle", false) ||
               Bool(p, "ownedCastle", false) ||
               roomMode.Equals("castle", StringComparison.OrdinalIgnoreCase) ||
               roomMode.Equals("room", StringComparison.OrdinalIgnoreCase) ||
               roomMode.Equals("owned", StringComparison.OrdinalIgnoreCase) ||
               roomMode.Equals("admin", StringComparison.OrdinalIgnoreCase);
    }

    OperationResult BuildWall(Entity player, Dictionary<string, string> p, FlowActionContext c)
    {
        if (p.ContainsKey("prefab"))
            return SpawnStructure(player, p, c, "prefab", allowRealCastleGeometry: true);
        p["prefab"] = Text(p, "wallType", "TM_Castle_Wall_Tier02_Stone");
        return SpawnStructure(player, p, c, "prefab", allowRealCastleGeometry: true);
    }

    OperationResult PlaceFloor(Entity player, Dictionary<string, string> p, FlowActionContext c)
    {
        if (!EventGeometryMutationsEnabled)
        {
            BattleLuckPlugin.LogWarning("[FlowActionExecutor] floor.place skipped: event geometry spawning is disabled for server stability.");
            return OperationResult.Ok();
        }

        var prefabName = Text(p, "prefab", Text(p, "floorType", "TM_Castle_Floor_Tier02_Stone"));
        var prefab = ResolvePrefab(prefabName);
        if (!prefab.HasValue)
            return OperationResult.Fail("Unknown floor prefab.");

        var center = ResolvePosition(player, p, c);
        var width = Math.Max(0f, Float(p, "width", 0f));
        var length = Math.Max(0f, Float(p, "length", 0f));
        var spacing = Math.Clamp(Float(p, "spacing", 2.5f), 1f, 10f);
        var roomMode = Text(p, "roomMode", Text(p, "floorMode", "visual"));
        var visualOnly =
            !roomMode.Equals("castle", StringComparison.OrdinalIgnoreCase) &&
            !roomMode.Equals("admin", StringComparison.OrdinalIgnoreCase) &&
            !roomMode.Equals("owned", StringComparison.OrdinalIgnoreCase) &&
            !Bool(p, "ownedCastle", false) &&
            !Bool(p, "realCastle", false);

        if (visualOnly && !EventVisualFloorMutationsEnabled)
        {
            BattleLuckPlugin.LogWarning("[FlowActionExecutor] floor.place visual spawning skipped: live visual floor tiles are disabled for server stability. Use roomMode=freebuild after build.free for real build events.");
            return OperationResult.Ok();
        }

        if (!visualOnly && !CastleTileOwnershipService.TryEnsureAnchorForCurrentAdmin(out var anchorError))
            return OperationResult.Fail(anchorError);

        var estimatedColumns = Math.Max(1, (int)Math.Floor(Math.Max(spacing, width) / spacing) + 1);
        var estimatedRows = Math.Max(1, (int)Math.Floor(Math.Max(spacing, length) / spacing) + 1);
        var estimatedTiles = estimatedColumns * estimatedRows;
        const int safeFloorTileCap = 30;
        var requestedMaxTiles = Int(p, "maxTiles", Math.Min(estimatedTiles, safeFloorTileCap));
        var maxTiles = Math.Clamp(requestedMaxTiles, 1, safeFloorTileCap);
        if (requestedMaxTiles > safeFloorTileCap)
            BattleLuckPlugin.LogWarning($"[FlowActionExecutor] floor.place requested {requestedMaxTiles} tiles; clamped to {safeFloorTileCap} for server stability.");

        if (c.GameContext != null)
        {
            if (width <= spacing && length <= spacing)
            {
                width = spacing;
                length = spacing;
                maxTiles = 1;
            }

            var queued = Border(c).StartFloorFill(center, width, length, spacing, prefabName, maxTiles, visualOnly);
            c.GameContext.State["arenaSpawningRequested"] = true;
            c.GameContext.State["arenaFloorFillQueued"] = true;
            BattleLuckPlugin.LogInfo($"[FlowActionExecutor] floor.place queued {queued.Queued} tiles with {prefabName} (occupiedSkipped={queued.OccupiedSkipped}, deduped={queued.Deduped}, roomMode={(visualOnly ? "visual" : "castle")}).");
            return queued.Queued > 0 || queued.OccupiedSkipped > 0
                ? OperationResult.Ok()
                : OperationResult.Fail("No empty floor tiles found to queue.");
        }

        if (width <= spacing && length <= spacing)
        {
            if (!visualOnly)
            {
                if (!AdminTileBuildService.TryQueueTileBuild(prefab.Value, center, out var buildError))
                    return OperationResult.Fail($"Admin-owned floor build failed: {buildError}.");
                return OperationResult.Ok();
            }

            var entity = EntityExtensions.SpawnUnit(prefab.Value, Entity.Null, center);
            if (entity.Exists())
            {
                if (visualOnly)
                {
                    ProtectEventObject(entity);
                    EventTileSafety.StripRoomGraphComponents(VRisingCore.EntityManager, entity, stripTileGrid: true);
                    Track(c, "floors", entity);
                }
                else if (!CastleTileOwnershipService.TryStampOwnedTile(entity, center, prefabName, out var warning))
                {
                    try { entity.DestroyWithReason(); } catch { }
                    return OperationResult.Fail($"Castle floor ownership stamp failed: {warning}");
                }
            }
            return entity.Exists() ? OperationResult.Ok() : OperationResult.Fail("Floor spawn failed.");
        }

        var spawned = 0;
        for (var x = -width / 2f; x <= width / 2f && spawned < maxTiles; x += spacing)
        {
            for (var z = -length / 2f; z <= length / 2f && spawned < maxTiles; z += spacing)
            {
                var tilePosition = center + new float3(x, 0f, z);
                if (!visualOnly)
                {
                    if (AdminTileBuildService.TryQueueTileBuild(prefab.Value, tilePosition, out var buildError))
                    {
                        spawned++;
                        continue;
                    }

                    BattleLuckPlugin.LogWarning($"[FlowActionExecutor] Admin-owned floor tile skipped: {buildError}");
                    continue;
                }

                var entity = EntityExtensions.SpawnUnit(prefab.Value, Entity.Null, tilePosition);
                if (!entity.Exists())
                    continue;
                if (visualOnly)
                {
                    ProtectEventObject(entity);
                    EventTileSafety.StripRoomGraphComponents(VRisingCore.EntityManager, entity, stripTileGrid: true);
                    Track(c, "floors", entity);
                }
                else if (!CastleTileOwnershipService.TryStampOwnedTile(entity, center + new float3(x, 0f, z), prefabName, out var warning))
                {
                    try { entity.DestroyWithReason(); } catch { }
                    return OperationResult.Fail($"Castle floor ownership stamp failed: {warning}");
                }
                spawned++;
            }
        }

        BattleLuckPlugin.LogInfo($"[FlowActionExecutor] floor.place filled {spawned} tiles with {prefabName} (roomMode={(visualOnly ? "visual" : "castle")}).");
        return spawned > 0 ? OperationResult.Ok() : OperationResult.Fail("No floor tiles spawned.");
    }

OperationResult PlaceZoneBorder(FlowActionContext c, Dictionary<string, string> p)
     {
         if (!EventGeometryMutationsEnabled)
         {
             BattleLuckPlugin.LogWarning("[FlowActionExecutor] zone.border.place skipped: event geometry spawning is disabled for server stability.");
             return OperationResult.Ok();
         }

         if (c.Zone == null)
             return OperationResult.Fail("Zone is required for zone.border.place.");
         var border = Border(c);
         var cfg = c.Zone.Boundary?.Walls ?? new WallBoundaryConfig();
         var merged = new WallBoundaryConfig
         {
             Enabled = true,
             Height = Float(p, "height", cfg.Height),
             Spacing = Float(p, "spacing", cfg.Spacing),
             BatchSize = Int(p, "batchSize", cfg.BatchSize),
             WallPrefab = p.TryGetValue("wallPrefab", out var wp) ? wp : cfg.WallPrefab,
             FloorPrefab = p.TryGetValue("floorPrefab", out var fp) ? fp : cfg.FloorPrefab,
             SpawnWalls = p.TryGetValue("spawnWalls", out var sw) ? sw.Equals("true", StringComparison.OrdinalIgnoreCase) : cfg.SpawnWalls,
             SpawnFloors = false,
             RequireOnlineAdmin = p.TryGetValue("requireOnlineAdmin", out var roa) ? roa.Equals("true", StringComparison.OrdinalIgnoreCase) : cfg.RequireOnlineAdmin,
             FloorSpacing = p.TryGetValue("floorSpacing", out var fs) && float.TryParse(fs, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var fsv) ? fsv : cfg.FloorSpacing,
             Buffs = cfg.Buffs,
             Timers = cfg.Timers,
             Glow = cfg.Glow,
         };
         var modeId = c.Config?.ModeId ?? "bloodbath";
         try
         {
              border.StartZoneBoundary(modeId, ZoneCenter(c.Zone), c.Zone.Radius, merged);
              if (c.GameContext != null)
              {
                  c.GameContext.State["arenaSpawningRequested"] = true;
                  c.GameContext.State["arenaBorderQueued"] = true;
              }
              ApplyBorderEffects(merged, c);
         }
         catch (Exception ex)
         {
             BattleLuckPlugin.LogWarning($"[FlowActionExecutor] zone.border.place degraded: {ex.Message}");
         }
         return OperationResult.Ok();
     }

OperationResult RemoveZoneBorder(FlowActionContext c)
      {
          var cfg = c.Zone?.Boundary?.Walls;
          if (cfg != null) RemoveBorderEffects(cfg, c);
          Border(c).DespawnWalls();
          Border(c).DespawnFloors();
          return OperationResult.Ok();
      }

OperationResult PlaceAllZoneBorders(FlowActionContext c, Dictionary<string, string> p)
    {
        if (!EventGeometryMutationsEnabled)
        {
            BattleLuckPlugin.LogWarning("[FlowActionExecutor] zone.border.place_all skipped: event geometry spawning is disabled for server stability.");
            return OperationResult.Ok();
        }

        var config = c.Config;
        if (config?.Zones?.Zones == null || config.Zones.Zones.Count == 0)
            return OperationResult.Ok();

        var border = Border(c);
        var modeId = config.ModeId ?? "bloodbath";
        int placed = 0;

foreach (var zone in config.Zones.Zones)
         {
             var zoneCfg = zone.Boundary?.Walls ?? new WallBoundaryConfig();
             var merged = MergeBorderConfig(zoneCfg, p);
             try
             {
                  border.StartZoneBoundary(modeId, ZoneCenter(zone), zone.Radius, merged);
                  if (c.GameContext != null)
                  {
                      c.GameContext.State["arenaSpawningRequested"] = true;
                      c.GameContext.State["arenaBorderQueued"] = true;
                  }
                  ApplyBorderEffects(merged, c);
                 placed++;
             }
             catch (Exception ex)
             {
                 BattleLuckPlugin.LogWarning($"[FlowActionExecutor] zone.border.place_all skipped zone {zone.Hash}: {ex.Message}");
             }
         }

        BattleLuckPlugin.LogInfo($"[FlowActionExecutor] zone.border.place_all placed borders on {placed} zones for mode {modeId}.");
        return OperationResult.Ok();
    }

    OperationResult RemoveAllZoneBorders(FlowActionContext c)
    {
        Border(c).DespawnWalls();
        Border(c).DespawnFloors();
        var applied = BorderApplied(c);
        applied.Clear();
        BattleLuckLogger.Info("[FlowActionExecutor] zone.border.remove_all cleared all zone borders.");
        return OperationResult.Ok();
    }

static WallBoundaryConfig MergeBorderConfig(WallBoundaryConfig cfg, Dictionary<string, string> p)
     {
         return new WallBoundaryConfig
         {
             Enabled = true,
             Height = Float(p, "height", cfg.Height),
             Spacing = Float(p, "spacing", cfg.Spacing),
             BatchSize = Int(p, "batchSize", cfg.BatchSize),
             WallPrefab = p.TryGetValue("wallPrefab", out var wp) ? wp : cfg.WallPrefab,
             FloorPrefab = p.TryGetValue("floorPrefab", out var fp) ? fp : cfg.FloorPrefab,
             SpawnWalls = p.TryGetValue("spawnWalls", out var sw) ? sw.Equals("true", StringComparison.OrdinalIgnoreCase) : cfg.SpawnWalls,
             SpawnFloors = false,
             RequireOnlineAdmin = p.TryGetValue("requireOnlineAdmin", out var roa) ? roa.Equals("true", StringComparison.OrdinalIgnoreCase) : cfg.RequireOnlineAdmin,
             FloorSpacing = p.TryGetValue("floorSpacing", out var fs) && float.TryParse(fs, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var fsv) ? fsv : cfg.FloorSpacing,
             Buffs = cfg.Buffs,
             Timers = cfg.Timers,
             Glow = cfg.Glow,
         };
     }

void ApplyBorderEffects(WallBoundaryConfig cfg, FlowActionContext c)
     {
         // Set zone effect flags in context state for BorderController
         if (cfg.Glow?.DisableSunEffects == true && c.GameContext != null)
             c.GameContext.State["disableSunEffects"] = true;
         if (cfg.Glow?.NpcFriendly == true && c.GameContext != null)
             c.GameContext.State["npcFriendly"] = true;

var applied = BorderApplied(c);
          var buffs = cfg.Buffs ?? new List<BorderBuffEntry>();
          foreach (var entry in buffs)
         {
             if (string.IsNullOrWhiteSpace(entry.Prefab)) continue;
             // Dedup: same (zone, buff, player) tuple cannot be applied twice while the border is up
             var buffKey = $"buff_{entry.Prefab}_{c.ZoneHash}";
             foreach (var player in PlayersInContext(c))
             {
                 var pkey = $"{buffKey}_{player.GetSteamId()}";
                 if (!applied.Add(pkey)) continue;
                 // Immediate side-effect: BuffEntity with -1 -> 0f means permanent.
                 if (ResolvePrefab(entry.Prefab) is { } resolved)
                     player.BuffEntity(resolved, out _, entry.Duration < 0f ? 0f : entry.Duration);
             }
         }
foreach (var entry in cfg.Timers ?? new List<BorderTimerEntry>())
          {
              if (string.IsNullOrWhiteSpace(entry.TimerId)) continue;
              if (entry.Duration < 0f) continue;  // -1 = disabled
              var tkey = $"timer_{entry.TimerId}";
              if (!applied.Add(tkey)) continue;
              Timers(c.GameContext)[entry.TimerId] = DateTime.UtcNow.AddSeconds(entry.Duration);
              if (entry.Repeat) BorderTimerRepeat(c)[entry.TimerId] = entry;
              BattleLuckPlugin.LogInfo($"[FlowActionExecutor] border timer '{entry.TimerId}' started ({entry.Duration}s).");
          }
      }

void RemoveBorderEffects(WallBoundaryConfig cfg, FlowActionContext c)
      {
          // Clear zone effect flags
          c.GameContext?.State.Remove("disableSunEffects");
          c.GameContext?.State.Remove("npcFriendly");

          var applied = BorderApplied(c);
          var buffs2 = cfg.Buffs ?? new List<BorderBuffEntry>();
          foreach (var entry in buffs2)
          {
              if (string.IsNullOrWhiteSpace(entry.Prefab)) continue;
             var buffKey = $"buff_{entry.Prefab}_{c.ZoneHash}";
             foreach (var player in PlayersInContext(c))
             {
                  if (ResolvePrefab(entry.Prefab) is { } resolved)
                      player.TryRemoveBuff(resolved);
                  applied.Remove($"{buffKey}_{player.GetSteamId()}");
              }
          }
          var timers2 = cfg.Timers ?? new List<BorderTimerEntry>();
          foreach (var entry in timers2)
          {
              if (string.IsNullOrWhiteSpace(entry.TimerId)) continue;
              Timers(c.GameContext).Remove(entry.TimerId);
              BorderTimerRepeat(c).Remove(entry.TimerId);
              applied.Remove($"timer_{entry.TimerId}");
          }
      }

    static HashSet<string> BorderApplied(FlowActionContext c) =>
        GetState<HashSet<string>>(c.GameContext, "border_applied", () => new(StringComparer.OrdinalIgnoreCase));

    static Dictionary<string, BorderTimerEntry> BorderTimerRepeat(FlowActionContext c) =>
        GetState<Dictionary<string, BorderTimerEntry>>(c.GameContext, "border_timer_repeat", () => new(StringComparer.OrdinalIgnoreCase));

    // ECS action dispatch
    // These create ECS action entities for future ECS systems to consume.
    // SessionEntity is not yet modeled as an Entity -- Entity.Null is the canonical placeholder.

    void DispatchZoneBuffApply(FlowActionContext c, Entity target, string prefabName, float duration, int zoneHash)
    {
        try
        {
            var ecb = EcbHelper.GetEcb();
            if (ecb.Equals(default(EntityCommandBuffer))) return;
            var e = ecb.CreateEntity();
            ecb.AddComponent(e, new ZoneBuffApplyAction
            {
                TargetEntity = target,
                ZoneHash = zoneHash,
                BuffPrefab = ToFixed64(prefabName),
                Duration = duration, // -1 = permanent
                SessionEntity = Entity.Null
            });
            BattleLuckPlugin.LogInfo($"[FlowActionExecutor] ECS ZoneBuffApplyAction dispatched: zone={zoneHash} prefab={prefabName} duration={duration}.");
        }
        catch (Exception ex)
        {
            BattleLuckPlugin.LogWarning($"[FlowActionExecutor] ECS ZoneBuffApplyAction skipped: {ex.Message}");
        }
    }

    void DispatchZoneBuffRemove(FlowActionContext c, Entity target, string prefabName, int zoneHash)
    {
        try
        {
            var ecb = EcbHelper.GetEcb();
            if (ecb.Equals(default(EntityCommandBuffer))) return;
            var e = ecb.CreateEntity();
            ecb.AddComponent(e, new ZoneBuffRemoveAction
            {
                TargetEntity = target,
                ZoneHash = zoneHash,
                BuffPrefab = ToFixed64(prefabName),
                SessionEntity = Entity.Null
            });
            BattleLuckPlugin.LogInfo($"[FlowActionExecutor] ECS ZoneBuffRemoveAction dispatched: zone={zoneHash} prefab={prefabName}.");
        }
        catch (Exception ex)
        {
            BattleLuckPlugin.LogWarning($"[FlowActionExecutor] ECS ZoneBuffRemoveAction skipped: {ex.Message}");
        }
    }

    void DispatchPlayerBuffApply(Entity target, string prefabName, float duration, int stackCount)
    {
        try
        {
            var ecb = EcbHelper.GetEcb();
            if (ecb.Equals(default(EntityCommandBuffer))) return;
            var e = ecb.CreateEntity();
            ecb.AddComponent(e, new PlayerBuffApplyAction
            {
                TargetEntity = target,
                BuffPrefab = ToFixed64(prefabName),
                Duration = duration, // -1 = permanent
                StackCount = stackCount,
                SessionEntity = Entity.Null
            });
        }
        catch (Exception ex)
        {
            BattleLuckPlugin.LogWarning($"[FlowActionExecutor] ECS PlayerBuffApplyAction skipped: {ex.Message}");
        }
    }

    void DispatchPlayerBuffRemove(Entity target, string prefabName)
    {
        try
        {
            var ecb = EcbHelper.GetEcb();
            if (ecb.Equals(default(EntityCommandBuffer))) return;
            var e = ecb.CreateEntity();
            ecb.AddComponent(e, new PlayerBuffRemoveAction
            {
                TargetEntity = target,
                BuffPrefab = ToFixed64(prefabName),
                SessionEntity = Entity.Null
            });
        }
        catch (Exception ex)
        {
            BattleLuckPlugin.LogWarning($"[FlowActionExecutor] ECS PlayerBuffRemoveAction skipped: {ex.Message}");
        }
    }

    void DispatchShrinkZone(FlowActionContext c, bool apply, Dictionary<string, string> p)
    {
        try
        {
            var ecb = EcbHelper.GetEcb();
            if (ecb.Equals(default(EntityCommandBuffer))) return;
            var e = ecb.CreateEntity();
            if (apply)
            {
                ecb.AddComponent(e, new ShrinkZoneAction
                {
                    TargetEntity = c.PlayerCharacter,
                    ZoneHash = c.ZoneHash,
                    TargetRadius = Float(p, "targetRadius", 10f),
                    ShrinkRate = Float(p, "shrinkRate", 0.5f),
                    WarningDuration = Float(p, "warningDuration", 5f),
                    SessionEntity = Entity.Null
                });
            }
            else
            {
                ecb.AddComponent(e, new ShrinkZoneStopAction
                {
                    TargetEntity = c.PlayerCharacter,
                    ZoneHash = c.ZoneHash,
                    SessionEntity = Entity.Null
                });
            }
        }
        catch (Exception ex)
        {
            BattleLuckPlugin.LogWarning($"[FlowActionExecutor] ECS ShrinkZoneAction skipped: {ex.Message}");
        }
    }

    static FixedString64Bytes ToFixed64(string s)
    {
        if (string.IsNullOrEmpty(s)) return default;
        // FixedString64Bytes caps at 61 UTF-8 bytes; truncate safely
        return s.Length <= 61 ? new FixedString64Bytes(s) : new FixedString64Bytes(s.Substring(0, 61));
    }
    OperationResult DoorBestEffort(string actionName, Entity player, Dictionary<string, string> p, FlowActionContext c)
    {
        var state = State(c, "doors");
        state["lastAction"] = actionName;
        state["lastPosition"] = ResolvePosition(player, p, c);
        BattleLuckPlugin.LogInfo($"[FlowActionExecutor] {actionName} recorded as best-effort door state.");
        return OperationResult.Ok();
    }

    OperationResult SummonMount(Entity player, Dictionary<string, string> p, FlowActionContext c)
    {
        var mountType = Text(p, "mountType", "horse");
        var prefabName = mountType.Equals("horse", StringComparison.OrdinalIgnoreCase)
            ? "CHAR_Mount_Horse"
            : mountType;
        var prefab = ResolvePrefab(prefabName);
        if (!prefab.HasValue)
        {
            BattleLuckPlugin.LogWarning($"[FlowActionExecutor] Mount prefab '{prefabName}' not found; recording best-effort mount state.");
            State(c, "mounts")["lastSummon"] = mountType;
            return OperationResult.Ok();
        }
        var entity = EntityExtensions.SpawnUnit(prefab.Value, Entity.Null, ResolvePosition(player, p, c));
        if (entity.Exists())
            Track(c, "mounts", entity);
        return entity.Exists() ? OperationResult.Ok() : OperationResult.Fail("Mount spawn failed.");
    }

    OperationResult ZoneBuff(FlowActionContext c, Dictionary<string, string> p, bool apply)
    {
        var prefabName = Required(p, "buffPrefab");
        if (string.IsNullOrWhiteSpace(prefabName))
            return OperationResult.Fail("buffPrefab is required.");

        var buffPrefab = ResolvePrefab(prefabName) ?? PrefabGUID.Empty;
        if (buffPrefab == PrefabGUID.Empty)
            return OperationResult.Fail($"Unknown buff prefab: {prefabName}");

        var duration = EffectDuration(p, c, -1f, "duration", "durationSeconds");
        var mirrorEcs = Bool(p, "ecs", false);
        var players = PlayersInContext(c).ToList();
        int affected = 0;

        if (apply)
        {
            foreach (var player in players)
            {
                if (!player.Exists()) continue;
                if (player.BuffEntity(buffPrefab, out _, duration))
                {
                    affected++;
                    if (mirrorEcs)
                        DispatchZoneBuffApply(c, player, prefabName, duration, c.ZoneHash);
                }
            }
        }
        else
        {
            foreach (var player in players)
            {
                if (!player.Exists()) continue;
                player.TryRemoveBuff(buffPrefab);
                affected++;
                if (mirrorEcs)
                    DispatchZoneBuffRemove(c, player, prefabName, c.ZoneHash);
            }
        }

        BattleLuckPlugin.LogInfo($"[FlowActionExecutor] zone.buff.{(apply ? "apply" : "remove")} {prefabName} affected {affected} player(s) in zone {c.ZoneHash}.");
        return OperationResult.Ok();
    }

    OperationResult PlayerBuff(Entity player, Dictionary<string, string> p, FlowActionContext c, bool apply)
    {
        var prefabName = Required(p, "buffPrefab");
        if (string.IsNullOrWhiteSpace(prefabName))
            return OperationResult.Fail("buffPrefab is required.");
        var duration = EffectDuration(p, c, -1f, "duration", "durationSeconds");
        var stack = Int(p, "stackCount", 1);
        var mirrorEcs = Bool(p, "ecs", false) || Bool(p, "mirrorEcs", false);
        if (apply)
        {
            var buffPrefab = ResolvePrefab(prefabName) ?? PrefabGUID.Empty;
            if (buffPrefab == PrefabGUID.Empty)
                return OperationResult.Fail($"Unknown buff prefab: {prefabName}");
            if (!player.BuffEntity(buffPrefab, out _, duration))
                return OperationResult.Fail($"Buff prefab '{prefabName}' could not be applied in this server build.");
            if (mirrorEcs)
                DispatchPlayerBuffApply(player, prefabName, duration, stack);
        }
        else
        {
            if (ResolvePrefab(prefabName) is { } buffPrefab && buffPrefab != PrefabGUID.Empty)
                player.TryRemoveBuff(buffPrefab);
            if (mirrorEcs)
                DispatchPlayerBuffRemove(player, prefabName);
        }
        return OperationResult.Ok();
    }

    OperationResult PlaceTrap(Entity player, Dictionary<string, string> p, FlowActionContext c)
    {
        var traps = Traps(c);
        traps.Add(new TrapState(ResolvePosition(player, p, c), Text(p, "trapType", "spike"),
            Float(p, "damage", 25f), Float(p, "radius", 3f),
            DateTime.UtcNow.AddSeconds(Math.Max(1f, Float(p, "duration", 300f)))));
        return OperationResult.Ok();
    }

    OperationResult TriggerTraps(Entity player, Dictionary<string, string> p, FlowActionContext c)
    {
        var position = ResolvePosition(player, p, c);
        var radius = Float(p, "radius", 5f);
        foreach (var target in PlayersInContext(c))
        {
            if (math.distance(target.GetPosition(), position) <= radius)
                target.DealDamagePercent(0.15f);
        }
        return OperationResult.Ok();
    }

    OperationResult RemoveTraps(FlowActionContext c, Dictionary<string, string> p)
    {
        Traps(c).Clear();
        return OperationResult.Ok();
    }

    OperationResult PlaySequence(Entity player, Dictionary<string, string> p)
    {
        if (int.TryParse(Text(p, "sequenceGuid", ""), out var guid))
            player.PlaySequence(new SequenceGUID(guid), label: Text(p, "sequencePrefab", "flow"));
        else
            player.PlaySequence(ActionSequences.ContestCountdown, label: Text(p, "sequencePrefab", "flow"));
        return OperationResult.Ok();
    }

    OperationResult ExecuteCustomSequence(string actionName, Dictionary<string, string> p, FlowActionContext c)
    {
        var sequenceId = Text(p, "sequenceId", Text(p, "id", Text(p, "name", Text(p, "sequence", ""))));
        if (string.IsNullOrWhiteSpace(sequenceId))
            return OperationResult.Fail("sequence.custom.* requires sequenceId/id/name.");

        var service = new CustomSequenceService();
        var get = service.Get(sequenceId);
        if (!get.Success || get.Value == null)
            return OperationResult.Fail(get.Error ?? $"Custom sequence '{sequenceId}' was not found.");

        if (actionName.EndsWith(".preview", StringComparison.OrdinalIgnoreCase))
        {
            BattleLuckPlugin.LogInfo("[FlowActionExecutor] Custom sequence preview:\n" + service.RenderPreview(get.Value));
            return OperationResult.Ok();
        }

        var result = service.ExecuteImmediate(get.Value.Id, this, c);
        if (!result.Success || result.Value == null)
            return OperationResult.Fail(result.Error ?? $"Custom sequence '{sequenceId}' failed.");

        BattleLuckPlugin.LogInfo(
            $"[FlowActionExecutor] Custom sequence '{get.Value.Id}' executed: " +
            $"{result.Value.Executed} action(s), {result.Value.SkippedTimingMarkers} timing marker(s) skipped for immediate execution.");
        return OperationResult.Ok();
    }

    OperationResult RunSequenceStep(Entity player, Dictionary<string, string> p, FlowActionContext c)
    {
        var sequenceId = Text(p, "sequenceId", Text(p, "id", ""));
        var stepId = Text(p, "stepId", Text(p, "id", Text(p, "step", "")));
        if (string.IsNullOrWhiteSpace(sequenceId) || string.IsNullOrWhiteSpace(stepId))
            return OperationResult.Fail("sequence.step.run requires sequenceId and stepId.");

        if (!Registry.TryGetSequence(sequenceId, out var sequence) || sequence == null)
            return OperationResult.Fail($"Sequence '{sequenceId}' not found in action registry.");

        var step = sequence.Steps.FirstOrDefault(s => s.Id.Equals(stepId, StringComparison.OrdinalIgnoreCase)
                                                  || s.ActionId.Equals(stepId, StringComparison.OrdinalIgnoreCase));
        if (step == null)
            return OperationResult.Fail($"Step '{stepId}' not found in sequence '{sequenceId}'.");

        if (!Registry.TryResolveStepToActionString(step, out var actionString))
            return OperationResult.Fail($"Could not resolve step '{stepId}' in sequence '{sequenceId}' to an action.");

        var mergedParams = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (step.Params != null)
        {
            foreach (var kv in step.Params)
            {
                mergedParams[kv.Key] = kv.Value.ValueKind == JsonValueKind.String
                    ? kv.Value.GetString() ?? ""
                    : kv.Value.GetRawText();
            }
        }
        foreach (var kv in p)
            mergedParams[kv.Key] = kv.Value;

        return Execute(actionString, c);
    }

    OperationResult SkipSequenceStep(Entity player, Dictionary<string, string> p, FlowActionContext c)
    {
        var sequenceId = Text(p, "sequenceId", Text(p, "id", ""));
        var stepId = Text(p, "stepId", Text(p, "id", Text(p, "step", "")));
        BattleLuckPlugin.LogInfo($"[FlowActionExecutor] Skipping step '{stepId}' in sequence '{sequenceId}'.");
        return OperationResult.Ok();
    }

    OperationResult RetrySequenceStep(Entity player, Dictionary<string, string> p, FlowActionContext c)
    {
        var sequenceId = Text(p, "sequenceId", Text(p, "id", ""));
        var stepId = Text(p, "stepId", Text(p, "id", Text(p, "step", "")));
        var maxRetries = Int(p, "maxRetries", 3);
        BattleLuckPlugin.LogInfo($"[FlowActionExecutor] Retrying step '{stepId}' in sequence '{sequenceId}' (max={maxRetries}).");
        return RunSequenceStep(player, p, c);
    }

    OperationResult GrantPrefab(Entity player, Dictionary<string, string> p)
    {
        var prefabName = Required(p, "prefab");
        var amount = Int(p, "amount", 1);
        var guid = PrefabHelper.GetPrefabGuid(prefabName);
        if (!guid.HasValue)
            return OperationResult.Fail($"Unknown prefab: {prefabName}");
        if (!player.TryGiveItem(guid.Value, amount))
            return OperationResult.Fail($"Failed to grant prefab: {prefabName}");
        return OperationResult.Ok();
    }

    OperationResult QueryPrefab(Entity player, Dictionary<string, string> p)
    {
        var prefabName = Required(p, "prefab");
        var guid = PrefabHelper.GetPrefabGuid(prefabName);
        if (!guid.HasValue)
            return OperationResult.Fail($"Prefab not found: {prefabName}");
        player.SetPosition(player.GetPosition());
        BattleLuckPlugin.LogInfo($"[QueryPrefab] {prefabName} => {guid.Value.GuidHash}");
        return OperationResult.Ok();
    }

    OperationResult Glow(Entity player, Dictionary<string, string> p, FlowActionContext c, bool apply)
    {
        var prefab = Prefabs.Buff_InCombat != PrefabGUID.Empty ? Prefabs.Buff_InCombat : Prefabs.Buff_General_Ignite;
        if (apply)
            return ApplyTimedBuff(player, prefab, EffectDuration(p, c, -1f, "duration", "durationSeconds"));
        player.TryRemoveBuff(prefab);
        return OperationResult.Ok();
    }

    OperationResult AutoFly(Entity player, Dictionary<string, string> p)
    {
        if (TryParseFloat3(Text(p, "targetPosition", ""), out var target))
            player.SetPosition(target);
        else
            return OperationResult.Fail("Invalid targetPosition.");
        return OperationResult.Ok();
    }

    OperationResult ConsumeRevive(Entity player, Dictionary<string, string> p, FlowActionContext c)
    {
        var revives = Revives(c.GameContext);
        var steamId = player.GetSteamId();
        if (!revives.TryGetValue(steamId, out var lives) || lives <= 0)
            return OperationResult.Fail("No revive lives available.");
        revives[steamId] = Math.Max(0, lives - Int(p, "count", 1));
        player.HealToFull();
        if (c.Zone != null)
            player.SetPosition(c.Zone.TeleportSpawn.ToFloat3());
        return OperationResult.Ok();
    }

    OperationResult CaptureObjective(Dictionary<string, string> p, FlowActionContext c)
    {
        if (c.GameContext == null)
            return OperationResult.Ok();
        var id = Required(p, "objectiveId");
        var objectives = ObjectiveStates(c.GameContext);
        objectives[id] = Int(p, "teamId", 0);
        GameEvents.RaiseObjectiveCaptured(new ObjectiveCapturedEvent
        {
            SessionId = c.GameContext.SessionId,
            ObjectiveId = id,
            TeamId = objectives[id]
        });
        return OperationResult.Ok();
    }

    OperationResult ClanTaskAction(string actionName, Entity player, Dictionary<string, string> p, FlowActionContext c)
    {
        var service = BattleLuckPlugin.ClanTasks;
        if (service == null)
            return OperationResult.Fail("Clan task service is not initialized.");

        if (actionName == "clan.task.list")
        {
            var page = Math.Max(1, Int(p, "page", 1));
            var message = ClanTaskPresenter.BuildPage(service.ListForPlayer(
                player.GetSteamId(),
                clanId: ClanTaskGameAdapter.ResolveClanId(player),
                callerEventId: c.ModeId,
                callerSessionId: c.GameContext?.SessionId,
                restrictEventTasksToCallerSession: true), page);
            return BattleLuckPlugin.TryNotifyPlayerBySteamId(player.GetSteamId(), message)
                ? OperationResult.Ok()
                : OperationResult.Fail("Player is not online to receive the clan task list.");
        }

        var taskId = Required(p, "taskId");
        if (actionName == "clan.task.cancel")
            return service.Cancel(taskId);
        if (actionName == "clan.task.complete")
            return service.Complete(
                taskId,
                player.GetSteamId(),
                callerEventId: c.ModeId,
                callerSessionId: c.GameContext?.SessionId);
        if (actionName is "clan.task.update" or "clan.task.progress")
        {
            var result = service.AddProgress(taskId, Math.Max(1, Int(p, "amount", 1)), player.GetSteamId(), trustedGather: true,
                callerEventId: c.ModeId, callerSessionId: c.GameContext?.SessionId);
            return result.Success ? OperationResult.Ok() : OperationResult.Fail(result.Error ?? "Clan task progress failed.");
        }
        if (actionName == "clan.task.reward")
        {
            // Catalog-backed reward dispatch: fire score.add through the action executor
            // instead of embedding score mutation inside the task service.
            var rewardPoints = Int(p, "rewardPoints", Int(p, "points", 0));
            if (rewardPoints <= 0)
                return OperationResult.Fail("clan.task.reward requires rewardPoints > 0.");
            var rewardAction = $"score.add:points={rewardPoints}|reason=clan_task_reward";
            return Execute(rewardAction, c);
        }

        var objectiveRaw = Text(p, "objectiveType", Text(p, "objective", "manual"));
        var objectiveType = objectiveRaw.Trim().ToLowerInvariant() switch
        {
            "boss" or "bosskill" or "boss_kill" or "killboss" => ClanTaskObjectiveType.BossKill,
            "gather" or "gatheritem" or "gather_item" or "resource" => ClanTaskObjectiveType.GatherItem,
            _ => ClanTaskObjectiveType.Manual
        };
        var prefabName = Text(p, "itemPrefab", Text(p, "bossPrefab", Text(p, "prefab", "")));
        var prefabHash = Int(p, "prefabGuid", Int(p, "prefabGuidHash", 0));
        if (prefabHash == 0 && !string.IsNullOrWhiteSpace(prefabName))
            prefabHash = (PrefabHelper.GetPrefabGuidDeep(prefabName) ?? PrefabHelper.GetLivePrefabGuid(prefabName))?.GuidHash ?? 0;

        var scope = Text(p, "scope", "world").Equals("event", StringComparison.OrdinalIgnoreCase)
            ? ClanTaskScope.Event
            : ClanTaskScope.World;
        var requestedEventId = Text(p, "eventId", "");
        var requestedSessionId = Text(p, "sessionId", "");
        if (scope == ClanTaskScope.Event)
        {
            if (c.GameContext == null || string.IsNullOrWhiteSpace(c.ModeId) || string.IsNullOrWhiteSpace(c.GameContext.SessionId))
                return OperationResult.Fail("Event clan tasks require an active event session.");
            if (!string.IsNullOrWhiteSpace(requestedEventId) &&
                !requestedEventId.Equals(c.ModeId, StringComparison.OrdinalIgnoreCase))
            {
                return OperationResult.Fail($"Event clan task eventId '{requestedEventId}' does not match active event '{c.ModeId}'.");
            }
            if (!string.IsNullOrWhiteSpace(requestedSessionId) &&
                !requestedSessionId.Equals(c.GameContext.SessionId, StringComparison.OrdinalIgnoreCase))
            {
                return OperationResult.Fail($"Event clan task sessionId '{requestedSessionId}' does not match active session '{c.GameContext.SessionId}'.");
            }
        }
        var assignees = new HashSet<ulong>();
        var assigneeRaw = Text(p, "assigneeSteamId", Text(p, "assignee", ""));
        foreach (var token in assigneeRaw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            if (ulong.TryParse(token, out var steamId) && steamId != 0) assignees.Add(steamId);

        DateTime? expiresAt = null;
        var expiresRaw = Text(p, "expiresAt", "");
        if (DateTime.TryParse(expiresRaw, System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal, out var parsedExpiry))
            expiresAt = parsedExpiry;

        var create = service.Create(new CreateClanTaskRequest
        {
            TaskId = taskId,
            Description = Required(p, "description"),
            ClanId = Text(p, "clanId", ClanTaskGameAdapter.ResolveClanId(player)),
            AssignedSteamIds = assignees,
            Scope = scope,
            EventId = scope == ClanTaskScope.Event ? c.ModeId : "",
            SessionId = scope == ClanTaskScope.Event ? c.GameContext!.SessionId : "",
            ObjectiveType = objectiveType,
            PrefabGuidHash = prefabHash,
            PrefabName = prefabName,
            TargetAmount = Int(p, "targetAmount", Int(p, "amount", 0)),
            ExpiresAtUtc = expiresAt,
            RewardPoints = Int(p, "rewardPoints", 0)
        });
        return create.Success ? OperationResult.Ok() : OperationResult.Fail(create.Error ?? "Clan task creation failed.");
    }

    OperationResult DeliverObjective(Entity player, Dictionary<string, string> p, FlowActionContext c)
    {
        if (c.GameContext == null)
            return OperationResult.Fail("objective.deliver requires an active event session.");

        var objectiveId = Text(p, "objectiveId", "delivery");
        var amount = Math.Max(1, Int(p, "amount", 1));
        var radius = Math.Max(1f, Float(p, "radius", 4f));
        var destination = ResolvePosition(player, p, c);
        if (Bool(p, "watch", Bool(p, "register", Bool(p, "start", false))))
        {
            var deliveries = GetState<Dictionary<string, DeliveryObjectiveState>>(c.GameContext, "deliveryObjectives", () => new(StringComparer.OrdinalIgnoreCase));
            var itemGuid = ResolveObjectiveItem(p);
            deliveries[objectiveId] = new DeliveryObjectiveState
            {
                ObjectiveId = objectiveId,
                ItemGuidHash = itemGuid?.GuidHash,
                Amount = amount,
                Position = destination,
                Radius = radius,
                RewardPoints = Int(p, "rewardPoints", Int(p, "points", 25)),
                TeamId = OptionalTeamId(p),
                Message = Text(p, "message", ""),
                Repeatable = Bool(p, "repeatable", false),
                Enabled = true
            };

            c.GameContext.Broadcast?.Invoke($"Delivery objective armed: {objectiveId}.");
            return OperationResult.Ok();
        }

        var dist = math.distance(player.GetPosition().xz, destination.xz);
        if (dist > radius)
            return OperationResult.Fail($"Delivery target is {dist:F1}m away; required radius is {radius:F1}m.");

        var item = ResolveObjectiveItem(p);
        if (item.HasValue)
        {
            var have = CountInventoryItem(player, item.Value);
            if (have < amount)
                return OperationResult.Fail($"Need {amount} item(s) for objective '{objectiveId}', player has {have}.");
            if (!player.TryRemoveItem(item.Value, amount))
                return OperationResult.Fail($"Failed to remove delivered item for objective '{objectiveId}'.");
        }

        var points = Int(p, "rewardPoints", Int(p, "points", 25));
        if (OptionalTeamId(p) is { } teamId)
            c.GameContext.Scores.AddTeamScore(teamId, points);
        else
            c.GameContext.Scores.AddPlayerScore(player.GetSteamId(), points);

        var objectives = ObjectiveStates(c.GameContext);
        objectives[objectiveId] = OptionalTeamId(p) ?? 0;
        GameEvents.RaiseObjectiveCaptured(new ObjectiveCapturedEvent
        {
            SessionId = c.GameContext.SessionId,
            ObjectiveId = objectiveId,
            TeamId = objectives[objectiveId]
        });

        var message = Text(p, "message", "");
        c.GameContext.Broadcast?.Invoke(string.IsNullOrWhiteSpace(message)
            ? $"Objective delivered: {objectiveId} (+{points})."
            : message);
        return OperationResult.Ok();
    }

    void StartManualShrink(FlowActionContext c, Dictionary<string, string> p)
    {
        if (c.GameContext == null || c.Zone == null)
            return;

        var center = ZoneCenter(c.Zone);
        if (p.TryGetValue("center", out var centerText) && TryParseFloat3(centerText, out var parsedCenter))
            center = parsedCenter;

        var initialRadius = Float(p, "startRadius", c.Zone.Radius > 0f ? c.Zone.Radius : 30f);
        c.GameContext.State["manualShrink"] = new ManualShrinkState
        {
            Enabled = true,
            Center = center,
            CurrentRadius = initialRadius,
            TargetRadius = Float(p, "targetRadius", Math.Max(5f, initialRadius * 0.5f)),
            ShrinkRatePerSecond = Float(p, "shrinkRate", 0.25f),
            ExitBuffer = Float(p, "exitBuffer", 5f),
            DamageOnly = Bool(p, "damageOnly", true),
            NextBroadcastUtc = DateTime.UtcNow
        };
        c.GameContext.State["boundaryDamageOnly"] = Bool(p, "damageOnly", true);
    }

    OperationResult AiBestEffort(string actionName, Entity player, Dictionary<string, string> p, FlowActionContext c)
    {
        if (actionName == "ai.spawn_group")
            return SpawnNpcGroup(player, p, c);
        State(c, "ai")[actionName] = string.Join("|", p.Select(kv => $"{kv.Key}={kv.Value}"));
        return OperationResult.Ok();
    }

    // Boss Servant Actions
    OperationResult BossAddServant(Entity player, Dictionary<string, string> p, FlowActionContext c)
    {
        if (!EventBossServantMutationsEnabled)
            return SkipBossServantMutation("boss.add_servant");

        var servantPrefab = ResolvePrefab(Required(p, "prefab"));
        if (!servantPrefab.HasValue)
            return OperationResult.Fail("Unknown servant prefab.");

        var servantType = ServantEnumParsers.ParseServantType(Text(p, "servantType", "Guard"));
        var servantFaction = ServantEnumParsers.ParseServantFaction(Text(p, "servantFaction", "Unknown"));

        DispatchBossAddServant(c, player, servantPrefab.Value, servantType, servantFaction);
        BattleLuckPlugin.LogInfo($"[FlowActionExecutor] BossAddServant dispatched: boss={player.Index} prefab={servantPrefab.Value.GuidHash}");
        return OperationResult.Ok();
    }

    OperationResult BossRemoveServant(Entity player, Dictionary<string, string> p, FlowActionContext c)
    {
        if (!EventBossServantMutationsEnabled)
            return SkipBossServantMutation("boss.remove_servant");

        DispatchBossRemoveServant(c, player, Required(p, "servantId"));
        return OperationResult.Ok();
    }

    OperationResult BossCommandServants(Entity player, Dictionary<string, string> p, FlowActionContext c)
    {
        if (!EventBossServantMutationsEnabled)
            return SkipBossServantMutation("boss.command_servants");

        var command = ServantEnumParsers.ParseServantCommand(Required(p, "command"));
        DispatchBossCommandServants(c, player, command, Text(p, "target", ""), Float(p, "radius", 0f));
        return OperationResult.Ok();
    }

    OperationResult BossSpawnServants(Entity player, Dictionary<string, string> p, FlowActionContext c)
    {
        if (!EventBossServantMutationsEnabled)
            return SkipBossServantMutation("boss.spawn_servants");

        var prefab = ResolvePrefab(Required(p, "prefab"));
        if (!prefab.HasValue)
            return OperationResult.Fail("Unknown servant prefab.");

        DispatchBossSpawnServants(c, player, prefab.Value, Int(p, "count", 3),
            Int(p, "delaySeconds", 0), Int(p, "lifetimeSeconds", -1), Int(p, "intervalSeconds", 1),
            ServantEnumParsers.ParseServantFormation(Text(p, "formation", "Circle")),
            ServantEnumParsers.ParseServantType(Text(p, "servantType", "Guard")),
            ServantEnumParsers.ParseServantFaction(Text(p, "servantFaction", "Unknown")));
        return OperationResult.Ok();
    }

    static OperationResult SkipBossServantMutation(string actionName)
    {
        BattleLuckPlugin.LogWarning($"[FlowActionExecutor] {actionName} skipped: boss servant ECS mutations are disabled for server stability.");
        return OperationResult.Ok();
    }

    static string? EmptyToNull(string value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    OperationResult ApplyTimedBuff(Entity player, PrefabGUID buff, float duration)
    {
        if (buff == PrefabGUID.Empty)
            return OperationResult.Ok();
        return player.BuffEntity(buff, out _, NormalizeEffectDuration(duration))
            ? OperationResult.Ok()
            : OperationResult.Fail($"Buff prefab {buff.GuidHash} could not be applied in this server build.");
    }

    OperationResult ApplyNamedBuff(Entity player, Dictionary<string, string> p, FlowActionContext c)
    {
        var buffName = Text(p, "buff", Text(p, "buffName", ""));
        if (string.IsNullOrWhiteSpace(buffName))
            return OperationResult.Fail("buff.apply requires 'buff' parameter.");

        var guid = ResolvePrefab(buffName) ?? PrefabGUID.Empty;
        if (guid == PrefabGUID.Empty)
            return OperationResult.Fail($"Unknown buff prefab: {buffName}");

        var duration = EffectDuration(p, c, -1f, "duration", "durationSeconds");
        return player.BuffEntity(guid, out _, duration)
            ? OperationResult.Ok()
            : OperationResult.Fail($"Buff prefab '{buffName}' could not be applied in this server build.");
    }

    OperationResult RemoveNamedBuff(Entity player, Dictionary<string, string> p)
    {
        var buffName = Text(p, "buff", Text(p, "buffName", ""));
        if (string.IsNullOrWhiteSpace(buffName))
            return OperationResult.Fail("buff.remove requires 'buff' parameter.");

        var guid = ResolvePrefab(buffName) ?? PrefabGUID.Empty;
        if (guid == PrefabGUID.Empty)
            return OperationResult.Fail($"Unknown buff prefab: {buffName}");

        player.TryRemoveBuff(guid);
        return OperationResult.Ok();
    }

    OperationResult ToggleEventEntry(Entity player, Dictionary<string, string> p, FlowActionContext c)
    {
        var enabled = Bool(p, "enabled", true);
        var action = new EventEntryToggleAction
        {
            TargetEntity = player,
            Enabled = enabled,
            SessionEntity = Entity.Null // Resolved by system if needed
        };
        EcbHelper.GetEcb().AddComponent(EcbHelper.GetEcb().CreateEntity(), action);
        return OperationResult.Ok();
    }

    OperationResult TeamCornerTeleport(Entity player, Dictionary<string, string> p, FlowActionContext c)
    {
        var action = new TeamCornerTeleportAction
        {
            TargetEntity = player,
            ZoneHash = Int(p, "zoneHash", c.ZoneHash),
            Radius = Float(p, "radius", 5f),
            SessionEntity = Entity.Null
        };
        EcbHelper.GetEcb().AddComponent(EcbHelper.GetEcb().CreateEntity(), action);
        return OperationResult.Ok();
    }

    OperationResult SpawnChest(Entity player, Dictionary<string, string> p, FlowActionContext c)
    {
        var action = new ChestSpawnAction
        {
            Position = ResolvePosition(player, p, c),
            RequiredKills = Int(p, "requiredKills", 3),
            DeathLimit = Int(p, "deathLimit", 3),
            LootTable = new Unity.Collections.FixedString64Bytes(Text(p, "lootTable", "GoldIngots")),
            SessionEntity = Entity.Null
        };
        EcbHelper.GetEcb().AddComponent(EcbHelper.GetEcb().CreateEntity(), action);
        return OperationResult.Ok();
    }

    OperationResult SwapTeam(Entity player, Dictionary<string, string> p, FlowActionContext c)
    {
        var action = new TeamSwapAction
        {
            TargetEntity = player,
            NewTeamId = Int(p, "teamId", 0),
            SessionEntity = Entity.Null
        };
        EcbHelper.GetEcb().AddComponent(EcbHelper.GetEcb().CreateEntity(), action);
        return OperationResult.Ok();
    }

    OperationResult FinalizeEvent(Entity player, Dictionary<string, string> p, FlowActionContext c)
    {
        var action = new EventFinalizeAction
        {
            SessionEntity = Entity.Null,
            WinnerNames = new Unity.Collections.FixedString512Bytes(Text(p, "winners", "")),
            CleanupStructures = Bool(p, "cleanup", true)
        };
        EcbHelper.GetEcb().AddComponent(EcbHelper.GetEcb().CreateEntity(), action);
        return OperationResult.Ok();
    }

    OperationResult ChangeBlood(Entity player, Dictionary<string, string> p, FlowActionContext c)
    {
        if (!player.Has<Blood>())
            return OperationResult.Fail("Target has no Blood component.");

        var bloodTypeName = Text(p, "bloodType", "Scholar");
        if (!KindredBloodTypes.TryResolve(bloodTypeName, out var bloodType))
            return OperationResult.Fail($"Unknown blood type '{bloodTypeName}'. Known: {KindredBloodTypes.KnownNames}");

        var quality = Math.Clamp(Float(p, "quality", 100f), 0f, 100f);
        player.With((ref Blood blood) =>
        {
            blood.BloodType = bloodType;
            blood.Quality = quality;
            blood.Value = blood.MaxBlood;
        });
        BattleLuckPlugin.LogInfo($"[FlowActionExecutor] Changed blood to {bloodTypeName} ({bloodType.GuidHash}) quality={quality:F0}.");
        return OperationResult.Ok();
    }

    OperationResult LoadSchematic(Entity player, Dictionary<string, string> p, FlowActionContext c)
    {
        var eventName = Text(p, "eventName", Text(p, "name", Text(p, "schematic", "")));
        if (string.IsNullOrWhiteSpace(eventName))
            return OperationResult.Fail("schematic.load requires eventName/name/schematic.");

        float3 center;
        if (p.ContainsKey("position") || p.ContainsKey("zoneOffset") || p.ContainsKey("offset"))
        {
            center = ResolvePosition(player, p, c);
        }
        else if (c.Zone != null)
        {
            center = ZoneCenter(c.Zone);
        }
        else
        {
            center = ResolvePosition(player, p, c);
        }
        center += new float3(0f, Float(p, "heightOffset", Float(p, "yOffset", 0f)), 0f);
        var safetyMode = Text(p, "safetyMode", Text(p, "mode", ""));
        var eventTrackedZoneOnly =
            safetyMode.Equals("event_tracked_zone_only", StringComparison.OrdinalIgnoreCase) ||
            Bool(p, "eventTracked", false) ||
            Bool(p, "trackedZoneOnly", false);

        var radius = Float(p, "radius", 0f);
        var clearRadius = Float(p, "clearRadius", Float(p, "expandClear", 0f));

        if (eventTrackedZoneOnly)
        {
            if (c.GameContext == null || c.Zone == null)
                return OperationResult.Fail("event_tracked_zone_only schematic.load requires an active event zone context.");

            var zoneCenter = ZoneCenter(c.Zone);
            var zoneRadius = c.Zone.Radius > 0f ? c.Zone.Radius : 1f;
            if (!WithinXZ(center, zoneCenter, zoneRadius + 0.01f))
                return OperationResult.Fail("schematic.load center must be inside the active event zone.");

            radius = radius <= 0f ? zoneRadius : Math.Min(radius, zoneRadius);
            if (clearRadius > 0f)
                clearRadius = Math.Min(clearRadius, zoneRadius);

            c.GameContext.State["arenaSpawningRequested"] = true;
            c.GameContext.State[$"schematic:{eventName}:sessionId"] = c.GameContext.SessionId;
            c.GameContext.State[$"schematic:{eventName}:group"] = Text(p, "group", eventName);
        }

        if (EcbHelper.TryGetEcb(out var ecb))
        {
            var actionEntity = ecb.CreateEntity();
            ecb.AddComponent(actionEntity, new SchematicLoadAction
            {
                SchematicId = eventName,
                Center = center,
                Rotation = Float(p, "rotation", 0f),
                SessionEntity = Entity.Null
            });
            return OperationResult.Ok();
        }

        return OperationResult.Fail("EntityCommandBuffer unavailable for stable schematic load.");
    }

    OperationResult ClearSchematic(Dictionary<string, string> p)
    {
        var eventName = Text(p, "eventName", Text(p, "name", Text(p, "schematic", "")));
        if (string.IsNullOrWhiteSpace(eventName))
            return OperationResult.Fail("schematic.clear requires eventName/name/schematic.");

        var result = SchematicLoader.ClearByEventName(eventName);
        return result.Success
            ? OperationResult.Ok()
            : OperationResult.Fail(result.Error ?? "schematic.clear failed.");
    }

    OperationResult ClearSchematicRadius(Entity player, Dictionary<string, string> p, bool destroyWorld)
    {
        var radius = Math.Clamp(Float(p, "radius", Float(p, "clearRadius", 60f)), 1f, 500f);
        var center = ResolvePosition(player, p, new FlowActionContext { PlayerCharacter = player });
        var result = SchematicLoader.ClearTrackedInRadius(center, radius);

        return result.Success
            ? OperationResult.Ok()
            : OperationResult.Fail(result.Error ?? "schematic radius cleanup failed.");
    }

    public static (string actionName, Dictionary<string, string> parameters) ParseActionString(string actionString)
    {
        var parts = actionString.Split(':', 2);
        var parameters = new Dictionary<string, string>(KeyComparer);
        if (parts.Length > 1)
        {
            foreach (var param in parts[1].Split('|', StringSplitOptions.RemoveEmptyEntries))
            {
                var kv = param.Split('=', 2);
                if (kv.Length == 2)
                    parameters[kv[0].Trim()] = kv[1].Trim();
            }
        }
        return (parts[0].Trim(), parameters);
    }

    static bool IsRegisteredAction(string actionName) =>
        SupportedActions.Contains(actionName, KeyComparer) ||
        LiveSystemRegistryService.IsRegisteredAction(actionName) ||
        Registry.TryGetAction(actionName, out _);

    /// <summary>Parse JSON action response from AI and convert to action string format.</summary>
    public static string? ParseJsonToActionString(string jsonResponse)
    {
        try
        {
            using var doc = JsonDocument.Parse(jsonResponse);
            var root = doc.RootElement;
            
            if (!root.TryGetProperty("action", out var actionProp))
                return null;
                
            var actionName = actionProp.GetString();
            if (string.IsNullOrWhiteSpace(actionName))
                return null;
            
            var parameters = new List<string>();
            
            if (root.TryGetProperty("parameters", out var paramsProp) && paramsProp.ValueKind == JsonValueKind.Object)
            {
                foreach (var param in paramsProp.EnumerateObject())
                {
                    var value = param.Value.ValueKind == JsonValueKind.String 
                        ? param.Value.GetString() 
                        : param.Value.ToString();
                    if (!string.IsNullOrWhiteSpace(value))
                        parameters.Add($"{param.Name}={value}");
                }
            }
            
            if (parameters.Count > 0)
                return $"{actionName}:{string.Join("|", parameters)}";
            
            return actionName;
        }
        catch
        {
            return null;
        }
    }

    static string Required(Dictionary<string, string> p, string key)
    {
        if (p.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value))
            return value;
        throw new InvalidOperationException($"Missing required parameter '{key}'.");
    }

    static string RequiredAny(Dictionary<string, string> p, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (p.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value))
                return value;
        }

        throw new InvalidOperationException($"Missing required parameter '{string.Join("/", keys)}'.");
    }

    static string Text(Dictionary<string, string> p, string key, string fallback) =>
        p.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value) ? value : fallback;

    static string? NullIfEmpty(string value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;

    static IReadOnlyList<string>? ParseStringList(string value) =>
        string.IsNullOrWhiteSpace(value)
            ? null
            : value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    static int Int(Dictionary<string, string> p, string key, int fallback) =>
        p.TryGetValue(key, out var value) && int.TryParse(value, out var parsed) ? parsed : fallback;

    static ulong ULong(Dictionary<string, string> p, string key, ulong fallback) =>
        p.TryGetValue(key, out var value) && ulong.TryParse(value, out var parsed) ? parsed : fallback;

    static int? OptionalTeamId(Dictionary<string, string> p)
    {
        var raw = Text(p, "teamId", Text(p, "team", ""));
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        raw = raw.Trim();
        if (raw.StartsWith("team", StringComparison.OrdinalIgnoreCase))
            raw = raw[4..];

        return int.TryParse(raw, out var parsed) ? parsed : null;
    }

    static NpcBehaviorMode ParseNpcControlMode(string raw)
    {
        return (raw ?? "").Trim().ToLowerInvariant() switch
        {
            "guard" or "defender" or "defend" => NpcBehaviorMode.Guard,
            "chase" or "aggressive" or "attack" => NpcBehaviorMode.Aggro,
            "follow" => NpcBehaviorMode.Follow,
            "goto" or "move" => NpcBehaviorMode.GoTo,
            "hold" or "stay" => NpcBehaviorMode.Hold,
            "patrol" => NpcBehaviorMode.Patrol,
            "wander" => NpcBehaviorMode.Wander,
            "flee" => NpcBehaviorMode.Flee,
            _ => NpcBehaviorMode.Idle
        };
    }

    OperationResult ReturnNpcHome(Entity player, Dictionary<string, string> p, FlowActionContext c)
    {
        var service = BattleLuckPlugin.NpcService;
        if (service == null) return OperationResult.Fail("NPC control service is not initialized.");
        var selector = Text(p, "npcId", Text(p, "bossId", Text(p, "id", "")));
        var entry = !string.IsNullOrWhiteSpace(selector) && service.TryGet(selector, out var exact)
            ? exact
            : service.GetLatest(c.GameContext?.SessionId ?? "_flow_");
        return entry == null ? OperationResult.Fail("Controlled NPC was not found.") : service.GoTo(entry.NpcId, entry.HomePosition, 2f);
    }

    OperationResult SetNpcBehavior(Entity player, Dictionary<string, string> p, FlowActionContext c)
    {
        var behavior = Text(p, "behavior", "hold").Trim().ToLowerInvariant();
        var canonical = behavior switch
        {
            "aggro" or "attack" or "chase" => "npc.aggro",
            "follow" => "npc.follow",
            "wander" => "npc.wander",
            "guard" => "npc.guard",
            "release" or "idle" or "deaggro" => "npc.release",
            _ => "npc.hold"
        };
        return NpcControl(canonical, player, p, c);
    }

    static void ApplyInitialNpcMode(
        NpcControlService service,
        ControlledNpcEntry entry,
        NpcBehaviorMode mode,
        Entity target,
        float homeRadius)
    {
        _ = mode switch
        {
            NpcBehaviorMode.Aggro => service.Aggro(entry.NpcId, target, 3f, Math.Max(homeRadius * 2f, 80f)),
            NpcBehaviorMode.Follow => service.Follow(entry.NpcId, target, 6f, Math.Max(homeRadius * 2f, 80f)),
            NpcBehaviorMode.Guard or NpcBehaviorMode.Hold => service.Hold(entry.NpcId, homeRadius),
            _ => service.Release(entry.NpcId)
        };
    }

    static int AbilitySlot(Dictionary<string, string> p, string key, int fallback)
    {
        if (!p.TryGetValue(key, out var value) || string.IsNullOrWhiteSpace(value))
            return fallback;

        return value.Trim().ToUpperInvariant() switch
        {
            "Q" => 5,
            "E" => 6,
            "R" => 7,
            _ when int.TryParse(value, out var parsed) => parsed,
            _ => fallback
        };
    }

    static float Float(Dictionary<string, string> p, string key, float fallback) =>
        p.TryGetValue(key, out var value) && float.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var parsed) ? parsed : fallback;

    static float EffectDuration(Dictionary<string, string> p, FlowActionContext? c, float fallback, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (p.TryGetValue(key, out var value) &&
                float.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var parsed))
                return NormalizeEffectDuration(parsed);
        }

        var matchDuration = RemainingMatchDuration(c);
        if (matchDuration.HasValue && fallback < 0f)
            return matchDuration.Value;

        return NormalizeEffectDuration(fallback);
    }

    static float NormalizeEffectDuration(float duration) => duration < 0f ? 0f : duration;

    static float? RemainingMatchDuration(FlowActionContext? c)
    {
        var context = c?.GameContext;
        if (context == null || context.TimeLimitSeconds <= 0)
            return null;

        if (context.StartTimeUtc != default)
        {
            var remaining = context.TimeLimitSeconds - (float)(DateTime.UtcNow - context.StartTimeUtc).TotalSeconds;
            return remaining > 1f ? remaining : 1f;
        }

        return context.TimeLimitSeconds;
    }

    static bool Bool(Dictionary<string, string> p, string key, bool fallback) =>
        p.TryGetValue(key, out var value) && bool.TryParse(value, out var parsed) ? parsed : fallback;

    static IEnumerable<Entity> TargetPlayers(Entity player, FlowActionContext c, Dictionary<string, string> p)
    {
        var selector = Text(p, "target", Text(p, "scope", ""));
        var all = Bool(p, "all", false) ||
                  selector.Equals("all", StringComparison.OrdinalIgnoreCase) ||
                  selector.Equals("players", StringComparison.OrdinalIgnoreCase) ||
                  selector.Equals("session", StringComparison.OrdinalIgnoreCase);

        return all ? PlayersInContext(c) : new[] { player };
    }

    static OperationResult ForEachTargetPlayer(
        Entity player,
        FlowActionContext c,
        Dictionary<string, string> p,
        Func<Entity, OperationResult> action)
    {
        var ok = 0;
        var errors = new List<string>();
        foreach (var target in TargetPlayers(player, c, p))
        {
            if (!target.Exists())
                continue;
            var result = action(target);
            if (result.Success) ok++;
            else if (!string.IsNullOrWhiteSpace(result.Error)) errors.Add(result.Error);
        }

        if (ok > 0)
            return OperationResult.Ok();
        return OperationResult.Fail(errors.Count > 0 ? string.Join("; ", errors.Take(3)) : "No target players found.");
    }

    static PrefabGUID? ResolveObjectiveItem(Dictionary<string, string> p)
    {
        var itemText = Text(p, "itemId", Text(p, "itemPrefab", Text(p, "prefab", "")));
        if (string.IsNullOrWhiteSpace(itemText))
            return null;
        if (int.TryParse(itemText, out var guidHash))
            return new PrefabGUID(guidHash);
        return ResolvePrefab(itemText);
    }

    static int CountInventoryItem(Entity player, PrefabGUID item)
    {
        var em = VRisingCore.EntityManager;
        if (!InventoryUtilities.TryGetInventoryEntity(em, player, out var inventoryEntity) ||
            !em.HasBuffer<InventoryBuffer>(inventoryEntity))
            return 0;

        var total = 0;
        var buffer = em.GetBuffer<InventoryBuffer>(inventoryEntity);
        for (var i = 0; i < buffer.Length; i++)
        {
            var slot = buffer[i];
            if (slot.ItemType.GuidHash == item.GuidHash)
                total += slot.Amount;
        }
        return total;
    }

    static void ProtectEventObject(Entity entity)
    {
        if (!entity.Exists() || entity.Has<PlayerCharacter>())
            return;

        try
        {
            if (Prefabs.Admin_Invulnerable_Buff != PrefabGUID.Empty)
                entity.BuffEntity(Prefabs.Admin_Invulnerable_Buff, out _, 0f, persistThroughDeath: true);
        }
        catch { }

        try
        {
            if (entity.Has<EditableTileModel>())
            {
                var editable = entity.Read<EditableTileModel>();
                editable.CanDismantle = false;
                entity.Write(editable);
            }
        }
        catch { }
    }

    static Entity ResolveNpcTarget(Entity player, Dictionary<string, string> p)
    {
        var selector = Text(p, "target", Text(p, "targetId", "self"));
        if (string.IsNullOrWhiteSpace(selector) ||
            selector.Equals("self", StringComparison.OrdinalIgnoreCase) ||
            selector.Equals("player", StringComparison.OrdinalIgnoreCase))
        {
            return player;
        }

        if (BattleLuckPlugin.NpcService?.TryGet(selector, out var npc) == true && npc.IsAlive)
            return npc.Entity;

        if (ulong.TryParse(selector, out var steamId))
        {
            foreach (var online in VRisingCore.GetOnlinePlayers())
            {
                if (online.Exists() && online.IsPlayer() && online.GetSteamId() == steamId)
                    return online;
            }
        }

        return player;
    }

    static bool TryParseFloat3(string data, out float3 value)
    {
        value = float3.zero;
        var parts = data.Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries);
        return parts.Length == 3
            && float.TryParse(parts[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out value.x)
            && float.TryParse(parts[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out value.y)
            && float.TryParse(parts[2], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out value.z);
    }

    static float3 ResolvePosition(Entity player, Dictionary<string, string> p, FlowActionContext c)
    {
        if (p.TryGetValue("position", out var posText))
        {
            if (posText.Equals("zone", StringComparison.OrdinalIgnoreCase) ||
                posText.Equals("zoneCenter", StringComparison.OrdinalIgnoreCase))
            {
                var zoneCenter = c.Zone != null ? ZoneCenter(c.Zone) : player.GetPosition();
                if (p.TryGetValue("zoneOffset", out var zoneOffsetText) && TryParseFloat3(zoneOffsetText, out var zoneOffset))
                    return zoneCenter + zoneOffset;
                return zoneCenter;
            }

            if (TryParseFloat3(posText, out var pos))
                return pos;
        }
        if (p.TryGetValue("zoneOffset", out var offsetFromZoneText) && TryParseFloat3(offsetFromZoneText, out var offsetFromZone))
            return (c.Zone != null ? ZoneCenter(c.Zone) : player.GetPosition()) + offsetFromZone;
        if (p.TryGetValue("offset", out var offsetText) && TryParseFloat3(offsetText, out var offset))
            return player.GetPosition() + offset;
        return player.GetPosition();
    }

    static bool WithinXZ(float3 point, float3 center, float radius)
    {
        var dx = point.x - center.x;
        var dz = point.z - center.z;
        return dx * dx + dz * dz <= radius * radius;
    }

    static float3 ZoneCenter(ZoneDefinition zone)
    {
        var center = zone.Position.ToFloat3();
        if (math.lengthsq(center) > 0.0001f)
            return center;

        return zone.TeleportSpawn.ToFloat3();
    }

    static PrefabGUID? ResolvePrefab(string name)
    {
        if (KindredSpawnSafety.IsSpawnBanned(name, out var bannedReason))
        {
            BattleLuckPlugin.LogWarning($"[FlowActionExecutor] Refusing banned spawn prefab '{name}': {bannedReason}");
            return null;
        }

        if (int.TryParse(name, out var guid))
            return new PrefabGUID(guid);
        return PrefabHelper.GetPrefabGuidDeep(name);
    }

    static IEnumerable<string> RegisteredModes(FlowActionContext c) =>
        c.Registry?.GetRegisteredModes() ?? new[] { "bloodbath", "colosseum", "siege", "trials", "aievent" };

    static SpawnController Spawner(FlowActionContext c)
    {
        if (c.GameContext?.State.TryGetValue("spawner", out var spawner) == true && spawner is SpawnController existing)
            return existing;
        var created = new SpawnController();
        if (c.GameContext != null)
            c.GameContext.State["spawner"] = created;
        return created;
    }

    static BorderWallController Border(FlowActionContext c)
    {
        if (c.GameContext?.State.TryGetValue("border", out var border) == true && border is BorderWallController existing)
            return existing;
        var created = new BorderWallController();
        if (c.GameContext != null)
            c.GameContext.State["border"] = created;
        return created;
    }

    static Dictionary<string, object?> State(FlowActionContext c, string key)
    {
        if (c.GameContext == null)
            return new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        if (!c.GameContext.State.TryGetValue(key, out var value) || value is not Dictionary<string, object?> state)
        {
            state = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            c.GameContext.State[key] = state;
        }
        return state;
    }

    static bool IsEntryPrepared(FlowActionContext c)
    {
        var steamId = c.PlayerCharacter.GetSteamId();
        if (steamId == 0 || c.GameContext == null)
            return false;

        return c.GameContext.State.TryGetValue($"entryPrepared:{steamId}", out var value)
            && value is bool prepared
            && prepared;
    }

    static OperationResult UnlockAllVBloods(Entity player)
    {
        return BattleLuckPlugin.Progression?.UnlockAllVBloods(player)
            ?? OperationResult.Fail("Progression service is not initialized.");
    }

    static OperationResult UnlockAllResearch(Entity player)
    {
        return BattleLuckPlugin.Progression?.UnlockAllResearch(player)
            ?? OperationResult.Fail("Progression service is not initialized.");
    }

    static OperationResult SetProgressionTier(Entity player, Dictionary<string, string> p, FlowActionContext c)
    {
        var tier = Int(p, "tier", 8);
        var result = BattleLuckPlugin.Progression?.SetTier(player, tier)
            ?? OperationResult.Fail("Progression service is not initialized.");
        if (!result.Success) return result;
        
        var gearLevel = Int(p, "gearLevel", tier switch
        {
            <= 5 => 70,
            6 => 80,
            7 => 85,
            _ => 90
        });

        player.SetEquipmentLevel(gearLevel, gearLevel, gearLevel);

        var message = Text(p, "message", "");
        if (!string.IsNullOrWhiteSpace(message))
            c.GameContext?.Broadcast?.Invoke(message);

        BattleLuckPlugin.LogInfo($"[FlowActionExecutor] progression.set_tier tier={tier} gearLevel={gearLevel}.");
        return OperationResult.Ok();
    }

    static OperationResult UnlockGear(Entity player, Dictionary<string, string> p)
    {
        KitController.SetMaxLevel(player);
        if (Bool(p, "givesItems", false))
        {
            KitController.ApplyWeaponsKit(player);
            KitController.ApplyArmorKit(player);
        }

        return OperationResult.Ok();
    }

    static void HealPercent(Entity player, float percent)
    {
        if (!player.Has<Health>()) return;
        player.With((ref Health health) =>
        {
            var amount = health.MaxHealth.Value * Math.Clamp(percent, 0f, 1f);
            var value = Math.Min(health.MaxHealth.Value, health.Value + amount);
            health.Value = new ModifiableFloat(value);
            health.MaxRecoveryHealth = health.MaxHealth;
        });
    }

    static OperationResult ClearInventory(Entity player)
    {
        var em = VRisingCore.EntityManager;
        if (!InventoryUtilities.TryGetInventoryEntity(em, player, out var inventoryEntity) ||
            !em.HasBuffer<InventoryBuffer>(inventoryEntity))
            return OperationResult.Fail("Inventory not found.");

        var buffer = em.GetBuffer<InventoryBuffer>(inventoryEntity);
        var removed = 0;
        for (var i = 0; i < buffer.Length; i++)
        {
            var item = buffer[i];
            if (item.ItemType == PrefabGUID.Empty || item.Amount <= 0)
                continue;

            if (player.TryRemoveItem(item.ItemType, item.Amount))
                removed += item.Amount;
        }

        BattleLuckPlugin.LogInfo($"[FlowActionExecutor] inventory.clear_all removed {removed} item(s).");
        return OperationResult.Ok();
    }

    static OperationResult CountInventory(Entity player, Dictionary<string, string> p)
    {
        var em = VRisingCore.EntityManager;
        if (!InventoryUtilities.TryGetInventoryEntity(em, player, out var inventoryEntity) ||
            !em.HasBuffer<InventoryBuffer>(inventoryEntity))
            return OperationResult.Fail("Inventory not found.");

        var itemFilter = Text(p, "itemId", "");
        PrefabGUID? filter = int.TryParse(itemFilter, out var guidHash) ? new PrefabGUID(guidHash) : null;
        var total = 0;
        var buffer = em.GetBuffer<InventoryBuffer>(inventoryEntity);
        for (var i = 0; i < buffer.Length; i++)
        {
            var item = buffer[i];
            if (item.ItemType == PrefabGUID.Empty || item.Amount <= 0)
                continue;
            if (filter.HasValue && item.ItemType.GuidHash != filter.Value.GuidHash)
                continue;
            total += item.Amount;
        }

        BattleLuckPlugin.LogInfo($"[FlowActionExecutor] inventory.count total={total} filter={itemFilter}.");
        return OperationResult.Ok();
    }

    static OperationResult RecordBestEffort(FlowActionContext c, string stateKey, string actionName, Dictionary<string, string> p, string message)
    {
        State(c, stateKey)[actionName] = string.Join("|", p.Select(kv => $"{kv.Key}={kv.Value}"));
        BattleLuckPlugin.LogInfo($"[FlowActionExecutor] {message} {actionName}");
        return OperationResult.Ok();
    }

    static Dictionary<string, DateTime> Timers(GameModeContext? ctx) => GetState<Dictionary<string, DateTime>>(ctx, "timers", () => new(StringComparer.OrdinalIgnoreCase));
    static Dictionary<string, float3> SpatialPoints(GameModeContext? ctx) => GetState<Dictionary<string, float3>>(ctx, "spatialPoints", () => new(StringComparer.OrdinalIgnoreCase));
    static Dictionary<ulong, int> Revives(GameModeContext? ctx) => GetState<Dictionary<ulong, int>>(ctx, "revives", () => new());
    static Dictionary<string, int> ObjectiveStates(GameModeContext ctx) => GetState<Dictionary<string, int>>(ctx, "objectives", () => new(StringComparer.OrdinalIgnoreCase));
    static List<TrapState> Traps(FlowActionContext c) => GetState<List<TrapState>>(c.GameContext, "traps", () => new());

    static T GetState<T>(GameModeContext? ctx, string key, Func<T> factory)
    {
        if (ctx == null)
            return factory();
        if (!ctx.State.TryGetValue(key, out var value) || value is not T typed)
        {
            typed = factory();
            ctx.State[key] = typed;
        }
        return typed;
    }

    static List<Entity> Tracked(FlowActionContext c, string key)
    {
        if (c.GameContext == null)
            return new List<Entity>();
        if (!c.GameContext.State.TryGetValue(key, out var value) || value is not List<Entity> list)
        {
            list = new List<Entity>();
            c.GameContext.State[key] = list;
        }
        return list;
    }

    static void Track(FlowActionContext c, string key, Entity entity)
    {
        if (entity.Exists())
            Tracked(c, key).Add(entity);
    }

    static OperationResult DestroyTracked(FlowActionContext c, string key)
    {
        int destroyed = 0;
        int deferred = 0;
        foreach (var entity in Tracked(c, key).ToList())
        {
            if (!entity.Exists()) continue;
            try { entity.DestroyWithReason(); destroyed++; }
            catch (Exception ex) when (ex.Message.Contains("in live", StringComparison.OrdinalIgnoreCase))
            {
                // Entity is currently being processed - will be cleaned up on next tick
                deferred++;
            }
            catch { }
        }
        Tracked(c, key).Clear();
        if (destroyed > 0 || deferred > 0)
            BattleLuckPlugin.LogInfo($"[FlowActionExecutor] DestroyTracked '{key}': destroyed={destroyed}, deferred={deferred} (in live state).");
        return OperationResult.Ok();
    }

    static IEnumerable<Entity> PlayersInContext(FlowActionContext c)
    {
        if (c.GameContext == null || c.GameContext.Players.Count == 0)
            return new[] { c.PlayerCharacter };
        var players = VRisingCore.GetOnlinePlayers();
        return players.Where(p => c.GameContext.Players.Contains(p.GetSteamId())).ToList();
    }

    static void ClearKitItems(Entity player, string kitId)
    {
        foreach (var prefab in KitController.GetKitPrefabs(kitId))
        {
            try { player.TryRemoveItem(prefab, 99); } catch { }
        }
    }

    static void ClearKnownBuffs(Entity player)
    {
        foreach (var buff in new[] { Prefabs.Buff_General_Ignite, Prefabs.Buff_General_Slow, Prefabs.Buff_General_Freeze, Prefabs.Buff_InCombat })
        {
            if (buff != PrefabGUID.Empty)
                player.TryRemoveBuff(buff);
        }
    }

    OperationResult EntitySpawn(Entity player, Dictionary<string, string> p, FlowActionContext c)
    {
        var prefab = ResolvePrefab(Required(p, "prefab"));
        if (!prefab.HasValue)
            return OperationResult.Fail("Unknown prefab.");
        var count = Int(p, "count", 1);
        var center = ResolvePosition(player, p, c);
        var protect = Bool(p, "protect", false);
        for (var i = 0; i < count; i++)
        {
            var offset = new Unity.Mathematics.float3(i * 2f, 0, 0);
            Spawner(c).SpawnWithCallback(prefab.Value, center + offset, postActions: entity =>
            {
                Track(c, "spawned", entity);
                if (protect)
                    ProtectEventObject(entity);
            });
        }
        BattleLuckPlugin.LogInfo($"[FlowActionExecutor] entity.spawn prefab={prefab.Value.GuidHash} x{count}");
        return OperationResult.Ok();
    }

    static OperationResult EntityDestroy(Dictionary<string, string> p)
    {
        var type = Required(p, "type");
        var max = p.TryGetValue("max", out var maxStr) && int.TryParse(maxStr, out var m) ? (int?)m : null;
        var destroyed = VRisingCore.DestroyEntities(type, max);
        BattleLuckPlugin.LogInfo($"[FlowActionExecutor] entity.destroy type={type} destroyed={destroyed}");
        return OperationResult.Ok();
    }

    static OperationResult EntityCount(Entity player, Dictionary<string, string> p, FlowActionContext c)
    {
        var type = Required(p, "type");
        var count = VRisingCore.CountEntities(type);
        BattleLuckPlugin.LogInfo($"[FlowActionExecutor] entity.count type={type} = {count}");
        if (p.TryGetValue("score", out var scoreStr) && scoreStr.Equals("true", StringComparison.OrdinalIgnoreCase))
            c.GameContext?.Scores.AddPlayerScore(player.GetSteamId(), count);
        return OperationResult.Ok();
    }

    static OperationResult EntityQuery(Dictionary<string, string> p)
    {
        var type = Required(p, "type");
        var count = VRisingCore.CountEntities(type);
        BattleLuckPlugin.LogInfo($"[FlowActionExecutor] entity.query type={type} = {count}");
        return OperationResult.Ok();
    }

    static OperationResult EntityValidate(Dictionary<string, string> p)
    {
        var type = Required(p, "type");
        var count = VRisingCore.CountEntities(type);
        if (p.TryGetValue("min", out var minStr) && int.TryParse(minStr, out var min) && count < min)
            return OperationResult.Fail($"entity.validate: {type} count {count} < min {min}.");
        if (p.TryGetValue("max", out var maxStr) && int.TryParse(maxStr, out var max) && count > max)
            return OperationResult.Fail($"entity.validate: {type} count {count} > max {max}.");
        BattleLuckPlugin.LogInfo($"[FlowActionExecutor] entity.validate type={type} count={count} passed.");
        return OperationResult.Ok();
    }

    void TryRestore(Entity player, int zoneHash, FlowActionContext? context = null)
    {
        try { (context?.PlayerState ?? _playerState).RestoreSnapshot(player, zoneHash); }
        catch (Exception ex) { BattleLuckPlugin.LogError($"[FlowActionExecutor] Rollback restore failed: {ex.Message}"); }
    }

    static List<PrefabGUID> GetWavePrefabs(int waveId) => waveId switch
    {
        1 => SpawnController.Tier1Enemies,
        2 => SpawnController.Tier2Enemies,
        3 => SpawnController.Tier3Enemies,
        4 => SpawnController.Tier4Enemies,
        5 => SpawnController.EliteEnemies,
        _ => SpawnController.Tier1Enemies,
    };

    static float3 FormationOffset(int index, int count, string formation)
    {
        if (formation.Equals("circle", StringComparison.OrdinalIgnoreCase) && count > 1)
        {
            var angle = 2f * math.PI * index / count;
            return new float3(math.cos(angle) * 3f, 0, math.sin(angle) * 3f);
        }
        if (formation.Equals("wedge", StringComparison.OrdinalIgnoreCase))
            return new float3((index % 2 == 0 ? 1 : -1) * index, 0, index * 2f);
        return new float3(index * 2f, 0, 0);
    }

    // Boss Servant Action Dispatchers
    void DispatchBossAddServant(FlowActionContext c, Entity boss, PrefabGUID servantPrefab, ServantType servantType, ServantFaction servantFaction)
    {
        var ecb = EcbHelper.GetEcb();
        if (ecb.Equals(default(EntityCommandBuffer))) return;
        var e = ecb.CreateEntity();
        ecb.AddComponent(e, new BossAddServantAction
        {
            BossEntity = boss,
            ServantEntity = Entity.Null,
            ServantType = servantType,
            ServantFaction = servantFaction,
            SessionEntity = Entity.Null
        });
        BattleLuckPlugin.LogInfo($"[FlowActionExecutor] BossAddServant dispatched: boss={boss.Index} prefab={servantPrefab.GuidHash}");
    }

    void DispatchBossRemoveServant(FlowActionContext c, Entity boss, string servantId)
    {
        var ecb = EcbHelper.GetEcb();
        if (ecb.Equals(default(EntityCommandBuffer))) return;
        var servants = Servants(c);
        if (servants.TryGetValue(servantId, out var servantEntity))
        {
            var e = ecb.CreateEntity();
            ecb.AddComponent(e, new BossRemoveServantAction
            {
                BossEntity = boss,
                ServantEntity = servantEntity,
                SessionEntity = Entity.Null
            });
        }
    }

    void DispatchBossCommandServants(FlowActionContext c, Entity boss, ServantCommand command, string targetId, float radius)
    {
        var ecb = EcbHelper.GetEcb();
        if (ecb.Equals(default(EntityCommandBuffer))) return;
        var e = ecb.CreateEntity();
        ecb.AddComponent(e, new BossCommandServantsAction
        {
            BossEntity = boss,
            Command = command,
            TargetEntity = Entity.Null,
            Radius = radius,
            SessionEntity = Entity.Null
        });
        BattleLuckPlugin.LogInfo($"[FlowActionExecutor] BossCommandServants dispatched: boss={boss.Index} command={command}");
    }

    void DispatchBossSpawnServants(FlowActionContext c, Entity boss, PrefabGUID prefab, int count, int delay, int lifetime, int interval, ServantFormation formation, ServantType servantType, ServantFaction servantFaction)
    {
        var ecb = EcbHelper.GetEcb();
        if (ecb.Equals(default(EntityCommandBuffer))) return;
        var e = ecb.CreateEntity();
        ecb.AddComponent(e, new BossSpawnServantGroupAction
        {
            BossEntity = boss,
            Prefab = ToFixed64(prefab.GuidHash.ToString()),
            Position = c.PlayerCharacter.GetPosition(),
            Count = count,
            DelaySeconds = delay,
            LifetimeSeconds = lifetime,
            IntervalSeconds = interval,
            Formation = formation,
            ServantType = servantType,
            ServantFaction = servantFaction,
            SessionEntity = Entity.Null
        });
        BattleLuckPlugin.LogInfo($"[FlowActionExecutor] BossSpawnServantGroup dispatched: boss={boss.Index} prefab={prefab.GuidHash} x{count}");
    }

    static Dictionary<string, Entity> Servants(FlowActionContext c) =>
        GetState<Dictionary<string, Entity>>(c.GameContext, "boss_servants", () => new(StringComparer.OrdinalIgnoreCase));

    readonly record struct TrapState(float3 Position, string TrapType, float Damage, float Radius, DateTime ExpiresUtc);
    }
}
