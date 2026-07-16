using Unity.Entities;

namespace BattleLuck.ECS.Actions.Components;

/// <summary>
/// ECS action component for healing players to full health.
/// Replaces the "heal" flow action string.
/// </summary>
public struct HealAction
{
    public Entity TargetEntity;
    public Entity SessionEntity;
}
