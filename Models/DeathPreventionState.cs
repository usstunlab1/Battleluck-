namespace BattleLuck.Models;

public sealed class DeathPreventionState
{
    public int InitialCharges { get; init; }
    public int RemainingCharges { get; set; }
    public float ActiveWindowSeconds { get; init; }
    public float TriggerCooldownSeconds { get; init; }
    public DateTime ArmedAtUtc { get; init; } = DateTime.UtcNow;
    public DateTime? LastTriggeredUtc { get; set; }
    public string OnTriggeredSequenceId { get; init; } = "";
    public bool IsExpired(DateTime utcNow) => ActiveWindowSeconds > 0 && utcNow >= ArmedAtUtc.AddSeconds(ActiveWindowSeconds);
}
