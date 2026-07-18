// Models/ExecutionLifecycle.cs
// Per-target execution lifecycle state machine.
//
// ExecutionLifecycle models the stages a single action target goes through
// from submission to completion. It is the per-action counterpart of
// SessionPhase (which models the session-level lifecycle).
//
// Allowed transitions:
//   Submitted → Validating → Validated → Executing → Succeeded
//                                                    → Failed
//                                      → Invalid (validation rejected)
//   Failed    → Submitted  (retry path)
//   Invalid   → Submitted  (retry path)

namespace BattleLuck.Models;

/// <summary>
/// All valid lifecycle phases of a single execution target.
/// </summary>
public enum ExecutionLifecycle
{
    /// <summary>Target received but not yet validated.</summary>
    Submitted,

    /// <summary>Validation in progress (cooldown, permission, confidence checks).</summary>
    Validating,

    /// <summary>Validation passed; awaiting execution.</summary>
    Validated,

    /// <summary>LLM is generating or action is being executed in the game world.</summary>
    Executing,

    /// <summary>Execution completed successfully.</summary>
    Succeeded,

    /// <summary>Execution failed due to an error.</summary>
    Failed,

    /// <summary>Validation rejected the target (not retriable without changes).</summary>
    Invalid,
}

/// <summary>
/// Result of a <see cref="ExecutionLifecycleStateMachine.TryTransition"/> call.
/// </summary>
public sealed class ExecutionLifecycleTransitionResult
{
    public static readonly ExecutionLifecycleTransitionResult Ok =
        new(true, null, ExecutionLifecycle.Submitted, ExecutionLifecycle.Submitted);

    private ExecutionLifecycleTransitionResult(
        bool succeeded,
        string? error,
        ExecutionLifecycle from,
        ExecutionLifecycle to)
    {
        Succeeded = succeeded;
        Error = error;
        From = from;
        To = to;
    }

    public bool Succeeded { get; }
    public string? Error { get; }
    public ExecutionLifecycle From { get; }
    public ExecutionLifecycle To { get; }

    public static ExecutionLifecycleTransitionResult Success(
        ExecutionLifecycle from, ExecutionLifecycle to) =>
        new(true, null, from, to);

    public static ExecutionLifecycleTransitionResult Failure(
        ExecutionLifecycle from, ExecutionLifecycle to, string reason) =>
        new(false, reason, from, to);
}

/// <summary>
/// Single-source-of-truth for valid <see cref="ExecutionLifecycle"/> transitions.
/// </summary>
public static class ExecutionLifecycleStateMachine
{
    // Allowed (from → to) pairs.
    private static readonly HashSet<(ExecutionLifecycle, ExecutionLifecycle)> _allowed = new()
    {
        // Forward path
        (ExecutionLifecycle.Submitted,   ExecutionLifecycle.Validating),
        (ExecutionLifecycle.Validating,  ExecutionLifecycle.Validated),
        (ExecutionLifecycle.Validating,  ExecutionLifecycle.Invalid),   // validation rejected
        (ExecutionLifecycle.Validated,   ExecutionLifecycle.Executing),
        (ExecutionLifecycle.Executing,   ExecutionLifecycle.Succeeded),
        (ExecutionLifecycle.Executing,   ExecutionLifecycle.Failed),

        // Retry path — can resubmit from terminal states
        (ExecutionLifecycle.Failed,      ExecutionLifecycle.Submitted),
        (ExecutionLifecycle.Invalid,     ExecutionLifecycle.Submitted),
    };

    /// <summary>
    /// Attempt a lifecycle transition. Returns success/failure with reason.
    /// </summary>
    public static ExecutionLifecycleTransitionResult TryTransition(
        ExecutionLifecycle current, ExecutionLifecycle target)
    {
        if (_allowed.Contains((current, target)))
            return ExecutionLifecycleTransitionResult.Success(current, target);

        return ExecutionLifecycleTransitionResult.Failure(
            current, target,
            $"Execution lifecycle transition {current} → {target} is not permitted.");
    }

    /// <summary>Returns true when the target is in a pending or active state.</summary>
    public static bool IsActive(ExecutionLifecycle phase) =>
        phase is ExecutionLifecycle.Submitted
            or ExecutionLifecycle.Validating
            or ExecutionLifecycle.Validated
            or ExecutionLifecycle.Executing;

    /// <summary>Returns true when the target has reached a terminal state.</summary>
    public static bool IsTerminal(ExecutionLifecycle phase) =>
        phase is ExecutionLifecycle.Succeeded
            or ExecutionLifecycle.Failed
            or ExecutionLifecycle.Invalid;

    /// <summary>Returns true if this phase allows a retry/resubmit.</summary>
    public static bool CanRetry(ExecutionLifecycle phase) =>
        phase is ExecutionLifecycle.Failed or ExecutionLifecycle.Invalid;
}