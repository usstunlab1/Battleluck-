using BattleLuck.Models;

namespace BattleLuck.Services.Npc;

/// <summary>
/// Recognizes meaningful player action patterns from server-visible observation data.
/// Does NOT attempt to reproduce player-only input systems or client-side animations.
/// Instead, it identifies behavioral signatures from ECS state changes.
/// </summary>
public sealed class PlayerPatternRecognizer
{
    public static PlayerPatternRecognizer Instance { get; } = new();

    // Pattern detection thresholds
    const float LowHealthThreshold = 0.3f;
    const float MediumHealthThreshold = 0.6f;
    const float ChargeDistanceThreshold = 8f;
    const float RetreatDistanceThreshold = 15f;

    /// <summary>
    /// Evaluate a player observation and return the recognized pattern.
    /// </summary>
    public RecognizedPattern Evaluate(PlayerObservation observation, AdaptiveNpcMode currentNpcMode)
    {
        // Player is at very low health
        if (observation.HealthRatio < LowHealthThreshold)
        {
            if (observation.IsMovingAwayFromNpc)
                return RecognizedPattern.RetreatingLowHealth;
            if (observation.IsMovingTowardNpc)
                return RecognizedPattern.DesperateAttack;
            return RecognizedPattern.LowHealth;
        }

        // Player is casting (ranged/magic attack incoming)
        if (observation.IsCasting)
        {
            if (observation.DistanceToNpc > ChargeDistanceThreshold)
                return RecognizedPattern.RangedCasting;
            return RecognizedPattern.CloseQuartersCasting;
        }

        // Player is dashing
        if (observation.IsDashing)
        {
            if (observation.IsMovingTowardNpc)
                return RecognizedPattern.DashApproach;
            if (observation.IsMovingAwayFromNpc)
                return RecognizedPattern.DashRetreat;
            return RecognizedPattern.Dashing;
        }

        // Player movement relative to NPC
        if (observation.IsMovingTowardNpc && observation.DistanceToNpc < ChargeDistanceThreshold)
            return RecognizedPattern.MeleeApproach;

        if (observation.IsMovingAwayFromNpc && observation.DistanceToNpc > RetreatDistanceThreshold)
            return RecognizedPattern.Retreating;

        // Weapon-based patterns
        if (observation.WeaponCategory == WeaponCategory.Ranged && observation.DistanceToNpc > ChargeDistanceThreshold)
            return RecognizedPattern.RangedHarass;

        if (observation.WeaponCategory == WeaponCategory.Magic && observation.DistanceToNpc > ChargeDistanceThreshold)
            return RecognizedPattern.MagicHarass;

        // Player is in combat
        if (observation.IsInCombat)
        {
            if (observation.DistanceToNpc < 5f)
                return RecognizedPattern.CloseCombat;
            return RecognizedPattern.CombatEngaged;
        }

        // Player is idle or moving without combat intent
        if (observation.DistanceToNpc > RetreatDistanceThreshold)
            return RecognizedPattern.Distant;

        return RecognizedPattern.Neutral;
    }

    /// <summary>
    /// Determine the appropriate NPC reaction mode for a recognized pattern.
    /// </summary>
    public AdaptiveNpcMode DetermineReaction(RecognizedPattern pattern, WeaponCategory npcWeaponCategory)
    {
        return pattern switch
        {
            // Player is vulnerable — apply pressure
            RecognizedPattern.RetreatingLowHealth => AdaptiveNpcMode.Chase,
            RecognizedPattern.LowHealth => AdaptiveNpcMode.Attack,
            RecognizedPattern.DesperateAttack => AdaptiveNpcMode.KeepDistance,

            // Player is casting — counter or evade
            RecognizedPattern.RangedCasting => npcWeaponCategory == WeaponCategory.Ranged
                ? AdaptiveNpcMode.Attack
                : AdaptiveNpcMode.Evade,
            RecognizedPattern.CloseQuartersCasting => AdaptiveNpcMode.Attack,
            RecognizedPattern.MagicHarass => AdaptiveNpcMode.Flank,

            // Player is dashing — prepare to counter
            RecognizedPattern.DashApproach => AdaptiveNpcMode.Evade,
            RecognizedPattern.DashRetreat => AdaptiveNpcMode.Chase,

            // Player is approaching — engage or keep distance
            RecognizedPattern.MeleeApproach => npcWeaponCategory == WeaponCategory.Melee
                ? AdaptiveNpcMode.Attack
                : AdaptiveNpcMode.KeepDistance,
            RecognizedPattern.RangedHarass => AdaptiveNpcMode.Chase,
            RecognizedPattern.Retreating => AdaptiveNpcMode.Chase,

            // Player is fighting — respond in kind
            RecognizedPattern.CloseCombat => AdaptiveNpcMode.Attack,
            RecognizedPattern.CombatEngaged => AdaptiveNpcMode.Attack,

            // Player is far away — hold or approach
            RecognizedPattern.Distant => AdaptiveNpcMode.Follow,
            RecognizedPattern.Neutral => AdaptiveNpcMode.Follow,

            _ => AdaptiveNpcMode.Follow
        };
    }
}

/// <summary>
/// Recognized player behavior patterns from server-visible observation data.
/// </summary>
public enum RecognizedPattern
{
    Neutral,
    Distant,
    LowHealth,
    RetreatingLowHealth,
    DesperateAttack,
    RangedCasting,
    CloseQuartersCasting,
    DashApproach,
    DashRetreat,
    Dashing,
    MeleeApproach,
    Retreating,
    RangedHarass,
    MagicHarass,
    CloseCombat,
    CombatEngaged
}