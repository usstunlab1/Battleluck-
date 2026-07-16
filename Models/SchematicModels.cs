using System.Text.Json.Serialization;
using Unity.Entities;

namespace BattleLuck.Models;

/// <summary>
/// Schematic configuration for building custom zone layouts.
/// Each event (bloodbath, colosseum, etc.) can have its own schematic.
/// </summary>
public sealed class SchematicConfig
{
    [JsonPropertyName("eventName")]
    public string EventName { get; set; } = "";

    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = true;

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("version")]
    public string Version { get; set; } = "1.0";

    /// <summary>
    /// Controls what this schematic targets when loaded.
    /// Values: "all" (default, structures + items), "structures_only", "items_only", "world_map".
    /// When "structures_only", built items are skipped during LoadIntoWorld.
    /// When "items_only", structures are skipped.
    /// When "world_map", only map markers are created without spawning entities.
    /// </summary>
    [JsonPropertyName("targetScope")]
    public string TargetScope { get; set; } = "all";

    [JsonPropertyName("metadata")]
    public SchematicMetadata Metadata { get; set; } = new();

    /// <summary>Zone center position in world coordinates</summary>
    [JsonPropertyName("center")]
    public Vec3Config Center { get; set; } = new();

    /// <summary>Summary of the captured castle design and bounds.</summary>
    [JsonPropertyName("castleDesign")]
    public SchematicCastleDesign CastleDesign { get; set; } = new();

    /// <summary>All structure placements in this schematic</summary>
    [JsonPropertyName("structures")]
    public List<SchematicStructure> Structures { get; set; } = new();

    /// <summary>Nearby item/resource pickups captured with the build.</summary>
    [JsonPropertyName("builtItems")]
    public List<SchematicBuiltItem> BuiltItems { get; set; } = new();

    /// <summary>Optional schematic layers to load/clear (for example: structures, items).</summary>
    [JsonPropertyName("targets")]
    public List<string> Targets { get; set; } = new();

    /// <summary>
    /// Map markers to create when this schematic is loaded into the world.
    /// Markers show on the world map and minimap to highlight key locations.
    /// </summary>
    [JsonPropertyName("mapMarkers")]
    public List<SchematicMapMarker> MapMarkers { get; set; } = new();

    /// <summary>Global glow/buff settings that apply to all structures unless overridden</summary>
    [JsonPropertyName("globalEffects")]
    public SchematicEffectsConfig? GlobalEffects { get; set; }

    [JsonPropertyName("chestPositions")]
    public List<Vec3Config> ChestPositions { get; set; } = new();

    [JsonPropertyName("cornerPositions")]
    public List<Vec3Config> CornerPositions { get; set; } = new();

    /// <summary>Check if this schematic targets structures (scope is "all" or "structures_only").</summary>
    [JsonIgnore]
    public bool TargetsStructures =>
        TargetScope.Equals("all", StringComparison.OrdinalIgnoreCase) ||
        TargetScope.Equals("structures_only", StringComparison.OrdinalIgnoreCase);

    /// <summary>Check if this schematic targets items (scope is "all" or "items_only").</summary>
    [JsonIgnore]
    public bool TargetsItems =>
        TargetScope.Equals("all", StringComparison.OrdinalIgnoreCase) ||
        TargetScope.Equals("items_only", StringComparison.OrdinalIgnoreCase);

    /// <summary>Check if this schematic is world-map-only (no entity spawning).</summary>
    [JsonIgnore]
    public bool IsWorldMapOnly =>
        TargetScope.Equals("world_map", StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// A single structure placement in a schematic.
/// </summary>
public sealed class SchematicStructure
{
    /// <summary>Structure type: wall, floor, tile, chain, castle, gate, ramp, etc.</summary>
    [JsonPropertyName("type")]
    public string Type { get; set; } = "wall";

    /// <summary>Prefab name or GUID (e.g., "TM_Castle_Wall_Tier02_Stone")</summary>
    [JsonPropertyName("prefab")]
    public string Prefab { get; set; } = "";

    /// <summary>Position relative to schematic center (world units)</summary>
    [JsonPropertyName("position")]
    public Vec3Config Position { get; set; } = new();

    /// <summary>Rotation in degrees (Y-axis only for most structures)</summary>
    [JsonPropertyName("rotation")]
    public float Rotation { get; set; } = 0f;

    /// <summary>Optional scale multiplier (1 = normal size)</summary>
    [JsonPropertyName("scale")]
    public float Scale { get; set; } = 1f;

    /// <summary>Whether this structure should glow/have effects</summary>
    [JsonPropertyName("hasEffects")]
    public bool HasEffects { get; set; } = false;

    /// <summary>Override glow/buff settings for this specific structure</summary>
    [JsonPropertyName("effects")]
    public SchematicEffectsConfig? Effects { get; set; }

    /// <summary>Optional label/group for organization</summary>
    [JsonPropertyName("group")]
    public string? Group { get; set; }

    /// <summary>Captured entity GUID hash for diagnostics and replay tooling.</summary>
    [JsonPropertyName("prefabGuid")]
    public int? PrefabGuid { get; set; }
}

public sealed class SchematicCastleDesign
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("source")]
    public string Source { get; set; } = "runtime_capture";

    [JsonPropertyName("capturedAtUtc")]
    public string CapturedAtUtc { get; set; } = "";

    [JsonPropertyName("radius")]
    public float Radius { get; set; }

    [JsonPropertyName("structureCount")]
    public int StructureCount { get; set; }

    [JsonPropertyName("builtItemCount")]
    public int BuiltItemCount { get; set; }

    [JsonPropertyName("bounds")]
    public SchematicBounds Bounds { get; set; } = new();

    [JsonPropertyName("notes")]
    public List<string> Notes { get; set; } = new();
}

public sealed class SchematicBounds
{
    [JsonPropertyName("min")]
    public Vec3Config Min { get; set; } = new();

    [JsonPropertyName("max")]
    public Vec3Config Max { get; set; } = new();
}

public sealed class SchematicBuiltItem
{
    [JsonPropertyName("prefab")]
    public string Prefab { get; set; } = "";

    [JsonPropertyName("prefabGuid")]
    public int PrefabGuid { get; set; }

    [JsonPropertyName("amount")]
    public int Amount { get; set; } = 1;

    [JsonPropertyName("position")]
    public Vec3Config Position { get; set; } = new();

    [JsonPropertyName("group")]
    public string Group { get; set; } = "captured_items";

    [JsonPropertyName("source")]
    public string Source { get; set; } = "item_pickup";
}

public sealed class SchematicLoadReport
{
    public string EventName { get; set; } = "";
    public Vec3Config Center { get; set; } = new();
    public float Radius { get; set; }
    public int SpawnedStructures { get; set; }
    public int SpawnedBuiltItems { get; set; }
    public int SpawnedMapMarkers { get; set; }
    public int FailedStructures { get; set; }
    public int FailedBuiltItems { get; set; }
    public int DestroyedOld { get; set; }
    public int TotalSpawned => SpawnedStructures + SpawnedBuiltItems + SpawnedMapMarkers;
}

public sealed class SchematicClearReport
{
    public string Target { get; set; } = "";
    public int DestroyedTracked { get; set; }
    public int DestroyedWorldEntities { get; set; }
    public int TotalDestroyed => DestroyedTracked + DestroyedWorldEntities;
}

public sealed class SchematicSpawnReport
{
    [JsonIgnore]
    public Entity Entity { get; set; } = Entity.Null;

    public string TrackingGroup { get; set; } = "manual_build";
    public string Prefab { get; set; } = "";
    public int PrefabGuid { get; set; }
    public int EntityIndex { get; set; }
    public Vec3Config Position { get; set; } = new();
}

/// <summary>
/// Glow and buff effects configuration for schematic structures.
/// </summary>
public sealed class SchematicEffectsConfig
{
    /// <summary>Prefab name for glow entity (e.g., fire, magic effect)</summary>
    [JsonPropertyName("glowPrefab")]
    public string? GlowPrefab { get; set; }

    /// <summary>Number of glow entities to spawn</summary>
    [JsonPropertyName("glowCount")]
    public int GlowCount { get; set; } = 1;

    /// <summary>Offset from structure position for glow entities</summary>
    [JsonPropertyName("glowOffset")]
    public Vec3Config? GlowOffset { get; set; }

    /// <summary>Buff prefab to apply to players near this structure</summary>
    [JsonPropertyName("buffPrefab")]
    public string? BuffPrefab { get; set; }

    /// <summary>Buff duration in seconds (-1 = permanent)</summary>
    [JsonPropertyName("buffDuration")]
    public float BuffDuration { get; set; } = -1f;

    /// <summary>Disable sun effects for players near this structure</summary>
    [JsonPropertyName("disableSunEffects")]
    public bool DisableSunEffects { get; set; } = false;

    /// <summary>Make players friendly to NPCs near this structure</summary>
    [JsonPropertyName("npcFriendly")]
    public bool NpcFriendly { get; set; } = false;

    /// <summary>Effect radius in world units</summary>
    [JsonPropertyName("radius")]
    public float Radius { get; set; } = 5f;
}

/// <summary>
/// A map marker to create when the schematic is loaded into the world.
/// Shows on world map and minimap.
/// </summary>
public sealed class SchematicMapMarker
{
    /// <summary>Label displayed on the map for this marker.</summary>
    [JsonPropertyName("label")]
    public string Label { get; set; } = "";

    /// <summary>Position relative to schematic center.</summary>
    [JsonPropertyName("position")]
    public Vec3Config Position { get; set; } = new();

    /// <summary>
    /// Icon style hint for future visual differentiation (e.g., "waypoint", "arena", "boss", "gate").
    /// Currently stored in config for tooling/export but not mapped to an ECS visual component at runtime.
    /// </summary>
    [JsonPropertyName("icon")]
    public string Icon { get; set; } = "waypoint";

    /// <summary>Whether to show on the minimap in addition to the world map.</summary>
    [JsonPropertyName("showOnMinimap")]
    public bool ShowOnMinimap { get; set; } = true;

    /// <summary>Render order priority (higher = drawn on top).</summary>
    [JsonPropertyName("renderOrder")]
    public int RenderOrder { get; set; } = 90;
}

/// <summary>
/// Schematic metadata and import/export helpers.
/// </summary>
public sealed class SchematicMetadata
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("author")]
    public string? Author { get; set; }

    [JsonPropertyName("created")]
    public string Created { get; set; } = "";

    [JsonPropertyName("modified")]
    public string Modified { get; set; } = "";

    [JsonPropertyName("tags")]
    public List<string> Tags { get; set; } = new();
}

public static class SchematicTargeting
{
    public static List<string> BuildDeclaredTargets(SchematicConfig schematic)
    {
        var targets = new List<string>();
        if (schematic.Structures.Count > 0)
            targets.Add("structures");
        if (schematic.BuiltItems.Count > 0)
            targets.Add("items");
        if (targets.Count == 0)
            targets.Add("structures");
        return targets;
    }
}
