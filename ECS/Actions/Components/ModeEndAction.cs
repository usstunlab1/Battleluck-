namespace BattleLuck.ECS.Actions.Components;

/// <summary>
/// ECS action component for ending a game mode.
/// Replaces the "mode.end" flow action string.
/// </summary>
public struct ModeEndAction
{
    public FixedString64Bytes ModeId;
    public Entity SessionEntity;
}
