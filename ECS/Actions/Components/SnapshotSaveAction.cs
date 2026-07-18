namespace BattleLuck.ECS.Actions.Components;

/// <summary>
/// ECS action component for saving player state snapshots.
/// Replaces the "snapshot.save" flow action string.
/// </summary>
public struct SnapshotSaveAction
{
    public Entity TargetEntity;
    public int EventId;
    public Entity SessionEntity;
}
