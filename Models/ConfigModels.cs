// ── Config Models ─────────────────────────────────────────────────────────
// Shared configuration types extracted from Core/ConfigLoader.cs for proper visibility
// across the codebase. All types are in the global namespace to match the plugin's
// coding conventions.
// ─────────────────────────────────────────────────────────────────────────────

// NOTE: The following types are now defined in Core/ConfigLoader.cs:
//   DelayConfig, ZonesConfig, DetectionConfig, AutoEnterConfig, Vec3Config,
//   ZoneDefinition, BoundaryConfig, DotBoundaryConfig, WallBoundaryConfig,
//   BorderBuffEntry, BorderTimerEntry, FlowConfig, FlowDefinition
// They are kept here only for backward compatibility references.

/// <summary>AI rules specific to a zone.</summary>
public sealed class ZoneAiRules
{
    /// <summary>Actions that the AI is explicitly allowed to use.</summary>
    [JsonPropertyName("allowedActions")]
    public List<string> AllowedActions { get; set; } = new();

    /// <summary>Actions that the AI is explicitly blocked from using.</summary>
    [JsonPropertyName("blockedActions")]
    public List<string> BlockedActions { get; set; } = new();

    /// <summary>Tech IDs that the AI can enable for this zone.</summary>
    [JsonPropertyName("allowedTechs")]
    public List<string> AllowedTechs { get; set; } = new();

    /// <summary>Whether the AI can autonomously execute actions without approval.</summary>
    [JsonPropertyName("allowAutonomousExecution")]
    public bool AllowAutonomousExecution { get; set; } = false;
}

/// <summary>Schematic configuration for zone enter/exit.</summary>
public sealed class ZoneSchematic
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("loadOnEnter")]
    public bool LoadOnEnter { get; set; } = true;

    [JsonPropertyName("clearOnExit")]
    public bool ClearOnExit { get; set; } = true;
}
