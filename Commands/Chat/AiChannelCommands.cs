using BattleLuck.Models.Chat;
using BattleLuck.Services.Chat;

namespace BattleLuck.Commands.Chat;

/// <summary>
/// Selects between the client-managed native game channel and BattleLuck's
/// shared AI channel. Native never replaces or rewrites Global, Local,
/// Team/Clan, or Whisper; the client's current selection passes through.
/// </summary>
public static class AiChannelCommands
{
    [BattleLuckCommand("blch", description: "Show the current BattleLuck chat channel")]
    public static void Channel(BattleLuckCommandContext ctx) =>
        ReplyCurrent(ctx);

    [BattleLuckCommand("blch ai", description: "Enter the shared BattleLuck AI channel")]
    public static void EnterAi(BattleLuckCommandContext ctx)
    {
        AiChannelState.Enter(ctx.SenderSteamId);
        ctx.Reply("BattleLuck chat channel: AI. Normal messages now stay in the blue AI room.");
    }

    [BattleLuckCommand("blch off", description: "Leave the AI channel and return to native game chat")]
    public static void LeaveAi(BattleLuckCommandContext ctx)
    {
        AiChannelState.Leave(ctx.SenderSteamId);
        ctx.Reply("BattleLuck chat channel: Native. Your client-selected game channel is unchanged.");
    }

    [BattleLuckCommand("blch next", description: "Toggle between Native and AI chat")]
    public static void Next(BattleLuckCommandContext ctx)
    {
        var selected = AiChannelState.SelectNext(ctx.SenderSteamId);
        ctx.Reply(selected == BattleLuckChatChannel.AI
            ? "BattleLuck chat channel: AI. Normal messages now stay in the blue AI room."
            : "BattleLuck chat channel: Native. Your client-selected game channel is unchanged.");
    }

    [BattleLuckCommand("blch current", description: "Show the current BattleLuck chat channel")]
    public static void Current(BattleLuckCommandContext ctx) =>
        ReplyCurrent(ctx);

    private static void ReplyCurrent(BattleLuckCommandContext ctx)
    {
        var channel = AiChannelState.GetChannel(ctx.SenderSteamId);
        ctx.Reply(channel == BattleLuckChatChannel.AI
            ? "Current BattleLuck chat channel: AI."
            : "Current BattleLuck chat channel: Native (client-selected Global, Local, Team/Clan, or Whisper).");
    }
}
