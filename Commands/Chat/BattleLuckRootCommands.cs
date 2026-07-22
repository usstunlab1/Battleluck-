using BattleLuck.Commands;
using BattleLuck.Services.AI;
using BattleLuck.Services.Assistant;
using VampireCommandFramework;

namespace BattleLuck.Commands.Chat;

/// <summary>
/// The complete public BattleLuck command surface. All former .bl routes are
/// intentionally retired; requests enter through one private .ai request command.
/// </summary>
public static class BattleLuckRootCommands
{
    static readonly AiLiteKnowledgeService AiLite = new();

    /// <summary>
    /// VCF registration surface used for command discovery and the short usage
    /// response. The Harmony chat prefix handles complete multi-word requests
    /// before VCF tokenizes them, then routes them to <see cref="Request"/>.
    /// </summary>
    [Command("ai", usage: ".ai request <text>", description: "Send a private request to the BattleLuck assistant")]
    public static void Ai(ChatCommandContext ctx, string operation = "", string request = "")
    {
        if (!operation.Equals("request", StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(request))
        {
            ctx.Reply("Usage: .ai request <text>");
            return;
        }

        if (!ctx.TryGetSenderIdentity(out var character, out var steamId))
        {
            ctx.Reply("BattleLuck could not resolve your player identity.");
            return;
        }

        BattleLuckCommandDispatcher.TryDispatch(
            $".ai request {request}",
            character,
            steamId,
            ctx.Event.User.IsAdmin,
            isConsole: false);
    }

    [BattleLuckCommand("ai request", description: "Send a private request to the BattleLuck assistant")]
    public static async Task Request(BattleLuckCommandContext ctx, string request = "")
    {
        request = request.Trim();
        if (request.Length == 0)
        {
            ctx.Reply("Usage: .ai request <text>");
            return;
        }

        if (request.Equals("end", StringComparison.OrdinalIgnoreCase))
        {
            var ended = GameChatAiBridge.EndSession(ctx.SenderSteamId);
            ctx.Reply(ended ? "AI conversation ended." : "No AI conversation is active.");
            return;
        }

        GameChatAiBridge.BeginSession(ctx.SenderSteamId);
        try
        {
            if (IntentActionRouter.TryHandlePlayerSelfService(ctx.SenderSteamId, request))
                return;
        }
        catch (Exception ex)
        {
            BattleLuckPlugin.LogWarning($"[AI Command] Request routing failed: {ex.Message}");
        }

        var assistant = BattleLuckPlugin.AIAssistant;
        if (assistant == null)
        {
            ctx.Reply(AiLite.Answer(request));
            GameChatAiBridge.RecordReply(ctx.SenderSteamId);
            return;
        }

        try
        {
            var reply = await assistant.HandleDirectQuery(
                ctx.SenderSteamId,
                request,
                source: "ai_request",
                broadcastToInGameChat: false).ConfigureAwait(false);

            BattleLuckPlugin.NotifyPlayerBySteamIdOnMainThread(
                ctx.SenderSteamId,
                string.IsNullOrWhiteSpace(reply) ? AiLite.Answer(request) : reply);
            GameChatAiBridge.RecordReply(ctx.SenderSteamId);
        }
        catch (Exception ex)
        {
            BattleLuckPlugin.LogWarning($"[AI Command] Request failed: {ex.Message}");
            BattleLuckPlugin.NotifyPlayerBySteamIdOnMainThread(ctx.SenderSteamId, AiLite.Answer(request));
            GameChatAiBridge.RecordReply(ctx.SenderSteamId);
        }
    }
}
