using BattleLuck.Services.AI;
using BattleLuck.Services.Flow;
using BattleLuck.Services.Runtime;

namespace BattleLuck.Tests.Services;

[Collection("Action manifest")]
public sealed class NaturalLanguageActionRouterTests
{
    [Fact]
    public void Boss_spawn_description_resolves_to_canonical_action()
    {
        WithRepositoryManifest(manifest =>
        {
            var resolved = NaturalLanguageActionRouter.TryResolveEntry(
                "boss spawn pos randum", manifest, out var entry, out var error);

            Assert.True(resolved, error);
            Assert.Equal("spawn.boss", entry.Name);
        });
    }

    [Fact]
    public void Ordinary_question_is_not_treated_as_an_action()
    {
        WithRepositoryManifest(manifest =>
        {
            var resolved = NaturalLanguageActionRouter.TryResolveEntry(
                "how many bosses are alive?", manifest, out _, out var error);

            Assert.False(resolved);
            Assert.Empty(error);
        });
    }

    static void WithRepositoryManifest(Action<ActionManifestService> assertion)
    {
        var previousRoot = ConfigLoader.ConfigRoot;
        try
        {
            ConfigLoader.ConfigRoot = Path.Combine(FindRepositoryRoot(), "config", "BattleLuck");
            assertion(new ActionManifestService());
        }
        finally
        {
            ConfigLoader.ConfigRoot = previousRoot;
        }
    }

    static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current != null)
        {
            if (File.Exists(Path.Combine(current.FullName, "BattleLuck.csproj")))
                return current.FullName;
            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the BattleLuck repository root.");
    }
}

[CollectionDefinition("Action manifest", DisableParallelization = true)]
public sealed class ActionManifestCollection;
