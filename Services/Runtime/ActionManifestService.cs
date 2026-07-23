using BattleLuck.Models;
using BattleLuck.Commands.Converters;
using BattleLuck.Services.Flow;
using System.Text.Json;

namespace BattleLuck.Services.Runtime;

public sealed class ActionManifestService
{
    internal static Action<string>? WarningSink { get; set; }
    internal static bool LiveSystemEntriesEnabled { get; set; }
    /// <summary>Shared singleton for static callers.</summary>
    static readonly Lazy<ActionManifestService> Shared = new(() => new ActionManifestService());
    public static ActionManifestService Instance => Shared.Value;

    readonly Dictionary<string, ActionManifestEntry> _entries = new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<string, SequenceDefinition> _sequences = new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<string, string> _aliasMappings = new(StringComparer.OrdinalIgnoreCase);
    readonly HashSet<string> _supportedActions = new(StringComparer.OrdinalIgnoreCase);
    static readonly HashSet<string> StrictlyBlockedNativeMutations = new(StringComparer.OrdinalIgnoreCase)
    {
        "build.free", "build.spawn", "structure.spawn", "tile.place", "wall.build", "floor.place", "wall.destroy",
        "zone.border.place", "zone.border.place_all", "zone.border.remove", "zone.border.remove_all"
    };
    static readonly JsonSerializerOptions CatalogJsonOpts = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
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
        _sequences.Clear();
        _aliasMappings.Clear();
        _supportedActions.Clear();
        foreach (var action in LoadSupportedActionNames())
        {
            if (string.IsNullOrWhiteSpace(action)) continue;
            _supportedActions.Add(action);
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
        // These handlers are supplied by BattleLuck rather than the
        // owner-editable JSON catalog. They must exist before configured modes
        // are loaded: mode registration runs before Harmony patching and before
        // the V Rising server world is available.
        RuntimeEffectActionCatalog.InjectEntries(_entries, typeof(ActionManifestEntry));
    }

    void LoadCatalogMetadata()
    {
        try
        {
            var path = Path.Combine(ConfigLoader.ConfigRoot, "actions_catalog.json");
            if (!File.Exists(path)) return;

            var json = File.ReadAllText(path);
            using var doc = JsonDocument.Parse(json);

            // ── Load full action definitions from the "actions" array ────────
            if (doc.RootElement.TryGetProperty("actions", out var actionsArray) && actionsArray.ValueKind == JsonValueKind.Array)
            {
                var catalog = JsonSerializer.Deserialize<ActionCatalog>(json, CatalogJsonOpts);
                if (catalog != null)
                {
                    foreach (var def in catalog.Actions)
                    {
                        if (string.IsNullOrWhiteSpace(def.ActionId)) continue;

                        if (!_entries.TryGetValue(def.ActionId, out var entry))
                        {
                            entry = new ActionManifestEntry { Name = def.ActionId };
                            _entries[def.ActionId] = entry;
                        }

                        entry.Action = def.Action;
                        entry.Params = new Dictionary<string, JsonElement>(def.Params, StringComparer.OrdinalIgnoreCase);
                        entry.Category = string.IsNullOrWhiteSpace(def.Category) ? entry.Category : def.Category;
                        entry.Description = string.IsNullOrWhiteSpace(def.Description) ? entry.Description : def.Description;
                        entry.RiskLevel = string.IsNullOrWhiteSpace(def.RiskLevel) ? entry.RiskLevel : def.RiskLevel;
                        entry.RequiresApproval = def.RequiresApproval;
                        entry.RollbackAction = def.RollbackAction;
                        entry.Handler = def.Handler;
                        entry.MainThreadRequired = def.MainThreadRequired;
                        entry.SideEffects = new List<string>(def.SideEffects);
                        entry.Availability = def.Availability;
                        entry.Executable = def.Executable;
                        entry.ClientRequired = def.ClientRequired;
                        entry.UsesNativeReplication = def.UsesNativeReplication;
                        entry.IsReversible = def.Reversible;

                        if (def.Aliases.Count > 0)
                        {
                            foreach (var alias in def.Aliases)
                                if (!entry.Aliases.Contains(alias, StringComparer.OrdinalIgnoreCase))
                                    entry.Aliases.Add(alias);
                        }
                        if (def.Required.Count > 0)
                        {
                            foreach (var r in def.Required)
                                if (!entry.Required.Contains(r, StringComparer.OrdinalIgnoreCase))
                                    entry.Required.Add(r);
                        }
                        if (def.Optional.Count > 0)
                        {
                            foreach (var o in def.Optional)
                                if (!entry.Optional.Contains(o, StringComparer.OrdinalIgnoreCase))
                                    entry.Optional.Add(o);
                        }
                    }

                    // ── Load sequences ────────────────────────────────────────
                    foreach (var seq in catalog.Sequences)
                    {
                        if (!string.IsNullOrWhiteSpace(seq.SequenceId))
                            _sequences[seq.SequenceId] = seq;
                    }

                    // ── Load LLM alias mappings ──────────────────────────────
                    if (catalog.LlmGuidance?.LegacyMappings != null)
                    {
                        foreach (var kvp in catalog.LlmGuidance.LegacyMappings)
                            _aliasMappings[kvp.Key] = kvp.Value;
                    }
                }
            }

            // ── Load examples and metadata overlays ──────────────────────────
            // ── Load examples overlay ────────────────────────────────────────
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

            // ── Load metadata overlay ────────────────────────────────────────
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
                    ReadCapabilityMetadata(obj, entry);
                }
            }

            foreach (var entry in _entries.Values)
            {
                if (string.IsNullOrWhiteSpace(entry.RiskLevel) || entry.RiskLevel.Equals("controlled", StringComparison.OrdinalIgnoreCase))
                    entry.RiskLevel = InferRisk(entry.Name, entry.Category);
                entry.RequiresApproval = !entry.RiskLevel.Equals("safe", StringComparison.OrdinalIgnoreCase);
                entry.HandlerAvailable = entry.HandlerAvailable && _supportedActions.Contains(entry.Name);
            }
        }
        catch (Exception ex)
        {
            WarningSink?.Invoke($"[ActionManifestService] Failed to load catalog metadata: {ex.Message}");
        }
    }

    static IReadOnlyList<string> LoadSupportedActionNames()
    {
        var path = Path.Combine(ConfigLoader.ConfigRoot, "actions_catalog.json");
        if (!File.Exists(path))
            return Array.Empty<string>();

        using var document = JsonDocument.Parse(File.ReadAllText(path));
        if (!document.RootElement.TryGetProperty("registered", out var registered) ||
            registered.ValueKind != JsonValueKind.Array)
            return Array.Empty<string>();

        var metadata = document.RootElement.TryGetProperty("metadata", out var metadataElement) &&
                       metadataElement.ValueKind == JsonValueKind.Object
            ? metadataElement
            : default;

        var supported = new List<string>();
        foreach (var item in registered.EnumerateArray())
        {
            var name = item.GetString();
            if (string.IsNullOrWhiteSpace(name))
                continue;

            if (metadata.ValueKind == JsonValueKind.Object &&
                metadata.TryGetProperty(name, out var entry) &&
                entry.ValueKind == JsonValueKind.Object &&
                entry.TryGetProperty("handlerAvailable", out var available) &&
                available.ValueKind is JsonValueKind.True or JsonValueKind.False &&
                !available.GetBoolean())
                continue;

            supported.Add(name);
        }
        return supported;
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

        // Check entry-level aliases.
        var alias = _entries.Values.FirstOrDefault(e => e.Aliases.Any(a => a.Equals(trimmed, StringComparison.OrdinalIgnoreCase)));
        if (alias != null) return alias.Name;

        // Check LLM legacy mappings.
        if (_aliasMappings.TryGetValue(trimmed, out var canonical))
            return canonical;

        return trimmed;
    }

    /// <summary>
    /// Normalize an action name using alias mappings. Drop-in replacement for
    /// the former <c>ActionRegistry.Normalize</c>.
    /// </summary>
    public string Normalize(string name) => NormalizeActionName(name);

    /// <summary>
    /// Returns true when the action is registered or known via alias.
    /// Drop-in replacement for the former <c>ActionRegistry.IsKnown</c>.
    /// </summary>
    public bool IsKnown(string actionName)
    {
        if (string.IsNullOrWhiteSpace(actionName))
            return false;
        var normalized = Normalize(actionName);
        return _entries.ContainsKey(normalized) ||
               (LiveSystemEntriesEnabled && LiveSystemRegistryService.TryGet(normalized, out _));
    }

    /// <summary>Try to get an action entry by id or alias.</summary>
    public bool TryGetAction(string actionId, out ActionManifestEntry? entry)
    {
        AddLiveSystemEntries();
        if (_entries.TryGetValue(actionId, out entry))
            return true;
        var normalized = Normalize(actionId);
        return _entries.TryGetValue(normalized, out entry);
    }

    /// <summary>Try to get a sequence definition by id.</summary>
    public bool TryGetSequence(string sequenceId, out SequenceDefinition? sequence)
    {
        if (string.IsNullOrWhiteSpace(sequenceId))
        {
            sequence = null;
            return false;
        }
        return _sequences.TryGetValue(sequenceId, out sequence);
    }

    /// <summary>
    /// Build an action string from a template and parameters.
    /// Drop-in replacement for the former <c>ActionRegistry.BuildActionString</c>.
    /// </summary>
    public static string BuildActionString(string action, Dictionary<string, JsonElement> parameters)
    {
        if (string.IsNullOrWhiteSpace(action))
            return string.Empty;

        if (parameters == null || parameters.Count == 0)
            return action.Trim();

        var parts = new List<string>();
        foreach (var kv in parameters)
        {
            var value = kv.Value.ValueKind switch
            {
                JsonValueKind.String => kv.Value.GetString() ?? "",
                JsonValueKind.Number => kv.Value.GetRawText(),
                JsonValueKind.True => "true",
                JsonValueKind.False => "false",
                JsonValueKind.Null => "",
                _ => kv.Value.GetRawText()
            };
            if (!string.IsNullOrWhiteSpace(value))
                parts.Add($"{kv.Key}={value}");
        }

        return parts.Count > 0 ? $"{action.Trim()}:{string.Join("|", parts)}" : action.Trim();
    }

    /// <summary>
    /// Resolve a sequence step to a full action string using catalog definitions.
    /// Drop-in replacement for the former <c>ActionRegistry.TryResolveStepToActionString</c>.
    /// </summary>
    public bool TryResolveStepToActionString(SequenceStep step, out string actionString)
    {
        actionString = string.Empty;

        if (!string.IsNullOrWhiteSpace(step.ActionId) && _entries.TryGetValue(step.ActionId, out var entry))
        {
            var mergedParams = new Dictionary<string, JsonElement>(entry.Params);
            if (step.Params != null)
            {
                foreach (var kv in step.Params)
                    mergedParams[kv.Key] = kv.Value;
            }
            actionString = BuildActionString(
                !string.IsNullOrWhiteSpace(entry.Action) ? entry.Action : entry.Name,
                mergedParams);
            return true;
        }

        if (!string.IsNullOrWhiteSpace(step.Action))
        {
            actionString = BuildActionString(step.Action, step.Params ?? new Dictionary<string, JsonElement>());
            return true;
        }

        return false;
    }

    public OperationResult Validate(EventActionDefinition action)
    {
        var actionString = action.ToActionString();
        if (string.IsNullOrWhiteSpace(actionString))
            return OperationResult.Fail("Action definition is empty.");

        var (parsedType, parsedParams) = ActionStringParser.Parse(actionString);
        var normalized = ActionParameterConverter.Normalize(parsedType, parsedParams);

        var type = action.Type;
        if (string.IsNullOrWhiteSpace(type))
            type = normalized.ActionName;

        type = NormalizeActionName(type);
        if (!_entries.TryGetValue(type, out var entry) &&
            LiveSystemEntriesEnabled &&
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
        if (!LiveSystemEntriesEnabled)
            return;

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

    static void ReadCapabilityMetadata(JsonElement obj, ActionManifestEntry entry)
    {
        if (obj.TryGetProperty("isMutating", out var mutating) &&
            (mutating.ValueKind == JsonValueKind.True || mutating.ValueKind == JsonValueKind.False))
            entry.IsMutating = mutating.GetBoolean();
        if (obj.TryGetProperty("isReversible", out var reversible) &&
            (reversible.ValueKind == JsonValueKind.True || reversible.ValueKind == JsonValueKind.False))
            entry.IsReversible = reversible.GetBoolean();
        if (obj.TryGetProperty("isIdempotent", out var idempotent) &&
            (idempotent.ValueKind == JsonValueKind.True || idempotent.ValueKind == JsonValueKind.False))
            entry.IsIdempotent = idempotent.GetBoolean();
        if (obj.TryGetProperty("executable", out var executable) &&
            (executable.ValueKind == JsonValueKind.True || executable.ValueKind == JsonValueKind.False))
            entry.Executable = executable.GetBoolean();
        if (obj.TryGetProperty("clientRequired", out var clientReq) &&
            (clientReq.ValueKind == JsonValueKind.True || clientReq.ValueKind == JsonValueKind.False))
            entry.ClientRequired = clientReq.GetBoolean();
        if (obj.TryGetProperty("usesNativeReplication", out var nativeRep) &&
            (nativeRep.ValueKind == JsonValueKind.True || nativeRep.ValueKind == JsonValueKind.False))
            entry.UsesNativeReplication = nativeRep.GetBoolean();
        if (obj.TryGetProperty("availability", out var avail) && avail.ValueKind == JsonValueKind.String)
            entry.Availability = avail.GetString() ?? entry.Availability;

        // Derive AllowedSources from the "permission" string when present.
        if (obj.TryGetProperty("permission", out var perm) && perm.ValueKind == JsonValueKind.String)
        {
            entry.AllowedSources = perm.GetString()?.ToLowerInvariant() switch
            {
                "any" or "all" => ActionSourcePermissions.All,
                "admin" => ActionSourcePermissions.Admin | ActionSourcePermissions.System,
                "admin_approval" => ActionSourcePermissions.Admin | ActionSourcePermissions.System,
                "player" => ActionSourcePermissions.Admin | ActionSourcePermissions.Player | ActionSourcePermissions.System,
                "system" => ActionSourcePermissions.System,
                _ => entry.AllowedSources,
            };
        }

        // Derive AllowedPhases from "eventAllowed" boolean.
        if (obj.TryGetProperty("eventAllowed", out var eventAllowed) &&
            (eventAllowed.ValueKind == JsonValueKind.True || eventAllowed.ValueKind == JsonValueKind.False))
        {
            if (!eventAllowed.GetBoolean())
                entry.AllowedPhases = SessionPhaseAllowance.None;
        }
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
