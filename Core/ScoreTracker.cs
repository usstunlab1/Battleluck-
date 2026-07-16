using System;
using System.Collections.Generic;

/// <summary>
/// Tracks per-player and per-team scores for a game session.
/// </summary>
public sealed class ScoreTracker
{
    private readonly Dictionary<ulong, int> _playerScores = new();
    private readonly Dictionary<int, int> _teamScores = new();

    // ── Player scores ─────────────────────────────────────────────────

    public int GetPlayerScore(ulong steamId) =>
        _playerScores.TryGetValue(steamId, out var s) ? s : 0;

    public void AddPlayerScore(ulong steamId, int points)
    {
        _playerScores.TryGetValue(steamId, out var current);
        _playerScores[steamId] = current + points;
    }

    public void SetPlayerScore(ulong steamId, int score) =>
        _playerScores[steamId] = score;

    public IReadOnlyDictionary<ulong, int> GetAllPlayerScores() => _playerScores;

    /// <summary>Returns steam IDs sorted by score descending.</summary>
    public List<ulong> GetLeaderboard()
    {
        var list = new List<ulong>(_playerScores.Keys);
        list.Sort((a, b) => _playerScores[b].CompareTo(_playerScores[a]));
        return list;
    }

    // ── Team scores ───────────────────────────────────────────────────

    public int GetTeamScore(int teamId) =>
        _teamScores.TryGetValue(teamId, out var s) ? s : 0;

    public void AddTeamScore(int teamId, int points)
    {
        _teamScores.TryGetValue(teamId, out var current);
        _teamScores[teamId] = current + points;
    }

    public void SetTeamScore(int teamId, int score) =>
        _teamScores[teamId] = score;

    public IReadOnlyDictionary<int, int> GetAllTeamScores() => _teamScores;

    /// <summary>Returns the team ID with the highest score (or -1 if no teams).</summary>
    public int GetLeadingTeam()
    {
        int bestTeam = -1;
        int bestScore = int.MinValue;
        foreach (var kvp in _teamScores)
        {
            if (kvp.Value > bestScore)
            {
                bestScore = kvp.Value;
                bestTeam = kvp.Key;
            }
        }
        return bestTeam;
    }

    // ── Reset ─────────────────────────────────────────────────────────

    public void Reset()
    {
        _playerScores.Clear();
        _teamScores.Clear();
    }

    public void ResetPlayers() => _playerScores.Clear();
    public void ResetTeams() => _teamScores.Clear();
}
