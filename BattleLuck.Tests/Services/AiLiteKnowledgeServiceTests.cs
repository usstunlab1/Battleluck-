using BattleLuck.Services.Assistant;
using FluentAssertions;

namespace BattleLuck.Tests.Services;

public sealed class AiLiteKnowledgeServiceTests
{
    [Fact]
    public void EmbeddedKnowledge_AnswersWithoutNetworkOrConfiguration()
    {
        var service = new AiLiteKnowledgeService();

        service.Answer("How do I configure an event?").ToLowerInvariant()
            .Should().NotContain("could not match");
    }

    [Fact]
    public void UnknownQuestion_FailsClosedToAiRequestHelp()
    {
        var service = new AiLiteKnowledgeService();

        service.Answer("xyzzy-unrecognized-request")
            .Should().Contain(".ai <request>");
    }
}
