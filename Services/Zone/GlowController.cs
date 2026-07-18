/// <summary>
/// Attaches glow effects to spawnable entities in a zone. Batched execution to avoid server spikes.
/// Reacts to GameEvents.OnModeStarted / OnModeEnded.
/// </summary>
public sealed class GlowController
{
    readonly List<Entity> _glowedEntities = new();
    readonly Queue<Entity> _pendingGlow = new();
    PrefabGUID _glowPrefab;
    int _batchSize = 10;
    bool _configured;

    public int GlowCount => _glowedEntities.Count;
    public bool IsAttaching => _pendingGlow.Count > 0;

    public void Configure(GlowConfig? config)
    {
        if (config == null || !config.Enabled)
        {
            _configured = false;
            return;
        }

        if (!PrefabHelper.TryGetPrefabGuid(config.Prefab, out _glowPrefab))
        {
            BattleLuckPlugin.LogWarning($"[Glow] Unknown glow prefab: {config.Prefab}");
            _configured = false;
            return;
        }

        _batchSize = Math.Max(1, config.BatchSize);
        _configured = true;
    }

    /// <summary>
    /// Begin batched glow attachment for entities in the specified zone area.
    /// </summary>
    public void AttachGlowToZoneEntities(float3 center, float radius)
    {
        if (!_configured) return;

        ClearAllGlow();
        float radiusSq = radius * radius;

        try
        {
            var em = VRisingCore.EntityManager;
            var query = em.CreateEntityQuery(
                Unity.Entities.ComponentType.ReadOnly<Unity.Transforms.Translation>()
            );
            var entities = query.ToEntityArray(Unity.Collections.Allocator.Temp);
            try
            {
                for (int i = 0; i < entities.Length; i++)
                {
                    try
                    {
                        var pos = em.GetComponentData<Unity.Transforms.Translation>(entities[i]).Value;
                        float distSq = math.distancesq(pos.xz, center.xz);
                        if (distSq <= radiusSq)
                        {
                            _pendingGlow.Enqueue(entities[i]);
                        }
                    }
                    catch { /* skip */ }
                }
            }
            finally
            {
                if (entities.IsCreated)
                    entities.Dispose();
                query.Dispose();
            }
        }
        catch (Exception ex)
        {
            BattleLuckPlugin.LogWarning($"[Glow] Zone scan failed: {ex.Message}");
        }
    }

    /// <summary>Tick-driven batch glow attach. Call each frame.</summary>
    public void Tick()
    {
        if (!_configured || _pendingGlow.Count == 0) return;

        int processed = 0;
        var em = VRisingCore.EntityManager;
        while (_pendingGlow.Count > 0 && processed < _batchSize)
        {
            var entity = _pendingGlow.Dequeue();
            try
            {
                if (em.Exists(entity))
                {
                    entity.TryApplyBuff(_glowPrefab);
                    _glowedEntities.Add(entity);
                }
            }
            catch { /* skip */ }
            processed++;
        }
    }

    public void ClearAllGlow()
    {
        _pendingGlow.Clear();
        var em = VRisingCore.EntityManager;
        foreach (var entity in _glowedEntities)
        {
            try
            {
                if (em.Exists(entity))
                    entity.TryRemoveBuff(_glowPrefab);
            }
            catch { /* best-effort */ }
        }
        _glowedEntities.Clear();
    }
}

/// <summary>Glow config for zone entities.</summary>
public sealed class GlowConfig
{
    [System.Text.Json.Serialization.JsonPropertyName("enabled")]
    public bool Enabled { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("prefab")]
    public string Prefab { get; set; } = "Buff_General_Ignite";

    [System.Text.Json.Serialization.JsonPropertyName("batchSize")]
    public int BatchSize { get; set; } = 10;

    [System.Text.Json.Serialization.JsonPropertyName("applyToSpawners")]
    public bool ApplyToSpawners { get; set; } = true;

    [System.Text.Json.Serialization.JsonPropertyName("applyToTiles")]
    public bool ApplyToTiles { get; set; }
}
