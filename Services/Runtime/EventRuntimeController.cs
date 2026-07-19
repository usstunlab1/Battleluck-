using BattleLuck.ECS.Events;
using BattleLuck.Models;
using BattleLuck.Services.Flow;
using Unity.Entities;

namespace BattleLuck.Services.Runtime;

public sealed class EventRuntimeController
{
    readonly Dictionary<string, EventRuntimeSession> _sessions = new(StringComparer.OrdinalIgnoreCase);
    readonly FlowActionExecutor _executor;
    readonly PlayerStateController _playerState;
    readonly GameModeRegistry _registry;
    readonly SpawnedEntityRegistry _spawned = new();
    readonly CustomSequenceService _customSequences = new();
    readonly ActionManifestService _manifest = new();
    bool _subscribed;

    public EventRuntimeController(PlayerStateController playerState, GameModeRegistry registry)
    {
        _playerState = playerState;
        _registry = registry;
        _executor = new FlowActionExecutor(playerState, registry);
    }

    public SpawnedEntityRegistry Spawned => _spawned;

    public EventRuntimeStatus? GetStatus(string sessionId)
    {
        if (!_sessions.TryGetValue(sessionId, out var runtime))
            return null;

        var definition = runtime.Definition;
        return new EventRuntimeStatus
        {
            SessionId = sessionId,
            EventId = definition.Metadata.Id,
            DisplayName = definition.Metadata.DisplayName,
            Enabled = definition.Metadata.Enabled,
            Started = runtime.Started,
            ElapsedSeconds = runtime.ElapsedSeconds,
            Zones = definition.Zones.Count,
            Objects = definition.Objects.Count,
            Glows = definition.Glows.Count,
            EntityDefinitions = definition.VBloodList.Count,
            Phases = definition.Phases.Count,
            Triggers = definition.Triggers.Count,
            EventActions = definition.Actions.Count,
            TotalConfiguredActions = CountConfiguredActions(definition),
            ActiveCustomSequences = runtime.CustomSequenceRuns.Count,
            ExecutedTimedPhases = runtime.ExecutedTimedPhases.ToList(),
            Timers = runtime.Timers.Values.Select(t => new EventRuntimeTimerStatus
            {
                TimerId = t.Definition.TimerId,
                StartPhase = t.Definition.StartPhase,
                DurationSeconds = t.Definition.DurationSeconds,
                RemainingSeconds = Math.Max(0, (int)Math.Ceiling((t.EndsAtUtc - DateTime.UtcNow).TotalSeconds)),
                Fired = t.Fired
            }).ToList(),
            TrackedEntities = _spawned.Snapshot(sessionId)
        };
    }

    public IReadOnlyList<EventRuntimeStatus> GetAllStatuses() =>
        _sessions.Keys.Select(GetStatus).Where(s => s != null).Select(s => s!).ToList();

    public void StartSession(UnifiedEventDefinition? definition, GameModeContext context, ModeConfig config, ZoneDefinition zone)
    {
        if (definition == null)
            return;

        EnsureSubscribed();
        _sessions[context.SessionId] = new EventRuntimeSession(definition, context, config, zone);
        ExecuteDefinitionSetup(context.SessionId);
        ExecutePhase(context.SessionId, "setup");
    }

    public void MarkActive(string sessionId)
    {
        ExecutePhase(sessionId, "active");
    }

    public void Tick(float deltaSeconds)
    {
        foreach (var runtime in _sessions.Values.ToList())
        {
            if (!runtime.Started)
                continue;

            runtime.ElapsedSeconds += deltaSeconds;
            foreach (var phase in runtime.Definition.Phases)
            {
                if (phase.DurationSeconds <= 0 || runtime.ExecutedTimedPhases.Contains(phase.Name))
                    continue;

                if (runtime.ElapsedSeconds >= phase.DurationSeconds)
                {
                    runtime.ExecutedTimedPhases.Add(phase.Name);
                    ExecutePhase(runtime.Context.SessionId, phase.Name);
                }
            }

            TickTimers(runtime);
            TickCustomSequences(runtime);
            TickRuntimeInject(runtime);
        }
    }

    void TickRuntimeInject(EventRuntimeSession runtime)
    {
        var now = DateTime.UtcNow;
        if ((now - runtime.LastRuntimeInjectCheckUtc).TotalSeconds < 1)
            return;
        runtime.LastRuntimeInjectCheckUtc = now;

        var actions = FlowActionExecutor.GetRuntimeInject(runtime.Definition.Metadata.Id);
        var signature = string.Join("\n", actions);
        if (signature.Equals(runtime.LastRuntimeInjectSignature, StringComparison.Ordinal))
            return;
        runtime.LastRuntimeInjectSignature = signature;

        foreach (var actionString in actions)
        {
            var action = new EventActionDefinition { Action = actionString };
            var validation = _manifest.Validate(action);
            if (!validation.Success)
            {
                BattleLuckPlugin.LogWarning($"[EventRuntime] runtime_inject rejected '{actionString}': {validation.Error}");
                continue;
            }

            var actionName = actionString.Split(':', 2)[0].Trim();
            var prompt = runtime.Config.EventPrompt;
            if (prompt?.BlockedActions.Contains(actionName, StringComparer.OrdinalIgnoreCase) == true ||
                (prompt?.AllowedActions.Count > 0 && !prompt.AllowedActions.Contains(actionName, StringComparer.OrdinalIgnoreCase)))
            {
                BattleLuckPlugin.LogWarning($"[EventRuntime] runtime_inject rejected '{actionString}' by prompt policy.");
                continue;
            }

            ExecuteActions(runtime, new[] { action }, "runtime_inject");
        }
    }

    public void EndSession(string sessionId)
    {
        if (!_sessions.TryGetValue(sessionId, out var runtime))
            return;

        RunEndingPhases(sessionId);
        ExecuteSchematicObjects(runtime, load: false);
        foreach (var obj in runtime.Definition.Objects.Where(o => o.MapVisible))
            BattleLuckPlugin.ZoneMap?.ClearSchematicMarkers(MapMarkerGroup(runtime, obj));
        var destroyed = _spawned.ClearSession(sessionId);
        if (destroyed > 0)
            BattleLuckPlugin.LogInfo($"[EventRuntime] Destroyed {destroyed} tracked event entities for {sessionId}.");

        _sessions.Remove(sessionId);
    }

    /// <summary>
    /// Removes a runtime that never committed successfully. Unlike
    /// <see cref="EndSession"/>, this does not execute ending or cleanup
    /// gameplay phases, because a failed first entry has no valid participant
    /// context for those actions.
    /// </summary>
    public void AbortSession(string sessionId)
    {
        if (!_sessions.TryGetValue(sessionId, out var runtime))
            return;

        foreach (var obj in runtime.Definition.Objects.Where(o => o.MapVisible))
            BattleLuckPlugin.ZoneMap?.ClearSchematicMarkers(MapMarkerGroup(runtime, obj));

        var destroyed = _spawned.ClearSession(sessionId);
        if (destroyed > 0)
            BattleLuckPlugin.LogInfo($"[EventRuntime] Destroyed {destroyed} tracked event entities while aborting {sessionId}.");

        _sessions.Remove(sessionId);
    }

    public void AbortAll()
    {
        foreach (var sessionId in _sessions.Keys.ToList())
            AbortSession(sessionId);
    }

    public void RunEndingPhases(string sessionId)
    {
        if (!_sessions.TryGetValue(sessionId, out var runtime) || runtime.EndPhasesExecuted)
            return;

        runtime.EndPhasesExecuted = true;
        ExecutePhase(sessionId, "ending");
        ExecutePhase(sessionId, "cleanup");
    }

    public void Shutdown()
    {
        foreach (var id in _sessions.Keys.ToList())
            EndSession(id);
        _sessions.Clear();
    }

    public void ExecutePhase(string sessionId, string phaseName)
    {
        if (!_sessions.TryGetValue(sessionId, out var runtime))
            return;

        var phase = runtime.Definition.Phases.FirstOrDefault(p => p.Name.Equals(phaseName, StringComparison.OrdinalIgnoreCase));
        if (phase == null)
            return;

        if (phaseName.Equals("active", StringComparison.OrdinalIgnoreCase))
        {
            runtime.Started = true;
            SpawnEventVBloods(runtime);
        }

        ExecuteActions(runtime, phase.Actions, $"phase:{phaseName}");
        StartTimersForPhase(runtime, phaseName);
    }

    void SpawnEventVBloods(EventRuntimeSession runtime)
    {
        foreach (var definition in runtime.Definition.VBloodList)
        {
            if (!definition.Enabled ||
                !definition.SpawnTrigger.Equals("event_start", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            SpawnEventVBlood(runtime.Context.SessionId, definition);
        }
    }

    void SpawnEventVBlood(string sessionId, EventVBloodDefinition definition)
    {
        var spawner = new SpawnController();
        var prefab = PrefabHelper.GetPrefabGuidDeep(definition.Prefab);

        if ((!prefab.HasValue || prefab.Value == PrefabGUID.Empty) && definition.PrefabGuid != 0)
        {
            prefab = new PrefabGUID(definition.PrefabGuid);
        }

        if (!prefab.HasValue || prefab.Value == PrefabGUID.Empty)
        {
            BattleLuckPlugin.LogWarning(
                $"[EventVBlood] Unknown prefab '{definition.Prefab}' " +
                $"for '{definition.Id}'.");

            return;
        }

        var position = definition.Location.ToFloat3();

        spawner.SpawnBoss(
            prefab.Value,
            position,
            definition.Level,
            entity =>
            {
                if (!entity.Exists())
                    return;

                BattleLuckPlugin.NpcService?.RegisterNpc(
                    sessionId,
                    definition.Id,
                    definition.Prefab,
                    prefab.Value,
                    entity,
                    position,
                    definition.HomeRadius,
                    preventDisable: true);
            });

        BattleLuckPlugin.LogInfo($"[EventVBlood] Spawned VBlood '{definition.Id}' at event start via NpcControlService.");
    }

    void StartTimersForPhase(EventRuntimeSession runtime, string phaseName)
    {
        foreach (var timer in runtime.Definition.Timers.Where(t => t.StartPhase.Equals(phaseName, StringComparison.OrdinalIgnoreCase)))
        {
            if (runtime.Timers.ContainsKey(timer.TimerId))
                continue;

            runtime.Timers[timer.TimerId] = new EventRuntimeTimer(timer, DateTime.UtcNow.AddSeconds(timer.DurationSeconds));
            BattleLuckPlugin.LogInfo($"[EventRuntime] Timer '{timer.TimerId}' started for {runtime.Context.SessionId}: {timer.DurationSeconds}s.");

            if (timer.AnnounceStart)
                ExecuteActions(runtime, new[]
                {
                    new EventActionDefinition
                    {
                        Action = $"announce:title=Timer|message={timer.TimerId} started for {timer.DurationSeconds}s.|color=#5CC8FF|level=info"
                    }
                }, $"timer:{timer.TimerId}:start");
        }
    }

    void TickTimers(EventRuntimeSession runtime)
    {
        var now = DateTime.UtcNow;
        foreach (var timer in runtime.Timers.Values.ToList())
        {
            if (timer.Fired || now < timer.EndsAtUtc)
                continue;

            timer.Fired = true;
            if (timer.Definition.AnnounceComplete)
            {
                ExecuteActions(runtime, new[]
                {
                    new EventActionDefinition
                    {
                        Action = $"announce:title=Timer|message={timer.Definition.TimerId} complete.|color=#FFD166|level=warning"
                    }
                }, $"timer:{timer.Definition.TimerId}:complete.announce");
            }

            ExecuteActions(runtime, timer.Definition.OnCompleteActions, $"timer:{timer.Definition.TimerId}:complete");

            if (timer.Definition.Repeat)
            {
                timer.Fired = false;
                timer.EndsAtUtc = now.AddSeconds(timer.Definition.DurationSeconds);
            }
        }
    }

    void ExecuteDefinitionSetup(string sessionId)
    {
        if (!_sessions.TryGetValue(sessionId, out var runtime))
            return;

        var setupActions = runtime.Definition.Actions.Where(IsSafeTopLevelSetupAction).ToList();
        var skippedSetupActions = runtime.Definition.Actions.Count - setupActions.Count;
        if (skippedSetupActions > 0)
        {
            var skippedActionNames = runtime.Definition.Actions
                .Where(a => !IsSafeTopLevelSetupAction(a))
                .Select(a => a.ToActionString())
                .ToList();
            BattleLuckPlugin.LogWarning(
                $"[EventRuntime] Skipped {skippedSetupActions} unsafe top-level event action(s) for {runtime.Context.SessionId}. " +
                $"Skipped actions: [{string.Join(", ", skippedActionNames.Take(5))}]... " +
                "Put live mutations in phases, timers, triggers, or object actions instead.");
        }

        ExecuteActions(runtime, setupActions, "event:actions");

        foreach (var obj in runtime.Definition.Objects)
        {
            if (obj.MapVisible)
            {
                BattleLuckPlugin.ZoneMap?.RegisterSchematicMarkers(
                    MapMarkerGroup(runtime, obj),
                    obj.Position.ToFloat3(),
                    new[]
                    {
                        new SchematicMapMarker
                        {
                            Label = string.IsNullOrWhiteSpace(obj.Group) ? runtime.Definition.Metadata.DisplayName : obj.Group,
                            Position = new Vec3Config(),
                            Icon = obj.Kind,
                            ShowOnMinimap = true,
                            RenderOrder = 90
                        }
                    });
            }

            if (IsSchematicObject(obj) && !HasSchematicAction(obj, "schematic.load"))
                ExecuteActions(runtime, new[] { BuildSchematicAction(runtime, obj, load: true) }, $"object:{obj.Group}:schematic.load");

            ExecuteActions(runtime, obj.Actions, $"object:{obj.Group}");
        }

        foreach (var glow in runtime.Definition.Glows)
            ExecuteActions(runtime, glow.Actions, $"glow:{glow.Group}");
    }

    void ExecuteSchematicObjects(EventRuntimeSession runtime, bool load)
    {
        foreach (var obj in runtime.Definition.Objects.Where(IsSchematicObject))
        {
            if (load && HasSchematicAction(obj, "schematic.load"))
                continue;

            ExecuteActions(runtime, new[] { BuildSchematicAction(runtime, obj, load) },
                $"object:{obj.Group}:schematic.{(load ? "load" : "clear")}");
        }
    }

    static bool IsSchematicObject(EventObjectDefinition obj) =>
        obj.Kind.Equals("schematic", StringComparison.OrdinalIgnoreCase) ||
        obj.Kind.Equals("blueprint", StringComparison.OrdinalIgnoreCase) ||
        !string.IsNullOrWhiteSpace(obj.Schematic);

    static string MapMarkerGroup(EventRuntimeSession runtime, EventObjectDefinition obj) =>
        $"event:{runtime.Context.SessionId}:map:{obj.Group}";

    static bool HasSchematicAction(EventObjectDefinition obj, string actionName) =>
        obj.Actions.Any(a =>
        {
            var action = a.ToActionString();
            if (string.IsNullOrWhiteSpace(action))
                return false;
            var name = action.Split(':', 2)[0].Trim();
            return name.Equals(actionName, StringComparison.OrdinalIgnoreCase);
        });

    static EventActionDefinition BuildSchematicAction(EventRuntimeSession runtime, EventObjectDefinition obj, bool load)
    {
        var schematicName = !string.IsNullOrWhiteSpace(obj.Schematic) ? obj.Schematic : obj.Prefab;
        if (string.IsNullOrWhiteSpace(schematicName))
            schematicName = obj.Group;

        var center = runtime.Zone.Position.ToFloat3();
        if (Unity.Mathematics.math.lengthsq(center) <= 0.0001f)
            center = runtime.Zone.TeleportSpawn.ToFloat3();

        var action = load
            ? $"schematic.load:eventName={schematicName}|position={center.x.ToString(System.Globalization.CultureInfo.InvariantCulture)},{center.y.ToString(System.Globalization.CultureInfo.InvariantCulture)},{center.z.ToString(System.Globalization.CultureInfo.InvariantCulture)}|radius={runtime.Zone.Radius.ToString(System.Globalization.CultureInfo.InvariantCulture)}|clearOld=true|spawnItems=true|safetyMode=event_tracked_zone_only|group={obj.Group}|sessionId={runtime.Context.SessionId}|zoneHash={runtime.Context.ZoneHash}"
            : $"schematic.clear:eventName={schematicName}";

        return new EventActionDefinition { Action = action };
    }

    static bool IsSafeTopLevelSetupAction(EventActionDefinition action)
    {
        var actionString = action.ToActionString();
        if (string.IsNullOrWhiteSpace(actionString))
            return false;

        var name = actionString.Split(':', 2)[0].Trim();
        return name.Equals("announce", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("notification", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("notify", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("send_message", StringComparison.OrdinalIgnoreCase);
    }

    void Dispatch(string triggerName, object payload)
    {
        triggerName = EventTriggerRegistry.Normalize(triggerName);
        foreach (var runtime in _sessions.Values.ToList())
        {
            foreach (var trigger in runtime.Definition.Triggers.Where(t => EventTriggerRegistry.Normalize(t.Name).Equals(triggerName, StringComparison.OrdinalIgnoreCase)))
            {
                if (!Matches(runtime, trigger, payload))
                    continue;

                ExecuteActions(runtime, trigger.Actions, $"trigger:{triggerName}");
            }
        }
    }

    bool Matches(EventRuntimeSession runtime, EventTriggerDefinition trigger, object payload)
    {
        foreach (var filter in trigger.Filters)
        {
            if (filter.Key.Equals("modeId", StringComparison.OrdinalIgnoreCase) &&
                !runtime.Context.ModeId.Equals(filter.Value, StringComparison.OrdinalIgnoreCase))
                return false;

            if (filter.Key.Equals("sessionId", StringComparison.OrdinalIgnoreCase) &&
                !runtime.Context.SessionId.Equals(filter.Value, StringComparison.OrdinalIgnoreCase))
                return false;

            if (filter.Key.Equals("zoneHash", StringComparison.OrdinalIgnoreCase) &&
                !runtime.Context.ZoneHash.ToString().Equals(filter.Value, StringComparison.OrdinalIgnoreCase))
                return false;

            var property = payload.GetType().GetProperty(filter.Key);
            if (property == null)
                continue;

            var value = property.GetValue(payload)?.ToString() ?? "";
            if (!value.Equals(filter.Value, StringComparison.OrdinalIgnoreCase))
                return false;
        }

        return true;
    }

    void ExecuteActions(EventRuntimeSession runtime, IEnumerable<EventActionDefinition> actions, string reason)
    {
        var context = CreateActionContext(runtime, reason);
        if (context == null)
            return;

        foreach (var action in actions)
        {
            var validation = _manifest.Validate(action);
            if (!validation.Success)
            {
                BattleLuckPlugin.LogWarning($"[EventRuntime] {reason} action '{action.ToActionString()}' skipped before execution: {validation.Error}");
                continue;
            }

            if (TryHandleCustomSequence(runtime, action, context, reason))
                continue;

            var result = _executor.Execute(action.ToActionString(), context);
            if (!result.Success)
                BattleLuckPlugin.LogWarning($"[EventRuntime] {reason} action '{action.ToActionString()}' failed: {result.Error}");
        }
    }

    FlowActionContext? CreateActionContext(EventRuntimeSession runtime, string reason)
    {
        var player = ResolveActionPlayer(runtime);
        if (!player.Exists())
        {
            BattleLuckPlugin.LogWarning($"[EventRuntime] Skipped {reason} for {runtime.Context.SessionId}: no online player entity available for FlowActionExecutor context.");
            return null;
        }

        return new FlowActionContext
        {
            PlayerCharacter = player,
            ZoneHash = runtime.Context.ZoneHash,
            PlayerState = _playerState,
            Registry = _registry,
            Config = runtime.Config,
            Zone = runtime.Zone,
            GameContext = runtime.Context
        };
    }

    bool TryHandleCustomSequence(
        EventRuntimeSession runtime,
        EventActionDefinition action,
        FlowActionContext context,
        string reason)
    {
        var actionString = action.ToActionString();
        if (!_customSequences.TryReadCustomSequenceAction(actionString, out var sequenceId, out var schedule, out var preview))
            return false;

        if (string.IsNullOrWhiteSpace(sequenceId))
        {
            BattleLuckPlugin.LogWarning($"[EventRuntime] {reason} custom sequence action missing sequenceId.");
            return true;
        }

        if (preview)
        {
            var get = _customSequences.Get(sequenceId);
            BattleLuckPlugin.LogInfo(get.Success && get.Value != null
                ? "[EventRuntime] Custom sequence preview:\n" + _customSequences.RenderPreview(get.Value)
                : $"[EventRuntime] Custom sequence preview failed: {get.Error}");
            return true;
        }

        if (!schedule)
        {
            var executed = _customSequences.ExecuteImmediate(sequenceId, _executor, context);
            if (!executed.Success || executed.Value == null)
                BattleLuckPlugin.LogWarning($"[EventRuntime] {reason} custom sequence '{sequenceId}' failed: {executed.Error}");
            else
                BattleLuckPlugin.LogInfo($"[EventRuntime] {reason} custom sequence '{sequenceId}' executed immediately: {executed.Value.Executed} action(s).");
            return true;
        }

        var run = _customSequences.BuildRuntimeRun(sequenceId, runtime.ElapsedSeconds, reason);
        if (!run.Success || run.Value == null)
        {
            var error = run.Error ?? "Unknown error";
            BattleLuckPlugin.LogWarning($"[EventRuntime] {reason} custom sequence '{sequenceId}' could not be queued: {error}");
            ProjectMEventRouter.Instance?.RaiseSequenceFailed(
                new SequenceFailedEvent(runtime.Context.SessionId, sequenceId, error));
            return true;
        }

        runtime.CustomSequenceRuns.Add(run.Value);
        BattleLuckPlugin.LogInfo($"[EventRuntime] Queued custom sequence '{run.Value.SequenceId}' with {run.Value.Steps.Count} step(s) for {runtime.Context.SessionId}.");
        
        // Publish sequence started event
        ProjectMEventRouter.Instance?.RaiseSequenceStarted(
            new SequenceStartedEvent(runtime.Context.SessionId, run.Value.SequenceId, DateTime.UtcNow));
        return true;
    }

    void TickCustomSequences(EventRuntimeSession runtime)
    {
        if (runtime.CustomSequenceRuns.Count == 0)
            return;

        var context = CreateActionContext(runtime, "custom_sequence.tick");
        if (context == null)
            return;

        foreach (var run in runtime.CustomSequenceRuns.ToList())
        {
            foreach (var step in run.Steps.Where(s => !s.Executed && runtime.ElapsedSeconds >= s.DueElapsedSeconds).ToList())
            {
                // Publish step started event
                ProjectMEventRouter.Instance?.RaiseSequenceStepStarted(
                    new SequenceStepStartedEvent(runtime.Context.SessionId, run.SequenceId, step.StepIndex, step.StepLabel));
                
                step.Executed = true;
                var result = _executor.Execute(step.Action, context);
                
                // Publish step completed event
                ProjectMEventRouter.Instance?.RaiseSequenceStepCompleted(
                    new SequenceStepCompletedEvent(runtime.Context.SessionId, run.SequenceId, step.StepIndex, result.Success));
                
                if (!result.Success)
                    BattleLuckPlugin.LogWarning($"[EventRuntime] custom sequence '{run.SequenceId}' step '{step.StepId}' failed: {result.Error}");
            }

            if (run.Complete)
            {
                // Publish sequence completed event
                ProjectMEventRouter.Instance?.RaiseSequenceCompleted(
                    new SequenceCompletedEvent(runtime.Context.SessionId, run.SequenceId));
                runtime.CustomSequenceRuns.Remove(run);
            }
        }
    }

    static int CountConfiguredActions(UnifiedEventDefinition definition)
    {
        var count = definition.Actions.Count;
        count += definition.Objects.Sum(o => o.Actions.Count);
        count += definition.Glows.Sum(g => g.Actions.Count);
        count += definition.VBloodList.Sum(b => b.DeathActions.Count + b.HealthTriggers.Sum(t => t.Actions.Count));
        count += definition.Phases.Sum(p => p.Actions.Count);
        count += definition.Timers.Sum(t => t.OnCompleteActions.Count);
        count += definition.Triggers.Sum(t => t.Actions.Count);
        return count;
    }

    Entity ResolveActionPlayer(EventRuntimeSession runtime)
    {
        var online = VRisingCore.GetOnlinePlayers().Where(e => e.Exists() && e.IsPlayer()).ToList();
        foreach (var steamId in runtime.Context.Players)
        {
            var player = online.FirstOrDefault(e => e.GetSteamId() == steamId);
            if (player.Exists())
                return player;
        }

        return online.FirstOrDefault();
    }

    void EnsureSubscribed()
    {
        if (_subscribed)
            return;

        GameEvents.OnZoneEnter += e => Dispatch("battleluck.zone.enter", e);
        GameEvents.OnZoneExit += e => Dispatch("battleluck.zone.exit", e);
        GameEvents.OnModeStarted += e => Dispatch("battleluck.mode.started", e);
        GameEvents.OnModeEnded += e => Dispatch("battleluck.mode.ended", e);
        GameEvents.OnRoundEnded += e => Dispatch("battleluck.round.ended", e);
        GameEvents.OnPlayerScored += e => Dispatch("battleluck.player.scored", e);
        GameEvents.OnWaveStarted += e => Dispatch("battleluck.wave.started", e);
        GameEvents.OnWaveCleared += e => Dispatch("battleluck.wave.cleared", e);
        GameEvents.OnWaveFinal += e => Dispatch("battleluck.wave.final", e);
        GameEvents.OnObjectiveCaptured += e => Dispatch("battleluck.objective.captured", e);
        GameEvents.OnZoneShrink += e => Dispatch("battleluck.zone.shrink", e);
        GameEvents.OnRealityChanged += e => Dispatch("battleluck.reality.changed", e);
        GameEvents.OnBossSpawned += e => Dispatch("battleluck.boss.spawned", e);
        GameEvents.OnPlatformStateChanged += e => Dispatch("battleluck.platform.state.changed", e);
        GameEvents.OnCrateCollected += e => Dispatch("battleluck.crate.collected", e);
        GameEvents.OnPlayerEliminated += e => Dispatch("battleluck.player.eliminated", e);
        GameEvents.OnPlayerLeft += e => Dispatch("battleluck.player.left", e);
        GameEvents.OnActionPerformed += e => Dispatch("battleluck.action.performed", e);
        GameEvents.OnEloUpdate += e => Dispatch("battleluck.elo.update", e);
        GameEvents.OnWebhookAction += e => Dispatch("battleluck.webhook.action", e);
        GameEvents.OnDiscordCommand += e => Dispatch("battleluck.discord.command", e);

        var router = ProjectMEventRouter.Instance;
        if (router != null)
            SubscribeProjectM(router);

        _subscribed = true;
    }

    void SubscribeProjectM(ProjectMEventRouter router)
    {
        router.OnPlayerDeath += e => Dispatch("projectm.player.death", e);
        router.OnDamageDealt += e => Dispatch("projectm.damage.dealt", e);
        router.OnKill += e => Dispatch("projectm.kill", e);
        router.OnDeathReaction += e => Dispatch("projectm.death.reaction", e);
        router.OnVampireDowned += e => Dispatch("projectm.vampire.downed", e);
        router.OnBuffSpawned += e => Dispatch("projectm.buff.spawned", e);
        router.OnAbilityCastStarted += e => Dispatch("projectm.ability.cast.started", e);
        router.OnMinionSpawned += e => Dispatch("projectm.minion.spawned", e);
        router.OnBuffApplied += e => Dispatch("projectm.buff.applied", e);
        router.OnBuffRemoved += e => Dispatch("projectm.buff.removed", e);
        router.OnItemEquipped += e => Dispatch("projectm.item.equipped", e);
        router.OnItemDropped += e => Dispatch("projectm.item.dropped", e);
        router.OnItemMoved += e => Dispatch("projectm.item.moved", e);
        router.OnItemPickedUp += e => Dispatch("projectm.item.picked.up", e);
        router.OnItemUnequipped += e => Dispatch("projectm.item.unequipped", e);
        router.OnTeleport += e => Dispatch("projectm.teleport", e);
        router.OnMoveTowardsPosition += e => Dispatch("projectm.move.towards.position", e);
        router.OnPlayerLocationTeleport += e => Dispatch("projectm.player.location.teleport", e);
        router.OnPlayerTeleportCommand += e => Dispatch("projectm.player.teleport.command", e);
        router.OnUnitSpawnerReact += e => Dispatch("projectm.unit.spawner.react", e);
        router.OnPrefabSpawned += e => Dispatch("projectm.prefab.spawned", e);
        router.OnMinionSpawnSlot += e => Dispatch("projectm.minion.spawn.slot", e);
        router.OnCharacterRespawn += e => Dispatch("projectm.character.respawn", e);
        router.OnCastleBuff += e => Dispatch("projectm.castle.buff", e);
        router.OnCastleHeartState += e => Dispatch("projectm.castle.heart.state", e);
        router.OnSequencer += e => Dispatch("projectm.sequencer", e);
        router.OnDoorState += e => Dispatch("projectm.door.state", e);
        router.OnCastleFloorWalls += e => Dispatch("projectm.castle.floor.walls", e);
    }
}

public sealed class EventRuntimeStatus
{
    public string SessionId { get; set; } = "";
    public string EventId { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public bool Enabled { get; set; }
    public bool Started { get; set; }
    public float ElapsedSeconds { get; set; }
    public int Zones { get; set; }
    public int Objects { get; set; }
    public int Glows { get; set; }
        public int EntityDefinitions { get; set; }
    public int Phases { get; set; }
    public int Triggers { get; set; }
    public int EventActions { get; set; }
    public int TotalConfiguredActions { get; set; }
    public int ActiveCustomSequences { get; set; }
    public List<string> ExecutedTimedPhases { get; set; } = new();
    public List<EventRuntimeTimerStatus> Timers { get; set; } = new();
    public SpawnedEntityRegistrySnapshot TrackedEntities { get; set; } = new("", 0, 0, new(), new());
}

public sealed class EventRuntimeTimerStatus
{
    public string TimerId { get; set; } = "";
    public string StartPhase { get; set; } = "";
    public int DurationSeconds { get; set; }
    public int RemainingSeconds { get; set; }
    public bool Fired { get; set; }
}

public sealed class EventRuntimeSession
{
    public EventRuntimeSession(UnifiedEventDefinition definition, GameModeContext context, ModeConfig config, ZoneDefinition zone)
    {
        Definition = definition;
        Context = context;
        Config = config;
        Zone = zone;
    }

    public UnifiedEventDefinition Definition { get; }
    public GameModeContext Context { get; }
    public ModeConfig Config { get; }
    public ZoneDefinition Zone { get; }
    public bool Started { get; set; }
    public float ElapsedSeconds { get; set; }
    public bool EndPhasesExecuted { get; set; }
    public HashSet<string> ExecutedTimedPhases { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, EventRuntimeTimer> Timers { get; } = new(StringComparer.OrdinalIgnoreCase);
    public List<CustomSequenceRuntimeRun> CustomSequenceRuns { get; } = new();
    public DateTime LastRuntimeInjectCheckUtc { get; set; } = DateTime.MinValue;
    public string LastRuntimeInjectSignature { get; set; } = "";
}

public sealed class EventRuntimeTimer
{
    public EventRuntimeTimer(EventTimerDefinition definition, DateTime endsAtUtc)
    {
        Definition = definition;
        EndsAtUtc = endsAtUtc;
    }

    public EventTimerDefinition Definition { get; }
    public DateTime EndsAtUtc { get; set; }
    public bool Fired { get; set; }
}
