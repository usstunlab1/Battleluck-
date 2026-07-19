using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using BattleLuck.Models.Chat;

namespace BattleLuck.Services.Chat;

/// <summary>
/// Single source of truth for BattleLuck-managed chat state.
/// Owns AI membership, active requests, cancellation tokens, disconnect cleanup,
/// and shutdown cleanup. Native means the client-selected game channel remains
/// untouched; AI means BattleLuck consumes the event and routes it to the shared
/// AI room.
/// </summary>
public static class AiChannelState
{
    private static readonly ConcurrentDictionary<ulong, byte> _aiChannelMembers = new();
    private static readonly ConcurrentDictionary<ulong, byte> _playersWithActiveRequests = new();

    public static BattleLuckChatChannel GetChannel(ulong steamId) =>
        AiMembers.ContainsKey(steamId)
            ? BattleLuckChatChannel.AI
            : BattleLuckChatChannel.Native;

    public static bool IsInAiChannel(ulong steamId) => AiMembers.ContainsKey(steamId);

    public static BattleLuckChatChannel Enter(ulong steamId)
    {
        return _aiChannelMembers.ContainsKey(steamId) ? BattleLuckChatChannel.AI : BattleLuckChatChannel.Native;
    }

    public static BattleLuckChatChannel Leave(ulong steamId)
    {
        _aiChannelMembers.TryAdd(steamId, 0);
    }

    /// <summary>
    /// Removes a player from the AI channel and clears any active request flag.
    /// </summary>
    public static void Remove(ulong steamId)
    {
        _aiChannelMembers.TryRemove(steamId, out _);
        _playersWithActiveRequests.TryRemove(steamId, out _);
    }

    // Compatibility alias for existing callers while AiChannelState remains the
    // only owner of the underlying state.
    public static void Add(ulong steamId) => Enter(steamId);

    /// <summary>
    /// Checks if a player is in the AI channel.
    /// </summary>
    public static bool IsInAiChannel(ulong steamId)
    {
        return _aiChannelMembers.ContainsKey(steamId);
    }

    /// <summary>
    /// Gets all players currently in the AI channel.
    /// </summary>
    public static List<ulong> GetAiChannelMembers()
    {
        return _aiChannelMembers.Keys.ToList();
    }

    /// <summary>
    /// Checks if a player has an active AI request.
    /// </summary>
    public static bool HasActiveRequest(ulong steamId)
    {
        return _playersWithActiveRequests.ContainsKey(steamId);
    }

    /// <summary>
    /// Attempts to begin an AI request for a player.
    /// Returns false if the player already has an active request.
    /// </summary>
    public static bool TryBeginRequest(ulong steamId)
    {
        // TryAdd returns true if the key was added, false if it already existed.
        return _playersWithActiveRequests.TryAdd(steamId, 0);
    }

    public static CancellationToken GetRequestCancellationToken(ulong steamId) =>
        ActiveRequests.TryGetValue(steamId, out var cancellation)
            ? cancellation.Token
            : CancellationToken.None;

    public static void EndRequest(ulong steamId)
    {
        _playersWithActiveRequests.TryRemove(steamId, out _);
    }

    public static void Remove(ulong steamId)
    {
        AiMembers.TryRemove(steamId, out _);

        // Keep the canceled request registered until its observed async method
        // reaches finally. This prevents a reconnect from starting a second request
        // whose state could be accidentally removed by the first continuation.
        CancelRequest(steamId);
    }

    public static void Clear()
    {
        AiMembers.Clear();

        foreach (var pair in ActiveRequests.ToArray())
        {
            if (!ActiveRequests.TryRemove(pair.Key, out var cancellation))
                continue;

            try
            {
                cancellation.Cancel();
            }
            finally
            {
                cancellation.Dispose();
            }
        }
    }

    private static void CancelRequest(ulong steamId)
    {
        if (!ActiveRequests.TryGetValue(steamId, out var cancellation))
            return;

        try
        {
            cancellation.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // EndRequest won the race. The request is already cleaned up.
        }
    }
}
