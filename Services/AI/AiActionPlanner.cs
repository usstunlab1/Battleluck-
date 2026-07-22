using System.Collections.Generic;
using System.Text;
using System.Text.Json;

namespace BattleLuck.Services.AI;

public sealed class AiActionPlanStep
{
    public string Action { get; set; } = "";
    public string Reason { get; set; } = "";
    public float Confidence { get; set; }
}

public sealed class AiActionPlan
{
    public List<AiActionPlanStep> Steps { get; set; } = new();
    public string Raw { get; set; } = "";
}

/// <summary>
/// PLAN stage of the AI action lifecycle. Given a goal (plus recent conversation
/// history) and the catalog-backed game-system universe, asks the LLM to produce
/// a step-by-step plan of actions. An invalid response produces no action; raw
/// user text is never treated as an executable fallback action.
/// </summary>
public sealed class AiActionPlanner
{
    readonly GameSystemsActionSource _source;
    readonly ActionManifestService _manifest = new();

    public AiActionPlanner(GameSystemsActionSource source) => _source = source;

    public async Task<AiActionPlan> GeneratePlanAsync(string goal, string history)
    {
        var prompt = new StringBuilder();
        prompt.AppendLine("You are the action planner for a V Rising game-server admin assistant.");
        prompt.AppendLine("Given the player's goal, produce a STEP-BY-STEP PLAN as a JSON array of actions.");
        prompt.AppendLine("Each step: {\"action\":\"action.name|key=value\", \"reason\":\"...\", \"confidence\":0.0}.");
        prompt.AppendLine("Only use actions from the available systems below. Keep steps atomic and safe.");
        prompt.AppendLine("Timing belongs in a reusable sequence: use wait:<seconds> and tick:<event-second> markers when a task is later converted to a sequence.");
        prompt.AppendLine();
        prompt.AppendLine(_source.GetPlanningContext());
        if (!string.IsNullOrWhiteSpace(history))
        {
            prompt.AppendLine();
            prompt.AppendLine("Recent conversation:");
            prompt.AppendLine(history);
        }
        prompt.AppendLine();
        prompt.AppendLine($"Player goal: {goal}");
        prompt.AppendLine("Reply with ONLY the JSON plan array.");

        var reply = await QueryLlmAsync(prompt.ToString()).ConfigureAwait(false);
        var plan = ParsePlan(reply);
        plan.Steps = plan.Steps
            .Where(step => _manifest.Validate(new EventActionDefinition { Action = step.Action }).Success)
            .Take(32)
            .ToList();
        plan.Raw = reply ?? "";
        return plan;
    }

    async Task<string?> QueryLlmAsync(string prompt)
    {
        var assistant = BattleLuckPlugin.AIAssistant;
        if (assistant == null)
            return null;
        return await assistant.HandleDirectQuery(0, prompt, source: "planner", broadcastToInGameChat: false)
            .ConfigureAwait(false);
    }

    static AiActionPlan ParsePlan(string? text)
    {
        var plan = new AiActionPlan();
        if (string.IsNullOrWhiteSpace(text))
            return plan;

        try
        {
            var json = ExtractJsonArray(text);
            if (json == null)
                return plan;

            // LLMs sometimes escape quotes in JSON responses; normalize them.
            json = json.Replace("\\\"", "\"");

            var steps = JsonSerializer.Deserialize<List<AiActionPlanStep>>(json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (steps != null)
                plan.Steps = steps.FindAll(s => !string.IsNullOrWhiteSpace(s.Action));
        }
        catch
        {
            // Keep an empty plan; the caller's fallback handles it.
        }
        return plan;
    }

    static string? ExtractJsonArray(string text)
    {
        var start = text.IndexOf('[');
        var end = text.LastIndexOf(']');
        return start >= 0 && end > start ? text.Substring(start, end - start + 1) : null;
    }
}
