using FluentAssertions;

namespace BattleLuck.Tests.Events;

public sealed class EventBusTests
{
    [Fact(Skip = "Requires executable Unity.Entities binaries from a V Rising dedicated-server installation.")]
    public void RaiseZoneExit_IsolatesThrowingSubscribers()
    {
        var observed = 0;
        Action<ZoneExitEvent> throwing = _ => throw new InvalidOperationException("subscriber failure");
        Action<ZoneExitEvent> observing = _ => observed++;
        GameEvents.OnZoneExit += throwing;
        GameEvents.OnZoneExit += observing;

        try
        {
            var publish = () => GameEvents.RaiseZoneExit(new ZoneExitEvent());

            publish.Should().NotThrow();
            observed.Should().Be(1);
        }
        finally
        {
            GameEvents.OnZoneExit -= throwing;
            GameEvents.OnZoneExit -= observing;
        }
    }
}
