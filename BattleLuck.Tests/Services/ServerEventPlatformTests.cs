using System.Text.Json;
using BattleLuck.Models;
using BattleLuck.Services.Runtime;

namespace BattleLuck.Tests.Services;

public sealed class ServerEventPlatformTests : IDisposable
{
    readonly string _root = Path.Combine(Path.GetTempPath(), "battleluck-tests-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void Ledger_recovers_only_a_truncated_final_record()
    {
        var ledger = new EventLedger(Path.Combine(_root, "ledger"));
        ledger.Append(new GameEventEnvelope { EventId = BattleLuckEventIds.EventStarted, EventRunId = "run-1", Sequence = 1 });
        File.AppendAllText(ledger.GetPath("run-1"), "{\"event_id\":");

        var recovered = ledger.ReadRecoverable("run-1");

        Assert.Single(recovered);
        Assert.Equal(BattleLuckEventIds.EventStarted, recovered[0].EventId);
    }

    [Fact]
    public void Ledger_rejects_corruption_before_the_final_record()
    {
        var ledger = new EventLedger(Path.Combine(_root, "ledger"));
        Directory.CreateDirectory(Path.GetDirectoryName(ledger.GetPath("run-2"))!);
        var valid = JsonSerializer.Serialize(new GameEventEnvelope { EventId = "event.ended", EventRunId = "run-2" });
        File.WriteAllText(ledger.GetPath("run-2"), "not-json\n" + valid + "\n");

        Assert.Throws<InvalidDataException>(() => ledger.ReadRecoverable("run-2"));
    }

    [Fact]
    public void Results_use_the_declared_deterministic_tie_breakers()
    {
        using var platform = new ServerEventPlatform(Path.Combine(_root, "runtime"));
        var run = platform.Start("session", "bloodbath", DateTimeOffset.Parse("2026-01-01T00:00:00Z"));
        platform.Publish(new GameEventEnvelope { EventId = BattleLuckEventIds.ScoreChanged, EventRunId = run,
            ModeId = "bloodbath", ActorSteamId = 2, Points = 10, OccurredUtc = DateTimeOffset.Parse("2026-01-01T00:00:02Z") });
        platform.Publish(new GameEventEnvelope { EventId = BattleLuckEventIds.ScoreChanged, EventRunId = run,
            ModeId = "bloodbath", ActorSteamId = 1, Points = 10, OccurredUtc = DateTimeOffset.Parse("2026-01-01T00:00:01Z") });

        var result = platform.Finish("session", "score_limit", DateTimeOffset.Parse("2026-01-01T00:10:00Z"));

        Assert.NotNull(result);
        Assert.Equal((ulong)1, result!.Standings[0].SteamId);
        Assert.Equal("1", result.Winner!.Id);
        Assert.Equal(2, platform.Ledger.ReadRecoverable(run).Count(e => e.EventId == BattleLuckEventIds.ScoreChanged));
    }

    [Fact]
    public void Normal_event_processing_stays_within_the_release_budget()
    {
        using var platform = new ServerEventPlatform(Path.Combine(_root, "performance"));
        var run = platform.Start("performance-session", "bloodbath");
        for (var i = 0; i < 250; i++)
            platform.Publish(new GameEventEnvelope
            {
                EventId = BattleLuckEventIds.ScoreChanged,
                EventRunId = run,
                ActorSteamId = (ulong)(i % 16 + 1),
                Points = 1
            });

        var metrics = platform.GetDiagnostics();
        Assert.True(metrics.AverageMilliseconds < 1,
            $"Average event cost was {metrics.AverageMilliseconds:F3} ms.");
        Assert.True(metrics.P99Milliseconds < 5,
            $"P99 event cost was {metrics.P99Milliseconds:F3} ms.");
    }

    [Fact]
    public void KillAndDeathSignals_DoNotDoubleCountVictimDeaths()
    {
        using var platform = new ServerEventPlatform(Path.Combine(_root, "death-count"));
        var run = platform.Start("death-session", "bloodbath");
        platform.Publish(new GameEventEnvelope { EventId = BattleLuckEventIds.PlayerKill,
            EventRunId = run, ActorSteamId = 1, TargetSteamId = 2 });
        platform.Publish(new GameEventEnvelope { EventId = BattleLuckEventIds.PlayerDeath,
            EventRunId = run, ActorSteamId = 1, TargetSteamId = 2 });

        Assert.Equal(1, platform.Scores.Snapshot(run).Single(row => row.SteamId == 2).Deaths);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }
}
