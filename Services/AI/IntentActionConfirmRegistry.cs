namespace BattleLuck.Services.AI;

/// <summary>
/// Short-lived registry of "risky/admin" actions that have been PROPOSED to a player and are
/// waiting for an explicit confirmation before they run.
///
/// <see cref="IntentActionRouter"/> registers a pending action (capturing how to execute it),
/// and the player either confirms (chat "yes" / "confirm &lt;id&gt;") or it quietly expires.
///
/// Ownership is enforced: a token can only be confirmed by the same steamId that created it,
/// so confirmation commands can never trigger someone else's admin action
/// (and admin actions are only ever registered after an admin check at propose time).
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
}
