using BattleLuck.Models;
using BattleLuck.Services.Runtime;

namespace BattleLuck.Services.Npc;

/// <summary>
/// Controls NPC movement behavior including offset-based pursuit (formation
/// following), chase/retreat decisions, and navigation with stuck detection.
/// Does NOT set the NPC transform directly — instead updates navigation
/// destinations for the native movement system.
/// </summary>
public sealed class NpcMovementController
{
    public static NpcMovementController Instance { get; } = new();

    /// <summary>
    /// Calculate the desired offset position from the player's transform.
    /// For player-relative offset:
    ///   desiredPosition = playerPosition + playerForward * forwardOffset + playerRight * sideOffset
    /// </summary>
    public float3 CalculateOffsetPosition(
        Entity playerEntity,
        OffsetFollowConfig config,
        float3? explicitPlayerPosition = null,
        quaternion? explicitPlayerRotation = null)
    {
        if (!playerEntity.Exists())
            return float3.zero;

        float3 playerPos;
        quaternion playerRot;

        if (explicitPlayerPosition.HasValue)
        {
            playerPos = explicitPlayerPosition.Value;
            playerRot = explicitPlayerRotation ?? quaternion.identity;
        }
        else
        {
            try
            {
                var localToWorld = playerEntity.Read<LocalToWorld>();
                playerPos = localToWorld.Position;
                playerRot = localToWorld.Rotation;
            }
            catch
            {
                playerPos = playerEntity.GetPosition();
                playerRot = quaternion.identity;
            }
        }

        var forward = math.forward(playerRot);
        var right = math.mul(playerRot, new float3(1, 0, 0));

        return playerPos
               + forward * config.ForwardOffset
               + right * config.SideOffset;
    }

    /// <summary>
    /// Move an NPC toward a target position via the NpcControlService.
    /// Uses the existing Follow/GoTo mechanism for smooth navigation.
    /// </summary>
    public void MoveToward(string npcId, float3 targetPosition, float arrivalRange = 2f)
    {
        if (BattleLuckPlugin.NpcService == null) return;

        try
        {
            var entry = BattleLuckPlugin.NpcService.GetEntry(npcId);
            if (entry == null || !entry.IsAlive) return;

            var pos = entry.Entity.GetPosition();
            var dist = math.distance(pos, targetPosition);

            if (dist > arrivalRange)
            {
                // Use GoTo for destination-based movement
                BattleLuckPlugin.NpcService.GoTo(npcId, targetPosition, arrivalRange);
            }
            else
            {
                // We're close enough — switch to Hold
                BattleLuckPlugin.NpcService.Hold(npcId, arrivalRange);
            }
        }
        catch (Exception ex)
        {
            BattleLuckPlugin.LogWarning($"[NpcMovement] MoveToward failed for '{npcId}': {ex.Message}");
        }
    }

    /// <summary>
    /// Execute offset-follow movement: follow the player at a configured
    /// distance and direction. Updates the NPC navigation destination
    /// smoothly with path update throttling.
    /// </summary>
    public void TickOffsetFollow(AdaptiveNpcSession session, float deltaSeconds)
    {
        if (!session.ObservedPlayerEntity.Exists() || !session.NpcEntity.Exists())
            return;

        var config = session.FollowConfig;
        var now = (float)DateTime.UtcNow.TimeOfDay.TotalSeconds;

        // Calculate desired offset position
        var desiredPosition = CalculateOffsetPosition(
            session.ObservedPlayerEntity,
            config);

        // Check distance
        var npcPos = session.NpcEntity.GetPosition();
        var dist = math.distance(npcPos, desiredPosition);

        // Check if stuck
        var moved = math.distance(session.LastPosition, npcPos);
        session.LastPosition = npcPos;

        if (moved < 0.05f && dist > config.PositionTolerance)
        {
            session.StuckTimer += deltaSeconds;
            if (session.StuckTimer >= config.StuckTimeoutSeconds && !session.IsStuck)
            {
                session.IsStuck = true;
                BattleLuckPlugin.LogInfo($"[NpcMovement] NPC '{session.NpcId}' appears stuck. Attempting recovery.");
            }
        }
        else
        {
            session.StuckTimer = 0;
            session.IsStuck = false;
        }

        // If stuck, try to recalculate
        if (session.IsStuck)
        {
            // Reset stuck state and force path update
            session.IsStuck = false;
            session.StuckTimer = 0;
            MoveToward(session.NpcId, desiredPosition, config.PositionTolerance);
            return;
        }

        // Update path destination only when difference exceeds tolerance
        var timeSinceLastUpdate = now - session.LastPathUpdateTime;
        if (dist > config.PositionTolerance && timeSinceLastUpdate >= config.PathUpdateIntervalSeconds)
        {
            session.LastPathUpdateTime = now;
            MoveToward(session.NpcId, desiredPosition, config.PositionTolerance);
        }

        // Smooth speed adjustment based on distance
        if (dist > config.MaximumFollowDistance)
        {
            // Too far — teleport to offset to prevent endless chasing
            TeleportToOffset(session, desiredPosition);
        }
    }

    /// <summary>
    /// Chase the player entity aggressively (used in Attack/Chase modes).
    /// </summary>
    public void TickChase(AdaptiveNpcSession session, float deltaSeconds)
    {
        if (!session.ObservedPlayerEntity.Exists())
            return;

        // Move to within preferred combat distance
        MoveToward(session.NpcId, session.ObservedPlayerEntity.GetPosition(),
                   session.PreferredCombatDistance);
    }

    /// <summary>
    /// Keep distance from the player (used in KeepDistance/Evade modes).
    /// </summary>
    public void TickKeepDistance(AdaptiveNpcSession session, float deltaSeconds)
    {
        if (!session.ObservedPlayerEntity.Exists() || !session.NpcEntity.Exists())
            return;

        var npcPos = session.NpcEntity.GetPosition();
        var playerPos = session.ObservedPlayerEntity.GetPosition();
        var dist = math.distance(npcPos, playerPos);

        if (dist < session.PreferredCombatDistance * 0.7f)
        {
            // Too close — move away
            var awayDir = math.normalizesafe(npcPos - playerPos);
            var retreatTarget = npcPos + awayDir * session.PreferredCombatDistance;
            MoveToward(session.NpcId, retreatTarget, 2f);
        }
        else if (dist > session.PreferredCombatDistance * 1.3f)
        {
            // Too far — move closer
            MoveToward(session.NpcId, playerPos, session.PreferredCombatDistance);
        }
    }

    /// <summary>
    /// Flank the player — move to the side while maintaining distance.
    /// </summary>
    public void TickFlank(AdaptiveNpcSession session, float deltaSeconds)
    {
        if (!session.ObservedPlayerEntity.Exists() || !session.NpcEntity.Exists())
            return;

        var config = session.FollowConfig;
        // Swap side offset periodically for flanking behavior
        var flankConfig = new OffsetFollowConfig
        {
            ForwardOffset = 0,
            SideOffset = config.SideOffset > 0 ? -Math.Abs(config.SideOffset) : Math.Abs(config.SideOffset),
            PositionTolerance = config.PositionTolerance,
            PathUpdateIntervalSeconds = config.PathUpdateIntervalSeconds * 0.5f,
            MaximumFollowDistance = config.MaximumFollowDistance,
            MinimumMovementSpeed = config.MinimumMovementSpeed,
            MaximumMovementSpeed = config.MaximumMovementSpeed * 1.2f,
            StuckTimeoutSeconds = config.StuckTimeoutSeconds,
            FollowGain = config.FollowGain
        };

        var desiredPosition = CalculateOffsetPosition(
            session.ObservedPlayerEntity,
            flankConfig);

        MoveToward(session.NpcId, desiredPosition, 3f);
    }

    /// <summary>
    /// Retreat toward the NPC's home position.
    /// </summary>
    public void TickRetreat(AdaptiveNpcSession session, float deltaSeconds)
    {
        MoveToward(session.NpcId, session.HomePosition, 2f);
    }

    /// <summary>
    /// Hold position — stay at current position.
    /// </summary>
    public void TickHoldPosition(AdaptiveNpcSession session, float deltaSeconds)
    {
        if (BattleLuckPlugin.NpcService == null) return;
        BattleLuckPlugin.NpcService.Hold(session.NpcId, 3f);
    }

    /// <summary>
    /// Emergency teleport to the offset position when distance exceeds max follow.
    /// Only used as recovery, not normal movement.
    /// </summary>
    void TeleportToOffset(AdaptiveNpcSession session, float3 desiredPosition)
    {
        if (!session.NpcEntity.Exists()) return;

        try
        {
            session.NpcEntity.SetPosition(desiredPosition);
            BattleLuckPlugin.LogInfo($"[NpcMovement] Teleported NPC '{session.NpcId}' to offset position (distance exceeded max follow).");
        }
        catch (Exception ex)
        {
            BattleLuckPlugin.LogWarning($"[NpcMovement] Teleport failed for '{session.NpcId}': {ex.Message}");
        }
    }
}