namespace BattleLuck.ECS.Actions.Components;

/// <summary>
/// ECS action component for objective capture.
/// Replaces "objective.capture" flow action string.
/// </summary>
public struct ObjectiveCaptureAction
{
    public Entity TargetEntity;
    public FixedString64Bytes ObjectiveId;
    public int ZoneHash;
    public float CaptureTime;
    public int TeamId;
    public Entity SessionEntity;
}

/// <summary>
/// ECS action component for objective completion.
/// Replaces "objective.complete" flow action string.
/// </summary>
public struct ObjectiveCompleteAction
{
    public Entity TargetEntity;
    public FixedString64Bytes ObjectiveId;
    public int RewardPoints;
    public Entity SessionEntity;
}

/// <summary>
/// ECS action component for objective reset.
/// Replaces "objective.reset" flow action string.
/// </summary>
public struct ObjectiveResetAction
{
    public Entity TargetEntity;
    public FixedString64Bytes ObjectiveId;
    public Entity SessionEntity;
}