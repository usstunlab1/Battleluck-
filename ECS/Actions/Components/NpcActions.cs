using Unity.Entities;
using Unity.Mathematics;

namespace BattleLuck.ECS.Actions.Components;

/// <summary>
/// ECS action component for spawning NPCs via NpcControlService.
/// Replaces "npc.spawn" flow action string.
/// </summary>
public struct NpcSpawnAction
{
    public Entity TargetEntity;
    public FixedString64Bytes PrefabName;
    public float3 Position;
    public int Count;
    public FixedString64Bytes Formation;
    public float HomeRadius;
    public Entity SessionEntity;
}

/// <summary>
/// ECS action component for despawning NPCs via NpcControlService.
/// Replaces "npc.despawn" flow action string.
/// </summary>
public struct NpcDespawnAction
{
    public Entity TargetEntity;
    public FixedString64Bytes NpcId;
    public Entity SessionEntity;
}

/// <summary>
/// ECS action component for NPC follow behavior via NpcControlService.
/// Replaces "npc.follow" flow action string.
/// </summary>
public struct NpcFollowAction
{
    public Entity TargetEntity;
    public Entity FollowTarget;
    public float FollowRange;
    public float LeashRange;
    public Entity SessionEntity;
}

/// <summary>
/// ECS action component for NPC hold position via NpcControlService.
/// Replaces "npc.hold" flow action string.
/// </summary>
public struct NpcHoldAction
{
    public Entity TargetEntity;
    public float Radius;
    public Entity SessionEntity;
}

/// <summary>
/// ECS action component for NPC goto position via NpcControlService.
/// Replaces "npc.goto" flow action string.
/// </summary>
public struct NpcGotoAction
{
    public Entity TargetEntity;
    public float3 TargetPosition;
    public float ArrivalRange;
    public Entity SessionEntity;
}

/// <summary>
/// ECS action component for NPC aggro via NpcControlService.
/// Replaces "npc.aggro" flow action string.
/// </summary>
public struct NpcAggroAction
{
    public Entity TargetEntity;
    public Entity AggroTarget;
    public float AggroRange;
    public float LeashRange;
    public Entity SessionEntity;
}

/// <summary>
/// ECS action component for NPC release via NpcControlService.
/// Replaces "npc.release" flow action string.
/// </summary>
public struct NpcReleaseAction
{
    public Entity TargetEntity;
    public FixedString64Bytes NpcId;
    public Entity SessionEntity;
}

/// <summary>
/// ECS action component for NPC equip via NpcControlService.
/// Replaces "npc.equip" flow action string.
/// </summary>
public struct NpcEquipAction
{
    public Entity TargetEntity;
    public FixedString64Bytes NpcId;
    public FixedString64Bytes EquipmentPrefab;
    public Entity SessionEntity;
}

/// <summary>
/// ECS action component for NPC unequip via NpcControlService.
/// Replaces "npc.unequip" flow action string.
/// </summary>
public struct NpcUnequipAction
{
    public Entity TargetEntity;
    public FixedString64Bytes NpcId;
    public FixedString64Bytes EquipmentPrefab;
    public Entity SessionEntity;
}