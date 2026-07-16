using BattleLuck.Models;
using Unity.Entities;

namespace BattleLuck.Services;

public sealed class DeathPreventionService : IDisposable
{
    readonly object _gate = new();
    readonly Dictionary<ulong, DeathPreventionState> _states = new();

    public DeathPreventionService() => DeathHook.OnDeath += OnDeath;

    public OperationResult Arm(Entity player, int charges, float activeWindowSeconds, float cooldownSeconds, string sequenceId)
    {
        var steamId = player.GetSteamId();
        if (steamId == 0) return OperationResult.Fail("Player SteamID is unavailable.");
        if (charges <= 0) return OperationResult.Fail("death.prevent requires at least one charge.");

        lock (_gate)
        {
            _states[steamId] = new DeathPreventionState
            {
                InitialCharges = charges,
                RemainingCharges = charges,
                ActiveWindowSeconds = Math.Max(0f, activeWindowSeconds),
                TriggerCooldownSeconds = Math.Max(0f, cooldownSeconds),
                OnTriggeredSequenceId = sequenceId?.Trim() ?? ""
            };
        }
        return OperationResult.Ok();
    }

    public void Disarm(ulong steamId) { lock (_gate) _states.Remove(steamId); }
    public void Clear() { lock (_gate) _states.Clear(); }

    public bool TryConsume(ulong steamId, out DeathPreventionState? state)
    {
        state = null;
        var now = DateTime.UtcNow;
        lock (_gate)
        {
            if (!_states.TryGetValue(steamId, out var current)) return false;
            if (current.IsExpired(now) || current.RemainingCharges <= 0)
            {
                _states.Remove(steamId);
                return false;
            }
            if (current.LastTriggeredUtc.HasValue &&
                now < current.LastTriggeredUtc.Value.AddSeconds(current.TriggerCooldownSeconds))
                return false;

            current.RemainingCharges--;
            current.LastTriggeredUtc = now;
            state = current;
            if (current.RemainingCharges == 0) _states.Remove(steamId);
            return true;
        }
    }

    void OnDeath(Entity died, Entity _)
    {
        if (!died.Exists() || !died.IsPlayer()) return;
        var steamId = died.GetSteamId();
        if (!TryConsume(steamId, out var state)) return;

        MainThreadDispatcher.Enqueue(() =>
        {
            if (!died.Exists()) return;
            died.HealToFull();
            BattleLuckPlugin.TryNotifyPlayerBySteamId(
                steamId,
                $"Death prevented. {state!.RemainingCharges} charge(s) remain.");
            if (!string.IsNullOrWhiteSpace(state.OnTriggeredSequenceId))
                BattleLuckPlugin.LogInfo($"[DeathPrevention] Trigger sequence requested: {state.OnTriggeredSequenceId} for {steamId}.");
        });
    }

    public void Dispose()
    {
        DeathHook.OnDeath -= OnDeath;
        Clear();
    }
}
