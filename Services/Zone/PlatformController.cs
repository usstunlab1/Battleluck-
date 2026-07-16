using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using Stunlock.Core;
using Unity.Collections;
using Unity.Physics;
using Math = System.Math;

/// <summary>
/// Spawns and manages a 5x5 tile grid battle platform that follows the arena center.
/// Uses proper V Rising entity instantiation from prefab entities.
/// Tiles move with the center via SyncPlatformWithCenter().
/// </summary>
public sealed class PlatformController
{
    readonly List<PlatformTileInfo> _tiles = new();
    readonly string _reservationOwner = $"platform:{Guid.NewGuid():N}";
    float3 _lastCenter;
    int _gridSize = 5;
    float _tileSpacing = 2.5f;
    float _heightOffset = 0.1f;
    bool _glowEdge = true;
    PrefabGUID _tilePrefab;
    bool _configured;
    bool _spawned;

    public bool IsSpawned => _spawned;
    public int TileCount => _tiles.Count;

    public void Configure(MovingPlatformConfig? config)
    {
        if (config == null || !config.Enabled)
        {
            _configured = false;
            return;
        }

        _gridSize = Math.Max(1, config.GridSize);
        _tileSpacing = config.TileSpacing;
        _heightOffset = config.HeightOffset;
        _glowEdge = config.GlowEdge;

        if (!PrefabHelper.TryGetValidPrefabGuidDeep(config.TilePrefab, out _tilePrefab))
        {
            BattleLuckPlugin.LogWarning($"[Platform] Unknown or invalid tile prefab: {config.TilePrefab}");
            _configured = false;
            return;
        }

        if (!EventTileSafety.TryResolveSafeTileModelPrefab(_tilePrefab, out _, out var prefabError))
        {
            BattleLuckPlugin.LogWarning($"[Platform] Unsafe tile prefab '{config.TilePrefab}': {prefabError}");
            _configured = false;
            return;
        }

        _configured = true;
    }

    public void SpawnPlatform(float3 center)
    {
        if (!_configured || _spawned) return;

        _lastCenter = center;
        float halfGrid = (_gridSize - 1) * _tileSpacing / 2f;

        var em = VRisingCore.EntityManager;
        var pcs = VRisingCore.PrefabCollectionSystem;

        if (!pcs._PrefabGuidToEntityMap.TryGetValue(_tilePrefab, out var prefabEntity))
        {
            BattleLuckPlugin.LogWarning($"[Platform] Tile prefab {_tilePrefab.GuidHash} not found in PrefabGuidToEntityMap.");
            return;
        }

        try
        {
            var occupiedTiles = ScanOccupiedTiles();
            var skippedOccupied = 0;
            for (int row = 0; row < _gridSize; row++)
            {
                for (int col = 0; col < _gridSize; col++)
                {
                    float localX = col * _tileSpacing - halfGrid;
                    float localZ = row * _tileSpacing - halfGrid;
                    var localOffset = new float3(localX, 0, localZ);
                    var worldPos = new float3(center.x + localX, center.y + _heightOffset, center.z + localZ);
                    var tilePos = ToTilePosition(worldPos);
                    var tileKey = ToTileKey(tilePos);
                    if (occupiedTiles.Contains(tileKey))
                    {
                        skippedOccupied++;
                        continue;
                    }

                    if (!EventTileReservationService.TryReserve(_reservationOwner, tilePos, out _, out _))
                    {
                        skippedOccupied++;
                        continue;
                    }

                    bool isEdge = row == 0 || row == _gridSize - 1 || col == 0 || col == _gridSize - 1;

                    var entity = em.Instantiate(prefabEntity);

                    ConfigurePlatformTile(em, entity, worldPos, tilePos);

                    _tiles.Add(new PlatformTileInfo
                    {
                        Entity = entity,
                        LocalOffset = localOffset,
                        IsEdge = isEdge
                    });

                    if (isEdge && _glowEdge && Prefabs.Buff_General_Ignite != PrefabGUID.Empty)
                    {
                        // Infinite border fire-glow: duration 0 = no timer/expiry (persists for the whole event).
                        entity.BuffEntity(Prefabs.Buff_General_Ignite, out _, duration: 0f, persistThroughDeath: true);
                    }

                    occupiedTiles.Add(tileKey);
                }
            }

            _spawned = true;
            BattleLuckPlugin.LogInfo($"[Platform] Spawned {_tiles.Count} tiles at center ({center.x:F0}, {center.z:F0}) (occupiedSkipped={skippedOccupied})");
        }
        catch (Exception ex)
        {
            BattleLuckPlugin.LogWarning($"[Platform] Spawn failed: {ex.Message}");
            DespawnPlatform();
        }
    }

    /// <summary>
    /// Moves all tiles to match the new arena center. Only repositions if movement > 0.01.
    /// Returns the center delta for crate sync.
    /// </summary>
    public float3 SyncPlatformWithCenter(float3 newCenter)
    {
        if (!_spawned) return float3.zero;

        float3 delta = newCenter - _lastCenter;
        if (math.lengthsq(delta.xz) < 0.0001f) return float3.zero;

        var targetTiles = _tiles.Select(tile => ToTilePosition(new float3(
            newCenter.x + tile.LocalOffset.x,
            newCenter.y + _heightOffset,
            newCenter.z + tile.LocalOffset.z))).ToList();
        if (!EventTileReservationService.TryReplaceOwnerReservations(_reservationOwner, targetTiles, out var conflictingOwner))
        {
            BattleLuckPlugin.LogWarning($"[Platform] Move rejected because target tiles overlap reservation owner '{conflictingOwner}'.");
            return float3.zero;
        }

        _lastCenter = newCenter;

        foreach (var tile in _tiles)
        {
            try
            {
                if (tile.Entity.Exists())
                {
                    var worldPos = new float3(
                        newCenter.x + tile.LocalOffset.x,
                        newCenter.y + _heightOffset,
                        newCenter.z + tile.LocalOffset.z
                    );
                    ConfigurePlatformTile(VRisingCore.EntityManager, tile.Entity, worldPos, ToTilePosition(worldPos));
                }
            }
            catch { }
        }

        return delta;
    }

    public void DespawnPlatform()
    {
        var em = VRisingCore.EntityManager;
        foreach (var tile in _tiles)
        {
            try
            {
                if (em.Exists(tile.Entity))
                    tile.Entity.Destroy();
            }
            catch { }
        }
        _tiles.Clear();
        _spawned = false;
        EventTileReservationService.ReleaseOwner(_reservationOwner);
    }

    /// <summary>
    /// Checks if a player is on the platform (XZ within grid bounds, Y within tolerance).
    /// </summary>
    public bool IsPlayerOnPlatform(float3 playerPos)
    {
        if (!_spawned) return false;

        float halfExtent = (_gridSize * _tileSpacing) / 2f;
        float dx = Math.Abs(playerPos.x - _lastCenter.x);
        float dz = Math.Abs(playerPos.z - _lastCenter.z);
        float dy = Math.Abs(playerPos.y - (_lastCenter.y + _heightOffset));

        return dx <= halfExtent && dz <= halfExtent && dy <= 3.0f;
    }

    static void ConfigurePlatformTile(EntityManager em, Entity entity, float3 worldPos, int2 tilePos)
    {
        if (!entity.Has<Translation>()) em.AddComponent<Translation>(entity);
        entity.Write(new Translation { Value = worldPos });
        if (entity.Has<LastTranslation>())
            entity.Write(new LastTranslation { Value = worldPos });

        if (!entity.Has<Rotation>()) em.AddComponent<Rotation>(entity);
        entity.Write(new Rotation { Value = quaternion.identity });

        if (!entity.Has<TilePosition>()) em.AddComponent<TilePosition>(entity);
        entity.Write(new TilePosition { Tile = tilePos });

        if (!entity.Has<TileBounds>()) em.AddComponent<TileBounds>(entity);
        entity.Write(new TileBounds
        {
            Value = new BoundsMinMax { Min = tilePos, Max = tilePos }
        });

        if (!entity.Has<PhysicsCustomTags>()) em.AddComponent<PhysicsCustomTags>(entity);

        if (entity.Has<EditableTileModel>())
        {
            var editable = entity.Read<EditableTileModel>();
            editable.CanDismantle = false;
            entity.Write(editable);
        }

        EventTileSafety.StripRoomGraphComponents(em, entity, stripTileGrid: true);
    }

    static int2 ToTilePosition(float3 position)
    {
        return new int2(
            (int)math.round(position.x * 2f),
            (int)math.round(position.z * 2f));
    }

    static long ToTileKey(int2 tile)
    {
        return ((long)tile.x << 32) ^ (uint)tile.y;
    }

    static HashSet<long> ScanOccupiedTiles()
    {
        var occupied = new HashSet<long>();
        try
        {
            var em = VRisingCore.EntityManager;
            var query = em.CreateEntityQuery(ComponentType.ReadOnly<TilePosition>());
            var entities = query.ToEntityArray(Allocator.Temp);
            try
            {
                foreach (var entity in entities)
                {
                    if (!em.Exists(entity) || !em.HasComponent<TilePosition>(entity) || entity.Has<PlayerCharacter>())
                        continue;

                    occupied.Add(ToTileKey(em.GetComponentData<TilePosition>(entity).Tile));
                }
            }
            finally
            {
                entities.Dispose();
                query.Dispose();
            }
        }
        catch (Exception ex)
        {
            BattleLuckPlugin.LogWarning($"[Platform] Occupancy scan failed: {ex.Message}");
        }

        return occupied;
    }
}
