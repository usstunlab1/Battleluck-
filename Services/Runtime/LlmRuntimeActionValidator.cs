namespace BattleLuck.Services.Runtime;

/// <summary>
/// Validates LLM-generated action strings against runtime constraints.
/// Checks prefab validity, NPC existence, and boss targeting for actions
/// that will be executed at runtime.
/// </summary>
public sealed class LlmRuntimeActionValidator
{
    readonly ActionManifestService _manifest;

    static readonly HashSet<string> PrefabParameterNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "prefab",
        "itemPrefab",
        "abilityPrefab",
        "buff",
        "buffPrefab",
        "wallPrefab",
        "floorPrefab",
        "factionId"
    };

    static readonly string[] NpcActionPrefixes = { "npc.", "ai.npc." };
    static readonly string[] BossActionPrefixes = { "boss.", "ai.boss." };

    public LlmRuntimeActionValidator(ActionManifestService? manifest = null)
    {
        _manifest = manifest ?? new ActionManifestService();
    }

    public OperationResult ValidateAction(string actionString, FlowActionContext? context = null, string? sessionIdOverride = null)
    {
        if (string.IsNullOrWhiteSpace(actionString))
            return OperationResult.Fail("Action string is empty.");

        var manifestValidation = _manifest.Validate(new EventActionDefinition { Action = actionString });
        if (!manifestValidation.Success)
            return OperationResult.Fail(manifestValidation.Error ?? "Action is not registered for runtime execution.");

        var (rawActionName, parameters) = FlowActionExecutor.ParseActionString(actionString);
        if (string.IsNullOrWhiteSpace(rawActionName))
            return OperationResult.Fail("Directive action is empty.");

        var actionName = _manifest.NormalizeActionName(rawActionName);

        var policyError = ValidateEventPromptPolicy(actionName, context, sessionIdOverride);
        if (policyError != null)
            return policyError;

        var prefabError = ValidatePrefabParameters(actionName, parameters);
        if (prefabError != null)
            return prefabError;

        var npcError = ValidateNpcAction(actionName, parameters);
        if (npcError != null)
            return npcError;

        var bossError = ValidateBossAction(actionName, parameters, sessionIdOverride, context);
        if (bossError != null)
            return bossError;

        return OperationResult.Ok();
    }

    private static OperationResult? ValidateEventPromptPolicy(string actionName, FlowActionContext? context, string? sessionIdOverride)
    {
        var modeId = context?.GameContext?.ModeId;
        if (string.IsNullOrWhiteSpace(modeId) && !string.IsNullOrWhiteSpace(sessionIdOverride))
        {
            modeId = BattleLuckPlugin.Session?.ActiveSessions.Values
                .FirstOrDefault(session => session.Context.SessionId.Equals(sessionIdOverride, StringComparison.OrdinalIgnoreCase))
                ?.Context.ModeId;
        }

        if (string.IsNullOrWhiteSpace(modeId))
            return null;

        var prompt = new PromptContextLoader().Load(modeId);
        if (prompt == null)
            return null;

        if (prompt.BlockedActions.Contains(actionName, StringComparer.OrdinalIgnoreCase))
            return OperationResult.Fail($"{actionName}: blocked by {modeId}/prompt.txt.");

        if (prompt.AllowedActions.Count > 0 &&
            !prompt.AllowedActions.Contains(actionName, StringComparer.OrdinalIgnoreCase))
        {
            return OperationResult.Fail($"{actionName}: not allowed by {modeId}/prompt.txt.");
        }

        return null;
    }

    private OperationResult? ValidatePrefabParameters(string actionName, Dictionary<string, string> parameters)
    {
        foreach (var kv in parameters)
        {
            if (string.IsNullOrWhiteSpace(kv.Value))
                continue;

            if (!IsPrefabParameter(kv.Key))
                continue;

            if (!PrefabHelper.TryGetValidPrefabGuidDeep(kv.Value, out _))
                return OperationResult.Fail($"{actionName}: '{kv.Key}' value '{kv.Value}' does not resolve to a live prefab.");
        }

        return null;
    }

    private static bool IsPrefabParameter(string key)
    {
        return PrefabParameterNames.Contains(key) || key.EndsWith("prefab", StringComparison.OrdinalIgnoreCase);
    }

    private OperationResult? ValidateNpcAction(string actionName, Dictionary<string, string> parameters)
    {
        if (!IsNpcAction(actionName))
            return null;

        if (!parameters.TryGetValue("npcId", out var npcId) || string.IsNullOrWhiteSpace(npcId))
            return null;

        var npcService = BattleLuckPlugin.NpcService;
        if (npcService == null || !npcService.TryGet(npcId, out var npc) || !npc.IsAlive)
            return OperationResult.Fail($"{actionName}: npcId '{npcId}' is not a live tracked NPC.");

        return null;
    }

    private static bool IsNpcAction(string actionName)
    {
        return NpcActionPrefixes.Any(prefix => actionName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
    }

    private OperationResult? ValidateBossAction(string actionName, Dictionary<string, string> parameters, string? sessionIdOverride, FlowActionContext? context)
    {
        if (!IsBossAction(actionName))
            return null;

        var sessionId = ResolveSessionId(sessionIdOverride, context);
        if (string.IsNullOrWhiteSpace(sessionId))
            return OperationResult.Fail($"{actionName}: no active runtime session context available.");

        var controlledNpcs = BattleLuckPlugin.NpcService?.List(sessionId) ?? Array.Empty<ControlledNpcEntry>();
        if (controlledNpcs.Count == 0)
            return OperationResult.Fail($"{actionName}: no controlled NPC registry entries found for active session '{sessionId}'.");

        parameters.TryGetValue("bossId", out var bossId);
        parameters.TryGetValue("prefab", out var bossPrefab);

        if (!string.IsNullOrWhiteSpace(bossId) || !string.IsNullOrWhiteSpace(bossPrefab))
        {
            var resolvedNpc = controlledNpcs.FirstOrDefault(npc =>
                (!string.IsNullOrWhiteSpace(bossId) && npc.NpcId.Equals(bossId, StringComparison.OrdinalIgnoreCase)) ||
                (!string.IsNullOrWhiteSpace(bossPrefab) && npc.PrefabName.Contains(bossPrefab, StringComparison.OrdinalIgnoreCase)));
            if (resolvedNpc == null || !resolvedNpc.IsAlive)
                return OperationResult.Fail($"{actionName}: NPC target (id='{bossId}', prefab='{bossPrefab}') is not live in session '{sessionId}'.");
        }

        return null;
    }

    private static bool IsBossAction(string actionName)
    {
        return BossActionPrefixes.Any(prefix => actionName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) ||
               actionName.Equals("ai.set_behavior", StringComparison.OrdinalIgnoreCase);
    }

    private static string? ResolveSessionId(string? sessionIdOverride, FlowActionContext? context)
    {
        if (!string.IsNullOrWhiteSpace(sessionIdOverride))
            return sessionIdOverride;

        return context?.GameContext?.SessionId;
    }
}
