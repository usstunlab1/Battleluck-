// Models/PlayerContext.cs
// (Previously: PlayerContext.FIXED.cs)
//
// PlayerContext with LastActivityUtc for background janitor tracking.
// Renamed from the temporary FIXED suffix now that the fix is canonical.

using System.Text.Json.Serialization;

namespace BattleLuck.Models;

/// <summary>
/// Per-player context tracked by <c>AIAssistant</c>: conversation history,
/// recent game events, and last-activity timestamp for janitor eviction.
/// </summary>
public class PlayerContext
{
    [JsonPropertyName("steam_id")]
    public ulong SteamId { get; set; }

    [JsonPropertyName("messages")]
    public List<ChatMessage> Messages { get; } = new();

    [JsonPropertyName("recent_events")]
    public List<string> RecentEvents { get; } = new();

    [JsonPropertyName("created_at")]
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    /// <summary>Tracks the last time this context had player activity; used for janitor eviction.</summary>
    [JsonPropertyName("last_activity_at")]
    public DateTime LastActivityUtc { get; set; } = DateTime.UtcNow;

    [JsonPropertyName("message_count")]
    public int MessageCount => Messages.Count;

    public void AddMessage(ChatMessage message)
    {
        Messages.Add(message);
        LastActivityUtc = DateTime.UtcNow;

        // Rolling window: keep only the most recent messages in memory.
        const int maxMessages = 40;
        while (Messages.Count > maxMessages)
            Messages.RemoveAt(0);
    }

    public List<ChatMessage> GetRecentMessages(int count)
    {
        if (Messages.Count <= count)
            return new List<ChatMessage>(Messages);

        return Messages.GetRange(Messages.Count - count, count);
    }

    public void RecordEvent(string description)
    {
        LastActivityUtc = DateTime.UtcNow;
        RecentEvents.Add($"[{DateTime.UtcNow:HH:mm:ss}] {description}");

        // Rolling window: keep only the most recent events.
        const int maxEvents = 20;
        while (RecentEvents.Count > maxEvents)
            RecentEvents.RemoveAt(0);
    }

    public void Clear()
    {
        Messages.Clear();
        RecentEvents.Clear();
        LastActivityUtc = DateTime.UtcNow;
    }
}

/// <summary>Chat message entry in a player conversation history.</summary>
public class ChatMessage
{
    [JsonPropertyName("role")]
    public string Role { get; init; } = "";

    [JsonPropertyName("content")]
    public string Content { get; init; } = "";

    public static ChatMessage System(string content)    => new() { Role = "system",    Content = content };
    public static ChatMessage User(string content)      => new() { Role = "user",      Content = content };
    public static ChatMessage Assistant(string content) => new() { Role = "assistant", Content = content };
}
