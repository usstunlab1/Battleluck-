using BattleLuck.Core;

namespace BattleLuck.Models;

/// <summary>
/// All trackable player actions across BattleLuck game modes.
/// Used by quest/objective systems and AI analytics.
/// </summary>
public enum ActionType
{
    // ── Universal ────────────────────────────────────────────────────
    Kill,
    Death,
    Assist,
    Survive,

    // ── Bloodbath ────────────────────────────────────────────────────
    LootCrate,
    BossKill,
    EliteKill,
    Elimination,
    BloodFrenzyActivated,
    BloodFrenzyKill,

    // ── Colosseum ────────────────────────────────────────────────────
    DuelWin,
    DuelLoss,
    EloGain,

    // ── Gauntlet ─────────────────────────────────────────────────────
    WaveKill,
    WaveClear,
    WaveSurvive,

    // ── Siege ────────────────────────────────────────────────────────
    ObjectiveCapture,
    ObjectiveDefend,
    TeamWipeRound,

    // ── Trials ───────────────────────────────────────────────────────
    TrialKill,
    ObjectiveComplete,
    TimeBonus,
    SpeedBonus,

    // ── New Actions (Phase 2) ────────────────────────────────────────
    DoorOpen,
    DoorClose,
    DoorLock,
    DoorUnlock,
    BossFollow,
    BossClearFollow,
    TrapPlaced,
    TrapTriggered,
    MountSummoned,
    MountDismissed,
    MountSlowed,
    ZoneBuffApplied,
    ZoneBuffRemoved,
    PlayerBuffApplied,
    PlayerBuffRemoved,
    WallBuilt,
    WallDestroyed,
    FloorPlaced,
    SequencePlayed,
    SequenceStopped,
    GlowEnabled,
    GlowDisabled,
    AutoTeleportTriggered,
    AutoFlyTriggered,
    ReviveLifeUsed,
    ReviveLifeGranted,
    ObjectiveCaptured,
    ObjectiveCompleted,
    ShrinkZoneStarted,
    ShrinkZoneStopped,
    PlayerDowngraded,
    PlayerUpgraded,
    EquipRestricted,
    EquipUnrestricted,
    AutotrashCleared,
    AutotrashSet,
    AIBossAggroSet,
    AIBossDeaggroSet,
    AISetBehaviorSet,
    AISpawnGroupSpawned,
    EntityDamaged,
    EntityHealed,
    TimerStarted,
    TimerStopped,
    ScoreAdded,
    ScoreResetDone,
    NotificationSent,
    ConditionChecked,
    SpatialPointSet,
    SpatialEffectSpawned,
    FactionSetDone,
    FactionCleared,
    DeathPrevented,
    DeathAllowed
}

/// <summary>
/// Extension methods for ActionType enum.
/// </summary>
public static class ActionTypeExtensions
{
    public static string ToConfigString(this ActionType type) => type.ToString();

    public static ActionType FromConfigString(string value) =>
        Enum.TryParse<ActionType>(value, out var result) ? result : ActionType.Kill;
}

/// <summary>
/// Reference fallback SequenceGUID constants for BattleLuck event VFX.
/// They are not target-build verification. Confirm hashes in-game and promote
/// only the confirmed values to config/BattleLuck/sequences/uuid_catalog.json.
/// Loaded from config/action_config.json when present.
/// </summary>
public static class ActionSequences
{
    private static Dictionary<string, Dictionary<string, int>>? _sequences;
    private static Dictionary<string, string>? _actionVFXMapping;
    private static Dictionary<string, SequenceGUID>? _sequenceCache;

    static ActionSequences()
    {
        LoadFromConfig();
    }

    public static void LoadFromConfig()
    {
        try
        {
            var config = ConfigLoader.LoadActionConfig();
            _sequences = config.Sequences;
            _actionVFXMapping = config.ActionVFXMapping;
            _sequenceCache = new();

            if (_sequences != null)
            {
                foreach (var category in _sequences)
                {
                    foreach (var sequence in category.Value)
                    {
                        _sequenceCache[$"{category.Key}.{sequence.Key}"] = new SequenceGUID(sequence.Value);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            BattleLuckPlugin.LogWarning($"[ActionSequences] Failed to load from config: {ex.Message}");
            _sequences = new();
            _actionVFXMapping = new();
            _sequenceCache = new();
        }
    }

    public static void Reload()
    {
        LoadFromConfig();
    }

    // ── Combat ───────────────────────────────────────────────────────
    public static SequenceGUID Kill_Impact => GetSequence("Combat.Kill_Impact", -476819435);
    public static SequenceGUID Death_Dissolve => GetSequence("Combat.Death_Dissolve", 1498540414);
    public static SequenceGUID Assist_Glow => GetSequence("Combat.Assist_Glow", 893118035);
    public static SequenceGUID Elimination_Burst => GetSequence("Combat.Elimination_Burst", -906728229);

    // ── Level / Score ────────────────────────────────────────────────
    public static SequenceGUID LevelUp => GetSequence("Level_Score.LevelUp", -1046001899);
    public static SequenceGUID EloGain_Sparkle => GetSequence("Level_Score.EloGain_Sparkle", 1845986301);
    public static SequenceGUID ScoreFlash => GetSequence("Level_Score.ScoreFlash", 785779005);

    // ── Boss / Elite ─────────────────────────────────────────────────
    public static SequenceGUID BossKill_Explosion => GetSequence("Boss_Elite.BossKill_Explosion", -1790763425);
    public static SequenceGUID BossWounded => GetSequence("Boss_Elite.BossWounded", -868779850);
    public static SequenceGUID EliteKill_Shatter => GetSequence("Boss_Elite.EliteKill_Shatter", -1654901741);
    public static SequenceGUID BossSpawn_Aura => GetSequence("Boss_Elite.BossSpawn_Aura", 1127550179);

    // ── Loot / Pickup ────────────────────────────────────────────────
    public static SequenceGUID LootCrate_Open => GetSequence("Loot_Pickup.LootCrate_Open", 1268037080);
    public static SequenceGUID ItemPickup => GetSequence("Loot_Pickup.ItemPickup", -1430997544);

    // ── Objectives ───────────────────────────────────────────────────
    public static SequenceGUID ObjectiveCapture_Flag => GetSequence("Objectives.ObjectiveCapture_Flag", 744374235);
    public static SequenceGUID ObjectiveDefend_Shield => GetSequence("Objectives.ObjectiveDefend_Shield", 1427675323);
    public static SequenceGUID ObjectiveComplete_Win => GetSequence("Objectives.ObjectiveComplete_Win", 922857755);
    public static SequenceGUID ContestCountdown => GetSequence("Objectives.ContestCountdown", -1142510568);
    public static SequenceGUID ContestBossCountdown => GetSequence("Objectives.ContestBossCountdown", -679471019);

    // ── Wave / Gauntlet ──────────────────────────────────────────────
    public static SequenceGUID WaveKill_Hit => GetSequence("Wave_Gauntlet.WaveKill_Hit", -173616274);
    public static SequenceGUID WaveClear_Pulse => GetSequence("Wave_Gauntlet.WaveClear_Pulse", 2049283058);
    public static SequenceGUID WaveSurvive_Shield => GetSequence("Wave_Gauntlet.WaveSurvive_Shield", -723783303);

    // ── Duel / Colosseum ─────────────────────────────────────────────
    public static SequenceGUID DuelWin_Triumph => GetSequence("Duel_Colosseum.DuelWin_Triumph", -192572431);
    public static SequenceGUID DuelLoss_Fade => GetSequence("Duel_Colosseum.DuelLoss_Fade", -262774549);
    public static SequenceGUID DuelStart_Flash => GetSequence("Duel_Colosseum.DuelStart_Flash", 1382804845);

    // ── Siege ────────────────────────────────────────────────────────
    public static SequenceGUID TeamFlag_01 => GetSequence("Siege.TeamFlag_01", 2136921210);
    public static SequenceGUID TeamFlag_02 => GetSequence("Siege.TeamFlag_02", 617961739);
    public static SequenceGUID TeamFlag_03 => GetSequence("Siege.TeamFlag_03", -146303596);
    public static SequenceGUID TeamFlag_04 => GetSequence("Siege.TeamFlag_04", 478520190);
    public static SequenceGUID TeamWipe_Shake => GetSequence("Siege.TeamWipe_Shake", -648034606);

    // ── Trials / Speed / Time ────────────────────────────────────────
    public static SequenceGUID TimeBonus_Glow => GetSequence("Trials_Speed_Time.TimeBonus_Glow", -217996174);
    public static SequenceGUID SpeedBonus_Dash => GetSequence("Trials_Speed_Time.SpeedBonus_Dash", -207886408);
    public static SequenceGUID TrialKill_Slash => GetSequence("Trials_Speed_Time.TrialKill_Slash", -1765901933);

    // ── Environment / Utility ────────────────────────────────────────
    public static SequenceGUID Teleport_Arrive => GetSequence("Environment_Utility.Teleport_Arrive", -1780773164);
    public static SequenceGUID Waypoint_Active => GetSequence("Environment_Utility.Waypoint_Active", -1096288616);
    public static SequenceGUID Dawn_Warning => GetSequence("Environment_Utility.Dawn_Warning", 392579464);
    public static SequenceGUID Dusk_Signal => GetSequence("Environment_Utility.Dusk_Signal", -1603359499);
    public static SequenceGUID Coffin_Respawn => GetSequence("Environment_Utility.Coffin_Respawn", 1043723895);
    public static SequenceGUID ShrinkZone_Effect => GetSequence("Environment_Utility.ShrinkZone_Effect", -850896030);
    public static SequenceGUID Stun_VFX => GetSequence("Environment_Utility.Stun_VFX", 1972314495);
    public static SequenceGUID Poison_Debuff => GetSequence("Environment_Utility.Poison_Debuff", -174380957);
    public static SequenceGUID BellRing => GetSequence("Environment_Utility.BellRing", 1968734759);

    private static SequenceGUID GetSequence(string key, int fallback)
    {
        if (_sequenceCache != null && _sequenceCache.TryGetValue(key, out var seq))
            return seq;
        return new SequenceGUID(fallback);
    }

    /// <summary>
/// Maps each ActionType to its primary VFX SequenceGUID.
/// </summary>
    public static SequenceGUID GetVFX(ActionType type)
    {
        if (_actionVFXMapping != null && _actionVFXMapping.TryGetValue(type.ToString(), out var sequenceKey))
        {
            // Try to find the sequence in the cache
            foreach (var category in _sequences ?? new())
            {
                if (category.Value.TryGetValue(sequenceKey, out var guid))
                    return new SequenceGUID(guid);
            }
        }
        return Kill_Impact;
    }
 }

/// <summary>
/// Category grouping for display filtering.
/// </summary>
public enum ActionCategory
{
    Combat,
    Objective,
    Survival,
    Bonus
}

/// <summary>
/// Defines display metadata and point values for each action type per mode.
/// Loaded from config/action_config.json.
/// </summary>
public static class ActionRegistry
{
    private static Dictionary<ActionType, ActionInfo>? _actions;
    private static Dictionary<string, HashSet<ActionType>>? _modeActions;

    static ActionRegistry()
    {
        LoadFromConfig();
    }

    public static void LoadFromConfig()
    {
        try
        {
            var config = ConfigLoader.LoadActionConfig();
            _actions = new();
            _modeActions = new(StringComparer.OrdinalIgnoreCase);

            // Load action metadata
            if (config.Actions != null)
            {
                foreach (var kvp in config.Actions)
                {
                    if (Enum.TryParse<ActionType>(kvp.Key, out var actionType))
                    {
                        var info = kvp.Value;
                        if (Enum.TryParse<ActionCategory>(info.Category, out var category))
                        {
                            _actions[actionType] = new ActionInfo(
                                info.Name,
                                category,
                                info.ColoredLabel,
                                info.DefaultPoints
                            );
                        }
                    }
                }
            }

            // Load mode actions
            if (config.ModeActions != null)
            {
                foreach (var kvp in config.ModeActions)
                {
                    var actionSet = new HashSet<ActionType>();
                    foreach (var actionName in kvp.Value)
                    {
                        if (Enum.TryParse<ActionType>(actionName, out var actionType))
                        {
                            actionSet.Add(actionType);
                        }
                    }
                    _modeActions[kvp.Key] = actionSet;
                }
            }
        }
        catch (Exception ex)
        {
            BattleLuckPlugin.LogWarning($"[ActionRegistry] Failed to load from config: {ex.Message}");
            _actions = new();
            _modeActions = new(StringComparer.OrdinalIgnoreCase);
        }
    }

    public static void Reload()
    {
        LoadFromConfig();
    }

    public static Dictionary<ActionType, ActionInfo> Actions => _actions ?? new();

    public static Dictionary<string, HashSet<ActionType>> ModeActions => _modeActions ?? new(StringComparer.OrdinalIgnoreCase);

    public static string GetColoredName(ActionType type) =>
        Actions.TryGetValue(type, out var info) ? info.ColoredLabel : type.ToString();

    public static int GetDefaultPoints(ActionType type) =>
        Actions.TryGetValue(type, out var info) ? info.DefaultPoints : 0;

    public static bool IsValidForMode(string modeId, ActionType type) =>
        ModeActions.TryGetValue(modeId, out var set) && set.Contains(type);

    public static IEnumerable<ActionType> GetActionsForMode(string modeId) =>
        ModeActions.TryGetValue(modeId, out var set) ? set : Enumerable.Empty<ActionType>();
}

public sealed record ActionInfo(string Name, ActionCategory Category, string ColoredLabel, int DefaultPoints);
