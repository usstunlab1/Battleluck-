using System.Globalization;
using System.Text.RegularExpressions;
using BattleLuck.Models;
using BattleLuck.Services.Flow;
using BattleLuck.Services.Runtime;
using Unity.Entities;
using Unity.Mathematics;

namespace BattleLuck.Services.AI;

/// <summary>
/// Resolves imperative <c>.ai <text></c> requests against the canonical
/// action manifest. The router never asks an LLM to execute ECS code: it builds
/// one fixed catalog action, validates it, binds it to an active session, and
/// requires player-owned confirmation before any mutation runs.
///
/// Resolution order:
/// 1. Parse operation verb.
/// 2. Parse object category.
/// 3. Parse requested object name.
/// 4. Resolve the object through the correct catalog.
/// 5. Select an action compatible with that object category.
/// 6. Validate that the resolved target matches the requested name.
/// 7. Create an immutable proposal.
/// 8. Preview.
/// 9. Confirm the exact proposal ID.
/// 10. Execute and clear the proposal.
/// </summary>
public static class NaturalLanguageActionRouter
{
    static readonly Regex KeyValuePattern = new(
        "\\b(?<key>[A-Za-z][A-Za-z0-9_.-]*)\\s*=\\s*(?<value>\"[^\"]+\"|'[^']+'|[^\\s|]+)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    static readonly Regex PrefabPattern = new(
        @"\bCHAR_[A-Za-z0-9_]+\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    // Category keywords that determine the object type
    static readonly Dictionary<string, string> CategoryKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        { "floor", "floor_schematic" },
        { "floors", "floor_schematic" },
        { "tile", "floor_schematic" },
        { "tiles", "floor_schematic" },
        { "schematic", "schematic" },
        { "schematics", "schematic" },
        { "blueprint", "schematic" },
        { "blueprints", "schematic" },
        { "boss", "boss" },
        { "bosses", "boss" },
        { "vblood", "vblood" },
        { "v blood", "vblood" },
        { "v-blood", "vblood" },
        { "npc", "npc" },
        { "npcs", "npc" },
        { "creature", "npc" },
        { "creatures", "npc" },
        { "mob", "npc" },
        { "mobs", "npc" },
    };

    // Verb-to-action mappings for each category
    static readonly Dictionary<string, string> CategoryActions = new(StringComparer.OrdinalIgnoreCase)
    {
        { "boss", "spawn.boss" },
        { "vblood", "spawn.boss" },
        { "npc", "spawn.npc" },
        { "schematic", "schematic.loadatpos" },
        { "floor_schematic", "schematic.loadatpos" },
    };

    // Verbs that imply spawn/place actions
    static readonly HashSet<string> SpawnVerbs = new(StringComparer.OrdinalIgnoreCase)
    {
        "spawn", "summon", "place", "put", "create", "build", "add"
    };

    static readonly HashSet<string> SearchVerbs = new(StringComparer.OrdinalIgnoreCase)
    {
        "search", "find", "list", "show"
    };

    static readonly HashSet<string> ImperativeTerms = new(StringComparer.OrdinalIgnoreCase)
    {
        "add", "apply", "assign", "build", "change", "clear", "create", "delete",
        "despawn", "disable", "do", "enable", "end", "execute", "give", "grant",
        "kill", "move", "pause", "place", "remove", "rename", "reset", "resume",
        "revive", "run", "set", "spawn", "start", "stop", "summon", "swap",
        "teleport", "update", "search", "find", "list", "show"
    };

    public static bool TryHandle(ulong steamId, string query)
    {
        if (steamId == 0 || string.IsNullOrWhiteSpace(query) || !VRisingCore.IsReady)
            return false;

        var manifest = ActionManifestService.Instance;

        // Step 1-2: Parse the request to extract verb, category, and target name
        var parsed = ParseRequest(query);

        // Handle search requests
        if (parsed.verb != null && SearchVerbs.Contains(parsed.verb))
        {
            return HandleSearch(steamId, parsed, manifest);
        }

        // Step 3-4: Resolve through TryResolveEntry (existing logic for action name)
        if (!TryResolveEntry(query, manifest, out var entry, out var resolutionError))
        {
            if (!string.IsNullOrWhiteSpace(resolutionError))
            {
                Reply(steamId, resolutionError);
                return true;
            }
            return false;
        }

        if (!IsAdmin(steamId))
        {
            Reply(steamId, $"Action '{entry.Name}' requires server-admin permission.");
            return true;
        }

        if (!entry.HandlerAvailable || !entry.IsServerContractValid)
        {
            Reply(steamId, entry.ServerContractViolationReason ??
                $"Action '{entry.Name}' has no server-side handler.");
            return true;
        }

        // Step 5: Validate that the selected action is compatible with the detected category
        if (parsed.category != null && parsed.verb != null && SpawnVerbs.Contains(parsed.verb))
        {
            var actionCategory = InferObjectCategory(entry.Name);
            if (actionCategory != null && !actionCategory.Equals(parsed.category, StringComparison.OrdinalIgnoreCase))
            {
                Reply(steamId,
                    $"Action '{entry.Name}' is for {actionCategory}, not {parsed.category}. " +
                    $"Use a {parsed.category}-specific action.");
                return true;
            }
        }

        var character = ResolveCharacter(steamId);
        var session = ResolveSession(steamId, query);
        if (character == Entity.Null || session == null)
        {
            Reply(steamId, "Catalog actions need one active event session so spawned entities and rollback state have an owner.");
            return true;
        }

        if (!TryBuildAction(query, entry, character, session, out var action, out var buildError))
        {
            Reply(steamId, buildError);
            return true;
        }

        if (!TryCreateExecutionContext(character, session, out var executor, out var context))
        {
            Reply(steamId, "The active event context is not ready for actions.");
            return true;
        }

        var validator = new LlmRuntimeActionValidator(manifest);
        var validation = validator.ValidateAction(action, context, session.Context.SessionId);
        if (!validation.Success)
        {
            Reply(steamId, $"Action preview rejected: {validation.Error}");
            return true;
        }

        var summary = Summarize(action);

        // Step 6: Request-result validation guard
        if (parsed.targetName != null)
        {
            var resolvedPrefab = ExtractResolvedPrefab(action);
            if (resolvedPrefab != null && !TargetMatches(parsed.targetName, resolvedPrefab, parsed.category))
            {
                BattleLuckPlugin.LogWarning(
                    $"[AIResolver] Rejected target mismatch: " +
                    $"request=\"{parsed.targetName}\" resolved=\"{resolvedPrefab}\" action=\"{entry.Name}\"");
                Reply(steamId,
                    $"I could not resolve '{parsed.targetName}' to one registered {parsed.category ?? "entity"}. " +
                    $"Use .ai search {parsed.category ?? "boss"} {parsed.targetName}.");
                return true;
            }
        }

        // Step 7-8: Create immutable proposal and preview
        if (!entry.IsMutating && !entry.RequiresApproval &&
            entry.RiskLevel.Equals("safe", StringComparison.OrdinalIgnoreCase))
        {
            var immediate = executor.ExecuteViaRuntime(action, context);
            Reply(steamId, immediate.Success ? $"Executed: {summary}." : $"Action failed: {immediate.Error}");
            return true;
        }

        var boundSessionId = session.Context.SessionId;

        // Create the immutable proposal
        var resolvedCategory = parsed.category ?? InferObjectCategory(entry.Name) ?? "unknown";
        var resolvedName = ExtractResolvedPrefab(action) ?? entry.Name;
        var resolvedGuid = ResolvePrefabGuidFromAction(action);
        var arguments = BuildArgumentsDictionary(action);
        var proposal = ActionProposalStore.CreateProposal(
            steamId,
            query,
            entry.Name,
            resolvedCategory,
            resolvedName,
            resolvedGuid,
            arguments,
            requiresApproval: true);

        Reply(steamId,
            $"Ready: {summary}.\n" +
            $"Proposal: {proposal.ProposalId}\n" +
            $"Confirm: .ai yes {proposal.ProposalId} | Cancel: .ai cancel {proposal.ProposalId}");
        return true;
    }

    /// <summary>
    /// Parse a natural language request to extract verb, category, and target name.
    /// </summary>
    sealed class ParsedRequest
    {
        public string? verb;
        public string? category;
        public string? targetName;
        public string? rawQuery;
    }

    static ParsedRequest ParseRequest(string query)
    {
        var result = new ParsedRequest { rawQuery = query };
        var trimmed = query.Trim();
        var tokens = trimmed.Split(new[] { ' ', '\t', ',', ';' },
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (tokens.Length == 0)
            return result;

        // Identify the verb (first word if it's an imperative or spawn verb)
        var firstToken = tokens[0].ToLowerInvariant();
        if (ImperativeTerms.Contains(firstToken))
            result.verb = firstToken;

        // Search for category keywords in the remaining tokens
        for (int i = 1; i < tokens.Length; i++)
        {
            var token = tokens[i].ToLowerInvariant();
            // Handle multi-word categories like "v blood"
            if (token == "v" && i + 1 < tokens.Length && tokens[i + 1].ToLowerInvariant() == "blood")
            {
                result.category = "vblood";
                // The target name is whatever follows "v blood"
                if (i + 2 < tokens.Length)
                    result.targetName = string.Join(" ", tokens.Skip(i + 2));
                return result;
            }

            if (CategoryKeywords.TryGetValue(token, out var category))
            {
                result.category = category;
                // Everything after the category keyword is the target name
                if (i + 1 < tokens.Length)
                    result.targetName = string.Join(" ", tokens.Skip(i + 1));
                return result;
            }
        }

        // If no category keyword found, treat the last token(s) as potential target
        if (tokens.Length > 1 && result.verb != null)
        {
            result.targetName = string.Join(" ", tokens.Skip(1));
        }

        return result;
    }

    /// <summary>
    /// Handle a search request by looking up targets in the appropriate catalog.
    /// </summary>
    static bool HandleSearch(ulong steamId, ParsedRequest parsed, ActionManifestService manifest)
    {
        if (string.IsNullOrWhiteSpace(parsed.category))
        {
            Reply(steamId, "Search what? Try: .ai search boss dracula, .ai search schematic floor, .ai search npc bandit");
            return true;
        }

        var searchTerm = parsed.targetName ?? "";

        switch (parsed.category)
        {
            case "boss":
            case "vblood":
                var bossResult = CategoryResolvers.ResolveBoss(searchTerm);
                if (bossResult.Kind == CategoryResolvers.ResolutionKind.Resolved)
                {
                    Reply(steamId, $"Found: {bossResult.CanonicalName}. Use .ai spawn {parsed.category} {searchTerm}");
                }
                else if (bossResult.Kind == CategoryResolvers.ResolutionKind.Ambiguous)
                {
                    Reply(steamId, $"Multiple matches: {string.Join(", ", bossResult.Choices)}");
                }
                else
                {
                    Reply(steamId, $"No {parsed.category} found matching '{searchTerm}'.");
                }
                return true;

            case "schematic":
            case "floor_schematic":
                var schematicResult = parsed.category == "floor_schematic"
                    ? CategoryResolvers.ResolveFloorSchematic(searchTerm)
                    : CategoryResolvers.ResolveSchematic(searchTerm);
                if (schematicResult.Kind == CategoryResolvers.ResolutionKind.Resolved)
                {
                    Reply(steamId, $"Found schematic: {schematicResult.CanonicalName}.");
                }
                else if (schematicResult.Kind == CategoryResolvers.ResolutionKind.Ambiguous)
                {
                    var choices = string.Join(", ", schematicResult.Choices);
                    Reply(steamId, $"Multiple schematics match '{searchTerm}': {choices}");
                }
                else
                {
                    Reply(steamId, $"No schematic found matching '{searchTerm}'.");
                }
                return true;

            case "npc":
                var npcResult = CategoryResolvers.ResolveNpc(searchTerm);
                if (npcResult.Kind == CategoryResolvers.ResolutionKind.Resolved)
                {
                    Reply(steamId, $"Found NPC: {npcResult.CanonicalName}.");
                }
                else
                {
                    Reply(steamId, $"No NPC found matching '{searchTerm}'.");
                }
                return true;
        }

        Reply(steamId, $"Unknown category '{parsed.category}'. Try: boss, npc, schematic.");
        return true;
    }

    /// <summary>
    /// Infer the object category from an action name.
    /// </summary>
    static string? InferObjectCategory(string actionName)
    {
        if (actionName.Contains("boss", StringComparison.OrdinalIgnoreCase) ||
            actionName.Contains("vblood", StringComparison.OrdinalIgnoreCase))
            return "boss";
        if (actionName.Contains("npc", StringComparison.OrdinalIgnoreCase))
            return "npc";
        if (actionName.Contains("schematic", StringComparison.OrdinalIgnoreCase))
            return "schematic";
        return null;
    }

    /// <summary>
    /// Extract the resolved prefab name from a built action string.
    /// </summary>
    static string? ExtractResolvedPrefab(string action)
    {
        var (_, parameters) = FlowActionExecutor.ParseActionString(action);
        foreach (var key in new[] { "prefab", "prefabName", "schematicId", "eventName" })
        {
            if (parameters.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value))
                return value;
        }
        return null;
    }

    /// <summary>
    /// Extract the prefab GUID from a built action string.
    /// </summary>
    static int ResolvePrefabGuidFromAction(string action)
    {
        var prefab = ExtractResolvedPrefab(action);
        if (prefab == null)
            return 0;

        if (int.TryParse(prefab, out var hash))
            return hash;

        try
        {
            if (PrefabHelper.TryGetPrefabGuid(prefab, out var guid))
                return guid.GuidHash;
        }
        catch { }

        return 0;
    }

    /// <summary>
    /// Build an arguments dictionary from a serialized action string.
    /// </summary>
    static Dictionary<string, string> BuildArgumentsDictionary(string action)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var (name, parameters) = FlowActionExecutor.ParseActionString(action);
        result["actionName"] = name;
        foreach (var kvp in parameters)
            result[kvp.Key] = kvp.Value;
        return result;
    }

    /// <summary>
    /// Check whether the requested target name reasonably matches the resolved prefab.
    /// Uses the same resolution priority as CategoryResolvers.
    /// </summary>
    static bool TargetMatches(string requestedName, string resolvedPrefab, string? category)
    {
        if (string.IsNullOrWhiteSpace(requestedName) || string.IsNullOrWhiteSpace(resolvedPrefab))
            return true; // Can't validate, don't block

        // Try category-specific resolution
        var resolution = category switch
        {
            "boss" or "vblood" => CategoryResolvers.ResolveBoss(requestedName),
            "npc" => CategoryResolvers.ResolveNpc(requestedName),
            "schematic" or "floor_schematic" => CategoryResolvers.ResolveSchematic(requestedName),
            _ => null
        };

        if (resolution != null && resolution.Kind == CategoryResolvers.ResolutionKind.Resolved)
        {
            return resolution.CanonicalName.Equals(resolvedPrefab, StringComparison.OrdinalIgnoreCase);
        }

        // Fall back to simple string matching
        return requestedName.Contains(resolvedPrefab, StringComparison.OrdinalIgnoreCase) ||
               resolvedPrefab.Contains(requestedName, StringComparison.OrdinalIgnoreCase);
    }

    internal static bool TryResolveEntry(
        string query,
        ActionManifestService manifest,
        out ActionManifestEntry entry,
        out string error)
    {
        entry = null!;
        error = string.Empty;
        var candidateText = StripExecutionPrefix(query.Trim());
        var (explicitName, _) = FlowActionExecutor.ParseActionString(candidateText);
        var canonical = manifest.NormalizeActionName(explicitName);
        if (!string.IsNullOrWhiteSpace(canonical) && manifest.Entries.TryGetValue(canonical, out entry!))
            return true;

        var tokens = candidateText
            .Split(new[] { ' ', '\t', ',', ';', ':', '|', '.', '_', '-' },
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (!tokens.Any(ImperativeTerms.Contains))
            return false;

        var exactAlias = manifest.Entries.Values.FirstOrDefault(item =>
            item.Aliases.Any(alias => candidateText.Contains(alias, StringComparison.OrdinalIgnoreCase)));
        if (exactAlias != null)
        {
            entry = exactAlias;
            return true;
        }

        var matches = manifest.Search(candidateText, 3)
            .Where(match => match.Score >= 10)
            .ToArray();
        if (matches.Length == 0)
            return false;

        if (matches.Length > 1 && matches[0].Score == matches[1].Score &&
            !matches[0].Name.Equals(matches[1].Name, StringComparison.OrdinalIgnoreCase))
        {
            error = "Action description is ambiguous. Try one canonical name: " +
                    string.Join(", ", matches.Take(3).Select(match => match.Name)) + ".";
            return false;
        }

        return manifest.Entries.TryGetValue(matches[0].Name, out entry!);
    }

    static bool TryBuildAction(
        string query,
        ActionManifestEntry entry,
        Entity character,
        ActiveSession session,
        out string action,
        out string error)
    {
        action = string.Empty;
        error = string.Empty;
        var candidateText = StripExecutionPrefix(query.Trim());
        var (explicitName, explicitParameters) = FlowActionExecutor.ParseActionString(candidateText);
        var explicitCanonical = ActionManifestService.Instance.NormalizeActionName(explicitName);
        var parameters = explicitCanonical.Equals(entry.Name, StringComparison.OrdinalIgnoreCase)
            ? new Dictionary<string, string>(explicitParameters, StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, string>(entry.Defaults, StringComparer.OrdinalIgnoreCase);

        foreach (Match match in KeyValuePattern.Matches(candidateText))
        {
            var key = match.Groups["key"].Value;
            var value = match.Groups["value"].Value.Trim('"', '\'');
            if (entry.Required.Contains(key, StringComparer.OrdinalIgnoreCase) ||
                entry.Optional.Contains(key, StringComparer.OrdinalIgnoreCase) ||
                entry.Defaults.ContainsKey(key))
            {
                parameters[key] = value;
            }
        }

        var wantsRandom = candidateText.Contains("random", StringComparison.OrdinalIgnoreCase) ||
                          candidateText.Contains("randum", StringComparison.OrdinalIgnoreCase);

        if (entry.Name.Equals("spawn.boss", StringComparison.OrdinalIgnoreCase))
        {
            if (!parameters.TryGetValue("prefab", out var prefab) || string.IsNullOrWhiteSpace(prefab))
            {
                var prefabMatch = PrefabPattern.Match(candidateText);
                if (prefabMatch.Success)
                {
                    prefab = prefabMatch.Value;
                }
                else
                {
                    // Try to resolve the target name through CategoryResolvers
                    var parsed = ParseRequest(candidateText);
                    if (parsed.targetName != null)
                    {
                        var bossResult = CategoryResolvers.ResolveBoss(parsed.targetName);
                        if (bossResult.Kind == CategoryResolvers.ResolutionKind.Resolved)
                        {
                            prefab = bossResult.CanonicalName;
                        }
                        else if (bossResult.Kind == CategoryResolvers.ResolutionKind.Ambiguous)
                        {
                            error = $"Multiple bosses match '{parsed.targetName}': {string.Join(", ", bossResult.Choices)}";
                            return false;
                        }
                        else
                        {
                            error = $"'{parsed.targetName}' is not registered in the local prefab catalog. Use .ai search boss {parsed.targetName}.";
                            return false;
                        }
                    }
                    else
                    {
                        // No target specified - return ambiguity
                        error = "Which boss should I spawn? Use .ai search boss <name> or specify a boss name.";
                        return false;
                    }
                }

                if (!string.IsNullOrWhiteSpace(prefab))
                    parameters["prefab"] = prefab;
            }

            if (!parameters.ContainsKey("bossId"))
                parameters["bossId"] = $"ai_boss_{Guid.NewGuid():N}"[..15];
        }

        if (entry.Name.Equals("spawn.npc", StringComparison.OrdinalIgnoreCase))
        {
            if (!parameters.TryGetValue("prefab", out var prefab) || string.IsNullOrWhiteSpace(prefab))
            {
                var prefabMatch = PrefabPattern.Match(candidateText);
                if (prefabMatch.Success)
                {
                    prefab = prefabMatch.Value;
                }
                else
                {
                    // Try to resolve through CategoryResolvers
                    var parsed = ParseRequest(candidateText);
                    if (parsed.targetName != null)
                    {
                        var npcResult = CategoryResolvers.ResolveNpc(parsed.targetName);
                        if (npcResult.Kind == CategoryResolvers.ResolutionKind.Resolved)
                        {
                            prefab = npcResult.CanonicalName;
                        }
                        else
                        {
                            error = $"'{parsed.targetName}' is not registered as an NPC. Use .ai search npc {parsed.targetName}.";
                            return false;
                        }
                    }
                }

                if (!string.IsNullOrWhiteSpace(prefab))
                    parameters["prefab"] = prefab;
            }
        }

        if (wantsRandom &&
            (entry.Name.Equals("spawn.boss", StringComparison.OrdinalIgnoreCase) ||
             entry.Required.Contains("position", StringComparer.OrdinalIgnoreCase) ||
             entry.Optional.Contains("position", StringComparer.OrdinalIgnoreCase)))
        {
            parameters["position"] = FormatPosition(RandomPosition(character, session));
        }

        var missing = entry.Required
            .Where(name => !parameters.TryGetValue(name, out var value) || string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (missing.Length > 0)
        {
            var example = entry.Examples.FirstOrDefault();
            error = $"Action '{entry.Name}' needs: {string.Join(", ", missing)}." +
                    (string.IsNullOrWhiteSpace(example) ? string.Empty : $" Example: .ai execute {example}");
            return false;
        }

        var ordered = entry.Required
            .Concat(parameters.Keys.OrderBy(key => key, StringComparer.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(parameters.ContainsKey)
            .Select(key => $"{key}={parameters[key]}")
            .ToArray();
        action = ordered.Length == 0 ? entry.Name : $"{entry.Name}:{string.Join("|", ordered)}";
        return true;
    }

    static (bool ok, string message) ExecuteConfirmed(
        ulong steamId,
        string sessionId,
        string action,
        ActionManifestService manifest)
    {
        if (!IsAdmin(steamId))
            return (false, "Admin permission was revoked before confirmation.");

        var character = ResolveCharacter(steamId);
        var session = BattleLuckPlugin.Session?.ActiveSessions.Values.FirstOrDefault(active =>
            active.Context.SessionId.Equals(sessionId, StringComparison.Ordinal));
        if (character == Entity.Null || session == null)
            return (false, "The bound event session ended or the player entity changed. Create a new preview.");

        if (!TryCreateExecutionContext(character, session, out var executor, out var context))
            return (false, "The bound event context is no longer ready.");

        var validation = new LlmRuntimeActionValidator(manifest)
            .ValidateAction(action, context, sessionId);
        if (!validation.Success)
            return (false, validation.Error ?? "Action validation failed after confirmation.");

        var result = executor.ExecuteViaRuntime(action, context);
        return result.Success
            ? (true, $"Executed {Summarize(action)}.")
            : (false, result.Error ?? "Action execution failed.");
    }

    static bool TryCreateExecutionContext(
        Entity character,
        ActiveSession session,
        out FlowActionExecutor executor,
        out FlowActionContext context)
    {
        executor = null!;
        context = null!;
        if (!character.Exists() || session.Context == null)
            return false;

        var playerState = BattleLuckPlugin.PlayerState ?? new PlayerStateController();
        var zone = session.Config.Zones.Zones.FirstOrDefault(item => item.Hash == session.Context.ZoneHash);
        executor = new FlowActionExecutor(playerState, BattleLuckPlugin.GameModes);
        context = new FlowActionContext
        {
            PlayerCharacter = character,
            ZoneHash = session.Context.ZoneHash,
            PlayerState = playerState,
            Registry = BattleLuckPlugin.GameModes,
            Config = session.Config,
            Zone = zone,
            GameContext = session.Context
        };
        return true;
    }

    static ActiveSession? ResolveSession(ulong steamId, string query)
    {
        var sessions = BattleLuckPlugin.Session?.ActiveSessions.Values
            .Where(session => session.Context != null)
            .ToArray() ?? Array.Empty<ActiveSession>();
        if (sessions.Length == 0)
            return null;

        var participant = sessions.FirstOrDefault(session => session.Context.Players.Contains(steamId));
        if (participant != null)
            return participant;

        var named = sessions.Where(session =>
            query.Contains(session.Context.ModeId, StringComparison.OrdinalIgnoreCase) ||
            query.Contains(session.Config.DisplayName ?? string.Empty, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (named.Length == 1)
            return named[0];

        return sessions.Length == 1 ? sessions[0] : null;
    }

    static string PickRandomBossPrefab(ActionManifestEntry entry)
    {
        var candidates = entry.Examples
            .Select(example => FlowActionExecutor.ParseActionString(example).parameters)
            .Where(parameters => parameters.TryGetValue("prefab", out var prefab) &&
                                 !string.IsNullOrWhiteSpace(prefab))
            .Select(parameters => parameters["prefab"])
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(prefab => PrefabHelper.TryGetValidPrefabGuidDeep(prefab, out _))
            .ToArray();
        return candidates.Length == 0 ? string.Empty : candidates[System.Random.Shared.Next(candidates.Length)];
    }

    static float3 RandomPosition(Entity character, ActiveSession session)
    {
        var zone = session.Config.Zones.Zones.FirstOrDefault(item => item.Hash == session.Context.ZoneHash);
        var fallback = character.GetPosition();
        if (zone == null)
            return fallback;

        var center = zone.Position.ToFloat3();
        if (math.lengthsq(center) < 0.0001f)
            center = zone.TeleportSpawn.ToFloat3();
        if (math.lengthsq(center) < 0.0001f)
            center = fallback;

        var radius = Math.Clamp(zone.Radius * 0.65f, 8f, 40f);
        var distance = MathF.Sqrt((float)System.Random.Shared.NextDouble()) * radius;
        var angle = (float)(System.Random.Shared.NextDouble() * Math.PI * 2d);
        return new float3(
            center.x + MathF.Cos(angle) * distance,
            Math.Abs(center.y) > 0.001f ? center.y : fallback.y,
            center.z + MathF.Sin(angle) * distance);
    }

    static string FormatPosition(float3 position) => string.Create(
        CultureInfo.InvariantCulture,
        $"{position.x:F1},{position.y:F1},{position.z:F1}");

    static string StripExecutionPrefix(string value)
    {
        foreach (var prefix in new[] { "execute ", "run ", "do " })
            if (value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return value[prefix.Length..].Trim();
        return value;
    }

    static string Summarize(string action)
    {
        var (name, parameters) = FlowActionExecutor.ParseActionString(action);
        var visible = parameters
            .Where(kv => !kv.Key.Contains("token", StringComparison.OrdinalIgnoreCase) &&
                         !kv.Key.Contains("password", StringComparison.OrdinalIgnoreCase))
            .Take(6)
            .Select(kv => $"{kv.Key}={kv.Value}");
        return parameters.Count == 0 ? name : $"{name} ({string.Join(", ", visible)})";
    }

    static bool IsAdmin(ulong steamId)
    {
        var character = ResolveCharacter(steamId);
        return character != Entity.Null && FlowController.TryGetUser(character, out var user) && user.IsAdmin;
    }

    static Entity ResolveCharacter(ulong steamId)
    {
        if (!VRisingCore.IsReady)
            return Entity.Null;
        return VRisingCore.GetOnlinePlayers()
            .FirstOrDefault(player => player.Exists() && player.IsPlayer() && player.GetSteamId() == steamId);
    }

    static void Reply(ulong steamId, string message)
    {
        var character = ResolveCharacter(steamId);
        if (character != Entity.Null && FlowController.TryGetUser(character, out var user))
            NotificationHelper.NotifyPlayer(user, $"[AI] {message}");
    }
}