// Models/SessionPhase.cs
// Canonical session lifecycle state machine.
//
// SessionPhase is the single authority for where a session is in its life.
// SessionController exposes the current phase; all transition checks go
// through SessionPhaseStateMachine to keep invalid transitions consistent.

namespace BattleLuck.Models;

// ── Phase enum ───────────────────────────────────────────────────────────────

/// <summary>
/// All valid lifecycle phases of a BattleLuck session.
/// </summary>
public enum SessionPhase
{
    /// <summary>Zone accepted, resources being allocated (teams, kits, arena build).</summary>
    Preparing,

    /// <summary>Arena is built; awaiting player ready-checks or auto-start timer.</summary>
    ArenaReady,

    /// <summary>Countdown / grace period before combat is live.</summary>
    Warmup,

    /// <summary>Session is fully live; gameplay is running.</summary>
    Active,

    /// <summary>End condition met; wrapping up (final scores, rewards, teardown queued).</summary>
    Ending,

    /// <summary>Session ended cleanly; cleanup complete.</summary>
    Completed,

    /// <summary>Session ended due to an error, force-end, or unrecoverable state.</summary>
    Failed,
}

// ── Transition result ─────────────────────────────────────────────────────────

/// <summary>
/// Result of a <see cref="SessionPhaseStateMachine.TryTransition"/> call.
/// </summary>
public sealed class SessionPhaseTransitionResult
{
    public static readonly SessionPhaseTransitionResult Ok =
        new(true, null, SessionPhase.Preparing, SessionPhase.Preparing);

    private SessionPhaseTransitionResult(
        bool succeeded,
        string? error,
        SessionPhase from,
        SessionPhase to)
    {
        Succeeded = succeeded;
        Error = error;
        From = from;
        To = to;
    }

    public bool Succeeded { get; }
    public string? Error { get; }
    public SessionPhase From { get; }
    public SessionPhase To { get; }

    public static SessionPhaseTransitionResult Success(SessionPhase from, SessionPhase to) =>
        new(true, null, from, to);

    public static SessionPhaseTransitionResult Failure(SessionPhase from, SessionPhase to, string reason) =>
        new(false, reason, from, to);
}

// ── State machine ─────────────────────────────────────────────────────────────

/// <summary>
/// Single-source-of-truth for valid <see cref="SessionPhase"/> transitions.
/// Centralizes the rules so invalid starts, double endings, and late-join
/// denials are all enforced in one place.
/// </summary>
public static class SessionPhaseStateMachine
{
    // Allowed (from → to) pairs.
    private static readonly HashSet<(SessionPhase, SessionPhase)> _allowed = new()
    {
        (SessionPhase.Preparing,   SessionPhase.ArenaReady),
        (SessionPhase.Preparing,   SessionPhase.Failed),      // build-time failure
        (SessionPhase.ArenaReady,  SessionPhase.Warmup),
        (SessionPhase.ArenaReady,  SessionPhase.Active),      // skip warmup path
        (SessionPhase.ArenaReady,  SessionPhase.Failed),
        (SessionPhase.Warmup,      SessionPhase.Active),
        (SessionPhase.Warmup,      SessionPhase.Failed),
        (SessionPhase.Active,      SessionPhase.Ending),
        (SessionPhase.Active,      SessionPhase.Failed),
        (SessionPhase.Ending,      SessionPhase.Completed),
        (SessionPhase.Ending,      SessionPhase.Failed),
        // Force-end from any live phase → Failed
        (SessionPhase.Preparing,   SessionPhase.Failed),
    };

    /// <summary>Phases from which a player can still be admitted.</summary>
    private static readonly HashSet<SessionPhase> _admitPhases = new()
    {
        SessionPhase.Preparing,
        SessionPhase.ArenaReady,
        SessionPhase.Warmup,
    };

    public static SessionPhaseTransitionResult TryTransition(
        SessionPhase current, SessionPhase target)
    {
        if (_allowed.Contains((current, target)))
            return SessionPhaseTransitionResult.Success(current, target);

        return SessionPhaseTransitionResult.Failure(
            current, target,
            $"Transition {current} → {target} is not permitted.");
    }

    /// <summary>Returns true when the session is still accepting new players.</summary>
    public static bool CanAdmitPlayers(SessionPhase phase) => _admitPhases.Contains(phase);

    /// <summary>Returns true when the session is in a live/running state.</summary>
    public static bool IsLive(SessionPhase phase) =>
        phase is SessionPhase.Warmup or SessionPhase.Active;

    /// <summary>Returns true when the session has reached a terminal state.</summary>
    public static bool IsTerminal(SessionPhase phase) =>
        phase is SessionPhase.Completed or SessionPhase.Failed;
}
