using BattleLuck.Models;
using BattleLuck.Services.Npc;
using Unity.Entities;
using Unity.Mathematics;

namespace BattleLuck.Services.Companion;

/// <summary>
/// Manages companion assignments, limits, and behavior routing through NpcControlService.
/// All companion.* actions dispatch through this service, not through separate servant/pet/ai_vampire handlers.
/// </summary>
public sealed class CompanionService
{
    readonly NpcControlService _npcService;
    readonly object _lock = new();

    // Per-player companion tracking: steamId -> list of npcIds
    readonly Dictionary<ulong, List<string>> _playerCompanions = new();
    // Per-clan companion tracking: clanId -> list of npcIds
    readonly Dictionary<ulong, List<string>> _clanCompanions = new();
    // Per-event companion tracking: sessionId -> list of npcIds
    readonly Dictionary<string, List<string>> _eventCompanions = new(StringComparer.OrdinalIgnoreCase);
    // Reverse lookup: npcId -> owner info
    readonly Dictionary<string, CompanionOwner> _companionOwners = new(StringComparer.OrdinalIgnoreCase);

    // Default limits
    int _defaultPlayerLimit = 3;
    int _defaultClanLimit = 10;
    int _defaultEventLimit = 50;

    public CompanionService(NpcControlService npcService)
    {
        _npcService = npcService;
    }

    public sealed record CompanionOwner(
        ulong? PlayerSteamId,
        ulong? ClanId,
        string? SessionId,
        string Role,
        bool Persist);

    /// <summary>
    /// Assign an already-controlled NPC to a player as a companion.
    /// </summary>
    public OperationResult Assign(string entityId, ulong ownerSteamId, string role = "follow", bool persist = false)
    {
        if (!_npcService.TryGet(entityId, out var entry))
            return OperationResult.Fail($"NPC '{entityId}' is not tracked by NpcControlService.");

        if (entry.Entity.Has<PlayerCharacter>())
            return OperationResult.Fail("Cannot assign a player entity as a companion.");

        lock (_lock)
        {
            // Check per-player limit
            if (_playerCompanions.TryGetValue(ownerSteamId, out var playerList) && playerList.Count >= _defaultPlayerLimit)
                return OperationResult.Fail($"Player companion limit ({_defaultPlayerLimit}) reached.");

            // Check if already assigned
            if (_companionOwners.ContainsKey(entityId))
                return OperationResult.Fail($"NPC '{entityId}' is already assigned as a companion.");

            var owner = new CompanionOwner(ownerSteamId, null, null, role, persist);
            _companionOwners[entityId] = owner;

            if (!_playerCompanions.ContainsKey(ownerSteamId))
                _playerCompanions[ownerSteamId] = new();
            _playerCompanions[ownerSteamId].Add(entityId);

            // Apply initial behavior based on role
            switch (role.ToLowerInvariant())
            {
                case "guard":
                    _npcService.Hold(entityId, entry.HomeRadius);
                    break;
                case "support":
                case "combat":
                case "follow":
                default:
                    _npcService.Follow(entityId, Entity.Null, 6f, 80f);
                    break;
            }

            return OperationResult.Ok();
        }
    }

    /// <summary>
    /// Command a companion to follow a player.
    /// </summary>
    public OperationResult Follow(string entityId, ulong targetSteamId, float distance = 6f, float teleportCatchupDistance = 80f, bool combatEnabled = true)
    {
        if (!ValidateOwnership(entityId, out var error))
            return OperationResult.Fail(error);

        var targetEntity = PlayerEntityHelper.GetEntityBySteamId(targetSteamId);
        if (!targetEntity.Exists())
            return OperationResult.Fail($"Target player {targetSteamId} not found.");

        _npcService.Follow(entityId, targetEntity, distance, teleportCatchupDistance);
        return OperationResult.Ok();
    }

    /// <summary>
    /// Command a companion to guard a position.
    /// </summary>
    public OperationResult Guard(string entityId, float3 center, float radius = 20f, string aggroMode = "aggressive", bool returnToCenter = true)
    {
        if (!ValidateOwnership(entityId, out var error))
            return OperationResult.Fail(error);

        _npcService.Hold(entityId, radius);
        return OperationResult.Ok();
    }

    /// <summary>
    /// Unassign a companion without destroying the NPC.
    /// </summary>
    public OperationResult Dismiss(string entityId)
    {
        lock (_lock)
        {
            if (!_companionOwners.Remove(entityId, out var owner))
                return OperationResult.Fail($"NPC '{entityId}' is not assigned as a companion.");

            if (owner.PlayerSteamId.HasValue && _playerCompanions.TryGetValue(owner.PlayerSteamId.Value, out var list))
                list.Remove(entityId);

            _npcService.Release(entityId);
            return OperationResult.Ok();
        }
    }

    /// <summary>
    /// Destroy a BattleLuck-owned companion and remove persistent tracking.
    /// </summary>
    public OperationResult Despawn(string entityId)
    {
        lock (_lock)
        {
            Dismiss(entityId);
            _npcService.Despawn(entityId);
            return OperationResult.Ok();
        }
    }

    /// <summary>
    /// Set companion behavior: follow, guard, patrol, wander, flee, or assist.
    /// </summary>
    public OperationResult SetBehavior(string entityId, string behavior, float radius = 6f, ulong? targetSteamId = null, bool combatEnabled = true)
    {
        if (!ValidateOwnership(entityId, out var error))
            return OperationResult.Fail(error);

        switch (behavior.ToLowerInvariant())
        {
            case "follow":
                if (targetSteamId.HasValue)
                    return Follow(entityId, targetSteamId.Value, radius, 80f, combatEnabled);
                _npcService.Follow(entityId, Entity.Null, radius, 80f);
                return OperationResult.Ok();
            case "guard":
                _npcService.Hold(entityId, radius);
                return OperationResult.Ok();
            case "patrol":
                _npcService.Patrol(entityId, new List<NpcPatrolWaypoint>());
                return OperationResult.Ok();
            case "wander":
                _npcService.Wander(entityId, new NpcWanderConfig { Radius = radius });
                return OperationResult.Ok();
            case "flee":
                _npcService.Flee(entityId, new NpcFleeConfig
                {
                    FromEntity = targetSteamId.HasValue ? PlayerEntityHelper.GetEntityBySteamId(targetSteamId.Value) : null,
                    SafeDistance = radius,
                    DurationSeconds = 10f
                });
                return OperationResult.Ok();
            case "assist":
                _npcService.Follow(entityId, Entity.Null, radius, 80f);
                return OperationResult.Ok();
            default:
                return OperationResult.Fail($"Unknown companion behavior '{behavior}'. Valid: follow, guard, patrol, wander, flee, assist.");
        }
    }

    /// <summary>
    /// Set per-player, per-clan, or per-event companion limits.
    /// </summary>
    public OperationResult SetLimit(string scope, int maximum, string overflowPolicy = "reject")
    {
        switch (scope.ToLowerInvariant())
        {
            case "player":
                _defaultPlayerLimit = Math.Max(1, maximum);
                return OperationResult.Ok();
            case "clan":
                _defaultClanLimit = Math.Max(1, maximum);
                return OperationResult.Ok();
            case "event":
                _defaultEventLimit = Math.Max(1, maximum);
                return OperationResult.Ok();
            default:
                return OperationResult.Fail($"Unknown scope '{scope}'. Valid: player, clan, event.");
        }
    }

    bool ValidateOwnership(string entityId, out string error)
    {
        lock (_lock)
        {
            if (!_companionOwners.ContainsKey(entityId))
            {
                error = $"NPC '{entityId}' is not assigned as a companion.";
                return false;
            }
            if (!_npcService.TryGet(entityId, out _))
            {
                error = $"NPC '{entityId}' is no longer tracked by NpcControlService.";
                return false;
            }
            error = "";
            return true;
        }
    }
}