// Services/Runtime/RuntimeActionTypes.cs
// Canonical action pipeline value types.
//
// These records are pure C# — no BepInEx, VRising, or Unity references.
// They are shared by the plugin, the test project, and any external tooling.

using System.Text.Json.Serialization;
using Unity.Entities;
using BattleLuck.Services;
using BattleLuck.Models;

namespace BattleLuck.Services.Runtime;

// ── Source ────────────────────────────────────────────────────────────────────

/// <summary>Who or what originated an action request.</summary>
public enum ActionSource
{
    Unknown = 0,
    Admin,
    Player,
    AI,
    Webhook,
    MCP,
    EventRuntime,
    DevConsole,
    System,
}


// ── Risk ─────────────────────────────────────────────────────────────────────

/// <summary>
/// Coarse risk classification used by the validation pipeline to determine
/// whether an action may auto-execute or requires confirmation.
/// </summary>
public enum ActionRiskLevel
{
    /// <summary>Read-only query; no world state change.</summary>
    ReadOnly = 0,

    /// <summary>Reversible, low-impact mutation (e.g. buff a player).</summary>
    Low,

    /// <summary>Significant mutation with undo path (e.g. teleport player).</summary>
    Medium,

    /// <summary>Destructive or irreversible mutation (e.g. kill entity, wipe zone).</summary>
    High,

    /// <summary>Server-wide impact (e.g. shutdown, world reset).</summary>
    Critical,
}

// ── Intent ────────────────────────────────────────────────────────────────────

/// <summary>
/// Canonical representation of a parsed action request, before any execution.
/// Created by parsers (command text, EventActionDefinition, AI JSON, webhook payload).
/// Consumed by <see cref="IActionRuntime"/>.
/// </summary>
public sealed record RuntimeActionIntent
{
    /// <summary>Canonical action name (e.g. "teleport", "give_kit", "spawn_boss").</summary>
    public string ActionName { get; init; } = string.Empty;

    /// <summary>Resolved parameters, keyed by parameter name.</summary>
    public Dictionary<string, string> Parameters { get; init; }
        = new Dictionary<string, string>();

    /// <summary>Who requested this action.</summary>
    public ActionSource Source { get; init; } = ActionSource.Unknown;

    /// <summary>Steam64 or system actor identifier. Null for system-originated actions.</summary>
    public string? ActorId { get; init; }

    /// <summary>Legacy alias for ActorId.</summary>
    public string? Actor { get; init; }

    /// <summary>Steam64 of the target player, when applicable.</summary>
    public string? TargetPlayerId { get; init; }

    /// <summary>Session that this action is scoped to. Null for global actions.</summary>
    public string? SessionId { get; init; }

    /// <summary>Zone hash the action is scoped to. 0 for zone-unscoped actions.</summary>
    public int ZoneHash { get; init; }

    /// <summary>Opaque correlation id for end-to-end telemetry tracing.</summary>
    public string CorrelationId { get; init; } = Guid.NewGuid().ToString("N");

    /// <summary>UTC timestamp when the intent was created.</summary>
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;

    // ── Command/AI execution context fields ──────────────────────────────────

    /// <summary>Name of the command that produced this intent (if from a command). Null for AI/direct.</summary>
    public string? CommandName { get; init; }

    /// <summary>Event ID this intent is scoped to. Null for global actions.</summary>
    public string? EventId { get; init; }

    /// <summary>Whether this originated from the server console (trusted source).</summary>
    public bool IsConsoleRequest { get; init; }

    /// <summary>Approval operation ID if this intent was pre-approved. Null otherwise.</summary>
    public string? ApprovalId { get; init; }

    /// <summary>Effective permission level of the requester at submission time.</summary>
    public ActionPermissionLevel Permission { get; init; } = ActionPermissionLevel.Readonly;
}

/// <summary>Effective permission level for action requesters.</summary>
public enum ActionPermissionLevel
{
    Readonly = 0,
    Mutation,
    Admin,
    Dangerous,
}

// ── Context ───────────────────────────────────────────────────────────────────

/// <summary>
/// Runtime services and state injected into the action pipeline.
/// Populated by the execution host before calling <see cref="IActionRuntime.ExecuteAsync"/>.
/// </summary>
public sealed class RuntimeActionContext
{
    /// <summary>The intent being processed.</summary>
    public RuntimeActionIntent Intent { get; init; } = null!;

    /// <summary>Mode config for the session's game mode. May be null for global actions.</summary>
    public ModeConfig? ModeConfig { get; init; }

    /// <summary>Zone definition resolved for <see cref="RuntimeActionIntent.ZoneHash"/>.</summary>
    public ZoneDefinition? ZoneDefinition { get; init; }

    /// <summary>
    /// Ambient service locator used by action implementations to resolve
    /// services without taking hard dependencies on the plugin class.
    /// </summary>
    public IServiceProvider? Services { get; init; }

    /// <summary>
    /// Additional context entries for action-specific ambient data
    /// (e.g. Entity handle of the acting player).
    /// </summary>
    public IReadOnlyDictionary<string, object?> Extras { get; init; }
        = new Dictionary<string, object?>();

    // ── Legacy compatibility properties ─────────────────────────────────────────

    /// <summary>Entity handle of the acting player.</summary>
    public Unity.Entities.Entity PlayerCharacter { get; init; } = default;

    /// <summary>Zone hash for zone-aware operations.</summary>
    public int ZoneHash { get; init; }

    /// <summary>Zone definition for the current zone.</summary>
    public ZoneDefinition? Zone { get; init; }

    /// <summary>Session context for the current session.</summary>
    public GameModeContext? SessionContext { get; init; }

    /// <summary>Player state controller.</summary>
    public BattleLuck.Services.PlayerStateController? PlayerState { get; init; }

    /// <summary>Prefab registry.</summary>
    public GameModeRegistry? Registry { get; init; }
}

// ── Validation stage ──────────────────────────────────────────────────────────

public enum ValidationStage
{
    /// <summary>Pure config check — does the action exist and are parameters valid?</summary>
    StaticConfig = 0,

    /// <summary>Live-state check — is the session in a phase that permits this action?</summary>
    RuntimeState = 1,

    /// <summary>Safety/policy check — source permissions, risk gate, confirmation.</summary>
    SafetyPolicy = 2,
}

public enum ValidationOutcome
{
    Passed,
    Denied,
    RequiresConfirmation,
    Skipped,
}

// ── Legacy compatibility enums ───────────────────────────────────────────────

public enum ValidationStatus
{
    Allowed,
    Rejected,
    RequiresConfirmation,
}

public sealed record ValidationStageResult
{
    public ValidationStage Stage { get; init; }
    public ValidationOutcome Outcome { get; init; }
    public string? Reason { get; init; }
}

// ── Report ────────────────────────────────────────────────────────────────────

public enum ExecutionStatus
{
    NotStarted = 0,
    NotExecuted,
    Succeeded,
    PartialSuccess,
    Failed,
    Skipped,
    DeniedByValidation,
    AwaitingConfirmation,
}

/// <summary>
/// Full record of what happened when an intent was processed: validation
/// results, execution outcome, emitted events, and rollback notes.
/// </summary>
public sealed class RuntimeActionReport
{
    /// <summary>The intent that produced this report.</summary>
    public RuntimeActionIntent Intent { get; init; } = null!;

    /// <summary>Ordered results of each validation stage that ran.</summary>
    public IReadOnlyList<ValidationStageResult> ValidationResults { get; init; }
        = Array.Empty<ValidationStageResult>();

    public ExecutionStatus ExecutionStatus { get; init; } = ExecutionStatus.NotStarted;

    /// <summary>Human-readable summary of the execution result.</summary>
    public string? Summary { get; init; }

    /// <summary>Named events emitted during execution (for telemetry/event-bus publishing).</summary>
    public IReadOnlyList<string> EmittedEvents { get; init; } = Array.Empty<string>();

    /// <summary>Non-fatal warnings raised during execution.</summary>
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Compensation/rollback notes. Populated when the action was partially
    /// applied and the caller may need to undo side effects.
    /// </summary>
    public string? RollbackNotes { get; init; }

    /// <summary>UTC timestamp when execution completed (or was denied).</summary>
    public DateTimeOffset CompletedAt { get; init; } = DateTimeOffset.UtcNow;

    // ── Legacy compatibility properties ─────────────────────────────────────────

    /// <summary>Legacy validation status.</summary>
    public ValidationStatus Validation { get; set; } = ValidationStatus.Allowed;

    /// <summary>Legacy execution status.</summary>
    public ExecutionStatus Execution { get; set; } = ExecutionStatus.NotStarted;

    /// <summary>Legacy error message.</summary>
    public string? Error { get; set; }

    /// <summary>Legacy troubleshooting info.</summary>
    public string? Troubleshooting { get; set; }

    /// <summary>Legacy events list (for backward compatibility).</summary>
    public List<string> Events { get; set; } = new();

    /// <summary>Legacy started timestamp.</summary>
    public DateTimeOffset StartedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Legacy completed timestamp.</summary>
    public DateTimeOffset CompletedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Legacy success property.</summary>
    public bool Success => IsSuccess;

    // ── Convenience helpers ───────────────────────────────────────────────────

    public bool ValidationPassed =>
        ValidationResults.All(r => r.Outcome is ValidationOutcome.Passed or ValidationOutcome.Skipped);

    public bool IsSuccess =>
        ExecutionStatus is ExecutionStatus.Succeeded or ExecutionStatus.PartialSuccess;

    public ValidationStageResult? FirstDenial =>
        ValidationResults.FirstOrDefault(r => r.Outcome == ValidationOutcome.Denied);

    /// <summary>
    /// Serialize to JSONL-compatible JSON for the runtime event log.
    /// </summary>
    public string ToJsonLine()
    {
        return System.Text.Json.JsonSerializer.Serialize(new
        {
            correlationId = Intent.CorrelationId,
            sessionId = Intent.SessionId,
            zoneHash = Intent.ZoneHash,
            action = Intent.ActionName,
            source = Intent.Source.ToString(),
            actorId = Intent.ActorId,
            validationResults = ValidationResults.Select(r => new
            {
                stage = r.Stage.ToString(),
                outcome = r.Outcome.ToString(),
                reason = r.Reason,
            }),
            executionStatus = ExecutionStatus.ToString(),
            summary = Summary,
            emittedEvents = EmittedEvents,
            warnings = Warnings,
            rollbackNotes = RollbackNotes,
            requestedAt = Intent.CreatedAt,
            completedAt = CompletedAt,
        });
    }
}
