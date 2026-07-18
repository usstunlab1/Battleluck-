using BattleLuck.Services;

namespace BattleLuck.Commands;

internal static class ClanTaskCommands
{
    [Command("activityclan", usage: ".activityclan [page]", description: "View active world and event clan tasks.")]
    public static void ActivityClan(ChatCommandContext ctx, int page = 1) => Show(ctx, page);

    [Command("ac", usage: ".ac [page]", description: "View active world and event clan tasks.")]
    public static void ActivityClanShort(ChatCommandContext ctx, int page = 1) => Show(ctx, page);

    [Command("clantask.cancel", usage: ".clantask.cancel <taskId>", description: "Cancel an active clan task.", adminOnly: true)]
    public static void Cancel(ChatCommandContext ctx, string taskId = "")
    {
        var service = BattleLuckPlugin.ClanTasks;
        if (service == null) { ctx.Reply("Clan task service is not ready."); return; }
        if (string.IsNullOrWhiteSpace(taskId)) { ctx.Reply("Usage: .clantask.cancel <taskId>"); return; }
        var eventContext = ResolveEventContext(ctx.GetSenderSteamId());
        var result = service.Cancel(taskId, eventContext.EventId, eventContext.SessionId);
        ctx.Reply(result.Success ? $"Clan task '{taskId}' cancelled." : result.UserMessage);
    }

    [Command("clantask.complete", usage: ".clantask.complete <taskId>", description: "Complete an active clan task.", adminOnly: true)]
    public static void Complete(ChatCommandContext ctx, string taskId = "")
    {
        var service = BattleLuckPlugin.ClanTasks;
        if (service == null) { ctx.Reply("Clan task service is not ready."); return; }
        if (string.IsNullOrWhiteSpace(taskId)) { ctx.Reply("Usage: .clantask.complete <taskId>"); return; }
        var eventContext = ResolveEventContext(ctx.GetSenderSteamId());
        var result = service.Complete(
            taskId,
            ctx.GetSenderSteamId(),
            callerEventId: eventContext.EventId,
            callerSessionId: eventContext.SessionId,
            bypassAssigneeCheck: true);
        ctx.Reply(result.Success ? $"Clan task '{taskId}' completed." : result.UserMessage);
    }

    [Command("clantask.progress", usage: ".clantask.progress <taskId> <amount>", description: "Add trusted progress to a clan task.", adminOnly: true)]
    public static void Progress(ChatCommandContext ctx, string taskId = "", int amount = 1)
    {
        var eventContext = ResolveEventContext(ctx.GetSenderSteamId());
        var result = BattleLuckPlugin.ClanTasks?.AddProgress(
                taskId,
                amount,
                ctx.GetSenderSteamId(),
                trustedGather: true,
                callerEventId: eventContext.EventId,
                callerSessionId: eventContext.SessionId,
                bypassAssigneeCheck: true)
            ?? OperationResult<ClanTask>.Fail("Clan task service is not ready.");
        ctx.Reply(result.Success && result.Value != null
            ? $"Clan task '{result.Value.TaskId}' progress: {result.Value.CurrentAmount}/{result.Value.TargetAmount}."
            : result.UserMessage);
    }

    [BattleLuckCommand("activityclan", description: "View active world and event clan tasks.")]
    static void ActivityClanDispatch(BattleLuckCommandContext ctx, int page = 1) => Show(ctx, page);

    [BattleLuckCommand("ac", description: "View active world and event clan tasks.")]
    static void ActivityClanShortDispatch(BattleLuckCommandContext ctx, int page = 1) => Show(ctx, page);

    [BattleLuckCommand("clantask.cancel", description: "Cancel an active clan task.", adminOnly: true)]
    static void CancelDispatch(BattleLuckCommandContext ctx, string taskId = "")
    {
        var eventContext = ResolveEventContext(ctx.SenderSteamId);
        var result = BattleLuckPlugin.ClanTasks?.Cancel(taskId, eventContext.EventId, eventContext.SessionId) ?? OperationResult.Fail("Clan task service is not ready.");
        ctx.Reply(result.Success ? $"Clan task '{taskId}' cancelled." : result.UserMessage);
    }

    [BattleLuckCommand("clantask.complete", description: "Complete an active clan task.", adminOnly: true)]
    static void CompleteDispatch(BattleLuckCommandContext ctx, string taskId = "")
    {
        var eventContext = ResolveEventContext(ctx.SenderSteamId);
        var result = BattleLuckPlugin.ClanTasks?.Complete(
                taskId,
                ctx.SenderSteamId,
                callerEventId: eventContext.EventId,
                callerSessionId: eventContext.SessionId,
                bypassAssigneeCheck: true)
            ?? OperationResult.Fail("Clan task service is not ready.");
        ctx.Reply(result.Success ? $"Clan task '{taskId}' completed." : result.UserMessage);
    }

    [BattleLuckCommand("clantask.progress", description: "Add trusted progress to a clan task.", adminOnly: true)]
    static void ProgressDispatch(BattleLuckCommandContext ctx, string taskId = "", int amount = 1)
    {
        var eventContext = ResolveEventContext(ctx.SenderSteamId);
        var result = BattleLuckPlugin.ClanTasks?.AddProgress(
                taskId,
                amount,
                ctx.SenderSteamId,
                trustedGather: true,
                callerEventId: eventContext.EventId,
                callerSessionId: eventContext.SessionId,
                bypassAssigneeCheck: true)
            ?? OperationResult<ClanTask>.Fail("Clan task service is not ready.");
        ctx.Reply(result.Success && result.Value != null
            ? $"Clan task '{result.Value.TaskId}' progress: {result.Value.CurrentAmount}/{result.Value.TargetAmount}."
            : result.UserMessage);
    }

    static void Show(ChatCommandContext ctx, int page)
    {
        var service = BattleLuckPlugin.ClanTasks;
        if (service == null) { ctx.Reply("Clan task service is not ready."); return; }
        var character = ctx.GetSenderCharacterEntity();
        var eventContext = ResolveEventContext(ctx.GetSenderSteamId());
        ctx.Reply(ClanTaskPresenter.BuildPage(service.ListForPlayer(
            ctx.GetSenderSteamId(),
            clanId: ClanTaskGameAdapter.ResolveClanId(character),
            callerEventId: eventContext.EventId,
            callerSessionId: eventContext.SessionId,
            restrictEventTasksToCallerSession: true), page));
    }

    static void Show(BattleLuckCommandContext ctx, int page)
    {
        var service = BattleLuckPlugin.ClanTasks;
        if (service == null) { ctx.Reply("Clan task service is not ready."); return; }
        var eventContext = ResolveEventContext(ctx.SenderSteamId);
        ctx.Reply(ClanTaskPresenter.BuildPage(service.ListForPlayer(
            ctx.SenderSteamId,
            clanId: ClanTaskGameAdapter.ResolveClanId(ctx.SenderCharacterEntity),
            callerEventId: eventContext.EventId,
            callerSessionId: eventContext.SessionId,
            restrictEventTasksToCallerSession: true), page));
    }

    static (string EventId, string SessionId) ResolveEventContext(ulong steamId)
    {
        var session = BattleLuckPlugin.Session?.ActiveSessions.Values
            .FirstOrDefault(candidate => candidate.Context?.Players.Contains(steamId) == true);
        return session?.Context == null
            ? ("", "")
            : (session.Context.ModeId, session.Context.SessionId);
    }
}
