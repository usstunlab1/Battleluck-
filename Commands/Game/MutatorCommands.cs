public static class MutatorCommands
{
    static readonly Dictionary<string, MutatorDef> _available = new()
    {
        ["no_dodge"] = new MutatorDef { Id = "no_dodge", Description = "Disable dodge" },
        ["one_shot"] = new MutatorDef { Id = "one_shot", Description = "One-shot kills" },
        ["infinite_ammo"] = new MutatorDef { Id = "infinite_ammo", Description = "Unlimited arrows" },
        ["speed_100"] = new MutatorDef { Id = "speed_100", Description = "100% speed" },
        ["no_cooldowns"] = new MutatorDef { Id = "no_cooldowns", Description = "Abilities ready instantly" },
        ["invincible_npc"] = new MutatorDef { Id = "invincible_npc", Description = "NPCs take no damage" },
        ["no_abilities"] = new MutatorDef { Id = "no_abilities", Description = "Disable abilities" },
        ["gravity"] = new MutatorDef { Id = "gravity", Description = "Higher gravity" },
    };

    static readonly HashSet<string> _enabled = new();

    [Command("mutatorlist", description: "List available mutators")]
    public static void ListMutators(ChatCommandContext ctx)
    {
        ctx.Reply("Available mutators:");
        foreach (var kv in _available)
        {
            var status = _enabled.Contains(kv.Key) ? " [ON]" : "";
            ctx.Reply($"  {kv.Key} — {kv.Value.Description}{status}");
        }
    }

    [Command("mutatorenable", description: "Enable a mutator", adminOnly: true)]
    public static void EnableMutator(ChatCommandContext ctx, string mutatorId)
    {
        if (!_available.ContainsKey(mutatorId))
        {
            ctx.Reply($"Unknown mutator: {mutatorId}. Use mutatorlist.");
            return;
        }

        _enabled.Add(mutatorId);
        ApplyMutator(mutatorId, true);
        ctx.Reply($"Mutator '{mutatorId}' enabled.");
    }

    [Command("mutatordisable", description: "Disable a mutator", adminOnly: true)]
    public static void DisableMutator(ChatCommandContext ctx, string mutatorId)
    {
        if (!_enabled.Contains(mutatorId))
        {
            ctx.Reply($"Mutator '{mutatorId}' is not enabled.");
            return;
        }

        _enabled.Remove(mutatorId);
        ApplyMutator(mutatorId, false);
        ctx.Reply($"Mutator '{mutatorId}' disabled.");
    }

    [Command("mutatorclear", description: "Clear all mutators", adminOnly: true)]
    public static void ClearMutators(ChatCommandContext ctx)
    {
        foreach (var id in _enabled)
        {
            ApplyMutator(id, false);
        }
        _enabled.Clear();
        ctx.Reply("All mutators cleared.");
    }

    static void ApplyMutator(string mutatorId, bool enable)
    {
        BattleLuckPlugin.LogInfo($"[Mutator] {mutatorId} = {enable}");
    }

    public static bool IsEnabled(string mutatorId) => _enabled.Contains(mutatorId);

    public static IReadOnlyCollection<string> GetEnabled() => _enabled;
}

public class MutatorDef
{
    public string Id { get; set; } = "";
    public string Description { get; set; } = "";
}