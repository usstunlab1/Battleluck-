using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using BattleLuck.Core.Loaders;
using BattleLuck.Models;

namespace BattleLuck.Services.Runtime;

/// <summary>
/// Loads the AI prompt, action policy, and examples directly from the unified
/// event JSON at config/BattleLuck/events/<modeId>.json.
///
/// config/BattleLuck/events/<modeId>/prompt.txt is optional and provides
/// a narrative override. The JSON ai block owns the structured policy.
/// </summary>
public sealed class PromptContextLoader
{
    readonly ActionManifestService _actions = ActionManifestService.Instance;
    const int MaxPromptBytes = 128 * 1024;

    public sealed class PromptContext
    {
        public string EventId { get; set; } = "";
        public bool Enabled { get; set; } = true;
        public List<string> AllowedActions { get; set; } = new();
        public List<string> BlockedActions { get; set; } = new();
        public List<string> AllowedTechs { get; set; } = new();
        public List<string> RequireApproval { get; set; } = new();
        public List<string> Instructions { get; set; } = new();
        public List<PromptExample> Examples { get; set; } = new();
        public string Role { get; set; } = "";
        public string Objective { get; set; } = "";
        public string ResponseMode { get; set; } = "action_plan";
        public string Narrative { get; set; } = "";
        public bool IncludeActionCatalog { get; set; } = true;
    }

    public sealed class PromptExample
    {
        public string Request { get; set; } = "";
        public JsonElement Actions { get; set; }
    }

    public PromptContext? Load(string modeId)
    {
        if (string.IsNullOrWhiteSpace(modeId))
            return null;

        var jsonPath = Path.Combine(ConfigLoader.ConfigRoot, "events", $"{modeId}.json");
        if (!File.Exists(jsonPath))
            return null;

        try
        {
            var context = ParseEventJson(File.ReadAllText(jsonPath), modeId);
            if (context == null)
                return null;

            // Optional narrative override from prompt.txt
            var promptPath = Path.Combine(ConfigLoader.ConfigRoot, "events", modeId, "prompt.txt");
            if (File.Exists(promptPath))
            {
                try
                {
                    var promptText = File.ReadAllText(promptPath);
                    if (!string.IsNullOrWhiteSpace(promptText))
                    {
                        var bytes = Encoding.UTF8.GetByteCount(promptText);
                        if (bytes > MaxPromptBytes)
                        {
                            BattleLuckPlugin.LogWarning($"[PromptContextLoader] Event '{modeId}' prompt.txt exceeds {MaxPromptBytes} bytes, ignoring override.");
                        }
                        else
                        {
                            context.Narrative = promptText.Trim();
                        }
                    }
                }
                catch (Exception ex)
                {
                    BattleLuckPlugin.LogWarning($"[PromptContextLoader] Failed to read prompt.txt for '{modeId}': {ex.Message}");
                }
            }

            return context;
        }
        catch (Exception ex)
        {
            BattleLuckPlugin.LogWarning($"[PromptContextLoader] Failed to load AI config from {jsonPath}: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Parses the embedded AI policy from a candidate unified event document.
    /// Deployment validation uses this overload so it never reads a stale live
    /// event while validating a staged replacement.
    /// </summary>
    public PromptContext? ParseEventJson(string eventJson, string fallbackEventId = "")
    {
        if (string.IsNullOrWhiteSpace(eventJson))
            return null;

        try
        {
            using var document = JsonDocument.Parse(eventJson);
            var root = document.RootElement;
            if (!root.TryGetProperty("ai", out var ai) || ai.ValueKind != JsonValueKind.Object)
                return null;

            var context = new PromptContext
            {
                EventId = ReadEventId(root, fallbackEventId),
                Enabled = ReadBoolean(ai, "enabled", true)
            };

            if (!context.Enabled)
                return context;

            if (ai.TryGetProperty("prompt", out var prompt) && prompt.ValueKind == JsonValueKind.Object)
                ReadPrompt(prompt, context);

            if (ai.TryGetProperty("policy", out var policy) && policy.ValueKind == JsonValueKind.Object)
                ReadPolicy(policy, context);

            if (ai.TryGetProperty("examples", out var examples) && examples.ValueKind == JsonValueKind.Array)
                ReadExamples(examples, context);

            NormalizeActions(context.AllowedActions, context.EventId, "allowedActions");
            NormalizeActions(context.BlockedActions, context.EventId, "blockedActions");
            NormalizeActions(context.RequireApproval, context.EventId, "requireApproval");

            var overlap = context.AllowedActions
                .Intersect(context.BlockedActions, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (overlap.Length > 0)
            {
                BattleLuckPlugin.LogWarning(
                    $"[PromptContextLoader] Event '{context.EventId}' contains actions in both allowedActions and blockedActions: {string.Join(", ", overlap)}");
            }

            context.Narrative = BuildNarrative(context);
            return context;
        }
        catch (Exception ex)
        {
            BattleLuckPlugin.LogWarning($"[PromptContextLoader] Failed to parse staged event AI policy: {ex.Message}");
            return null;
        }
    }

    static string ReadEventId(JsonElement root, string fallback)
    {
        if (root.TryGetProperty("metadata", out var metadata) &&
            metadata.ValueKind == JsonValueKind.Object &&
            metadata.TryGetProperty("id", out var id) &&
            id.ValueKind == JsonValueKind.String)
        {
            return id.GetString() ?? fallback;
        }

        return fallback;
    }

    static bool ReadBoolean(JsonElement element, string propertyName, bool fallback)
    {
        if (!element.TryGetProperty(propertyName, out var value))
            return fallback;
        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => fallback
        };
    }

    static void ReadPrompt(JsonElement prompt, PromptContext context)
    {
        context.Role = ReadString(prompt, "role");
        context.Objective = ReadString(prompt, "objective");
        context.ResponseMode = ReadString(prompt, "responseMode", "action_plan");
        context.IncludeActionCatalog = ReadBoolean(prompt, "includeActionCatalog", true);
        ReadStringArray(prompt, "instructions", context.Instructions);
    }

    static void ReadPolicy(JsonElement policy, PromptContext context)
    {
        ReadStringArray(policy, "allowedActions", context.AllowedActions);
        ReadStringArray(policy, "blockedActions", context.BlockedActions);
        ReadStringArray(policy, "allowedTechs", context.AllowedTechs);
        ReadStringArray(policy, "requireApproval", context.RequireApproval);
    }

    static void ReadExamples(JsonElement examples, PromptContext context)
    {
        foreach (var example in examples.EnumerateArray())
        {
            if (example.ValueKind != JsonValueKind.Object)
                continue;

            var request = ReadString(example, "request");
            if (string.IsNullOrWhiteSpace(request))
                continue;

            var actions = example.TryGetProperty("actions", out var actionElement)
                ? actionElement.Clone()
                : default;

            context.Examples.Add(new PromptExample
            {
                Request = request,
                Actions = actions
            });
        }
    }

    static string ReadString(JsonElement element, string propertyName, string fallback = "")
    {
        if (!element.TryGetProperty(propertyName, out var value) || value.ValueKind != JsonValueKind.String)
            return fallback;
        return value.GetString() ?? fallback;
    }

    static void ReadStringArray(JsonElement element, string propertyName, ICollection<string> target)
    {
        if (!element.TryGetProperty(propertyName, out var values) || values.ValueKind != JsonValueKind.Array)
            return;

        foreach (var value in values.EnumerateArray())
        {
            if (value.ValueKind != JsonValueKind.String)
                continue;
            var text = value.GetString()?.Trim();
            if (!string.IsNullOrWhiteSpace(text) && !target.Contains(text, StringComparer.OrdinalIgnoreCase))
                target.Add(text);
        }
    }

    void NormalizeActions(List<string> values, string eventId, string scope)
    {
        for (var i = 0; i < values.Count; i++)
        {
            var original = values[i].Trim();
            var canonical = _actions.NormalizeActionName(original);
            values[i] = canonical;

            if (!_actions.IsKnown(canonical))
            {
                BattleLuckPlugin.LogWarning(
                    $"[PromptContextLoader] Event '{eventId}' {scope} contains unknown action '{original}'. Startup validation will reject it.");
            }
            else if (!original.Equals(canonical, StringComparison.OrdinalIgnoreCase))
            {
                BattleLuckPlugin.LogInfo(
                    $"[PromptContextLoader] Event '{eventId}' mapped action alias '{original}' to '{canonical}'.");
            }
        }

        var unique = values
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        values.Clear();
        values.AddRange(unique);
    }

    static string BuildNarrative(PromptContext context)
    {
        var sb = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(context.Role))
            sb.AppendLine($"Role: {context.Role}");
        if (!string.IsNullOrWhiteSpace(context.Objective))
            sb.AppendLine($"Objective: {context.Objective}");
        if (context.Instructions.Count > 0)
        {
            sb.AppendLine("Instructions:");
            foreach (var instruction in context.Instructions)
                sb.AppendLine($"- {instruction}");
        }
        return sb.ToString().Trim();
    }

    /// <summary>
    /// Builds the complete event-scoped LLM prompt. The model receives canonical
    /// action names and catalog metadata, then must return structured action_plan
    /// JSON. Execution still passes through the normal validation pipeline.
    /// </summary>
    public string BuildPrompt(PromptContext context, RuntimeActionContext runtimeContext)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Event: {context.EventId}");
        sb.AppendLine($"Response mode: {context.ResponseMode}");
        sb.AppendLine();
        sb.AppendLine(context.Narrative);
        sb.AppendLine();

        sb.AppendLine("Return JSON only using this contract:");
        sb.AppendLine("{\"type\":\"action_plan\",\"reason\":\"short explanation\",\"actions\":[{\"action\":\"canonical.action\",\"params\":{}}]}");
        sb.AppendLine("Never invent action names or parameter names.");
        sb.AppendLine();

        if (context.AllowedActions.Count > 0)
        {
            sb.AppendLine("Allowed actions:");
            foreach (var actionName in context.AllowedActions)
            {
                if (context.IncludeActionCatalog && _actions.TryGetAction(actionName, out var entry) && entry != null)
                {
                    var required = entry.Required.Count > 0 ? string.Join(",", entry.Required) : "none";
                    var optional = entry.Optional.Count > 0 ? string.Join(",", entry.Optional) : "none";
                    sb.AppendLine($"- {entry.Name}: {entry.Description} | required={required} | optional={optional} | risk={entry.RiskLevel} | approval={entry.RequiresApproval}");
                }
                else
                {
                    sb.AppendLine($"- {actionName}");
                }
            }
            sb.AppendLine();
        }

        if (context.BlockedActions.Count > 0)
            sb.AppendLine($"Blocked actions: {string.Join(", ", context.BlockedActions)}");
        if (context.RequireApproval.Count > 0)
            sb.AppendLine($"Actions requiring approval: {string.Join(", ", context.RequireApproval)}");
        if (context.AllowedTechs.Count > 0)
            sb.AppendLine($"Allowed techs: {string.Join(", ", context.AllowedTechs)}");

        if (runtimeContext.ZoneDefinition != null)
        {
            sb.AppendLine();
            sb.AppendLine($"Current zone: {runtimeContext.ZoneDefinition.Name}");
            sb.AppendLine($"Zone coordinates: ({runtimeContext.ZoneDefinition.Position.X}, {runtimeContext.ZoneDefinition.Position.Y}, {runtimeContext.ZoneDefinition.Position.Z})");
            sb.AppendLine($"Zone radius: {runtimeContext.ZoneDefinition.Radius}");
        }

        if (context.Examples.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("Validated examples:");
            foreach (var example in context.Examples)
            {
                sb.AppendLine($"Request: {example.Request}");
                if (example.Actions.ValueKind != JsonValueKind.Undefined)
                    sb.AppendLine($"Actions: {example.Actions.GetRawText()}");
            }
        }

        return sb.ToString();
    }
}