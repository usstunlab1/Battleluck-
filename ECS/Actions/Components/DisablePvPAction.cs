using Unity.Entities;

namespace BattleLuck.ECS.Actions.Components;

/// <summary>
/// ECS action component for disabling PvP for a player.
/// Replaces the "disable_pvp" flow action string.
/// </summary>
public struct DisablePvPAction
{
    public Entity TargetEntity;
    public Entity SessionEntity;
}
