using BattleLuck.Services.Runtime;
using VampireCommandFramework;

public static class RoadmapCommands
{
    [Command("roadmap.status", description: "Show the server roadmap and milestone status.", adminOnly: true)]
    public static void Status(ChatCommandContext ctx)
    {
        var service = BattleLuckPlugin.Roadmap;
        if (service == null)
        {
            ctx.Reply("Roadmap service is not initialized.");
            return;
        }

        var snapshot = service.GetSnapshot();
        ctx.Reply($"{snapshot.Project} roadmap: {snapshot.Milestones.Count} milestones (loaded {snapshot.LoadedAtUtc.ToLocalTime():g}).");
        foreach (var milestone in snapshot.Milestones)
            ctx.Reply($"[{milestone.Status}] {milestone.Id}: {milestone.Title}");

        if (!string.IsNullOrWhiteSpace(service.LastError))
            ctx.Reply($"Roadmap warning: {service.LastError}");
    }

    [Command("roadmap.show", description: "Show one roadmap milestone. Usage: .roadmap.show <milestoneId>", adminOnly: true)]
    public static void Show(ChatCommandContext ctx, string milestoneId)
    {
        var service = BattleLuckPlugin.Roadmap;
        var milestone = service?.FindMilestone(milestoneId);
        if (milestone == null)
        {
            ctx.Reply($"Unknown roadmap milestone '{milestoneId}'. Use .roadmap.status.");
            return;
        }

        ctx.Reply($"[{milestone.Status}] {milestone.Id}: {milestone.Title}");
        ctx.Reply(milestone.Summary);
        if (milestone.Dependencies.Count > 0)
            ctx.Reply($"Dependencies: {string.Join(", ", milestone.Dependencies)}");
        foreach (var acceptance in milestone.Acceptance)
            ctx.Reply($"  ✓ {acceptance}");
    }

    [Command("roadmap.prompt", description: "Show the server prompt contract. Usage: .roadmap.prompt <llm|developer>", adminOnly: true)]
    public static void Prompt(ChatCommandContext ctx, string roleId = "llm")
    {
        var service = BattleLuckPlugin.Roadmap;
        if (service == null)
        {
            ctx.Reply("Roadmap service is not initialized.");
            return;
        }

        var prompt = service.LoadRolePrompt(roleId);
        if (string.IsNullOrWhiteSpace(prompt))
        {
            ctx.Reply($"No prompt registered for role '{roleId}'.");
            return;
        }

        ctx.Reply($"Prompt role '{roleId}':");
        var lines = prompt.Split('\n');
        foreach (var line in lines.Take(18))
            ctx.Reply(line.TrimEnd('\r'));
        if (lines.Length > 18)
            ctx.Reply($"... {lines.Length - 18} more lines; see config/BattleLuck/prompts.");
    }

    [Command("roadmap.reload", description: "Reload roadmap.json and role prompts.", adminOnly: true)]
    public static void Reload(ChatCommandContext ctx)
    {
        var service = BattleLuckPlugin.Roadmap;
        if (service == null)
        {
            ctx.Reply("Roadmap service is not initialized.");
            return;
        }

        service.Reload();
        var snapshot = service.GetSnapshot();
        ctx.Reply($"Roadmap reloaded: {snapshot.Milestones.Count} milestone(s), {snapshot.Roles.Count} role prompt(s).");
    }
}
