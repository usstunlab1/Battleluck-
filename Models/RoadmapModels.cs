namespace BattleLuck.Models;

/// <summary>
/// Server-owned roadmap configuration. The roadmap is deliberately data-driven so
/// the LLM can report the same milestones that operators and developers see.
/// </summary>
public sealed class RoadmapDefinition
{
    [JsonPropertyName("schemaVersion")]
    public int SchemaVersion { get; set; } = 1;

    [JsonPropertyName("project")]
    public string Project { get; set; } = "BattleLuck";

    [JsonPropertyName("description")]
    public string Description { get; set; } = "Server roadmap for the BattleLuck runtime, LLM director, and developer workflow.";

    [JsonPropertyName("milestones")]
    public List<RoadmapMilestone> Milestones { get; set; } = new();

    [JsonPropertyName("roles")]
    public List<RoadmapRole> Roles { get; set; } = new();
}

public sealed class RoadmapMilestone
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("title")]
    public string Title { get; set; } = "";

    [JsonPropertyName("status")]
    public string Status { get; set; } = "planned";

    [JsonPropertyName("summary")]
    public string Summary { get; set; } = "";

    [JsonPropertyName("owner")]
    public string Owner { get; set; } = "server";

    [JsonPropertyName("dependencies")]
    public List<string> Dependencies { get; set; } = new();

    [JsonPropertyName("acceptance")]
    public List<string> Acceptance { get; set; } = new();

    [JsonPropertyName("promptRefs")]
    public List<string> PromptRefs { get; set; } = new();
}

public sealed class RoadmapRole
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("title")]
    public string Title { get; set; } = "";

    [JsonPropertyName("description")]
    public string Description { get; set; } = "";

    [JsonPropertyName("promptFile")]
    public string PromptFile { get; set; } = "";

    [JsonPropertyName("capabilities")]
    public List<string> Capabilities { get; set; } = new();

    [JsonPropertyName("guardrails")]
    public List<string> Guardrails { get; set; } = new();
}

public sealed class RoadmapSnapshot
{
    public string Project { get; init; } = "BattleLuck";
    public string Description { get; init; } = "";
    public DateTime LoadedAtUtc { get; init; }
    public IReadOnlyList<RoadmapMilestone> Milestones { get; init; } = Array.Empty<RoadmapMilestone>();
    public IReadOnlyList<RoadmapRole> Roles { get; init; } = Array.Empty<RoadmapRole>();
}
