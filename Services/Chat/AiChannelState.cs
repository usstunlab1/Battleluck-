using System.Collections.Generic;
using System.Linq;
using BattleLuck.Models.Chat;

namespace BattleLuck.Services.Chat;

/// <summary>
/// Single source of truth for AI channel membership and request state.
/// Manages which players are in the AI channel and tracks active AI requests.
/// </summary>
public static class AiChannelState
{
    private static readonly HashSet<ulong> _aiChannelMembers = new();
    private static readonly HashSet<ulong> _playersWithActiveRequests = new();

    /// <summary>
    /// Gets the BattleLuck chat channel for a player.
    /// Returns AI if the player is in the AI channel, Native otherwise.
    /// </summary>
    public static BattleLuckChatChannel GetChannel(ulong steamId)
    {
        return _aiChannelMembers.Contains(steamId) ? BattleLuckChatChannel.AI : BattleLuckChatChannel.Native;
    }

    /// <summary>
    /// Adds a player to the AI channel.
    /// </summary>
    public static void Add(ulong steamId)
    {
        _aiChannelMembers.Add(steamId);
    }

    /// <summary>
    /// Removes a player from the AI channel and clears any active request flag.
    /// </summary>
    public static void Remove(ulong steamId)
    {
        _aiChannelMembers.Remove(steamId);
        _playersWithActiveRequests.Remove(steamId);
    }

    /// <summary>
    /// Clears all AI channel state (used on plugin shutdown).
    /// </summary>
    public static void Clear()
    {
        _aiChannelMembers.Clear();
        _playersWithActiveRequests.Clear();
    }

    /// <summary>
    /// Checks if a player is in the AI channel.
    /// </summary>
    public static bool IsInAiChannel(ulong steamId)
    {
        return _aiChannelMembers.Contains(steamId);
    }

    /// <summary>
    /// Gets all players currently in the AI channel.
    /// </summary>
    public static List<ulong> GetAiChannelMembers()
    {
        return _aiChannelMembers.ToList();
    }

    /// <summary>
    /// Checks if a player has an active AI request.
    /// </summary>
    public static bool HasActiveRequest(ulong steamId)
    {
        return _playersWithActiveRequests.Contains(steamId);
    }

    /// <summary>
    /// Attempts to begin an AI request for a player.
    /// Returns false if the player already has an active request.
    /// </summary>
    public static bool TryBeginRequest(ulong steamId)
    {
        if (_playersWithActiveRequests.Contains(steamId))
            return false;
        
        _playersWithActiveRequests.Add(steamId);
        return true;
    }

    /// <summary>
    /// Ends an AI request for a player.
    /// </summary>
    public static void EndRequest(ulong steamId)
    {
        _playersWithActiveRequests.Remove(steamId);
    }
}