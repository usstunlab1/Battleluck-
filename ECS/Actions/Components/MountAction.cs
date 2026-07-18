namespace BattleLuck.ECS.Actions.Components;

/// <summary>
/// ECS action component for summoning mounts.
/// Replaces "mount.summon" flow action string.
/// MountType: horse, bat, wolf
/// </summary>
public struct MountSummonAction
{
    public Entity TargetEntity;
    public FixedString64Bytes MountType;
    public float3 Position;
    public Entity SessionEntity;
}

/// <summary>
/// ECS action component for dismissing mounts.
/// Replaces "mount.dismiss" flow action string.
/// </summary>
public struct MountDismissAction
{
    public Entity TargetEntity;
    public Entity SessionEntity;
}

/// <summary>
/// ECS action component for mount slowdown in zones.
/// Replaces "mount.slowdown" flow action string.
/// Condition: killed_low_level, specific_zone, low_health, pvp_active
/// </summary>
public struct MountSlowdownAction
{
    public Entity TargetEntity;
    public int ZoneHash;
    public float SlowMultiplier; // 0.0 = full stop, 0.5 = half speed
    public FixedString64Bytes Condition;
    public Entity SessionEntity;
}