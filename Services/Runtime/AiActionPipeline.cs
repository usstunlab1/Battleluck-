using BattleLuck.Models;
using BattleLuck.Services.Flow;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace BattleLuck.Services.Runtime;

/// <summary>Ordered validator chain for action intent validation and execution.</summary>
public sealed class AiActionPipeline
{
    readonly IActionRuntime _executor;
    readonly ActionManifestService _manifest;

    public AiActionPipeline(IActionRuntime executor)
    {
        _executor = executor ?? throw new ArgumentNullException(nameof(executor));
        _manifest = new ActionManifestService();
    }

    static string FormatParams(IReadOnlyDictionary<string, string>? p)
    {
        if (p == null || p.Count == 0)
            return "{}";
        var parts = new List<string>();
        foreach (var kv in p)
            parts.Add($"{kv.Key}={kv.Value}");
        return "{" + string.Join(", ", parts) + "}";
    }

    /// <summary>
    /// Validate and execute intent through ordered pipeline:
    /// 1. Action Registry Validation (is action registered)
    /// 2. Zone/Risk validation (zone constraints, AI rules)
    /// 3. Tech Validation (tech state constraints)
    /// 4. DryRunExecutor (ValidateOnly mode)
    /// 5. Executor (actual execution)
    /// </summary>
    public async Task<RuntimeActionReport> ExecuteAsync(
        RuntimeActionIntent intent,
        RuntimeActionContext context,
        bool dryRunOnly = false,
        CancellationToken cancellationToken = default)
    {
        var validationResults = new List<ValidationStageResult>();

        BattleLuckLogger.Info(
            $"[AiActionPipeline] ExecuteAsync start: action='{intent.ActionName}' " +
            $"source={intent.Source} params={FormatParams(intent.Parameters)} dryRun={dryRunOnly}");

        // Step 1: Check if action is registered
        if (!_executor.IsKnownAction(intent.ActionName))
        {
            validationResults.Add(new ValidationStageResult
            {
                Stage = ValidationStage.StaticConfig,
                Outcome = ValidationOutcome.Denied,
                Reason = $"Action '{intent.ActionName}' is not registered."
            });

            BattleLuckLogger.Warning(
                $"[AiActionPipeline] DENIED at StaticConfig: action '{intent.ActionName}' is not registered.");

            return new RuntimeActionReport
            {
                Intent = intent,
                ValidationResults = validationResults,
                ExecutionStatus = ExecutionStatus.DeniedByValidation,
                Validation = ValidationStatus.Rejected,
                Error = $"Action '{intent.ActionName}' is not registered."
            };
        }

        validationResults.Add(new ValidationStageResult
        {
            Stage = ValidationStage.StaticConfig,
            Outcome = ValidationOutcome.Passed
        });
        BattleLuckLogger.Debug($"[AiActionPipeline] PASSED StaticConfig: action '{intent.ActionName}' is registered.");

        // Step 2: Zone/Risk validation
        var zoneValidator = new ZoneValidator();
        var zoneValid = zoneValidator.Validate(intent, context, out var zoneError);
        if (!zoneValid)
        {
            validationResults.Add(new ValidationStageResult
            {
                Stage = ValidationStage.RuntimeState,
                Outcome = ValidationOutcome.Denied,
                Reason = zoneError
            });

            BattleLuckLogger.Warning(
                $"[AiActionPipeline] DENIED at RuntimeState (Zone): action='{intent.ActionName}' source={intent.Source} reason={zoneError}");

            return new RuntimeActionReport
            {
                Intent = intent,
                ValidationResults = validationResults,
                ExecutionStatus = ExecutionStatus.DeniedByValidation,
                Validation = ValidationStatus.Rejected,
                Error = zoneError
            };
        }

        validationResults.Add(new ValidationStageResult
        {
            Stage = ValidationStage.RuntimeState,
            Outcome = ValidationOutcome.Passed
        });
        BattleLuckLogger.Debug($"[AiActionPipeline] PASSED RuntimeState (Zone): action='{intent.ActionName}'.");

        // Step 3: Risk/Safety validation
        var riskValidator = new RiskValidator();
        var riskValid = riskValidator.Validate(intent, context, out var riskError);
        if (!riskValid)
        {
            validationResults.Add(new ValidationStageResult
            {
                Stage = ValidationStage.SafetyPolicy,
                Outcome = ValidationOutcome.Denied,
                Reason = riskError
            });

            BattleLuckLogger.Warning(
                $"[AiActionPipeline] DENIED at SafetyPolicy (Risk): action='{intent.ActionName}' source={intent.Source} reason={riskError}");

            return new RuntimeActionReport
            {
                Intent = intent,
                ValidationResults = validationResults,
                ExecutionStatus = ExecutionStatus.DeniedByValidation,
                Validation = ValidationStatus.RequiresConfirmation,
                Error = riskError
            };
        }

        validationResults.Add(new ValidationStageResult
        {
            Stage = ValidationStage.SafetyPolicy,
            Outcome = ValidationOutcome.Passed
        });
        BattleLuckLogger.Debug($"[AiActionPipeline] PASSED SafetyPolicy (Risk): action='{intent.ActionName}'.");

        // Step 4: Tech validation
        if (context.SessionContext?.State != null)
        {
            var actionTechValidator = new ActionTechValidator();
            var (techValid, _, techError) = actionTechValidator.ValidateTechForAction(intent, context);
            if (!techValid)
            {
                validationResults.Add(new ValidationStageResult
                {
                    Stage = ValidationStage.SafetyPolicy,
                    Outcome = ValidationOutcome.Denied,
                    Reason = techError
                });

                BattleLuckLogger.Warning(
                    $"[AiActionPipeline] DENIED at Tech: action='{intent.ActionName}' source={intent.Source} reason={techError}");

                return new RuntimeActionReport
                {
                    Intent = intent,
                    ValidationResults = validationResults,
                    ExecutionStatus = ExecutionStatus.DeniedByValidation,
                    Validation = ValidationStatus.Rejected,
                    Error = techError
                };
            }

            BattleLuckLogger.Debug($"[AiActionPipeline] PASSED Tech: action='{intent.ActionName}'.");
        }
        else
        {
            BattleLuckLogger.Debug($"[AiActionPipeline] SKIPPED Tech: no session tech state present.");
        }

        // Step 5: Dry run if requested
        if (dryRunOnly)
        {
            BattleLuckLogger.Info($"[AiActionPipeline] Dry-run requested; validating only for action='{intent.ActionName}'.");
            return await _executor.ValidateOnlyAsync(intent, context, cancellationToken);
        }

        // Step 6: Actual execution
        BattleLuckLogger.Info($"[AiActionPipeline] All stages passed; executing action='{intent.ActionName}' source={intent.Source}.");
        var report = await _executor.ExecuteAsync(intent, context, cancellationToken);
        BattleLuckLogger.Info(
            $"[AiActionPipeline] Execution complete: action='{intent.ActionName}' " +
            $"status={report.ExecutionStatus} validation={report.Validation} error={(report.Error ?? "none")}");
        return report;
    }

    /// <summary>Validate only without executing.</summary>
    public async Task<RuntimeActionReport> ValidateOnlyAsync(
        RuntimeActionIntent intent,
        RuntimeActionContext context,
        CancellationToken cancellationToken = default)
    {
        return await ExecuteAsync(intent, context, dryRunOnly: true, cancellationToken);
    }
}

/// <summary>Zone validation for action constraints and AI rules.</summary>
public sealed class ZoneValidator
{
    public bool Validate(RuntimeActionIntent intent, RuntimeActionContext context, out string? error)
    {
        error = null;

        // Check zone context
        if (context.ZoneDefinition != null)
        {
            // Check zone-wide blocked actions
            if (!string.IsNullOrEmpty(intent.ActionName) &&
                context.ZoneDefinition.BlockedActions?.Count > 0)
            {
                var actionName = intent.ActionName.Split(':')[0].Trim();
                foreach (var blocked in context.ZoneDefinition.BlockedActions)
                {
                    if (actionName.Equals(blocked, StringComparison.OrdinalIgnoreCase) ||
                        actionName.StartsWith(blocked.Split('.')[0], StringComparison.OrdinalIgnoreCase))
                    {
                        error = $"Action '{intent.ActionName}' is blocked in zone '{context.ZoneDefinition.Name}'.";
                        return false;
                    }
                }
            }

            // Check AI-specific rules if present
            if (context.ZoneDefinition.AiRules != null && intent.Source == ActionSource.AI)
            {
                var aiRules = context.ZoneDefinition.AiRules;
                var actionName = intent.ActionName.Split(':')[0].Trim();

                // Blocked actions take precedence
                if (aiRules.BlockedActions?.Count > 0)
                {
                    foreach (var blocked in aiRules.BlockedActions)
                    {
                        if (actionName.Equals(blocked, StringComparison.OrdinalIgnoreCase) ||
                            actionName.StartsWith(blocked.Split('.')[0], StringComparison.OrdinalIgnoreCase))
                        {
                            error = $"AI action '{intent.ActionName}' is blocked by zone AI rules.";
                            return false;
                        }
                    }
                }

                // Allowed actions list restricts if non-empty
                if (aiRules.AllowedActions?.Count > 0)
                {
                    bool isAllowed = false;
                    foreach (var allowed in aiRules.AllowedActions)
                    {
                        if (actionName.Equals(allowed, StringComparison.OrdinalIgnoreCase) ||
                            actionName.StartsWith(allowed.Split('.')[0], StringComparison.OrdinalIgnoreCase))
                        {
                            isAllowed = true;
                            break;
                        }
                    }

                    if (!isAllowed)
                    {
                        error = $"AI action '{intent.ActionName}' is not in zone allowed actions list.";
                        return false;
                    }
                }

                // Check if AI can execute autonomously
                if (!aiRules.AllowAutonomousExecution)
                {
                    error = $"AI autonomous execution is disabled in zone '{context.ZoneDefinition.Name}'.";
                    return false;
                }
            }
        }

        return true;
    }
}

/// <summary>Risk validation for action safety and approval requirements.</summary>
public sealed class RiskValidator
{
    /// <summary>
    /// Validates action risk against source permissions.
    /// Returns false if the action requires approval and source is not authorized.
    /// </summary>
    public bool Validate(RuntimeActionIntent intent, RuntimeActionContext context, out string? error)
    {
        error = null;

        // Get risk level from manifest
        var actionName = intent.ActionName.Split(':')[0].Trim();
        var validation = new ActionManifestService().Validate(new EventActionDefinition { Action = actionName });
        
        var requiresApproval = !validation.Success || 
            (intent.Source == ActionSource.AI || intent.Source == ActionSource.Webhook) &&
            !IsSafeAction(actionName);

        // Check if action requires admin approval
        if (requiresApproval && intent.Source != ActionSource.Admin)
        {
            // Check if source has admin privileges (for now, only Admin source is allowed)
            error = $"Action '{intent.ActionName}' requires admin approval (risk: controlled/destructive).";
            return false;
        }

        return true;
    }

    /// <summary>Determines if an action is universally safe for any source.</summary>
    static bool IsSafeAction(string actionName)
    {
        var safeActions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "announce", "notification", "notify", "send_message",
            "query", "count", "timer.start", "timer.stop",
            "system.find", "system.search", "prefab.query"
        };

        return actionName.StartsWith("query", StringComparison.Ordinal) ||
               safeActions.Contains(actionName);
    }
}

/// <summary>Tech validation for action constraints (standalone validator).</summary>
public sealed class ActionTechValidator
{
    /// <summary>
    /// Validates tech constraints for a specific action.
    /// </summary>
    public (bool Valid, TechDefinition? ConflictingTech, string? Error) ValidateTechForAction(
        RuntimeActionIntent intent,
        RuntimeActionContext context)
    {
        // Check if action references tech-restricted operations
        var actionName = intent.ActionName.Split(':')[0].Trim();
        
        // Load active techs for this session
        if (context.ZoneDefinition?.AiRules?.AllowedTechs?.Count > 0 &&
            intent.Source == ActionSource.AI)
        {
            // AI can only use techs that are in the allowed list
            var techId = intent.Parameters.GetValueOrDefault("techId", "") ?? "";
            if (!string.IsNullOrWhiteSpace(techId) &&
                !context.ZoneDefinition.AiRules.AllowedTechs.Contains(techId, StringComparer.OrdinalIgnoreCase))
            {
                return (false, null, $"Tech '{techId}' is not in zone allowed techs list.");
            }
        }

        return (true, null, null);
    }
}
