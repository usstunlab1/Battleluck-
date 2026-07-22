using BattleLuck.Commands;
using BattleLuck.Services.AI;
using BattleLuck.Services.Assistant;

namespace BattleLuck.Commands.Chat;

/// <summary>
/// The complete public BattleLuck command surface. All former .bl routes are
/// intentionally retired; requests enter through one private .ai request command.
/// </summary>
public static class BattleLuckRootCommands
{
    static readonly AiLiteKnowledgeService AiLite = new();

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
