using System.Text.Json;
using BattleLuck.Services.Runtime;
using FluentAssertions;

namespace BattleLuck.Tests.Services;

public sealed class UnifiedEventMigrationServiceTests : IDisposable
{
    readonly string _root = Path.Combine(Path.GetTempPath(), "battleluck-migration-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void SplitKit_IsBackedUpAndMergedWithoutOverwritingOnSecondRun()
    {
        var events = Path.Combine(_root, "events");
        var legacy = Path.Combine(events, "arena");
        Directory.CreateDirectory(legacy);
        var eventPath = Path.Combine(events, "arena.json");
        File.WriteAllText(eventPath, """{"metadata":{"id":"arena","enabled":true},"rules":{},"zones":[],"actions":[]}""");
        File.WriteAllText(Path.Combine(legacy, "kits.json"),
            """{"settings":{"clearInventory":false},"weapons":[],"items":[],"abilities":{},"passiveSpells":[]}""");

        UnifiedEventMigrationService.MigrateSplitDefinitions(_root).Should().Be(1);
        UnifiedEventMigrationService.MigrateSplitDefinitions(_root).Should().Be(0);

        File.Exists(eventPath + ".split-v1.bak").Should().BeTrue();
        using var document = JsonDocument.Parse(File.ReadAllText(eventPath));
        document.RootElement.TryGetProperty("kit", out _).Should().BeTrue();
        document.RootElement.GetProperty("metadata").GetProperty("id").GetString().Should().Be("arena");
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}
