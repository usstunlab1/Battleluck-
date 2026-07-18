using BattleLuck.Commands.Converters;

namespace BattleLuck.Services.Runtime;

public sealed class ActionManifestService
{
    readonly Dictionary<string, ActionManifestEntry> _entries = new(StringComparer.OrdinalIgnoreCase);
    static readonly HashSet<string> StrictlyBlockedNativeMutations = new(StringComparer.OrdinalIgnoreCase)
    {
        "build.free", "build.spawn", "structure.spawn", "tile.place", "wall.build", "floor.place", "wall.destroy",
        "zone.border.place", "zone.border.place_all", "zone.border.remove", "zone.border.remove_all"
    };

    public ActionManifestService()
    {
        Reload();
    }

    public IReadOnlyDictionary<string, ActionManifestEntry> Entries
    {
        get
        {
            AddLiveSystemEntries();
            return _entries;
        }
    }

    public void Reload()
    {
        _entries.Clear();
        foreach (var action in FlowActionExecutor.SupportedActions)
        {
            if (string.IsNullOrWhiteSpace(action)) continue;
            _entries[action] = new ActionManifestEntry
            {
                Name = action,
                Category = GuessCategory(action),
                Examples = { action }
            };
        }

        LoadCatalogMetadata();
        AddLiveSystemEntries();
        ApplyRequiredFallbacks();
    }

    void LoadCatalogMetadata()
    {
        try
        {
            var path = Path.Combine(ConfigLoader.ConfigRoot, "actions_catalog.json");
            if (!File.Exists(path)) return;

            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            if (doc.RootElement.TryGetProperty("examples", out var examples) && examples.ValueKind == JsonValueKind.Object)
            {
                foreach (var category in examples.EnumerateObject())
                {
                    if (category.Value.ValueKind != JsonValueKind.Array) continue;
                    foreach (var item in category.Value.EnumerateArray())
                    {
                        var actionString = item.GetString();
                        if (string.IsNullOrWhiteSpace(actionString)) continue;
                        var name = actionString.Split(':', 2)[0].Trim();
                        if (string.IsNullOrWhiteSpace(name)) continue;

                        if (!_entries.TryGetValue(name, out var entry))
                        {
                            entry = new ActionManifestEntry { Name = name };
                            _entries[name] = entry;
                        }

                        entry.Category = category.Name;
                        if (!entry.Examples.Contains(actionString, StringComparer.OrdinalIgnoreCase))
                            entry.Examples.Add(actionString);
                    }
                }
            }

            if (doc.RootElement.TryGetProperty("metadata", out var metadata) && metadata.ValueKind == JsonValueKind.Object)
            {
                foreach (var property in metadata.EnumerateObject())
                {
                    if (!_entries.TryGetValue(property.Name, out var entry))
                    {
                        entry = new ActionManifestEntry { Name = property.Name };
                        _entries[property.Name] = entry;
                    }

                    var obj = property.Value;
                    if (obj.ValueKind != JsonValueKind.Object) continue;
                    if (obj.TryGetProperty("category", out var category) && category.ValueKind == JsonValueKind.String)
                        entry.Category = category.GetString() ?? entry.Category;
                    if (obj.TryGetProperty("description", out var description) && description.ValueKind == JsonValueKind.String)
                        entry.Description = description.GetString() ?? entry.Description;
                    if (obj.TryGetProperty("riskLevel", out var risk) && risk.ValueKind == JsonValueKind.String)
                        entry.RiskLevel = risk.GetString() ?? entry.RiskLevel;
                    if (obj.TryGetProperty("requiresApproval", out var approval) &&
                        (approval.ValueKind == JsonValueKind.True || approval.ValueKind == JsonValueKind.False))
                        entry.RequiresApproval = approval.GetBoolean();
                    if (obj.TryGetProperty("handlerAvailable", out var handler) &&
                        (handler.ValueKind == JsonValueKind.True || handler.ValueKind == JsonValueKind.False))
                        entry.HandlerAvailable = handler.GetBoolean();
                    if (obj.TryGetProperty("aliases", out var aliases) && aliases.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var alias in aliases.EnumerateArray())
                        {
                            var value = alias.GetString();
                            if (!string.IsNullOrWhiteSpace(value) &&
                                !entry.Aliases.Contains(value, StringComparer.OrdinalIgnoreCase))
                                entry.Aliases.Add(value);
                        }
                    }
                    ReadStringArray(obj, "required", entry.Required);
                    ReadStringArray(obj, "optional", entry.Optional);
                    ReadStringArray(obj, "examples", entry.Examples);
                    ReadDefaults(obj, entry.Defaults);
                    ReadParamMetadata(obj, entry);
                }
            }

            foreach (var entry in _entries.Values)
            {
                if (string.IsNullOrWhiteSpace(entry.RiskLevel) || entry.RiskLevel.Equals("controlled", StringComparison.OrdinalIgnoreCase))
                    entry.RiskLevel = InferRisk(entry.Name, entry.Category);
                entry.RequiresApproval = !entry.RiskLevel.Equals("safe", StringComparison.OrdinalIgnoreCase);
                entry.HandlerAvailable = entry.HandlerAvailable && FlowActionExecutor.SupportedActions.Contains(entry.Name, StringComparer.OrdinalIgnoreCase);
            }
        }
        catch (Exception ex)
        {
            BattleLuckPlugin.LogWarning($"[ActionManifestService] Failed to load catalog metadata: {ex.Message}");
        }
    }

    public List<CatalogActionSearchResult> Search(string query, int maxResults = 10)
    {
        AddLiveSystemEntries();
        var terms = (query ?? "")
            .Split(new[] { ' ', ',', ';', ':', '|', '.', '_' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(t => t.Length > 1)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (terms.Count == 0)
            return _entries.Values.Take(maxResults).Select(ToSearchResult).ToList();

        return _entries.Values
            .Select(entry => (entry, score: Score(entry, terms)))
            .Where(item => item.score > 0)
            .OrderByDescending(item => item.score)
            .ThenBy(item => item.entry.Name, StringComparer.OrdinalIgnoreCase)
            .Take(Math.Max(1, maxResults))
            .Select(item =>
            {
                var result = ToSearchResult(item.entry);
                result.Score = item.score;
                return result;
            })
            .ToList();
    }

    public string NormalizeActionName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return "";

        var trimmed = name.Trim();
        if (_entries.ContainsKey(trimmed))
            return trimmed;

        var alias = _entries.Values.FirstOrDefault(e => e.Aliases.Any(a => a.Equals(trimmed, StringComparison.OrdinalIgnoreCase)));
        return alias?.Name ?? trimmed;
    }

    public OperationResult Validate(EventActionDefinition action)
    {
        var actionString = action.ToActionString();
        if (string.IsNullOrWhiteSpace(actionString))
            return OperationResult.Fail("Action definition is empty.");

        var (parsedType, parsedParams) = FlowActionExecutor.ParseActionString(actionString);
        var normalized = ActionParameterConverter.Normalize(parsedType, parsedParams);

        var type = action.Type;
        if (string.IsNullOrWhiteSpace(type))
            type = normalized.ActionName;

        type = NormalizeActionName(type);
        if (!_entries.TryGetValue(type, out var entry) &&
            LiveSystemRegistryService.TryGet(type, out var liveSystem))
        {
            entry = CreateLiveSystemEntry(liveSystem);
            _entries[type] = entry;
        }

        if (entry == null)
            return OperationResult.Fail($"Unknown action '{type}'. Add it to actions_catalog.json or register a verified ProjectM/Unity system first.");

        if (!entry.HandlerAvailable)
            return OperationResult.Fail($"Action '{type}' is cataloged but no runtime handler is available.");

        foreach (var required in entry.Required)
        {
            if (!normalized.Parameters.TryGetValue(required, out var value) || string.IsNullOrWhiteSpace(value))
                return OperationResult.Fail($"Action '{type}' is missing required parameter '{required}'.");
        }

        var safety = ValidateSemanticSafety(type, normalized.Parameters);
        if (!safety.Success)
            return safety;

        return OperationResult.Ok();
    }

    static OperationResult ValidateSemanticSafety(string type, IReadOnlyDictionary<string, string> parameters)
    {
        var canonical = type.Trim().ToLowerInvariant();
        if (StrictlyBlockedNativeMutations.Contains(canonical))
            return OperationResult.Fail($"Action '{type}' is disabled by the strict server-stability profile (native construction, schematic, tech/progression, and destructive mutations are blocked).");

        var isTileGeometry = canonical is "wall.build" or "floor.place" or "tile.place" or "zone.border.place" or "zone.border.place_all";
        if (isTileGeometry)
        {
            foreach (var key in new[] { "prefab", "wallPrefab", "floorPrefab", "tilePrefab", "wallType", "floorType" })
            {
                if (!parameters.TryGetValue(key, out var prefab) || string.IsNullOrWhiteSpace(prefab))
                    continue;
                if (prefab.StartsWith("BP_", StringComparison.OrdinalIgnoreCase))
                    return OperationResult.Fail($"Action '{type}' cannot use blueprint prefab '{prefab}'; use a validated TM_ tile model.");
                if (int.TryParse(prefab, out var prefabHash))
                {
                    if (VRisingCore.IsReady &&
                        !EventTileSafety.TryResolveSafeTileModelPrefab(new PrefabGUID(prefabHash), out _, out var prefabError))
                    {
                        return OperationResult.Fail($"Action '{type}' has unsafe numeric tile prefab '{prefab}': {prefabError}.");
                    }
                    continue;
                }
                if (!prefab.StartsWith("TM_", StringComparison.OrdinalIgnoreCase))
                    return OperationResult.Fail($"Action '{type}' requires a TM_ tile-model prefab; got '{prefab}'.");
            }

            if (parameters.TryGetValue("maxTiles", out var maxTilesText) &&
                int.TryParse(maxTilesText, out var maxTiles) && maxTiles > 30)
            {
                return OperationResult.Fail($"Action '{type}' requests {maxTiles} tiles; the live safety cap is 30.");
            }

            if (parameters.TryGetValue("batchSize", out var batchText) &&
                int.TryParse(batchText, out var batchSize) && batchSize > 2)
            {
                return OperationResult.Fail($"Action '{type}' requests batchSize={batchSize}; the safe live geometry maximum is 2.");
            }
        }

        if (canonical is "schematic.load" or "schematic.loadat" or "schematic.loadatpos")
        {
            if (!parameters.TryGetValue("safetyMode", out var safetyMode) ||
                !safetyMode.Equals("event_tracked_zone_only", StringComparison.OrdinalIgnoreCase))
            {
                return OperationResult.Fail($"Action '{type}' requires safetyMode=event_tracked_zone_only so only first-entry session geometry is tracked and cleaned.");
            }
        }

        if (canonical is "entity.spawn" or "prefab.spawn" or "npc.spawn" or "spawn.npc")
        {
            if (parameters.TryGetValue("count", out var countText) &&
                int.TryParse(countText, out var count) && count > 50)
            {
                return OperationResult.Fail($"Action '{type}' requests count={count}; the live safety cap is 50.");
            }
        }

        return OperationResult.Ok();
    }

    void ApplyRequiredFallbacks()
    {
        AddRequired("announce", "message");
        AddRequired("notification", "message");
        AddRequired("notify", "message");
        AddRequired("send_message", "message");
        AddRequired("inventory.send", "itemId");
        AddRequired("prefab.grant", "prefab");
        AddRequired("merchant.run", "listingId");
        AddRequired("merchant.grant", "listingId");
        AddRequired("prefab.spawn", "prefab");
        AddRequired("entity.spawn", "prefab");
        AddRequired("entity.destroy", "type");
        AddRequired("entity.count", "type");
        AddRequired("entity.query", "type");
        AddRequired("entity.validate", "type");
        AddRequired("spawn.boss", "prefab");
        AddRequired("boss.spawn", "prefab");
        AddRequired("npc.spawn", "prefab");
        AddRequired("spawn.npc", "prefab");
        AddRequired("boss.add_servant", "prefab");
        AddRequired("boss.remove_servant", "servantId");
        AddRequired("boss.command_servants", "command");
        AddRequired("boss.spawn_servants", "prefab");
        AddRequired("schematic.load", "eventName");
        AddRequired("schematic.loadat", "eventName");
        AddRequired("schematic.loadatpos", "eventName");
        AddRequired("schematic.clear", "eventName");
        AddRequired("sequence.custom.play", "sequenceId");
        AddRequired("sequence.custom.run", "sequenceId");
        AddRequired("sequence.custom.execute", "sequenceId");
        AddRequired("sequence.custom.preview", "sequenceId");
    }

    void AddLiveSystemEntries()
    {
        foreach (var registration in LiveSystemRegistryService.GetAll())
            _entries[registration.Action] = CreateLiveSystemEntry(registration);
    }

    static ActionManifestEntry CreateLiveSystemEntry(LiveSystemRegistration registration) => new()
    {
        Name = registration.Action,
        Category = "system",
        Description = string.IsNullOrWhiteSpace(registration.Description)
            ? $"Verified {registration.Runtime} system reference: {registration.SystemType}. Native ECS execution is not auto-created."
            : registration.Description,
        RiskLevel = "controlled",
        RequiresApproval = true,
        HandlerAvailable = true,
        Examples = { registration.Action }
    };

    void AddRequired(string actionName, params string[] names)
    {
        if (!_entries.TryGetValue(actionName, out var entry))
            return;

        foreach (var name in names)
        {
            if (!entry.Required.Contains(name, StringComparer.OrdinalIgnoreCase))
                entry.Required.Add(name);
        }
    }

    static void ReadStringArray(JsonElement obj, string propertyName, List<string> target)
    {
        if (!obj.TryGetProperty(propertyName, out var array) || array.ValueKind != JsonValueKind.Array)
            return;

        foreach (var item in array.EnumerateArray())
        {
            var value = item.GetString();
            if (!string.IsNullOrWhiteSpace(value) &&
                !target.Contains(value, StringComparer.OrdinalIgnoreCase))
                target.Add(value);
        }
    }

    static void ReadDefaults(JsonElement obj, Dictionary<string, string> target)
    {
        if (!obj.TryGetProperty("defaults", out var defaults) || defaults.ValueKind != JsonValueKind.Object)
            return;

        foreach (var property in defaults.EnumerateObject())
            target[property.Name] = property.Value.ValueKind == JsonValueKind.String
                ? property.Value.GetString() ?? ""
                : property.Value.ToString();
    }

    static void ReadParamMetadata(JsonElement obj, ActionManifestEntry entry)
    {
        if (!obj.TryGetProperty("params", out var parameters) || parameters.ValueKind != JsonValueKind.Object)
            return;

        foreach (var parameter in parameters.EnumerateObject())
        {
            if (parameter.Value.ValueKind == JsonValueKind.Object &&
                parameter.Value.TryGetProperty("required", out var required) &&
                required.ValueKind == JsonValueKind.True)
            {
                if (!entry.Required.Contains(parameter.Name, StringComparer.OrdinalIgnoreCase))
                    entry.Required.Add(parameter.Name);
            }
            else if (!entry.Optional.Contains(parameter.Name, StringComparer.OrdinalIgnoreCase))
            {
                entry.Optional.Add(parameter.Name);
            }

            if (parameter.Value.ValueKind == JsonValueKind.Object &&
                parameter.Value.TryGetProperty("default", out var defaultValue))
            {
                entry.Defaults[parameter.Name] = defaultValue.ValueKind == JsonValueKind.String
                    ? defaultValue.GetString() ?? ""
                    : defaultValue.ToString();
            }
        }
    }

    static string GuessCategory(string action)
    {
        var dot = action.IndexOf('.');
        if (dot > 0) return action[..dot];
        var colon = action.IndexOf(':');
        return colon > 0 ? action[..colon] : "runtime";
    }

    static int Score(ActionManifestEntry entry, List<string> terms)
    {
        var haystack = string.Join(" ", new[]
        {
            entry.Name,
            entry.Category,
            entry.Description,
            string.Join(" ", entry.Aliases),
            string.Join(" ", entry.Examples)
        });

        var score = 0;
        foreach (var term in terms)
        {
            if (entry.Name.Contains(term, StringComparison.OrdinalIgnoreCase)) score += 8;
            if (entry.Category.Contains(term, StringComparison.OrdinalIgnoreCase)) score += 4;
            if (entry.Description.Contains(term, StringComparison.OrdinalIgnoreCase)) score += 4;
            if (entry.Aliases.Any(a => a.Contains(term, StringComparison.OrdinalIgnoreCase))) score += 5;
            if (entry.Examples.Any(e => e.Contains(term, StringComparison.OrdinalIgnoreCase))) score += 3;
            if (haystack.Contains(term, StringComparison.OrdinalIgnoreCase)) score += 1;
        }
        return score;
    }

    static CatalogActionSearchResult ToSearchResult(ActionManifestEntry entry) => new()
    {
        Name = entry.Name,
        Category = entry.Category,
        RiskLevel = entry.RiskLevel,
        RequiresApproval = entry.RequiresApproval,
        Examples = entry.Examples.Take(3).ToList()
    };

    static string InferRisk(string name, string category)
    {
        var text = $"{category}.{name}";
        if (text.Contains("query", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("notify", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("notification", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("timer", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("score", StringComparison.OrdinalIgnoreCase))
            return "safe";

        if (text.Contains("destroy", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("remove", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("clear", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("damage", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("death", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("mode.end", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("rollback", StringComparison.OrdinalIgnoreCase))
            return "destructive";

        return "controlled";
    }
}
