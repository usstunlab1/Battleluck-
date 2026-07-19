// Services/Runtime/ActionValidationPipeline.cs
// Default three-stage validation pipeline implementation.
//
// Stage 0 — StaticConfig  : action must be registered; parameters must be present.
// Stage 1 — RuntimeState  : session phase must permit this action.
// Stage 2 — SafetyPolicy  : source must be permitted; high-risk AI actions need confirmation.
//
// Pure C# — no BepInEx/VRising/Unity references.

namespace BattleLuck.Services.Runtime;

/// <summary>
/// Concrete implementation of <see cref="IActionValidationPipeline"/>.
///
/// Runs stages in order; short-circuits on the first <see cref="ValidationOutcome.Denied"/>.
/// </summary>
public sealed class ActionValidationPipeline : IActionValidationPipeline
{
    private readonly CapabilityRegistry _registry;

    public ActionValidationPipeline(CapabilityRegistry registry)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
    }

    public Task<IReadOnlyList<ValidationStageResult>> ValidateAsync(
        RuntimeActionIntent intent,
        RuntimeActionContext context,
        CancellationToken cancellationToken = default)
    {
        var results = new List<ValidationStageResult>(3);

        // ── Stage 0: Static config ────────────────────────────────────────────
        var staticResult = RunStaticConfig(intent);
        results.Add(staticResult);

        if (staticResult.Outcome == ValidationOutcome.Denied)
            return Task.FromResult<IReadOnlyList<ValidationStageResult>>(results);

        // Resolve capability descriptor for the remaining stages.
        // Null here means the action is unknown — already denied above.
        var descriptor = _registry.TryGet(intent.ActionName);

        // ── Stage 1: Runtime state ────────────────────────────────────────────
        var runtimeResult = RunRuntimeState(intent, context, descriptor);
        results.Add(runtimeResult);

        if (runtimeResult.Outcome == ValidationOutcome.Denied)
            return Task.FromResult<IReadOnlyList<ValidationStageResult>>(results);

        // ── Stage 2: Safety / policy ──────────────────────────────────────────
        var policyResult = RunSafetyPolicy(intent, descriptor);
        results.Add(policyResult);

        return Task.FromResult<IReadOnlyList<ValidationStageResult>>(results);
    }

    // ── Stage implementations ─────────────────────────────────────────────────

    private ValidationStageResult RunStaticConfig(RuntimeActionIntent intent)
    {
        if (string.IsNullOrWhiteSpace(intent.ActionName))
            return Denied(ValidationStage.StaticConfig, "Action name is empty or whitespace.");

        if (!_registry.IsRegistered(intent.ActionName))
            return Denied(ValidationStage.StaticConfig,
                $"Unknown action '{intent.ActionName}'. Not present in capability registry.");

        return Passed(ValidationStage.StaticConfig);
    }

    private ValidationStageResult RunRuntimeState(
        RuntimeActionIntent intent,
        RuntimeActionContext context,
        ActionCapabilityDescriptor? descriptor)
    {
        if (descriptor == null)
            return Skipped(ValidationStage.RuntimeState, "No descriptor; already denied by static config.");

        // Resolve the current session phase from context extras, if available.
        if (!context.Extras.TryGetValue("sessionPhase", out var phaseObj) || phaseObj == null)
        {
            // No phase info — skip phase check; the action may still be denied by policy.
            // This covers global (session-less) actions.
            if (descriptor.AllowedPhases.HasFlag(SessionPhaseAllowance.NoSession))
                return Passed(ValidationStage.RuntimeState);

            // If the descriptor requires a specific session phase but none is
            // available, we conservatively allow it (state not checkable).
            return Passed(ValidationStage.RuntimeState);
        }

        if (phaseObj is SessionPhaseAllowance phaseFlag)
        {
            if (!descriptor.IsPhaseAllowed(phaseFlag))
                return Denied(ValidationStage.RuntimeState,
                    $"Action '{intent.ActionName}' is not permitted in phase '{phaseFlag}'.");
        }

        return Passed(ValidationStage.RuntimeState);
    }

    private ValidationStageResult RunSafetyPolicy(
        RuntimeActionIntent intent,
        ActionCapabilityDescriptor? descriptor)
    {
        if (descriptor == null)
            return Skipped(ValidationStage.SafetyPolicy, "No descriptor; already denied by static config.");

        // Source permission check.
        if (!descriptor.IsSourceAcknowledged(intent.Source))
            return Denied(ValidationStage.SafetyPolicy,
                $"Source '{intent.Source}' is not permitted to call action '{intent.ActionName}'.");

        // Confirmation gate: source requires confirmation.
        if (descriptor.RequiresConfirmation(intent.Source))
            return new ValidationStageResult
            {
                Stage = ValidationStage.SafetyPolicy,
                Outcome = ValidationOutcome.RequiresConfirmation,
                Reason = $"Action '{intent.ActionName}' requires explicit confirmation from source '{intent.Source}'.",
            };

        // AI auto-run safety: mutating high-risk actions from AI must not auto-execute.
        if (intent.Source == ActionSource.AI
            && descriptor.IsMutating
            && descriptor.RiskLevel >= ActionRiskLevel.High)
        {
            return new ValidationStageResult
            {
                Stage = ValidationStage.SafetyPolicy,
                Outcome = ValidationOutcome.RequiresConfirmation,
                Reason = $"AI-originated action '{intent.ActionName}' has risk level "
                       + $"'{descriptor.RiskLevel}' and requires confirmation.",
            };
        }

        return Passed(ValidationStage.SafetyPolicy);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static ValidationStageResult Passed(ValidationStage stage) =>
        new() { Stage = stage, Outcome = ValidationOutcome.Passed };

    private static ValidationStageResult Denied(ValidationStage stage, string reason) =>
        new() { Stage = stage, Outcome = ValidationOutcome.Denied, Reason = reason };

    private static ValidationStageResult Skipped(ValidationStage stage, string? reason = null) =>
        new() { Stage = stage, Outcome = ValidationOutcome.Skipped, Reason = reason };
}
