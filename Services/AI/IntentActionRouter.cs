
namespace BattleLuck.Services.AI;

/// <summary>
/// Deterministic intent router for the BattleLuck AI chat surface.
///
/// When a player types natural language on the <c>ai:</c> path, this turns a clear,
/// imperative request into a BattleLuck action: SAFE verbs (<c>join [mode]</c> / <c>leave</c>)
/// run immediately; risky/admin verbs (<c>start</c> / <c>pause</c> / <c>resume</c> /
/// <c>end</c>) are admin-gated and require confirmation. Anything it doesn't recognize returns
/// <c>false</c> so <see cref="GameChatAiBridge"/> falls through to the LLM assistant.
///
/// Replies are delivered tagged <c>[AI]</c> so any client UI can route them into the BattleLuck AI
/// chat tab (and they read fine as plain System chat without a client UI).
///
/// Threading: called synchronously from the chat-system postfix (main server thread), so it
/// is safe to touch ECS via <see cref="SessionController"/> here. Risky/admin verbs require
/// confirmation via <see cref="IntentActionConfirmRegistry"/> before running.
/// </summary>
public static class IntentActionRouter
{
    enum Intent { None, Join, Leave, Start, Pause, Resume, End, EndAll, Confirm, Cancel }

    /// <summary>Try to handle a message as a BattleLuck action. Returns true if it was handled.</summary>
    public static bool TryHandle(ulong steamId, string query)
    {
        if (steamId == 0 || string.IsNullOrWhiteSpace(query))
            return false;

        var session = BattleLuckPlugin.Session;
        if (session == null)
            return false;

        var (intent, arg) = ParseIntent(steamId, query);
        return intent switch
        {
            // Safe — run immediately.
            Intent.Join => ExecuteJoin(session, steamId, arg),
            Intent.Leave => ExecuteLeave(session, steamId),

            // Risky / admin — propose, then confirm.
            Intent.Start => ProposeAdmin(steamId, "start", "force-start your prepared session",
                () => { var r = session.ForceStartForPlayer(steamId); return (r.Success, r.Success ? "Start requested." : r.UserMessage); }),
            Intent.Pause => ProposeAdmin(steamId, "pause", "pause ALL active sessions",
                () => { var n = session.ActiveSessions.Count; session.PauseAll(); return (true, $"Paused {n} session(s)."); }),
            Intent.Resume => ProposeAdmin(steamId, "resume", "resume paused sessions",
                () => (true, $"Resumed {session.ResumeAll()} session(s).")),
            Intent.End => ProposeEnd(session, steamId, arg),
            Intent.EndAll => ProposeAdmin(steamId, "endall", "end ALL active sessions",
                () => (true, EndAllModes(session))),

            // Confirmation flow.
            Intent.Confirm => HandleConfirm(steamId, arg),
            Intent.Cancel => HandleCancel(steamId),

            _ => false,
        };
    }

    /// <summary>
    /// Player-chat surface: only self-service join/leave intents are allowed.
    /// Controlled session actions stay on explicit authenticated admin commands
    /// rather than being reachable through free-form AI chat.
    /// </summary>
    public static bool TryHandlePlayerSelfService(ulong steamId, string query)
    {
        if (steamId == 0 || string.IsNullOrWhiteSpace(query))
            return false;

        var session = BattleLuckPlugin.Session;
        if (session == null)
            return false;

        var (intent, arg) = ParseIntent(steamId, query);
        return intent switch
        {
            Intent.Join => ExecuteJoin(session, steamId, arg),
            Intent.Leave => ExecuteLeave(session, steamId),
            _ => false
        };
    }

    static (Intent intent, string arg) ParseIntent(ulong steamId, string query)
    {
        var trimmed = query.Trim();
        var q = trimmed.ToLowerInvariant();

        // Confirm / cancel a pending action. Bare affirmations only count when something is
        // actually pending, so ordinary conversation with the AI isn't hijacked.
        if (q.StartsWith("confirm ", StringComparison.Ordinal))
            return (Intent.Confirm, trimmed["confirm ".Length..].Trim());
        if (q is "yes" or "y" or "ok" or "okay" or "confirm" or "do it" or "go ahead")
            return IntentActionConfirmRegistry.HasPending(steamId) ? (Intent.Confirm, string.Empty) : (Intent.None, string.Empty);
        if (q is "no" or "n" or "cancel" or "nvm" or "nevermind" or "never mind")
            return IntentActionConfirmRegistry.HasPending(steamId) ? (Intent.Cancel, string.Empty) : (Intent.None, string.Empty);

        // leave / exit the current session (safe)
        if (q is "leave" or "exit" or "quit" or "leave match" or "leave arena" or "leave session"
            || q.StartsWith("leave ", StringComparison.Ordinal)
            || q.StartsWith("exit ", StringComparison.Ordinal))
            return (Intent.Leave, string.Empty);

        // join / enter / queue [mode] (safe)
        foreach (var kw in new[] { "join", "enter", "queue" })
        {
            if (q == kw)
                return (Intent.Join, string.Empty);

            var prefix = kw + " ";
            if (q.StartsWith(prefix, StringComparison.Ordinal))
                return (Intent.Join, StripFiller(trimmed[prefix.Length..]));
        }

        // session controls (risky / admin)
        if (q is "start" or "start match" or "start the match" or "begin")
            return (Intent.Start, string.Empty);
        if (q is "pause" or "pause all" or "pause sessions")
            return (Intent.Pause, string.Empty);
        if (q is "resume" or "unpause" or "resume all")
            return (Intent.Resume, string.Empty);
        if (q is "end all" or "endall" or "end everything" or "stop everything")
            return (Intent.EndAll, string.Empty);
        if (q is "end" or "end match" or "end session" or "end event")
            return (Intent.End, string.Empty);
        if (q.StartsWith("end ", StringComparison.Ordinal))
            return (Intent.End, StripFiller(trimmed["end ".Length..]));

        return (Intent.None, string.Empty);
    }

    /// <summary>Drop common filler words so "join the bloodbath mode" &#8594; "bloodbath".</summary>
    static string StripFiller(string s)
    {
        var tokens = s.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(t => t.ToLowerInvariant() is not ("the" or "a" or "an" or "mode" or "match"
                or "arena" or "please" or "session"))
            .ToArray();
        return string.Join(" ", tokens).Trim();
    }

    static bool ExecuteJoin(SessionController session, ulong steamId, string modeArg)
    {
        var character = ResolveCharacter(steamId);
        if (character == Entity.Null)
            return false;

        var modeId = ResolveModeId(modeArg);
        var result = session.ToggleEnter(steamId, character, modeId);
        Reply(steamId, result.Success
            ? (string.IsNullOrEmpty(modeId) ? "Joining the session\u2026" : $"Joining {DisplayName(modeId)}\u2026")
            : result.Error ?? "Could not join the session.");
        return true;
    }

    static bool ExecuteLeave(SessionController session, ulong steamId)
    {
        var character = ResolveCharacter(steamId);
        if (character == Entity.Null)
            return false;

        var result = session.ToggleLeave(steamId, character);
        Reply(steamId, result.Success
            ? "Leaving the session \u2014 your gear and position are restored."
            : result.Error ?? "Could not leave the session.");
        return true;
    }

    // ── Risky / admin proposals (require confirmation before running) ───────────

    static bool ProposeAdmin(ulong steamId, string label, string summary, Func<(bool ok, string message)> execute)
    {
        if (!IsAdmin(steamId))
        {
            Reply(steamId, "That action needs admin permission.");
            return true;
        }
        return Propose(steamId, label, summary, () =>
        {
            if (!IsAdmin(steamId))
                return (false, "Admin permission was revoked before confirmation.");
            return execute();
        });
    }

    static bool ProposeEnd(SessionController session, ulong steamId, string modeArg)
    {
        if (!IsAdmin(steamId))
        {
            Reply(steamId, "That action needs admin permission.");
            return true;
        }

        var modeId = ResolveModeId(modeArg);
        if (string.IsNullOrEmpty(modeId))
        {
            var modes = ActiveModeIds(session);
            if (modes.Count == 1)
                modeId = modes[0];
            else
            {
                Reply(steamId, modes.Count == 0
                    ? "No active sessions to end."
                    : $"Which mode? Say 'end <mode>'. Active: {string.Join(", ", modes)}");
                return true;
            }
        }

        var target = modeId!;
        return Propose(steamId, $"end:{target}", $"end all '{DisplayName(target)}' sessions",
            () =>
            {
                if (!IsAdmin(steamId))
                    return (false, "Admin permission was revoked before confirmation.");
                session.ForceEndByModeId(target);
                return (true, $"Ended '{DisplayName(target)}'.");
            });
    }

    static bool Propose(ulong steamId, string label, string summary, Func<(bool ok, string message)> execute)
    {
        var token = IntentActionConfirmRegistry.Register(steamId, label, summary, execute);
        Reply(steamId, $"Confirm: {summary}? Reply 'yes' (or 'confirm {token}'), or 'no' to cancel.");
        return true;
    }

    static bool HandleConfirm(ulong steamId, string token)
    {
        if (!IntentActionConfirmRegistry.TryConfirm(steamId, token, out var ok, out var message))
        {
            Reply(steamId, "Nothing to confirm.");
            return true;
        }

        Reply(steamId, ok ? message : $"Couldn't do that: {message}");
        return true;
    }

    static bool HandleCancel(ulong steamId)
    {
        Reply(steamId, IntentActionConfirmRegistry.CancelLatest(steamId, out _) ? "Cancelled." : "Nothing to cancel.");
        return true;
    }

    static string EndAllModes(SessionController session)
    {
        var modes = ActiveModeIds(session);
        foreach (var modeId in modes)
            session.ForceEndByModeId(modeId);
        return $"Ended {modes.Count} mode(s).";
    }

    static List<string> ActiveModeIds(SessionController session)
        => session.ActiveSessions.Values
            .Select(s => s.Context?.ModeId)
            .Where(m => !string.IsNullOrEmpty(m))
            .Select(m => m!)
            .Distinct()
            .ToList();

    static bool IsAdmin(ulong steamId)
    {
        var character = ResolveCharacter(steamId);
        if (character == Entity.Null)
            return false;
        return FlowController.TryGetUser(character, out var user) && user.IsAdmin;
    }

    /// <summary>Map a free-text mode name to a registered mode id, or null (= use the current zone).</summary>
    static string? ResolveModeId(string modeArg)
    {
        if (string.IsNullOrWhiteSpace(modeArg))
            return null;

        var registry = BattleLuckPlugin.GameModes;
        if (registry == null)
            return modeArg;

        foreach (var id in registry.All.Keys)
            if (id.Equals(modeArg, StringComparison.OrdinalIgnoreCase))
                return id;

        var compact = modeArg.Replace(" ", string.Empty);
        foreach (var kv in registry.All)
        {
            var name = kv.Value.DisplayName ?? string.Empty;
            if (name.Equals(modeArg, StringComparison.OrdinalIgnoreCase)
                || name.Replace(" ", string.Empty).Equals(compact, StringComparison.OrdinalIgnoreCase)
                || kv.Key.Contains(modeArg, StringComparison.OrdinalIgnoreCase)
                || name.Contains(modeArg, StringComparison.OrdinalIgnoreCase))
                return kv.Key;
        }

        // Unknown — pass through so ToggleEnter can reject it with a helpful message.
        return modeArg;
    }

    static Entity ResolveCharacter(ulong steamId)
    {
        if (!VRisingCore.IsReady)
            return Entity.Null;
        foreach (var player in VRisingCore.GetOnlinePlayers())
        {
            if (player.Exists() && player.IsPlayer() && player.GetSteamId() == steamId)
                return player;
        }
        return Entity.Null;
    }

    static string DisplayName(string? modeId)
    {
        if (string.IsNullOrEmpty(modeId))
            return "the session";

        var mode = BattleLuckPlugin.GameModes?.Resolve(modeId);
        return string.IsNullOrWhiteSpace(mode?.DisplayName) ? modeId! : mode!.DisplayName;
    }

    static void Reply(ulong steamId, string message)
    {
        var character = ResolveCharacter(steamId);
        if (character == Entity.Null)
            return;
        if (FlowController.TryGetUser(character, out var user))
            NotificationHelper.NotifyPlayer(user, $"[AI] {message}");
    }
}
