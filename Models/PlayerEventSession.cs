namespace BattleLuck.Models;

/// <summary>
/// Represents a player's session within an event, tracked outside ECS.
/// Canonical managed session lifecycle state for an event participant.
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
    public PlayerSessionState State { get; set; } = PlayerSessionState.Reserved;
    public string FailedStage { get; set; } = "";
    public string FailureReason { get; set; } = "";
    public DateTime CreatedAtUtc { get; init; } = DateTime.UtcNow;
    public DateTime? ActivatedAtUtc { get; set; }
    public int TeamIndex { get; set; }
    public int DeathCount { get; set; }
    public bool Eliminated { get; set; }

    /// <summary>
    /// Records one death and returns whether the configured death limit has
    /// been reached. Invalid limits are defensively treated as one death.
    /// Repeated notifications after elimination are idempotent.
    /// </summary>
    public bool RegisterDeath(int maxDeathsPerParticipant)
    {
        if (Eliminated)
            return true;

        DeathCount++;
        Eliminated = DeathCount >= Math.Max(1, maxDeathsPerParticipant);
        return Eliminated;
    }
}

public enum PlayerSessionState
{
    Reserved,
    Active,
    Leaving,
    Failed
}
