using BattleLuck.Models;

namespace BattleLuck.Services.Runtime;

/// <summary>
/// Validates and controls reward distribution for adaptive events.
/// All rewards must pass through the active event's reward configuration —
/// the AI planning layer may only select reward profile identifiers, not
/// directly grant items.
/// </summary>
public sealed class EventRewardLimiter
{
    public static EventRewardLimiter Instance { get; } = new();

    // Per-event reward tracking
    readonly Dictionary<string, EventRewardState> _eventRewardState = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Initialize reward tracking for an event session.
    /// </summary>
    public void InitializeSession(string sessionId, RewardBudget budget)
    {
        _eventRewardState[sessionId] = new EventRewardState
        {
            SessionId = sessionId,
            Budget = budget,
            TotalItemsAwarded = 0,
            PlayerItemsAwarded = new Dictionary<ulong, int>()
        };

        BattleLuckPlugin.LogInfo($"[EventRewardLimiter] Initialized session '{sessionId}' with budget: max {budget.MaximumItemsPerPlayer} items/player, {budget.MaximumItemsTotal} total.");
    }

    /// <summary>
    /// Validate whether a reward can be issued to a player.
    /// </summary>
    public RewardValidationResult ValidateReward(
        string sessionId,
        ulong steamId,
        PrefabGUID itemPrefab,
        float itemValue)
    {
        if (!_eventRewardState.TryGetValue(sessionId, out var state))
            return RewardValidationResult.Fail("Session not initialized for rewards.");

        var budget = state.Budget;

        // Check if rewards are allowed at all
        if (budget.MaximumItemsPerPlayer <= 0 || budget.MaximumItemsTotal <= 0)
            return RewardValidationResult.Fail("Rewards are disabled for this event.");

        // Check if item is blocked
        if (budget.BlockedItems.Contains(itemPrefab))
            return RewardValidationResult.Fail($"Item '{itemPrefab.GuidHash}' is blocked by event configuration.");

        // Check if item is allowed (if the allowed set is non-empty)
        if (budget.AllowedItems.Count > 0 && !budget.AllowedItems.Contains(itemPrefab))
            return RewardValidationResult.Fail($"Item '{itemPrefab.GuidHash}' is not in the allowed reward set.");

        // Check per-player limit
        var playerCount = state.PlayerItemsAwarded.TryGetValue(steamId, out var count) ? count : 0;
        if (playerCount >= budget.MaximumItemsPerPlayer)
            return RewardValidationResult.Fail($"Player {steamId} has reached the maximum of {budget.MaximumItemsPerPlayer} items.");

        // Check total event limit
        if (state.TotalItemsAwarded >= budget.MaximumItemsTotal)
            return RewardValidationResult.Fail($"Event has reached the maximum of {budget.MaximumItemsTotal} total items.");

        // Check value per player
        if (itemValue > budget.MaximumValuePerPlayer)
            return RewardValidationResult.Fail($"Item value {itemValue} exceeds per-player maximum of {budget.MaximumValuePerPlayer}.");

        return RewardValidationResult.Ok();
    }

    /// <summary>
    /// Record that a reward was issued (call after successful reward grant).
    /// </summary>
    public void RecordReward(string sessionId, ulong steamId, PrefabGUID itemPrefab)
    {
        if (!_eventRewardState.TryGetValue(sessionId, out var state)) return;

        state.TotalItemsAwarded++;
        if (!state.PlayerItemsAwarded.TryGetValue(steamId, out var count))
            state.PlayerItemsAwarded[steamId] = 1;
        else
            state.PlayerItemsAwarded[steamId] = count + 1;

        BattleLuckLogger.Info($"[EventRewardLimiter] Reward issued: player={steamId} item={itemPrefab.GuidHash} session={sessionId} " +
            $"(player total: {state.PlayerItemsAwarded[steamId]}, event total: {state.TotalItemsAwarded}).");
    }

    /// <summary>
    /// Clean up reward state when an event ends.
    /// </summary>
    public void CleanupSession(string sessionId)
    {
        _eventRewardState.Remove(sessionId);
        BattleLuckPlugin.LogInfo($"[EventRewardLimiter] Cleaned up reward state for session '{sessionId}'.");
    }

    /// <summary>
    /// Get a summary of rewards issued for a session.
    /// </summary>
    public EventRewardSummary? GetSummary(string sessionId)
    {
        if (!_eventRewardState.TryGetValue(sessionId, out var state))
            return null;

        return new EventRewardSummary
        {
            SessionId = sessionId,
            TotalItemsAwarded = state.TotalItemsAwarded,
            PlayersRewarded = state.PlayerItemsAwarded.Count,
            Budget = state.Budget
        };
    }
}

public sealed class EventRewardState
{
    public string SessionId { get; init; } = "";
    public RewardBudget Budget { get; init; } = new();
    public int TotalItemsAwarded { get; set; }
    public Dictionary<ulong, int> PlayerItemsAwarded { get; init; } = new();
}

public sealed class RewardValidationResult
{
    public bool Success { get; init; }
    public string Error { get; init; } = "";

    public static RewardValidationResult Ok() => new() { Success = true };
    public static RewardValidationResult Fail(string error) => new() { Success = false, Error = error };
}

public sealed class EventRewardSummary
{
    public string SessionId { get; init; } = "";
    public int TotalItemsAwarded { get; init; }
    public int PlayersRewarded { get; init; }
    public RewardBudget Budget { get; init; } = new();
}