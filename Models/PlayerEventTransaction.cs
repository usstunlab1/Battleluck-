using System.Text.Json.Serialization;

namespace BattleLuck.Models;

/// <summary>
/// Lifecycle state for a player's event entry transaction.
/// </summary>
public enum PlayerEventState
{
    None,
    SnapshotSaving,
    SnapshotSaved,
    Preparing,
    Teleporting,
    Active,
    Eliminated,
    Restoring,
    Restored,
    Failed
}

/// <summary>
/// Tracks a player's in-progress event entry transaction.
/// Used to recover player state if disconnect occurs during entry flow.
/// </summary>
public sealed class PlayerEventTransaction
{
    public ulong SteamId { get; init; }
    public string EventId { get; init; } = "";
    public int ZoneHash { get; init; }

    public PlayerEventState State { get; set; }

    public string SnapshotId { get; set; } = "";
    public bool SnapshotPersisted { get; set; }
    public bool EventKitApplied { get; set; }
    public bool TeleportCompleted { get; set; }
    public bool PvpChanged { get; set; }
    public bool RestoreRequired { get; set; }

    public DateTime CreatedUtc { get; init; } = DateTime.UtcNow;
    public DateTime UpdatedUtc { get; set; }
}

public sealed class float3Snapshot
{
    public float X { get; set; }
    public float Y { get; set; }
    public float Z { get; set; }
}