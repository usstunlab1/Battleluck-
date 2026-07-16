using Unity.Entities;

namespace BattleLuck.ECS.Actions.Components;

/// <summary>
/// ECS action component for clearing all buffs from a player.
/// Replaces the "buff.clear_all" flow action string.
/// </summary>
public struct BuffClearAction
{
    public Entity TargetEntity;
    public Entity SessionEntity;
}
