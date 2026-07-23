namespace BattleLuck.Services.Planning;

using BattleLuck.Models;

/// <summary>
/// A planning strategy that generates a developer plan based on a goal and a security manifest.
/// This contains the logic moved from AiDeveloperBridge.
/// </summary>
public sealed class DeveloperBridgeStrategy : IPlanningStrategy
{
    public PlanningStrategyType StrategyType => PlanningStrategyType.DeveloperBridge;

    public Task<OperationResult<DeveloperPlan>> GeneratePlanAsync(PlanningRequest request)
    {
        var context = request.GetContext<DeveloperBridgeContext>();
        var manifest = context.Manifest;
        var goal = request.Goal;

        var goalTokens = goal.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(t => t.ToLowerInvariant()).ToHashSet();

        var steps = manifest.Actions
            .Where(action => goalTokens.Any(token => action.Contains(token, StringComparison.OrdinalIgnoreCase)))
            .Take(Math.Min(manifest.Limits.MaxActions, 5))
            .Select((action, index) => new DeveloperPlanStep($"step-{index + 1}", action, new Dictionary<string, string>(), $"Selected action '{action}' based on goal."))
            .ToArray();

        var protoPlan = new DeveloperPlan(
            1, "", "", "", goal,
            steps,
            new[] { "action_count_within_limit", "all_actions_catalogued", "cleanup_declared" },
            new[] { "NPC actions require validated prefab and target parameters before execution" },
            new[] { "dev.entities.destroy", "player.snapshot.restore" },
            false, "");

        return Task.FromResult(OperationResult<DeveloperPlan>.Ok(protoPlan));
    }
}
