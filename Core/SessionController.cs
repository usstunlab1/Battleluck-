using BattleLuck.ECS.Actions.Components;
using BattleLuck.Services.Modes;
using ProjectM.Shared;

/// <summary>
/// Central session manager with toggle-based enter/exit flow.
///
/// Enter flow:
///   1. Player uses .toggleenter OR walks into a mapped zone
///   2. Enter flow prepares player and queues arena build work
///   3. Arena finishes building + 2-minute preparation window elapses
///   4. Joined players are teleported, stunned for 10 seconds, then mode starts
///
/// Exit flow:
///   - .toggleleave: clean exit (kit cleared, exit preset applied, snapshot restored)
///   - Walking out without .toggleleave: burning effect until death
///   - Penalty death: restore old kit/loadout, teleport to penalty spawn (-1000, 0, -500)
/// </summary>
public sealed class SessionController
{
    readonly GameModeRegistry _registry;
    readonly PlayerStateController _playerState;
    readonly FlowController _flow;
    readonly ZoneDetectionSystem _zoneDetection;
    readonly SpawnController _spawner = new();
    readonly AutoTrashController _autoTrash = new();
    readonly global::BattleLuck.Services.Runtime.EventDefinitionLoader _eventDefinitions = new();
    readonly global::BattleLuck.Services.Runtime.EventRuntimeController _eventRuntime;
    readonly FlowActionExecutor _actionExecutor;

    public AutoTrashController AutoTrash => _autoTrash;
    public EventRuntimeController EventRuntime => _eventRuntime;

    // Active sessions: zoneHash → ActiveSession
    readonly Dictionary<int, ActiveSession> _activeSessions = new();
    readonly List<int> _pendingEnd = new();
    static readonly Dictionary<string, int> ArenaRotationCursor = new(StringComparer.OrdinalIgnoreCase);
    // Managed player session lifecycle state.
    readonly Dictionary<ulong, PlayerEventSession> _playerSessions = new();

    // Zone hash → mode ID mapping
    readonly Dictionary<int, string> _zoneModeMap = new();

    // Toggle-enter state tracking
    readonly HashSet<ulong> _readyPlayers = new();           // Players who used .toggleenter (ready to enter)
    readonly HashSet<ulong> _enteringPlayers = new();        // Players currently running the enter flow
    readonly HashSet<ulong> _enteredPlayers = new();          // Players inside zone via proper enter flow
    readonly HashSet<ulong> _arenaTeleportedPlayers = new();  // Leave boundary activates only after teleport + start
    readonly HashSet<ulong> _penaltyBurning = new();          // Players burning from unauthorized exit
    readonly Dictionary<ulong, int> _playerZoneMap = new();   // steamId → zoneHash they entered properly
    // steamId → remaining suppress ticks after death (prevents respawn teleport triggering walk-out penalty)
    readonly Dictionary<ulong, int> _recentlyDied = new();
    readonly Dictionary<ulong, int> _offlineTicks = new();     // Debounced disconnect cleanup for entered players
    readonly Dictionary<ulong, PendingArenaRespawn> _pendingArenaRespawns = new();

    // Penalty spawn point
    static readonly float3 PenaltySpawn = new(-1000f, 0f, -500f);

    // Penalty HP drain per tick (percentage of max HP)
    const float PenaltyDrainPercent = 0.08f;

    // Fallback mode duration when MatchDurationMinutes is 0 in session.json
    const int DefaultModeDurationSeconds = 120; // 2 minutes
    // Native castle-tile instantiation can terminate the dedicated server
    // without a managed exception. Event arenas use logical boundaries only.
    static readonly bool EventGeometryMutationsEnabled = false;
    const int ArenaPreparationSeconds = 120;
    const int MatchStartStunSeconds = 10;
    const string StagedEnterPlayersStateKey = "stagedEnterPlayers";
    readonly record struct PendingArenaRespawn(int ZoneHash, int AttemptsRemaining);

    public SessionController(GameModeRegistry registry, PlayerStateController playerState, FlowController flow, ZoneDetectionSystem zoneDetection)
    {
        _registry = registry;
        _playerState = playerState;
        _flow = flow;
        _zoneDetection = zoneDetection;
        _eventRuntime = new EventRuntimeController(playerState, registry);
        _actionExecutor = new FlowActionExecutor(playerState, registry);
    }

    public void Initialize()
    {
        foreach (var modeId in _registry.GetRegisteredModes())
        {
            TryRegisterModeZones(modeId, out _);
        }

        _zoneDetection.OnPlayerEnterZone += HandlePlayerWalkIntoZone;
        _zoneDetection.OnPlayerExitZone += HandlePlayerWalkOutOfZone;
        DeathHook.OnDeath += HandleDeath;

        _autoTrash.Initialize();
        BattleLuckPlugin.LogInfo($"[SessionController] Initialized with {_zoneModeMap.Count} zone-mode mappings.");
    }

    /// <summary>
    /// Add zones for a mode created after server startup to the live detection
    /// map. This keeps .toggleenter and walk-in detection working immediately.
    /// </summary>
    public bool TryRegisterModeZones(string modeId, out string error)
    {
        error = "";
        try
        {
            var config = LoadEffectiveConfig(modeId);
            if (config.Zones.Zones.Count == 0)
            {
                error = $"Event '{modeId}' has no zones.";
                return false;
            }

            foreach (var zone in config.Zones.Zones)
            {
                if (zone.Hash == 0)
                {
                    error = $"Event '{modeId}' contains a zone with hash 0.";
                    return false;
                }

                _zoneModeMap[zone.Hash] = modeId;
            }

            return true;
        }
        catch (Exception ex)
        {
            error = $"Could not register zones for '{modeId}': {ex.Message}";
            return false;
        }
    }

    public ActiveSession? GetSessionByEntity(Entity sessionEntity)
    {
        return _activeSessions.Values.FirstOrDefault(s => s.SessionEntity == sessionEntity);
    }

    public void Shutdown()
    {
        _zoneDetection.OnPlayerEnterZone -= HandlePlayerWalkIntoZone;
        _zoneDetection.OnPlayerExitZone -= HandlePlayerWalkOutOfZone;
        DeathHook.OnDeath -= HandleDeath;

        foreach (var session in _activeSessions.Values)
        {
            session.Spawner.DespawnAll();
            if (EventGeometryMutationsEnabled)
            {
                session.Border?.DespawnWalls();
                session.Border?.DespawnFloors();
                session.Platform?.DespawnPlatform();
                if (session.Context != null)
                    SchematicLoader.ClearTrackingGroup(session.Context.SessionId);
            }
        }
        _eventRuntime.Shutdown();
        _activeSessions.Clear();
        _readyPlayers.Clear();
        _enteringPlayers.Clear();
        _enteredPlayers.Clear();
        _arenaTeleportedPlayers.Clear();
        _penaltyBurning.Clear();
        _playerZoneMap.Clear();
        _recentlyDied.Clear();
        _offlineTicks.Clear();
        _pendingArenaRespawns.Clear();
    }

    ModeConfig LoadEffectiveConfig(string modeId) => _eventDefinitions.LoadEffectiveConfig(modeId);

    UnifiedEventDefinition? LoadRuntimeDefinition(string modeId) => _eventDefinitions.LoadRuntimeDefinition(modeId);

    // ── Public API for commands ─────────────────────────────────────────

    /// <summary>
    /// Toggle-enter: mark player as ready and execute the enter flow.
    /// If modeId is provided and player is outside, teleport them to the zone first.
    /// </summary>
    public OperationResult ToggleEnter(ulong steamId, Entity playerCharacter, string? modeId = null)
    {
        if (steamId == 0 || !playerCharacter.Exists() || !playerCharacter.IsPlayer())
            return OperationResult.Fail("Connected player identity is unavailable. Retry after the character finishes loading.");

        if (_enteredPlayers.Contains(steamId))
            return OperationResult.Fail("You are already in an active session.");
        if (_enteringPlayers.Contains(steamId))
            return OperationResult.Fail("You are already entering an active session.");

        int zoneHash = 0;
        try
        {
            zoneHash = _zoneDetection.GetPlayerZone(steamId);
        }
        catch (Exception ex)
        {
            BattleLuckLogger.ErrorWithContext("Zone detection failed during ToggleEnter.", new
            {
                SteamId = steamId,
                ModeId = modeId ?? "(null)",
                Exception = ex.Message
            });
            return OperationResult.Fail("Could not determine your zone. Please try again in a few seconds.");
        }

        // Player is outside any zone — need a mode name to know where to go
        if (zoneHash == 0)
        {
            if (string.IsNullOrEmpty(modeId))
                return OperationResult.Fail("You are not in a zone. Use: .toggleenter <modeName>");

            // Find zone for the requested mode
            var zoneEntry = _zoneModeMap.FirstOrDefault(kv => kv.Value.Equals(modeId, StringComparison.OrdinalIgnoreCase));
            if (zoneEntry.Value == null)
                return OperationResult.Fail($"Unknown mode '{modeId}'. Use .modelist to see available modes.");

            zoneHash = zoneEntry.Key;
            var config = LoadEffectiveConfig(modeId);
            var zone = config.Zones.Zones.FirstOrDefault(z => z.Hash == zoneHash);
            if (zone == null)
                return OperationResult.Fail($"Zone definition not found for mode '{modeId}'.");

            // Do not teleport yet: ExecuteEnterFlow snapshots the real return
            // position before event cleanup, kit application, and arena teleport.
        }

        if (!_zoneModeMap.TryGetValue(zoneHash, out var resolvedModeId))
            return OperationResult.Fail("This zone is not mapped to any game mode.");

        _readyPlayers.Add(steamId);
        try
        {
            var result = ExecuteEnterFlow(steamId, playerCharacter, zoneHash, resolvedModeId);
            if (!result.Success && !_enteredPlayers.Contains(steamId))
                _readyPlayers.Remove(steamId);
            return result;
        }
        catch (Exception ex)
        {
            // Defensive gate: never let an unhandled exception disconnect the player.
            _readyPlayers.Remove(steamId);
            _enteringPlayers.Remove(steamId);
            BattleLuckLogger.ErrorWithContext("Unhandled exception in ExecuteEnterFlow.", new
            {
                SteamId = steamId,
                ZoneHash = zoneHash,
                ModeId = resolvedModeId,
                Exception = ex.ToString()
            });
            return OperationResult.Fail("Enter failed due to a server error. Please try again or contact an admin.");
        }
    }

    /// <summary>
    /// Toggle-leave: clean exit with kit restore.
    /// Called from .toggleleave command.
    /// </summary>
    public OperationResult ToggleLeave(ulong steamId, Entity playerCharacter)
    {
        if (!_enteredPlayers.Contains(steamId))
            return OperationResult.Fail("You are not in an active session. Use .toggleenter to join.");

        if (!_playerZoneMap.TryGetValue(steamId, out var zoneHash))
            return OperationResult.Fail("Cannot determine your zone.");

        return ExecuteLeaveFlow(steamId, playerCharacter, zoneHash);
    }

    /// <summary>Check if a player is properly entered (for command validation).</summary>
    public bool IsPlayerEntered(ulong steamId) => _enteredPlayers.Contains(steamId);

    /// <summary>Check if a player is burning from penalty.</summary>
    public bool IsPlayerBurning(ulong steamId) => _penaltyBurning.Contains(steamId);

    // ── Core enter/exit flows ───────────────────────────────────────────

    OperationResult ExecuteEnterFlow(ulong steamId, Entity playerCharacter, int zoneHash, string modeId, float3? returnPositionOverride = null, bool skipEnterActions = false)
    {
        if (_enteredPlayers.Contains(steamId))
            return OperationResult.Fail("You are already in an active session.");
        if (!_enteringPlayers.Add(steamId))
            return OperationResult.Fail("Enter is already in progress.");

        var config = LoadEffectiveConfig(modeId);
        var zone = config.Zones.Zones.FirstOrDefault(z => z.Hash == zoneHash);
        if (zone == null)
        {
            _enteringPlayers.Remove(steamId);
            return OperationResult.Fail($"Zone definition not found for hash {zoneHash}.");
        }

        var rules = config.Session.Rules;
        var staging = ResolveActionStagingRules(config);
        var stageEnterActions = staging.Enabled && staging.StageOnZoneEnter && !skipEnterActions;
        var session = GetOrCreateSession(zoneHash, modeId);

        // Check player limits
        if (session.Context.Players.Count >= rules.MaxPlayers)
        {
            _enteringPlayers.Remove(steamId);
            return OperationResult.Fail("Zone is full.");
        }

        if (session.IsStarted && !rules.AllowLateJoin)
        {
            _enteringPlayers.Remove(steamId);
            return OperationResult.Fail("This match already started and late join is disabled.");
        }

        // Execute enter flow: save snapshot → clear kit → apply enter kit → heal → teleport
        var returnPositionKey = $"returnPosition:{steamId}";
        if (returnPositionOverride.HasValue)
            session.Context.State[returnPositionKey] = returnPositionOverride.Value;

        OperationResult enterResult;
        try
        {
            if (skipEnterActions || stageEnterActions)
            {
                // Skip enter actions and do only safe player prep/placement.
                // Used by admin safety mode and action staging mode.
                var prepareResult = _playerState.PrepareForEventEntry(playerCharacter, zoneHash, returnPositionOverride, steamId, session.Context.SessionId, modeId);
                if (!prepareResult.Success)
                {
                    _enteringPlayers.Remove(steamId);
                    return OperationResult.Fail($"Enter prep failed: {prepareResult.Error}");
                }

                if (rules.EnablePvP)
                    playerCharacter.SetTeam(zoneHash + (int)(steamId % 1000));

                playerCharacter.HealToFull();

                if (stageEnterActions)
                    MarkStagedEnterAction(session, steamId);

                enterResult = OperationResult.Ok();
            }
            else
            {
                enterResult = _flow.ExecuteEnter(config, playerCharacter, zone, session.Context, steamId);
            }
        }
        catch
        {
            _enteringPlayers.Remove(steamId);
            throw;
        }
        finally
        {
            session.Context.State.Remove(returnPositionKey);
        }

        if (!enterResult.Success)
        {
            _enteringPlayers.Remove(steamId);
            return OperationResult.Fail($"Enter failed: {enterResult.Error}");
        }

        if (session.Context.State.ContainsKey("arenaSpawningRequested"))
            session.ArenaSpawning = true;

        // Mark as properly entered
        _enteredPlayers.Add(steamId);
        _enteringPlayers.Remove(steamId);
        _playerZoneMap[steamId] = zoneHash;
        _zoneDetection.SetPlayerZone(steamId, zoneHash);
        _readyPlayers.Remove(steamId);
        _penaltyBurning.Remove(steamId);

        session.Context.Players.Add(steamId);

        // Add EventParticipant component
        var em = VRisingCore.EntityManager;
        if (playerCharacter.Exists() && !em.HasComponent<EventParticipant>(playerCharacter))
        {
            em.AddComponentData(playerCharacter, new EventParticipant
            {
                SessionEntity = session.SessionEntity,
                SessionId = session.Context.SessionId,
                DeathCount = 0,
                Eliminated = false,
                TeamIndex = session.Context.Teams.TryGetValue(steamId, out var team) ? team : 0
            });
        }

        BattleLuckPlugin.ZoneMap?.HandlePlayerEnteredZone(steamId, playerCharacter, zone);
        BattleLuckPlugin.EquipmentTracker?.StartTrackingPlayer(playerCharacter);

        if (!session.EventRuntimeInitialized && !session.ArenaInitialized)
        {
            session.Context.State["arenaBuildOwnerSteamId"] = steamId;
            session.Context.State["arenaBuildInitializedOnce"] = true;
        }

        if (!session.EventRuntimeInitialized && session.EventDefinition != null)
        {
            session.EventRuntimeInitialized = true;
            _eventRuntime.StartSession(session.EventDefinition, session.Context, session.Config, zone);
            if (session.Context.State.ContainsKey("arenaSpawningRequested"))
                session.ArenaSpawning = true;
        }

        // Spawn arena assets exactly once, on first successful player entry.
        if (!session.ArenaInitialized)
        {
            session.ArenaInitialized = true;
            SpawnArenaTiles(session, zone, modeId, config);
            EnsureArenaPreparationScheduled(session, modeId);
            BattleLuckPlugin.LogInfo($"[Session] Arena initialized for {modeId} zone {zone.Hash} on first entry ({steamId}).");
        }
        else if (session.Context.State.TryGetValue("arenaBuildOwnerSteamId", out var owner) && owner is ulong ownerSteamId && ownerSteamId != steamId)
        {
            BattleLuckPlugin.LogInfo($"[Session] Player {steamId} joined existing arena {modeId}/{zone.Hash}; geometry owner is first entrant {ownerSteamId}, no build replay.");
        }

        if (session.StartWarmupActive)
            PreparePlayerForStartWarmup(session, playerCharacter, zone);

        if (session.IsStarted)
        {
            if (!_arenaTeleportedPlayers.Contains(steamId))
                PreparePlayerForStartWarmup(session, playerCharacter, zone);
            session.Mode?.OnPlayerJoin(session.Context, steamId);
        }
        else if (session.Context.Players.Count >= rules.MinPlayers)
        {
            EnsureArenaPreparationScheduled(session, modeId);
        }

        BattleLuckPlugin.LogInfo($"[Session] Player {EntityExtensions.FormatPlayer(steamId, playerCharacter)} entered zone {zone.Name} ({modeId}) via toggle-enter.");
        return OperationResult.Ok();
    }

    OperationResult ExecuteLeaveFlow(ulong steamId, Entity playerCharacter, int zoneHash)
    {
        if (!_zoneModeMap.TryGetValue(zoneHash, out var modeId))
            return OperationResult.Fail("Zone not mapped.");

        var config = LoadEffectiveConfig(modeId);
        var zone = config.Zones.Zones.FirstOrDefault(z => z.Hash == zoneHash);
        if (zone == null)
            return OperationResult.Fail("Zone definition not found.");

        if (!_activeSessions.TryGetValue(zoneHash, out var session))
            return OperationResult.Fail("No active session.");

        // Execute exit flow: clear kit → restore snapshot
        var exitResult = _flow.ExecuteExit(config, playerCharacter, zone, session.Context);
        if (!exitResult.Success)
            BattleLuckPlugin.LogError($"[Session] Exit flow failed for {EntityExtensions.FormatPlayer(steamId, playerCharacter)}: {exitResult.Error}");

        // Clean up player tracking
        CleanupPlayerState(steamId, session);

        BattleLuckPlugin.LogInfo($"[Session] Player {EntityExtensions.FormatPlayer(steamId, playerCharacter)} exited zone {zoneHash} ({modeId}) via toggle-leave.");
        return OperationResult.Ok();
    }

    void CleanupPlayerState(ulong steamId, ActiveSession session)
    {
        _enteredPlayers.Remove(steamId);
        _arenaTeleportedPlayers.Remove(steamId);
        _pendingArenaRespawns.Remove(steamId);
        _playerZoneMap.Remove(steamId);
        _readyPlayers.Remove(steamId);
        _penaltyBurning.Remove(steamId);
        session.Context.Players.Remove(steamId);

        // Remove EventParticipant component
        var em = VRisingCore.EntityManager;
        var playerEntity = VRisingCore.GetPlayerEntityBySteamId(steamId);
        if (playerEntity.Exists() && em.HasComponent<EventParticipant>(playerEntity))
            em.RemoveComponent<EventParticipant>(playerEntity);

        // Remove ignite visual when leaving zone
        var player = VRisingCore.GetOnlinePlayers().FirstOrDefault(e => e.IsPlayer() && e.GetSteamId() == steamId);
        if (player.Exists())
        {
            player.TryRemoveBuff(Prefabs.Buff_General_Ignite);
            RemoveZoneProtectionBuffs(player);
        }

        session.Mode?.OnPlayerLeave(session.Context, steamId);
        FloorLockService.UnlockPlayer(steamId);
        BattleLuckPlugin.EquipmentTracker?.StopTrackingPlayer(steamId);

        if (session.Context.Players.Count == 0)
            EndSession(session.Context.ZoneHash);
    }

    // ── Zone walk-in/walk-out handlers ──────────────────────────────────

    /// <summary>
    /// Fired when ZoneDetection detects player walked into a zone.
    /// Auto-enters if player is ready, otherwise just tracks position.
    /// </summary>
    void HandlePlayerWalkIntoZone(ulong steamId, Entity playerEntity, ZoneDefinition zone)
    {
        if (!_zoneModeMap.TryGetValue(zone.Hash, out var modeId)) return;

        var config = LoadEffectiveConfig(modeId);
        var zoneEnterRule = ResolveZoneEnterRule(config);

        // If already properly entered, ignore (re-entering after penalty teleport, etc.)
        if (_enteredPlayers.Contains(steamId)) return;
        if (_enteringPlayers.Contains(steamId)) return;

        if (zoneEnterRule.Equals("manual_only", StringComparison.OrdinalIgnoreCase))
            return;

        if (zoneEnterRule.Equals("ready_check", StringComparison.OrdinalIgnoreCase) && !_readyPlayers.Contains(steamId))
            return;

        // Auto-enter: treat walking into zone as ready + enter
        var result = ExecuteEnterFlow(steamId, playerEntity, zone.Hash, modeId, _zoneDetection.GetLastOutsidePosition(steamId));
        if (result.Success)
        {
            BattleLuckPlugin.LogInfo($"[Session] Player {EntityExtensions.FormatPlayer(steamId, playerEntity)} auto-entered zone {zone.Name} by walking in.");
        }
        else
        {
            BattleLuckPlugin.LogWarning($"[Session] Auto-enter failed for {EntityExtensions.FormatPlayer(steamId, playerEntity)}: {result.Error}");
        }
    }

    /// <summary>
    /// Fired when ZoneDetection detects player walked out of a zone.
    /// If they didn't use .toggleleave, treat it as an unsafe/disconnected exit.
    /// </summary>
    void HandlePlayerWalkOutOfZone(ulong steamId, Entity playerEntity, int previousZoneHash)
    {
        if (!_zoneModeMap.TryGetValue(previousZoneHash, out var modeId)) return;

        // If not properly entered, nothing to penalize
        if (!_enteredPlayers.Contains(steamId))
        {
            BattleLuckPlugin.LogInfo($"[Session] Player {EntityExtensions.FormatPlayer(steamId, playerEntity)} exited zone {previousZoneHash} but was not entered — skipping penalty.");
            return;
        }

        // CRITICAL: Skip penalty if player just died (respawn teleports them out of zone)
        if (_recentlyDied.ContainsKey(steamId))
        {
            BattleLuckPlugin.LogInfo($"[Session] Player {EntityExtensions.FormatPlayer(steamId, playerEntity)} zone-exit suppressed — recently died, not a walk-out.");
            return;
        }

        // Registration happens before arena construction completes. Boundary
        // enforcement becomes authoritative only after the match starts and
        // this player has actually been teleported into the arena.
        if (!_activeSessions.TryGetValue(previousZoneHash, out var activeSession) ||
            !activeSession.IsStarted ||
            !_arenaTeleportedPlayers.Contains(steamId))
        {
            BattleLuckPlugin.LogInfo($"[Session] Ignored pre-start zone exit for {EntityExtensions.FormatPlayer(steamId, playerEntity)} in {previousZoneHash}; .toggleleave boundary rule is not active yet.");
            return;
        }

        _penaltyBurning.Remove(steamId);

        var config = LoadEffectiveConfig(modeId);
        var zone = config.Zones.Zones.FirstOrDefault(z => z.Hash == previousZoneHash);
        if (zone != null && playerEntity.Exists())
        {
            playerEntity.SetPosition(ZoneCenter(zone));
            _zoneDetection.SetPlayerZone(steamId, previousZoneHash);
            BattleLuckPlugin.LogInfo($"[Session] Player {EntityExtensions.FormatPlayer(steamId, playerEntity)} returned inside zone {previousZoneHash}; .toggleleave is required to exit.");

            if (FlowController.TryGetUser(playerEntity, out var boundaryUser))
                NotificationHelper.NotifyPlayer(boundaryUser, "You cannot leave this event boundary directly. Use .toggleleave for a clean exit.");
            return;
        }

        BattleLuckPlugin.LogInfo($"[Session] Player {EntityExtensions.FormatPlayer(steamId, playerEntity)} left zone {previousZoneHash} without .toggleleave — penalty skipped for reconnect safety.");

        if (FlowController.TryGetUser(playerEntity, out var user))
            NotificationHelper.NotifyPlayer(user, "You left the event zone. Penalty damage is disabled; use .toggleleave when possible for a clean restore.");
    }

    // ── Tick ────────────────────────────────────────────────────────────

    public void Tick(IEnumerable<Entity> onlinePlayers, float deltaSeconds)
    {
        var onlineList = onlinePlayers as IReadOnlyList<Entity> ?? onlinePlayers.ToList();

        _zoneDetection.Tick(onlineList);
        _eventRuntime.Tick(deltaSeconds);
        ProcessPendingArenaRespawns(onlineList);
        ReconcileOfflinePlayers(onlineList);

        // Process pending buff re-applications (RemoveAndAddBuff queue)
        EntityExtensions.TickPendingBuffs();

        // Penalty burning is disabled for reconnect safety. Keep clearing
        // stale entries that may have been serialized in older sessions.
        _penaltyBurning.Clear();

        // Decrement suppress-ticks for recently-died players; remove when counter reaches zero.
        if (_recentlyDied.Count > 0)
        {
            var expired = new List<ulong>();
            var decrement = new List<ulong>();
            foreach (var kv in _recentlyDied)
            {
                if (kv.Value <= 1) expired.Add(kv.Key);
                else decrement.Add(kv.Key);
            }
            foreach (var id in expired) _recentlyDied.Remove(id);
            foreach (var id in decrement) _recentlyDied[id]--;
        }

        foreach (var kv in _activeSessions)
        {
            var session = kv.Value;
            if (session.Mode == null || session.Context == null) continue;

            // Tick arena tile spawning
            if (session.ArenaSpawning && session.Border != null)
                session.ArenaSpawning = session.Border.TickSpawnAll();

            if (!session.IsStarted)
            {
                TickPendingStart(session, kv.Key, onlineList);
                continue;
            }

            if (!session.IsPaused)
                session.Mode.OnTick(session.Context, deltaSeconds);

            TickDeliveryObjectives(session, onlineList);
            TickManualShrinkBoundary(session, kv.Key, onlineList, deltaSeconds);

            // Tick DOT boundary enforcement
            if (session.BorderDot != null && session.Mode is GameModeEngine bbMode && bbMode.Shrink.IsActive)
            {
                var zone = session.Config?.Zones?.Zones?.FirstOrDefault(z => z.Hash == kv.Key);
                var dotCfg = zone?.Boundary?.Dot;
                if (zone != null && dotCfg != null)
                {
                    var zoneEntities = onlineList.Where(e => session.Context.Players.Contains(e.GetSteamId()));
                    session.BorderDot.Tick(session.Context, zoneEntities,
                        bbMode.Shrink.GetCurrentCenter(), bbMode.Shrink.CurrentRadius,
                        zone.ExitRadius, dotCfg);
                }
            }

            // Auto-end session when mode signals completion
            if (session.Context.State.ContainsKey("result"))
                _pendingEnd.Add(kv.Key);
        }

        // Auto-trash dropped items in active zones
        _autoTrash.Tick();

        foreach (var zoneHash in _pendingEnd)
            EndSession(zoneHash);
        _pendingEnd.Clear();
    }

    void ReconcileOfflinePlayers(IReadOnlyList<Entity> onlinePlayers)
    {
        if (_activeSessions.Count == 0 || _enteredPlayers.Count == 0)
        {
            _offlineTicks.Clear();
            return;
        }

        var onlineIds = onlinePlayers
            .Where(e => e.Exists() && e.IsPlayer() && FlowController.TryGetUser(e, out var user) && user.IsConnected)
            .Select(e => e.GetSteamId())
            .Where(id => id != 0)
            .ToHashSet();

        foreach (var steamId in onlineIds)
            _offlineTicks.Remove(steamId);

        var removals = new List<(ulong SteamId, ActiveSession Session)>();
        foreach (var session in _activeSessions.Values)
        {
            if (session.Context == null)
                continue;

            if (session.Config?.Zones?.AutoEnter?.ExitOnDisconnect == false)
                continue;

            foreach (var steamId in session.Context.Players.ToList())
            {
                if (onlineIds.Contains(steamId))
                    continue;

                var ticks = _offlineTicks.GetValueOrDefault(steamId, 0) + 1;
                _offlineTicks[steamId] = ticks;
                if (ticks >= 3)
                    removals.Add((steamId, session));
            }
        }

        foreach (var (steamId, session) in removals)
            CleanupDisconnectedPlayerState(steamId, session);
    }

    void CleanupDisconnectedPlayerState(ulong steamId, ActiveSession session)
    {
        if (session.Context == null || !session.Context.Players.Contains(steamId))
            return;

        _offlineTicks.Remove(steamId);
        _enteredPlayers.Remove(steamId);
        _arenaTeleportedPlayers.Remove(steamId);
        _pendingArenaRespawns.Remove(steamId);
        _playerZoneMap.Remove(steamId);
        _readyPlayers.Remove(steamId);
        _penaltyBurning.Remove(steamId);
        _recentlyDied.Remove(steamId);
        _zoneDetection.RemovePlayer(steamId);
        session.Context.Players.Remove(steamId);
        session.Context.Teams.Remove(steamId);

        session.Mode?.OnPlayerLeave(session.Context, steamId);
        FloorLockService.UnlockPlayer(steamId);
        BattleLuckPlugin.EquipmentTracker?.StopTrackingPlayer(steamId);

        BattleLuckPlugin.LogInfo($"[Session] Player {steamId} disconnected inside {session.Context.ModeId}/{session.Context.ZoneHash}; removed from event state and kept rollback snapshot for reconnect.");

        if (session.Context.Players.Count == 0)
            _pendingEnd.Add(session.Context.ZoneHash);
    }

    void TickManualShrinkBoundary(ActiveSession session, int zoneHash, IReadOnlyList<Entity> onlinePlayers, float deltaSeconds)
    {
        if (session.BorderDot == null ||
            session.Context == null ||
            !session.Context.State.TryGetValue("manualShrink", out var value) ||
            value is not ManualShrinkState shrink ||
            !shrink.Enabled)
        {
            return;
        }

        var zone = session.Config.Zones.Zones.FirstOrDefault(z => z.Hash == zoneHash);
        if (zone == null)
            return;

        if (shrink.CurrentRadius <= 0f)
            shrink.CurrentRadius = zone.Radius > 0f ? zone.Radius : 30f;
        if (shrink.TargetRadius <= 0f)
            shrink.TargetRadius = Math.Max(5f, shrink.CurrentRadius * 0.5f);
        if (shrink.ShrinkRatePerSecond > 0f && shrink.CurrentRadius > shrink.TargetRadius)
            shrink.CurrentRadius = Math.Max(shrink.TargetRadius, shrink.CurrentRadius - shrink.ShrinkRatePerSecond * deltaSeconds);

        var now = DateTime.UtcNow;
        var bucket = (int)Math.Floor(shrink.CurrentRadius / 5f) * 5;
        if (now >= shrink.NextBroadcastUtc && bucket != shrink.LastRadiusBucket)
        {
            shrink.LastRadiusBucket = bucket;
            shrink.NextBroadcastUtc = now.AddSeconds(10);
            session.Context.Broadcast?.Invoke(NotificationHelper.ColorizeText($"Damage zone closing: radius {shrink.CurrentRadius:F0}m.", "#FFD166"));
        }

        session.Context.State["boundaryDamageOnly"] = shrink.DamageOnly;
        var players = onlinePlayers.Where(e => e.Exists() && e.IsPlayer() && session.Context.Players.Contains(e.GetSteamId()));
        var dot = zone.Boundary?.Dot ?? new DotBoundaryConfig
        {
            Enabled = true,
            WarningRadiusPercent = 0.80f,
            DangerRadiusPercent = 0.95f,
            TeleportOnExit = false
        };

        session.BorderDot.Tick(
            session.Context,
            players,
            shrink.Center,
            shrink.CurrentRadius,
            shrink.CurrentRadius + Math.Max(2f, shrink.ExitBuffer),
            dot);
    }

    void TickDeliveryObjectives(ActiveSession session, IReadOnlyList<Entity> onlinePlayers)
    {
        if (session.Context == null ||
            !session.Context.State.TryGetValue("deliveryObjectives", out var value) ||
            value is not Dictionary<string, DeliveryObjectiveState> objectives)
        {
            return;
        }

        foreach (var objective in objectives.Values.Where(o => o.Enabled).ToList())
        {
            foreach (var player in onlinePlayers.Where(e => e.Exists() && e.IsPlayer() && session.Context.Players.Contains(e.GetSteamId())))
            {
                var steamId = player.GetSteamId();
                if (!objective.Repeatable && objective.CompletedSteamIds.Contains(steamId))
                    continue;

                var dist = math.distance(player.GetPosition().xz, objective.Position.xz);
                if (dist > objective.Radius)
                    continue;

                if (objective.ItemGuidHash.HasValue)
                {
                    var item = new PrefabGUID(objective.ItemGuidHash.Value);
                    if (CountInventoryItem(player, item) < objective.Amount)
                        continue;
                    if (!player.TryRemoveItem(item, objective.Amount))
                        continue;
                }

                if (objective.TeamId.HasValue)
                    session.Context.Scores.AddTeamScore(objective.TeamId.Value, objective.RewardPoints);
                else
                    session.Context.Scores.AddPlayerScore(steamId, objective.RewardPoints);

                objective.CompletedSteamIds.Add(steamId);
                if (!objective.Repeatable)
                    objective.Enabled = false;

                GameEvents.OnObjectiveCaptured?.Invoke(new ObjectiveCapturedEvent
                {
                    SessionId = session.Context.SessionId,
                    ObjectiveId = objective.ObjectiveId,
                    TeamId = objective.TeamId ?? 0
                });

                session.Context.Broadcast?.Invoke(NotificationHelper.ColorizeText(string.IsNullOrWhiteSpace(objective.Message)
                    ? $"Objective delivered: {objective.ObjectiveId} (+{objective.RewardPoints})."
                    : objective.Message, "#47FF8A"));
                break;
            }
        }
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

    // ── Death handler ───────────────────────────────────────────────────

    void HandleDeath(Entity died, Entity killer)
    {
        if (!died.IsPlayer())
        {
            if (killer.IsPlayer())
                RouteEnemyKill(died, killer);
            return;
        }

        ulong victimId = died.GetSteamId();

        BattleLuckPlugin.LogInfo($"[Session] HandleDeath: victim={EntityExtensions.FormatPlayer(victimId, died)}, killer={killer.Index}, isPlayer={killer.IsPlayer()}");

        // Suppress zone-exit detection for 3 ticks to let the respawn teleport settle
        _recentlyDied[victimId] = 3;

        // PENALTY DEATH: player was under HP drain from unauthorized zone exit
        if (_penaltyBurning.Contains(victimId))
        {
            HandlePenaltyDeath(victimId, died);
            return;
        }

        // Route to session death handling
        foreach (var kv in _activeSessions)
        {
            var session = kv.Value;
            if (session.Mode == null || session.Context == null) continue;

            if (!session.Context.Players.Contains(victimId)) continue;

            // Boundary DOT death
            if ((!killer.IsPlayer() || killer == died) &&
                session.BorderDot != null && session.BorderDot.HandleBoundaryDeath(died, victimId))
            {
                BattleLuckPlugin.LogInfo($"[Session] Player {EntityExtensions.FormatPlayer(victimId, died)} died from boundary DOT — returned to safe position.");
                return;
            }

            // PvP kill
            if (killer.IsPlayer() && killer != died)
            {
                ulong killerId = killer.GetSteamId();
                HandleSessionParticipantDeath(session, victimId, died, killerId, killer);
                return;
            }

            // PvE / self death
            HandleSessionParticipantDeath(session, victimId, died, 0, Entity.Null);
            return;
        }
    }

    void HandleSessionParticipantDeath(ActiveSession session, ulong victimId, Entity victimEntity, ulong killerId, Entity killerEntity)
    {
        var em = VRisingCore.EntityManager;
        if (!em.HasComponent<EventParticipant>(victimEntity))
        {
            // Fallback for legacy modes or failed component initialization
            session.Mode.OnPlayerDowned(session.Context, victimId, killerId);
            if (!session.Context.Players.Contains(victimId))
            {
                ForceExitEliminatedPlayer(session, victimId, victimEntity);
                return;
            }
            QueueImmediateArenaRespawn(session, victimId, victimEntity);
            return;
        }

        var participant = em.GetComponentData<EventParticipant>(victimEntity);
        participant.DeathCount += 1;
        em.SetComponentData(victimEntity, participant);

        BattleLuckPlugin.LogInfo($"[Session] death: player={victimId} session={participant.SessionId} deathCount={participant.DeathCount}");

        // Increment team kill counters for killer's team
        if (killerEntity != Entity.Null && em.HasComponent<EventParticipant>(killerEntity))
        {
            var killerPart = em.GetComponentData<EventParticipant>(killerEntity);
            IncrementTeamKill(killerPart.TeamIndex, participant.SessionEntity);
        }

        session.Mode.OnPlayerDowned(session.Context, victimId, killerId);

        var rules = session.Config.Rules;
        var maxDeaths = rules?.MaxDeathsPerParticipant ?? 3;

        if (participant.DeathCount < maxDeaths)
        {
            // Queue immediate respawn inside arena via intent
            QueueImmediateArenaRespawn(victimEntity);
        }
        else
        {
            // Mark eliminated and schedule rollback
            participant.Eliminated = true;
            em.SetComponentData(victimEntity, participant);

            // Remove event kit safely via intent
            EnqueueIntentForPlayer(victimEntity, participant.SessionEntity, "act:snapshot.restore");

            // Teleport out via rollback intent
            EnqueueIntentForPlayer(victimEntity, participant.SessionEntity, "act:event.rollback.teleport_out");

            BattleLuckPlugin.LogInfo($"[Session] player.eliminated: player={victimId} session={participant.SessionId} deathCount={participant.DeathCount}");
        }
    }

    void IncrementTeamKill(int teamIndex, Entity sessionEntity)
    {
        // Team kill tracking is now handled via GameModeContext state
        // using the managed player session registry.
        // Find the session by entity and update its context state.
        var session = _activeSessions.Values.FirstOrDefault(s => s.SessionEntity == sessionEntity);
        if (session?.Context == null)
            return;

        var teamKillsKey = $"teamKills_{teamIndex}";
        var currentKills = session.Context.State.TryGetValue(teamKillsKey, out var val) && val is int kills ? kills : 0;
        currentKills++;
        session.Context.State[teamKillsKey] = currentKills;
    }

    void EnqueueIntentForPlayer(Entity player, Entity sessionEntity, string actionId, string prefabGuid = "")
    {
        var em = VRisingCore.EntityManager;
        var intent = em.CreateEntity(ComponentType.ReadOnly<ActionIntent>());
        em.SetComponentData(intent, new ActionIntent
        {
            ActionId = actionId,
            IntentId = Guid.NewGuid().ToString(),
            CreatedAt = (double)DateTime.UtcNow.Ticks,
            PrefabGuid = prefabGuid,
            Position = Unity.Mathematics.float3.zero,
            Rotation = 0f,
            Caller = player,
            EventEntity = sessionEntity
        });
    }

    public void QueueImmediateArenaRespawn(Entity player)
    {
        // Create an intent entity via ECB so systems handle the actual respawn
        var em = VRisingCore.EntityManager;
        var ecb = new EntityCommandBuffer(Unity.Collections.Allocator.Temp);
        var intent = ecb.CreateEntity();
        var intentComp = new ActionIntent
        {
            ActionId = "act:event.respawn_in_arena",
            IntentId = Guid.NewGuid().ToString(),
            CreatedAt = (double)DateTime.UtcNow.Ticks,
            PrefabGuid = "",
            Position = Unity.Mathematics.float3.zero,
            Rotation = 0f,
            Caller = player,
            EventEntity = GetPlayerSessionEntity(player)
        };
        ecb.AddComponent(intent, intentComp);
        ecb.Playback(em);
        ecb.Dispose();
    }

    Entity GetPlayerSessionEntity(Entity player)
    {
        var em = VRisingCore.EntityManager;
        if (em.HasComponent<EventParticipant>(player))
            return em.GetComponentData<EventParticipant>(player).SessionEntity;
        return Entity.Null;
    }

    void EnqueueIntent(Entity sessionEntity, Entity caller, string actionId, Entity target, string prefabGuid = "")
    {
        var em = VRisingCore.EntityManager;
        var intent = em.CreateEntity(ComponentType.ReadOnly<ActionIntent>());
        em.SetComponentData(intent, new ActionIntent
        {
            ActionId = actionId,
            IntentId = Guid.NewGuid().ToString(),
            CreatedAt = (double)DateTime.UtcNow.Ticks,
            PrefabGuid = prefabGuid,
            Position = Unity.Mathematics.float3.zero,
            Rotation = 0f,
            Caller = caller,
            EventEntity = sessionEntity
        });
        // If target is needed, we might need a more complex ActionIntent or separate component
    }

    void RouteEnemyKill(Entity died, Entity killer)
    {
        ulong killerId = killer.GetSteamId();
        foreach (var kv in _activeSessions)
        {
            var session = kv.Value;
            if (session.Context == null || !session.Context.Players.Contains(killerId))
                continue;

            if (session.Spawner.RecordKill(died, out var trackedNpc) && session.Spawner.TryRespawn(trackedNpc))
            {
                session.Context.Broadcast?.Invoke(NotificationHelper.ColorizeText(
                    $"NPC respawning. {trackedNpc.LivesRemaining - 1} life/lives remaining.",
                    "#FFD166"));
                return;
            }
            var waveCleared = session.Mode?.RecordEnemyKill(session.Context, killerId) == true;
            if (waveCleared)
                BattleLuckPlugin.LogInfo($"[Session] Enemy kill by {killerId} cleared a wave/objective in {session.Context.ModeId}.");
            return;
        }
    }

    public bool QueueImmediateArenaRespawn(ActiveSession session, ulong steamId, Entity player)
    {
        var zone = session.Config.Zones.Zones.FirstOrDefault(z => z.Hash == session.Context.ZoneHash);
        if (zone == null || !player.Exists())
            return false;

        var spawn = ResolveArenaSpawn(session, steamId, zone);
        try
        {
            if (PrefabHelper.TryGetValidPrefabGuidDeep("Buff_General_Vampire_Wounded_Buff", out var woundedBuff))
                player.TryRemoveBuff(woundedBuff);

            if (player.Has<Health>())
            {
                var health = player.Read<Health>();
                if (!health.IsDead)
                {
                    health.Value = health.MaxHealth._Value;
                    health.MaxRecoveryHealth = health.MaxHealth;
                    player.Write(health);
                    FinalizeArenaRespawn(session, steamId, player, spawn);
                    return true;
                }
            }

            var userEntity = player.GetUserEntity();
            if (!userEntity.Exists())
                return false;

            var bootstrap = VRisingCore.Server.GetExistingSystemManaged<ServerBootstrapSystem>();
            var ecbSystem = VRisingCore.Server.GetExistingSystemManaged<EntityCommandBufferSystem>();
            if (bootstrap == null || ecbSystem == null)
                return false;

            var commandBuffer = ecbSystem.CreateCommandBuffer();
            var spawnLocation = new Il2CppSystem.Nullable_Unboxed<float3> { value = spawn };
            bootstrap.RespawnCharacter(
                commandBuffer,
                userEntity,
                customSpawnLocation: spawnLocation,
                previousCharacter: player);

            _pendingArenaRespawns[steamId] = new PendingArenaRespawn(session.Context.ZoneHash, 90);
            BattleLuckPlugin.LogInfo($"[Session] Native immediate arena respawn queued for {steamId} in {session.Context.ModeId}.");
            return true;
        }
        catch (Exception ex)
        {
            BattleLuckPlugin.LogWarning($"[Session] Immediate arena respawn failed for {steamId}: {ex.Message}");
            return false;
        }
    }

    void ProcessPendingArenaRespawns(IReadOnlyList<Entity> onlinePlayers)
    {
        if (_pendingArenaRespawns.Count == 0)
            return;

        foreach (var (steamId, pending) in _pendingArenaRespawns.ToList())
        {
            if (!_activeSessions.TryGetValue(pending.ZoneHash, out var session) ||
                session.Context == null ||
                !session.Context.Players.Contains(steamId))
            {
                _pendingArenaRespawns.Remove(steamId);
                continue;
            }

            var player = onlinePlayers.FirstOrDefault(entity =>
                entity.Exists() && entity.IsPlayer() && entity.GetSteamId() == steamId);
            if (player.Exists() && player.Has<Health>() && !player.Read<Health>().IsDead)
            {
                var zone = session.Config.Zones.Zones.FirstOrDefault(z => z.Hash == pending.ZoneHash);
                if (zone != null)
                    FinalizeArenaRespawn(session, steamId, player, ResolveArenaSpawn(session, steamId, zone));
                _pendingArenaRespawns.Remove(steamId);
                continue;
            }

            if (pending.AttemptsRemaining <= 1)
            {
                BattleLuckPlugin.LogWarning($"[Session] Native arena respawn timed out for {steamId}; player remains in the session for normal game respawn recovery.");
                _pendingArenaRespawns.Remove(steamId);
            }
            else
            {
                _pendingArenaRespawns[steamId] = pending with { AttemptsRemaining = pending.AttemptsRemaining - 1 };
            }
        }
    }

    void FinalizeArenaRespawn(ActiveSession session, ulong steamId, Entity player, float3 spawn)
    {
        player.SetPosition(spawn);
        player.HealToFull();
        _arenaTeleportedPlayers.Add(steamId);
        _zoneDetection.SetPlayerZone(steamId, session.Context.ZoneHash);

        var kitId = session.Config.Zones.Zones.FirstOrDefault(z => z.Hash == session.Context.ZoneHash)?.KitId;
        if (string.IsNullOrWhiteSpace(kitId))
            kitId = session.Config.KitId;
        var kit = KitController.LoadKit(kitId ?? session.Context.ModeId);
        AbilityController.ClearAbilitySlots(player);
        AbilityController.ClearPassiveSpells(player);
        if (kit?.Abilities != null)
            AbilityController.EquipAbilities(player, kit.Abilities);
        if (kit?.PassiveSpells.Count > 0)
            AbilityController.EquipPassiveSpells(player, kit.PassiveSpells);

        ApplyZoneProtectionBuffs(session, player);
        BattleLuckPlugin.LogInfo($"[Session] Immediate arena respawn completed for {steamId} in {session.Context.ModeId}.");
    }

    static float3 ResolveArenaSpawn(ActiveSession session, ulong steamId, ZoneDefinition zone)
    {
        var spawn = zone.TeleportSpawn.ToFloat3();
        if (!session.Context.Teams.TryGetValue(steamId, out var teamId))
            return spawn;

        var offset = Math.Max(8f, zone.Radius * 0.55f);
        return spawn + (teamId % 2 == 0
            ? new float3(offset, 0f, offset)
            : new float3(-offset, 0f, -offset));
    }

    /// <summary>
    /// Handle death from burning penalty: restore old kit, teleport to penalty spawn.
    /// </summary>
    void HandlePenaltyDeath(ulong steamId, Entity playerEntity)
    {
        _penaltyBurning.Remove(steamId);

        BattleLuckPlugin.LogInfo($"[Session] HandlePenaltyDeath: {EntityExtensions.FormatPlayer(steamId, playerEntity)} — restoring snapshot.");

        // Restore original snapshot (saved on enter)
        if (_playerZoneMap.TryGetValue(steamId, out var zoneHash))
        {
            _playerState.RestoreSnapshot(playerEntity, zoneHash);
        }

        // Clean up from active session
        if (_playerZoneMap.TryGetValue(steamId, out var zh) && _activeSessions.TryGetValue(zh, out var session))
        {
            CleanupPlayerState(steamId, session);
        }
        else
        {
            _enteredPlayers.Remove(steamId);
            _arenaTeleportedPlayers.Remove(steamId);
            _pendingArenaRespawns.Remove(steamId);
            _playerZoneMap.Remove(steamId);
        }

        // Teleport to penalty spawn
        playerEntity.SetPosition(PenaltySpawn);

        // Heal to full after restore
        playerEntity.HealToFull();

        BattleLuckPlugin.LogInfo($"[Session] Player {EntityExtensions.FormatPlayer(steamId, playerEntity)} died from penalty burn — kit restored, teleported to {PenaltySpawn}.");
    }

    // ── Arena tile spawning ────────────────────────────────────────────

    void SpawnArenaTiles(ActiveSession session, ZoneDefinition zone, string modeId, ModeConfig config)
    {
        if (!EventGeometryMutationsEnabled)
        {
            session.Context.State.Remove("arenaSpawningRequested");
            session.Context.State.Remove("arenaFloorFillQueued");
            session.ArenaSpawning = false;
            BattleLuckPlugin.LogWarning($"[Session] Strict stability profile: no walls, floors, platforms, schematics, or native arena geometry will spawn for {modeId}.");
            return;
        }

        try
        {
            float radius = zone.Radius > 0 ? zone.Radius : 50f;
            var center = ZoneCenter(zone);

            if (session.Border != null && zone.Boundary?.Walls != null && !UnifiedOwnsArenaGeometry(session))
            {
                var wallConfig = zone.Boundary.Walls;

                session.Border.StartZoneBoundary(modeId, center, radius, wallConfig);
                session.ArenaSpawning = true;
                BattleLuckPlugin.LogInfo($"[Session] Arena tiles queued for {modeId} zone {zone.Hash}");
            }

            var floorAlreadyQueued = session.Context.State.ContainsKey("arenaFloorFillQueued") || UnifiedOwnsFloorGeometry(session);
            if (session.Platform != null && !floorAlreadyQueued)
            {
                var platformCfg = zone.MovingPlatform;
                if (platformCfg != null)
                {
                    session.Platform.Configure(platformCfg);
                    session.Platform.SpawnPlatform(center);
                }
            }
            else if (floorAlreadyQueued)
            {
                BattleLuckPlugin.LogInfo($"[Session] Center platform skipped for {modeId} zone {zone.Hash}: arena floor fill already owns those floor blocks.");
            }
        }
        catch (Exception ex)
        {
            BattleLuckPlugin.LogError($"[Session] Arena tile spawn failed for {modeId}: {ex.Message}");
        }
    }

    void EnsureArenaPreparationScheduled(ActiveSession session, string modeId)
    {
        if (session.ArenaReadyUtc != default)
            return;

        session.ArenaPreparationStartedUtc = DateTime.UtcNow;
        session.ArenaReadyUtc = session.ArenaPreparationStartedUtc.AddSeconds(ArenaPreparationSeconds);
        session.Context.State["arenaPreparationStartedUtc"] = session.ArenaPreparationStartedUtc;
        session.Context.State["arenaReadyUtc"] = session.ArenaReadyUtc;
        session.Context.Broadcast?.Invoke(NotificationHelper.ColorizeText($"{modeId} arena is preparing. Match auto-starts after build checks and a 2-minute ready window.", "#5CC8FF"));
        BattleLuckPlugin.LogInfo($"[Session] Arena preparation scheduled for {modeId}: ready at {session.ArenaReadyUtc:O}.");
    }

    void TickPendingStart(ActiveSession session, int zoneHash, IReadOnlyList<Entity> onlinePlayers)
    {
        if (session.Context == null || session.Mode == null)
            return;

        var rules = session.Config.Session.Rules;
        if (session.Context.Players.Count < rules.MinPlayers)
            return;

        EnsureArenaPreparationScheduled(session, session.Context.ModeId);
        var now = DateTime.UtcNow;

        if (session.ArenaSpawning || session.Border?.HasPendingBuildWork == true)
        {
            LogReadinessWaiting(session, "arena build queue still running");
            return;
        }

        if (!session.StartForced && session.ArenaReadyUtc != default && now < session.ArenaReadyUtc)
        {
            var remaining = Math.Max(0, (int)Math.Ceiling((session.ArenaReadyUtc - now).TotalSeconds));
            BroadcastPreparationCountdown(session, remaining);
            LogReadinessWaiting(session, $"arena preparation window {remaining}s remaining");
            return;
        }

        var zone = session.Config.Zones.Zones.FirstOrDefault(z => z.Hash == zoneHash);
        if (zone == null)
            return;

        if (!session.StartWarmupActive)
        {
            BeginStartWarmup(session, zone, onlinePlayers);
            return;
        }

        foreach (var player in onlinePlayers.Where(e => e.Exists() && e.IsPlayer() && session.Context.Players.Contains(e.GetSteamId())))
            PreparePlayerForStartWarmup(session, player, zone);

        BroadcastWarmupCountdown(session, now);

        if (now >= session.StartWarmupEndsUtc)
            StartModeSession(session, zone);
    }

    void BeginStartWarmup(ActiveSession session, ZoneDefinition zone, IReadOnlyList<Entity> onlinePlayers)
    {
        session.StartWarmupActive = true;
        session.StartWarmupEndsUtc = DateTime.UtcNow.AddSeconds(MatchStartStunSeconds);
        session.Context.State["startWarmupEndsUtc"] = session.StartWarmupEndsUtc;

        if (session.Config.Session.Rules.EnablePvP &&
            !session.Context.ModeId.Equals("bloodbath", StringComparison.OrdinalIgnoreCase) &&
            !session.Context.State.ContainsKey("teamsAssigned"))
        {
            var teams = TeamBalancer.AssignTeams(session.Context);
            if (teams.Success)
                session.Context.State["teamsAssigned"] = true;
            else
                BattleLuckPlugin.LogWarning($"[Session] Team assignment failed for {session.Context.ModeId}: {teams.Error}");
        }

        foreach (var player in onlinePlayers.Where(e => e.Exists() && e.IsPlayer() && session.Context.Players.Contains(e.GetSteamId())))
            PreparePlayerForStartWarmup(session, player, zone);

        session.LastWarmupCountdownSecond = MatchStartStunSeconds;
        session.Context.Broadcast?.Invoke(NotificationHelper.ColorizeText($"Arena ready. Players teleported and locked. Match starts in {MatchStartStunSeconds}s.", "#47FF8A"));
        BattleLuckPlugin.LogInfo($"[Session] Start warmup begun for {session.Context.ModeId}/{session.Context.ZoneHash}; players={session.Context.Players.Count}.");
    }

    void BroadcastPreparationCountdown(ActiveSession session, int remaining)
    {
        var bucket = remaining <= 10 ? 10
            : remaining <= 30 ? 30
            : remaining <= 60 ? 60
            : 0;

        if (bucket <= 0 || session.LastPreparationCountdownBucket == bucket)
            return;

        session.LastPreparationCountdownBucket = bucket;
        session.Context.Broadcast?.Invoke(NotificationHelper.ColorizeText($"Arena preparing: match warmup begins in {remaining}s.", "#5CC8FF"));
    }

    void BroadcastWarmupCountdown(ActiveSession session, DateTime now)
    {
        var remaining = Math.Max(0, (int)Math.Ceiling((session.StartWarmupEndsUtc - now).TotalSeconds));
        if (remaining <= 0 || remaining > MatchStartStunSeconds)
            return;

        var shouldBroadcast = remaining <= 5 || remaining == 10;
        if (!shouldBroadcast || session.LastWarmupCountdownSecond == remaining)
            return;

        session.LastWarmupCountdownSecond = remaining;
        session.Context.Broadcast?.Invoke(NotificationHelper.ColorizeText($"Match starts in {remaining}s.", remaining <= 5 ? "#FFD166" : "#47FF8A"));
    }

    void PreparePlayerForStartWarmup(ActiveSession session, Entity player, ZoneDefinition zone)
    {
        var steamId = player.GetSteamId();
        var spawn = ResolveArenaSpawn(session, steamId, zone);
        player.SetPosition(spawn);
        _arenaTeleportedPlayers.Add(steamId);
        _zoneDetection.SetPlayerZone(steamId, session.Context.ZoneHash);
        ApplyZoneProtectionBuffs(session, player);

        var result = ExecuteSessionAction(session, player, $"player.buff.apply:buffPrefab=Buff_General_Stun|duration={MatchStartStunSeconds}");
        if (!result.Success && Prefabs.Buff_General_Stun != Stunlock.Core.PrefabGUID.Empty)
            player.BuffEntity(Prefabs.Buff_General_Stun, out _, MatchStartStunSeconds);
    }

    public void ForceExitEliminatedPlayer(ActiveSession session, ulong steamId, Entity player)
    {
        var zoneHash = session.Context.ZoneHash;
        RemoveZoneProtectionBuffs(player);
        if (!_playerState.RestoreSnapshot(player, zoneHash))
        {
            player.SetPosition(PenaltySpawn);
            player.HealToFull();
        }

        _enteredPlayers.Remove(steamId);
        _arenaTeleportedPlayers.Remove(steamId);
        _pendingArenaRespawns.Remove(steamId);
        _readyPlayers.Remove(steamId);
        _penaltyBurning.Remove(steamId);
        _playerZoneMap.Remove(steamId);
        session.Context.Teams.Remove(steamId);
        FloorLockService.UnlockPlayer(steamId);
        BattleLuckPlugin.EquipmentTracker?.StopTrackingPlayer(steamId);
        session.Context.Broadcast?.Invoke(NotificationHelper.ColorizeText(
            $"{ResolvePlayerName(steamId)} reached the death limit and was returned to the saved position.",
            "#FF6B6B"));
    }

    static string ResolvePlayerName(ulong steamId)
    {
        var player = VRisingCore.GetOnlinePlayers().FirstOrDefault(e => e.Exists() && e.IsPlayer() && e.GetSteamId() == steamId);
        return player.Exists() ? EntityExtensions.FormatPlayer(steamId, player) : steamId.ToString();
    }

    OperationResult ExecuteSessionAction(ActiveSession session, Entity player, string action)
    {
        var zone = session.Config?.Zones?.Zones?.FirstOrDefault(z => z.Hash == session.Context.ZoneHash);
        var context = new FlowActionContext
        {
            PlayerCharacter = player,
            ZoneHash = session.Context.ZoneHash,
            PlayerState = _playerState,
            Registry = _registry,
            Config = session.Config,
            Zone = zone,
            GameContext = session.Context
        };

        return _actionExecutor.Execute(action, context);
    }

    void ApplyZoneProtectionBuffs(ActiveSession session, Entity player)
    {
        if (Prefabs.Buff_General_Ignite != Stunlock.Core.PrefabGUID.Empty)
            player.TryRemoveBuff(Prefabs.Buff_General_Ignite);

        var holy = ExecuteSessionAction(session, player, "player.buff.apply:buffPrefab=Buff_General_HolyAreaProtection|duration=-1");
        if (!holy.Success)
            ExecuteSessionAction(session, player, "player.buff.apply:buffPrefab=Buff_General_Holy_T01|duration=-1");

        var garlic = ExecuteSessionAction(session, player, "player.buff.apply:buffPrefab=Buff_General_GarlicAreaProtection|duration=-1");
        if (!garlic.Success)
            ExecuteSessionAction(session, player, "player.buff.apply:buffPrefab=Buff_General_Garlic_Area|duration=-1");

        ExecuteSessionAction(session, player, "player.buff.apply:buffPrefab=Buff_InCombat|duration=-1");
    }

    void ApplyBoundaryTransitionSlow(ActiveSession session, Entity player, float durationSeconds)
    {
        ExecuteSessionAction(session, player, $"player.buff.apply:buffPrefab=Buff_General_Slow|duration={durationSeconds:0.###}");
    }

    static void RemoveZoneProtectionBuffs(Entity player)
    {
        foreach (var name in new[]
        {
            "Buff_General_HolyAreaProtection",
            "Buff_General_Holy_T01",
            "Buff_General_GarlicAreaProtection",
            "Buff_General_Garlic_Area",
            "Buff_InCombat",
            "Buff_General_Slow",
            "Buff_General_Stun"
        })
        {
            try
            {
                if (PrefabHelper.TryGetValidPrefabGuidDeep(name, out var guid))
                    player.TryRemoveBuff(guid);
            }
            catch { }
        }
    }

    void StartModeSession(ActiveSession session, ZoneDefinition zone)
    {
        if (session.IsStarted)
            return;

        var rules = session.Config.Session.Rules;
        var staging = ResolveActionStagingRules(session.Config);
        session.StartWarmupActive = false;
        session.IsStarted = true;
        if (session.StartForced ||
            (rules.AllowAdminSoloTest && session.Context.Players.Count <= Math.Max(1, rules.AdminTestMinPlayers)))
        {
            session.Context.State["testMode"] = true;
        }
        session.Context.TimeLimitSeconds = rules.MatchDurationMinutes > 0
            ? rules.MatchDurationMinutes * 60
            : DefaultModeDurationSeconds;
        session.Context.StartTimeUtc = DateTime.UtcNow;

        session.Context.TechState = new SessionTechState();
        session.Context.State["techMutationsDisabled"] = true;

        if (staging.Enabled && staging.StageOnZoneEnter && staging.ReleaseOnMatchStart)
            ReleaseStagedEnterActions(session, zone);

        foreach (var player in VRisingCore.GetOnlinePlayers().Where(e => e.Exists() && e.IsPlayer() && session.Context.Players.Contains(e.GetSteamId())))
            _flow.ExecuteStart(session.Config, player, zone, session.Context);

        session.Mode?.OnStart(session.Context);
        _eventRuntime.MarkActive(session.Context.SessionId);

        BattleLuckPlugin.LogInfo($"[Session] Mode {session.Context.ModeId} started with {session.Context.Players.Count} players after arena readiness.");
    }

    void LogReadinessWaiting(ActiveSession session, string reason)
    {
        var now = DateTime.UtcNow;
        if ((now - session.LastReadinessLogUtc).TotalSeconds < 15)
            return;

        session.LastReadinessLogUtc = now;
        BattleLuckPlugin.LogInfo($"[Session] Waiting to start {session.Context.ModeId}/{session.Context.ZoneHash}: {reason}.");
    }

    static float3 ZoneCenter(ZoneDefinition zone)
    {
        var center = zone.Position.ToFloat3();
        if (math.lengthsq(center) > 0.0001f)
            return center;

        return zone.TeleportSpawn.ToFloat3();
    }

    static WallBoundaryConfig WithoutFloorRing(WallBoundaryConfig source) => new()
    {
        Enabled = source.Enabled,
        Height = source.Height,
        Spacing = source.Spacing,
        BatchSize = source.BatchSize,
        WallPrefab = source.WallPrefab,
        FloorPrefab = source.FloorPrefab,
        SpawnWalls = source.SpawnWalls,
        SpawnFloors = false,
        RequireOnlineAdmin = source.RequireOnlineAdmin,
        FloorSpacing = source.FloorSpacing,
        Buffs = source.Buffs,
        Timers = source.Timers,
        Glow = source.Glow
    };

    static bool UnifiedOwnsArenaGeometry(ActiveSession session)
    {
        var definition = session.EventDefinition;
        if (definition == null)
            return false;

        return definition.Objects
            .SelectMany(o => o.Actions)
            .Select(a => string.IsNullOrWhiteSpace(a.Type) ? a.ToActionString().Split(':', 2)[0] : a.Type)
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Any(t =>
                t.Equals("zone.border.place", StringComparison.OrdinalIgnoreCase) ||
                t.Equals("zone.border.place_all", StringComparison.OrdinalIgnoreCase) ||
                t.Equals("wall.build", StringComparison.OrdinalIgnoreCase) ||
                t.Equals("floor.place", StringComparison.OrdinalIgnoreCase) ||
                t.Equals("tile.place", StringComparison.OrdinalIgnoreCase));
    }

    static bool UnifiedOwnsFloorGeometry(ActiveSession session)
    {
        var definition = session.EventDefinition;
        if (definition == null)
            return false;

        return definition.Objects
            .SelectMany(o => o.Actions)
            .Select(a => string.IsNullOrWhiteSpace(a.Type) ? a.ToActionString().Split(':', 2)[0] : a.Type)
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Any(t =>
                t.Equals("floor.place", StringComparison.OrdinalIgnoreCase) ||
                t.Equals("tile.place", StringComparison.OrdinalIgnoreCase) ||
                t.Equals("schematic.load", StringComparison.OrdinalIgnoreCase));
    }

    static string ResolveZoneEnterRule(ModeConfig config)
    {
        var sessionRule = config.Session?.Rules?.ZoneEnterRule;
        if (!string.IsNullOrWhiteSpace(sessionRule))
            return sessionRule.Trim();

        var rulesRule = config.Rules?.ZoneEnterRule;
        if (!string.IsNullOrWhiteSpace(rulesRule))
            return rulesRule.Trim();

        return "auto_enter";
    }

    static ActionStagingRules ResolveActionStagingRules(ModeConfig config)
    {
        return config.Session?.Rules?.ActionStaging
            ?? config.Rules?.ActionStaging
            ?? new ActionStagingRules();
    }

    static bool HasConfiguredFlow(FlowConfig flow)
    {
        return flow.ExecutionOrder?.Count > 0 || flow.Flows?.Count > 0;
    }

    static HashSet<ulong> GetOrCreateStagedEnterSet(GameModeContext context)
    {
        if (context.State.TryGetValue(StagedEnterPlayersStateKey, out var value) && value is HashSet<ulong> set)
            return set;

        set = new HashSet<ulong>();
        context.State[StagedEnterPlayersStateKey] = set;
        return set;
    }

    static void MarkStagedEnterAction(ActiveSession session, ulong steamId)
    {
        GetOrCreateStagedEnterSet(session.Context).Add(steamId);
    }

    void ReleaseStagedEnterActions(ActiveSession session, ZoneDefinition zone)
    {
        if (!HasConfiguredFlow(session.Config.FlowEnter))
            return;

        if (!session.Context.State.TryGetValue(StagedEnterPlayersStateKey, out var value) || value is not HashSet<ulong> staged || staged.Count == 0)
            return;

        var players = VRisingCore.GetOnlinePlayers()
            .Where(e => e.Exists() && e.IsPlayer() && staged.Contains(e.GetSteamId()))
            .ToList();

        foreach (var player in players)
        {
            var context = new FlowActionContext
            {
                PlayerCharacter = player,
                ZoneHash = session.Context.ZoneHash,
                PlayerState = _playerState,
                Registry = _registry,
                Config = session.Config,
                Zone = zone,
                GameContext = session.Context
            };

            var result = _actionExecutor.ExecuteFlow(session.Config.FlowEnter, context, rollbackOnFailure: false);
            if (!result.Success)
                BattleLuckPlugin.LogWarning($"[Session] Staged enter action release failed for {EntityExtensions.FormatPlayer(player.GetSteamId(), player)}: {result.Error}");
        }

        session.Context.State.Remove(StagedEnterPlayersStateKey);
    }

    // ── Session management ──────────────────────────────────────────────

    ActiveSession GetOrCreateSession(int zoneHash, string modeId)
    {
        if (_activeSessions.TryGetValue(zoneHash, out var existing))
            return existing;

        var mode = _registry.Resolve(modeId);
        var config = LoadEffectiveConfig(modeId);
        var eventDefinition = LoadRuntimeDefinition(modeId);
        ApplyArenaRotationForNewSession(modeId, config, eventDefinition);
        var zone = config.Zones.Zones.FirstOrDefault(z => z.Hash == zoneHash);

        var ctx = new GameModeContext
        {
            SessionId = $"{modeId}_{zoneHash}_{DateTime.UtcNow.Ticks}",
            ZoneHash = zoneHash,
            ModeId = modeId,
            TimeLimitSeconds = config.Session.Rules.MatchDurationMinutes > 0
                ? config.Session.Rules.MatchDurationMinutes * 60
                : DefaultModeDurationSeconds,
            Broadcast = msg => BattleLuckPlugin.BroadcastToSession?.Invoke(modeId, msg)
        };

        var spawner = new SpawnController();
        var border = new BorderWallController();
        var borderDot = new BorderController();
        var platform = new PlatformController();
        ctx.State["spawner"] = spawner;
        ctx.State["config"] = config;
        ctx.State["border"] = border;
        ctx.State["borderDot"] = borderDot;
        ctx.State["platform"] = platform;
        if (eventDefinition != null)
            ctx.State["eventDefinition"] = eventDefinition;

        // Session lifecycle state remains in the managed registry.
        // Team kill tracking is handled via GameModeContext state dictionary.
        var em = VRisingCore.EntityManager;
        var sessionEntity = Entity.Null;
        try
        {
            sessionEntity = em.CreateEntity();
        }
        catch (Exception ex)
        {
            BattleLuckPlugin.LogWarning($"[SessionController] Failed to create session entity: {ex.Message}. Using Entity.Null for session tracking.");
        }

        var session = new ActiveSession
        {
            SessionEntity = sessionEntity,
            Mode = mode,
            Context = ctx,
            Spawner = spawner,
            Border = border,
            BorderDot = borderDot,
            Platform = platform,
            Config = config,
            EventDefinition = eventDefinition
        };

        _activeSessions[zoneHash] = session;

        // Register zone for auto-trash
        var trashZone = zone;
        if (trashZone != null)
            _autoTrash.RegisterZone(zoneHash, trashZone);

        return session;
    }

    void EndSession(int zoneHash)
    {
        if (!_activeSessions.TryGetValue(zoneHash, out var session)) return;

        // Destroy Session Entity
        var em = VRisingCore.EntityManager;
        if (session.SessionEntity != Entity.Null && em.Exists(session.SessionEntity))
            em.DestroyEntity(session.SessionEntity);

        if (session.Context != null)
            _eventRuntime.RunEndingPhases(session.Context.SessionId);

        if (session.Context != null)
        {
            var zone = session.Config.Zones.Zones.FirstOrDefault(z => z.Hash == zoneHash)
                ?? new ZoneDefinition { Hash = zoneHash, Name = zoneHash.ToString() };
            foreach (var player in VRisingCore.GetOnlinePlayers().Where(e => e.Exists() && e.IsPlayer() && session.Context.Players.Contains(e.GetSteamId())))
                _flow.ExecuteEnding(session.Config, player, zone, session.Context);
        }

        if (session.Mode != null && session.Context != null)
        {
            session.Mode.OnEnd(session.Context);
            session.Mode.OnReset(session.Context);
        }

        if (session.Context != null)
            _eventRuntime.EndSession(session.Context.SessionId);

        if (EventGeometryMutationsEnabled && session.Context != null)
        {
            var schematicCleanup = SchematicLoader.ClearTrackingGroup(session.Context.SessionId);
            if (!schematicCleanup.Success)
                BattleLuckPlugin.LogWarning($"[Session] Failed to clear tracked schematics for {session.Context.SessionId}: {schematicCleanup.Error}");
        }

        RestorePlayersForSessionEnd(session, zoneHash);
        GrantPendingWinnerRewards(session);

        // Clean up all players in this session
        if (session.Context != null)
        {
            var sessionPlayers = VRisingCore.GetOnlinePlayers()
                .Where(e => e.Exists() && e.IsPlayer() && session.Context.Players.Contains(e.GetSteamId()))
                .ToList();
            session.BorderDot?.CleanupAll(sessionPlayers);

            foreach (var steamId in session.Context.Players.ToList())
            {
                _enteredPlayers.Remove(steamId);
                _arenaTeleportedPlayers.Remove(steamId);
                _pendingArenaRespawns.Remove(steamId);
                _playerZoneMap.Remove(steamId);
                FloorLockService.UnlockPlayer(steamId);
                BattleLuckPlugin.EquipmentTracker?.StopTrackingPlayer(steamId);
            }
        }

        // Remove burning penalty from ALL players who left this zone without .toggleleave
        ClearAllBurningForZone(zoneHash);

        session.Spawner.DespawnAll();
        session.BorderDot?.Reset();
        if (EventGeometryMutationsEnabled && session.Border != null)
        {
            session.Border.DespawnWalls();
            session.Border.DespawnFloors();
        }
        if (EventGeometryMutationsEnabled)
            session.Platform?.DespawnPlatform();

        _autoTrash.UnregisterZone(zoneHash);

        if (session.Context != null)
            session.Context.TechState = new SessionTechState();

        _activeSessions.Remove(zoneHash);
        if (!_activeSessions.Values.Any(s => s.Context?.State.ContainsKey("freeBuildEnabled") == true))
            global::BuildingRestrictionController.EnableRestrictions();
        BattleLuckPlugin.LogInfo($"[Session] Session ended for zone {zoneHash}.");
    }

    void RestorePlayersForSessionEnd(ActiveSession session, int zoneHash)
    {
        if (session.Context == null)
            return;

        var trackedIds = _playerZoneMap
            .Where(kv => kv.Value == zoneHash)
            .Select(kv => kv.Key)
            .Union(session.Context.Players)
            .Distinct()
            .ToList();

        if (trackedIds.Count == 0)
            return;

        var zone = session.Config.Zones.Zones.FirstOrDefault(z => z.Hash == zoneHash);
        var fallbackSpawn = zone?.TeleportSpawn.ToFloat3() ?? PenaltySpawn;
        var online = VRisingCore.GetOnlinePlayers()
            .Where(e => e.Exists() && e.IsPlayer())
            .ToDictionary(e => e.GetSteamId(), e => e);

        var restored = 0;
        var fallback = 0;
        var offline = 0;

        foreach (var steamId in trackedIds)
        {
            _enteredPlayers.Remove(steamId);
            _arenaTeleportedPlayers.Remove(steamId);
            _pendingArenaRespawns.Remove(steamId);
            _readyPlayers.Remove(steamId);
            _penaltyBurning.Remove(steamId);
            _playerZoneMap.Remove(steamId);
            _recentlyDied.Remove(steamId);
            FloorLockService.UnlockPlayer(steamId);
            BattleLuckPlugin.EquipmentTracker?.StopTrackingPlayer(steamId);

            if (!online.TryGetValue(steamId, out var player) || !player.Exists())
            {
                offline++;
                continue;
            }

            RemoveZoneProtectionBuffs(player);
            if (_playerState.RestoreSnapshot(player, zoneHash))
            {
                restored++;
                continue;
            }

            player.SetPosition(fallbackSpawn);
            player.HealToFull();
            fallback++;
        }

        BattleLuckPlugin.LogInfo($"[Session] End rollback for {session.Context.ModeId}/{zoneHash}: restored={restored}, fallbackSpawn={fallback}, offline={offline}.");
    }

    static void GrantPendingWinnerRewards(ActiveSession session)
    {
        if (session.Context == null ||
            !session.Context.State.TryGetValue("pendingWinnerRewardSteamId", out var winnerValue) ||
            winnerValue is not ulong winnerSteamId ||
            !session.Context.State.TryGetValue("pendingWinnerRewards", out var rewardsValue) ||
            rewardsValue is not List<(PrefabGUID Item, int Amount)> rewards)
        {
            return;
        }

        var winner = VRisingCore.GetOnlinePlayers().FirstOrDefault(player =>
            player.Exists() && player.IsPlayer() && player.GetSteamId() == winnerSteamId);
        if (!winner.Exists())
        {
            BattleLuckPlugin.LogWarning($"[Session] Winner reward deferred but player {winnerSteamId} is offline.");
            return;
        }

        var granted = 0;
        foreach (var reward in rewards)
        {
            if (reward.Item != PrefabGUID.Empty && reward.Amount > 0 && winner.TryGiveItem(reward.Item, reward.Amount))
                granted += reward.Amount;
        }

        session.Context.State.Remove("pendingWinnerRewardSteamId");
        session.Context.State.Remove("pendingWinnerRewards");
        session.Context.Broadcast?.Invoke($"Winner reward granted after rollback: {granted} item(s) to {ResolvePlayerName(winnerSteamId)}.");
        BattleLuckPlugin.LogInfo($"[Session] Granted {granted} post-rollback winner reward item(s) to {winnerSteamId}.");
    }

    /// <summary>Clear burning from all penalty players (used on event end).</summary>
    void ClearAllBurningForZone(int zoneHash)
    {
        // Remove burning from players who were penalized from this zone
        var toClear = _playerZoneMap.Where(kv => kv.Value == zoneHash && _penaltyBurning.Contains(kv.Key))
                                     .Select(kv => kv.Key).ToList();

        // Also clear any burning players no longer tracked in zone map
        // (they may have already been partially cleaned up)
        foreach (var steamId in toClear)
        {
            _penaltyBurning.Remove(steamId);
            _playerZoneMap.Remove(steamId);
        }

        if (toClear.Count > 0)
            BattleLuckPlugin.LogInfo($"[Session] Cleared burning penalty from {toClear.Count} player(s) on event end.");
    }

    /// <summary>Clear ALL burning penalties globally (admin command).</summary>
    public int ClearAllBurning(IEnumerable<Entity> onlinePlayers)
    {
        if (_penaltyBurning.Count == 0) return 0;

        int cleared = 0;
        foreach (var player in onlinePlayers)
        {
            if (!player.Exists() || !player.IsPlayer()) continue;
            ulong steamId = player.GetSteamId();
            if (_penaltyBurning.Remove(steamId))
            {
                cleared++;
            }
        }
        _penaltyBurning.Clear();
        return cleared;
    }

    /// <summary>Get count of currently burning players.</summary>
    public int BurningPlayerCount => _penaltyBurning.Count;

    /// <summary>Get count of entered players.</summary>
    public int EnteredPlayerCount => _enteredPlayers.Count;

    // ── Admin commands ──────────────────────────────────────────────────

    public void ForceEndByModeId(string modeId)
    {
        var toEnd = _activeSessions.Where(kv => string.Equals(kv.Value.Context?.ModeId, modeId, StringComparison.OrdinalIgnoreCase)).Select(kv => kv.Key).ToList();
        foreach (var hash in toEnd)
            EndSession(hash);
    }

    /// <summary>Force-start: teleport player to zone and auto-enter.</summary>
    public void ForceStart(string modeId, Entity playerCharacter, bool skipEnterActions = true)
    {
        var zoneEntry = _zoneModeMap.FirstOrDefault(kv => kv.Value.Equals(modeId, StringComparison.OrdinalIgnoreCase));
        if (zoneEntry.Value == null)
        {
            BattleLuckPlugin.LogWarning($"[Session] No zone mapped for mode '{modeId}'.");
            return;
        }

        modeId = zoneEntry.Value; // normalize to the registered mode id casing
        int zoneHash = zoneEntry.Key;
        var config = LoadEffectiveConfig(modeId);
        var zone = config.Zones.Zones.FirstOrDefault(z => z.Hash == zoneHash);
        if (zone == null) return;

        ulong steamId = playerCharacter.GetSteamId();

        // Execute enter flow directly; it snapshots the current return position
        // before cleanup and teleports as part of the normal event entry flow.
        var result = ExecuteEnterFlow(steamId, playerCharacter, zoneHash, modeId, skipEnterActions: skipEnterActions);
        if (result.Success && _activeSessions.TryGetValue(zoneHash, out var session))
        {
            session.StartForced = true;
            session.Context.State["testMode"] = true;
            session.ArenaReadyUtc = DateTime.UtcNow;
            session.Context.State["arenaReadyUtc"] = session.ArenaReadyUtc;
            if (skipEnterActions)
                session.Context.State["forceEnterSkipActions"] = true;

            BattleLuckPlugin.LogInfo($"[Session] Force start requested for {modeId}/{zoneHash}; start will begin after build queue clears. skipEnterActions={skipEnterActions}.");
        }
    }

    public OperationResult ForceStartForPlayer(ulong steamId)
    {
        var session = _activeSessions.Values.FirstOrDefault(s => s.Context.Players.Contains(steamId));
        if (session == null)
            return OperationResult.Fail("You are not in an active prepared session.");

        if (session.IsStarted)
            return OperationResult.Fail("This match already started.");

        session.StartForced = true;
        session.Context.State["testMode"] = true;
        session.ArenaReadyUtc = DateTime.UtcNow;
        session.Context.State["arenaReadyUtc"] = session.ArenaReadyUtc;
        return OperationResult.Ok();
    }

    /// <summary>Force a player to exit their current zone session.</summary>
    public bool ForceExitPlayer(ulong steamId, Entity playerCharacter)
    {
        if (!_playerZoneMap.TryGetValue(steamId, out var zoneHash))
            return false;

        // Stop any burning
        _penaltyBurning.Remove(steamId);

        ExecuteLeaveFlow(steamId, playerCharacter, zoneHash);
        _zoneDetection.SetPlayerZone(steamId, 0);
        return true;
    }

    public ActiveSession? GetSession(int zoneHash) => _activeSessions.GetValueOrDefault(zoneHash);
    public IReadOnlyDictionary<int, ActiveSession> ActiveSessions => _activeSessions;
    public SpawnController GetSpawner(int zoneHash) => _activeSessions.TryGetValue(zoneHash, out var s) ? s.Spawner : _spawner;

    public void PauseAll()
    {
        foreach (var session in _activeSessions.Values)
            session.IsPaused = true;
    }

    public int ResumeAll()
    {
        int count = 0;
        foreach (var session in _activeSessions.Values)
        {
            if (session.IsPaused) { session.IsPaused = false; count++; }
        }
        return count;
    }

    public bool TryKickPlayer(ulong steamId, out string? error)
    {
        error = null;
        foreach (var session in _activeSessions.Values)
        {
            if (session.Context.Players.Contains(steamId))
            {
                CleanupPlayerState(steamId, session);
                return true;
            }
        }
        error = "Player not in any active session";
        return false;
    }

    public bool TrySetWinner(ulong steamId, out string? error)
    {
        error = null;
        foreach (var session in _activeSessions.Values)
        {
            if (session.Context.Players.Contains(steamId))
            {
                session.Context.State["winner"] = steamId;
                session.Context.State["result"] = "admin_forced";
                EndSession(session.Context.ZoneHash);
                return true;
            }
        }
        error = "Player not in any active session";
        return false;
    }

    static void ApplyArenaRotationForNewSession(string modeId, ModeConfig config, UnifiedEventDefinition? eventDefinition)
    {
        var rotation = eventDefinition?.ArenaRotation;
        if (rotation?.Enabled != true || rotation.Points.Count == 0 || config.Zones.Zones.Count == 0)
            return;

        var index = NextArenaRotationIndex(modeId, rotation.Points.Count);
        var point = rotation.Points[index];
        var radius = rotation.Radius > 0f ? rotation.Radius : config.Zones.Zones[0].Radius;
        var exitRadius = rotation.ExitRadius > 0f ? rotation.ExitRadius : radius + 10f;

        foreach (var zone in config.Zones.Zones)
        {
            zone.Position = point.Center;
            zone.TeleportSpawn = point.Center;
            zone.Radius = radius;
            zone.ExitRadius = exitRadius;
        }

        BattleLuckPlugin.LogInfo(
            $"[Session] Arena rotation selected for {modeId}: {point.Id} at " +
            $"({point.Center.X:F1},{point.Center.Y:F1},{point.Center.Z:F1}) radius={radius:F0} exit={exitRadius:F0}.");
    }

    static int NextArenaRotationIndex(string modeId, int count)
    {
        if (count <= 1)
            return 0;

        lock (ArenaRotationCursor)
        {
            ArenaRotationCursor.TryGetValue(modeId, out var current);
            var index = Math.Abs(current % count);
            ArenaRotationCursor[modeId] = (index + 1) % count;
            return index;
        }
    }

    void InitializeSessionTechState(ActiveSession session)
    {
        if (session.Context == null)
            return;

        var allowedTechIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (session.Config.Rules?.TechIds != null)
            foreach (var id in session.Config.Rules.TechIds)
                if (!string.IsNullOrWhiteSpace(id))
                    allowedTechIds.Add(id);

        if (session.Config.Session?.Rules?.TechIds != null)
            foreach (var id in session.Config.Session.Rules.TechIds)
                if (!string.IsNullOrWhiteSpace(id))
                    allowedTechIds.Add(id);

        if (allowedTechIds.Count == 0)
            return;

        try
        {
            var techCatalog = ConfigLoader.LoadTechCatalog();
            if (techCatalog == null)
                return;

            var resolver = new TechResolver(techCatalog);
            var (success, state, error) = resolver.Resolve(allowedTechIds.ToList());
            if (success && state != null)
            {
                session.Context.TechState = state;
                BattleLuckPlugin.LogInfo($"[Session] Initialized {state.ActiveTechs.Count} tech(s) for mode '{session.Context.ModeId}'.");
            }
            else if (!string.IsNullOrWhiteSpace(error))
            {
                BattleLuckPlugin.LogWarning($"[Session] Tech initialization failed for mode '{session.Context.ModeId}': {error}");
            }
        }
        catch (Exception ex)
        {
            BattleLuckPlugin.LogWarning($"[Session] Tech initialization error for mode '{session.Context.ModeId}': {ex.Message}");
        }
    }
}

public sealed class ActiveSession
{
    public Entity SessionEntity { get; set; }
    public GameModeBase? Mode { get; set; }
    public GameModeContext Context { get; set; } = new();
    public SpawnController Spawner { get; set; } = new();
    public BorderWallController? Border { get; set; }
    public BorderController? BorderDot { get; set; }
    public PlatformController? Platform { get; set; }
    public ModeConfig Config { get; set; } = new();
    public bool IsStarted { get; set; }
    public bool IsPaused { get; set; }
    public bool ArenaInitialized { get; set; }
    public bool ArenaSpawning { get; set; }
    public DateTime ArenaPreparationStartedUtc { get; set; }
    public DateTime ArenaReadyUtc { get; set; }
    public DateTime StartWarmupEndsUtc { get; set; }
    public DateTime LastReadinessLogUtc { get; set; }
    public int LastPreparationCountdownBucket { get; set; }
    public int LastWarmupCountdownSecond { get; set; }
    public bool StartWarmupActive { get; set; }
    public bool StartForced { get; set; }
    public bool EventRuntimeInitialized { get; set; }
    public UnifiedEventDefinition? EventDefinition { get; set; }
}
