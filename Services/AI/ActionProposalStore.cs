using System.Collections.Concurrent;

namespace BattleLuck.Services.AI;

/// <summary>
/// Immutable proposal store for the BattleLuck action confirmation lifecycle.
///
/// Every proposal captures the original request, resolved target, action, and
/// arguments at preview time. Confirmation uses a unique proposal ID so there
/// is no ambiguity about which proposal is being confirmed.
///
/// Rules:
/// - A new valid request replaces the caller's older unconfirmed proposal.
/// - A failed or ambiguous request does not preserve an unrelated proposal.
/// - Confirmation matches only the exact proposal ID.
/// - After execution, failure, cancellation, timeout, disconnect, or .ai end,
///   the proposal is cleared.
/// - Preview and execution use the same immutable proposal.
/// - Proposal TTL is 60 seconds.
/// </summary>
public static class ActionProposalStore
{
    public sealed class ActionProposal
    {
        public string ProposalId { get; init; } = "";
        public ulong SteamId { get; init; }
        public string OriginalRequest { get; init; } = "";
        public string ActionName { get; init; } = "";
        public string ResolvedCategory { get; init; } = "";
        public string ResolvedCanonicalName { get; init; } = "";
        public int ResolvedPrefabGuid { get; init; }
        public Dictionary<string, string> Arguments { get; init; } = new(StringComparer.OrdinalIgnoreCase);
        public DateTime CreatedAtUtc { get; init; }
        public DateTime ExpiresAtUtc { get; init; }
        public bool RequiresApproval { get; init; }
    }

    const int DefaultTtlSeconds = 60;

    static readonly ConcurrentDictionary<string, ActionProposal> _proposals = new(StringComparer.OrdinalIgnoreCase);
    static readonly ConcurrentDictionary<ulong, string> _playerLatest = new();

    /// <summary>
    /// Create a new proposal for the given player. Replaces any older unconfirmed
    /// proposal for the same player.
    /// </summary>
    public static ActionProposal CreateProposal(ulong steamId, string originalRequest, string actionName,
        string resolvedCategory, string resolvedCanonicalName, int resolvedPrefabGuid,
        Dictionary<string, string> arguments, bool requiresApproval, int ttlSeconds = DefaultTtlSeconds)
    {
        // Clear any existing proposal for this player
        if (_playerLatest.TryRemove(steamId, out var oldId))
            _proposals.TryRemove(oldId, out _);

        var now = DateTime.UtcNow;
        var proposal = new ActionProposal
        {
            ProposalId = Guid.NewGuid().ToString("N")[..6],
            SteamId = steamId,
            OriginalRequest = originalRequest,
            ActionName = actionName,
            ResolvedCategory = resolvedCategory,
            ResolvedCanonicalName = resolvedCanonicalName,
            ResolvedPrefabGuid = resolvedPrefabGuid,
            Arguments = new Dictionary<string, string>(arguments, StringComparer.OrdinalIgnoreCase),
            CreatedAtUtc = now,
            ExpiresAtUtc = now.AddSeconds(ttlSeconds),
            RequiresApproval = requiresApproval
        };

        _proposals[proposal.ProposalId] = proposal;
        _playerLatest[steamId] = proposal.ProposalId;

        BattleLuckPlugin.LogInfo(
            $"[AIProposal] created proposalId={proposal.ProposalId} steamId={steamId} " +
            $"action={actionName} category={resolvedCategory} target={resolvedCanonicalName} " +
            $"originalRequest='{originalRequest}'");

        return proposal;
    }

    /// <summary>
    /// Try to confirm a proposal by its exact ID. Returns false if the proposal
    /// doesn't exist, expired, or belongs to another player.
    /// </summary>
    public static bool TryConfirm(ulong steamId, string proposalId, out ActionProposal? proposal)
    {
        proposal = null;
        Purge();

        if (string.IsNullOrWhiteSpace(proposalId))
            return false;

        if (!_proposals.TryGetValue(proposalId.Trim(), out var found))
        {
            BattleLuckPlugin.LogInfo($"[AIProposal] confirm failed: proposalId={proposalId} not found");
            return false;
        }

        if (found.SteamId != steamId)
        {
            BattleLuckPlugin.LogInfo($"[AIProposal] confirm failed: proposalId={proposalId} owned by different player");
            return false;
        }

        if (found.ExpiresAtUtc < DateTime.UtcNow)
        {
            _proposals.TryRemove(proposalId, out _);
            _playerLatest.TryRemove(new KeyValuePair<ulong, string>(steamId, proposalId));
            BattleLuckPlugin.LogInfo($"[AIProposal] confirm failed: proposalId={proposalId} expired");
            return false;
        }

        proposal = found;
        // Remove the proposal so it can't be confirmed twice
        _proposals.TryRemove(proposalId, out _);
        _playerLatest.TryRemove(new KeyValuePair<ulong, string>(steamId, proposalId));

        BattleLuckPlugin.LogInfo($"[AIProposal] confirmed proposalId={proposalId} action={proposal.ActionName} target={proposal.ResolvedCanonicalName}");
        return true;
    }

    /// <summary>
    /// Get the latest proposal for a player (for bare "yes" without an ID).
    /// </summary>
    public static ActionProposal? GetLatest(ulong steamId)
    {
        Purge();
        if (_playerLatest.TryGetValue(steamId, out var proposalId) &&
            _proposals.TryGetValue(proposalId, out var proposal))
        {
            return proposal;
        }
        return null;
    }

    /// <summary>
    /// Cancel the latest proposal for a player. Returns the action label if one existed.
    /// </summary>
    public static bool CancelLatest(ulong steamId, out string actionLabel)
    {
        actionLabel = string.Empty;
        Purge();

        if (!_playerLatest.TryRemove(steamId, out var proposalId))
            return false;

        if (_proposals.TryRemove(proposalId, out var proposal))
        {
            actionLabel = proposal.ActionName;
            BattleLuckPlugin.LogInfo($"[AIProposal] cancelled proposalId={proposalId} steamId={steamId}");
            return true;
        }

        return false;
    }

    /// <summary>
    /// Whether the player has any pending proposal.
    /// </summary>
    public static bool HasPending(ulong steamId)
    {
        Purge();
        return _playerLatest.TryGetValue(steamId, out var proposalId) &&
               _proposals.ContainsKey(proposalId);
    }

    /// <summary>
    /// Clear all proposals for a player (e.g. on disconnect or .ai end).
    /// </summary>
    public static void ClearPlayer(ulong steamId)
    {
        if (_playerLatest.TryRemove(steamId, out var proposalId))
            _proposals.TryRemove(proposalId, out _);
    }

    static void Purge()
    {
        var now = DateTime.UtcNow;
        foreach (var kvp in _proposals)
        {
            if (kvp.Value.ExpiresAtUtc < now)
            {
                _proposals.TryRemove(kvp.Key, out _);
                _playerLatest.TryRemove(new KeyValuePair<ulong, string>(kvp.Value.SteamId, kvp.Key));
            }
        }
    }
}