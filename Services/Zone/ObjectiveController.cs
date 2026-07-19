using System;
using System.Collections.Generic;

/// <summary>
/// Manages capture-point objectives for team-based modes (Siege, CTF).
/// </summary>
public sealed class ObjectiveController
{
    private readonly Dictionary<string, ObjectiveState> _objectives = new(StringComparer.OrdinalIgnoreCase);

    public int TotalObjectives => _objectives.Count;
    public IReadOnlyDictionary<string, ObjectiveState> Objectives => _objectives;

    public void AddObjective(string id, float x, float z, float captureRadius = 10f, int captureTimeSeconds = 15)
    {
        _objectives[id] = new ObjectiveState
        {
            Id = id,
            X = x,
            Z = z,
            CaptureRadius = captureRadius,
            CaptureTimeSeconds = captureTimeSeconds
        };
    }

    /// <summary>Attempt to capture an objective for a team. Returns true if newly captured.</summary>
    public bool TryCaptureObjective(string objectiveId, int teamId, GameModeContext ctx)
    {
        if (!_objectives.TryGetValue(objectiveId, out var obj)) return false;
        if (obj.CapturedByTeam == teamId) return false;

        obj.CapturedByTeam = teamId;
        obj.CapturedAtUtc = DateTime.UtcNow;

        ctx.Broadcast?.Invoke($"🏴 Objective '{objectiveId}' captured by Team {teamId}!");
        GameEvents.OnObjectiveCaptured?.Invoke(new ObjectiveCapturedEvent
        {
            SessionId = ctx.SessionId,
            ObjectiveId = objectiveId,
            TeamId = teamId
        });

        return true;
    }

    /// <summary>Count how many objectives a team controls.</summary>
    public int GetTeamObjectiveCount(int teamId)
    {
        int count = 0;
        foreach (var obj in _objectives.Values)
        {
            if (obj.CapturedByTeam == teamId) count++;
        }
        return count;
    }

    /// <summary>Check if a team controls all objectives.</summary>
    public bool TeamControlsAll(int teamId) => GetTeamObjectiveCount(teamId) == TotalObjectives;

    public void Reset()
    {
        foreach (var obj in _objectives.Values)
        {
            obj.CapturedByTeam = -1;
            obj.CapturedAtUtc = null;
        }
    }

    public void Clear() => _objectives.Clear();
}

public sealed class ObjectiveState
{
    public string Id { get; init; } = "";
    public float X { get; init; }
    public float Z { get; init; }
    public float CaptureRadius { get; init; }
    public int CaptureTimeSeconds { get; init; }
    public int CapturedByTeam { get; set; } = -1;
    public DateTime? CapturedAtUtc { get; set; }
}
