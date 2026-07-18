using Unity.Physics;
using Unity.Transforms;
using Math = System.Math;

/// <summary>
/// Spawns a ring of wall entities around a zone center using proper V Rising entity instantiation.
/// Pattern: Instantiate from prefab entity → set Translation/Rotation → PhysicsCustomTags.
/// Staggered spawning: N walls per tick to avoid server spikes.
/// Hard cap: MAX_WALLS = 100.
/// </summary>
public sealed class BorderWallController
{
    const int MAX_WALLS = 256;
    const int MAX_FLOORS = 30;
    const int MAX_WALL_BATCH_SIZE = 2;
    const int FLOOR_BATCH_SIZE = 2;

    struct PendingWall
    {
        public float3 Position;
        public float Angle;
        public int SlotIndex;
    }

    struct PendingFloor
    {
        public float3 Position;
    }

    readonly List<Entity> _spawnedWalls = new();
    readonly string _wallReservationOwner = $"border-walls:{Guid.NewGuid():N}";
    readonly string _floorReservationOwner = $"border-floors:{Guid.NewGuid():N}";
    readonly List<PendingWall> _wallSlots = new();
    readonly Queue<PendingWall> _pendingWalls = new();
    readonly HashSet<long> _queuedWallTiles = new();
    readonly HashSet<long> _processedWallTiles = new();
    readonly HashSet<long> _knownOccupiedWallTiles = new();
    readonly List<Entity> _spawnedFloors = new();
    readonly Queue<PendingFloor> _pendingFloors = new();
    readonly HashSet<long> _queuedFloorTiles = new();
    readonly HashSet<long> _processedFloorTiles = new();
    readonly HashSet<long> _knownOccupiedFloorTiles = new();
    int _floorQueuedTotal;
    int _floorOccupiedSkippedTotal;
    int _floorDedupedTotal;
    int _floorRuntimeSkippedTotal;
    int _wallOccupiedSkippedTotal;
    int _floorRoomGraphStrippedTotal;
    int _queuedWallBuilds;
    DateTime _nextNoAdminWallLogUtc = DateTime.MinValue;
    DateTime _nextNoAdminFloorLogUtc = DateTime.MinValue;
    readonly object _scanSync = new();
    float3 _center;
    float _radius;
    float _halfWidth;
    float _halfLength;
    float _height;
    int _batchSize;
    bool _spawning;
    bool _spawningFloors;
    PrefabGUID _wallPrefab;
    PrefabGUID _floorPrefab;
    float _floorSpacing = 3f;
    bool _floorVisualOnly = true;
    WallBoundaryConfig? _lastConfig;
    // Glow entities for zone border
    PrefabGUID _glowEntityPrefab;
    readonly List<Entity> _spawnedGlowEntities = new();
    int _glowEntityCount;
    float _glowRadius;
    bool _disableSunEffects;
    bool _npcFriendly;
    bool _requireOnlineAdminForBuilds = true;

    // ── Event → wall prefab mapping ─────────────────────────────────────
    static readonly Dictionary<string, string> _eventWallPrefabs = new(StringComparer.OrdinalIgnoreCase)
    {
        ["bloodbath"]  = "TM_Castle_Wall_Tier02_Stone",
        ["colosseum"]  = "TM_Castle_Wall_Tier03_Marble",
        ["siege"]      = "TM_Castle_Wall_Tier02_Stone",
        ["trials"]     = "TM_Castle_Wall_Tier03_Marble",
    };

    // ── Event → floor prefab mapping ────────────────────────────────────
    static readonly Dictionary<string, string> _eventFloorPrefabs = new(StringComparer.OrdinalIgnoreCase)
    {
        ["bloodbath"]  = "TM_Castle_Floor_Tier02_Stone",
        ["colosseum"]  = "TM_Castle_Floor_Tier03_Marble",
        ["siege"]      = "TM_Castle_Floor_Tier02_Stone",
        ["trials"]     = "TM_Castle_Floor_Tier03_Marble",
    };

    const string DefaultWallPrefabName = "TM_Castle_Wall_Tier02_Stone";
    const string DefaultFloorPrefabName = "TM_Castle_Floor_Tier02_Stone";
    static readonly TimeSpan NoAdminRetryLogInterval = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Begin staggered rectangular wall spawn. Call once, then call TickSpawn() each frame.
    /// The footprint uses radius as half-width/half-length so it matches rectangular floor.fill arenas.
    /// </summary>
    public void StartWallRing(float3 center, float radius, WallBoundaryConfig config)
    {
        lock (_scanSync)
        {
            DespawnWalls();

            _center = center;
            _radius = radius;
            _halfWidth = Math.Max(1f, radius);
            _halfLength = Math.Max(1f, radius);
            _height = config.Height;
            _batchSize = Math.Clamp(config.BatchSize, 1, MAX_WALL_BATCH_SIZE);
            _lastConfig = config;
            _requireOnlineAdminForBuilds = config.RequireOnlineAdmin;
            _spawning = true;
            _knownOccupiedWallTiles.Clear();
            _knownOccupiedWallTiles.UnionWith(ScanOccupiedWallTiles());
            _wallOccupiedSkippedTotal = 0;
            _queuedWallBuilds = 0;
            // Config prefab override takes priority, then caller-set, then default
            if (!string.IsNullOrWhiteSpace(config.WallPrefab))
                _wallPrefab = ResolveBoundaryPrefab(config.WallPrefab!, "wall");
            else if (_wallPrefab == default)
                _wallPrefab = ResolveBoundaryPrefab(DefaultWallPrefabName, "wall");

            if (!EventTileSafety.TryResolveSafeTileModelPrefab(_wallPrefab, out _, out var prefabError))
            {
                BattleLuckPlugin.LogWarning($"[BorderWallController] Wall boundary disabled: {prefabError}.");
                _spawning = false;
                return;
            }

            var spacing = Math.Max(1f, config.Spacing);
            int queued = 0;
            int deduped = 0;

            QueueRectangularWallBoundary(center, _halfWidth, _halfLength, spacing, ref queued, ref deduped);

            BattleLuckPlugin.LogInfo($"[BorderWallController] Queued {queued} rectangular walls (deduped={deduped}, size={_halfWidth * 2f:F0}x{_halfLength * 2f:F0}, spacing={spacing:F0}).");
        }
    }

    void QueueRectangularWallBoundary(float3 center, float halfWidth, float halfLength, float spacing, ref int queued, ref int deduped)
    {
        var minX = center.x - halfWidth;
        var maxX = center.x + halfWidth;
        var minZ = center.z - halfLength;
        var maxZ = center.z + halfLength;

        for (var x = minX; x <= maxX + 0.001f && queued < MAX_WALLS; x += spacing)
        {
            QueueWallSlot(new float3(x, center.y, minZ), 0f, ref queued, ref deduped);
            if (queued >= MAX_WALLS) break;
            QueueWallSlot(new float3(x, center.y, maxZ), math.PI, ref queued, ref deduped);
        }

        for (var z = minZ + spacing; z <= maxZ - spacing + 0.001f && queued < MAX_WALLS; z += spacing)
        {
            QueueWallSlot(new float3(minX, center.y, z), math.PI * 0.5f, ref queued, ref deduped);
            if (queued >= MAX_WALLS) break;
            QueueWallSlot(new float3(maxX, center.y, z), -math.PI * 0.5f, ref queued, ref deduped);
        }
    }

    void QueueWallSlot(float3 position, float angle, ref int queued, ref int deduped)
    {
        var pos = SnapToBuildGrid(position);
        var tileKey = ToTileKey(pos);
        if (_knownOccupiedWallTiles.Contains(tileKey))
        {
            _wallOccupiedSkippedTotal++;
            return;
        }

        if (!_queuedWallTiles.Add(tileKey))
        {
            deduped++;
            return;
        }

        if (!EventTileReservationService.TryReserve(_wallReservationOwner, ToTilePosition(pos), out _, out _))
        {
            _queuedWallTiles.Remove(tileKey);
            _wallOccupiedSkippedTotal++;
            return;
        }

        var slot = new PendingWall
        {
            Position = pos,
            Angle = angle,
            SlotIndex = _wallSlots.Count
        };
        _wallSlots.Add(slot);
        _spawnedWalls.Add(Entity.Null);
        _pendingWalls.Enqueue(slot);
        queued++;
    }

    /// <summary>Spawn next batch of walls. Returns true while still spawning.</summary>
    public bool TickSpawn()
    {
        lock (_scanSync)
        {
            if (!_spawning)
                return false;

            if (_pendingWalls.Count == 0)
            {
                _spawning = false;
                if (_lastConfig?.Glow?.SpawnGlowEntities != null)
                    SpawnGlowEntities(_lastConfig, _center, _radius);
                return false;
            }

            if (!EventTileSafety.TryResolveSafeTileModelPrefab(_wallPrefab, out var prefabEntity, out var prefabError))
            {
                BattleLuckPlugin.LogWarning($"[BorderWallController] Cancelling unsafe wall boundary: {prefabError}.");
                foreach (var pending in _pendingWalls)
                    EventTileReservationService.Release(_wallReservationOwner, ToTileKey(pending.Position));
                _pendingWalls.Clear();
                _queuedWallTiles.Clear();
                _spawning = false;
                return false;
            }

            int toSpawn = Math.Min(Math.Max(1, _batchSize), _pendingWalls.Count);
            var occupiedWallTiles = ScanOccupiedWallTiles();
            for (int i = 0; i < toSpawn; i++)
            {
                var pending = _pendingWalls.Dequeue();
                long tileKey = ToTileKey(pending.Position);
                _queuedWallTiles.Remove(tileKey);
                if (occupiedWallTiles.Contains(tileKey) || _knownOccupiedWallTiles.Contains(tileKey))
                {
                    _wallOccupiedSkippedTotal++;
                    EventTileReservationService.Release(_wallReservationOwner, tileKey);
                    continue;
                }

                if (!_processedWallTiles.Add(tileKey))
                {
                    BattleLuckPlugin.LogWarning($"[BorderWallController] Duplicate wall processing prevented at {pending.Position}.");
                    EventTileReservationService.Release(_wallReservationOwner, tileKey);
                    continue;
                }

                try
                {
                    // KindredSchematics-compatible path: instantiate a validated
                    // TM_ prefab directly, configure it, and retain the entity so
                    // event cleanup can destroy exactly what BattleLuck created.
                    var entity = VRisingCore.EntityManager.Instantiate(prefabEntity);
                    if (!entity.Has<PhysicsCustomTags>())
                        VRisingCore.EntityManager.AddComponent<PhysicsCustomTags>(entity);
                    ConfigureWallEntity(VRisingCore.EntityManager, entity, pending);
                    ProtectBoundaryEntity(entity);
                    ApplyWallGlowIfConfigured(entity);
                    _spawnedWalls[pending.SlotIndex] = entity;
                    occupiedWallTiles.Add(tileKey);
                    _knownOccupiedWallTiles.Add(tileKey);
                }
                catch (Exception ex)
                {
                    EventTileReservationService.Release(_wallReservationOwner, tileKey);
                    BattleLuckPlugin.LogWarning($"[BorderWallController] Failed to queue wall at {pending.Position}: {ex.Message}");
                }
            }

            if (_pendingWalls.Count == 0)
            {
                _spawning = false;
                BattleLuckPlugin.LogInfo($"[BorderWallController] All {WallCount} rectangular walls spawned and tracked (processed={_processedWallTiles.Count}, occupiedSkipped={_wallOccupiedSkippedTotal}).");
                if (_lastConfig != null && _lastConfig.Glow?.SpawnGlowEntities != null)
                    SpawnGlowEntities(_lastConfig, _center, _radius);
            }

            return _pendingWalls.Count > 0;
        }
    }

    static int ConfigureWallEntity(EntityManager em, Entity entity, PendingWall pending)
    {
        if (!entity.Has<Translation>()) em.AddComponent<Translation>(entity);
        entity.Write(new Translation { Value = pending.Position });
        if (entity.Has<LastTranslation>())
            entity.Write(new LastTranslation { Value = pending.Position });

        if (!entity.Has<Rotation>()) em.AddComponent<Rotation>(entity);
        entity.Write(new Rotation { Value = quaternion.RotateY(pending.Angle) });

        var tilePos = ToTilePosition(pending.Position);
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
            var etm = entity.Read<EditableTileModel>();
            etm.CanDismantle = false;
            entity.Write(etm);
        }

        return EventTileSafety.StripRoomGraphComponents(em, entity, stripTileGrid: true);
    }

    static void ProtectBoundaryEntity(Entity entity)
    {
        if (!entity.Exists() || entity.Has<PlayerCharacter>())
            return;

        try
        {
            if (Prefabs.Admin_Invulnerable_Buff != PrefabGUID.Empty)
                entity.BuffEntity(Prefabs.Admin_Invulnerable_Buff, out _, 0f, persistThroughDeath: true);
        }
        catch { }

        try
        {
            if (entity.Has<EditableTileModel>())
            {
                var editable = entity.Read<EditableTileModel>();
                editable.CanDismantle = false;
                entity.Write(editable);
            }
        }
        catch { }
    }

    /// <summary>Destroy all tracked wall entities.</summary>
    public void DespawnWalls()
    {
        lock (_scanSync)
        {
            var em = VRisingCore.EntityManager;
            int destroyed = 0;
            foreach (var wall in _spawnedWalls)
            {
                try
                {
                    if (em.Exists(wall))
                    {
                        if (CastleTileOwnershipService.IsPermanentCastleEntity(wall))
                            continue;
                        wall.Destroy();
                        destroyed++;
                    }
                }
                catch { }
            }
            _spawnedWalls.Clear();
            _wallSlots.Clear();
            _pendingWalls.Clear();
            _queuedWallTiles.Clear();
            _processedWallTiles.Clear();
            _knownOccupiedWallTiles.Clear();
            _wallOccupiedSkippedTotal = 0;
            _queuedWallBuilds = 0;
            _spawning = false;
            EventTileReservationService.ReleaseOwner(_wallReservationOwner);
            DespawnGlowEntities();

            if (destroyed > 0)
                BattleLuckPlugin.LogInfo($"[BorderWallController] Despawned {destroyed} walls.");
        }
    }

    /// <summary>Move all walls to new center (same ring shape, new origin).</summary>
    public void UpdateCenter(float3 newCenter, float radius)
    {
        lock (_scanSync)
        {
            if (_wallSlots.Count == 0) return;

            var delta = new float3(newCenter.x - _center.x, newCenter.y - _center.y, newCenter.z - _center.z);
            var targetTiles = _wallSlots.Select(slot => ToTilePosition(new float3(
                slot.Position.x + delta.x,
                newCenter.y,
                slot.Position.z + delta.z))).ToList();
            if (!EventTileReservationService.TryReplaceOwnerReservations(_wallReservationOwner, targetTiles, out var conflictingOwner))
            {
                BattleLuckPlugin.LogWarning($"[BorderWallController] Boundary move rejected because target tiles overlap reservation owner '{conflictingOwner}'.");
                return;
            }

            for (int i = 0; i < _wallSlots.Count; i++)
            {
                var slot = _wallSlots[i];
                slot.Position = new float3(slot.Position.x + delta.x, newCenter.y, slot.Position.z + delta.z);
                _wallSlots[i] = slot;

                try
                {
                    if (i < _spawnedWalls.Count && _spawnedWalls[i].Exists())
                    {
                        _spawnedWalls[i].SetPosition(slot.Position);
                        if (_spawnedWalls[i].Has<Rotation>())
                            _spawnedWalls[i].Write(new Rotation { Value = quaternion.RotateY(slot.Angle) });
                    }
                }
                catch { }
            }
            _center = newCenter;
            _radius = radius;
            _halfWidth = Math.Max(1f, radius);
            _halfLength = Math.Max(1f, radius);
        }
    }

    public bool IsSpawning => _spawning;
    public bool IsSpawningFloors => _spawningFloors;
    public bool HasPendingBuildWork
    {
        get
        {
            lock (_scanSync)
                return _spawning || _spawningFloors || _pendingWalls.Count > 0 || _pendingFloors.Count > 0;
        }
    }
    public int WallCount => _spawnedWalls.Count(w => w.Exists()) + _queuedWallBuilds;
    public int FloorCount => _spawnedFloors.Count;

    // ── Floor ring spawning ─────────────────────────────────────────────

    /// <summary>
    /// Queue floor tiles along the border ring perimeter. Call TickSpawnFloors() each frame.
    /// </summary>
    public void StartFloorRing(float3 center, float radius, float spacing = 2.5f, string? prefabOverride = null)
    {
        lock (_scanSync)
        {
            DespawnFloors();
            var occupied = ScanOccupiedTiles();

            _floorSpacing = Math.Max(1f, spacing);
            _floorVisualOnly = true;
            _spawningFloors = true;

            if (!string.IsNullOrWhiteSpace(prefabOverride))
                _floorPrefab = ResolveBoundaryPrefab(prefabOverride!, "floor");
            else if (_floorPrefab == default)
                _floorPrefab = ResolveBoundaryPrefab(DefaultFloorPrefabName, "floor");

            if (!EventTileSafety.TryResolveSafeTileModelPrefab(_floorPrefab, out _, out var prefabError))
            {
                BattleLuckPlugin.LogWarning($"[BorderWallController] Floor boundary disabled: {prefabError}.");
                _spawningFloors = false;
                return;
            }

            float circumference = 2f * math.PI * radius;
            int floorCount = (int)math.ceil(circumference / _floorSpacing);
            floorCount = math.min(floorCount, MAX_FLOORS);
            int queued = 0;
            int deduped = 0;
            int occupiedSkipped = 0;

            for (int i = 0; i < floorCount; i++)
            {
                float angle = 2f * math.PI * i / floorCount;
                float rawX = center.x + radius * math.cos(angle);
                float rawZ = center.z + radius * math.sin(angle);

                float worldX = math.round(rawX / 2.5f) * 2.5f;
                float worldZ = math.round(rawZ / 2.5f) * 2.5f;
                float3 tilePos = new float3(worldX, center.y, worldZ);

                long tileKey = ToTileKey(tilePos);
                if (occupied.Contains(tileKey) || _knownOccupiedFloorTiles.Contains(tileKey))
                {
                    occupiedSkipped++;
                    continue;
                }

                if (!_queuedFloorTiles.Add(tileKey))
                {
                    deduped++;
                    continue;
                }

                if (!EventTileReservationService.TryReserve(_floorReservationOwner, ToTilePosition(tilePos), out _, out _))
                {
                    _queuedFloorTiles.Remove(tileKey);
                    occupiedSkipped++;
                    continue;
                }

                _pendingFloors.Enqueue(new PendingFloor { Position = tilePos });
                queued++;
            }

            _floorQueuedTotal += queued;
            _floorOccupiedSkippedTotal += occupiedSkipped;
            _floorDedupedTotal += deduped;
        }
    }

    public (int Queued, int OccupiedSkipped, int Deduped) StartFloorFill(
        float3 center,
        float width,
        float length,
        float spacing = 2.5f,
        string? prefabOverride = null,
        int maxTiles = MAX_FLOORS,
        bool visualOnly = true)
    {
        lock (_scanSync)
        {
            _floorSpacing = Math.Max(1f, spacing);
            _floorVisualOnly = visualOnly;
            _spawningFloors = true;

            if (!string.IsNullOrWhiteSpace(prefabOverride))
                _floorPrefab = ResolveBoundaryPrefab(prefabOverride!, "floor");
            else if (_floorPrefab == default)
                _floorPrefab = ResolveBoundaryPrefab(DefaultFloorPrefabName, "floor");

            if (!EventTileSafety.TryResolveSafeTileModelPrefab(_floorPrefab, out _, out var prefabError))
            {
                BattleLuckPlugin.LogWarning($"[BorderWallController] Floor fill disabled: {prefabError}.");
                _spawningFloors = false;
                return (0, 0, 0);
            }

            width = Math.Max(_floorSpacing, width);
            length = Math.Max(_floorSpacing, length);
            maxTiles = Math.Clamp(maxTiles, 1, MAX_FLOORS);

            var columns = Math.Max(1, (int)Math.Floor(width / _floorSpacing) + 1);
            var rows = Math.Max(1, (int)Math.Floor(length / _floorSpacing) + 1);
            var startX = SnapCoordinate(center.x - ((columns - 1) * _floorSpacing / 2f), _floorSpacing);
            var startZ = SnapCoordinate(center.z - ((rows - 1) * _floorSpacing / 2f), _floorSpacing);
            var occupied = ScanOccupiedTiles();
            var queued = 0;
            var deduped = 0;
            var occupiedSkipped = 0;

            for (var row = 0; row < rows && queued < maxTiles; row++)
            {
                for (var col = 0; col < columns && queued < maxTiles; col++)
                {
                    var tilePos = SnapToBuildGrid(new float3(startX + col * _floorSpacing, center.y, startZ + row * _floorSpacing));
                    var tileKey = ToTileKey(tilePos);

                    if (occupied.Contains(tileKey) || _knownOccupiedFloorTiles.Contains(tileKey))
                    {
                        occupiedSkipped++;
                        continue;
                    }

                    if (!_queuedFloorTiles.Add(tileKey) || _processedFloorTiles.Contains(tileKey))
                    {
                        deduped++;
                        continue;
                    }

                    if (!EventTileReservationService.TryReserve(_floorReservationOwner, ToTilePosition(tilePos), out _, out _))
                    {
                        _queuedFloorTiles.Remove(tileKey);
                        occupiedSkipped++;
                        continue;
                    }

                    _pendingFloors.Enqueue(new PendingFloor { Position = tilePos });
                    queued++;
                }
            }

            _floorQueuedTotal += queued;
            _floorOccupiedSkippedTotal += occupiedSkipped;
            _floorDedupedTotal += deduped;
            return (queued, occupiedSkipped, deduped);
        }
    }

    /// <summary>Spawn next batch of floor tiles. Returns true while still spawning.</summary>
    public bool TickSpawnFloors()
    {
        lock (_scanSync)
        {
            if (!_spawningFloors || _pendingFloors.Count == 0)
            {
                _spawningFloors = false;
                return false;
            }

            var em = VRisingCore.EntityManager;
            if (!EventTileSafety.TryResolveSafeTileModelPrefab(_floorPrefab, out var prefabEntity, out var prefabError))
            {
                BattleLuckPlugin.LogWarning($"[BorderWallController] Floor spawning cancelled: {prefabError}.");
                foreach (var pending in _pendingFloors)
                    EventTileReservationService.Release(_floorReservationOwner, ToTileKey(pending.Position));
                _spawningFloors = false;
                _pendingFloors.Clear();
                _queuedFloorTiles.Clear();
                return false;
            }

            int toSpawn = Math.Min(FLOOR_BATCH_SIZE, _pendingFloors.Count);
            var occupiedThisBatch = ScanOccupiedTiles();
            int newlySearched = 0;
            int duplicateProcessSkipped = 0;
            for (int i = 0; i < toSpawn; i++)
            {
                var pending = _pendingFloors.Dequeue();
                long tileKey = ToTileKey(pending.Position);
                _queuedFloorTiles.Remove(tileKey);
                if (!_processedFloorTiles.Add(tileKey))
                {
                    duplicateProcessSkipped++;
                    _floorRuntimeSkippedTotal++;
                    EventTileReservationService.Release(_floorReservationOwner, tileKey);
                    continue;
                }

                newlySearched++;

                try
                {
                    var tilePos = ToTilePosition(pending.Position);
                    var tileKeyNow = ToTileKey(tilePos);
                    if (occupiedThisBatch.Contains(tileKeyNow))
                    {
                        duplicateProcessSkipped++;
                        _floorRuntimeSkippedTotal++;
                        _knownOccupiedFloorTiles.Add(tileKeyNow);
                        EventTileReservationService.Release(_floorReservationOwner, tileKey);
                        continue;
                    }

                    if (!_floorVisualOnly)
                    {
                        var canQueueFloorBuild = _requireOnlineAdminForBuilds
                            ? AdminTileBuildService.CanQueueTileBuildAdminOnly(out var floorBuilderError)
                            : AdminTileBuildService.CanQueueTileBuild(out floorBuilderError);

                        if (!canQueueFloorBuild)
                        {
                            if (DateTime.UtcNow >= _nextNoAdminFloorLogUtc)
                            {
                                _nextNoAdminFloorLogUtc = DateTime.UtcNow.Add(NoAdminRetryLogInterval);
                                BattleLuckPlugin.LogWarning($"[BorderWallController] Waiting for admin builder before spawning floors (pending={_pendingFloors.Count + 1}): {floorBuilderError}.");
                            }

                            _pendingFloors.Enqueue(pending);
                            _queuedFloorTiles.Add(tileKey);
                            _processedFloorTiles.Remove(tileKey);
                            continue;
                        }

                        var queuedFloor = _requireOnlineAdminForBuilds
                            ? AdminTileBuildService.TryQueueTileBuildAdminOnly(_floorPrefab, pending.Position, out var buildError)
                            : AdminTileBuildService.TryQueueTileBuild(_floorPrefab, pending.Position, out buildError);

                        if (!queuedFloor)
                        {
                            EventTileReservationService.Release(_floorReservationOwner, tileKey);
                            BattleLuckPlugin.LogWarning($"[BorderWallController] Failed to queue admin-owned floor at {pending.Position}: {buildError}");
                            continue;
                        }

                        _knownOccupiedFloorTiles.Add(tileKeyNow);
                        occupiedThisBatch.Add(tileKeyNow);
                        continue;
                    }

                    var entity = em.Instantiate(prefabEntity);

                    // Set world position
                    if (!entity.Has<Translation>()) em.AddComponent<Translation>(entity);
                    entity.Write(new Translation { Value = pending.Position });
                    if (entity.Has<LastTranslation>())
                        entity.Write(new LastTranslation { Value = pending.Position });

                    // Set rotation
                    if (!entity.Has<Rotation>()) em.AddComponent<Rotation>(entity);
                    entity.Write(new Rotation { Value = quaternion.identity });

                    // Set tile grid position (world coords × 2, floored to int)
                    if (!entity.Has<TilePosition>()) em.AddComponent<TilePosition>(entity);
                    entity.Write(new TilePosition { Tile = tilePos });

                    // Set tile AABB bounds (single tile)
                    if (!entity.Has<TileBounds>()) em.AddComponent<TileBounds>(entity);
                    entity.Write(new TileBounds
                    {
                        Value = new BoundsMinMax { Min = tilePos, Max = tilePos }
                    });

                    // Mark as schematic-spawned entity
                    if (!entity.Has<PhysicsCustomTags>()) em.AddComponent<PhysicsCustomTags>(entity);

                    // Make immortal so players can't dismantle
                    if (entity.Has<EditableTileModel>())
                    {
                        var etm = entity.Read<EditableTileModel>();
                        etm.CanDismantle = false;
                        entity.Write(etm);
                    }
                    if (_floorVisualOnly)
                    {
                        _floorRoomGraphStrippedTotal += EventTileSafety.StripRoomGraphComponents(em, entity, stripTileGrid: true);

                        _spawnedFloors.Add(entity);
                    }
                    else if (!CastleTileOwnershipService.TryStampOwnedTile(entity, pending.Position, out var warning))
                    {
                        try { entity.DestroyWithReason(); } catch { }
                        BattleLuckPlugin.LogWarning($"[BorderWallController] Castle floor ownership stamp failed at {pending.Position}: {warning}");
                        continue;
                    }

                    _knownOccupiedFloorTiles.Add(tileKeyNow);
                    occupiedThisBatch.Add(tileKeyNow);
                }
                catch (Exception ex)
                {
                    EventTileReservationService.Release(_floorReservationOwner, tileKey);
                    BattleLuckPlugin.LogWarning($"[BorderWallController] Failed to spawn floor at {pending.Position}: {ex.Message}");
                }
            }

            if (_pendingFloors.Count == 0)
            {
                _spawningFloors = false;
                BattleLuckPlugin.LogInfo($"[BorderWallController] Floor build complete: spawned={_spawnedFloors.Count}, queued={_floorQueuedTotal}, searched={_processedFloorTiles.Count}, occupiedSkipped={_floorOccupiedSkippedTotal}, deduped={_floorDedupedTotal}, runtimeSkipped={_floorRuntimeSkippedTotal}, visualOnly={_floorVisualOnly}, roomGraphStripped={_floorRoomGraphStrippedTotal}.");
            }

            return _pendingFloors.Count > 0;
        }
    }

    /// <summary>Destroy all tracked floor entities.</summary>
    public void DespawnFloors()
    {
        lock (_scanSync)
        {
            int destroyed = 0;
            foreach (var floor in _spawnedFloors)
            {
                try
                {
                    if (floor.Exists())
                    {
                        if (CastleTileOwnershipService.IsPermanentCastleEntity(floor))
                            continue;
                        floor.Destroy();
                        destroyed++;
                    }
                }
                catch { }
            }
            _spawnedFloors.Clear();
            _pendingFloors.Clear();
            _queuedFloorTiles.Clear();
            _processedFloorTiles.Clear();
            _knownOccupiedFloorTiles.Clear();
            _floorQueuedTotal = 0;
            _floorOccupiedSkippedTotal = 0;
            _floorDedupedTotal = 0;
            _floorRuntimeSkippedTotal = 0;
            _floorRoomGraphStrippedTotal = 0;
            _floorVisualOnly = true;
            _spawningFloors = false;
            EventTileReservationService.ReleaseOwner(_floorReservationOwner);

            if (destroyed > 0)
                BattleLuckPlugin.LogInfo($"[BorderWallController] Despawned {destroyed} floor tiles.");
        }
    }

    // ── Unified zone boundary ───────────────────────────────────────────

    /// <summary>Spawn floor and/or wall boundary for an event using config-driven prefabs and toggles.
    /// Also spawns glow entities on zone border if configured.</summary>
    public void StartZoneBoundary(string eventName, float3 center, float radius, WallBoundaryConfig config)
    {
        if (!config.Enabled)
            return;

        // Wall ring
        if (config.SpawnWalls)
        {
            var wallPrefabName = !string.IsNullOrWhiteSpace(config.WallPrefab)
                ? config.WallPrefab!
                : (_eventWallPrefabs.TryGetValue(eventName, out var ewp) ? ewp : DefaultWallPrefabName);
            _wallPrefab = ResolveBoundaryPrefab(wallPrefabName, "wall");
            BattleLuckPlugin.LogInfo($"[BorderWallController] StartZoneBoundary '{eventName}' → walls={_wallPrefab.GuidHash}");
            StartWallRing(center, radius, config);
        }

        if (config.SpawnFloors)
            StartFloorRing(center, radius, config.FloorSpacing ?? 2.5f, config.FloorPrefab);

        // With walls, glow entities are created only after the final wall batch.
        // Without walls there is no build queue, so the glow setup is complete here.
        if (!config.SpawnWalls)
            SpawnGlowEntities(config, center, radius);
    }

    /// <summary>Tick all boundary spawning. Returns true while walls or floors are still spawning.</summary>
    public bool TickSpawnAll()
    {
        var wallsStillSpawning = TickSpawn();
        var floorsStillSpawning = TickSpawnFloors();
        return wallsStillSpawning || floorsStillSpawning;
    }

    // ── Event-triggered spawning ────────────────────────────────────────

    /// <summary>
    /// Spawn an event-specific wall border. Picks the right prefab for the event.
    /// Usage: SpawnEventBorder("bloodbath", zoneCenter, 25f, config)
    /// </summary>
    public void SpawnEventBorder(string eventName, float3 center, float radius, WallBoundaryConfig config)
    {
        var wallPrefabName = _eventWallPrefabs.TryGetValue(eventName, out var prefab)
            ? prefab
            : DefaultWallPrefabName;

        _wallPrefab = ResolveBoundaryPrefab(wallPrefabName, "wall");

        BattleLuckPlugin.LogInfo($"[BorderWallController] Event '{eventName}' → wall prefab {_wallPrefab.GuidHash}");
        StartWallRing(center, radius, config);
    }

    /// <summary>Clean up walls and floors at event end.</summary>
    public void OnEventEnd()
    {
        DespawnWalls();
        DespawnFloors();
        BattleLuckPlugin.LogInfo("[BorderWallController] Event border cleared.");
    }

    // ── Zone heartbeat: re-check and repair destroyed walls ─────────────

    /// <summary>
    /// Call periodically (e.g. every 5s) to detect and respawn any walls destroyed mid-event.
    /// Returns the number of walls repaired.
    /// </summary>
    public int ZoneHeartbeat()
    {
        lock (_scanSync)
        {
            if (_wallSlots.Count == 0 || _lastConfig == null) return 0;

            var em = VRisingCore.EntityManager;
            var pcs = VRisingCore.PrefabCollectionSystem;

            if (!pcs._PrefabGuidToEntityMap.TryGetValue(_wallPrefab, out var prefabEntity))
                return 0;

            int repaired = 0;

            for (int i = 0; i < _wallSlots.Count; i++)
            {
                if (i < _spawnedWalls.Count && _spawnedWalls[i].Exists()) continue;
                var slot = _wallSlots[i];

                try
                {
                    var entity = em.Instantiate(prefabEntity);
                    ConfigureWallEntity(em, entity, slot);
                    ProtectBoundaryEntity(entity);
                    ApplyWallGlowIfConfigured(entity);
                    while (_spawnedWalls.Count <= i)
                        _spawnedWalls.Add(Entity.Null);
                    _spawnedWalls[i] = entity;
                    repaired++;
                }
                catch { }
            }

            if (repaired > 0)
                BattleLuckPlugin.LogInfo($"[BorderWallController] Heartbeat repaired {repaired} walls.");

            return repaired;
        }
    }

    public void Reset()
    {
        DespawnWalls();
        DespawnFloors();
        DespawnGlowEntities();
    }

    /// <summary>Destroy all tracked glow entities.</summary>
    void DespawnGlowEntities()
    {
        var em = VRisingCore.EntityManager;
        int destroyed = 0;
        foreach (var glow in _spawnedGlowEntities)
        {
            try
            {
                if (em.Exists(glow))
                {
                    glow.Destroy();
                    destroyed++;
                }
            }
            catch { }
        }
        _spawnedGlowEntities.Clear();
        _glowEntityPrefab = PrefabGUID.Empty;
        if (destroyed > 0)
            BattleLuckPlugin.LogInfo($"[BorderWallController] Despawned {destroyed} glow entities.");
    }

    static PrefabGUID ResolveBoundaryPrefab(string prefabName, string context)
    {
        if (PrefabHelper.TryGetValidPrefabGuidDeep(prefabName, out var guid))
            return guid;

        BattleLuckPlugin.LogWarning($"[BorderWallController] Could not resolve {context} prefab '{prefabName}' from live prefab map.");
        return PrefabGUID.Empty;
    }

    static long ToTileKey(float3 position)
    {
        return ToTileKey(ToTilePosition(position));
    }

    static long ToTileKey(int2 tile)
    {
        return ((long)tile.x << 32) ^ (uint)tile.y;
    }

    static int2 ToTilePosition(float3 position)
    {
        return new int2(
            (int)math.round(position.x * 2f),
            (int)math.round(position.z * 2f));
    }

    static float3 SnapToBuildGrid(float3 position)
    {
        return new float3(
            math.round(position.x / 2.5f) * 2.5f,
            position.y,
            math.round(position.z / 2.5f) * 2.5f);
    }

    static float SnapCoordinate(float value, float spacing)
    {
        spacing = Math.Max(0.001f, spacing);
        return math.round(value / spacing) * spacing;
    }

    static HashSet<long> ScanOccupiedTiles()
    {
        var occupied = new HashSet<long>();
        try
        {
            var em = VRisingCore.EntityManager;
            var query = em.CreateEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<TilePosition>() },
                None = new[] { ComponentType.ReadOnly<PlayerCharacter>() }
            });
            var entities = query.ToEntityArray(Allocator.Temp);
            try
            {
                foreach (var entity in entities)
                {
                    if (!em.Exists(entity) || !em.HasComponent<TilePosition>(entity))
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
            BattleLuckPlugin.LogWarning($"[BorderWallController] Tile occupancy scan failed: {ex.Message}");
        }

        return occupied;
    }

    static HashSet<long> ScanOccupiedWallTiles()
    {
        var occupied = new HashSet<long>();
        try
        {
            var em = VRisingCore.EntityManager;
            var query = em.CreateEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<TilePosition>(), ComponentType.ReadOnly<PrefabGUID>() },
                None = new[] { ComponentType.ReadOnly<PlayerCharacter>() }
            });
            var entities = query.ToEntityArray(Allocator.Temp);
            try
            {
                foreach (var entity in entities)
                {
                    if (!em.Exists(entity) || !em.HasComponent<TilePosition>(entity) || !em.HasComponent<PrefabGUID>(entity))
                        continue;

                    var guid = em.GetComponentData<PrefabGUID>(entity);
                    var name = PrefabHelper.GetLivePrefabName(guid) ?? PrefabHelper.GetName(guid) ?? "";
                    if (!name.Contains("Wall", StringComparison.OrdinalIgnoreCase))
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
            BattleLuckPlugin.LogWarning($"[BorderWallController] Wall occupancy scan failed: {ex.Message}");
        }

        return occupied;
    }

    /// <summary>Spawn glow entities around zone border.</summary>
    void SpawnGlowEntities(WallBoundaryConfig config, float3 center, float radius)
    {
        var glowPrefabName = config.Glow?.SpawnGlowEntities;
        if (string.IsNullOrWhiteSpace(glowPrefabName)) return;

        _glowEntityPrefab = ResolveBoundaryPrefab(glowPrefabName, "glow");
        if (_glowEntityPrefab == PrefabGUID.Empty) return;

        _glowEntityCount = config.Glow?.GlowEntityCount > 0 ? config.Glow.GlowEntityCount : 20;
        _glowRadius = config.Glow?.GlowRadius > 0 ? config.Glow.GlowRadius : radius;
        _disableSunEffects = config.Glow?.DisableSunEffects ?? false;
        _npcFriendly = config.Glow?.NpcFriendly ?? false;

        var em = VRisingCore.EntityManager;
        var pcs = VRisingCore.PrefabCollectionSystem;

        if (!pcs._PrefabGuidToEntityMap.TryGetValue(_glowEntityPrefab, out var prefabEntity)) return;

        for (int i = 0; i < _glowEntityCount; i++)
        {
            try
            {
                float angle = 2f * math.PI * i / _glowEntityCount;
                float3 pos = new(
                    center.x + _glowRadius * math.cos(angle),
                    center.y + 1f,
                    center.z + _glowRadius * math.sin(angle)
                );

                var entity = em.Instantiate(prefabEntity);
                entity.SetPosition(pos);
                entity.Write(new Rotation { Value = quaternion.identity });
                _spawnedGlowEntities.Add(entity);
            }
            catch { }
        }
        BattleLuckPlugin.LogInfo($"[BorderWallController] Spawned {_spawnedGlowEntities.Count} glow entities on zone border.");
    }

    void ApplyWallGlowIfConfigured(Entity entity)
    {
        if (_lastConfig?.Glow?.Enabled != true || Prefabs.Buff_General_Ignite == PrefabGUID.Empty)
            return;

        try { entity.TryApplyBuff(Prefabs.Buff_General_Ignite); }
        catch { }
    }

    /// <summary>Apply sun immunity and NPC friendly buffs to players inside zone.</summary>
    public void ApplyZoneEffects(Entity player)
    {
        if (_disableSunEffects && Prefabs.Buff_General_Holy_T01 != PrefabGUID.Empty)
            player.BuffEntity(Prefabs.Buff_General_Holy_T01, out _);
        if (_npcFriendly)
            player.SetTeam(100); // Friendly team ID
    }
}
