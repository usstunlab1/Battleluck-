namespace BattleLuck.ECS.Actions.Components;

/// <summary>
/// ECS action component for applying kit loadouts to players.
/// Replaces the "kit.apply" flow action string.
/// </summary>
public struct KitApplyAction
{
    public Entity TargetEntity;
    public FixedString64Bytes KitId;
    public Entity SessionEntity;
}
