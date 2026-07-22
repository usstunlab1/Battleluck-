using System.Text;
using System.Text.Json;
using System.Diagnostics;
using BattleLuck.Models;

namespace BattleLuck.Services.Runtime;

public sealed class EventLedger : IDisposable
{
    static readonly UTF8Encoding Utf8NoBom = new(false);
    static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    readonly object _gate = new();
    readonly string _ledgerRoot;
    readonly Dictionary<string, StreamWriter> _writers = new(StringComparer.OrdinalIgnoreCase);

    public EventLedger(string ledgerRoot) => _ledgerRoot = ledgerRoot;

    public string GetPath(string runId) => Path.Combine(_ledgerRoot, SafeId(runId) + ".jsonl");

    public void Append(GameEventEnvelope envelope)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        if (string.IsNullOrWhiteSpace(envelope.EventRunId))
            throw new ArgumentException("An event run id is required.", nameof(envelope));

        lock (_gate)
        {
            Directory.CreateDirectory(_ledgerRoot);
            if (!_writers.TryGetValue(envelope.EventRunId, out var writer))
            {
                var stream = new FileStream(GetPath(envelope.EventRunId), FileMode.Append, FileAccess.Write,
                    FileShare.ReadWrite, bufferSize: 16384, FileOptions.SequentialScan);
                writer = new StreamWriter(stream, Utf8NoBom, 16384) { AutoFlush = true };
                _writers[envelope.EventRunId] = writer;
            }
            writer.WriteLine(JsonSerializer.Serialize(envelope, JsonOptions));
        }
    }

    public void Close(string runId)
    {
        lock (_gate)
            if (_writers.Remove(runId, out var writer)) writer.Dispose();
    }

    public void Dispose()
    {
        lock (_gate)
        {
            foreach (var writer in _writers.Values) writer.Dispose();
            _writers.Clear();
        }
    }

    public IReadOnlyList<GameEventEnvelope> ReadRecoverable(string runId)
    {
        var path = GetPath(runId);
        if (!File.Exists(path))
            return Array.Empty<GameEventEnvelope>();

        var lines = File.ReadAllLines(path, Utf8NoBom);
        var lastContentIndex = Array.FindLastIndex(lines, line => !string.IsNullOrWhiteSpace(line));
        var events = new List<GameEventEnvelope>(Math.Max(0, lastContentIndex + 1));
        for (var i = 0; i <= lastContentIndex; i++)
        {
            if (string.IsNullOrWhiteSpace(lines[i]))
                continue;
            try
            {
                var envelope = JsonSerializer.Deserialize<GameEventEnvelope>(lines[i], JsonOptions)
                    ?? throw new JsonException("Event was null.");
                events.Add(envelope);
            }
            catch (JsonException) when (i == lastContentIndex)
            {
                // A process may stop between writing the final bytes and newline.
                // Only the final record is recoverable; earlier corruption is fatal.
            }
            catch (JsonException ex)
            {
                throw new InvalidDataException($"Ledger '{path}' is corrupt at line {i + 1}.", ex);
            }
        }
        return events;
    }

    static string SafeId(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return new string(value.Select(ch => invalid.Contains(ch) || ch is '/' or '\\' ? '_' : ch).ToArray());
    }
}

public sealed class ScoreService
{
    sealed class MutableStanding
    {
        public ulong SteamId;
        public int TeamId = -1;
        public int Score;
        public int Objectives;
        public int Kills;
        public int Assists;
        public int Deaths;
        public int CurrentStreak;
        public int LongestStreak;
        public DateTimeOffset? FirstScoreUtc;
    }

    readonly object _gate = new();
    readonly Dictionary<string, Dictionary<ulong, MutableStanding>> _runs = new(StringComparer.OrdinalIgnoreCase);

    public void Apply(GameEventEnvelope evt)
    {
        if (string.IsNullOrWhiteSpace(evt.EventRunId)) return;
        lock (_gate)
        {
            var run = GetRun(evt.EventRunId);
            var scorable = !evt.Data.TryGetValue("scorable", out var scorableValue) ||
                           scorableValue.ValueKind != JsonValueKind.False;
            if (evt.ActorSteamId is { } actor && actor != 0)
            {
                var standing = GetStanding(run, actor);
                if (evt.TeamId.HasValue) standing.TeamId = evt.TeamId.Value;
                if (evt.Points != 0)
                {
                    standing.Score += evt.Points;
                    standing.FirstScoreUtc ??= evt.OccurredUtc;
                }
                if (!scorable) return;
                switch (evt.EventId)
                {
                    case BattleLuckEventIds.PlayerKill:
                        standing.Kills++;
                        standing.CurrentStreak++;
                        standing.LongestStreak = Math.Max(standing.LongestStreak, standing.CurrentStreak);
                        break;
                    case BattleLuckEventIds.PlayerAssist:
                        standing.Assists++;
                        break;
                    case BattleLuckEventIds.ObjectiveCaptured:
                        standing.Objectives++;
                        break;
                }
            }

            if (evt.EventId == BattleLuckEventIds.PlayerDeath &&
                evt.TargetSteamId is { } target && target != 0)
            {
                var victim = GetStanding(run, target);
                victim.Deaths++;
                victim.CurrentStreak = 0;
            }
        }
    }

    public IReadOnlyList<EventStanding> Snapshot(string runId)
    {
        lock (_gate)
        {
            if (!_runs.TryGetValue(runId, out var run)) return Array.Empty<EventStanding>();
            return run.Values.Select(ToImmutable).OrderByDescending(x => x.Score)
                .ThenByDescending(x => x.Objectives).ThenByDescending(x => x.Kills)
                .ThenByDescending(x => x.Assists).ThenBy(x => x.Deaths)
                .ThenBy(x => x.FirstScoreUtc ?? DateTimeOffset.MaxValue).ThenBy(x => x.SteamId).ToArray();
        }
    }

    public void Remove(string runId) { lock (_gate) _runs.Remove(runId); }

    Dictionary<ulong, MutableStanding> GetRun(string runId)
    {
        if (!_runs.TryGetValue(runId, out var run)) _runs[runId] = run = new();
        return run;
    }

    static MutableStanding GetStanding(IDictionary<ulong, MutableStanding> run, ulong steamId)
    {
        if (!run.TryGetValue(steamId, out var value)) run[steamId] = value = new() { SteamId = steamId };
        return value;
    }

    static EventStanding ToImmutable(MutableStanding value) => new()
    {
        SteamId = value.SteamId, TeamId = value.TeamId, Score = value.Score,
        Objectives = value.Objectives, Kills = value.Kills, Assists = value.Assists,
        Deaths = value.Deaths, LongestStreak = value.LongestStreak, FirstScoreUtc = value.FirstScoreUtc
    };
}

public sealed class ResultService
{
    static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    readonly string _resultRoot;
    readonly int _retention;

    public ResultService(string resultRoot, int retention = 20)
    {
        _resultRoot = resultRoot;
        _retention = Math.Clamp(retention, 1, 1000);
    }

    public EventResult Finalize(string runId, string modeId, DateTimeOffset started, DateTimeOffset ended,
        string reason, IReadOnlyList<EventStanding> standings)
    {
        var winnerStanding = standings.FirstOrDefault();
        var result = new EventResult
        {
            EventRunId = runId, ModeId = modeId, StartedUtc = started, EndedUtc = ended, EndReason = reason,
            Winner = winnerStanding == null ? null : new EventWinner
            {
                Type = winnerStanding.TeamId >= 0 ? "team" : "player",
                Id = winnerStanding.TeamId >= 0 ? winnerStanding.TeamId.ToString() : winnerStanding.SteamId.ToString(),
                Score = winnerStanding.Score
            },
            Standings = standings.ToArray(),
            Awards = BuildAwards(standings),
            Counters = new Dictionary<string, int>
            {
                ["kills"] = standings.Sum(x => x.Kills), ["assists"] = standings.Sum(x => x.Assists),
                ["objectives"] = standings.Sum(x => x.Objectives), ["participants"] = standings.Count
            }
        };
        Save(result);
        return result;
    }

    public EventResult? GetLast(string? modeId = null) => LoadAll()
        .Where(x => string.IsNullOrWhiteSpace(modeId) || x.ModeId.Equals(modeId, StringComparison.OrdinalIgnoreCase))
        .OrderByDescending(x => x.EndedUtc).FirstOrDefault();

    public EventResult? Get(string runId) => LoadAll().FirstOrDefault(x => x.EventRunId.Equals(runId, StringComparison.OrdinalIgnoreCase));

    void Save(EventResult result)
    {
        Directory.CreateDirectory(_resultRoot);
        var path = Path.Combine(_resultRoot, SafeId(result.EventRunId) + ".json");
        var temp = path + ".tmp-" + Guid.NewGuid().ToString("N");
        File.WriteAllText(temp, JsonSerializer.Serialize(result, JsonOptions), new UTF8Encoding(false));
        File.Move(temp, path, true);

        foreach (var stale in Directory.EnumerateFiles(_resultRoot, "*.json")
                     .OrderByDescending(File.GetLastWriteTimeUtc).Skip(_retention))
            File.Delete(stale);
    }

    IReadOnlyList<EventResult> LoadAll()
    {
        if (!Directory.Exists(_resultRoot)) return Array.Empty<EventResult>();
        var results = new List<EventResult>();
        foreach (var path in Directory.EnumerateFiles(_resultRoot, "*.json"))
        {
            try
            {
                var value = JsonSerializer.Deserialize<EventResult>(File.ReadAllText(path), JsonOptions);
                if (value != null) results.Add(value);
            }
            catch (JsonException) { }
        }
        return results;
    }

    static IReadOnlyList<EventAward> BuildAwards(IReadOnlyList<EventStanding> standings)
    {
        if (standings.Count == 0) return Array.Empty<EventAward>();
        var awards = new List<EventAward>();
        Add("mvp", standings.OrderByDescending(x => x.Score).ThenBy(x => x.SteamId).First(), x => x.Score);
        Add("objective_leader", standings.OrderByDescending(x => x.Objectives).ThenBy(x => x.SteamId).First(), x => x.Objectives);
        Add("support_leader", standings.OrderByDescending(x => x.Assists).ThenBy(x => x.SteamId).First(), x => x.Assists);
        Add("longest_streak", standings.OrderByDescending(x => x.LongestStreak).ThenBy(x => x.SteamId).First(), x => x.LongestStreak);
        return awards;

        void Add(string id, EventStanding standing, Func<EventStanding, int> selector)
        {
            var value = selector(standing);
            if (value > 0) awards.Add(new EventAward { Id = id, SteamId = standing.SteamId, Value = value });
        }
    }

    static string SafeId(string value) => string.Concat(value.Select(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_' ? ch : '_'));
}

/// <summary>Single owner of canonical sequence, persistence, scoring, and result finalization.</summary>
public sealed class ServerEventPlatform : IDisposable
{
    sealed record ActiveRun(string RunId, string ModeId, DateTimeOffset StartedUtc);
    readonly object _gate = new();
    readonly Dictionary<string, ActiveRun> _runsBySession = new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<string, long> _sequences = new(StringComparer.OrdinalIgnoreCase);
    readonly double[] _publishMilliseconds = new double[1024];
    long _publishCount;
    double _publishTotalMilliseconds;

    public EventLedger Ledger { get; }
    public ScoreService Scores { get; } = new();
    public ResultService Results { get; }
    public event Action<GameEventEnvelope>? Published;

    public ServerEventPlatform(string runtimeRoot, int resultRetention = 20)
    {
        Ledger = new EventLedger(Path.Combine(runtimeRoot, "ledger"));
        Results = new ResultService(Path.Combine(runtimeRoot, "results"), resultRetention);
    }

    public string Start(string sessionId, string modeId, DateTimeOffset? occurred = null)
    {
        var run = new ActiveRun(string.IsNullOrWhiteSpace(sessionId) ? Guid.NewGuid().ToString("N") : sessionId,
            modeId, occurred ?? DateTimeOffset.UtcNow);
        lock (_gate) _runsBySession[sessionId] = run;
        Publish(new GameEventEnvelope { EventId = BattleLuckEventIds.EventStarted, EventRunId = run.RunId,
            ModeId = modeId, OccurredUtc = run.StartedUtc });
        return run.RunId;
    }

    public bool TryGetRun(string sessionId, out string runId, out string modeId)
    {
        lock (_gate)
        {
            if (_runsBySession.TryGetValue(sessionId, out var run))
            { runId = run.RunId; modeId = run.ModeId; return true; }
        }
        runId = modeId = ""; return false;
    }

    public GameEventEnvelope Publish(GameEventEnvelope evt)
    {
        var started = Stopwatch.GetTimestamp();
        GameEventEnvelope sequenced;
        try
        {
            lock (_gate)
            {
                _sequences.TryGetValue(evt.EventRunId, out var sequence);
                sequenced = evt with { Sequence = sequence + 1 };
                _sequences[evt.EventRunId] = sequenced.Sequence;
                Ledger.Append(sequenced);
                Scores.Apply(sequenced);
            }
            Published?.Invoke(sequenced);
            return sequenced;
        }
        finally
        {
            var elapsed = (Stopwatch.GetTimestamp() - started) * 1000d / Stopwatch.Frequency;
            lock (_gate)
            {
                var index = (int)(_publishCount % _publishMilliseconds.Length);
                _publishMilliseconds[index] = elapsed;
                _publishCount++;
                _publishTotalMilliseconds += elapsed;
            }
        }
    }

    public EventPlatformDiagnostics GetDiagnostics()
    {
        lock (_gate)
        {
            var sampleCount = (int)Math.Min(_publishCount, _publishMilliseconds.Length);
            var sample = _publishMilliseconds.Take(sampleCount).OrderBy(value => value).ToArray();
            var p99 = sampleCount == 0 ? 0 : sample[Math.Min(sampleCount - 1, (int)Math.Ceiling(sampleCount * 0.99) - 1)];
            return new EventPlatformDiagnostics(_publishCount,
                _publishCount == 0 ? 0 : _publishTotalMilliseconds / _publishCount,
                p99, sampleCount == 0 ? 0 : sample[^1]);
        }
    }

    public EventResult? Finish(string sessionId, string reason, DateTimeOffset? occurred = null)
    {
        ActiveRun? run;
        lock (_gate)
        {
            if (!_runsBySession.Remove(sessionId, out run)) return null;
        }
        var ended = occurred ?? DateTimeOffset.UtcNow;
        Publish(new GameEventEnvelope { EventId = BattleLuckEventIds.EventEnded, EventRunId = run.RunId,
            ModeId = run.ModeId, OccurredUtc = ended, Reason = reason });
        var standings = Scores.Snapshot(run.RunId);
        var result = Results.Finalize(run.RunId, run.ModeId, run.StartedUtc, ended, reason, standings);
        Scores.Remove(run.RunId);
        Ledger.Close(run.RunId);
        return result;
    }

    public void Dispose() => Ledger.Dispose();
}

public sealed record EventPlatformDiagnostics(long Published, double AverageMilliseconds,
    double P99Milliseconds, double MaximumMilliseconds);
