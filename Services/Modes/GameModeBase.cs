namespace BattleLuck.Services.Modes;

/// <summary>
/// Base class for all game modes (Bloodbath, Colosseum, Siege, Trials, AI Event Test).
/// Subclasses implement mode-specific scoring, objective completion, and state transitions.
/// </summary>
public abstract class GameModeBase
{
    public virtual string DisplayName { get; protected set; } = "Unknown Mode";

    public virtual void OnStart(GameModeContext context) { }

    public virtual void OnTick(GameModeContext context, float deltaSeconds) { }

    public virtual void OnEnd(GameModeContext context) { }

    public virtual void OnReset(GameModeContext context) { }

    public virtual void OnPlayerJoin(GameModeContext context, ulong steamId) { }

    public virtual void OnPlayerLeave(GameModeContext context, ulong steamId) { }

    public virtual void OnPlayerDowned(GameModeContext context, ulong victimId, ulong killerId) { }

    public virtual bool RecordEnemyKill(GameModeContext context, ulong killerId) => false;
}
