using System.Collections.Generic;
using Unity.Entities;
using Unity.Mathematics;

namespace BattleLuck.Models;

/// <summary>
/// Extended NPC behavioral modes beyond the core NpcControlService set.
/// </summary>
public enum NpcBehaviorMode
{
    Idle,
    Hold,
    Follow,
    GoTo,
    Aggro,
    Patrol,
    Guard,
    Flee,
    Wander,
    Formation
}

/// <summary>
/// A single waypoint in an NPC patrol route.
/// </summary>
public sealed class NpcPatrolWaypoint
{
    public float3 Position { get; set; }
    public float PauseSeconds { get; set; } = 0f;
    public string? Label { get; set; }
}

/// <summary>
/// Guard behavior configuration.
/// </summary>
public sealed class NpcGuardPost
{
    public float3 Position { get; set; }
    public Entity? TargetEntity { get; set; }
    public float DetectionRadius { get; set; } = 15f;
    public float ChaseRange { get; set; } = 25f;
    public float ReturnRadius { get; set; } = 35f;
    public float ChaseSpeedMultiplier { get; set; } = 1.2f;
    public bool ReturnOnLostTarget { get; set; } = true;
}

/// <summary>
/// Flee behavior configuration.
/// </summary>
public sealed class NpcFleeConfig
{
    public Entity? FromEntity { get; set; }
    public float3? FromPosition { get; set; }
    public float SafeDistance { get; set; } = 20f;
    public float FleeSpeedMultiplier { get; set; } = 1.5f;
    public float DurationSeconds { get; set; } = 10f;
    public bool ResumePreviousOnExpiry { get; set; } = true;
}

/// <summary>
/// Wander behavior configuration.
/// </summary>
public sealed class NpcWanderConfig
{
    public float Radius { get; set; } = 15f;
    public float MinPauseSeconds { get; set; } = 2f;
    public float MaxPauseSeconds { get; set; } = 6f;
    public float Jitter { get; set; } = 3f;
    public int MaxWaypoints { get; set; } = 8;
    public bool StayOnNavmesh { get; set; } = true;
}

/// <summary>
/// Formation slot relative to a leader or center.
/// </summary>
public sealed class NpcFormationSlot
{
    public string NpcId { get; set; } = "";
    public float3 Offset { get; set; }
    public float AcceptRadius { get; set; } = 2f;
    public int Priority { get; set; } = 0;
}

/// <summary>
/// Point-in-time NPC state snapshot for audit replay.
/// </summary>
public sealed class NpcStateSnapshot
{
    public string NpcId { get; set; } = "";
    public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;
    public NpcBehaviorMode Mode { get; set; }
    public float3 Position { get; set; }
    public float3 HomePosition { get; set; }
    public float3 TargetPosition { get; set; }
    public Entity TargetEntity { get; set; }
    public float MoveSpeed { get; set; }
    public int? ForcedTeamId { get; set; }
    public int? ForcedFactionId { get; set; }
    public string? DisplayName { get; set; }
    public Dictionary<string, object> Extensions { get; set; } = new();
}

/// <summary>
/// Result of a single NPC command consistency check.
/// </summary>
public sealed class NpcCommandConsistencyResult
{
    public bool IsConsistent { get; set; }
    public string? Violation { get; set; }
    public string? SuggestedFix { get; set; }
    public NpcConsistencySeverity Severity { get; set; } = NpcConsistencySeverity.Warning;
}

/// <summary>
/// Severity levels for consistency violations.
/// </summary>
public enum NpcConsistencySeverity
{
    Info,
    Warning,
    Error,
    Critical
}

/// <summary>
/// Structured audit entry for an NPC command execution.
/// </summary>
public sealed class NpcAuditEntry
{
    public string CorrelationId { get; set; } = Guid.NewGuid().ToString("N");
    public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;
    public string Source { get; set; } = ""; // Flow, Discord, Chat, Api
    public string PlayerId { get; set; } = "";
    public string ActionName { get; set; } = "";
    public string NpcId { get; set; } = "";
    public string SessionId { get; set; } = "";
    public NpcBehaviorMode? PreviousMode { get; set; }
    public NpcBehaviorMode? NewMode { get; set; }
    public Dictionary<string, object> Parameters { get; set; } = new();
    public bool PreValidationPassed { get; set; }
    public bool PostValidationPassed { get; set; }
    public bool Executed { get; set; }
    public string? Error { get; set; }
    public NpcStateSnapshot? BeforeSnapshot { get; set; }
    public NpcStateSnapshot? AfterSnapshot { get; set; }
    public long ElapsedMs { get; set; }
    public List<string> ConsistencyWarnings { get; set; } = new();
}
