using BattleLuck.Services.AI;
using FluentAssertions;

namespace BattleLuck.Tests.Services.AI;

public class ConversationStoreTests
{
    [Fact]
    public void Append_RemainsBoundedDuringLongRunningUse()
    {
        var store = new ConversationStore(maxTurns: 200);

        for (var index = 0; index < 10_000; index++)
        {
            store.Append(new ConversationTurn
            {
                SteamId = 1,
                Speaker = ConversationSpeaker.Player,
                Text = $"message-{index}"
            });
        }

        var recent = store.Recent(10_000);
        recent.Should().HaveCount(200);
        recent[0].Text.Should().Be("message-9800");
        recent[^1].Text.Should().Be("message-9999");
    }

    [Fact]
    public void Clear_RemovesHistoryAndInteractiveSessions()
    {
        var store = new ConversationStore();
        store.BeginInteractiveSession(42);
        store.Append(new ConversationTurn { SteamId = 42, Text = "hello" });

        store.Clear();

        store.HasInteractiveSession(42).Should().BeFalse();
        store.Recent(10).Should().BeEmpty();
    }
}
