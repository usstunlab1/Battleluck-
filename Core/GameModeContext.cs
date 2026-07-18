/// <summary>
/// Runtime context passed to game mode lifecycle hooks.
/// Contains session state, participating players, scoring, and round info.
/// </summary>
public sealed class GameModeContext
{
    public string SessionId { get; init; } = "";
    public int ZoneHash { get; init; }
    public string ModeId { get; init; } = "";

    /// <summary>All players currently in the zone (steam IDs).</summary>
    public HashSet<ulong> Players { get; } = new();

    /// <summary>Team assignments (steamId → teamId). 0 = no team / FFA.</summary>
    public Dictionary<ulong, int> Teams { get; } = new();

    /// <summary>Per-player score tracker.</summary>
    public ScoreTracker Scores { get; } = new();

    /// <summary>Round manager for multi-round modes.</summary>
    public RoundManager Rounds { get; init; } = new();

    /// <summary>Arbitrary mode-specific state bag.</summary>
    public Dictionary<string, object?> State { get; } = new();

    /// <summary>Active tech state for this event session instance.</summary>
    public SessionTechState TechState { get; set; } = new();

    /// <summary>When the mode started (UTC).</summary>
    public DateTime StartTimeUtc { get; set; } = DateTime.UtcNow;

    /// <summary>Optional time limit in seconds (0 = no limit).</summary>
    public int TimeLimitSeconds { get; set; }

    /// <summary>Elapsed seconds since mode started.</summary>
    public double ElapsedSeconds => (DateTime.UtcNow - StartTimeUtc).TotalSeconds;

    /// <summary>Whether the time limit has been exceeded.</summary>
    public bool IsTimeUp => TimeLimitSeconds > 0 && ElapsedSeconds >= TimeLimitSeconds;

    /// <summary>Broadcast a message to all players in the session.</summary>
    public Action<string>? Broadcast { get; init; }
}
