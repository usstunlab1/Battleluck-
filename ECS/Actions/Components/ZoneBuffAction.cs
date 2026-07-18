namespace BattleLuck.ECS.Actions.Components;

/// <summary>
/// ECS action component for zone-wide buff application.
/// Replaces "zone.buff.apply" flow action string.
/// </summary>
public struct ZoneBuffApplyAction
{
    public Entity TargetEntity;
    public int ZoneHash;
    public FixedString64Bytes BuffPrefab;
    public float Duration; // -1 = permanent
    public Entity SessionEntity;
}

/// <summary>
/// ECS action component for removing zone-wide buffs.
/// Replaces "zone.buff.remove" flow action string.
/// </summary>
public struct ZoneBuffRemoveAction
{
    public Entity TargetEntity;
    public int ZoneHash;
    public FixedString64Bytes BuffPrefab;
    public Entity SessionEntity;
}

/// <summary>
/// ECS action component for player-targeted buff application.
/// Replaces "player.buff.apply" flow action string.
/// </summary>
public struct PlayerBuffApplyAction
{
    public Entity TargetEntity;
    public FixedString64Bytes BuffPrefab;
    public float Duration; // -1 = permanent
    public int StackCount;
    public Entity SessionEntity;
}

/// <summary>
/// ECS action component for removing player buffs.
/// Replaces "player.buff.remove" flow action string.
/// </summary>
public struct PlayerBuffRemoveAction
{
    public Entity TargetEntity;
    public FixedString64Bytes BuffPrefab;
    public Entity SessionEntity;
}

/// <summary>
/// ECS action component for shrink zone effect.
/// Replaces "shrink.zone" flow action string.
/// </summary>
public struct ShrinkZoneAction
{
    public Entity TargetEntity;
    public int ZoneHash;
    public float TargetRadius;
    public float ShrinkRate;
    public float WarningDuration;
    public Entity SessionEntity;
}

/// <summary>
/// ECS action component for stopping zone shrinkage.
/// Replaces "shrink.stop" flow action string.
/// </summary>
public struct ShrinkZoneStopAction
{
    public Entity TargetEntity;
    public int ZoneHash;
    public Entity SessionEntity;
}
