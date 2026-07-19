using System.Collections.Generic;
using System.Linq;
using VampireCommandFramework;

public static class ModeCommands
{
    [Command("modelist", description: "List all registered game modes", adminOnly: true)]
    public static void ListModes(ChatCommandContext ctx)
    {
        var registry = BattleLuckPlugin.GameModes;
        if (registry == null)
        {
            ctx.Reply("Game mode registry not initialized.");
            return;
        }

        var modes = registry.GetRegisteredModes();
        if (modes.Count == 0)
        {
            ctx.Reply("No game modes registered.");
            return;
        }

        ctx.Reply($"Registered modes ({modes.Count}):");
        foreach (var modeId in modes)
        {
            var mode = registry.Resolve(modeId);
            ctx.Reply($"  {modeId} — {mode?.DisplayName ?? "?"}");
        }

        var session = BattleLuckPlugin.Session;
        if (session != null && session.ActiveSessions.Count > 0)
        {
            ctx.Reply($"Active sessions ({session.ActiveSessions.Count}):");
            foreach (var kv in session.ActiveSessions)
            {
                ctx.Reply($"  Zone {kv.Key} — {kv.Value.Context.ModeId} ({kv.Value.Context.Players.Count} players)");
            }
        }
    }

    [Command("modestart", description: "Start a game mode manually", adminOnly: true)]
    public static void StartMode(ChatCommandContext ctx, string modeId)
    {
        var registry = BattleLuckPlugin.GameModes;
        if (registry == null)
        {
            ctx.Reply("Game mode registry not initialized.");
            return;
        }

        var mode = registry.Resolve(modeId);
        if (mode == null)
        {
            ctx.Reply($"Unknown mode: {modeId}");
            return;
        }

        ctx.Reply($"Mode '{mode.DisplayName}' is registered. Walk into the zone to start a session, or use force {modeId}.");
    }

    [Command("modeend", description: "Force-end all sessions for a mode", adminOnly: true)]
    public static void EndMode(ChatCommandContext ctx, string modeId)
    {
        var session = BattleLuckPlugin.Session;
        if (session == null)
        {
            ctx.Reply("Session controller not initialized.");
            return;
        }

        session.ForceEndByModeId(modeId);
        ctx.Reply($"Force-ended all sessions for '{modeId}'.");
    }

    [Command("modeinfo", description: "Show mode configuration details", adminOnly: true)]
    public static void ModeInfo(ChatCommandContext ctx, string modeId)
    {
        try
        {
            var config = ConfigLoader.Load(modeId);

            ctx.Reply($"Mode: {modeId}");
            ctx.Reply($"  Display: {config.Session.DisplayName}");
            ctx.Reply($"  Description: {config.Session.Description}");
            ctx.Reply($"  MinPlayers: {config.Session.Rules.MinPlayers}");
            ctx.Reply($"  MaxPlayers: {config.Session.Rules.MaxPlayers}");
            ctx.Reply($"  MatchDuration: {config.Session.Rules.MatchDurationMinutes} min");
            ctx.Reply($"  EnablePvP: {config.Session.Rules.EnablePvP}");
            ctx.Reply($"  EnableVBloods: {config.Session.Rules.EnableVBloods}");

            if (config.Zones.Zones.Count > 0)
            {
                ctx.Reply($"  Zones ({config.Zones.Zones.Count}):");
                foreach (var zone in config.Zones.Zones)
                {
                    ctx.Reply($"    - {zone.Name} (hash={zone.Hash}, radius={zone.Radius})");
                }
            }
        }
        catch (System.Exception ex)
        {
            ctx.Reply($"Error loading config: {ex.Message}");
        }
    }

    [Command("force", description: "Teleport to mode's zone and auto-start session", adminOnly: true)]
    public static void ForceStart(ChatCommandContext ctx, string modeId)
    {
        var registry = BattleLuckPlugin.GameModes;
        if (registry == null)
        {
            ctx.Reply("Game mode registry not initialized.");
            return;
        }

        var mode = registry.Resolve(modeId);
        if (mode == null)
        {
            ctx.Reply($"Unknown mode: {modeId}. Use modelist to see available modes.");
            return;
        }

        var session = BattleLuckPlugin.Session;
        if (session == null)
        {
            ctx.Reply("Session controller not initialized.");
            return;
        }

        var entity = ctx.Event.SenderCharacterEntity;
        var result = session.ForceStart(modeId, entity);
        if (!result.Success)
        {
            ctx.Reply($"mode.start failed: {result.Error}");
            return;
        }
        ctx.Reply($"Entering {mode.DisplayName}; forced start is queued after build checks and the 10s stun countdown.");
    }

    [Command("entermode", description: "Admin alias for force-entering a mode and queueing start", adminOnly: true)]
    public static void EnterMode(ChatCommandContext ctx, string modeId)
    {
        ForceStart(ctx, modeId);
    }

    [Command("autoend", description: "End the current event session or all sessions for a mode", adminOnly: true)]
    public static void AutoEnd(ChatCommandContext ctx, string modeId = "")
    {
        var session = BattleLuckPlugin.Session;
        if (session == null)
        {
            ctx.Reply("Session controller not initialized.");
            return;
        }

        if (!string.IsNullOrWhiteSpace(modeId))
        {
            session.ForceEndByModeId(modeId);
            ctx.Reply($"Auto-ended sessions for '{modeId}'.");
            return;
        }

        var steamId = ctx.Event.SenderCharacterEntity.GetSteamId();
        var active = session.ActiveSessions.Values.FirstOrDefault(s => s.Context.Players.Contains(steamId));
        if (active == null)
        {
            ctx.Reply("You are not in an active event session. Use .autoend <modeId>.");
            return;
        }

        session.ForceEndByModeId(active.Context.ModeId);
        ctx.Reply($"Auto-ended current mode '{active.Context.ModeId}'.");
    }

    [Command("modepolicy", description: "Show effective zone enter and action staging policy. Usage: .modepolicy [modeId]", adminOnly: true)]
    public static void ModePolicy(ChatCommandContext ctx, string modeId = "")
    {
        try
        {
            if (string.IsNullOrWhiteSpace(modeId))
            {
                var session = BattleLuckPlugin.Session;
                if (session != null)
                {
                    var steamId = ctx.Event.SenderCharacterEntity.GetSteamId();
                    var active = session.ActiveSessions.Values.FirstOrDefault(s => s.Context.Players.Contains(steamId));
                    if (active != null)
                        modeId = active.Context.ModeId;
                }
            }

            if (string.IsNullOrWhiteSpace(modeId))
            {
                ctx.Reply("Provide a mode id, or run this while inside an active session.");
                return;
            }

            var config = ConfigLoader.Load(modeId);
            var zoneEnterRule = ResolveZoneEnterRule(config);
            var staging = ResolveActionStagingRules(config);

            ctx.Reply($"Mode policy for {modeId}:");
            ctx.Reply($"  zoneEnterRule={zoneEnterRule}");
            ctx.Reply($"  actionStaging.enabled={staging.Enabled}");
            ctx.Reply($"  actionStaging.stageOnZoneEnter={staging.StageOnZoneEnter}");
            ctx.Reply($"  actionStaging.releaseOnMatchStart={staging.ReleaseOnMatchStart}");
        }
        catch (System.Exception ex)
        {
            ctx.Reply($"Failed to read mode policy: {ex.Message}");
        }
    }

    [Command("sessionstaging", description: "Show live session staged-enter state. Usage: .sessionstaging [modeId]", adminOnly: true)]
    public static void SessionStaging(ChatCommandContext ctx, string modeId = "")
    {
        var session = BattleLuckPlugin.Session;
        if (session == null)
        {
            ctx.Reply("Session controller not initialized.");
            return;
        }

        var sessions = session.ActiveSessions.Values
            .Where(s => string.IsNullOrWhiteSpace(modeId) ||
                        s.Context.ModeId.Equals(modeId, System.StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (sessions.Count == 0)
        {
            ctx.Reply(string.IsNullOrWhiteSpace(modeId)
                ? "No active sessions."
                : $"No active sessions for mode '{modeId}'.");
            return;
        }

        ctx.Reply($"Live staging across {sessions.Count} session(s):");
        foreach (var active in sessions)
        {
            var stagedCount = 0;
            if (active.Context.State.TryGetValue("stagedEnterPlayers", out var value) && value is HashSet<ulong> staged)
                stagedCount = staged.Count;

            ctx.Reply(
                $"  {active.Context.ModeId}/zone={active.Context.ZoneHash} started={active.IsStarted} warmup={active.StartWarmupActive} players={active.Context.Players.Count} staged={stagedCount}");
        }
    }

    static string ResolveZoneEnterRule(ModeConfig config)
    {
        var sessionRule = config.Session?.Rules?.ZoneEnterRule;
        if (!string.IsNullOrWhiteSpace(sessionRule))
            return sessionRule.Trim();

        var rulesRule = config.Rules?.ZoneEnterRule;
        if (!string.IsNullOrWhiteSpace(rulesRule))
            return rulesRule.Trim();

        return "auto_enter";
    }

    static ActionStagingRules ResolveActionStagingRules(ModeConfig config)
    {
        return config.Session?.Rules?.ActionStaging
            ?? config.Rules?.ActionStaging
            ?? new ActionStagingRules();
    }
}