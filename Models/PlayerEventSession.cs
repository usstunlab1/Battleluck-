namespace BattleLuck.Models;

/// <summary>
/// Represents a player's session within an event, tracked outside ECS.
/// Canonical managed session lifecycle state for an event participant.
/// All mutable lifecycle properties are privately set; transitions occur
/// through controlled methods only.
/// </summary>
public sealed class PlayerEventSession
{
    public ulong SteamId { get; init; }
    public string SessionId { get; init; } = "";
    /// <summary>
    /// Event definition identifier, such as bloodbath or colosseum.
    /// Historically named ModeId for configuration and API compatibility.
    /// </summary>
    public string ModeId { get; init; } = "";
    public int ZoneHash { get; init; }
    public DateTime CreatedAtUtc { get; init; } = DateTime.UtcNow;

    // ── Lifecycle state (privately set) ────────────────────────────────────

    public PlayerSessionState State { get; private set; } = PlayerSessionState.Reserved;
    public DateTime? ActivatedAtUtc { get; private set; }
    public int TeamIndex { get; private set; } = -1;
    public int DeathCount { get; private set; }
    public bool Eliminated { get; private set; }
    public EventExitReason ExitReason { get; private set; } = EventExitReason.None;
    public string FailedStage { get; private set; } = "";
    public string FailureReason { get; private set; } = "";

    // ── Controlled transitions ─────────────────────────────────────────────

    /// <summary>
    /// Transition from Reserved to Active. Sets ActivatedAtUtc.
    /// </summary>
    public void Activate()
    {
        if (State != PlayerSessionState.Reserved)
            throw new InvalidOperationException(
                $"Cannot activate session in state {State}; expected Reserved.");
        State = PlayerSessionState.Active;
        ActivatedAtUtc = DateTime.UtcNow;
    }

    /// <summary>
    /// Assign the participant to a team. Only valid while Active or Reserved.
    /// </summary>
    public void AssignTeam(int teamIndex)
    {
        if (State is PlayerSessionState.Leaving or PlayerSessionState.Left or PlayerSessionState.Failed)
            throw new InvalidOperationException(
                $"Cannot assign team in state {State}.");
        TeamIndex = teamIndex;
    }

    /// <summary>
    /// Records one death and returns whether the configured death limit has
    /// been reached. Invalid limits are defensively treated as one death.
    /// Repeated notifications after elimination are idempotent.
    /// Only valid while Active.
    /// </summary>
    public bool RegisterDeath(int maxDeathsPerParticipant)
    {
        if (Eliminated)
            return true;

        if (State != PlayerSessionState.Active)
            throw new InvalidOperationException(
                $"Cannot register death in state {State}; expected Active.");

        DeathCount++;
        Eliminated = DeathCount >= Math.Max(1, maxDeathsPerParticipant);
        return Eliminated;
    }

    /// <summary>
    /// Begin the leaving process (rollback, teleport, buff removal).
    /// </summary>
    public void BeginLeaving(EventExitReason reason)
    {
        if (State is PlayerSessionState.Left or PlayerSessionState.Failed)
            throw new InvalidOperationException(
                $"Cannot begin leaving in state {State}.");

        // Idempotent: if already Leaving, just update reason if not yet set.
        if (State == PlayerSessionState.Leaving)
        {
            if (ExitReason == EventExitReason.None)
                ExitReason = reason;
            return;
        }

        State = PlayerSessionState.Leaving;
        ExitReason = reason;
    }

    /// <summary>
    /// Mark the participant as having fully left the event.
    /// The player remains connected to the server.
    /// </summary>
    public void MarkLeft()
    {
        if (State != PlayerSessionState.Leaving)
            throw new InvalidOperationException(
                $"Cannot mark left from state {State}; expected Leaving.");
        State = PlayerSessionState.Left;
    }

    /// <summary>
    /// Mark the session as failed at a given stage with a reason.
    /// </summary>
    public void MarkFailed(string stage, string reason)
    {
        State = PlayerSessionState.Failed;
        FailedStage = stage ?? "";
        FailureReason = reason ?? "";
    }
}

public enum PlayerSessionState
{
    Reserved,
    Active,
    Leaving,
    Left,
    Failed
}

/// <summary>
/// Why a participant left the event. Independent from the lifecycle state.
/// ServerDisconnected means the server connection ended; it does not redefine
/// what Leaving means.
/// </summary>
public enum EventExitReason
{
    None,
    Voluntary,
    Eliminated,
    EventEnded,
    AdminRemoved,
    ServerDisconnected
}
