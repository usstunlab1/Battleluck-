using System;

/// <summary>
/// Manages countdown timers for timed game modes (Trials, Bloodbath, etc).
/// Supports time bonuses and time penalties.
/// </summary>
public sealed class TimerController
{
    public int TotalSeconds { get; private set; }
    public int BonusSeconds { get; private set; }
    public DateTime? StartedAt { get; private set; }

    public bool IsRunning => StartedAt.HasValue;

    public double ElapsedSeconds => StartedAt.HasValue
        ? (DateTime.UtcNow - StartedAt.Value).TotalSeconds
        : 0;

    public double RemainingSeconds => Math.Max(0, (TotalSeconds + BonusSeconds) - ElapsedSeconds);

    public bool IsExpired => IsRunning && RemainingSeconds <= 0;

    public double ElapsedPercent => TotalSeconds > 0 ? Math.Clamp(ElapsedSeconds / (TotalSeconds + BonusSeconds), 0, 1) : 0;

    public void Start(int seconds)
    {
        TotalSeconds = seconds;
        BonusSeconds = 0;
        StartedAt = DateTime.UtcNow;
    }

    public void AddBonus(int seconds, GameModeContext? ctx = null)
    {
        BonusSeconds += seconds;
        ctx?.Broadcast?.Invoke($"⏱️ +{seconds}s bonus time! ({RemainingSeconds:F0}s remaining)");
    }

    public void AddPenalty(int seconds, GameModeContext? ctx = null)
    {
        BonusSeconds -= seconds;
        ctx?.Broadcast?.Invoke($"⏱️ -{seconds}s penalty! ({RemainingSeconds:F0}s remaining)");
    }

    public void Stop() => StartedAt = null;

    public void Reset()
    {
        TotalSeconds = 0;
        BonusSeconds = 0;
        StartedAt = null;
    }

    /// <summary>Format remaining time as MM:SS.</summary>
    public string FormatRemaining()
    {
        var r = (int)RemainingSeconds;
        return $"{r / 60:D2}:{r % 60:D2}";
    }
}
