namespace BattleLuck.Services.AI;

/// <summary>
/// Short-lived registry of "risky/admin" actions that have been PROPOSED to a player and are
/// waiting for an explicit confirmation before they run.
///
/// DEPRECATED: Use <see cref="ActionProposalStore"/> for new code. This class now delegates
/// to ActionProposalStore for proposal management while maintaining backward compatibility
/// for existing callers (<see cref="IntentActionRouter"/>).
///
/// Ownership is enforced: a token can only be confirmed by the same steamId that created it,
/// so confirmation commands can never trigger someone else's admin action.
/// </summary>
public static class IntentActionConfirmRegistry
{
    sealed class Pending
    {
        public ulong SteamId;
        public string Token = string.Empty;
        public string ActionLabel = string.Empty;
        public string Summary = string.Empty;
        public Func<(bool ok, string message)> Execute = () => (false, string.Empty);
        public DateTime ExpiresUtc;
    }

    const int DefaultTtlSeconds = 60;

    static readonly Dictionary<string, Pending> _pending = new();
    static readonly object _lock = new();

    /// <summary>Register a pending action and return its short confirmation token.</summary>
    public static string Register(ulong steamId, string actionLabel, string summary,
        Func<(bool ok, string message)> execute, int ttlSeconds = DefaultTtlSeconds)
    {
        lock (_lock)
        {
            Purge();
            var token = Guid.NewGuid().ToString("N").Substring(0, 6);
            _pending[token] = new Pending
            {
                SteamId = steamId,
                Token = token,
                ActionLabel = actionLabel,
                Summary = summary,
                Execute = execute,
                ExpiresUtc = DateTime.UtcNow.AddSeconds(ttlSeconds),
            };
            return token;
        }
    }

    /// <summary>Whether the player has any pending action (used to gate bare "yes"/"no").</summary>
    public static bool HasPending(ulong steamId)
    {
        // Check both stores
        if (ActionProposalStore.HasPending(steamId))
            return true;

        lock (_lock)
        {
            Purge();
            return _pending.Values.Any(p => p.SteamId == steamId);
        }
    }

    /// <summary>
    /// Confirm and run a pending action owned by <paramref name="steamId"/>. If
    /// <paramref name="token"/> is null/empty the player's most recent pending action is used.
    /// Returns false (with no side effects) when nothing matches.
    /// </summary>
    public static bool TryConfirm(ulong steamId, string? token, out bool ok, out string message)
    {
        ok = false;
        message = string.Empty;

        // Try the new ActionProposalStore first
        if (!string.IsNullOrWhiteSpace(token) && token != "latest" && token.Length <= 8)
        {
            if (ActionProposalStore.TryConfirm(steamId, token!, out var proposal) && proposal != null)
            {
                // Execute the confirmed proposal
                var manifest = ActionManifestService.Instance;
                var character = ResolveCharacter(steamId);
                var session = BattleLuckPlugin.Session?.ActiveSessions.Values.FirstOrDefault(active =>
                    active.Context != null && active.Context.Players.Contains(steamId));

                if (character == Entity.Null || session == null)
                {
                    message = "The bound event session ended. Create a new preview.";
                    return false;
                }

                // Build the action string from the proposal
                var actionParts = new List<string> { proposal.ActionName };
                foreach (var kvp in proposal.Arguments)
                {
                    if (!kvp.Key.Equals("actionName", StringComparison.OrdinalIgnoreCase))
                        actionParts.Add($"{kvp.Key}={kvp.Value}");
                }
                var actionString = string.Join("|", actionParts);

                if (!TryCreateExecutionContext(character, session, out var executor, out var context))
                {
                    message = "The bound event context is no longer ready.";
                    return false;
                }

                var validation = new LlmRuntimeActionValidator(manifest)
                    .ValidateAction(actionString, context, session.Context.SessionId);
                if (!validation.Success)
                {
                    message = validation.Error ?? "Action validation failed after confirmation.";
                    return false;
                }

                var result = executor.ExecuteViaRuntime(actionString, context);
                ok = result.Success;
                message = result.Success
                    ? $"Executed {proposal.ActionName} ({proposal.ResolvedCanonicalName})."
                    : result.Error ?? "Action execution failed.";
                return true;
            }
        }

        // Fall back to legacy pending store
        Pending? pending;
        lock (_lock)
        {
            Purge();
            if (!string.IsNullOrWhiteSpace(token) && token != "latest")
            {
                _pending.TryGetValue(token!.Trim(), out pending);
                if (pending != null && pending.SteamId != steamId)
                    pending = null; // not yours
            }
            else
            {
                pending = _pending.Values
                    .Where(p => p.SteamId == steamId)
                    .OrderByDescending(p => p.ExpiresUtc)
                    .FirstOrDefault();
            }

            if (pending != null)
                _pending.Remove(pending.Token);
        }

        if (pending == null)
        {
            message = "no pending action to confirm";
            return false;
        }

        try
        {
            (ok, message) = pending.Execute();
        }
        catch (Exception ex)
        {
            ok = false;
            message = $"action failed: {ex.Message}";
        }
        return true;
    }

    /// <summary>Drop the player's most recent pending action. Returns its label if one existed.</summary>
    public static bool CancelLatest(ulong steamId, out string label)
    {
        label = string.Empty;

        // Try the new store first
        if (ActionProposalStore.CancelLatest(steamId, out var proposalLabel))
        {
            label = proposalLabel;
            return true;
        }

        lock (_lock)
        {
            Purge();
            var pending = _pending.Values
                .Where(p => p.SteamId == steamId)
                .OrderByDescending(p => p.ExpiresUtc)
                .FirstOrDefault();
            if (pending == null)
                return false;

            label = pending.ActionLabel;
            _pending.Remove(pending.Token);
            return true;
        }
    }

    static void Purge()
    {
        var now = DateTime.UtcNow;
        var expired = _pending.Where(kv => kv.Value.ExpiresUtc < now).Select(kv => kv.Key).ToList();
        foreach (var key in expired)
            _pending.Remove(key);
    }

    static Entity ResolveCharacter(ulong steamId)
    {
        if (!VRisingCore.IsReady)
            return Entity.Null;
        foreach (var player in VRisingCore.GetOnlinePlayers())
        {
            if (player.Exists() && player.IsPlayer() && player.GetSteamId() == steamId)
                return player;
        }
        return Entity.Null;
    }

    static bool TryCreateExecutionContext(
        Entity character,
        ActiveSession session,
        out FlowActionExecutor executor,
        out FlowActionContext context)
    {
        executor = null!;
        context = null!;
        if (!character.Exists() || session.Context == null)
            return false;

        executor = new FlowActionExecutor(
            BattleLuckPlugin.PlayerState ?? new PlayerStateController(),
            BattleLuckPlugin.GameModes);
        context = new FlowActionContext
        {
            PlayerCharacter = character,
            ZoneHash = session.Context.ZoneHash,
            PlayerState = BattleLuckPlugin.PlayerState ?? new PlayerStateController(),
            Registry = BattleLuckPlugin.GameModes,
            Config = session.Config,
            Zone = session.Config.Zones.Zones.FirstOrDefault(item => item.Hash == session.Context.ZoneHash),
            GameContext = session.Context
        };
        return true;
    }
}
