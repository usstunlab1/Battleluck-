using BattleLuck.Models;

namespace BattleLuck.Services.Npc;

/// <summary>
/// Master controller for adaptive NPC behavior. Orchestrates movement, combat,
/// observation, and pattern recognition into a single tick loop. Each NPC
/// assigned to the controller gets an AdaptiveNpcSession that tracks its
/// entity references, mode, and configuration.
/// </summary>
public sealed class AdaptiveNpcController
{
    public static AdaptiveNpcController Instance { get; } = new();

    readonly Dictionary<string, AdaptiveNpcSession> _sessions = new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<string, List<string>> _sessionsByEvent = new(StringComparer.OrdinalIgnoreCase);
    readonly NpcMovementController _movement = NpcMovementController.Instance;
    readonly NpcCombatController _combat = NpcCombatController.Instance;
    readonly NpcTargetObserver _observer = NpcTargetObserver.Instance;
    readonly PlayerPatternRecognizer _recognizer = PlayerPatternRecognizer.Instance;

    readonly object _lock = new();

    /// <summary>
    /// Register an NPC for adaptive control.
    /// </summary>
    public AdaptiveNpcSession RegisterSession(
        string npcId,
        string sessionId,
        Entity npcEntity,
        Entity playerEntity,
        ulong playerSteamId,
        float3 homePosition,
        OffsetFollowConfig? followConfig = null)
    {
        var session = new AdaptiveNpcSession
        {
            NpcId = npcId,
            SessionId = sessionId,
            NpcEntity = npcEntity,
            ObservedPlayerEntity = playerEntity,
            ObservedPlayerSteamId = playerSteamId,
            CurrentMode = AdaptiveNpcMode.Follow,
            DesiredOffset = float3.zero,
            FollowConfig = followConfig ?? new OffsetFollowConfig
            {
                ForwardOffset = -3f,
                SideOffset = 0f,
                PositionTolerance = 1.5f,
                PathUpdateIntervalSeconds = 0.3f,
                MaximumFollowDistance = 40f,
                MinimumMovementSpeed = 2f,
                MaximumMovementSpeed = 10f,
                StuckTimeoutSeconds = 5f,
                FollowGain = 2.5f
            },
            HomePosition = homePosition,
            LeashRange = 80f,
            PreferredCombatDistance = 8f,
            LastPosition = npcEntity.Exists() ? npcEntity.GetPosition() : float3.zero
        };

        lock (_lock)
        {
            _sessions[npcId] = session;
            if (!_sessionsByEvent.TryGetValue(sessionId, out var list))
            {
                list = new List<string>();
                _sessionsByEvent[sessionId] = list;
            }
            list.Add(npcId);
        }

        BattleLuckPlugin.LogInfo($"[AdaptiveNpcController] Registered NPC '{npcId}' for session '{sessionId}'.");

        // Apply initial mode
        _combat.ApplyMode(session, AdaptiveNpcMode.Follow);

        return session;
    }

    /// <summary>
    /// Unregister a single NPC from adaptive control.
    /// </summary>
    public void UnregisterSession(string npcId)
    {
        lock (_lock)
        {
            if (_sessions.TryGetValue(npcId, out var session))
            {
                if (_sessionsByEvent.TryGetValue(session.SessionId, out var list))
                    list.Remove(npcId);
                _sessions.Remove(npcId);
            }
        }
    }

    /// <summary>
    /// Unregister all NPCs for an event session.
    /// </summary>
    public void UnregisterSessionId(string sessionId)
    {
        lock (_lock)
        {
            if (_sessionsByEvent.TryGetValue(sessionId, out var list))
            {
                foreach (var npcId in list)
                    _sessions.Remove(npcId);
                _sessionsByEvent.Remove(sessionId);
            }
        }
    }

    /// <summary>
    /// Get a session by NPC ID.
    /// </summary>
    public AdaptiveNpcSession? GetSession(string npcId)
    {
        lock (_lock)
            return _sessions.TryGetValue(npcId, out var session) ? session : null;
    }

    /// <summary>
    /// Get all sessions for an event.
    /// </summary>
    public IReadOnlyList<AdaptiveNpcSession> GetSessions(string sessionId)
    {
        lock (_lock)
        {
            if (_sessionsByEvent.TryGetValue(sessionId, out var list))
                return list.Select(id => _sessions.TryGetValue(id, out var s) ? s : null)
                    .Where(s => s != null)
                    .Select(s => s!)
                    .ToList();
            return Array.Empty<AdaptiveNpcSession>();
        }
    }

    /// <summary>
    /// Main tick — called each server tick for all registered adaptive NPCs.
    /// </summary>
    public void Tick(float deltaSeconds)
    {
        AdaptiveNpcSession[] sessions;

        lock (_lock)
        {
            // Clean up dead NPCs
            var dead = _sessions.Values.Where(s => !s.NpcEntity.Exists()).Select(s => s.NpcId).ToList();
            foreach (var id in dead)
            {
                if (_sessions.TryGetValue(id, out var ds) &&
                    _sessionsByEvent.TryGetValue(ds.SessionId, out var list))
                    list.Remove(id);
                _sessions.Remove(id);
            }

            sessions = _sessions.Values.ToArray();
        }

        foreach (var session in sessions)
        {
            try
            {
                TickSession(session, deltaSeconds);
            }
            catch (Exception ex)
            {
                BattleLuckPlugin.LogWarning($"[AdaptiveNpcController] Tick failed for '{session.NpcId}': {ex.Message}");
            }
        }
    }

    void TickSession(AdaptiveNpcSession session, float deltaSeconds)
    {
        // Validate entities
        if (!session.NpcEntity.Exists())
        {
            UnregisterSession(session.NpcId);
            return;
        }

        // Try to re-resolve the player entity if it was lost (e.g., after respawn)
        if (!session.ObservedPlayerEntity.Exists() && session.ObservedPlayerSteamId != 0)
        {
            var resolved = ResolvePlayerEntity(session.ObservedPlayerSteamId);
            if (resolved.HasValue)
            {
                session.ObservedPlayerEntity = resolved.Value;
                BattleLuckPlugin.LogInfo($"[AdaptiveNpcController] Re-resolved player entity for NPC '{session.NpcId}'.");
            }
            else
            {
                // Player is offline — hold position
                _movement.TickHoldPosition(session, deltaSeconds);
                return;
            }
        }

        var playerEntity = session.ObservedPlayerEntity;
        if (!playerEntity.Exists())
        {
            _movement.TickHoldPosition(session, deltaSeconds);
            return;
        }

        // Observe player state
        var observation = _observer.Observe(playerEntity, session.NpcEntity);

        // Recognize patterns
        var pattern = _recognizer.Evaluate(observation, session.CurrentMode);

        // Determine appropriate reaction
        var npcWeaponCategory = DetermineNpcWeaponCategory(session.NpcEntity);
        var nextMode = _recognizer.DetermineReaction(pattern, npcWeaponCategory);

        // Apply mode transition if changed
        if (nextMode != session.CurrentMode)
        {
            _combat.ApplyMode(session, nextMode);
        }

        // Execute movement behavior for the current mode
        switch (session.CurrentMode)
        {
            case AdaptiveNpcMode.OffsetFollow:
                _movement.TickOffsetFollow(session, deltaSeconds);
                break;

            case AdaptiveNpcMode.Chase:
            case AdaptiveNpcMode.Attack:
                _movement.TickChase(session, deltaSeconds);
                break;

            case AdaptiveNpcMode.KeepDistance:
                _movement.TickKeepDistance(session, deltaSeconds);
                break;

            case AdaptiveNpcMode.Evade:
                _movement.TickKeepDistance(session, deltaSeconds);
                break;

            case AdaptiveNpcMode.Flank:
                _movement.TickFlank(session, deltaSeconds);
                break;

            case AdaptiveNpcMode.Retreat:
                _movement.TickRetreat(session, deltaSeconds);
                break;

            case AdaptiveNpcMode.HoldPosition:
                _movement.TickHoldPosition(session, deltaSeconds);
                break;

            case AdaptiveNpcMode.Follow:
            default:
                _movement.TickChase(session, deltaSeconds);
                break;
        }
    }

    /// <summary>
    /// Resolve a player entity by Steam ID from the server entity manager.
    /// </summary>
    static Entity? ResolvePlayerEntity(ulong steamId)
    {
        try
        {
            foreach (var player in VRisingCore.GetOnlinePlayers())
            {
                if (player.Exists() && player.IsPlayer() && player.GetSteamId() == steamId)
                    return player;
            }
        }
        catch { }

        return null;
    }

    /// <summary>
    /// Attempt to determine the NPC's weapon category from its prefab name.
    /// </summary>
    static WeaponCategory DetermineNpcWeaponCategory(Entity npcEntity)
    {
        if (!npcEntity.Exists()) return WeaponCategory.Melee;

        try
        {
            if (npcEntity.Has<Equipment>())
            {
                return WeaponCategory.Melee;
            }
        }
        catch { }

        // Default to melee for unknown NPCs
        return WeaponCategory.Melee;
    }

    static WeaponCategory ClassifyNpcWeapon(PrefabGUID weapon)
    {
        var name = PrefabHelper.GetName(weapon) ?? "";
        if (name.Contains("bow", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("crossbow", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("rifle", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("pistol", StringComparison.OrdinalIgnoreCase))
            return WeaponCategory.Ranged;
        if (name.Contains("staff", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("wand", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("scepter", StringComparison.OrdinalIgnoreCase))
            return WeaponCategory.Magic;
        return WeaponCategory.Melee;
    }

    /// <summary>
    /// Get the total number of active adaptive NPCs.
    /// </summary>
    public int ActiveCount
    {
        get
        {
            lock (_lock)
            {
                CleanupDeadLocked();
                return _sessions.Count;
            }
        }
    }

    void CleanupDeadLocked()
    {
        var dead = _sessions.Values.Where(s => !s.NpcEntity.Exists()).Select(s => s.NpcId).ToList();
        foreach (var id in dead)
        {
            if (_sessions.TryGetValue(id, out var ds) &&
                _sessionsByEvent.TryGetValue(ds.SessionId, out var list))
                list.Remove(id);
            _sessions.Remove(id);
        }
    }
}