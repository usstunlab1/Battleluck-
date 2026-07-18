namespace BattleLuck.ECS.Actions.Components;

/// <summary>
/// ECS action component for playing visual sequences/VFX.
/// Replaces "sequence.play" flow action string.
/// Duration -1 = permanent/lifetime (until exit or death).
/// </summary>
public struct SequencePlayAction
{
    public Entity TargetEntity;
    public FixedString64Bytes SequencePrefab;
    public float3 Position;
    public float Duration; // -1 = permanent
    public bool AttachToPlayer;
    public Entity AttachToEntity;
    public Entity SessionEntity;
}

/// <summary>
/// ECS action component for stopping visual sequences.
/// Replaces "sequence.stop" flow action string.
/// </summary>
public struct SequenceStopAction
{
    public Entity TargetEntity;
    public FixedString64Bytes SequencePrefab;
    public float3 Position;
    public Entity Target; // Entity to stop sequence from
    public Entity SessionEntity;
}

/// <summary>
/// ECS action component for persistent sequences (survive zone changes).
/// Replaces "sequence.persist" flow action string.
/// Duration -1 = until exit/death.
/// </summary>
public struct SequencePersistAction
{
    public Entity TargetEntity;
    public FixedString64Bytes SequencePrefab;
    public float3 Position;
    public float Duration; // -1 = until exit/death
    public Entity SessionEntity;
}

/// <summary>
/// ECS action component for enabling glow effects.
/// Replaces "glow.enable" flow action string.
/// Duration -1 = permanent.
/// </summary>
public struct GlowEnableAction
{
    public Entity TargetEntity;
    public Entity Target; // Entity to apply glow to
    public FixedString64Bytes Color;
    public float Radius;
    public float Duration; // -1 = permanent
    public Entity SessionEntity;
}

/// <summary>
/// ECS action component for disabling glow effects.
/// Replaces "glow.disable" flow action string.
/// </summary>
public struct GlowDisableAction
{
    public Entity TargetEntity;
    public Entity Target; // Entity to remove glow from
    public Entity SessionEntity;
}

/// <summary>
/// ECS action component for auto-teleport functionality.
/// Replaces "auto.teleport" flow action string.
/// Condition: death, zone_enter, low_health, timer
/// </summary>
public struct AutoTeleportAction
{
    public Entity TargetEntity;
    public int TargetZoneHash;
    public FixedString64Bytes Condition;
    public float Delay;
    public Entity SessionEntity;
}

/// <summary>
/// ECS action component for auto-fly functionality.
/// Replaces "auto.fly" flow action string.
/// Condition: zone_enter, low_health, timer
/// </summary>
public struct AutoFlyAction
{
    public Entity TargetEntity;
    public float3 TargetPosition;
    public float Speed;
    public FixedString64Bytes Condition;
    public Entity SessionEntity;
}