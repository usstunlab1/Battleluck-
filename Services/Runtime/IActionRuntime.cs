// Services/Runtime/IActionRuntime.cs
// The single canonical execution entry point for all action requests.
//
// All callers — commands, AI, webhooks, MCP, EventRuntimeController —
// must go through this interface.

using BattleLuck.Models;
using BattleLuck.Services.Flow;

namespace BattleLuck.Services.Runtime;

/// <summary>
/// Canonical action execution pipeline.
///
/// <para>
/// Implementations must:
/// <list type="number">
///   <item>Run the full <see cref="IActionValidationPipeline"/> (static config →
///         runtime state → safety/policy) before executing.</item>
///   <item>Return a <see cref="RuntimeActionReport"/> regardless of outcome so
///         callers can log, trace, or surface errors consistently.</item>
///   <item>Vever throw — unexpected errors must be caught and reflected in the
///         returned report with <see cref="ExecutionStatus.Failed"/>.</item>
/// </list>
/// </para>
/// </summary>
public interface IActionRuntime
{
    /// <summary>
    /// Validate and execute the given <paramref name="intent"/> within
    /// <paramref name="context"/>, returning a full <see cref="RuntimeActionReport"/>.
    /// </summary>
    Task<RuntimeActionReport> ExecuteAsync(
        RuntimeActionIntent intent,
        RuntimeActionContext context,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Synchronous version of ExecuteAsync for backward compatibility.
    /// </summary>
    RuntimeActionReport Execute(RuntimeActionIntent intent, RuntimeActionContext context);

    /// <summary>
    /// Validate the intent without executing it.
    /// Useful for dry-run checks and admin confirmation prompts.
    /// </summary>
    Task<RuntimeActionReport> ValidateOnlyAsync(
        RuntimeActionIntent intent,
        RuntimeActionContext context,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns true when an action with the given <paramref name="actionName"/>
    /// is registered and can be resolved.
    /// </summary>
    bool IsKnownAction(string actionName);
}
