// Core/ExecutionPipeline.cs
// Orchestrates the full lifecycle of an action execution from submission
// through LLM generation to game action execution.
//
// Flow:
//   Submit → Validate → Call /generate on Docker ai_runtime → FlowActionExecutor.Execute()
//
// The pipeline is the single entry point for AI-driven actions and is the
// sole source of truth for execution state tracking.

namespace BattleLuck.Core;

// Alias to disambiguate from System.Threading.ExecutionContext
using ExecContext = BattleLuck.Models.ExecutionContext;

/// <summary>
/// Request contract for the Docker ai_runtime /generate endpoint.
/// </summary>
public sealed class GenerateRequest
{
    [System.Text.Json.Serialization.JsonPropertyName("group")]
    public string Group { get; set; } = "";

    [System.Text.Json.Serialization.JsonPropertyName("prompt")]
    public string Prompt { get; set; } = "";

    [System.Text.Json.Serialization.JsonPropertyName("temperature")]
    public float? Temperature { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("max_tokens")]
    public int? MaxTokens { get; set; }
}

/// <summary>
/// Response contract for the Docker ai_runtime /generate endpoint.
/// </summary>
public sealed class GenerateResponse
{
    [System.Text.Json.Serialization.JsonPropertyName("result")]
    public string Result { get; set; } = "";

    [System.Text.Json.Serialization.JsonPropertyName("model")]
    public string Model { get; set; } = "";

    [System.Text.Json.Serialization.JsonPropertyName("group")]
    public string Group { get; set; } = "";

    [System.Text.Json.Serialization.JsonPropertyName("tokens")]
    public int Tokens { get; set; }
}

/// <summary>
/// Response contract for the Docker ai_runtime /llm-groups endpoint.
/// </summary>
public sealed class LlmGroupsResponse
{
    [System.Text.Json.Serialization.JsonPropertyName("groups")]
    public Dictionary<string, LlmGroupInfo> Groups { get; set; } = new();
}

public sealed class LlmGroupInfo
{
    [System.Text.Json.Serialization.JsonPropertyName("models")]
    public List<string> Models { get; set; } = new();

    [System.Text.Json.Serialization.JsonPropertyName("fallback_behavior")]
    public string FallbackBehavior { get; set; } = "round_robin";

    [System.Text.Json.Serialization.JsonPropertyName("temperature")]
    public float Temperature { get; set; } = 0.7f;

    [System.Text.Json.Serialization.JsonPropertyName("max_tokens")]
    public int MaxTokens { get; set; } = 512;
}

/// <summary>
/// Result returned by <see cref="ExecutionPipeline.RunAsync"/>.
/// </summary>
public sealed class ExecutionPipelineResult
{
    public bool Success { get; init; }
    public ExecContext Context { get; init; } = null!;
    public string? Error { get; init; }

    public static ExecutionPipelineResult Ok(ExecContext ctx) =>
        new() { Success = true, Context = ctx };

    public static ExecutionPipelineResult Fail(ExecContext ctx, string error) =>
        new() { Success = false, Context = ctx, Error = error };
}

/// <summary>
/// Orchestrates the full lifecycle: validate → generate (LLM) → execute (game action).
/// </summary>
public sealed class ExecutionPipeline
{
    private readonly HttpClient _httpClient;
    private readonly string _runtimeBaseUrl;
    private readonly FlowActionExecutor _actionExecutor;
    private readonly ProjectMAiGroupSettings? _aiGroupSettings;

    // Maps action name prefixes to LLM group names.
    // Configured at construction; can be extended.
    private static readonly Dictionary<string, string> ActionGroupMapping = new(
        StringComparer.OrdinalIgnoreCase)
    {
        ["npc."] = "combat",
        ["boss."] = "combat",
        ["ai."] = "combat",
        ["spawn."] = "combat",
        ["announce"] = "narrative",
        ["notify"] = "narrative",
        ["send_message"] = "narrative",
        ["notification"] = "narrative",
        ["mode."] = "planning",
        ["session."] = "planning",
        ["sequence."] = "planning",
        ["kit."] = "planning",
        ["inventory."] = "planning",
        ["objective."] = "planning",
        ["shrink."] = "planning",
        ["team."] = "planning",
    };

    private const string DefaultGroup = "planning";

    /// <summary>
    /// Create the pipeline.
    /// </summary>
    /// <param name="httpClient">HTTP client for calling the Docker ai_runtime.</param>
    /// <param name="runtimeBaseUrl">Base URL of the ai_runtime (e.g., "http://localhost:8000").</param>
    /// <param name="actionExecutor">The game action executor.</param>
    /// <param name="aiGroupSettings">Optional AI group action policies for validation.</param>
    public ExecutionPipeline(
        HttpClient httpClient,
        string runtimeBaseUrl,
        FlowActionExecutor actionExecutor,
        ProjectMAiGroupSettings? aiGroupSettings = null)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _runtimeBaseUrl = runtimeBaseUrl?.TrimEnd('/') ?? throw new ArgumentNullException(nameof(runtimeBaseUrl));
        _actionExecutor = actionExecutor ?? throw new ArgumentNullException(nameof(actionExecutor));
        _aiGroupSettings = aiGroupSettings;
    }

    /// <summary>
    /// Run an action directive through the full pipeline.
    /// </summary>
    public async Task<ExecutionPipelineResult> RunAsync(
        AiGroupProjectMDirective directive,
        FlowActionContext? actionContext = null)
    {
        var ctx = new ExecContext
        {
            Directive = directive,
            ActionString = directive.Action,
            ActionName = ExtractActionName(directive.Action),
            Confidence = directive.Confidence,
        };

        return await RunInternalAsync(ctx, actionContext);
    }

    /// <summary>
    /// Run a raw action string through the full pipeline.
    /// </summary>
    public async Task<ExecutionPipelineResult> RunAsync(
        string actionString,
        FlowActionContext? actionContext = null)
    {
        var ctx = new ExecContext
        {
            ActionString = actionString,
            ActionName = ExtractActionName(actionString),
        };

        return await RunInternalAsync(ctx, actionContext);
    }

    private async Task<ExecutionPipelineResult> RunInternalAsync(
        ExecContext ctx,
        FlowActionContext? actionContext)
    {
        // ── Phase 1: Submitted → Validating ──────────────────────────────
        var transition = ctx.TransitionTo(ExecutionLifecycle.Validating);
        if (!transition.Succeeded)
            return ExecutionPipelineResult.Fail(ctx, transition.Error ?? "Invalid initial state.");

        // ── Phase 2: Validate ────────────────────────────────────────────
        var validationResult = Validate(ctx);
        if (!validationResult.Succeeded)
        {
            ctx.TransitionTo(ExecutionLifecycle.Invalid);
            ctx.Error = validationResult.Error;
            return ExecutionPipelineResult.Fail(ctx, validationResult.Error ?? "Validation rejected.");
        }

        // ── Phase 3: Validating → Validated ──────────────────────────────
        ctx.TransitionTo(ExecutionLifecycle.Validated);

        // ── Phase 4: Validated → Executing ───────────────────────────────
        ctx.TransitionTo(ExecutionLifecycle.Executing);

        // ── Phase 5: Resolve LLM group and call /generate ────────────────
        ctx.SelectedGroup = ResolveGroup(ctx.ActionName);

        try
        {
            var generateResult = await CallGenerateAsync(ctx);
            if (generateResult == null)
            {
                ctx.TransitionTo(ExecutionLifecycle.Failed);
                ctx.Error = "LLM generation returned no result.";
                return ExecutionPipelineResult.Fail(ctx, ctx.Error);
            }

            ctx.SelectedModel = generateResult.Model;
            ctx.LlmResponse = generateResult.Result;
        }
        catch (Exception ex)
        {
            ctx.TransitionTo(ExecutionLifecycle.Failed);
            ctx.Error = $"LLM call failed: {ex.Message}";
            return ExecutionPipelineResult.Fail(ctx, ctx.Error);
        }

        // ── Phase 6: Execute the game action ─────────────────────────────
        if (actionContext != null && !string.IsNullOrWhiteSpace(ctx.ActionString))
        {
            try
            {
                ctx.ActionResult = _actionExecutor.Execute(ctx.ActionString, actionContext);
                if (ctx.ActionResult is { Success: false })
                {
                    ctx.TransitionTo(ExecutionLifecycle.Failed);
                    ctx.Error = ctx.ActionResult.Error ?? "Action execution failed.";
                    return ExecutionPipelineResult.Fail(ctx, ctx.Error);
                }
            }
            catch (Exception ex)
            {
                ctx.TransitionTo(ExecutionLifecycle.Failed);
                ctx.Error = $"Action execution threw: {ex.Message}";
                return ExecutionPipelineResult.Fail(ctx, ctx.Error);
            }
        }

        // ── Phase 7: Executing → Succeeded ───────────────────────────────
        ctx.TransitionTo(ExecutionLifecycle.Succeeded);
        return ExecutionPipelineResult.Ok(ctx);
    }

    /// <summary>
    /// Validate the target before allowing execution.
    /// Checks confidence thresholds, cooldowns, and action policies.
    /// </summary>
    private ExecutionLifecycleTransitionResult Validate(ExecContext ctx)
    {
        if (_aiGroupSettings == null || !_aiGroupSettings.Enabled)
            return ExecutionLifecycleTransitionResult.Success(
                ExecutionLifecycle.Validating, ExecutionLifecycle.Validated);

        var actionName = ctx.ActionName;
        if (string.IsNullOrWhiteSpace(actionName))
            return ExecutionLifecycleTransitionResult.Failure(
                ExecutionLifecycle.Validating, ExecutionLifecycle.Invalid,
                "Action name is empty.");

        // Check if action has a specific policy
        if (_aiGroupSettings.ActionPolicies.TryGetValue(actionName, out var policy))
        {
            if (!policy.Enabled)
                return ExecutionLifecycleTransitionResult.Failure(
                    ExecutionLifecycle.Validating, ExecutionLifecycle.Invalid,
                    $"Action '{actionName}' is disabled by policy.");

            // Confidence check
            if (ctx.Confidence < policy.MinConfidence)
                return ExecutionLifecycleTransitionResult.Failure(
                    ExecutionLifecycle.Validating, ExecutionLifecycle.Invalid,
                    $"Confidence {ctx.Confidence:F2} is below minimum {policy.MinConfidence:F2} for '{actionName}'.");

            // Cooldown check (simplified — a real implementation would use a cooldown tracker)
            // if (policy.CooldownSeconds > 0 && !IsCooldownElapsed(actionName, policy.CooldownSeconds))
            //     return ... "Action is on cooldown."
        }

        // Action must be registered
        if (!FlowActionExecutor.SupportedActions.Contains(actionName, StringComparer.OrdinalIgnoreCase) &&
            !LiveSystemRegistryService.IsRegisteredAction(actionName))
        {
            return ExecutionLifecycleTransitionResult.Failure(
                ExecutionLifecycle.Validating, ExecutionLifecycle.Invalid,
                $"Action '{actionName}' is not registered in actions_catalog.json or the live system registry.");
        }

        return ExecutionLifecycleTransitionResult.Success(
            ExecutionLifecycle.Validating, ExecutionLifecycle.Validated);
    }

    /// <summary>
    /// Call the Docker ai_runtime /generate endpoint.
    /// </summary>
    private async Task<GenerateResponse?> CallGenerateAsync(ExecContext ctx)
    {
        var request = new GenerateRequest
        {
            Group = ctx.SelectedGroup ?? DefaultGroup,
            Prompt = BuildPrompt(ctx),
        };

        var json = JsonSerializer.Serialize(request);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        var response = await _httpClient.PostAsync($"{_runtimeBaseUrl}/generate", content);
        response.EnsureSuccessStatusCode();

        var responseJson = await response.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<GenerateResponse>(responseJson);
    }

    /// <summary>
    /// Build the prompt for the LLM based on the execution context.
    /// </summary>
    private static string BuildPrompt(ExecContext ctx)
    {
        if (ctx.Directive != null)
        {
            // Structured directive from AI group system
            return $"{ctx.Directive.Directive}\n\nReason: {ctx.Directive.Reason}\n\nGenerate an appropriate response for action: {ctx.Directive.Action}";
        }

        return $"Generate an appropriate response for action: {ctx.ActionString}";
    }

    /// <summary>
    /// Resolve the LLM group name based on the action name prefix.
    /// </summary>
    private static string ResolveGroup(string? actionName)
    {
        if (string.IsNullOrWhiteSpace(actionName))
            return DefaultGroup;

        foreach (var (prefix, group) in ActionGroupMapping)
        {
            if (actionName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return group;
        }

        return DefaultGroup;
    }

    /// <summary>
    /// Extract the action name from a full action string (e.g., "npc.aggro:prefab=vampire" → "npc.aggro").
    /// </summary>
    private static string? ExtractActionName(string? actionString)
    {
        if (string.IsNullOrWhiteSpace(actionString))
            return null;

        var colonIndex = actionString.IndexOf(':');
        return colonIndex >= 0 ? actionString[..colonIndex] : actionString;
    }
}
