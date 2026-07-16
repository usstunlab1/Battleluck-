using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using BattleLuck.Commands.Converters;
using BattleLuck.Core;
using BattleLuck.Models;
using BattleLuck.Services.Flow;

namespace BattleLuck.Services.AI;

public sealed class LiveEventOperatorService
{
    static readonly ConcurrentDictionary<string, OperatorProposal> Pending = new(StringComparer.OrdinalIgnoreCase);
    readonly ActionManifestService _manifest = new();
    readonly LlmRuntimeActionValidator _runtimeValidator = new();
    readonly global::BattleLuck.Services.Runtime.EventDefinitionLoader _loader = new();

    public IReadOnlyCollection<OperatorProposal> PendingOperations => Pending.Values.ToList();

    public List<CatalogActionSearchResult> SearchCatalog(string query, int maxResults = 10) =>
        _manifest.Search(query, maxResults);

    public async Task<OperationResult<string>> ReviewEventAsync(
        AIAssistant aiAssistant,
        ulong requestedBy,
        string modeId,
        string request,
        ActiveSession? session)
    {
        if (string.IsNullOrWhiteSpace(modeId))
            return OperationResult<string>.Fail("Mode id is required.");

        var eventPath = GetEventPath(modeId);
        var eventJson = LoadEventJson(modeId, eventPath);
        var catalogMatches = SearchCatalog(string.IsNullOrWhiteSpace(request) ? "event actions boss wall glow zone cleanup" : request, 25);

        var systemPrompt = string.Join(Environment.NewLine,
            "You are BattleLuck's Live AI Event Operator reviewer.",
            "BattleLuck V Rising server modding, event JSON, action catalogs, configs, bosses, zones, sessions, cleanup, and admin commands are explicitly in scope.",
            "Do not say game server modding is outside your scope. Do not answer as a generic Cloudflare support assistant unless the admin asks specifically about Cloudflare.",
            "Review the event like both a player and a coder.",
            "Focus on practical event quality, missing wiring, unsafe automation, cleanup, catalog action validity, player experience, and rollback safety.",
            "Do not propose uncontrolled recursive self-improvement or automatic code/config mutation.",
            "Every risky change must remain preview-first, admin-approved, validated, audited, and reversible.",
            "Return concise findings and concrete next requests the admin can paste into `.ai event request`.");

        var user = new StringBuilder();
        user.AppendLine($"Requested by SteamId: {requestedBy}");
        user.AppendLine($"Mode: {modeId}");
        user.AppendLine($"Review request: {(string.IsNullOrWhiteSpace(request) ? "general audit" : request)}");
        user.AppendLine();

        if (session?.Context != null)
        {
            user.AppendLine("Live session:");
            user.AppendLine($"sessionId={session.Context.SessionId}");
            user.AppendLine($"zoneHash={session.Context.ZoneHash}");
            user.AppendLine($"started={session.IsStarted}");
            user.AppendLine($"players={session.Context.Players.Count}");
            user.AppendLine($"elapsedSeconds={session.Context.ElapsedSeconds:F1}");
            user.AppendLine();
        }

        user.AppendLine("Relevant catalog matches:");
        foreach (var match in catalogMatches)
            user.AppendLine($"- {match.Name} [{match.Category}/{match.RiskLevel}] {string.Join(" ; ", match.Examples.Take(2))}");

        user.AppendLine();
        user.AppendLine("Current flow.json:");
        user.AppendLine(eventJson.Length > 18000 ? eventJson[..18000] + "\n...<truncated>" : eventJson);

        var response = await aiAssistant.GenerateOperatorReviewAsync(systemPrompt, user.ToString());
        if (string.IsNullOrWhiteSpace(response))
            return OperationResult<string>.Fail("AI returned an empty event review.");

        Audit("review", new OperatorProposal
        {
            OperationId = NewOperationId(),
            ModeId = modeId,
            SessionId = session?.Context?.SessionId ?? "",
            RequestedBy = requestedBy,
            Request = request,
            Reason = "Non-mutating AI event review",
            RiskLevel = "safe",
            Actions = catalogMatches.Select(m => m.Name).Take(20).ToList()
        });

        return OperationResult<string>.Ok(response);
    }

    public async Task<OperationResult<OperatorProposal>> PreviewEventRequestAsync(
        AIAssistant aiAssistant,
        ulong requestedBy,
        string modeId,
        string request,
        ActiveSession? session)
    {
        if (!aiAssistant.EventAuthoringEnabled)
            return OperationResult<OperatorProposal>.Fail("AI event authoring is disabled in ai_config.json.");

        if (string.IsNullOrWhiteSpace(modeId))
            return OperationResult<OperatorProposal>.Fail("Mode id is required.");

        if (string.IsNullOrWhiteSpace(request))
            return OperationResult<OperatorProposal>.Fail("Request is empty.");

        var maxActions = aiAssistant.EventAuthoringMaxActions;
        var eventPath = GetEventPath(modeId);
        var originalJson = LoadEventJson(modeId, eventPath);
        var existingRootMutations = GetNonNotificationRootActions(originalJson);
        var currentConfigs = new Dictionary<string, JsonDocument>(StringComparer.OrdinalIgnoreCase)
        {
            ["flow.json"] = JsonDocument.Parse(originalJson)
        };

        var searchResults = SearchCatalog(request, 20);
        var description = BuildOperatorPrompt(modeId, request, session, searchResults, maxActions);
        var response = await aiAssistant.GenerateConfigEditAsync(description, currentConfigs);

        UnifiedEventDefinition? definition;
        var generatedBy = "LLM";
        if (string.IsNullOrWhiteSpace(response))
        {
            var fallback = BuildLocalCatalogProposal(modeId, request, originalJson, searchResults, maxActions, session);
            if (!fallback.Success || fallback.Value == null)
                return OperationResult<OperatorProposal>.Fail(fallback.Error ?? "External AI returned no event edit and local catalog fallback could not infer actions.");

            definition = fallback.Value;
            generatedBy = "local catalog fallback";
        }
        else
        {
            JsonElement eventElement;
            try
            {
                using var responseDoc = JsonDocument.Parse(ExtractJsonObject(response));
                if (!responseDoc.RootElement.TryGetProperty("flow.json", out eventElement) &&
                    !responseDoc.RootElement.TryGetProperty("event.json", out eventElement))
                {
                    return OperationResult<OperatorProposal>.Fail("AI response did not include flow.json.");
                }
                eventElement = eventElement.Clone();
            }
            catch (Exception ex)
            {
                return OperationResult<OperatorProposal>.Fail($"AI response was not valid JSON: {ex.Message}");
            }

            try
            {
                definition = eventElement.Deserialize<UnifiedEventDefinition>(ConfigLoader.JsonOptions);
            }
            catch (Exception ex)
            {
                return OperationResult<OperatorProposal>.Fail($"AI event JSON could not deserialize: {ex.Message}");
            }
        }

        if (definition == null)
            return OperationResult<OperatorProposal>.Fail("AI event JSON was empty.");

        var newlyIntroducedRootMutations = definition.Actions
            .Select(action => action.ToActionString())
            .Where(action => !IsNotificationOnlyRootAction(action))
            .Where(action => !existingRootMutations.Contains(action))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(4)
            .ToList();
        if (newlyIntroducedRootMutations.Count > 0)
        {
            return OperationResult<OperatorProposal>.Fail(
                "AI event JSON put executable actions at root, where the runtime skips them: " +
                string.Join(", ", newlyIntroducedRootMutations) +
                ". Put them in a phase, timer completion, trigger, or object action list.");
        }

        var validation = new EventValidationResult();
        _loader.Validate(definition, validation, maxActions);
        if (!validation.Success)
            return OperationResult<OperatorProposal>.Fail("AI event JSON failed validation: " + string.Join("; ", validation.Errors));

        var proposedJson = JsonSerializer.Serialize(definition, ConfigLoader.JsonOptions);
        var actions = ExtractActions(definition).ToList();
        var proposal = new OperatorProposal
        {
            OperationId = NewOperationId(),
            ModeId = modeId,
            SessionId = session?.Context?.SessionId ?? "",
            RequestedBy = requestedBy,
            Request = request,
            Reason = $"{generatedBy} event request for {modeId}; {actions.Count} action(s) in proposed event.",
            RiskLevel = DetermineRisk(actions),
            Actions = actions.Take(1000).ToList(),
            JsonDiff = BuildDiffSummary(originalJson, proposedJson, actions),
            EventPath = eventPath,
            OriginalJson = originalJson,
            ProposedJson = proposedJson,
            ExpiresAtUtc = DateTime.UtcNow.AddMinutes(Math.Clamp(ConfigLoader.LoadAIConfig().EventAuthoring.PendingOperationMinutes, 1, 240))
        };

        Pending[proposal.OperationId] = proposal;
        Audit("preview", proposal);
        return OperationResult<OperatorProposal>.Ok(proposal);
    }

    OperationResult<UnifiedEventDefinition> BuildLocalCatalogProposal(
        string modeId,
        string request,
        string originalJson,
        List<CatalogActionSearchResult> matches,
        int maxActions,
        ActiveSession? session)
    {
        UnifiedEventDefinition? definition;
        try
        {
            definition = JsonSerializer.Deserialize<UnifiedEventDefinition>(originalJson, ConfigLoader.JsonOptions);
        }
        catch (Exception ex)
        {
            return OperationResult<UnifiedEventDefinition>.Fail($"Existing flow.json could not be parsed for local fallback: {ex.Message}");
        }

        definition ??= new UnifiedEventDefinition();
        if (string.IsNullOrWhiteSpace(definition.Metadata.Id))
            definition.Metadata.Id = modeId;
        if (string.IsNullOrWhiteSpace(definition.Metadata.DisplayName))
            definition.Metadata.DisplayName = modeId;
        definition.Metadata.Enabled = true;

        var remaining = Math.Max(0, Math.Min(maxActions, 1000) - EventDefinitionLoader.CountActions(definition));
        if (remaining <= 0)
            return OperationResult<UnifiedEventDefinition>.Fail($"Event already has the maximum allowed {maxActions} actions.");

        var originalActionCount = EventDefinitionLoader.CountActions(definition);
        ApplyLocalRulesAndTimers(definition, request, modeId);
        var selected = SelectLocalActionExamples(request, matches, remaining, session);
        if (selected.Count == 0 && EventDefinitionLoader.CountActions(definition) == originalActionCount)
            selected.Add(BuildGeneratedAnnounceAction(request));

        foreach (var action in selected)
            definition.Actions.Add(new EventActionDefinition { Action = action });

        return OperationResult<UnifiedEventDefinition>.Ok(definition);
    }

    List<string> SelectLocalActionExamples(
        string request,
        List<CatalogActionSearchResult> matches,
        int remaining,
        ActiveSession? session)
    {
        var selected = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var lower = request.ToLowerInvariant();

        void Add(string action)
        {
            if (selected.Count >= remaining || string.IsNullOrWhiteSpace(action) || !seen.Add(action))
                return;

            if (!IsSafeTopLevelFallbackAction(action))
                return;

            var validation = _manifest.Validate(new EventActionDefinition { Action = action });
            if (validation.Success)
                selected.Add(action);
        }

        if (lower.Contains("announce") || lower.Contains("message") || lower.Contains("timer") || lower.Contains("start"))
            Add("announce:title=AI Update|message=Event update prepared by local AI fallback.|color=#5CC8FF|level=info");
        if (lower.Contains("boss"))
            Add("npc.spawn:prefab=CHAR_Manticore_VBlood|npcId=boss1|position=-2008,5,-2800|homeRadius=40");
        if (lower.Contains("wall") || lower.Contains("border") || lower.Contains("floor") || lower.Contains("tile") || lower.Contains("carpet"))
            Add("announce:title=Arena|message=All event geometry is loaded and cleared through tracked schematics.|color=#5CC8FF|level=info");
        if (lower.Contains("build") || lower.Contains("palette") || lower.Contains("kindredschematics"))
        {
            Add("build.search:filter=castle floor");
            Add("build.spawn:prefab=TM_Castle_Floor_Tier02_Stone|position=-2000,5,-2800|group=manual_build");
        }
        if (lower.Contains("schematic") || lower.Contains("castle"))
            Add(lower.Contains("clear") || lower.Contains("delete") || lower.Contains("remove")
                ? "schematic.clear.radius:position=-2000,5,-2800|radius=60"
                : "schematic.loadat:eventName=castle_design_template|position=-2000,5,-2800|clearRadius=30|spawnItems=true");
        if (lower.Contains("stun"))
            Add("player.buff.apply:buffPrefab=Buff_General_Stun|duration=3");
        if (lower.Contains("slow"))
            Add("player.buff.apply:buffPrefab=Buff_General_Slow|duration=10");
        if (lower.Contains("unlock") && lower.Contains("boss"))
            Add("progression.unlock.all_vbloods");
        foreach (var match in matches)
        {
            foreach (var example in match.Examples.DefaultIfEmpty(match.Name))
                Add(example);
        }

        return selected;
    }

    bool IsSafeTopLevelFallbackAction(string action)
    {
        var (name, _) = FlowActionExecutor.ParseActionString(action);
        name = _manifest.NormalizeActionName(name);
        return name.Equals("announce", StringComparison.OrdinalIgnoreCase)
            || name.Equals("notification", StringComparison.OrdinalIgnoreCase)
            || name.Equals("notify", StringComparison.OrdinalIgnoreCase)
            || name.Equals("send_message", StringComparison.OrdinalIgnoreCase);
    }

    void ApplyLocalBuildAndCastleObjects(
        UnifiedEventDefinition definition,
        string request,
        string modeId,
        ActiveSession? session,
        int remainingActions)
    {
        if (remainingActions < 2 || !LooksLikeBuildCastleRequest(request))
            return;

        var lower = request.ToLowerInvariant();
        var center = ResolveEventCenter(definition, session);
        var position = FormatVec3(center);
        var schematicName = SelectCastleSchematic(lower);
        var radius = SelectBuildRadius(lower);
        var group = lower.Contains("floor") && !lower.Contains("wall") && !lower.Contains("castle")
            ? "generated_floor_layout"
            : "generated_castle_layout";

        var obj = definition.Objects.FirstOrDefault(o => o.Group.Equals(group, StringComparison.OrdinalIgnoreCase));
        if (obj == null)
        {
            obj = new EventObjectDefinition
            {
                Group = group,
                Kind = "schematic",
                Schematic = schematicName,
                Position = center
            };
            definition.Objects.Add(obj);
        }

        obj.Kind = "schematic";
        obj.Schematic = schematicName;
        obj.Position = center;

        AddUniqueAction(obj.Actions,
            $"schematic.load:eventName={schematicName}|position={position}|radius={radius}|clearOld=true|spawnItems={WantsItems(lower).ToString().ToLowerInvariant()}");

        if (lower.Contains("palette") || lower.Contains("pal") || lower.Contains("kindred"))
        {
            AddUniqueAction(obj.Actions, "palette.add:search=castle floor stone");
            AddUniqueAction(obj.Actions, "palette.add:search=castle wall stone");
        }

        if (lower.Contains("wall"))
            AddUniqueAction(obj.Actions, "build.search:filter=castle wall stone");
        if (lower.Contains("floor") || lower.Contains("tile"))
            AddUniqueAction(obj.Actions, "build.search:filter=castle floor stone");
        if (lower.Contains("gate") || lower.Contains("door"))
            AddUniqueAction(obj.Actions, "build.search:filter=castle gate door");
        if (WantsItems(lower))
            AddUniqueAction(obj.Actions, "announce:title=Items|message=Castle schematic includes template resource items. Capture a live castle schematic to preserve exact chests and item pickups.|color=#FFD166|level=info");

        var cleanup = EnsurePhase(definition, "cleanup");
        AddUniqueAction(cleanup.Actions, $"schematic.clear:eventName={schematicName}");
    }

    static bool LooksLikeBuildCastleRequest(string request)
    {
        var lower = request.ToLowerInvariant();
        return ContainsAny(lower,
            "build", "building", "castle", "palace", "fort", "base",
            "wall", "floor", "tile", "gate", "door", "carpet",
            "schematic", "kindred", "palette", "pal ",
            "item", "items", "resource", "resources", "chest", "storage", "loot");
    }

    static string SelectCastleSchematic(string lower)
    {
        if ((lower.Contains("floor") || lower.Contains("carpet") || lower.Contains("arena")) &&
            !lower.Contains("wall") &&
            !lower.Contains("gate") &&
            !lower.Contains("door") &&
            !lower.Contains("chest") &&
            !lower.Contains("item"))
        {
            return "arena_filled_floor_template";
        }

        return "castle_design_template";
    }

    static int SelectBuildRadius(string lower)
    {
        var match = System.Text.RegularExpressions.Regex.Match(lower, @"(?<n>\d{1,3})\s*(radius|tiles|size|wide|width)");
        if (match.Success && int.TryParse(match.Groups["n"].Value, out var parsed))
            return Math.Clamp(parsed, 5, 120);

        if (lower.Contains("big") || lower.Contains("large") || lower.Contains("full"))
            return 60;
        if (lower.Contains("small"))
            return 20;
        return 35;
    }

    static bool WantsItems(string lower) =>
        ContainsAny(lower, "item", "items", "resource", "resources", "chest", "storage", "loot", "wood", "stone");

    static Vec3Config ResolveEventCenter(UnifiedEventDefinition definition, ActiveSession? session)
    {
        var zone = session?.Config?.Zones?.Zones?.FirstOrDefault(z => z.Hash == session.Context.ZoneHash);
        if (zone != null)
        {
            var center = zone.Position;
            if (Math.Abs(center.X) > 0.0001f || Math.Abs(center.Y) > 0.0001f || Math.Abs(center.Z) > 0.0001f)
                return center;
            return zone.TeleportSpawn;
        }

        var eventZone = definition.Zones.FirstOrDefault(z => z.Hash != 0);
        if (eventZone != null)
            return eventZone.Center;

        return new Vec3Config { X = -2000, Y = 5, Z = -2800 };
    }

    static string FormatVec3(Vec3Config value) =>
        string.Join(",",
            value.X.ToString(System.Globalization.CultureInfo.InvariantCulture),
            value.Y.ToString(System.Globalization.CultureInfo.InvariantCulture),
            value.Z.ToString(System.Globalization.CultureInfo.InvariantCulture));

    static EventPhaseDefinition EnsurePhase(UnifiedEventDefinition definition, string name)
    {
        var phase = definition.Phases.FirstOrDefault(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (phase != null)
            return phase;

        phase = new EventPhaseDefinition { Name = name };
        definition.Phases.Add(phase);
        return phase;
    }

    static void AddUniqueAction(List<EventActionDefinition> actions, string action)
    {
        if (actions.Any(a => a.ToActionString().Equals(action, StringComparison.OrdinalIgnoreCase)))
            return;
        actions.Add(new EventActionDefinition { Action = action });
    }

    void ApplyLocalRulesAndTimers(UnifiedEventDefinition definition, string request, string modeId)
    {
        var lower = request.ToLowerInvariant();
        var minutes = ExtractMinutes(request);
        var lives = ExtractLives(request);

        if (minutes.HasValue)
            definition.Rules.MatchDurationMinutes = minutes.Value;
        if (lives.HasValue)
        {
            definition.Rules.LivesPerPlayer = lives.Value;
            definition.Rules.EliminationMode = true;
        }
        if (lower.Contains("pvp"))
            definition.Rules.EnablePvP = !ContainsAny(lower, "disable pvp", "no pvp", "pvp off");
        if (ContainsAny(lower, "late join", "latejoin"))
            definition.Rules.AllowLateJoin = !ContainsAny(lower, "no late", "disable late", "late join false", "latejoin false");

        if (ContainsAny(lower, "timer", "countdown", "time limit", "duration", "minutes", "minute"))
        {
            var durationSeconds = Math.Max(30, (minutes ?? definition.Rules.MatchDurationMinutes ?? 5) * 60);
            var timerId = lower.Contains("shrink") ? "shrink" : "match";
            if (!definition.Timers.Any(t => t.TimerId.Equals(timerId, StringComparison.OrdinalIgnoreCase)))
            {
                definition.Timers.Add(new EventTimerDefinition
                {
                    TimerId = timerId,
                    DurationSeconds = durationSeconds,
                    StartPhase = "active",
                    AnnounceStart = true,
                    AnnounceComplete = true,
                    OnCompleteActions = new List<EventActionDefinition>
                    {
                        new()
                        {
                            Action = timerId.Equals("match", StringComparison.OrdinalIgnoreCase)
                                ? $"mode.end:modeId={modeId}|reason=timer_complete"
                                : "announce:title=Timer|message=Shrink timer complete.|color=#FFD166|level=warning"
                        }
                    }
                });
            }
        }
    }

    static string BuildGeneratedAnnounceAction(string request)
    {
        var message = SanitizeActionText(string.IsNullOrWhiteSpace(request)
            ? "Event update generated by local fallback."
            : $"Event update generated: {request}");
        return $"announce:title=Generated|message={message}|color=#5CC8FF|level=info";
    }

    static int? ExtractMinutes(string request)
    {
        var match = System.Text.RegularExpressions.Regex.Match(request, @"(?<n>\d{1,3})\s*(m|min|mins|minute|minutes)\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (match.Success && int.TryParse(match.Groups["n"].Value, out var minutes))
            return Math.Clamp(minutes, 1, 240);

        return null;
    }

    static int? ExtractLives(string request)
    {
        var match = System.Text.RegularExpressions.Regex.Match(request, @"(?<n>\d{1,2})\s*(life|lives)\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (match.Success && int.TryParse(match.Groups["n"].Value, out var lives))
            return Math.Clamp(lives, 1, 20);

        return null;
    }

    static bool ContainsAny(string value, params string[] terms) =>
        terms.Any(t => value.Contains(t, StringComparison.OrdinalIgnoreCase));

    static string SanitizeActionText(string value)
    {
        return value
            .Replace("|", "/")
            .Replace(":", "-")
            .Replace("\r", " ")
            .Replace("\n", " ")
            .Trim();
    }

    static bool LooksLikeKitConfigEdit(string request)
    {
        var lower = request.ToLowerInvariant();
        return (lower.Contains("kit") || lower.Contains("abilit") || lower.Contains("slot")) &&
               (lower.Contains("empty") || lower.Contains("clear") || lower.Contains("remove") || lower.Contains("prefab"));
    }

    public OperationResult<OperatorProposal> GetProposal(string operationId)
    {
        if (!Pending.TryGetValue(operationId ?? "", out var proposal))
            return OperationResult<OperatorProposal>.Fail($"No pending AI operation '{operationId}' found.");

        if (proposal.IsExpired)
        {
            Pending.TryRemove(proposal.OperationId, out _);
            return OperationResult<OperatorProposal>.Fail($"AI operation '{operationId}' expired.");
        }

        return OperationResult<OperatorProposal>.Ok(proposal);
    }

    public OperationResult<OperatorProposal> GetLatestProposalFor(ulong requestedBy)
    {
        var proposal = Pending.Values
            .Where(p => p.RequestedBy == requestedBy)
            .OrderByDescending(p => p.CreatedAtUtc)
            .FirstOrDefault();

        if (proposal == null)
            return OperationResult<OperatorProposal>.Fail("No pending AI operation found for you.");

        return GetProposal(proposal.OperationId);
    }

    public OperationResult<OperatorProposal> PreviewLiveActions(
        ulong requestedBy,
        string modeId,
        string sessionId,
        string request,
        IEnumerable<string> actions)
    {
        var actionList = actions
            .Where(a => !string.IsNullOrWhiteSpace(a))
            .Select(a => ActionParameterConverter.NormalizeActionString(a.Trim()))
            .Where(a => !string.IsNullOrWhiteSpace(a))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(25)
            .ToList();

        if (actionList.Count == 0)
            return OperationResult<OperatorProposal>.Fail("No live actions were provided.");

        var errors = new List<string>();
        foreach (var action in actionList)
        {
            var validation = _manifest.Validate(new EventActionDefinition { Action = action });
            if (!validation.Success)
                errors.Add($"{action}: {validation.Error}. {BuildActionSolution(action, validation.Error)}");

            var runtimeValidation = _runtimeValidator.ValidateAction(action, context: null, sessionId);
            if (!runtimeValidation.Success)
                errors.Add($"{action}: {runtimeValidation.Error}. {BuildActionSolution(action, runtimeValidation.Error)}");
        }

        if (errors.Count > 0)
            return OperationResult<OperatorProposal>.Fail("Live action validation failed: " + string.Join("; ", errors.Take(4)));

        var proposal = new OperatorProposal
        {
            Kind = "live_action",
            OperationId = NewOperationId(),
            ModeId = modeId,
            SessionId = sessionId,
            RequestedBy = requestedBy,
            Request = request,
            Reason = $"Approval-gated live action request for {modeId}; {actionList.Count} action(s).",
            RiskLevel = DetermineRisk(actionList),
            Actions = actionList,
            JsonDiff = $"LIVE ACTIONS ONLY; no config file write. Actions={string.Join(", ", actionList.Take(8))}",
            ExpiresAtUtc = DateTime.UtcNow.AddMinutes(Math.Clamp(ConfigLoader.LoadAIConfig().EventAuthoring.PendingOperationMinutes, 1, 240))
        };

        Pending[proposal.OperationId] = proposal;
        Audit("preview_live_action", proposal);
        return OperationResult<OperatorProposal>.Ok(proposal);
    }

    public OperationResult<OperatorProposal> ApproveLiveActions(
        string operationId,
        ulong approvedBy,
        Func<string, OperationResult> executeAction)
    {
        var get = GetProposal(operationId);
        if (!get.Success || get.Value == null)
            return get;

        var proposal = get.Value;
        var approval = ValidateApproval(proposal, approvedBy);
        if (!approval.Success)
            return OperationResult<OperatorProposal>.Fail(approval.Error ?? "Live action approval was rejected.");

        if (!proposal.Kind.Equals("live_action", StringComparison.OrdinalIgnoreCase))
            return OperationResult<OperatorProposal>.Fail($"AI operation '{operationId}' is not a live action proposal.");

        var failures = new List<string>();
        foreach (var action in proposal.Actions)
        {
            var runtimeValidation = _runtimeValidator.ValidateAction(action, context: null, proposal.SessionId);
            if (!runtimeValidation.Success)
            {
                failures.Add($"{action}: {runtimeValidation.Error}. {BuildActionSolution(action, runtimeValidation.Error)}");
                continue;
            }

            var result = executeAction(action);
            if (!result.Success)
                failures.Add($"{action}: {result.Error}. {BuildActionSolution(action, result.Error)}");
        }

        if (failures.Count > 0)
            return OperationResult<OperatorProposal>.Fail("Live action approval failed: " + string.Join("; ", failures.Take(4)));

        Pending.TryRemove(proposal.OperationId, out _);
        Audit($"approve_live_action by {approvedBy}", proposal);
        return OperationResult<OperatorProposal>.Ok(proposal);
    }

    string BuildActionSolution(string action, string? error)
    {
        var (name, parameters) = FlowActionExecutor.ParseActionString(action ?? "");
        name = string.IsNullOrWhiteSpace(name) ? action ?? "" : name;

        var normalized = _manifest.NormalizeActionName(name);
        if (!normalized.Equals(name, StringComparison.OrdinalIgnoreCase))
            return $"Try canonical action `{normalized}{FormatParameters(parameters)}`.";

        var query = $"{name} {error}";
        var matches = _manifest.Search(query, 3);
        if (matches.Count == 0)
            matches = _manifest.Search(name, 3);

        if (matches.Count == 0)
            return "Run `.ai catalog search <keyword>` to find a supported replacement.";

        var suggestions = matches
            .Select(m => m.Examples.FirstOrDefault() ?? m.Name)
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(3)
            .ToList();

        return suggestions.Count == 0
            ? $"Try `.ai catalog search {name}` for valid parameters."
            : $"Immediate solution: try `{suggestions[0]}`" + (suggestions.Count > 1 ? $" or `{suggestions[1]}`." : ".");
    }

    static string FormatParameters(Dictionary<string, string> parameters)
    {
        if (parameters.Count == 0)
            return "";
        return ":" + string.Join("|", parameters.Select(kv => $"{kv.Key}={kv.Value}"));
    }

    public OperationResult<OperatorProposal> Approve(string operationId, ulong approvedBy)
    {
        var get = GetProposal(operationId);
        if (!get.Success || get.Value == null)
            return get;

        var proposal = get.Value;
        var approval = ValidateApproval(proposal, approvedBy);
        if (!approval.Success)
            return OperationResult<OperatorProposal>.Fail(approval.Error ?? "AI operation approval was rejected.");

        if (proposal.Kind.Equals("live_action", StringComparison.OrdinalIgnoreCase))
            return OperationResult<OperatorProposal>.Fail($"AI operation '{operationId}' is a live action. Use ApproveLiveActions.");

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(proposal.EventPath)!);
            var backupPath = $"{proposal.EventPath}.{proposal.OperationId}.bak";
            if (File.Exists(proposal.EventPath))
                File.Copy(proposal.EventPath, backupPath, overwrite: true);
            else
                File.WriteAllText(backupPath, proposal.OriginalJson);

            var tmpPath = proposal.EventPath + ".tmp";
            File.WriteAllText(tmpPath, proposal.ProposedJson);
            if (File.Exists(proposal.EventPath))
                File.Replace(tmpPath, proposal.EventPath, backupPath, ignoreMetadataErrors: true);
            else
                File.Move(tmpPath, proposal.EventPath, overwrite: true);

            proposal.BackupPath = backupPath;
            ConfigLoader.Reload(proposal.ModeId);
            Pending.TryRemove(proposal.OperationId, out _);
            Audit($"approve by {approvedBy}", proposal);
            return OperationResult<OperatorProposal>.Ok(proposal);
        }
        catch (Exception ex)
        {
            return OperationResult<OperatorProposal>.Fail($"Approve failed: {ex.Message}");
        }
    }

    public OperationResult<OperatorProposal> Rollback(string operationId, ulong requestedBy)
    {
        var get = GetProposal(operationId);
        if (!get.Success || get.Value == null)
            return get;

        var proposal = get.Value;
        if (proposal.Kind.Equals("live_action", StringComparison.OrdinalIgnoreCase))
        {
            Pending.TryRemove(proposal.OperationId, out _);
            Audit($"discard_live_action by {requestedBy}", proposal);
            return OperationResult<OperatorProposal>.Ok(proposal);
        }

        try
        {
            File.WriteAllText(proposal.EventPath, proposal.OriginalJson);
            ConfigLoader.Reload(proposal.ModeId);
            Pending.TryRemove(proposal.OperationId, out _);
            Audit($"rollback by {requestedBy}", proposal);
            return OperationResult<OperatorProposal>.Ok(proposal);
        }
        catch (Exception ex)
        {
            return OperationResult<OperatorProposal>.Fail($"Rollback failed: {ex.Message}");
        }
    }

    static OperationResult ValidateApproval(OperatorProposal proposal, ulong approvedBy)
    {
        if (proposal.RequestedBy != approvedBy)
        {
            return OperationResult.Fail(
                $"AI operation '{proposal.OperationId}' was requested by {proposal.RequestedBy} and may only be approved by that player.");
        }

        // World/no-session proposals deliberately remain approvable without a
        // session lookup. A session-bound proposal must stay bound to that exact
        // live session through approval and execution.
        if (string.IsNullOrWhiteSpace(proposal.SessionId))
            return OperationResult.Ok();

        var activeSession = BattleLuckPlugin.Session?.ActiveSessions?.Values
            .FirstOrDefault(session => session.Context?.SessionId.Equals(proposal.SessionId, StringComparison.OrdinalIgnoreCase) == true);
        if (activeSession?.Context == null)
        {
            return OperationResult.Fail(
                $"AI operation '{proposal.OperationId}' is bound to session '{proposal.SessionId}', which is no longer active. Create a new preview for the current session.");
        }

        if (!string.IsNullOrWhiteSpace(proposal.ModeId) &&
            !activeSession.Context.ModeId.Equals(proposal.ModeId, StringComparison.OrdinalIgnoreCase))
        {
            return OperationResult.Fail(
                $"AI operation '{proposal.OperationId}' is bound to session '{proposal.SessionId}', but that session now reports mode '{activeSession.Context.ModeId}' instead of '{proposal.ModeId}'. Create a new preview.");
        }

        return OperationResult.Ok();
    }

    static string GetEventPath(string modeId)
    {
        var eventsRoot = Path.Combine(ConfigLoader.ConfigRoot, "events");
        var canonical = Path.Combine(eventsRoot, modeId, "flow.json");
        var legacy = Path.Combine(eventsRoot, $"{modeId}.json");

        // The runtime resolves flow.json first.  Always write that canonical path unless
        // this is an intentionally legacy-only mode, so an approved proposal is visible
        // to the same loader that starts live sessions.
        return File.Exists(canonical) || !File.Exists(legacy) ? canonical : legacy;
    }

    static HashSet<string> GetNonNotificationRootActions(string json)
    {
        try
        {
            var definition = JsonSerializer.Deserialize<UnifiedEventDefinition>(json, ConfigLoader.JsonOptions);
            return definition?.Actions
                .Select(action => action.ToActionString())
                .Where(action => !IsNotificationOnlyRootAction(action))
                .ToHashSet(StringComparer.OrdinalIgnoreCase)
                ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }
    }

    static bool IsNotificationOnlyRootAction(string action)
    {
        var actionName = action.Split(':', 2)[0].Trim();
        return actionName.Equals("announce", StringComparison.OrdinalIgnoreCase) ||
               actionName.Equals("notification", StringComparison.OrdinalIgnoreCase) ||
               actionName.Equals("notify", StringComparison.OrdinalIgnoreCase) ||
               actionName.Equals("send_message", StringComparison.OrdinalIgnoreCase);
    }

    static string LoadEventJson(string modeId, string eventPath)
    {
        if (File.Exists(eventPath))
            return File.ReadAllText(eventPath);

        var stub = new UnifiedEventDefinition
        {
            Metadata = new EventMetadata
            {
                Id = modeId,
                DisplayName = modeId,
                Enabled = true,
                Version = "1"
            }
        };
        return JsonSerializer.Serialize(stub, ConfigLoader.JsonOptions);
    }

    static string BuildOperatorPrompt(string modeId, string request, ActiveSession? session, List<CatalogActionSearchResult> matches, int maxActions)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Live Operator event-authoring request for mode '{modeId}':");
        sb.AppendLine(request);
        sb.AppendLine();
        sb.AppendLine("Return a complete updated flow config only under the flow.json key.");
        sb.AppendLine("Use only actions from the catalog matches or registered catalog names.");
        sb.AppendLine($"Do not exceed {maxActions} total event actions.");
        sb.AppendLine("Preserve unrelated existing content. The schema uses arrays for zones, objects, glows, bosses, phases, timers, and triggers; root actions are reserved for announcements.");
        sb.AppendLine("Use rules.minPlayers/maxPlayers/enablePvP/matchDurationMinutes/allowLateJoin/eliminationMode (boolean)/livesPerPlayer.");
        sb.AppendLine("Use phases[].name and phases[].durationSeconds. A duration is an elapsed-time trigger, not a sequential lifecycle; setup runs at session initialization and active runs when the session is marked active.");
        sb.AppendLine("Use timers[].timerId/durationSeconds/startPhase/announceStart/announceComplete/onCompleteActions.");
        sb.AppendLine("Use structured actions: { \"type\": \"action.name\", \"params\": { ... } }.");
        sb.AppendLine("Top-level actions are announcement-only (announce, notification, notify, send_message). Put gameplay mutations in phase, timer completion, trigger, or object action lists.");
        sb.AppendLine("bosses[] is descriptive/validated metadata only; use a validated spawn.boss action in an executable phase to create a live boss.");
        sb.AppendLine("Use announce actions for colored phase/timer/rule messages: color=#RRGGBB and level=info/success/warning/error.");
        sb.AppendLine("Use boss.goto, boss.goto.pos, boss.return_home, boss.follow, ai.boss.aggro/deaggro, and ai.set_behavior for live boss control.");
        sb.AppendLine("For reusable gathered action plans, use custom_sequences.json plus sequence.custom.play:sequenceId=<id>|schedule=true. Do not inline huge repeated action lists when a named custom sequence exists.");
        sb.AppendLine("Custom sequence steps may include action steps plus wait:<seconds> or tick:<event-second> markers; unified event runtime queues timed steps on the session clock.");
        sb.AppendLine("Do not propose strict-profile blocked native construction actions: build.free, build.spawn, structure.spawn, tile.place, wall.build, floor.place, wall.destroy, or zone.border.*.");
        sb.AppendLine("A schematic action is allowed only when the catalog validates it and it includes safetyMode=event_tracked_zone_only; otherwise omit it.");
        sb.AppendLine("Event kits are transactional: old kit/equipment/ability/passive slots are snapshotted and removed; exit/elimination removes the event kit before snapshot restore. Never mutate kit, abilities, blood, level, or inventory after snapshot.restore.");
        sb.AppendLine("livesPerPlayer counts immediate in-arena respawns. livesPerPlayer=3 means deaths 1-3 respawn and death 4 eliminates with personal rollback.");
        sb.AppendLine("The .toggleleave boundary rule activates only after session start and per-player arena teleport.");
        sb.AppendLine("Validate every action before returning JSON. Omit actions rejected by handler, prompt, prefab, safetyMode, count, batch, or tile-cap validation.");
        sb.AppendLine("A preview is not applied. Approval writes and reloads the config; only the command result can confirm success. Rollback restores a pending config proposal or discards a pending live action, not an action that already executed.");
        sb.AppendLine();

        var eventPrompt = new PromptContextLoader().Load(modeId);
        if (eventPrompt != null)
        {
            sb.AppendLine("Event prompt policy (must be followed):");
            sb.AppendLine(new PromptContextLoader().BuildPrompt(eventPrompt, new RuntimeActionContext()));
            sb.AppendLine();
        }

        var customSequences = new CustomSequenceService().List().Take(12).ToList();
        if (customSequences.Count > 0)
        {
            sb.AppendLine("Reusable custom sequences:");
            foreach (var seq in customSequences)
                sb.AppendLine($"- {seq.Id}: actions={seq.Actions} steps={seq.Steps} timing={(seq.HasTiming ? "yes" : "no")} risk={seq.RiskLevel}");
            sb.AppendLine();
        }

        if (session?.Context != null)
        {
            sb.AppendLine("Live session context:");
            sb.AppendLine($"sessionId={session.Context.SessionId}");
            sb.AppendLine($"zoneHash={session.Context.ZoneHash}");
            sb.AppendLine($"started={session.IsStarted}");
            sb.AppendLine($"players={session.Context.Players.Count}");
            sb.AppendLine($"elapsedSeconds={session.Context.ElapsedSeconds:F1}");
            sb.AppendLine();
        }

        sb.AppendLine("Top actions_catalog.json matches:");
        foreach (var match in matches)
        {
            sb.AppendLine($"- {match.Name} category={match.Category} risk={match.RiskLevel} examples={string.Join(" ; ", match.Examples.Take(2))}");
        }

        return sb.ToString();
    }

    static IEnumerable<string> ExtractActions(UnifiedEventDefinition definition)
    {
        foreach (var action in definition.Actions) yield return action.ToActionString();
        foreach (var obj in definition.Objects)
        foreach (var action in obj.Actions) yield return action.ToActionString();
        foreach (var glow in definition.Glows)
        foreach (var action in glow.Actions) yield return action.ToActionString();
        foreach (var phase in definition.Phases)
        foreach (var action in phase.Actions) yield return action.ToActionString();
        foreach (var timer in definition.Timers)
        foreach (var action in timer.OnCompleteActions) yield return action.ToActionString();
        foreach (var trigger in definition.Triggers)
        foreach (var action in trigger.Actions) yield return action.ToActionString();
    }

    static string DetermineRisk(IEnumerable<string> actions)
    {
        var risk = "safe";
        foreach (var action in actions)
        {
            var name = action.Split(':', 2)[0];
            var inferred = InferRisk(name);
            if (inferred == "destructive") return "destructive";
            if (inferred == "controlled") risk = "controlled";
        }
        return risk;
    }

    static string InferRisk(string name)
    {
        if (name.Contains("query", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("notify", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("timer", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("score", StringComparison.OrdinalIgnoreCase))
            return "safe";
        if (name.Contains("remove", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("destroy", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("clear", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("damage", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("death", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("mode.end", StringComparison.OrdinalIgnoreCase))
            return "destructive";
        return "controlled";
    }

    static string BuildDiffSummary(string originalJson, string proposedJson, List<string> actions)
    {
        var originalLines = originalJson.Split('\n').Length;
        var proposedLines = proposedJson.Split('\n').Length;
        return $"flow.json lines {originalLines}->{proposedLines}; actions={actions.Count}; first actions={string.Join(", ", actions.Take(8))}";
    }

    static string ExtractJsonObject(string value)
    {
        var trimmed = value.Trim();
        if (trimmed.StartsWith("```", StringComparison.Ordinal))
        {
            var firstNewline = trimmed.IndexOf('\n');
            var lastFence = trimmed.LastIndexOf("```", StringComparison.Ordinal);
            if (firstNewline >= 0 && lastFence > firstNewline)
                trimmed = trimmed[(firstNewline + 1)..lastFence].Trim();
        }

        var start = trimmed.IndexOf('{');
        var end = trimmed.LastIndexOf('}');
        if (start >= 0 && end >= start)
            return trimmed[start..(end + 1)];
        return trimmed;
    }

    static string NewOperationId() => $"aio_{DateTime.UtcNow:yyyyMMddHHmmss}_{Guid.NewGuid():N}"[..26];

    static void Audit(string action, OperatorProposal proposal)
    {
        try
        {
            var path = Path.Combine(ConfigLoader.ConfigRoot, "ai_operations.log");
            var line = JsonSerializer.Serialize(new
            {
                at = DateTime.UtcNow,
                action,
                proposal.OperationId,
                proposal.ModeId,
                proposal.SessionId,
                proposal.RiskLevel,
                proposal.RequestedBy,
                proposal.Request,
                proposal.Actions
            });
            File.AppendAllText(path, line + Environment.NewLine);
        }
        catch (Exception ex)
        {
            BattleLuckPlugin.LogWarning($"[LiveEventOperator] Audit write failed: {ex.Message}");
        }
    }
}
