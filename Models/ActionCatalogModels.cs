using System.Text.Json;
using System.Text.Json.Serialization;

namespace BattleLuck.Models;

public sealed class ActionCatalog
{
    [JsonPropertyName("_comment")]
    public string Comment { get; set; } = "";

    [JsonPropertyName("_usage")]
    public CatalogUsage Usage { get; set; } = new();

    [JsonPropertyName("llm_guidance")]
    public LlmGuidance? LlmGuidance { get; set; }

    [JsonPropertyName("registered")]
    public List<string> Registered { get; set; } = new();

    [JsonPropertyName("examples")]
    public Dictionary<string, List<string>> Examples { get; set; } = new();

    [JsonPropertyName("actions")]
    public List<ActionDefinition> Actions { get; set; } = new();

    [JsonPropertyName("sequences")]
    public List<SequenceDefinition> Sequences { get; set; } = new();

    [JsonPropertyName("runtime_inject")]
    public Dictionary<string, List<string>> RuntimeInject { get; set; } = new();
}

public sealed class CatalogUsage
{
    [JsonPropertyName("registered")]
    public string Registered { get; set; } = "";

    [JsonPropertyName("examples")]
    public string Examples { get; set; } = "";

    [JsonPropertyName("runtime_inject")]
    public string RuntimeInject { get; set; } = "";
}

public sealed class LlmGuidance
{
    [JsonPropertyName("prefer_canonical")]
    public bool PreferCanonical { get; set; } = true;

    [JsonPropertyName("keep_legacy_compatible")]
    public bool KeepLegacyCompatible { get; set; } = true;

    [JsonPropertyName("legacy_mappings")]
    public Dictionary<string, string> LegacyMappings { get; set; } = new();

    [JsonPropertyName("rules")]
    public List<string> Rules { get; set; } = new();
}

public sealed class ActionDefinition
{
    [JsonPropertyName("actionId")]
    public string ActionId { get; set; } = "";

    [JsonPropertyName("action")]
    public string Action { get; set; } = "";

    [JsonPropertyName("params")]
    public Dictionary<string, JsonElement> Params { get; set; } = new();

    [JsonPropertyName("category")]
    public string Category { get; set; } = "";

    [JsonPropertyName("riskLevel")]
    public string RiskLevel { get; set; } = "safe";

    [JsonPropertyName("requiresApproval")]
    public bool RequiresApproval { get; set; }

    [JsonPropertyName("aliases")]
    public List<string> Aliases { get; set; } = new();

    [JsonPropertyName("description")]
    public string Description { get; set; } = "";

    [JsonPropertyName("required")]
    public List<string> Required { get; set; } = new();

    [JsonPropertyName("optional")]
    public List<string> Optional { get; set; } = new();

    // ── Server-Only Action Contract Metadata ───────────────────────────────

    /// <summary>
    /// Availability classification for this action.
    /// Values: "server_only", "unsupported", "client_required"
    /// Server-only actions must change native server-authoritative state
    /// that vanilla clients already receive through normal replication.
    /// </summary>
    [JsonPropertyName("availability")]
    public string Availability { get; set; } = "server_only";

    /// <summary>
    /// Whether this action is executable (passes the registration gate).
    /// Actions that fail the gate must be cataloged as non-executable.
    /// </summary>
    [JsonPropertyName("executable")]
    public bool Executable { get; set; } = true;

    /// <summary>
    /// Indicates if this action requires a client mod to function.
    /// Server-only actions must have this set to false.
    /// </summary>
    [JsonPropertyName("clientRequired")]
    public bool ClientRequired { get; set; } = false;

    /// <summary>
    /// Indicates if this action must run on the main thread.
    /// Most server-only actions can run on job threads.
    /// </summary>
    [JsonPropertyName("mainThreadRequired")]
    public bool MainThreadRequired { get; set; } = false;

    /// <summary>
    /// Indicates if this action uses native server replication.
    /// Server-only actions must use native replication to work with vanilla clients.
    /// </summary>
    [JsonPropertyName("usesNativeReplication")]
    public bool UsesNativeReplication { get; set; } = true;

    /// <summary>
    /// Permission level required to execute this action.
    /// Values: "admin", "admin_approval", "player", "system", "any"
    /// </summary>
    [JsonPropertyName("permission")]
    public string Permission { get; set; } = "admin";

    /// <summary>
    /// Risk level for this action.
    /// Values: "safe", "controlled", "destructive", "critical"
    /// </summary>
    [JsonPropertyName("risk")]
    public string Risk { get; set; } = "safe";

    /// <summary>
    /// Indicates if this action can be reversed/undone.
    /// </summary>
    [JsonPropertyName("reversible")]
    public bool Reversible { get; set; } = false;

    /// <summary>
    /// Indicates if this action is allowed in event contexts.
    /// </summary>
    [JsonPropertyName("eventAllowed")]
    public bool EventAllowed { get; set; } = true;

    /// <summary>
    /// The action to invoke to reverse/rollback this action, if reversible.
    /// </summary>
    [JsonPropertyName("rollbackAction")]
    public string? RollbackAction { get; set; }

    /// <summary>
    /// Handler type name responsible for executing this action.
    /// </summary>
    [JsonPropertyName("handler")]
    public string? Handler { get; set; }

    /// <summary>
    /// Validation rules for action parameters.
    /// </summary>
    [JsonPropertyName("validation")]
    public List<string> Validation { get; set; } = new();

    /// <summary>
    /// Known side effects of executing this action.
    /// </summary>
    [JsonPropertyName("sideEffects")]
    public List<string> SideEffects { get; set; } = new();
}

public sealed class SequenceDefinition
{
    [JsonPropertyName("sequenceId")]
    public string SequenceId { get; set; } = "";

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("description")]
    public string Description { get; set; } = "";

    [JsonPropertyName("steps")]
    public List<SequenceStep> Steps { get; set; } = new();
}

public sealed class SequenceStep
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("actionId")]
    public string ActionId { get; set; } = "";

    [JsonPropertyName("action")]
    public string Action { get; set; } = "";

    [JsonPropertyName("params")]
    public Dictionary<string, JsonElement> Params { get; set; } = new();

    [JsonPropertyName("delaySeconds")]
    public float DelaySeconds { get; set; }

    [JsonPropertyName("condition")]
    public ConditionDefinition? Condition { get; set; }

    [JsonPropertyName("onFailure")]
    public string? OnFailure { get; set; }
}

public sealed class ConditionDefinition
{
    [JsonPropertyName("operator")]
    public string Operator { get; set; } = "";

    [JsonPropertyName("left")]
    public string Left { get; set; } = "";

    [JsonPropertyName("right")]
    public string Right { get; set; } = "";

    public bool Evaluate(Dictionary<string, object> context)
    {
        if (!context.TryGetValue(Left, out var leftValue))
            return Operator == "exists" ? false : true;

        return Operator switch
        {
            "equals" => Equals(leftValue, Right),
            "greaterThan" => CompareNumeric(leftValue, Right) > 0,
            "lessThan" => CompareNumeric(leftValue, Right) < 0,
            "exists" => true,
            _ => true
        };
    }

    static int CompareNumeric(object left, string right)
    {
        var leftText = Convert.ToString(left, CultureInfo.InvariantCulture);
        if (!double.TryParse(leftText, NumberStyles.Float, CultureInfo.InvariantCulture, out var lval) ||
            !double.TryParse(right, NumberStyles.Float, CultureInfo.InvariantCulture, out var rval))
            return 0;
        return lval.CompareTo(rval);
    }
}
