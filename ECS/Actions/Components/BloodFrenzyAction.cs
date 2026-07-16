using Unity.Entities;

namespace BattleLuck.ECS.Actions.Components;

/// <summary>
/// ECS action component for applying blood frenzy (kill-streak buff).
/// Replaces "gameplay.bloodfrenzy" flow action string.
/// Grants speed/damage multipliers and optional glow VFX.
/// Duration -1 = permanent (until death or exit).
/// </summary>
public struct BloodFrenzyApplyAction
{
    public Entity TargetEntity;
    public int StreakCount;
    public float SpeedMultiplier;
    public float DamageMultiplier;
    public float Duration; // -1 = permanent
    public bool ApplyGlow;
    public Entity SessionEntity;
}

/// <summary>
/// ECS action component for clearing blood frenzy state.
/// Replaces "gameplay.bloodfrenzy.clear" flow action string.
/// </summary>
public struct BloodFrenzyClearAction
{
    public Entity TargetEntity;
    public Entity SessionEntity;
}
