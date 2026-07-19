using BattleLuck.Models;
using FluentAssertions;

namespace BattleLuck.Tests.Models;

public sealed class PlayerEventSessionTests
{
    [Fact]
    public void RegisterDeath_EliminatesAtConfiguredLimit()
    {
        var participant = ActiveParticipant();

        participant.RegisterDeath(3).Should().BeFalse();
        participant.RegisterDeath(3).Should().BeFalse();
        participant.RegisterDeath(3).Should().BeTrue();

        participant.DeathCount.Should().Be(3);
        participant.Eliminated.Should().BeTrue();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void RegisterDeath_TreatsInvalidLimitAsOne(int invalidLimit)
    {
        var participant = ActiveParticipant();

        participant.RegisterDeath(invalidLimit).Should().BeTrue();

        participant.DeathCount.Should().Be(1);
        participant.Eliminated.Should().BeTrue();
    }

    [Fact]
    public void RegisterDeath_IsIdempotentAfterElimination()
    {
        var participant = ActiveParticipant();
        participant.RegisterDeath(1).Should().BeTrue();

        participant.RegisterDeath(1).Should().BeTrue();

        participant.DeathCount.Should().Be(1);
    }

    static PlayerEventSession ActiveParticipant()
    {
        var p = new PlayerEventSession
        {
            SteamId = 76561198000000000,
            SessionId = "session-1",
            ModeId = "bloodbath",
            ZoneHash = 101,
        };
        p.Activate();
        return p;
    }
}
