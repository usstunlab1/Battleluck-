// ── prefab.json model ───────────────────────────────────────────────────
// New additive model introduced during Stage A scaffolding for the planned
// per-mode prefabs/*.prefab.json files. Not yet referenced by ModeConfig or
// the loaders; later stages (PrefabValidator + AI ingestion) will wire it in.
// Declared in the global namespace to match the rest of the config model graph.

/// <summary>
/// Model for a single AI-generated prefab definition. Carries the prefab
/// GUID/name so PrefabValidator can verify it against the game's registry
/// before anything is injected into the live ECS.
/// </summary>
public sealed class PrefabConfig
{
    /// <summary>Logical name/key for the prefab within the mode.</summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    /// <summary>Prefab GUID hash as an integer (Stunlock PrefabGUID value).</summary>
    [JsonPropertyName("guid")]
    public int Guid { get; set; }

    /// <summary>Optional prefab name/alias used when a GUID is not supplied.</summary>
    [JsonPropertyName("prefabName")]
    public string PrefabName { get; set; } = "";
}
