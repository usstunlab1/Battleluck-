using Unity.Entities;

namespace BattleLuck.ECS.Actions.Components;

/// <summary>
/// ECS action component for stunning a player for a duration.
/// Replaces the "player.stun" flow action string.
/// </summary>
public struct PlayerStunAction
{
    public Entity TargetEntity;
    public float DurationSeconds;
    public Entity SessionEntity;
}
