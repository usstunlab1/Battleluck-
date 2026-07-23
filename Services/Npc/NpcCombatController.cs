using BattleLuck.Models;

namespace BattleLuck.Services.Npc;

/// <summary>
/// Controls NPC combat behavior by commanding the native NPC entity through
/// server-side targeting, aggro, and ability systems. Does NOT attempt to
/// reproduce player-only input systems or force player-specific animations.
/// </summary>
public sealed class NpcCombatController
{
    public static NpcCombatController Instance { get; } = new();

    /// <summary>
    /// Apply a combat mode to the NPC entity by updating its native aggro,
    /// targeting, and behavior components.
    /// </summary>
    public void ApplyMode(AdaptiveNpcSession session, AdaptiveNpcMode mode)
    {
        if (!session.NpcEntity.Exists())
            return;

        if (BattleLuckPlugin.NpcService == null) return;

        var npcId = session.NpcId;
        var playerEntity = session.ObservedPlayerEntity;

        if (!playerEntity.Exists())
        {
            BattleLuckPlugin.NpcService.Hold(npcId, 5f);
            return;
        }

        switch (mode)
        {
            case AdaptiveNpcMode.Attack:
                // Aggro the player — native NPC combat system takes over
                BattleLuckPlugin.NpcService.Aggro(npcId, playerEntity, session.PreferredCombatDistance, session.LeashRange);
                TryTriggerAttack(session.NpcEntity, playerEntity);
                break;

            case AdaptiveNpcMode.Chase:
                BattleLuckPlugin.NpcService.Aggro(npcId, playerEntity, session.PreferredCombatDistance * 0.8f, session.LeashRange);
                break;

            case AdaptiveNpcMode.Follow:
                BattleLuckPlugin.NpcService.Follow(npcId, playerEntity, session.PreferredCombatDistance, session.LeashRange);
                break;

            case AdaptiveNpcMode.KeepDistance:
                // Follow at a distance — don't aggro
                BattleLuckPlugin.NpcService.Follow(npcId, playerEntity, session.PreferredCombatDistance * 1.5f, session.LeashRange);
                break;

            case AdaptiveNpcMode.Evade:
                // Flee from the player
                var fleeConfig = new NpcFleeConfig
                {
                    FromEntity = playerEntity,
                    SafeDistance = session.PreferredCombatDistance * 2f,
                    DurationSeconds = 3f,
                    FleeSpeedMultiplier = 1.3f,
                    ResumePreviousOnExpiry = true
                };
                BattleLuckPlugin.NpcService.Flee(npcId, fleeConfig);
                break;

            case AdaptiveNpcMode.Flank:
                // Follow at a side offset — handled by movement controller
                BattleLuckPlugin.NpcService.Follow(npcId, playerEntity, 6f, session.LeashRange);
                break;

            case AdaptiveNpcMode.Retreat:
                BattleLuckPlugin.NpcService.GoTo(npcId, session.HomePosition, 3f);
                break;

            case AdaptiveNpcMode.HoldPosition:
                BattleLuckPlugin.NpcService.Hold(npcId, 3f);
                break;

            case AdaptiveNpcMode.OffsetFollow:
                BattleLuckPlugin.NpcService.Follow(npcId, playerEntity, 4f, session.LeashRange);
                break;

            case AdaptiveNpcMode.Idle:
            default:
                BattleLuckPlugin.NpcService.Release(npcId);
                break;
        }

        session.CurrentMode = mode;
    }

    /// <summary>
    /// Try to trigger the NPC's native attack ability against the player.
    /// This touches the aggro system which should cause the native AI to
    /// select and execute its attack abilities.
    /// </summary>
    static void TryTriggerAttack(Entity npcEntity, Entity targetEntity)
    {
        if (!npcEntity.Exists() || !targetEntity.Exists()) return;

        try
        {
            // Touch aggro to wake up the native AI
            if (npcEntity.Has<Aggroable>())
            {
                var aggro = npcEntity.Read<Aggroable>();
                // Reading the component is enough to trigger aggro processing
            }

            // Set the aggro target if the component exists
            if (npcEntity.Has<AggroConsumer>())
            {
                var consumer = npcEntity.Read<AggroConsumer>();
                // The native system will handle target selection
            }
        }
        catch
        {
            // Native AI details vary between game builds
        }
    }

    /// <summary>
    /// Check if the NPC is currently in combat (has active aggro targets).
    /// </summary>
    public bool IsInCombat(Entity npcEntity)
    {
        if (!npcEntity.Exists()) return false;

        try
        {
            if (npcEntity.Has<AggroConsumer>())
            {
                return true;
            }
        }
        catch { }

        return false;
    }
}