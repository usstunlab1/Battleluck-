using System.Text;

namespace BattleLuck.Services.Chat;

/// <summary>
/// Formats and delivers BattleLuck AI-channel messages through the existing
/// main-thread notification path. No ECS or player lookup is performed from
/// provider continuations.
/// </summary>
public static class AiChannelMessageService
{
    public const string AiBlue = "#4DA6FF";
    public const int MaximumChunkSize = 350;

    public static void BroadcastPlayerEcho(string playerName, string message)
    {
        var safeName = Sanitize(playerName);
        var safeMessage = Sanitize(message);

        foreach (var chunk in Chunk(safeMessage, MaximumChunkSize))
            BroadcastFormatted($"[AI] {safeName}: {chunk}");
    }

    public static void BroadcastAiReply(string reply)
    {
        var safeReply = Sanitize(reply);

        foreach (var chunk in Chunk(safeReply, MaximumChunkSize))
            BroadcastFormatted($"[AI] BattleLuck: {chunk}");
    }

    public static void SendStatus(ulong steamId, string message) =>
        SendPrivate(steamId, $"[AI] {Sanitize(message)}");

    public static void SendError(ulong steamId, string message) =>
        SendPrivate(steamId, $"[AI] {Sanitize(message)}");

    private static void BroadcastFormatted(string message)
    {
        var richText = Colorize(message);
        foreach (var memberSteamId in AiChannelState.GetAiChannelMembers())
            BattleLuckPlugin.NotifyPlayerBySteamIdOnMainThread(memberSteamId, richText);
    }

    private static void SendPrivate(ulong steamId, string message) =>
        BattleLuckPlugin.NotifyPlayerBySteamIdOnMainThread(steamId, Colorize(message));

    private static string Colorize(string message) =>
        $"<color={AiBlue}>{message}</color>";

    internal static string Sanitize(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;

        // V Rising chat accepts Unity rich text. Escape every delimiter before
        // wrapping our own trusted color tag so player/provider text cannot inject
        // formatting or unexpectedly close the channel label.
        return value
            .Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace(">", "&gt;", StringComparison.Ordinal)
            .Replace("\r", " ", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal)
            .Trim();
    }

    internal static IEnumerable<string> Chunk(string text, int maximumLength)
    {
        if (string.IsNullOrEmpty(text))
            yield break;

        if (maximumLength <= 0)
            throw new ArgumentOutOfRangeException(nameof(maximumLength));

        var offset = 0;
        while (offset < text.Length)
        {
            var remaining = text.Length - offset;
            var length = Math.Min(maximumLength, remaining);

            if (length < remaining)
            {
                var split = text.LastIndexOf(' ', offset + length - 1, length);
                if (split > offset)
                    length = split - offset;
            }

            var chunk = text.Substring(offset, length).Trim();
            if (chunk.Length > 0)
                yield return chunk;

            offset += length;
            while (offset < text.Length && text[offset] == ' ')
                offset++;
        }
    }
}
