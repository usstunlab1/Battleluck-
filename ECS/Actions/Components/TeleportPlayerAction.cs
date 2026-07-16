using Unity.Entities;
using Unity.Mathematics;

namespace BattleLuck.ECS.Actions.Components;

/// <summary>
/// ECS action component for teleporting players.
/// Replaces the "teleport" and "player.teleport" flow action strings.
/// </summary>
public struct TeleportPlayerAction
{
    public Entity TargetEntity;
    public float3 Position;
    public int TargetZoneHash;
    public Entity SessionEntity;
}
