using System;
using System.Collections.Generic;
using System.Threading.Tasks;

/// <summary>
/// Manages PvE wave spawning and tracking for Gauntlet and similar modes.
/// Waves are defined as lists of prefab GUIDs with counts.
/// </summary>
public sealed class WaveController
{
    public int CurrentWave { get; private set; }
    public int TotalWaves { get; set; } = 5;
    public bool IsWaveActive { get; private set; }
    public int RemainingEnemies { get; private set; }

    private readonly List<WaveDefinition> _waves = new();

    public bool IsComplete => CurrentWave > TotalWaves && !IsWaveActive;

    public void Configure(List<WaveDefinition> waves)
    {
        _waves.Clear();
        _waves.AddRange(waves);
        TotalWaves = _waves.Count;
        CurrentWave = 0;
        IsWaveActive = false;
        RemainingEnemies = 0;
    }

    /// <summary>Start the next wave. Returns the wave definition or null if all waves done.</summary>
    public WaveDefinition? StartNextWave(GameModeContext ctx)
    {
        CurrentWave++;
        if (CurrentWave > TotalWaves || CurrentWave > _waves.Count)
        {
            IsWaveActive = false;
            return null;
        }

        var wave = _waves[CurrentWave - 1];
        RemainingEnemies = wave.EnemyCount;
        IsWaveActive = true;

        ctx.Broadcast?.Invoke($"⚔️ Wave {CurrentWave}/{TotalWaves} — {wave.EnemyCount} enemies incoming!");
        GameEvents.RaiseWaveStarted(new WaveStartedEvent
        {
            SessionId = ctx.SessionId,
            WaveNumber = CurrentWave,
            EnemyCount = wave.EnemyCount
        });

        return wave;
    }

    /// <summary>Record an enemy kill. Returns true if the wave is now cleared.</summary>
    public bool RecordKill(GameModeContext ctx)
    {
        if (!IsWaveActive) return false;

        RemainingEnemies = Math.Max(0, RemainingEnemies - 1);
        if (RemainingEnemies <= 0)
        {
            IsWaveActive = false;
            ctx.Broadcast?.Invoke($"✅ Wave {CurrentWave} cleared!");
            GameEvents.RaiseWaveCleared(new WaveClearedEvent
            {
                SessionId = ctx.SessionId,
                WaveNumber = CurrentWave,
                ElapsedSeconds = ctx.ElapsedSeconds
            });
            return true;
        }
        return false;
    }

    public void Reset()
    {
        CurrentWave = 0;
        IsWaveActive = false;
        RemainingEnemies = 0;
    }
}

public sealed class WaveDefinition
{
    public int WaveNumber { get; init; }
    public int EnemyCount { get; init; }
    public List<string> Prefabs { get; init; } = new();
    public int DelaySeconds { get; init; }
    public string? KitId { get; init; }
}
