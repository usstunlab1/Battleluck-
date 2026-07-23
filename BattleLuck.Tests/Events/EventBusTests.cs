using FluentAssertions;

namespace BattleLuck.Tests.Events;

public sealed class EventBusTests
{
    [Fact]
    public void RaiseModeEnded_IsolatesThrowingSubscribers()
    {
        var observed = 0;
        Action<ModeEndedEvent> throwing = _ => throw new InvalidOperationException("subscriber failure");
        Action<ModeEndedEvent> observing = _ => observed++;
        GameEvents.OnModeEnded += throwing;
        GameEvents.OnModeEnded += observing;

        try
        {
            var publish = () => GameEvents.RaiseModeEnded(new ModeEndedEvent());

            publish.Should().NotThrow();
            observed.Should().Be(1);
        }
        finally
        {
            GameEvents.OnModeEnded -= throwing;
            GameEvents.OnModeEnded -= observing;
        }
    }
}
