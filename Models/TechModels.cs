namespace BattleLuck.Models;

public sealed class TechDefinition
{
    [JsonPropertyName("techId")]
    public string TechId { get; set; } = "";

    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = true;

    [JsonPropertyName("stackGroup")]
    public string StackGroup { get; set; } = "default";

    [JsonPropertyName("priority")]
    public int Priority { get; set; } = 0;

    [JsonPropertyName("conflicts")]
    public List<string> Conflicts { get; set; } = new();

    [JsonPropertyName("conflictMode")]
    public string ConflictMode { get; set; } = "Reject"; // Reject | ReplaceLowerPriority | Suspend

    [JsonPropertyName("description")]
    public string Description { get; set; } = "";

    [JsonPropertyName("modifiers")]
    public Dictionary<string, object> Modifiers { get; set; } = new();
}

/// <summary>JSON-serializable tech catalog root for config loading.</summary>
public sealed class TechCatalogRoot
{
    [JsonPropertyName("techs")]
    public List<TechDefinition> Techs { get; set; } = new();

    [JsonPropertyName("stackGroups")]
    public List<string> StackGroups { get; set; } = new();
}

/// <summary>Active tech set for a single event session instance.</summary>
public sealed class SessionTechState
{
    public Dictionary<string, TechDefinition> ActiveTechs { get; set; } = new(System.StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, TechDefinition> SuspendedTechs { get; set; } = new(System.StringComparer.OrdinalIgnoreCase);
    public string LastResolvedError { get; set; } = "";
}
