using Unity.Entities;
using Unity.Collections;

namespace BattleLuck.ECS.Actions.Components;

/// <summary>
/// ECS action component for starting a game mode.
/// Replaces the "mode.start" flow action string.
/// </summary>
public struct ModeStartAction
{
    public FixedString64Bytes ModeId;
    public Entity SessionEntity;
}
