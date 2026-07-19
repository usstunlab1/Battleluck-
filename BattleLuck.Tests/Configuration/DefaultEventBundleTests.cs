using System.Text.Json;
using FluentAssertions;

namespace BattleLuck.Tests.Configuration;

public sealed class DefaultEventBundleTests
{
    static readonly string[] ShippedModes =
    {
        "aievent",
        "bloodbath",
        "colosseum",
        "siege",
        "trial_of_all_actions",
        "trials"
    };

    [Fact]
    public void ShippedFlows_HavePositiveDeathLimitsAndUniqueUsableZones()
    {
        var seenHashes = new Dictionary<int, string>();
        var flowFiles = EnumerateFlowFiles().ToDictionary(x => x.Mode, x => x.Path, StringComparer.OrdinalIgnoreCase);

        flowFiles.Keys.Should().Contain(ShippedModes);

        foreach (var mode in ShippedModes)
        {
            using var document = Parse(flowFiles[mode]);
            var root = document.RootElement;
            var rules = root.GetProperty("rules");
            var lives = rules.GetProperty("livesPerPlayer").GetInt32();
            lives.Should().BeInRange(1, 10, $"{mode} must have a usable participant death limit");

            var zones = root.GetProperty("zones").EnumerateArray().ToArray();
            zones.Should().NotBeEmpty($"{mode} must expose at least one registered zone");

            foreach (var zone in zones)
            {
                var hash = zone.GetProperty("hash").GetInt32();
                hash.Should().BePositive($"{mode} zone hashes are runtime identifiers");
                seenHashes.Should().NotContainKey(hash, $"zone hash {hash} is already owned by {seenHashes.GetValueOrDefault(hash)}");
                seenHashes[hash] = mode;

                ReadVec3(zone, "center");
                ReadVec3(zone, "teleportSpawn");

                var radius = zone.GetProperty("radius").GetDouble();
                var exitRadius = zone.GetProperty("exitRadius").GetDouble();
                radius.Should().BeGreaterThan(0, $"{mode} zone {hash} needs a detection radius");
                exitRadius.Should().BeGreaterThanOrEqualTo(radius, $"{mode} zone {hash} exit radius cannot be inside its arena");
            }
        }
    }

    [Fact]
    public void LegacyZoneProjections_MatchTheirUnifiedCoordinates()
    {
        var checkedModes = new List<string>();

        foreach (var (mode, flowPath) in EnumerateFlowFiles())
        {
            var zonesPath = Path.Combine(Path.GetDirectoryName(flowPath)!, "zones.json");
            if (!File.Exists(zonesPath))
                continue;

            using var flowDocument = Parse(flowPath);
            using var zonesDocument = Parse(zonesPath);
            var unified = flowDocument.RootElement.GetProperty("zones").EnumerateArray()
                .ToDictionary(zone => zone.GetProperty("hash").GetInt32());
            var projected = zonesDocument.RootElement.GetProperty("zones").EnumerateArray()
                .ToDictionary(zone => zone.GetProperty("hash").GetInt32());

            projected.Keys.Should().BeEquivalentTo(unified.Keys, $"{mode} detection and unified flow must register the same zones");
            foreach (var (hash, zone) in projected)
            {
                ReadVec3(zone, "position").Should().Be(ReadVec3(unified[hash], "center"), $"{mode} zone {hash} detection must use the unified center");
                ReadVec3(zone, "center").Should().Be(ReadVec3(zone, "position"), $"{mode} zone {hash} legacy center must not drift from position");
                ReadVec3(zone, "teleportSpawn").Should().Be(ReadVec3(unified[hash], "teleportSpawn"), $"{mode} zone {hash} teleport positions must agree");
            }

            checkedModes.Add(mode);
        }

        checkedModes.Should().BeEquivalentTo(new[] { "aievent", "bloodbath", "colosseum", "siege", "trials" });
    }

    [Fact]
    public void ShippedZones_ResolveRuntimeShapedKits()
    {
        foreach (var (mode, flowPath) in EnumerateFlowFiles())
        {
            using var flow = Parse(flowPath);
            foreach (var zone in flow.RootElement.GetProperty("zones").EnumerateArray())
            {
                var kitId = zone.GetProperty("kitId").GetString();
                kitId.Should().Be(mode, $"{mode} must resolve kits.json through KitController's mode-folder lookup");
            }

            var kitPath = Path.Combine(Path.GetDirectoryName(flowPath)!, "kits.json");
            File.Exists(kitPath).Should().BeTrue($"{mode} must ship the kit used by its entry fallback");
            using var kit = Parse(kitPath);
            var root = kit.RootElement;
            root.TryGetProperty("settings", out _).Should().BeTrue($"{mode} kits.json must use the KitConfig shape");
            root.TryGetProperty("weapons", out _).Should().BeTrue();
            root.TryGetProperty("items", out _).Should().BeTrue();
            root.TryGetProperty("abilities", out _).Should().BeTrue();
            root.TryGetProperty("passiveSpells", out _).Should().BeTrue();
        }
    }

    [Fact]
    public void ShippedKitPrefabReferences_ExistInBundledReferenceInventory()
    {
        var allowlistPath = Path.Combine(RepositoryRoot, "docs", "audit", "systems", "allowlists", "prefabs.allowlist.txt");
        var allowlist = File.ReadLines(allowlistPath)
            .Select(line => line.Trim())
            .Where(line => line.Length > 0)
            .ToHashSet(StringComparer.Ordinal);
        var checkedReferences = 0;

        foreach (var mode in ShippedModes)
        {
            var kitPath = Path.Combine(EventsRoot, mode, "kits.json");
            using var kit = Parse(kitPath);
            var root = kit.RootElement;

            if (root.TryGetProperty("blood", out var blood) &&
                blood.TryGetProperty("type", out var bloodType))
            {
                AssertKnown($"BloodType_{bloodType.GetString()}", $"{mode} blood type");
            }

            if (root.TryGetProperty("armors", out var armors))
            {
                foreach (var armor in armors.EnumerateObject())
                    AssertKnown(armor.Value.GetString(), $"{mode} armor slot '{armor.Name}'");
            }

            foreach (var weapon in root.GetProperty("weapons").EnumerateArray())
                AssertKnown(weapon.GetProperty("prefab").GetString(), $"{mode} weapon");

            foreach (var item in root.GetProperty("items").EnumerateArray())
                AssertKnown(item.GetProperty("prefab").GetString(), $"{mode} item");

            foreach (var ability in root.GetProperty("abilities").EnumerateObject())
                AssertKnown(ability.Value.GetProperty("prefab").GetString(), $"{mode} ability slot '{ability.Name}'");

            foreach (var passive in root.GetProperty("passiveSpells").EnumerateArray())
                AssertKnown(passive.GetProperty("prefab").GetString(), $"{mode} passive spell");
        }

        checkedReferences.Should().BeGreaterThan(0, "the shipped kits must contain resolvable runtime resources");
        return;

        void AssertKnown(string? prefab, string context)
        {
            prefab.Should().NotBeNullOrWhiteSpace($"{context} must name a prefab");
            allowlist.Should().Contain(prefab!, $"{context} prefab '{prefab}' must exist in the bundled reference inventory");
            checkedReferences++;
        }
    }

    [Fact]
    public void EnabledBoundaryPrefabs_ExistInBundledReferenceInventory()
    {
        var allowlistPath = Path.Combine(RepositoryRoot, "docs", "audit", "systems", "allowlists", "prefabs.allowlist.txt");
        var allowlist = File.ReadLines(allowlistPath)
            .Select(line => line.Trim())
            .Where(line => line.Length > 0)
            .ToHashSet(StringComparer.Ordinal);
        var checkedReferences = 0;

        foreach (var (_, flowPath) in EnumerateFlowFiles())
        {
            using var document = Parse(flowPath);
            foreach (var zone in document.RootElement.GetProperty("zones").EnumerateArray())
            {
                if (!zone.TryGetProperty("boundary", out var boundary) ||
                    !boundary.TryGetProperty("walls", out var walls) ||
                    !walls.TryGetProperty("enabled", out var enabled) ||
                    !enabled.GetBoolean())
                {
                    continue;
                }

                if (walls.GetProperty("spawnWalls").GetBoolean())
                {
                    var prefab = walls.GetProperty("wallPrefab").GetString();
                    prefab.Should().NotBeNullOrWhiteSpace();
                    allowlist.Should().Contain(prefab!, $"enabled wall prefab '{prefab}' must exist in the bundled reference inventory");
                    checkedReferences++;
                }

                if (walls.GetProperty("spawnFloors").GetBoolean())
                {
                    var prefab = walls.GetProperty("floorPrefab").GetString();
                    prefab.Should().NotBeNullOrWhiteSpace();
                    allowlist.Should().Contain(prefab!, $"enabled floor prefab '{prefab}' must exist in the bundled reference inventory");
                    checkedReferences++;
                }
            }
        }

        checkedReferences.Should().BeGreaterThan(0, "the shipped testing event enables boundary construction");
    }

    [Fact]
    public void ShippedFlows_DoNotLoadSchematicsMissingFromTheBundle()
    {
        var available = EnumerateSchematicFiles()
            .Select(ReadSchematicName)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var (mode, flowPath) in EnumerateFlowFiles())
        {
            using var document = Parse(flowPath);
            foreach (var action in EnumerateActions(document.RootElement))
            {
                var actionName = ActionName(action);
                if (actionName is not ("schematic.load" or "schematic.loadat" or "schematic.loadatpos"))
                    continue;

                var schematicName = ActionParameter(action, "eventName") ?? ActionParameter(action, "schematicName");
                schematicName.Should().NotBeNullOrWhiteSpace($"{mode} schematic actions must name a bundled asset");
                available.Should().Contain(schematicName!, $"{mode} references schematic '{schematicName}', but no matching asset is shipped");
            }
        }
    }

    static IEnumerable<(string Mode, string Path)> EnumerateFlowFiles() =>
        Directory.EnumerateFiles(EventsRoot, "*.json", SearchOption.TopDirectoryOnly)
            .Select(path => (Mode: Path.GetFileNameWithoutExtension(path), Path: path));

    static IEnumerable<string> EnumerateSchematicFiles()
    {
        var global = Path.Combine(RepositoryRoot, "config", "BattleLuck", "schematics");
        if (Directory.Exists(global))
        {
            foreach (var path in Directory.EnumerateFiles(global, "*.json"))
                yield return path;
        }

        foreach (var eventDirectory in Directory.EnumerateDirectories(EventsRoot))
        {
            var schematics = Path.Combine(eventDirectory, "schematics");
            if (!Directory.Exists(schematics))
                continue;
            foreach (var path in Directory.EnumerateFiles(schematics, "*.json"))
                yield return path;
        }
    }

    static string? ReadSchematicName(string path)
    {
        using var document = Parse(path);
        return document.RootElement.TryGetProperty("eventName", out var name) ? name.GetString() : null;
    }

    static IEnumerable<JsonElement> EnumerateActions(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (property.Name is "actions" or "onCompleteActions" or "deathActions" &&
                    property.Value.ValueKind == JsonValueKind.Array)
                {
                    foreach (var action in property.Value.EnumerateArray())
                        yield return action;
                }

                foreach (var action in EnumerateActions(property.Value))
                    yield return action;
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var child in element.EnumerateArray())
            foreach (var action in EnumerateActions(child))
                yield return action;
        }
    }

    static string ActionName(JsonElement action)
    {
        if (action.TryGetProperty("type", out var type))
            return type.GetString()?.Trim().ToLowerInvariant() ?? "";
        if (action.TryGetProperty("action", out var text))
            return (text.GetString() ?? "").Split(':', 2)[0].Trim().ToLowerInvariant();
        return "";
    }

    static string? ActionParameter(JsonElement action, string parameter)
    {
        if (action.TryGetProperty("params", out var parameters) &&
            parameters.TryGetProperty(parameter, out var value))
        {
            return value.GetString();
        }

        if (!action.TryGetProperty("action", out var text))
            return null;
        foreach (var part in (text.GetString() ?? "").Split(':', 2).ElementAtOrDefault(1)?.Split('|') ?? Array.Empty<string>())
        {
            var pair = part.Split('=', 2);
            if (pair.Length == 2 && pair[0].Equals(parameter, StringComparison.OrdinalIgnoreCase))
                return pair[1];
        }
        return null;
    }

    static (double X, double Y, double Z) ReadVec3(JsonElement owner, string property)
    {
        var value = owner.GetProperty(property);
        return (
            value.GetProperty("x").GetDouble(),
            value.GetProperty("y").GetDouble(),
            value.GetProperty("z").GetDouble());
    }

    static JsonDocument Parse(string path) => JsonDocument.Parse(
        File.ReadAllText(path),
        new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });

    static string EventsRoot => Path.Combine(RepositoryRoot, "config", "BattleLuck", "events");

    static string RepositoryRoot
    {
        get
        {
            DirectoryInfo? directory = new(AppContext.BaseDirectory);
            while (directory != null &&
                   (!File.Exists(Path.Combine(directory.FullName, "BattleLuck.sln")) ||
                    !Directory.Exists(Path.Combine(directory.FullName, "config", "BattleLuck", "events"))))
                directory = directory.Parent;
            return directory?.FullName ?? throw new DirectoryNotFoundException("Could not locate the BattleLuck repository root.");
        }
    }
}
