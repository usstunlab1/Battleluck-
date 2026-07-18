namespace BattleLuck.ECS.Actions.Components;

/// <summary>
/// ECS action component for granting revive lives.
/// Replaces "revive.grant" flow action string.
/// </summary>
public struct ReviveGrantAction
{
    public Entity TargetEntity;
    public int MaxLives;
    public Entity SessionEntity;
}

/// <summary>
/// ECS action component for consuming a revive life (auto-respawn).
/// Replaces "revive.consume" flow action string.
/// </summary>
public struct ReviveConsumeAction
{
    public Entity TargetEntity;
    public int Count;
    public Entity SessionEntity;
}

/// <summary>
/// ECS action component for resetting revive lives.
/// Replaces "revive.reset" flow action string.
/// </summary>
public struct ReviveResetAction
{
    public Entity TargetEntity;
    public Entity SessionEntity;
}