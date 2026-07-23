using BattleLuck.Commands;
using BattleLuck.Services.AI;
using BattleLuck.Services.Assistant;
using BattleLuck.Services.Practice;
using BattleLuck.Services.Chat;
using VampireCommandFramework;

namespace BattleLuck.Commands.Chat;

/// <summary>
/// The complete public BattleLuck command surface. All former .bl routes are
/// intentionally retired; requests enter through one private .ai command.
/// </summary>
public static class BattleLuckRootCommands
{
    static readonly AiLiteKnowledgeService AiLite = new();
    static readonly ZuiPacketPresenter Zui = new(
        (steamId, packet) => BattleLuckPlugin.NotifyPlayerBySteamIdOnMainThread(steamId, packet));

    /// <summary>
    /// VCF registration surface used for command discovery and the short usage
    /// response. The Harmony chat prefix handles complete multi-word requests
    /// before VCF tokenizes them, then routes them to <see cref="Request"/>.
    /// </summary>
    [Command("ai", usage: ".ai <text>", description: "Ask BattleLuck or describe a catalog action")]
    public static void Ai(ChatCommandContext ctx, string text = "")
    {
        text = NormalizeRequest(text);
        if (string.IsNullOrWhiteSpace(text))
        {
            ctx.Reply("Usage: .ai <question or action description>");
            return;
        }

        if (!ctx.TryGetSenderIdentity(out var character, out var steamId))
        {
            ctx.Reply("BattleLuck could not resolve your player identity.");
            return;
        }

        BattleLuckCommandDispatcher.TryDispatch(
            $".ai {text}",
            character,
            steamId,
            ctx.Event.User.IsAdmin,
            isConsole: false);
    }

    [BattleLuckCommand("ai", description: "Ask BattleLuck or describe a catalog action")]
    public static async Task Request(BattleLuckCommandContext ctx, string request = "")
    {
        request = NormalizeRequest(request);
        if (!AiRequestPolicy.TryValidate(request, out request, out var validationError))
        {
            ctx.Reply(request.Length == 0
                ? "Usage: .ai <question or action description>"
                : validationError);
            return;
        }

        if (request.Equals("end", StringComparison.OrdinalIgnoreCase))
        {
            var ended = GameChatAiBridge.EndSession(ctx.SenderSteamId);
            ctx.Reply(ended ? "AI conversation ended." : "No AI conversation is active.");
            return;
        }

        if (TryHandleZui(ctx, request))
            return;

        if (TryHandleSoloPractice(ctx, request))
            return;

        GameChatAiBridge.BeginSession(ctx.SenderSteamId);
        try
        {
            if (IntentActionRouter.TryHandle(ctx.SenderSteamId, request) ||
                NaturalLanguageActionRouter.TryHandle(ctx.SenderSteamId, request))
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

            var playerReply = string.IsNullOrWhiteSpace(reply)
                ? AiLite.Answer(request)
                : assistant.FormatInGameResponse(request, reply);
            BattleLuckPlugin.NotifyPlayerBySteamIdOnMainThread(ctx.SenderSteamId, playerReply);
            GameChatAiBridge.RecordReply(ctx.SenderSteamId);
        }
        catch (Exception ex)
        {
            BattleLuckPlugin.LogWarning($"[AI Command] Request failed: {ex.Message}");
            BattleLuckPlugin.NotifyPlayerBySteamIdOnMainThread(ctx.SenderSteamId, AiLite.Answer(request));
            GameChatAiBridge.RecordReply(ctx.SenderSteamId);
        }
    }

    static bool TryHandleZui(BattleLuckCommandContext ctx, string request)
    {
        var parts = request.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0 || !parts[0].Equals("ui", StringComparison.OrdinalIgnoreCase))
            return false;

        if (parts.Length > 1 && parts[1].Equals("disable", StringComparison.OrdinalIgnoreCase))
        {
            Zui.Disable(ctx.SenderSteamId);
            ctx.Reply("BattleLuck ZUI disabled.");
            return true;
        }

        Zui.Enable(ctx.SenderSteamId);
        var section = parts.Length > 1 ? parts[1] : string.Empty;
        var window = section.Length == 0 || section.Equals("enable", StringComparison.OrdinalIgnoreCase)
            ? BattleLuckZuiDashboard.BuildHome()
            : BattleLuckZuiDashboard.BuildSection(section);

        if (!Zui.TrySend(ctx.SenderSteamId, window))
            ctx.Reply("BattleLuck could not send the ZUI window.");
        return true;
    }

    internal static string NormalizeRequest(string? value)
    {
        var text = (value ?? string.Empty).Trim();
        if (text.Equals("r", StringComparison.OrdinalIgnoreCase) ||
            text.Equals("request", StringComparison.OrdinalIgnoreCase))
            return string.Empty;

        if (text.StartsWith("r ", StringComparison.OrdinalIgnoreCase))
            return text["r ".Length..].Trim();
        if (text.StartsWith("request ", StringComparison.OrdinalIgnoreCase))
            return text["request ".Length..].Trim();

        var tokens = text.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (tokens.Length >= 2 && tokens[0].Equals("pr", StringComparison.OrdinalIgnoreCase))
        {
            var practiceMode = tokens[1].ToLowerInvariant() switch
            {
                "mir" => "mirror",
                "fol" => "follow",
                "fig" => "fight",
                "st" => "status",
                "stop" => "stop",
                _ => tokens[1]
            };
            return $"practice {practiceMode}";
        }

        return text;
    }

    static bool TryHandleSoloPractice(BattleLuckCommandContext ctx, string request)
    {
        var parts = request.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0 || !parts[0].Equals("practice", StringComparison.OrdinalIgnoreCase))
            return false;

        if (!ctx.IsAdmin)
        {
            ctx.Reply("Solo practice is an admin-only command.");
            return true;
        }

        var mode = parts.Length > 1 ? parts[1] : "";
        if (mode.Equals("status", StringComparison.OrdinalIgnoreCase))
        {
            ctx.Reply(SoloPracticeService.Instance.Status(ctx.SenderSteamId));
            return true;
        }
        var result = mode.Equals("stop", StringComparison.OrdinalIgnoreCase)
            ? SoloPracticeService.Instance.Stop(ctx.SenderSteamId)
            : SoloPracticeService.Instance.Start(ctx.SenderCharacterEntity, ctx.SenderSteamId, mode);
        ctx.Reply(result.Success
            ? (mode.Equals("stop", StringComparison.OrdinalIgnoreCase)
                ? "Solo practice NPC removed."
                : $"Solo practice AI is starting in {mode} mode.")
            : result.UserMessage);
        return true;
    }
}
