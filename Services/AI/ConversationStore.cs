using System;
using System.Collections.Generic;
using System.Text;

namespace BattleLuck.Services.AI;

/// <summary>Who authored a conversation turn.</summary>
public enum ConversationSpeaker
{
    Player,
    Admin,
    Ai,
    System
}

/// <summary>A single turn in the shared conversation log.</summary>
public sealed class ConversationTurn
{
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
    public ConversationSpeaker Speaker { get; init; }
    public string DisplayName { get; init; } = "";
    public ulong SteamId { get; init; }
    public string Text { get; init; } = "";
    public List<string> ActionResults { get; init; } = new();
}

/// <summary>
/// Single shared conversation log that humans and the AI both read and write.
/// This is the "same chat we can all read": every human message, AI reply, and
/// executed action outcome is appended here, and recent turns are fed back to the
/// LLM as context so it has conversation history.
/// </summary>
public sealed class ConversationStore
{
    public static ConversationStore Instance { get; } = new();

    readonly object _lock = new();
    readonly List<ConversationTurn> _turns = new();
    readonly int _maxTurns;

    public ConversationStore(int maxTurns = 200)
    {
        _maxTurns = maxTurns;
    }

    public void Append(ConversationTurn turn)
    {
        lock (_lock)
        {
            _turns.Add(turn);
            if (_turns.Count > _maxTurns)
                _turns.RemoveRange(0, _turns.Count - _maxTurns);
        }
    }

    public IReadOnlyList<ConversationTurn> Recent(int n)
    {
        lock (_lock)
        {
            var start = Math.Max(0, _turns.Count - n);
            return _turns.GetRange(start, _turns.Count - start);
        }
    }

    /// <summary>Render the last <paramref name="n"/> turns as plain text for LLM context.</summary>
    public string FormatForContext(int n)
    {
        var sb = new StringBuilder();
        foreach (var t in Recent(n))
        {
            sb.Append($"[{t.Timestamp:HH:mm:ss}] {t.Speaker}: {t.Text}\n");
            foreach (var r in t.ActionResults)
                sb.Append($"  -> {r}\n");
        }
        return sb.ToString();
    }
}
