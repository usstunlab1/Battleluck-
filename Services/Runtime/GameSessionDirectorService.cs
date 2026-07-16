namespace BattleLuck.Services.Runtime;

/// <summary>
/// Read-only director view for admins and LLM prompts.
/// It summarizes live sessions, runtime wiring, catalog health, and safe next steps.
/// </summary>
public static class GameSessionDirectorService
{
    public static GameDirectorReport Build(string? modeId = null)
    {
        var report = new GameDirectorReport
        {
            CapturedUtc = DateTime.UtcNow,
            ModeFilter = modeId?.Trim() ?? ""
        };

        LoadAi(report);
        LoadCatalog(report);
        LoadSessions(report);
        BuildRecommendations(report);

        return report;
    }

    public static string BuildPromptContext(string? modeId = null)
    {
        var report = Build(modeId);
        var lines = new List<string>
        {
            "BattleLuck Game Session Director snapshot:",
            $"health={report.Health}; onlinePlayers={report.OnlinePlayers}; activeSessions={report.ActiveSessions}; enteredPlayers={report.EnteredPlayers}; burningPenalty={report.BurningPlayers}",
            $"ai=requested:{report.AiRequestedProvider} active:{report.AiActiveProvider} eventAuthoring:{report.EventAuthoringEnabled} maxActions:{report.EventAuthoringMaxActions} status:{report.AiProviderStatus}",
            $"catalog=actions:{report.ActionCount} handlers:{report.HandlerCount} risk safe/controlled/destructive:{report.SafeActions}/{report.ControlledActions}/{report.DestructiveActions}"
        };

        foreach (var session in report.Sessions.Take(4))
        {
            lines.Add(
                $"session mode={session.ModeId} id={session.SessionId} zone={session.ZoneHash} phase={session.Phase} players={session.Players}/{session.MaxPlayers} elapsed={session.ElapsedSeconds:F0}/{session.TimeLimitSeconds:F0} unified={session.UnifiedRuntimeActive} tracked={session.TrackedObjects} bosses={session.BossesAlive}/{session.BossesTracked} objects walls/floors/platform={session.Walls}/{session.Floors}/{session.PlatformTiles}");
        }

        foreach (var recommendation in report.Recommendations.Take(6))
            lines.Add($"director_recommendation[{recommendation.Level}] {recommendation.Message}");

        lines.Add("LLM rule: act as a session director. Observe facts, choose catalog-backed actions, preview risky changes, require admin approval, and never mutate players/config directly from normal chat.");
        return string.Join("\n", lines);
    }

    static void LoadAi(GameDirectorReport report)
    {
        var assistant = BattleLuckPlugin.AIAssistant;
        var config = ConfigLoader.LoadAIConfig();

        report.AiEnabled = assistant?.IsEnabled == true;
        report.AiRequestedProvider = assistant?.Provider ?? config.Provider;
        report.AiActiveProvider = assistant?.ActiveProvider ?? "none";
        report.AiProviderStatus = assistant?.ProviderStatus ?? "not initialized";
        report.EventAuthoringEnabled = assistant?.EventAuthoringEnabled ?? config.EventAuthoring.Enabled;
        report.EventAuthoringMaxActions = assistant?.EventAuthoringMaxActions ?? Math.Clamp(config.EventAuthoring.MaxActionsPerEvent, 1, 1000);
        report.ProjectMAiGroupEnabled = config.ProjectMAiGroup.Enabled;
        report.ProjectMAiGroupWired = BattleLuckPlugin.AiGroupProjectMBridge != null;
        report.McpRuntimeHealthy = assistant?.IsMCPRuntimeHealthy == true;
        report.RuntimeServicesHealthy = assistant?.IsRuntimeServicesHealthy == true;
    }

    static void LoadCatalog(GameDirectorReport report)
    {
        try
        {
            var manifest = new ActionManifestService();
            var entries = manifest.Entries.Values.ToList();
            report.ActionCount = entries.Count;
            report.HandlerCount = entries.Count(e => e.HandlerAvailable);
            report.SafeActions = entries.Count(e => e.RiskLevel.Equals("safe", StringComparison.OrdinalIgnoreCase));
            report.ControlledActions = entries.Count(e => e.RiskLevel.Equals("controlled", StringComparison.OrdinalIgnoreCase));
            report.DestructiveActions = entries.Count(e => e.RiskLevel.Equals("destructive", StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception ex)
        {
            report.Warnings.Add($"Action catalog unavailable: {ex.Message}");
        }
    }

    static void LoadSessions(GameDirectorReport report)
    {
        report.OnlinePlayers = SafeOnlinePlayerCount();

        var controller = BattleLuckPlugin.Session;
        if (controller == null)
        {
            report.Warnings.Add("Session controller is not initialized.");
            return;
        }

        report.EnteredPlayers = controller.EnteredPlayerCount;
        report.BurningPlayers = controller.BurningPlayerCount;

        var sessions = controller.ActiveSessions.Values
            .Where(s => string.IsNullOrWhiteSpace(report.ModeFilter) ||
                        s.Context.ModeId.Equals(report.ModeFilter, StringComparison.OrdinalIgnoreCase))
            .ToList();

        report.ActiveSessions = sessions.Count;
        foreach (var session in sessions)
            report.Sessions.Add(BuildSession(controller, session));
    }

    static GameDirectorSession BuildSession(SessionController controller, ActiveSession session)
    {
        var zone = session.Config.Zones.Zones.FirstOrDefault(z => z.Hash == session.Context.ZoneHash);
        var rules = session.Config.Session.Rules;
        var runtime = controller.EventRuntime.GetStatus(session.Context.SessionId);
        var controlledNpcs = BattleLuckPlugin.NpcService?.List(session.Context.SessionId) ?? Array.Empty<ControlledNpcEntry>();

        return new GameDirectorSession
        {
            SessionId = session.Context.SessionId,
            ModeId = session.Context.ModeId,
            ZoneHash = session.Context.ZoneHash.ToString(),
            ZoneName = zone?.Name ?? "",
            Phase = session.IsPaused ? "paused" : session.IsStarted ? "active" : "preparing",
            Started = session.IsStarted,
            Paused = session.IsPaused,
            ArenaInitialized = session.ArenaInitialized,
            Players = session.Context.Players.Count,
            MaxPlayers = rules.MaxPlayers,
            ElapsedSeconds = session.Context.ElapsedSeconds,
            TimeLimitSeconds = session.Context.TimeLimitSeconds,
            UnifiedRuntimeActive = runtime != null,
            EventId = runtime?.EventId ?? "",
            EventActions = runtime?.TotalConfiguredActions ?? 0,
            TrackedObjects = runtime?.TrackedEntities.AliveNonPlayers ?? 0,
            TrackedPlayers = 0,
            RuntimeTriggers = runtime?.Triggers ?? 0,
            RuntimeTimers = runtime?.Timers.Count ?? 0,
            ActiveCustomSequences = runtime?.ActiveCustomSequences ?? 0,
            BossesTracked = controlledNpcs.Count,
            BossesAlive = controlledNpcs.Count(npc => npc.IsAlive),
            ServantsAlive = 0,
            NpcsTracked = BattleLuckPlugin.NpcService?.List(session.Context.SessionId).Count ?? 0,
            SpawnedUnits = session.Spawner.AliveCount,
            Walls = 0, // DISABLED: walls border
            Floors = 0, // DISABLED: walls border
            PlatformTiles = session.Platform?.TileCount ?? 0,
            FlowEnterActions = CountFlowActions(session.Config.FlowEnter)
                + CountFlowActions(session.Config.Session.Flow.Enter)
                + CountFlowActions(session.Config.Session.Flow.Start)
                + CountFlowActions(session.Config.Session.Flow.Tracking)
                + CountFlowActions(session.Config.Session.Flow.Winner),
            FlowExitActions = CountFlowActions(session.Config.FlowExit)
                + CountFlowActions(session.Config.Session.Flow.Exit)
                + CountFlowActions(session.Config.Session.Flow.Ending)
        };
    }

    static int CountFlowActions(FlowConfig? flow)
    {
        if (flow == null)
            return 0;

        return flow.Flows.Values.Sum(f => f.Actions.Count);
    }

    static int SafeOnlinePlayerCount()
    {
        try
        {
            return VRisingCore.GetOnlinePlayers().Count(e => e.Exists() && e.IsPlayer());
        }
        catch
        {
            return 0;
        }
    }

    static void BuildRecommendations(GameDirectorReport report)
    {
        if (!report.AiEnabled)
            report.Recommendations.Add(GameDirectorRecommendation.Error("AI assistant is not initialized. Run `.ai.reload` after checking ai_config.json."));
        else if (report.AiRequestedProvider.Contains("llama", StringComparison.OrdinalIgnoreCase) &&
                 report.AiActiveProvider.Equals("local", StringComparison.OrdinalIgnoreCase))
            report.Recommendations.Add(GameDirectorRecommendation.Warning("Local simple fallback is active. Start local llama-server, then run `.ai.reload`."));

        if (!report.EventAuthoringEnabled)
            report.Recommendations.Add(GameDirectorRecommendation.Warning("Event authoring is disabled; `.ai event request` cannot write previews."));

        if (report.ActionCount == 0)
            report.Recommendations.Add(GameDirectorRecommendation.Error("Action catalog is empty or missing. Check actions_catalog.json."));
        else if (report.HandlerCount < report.ActionCount)
            report.Recommendations.Add(GameDirectorRecommendation.Info($"{report.ActionCount - report.HandlerCount} catalog actions are metadata-only or missing handlers."));

        if (report.ActiveSessions == 0)
        {
            report.Recommendations.Add(GameDirectorRecommendation.Info("No active sessions. Use `.modelist`, `.toggleenter <mode>`, or `.event.start <mode>` to stage a session."));
        }
        else
        {
            foreach (var session in report.Sessions)
            {
                if (!session.UnifiedRuntimeActive)
                    report.Recommendations.Add(GameDirectorRecommendation.Warning($"{session.ModeId} is running legacy/split config only. Add a unified event file for director-grade orchestration."));
                if (!session.Started)
                    report.Recommendations.Add(GameDirectorRecommendation.Info($"{session.ModeId} is preparing. Wait for arena readiness or use `.start` as admin after checks."));
                if (session.BossesTracked > 0 && session.BossesAlive == 0)
                    report.Recommendations.Add(GameDirectorRecommendation.Warning($"{session.ModeId} has tracked boss definitions but no alive boss."));
                if (session.TrackedPlayers > 0)
                    report.Recommendations.Add(GameDirectorRecommendation.Error($"{session.ModeId} registry reports tracked players. Cleanup must never destroy players; audit owner tracking."));
            }
        }

        if (report.BurningPlayers > 0)
            report.Recommendations.Add(GameDirectorRecommendation.Warning($"{report.BurningPlayers} player(s) have burning penalty. Use `.event.clearburning` if this is inside event zones."));

        if (report.Recommendations.Count == 0)
            report.Recommendations.Add(GameDirectorRecommendation.Good("Director view is healthy. Use `.ai event request <change>` for preview-first changes."));

        report.Health = report.Recommendations.Any(r => r.Level == "error") ? "error" :
            report.Recommendations.Any(r => r.Level == "warning") ? "warning" :
            "good";
    }
}

public sealed class GameDirectorReport
{
    public DateTime CapturedUtc { get; set; }
    public string ModeFilter { get; set; } = "";
    public string Health { get; set; } = "unknown";
    public int OnlinePlayers { get; set; }
    public int ActiveSessions { get; set; }
    public int EnteredPlayers { get; set; }
    public int BurningPlayers { get; set; }
    public bool AiEnabled { get; set; }
    public string AiRequestedProvider { get; set; } = "";
    public string AiActiveProvider { get; set; } = "";
    public string AiProviderStatus { get; set; } = "";
    public bool EventAuthoringEnabled { get; set; }
    public int EventAuthoringMaxActions { get; set; }
    public bool ProjectMAiGroupEnabled { get; set; }
    public bool ProjectMAiGroupWired { get; set; }
    public bool McpRuntimeHealthy { get; set; }
    public bool RuntimeServicesHealthy { get; set; }
    public int ActionCount { get; set; }
    public int HandlerCount { get; set; }
    public int SafeActions { get; set; }
    public int ControlledActions { get; set; }
    public int DestructiveActions { get; set; }
    public List<GameDirectorSession> Sessions { get; } = new();
    public List<GameDirectorRecommendation> Recommendations { get; } = new();
    public List<string> Warnings { get; } = new();

    public IEnumerable<string> ToChatLines(int maxSessions = 4, int maxRecommendations = 6)
    {
        yield return $"Director: health={Health}, online={OnlinePlayers}, sessions={ActiveSessions}, entered={EnteredPlayers}, burn={BurningPlayers}";
        yield return $"AI: requested={AiRequestedProvider}, active={AiActiveProvider}, authoring={(EventAuthoringEnabled ? "on" : "off")}, maxActions={EventAuthoringMaxActions}, ProjectM={(ProjectMAiGroupEnabled && ProjectMAiGroupWired ? "wired" : ProjectMAiGroupEnabled ? "enabled/not-wired" : "off")}";
        yield return $"Catalog: actions={ActionCount}, handlers={HandlerCount}, risk safe/control/destructive={SafeActions}/{ControlledActions}/{DestructiveActions}";

        foreach (var session in Sessions.Take(maxSessions))
        {
            yield return $"{session.ModeId}/{session.ZoneHash}: {session.Phase}, players={session.Players}/{session.MaxPlayers}, elapsed={session.ElapsedSeconds:F0}/{session.TimeLimitSeconds:F0}s, unified={(session.UnifiedRuntimeActive ? session.EventId : "legacy")}";
            yield return $"  runtime: actions={session.EventActions}, triggers={session.RuntimeTriggers}, timers={session.RuntimeTimers}, sequences={session.ActiveCustomSequences}, tracked={session.TrackedObjects}, bosses={session.BossesAlive}/{session.BossesTracked}, npcs={session.NpcsTracked}";
            yield return $"  arena: units={session.SpawnedUnits}, walls={session.Walls}, floors={session.Floors}, platforms={session.PlatformTiles}, flows enter/exit={session.FlowEnterActions}/{session.FlowExitActions}";
        }

        foreach (var warning in Warnings.Take(3))
            yield return $"Warning: {warning}";

        foreach (var recommendation in Recommendations.Take(maxRecommendations))
            yield return $"{recommendation.Level.ToUpperInvariant()}: {recommendation.Message}";
    }
}

public sealed class GameDirectorSession
{
    public string SessionId { get; set; } = "";
    public string ModeId { get; set; } = "";
    public string ZoneHash { get; set; } = "";
    public string ZoneName { get; set; } = "";
    public string Phase { get; set; } = "";
    public bool Started { get; set; }
    public bool Paused { get; set; }
    public bool ArenaInitialized { get; set; }
    public bool ArenaSpawning { get; set; }
    public int Players { get; set; }
    public int MaxPlayers { get; set; }
    public double ElapsedSeconds { get; set; }
    public double TimeLimitSeconds { get; set; }
    public bool UnifiedRuntimeActive { get; set; }
    public string EventId { get; set; } = "";
    public int EventActions { get; set; }
    public int RuntimeTriggers { get; set; }
    public int RuntimeTimers { get; set; }
    public int ActiveCustomSequences { get; set; }
    public int TrackedObjects { get; set; }
    public int TrackedPlayers { get; set; }
    public int BossesTracked { get; set; }
    public int BossesAlive { get; set; }
    public int ServantsAlive { get; set; }
    public int NpcsTracked { get; set; }
    public int SpawnedUnits { get; set; }
    public int Walls { get; set; }
    public int Floors { get; set; }
    public int PlatformTiles { get; set; }
    public int FlowEnterActions { get; set; }
    public int FlowExitActions { get; set; }
}

public sealed class GameDirectorRecommendation
{
    public string Level { get; set; } = "info";
    public string Message { get; set; } = "";

    public static GameDirectorRecommendation Good(string message) => new() { Level = "good", Message = message };
    public static GameDirectorRecommendation Info(string message) => new() { Level = "info", Message = message };
    public static GameDirectorRecommendation Warning(string message) => new() { Level = "warning", Message = message };
    public static GameDirectorRecommendation Error(string message) => new() { Level = "error", Message = message };
}
