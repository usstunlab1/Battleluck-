using BattleLuck.Commands;
using BattleLuck.Commands.Chat;

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

        Assert.Equal(new[] { "ai request" }, BattleLuckCommandDispatcher.RegisteredCommands);
        Assert.DoesNotContain(BattleLuckCommandDispatcher.RegisteredCommands,
            command => command.Equals("bl", StringComparison.OrdinalIgnoreCase) ||
                       command.StartsWith("bl ", StringComparison.OrdinalIgnoreCase) ||
                       command.StartsWith("ai.dev", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Ai_root_is_registered_for_vcf_help_and_routing()
    {
        var commandAttributes = typeof(BattleLuckRootCommands)
            .GetMethod(nameof(BattleLuckRootCommands.Ai))!
            .GetCustomAttributesData()
            .Where(attribute => attribute.AttributeType.FullName ==
                                "VampireCommandFramework.CommandAttribute")
            .ToArray();

        var attribute = Assert.Single(commandAttributes);
        Assert.Equal("ai", attribute.ConstructorArguments[0].Value);
    }

    static class StaticCommands { }
    abstract class AbstractCommands { }
    sealed class GenericCommands<T> { }
}
