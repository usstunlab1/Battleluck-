using BattleLuck.Models;
using BattleLuck.Services.Npc;

namespace BattleLuck.Services.Drills;

/// <summary>
/// Controls combat drill execution. Evaluates drill rules against player
/// observations and applies NPC reaction modes according to the drill definition.
/// </summary>
public sealed class CombatDrillController
{
    public static CombatDrillController Instance { get; } = new();

    readonly Dictionary<string, DrillState> _activeDrills = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Start a combat drill for a specific NPC session.
    /// </summary>
    public void StartDrill(string npcId, string drillId, CombatDrillDefinition drill)
    {
        _activeDrills[npcId] = new DrillState
        {
            NpcId = npcId,
            DrillId = drillId,
            Drill = drill,
            StartedAt = DateTime.UtcNow,
            RuleCooldowns = new Dictionary<string, DateTime>()
        };

        BattleLuckPlugin.LogInfo($"[CombatDrillController] Started drill '{drillId}' for NPC '{npcId}'.");
    }

    /// <summary>
    /// Stop a drill for an NPC.
    /// </summary>
    public void StopDrill(string npcId)
    {
        _activeDrills.Remove(npcId);
    }

    /// <summary>
    /// Stop all drills for an event session.
    /// </summary>
    public void StopSessionDrills(string sessionId)
    {
        var toRemove = _activeDrills
            .Where(kv => kv.Value.NpcId.StartsWith(sessionId, StringComparison.OrdinalIgnoreCase))
            .Select(kv => kv.Key)
            .ToList();

        foreach (var npcId in toRemove)
            _activeDrills.Remove(npcId);

        if (toRemove.Count > 0)
            BattleLuckPlugin.LogInfo($"[CombatDrillController] Stopped {toRemove.Count} drills for session '{sessionId}'.");
    }

    /// <summary>
    /// Evaluate drill rules for a given NPC observation and return the
    /// recommended reaction mode. Returns null if no drill rule applies.
    /// </summary>
    public AdaptiveNpcMode? EvaluateDrill(
        string npcId,
        PlayerObservation observation,
        AdaptiveNpcMode currentMode)
    {
        if (!_activeDrills.TryGetValue(npcId, out var state))
            return null;

        var drill = state.Drill;
        if (!drill.Enabled || drill.Rules.Count == 0)
            return null;

        var now = DateTime.UtcNow;

        // Find the highest-priority matching rule
        DrillReactionRule? bestRule = null;
        var bestPriority = int.MinValue;

        foreach (var rule in drill.Rules)
        {
            // Check cooldown
            if (state.RuleCooldowns.TryGetValue(rule.Id, out var cooldownUntil) && now < cooldownUntil)
                continue;

            // Check if rule conditions match
            if (!EvaluateConditional(rule.Conditional, observation))
                continue;

            // Check trigger pattern (simplified — matches on trigger type)
            if (!MatchesTrigger(rule.Trigger, observation))
                continue;

            if (rule.Priority > bestPriority)
            {
                bestPriority = rule.Priority;
                bestRule = rule;
            }
        }

        if (bestRule == null)
            return null;

        // Apply cooldown
        state.RuleCooldowns[bestRule.Id] = now.AddSeconds(bestRule.CooldownSeconds);

        // Convert reaction mode string to enum
        var mode = ParseReactionMode(bestRule.ReactionMode);

        BattleLuckPlugin.LogInfo($"[CombatDrillController] Drill '{drill.Id}' rule '{bestRule.Id}' triggered for NPC '{npcId}': {mode}.");

        return mode;
    }

    static bool EvaluateConditional(DrillConditional? conditional, PlayerObservation observation)
    {
        if (conditional == null) return true;

        // Distance checks
        if (conditional.PlayerDistanceMin > 0 && observation.DistanceToNpc < conditional.PlayerDistanceMin)
            return false;
        if (conditional.PlayerDistanceMax < 50f && observation.DistanceToNpc > conditional.PlayerDistanceMax)
            return false;

        // Health checks
        if (conditional.PlayerHealthBelow < 1f && observation.HealthRatio > conditional.PlayerHealthBelow)
            return false;
        if (conditional.PlayerHealthAbove > 0 && observation.HealthRatio < conditional.PlayerHealthAbove)
            return false;

        // Weapon category check
        if (!string.IsNullOrWhiteSpace(conditional.WeaponCategory))
        {
            var weaponStr = conditional.WeaponCategory.ToLowerInvariant();
            var obsWeapon = observation.WeaponCategory.ToString().ToLowerInvariant();
            if (!obsWeapon.Contains(weaponStr))
                return false;
        }

        // State checks
        if (conditional.PlayerIsCasting.HasValue && observation.IsCasting != conditional.PlayerIsCasting.Value)
            return false;
        if (conditional.PlayerIsDashing.HasValue && observation.IsDashing != conditional.PlayerIsDashing.Value)
            return false;

        return true;
    }

    static bool MatchesTrigger(string trigger, PlayerObservation observation)
    {
        if (string.IsNullOrWhiteSpace(trigger) || trigger.Equals("*"))
            return true;

        return trigger.ToLowerInvariant() switch
        {
            "player_approaching" => observation.IsMovingTowardNpc && observation.DistanceToNpc < 10f,
            "player_retreating" => observation.IsMovingAwayFromNpc && observation.DistanceToNpc > 8f,
            "player_casting" => observation.IsCasting,
            "player_dashing" => observation.IsDashing,
            "player_low_health" => observation.HealthRatio < 0.3f,
            "player_ranged" => observation.WeaponCategory == WeaponCategory.Ranged,
            "player_melee" => observation.WeaponCategory == WeaponCategory.Melee,
            "player_magic" => observation.WeaponCategory == WeaponCategory.Magic,
            "player_combat" => observation.IsInCombat,
            "player_close" => observation.DistanceToNpc < 5f,
            "player_far" => observation.DistanceToNpc > 15f,
            _ => false
        };
    }

    static AdaptiveNpcMode ParseReactionMode(string mode)
    {
        return mode.ToLowerInvariant() switch
        {
            "attack" => AdaptiveNpcMode.Attack,
            "chase" => AdaptiveNpcMode.Chase,
            "evade" => AdaptiveNpcMode.Evade,
            "flank" => AdaptiveNpcMode.Flank,
            "keep_distance" or "keepdistance" => AdaptiveNpcMode.KeepDistance,
            "retreat" => AdaptiveNpcMode.Retreat,
            "counter" => AdaptiveNpcMode.Attack,
            "hold" or "hold_position" or "holdposition" => AdaptiveNpcMode.HoldPosition,
            "follow" => AdaptiveNpcMode.Follow,
            "offset_follow" or "offsetfollow" => AdaptiveNpcMode.OffsetFollow,
            _ => AdaptiveNpcMode.Follow
        };
    }

    class DrillState
    {
        public string NpcId { get; init; } = "";
        public string DrillId { get; init; } = "";
        public CombatDrillDefinition Drill { get; init; } = new();
        public DateTime StartedAt { get; init; }
        public Dictionary<string, DateTime> RuleCooldowns { get; init; } = new();
    }
}