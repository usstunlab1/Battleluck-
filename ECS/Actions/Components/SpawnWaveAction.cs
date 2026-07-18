namespace BattleLuck.ECS.Actions.Components;

/// <summary>
/// ECS action component for start waves of enemies.
/// Replaces wave spawning logic in game modes.
/// </summary>
public struct StartWaveAction
{
    public int WaveId;
    public FlowActionExecutor Executor;
    public int Count;
    public Entity SessionEntity;
}
