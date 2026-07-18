namespace BattleLuck.Models;

public sealed class UnifiedEventDefinition
{
    [JsonPropertyName("metadata")]
    public EventMetadata Metadata { get; set; } = new();

    [JsonPropertyName("rules")]
    public EventRulesDefinition Rules { get; set; } = new();

    [JsonPropertyName("arenaRotation")]
    public EventArenaRotationDefinition? ArenaRotation { get; set; }

    [JsonPropertyName("zones")]
    public List<EventZoneDefinition> Zones { get; set; } = new();

    [JsonPropertyName("objects")]
    public List<EventObjectDefinition> Objects { get; set; } = new();

    [JsonPropertyName("glows")]
    public List<EventGlowDefinition> Glows { get; set; } = new();

    // Canonical unified event-controlled entities collection
    // Replaces legacy boss/VBlood-specific collections.
    [JsonPropertyName("entities")]
    public List<EventEntityDefinition> Entities { get; set; } = new();

    [JsonPropertyName("vBloodList")]
    public List<EventVBloodDefinition> VBloodList { get; set; } = new();

    [JsonPropertyName("phases")]
    public List<EventPhaseDefinition> Phases { get; set; } = new();

    [JsonPropertyName("timers")]
    public List<EventTimerDefinition> Timers { get; set; } = new();

    [JsonPropertyName("triggers")]
    public List<EventTriggerDefinition> Triggers { get; set; } = new();

    [JsonPropertyName("actions")]
    public List<EventActionDefinition> Actions { get; set; } = new();
}

public sealed class EventMetadata
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("displayName")]
    public string DisplayName { get; set; } = "";

    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = true;

    [JsonPropertyName("version")]
    public string Version { get; set; } = "1";

    [JsonPropertyName("prompt")]
    public string Prompt { get; set; } = "";
}

public sealed class EventRulesDefinition
{
    [JsonPropertyName("minPlayers")]
    public int? MinPlayers { get; set; }

    [JsonPropertyName("adminTestMinPlayers")]
    public int? AdminTestMinPlayers { get; set; }

    [JsonPropertyName("allowAdminSoloTest")]
    public bool? AllowAdminSoloTest { get; set; }

    [JsonPropertyName("maxPlayers")]
    public int? MaxPlayers { get; set; }

    [JsonPropertyName("enablePvP")]
    public bool? EnablePvP { get; set; }

    [JsonPropertyName("enableVBloods")]
    public bool? EnableVBloods { get; set; }

    [JsonPropertyName("enableEliteMobs")]
    public bool? EnableEliteMobs { get; set; }

    [JsonPropertyName("matchDurationMinutes")]
    public int? MatchDurationMinutes { get; set; }

    [JsonPropertyName("allowLateJoin")]
    public bool? AllowLateJoin { get; set; }

    [JsonPropertyName("requireReadyCheck")]
    public bool? RequireReadyCheck { get; set; }

    [JsonPropertyName("restrictGear")]
    public bool? RestrictGear { get; set; }

    [JsonPropertyName("shareLoot")]
    public bool? ShareLoot { get; set; }

    [JsonPropertyName("resetOnExit")]
    public bool? ResetOnExit { get; set; }

    [JsonPropertyName("eliminationMode")]
    public bool? EliminationMode { get; set; }

    [JsonPropertyName("livesPerPlayer")]
    public int? LivesPerPlayer { get; set; }

    [JsonPropertyName("zoneEnterRule")]
    public string? ZoneEnterRule { get; set; }

    [JsonPropertyName("actionStaging")]
    public ActionStagingRules? ActionStaging { get; set; }

    [JsonPropertyName("eventConsole")]
    public EventConsoleSettings? EventConsole { get; set; }
}

public sealed class EventArenaRotationDefinition
{
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; }

    [JsonPropertyName("selection")]
    public string Selection { get; set; } = "round_robin";

    [JsonPropertyName("radius")]
    public float Radius { get; set; }

    [JsonPropertyName("exitRadius")]
    public float ExitRadius { get; set; }

    [JsonPropertyName("points")]
    public List<EventArenaRotationPoint> Points { get; set; } = new();
}

public sealed class EventArenaRotationPoint
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("center")]
    public Vec3Config Center { get; set; } = new();

    [JsonPropertyName("sourceA")]
    public float SourceA { get; set; }

    [JsonPropertyName("sourceB")]
    public float SourceB { get; set; }
}

public sealed class EventZoneDefinition
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("type")]
    public string Type { get; set; } = "arena";

    [JsonPropertyName("hash")]
    public int Hash { get; set; }

    [JsonPropertyName("priority")]
    public int Priority { get; set; } = 1;

    [JsonPropertyName("kitId")]
    public string KitId { get; set; } = "";

    [JsonPropertyName("center")]
    public Vec3Config Center { get; set; } = new();

    [JsonPropertyName("teleportSpawn")]
    public Vec3Config TeleportSpawn { get; set; } = new();

    [JsonPropertyName("radius")]
    public float Radius { get; set; } = 60f;

    [JsonPropertyName("exitRadius")]
    public float ExitRadius { get; set; } = 65f;

    [JsonPropertyName("safe")]
    public bool Safe { get; set; }

    [JsonPropertyName("boundaryPolicy")]
    public string BoundaryPolicy { get; set; } = "none";

    [JsonPropertyName("blockedActions")]
    public List<string> BlockedActions { get; set; } = new();

    [JsonPropertyName("boundary")]
    public BoundaryConfig? Boundary { get; set; }

    [JsonPropertyName("shrink")]
    public JsonElement? Shrink { get; set; }

    [JsonPropertyName("schematic")]
    public ZoneSchematic? Schematic { get; set; }

    public ZoneDefinition ToZoneDefinition()
    {
        return new ZoneDefinition
        {
            Name = string.IsNullOrWhiteSpace(Name) ? $"event-zone-{Hash}" : Name,
            Type = Type,
            Hash = Hash,
            Priority = Priority,
            KitId = KitId,
            Position = Center,
            TeleportSpawn = TeleportSpawn,
            Radius = Radius,
            ExitRadius = ExitRadius > 0 ? ExitRadius : Radius + 5f,
            IsSafe = Safe,
            BlockedActions = BlockedActions,
            Boundary = Boundary ?? new BoundaryConfig { Policy = BoundaryPolicy },
            Schematic = Schematic == null ? null : new global::ZoneSchematic
            {
                Id = Schematic.Id,
                LoadOnEnter = Schematic.LoadOnEnter,
                ClearOnExit = Schematic.ClearOnExit
            }
        };
    }
}

public sealed class EventObjectDefinition
{
    [JsonPropertyName("group")]
    public string Group { get; set; } = "";

    [JsonPropertyName("kind")]
    public string Kind { get; set; } = "prefab";

    [JsonPropertyName("prefab")]
    public string Prefab { get; set; } = "";

    [JsonPropertyName("schematic")]
    public string Schematic { get; set; } = "";

    /// <summary>
    /// Controls what is spawned from this object's schematic.
    /// Values: "all" (default), "structures_only", "items_only", "world_map".
    /// Overrides the schematic's own targetScope when set to a non-empty value other than "all".
    /// </summary>
    [JsonPropertyName("targetScope")]
    public string TargetScope { get; set; } = "";

    /// <summary>
    /// Optional whitelist of structure types to include when loading from a schematic.
    /// If empty, all structure types are included. Example: ["wall", "gate", "floor"].
    /// Only applies when kind is "schematic" and the schematic targets structures.
    /// </summary>
    [JsonPropertyName("structureFilter")]
    public List<string> StructureFilter { get; set; } = new();

    /// <summary>
    /// Whether to create a world map marker for this object group.
    /// When true, a map icon is placed at the object's position.
    /// </summary>
    [JsonPropertyName("mapVisible")]
    public bool MapVisible { get; set; }

    [JsonPropertyName("position")]
    public Vec3Config Position { get; set; } = new();

    [JsonPropertyName("actions")]
    public List<EventActionDefinition> Actions { get; set; } = new();
}

public sealed class EventGlowDefinition
{
    [JsonPropertyName("group")]
    public string Group { get; set; } = "";

    [JsonPropertyName("kind")]
    public string Kind { get; set; } = "zone";

    [JsonPropertyName("target")]
    public string Target { get; set; } = "";

    [JsonPropertyName("prefab")]
    public string Prefab { get; set; } = "";

    [JsonPropertyName("radius")]
    public float Radius { get; set; }

    [JsonPropertyName("actions")]
    public List<EventActionDefinition> Actions { get; set; } = new();
}

/// <summary>
/// Unified event-controlled entity definition (NPC, elite, vblood, servant, companion, wave, objective, merchant).
/// </summary>
public sealed class EventEntityDefinition
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("entityType")]
    public string EntityType { get; set; } = "npc"; // npc | elite | vblood | servant | companion | wave | objective | merchant

    [JsonPropertyName("prefab")]
    public string Prefab { get; set; } = "";

    [JsonPropertyName("position")]
    public EventVector3 Position { get; set; } = new();

    [JsonPropertyName("homeRadius")]
    public float HomeRadius { get; set; } = 0f;

    [JsonPropertyName("behavior")]
    public string Behavior { get; set; } = "hold"; // hold | guard | follow | aggro | wander

    [JsonPropertyName("spawnSequence")]
    public string SpawnSequence { get; set; } = ""; // UUID sequence id

    [JsonPropertyName("deathSequence")]
    public string DeathSequence { get; set; } = ""; // UUID sequence id
}

public sealed class EventVector3
{
    [JsonPropertyName("x")]
    public float X { get; set; }

    [JsonPropertyName("y")]
    public float Y { get; set; }

    [JsonPropertyName("z")]
    public float Z { get; set; }
}

public sealed class EventBossDefinition
{
    [JsonPropertyName("bossId")]
    public string BossId { get; set; } = "";

    [JsonPropertyName("prefab")]
    public string Prefab { get; set; } = "";

    [JsonPropertyName("level")]
    public int Level { get; set; }

    [JsonPropertyName("homeRadius")]
    public float HomeRadius { get; set; } = 30f;

    [JsonPropertyName("faction")]
    public string Faction { get; set; } = "";

    [JsonPropertyName("team")]
    public string Team { get; set; } = "";

    [JsonPropertyName("behavior")]
    public string Behavior { get; set; } = "";

    [JsonPropertyName("spawnTrigger")]
    public string SpawnTrigger { get; set; } = "";

    [JsonPropertyName("healthTriggers")]
    public List<EventTriggerDefinition> HealthTriggers { get; set; } = new();

    [JsonPropertyName("deathActions")]
    public List<EventActionDefinition> DeathActions { get; set; } = new();

    [JsonPropertyName("servants")]
    public List<EventBossServantDefinition> Servants { get; set; } = new();
}

public sealed class EventBossServantDefinition
{
    [JsonPropertyName("group")]
    public string Group { get; set; } = "";

    [JsonPropertyName("prefab")]
    public string Prefab { get; set; } = "";

    [JsonPropertyName("count")]
    public int Count { get; set; } = 1;
}

public sealed class EventVBloodDefinition
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("prefab")]
    public string Prefab { get; set; } = "";

    [JsonPropertyName("prefabGuid")]
    public int PrefabGuid { get; set; }

    [JsonPropertyName("level")]
    public int Level { get; set; }

    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = true;

    [JsonPropertyName("location")]
    public Vec3Config Location { get; set; } = new();

    [JsonPropertyName("homeRadius")]
    public float HomeRadius { get; set; } = 30f;

    [JsonPropertyName("behavior")]
    public string Behavior { get; set; } = "guard";

    [JsonPropertyName("spawnTrigger")]
    public string SpawnTrigger { get; set; } = "event_start";

    [JsonPropertyName("despawnOnEventEnd")]
    public bool DespawnOnEventEnd { get; set; } = true;

    [JsonPropertyName("deathActions")]
    public List<EventActionDefinition> DeathActions { get; set; } = new();

    [JsonPropertyName("healthTriggers")]
    public List<EventTriggerDefinition> HealthTriggers { get; set; } = new();
}

public sealed class EventPhaseDefinition
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("durationSeconds")]
    public int DurationSeconds { get; set; }

    [JsonPropertyName("actions")]
    public List<EventActionDefinition> Actions { get; set; } = new();
}

public sealed class EventTimerDefinition
{
    [JsonPropertyName("timerId")]
    public string TimerId { get; set; } = "match";

    [JsonPropertyName("durationSeconds")]
    public int DurationSeconds { get; set; }

    [JsonPropertyName("startPhase")]
    public string StartPhase { get; set; } = "active";

    [JsonPropertyName("repeat")]
    public bool Repeat { get; set; }

    [JsonPropertyName("announceStart")]
    public bool AnnounceStart { get; set; }

    [JsonPropertyName("announceComplete")]
    public bool AnnounceComplete { get; set; }

    [JsonPropertyName("onCompleteActions")]
    public List<EventActionDefinition> OnCompleteActions { get; set; } = new();
}

public sealed class EventTriggerDefinition
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("filters")]
    public Dictionary<string, string> Filters { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    [JsonPropertyName("actions")]
    public List<EventActionDefinition> Actions { get; set; } = new();
}

public sealed class EventActionDefinition
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "";

    [JsonPropertyName("action")]
    public string Action { get; set; } = "";

    [JsonPropertyName("params")]
    public Dictionary<string, JsonElement> Params { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    [JsonPropertyName("aliases")]
    public List<string> Aliases { get; set; } = new();

    public string ToActionString()
    {
        if (!string.IsNullOrWhiteSpace(Action))
            return Action;

        if (string.IsNullOrWhiteSpace(Type))
            return "";

        if (Params.Count == 0)
            return Type;

        var parts = Params.Select(kv => $"{kv.Key}={FormatValue(kv.Value)}");
        return $"{Type}:{string.Join("|", parts)}";
    }

    static string FormatValue(JsonElement value)
    {
        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString() ?? "",
            JsonValueKind.Number when value.TryGetInt64(out var i) => i.ToString(CultureInfo.InvariantCulture),
            JsonValueKind.Number when value.TryGetDouble(out var d) => d.ToString(CultureInfo.InvariantCulture),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            JsonValueKind.Null => "",
            _ => value.GetRawText()
        };
    }
}

public sealed class ActionManifestEntry
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("category")]
    public string Category { get; set; } = "runtime";

    [JsonPropertyName("description")]
    public string Description { get; set; } = "";

    [JsonPropertyName("required")]
    public List<string> Required { get; set; } = new();

    [JsonPropertyName("optional")]
    public List<string> Optional { get; set; } = new();

    [JsonPropertyName("defaults")]
    public Dictionary<string, string> Defaults { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    [JsonPropertyName("examples")]
    public List<string> Examples { get; set; } = new();

    [JsonPropertyName("aliases")]
    public List<string> Aliases { get; set; } = new();

    [JsonPropertyName("riskLevel")]
    public string RiskLevel { get; set; } = "controlled";

    [JsonPropertyName("requiresApproval")]
    public bool RequiresApproval { get; set; } = true;

    [JsonPropertyName("handlerAvailable")]
    public bool HandlerAvailable { get; set; } = true;
}

public sealed class EventValidationResult
{
    public List<string> Errors { get; } = new();
    public List<string> Warnings { get; } = new();
    public bool Success => Errors.Count == 0;
}
