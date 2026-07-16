using BattleLuck.Models;
using Unity.Entities;
using Unity.Mathematics;

// ──────────────────────────────────────────────
// All BattleLuck event types — plain sealed classes, no base class.
// Used exclusively with GameEvents static Action<T> delegates.
// ──────────────────────────────────────────────

// ── Zone lifecycle ──
public sealed class ZoneEnterEvent
{
    public Entity PlayerEntity { get; init; }
    public ulong SteamId { get; init; }
    public string ZoneId { get; init; } = "";
    public string SessionId { get; init; } = "";
}

public sealed class ZoneExitEvent
{
    public Entity PlayerEntity { get; init; }
    public ulong SteamId { get; init; }
    public string ZoneId { get; init; } = "";
    public string SessionId { get; init; } = "";
}

// ── Mode lifecycle ──
public sealed class ModeStartedEvent
{
    public string SessionId { get; init; } = "";
    public string ModeId { get; init; } = "";
    public DateTime TimestampUtc { get; init; } = DateTime.UtcNow;
}

public sealed class ModeEndedEvent
{
    public string SessionId { get; init; } = "";
    public string ModeId { get; init; } = "";
    public ulong? WinnerSteamId { get; init; }
    public int WinnerTeamId { get; init; } = -1;
    public DateTime TimestampUtc { get; init; } = DateTime.UtcNow;
}

// ── Round / scoring ──
public sealed class RoundEndedEvent
{
    public string SessionId { get; init; } = "";
    public string ModeId { get; init; } = "";
    public int RoundNumber { get; init; }
    public int WinnerId { get; init; }
}

public sealed class PlayerScoredEvent
{
    public string SessionId { get; init; } = "";
    public ulong SteamId { get; init; }
    public int Points { get; init; }
    public int TotalScore { get; init; }
    public string Reason { get; init; } = "";
    public ActionType? Action { get; init; }
}

/// <summary>Fired when any trackable action occurs (kill, capture, wave clear, etc.).</summary>
public sealed class ActionPerformedEvent
{
    public string SessionId { get; init; } = "";
    public ulong SteamId { get; init; }
    public ActionType Action { get; init; }
    public string ModeId { get; init; } = "";
    public int Points { get; init; }
    public Unity.Entities.Entity? PlayerEntity { get; init; }
}

// ── Waves ──
public sealed class WaveStartedEvent
{
    public string SessionId { get; init; } = "";
    public int WaveNumber { get; init; }
    public int EnemyCount { get; init; }
}

public sealed class WaveClearedEvent
{
    public string SessionId { get; init; } = "";
    public int WaveNumber { get; init; }
    public double ElapsedSeconds { get; init; }
}

public sealed class WaveFinalEvent
{
    public string SessionId { get; init; } = "";
    public int TotalWaves { get; init; }
    public double TotalSeconds { get; init; }
    public bool Victory { get; init; }
}

// ── World state ──
public sealed class ObjectiveCapturedEvent
{
    public string SessionId { get; init; } = "";
    public string ObjectiveId { get; init; } = "";
    public int TeamId { get; init; }
}

public sealed class ZoneShrinkEvent
{
    public string SessionId { get; init; } = "";
    public float OldRadius { get; init; }
    public float NewRadius { get; init; }
}

public sealed class RealityStateChanged
{
    public string SessionId { get; init; } = "";
    public string State { get; init; } = "";
    public float3 Center { get; init; }
    public float Radius { get; init; }
}

// ── Entity signals ──
public sealed class BossSpawnedEvent
{
    public string SessionId { get; init; } = "";
    public Entity BossEntity { get; init; }
    public int PrefabGuid { get; init; }
}

public sealed class PlatformStateChanged
{
    public string SessionId { get; init; } = "";
    public string PlatformId { get; init; } = "";
    public float3 Position { get; init; }
    public bool Active { get; init; }
}

public sealed class CrateCollectedEvent
{
    public string SessionId { get; init; } = "";
    public ulong SteamId { get; init; }
    public string CrateId { get; init; } = "";
    public string LootTable { get; init; } = "";
}

// ── Player lifecycle ──
public sealed class PlayerEliminatedEvent
{
    public string SessionId { get; init; } = "";
    public ulong SteamId { get; init; }
    public ulong? EliminatedBy { get; init; }
}

public sealed class PlayerLeftEvent
{
    public string SessionId { get; init; } = "";
    public ulong SteamId { get; init; }
    public string ModeId { get; init; } = "";
}

// ── Blood Frenzy ──
public sealed class BloodFrenzyActivatedEvent
{
    public string SessionId { get; init; } = "";
    public ulong SteamId { get; init; }
    public int StreakCount { get; init; }
}

public sealed class BloodFrenzyBountyEvent
{
    public string SessionId { get; init; } = "";
    public ulong KillerSteamId { get; init; }
    public ulong VictimSteamId { get; init; }
    public int BountyPoints { get; init; }
}

// ── ELO ──
public sealed class EloUpdateEvent
{
    public string SessionId { get; init; } = "";
    public ulong SteamId { get; init; }
    public int OldElo { get; init; }
    public int NewElo { get; init; }
}

// ── Webhook ──
public sealed class WebhookActionEvent
{
    public string Action { get; init; } = "";
    public ulong TargetSteamId { get; init; }
    public Dictionary<string, object>? Parameters { get; init; }
}

// ── Discord Bridge ──
public sealed class DiscordCommandEvent
{
    public string Command { get; init; } = "";
    public string DiscordUserId { get; init; } = "";
    public ulong? MappedSteamId { get; init; }
    public Dictionary<string, string>? Args { get; init; }
}
