namespace BattleLuck.Services.Planning;

using BattleLuck.Models;

/// <summary>
/// A planning strategy that generates NPC combat encounters based on player strength.
/// This contains the logic that would be moved from AdaptiveCombatDrillService.
/// </summary>
public sealed class CombatDrillStrategy : IPlanningStrategy
{
    public PlanningStrategyType StrategyType => PlanningStrategyType.CombatDrill;

    public Task<OperationResult<DeveloperPlan>> GeneratePlanAsync(PlanningRequest request)
    {
        var context = request.GetContext<CombatDrillContext>();

        // 1. Calculate threat budget based on player level and count.
        var budget = (context.AveragePlayerLevel * 1.5f) * context.PlayerCount;

        // 2. Select NPCs from a catalog to fit the budget.
        var steps = new List<DeveloperPlanStep>();
        if (budget > 150)
        {
            steps.Add(new DeveloperPlanStep("step-1", "npc.spawn", new Dictionary<string, string> { { "prefab", "CHAR_Undead_Priest" }, { "count", "1" } }, "Spawn a high-threat unit."));
        }
        if (budget > 50)
        {
            steps.Add(new DeveloperPlanStep("step-2", "npc.spawn", new Dictionary<string, string> { { "prefab", "CHAR_Undead_Skeleton_Warrior" }, { "count", "5" } }, "Spawn fodder units."));
        }
        if (steps.Count == 0)
        {
            steps.Add(new DeveloperPlanStep("step-1", "npc.spawn", new Dictionary<string, string> { { "prefab", "CHAR_Wildlife_Wolf" }, { "count", "2" } }, "Spawn basic wildlife."));
        }

        // The strategy creates a "proto-plan" that the calling service can finalize.
        var protoPlan = new DeveloperPlan(
            1, "", "", "", request.Goal,
            steps.ToArray(),
            new[] { "npc_count_within_budget", "all_npcs_catalogued" },
            Array.Empty<string>(),
            new[] { "npc.despawn_all" },
            false, "");

        return Task.FromResult(OperationResult<DeveloperPlan>.Ok(protoPlan));
    }
}
