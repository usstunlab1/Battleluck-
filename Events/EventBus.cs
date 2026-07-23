using Unity.Entities;

/// <summary>
/// Light Event Router (LER) — static Action&lt;T&gt; delegates for notifications,
/// reactions, and cross-system signals. NOT for tick order, logic flow, or state authority.
/// </summary>
public static class GameEvents
{
    internal static Action<string>? WarningSink { get; set; }
    // Zone lifecycle signals
    public static Action<ZoneEnterEvent>? OnZoneEnter;
    public static Action<ZoneExitEvent>? OnZoneExit;

    // Mode lifecycle signals
    public static Action<ModeStartedEvent>? OnModeStarted;
    public static Action<ModeEndedEvent>? OnModeEnded;

    // Round / scoring signals
    public static Action<RoundEndedEvent>? OnRoundEnded;
    public static Action<PlayerScoredEvent>? OnPlayerScored;

    // Wave signals
    public static Action<WaveStartedEvent>? OnWaveStarted;
    public static Action<WaveClearedEvent>? OnWaveCleared;
    public static Action<WaveFinalEvent>? OnWaveFinal;

    // World state signals
    public static Action<ObjectiveCapturedEvent>? OnObjectiveCaptured;
    public static Action<ZoneShrinkEvent>? OnZoneShrink;
    public static Action<RealityStateChanged>? OnRealityChanged;

    // Entity signals
    public static Action<BossSpawnedEvent>? OnBossSpawned;
    public static Action<PlatformStateChanged>? OnPlatformStateChanged;
    public static Action<CrateCollectedEvent>? OnCrateCollected;

    // Player lifecycle signals
    public static Action<PlayerEliminatedEvent>? OnPlayerEliminated;
    public static Action<PlayerLeftEvent>? OnPlayerLeft;

    // Action tracking
    public static Action<ActionPerformedEvent>? OnActionPerformed;

    // Blood Frenzy
    public static Action<BloodFrenzyActivatedEvent>? OnBloodFrenzyActivated;
    public static Action<BloodFrenzyBountyEvent>? OnBloodFrenzyBounty;

    // ELO
    public static Action<EloUpdateEvent>? OnEloUpdate;

    // Webhook
    public static Action<WebhookActionEvent>? OnWebhookAction;

    // Discord Bridge
    public static Action<DiscordCommandEvent>? OnDiscordCommand;

    // ── Raise methods (canonical publish entry points) ──────────────────

    /// <summary>Publish a committed zone entry without allowing one subscriber to break the lifecycle.</summary>
    public static void RaiseZoneEnter(ZoneEnterEvent value) => SafeInvoke(OnZoneEnter, value, nameof(OnZoneEnter));

    /// <summary>Publish a completed zone exit without allowing one subscriber to invalidate restoration.</summary>
    public static void RaiseZoneExit(ZoneExitEvent value) => SafeInvoke(OnZoneExit, value, nameof(OnZoneExit));

    public static void RaiseModeStarted(ModeStartedEvent value) => SafeInvoke(OnModeStarted, value, nameof(OnModeStarted));
    public static void RaiseModeEnded(ModeEndedEvent value) => SafeInvoke(OnModeEnded, value, nameof(OnModeEnded));
    public static void RaiseRoundEnded(RoundEndedEvent value) => SafeInvoke(OnRoundEnded, value, nameof(OnRoundEnded));
    public static void RaisePlayerScored(PlayerScoredEvent value) => SafeInvoke(OnPlayerScored, value, nameof(OnPlayerScored));
    public static void RaiseWaveStarted(WaveStartedEvent value) => SafeInvoke(OnWaveStarted, value, nameof(OnWaveStarted));
    public static void RaiseWaveCleared(WaveClearedEvent value) => SafeInvoke(OnWaveCleared, value, nameof(OnWaveCleared));
    public static void RaiseWaveFinal(WaveFinalEvent value) => SafeInvoke(OnWaveFinal, value, nameof(OnWaveFinal));
    public static void RaiseObjectiveCaptured(ObjectiveCapturedEvent value) => SafeInvoke(OnObjectiveCaptured, value, nameof(OnObjectiveCaptured));
    public static void RaiseZoneShrink(ZoneShrinkEvent value) => SafeInvoke(OnZoneShrink, value, nameof(OnZoneShrink));
    public static void RaiseRealityChanged(RealityStateChanged value) => SafeInvoke(OnRealityChanged, value, nameof(OnRealityChanged));
    public static void RaiseBossSpawned(BossSpawnedEvent value) => SafeInvoke(OnBossSpawned, value, nameof(OnBossSpawned));
    public static void RaisePlatformStateChanged(PlatformStateChanged value) => SafeInvoke(OnPlatformStateChanged, value, nameof(OnPlatformStateChanged));
    public static void RaiseCrateCollected(CrateCollectedEvent value) => SafeInvoke(OnCrateCollected, value, nameof(OnCrateCollected));
    public static void RaisePlayerEliminated(PlayerEliminatedEvent value) => SafeInvoke(OnPlayerEliminated, value, nameof(OnPlayerEliminated));
    public static void RaisePlayerLeft(PlayerLeftEvent value) => SafeInvoke(OnPlayerLeft, value, nameof(OnPlayerLeft));
    public static void RaiseActionPerformed(ActionPerformedEvent value) => SafeInvoke(OnActionPerformed, value, nameof(OnActionPerformed));
    public static void RaiseBloodFrenzyActivated(BloodFrenzyActivatedEvent value) => SafeInvoke(OnBloodFrenzyActivated, value, nameof(OnBloodFrenzyActivated));
    public static void RaiseBloodFrenzyBounty(BloodFrenzyBountyEvent value) => SafeInvoke(OnBloodFrenzyBounty, value, nameof(OnBloodFrenzyBounty));
    public static void RaiseEloUpdate(EloUpdateEvent value) => SafeInvoke(OnEloUpdate, value, nameof(OnEloUpdate));
    public static void RaiseWebhookAction(WebhookActionEvent value) => SafeInvoke(OnWebhookAction, value, nameof(OnWebhookAction));
    public static void RaiseDiscordCommand(DiscordCommandEvent value) => SafeInvoke(OnDiscordCommand, value, nameof(OnDiscordCommand));

    static void SafeInvoke<T>(Action<T>? handlers, T value, string eventName)
    {
        if (handlers == null)
            return;

        foreach (Action<T> handler in handlers.GetInvocationList())
        {
            try { handler(value); }
            catch (Exception ex) { WarningSink?.Invoke($"[GameEvents] {eventName} subscriber failed: {ex}"); }
        }
    }

    /// <summary>
    /// Unhook all subscribers. Call on plugin unload / session teardown.
    /// </summary>
    public static void Shutdown()
    {
        OnZoneEnter = null;
        OnZoneExit = null;
        OnModeStarted = null;
        OnModeEnded = null;
        OnRoundEnded = null;
        OnPlayerScored = null;
        OnWaveStarted = null;
        OnWaveCleared = null;
        OnWaveFinal = null;
        OnObjectiveCaptured = null;
        OnZoneShrink = null;
        OnRealityChanged = null;
        OnBossSpawned = null;
        OnPlatformStateChanged = null;
        OnCrateCollected = null;
        OnPlayerEliminated = null;
        OnPlayerLeft = null;
        OnActionPerformed = null;
        OnBloodFrenzyActivated = null;
        OnBloodFrenzyBounty = null;
        OnEloUpdate = null;
        OnWebhookAction = null;
        OnDiscordCommand = null;
        WarningSink = null;
    }
}
