using Unity.Entities;

/// <summary>
/// Light Event Router (LER) — static Action&lt;T&gt; delegates for notifications,
/// reactions, and cross-system signals. NOT for tick order, logic flow, or state authority.
/// </summary>
public static class GameEvents
{
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
    }
}
