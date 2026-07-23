using BattleLuck.Core;
using FluentAssertions;

namespace BattleLuck.Tests.Services.AI;

public class BaseAiServiceTests
{
    [Fact]
    public async Task RateLimiter_SerializesConcurrentProviderRequests()
    {
        using var service = new ProbeAiService();
        await service.EnterAsync();

        var secondEntry = service.EnterAsync();
        await Task.Delay(25);

        secondEntry.IsCompleted.Should().BeFalse();

        service.Exit();
        await secondEntry.WaitAsync(TimeSpan.FromSeconds(2));
        service.Exit();
    }

    private sealed class ProbeAiService : BaseAiService
    {
        public ProbeAiService() : base(maxRequestsPerSecond: 1000, timeoutSeconds: 1)
        {
        }

        public override bool IsEnabled => true;

        public Task EnterAsync() => ApplyRateLimitAsync();

        public void Exit() => ReleaseRateLimit();
    }
}
