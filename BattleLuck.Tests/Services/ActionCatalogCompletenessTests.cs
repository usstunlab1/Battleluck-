using System.Text.Json;
using System.Text.RegularExpressions;
using BattleLuck.Services.Flow;
using BattleLuck.Services.Runtime;

namespace BattleLuck.Tests.Services;

public sealed class ActionCatalogCompletenessTests
{
    [Fact]
    public void Catalog_documentation_and_handler_boundary_are_complete()
    {
        var root = FindRepositoryRoot();
        using var document = JsonDocument.Parse(File.ReadAllText(
            Path.Combine(root, "config", "BattleLuck", "actions_catalog.json")));

        var registered = document.RootElement.GetProperty("registered")
            .EnumerateArray()
            .Select(value => value.GetString()!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var metadata = document.RootElement.GetProperty("metadata")
            .EnumerateObject()
            .Select(property => property.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        Assert.Equal(309, registered.Count);
        Assert.True(registered.SetEquals(metadata), "Every registered action must have exactly one metadata record.");

        var executorSource = File.ReadAllText(Path.Combine(root, "Services", "Flow", "FlowActionExecutor.cs"));
        var switchCases = Regex.Matches(executorSource, "case\\s+\"([^\"]+)\"")
            .Select(match => match.Groups[1].Value)
            .Where(registered.Contains)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var catalogOnlyStart = executorSource.IndexOf(
            "public static readonly IReadOnlySet<string> CatalogOnlyActions",
            StringComparison.Ordinal);
        Assert.True(catalogOnlyStart >= 0);
        var catalogOnlyEnd = executorSource.IndexOf("};", catalogOnlyStart, StringComparison.Ordinal);
        Assert.True(catalogOnlyEnd > catalogOnlyStart);
        var catalogOnlySource = executorSource[catalogOnlyStart..catalogOnlyEnd];
        var catalogOnlyActions = Regex.Matches(catalogOnlySource, "\"([^\"]+)\"")
            .Select(match => match.Groups[1].Value)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var expectedCatalogOnly = registered.Except(switchCases, StringComparer.OrdinalIgnoreCase)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        Assert.True(
            expectedCatalogOnly.SetEquals(catalogOnlyActions),
            "Catalog-only actions must exactly equal registered names without an executor switch case.");

        Assert.Equal(227, switchCases.Count);
        Assert.Equal(82, catalogOnlyActions.Count);
        Assert.Equal(13, RuntimeEffectActionCatalog.Entries.Count);
        Assert.Empty(registered.Intersect(RuntimeEffectActionCatalog.Entries.Keys, StringComparer.OrdinalIgnoreCase));
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
