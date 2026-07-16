using Unity.Entities;
using Unity.Collections;

namespace BattleLuck.ECS.Events;

/// <summary>
/// ECS event component emitted when an action completes processing.
/// Replaces managed Action<T> delegates for action completion tracking.
/// </summary>
public struct ActionCompletedEvent
{
    public FixedString64Bytes ActionType;
    public Entity TargetEntity;
    public Entity SessionEntity;
    public bool Success;
    public FixedString512Bytes Error;
}
