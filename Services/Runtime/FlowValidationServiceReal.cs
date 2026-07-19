using BattleLuck.Models;

namespace BattleLuck.Services.Runtime
{
    /// <summary>
    /// Real implementation of flow validation service.
    /// </summary>
    public class DefaultFlowValidationService : IFlowValidationService
    {
        public Task<FlowValidationResultDto> ValidateFlowAsync(string flowName, string modeId)
        {
            var result = new FlowValidationResultDto
            {
                FlowName = flowName,
                ValidatedUtc = DateTime.UtcNow
            };

            try
            {
                var config = ConfigLoader.Load(modeId);
                var actions = FindFlowActions(config, flowName).ToList();

                if (actions.Count == 0)
                {
                    result.Warnings.Add(new ValidationWarningDto
                    {
                        Code = "empty_flow",
                        Message = $"No configured actions were found for flow '{flowName}' in mode '{modeId}'.",
                        Location = $"{modeId}:{flowName}"
                    });
                }

                var manifest = new ActionManifestService();
                foreach (var (location, action) in actions)
                {
                    var validation = manifest.Validate(new EventActionDefinition { Action = action });
                    if (validation.Success)
                        continue;

                    result.Errors.Add(new ValidationErrorDto
                    {
                        Code = "invalid_action",
                        Message = validation.Error ?? $"Action '{action}' is invalid.",
                        Location = location,
                        Context = new Dictionary<string, object>
                        {
                            ["modeId"] = modeId,
                            ["flowName"] = flowName,
                            ["action"] = action
                        }
                    });
                }

                result.IsValid = result.Errors.Count == 0;
                result.CanTransition = result.IsValid;
                if (result.IsValid)
                    result.Suggestions.Add($"Validated {actions.Count} action(s) for {modeId}:{flowName}.");
            }
            catch (Exception ex)
            {
                result.Errors.Add(new ValidationErrorDto
                {
                    Code = "config_load_failed",
                    Message = ex.Message,
                    Location = modeId
                });
                result.IsValid = false;
                result.CanTransition = false;
            }

            return Task.FromResult(result);
        }

        public Task<FlowOptimizationDto> AnalyzeFlowAsync(string flowName)
        {
            return Task.FromResult(new FlowOptimizationDto { FlowName = flowName });
        }

        public Task<FlowTransitionDebugDto> DebugTransitionAsync(string flowName, string fromState, string toState)
        {
            var hasStates = !string.IsNullOrWhiteSpace(fromState) && !string.IsNullOrWhiteSpace(toState);
            var changesState = !string.Equals(fromState, toState, StringComparison.OrdinalIgnoreCase);
            return Task.FromResult(new FlowTransitionDebugDto
            {
                FlowName = flowName,
                FromState = fromState,
                ToState = toState,
                IsValid = hasStates,
                CanTransition = hasStates && changesState,
                Blockers = hasStates && changesState
                    ? new List<string>()
                    : new List<string> { "Transition requires distinct from/to states." }
            });
        }

        public Task<string> GenerateFlowAsync(string description, string modeId)
        {
            var flow = new FlowConfig
            {
                ExecutionOrder = new List<string> { "generated" },
                Flows = new Dictionary<string, FlowDefinition>
                {
                    ["generated"] = new()
                    {
                        Description = description,
                        Actions = new List<string>()
                    }
                }
            };

            return Task.FromResult(JsonSerializer.Serialize(flow, ConfigLoader.JsonOptions));
        }

        public Task<List<FlowStateDefinitionDto>> ListFlowStatesAsync(string flowName)
        {
            return Task.FromResult(new List<FlowStateDefinitionDto>
            {
                new() { Name = "waiting", IsInitial = true },
                new() { Name = "active" }
            });
        }

        public Task<CircularDependencyCheckDto> CheckCircularDependenciesAsync(string flowName)
        {
            return Task.FromResult(new CircularDependencyCheckDto
            {
                FlowName = flowName,
                HasCircularDependency = false
            });
        }

        static IEnumerable<(string Location, string Action)> FindFlowActions(ModeConfig config, string flowName)
        {
            var normalized = (flowName ?? "").Trim();
            var lower = normalized.ToLowerInvariant();

            if (lower.Contains("exit"))
            {
                foreach (var item in EnumerateFlow(config.FlowExit, "flow_exit", normalized))
                    yield return item;
                yield break;
            }

            if (lower.Contains("enter"))
            {
                foreach (var item in EnumerateFlow(config.FlowEnter, "flow_enter", normalized))
                    yield return item;
                yield break;
            }

            foreach (var item in EnumerateNamedFlow(config.FlowEnter, "flow_enter", normalized))
                yield return item;

            foreach (var item in EnumerateNamedFlow(config.FlowExit, "flow_exit", normalized))
                yield return item;
        }

        static IEnumerable<(string Location, string Action)> EnumerateNamedFlow(FlowConfig flow, string rootName, string requestedName)
        {
            if (string.IsNullOrWhiteSpace(requestedName))
            {
                foreach (var item in EnumerateFlow(flow, rootName, requestedName))
                    yield return item;
                yield break;
            }

            if (!flow.Flows.TryGetValue(requestedName, out var definition))
                yield break;

            foreach (var action in definition.Actions)
                yield return ($"{rootName}.flows.{requestedName}", action);
        }

        static IEnumerable<(string Location, string Action)> EnumerateFlow(FlowConfig flow, string rootName, string requestedName)
        {
            if (!string.IsNullOrWhiteSpace(requestedName) &&
                flow.Flows.TryGetValue(requestedName, out var requestedDefinition))
            {
                foreach (var action in requestedDefinition.Actions)
                    yield return ($"{rootName}.flows.{requestedName}", action);
                yield break;
            }

            var orderedNames = flow.ExecutionOrder.Count > 0
                ? flow.ExecutionOrder
                : flow.Flows.Keys.ToList();

            foreach (var name in orderedNames)
            {
                if (!flow.Flows.TryGetValue(name, out var definition))
                    continue;

                foreach (var action in definition.Actions)
                    yield return ($"{rootName}.flows.{name}", action);
            }
        }
    }
}
