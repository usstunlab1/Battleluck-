using System.Text.Json;
using System.Text.Json.Serialization;

namespace BattleLuck.Models;

public static class BattleLuckEventIds
{
    public const string EventStarted = "event.started";
    public const string EventEnded = "event.ended";
    public const string EventAborted = "event.aborted";
    public const string RoundEnded = "round.ended";
    public const string PlayerJoined = "player.joined";
    public const string PlayerLeft = "player.left";
    public const string PlayerEliminated = "player.eliminated";
    public const string PlayerRestored = "player.restored";
    public const string PlayerKill = "player.kill";
    public const string PlayerAssist = "player.assist";
    public const string PlayerDeath = "player.death";
    public const string ObjectiveCaptured = "objective.captured";
    public const string WaveStarted = "wave.started";
    public const string WaveCleared = "wave.cleared";
    public const string ScoreChanged = "score.changed";
    public const string WinnerDeclared = "winner.declared";
    public const string ConfigLoaded = "config.loaded";
    public const string ConfigRejected = "config.rejected";
    public const string ActionStaged = "action.staged";
    public const string ActionApproved = "action.approved";
    public const string ActionExecuted = "action.executed";
    public const string ActionFailed = "action.failed";
}

/// <summary>Stable, append-only fact emitted by the server event platform.</summary>
public sealed record GameEventEnvelope
{
    [JsonPropertyName("schema")]
    public int Schema { get; init; } = 1;

    [JsonPropertyName("event_id")]
    public string EventId { get; init; } = "";

    [JsonPropertyName("event_run_id")]
    public string EventRunId { get; init; } = "";

    [JsonPropertyName("mode_id")]
    public string ModeId { get; init; } = "";

    [JsonPropertyName("sequence")]
    public long Sequence { get; init; }

    [JsonPropertyName("occurred_utc")]
    public DateTimeOffset OccurredUtc { get; init; } = DateTimeOffset.UtcNow;

    [JsonPropertyName("actor_steam_id")]
    public ulong? ActorSteamId { get; init; }

    [JsonPropertyName("target_steam_id")]
    public ulong? TargetSteamId { get; init; }

    [JsonPropertyName("team_id")]
    public int? TeamId { get; init; }

    [JsonPropertyName("points")]
    public int Points { get; init; }

    [JsonPropertyName("reason")]
    public string Reason { get; init; } = "";

    [JsonPropertyName("data")]
    public IReadOnlyDictionary<string, JsonElement> Data { get; init; }
        = new Dictionary<string, JsonElement>();
}

public sealed record EventStanding
{
    [JsonPropertyName("steam_id")]
    public ulong SteamId { get; init; }

    [JsonPropertyName("team_id")]
    public int TeamId { get; init; } = -1;

    [JsonPropertyName("score")]
    public int Score { get; init; }

    [JsonPropertyName("objectives")]
    public int Objectives { get; init; }

    [JsonPropertyName("kills")]
    public int Kills { get; init; }

    [JsonPropertyName("assists")]
    public int Assists { get; init; }

    [JsonPropertyName("deaths")]
    public int Deaths { get; init; }

    [JsonPropertyName("longest_streak")]
    public int LongestStreak { get; init; }

    [JsonPropertyName("first_score_utc")]
    public DateTimeOffset? FirstScoreUtc { get; init; }
}

public sealed record EventWinner
{
    [JsonPropertyName("type")]
    public string Type { get; init; } = "player";

    [JsonPropertyName("id")]
    public string Id { get; init; } = "";

    [JsonPropertyName("score")]
    public int Score { get; init; }
}

public sealed record EventAward
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = "";

    [JsonPropertyName("steam_id")]
    public ulong SteamId { get; init; }

    [JsonPropertyName("value")]
    public int Value { get; init; }
}

public sealed record EventResult
{
    [JsonPropertyName("schema")]
    public int Schema { get; init; } = 1;

    [JsonPropertyName("event_run_id")]
    public string EventRunId { get; init; } = "";

    [JsonPropertyName("mode_id")]
    public string ModeId { get; init; } = "";

    [JsonPropertyName("started_utc")]
    public DateTimeOffset StartedUtc { get; init; }

    [JsonPropertyName("ended_utc")]
    public DateTimeOffset EndedUtc { get; init; }

    [JsonPropertyName("end_reason")]
    public string EndReason { get; init; } = "completed";

    [JsonPropertyName("winner")]
    public EventWinner? Winner { get; init; }

    [JsonPropertyName("standings")]
    public IReadOnlyList<EventStanding> Standings { get; init; } = Array.Empty<EventStanding>();

    [JsonPropertyName("awards")]
    public IReadOnlyList<EventAward> Awards { get; init; } = Array.Empty<EventAward>();

    [JsonPropertyName("counters")]
    public IReadOnlyDictionary<string, int> Counters { get; init; }
        = new Dictionary<string, int>();
}

public sealed record PlayerProjection(
    string Id,
    string DisplayName,
    bool Online,
    bool HasCharacter,
    bool EventParticipant,
    int TeamId);

public sealed record CanonicalActionDefinition(
    string Name,
    IReadOnlyDictionary<string, string> Parameters,
    IReadOnlyList<string> Capabilities,
    bool Reversible,
    bool RequiresConfirmation);

public sealed record ProposedAction(
    string Id,
    string Action,
    IReadOnlyDictionary<string, JsonElement> Parameters,
    string Expected);

public sealed record ActionProposal(
    int Schema,
    string RequestId,
    string Goal,
    IReadOnlyList<ProposedAction> Steps,
    IReadOnlyList<string> Cleanup,
    bool RequiresLiveExecution);
