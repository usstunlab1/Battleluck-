// Models/ExecutionContext.cs
// Holds the full state for one action execution from submission to completion.
//
// ExecutionContext is created by ExecutionPipeline when it receives a target,
// and its Lifecycle field is updated at each stage. It carries all context
// needed to route, validate, execute, and report the result.

namespace BattleLuck.Models;

/// <summary>
/// Full state for a single action execution.
/// Instances are created by <see cref="Core.ExecutionPipeline"/> and tracked
/// until they reach a terminal lifecycle phase.
/// </summary>
public sealed class ExecutionContext
{
    /// <summary>Unique identifier for this execution.</summary>
    public string ExecutionId { get; init; } = Guid.NewGuid().ToString("N");

    /// <summary>The AI directive that triggered this execution (if any).</summary>
    public AiGroupProjectMDirective? Directive { get; init; }

    /// <summary>The raw action string (e.g., "npc.aggro:prefab=vampire_bat").</summary>
    public string? ActionString { get; init; }

    /// <summary>The parsed action name (e.g., "npc.aggro").</summary>
    public string? ActionName { get; set; }

    /// <summary>The LLM group selected for this execution (e.g., "combat", "planning").</summary>
    public string? SelectedGroup { get; set; }

    /// <summary>The model that was selected within the LLM group (e.g., "llama-3.2-3b").</summary>
    public string? SelectedModel { get; set; }

    /// <summary>The prompt sent to the LLM (if any).</summary>
    public string? Prompt { get; set; }

    /// <summary>The raw text response from the LLM.</summary>
    public string? LlmResponse { get; set; }

    /// <summary>Confidence score from the directive or LLM (0.0 – 1.0).</summary>
    public float Confidence { get; set; }

    /// <summary>Current lifecycle phase.</summary>
    public ExecutionLifecycle Lifecycle { get; set; } = ExecutionLifecycle.Submitted;

    /// <summary>Error message if the execution failed or was rejected.</summary>
    public string? Error { get; set; }

    /// <summary>UTC timestamp when this execution was created.</summary>
    public DateTime CreatedUtc { get; init; } = DateTime.UtcNow;

    /// <summary>UTC timestamp of the last lifecycle transition.</summary>
    public DateTime LastTransitionUtc { get; set; } = DateTime.UtcNow;

    /// <summary>UTC timestamp when execution completed (terminal phase reached).</summary>
    public DateTime? CompletedUtc { get; set; }

    /// <summary>
    /// The resulting OperationResult from running the action in FlowActionExecutor.
    /// Populated after the Executing phase.
    /// </summary>
    public OperationResult? ActionResult { get; set; }

    /// <summary>
    /// Number of retry attempts (incremented each time Failed → Submitted).
    /// </summary>
    public int RetryCount { get; set; }

    /// <summary>
    /// The game session context at the time of submission (optional, for enrichment).
    /// </summary>
    public BattleAiSessionContextDto? SessionContext { get; init; }

    /// <summary>The player context at the time of submission (optional).</summary>
    public BattleAiPlayerContextDto? PlayerContext { get; init; }

    /// <summary>Returns true if the lifecycle is in an active (non-terminal) phase.</summary>
    public bool IsActive => ExecutionLifecycleStateMachine.IsActive(Lifecycle);

    /// <summary>Returns true if the lifecycle is in a terminal phase.</summary>
    public bool IsTerminal => ExecutionLifecycleStateMachine.IsTerminal(Lifecycle);

    /// <summary>Returns true if the execution can be retried.</summary>
    public bool CanRetry => ExecutionLifecycleStateMachine.CanRetry(Lifecycle);

    /// <summary>
    /// Attempt to transition to a new lifecycle phase.
    /// Updates timestamps on success.
    /// </summary>
    public ExecutionLifecycleTransitionResult TransitionTo(ExecutionLifecycle target)
    {
        var result = ExecutionLifecycleStateMachine.TryTransition(Lifecycle, target);
        if (result.Succeeded)
        {
            Lifecycle = target;
            LastTransitionUtc = DateTime.UtcNow;

            if (ExecutionLifecycleStateMachine.IsTerminal(target))
                CompletedUtc = DateTime.UtcNow;
        }
        return result;
    }
}