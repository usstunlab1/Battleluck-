using BattleLuck.Commands;
using BattleLuck.Services.Assistant;
using BattleLuck.Services.Chat;
using BattleLuck.Services.Runtime;

namespace BattleLuck.Commands.Chat;

public static class BattleLuckRootCommands
{
    static readonly AiLiteKnowledgeService AiLite = new();
    static readonly ZuiPacketPresenter Zui = new((steamId, packet) =>
        BattleLuckPlugin.NotifyPlayerBySteamIdOnMainThread(steamId, packet));

    [BattleLuckCommand("bl", description: "BattleLuck home and status")]
    public static void Home(BattleLuckCommandContext ctx)
    {
        var active = FindSession(ctx.SenderSteamId);
        ctx.Reply(active == null
            ? "BattleLuck: no event joined. .bl event | .bl join <event> | .bl ai <question> | .bl help"
            : $"BattleLuck: {active.Context.ModeId} · players {active.Context.Players.Count}. .bl score | .bl top | .bl leave | .bl help");
    }

    [BattleLuckCommand("bl help", description: "Permission-aware BattleLuck help")]
    public static void Help(BattleLuckCommandContext ctx, string topic = "")
    {
        if (!string.IsNullOrWhiteSpace(topic)) { ctx.Reply(AiLite.Answer(topic)); return; }
        ctx.Reply("Player: .bl event | join <event> | leave | score | top | results last | stats | ai <question> | ui native|zui");
        if (ctx.IsAdmin) ctx.Reply("Admin: .bl admin diagnostics | config status|reload | event validate|start|stop | dev request|plan|simulate|execute|revoke");
    }

    [BattleLuckCommand("bl event", description: "Show current event")]
    public static void Event(BattleLuckCommandContext ctx)
    {
        var active = FindSession(ctx.SenderSteamId);
        ctx.Reply(active == null ? "You are not in an active event." :
            $"Event {active.Context.ModeId}; session {active.Context.SessionId}; players {active.Context.Players.Count}.");
    }

    [BattleLuckCommand("bl join", description: "Join an event")]
    public static void Join(BattleLuckCommandContext ctx, string modeId = "")
    {
        var session = BattleLuckPlugin.Session;
        if (session == null) { ctx.Reply("Event runtime is not ready."); return; }
        var result = session.ToggleEnter(ctx.SenderSteamId, ctx.SenderCharacterEntity,
            string.IsNullOrWhiteSpace(modeId) ? null : modeId);
        ctx.Reply(result.Success ? "Event entry is queued; your return state is protected." : result.UserMessage);
    }

    [BattleLuckCommand("bl leave", description: "Leave the current event safely")]
    public static void Leave(BattleLuckCommandContext ctx)
    {
        var result = BattleLuckPlugin.Session?.ToggleLeave(ctx.SenderSteamId, ctx.SenderCharacterEntity)
                     ?? OperationResult.Fail("Event runtime is not ready.");
        ctx.Reply(result.Success ? "You left the event and restoration completed." : result.UserMessage);
    }

    [BattleLuckCommand("bl score", description: "Show personal event score")]
    public static void Score(BattleLuckCommandContext ctx)
    {
        var run = FindRun(ctx.SenderSteamId);
        if (run == null) { ctx.Reply("No active event score is available."); return; }
        var row = BattleLuckPlugin.EventPlatform!.Scores.Snapshot(run.Value.RunId)
            .FirstOrDefault(value => value.SteamId == ctx.SenderSteamId);
        ctx.Reply(row == null ? "No score has been recorded yet." :
            $"Score {row.Score}; K/D/A {row.Kills}/{row.Deaths}/{row.Assists}; objectives {row.Objectives}.");
    }

    [BattleLuckCommand("bl top", description: "Show current event standings")]
    public static void Top(BattleLuckCommandContext ctx, string metric = "score")
    {
        var run = FindRun(ctx.SenderSteamId);
        if (run == null) { ctx.Reply("No active standings are available."); return; }
        var rows = BattleLuckPlugin.EventPlatform!.Scores.Snapshot(run.Value.RunId).Take(5).ToArray();
        ctx.Reply(rows.Length == 0 ? "No standings have been recorded yet." : string.Join(" · ", rows.Select((row, i) =>
            $"#{i + 1} {DisplayPlayer(row.SteamId, i)}: {row.Score} ({row.Kills}/{row.Deaths}/{row.Assists})")));
    }

    [BattleLuckCommand("bl results", description: "Show an immutable event result")]
    public static void Results(BattleLuckCommandContext ctx, string selector = "last")
    {
        var service = BattleLuckPlugin.EventPlatform?.Results;
        if (service == null) { ctx.Reply("Result service is not ready."); return; }
        var result = selector.Equals("last", StringComparison.OrdinalIgnoreCase) ? service.GetLast() : service.Get(selector);
        if (result == null) { ctx.Reply("No matching result was found."); return; }
        var winner = result.Winner == null ? "none" : $"{result.Winner.Type} {result.Winner.Id} ({result.Winner.Score})";
        ctx.Reply($"{result.ModeId} ended {result.EndedUtc:u}; winner {winner}; participants {result.Standings.Count}.");
    }

    [BattleLuckCommand("bl stats", description: "Show retained event statistics")]
    public static void Stats(BattleLuckCommandContext ctx, string season = "default")
    {
        var result = BattleLuckPlugin.EventPlatform?.Results.GetLast();
        var row = result?.Standings.FirstOrDefault(value => value.SteamId == ctx.SenderSteamId);
        ctx.Reply(row == null ? $"No retained statistics for season {season}." :
            $"Season {season}: latest event score {row.Score}, K/D/A {row.Kills}/{row.Deaths}/{row.Assists}.");
    }

    [BattleLuckCommand("bl ai", description: "Private AI-lite/local assistant")]
    public static async Task Ai(BattleLuckCommandContext ctx, string question = "")
    {
        if (string.IsNullOrWhiteSpace(question)) { ctx.Reply(AiLite.Answer("")); return; }
        var assistant = BattleLuckPlugin.AIAssistant;
        if (assistant == null || assistant.ActiveProvider.Equals("local", StringComparison.OrdinalIgnoreCase))
        { ctx.Reply(AiLite.Answer(question)); return; }
        try
        {
            var reply = await assistant.HandleDirectQuery(ctx.SenderSteamId, question, "bl_ai", false).ConfigureAwait(false);
            BattleLuckPlugin.NotifyPlayerBySteamIdOnMainThread(ctx.SenderSteamId,
                string.IsNullOrWhiteSpace(reply) ? AiLite.Answer(question) : reply);
        }
        catch { BattleLuckPlugin.NotifyPlayerBySteamIdOnMainThread(ctx.SenderSteamId, AiLite.Answer(question)); }
    }

    [BattleLuckCommand("bl ui zui", description: "Opt in to optional ZUI packets")]
    public static void EnableZui(BattleLuckCommandContext ctx)
    {
        if (!ConfigLoader.LoadBattleLuckConfig().Chat.ZuiOptIn) { ctx.Reply("ZUI integration is disabled by the server owner."); return; }
        Zui.Enable(ctx.SenderSteamId);
        ctx.Reply("ZUI packets enabled for this session. Use .bl ui native to disable them.");
        Zui.TrySend(ctx.SenderSteamId, new ZuiWindow("BattleLuck.Home", "BattleLuck",
            new[] { "Server event platform", "Native chat remains available." },
            new[] { new ZuiButton("Event", ".bl event"), new ZuiButton("Score", ".bl score"), new ZuiButton("Results", ".bl results last") }));
    }

    [BattleLuckCommand("bl ui native", description: "Use native chat only")]
    public static void DisableZui(BattleLuckCommandContext ctx) { Zui.Disable(ctx.SenderSteamId); ctx.Reply("Native BattleLuck presentation enabled."); }

    static ActiveSession? FindSession(ulong steamId) => BattleLuckPlugin.Session?.ActiveSessions.Values
        .FirstOrDefault(session => session.Context.Players.Contains(steamId));

    static (string RunId, string ModeId)? FindRun(ulong steamId)
    {
        var session = FindSession(steamId);
        if (session == null || BattleLuckPlugin.EventPlatform == null ||
            !BattleLuckPlugin.EventPlatform.TryGetRun(session.Context.SessionId, out var runId, out var modeId)) return null;
        return (runId, modeId);
    }

    static string DisplayPlayer(ulong steamId, int index) =>
        BattleLuckPlugin.PlayerDirectory?.TryFindSteam(steamId, out var player) == true &&
        !string.IsNullOrWhiteSpace(player.CharacterName)
            ? player.CharacterName
            : $"player-{index + 1}";
}
