using Unity.Entities;

namespace BattleLuck.ECS.Actions.Components;

/// <summary>
/// ECS action component for restoring player state snapshots.
/// Replaces the "snapshot.restore" flow action string.
/// </summary>
public struct SnapshotRestoreAction
{
    public Entity TargetEntity;
    public int eventId;
    public Entity SessionEntity;
}
