using System;

/// <summary>
/// Manages multi-round state for game modes that use best-of-N or fixed round counts.
/// </summary>
public sealed class RoundManager
{
    public int CurrentRound { get; private set; } = 1;
    public int TotalRounds { get; set; } = 1;
    public int[] RoundWinners { get; private set; } = Array.Empty<int>();

    private int _roundsCompleted;

    /// <summary>Whether all rounds have been played.</summary>
    public bool IsComplete => _roundsCompleted >= TotalRounds;

    /// <summary>Initialize for a specific number of rounds.</summary>
    public void Initialize(int totalRounds)
    {
        TotalRounds = Math.Max(1, totalRounds);
        RoundWinners = new int[TotalRounds];
        CurrentRound = 1;
        _roundsCompleted = 0;
    }

    /// <summary>Record the winner of the current round and advance.</summary>
    public bool CompleteRound(int winnerId)
    {
        if (_roundsCompleted < RoundWinners.Length)
        {
            RoundWinners[_roundsCompleted] = winnerId;
        }
        _roundsCompleted++;
        CurrentRound = _roundsCompleted + 1;
        return !IsComplete;
    }

    /// <summary>Count how many rounds a given ID has won.</summary>
    public int GetWinCount(int id)
    {
        int count = 0;
        for (int i = 0; i < _roundsCompleted && i < RoundWinners.Length; i++)
        {
            if (RoundWinners[i] == id) count++;
        }
        return count;
    }

    /// <summary>Check if a team/player has won majority (best-of-N).</summary>
    public bool HasMajority(int id) => GetWinCount(id) > TotalRounds / 2;

    /// <summary>Reset all round state.</summary>
    public void Reset()
    {
        CurrentRound = 1;
        _roundsCompleted = 0;
        RoundWinners = new int[TotalRounds];
    }
}
