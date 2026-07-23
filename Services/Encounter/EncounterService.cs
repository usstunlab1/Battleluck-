using BattleLuck.Models;
using BattleLuck.Services.Npc;
using BattleLuck.Services.Spawn;
using Unity.Entities;
using Unity.Mathematics;

namespace BattleLuck.Services.Encounter;

/// <summary>
/// Manages dynamic encounter spawning, wave sequences, and cleanup through the unified NPC pipeline.
/// All encounter.* actions dispatch through this service.
/// </summary>
public sealed class EncounterService
{
    readonly NpcControlService _npcService;
    readonly object _lock = new();

    // Active encounters: encounterId -> session data
    readonly Dictionary<string, EncounterSession> _activeEncounters = new(StringComparer.OrdinalIgnoreCase);
    // Active wave timers: encounterId -> wave state
    readonly Dictionary<string, WaveState> _activeWaves = new(StringComparer.OrdinalIgnoreCase);

    public EncounterService(NpcControlService npcService)
    {
        _npcService = npcService;
    }

    public sealed record EncounterSession(
        string EncounterId,
        string SessionId,
        float3 Center,
        string Faction,
        DateTime CreatedAt,
        List<string> TrackedNpcIds);

    public sealed record WaveState(
        string WaveSetId,
        int CurrentWave,
        int TotalWaves,
        float IntervalSeconds,
        DateTime LastWaveTime,
        bool Active);

    /// <summary>
    /// Spawn a tracked encounter group through the unified NPC pipeline.
    /// </summary>
    public OperationResult Spawn(string encounterId, string sessionId, float3 center, string faction,
        string prefabs, int count, string formation, float despawnSeconds)
    {
        lock (_lock)
        {
            if (_activeEncounters.ContainsKey(encounterId))
                return OperationResult.Fail($"Encounter '{encounterId}' is already active.");

            var trackedIds = new List<string>();
            var prefabList = prefabs.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            for (int i = 0; i < count && prefabList.Length > 0; i++)
            {
                var prefabName = prefabList[i % prefabList.Length];
                var npcId = $"{encounterId}_unit_{i}";
                var offset = new float3(i * 2f, 0f, i % 2 == 0 ? 0f : 2f);

                var prefab = PrefabHelper.GetPrefabGuidDeep(prefabName);
                if (!prefab.HasValue) continue;

                new SpawnController().SpawnWithCallback(prefab.Value, center + offset, 0f, spawned =>
                {
                    var result = _npcService.RegisterNpc(sessionId, npcId, prefabName, prefab.Value, spawned, center, 35f);
                    if (result.Success)
                        trackedIds.Add(npcId);
                });
            }

            var session = new EncounterSession(encounterId, sessionId, center, faction, DateTime.UtcNow, trackedIds);
            _activeEncounters[encounterId] = session;

            if (despawnSeconds > 0)
            {
                _ = ScheduleDespawn(encounterId, despawnSeconds);
            }

            return OperationResult.Ok();
        }
    }

    /// <summary>
    /// Spawn a named encounter unit with custom display name and behavior.
    /// </summary>
    public OperationResult SpawnNamedUnit(string prefab, string displayName, string levelPolicy, string behavior, string rewardTable)
    {
        var prefabGuid = PrefabHelper.GetPrefabGuidDeep(prefab);
        if (!prefabGuid.HasValue)
            return OperationResult.Fail($"Unknown prefab '{prefab}'.");

        var npcId = $"named_{prefab}_{DateTime.UtcNow.Ticks % 10000}";
        new SpawnController().SpawnWithCallback(prefabGuid.Value, float3.zero, 0f, spawned =>
        {
            var result = _npcService.RegisterNpc("_encounter_", npcId, prefab, prefabGuid.Value, spawned, float3.zero, 35f);
            if (result.Success && !string.IsNullOrWhiteSpace(displayName))
            {
                _npcService.Rename(npcId, displayName);
            }
        });

        return OperationResult.Ok();
    }

    /// <summary>
    /// Start a timed encounter wave sequence.
    /// </summary>
    public OperationResult StartWave(string waveSetId, int waveNumber, float intervalSeconds, bool scaleByPlayers)
    {
        lock (_lock)
        {
            if (_activeWaves.ContainsKey(waveSetId))
                return OperationResult.Fail($"Wave set '{waveSetId}' is already active.");

            _activeWaves[waveSetId] = new WaveState(waveSetId, waveNumber, waveNumber, intervalSeconds, DateTime.UtcNow, true);
            return OperationResult.Ok();
        }
    }

    /// <summary>
    /// Stop the current encounter wave.
    /// </summary>
    public OperationResult StopWave(string waveSetId)
    {
        lock (_lock)
        {
            _activeWaves.Remove(waveSetId);
            return OperationResult.Ok();
        }
    }

    /// <summary>
    /// Remove all entities tracked under the current encounter/session ID.
    /// </summary>
    public OperationResult Cleanup(string encounterId = "")
    {
        lock (_lock)
        {
            if (!string.IsNullOrWhiteSpace(encounterId))
            {
                if (_activeEncounters.TryGetValue(encounterId, out var session))
                {
                    foreach (var npcId in session.TrackedNpcIds)
                        _npcService.Despawn(npcId);
                    _activeEncounters.Remove(encounterId);
                }
                return OperationResult.Ok();
            }

            // Cleanup all encounters
            foreach (var kvp in _activeEncounters)
            {
                foreach (var npcId in kvp.Value.TrackedNpcIds)
                    _npcService.Despawn(npcId);
            }
            _activeEncounters.Clear();
            _activeWaves.Clear();
            return OperationResult.Ok();
        }
    }

    async System.Threading.Tasks.Task ScheduleDespawn(string encounterId, float seconds)
    {
        await System.Threading.Tasks.Task.Delay((int)(seconds * 1000));
        Cleanup(encounterId);
    }
}