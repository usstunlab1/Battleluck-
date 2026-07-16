using System.Text.Json.Serialization;

namespace BattleLuck.Models;

/// <summary>
/// AI rules for zone behavior - controls what actions the AI can use in this zone.
/// </summary>
public sealed class ZoneAiRules
{
    /// <summary>
    /// Actions that the AI is explicitly allowed to use.
    /// If empty, all actions are allowed except those in BlockedActions.
    /// </summary>
    [JsonPropertyName("allowedActions")]
    public List<string> AllowedActions { get; set; } = new();

    /// <summary>
    /// Actions that the AI is explicitly blocked from using.
    /// These take precedence over AllowedActions.
    /// </summary>
    [JsonPropertyName("blockedActions")]
    public List<string> BlockedActions { get; set; } = new();

    /// <summary>
    /// Tech IDs that the AI can enable for this zone.
    /// If empty, AI cannot activate techs.
    /// </summary>
    [JsonPropertyName("allowedTechs")]
    public List<string> AllowedTechs { get; set; } = new();

    /// <summary>
    /// Whether the AI can autonomously execute actions without approval.
    /// </summary>
    [JsonPropertyName("allowAutonomousExecution")]
    public bool AllowAutonomousExecution { get; set; } = false;
}

/// <summary>
/// Schematic configuration for zone-owned assets.
/// </summary>
public sealed class ZoneSchematic
{
    /// <summary>
    /// Schematic ID to load.
    /// </summary>
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    /// <summary>
    /// Whether to load the schematic when a player enters the zone.
    /// </summary>
    [JsonPropertyName("loadOnEnter")]
    public bool LoadOnEnter { get; set; } = false;

    /// <summary>
    /// Whether to clear the schematic when the zone is vacated.
    /// </summary>
    [JsonPropertyName("clearOnExit")]
    public bool ClearOnExit { get; set; } = false;

    /// <summary>
    /// Position offset for schematic placement.
    /// </summary>
    [JsonPropertyName("offset")]
    public Vec3Config? Offset { get; set; }
}

/// <summary>
/// Prompt context injected into AI when operating in this zone.
/// </summary>
public sealed class PromptContext
{
    /// <summary>
    /// Event ID this prompt context belongs to.
    /// </summary>
    [JsonPropertyName("eventId")]
    public string EventId { get; set; } = "";

    /// <summary>
    /// Allowed actions in this context.
    /// </summary>
    [JsonPropertyName("allowedActions")]
    public List<string> AllowedActions { get; set; } = new();

    /// <summary>
    /// Blocked actions in this context.
    /// </summary>
    [JsonPropertyName("blockedActions")]
    public List<string> BlockedActions { get; set; } = new();

    /// <summary>
    /// Allowed techs in this context.
    /// </summary>
    [JsonPropertyName("allowedTechs")]
    public List<string> AllowedTechs { get; set; } = new();

    /// <summary>
    /// Narrative description for AI context.
    /// </summary>
    [JsonPropertyName("narrative")]
    public string Narrative { get; set; } = "";
}