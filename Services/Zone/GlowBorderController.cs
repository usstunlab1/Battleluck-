using Stunlock.Core;
using Unity.Entities;
using Unity.Mathematics;

/// <summary>
/// Scans environment entities (trees, rocks) in a donut ring around the moving arena edge
/// and applies glow effects. MAX_GLOW_BORDER entities to avoid perf issues.
/// Batched attach — processes N per tick.
/// </summary>
public sealed class GlowBorderController
{
    const int MAX_GLOW_BORDER = 50;

    readonly List<Entity> _glowedEntities = new();
    readonly Queue<Entity> _pendingGlow = new();
    float3 _lastCenter;
    float _lastRadius;
    float _scanWidth = 10f;
    int _batchSize = 10;
    bool _configured;

    public int GlowCount => _glowedEntities.Count;
    public bool IsScanning => _pendingGlow.Count > 0;

    public void Configure(GlowBorderConfig? config)
    {
        if (config == null || !config.Enabled)
        {
            _configured = false;
            return;
        }
        _scanWidth = config.ScanWidth;
        _batchSize = Math.Max(1, config.BatchSize);
        _configured = true;
    }

    /// <summary>
    /// Called when the arena center or radius changes. Clears old glow, scans new donut ring.
    /// </summary>
    public void OnWaypointAdvance(float3 newCenter, float newRadius)
    {
        if (!_configured) return;

        ClearAllGlow();
        _lastCenter = newCenter;
        _lastRadius = newRadius;
        ScanAndQueue(newCenter, newRadius);
    }

    /// <summary>Tick-driven batch glow attach. Call each frame.</summary>
    public void Tick()
    {
        if (!_configured || _pendingGlow.Count == 0) return;

        int processed = 0;
        while (_pendingGlow.Count > 0 && processed < _batchSize && _glowedEntities.Count < MAX_GLOW_BORDER)
        {
            var entity = _pendingGlow.Dequeue();
            try
            {
                var em = VRisingCore.EntityManager;
                if (em.Exists(entity) && Prefabs.Buff_General_Ignite != PrefabGUID.Empty)
                {
                    entity.TryApplyBuff(Prefabs.Buff_General_Ignite);
                    _glowedEntities.Add(entity);
                }
            }
            catch (Exception ex)
            {
                BattleLuckPlugin.LogWarning($"[GlowBorder] Failed to glow entity: {ex.Message}");
            }
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
                if (em.Exists(entity) && Prefabs.Buff_General_Ignite != PrefabGUID.Empty)
                    entity.TryRemoveBuff(Prefabs.Buff_General_Ignite);
            }
            catch { /* best-effort cleanup */ }
        }
        _glowedEntities.Clear();
    }

    void ScanAndQueue(float3 center, float radius)
    {
        // Donut ring: from (radius - scanWidth) to (radius + scanWidth)
        float innerR = Math.Max(0, radius - _scanWidth);
        float outerR = radius + _scanWidth;
        float innerSq = innerR * innerR;
        float outerSq = outerR * outerR;

        try
        {
            var em = VRisingCore.EntityManager;
            // Query all entities with Translation — filter by distance to center in XZ plane
            // In V Rising ECS, environment objects have LocalToWorld or Translation
            var query = em.CreateEntityQuery(
                Unity.Entities.ComponentType.ReadOnly<Unity.Transforms.Translation>()
            );
            var entities = query.ToEntityArray(Unity.Collections.Allocator.Temp);
            try
            {
                for (int i = 0; i < entities.Length && _pendingGlow.Count < MAX_GLOW_BORDER; i++)
                {
                    var e = entities[i];
                    if (_glowedEntities.Contains(e)) continue;

                    try
                    {
                        var pos = em.GetComponentData<Unity.Transforms.Translation>(e).Value;
                        float dx = pos.x - center.x;
                        float dz = pos.z - center.z;
                        float distSq = dx * dx + dz * dz;

                        if (distSq >= innerSq && distSq <= outerSq)
                        {
                            _pendingGlow.Enqueue(e);
                        }
                    }
                    catch { /* skip unreadable entities */ }
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
            BattleLuckPlugin.LogWarning($"[GlowBorder] Scan failed: {ex.Message}");
        }
    }
}
