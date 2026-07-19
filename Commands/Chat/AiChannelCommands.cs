using System;
using BattleLuck.Models.Chat;
using BattleLuck.Services.Chat;
using BattleLuck.Commands;

namespace BattleLuck.Commands.Chat;

/// <summary>
/// Commands for managing the BattleLuck AI chat channel.
/// .blch next - Cycle to next channel (Native -> AI)
/// .blch ai   - Set to AI channel
/// .blch off  - Set to Native channel
/// .blch current - Show current channel
/// </summary>
public static class AiChannelCommands
{
    [BattleLuckCommand("blch next", description: "Cycle to the next chat channel (Native -> AI)")]
    public static void NextChannel(BattleLuckCommandContext ctx)
    {
        var steamId = ctx.SenderSteamId;
        var currentChannel = AiChannelState.GetChannel(steamId);
        var nextChannel = currentChannel == BattleLuckChatChannel.Native 
            ? BattleLuckChatChannel.AI 
            : BattleLuckChatChannel.Native;
        
        SetChannelInternal(steamId, nextChannel);
        ctx.Reply($"Chat channel set to: {nextChannel}");
    }

    [BattleLuckCommand("blch ai", description: "Set chat channel to AI")]
    public static void AiChannel(BattleLuckCommandContext ctx)
    {
        SetChannelInternal(ctx.SenderSteamId, BattleLuckChatChannel.AI);
        ctx.Reply("Chat channel set to: AI");
    }

    [BattleLuckCommand("blch off", description: "Set chat channel to Native (default)")]
    public static void OffChannel(BattleLuckCommandContext ctx)
    {
        SetChannelInternal(ctx.SenderSteamId, BattleLuckChatChannel.Native);
        ctx.Reply("Chat channel set to: Native");
    }

    [BattleLuckCommand("blch current", description: "Show current chat channel")]
    public static void CurrentChannel(BattleLuckCommandContext ctx)
    {
        var currentChannel = AiChannelState.GetChannel(ctx.SenderSteamId);
        ctx.Reply($"Current chat channel is: {currentChannel}");
    }

    private static void SetChannelInternal(ulong steamId, BattleLuckChatChannel channel)
    {
        if (channel == BattleLuckChatChannel.AI)
        {
            AiChannelState.Add(steamId);
        }
        else
        {
            AiChannelState.Remove(steamId);
        }
    }
}