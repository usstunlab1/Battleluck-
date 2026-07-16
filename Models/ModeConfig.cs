// ── Mode configuration aggregate ────────────────────────────────────────
// Extracted from Core/ConfigLoader.cs (Stage A) so future phases have a
// stable, discoverable model target. Declared in the global namespace to
// match the rest of the config model graph.

/// <summary>
/// Aggregate configuration for a single game mode, composed from the
/// per-file configs under config/BattleLuck/&lt;modeId&gt;/.
/// </summary>
public sealed class ModeConfig
{
    public string ModeId { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string Description { get; set; } = "";
    public int Version { get; set; } = 1;
    public string KitId { get; set; } = "";

    public SessionConfig Session { get; set; } = new();
    public RulesConfig Rules { get; set; } = new();
    public ZonesConfig Zones { get; set; } = new();
    public KitConfig KitConfig { get; set; } = new();
    public ManifestConfig Manifest { get; set; } = new();
    public FlowConfig FlowEnter { get; set; } = new();
    public FlowConfig FlowExit { get; set; } = new();

    public KitConfig? Kit { get; set; }
    public WallBoundaryConfig? Border { get; set; }
    public PrefabConfig[] Prefabs { get; set; } = Array.Empty<PrefabConfig>();
    public BattleLuck.Models.SchematicConfig[] Schematics { get; set; } = Array.Empty<BattleLuck.Models.SchematicConfig>();

    /// <summary>
    /// Parsed frontmatter from an Events/&lt;modeId&gt;/prompt.txt, when present.
    /// Fed by <c>ConfigAdapter</c> so Phase 6/7 validation can read allowed/blocked
    /// actions without re-parsing the file.
    /// </summary>
    public EventPromptDefinition? EventPrompt { get; set; }
}
