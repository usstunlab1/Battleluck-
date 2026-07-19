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
    private readonly ActionManifestService _manifest;

    public ActionValidationPipeline(ActionManifestService manifest)
    {
        _manifest = manifest ?? throw new ArgumentNullException(nameof(manifest));
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
        _manifest.Entries.TryGetValue(intent.ActionName, out var entry);

        // ── Stage 1: Runtime state ────────────────────────────────────────────
        var runtimeResult = RunRuntimeState(intent, context, entry);
        results.Add(runtimeResult);

        if (runtimeResult.Outcome == ValidationOutcome.Denied)
            return Task.FromResult<IReadOnlyList<ValidationStageResult>>(results);

        // ── Stage 2: Safety / policy ──────────────────────────────────────────
        var policyResult = RunSafetyPolicy(intent, entry);
        results.Add(policyResult);

        return Task.FromResult<IReadOnlyList<ValidationStageResult>>(results);
    }

    // ── Stage implementations ─────────────────────────────────────────────────

    private ValidationStageResult RunStaticConfig(RuntimeActionIntent intent)
    {
        if (string.IsNullOrWhiteSpace(intent.ActionName))
            return Denied(ValidationStage.StaticConfig, "Action name is empty or whitespace.");

        if (!_manifest.Entries.TryGetValue(intent.ActionName, out var entry))
            return Denied(ValidationStage.StaticConfig,
                $"Unknown action '{intent.ActionName}'. Not present in action catalog.");

        // Server-only contract gate: check availability and executable flags
        // via the manifest entry.
        if (!entry.IsServerContractValid)
        {
            return Denied(ValidationStage.StaticConfig,
                $"Action '{intent.ActionName}' does not satisfy the server-only action contract: "
                + entry.ServerContractViolationReason);
        }

        // ── Argument validation: required parameters ──────────────────────
        var missing = new List<string>();
        foreach (var required in entry.Required)
        {
            if (!intent.Parameters.TryGetValue(required, out var value) ||
                string.IsNullOrWhiteSpace(value))
            {
                // Check if a default exists in the manifest entry.
                if (!entry.Defaults.ContainsKey(required))
                    missing.Add(required);
            }
        }

        if (missing.Count > 0)
        {
            return Denied(ValidationStage.StaticConfig,
                $"Action '{intent.ActionName}' is missing required parameter(s): "
                + string.Join(", ", missing.Select(p => $"'{p}'"))
                + ".");
        }

        return Passed(ValidationStage.StaticConfig);
    }

    private ValidationStageResult RunRuntimeState(
        RuntimeActionIntent intent,
        RuntimeActionContext context,
        ActionManifestEntry? entry)
    {
        if (entry == null)
            return Skipped(ValidationStage.RuntimeState, "No entry; already denied by static config.");

        // Resolve the current session phase from context extras, if available.
        if (!context.Extras.TryGetValue("sessionPhase", out var phaseObj) || phaseObj == null)
        {
            // No phase info — skip phase check; the action may still be denied by policy.
            // This covers global (session-less) actions.
            if (entry.AllowedPhases.HasFlag(SessionPhaseAllowance.NoSession))
                return Passed(ValidationStage.RuntimeState);

            // If the entry requires a specific session phase but none is
            // available, we conservatively allow it (state not checkable).
            return Passed(ValidationStage.RuntimeState);
        }

        if (phaseObj is SessionPhaseAllowance phaseFlag)
        {
            if (!entry.IsPhaseAllowed(phaseFlag))
                return Denied(ValidationStage.RuntimeState,
                    $"Action '{intent.ActionName}' is not permitted in phase '{phaseFlag}'.");
        }

        return Passed(ValidationStage.RuntimeState);
    }

    private ValidationStageResult RunSafetyPolicy(
        RuntimeActionIntent intent,
        ActionManifestEntry? entry)
    {
        if (entry == null)
            return Skipped(ValidationStage.SafetyPolicy, "No entry; already denied by static config.");

        // Source permission check.
        if (!entry.IsSourceAcknowledged(intent.Source))
            return Denied(ValidationStage.SafetyPolicy,
                $"Source '{intent.Source}' is not permitted to call action '{intent.ActionName}'.");

        // Confirmation gate: source requires confirmation.
        if (entry.RequiresConfirmation(intent.Source))
            return new ValidationStageResult
            {
                Stage = ValidationStage.SafetyPolicy,
                Outcome = ValidationOutcome.RequiresConfirmation,
                Reason = $"Action '{intent.ActionName}' requires explicit confirmation from source '{intent.Source}'.",
            };

        // AI auto-run safety: mutating high-risk actions from AI must not auto-execute.
        if (intent.Source == ActionSource.AI
            && entry.IsMutating
            && entry.RiskLevel.Equals("destructive", StringComparison.OrdinalIgnoreCase))
        {
            return new ValidationStageResult
            {
                Stage = ValidationStage.SafetyPolicy,
                Outcome = ValidationOutcome.RequiresConfirmation,
                Reason = $"AI-originated action '{intent.ActionName}' has risk level "
                       + $"'{entry.RiskLevel}' and requires confirmation.",
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
