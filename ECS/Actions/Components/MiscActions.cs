namespace BattleLuck.ECS.Actions.Components;

/// <summary>
/// ECS action component for player upgrade/downgrade.
/// Replaces "player.upgrade" and "player.downgrade" flow action strings.
/// </summary>
public struct PlayerDowngradeAction
{
    public Entity TargetEntity;
    public int Level;
    public int GearLevel;
    public Entity SessionEntity;
}

public struct PlayerUpgradeAction
{
    public Entity TargetEntity;
    public int Level;
    public int GearLevel;
    public Entity SessionEntity;
}

/// <summary>
/// ECS action component for equipment restriction.
/// Replaces "equip.restrict" and "equip.unrestrict" flow action strings.
/// </summary>
public struct EquipRestrictAction
{
    public Entity TargetEntity;
    public int MaxGearLevel;
    public Entity SessionEntity;
}

public struct EquipUnrestrictAction
{
    public Entity TargetEntity;
    public Entity SessionEntity;
}

/// <summary>
/// ECS action component for autotrash operations.
/// Replaces "autotrash.clear" and "autotrash.set" flow action strings.
/// </summary>
public struct AutotrashClearAction
{
    public Entity TargetEntity;
    public int ZoneHash;
    public Entity SessionEntity;
}

public struct AutotrashSetAction
{
    public Entity TargetEntity;
    public int ZoneHash;
    public FixedString512Bytes Items; // Comma-separated item prefab names
    public Entity SessionEntity;
}

/// <summary>
/// ECS action component for entity damage/heal.
/// Replaces "entity.damage" and "entity.heal" flow action strings.
/// </summary>
public struct EntityDamageAction
{
    public Entity TargetEntity;
    public Entity DamageTarget;
    public float Damage;
    public FixedString64Bytes DamageType; // Physical, Spell, Fire, Poison
    public Entity SessionEntity;
}

public struct EntityHealAction
{
    public Entity TargetEntity;
    public Entity HealTarget;
    public float HealAmount;
    public Entity SessionEntity;
}

/// <summary>
/// ECS action component for timer operations.
/// Replaces "timer.start" and "timer.stop" flow action strings.
/// </summary>
public struct TimerStartAction
{
    public Entity TargetEntity;
    public FixedString64Bytes TimerId;
    public float Duration;
    public FixedString128Bytes OnComplete; // Action flow to execute on timer complete
    public Entity SessionEntity;
}

public struct TimerStopAction
{
    public Entity TargetEntity;
    public FixedString64Bytes TimerId;
    public Entity SessionEntity;
}

/// <summary>
/// ECS action component for score operations.
/// Replaces "score.add" and "score.reset" flow action strings.
/// </summary>
public struct ScoreAddAction
{
    public Entity TargetEntity;
    public int Points;
    public FixedString128Bytes Reason;
    public Entity SessionEntity;
}

public struct ScoreResetAction
{
    public Entity TargetEntity;
    public Entity SessionEntity;
}

/// <summary>
/// ECS action component for notifications.
/// Replaces "notification" flow action string.
/// Type: info, warning, success, error
/// </summary>
public struct NotificationAction
{
    public Entity TargetEntity;
    public FixedString512Bytes Message;
    public FixedString64Bytes Type;
    public float Duration;
    public Entity SessionEntity;
}

/// <summary>
/// ECS action component for condition checking.
/// Replaces "condition.check" flow action string.
/// </summary>
public struct ConditionCheckAction
{
    public Entity TargetEntity;
    public FixedString128Bytes Condition;
    public FixedString128Bytes OnTrue;
    public FixedString128Bytes OnFalse;
    public Entity SessionEntity;
}

/// <summary>
/// ECS action component for named spatial points used by effects and borders.
/// Spatial points are never teleport destinations.
/// </summary>
public struct SpatialPointSetAction
{
    public Entity TargetEntity;
    public FixedString64Bytes PointId;
    public float3 Position;
    public int ZoneHash;
    public Entity SessionEntity;
}

/// <summary>
/// ECS action component for faction/team operations.
/// Replaces "faction.set" and "faction.clear" flow action strings.
/// </summary>
public struct FactionSetAction
{
    public Entity TargetEntity;
    public FixedString64Bytes FactionId;
    public int TeamId;
    public Entity SessionEntity;
}

public struct FactionClearAction
{
    public Entity TargetEntity;
    public Entity SessionEntity;
}

/// <summary>
/// ECS action component for death prevention.
/// Replaces "death.prevent" and "death.allow" flow action strings.
/// </summary>
public struct DeathPreventAction
{
    public Entity TargetEntity;
    public int InitialCharges;
    public float ActiveWindowSeconds;
    public float TriggerCooldownSeconds;
    public FixedString64Bytes OnTriggeredSequenceId;
    public Entity SessionEntity;
}

public struct DeathAllowAction
{
    public Entity TargetEntity;
    public Entity SessionEntity;
}
