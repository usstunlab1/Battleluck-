using BattleLuck.Commands;

namespace BattleLuck.Tests.Services;

public sealed class CommandDispatcherTests
{
    [Fact]
    public void Static_command_containers_are_not_mistaken_for_abstract_bases()
    {
        Assert.True(CommandDiscoveryRules.IsCommandContainer(typeof(StaticCommands)));
        Assert.False(CommandDiscoveryRules.IsCommandContainer(typeof(AbstractCommands)));
        Assert.False(CommandDiscoveryRules.IsCommandContainer(typeof(GenericCommands<>)));
    }

    [Fact]
    public void Public_command_surface_contains_only_ai_request()
    {
        BattleLuckCommandDispatcher.EnsureScanned();

        Assert.Equal(new[] { "ai" }, BattleLuckCommandDispatcher.RegisteredCommands);
        Assert.DoesNotContain(BattleLuckCommandDispatcher.RegisteredCommands,
            command => command.Equals("bl", StringComparison.OrdinalIgnoreCase) ||
                       command.StartsWith("bl ", StringComparison.OrdinalIgnoreCase) ||
                       command.StartsWith("ai.dev", StringComparison.OrdinalIgnoreCase));
    }

    static class StaticCommands { }
    abstract class AbstractCommands { }
    sealed class GenericCommands<T> { }
}
