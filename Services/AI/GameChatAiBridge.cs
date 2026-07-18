using System.Collections.Concurrent;

namespace BattleLuck.Services.AI;

public static class GameChatAiBridge
{
    static readonly ConcurrentDictionary<string, DateTime> _recent = new();
    static readonly TimeSpan DedupWindow = TimeSpan.FromSeconds(2);
    static GenaiStackClient? _genaiClient;

    public static void Initialize(GenaiStackClient genaiClient)
    {
        _genaiClient = genaiClient;
    }

    /// <summary>Start the bounded four-reply .ai conversation for a player.</summary>
    public static void BeginSession(ulong steamId)
    {
        ConversationStore.Instance.BeginInteractiveSession(steamId);
        BattleLuckPlugin.AIAssistant?.SetInteractiveConversation(steamId, active: true);
    }

    /// <summary>Stop an interactive conversation and clear its provider context.</summary>
    public static bool EndSession(ulong steamId)
    {
        var ended = ConversationStore.Instance.EndInteractiveSession(steamId);
        BattleLuckPlugin.AIAssistant?.SetInteractiveConversation(steamId, active: false);
        return ended;
    }

    public static bool HasSession(ulong steamId) =>
        ConversationStore.Instance.HasInteractiveSession(steamId);

    /// <summary>Record one assistant reply and notify the player when the budget closes.</summary>
    public static void RecordReply(ulong steamId)
    {
        if (!ConversationStore.Instance.TryConsumeInteractiveReply(steamId, out var remaining, out var closed))
            return;

        if (closed)
        {
            BattleLuckPlugin.AIAssistant?.SetInteractiveConversation(steamId, active: false);
            BattleLuckPlugin.NotifyPlayerBySteamIdOnMainThread(
                steamId,
                "[AI] Four replies completed. Say .ai <question> to start another chat, or .ai end to close it earlier.");
        }
        else
        {
            BattleLuckPlugin.NotifyPlayerBySteamIdOnMainThread(
                steamId,
                $"[AI] Conversation active — {remaining} repl{(remaining == 1 ? "y" : "ies")} remaining. Say .ai end to stop now.");
        }
    }

    public static void HandleChatEvent(Entity eventEntity, ChatMessageEvent chatEvent)
    {
        try
        {
            var message = ExtractMessage(chatEvent);
            if (string.IsNullOrWhiteSpace(message))
                return;

            var steamId = ResolveSteamId(eventEntity, chatEvent);
            if (steamId == 0)
                return;

            var channel = ExtractChannel(chatEvent);
            if (!TryExtractAiQuery(message, channel, steamId, out var query))
                return;

            if (!TryMarkRecent(steamId, query))
                return;

            // A direct AI-channel query starts the same bounded conversation as
            // the .ai command. Plain messages are accepted only while that session
            // is active; ordinary game chat is otherwise untouched.
            if (!HasSession(steamId))
                BeginSession(steamId);

            // Record the human message in the shared conversation log so the AI
            // (and everyone else) can read the same chat history.
            ConversationStore.Instance.Append(new ConversationTurn
            {
                Speaker = ConversationSpeaker.Player,
                SteamId = steamId,
                Text = query
            });

            // Intent action router: clear action intents (join / leave) are handled
            // synchronously on the main thread here; only non-actions fall through to the
            // LLM assistant below. This also means actions work even when AI is disabled.
            try
            {
                if (IntentActionRouter.TryHandlePlayerSelfService(steamId, query))
                    return;
            }
            catch (Exception ex)
            {
                BattleLuckPlugin.LogWarning($"[GameChatAI] Action router failed: {ex.Message}");
            }

            // Try genai-stack first if available; fall back to local LLM if not.
            if (_genaiClient != null)
            {
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await HandleGenaiQuery(steamId, query);
                    }
                    catch (Exception ex)
                    {
                        BattleLuckPlugin.LogWarning($"[GameChatAI] Genai query failed: {ex.Message}");
                    }
                });
                return;
            }

            var ai = BattleLuckPlugin.AIAssistant;
            if (ai == null)
                return;

            _ = Task.Run(async () =>
            {
                try
                {
                    var reply = await ai.HandleDirectQuery(steamId, query, source: "game_chat", broadcastToInGameChat: true).ConfigureAwait(false);
                    if (!string.IsNullOrEmpty(reply))
                    {
                        AppendAiTurn(steamId, reply, actionResult: null);
                        RecordReply(steamId);
                    }
                }
                catch (Exception ex)
                {
                    BattleLuckPlugin.LogWarning($"[GameChatAI] Query failed: {ex.Message}");
                }
            });
        }
        catch (Exception ex)
        {
            BattleLuckPlugin.LogWarning($"[GameChatAI] HandleChatEvent failed: {ex.Message}");
        }
    }

    static bool TryExtractAiQuery(string message, string channel, ulong steamId, out string query)
    {
        query = string.Empty;
        var trimmed = message.Trim();
        if (trimmed.Length == 0)
            return false;

        // VCF normally consumes this command first. Keep a defensive fallback
        // so an alternate chat channel can never send the stop command to the LLM.
        if (trimmed.Equals(".ai end", StringComparison.OrdinalIgnoreCase))
            return false;

        if (string.Equals(channel, "ai", StringComparison.OrdinalIgnoreCase))
        {
            query = trimmed;
            return true;
        }

        string[] prefixes = { "ai:", "@ai", "!ai", "/ai", "#ai" };
        foreach (var prefix in prefixes)
        {
            if (!trimmed.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                continue;

            query = trimmed[prefix.Length..].TrimStart(':', ' ', '\t');
            return query.Length > 0;
        }

        // Once a player starts .ai, the next four normal chat messages are
        // conversational follow-ups. This intentionally does not activate for
        // players who have not opted into a session.
        if (HasSession(steamId) && !trimmed.StartsWith(".", StringComparison.Ordinal))
        {
            query = trimmed;
            return query.Length > 0;
        }

        return false;
    }

    static bool TryMarkRecent(ulong steamId, string query)
    {
        var key = $"{steamId}:{query}";
        var now = DateTime.UtcNow;
        if (_recent.TryGetValue(key, out var seenAt) && now - seenAt < DedupWindow)
            return false;

        _recent[key] = now;

        foreach (var kv in _recent)
        {
            if (now - kv.Value > DedupWindow)
                _recent.TryRemove(kv.Key, out _);
        }

        return true;
    }

    public static string ExtractMessage(ChatMessageEvent chatEvent)
        => chatEvent.MessageText.ToString();

    static string ExtractChannel(ChatMessageEvent chatEvent)
        => chatEvent.MessageType.ToString();

    public static ulong ResolveSteamId(Entity eventEntity, ChatMessageEvent chatEvent)
    {
        if (!eventEntity.TryGetComponent(out FromCharacter fromCharacter) ||
            !fromCharacter.User.TryGetComponent(out User user))
            return 0;

        return user.PlatformId;
    }

    static async Task HandleGenaiQuery(ulong steamId, string query)
    {
        if (_genaiClient == null)
            return;

        try
        {
            // Feed recent shared chat history as context so the AI can read prior turns.
            var history = ConversationStore.Instance.FormatForContext(20);
            var contextualQuery = string.IsNullOrWhiteSpace(history)
                ? query
                : $"Conversation history:\n{history}\n\nUser: {query}";

            var sb = new StringBuilder();
            var chunkSize = 200;

            await foreach (var token in _genaiClient.StreamQueryAsync(contextualQuery, useRag: false))
            {
                sb.Append(token);
                if (sb.Length >= chunkSize)
                {
                    BroadcastAIReply(steamId, sb.ToString());
                    sb.Clear();
                }
            }

            var reply = sb.ToString();
            if (reply.Length > 0)
            {
                BroadcastAIReply(steamId, reply);
                AppendAiTurn(steamId, reply, actionResult: null);
                RecordReply(steamId);
            }

            // Player chat is advice-only. Authenticated admins use the explicit
            // preview/approve command path for every live action.
        }
        catch (Exception ex)
        {
            BattleLuckPlugin.LogWarning($"[GameChatAI] HandleGenaiQuery failed: {ex.Message}");
            BroadcastAIReply(steamId, "Sorry, the AI is temporarily unavailable.");
        }
    }

    static void AppendAiTurn(ulong steamId, string reply, string? actionResult)
    {
        var turn = new ConversationTurn
        {
            Speaker = ConversationSpeaker.Ai,
            SteamId = steamId,
            Text = reply
        };
        if (!string.IsNullOrWhiteSpace(actionResult))
            turn.ActionResults.Add(actionResult);
        ConversationStore.Instance.Append(turn);
    }

    static void BroadcastAIReply(ulong steamId, string message)
    {
        BattleLuckPlugin.NotifyPlayerBySteamIdOnMainThread(steamId, $"[AI] {message}");
    }
}
