using BattleLuck.Commands;
using BattleLuck.Services.DeveloperBridge;

namespace BattleLuck.Commands.Admin;

public static class DeveloperBridgeCommands
{
    static readonly AiDeveloperBridge Bridge = new();

    [BattleLuckCommand("bl admin dev request", description: "Request scoped developer access", adminOnly: true)]
    public static void Request(BattleLuckCommandContext ctx, string manifestAndCapability = "npc-simulation read")
    {
        var parts = manifestAndCapability.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var result = Bridge.Request(ctx.SenderSteamId, parts.ElementAtOrDefault(0) ?? "npc-simulation", parts.ElementAtOrDefault(1) ?? "read");
        ctx.Reply(result.Success ? $"Grant {result.Value!.Id}; capability={result.Value.Capability}; expires={result.Value.ExpiresUtc:u}; manifest={result.Value.ManifestSha256[..12]}." : result.UserMessage);
    }

    [BattleLuckCommand("ai.dev request", description: "Compatibility alias for developer access", adminOnly: true)]
    public static void CompatibilityRequest(BattleLuckCommandContext ctx, string manifestAndCapability = "npc-simulation read") => Request(ctx, manifestAndCapability);

    [BattleLuckCommand("bl admin dev plan", description: "Create a catalog-bound NPC simulation plan", adminOnly: true)]
    public static void Plan(BattleLuckCommandContext ctx, string goal = "validate npc simulation")
    {
        var result = Bridge.Plan(ctx.SenderSteamId, goal);
        ctx.Reply(result.Success ? $"Plan {result.Value!.Id}; hash={result.Value.Sha256[..12]}; actions={result.Value.Steps.Count}; run .bl admin dev simulate {result.Value.Id}." : result.UserMessage);
    }

    [BattleLuckCommand("bl admin dev simulate", description: "Run static/model plan validation", adminOnly: true)]
    public static void Simulate(BattleLuckCommandContext ctx, string planId = "")
    {
        var result = Bridge.Simulate(ctx.SenderSteamId, planId);
        ctx.Reply(result.Success && result.Value != null ?
            $"Simulation success={result.Value.Success}; actions={result.Value.ActionCount}; errors={(result.Value.Errors.Count == 0 ? "none" : string.Join("; ", result.Value.Errors))}." : result.UserMessage);
    }

    [BattleLuckCommand("bl admin dev execute", description: "Prepare or confirm isolated dev-arena execution", adminOnly: true)]
    public static void Execute(BattleLuckCommandContext ctx, string planAndToken = "")
    {
        var parts = planAndToken.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var planId = parts.ElementAtOrDefault(0) ?? "";
        if (parts.Length < 2)
        {
            var prepared = Bridge.PrepareExecution(ctx.SenderSteamId, planId);
            ctx.Reply(prepared.Success ? $"Static simulation passed. Confirm once with .bl admin dev execute {planId} {prepared.Value}." : prepared.UserMessage);
            return;
        }
        var result = Bridge.ExecuteDevArena(ctx.SenderSteamId, ctx.SenderCharacterEntity, planId, parts[1]);
        ctx.Reply(result.Success ? "Dev-arena plan executed and remains owned by the dev session." : result.UserMessage);
    }

    [BattleLuckCommand("bl admin dev revoke", description: "Revoke developer access and confirmations", adminOnly: true)]
    public static void Revoke(BattleLuckCommandContext ctx) { Bridge.Revoke(ctx.SenderSteamId); ctx.Reply("Developer access revoked."); }
}
