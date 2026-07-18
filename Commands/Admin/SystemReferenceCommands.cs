using BattleLuck.Commands;

namespace BattleLuck.Commands.Admin;

public static class SystemReferenceCommands
{
    static readonly KindredSystemReferenceService Reference = new();

    [BattleLuckCommand("bl.system.find", description: "Ask AI to find ECS/ProjectM/Unity/EOS systems by description. Usage: .bl.system.find <description>", adminOnly: true)]
    public static async Task FindSystem(
        BattleLuckCommandContext ctx,
        string description,
        string p2 = "",
        string p3 = "",
        string p4 = "",
        string p5 = "",
        string p6 = "",
        string p7 = "",
        string p8 = "")
    {
        var fullDescription = JoinArgs(description, p2, p3, p4, p5, p6, p7, p8);
        if (string.IsNullOrWhiteSpace(fullDescription))
        {
            ctx.Reply("Usage: .bl.system.find <description>");
            return;
        }

        var matches = Reference.Search(fullDescription, limit: 8);
        if (matches.Count == 0)
        {
            ctx.Reply("No matching KindredExtract ECS/ProjectM/Unity reference found.");
            return;
        }

        var ai = BattleLuckPlugin.AIAssistant;
        if (ai == null)
        {
            ctx.Reply(Reference.FormatMatches(matches));
            return;
        }

        ctx.Reply("Searching KindredExtract ECS/ProjectM/Unity reference...");
        try
        {
            var prompt = Reference.BuildAiPrompt(fullDescription, matches);
            var steamId = ctx.SenderCharacterEntity.GetSteamId();
            var response = await ai.HandleDirectQuery(steamId, prompt, source: "system-reference");

            if (!string.IsNullOrWhiteSpace(response))
                ctx.Reply(ai.FormatInGameResponse(fullDescription, response));
            else
                ctx.Reply(Reference.FormatMatches(matches));
        }
        catch (Exception ex)
        {
            BattleLuckPlugin.LogWarning($"[SystemReferenceCommands] AI system lookup failed: {ex.Message}");
            ctx.Reply(Reference.FormatMatches(matches));
        }
    }

    [BattleLuckCommand("bl.system.search", description: "Search local ECS/ProjectM/Unity/EOS reference without AI. Usage: .bl.system.search <terms>", adminOnly: true)]
    public static void SearchSystem(
        BattleLuckCommandContext ctx,
        string description,
        string p2 = "",
        string p3 = "",
        string p4 = "",
        string p5 = "",
        string p6 = "",
        string p7 = "",
        string p8 = "")
    {
        var fullDescription = JoinArgs(description, p2, p3, p4, p5, p6, p7, p8);
        var matches = Reference.Search(fullDescription, limit: 8);
        ctx.Reply(Reference.FormatMatches(matches));
    }

    [BattleLuckCommand("bl.system.register", description: "Register a verified ProjectM/Unity ECS system reference live. Usage: .bl.system.register <ProjectM|Unity> <exactSystemType> [system.alias] [description]", adminOnly: true)]
    public static void RegisterSystem(
        BattleLuckCommandContext ctx,
        string runtime = "",
        string systemType = "",
        string alias = "",
        string p4 = "",
        string p5 = "",
        string p6 = "",
        string p7 = "",
        string p8 = "")
    {
        if (string.IsNullOrWhiteSpace(runtime) || string.IsNullOrWhiteSpace(systemType))
        {
            ctx.Reply("Usage: .bl.system.register <ProjectM|Unity> <exactSystemType> [system.alias] [description]");
            return;
        }

        var description = JoinArgs(p4, p5, p6, p7, p8);
        var requestedBy = ctx.SenderCharacterEntity.GetSteamId().ToString();
        var result = LiveSystemRegistryService.Register(systemType, runtime, alias, description, requestedBy);
        if (!result.Success || result.Value == null)
        {
            ctx.Reply(result.Error ?? "Could not register the system reference.");
            return;
        }

        var registration = result.Value;
        ctx.Reply(
            $"Registered {registration.Runtime} system '{registration.SystemType}' as '{registration.Action}'. " +
            "It is live immediately as a verified reference; native ECS execution is not auto-created.");
    }

    [BattleLuckCommand("bl.system.list", description: "List live ProjectM/Unity system references registered without a restart.", adminOnly: true)]
    public static void ListRegisteredSystems(BattleLuckCommandContext ctx)
    {
        var registrations = LiveSystemRegistryService.GetAll();
        if (registrations.Count == 0)
        {
            ctx.Reply("No live ProjectM/Unity system references are registered.");
            return;
        }

        var lines = registrations
            .Take(12)
            .Select(entry => $"{entry.Action} -> {entry.SystemType} [{entry.Runtime}]");
        var suffix = registrations.Count > 12 ? $"\n... and {registrations.Count - 12} more." : "";
        ctx.Reply(string.Join("\n", lines) + suffix);
    }

    static string JoinArgs(params string[] args)
    {
        return string.Join(" ", args.Where(a => !string.IsNullOrWhiteSpace(a))).Trim();
    }
}
