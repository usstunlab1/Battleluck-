using BattleLuck.Services.Runtime;

namespace BattleLuck.Tests.Services;

[Collection("Action manifest")]
public sealed class RuntimeEffectActionCatalogTests
{
    [Theory]
    [InlineData("effect.assign")]
    [InlineData("zone.border.effect.apply")]
    [InlineData("spawn.effect.assign")]
    [InlineData("tracking.group.effect.apply")]
    public void Runtime_effect_actions_exist_before_harmony_startup(string actionName)
    {
        var manifest = new FakeManifest();
        RuntimeEffectActionCatalog.InjectEntries(manifest.MutableEntries, typeof(FakeEntry));

        Assert.True(manifest.Entries.TryGetValue(actionName, out var entry));
        Assert.True(entry!.HandlerAvailable);
    }

    [Theory]
    [InlineData("effect.assign")]
    [InlineData("effect.remove")]
    [InlineData("effect.replace")]
    [InlineData("effect.status")]
    [InlineData("effect.clear_group")]
    [InlineData("zone.border.effect.apply")]
    [InlineData("zone.border.effect.remove")]
    [InlineData("zone.border.effect.replace")]
    [InlineData("zone.border.effect.status")]
    [InlineData("spawn.effect.assign")]
    [InlineData("spawn.effect.remove")]
    [InlineData("tracking.group.effect.apply")]
    [InlineData("tracking.group.effect.remove")]
    public void Real_manifest_exposes_runtime_effects_before_live_world(string actionName)
    {
        var previousRoot = ConfigLoader.ConfigRoot;
        try
        {
            ConfigLoader.ConfigRoot = Path.Combine(FindRepositoryRoot(), "config", "BattleLuck");
            var manifest = new ActionManifestService();

            Assert.True(manifest.TryGetAction(actionName, out var entry));
            Assert.NotNull(entry);
            Assert.True(entry!.HandlerAvailable);
        }
        finally
        {
            ConfigLoader.ConfigRoot = previousRoot;
        }
    }

    [Theory]
    [InlineData("aievent")]
    [InlineData("bloodbath")]
    public void Effect_enabled_events_validate_before_live_world(string modeId)
    {
        var previousRoot = ConfigLoader.ConfigRoot;
        try
        {
            ConfigLoader.ConfigRoot = Path.Combine(FindRepositoryRoot(), "config", "BattleLuck");
            var path = Path.Combine(ConfigLoader.ConfigRoot, "events", $"{modeId}.json");
            using var document = System.Text.Json.JsonDocument.Parse(File.ReadAllText(path));
            var allowedActions = document.RootElement
                .GetProperty("ai")
                .GetProperty("policy")
                .GetProperty("allowedActions")
                .EnumerateArray()
                .Select(value => value.GetString())
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var manifest = new ActionManifestService();

            foreach (var actionName in RuntimeEffectActionCatalog.Entries.Keys)
            {
                Assert.Contains(actionName, allowedActions);
                Assert.True(manifest.TryGetAction(actionName, out var entry), actionName);
                Assert.True(entry!.HandlerAvailable, actionName);
            }
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

    sealed class FakeManifest
    {
        // Field name and generic shape intentionally match ActionManifestService.
        readonly Dictionary<string, FakeEntry> _entries = new(StringComparer.OrdinalIgnoreCase);
        public System.Collections.IDictionary MutableEntries => _entries;
        public IReadOnlyDictionary<string, FakeEntry> Entries => _entries;
    }

    sealed class FakeEntry
    {
        public string Name { get; set; } = "";
        public string Category { get; set; } = "";
        public string Description { get; set; } = "";
        public string RiskLevel { get; set; } = "";
        public bool RequiresApproval { get; set; }
        public bool HandlerAvailable { get; set; }
        public bool Executable { get; set; }
        public bool MainThreadRequired { get; set; }
        public string Availability { get; set; } = "";
        public List<string> Required { get; } = new();
        public List<string> Optional { get; } = new();
        public List<string> Examples { get; } = new();
    }
}
