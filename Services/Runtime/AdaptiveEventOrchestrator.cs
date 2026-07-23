using BattleLuck.Models;
using BattleLuck.Services.Npc;
using BattleLuck.Services.Drills;
using BattleLuck.Services.Spawn;
using SpawnController = BattleLuck.Services.Spawn.SpawnController;

namespace BattleLuck.Services.Runtime;

/// <summary>
/// Main orchestrator for the adaptive event-driven NPC spawning and AI behavior system.
/// Coordinates the six cooperating layers:
///   1. Event Start → Participant Analysis → Catalog Loading → Spawn Planning → NPC Spawning → AI Runtime
///   2. Tick → Movement → Observation → Pattern Recognition → Behavior Transitions → NPC Actions
///   3. Event End → Stop Controllers → Despawn NPCs → Validate/Issue Rewards → Release Catalog
/// </summary>
public sealed class AdaptiveEventOrchestrator
{
    public static AdaptiveEventOrchestrator Instance { get; } = new();

    readonly EventParticipantAnalyzer _analyzer = EventParticipantAnalyzer.Instance;
    readonly EventCatalogContextService _catalogService = EventCatalogContextService.Instance;
    readonly AdaptiveSpawnPlanner _planner = AdaptiveSpawnPlanner.Instance;
    readonly EventRewardLimiter _rewardLimiter = EventRewardLimiter.Instance;
    readonly AdaptiveNpcController _npcController = AdaptiveNpcController.Instance;
    readonly CombatDrillController _drillController = CombatDrillController.Instance;
    readonly SpawnController _spawner = new();

    // Track active event sessions
    readonly Dictionary<string, ActiveEventState> _activeEvents = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Called when an event starts. Performs the full startup sequence:
    /// analyze participants → load catalog → build spawn plan → spawn NPCs → attach AI controllers.
    /// </summary>
    public void OnEventStart(GameModeContext context, ZoneDefinition zone)
    {
        if (context == null) return;

        var modeId = context.ModeId;
        if (string.IsNullOrWhiteSpace(modeId)) return;

        // Step 1: Load the event adaptive config
        var fullCatalog = EventCatalogLoader.Instance.LoadCatalog();
        if (fullCatalog == null)
        {
            BattleLuckPlugin.LogInfo($"[AdaptiveEvent] No catalog for event '{modeId}', skipping adaptive spawning.");
            return;
        }

        if (!fullCatalog.Events.TryGetValue(modeId, out var eventConfig) || !eventConfig.Enabled)
        {
            BattleLuckPlugin.LogInfo($"[AdaptiveEvent] Event '{modeId}' has no adaptive config or is disabled.");
            return;
        }

        // Step 2: Analyze participants
        var participants = _analyzer.Analyze(context);
        if (participants.PlayerCount == 0)
        {
            BattleLuckPlugin.LogInfo($"[AdaptiveEvent] No participants for event '{modeId}', skipping adaptive spawning.");
            return;
        }

        // Step 3: Build event-scoped catalog context
        var catalog = _catalogService.BuildContext(modeId);

        // Step 4: Build the spawn plan
        var plan = _planner.BuildPlan(modeId, participants, catalog, eventConfig);
        if (plan.Waves.Count == 0 || plan.Waves.All(w => w.Npcs.Count == 0))
        {
            BattleLuckPlugin.LogInfo($"[AdaptiveEvent] Spawn plan for '{modeId}' has no NPCs to spawn.");
            return;
        }

        // Step 5: Initialize reward tracking
        _rewardLimiter.InitializeSession(context.SessionId, plan.RewardBudget);

        // Step 6: Store active event state
        var state = new ActiveEventState
        {
            SessionId = context.SessionId,
            ModeId = modeId,
            Plan = plan,
            Config = eventConfig,
            Catalog = catalog,
            Participants = participants,
            Zone = zone,
            CurrentWaveIndex = -1,
            WaveStartTimes = new Dictionary<int, DateTime>(),
            WavePerformance = new List<WavePerformanceData>(),
            SpawnedNpcIds = new List<string>(),
            WaveDelayRemaining = 0f
        };
        _activeEvents[context.SessionId] = state;

        // Step 7: Spawn the first wave immediately
        SpawnNextWave(state, context);

        BattleLuckPlugin.LogInfo($"[AdaptiveEvent] Event '{modeId}' started: {participants.PlayerCount} players, " +
            $"{plan.Waves.Count} waves, avg strength {participants.AverageCombatStrength:F1}.");
    }

    /// <summary>
    /// Called each server tick. Updates movement, observes player state,
    /// evaluates behavior transitions, and executes NPC actions.
    /// </summary>
    public void OnEventTick(GameModeContext context, float deltaSeconds)
    {
        if (context == null) return;

        // Tick the adaptive NPC controller (handles all registered NPCs)
        _npcController.Tick(deltaSeconds);

        // Check wave completion for active events
        if (_activeEvents.TryGetValue(context.SessionId, out var state))
        {
            CheckWaveCompletion(state, context, deltaSeconds);
        }
    }

    /// <summary>
    /// Called when an event ends. Stops controllers, despawns NPCs,
    /// validates/issue rewards, and releases the catalog context.
    /// </summary>
    public void OnEventEnd(GameModeContext context)
    {
        if (context == null) return;

        var sessionId = context.SessionId;

        if (!_activeEvents.TryGetValue(sessionId, out var state))
            return;

        // Stop drill controllers
        _drillController.StopSessionDrills(sessionId);

        // Unregister NPCs from adaptive controller
        _npcController.UnregisterSessionId(sessionId);

        // Despawn all NPCs for this session
        if (BattleLuckPlugin.NpcService != null)
        {
            var despawned = BattleLuckPlugin.NpcService.DespawnSession(sessionId);
            BattleLuckPlugin.LogInfo($"[AdaptiveEvent] Despawned {despawned} NPCs for session '{sessionId}'.");
        }

        // Log reward summary
        var summary = _rewardLimiter.GetSummary(sessionId);
        if (summary != null)
        {
            BattleLuckPlugin.LogInfo($"[AdaptiveEvent] Event '{state.ModeId}' ended: {summary.TotalItemsAwarded} items awarded to {summary.PlayersRewarded} players.");
        }

        // Clean up reward state
        _rewardLimiter.CleanupSession(sessionId);

        // Clear wave delay and remove active event state
        _activeEvents.Remove(sessionId);

        BattleLuckPlugin.LogInfo($"[AdaptiveEvent] Event '{state.ModeId}' session '{sessionId}' cleaned up.");
    }

    /// <summary>
    /// Spawn the next wave of NPCs for an active event.
    /// </summary>
    void SpawnNextWave(ActiveEventState state, GameModeContext context)
    {
        state.CurrentWaveIndex++;
        if (state.CurrentWaveIndex >= state.Plan.Waves.Count)
        {
            BattleLuckPlugin.LogInfo($"[AdaptiveEvent] All waves completed for session '{state.SessionId}'.");
            return;
        }

        var wave = state.Plan.Waves[state.CurrentWaveIndex];
        var center = state.Zone?.Position.ToFloat3() ?? float3.zero;

        // Apply dynamic difficulty adjustment if we have performance data
        var adjustment = CalculateDifficultyAdjustment(state);
        var healthMult = adjustment.HealthMultiplier;
        var damageMult = adjustment.DamageMultiplier;

        state.WaveStartTimes[state.CurrentWaveIndex] = DateTime.UtcNow;

        // Spawn each NPC in the wave
        int spawnIndex = 0;
        foreach (var npcPlan in wave.Npcs)
        {
            for (int i = 0; i < npcPlan.Count; i++)
            {
                var prefab = npcPlan.PrefabGuid;
                if (prefab == PrefabGUID.Empty) continue;

                var offset = new float3(
                    (spawnIndex % 5) * 3f - 6f,
                    0,
                    (spawnIndex / 5) * 3f - 3f);
                var pos = center + offset;

                var capturedPlan = npcPlan;
                var capturedIndex = spawnIndex;

                _spawner.SpawnNPC(prefab, pos, entity =>
                {
                    if (!entity.Exists()) return;

                    var npcId = $"adaptive_{state.SessionId}_{state.CurrentWaveIndex}_{capturedIndex}";

                    // Register with NpcControlService
                    var registration = BattleLuckPlugin.NpcService?.RegisterNpc(
                        state.SessionId, npcId, capturedPlan.CatalogId, prefab, entity, pos, 80f);

                    if (registration == null || !registration.Success || registration.Value == null)
                        return;

                    state.SpawnedNpcIds.Add(npcId);

                    // Apply health and damage scaling
                    try
                    {
                        if (entity.Has<Health>())
                        {
                            var health = entity.Read<Health>();
                            health.MaxHealth._Value *= healthMult;
                            health.Value = Math.Min(health.Value, health.MaxHealth._Value);
                            entity.Write(health);
                        }
                    }
                    catch { }

                    // Find a player to observe (first available)
                    var targetPlayer = FindTargetPlayer(context);
                    if (targetPlayer == Entity.Null) return;

                    // Register with adaptive NPC controller
                    _npcController.RegisterSession(
                        npcId,
                        state.SessionId,
                        entity,
                        targetPlayer,
                        targetPlayer.GetSteamId(),
                        pos);

                    // Start drill if the wave has one
                    if (!string.IsNullOrWhiteSpace(wave.DrillId) &&
                        state.Catalog.Drills.TryGetValue(wave.DrillId, out var drillDef))
                    {
                        var drill = new BattleLuck.Services.Drills.CombatDrillDefinition
                        {
                            Id = drillDef.Id,
                            DisplayName = drillDef.Id,
                            Description = $"Drill for wave {state.CurrentWaveIndex}",
                            TriggerPattern = "*",
                            DefaultReaction = drillDef.Reaction,
                            Priority = 1,
                            Rules = new List<DrillReactionRule>
                            {
                                new()
                                {
                                    Id = $"{drillDef.Id}_default",
                                    Trigger = "*",
                                    ReactionMode = drillDef.Reaction,
                                    CooldownSeconds = 2f,
                                    DurationSeconds = 3f,
                                    Priority = 1
                                }
                            },
                            Enabled = true
                        };
                        _drillController.StartDrill(npcId, wave.DrillId, drill);
                    }
                });

                spawnIndex++;
            }
        }

        BattleLuckPlugin.LogInfo($"[AdaptiveEvent] Spawned wave {state.CurrentWaveIndex + 1}/{state.Plan.Waves.Count} " +
            $"({wave.Npcs.Sum(n => n.Count)} NPCs) for session '{state.SessionId}'.");
    }

    /// <summary>
    /// Check if the current wave has been completed and queue the next one.
    /// Uses tick-based delay accumulator instead of EnqueueDelayed.
    /// </summary>
    void CheckWaveCompletion(ActiveEventState state, GameModeContext context, float deltaSeconds)
    {
        if (state.CurrentWaveIndex < 0) return;
        if (state.CurrentWaveIndex >= state.Plan.Waves.Count) return;

        // If we're in a delay between waves, count down
        if (state.WaveDelayRemaining > 0f)
        {
            state.WaveDelayRemaining -= deltaSeconds;
            if (state.WaveDelayRemaining <= 0f)
            {
                state.WaveDelayRemaining = 0f;
                SpawnNextWave(state, context);
            }
            return;
        }

        var wave = state.Plan.Waves[state.CurrentWaveIndex];

        // Check if all NPCs in this wave are dead
        var aliveNpcs = state.SpawnedNpcIds
            .Select(id => BattleLuckPlugin.NpcService?.GetEntry(id))
            .Where(e => e != null && e.IsAlive)
            .ToList();

        if (aliveNpcs.Count == 0)
        {
            // Wave completed — record performance
            var elapsed = (float)(DateTime.UtcNow - state.WaveStartTimes[state.CurrentWaveIndex]).TotalSeconds;
            state.WavePerformance.Add(new WavePerformanceData
            {
                WaveIndex = state.CurrentWaveIndex,
                ClearTimeSeconds = elapsed
            });

            BattleLuckPlugin.LogInfo($"[AdaptiveEvent] Wave {state.CurrentWaveIndex + 1} completed in {elapsed:F1}s for session '{state.SessionId}'.");

            // Set delay before next wave
            var delay = wave.StartDelaySeconds > 0 ? wave.StartDelaySeconds : 3f;
            state.WaveDelayRemaining = delay;
        }
    }

    /// <summary>
    /// Calculate difficulty adjustment based on previous wave performance.
    /// </summary>
    static DifficultyAdjustment CalculateDifficultyAdjustment(ActiveEventState state)
    {
        var adjustment = new DifficultyAdjustment();

        if (state.WavePerformance.Count == 0)
            return adjustment;

        var lastWave = state.WavePerformance[^1];
        var config = state.Config.AdaptiveScaling;
        if (!config.Enabled)
            return adjustment;

        // If players cleared the wave quickly with high health, increase difficulty
        if (lastWave.ClearTimeSeconds < 30f && lastWave.AveragePlayerHealthRemaining > 0.7f)
        {
            adjustment.DifficultyMultiplier = Math.Min(1.2f, config.MaximumDifficultyMultiplier);
            adjustment.HealthMultiplier = 1.1f;
            adjustment.DamageMultiplier = 1.1f;
            adjustment.NpcCountAdjustment = 1;
            adjustment.PromoteToElite = config.AllowElitePromotion;
        }
        // If players struggled, decrease difficulty
        else if (lastWave.ClearTimeSeconds > 120f || lastWave.PlayerDeaths > 2)
        {
            adjustment.DifficultyMultiplier = Math.Max(0.85f, config.MinimumDifficultyMultiplier);
            adjustment.HealthMultiplier = 0.9f;
            adjustment.DamageMultiplier = 0.9f;
        }

        return adjustment;
    }

    /// <summary>
    /// Find a target player entity from the event context.
    /// </summary>
    static Entity FindTargetPlayer(GameModeContext context)
    {
        foreach (var player in VRisingCore.GetOnlinePlayers())
        {
            if (player.Exists() && player.IsPlayer() && context.Players.Contains(player.GetSteamId()))
                return player;
        }
        return Entity.Null;
    }

    /// <summary>
    /// Get the active event state for a session.
    /// </summary>
    public ActiveEventState? GetActiveState(string sessionId)
    {
        return _activeEvents.TryGetValue(sessionId, out var state) ? state : null;
    }
}

/// <summary>
/// Tracks the state of an active adaptive event session.
/// </summary>
public sealed class ActiveEventState
{
    public string SessionId { get; init; } = "";
    public string ModeId { get; init; } = "";
    public AdaptiveSpawnPlan Plan { get; init; } = new AdaptiveSpawnPlan("", 0, Array.Empty<SpawnNpcPlan>());
    public EventAdaptiveConfig Config { get; init; } = new();
    public EventCatalogContext Catalog { get; init; } = new();
    public EventParticipantProfile Participants { get; init; } = new EventParticipantProfile(Array.Empty<PlayerCombatProfile>(), 0);
    public ZoneDefinition Zone { get; init; } = new();
    public int CurrentWaveIndex { get; set; } = -1;
    public Dictionary<int, DateTime> WaveStartTimes { get; init; } = new();
    public List<WavePerformanceData> WavePerformance { get; init; } = new();
    public List<string> SpawnedNpcIds { get; init; } = new();
    public float WaveDelayRemaining { get; set; }
}
