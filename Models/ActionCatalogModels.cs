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
        if (!double.TryParse(left.ToString(), out var lval) ||
            !double.TryParse(right, out var rval))
            return 0;
        return lval.CompareTo(rval);
    }
}
