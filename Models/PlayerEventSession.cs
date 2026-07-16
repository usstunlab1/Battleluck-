namespace BattleLuck.Models;

/// <summary>
/// Represents a player's session within an event, tracked outside ECS.
/// Canonical managed session lifecycle state for an event participant.
/// </summary>
public sealed class PlayerEventSession
{
    public ulong SteamId { get; init; }
    public string SessionId { get; init; } = "";
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
}

public enum PlayerSessionState
{
    Reserved,
    Active,
    Leaving,
    Failed
}
