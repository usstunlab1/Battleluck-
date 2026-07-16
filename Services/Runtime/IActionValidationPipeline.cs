// Services/Runtime/IActionValidationPipeline.cs
// Three-stage ordered validation contract.
//
// Pure C# — no BepInEx/VRising/Unity references.

namespace BattleLuck.Services.Runtime;

/// <summary>
/// Ordered three-stage action validation pipeline.
///
/// <para>Stages run in order: static config → runtime state → safety/policy.
/// A <see cref="ValidationOutcome.Denied"/> result in any stage short-circuits
/// the remaining stages.</para>
/// </summary>
public interface IActionValidationPipeline
{
    /// <summary>
    /// Run all three validation stages for <paramref name="intent"/> within
    /// <paramref name="context"/>.
    /// Returns a result list with one entry per stage that was reached.
    /// </summary>
    Task<IReadOnlyList<ValidationStageResult>> ValidateAsync(
        RuntimeActionIntent intent,
        RuntimeActionContext context,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// A single validation stage handler, composable via the pipeline.
/// </summary>
public interface IValidationStage
{
    ValidationStage Stage { get; }

    Task<ValidationStageResult> RunAsync(
        RuntimeActionIntent intent,
        RuntimeActionContext context,
        CancellationToken cancellationToken = default);
}
