using BattleLuck.Services.AI;
using FluentAssertions;

namespace BattleLuck.Tests.Services.AI;

public class AiRequestPolicyTests
{
    [Fact]
    public void TryValidate_TrimsAndAcceptsBoundaryLength()
    {
        var request = $" {new string('a', AiRequestPolicy.MaxCharacters)} ";

        var accepted = AiRequestPolicy.TryValidate(request, out var normalized, out var error);

        accepted.Should().BeTrue();
        normalized.Should().HaveLength(AiRequestPolicy.MaxCharacters);
        error.Should().BeEmpty();
    }

    [Fact]
    public void TryValidate_RejectsOversizedInput()
    {
        var request = new string('a', AiRequestPolicy.MaxCharacters + 1);

        var accepted = AiRequestPolicy.TryValidate(request, out _, out var error);

        accepted.Should().BeFalse();
        error.Should().Contain(AiRequestPolicy.MaxCharacters.ToString());
    }

    [Fact]
    public void TryValidate_RejectsEmbeddedControlCharacters()
    {
        var accepted = AiRequestPolicy.TryValidate("spawn\u0000wolf", out var normalized, out var error);

        accepted.Should().BeFalse();
        normalized.Should().BeEmpty();
        error.Should().Contain("control characters");
    }
}
