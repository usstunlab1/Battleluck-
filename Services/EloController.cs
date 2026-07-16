using System;
using System.Collections.Generic;

/// <summary>
/// Tracks and calculates Elo ratings for ranked modes (Colosseum).
/// Uses standard Elo formula with configurable K-factor.
/// </summary>
public sealed class EloController
{
    private readonly Dictionary<ulong, int> _ratings = new();
    public int DefaultRating { get; set; } = 1200;
    public int KFactor { get; set; } = 32;

    public int GetRating(ulong steamId) =>
        _ratings.TryGetValue(steamId, out var r) ? r : DefaultRating;

    public IReadOnlyDictionary<ulong, int> GetAllRatings() => _ratings;

    /// <summary>Update Elo ratings after a match. Returns (winnerNew, loserNew).</summary>
    public (int WinnerElo, int LoserElo) RecordMatch(ulong winnerSteamId, ulong loserSteamId, GameModeContext? ctx = null)
    {
        int winnerOld = GetRating(winnerSteamId);
        int loserOld = GetRating(loserSteamId);

        double expectedWinner = 1.0 / (1.0 + Math.Pow(10, (loserOld - winnerOld) / 400.0));
        double expectedLoser = 1.0 - expectedWinner;

        int winnerNew = (int)Math.Round(winnerOld + KFactor * (1.0 - expectedWinner));
        int loserNew = (int)Math.Round(loserOld + KFactor * (0.0 - expectedLoser));
        loserNew = Math.Max(100, loserNew); // Floor at 100

        _ratings[winnerSteamId] = winnerNew;
        _ratings[loserSteamId] = loserNew;

        if (ctx != null)
        {
            GameEvents.OnEloUpdate?.Invoke(new EloUpdateEvent
            {
                SessionId = ctx.SessionId,
                SteamId = winnerSteamId,
                OldElo = winnerOld,
                NewElo = winnerNew
            });
            GameEvents.OnEloUpdate?.Invoke(new EloUpdateEvent
            {
                SessionId = ctx.SessionId,
                SteamId = loserSteamId,
                OldElo = loserOld,
                NewElo = loserNew
            });
        }

        return (winnerNew, loserNew);
    }

    /// <summary>Get the leaderboard sorted by Elo descending.</summary>
    public List<(ulong SteamId, int Elo)> GetLeaderboard()
    {
        var list = new List<(ulong SteamId, int Elo)>();
        foreach (var kvp in _ratings)
            list.Add((kvp.Key, kvp.Value));
        list.Sort((a, b) => b.Elo.CompareTo(a.Elo));
        return list;
    }

    public void SetRating(ulong steamId, int elo) => _ratings[steamId] = elo;

    public void Reset() => _ratings.Clear();
}
