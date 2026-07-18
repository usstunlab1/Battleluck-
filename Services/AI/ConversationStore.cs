namespace BattleLuck.Services.AI;

/// <summary>Who authored a conversation turn.</summary>
public enum ConversationSpeaker
{
    Player,
    Admin,
    Ai,
    System
}

/// <summary>A single turn in the shared conversation log.</summary>
public sealed class ConversationTurn
{
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
    public ConversationSpeaker Speaker { get; init; }
    public string DisplayName { get; init; } = "";
    public ulong SteamId { get; init; }
    public string Text { get; init; } = "";
    public List<string> ActionResults { get; init; } = new();
}

/// <summary>
/// Single shared conversation log that humans and the AI both read and write.
/// This is the "same chat we can all read": every human message, AI reply, and
/// executed action outcome is appended here, and recent turns are fed back to the
/// LLM as context so it has conversation history.
/// </summary>
public sealed class ConversationStore
{
    public static ConversationStore Instance { get; } = new();

    /// <summary>Interactive AI sessions allow four assistant replies by default.</summary>
    public const int InteractiveReplyLimit = 4;

    /// <summary>Chat history exposed by .ai history is transient and one day old at most.</summary>
    public static readonly TimeSpan HistoryRetention = TimeSpan.FromDays(1);

    readonly object _lock = new();
    readonly List<ConversationTurn> _turns = new();
    readonly Dictionary<ulong, InteractiveSession> _interactiveSessions = new();
    readonly int _maxTurns;

    public ConversationStore(int maxTurns = 200)
    {
        _maxTurns = maxTurns;
    }

    public void Append(ConversationTurn turn)
    {
        lock (_lock)
        {
            PruneExpiredUnsafe(DateTime.UtcNow);
            _turns.Add(turn);
            if (_turns.Count > _maxTurns)
                _turns.RemoveRange(0, _turns.Count - _maxTurns);
        }
    }

    public IReadOnlyList<ConversationTurn> Recent(int n)
    {
        lock (_lock)
        {
            PruneExpiredUnsafe(DateTime.UtcNow);
            var start = Math.Max(0, _turns.Count - n);
            return _turns.GetRange(start, _turns.Count - start);
        }
    }

    /// <summary>Return recent history within the one-day retention window.</summary>
    public IReadOnlyList<ConversationTurn> RecentWithin(TimeSpan age, int max, ulong? steamId = null)
    {
        var cutoff = DateTime.UtcNow - (age <= TimeSpan.Zero ? HistoryRetention : age);
        lock (_lock)
        {
            PruneExpiredUnsafe(DateTime.UtcNow);
            return _turns
                .Where(turn => turn.Timestamp >= cutoff && (!steamId.HasValue || turn.SteamId == steamId.Value))
                .TakeLast(Math.Clamp(max, 1, 200))
                .ToList();
        }
    }

    /// <summary>Start or reset a bounded interactive conversation for one player.</summary>
    public void BeginInteractiveSession(ulong steamId)
    {
        if (steamId == 0)
            return;

        lock (_lock)
        {
            _interactiveSessions[steamId] = new InteractiveSession
            {
                RemainingReplies = InteractiveReplyLimit,
                StartedUtc = DateTime.UtcNow,
                LastActivityUtc = DateTime.UtcNow
            };
        }
    }

    public bool HasInteractiveSession(ulong steamId)
    {
        lock (_lock)
            return _interactiveSessions.ContainsKey(steamId);
    }

    /// <summary>
    /// Consume one assistant reply. Returns the remaining budget and whether the
    /// session closed because the four-reply budget was exhausted.
    /// </summary>
    public bool TryConsumeInteractiveReply(ulong steamId, out int remainingReplies, out bool closed)
    {
        remainingReplies = 0;
        closed = false;
        lock (_lock)
        {
            if (!_interactiveSessions.TryGetValue(steamId, out var session))
                return false;

            session.LastActivityUtc = DateTime.UtcNow;
            session.RemainingReplies = Math.Max(0, session.RemainingReplies - 1);
            remainingReplies = session.RemainingReplies;
            if (session.RemainingReplies == 0)
            {
                _interactiveSessions.Remove(steamId);
                closed = true;
            }

            return true;
        }
    }

    public bool EndInteractiveSession(ulong steamId)
    {
        lock (_lock)
            return _interactiveSessions.Remove(steamId);
    }

    /// <summary>Remove stale history and abandoned interactive sessions.</summary>
    public void Prune()
    {
        lock (_lock)
            PruneExpiredUnsafe(DateTime.UtcNow);
    }

    /// <summary>Render the last <paramref name="n"/> turns as plain text for LLM context.</summary>
    public string FormatForContext(int n)
    {
        var sb = new StringBuilder();
        foreach (var t in Recent(n))
        {
            sb.Append($"[{t.Timestamp:HH:mm:ss}] {t.Speaker}: {t.Text}\n");
            foreach (var r in t.ActionResults)
                sb.Append($"  -> {r}\n");
        }
        return sb.ToString();
    }

    void PruneExpiredUnsafe(DateTime now)
    {
        var cutoff = now - HistoryRetention;
        _turns.RemoveAll(turn => turn.Timestamp < cutoff);

        var sessionCutoff = now - HistoryRetention;
        foreach (var steamId in _interactiveSessions
                     .Where(pair => pair.Value.LastActivityUtc < sessionCutoff)
                     .Select(pair => pair.Key)
                     .ToList())
        {
            _interactiveSessions.Remove(steamId);
        }
    }

    sealed class InteractiveSession
    {
        public int RemainingReplies { get; set; }
        public DateTime StartedUtc { get; set; }
        public DateTime LastActivityUtc { get; set; }
    }
}
