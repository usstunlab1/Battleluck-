namespace BattleLuck.Services.Runtime;

public sealed record KillAttributionDecision(bool Scorable, string Reason, ulong? AssistantSteamId);

/// <summary>Bounded event-only attribution, duplicate, team-kill, and farm filtering.</summary>
public sealed class KillAttributionService
{
    readonly object _gate = new();
    readonly Dictionary<string, DateTimeOffset> _recentDeaths = new(StringComparer.Ordinal);
    readonly Dictionary<string, Queue<DateTimeOffset>> _pairKills = new(StringComparer.Ordinal);
    static readonly TimeSpan ContributionWindow = TimeSpan.FromSeconds(15);
    static readonly TimeSpan DuplicateWindow = TimeSpan.FromSeconds(2);
    static readonly TimeSpan FarmWindow = TimeSpan.FromMinutes(1);
    const int FarmLimit = 3;

    public KillAttributionDecision Evaluate(string runId, ulong killer, ulong victim, ulong assistant,
        IReadOnlyDictionary<ulong, int>? teams, DateTimeOffset occurredUtc)
    {
        lock (_gate)
        {
            Prune(occurredUtc);
            if (killer == 0) return new(false, "environment_death", null);
            if (killer == victim) return new(false, "self_kill", null);
            if (teams != null && teams.TryGetValue(killer, out var killerTeam) && killerTeam > 0 &&
                teams.TryGetValue(victim, out var victimTeam) && killerTeam == victimTeam)
                return new(false, "team_kill", null);

            var deathKey = $"{runId}:{victim}";
            if (_recentDeaths.TryGetValue(deathKey, out var lastDeath) && occurredUtc - lastDeath <= DuplicateWindow)
                return new(false, "duplicate_kill", null);
            _recentDeaths[deathKey] = occurredUtc;

            var pairKey = $"{runId}:{killer}:{victim}";
            if (!_pairKills.TryGetValue(pairKey, out var history)) _pairKills[pairKey] = history = new();
            while (history.Count > 0 && occurredUtc - history.Peek() > FarmWindow) history.Dequeue();
            if (history.Count >= FarmLimit) return new(false, "farm_kill", null);
            history.Enqueue(occurredUtc);

            ulong? acceptedAssist = assistant != 0 && assistant != killer && assistant != victim ? assistant : null;
            return new(true, "projectm_kill", acceptedAssist);
        }
    }

    void Prune(DateTimeOffset now)
    {
        foreach (var key in _recentDeaths.Where(pair => now - pair.Value > ContributionWindow)
                     .Select(pair => pair.Key).ToArray()) _recentDeaths.Remove(key);
        foreach (var key in _pairKills.Where(pair => pair.Value.Count == 0 || now - pair.Value.Last() > FarmWindow)
                     .Select(pair => pair.Key).ToArray()) _pairKills.Remove(key);
    }
}
