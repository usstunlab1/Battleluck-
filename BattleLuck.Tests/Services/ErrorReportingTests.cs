using BattleLuck.Models;
using BattleLuck.Services.Diagnostics;
using FluentAssertions;

namespace BattleLuck.Tests.Services;

public sealed class ErrorReportingTests
{
    [Fact]
    public async Task DisabledReporter_IsAZeroImpactNoOp()
    {
        await using var reporter = new BacktraceHttpErrorReporter(new BacktraceSettings { Enabled = false }, null);

        reporter.Report(new InvalidOperationException("ignored"));
        await reporter.FlushAsync(TimeSpan.FromMilliseconds(20));

        var state = reporter.GetDiagnostics();
        state.Enabled.Should().BeFalse();
        state.Queued.Should().Be(0);
        state.DisabledReason.Should().Be("disabled");
    }

    [Fact]
    public async Task MissingEnvironmentToken_DisablesReporter()
    {
        await using var reporter = new BacktraceHttpErrorReporter(
            new BacktraceSettings { Enabled = true, Subdomain = "battleluck" }, null);

        reporter.GetDiagnostics().DisabledReason.Should().Be("missing environment token");
    }
}
