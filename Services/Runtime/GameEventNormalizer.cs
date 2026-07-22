using System.Text.Json;
using BattleLuck.ECS.Events;
using BattleLuck.Models;

namespace BattleLuck.Services.Runtime;

/// <summary>
/// The only adapter that converts legacy BattleLuck and ProjectM signals into
/// canonical event-platform facts. It never executes game actions.
/// </summary>
public sealed class GameEventNormalizer : IDisposable
{
    readonly ServerEventPlatform _platform;
    readonly ProjectMEventRouter? _router;
    readonly KillAttributionService _kills = new();
    bool _disposed;

    public GameEventNormalizer(ServerEventPlatform platform, ProjectMEventRouter? router)
    {
        _platform = platform;
        _router = router;
        GameEvents.OnModeStarted += OnModeStarted;
        GameEvents.OnModeEnded += OnModeEnded;
        GameEvents.OnRoundEnded += OnRoundEnded;
        GameEvents.OnPlayerScored += OnPlayerScored;
        GameEvents.OnPlayerLeft += OnPlayerLeft;
        GameEvents.OnPlayerEliminated += OnPlayerEliminated;
        GameEvents.OnObjectiveCaptured += OnObjectiveCaptured;
        GameEvents.OnWaveStarted += OnWaveStarted;
        GameEvents.OnWaveCleared += OnWaveCleared;
        GameEvents.OnActionPerformed += OnActionPerformed;
        if (_router != null)
        {
            _router.OnKill += OnKill;
            _router.OnPlayerDeath += OnPlayerDeath;
        }
    }

    void OnModeStarted(ModeStartedEvent evt) => _platform.Start(evt.SessionId, evt.ModeId, evt.TimestampUtc);

    void OnModeEnded(ModeEndedEvent evt)
    {
        if (!_platform.TryGetRun(evt.SessionId, out var runId, out var modeId))
            return;
        if (evt.WinnerSteamId is { } winner && winner != 0)
            Publish(evt.SessionId, BattleLuckEventIds.WinnerDeclared, winner, null, 0, "mode_winner");
        _platform.Finish(evt.SessionId, "completed", evt.TimestampUtc);
    }

    void OnRoundEnded(RoundEndedEvent evt) => Publish(evt.SessionId, BattleLuckEventIds.RoundEnded,
        null, null, 0, "round_ended", Data(("round", evt.RoundNumber), ("winner", evt.WinnerId)));

    void OnPlayerScored(PlayerScoredEvent evt) => Publish(evt.SessionId, BattleLuckEventIds.ScoreChanged,
        evt.SteamId, null, evt.Points, evt.Reason, Data(("total_score", evt.TotalScore)));

    void OnPlayerLeft(PlayerLeftEvent evt) => Publish(evt.SessionId, BattleLuckEventIds.PlayerLeft,
        evt.SteamId, null, 0, "disconnect");

    void OnPlayerEliminated(PlayerEliminatedEvent evt) => Publish(evt.SessionId, BattleLuckEventIds.PlayerEliminated,
        evt.EliminatedBy, evt.SteamId, 0, "eliminated");

    void OnObjectiveCaptured(ObjectiveCapturedEvent evt) => Publish(evt.SessionId, BattleLuckEventIds.ObjectiveCaptured,
        null, null, 0, evt.ObjectiveId, Data(("team_id", evt.TeamId)));

    void OnWaveStarted(WaveStartedEvent evt) => Publish(evt.SessionId, BattleLuckEventIds.WaveStarted,
        null, null, 0, "wave_started", Data(("wave", evt.WaveNumber), ("enemies", evt.EnemyCount)));

    void OnWaveCleared(WaveClearedEvent evt) => Publish(evt.SessionId, BattleLuckEventIds.WaveCleared,
        null, null, 0, "wave_cleared", Data(("wave", evt.WaveNumber), ("elapsed_seconds", evt.ElapsedSeconds)));

    void OnActionPerformed(ActionPerformedEvent evt) => Publish(evt.SessionId, BattleLuckEventIds.ActionExecuted,
        evt.SteamId, null, evt.Points, evt.Action.ToString());

    void OnKill(BattleLuck.ECS.Events.KillEvent evt)
    {
        var killer = SafeSteamId(evt.Killer);
        var victim = SafeSteamId(evt.Victim);
        var assistant = evt.Assistant.HasValue ? SafeSteamId(evt.Assistant.Value) : 0;
        var sessionId = FindSession(killer, victim);
        if (sessionId == null || victim == 0) return;
        var session = BattleLuckPlugin.Session?.ActiveSessions.Values.FirstOrDefault(value =>
            value.Context.SessionId.Equals(sessionId, StringComparison.OrdinalIgnoreCase));
        var decision = _kills.Evaluate(sessionId, killer, victim, assistant, session?.Context.Teams,
            DateTimeOffset.UtcNow);
        Publish(sessionId, BattleLuckEventIds.PlayerKill, killer == 0 ? null : killer, victim, 0,
            decision.Reason, Data(("scorable", decision.Scorable)));
        if (decision.Scorable && decision.AssistantSteamId is { } acceptedAssistant)
            Publish(sessionId, BattleLuckEventIds.PlayerAssist, acceptedAssistant, victim, 0, "projectm_assist");
    }

    void OnPlayerDeath(PlayerDeathEvent evt)
    {
        var victim = SafeSteamId(evt.Died);
        var killer = SafeSteamId(evt.Killer);
        var sessionId = FindSession(killer, victim);
        if (sessionId == null || victim == 0) return;
        Publish(sessionId, BattleLuckEventIds.PlayerDeath, killer == 0 ? null : killer, victim, 0, "projectm_death");
    }

    void Publish(string sessionId, string eventId, ulong? actor, ulong? target, int points, string reason,
        IReadOnlyDictionary<string, JsonElement>? data = null)
    {
        if (!_platform.TryGetRun(sessionId, out var runId, out var modeId)) return;
        _platform.Publish(new GameEventEnvelope
        {
            EventId = eventId, EventRunId = runId, ModeId = modeId, ActorSteamId = actor,
            TargetSteamId = target, Points = points, Reason = reason,
            Data = data ?? new Dictionary<string, JsonElement>()
        });
    }

    static IReadOnlyDictionary<string, JsonElement> Data(params (string Name, object Value)[] values) =>
        values.ToDictionary(value => value.Name, value => JsonSerializer.SerializeToElement(value.Value));

    static ulong SafeSteamId(Unity.Entities.Entity entity)
    {
        try { return entity.Exists() ? entity.GetSteamId() : 0; }
        catch { return 0; }
    }

    static string? FindSession(ulong first, ulong second)
    {
        var sessions = BattleLuckPlugin.Session?.ActiveSessions.Values;
        if (sessions == null) return null;
        return sessions.FirstOrDefault(session =>
            (first != 0 && session.Context.Players.Contains(first)) ||
            (second != 0 && session.Context.Players.Contains(second)))?.Context.SessionId;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        GameEvents.OnModeStarted -= OnModeStarted;
        GameEvents.OnModeEnded -= OnModeEnded;
        GameEvents.OnRoundEnded -= OnRoundEnded;
        GameEvents.OnPlayerScored -= OnPlayerScored;
        GameEvents.OnPlayerLeft -= OnPlayerLeft;
        GameEvents.OnPlayerEliminated -= OnPlayerEliminated;
        GameEvents.OnObjectiveCaptured -= OnObjectiveCaptured;
        GameEvents.OnWaveStarted -= OnWaveStarted;
        GameEvents.OnWaveCleared -= OnWaveCleared;
        GameEvents.OnActionPerformed -= OnActionPerformed;
        if (_router != null)
        {
            _router.OnKill -= OnKill;
            _router.OnPlayerDeath -= OnPlayerDeath;
        }
    }
}
