using BattleLuck.Services.Runtime;

namespace BattleLuck.Tests.Services;

public sealed class KillAttributionServiceTests
{
    [Fact]
    public void RejectsSelfTeamDuplicateAndFarmKillsWhileAcceptingOneAssist()
    {
        var service = new KillAttributionService();
        var now = DateTimeOffset.Parse("2026-01-01T00:00:00Z");
        var teams = new Dictionary<ulong, int> { [1] = 1, [2] = 1, [3] = 2, [4] = 2 };

        Assert.Equal("self_kill", service.Evaluate("run", 1, 1, 0, teams, now).Reason);
        Assert.Equal("team_kill", service.Evaluate("run", 1, 2, 0, teams, now).Reason);
        var accepted = service.Evaluate("run", 1, 3, 4, teams, now);
        Assert.True(accepted.Scorable);
        Assert.Equal((ulong)4, accepted.AssistantSteamId);
        Assert.Equal("duplicate_kill", service.Evaluate("run", 1, 3, 0, teams, now.AddSeconds(1)).Reason);

        Assert.True(service.Evaluate("run", 1, 3, 0, teams, now.AddSeconds(3)).Scorable);
        Assert.True(service.Evaluate("run", 1, 3, 0, teams, now.AddSeconds(6)).Scorable);
        Assert.Equal("farm_kill", service.Evaluate("run", 1, 3, 0, teams, now.AddSeconds(9)).Reason);
    }
}
