using System.Collections.Concurrent;
using System.Threading;
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
    private static readonly ConcurrentDictionary<ulong, byte> AiMembers = new();
    private static readonly ConcurrentDictionary<ulong, CancellationTokenSource> ActiveRequests = new();

    public static BattleLuckChatChannel GetChannel(ulong steamId) =>
        AiMembers.ContainsKey(steamId)
            ? BattleLuckChatChannel.AI
            : BattleLuckChatChannel.Native;

    public static bool IsInAiChannel(ulong steamId) => AiMembers.ContainsKey(steamId);

    public static BattleLuckChatChannel Enter(ulong steamId)
    {
        AiMembers[steamId] = 0;
        return BattleLuckChatChannel.AI;
    }

    public static BattleLuckChatChannel Leave(ulong steamId)
    {
        AiMembers.TryRemove(steamId, out _);
        CancelRequest(steamId);
        return BattleLuckChatChannel.Native;
    }

    public static BattleLuckChatChannel SelectNext(ulong steamId) =>
        IsInAiChannel(steamId) ? Leave(steamId) : Enter(steamId);

    // Compatibility alias for existing callers while AiChannelState remains the
    // only owner of the underlying state.
    public static void Add(ulong steamId) => Enter(steamId);

    public static IReadOnlyList<ulong> GetAiChannelMembers() =>
        AiMembers.Keys.ToArray();

    public static bool HasActiveRequest(ulong steamId) =>
        ActiveRequests.ContainsKey(steamId);

    public static bool TryBeginRequest(ulong steamId)
    {
        var cancellation = new CancellationTokenSource();
        if (ActiveRequests.TryAdd(steamId, cancellation))
            return true;

        cancellation.Dispose();
        return false;
    }

    public static CancellationToken GetRequestCancellationToken(ulong steamId) =>
        ActiveRequests.TryGetValue(steamId, out var cancellation)
            ? cancellation.Token
            : CancellationToken.None;

    public static void EndRequest(ulong steamId)
    {
        if (!ActiveRequests.TryRemove(steamId, out var cancellation))
            return;

        cancellation.Dispose();
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
