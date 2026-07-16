using Unity.Entities;
using Unity.Mathematics;
using Unity.Collections;

namespace BattleLuck.ECS.Actions.Components;

/// <summary>
/// ECS action component for controlling event entry.
/// Replaces .toggleenter flow action.
/// </summary>
public struct EventEntryToggleAction
{
    public Entity TargetEntity;
    public bool Enabled;
    public Entity SessionEntity;
}

/// <summary>
/// ECS action component for teleporting teams to specific corner zones.
/// </summary>
public struct TeamCornerTeleportAction
{
    public Entity TargetEntity;
    public int ZoneHash;
    public float Radius;
    public Entity SessionEntity;
}

/// <summary>
/// ECS action component for chest spawning with kill-based unlock logic.
/// </summary>
public struct ChestSpawnAction
{
    public float3 Position;
    public int RequiredKills;
    public int DeathLimit;
    public FixedString64Bytes LootTable;
    public Entity SessionEntity;
}

/// <summary>
/// ECS action component for swapping player/NPC teams.
/// </summary>
public struct TeamSwapAction
{
    public Entity TargetEntity;
    public int NewTeamId;
    public Entity SessionEntity;
}

/// <summary>
/// ECS action component for winner announcement and event cleanup.
/// </summary>
public struct EventFinalizeAction
{
    public Entity SessionEntity;
    public FixedString512Bytes WinnerNames;
    public bool CleanupStructures;
}
