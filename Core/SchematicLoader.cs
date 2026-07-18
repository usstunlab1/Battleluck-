using System.Reflection;
using ProjectM.CastleBuilding;
using Unity.Physics;
using Unity.Transforms;

namespace BattleLuck.Core;

/// <summary>
/// Loads and manages schematic configurations for zone layouts.
/// Schematics define custom wall, floor, tile, chain, and castle placements per event.
/// </summary>
public sealed class SchematicLoader
{
    static string SchematicsPath => Path.Combine(ConfigLoader.ConfigRoot, "schematics");
    /// <summary>
    /// Master toggle for schematic world mutations. Set to true to allow schematic
    /// spawning. When false, all LoadIntoWorld calls return a safe no-op report.
    /// </summary>
    static bool LiveWorldMutationsEnabled = true;
    static readonly Dictionary<string, SchematicConfig> _schematics = new(StringComparer.OrdinalIgnoreCase);
    static readonly Dictionary<string, List<SpawnedSchematicEntity>> _spawnedByEvent = new(StringComparer.OrdinalIgnoreCase);
    static readonly object _lock = new();

    /// <summary>Load all schematic JSON files from config/schematics/ directory.</summary>
    public static void LoadAll()
    {
        lock (_lock)
        {
            _schematics.Clear();

            // Load global schematics
            LoadFromDirectory(SchematicsPath);

            // Load event-specific schematics
            var eventsRoot = Path.Combine(ConfigLoader.ConfigRoot, "events");
            if (Directory.Exists(eventsRoot))
            {
                foreach (var eventDir in Directory.GetDirectories(eventsRoot))
                {
                    var eventSchematicDir = Path.Combine(eventDir, "schematics");
                    if (Directory.Exists(eventSchematicDir))
                    {
                        LoadFromDirectory(eventSchematicDir);
                    }
                }
            }

            BattleLuckPlugin.LogInfo($"[SchematicLoader] Loaded {_schematics.Count} schematics total.");
        }
    }

    static void LoadFromDirectory(string path)
    {
        if (!Directory.Exists(path)) return;

        var files = Directory.GetFiles(path, "*.json", SearchOption.TopDirectoryOnly);
        foreach (var file in files)
        {
            try
            {
                var json = File.ReadAllText(file);
                var schematic = JsonSerializer.Deserialize<SchematicConfig>(json, ConfigLoader.JsonOptions);
                if (schematic == null || string.IsNullOrWhiteSpace(schematic.EventName))
                {
                    BattleLuckPlugin.LogWarning($"[SchematicLoader] Invalid schematic in {file}");
                    continue;
                }

                _schematics[schematic.EventName] = schematic;
                BattleLuckPlugin.LogInfo($"[SchematicLoader] Loaded schematic '{schematic.EventName}' from {Path.GetFileName(file)}");
            }
            catch (Exception ex)
            {
                BattleLuckPlugin.LogError($"[SchematicLoader] Failed to load {file}: {ex.Message}");
            }
        }
    }

    /// <summary>Get schematic for a specific event name (e.g., "bloodbath").</summary>
    public static SchematicConfig? GetSchematic(string eventName)
    {
        lock (_lock)
        {
            return _schematics.TryGetValue(eventName, out var schematic) && schematic.Enabled ? schematic : null;
        }
    }

    /// <summary>Check if a schematic exists for an event.</summary>
    public static bool HasSchematic(string eventName)
    {
        lock (_lock)
        {
            return _schematics.TryGetValue(eventName, out var schematic) && schematic != null && schematic.Enabled;
        }
    }

    /// <summary>Get all loaded schematic event names.</summary>
    public static IEnumerable<string> GetAllEventNames()
    {
        lock (_lock)
        {
            return _schematics.Keys;
        }
    }

    /// <summary>Save a schematic to disk (for the schematic builder app or runtime editing).</summary>
    public static void SaveSchematic(SchematicConfig schematic)
    {
        lock (_lock)
        {
            if (!Directory.Exists(SchematicsPath))
                Directory.CreateDirectory(SchematicsPath);

            var fileName = $"{schematic.EventName}.json";
            var filePath = Path.Combine(SchematicsPath, fileName);
            var json = JsonSerializer.Serialize(schematic, new JsonSerializerOptions(ConfigLoader.JsonOptions)
            {
                WriteIndented = true
            });
            File.WriteAllText(filePath, json);

            _schematics[schematic.EventName] = schematic;
            BattleLuckPlugin.LogInfo($"[SchematicLoader] Saved schematic '{schematic.EventName}' to {fileName}");
        }
    }

    /// <summary>Import schematic from JSON string (for API/webhook import).</summary>
    public static bool ImportSchematic(string eventName, string json)
    {
        lock (_lock)
        {
            try
            {
                var schematic = JsonSerializer.Deserialize<SchematicConfig>(json, ConfigLoader.JsonOptions);
                if (schematic == null || string.IsNullOrWhiteSpace(schematic.EventName))
                {
                    BattleLuckPlugin.LogWarning($"[SchematicLoader] Invalid schematic JSON for import");
                    return false;
                }

                schematic.EventName = eventName; // Override with provided event name
                SaveSchematic(schematic);
                return true;
            }
            catch (Exception ex)
            {
                BattleLuckPlugin.LogError($"[SchematicLoader] Failed to import schematic: {ex.Message}");
                return false;
            }
        }
    }

    /// <summary>Export schematic to JSON string (for API/webhook export).</summary>
    public static string? ExportSchematic(string eventName)
    {
        lock (_lock)
        {
            if (!_schematics.TryGetValue(eventName, out var schematic))
                return null;

            return JsonSerializer.Serialize(schematic, ConfigLoader.JsonOptions);
        }
    }

    /// <summary>Spawn a loaded schematic into the live world at a target center.</summary>
    public static OperationResult<SchematicLoadReport> LoadIntoWorld(
        string eventName,
        float3 center,
        float radius = 0f,
        bool clearOld = true,
        bool spawnBuiltItems = true,
        float clearRadius = 0f,
        bool allowLiveWorldMutations = false,
        string? targetScopeOverride = null,
        IReadOnlyList<string>? structureFilter = null,
        string? trackingGroup = null)
    {
        if (!VRisingCore.IsReady)
            return OperationResult<SchematicLoadReport>.Fail("VRising core is not ready.");
        if (string.IsNullOrWhiteSpace(eventName))
            return OperationResult<SchematicLoadReport>.Fail("eventName is required.");

        SchematicConfig? schematic;
        lock (_lock)
        {
            if (!_schematics.TryGetValue(eventName, out schematic))
            {
                LoadAll();
                _schematics.TryGetValue(eventName, out schematic);
            }
        }

        if (schematic == null || !schematic.Enabled)
            return OperationResult<SchematicLoadReport>.Fail($"Schematic '{eventName}' was not found or is disabled.");

        // Resolve effective target scope: override > schematic's own scope > "all"
        var effectiveScope = !string.IsNullOrWhiteSpace(targetScopeOverride)
            ? targetScopeOverride
            : schematic.TargetScope;
        var shouldSpawnStructures = effectiveScope.Equals("all", StringComparison.OrdinalIgnoreCase) ||
                                    effectiveScope.Equals("structures_only", StringComparison.OrdinalIgnoreCase);
        var shouldSpawnItems = spawnBuiltItems &&
                               (effectiveScope.Equals("all", StringComparison.OrdinalIgnoreCase) ||
                                effectiveScope.Equals("items_only", StringComparison.OrdinalIgnoreCase));
        var isWorldMapOnly = effectiveScope.Equals("world_map", StringComparison.OrdinalIgnoreCase);

        var effectiveRadius = radius > 0f
            ? radius
            : schematic.CastleDesign.Radius > 0f
                ? schematic.CastleDesign.Radius
                : EstimateRadius(schematic);

        var report = new SchematicLoadReport
        {
            EventName = schematic.EventName,
            Center = ToVec3(center),
            Radius = effectiveRadius
        };
        // World-map-only schematics: create map markers without spawning world structures or items
        if (isWorldMapOnly)
        {
            report.SpawnedMapMarkers = SpawnMapMarkers(schematic, center, trackingGroup);
            BattleLuckPlugin.LogInfo($"[SchematicLoader] Loaded world-map-only schematic '{schematic.EventName}' at ({center.x:F1},{center.y:F1},{center.z:F1}): mapMarkers={report.SpawnedMapMarkers}.");
            return OperationResult<SchematicLoadReport>.Ok(report);
        }

        if (!LiveWorldMutationsEnabled)
        {
            BattleLuckPlugin.LogWarning($"[SchematicLoader] Live schematic spawning is disabled for server stability. Skipped '{schematic.EventName}' at ({center.x:F1},{center.y:F1},{center.z:F1}).");
            return OperationResult<SchematicLoadReport>.Ok(report);
        }

        if (clearOld)
        {
            var clearResult = string.IsNullOrWhiteSpace(trackingGroup)
                ? ClearByEventName(schematic.EventName)
                : ClearTrackedEventInGroup(trackingGroup, schematic.EventName);
            report.DestroyedOld = clearResult.Value?.TotalDestroyed ?? 0;
        }
        if (clearRadius > 0f)
        {
            var clearReport = ClearTrackedInRadius(center, clearRadius);
            report.DestroyedOld += clearReport.Value?.TotalDestroyed ?? 0;
        }

        var em = VRisingCore.EntityManager;
        var radiusSq = effectiveRadius > 0f ? effectiveRadius * effectiveRadius : float.MaxValue;
        var spawned = new List<SpawnedSchematicEntity>();
        var group = string.IsNullOrWhiteSpace(trackingGroup) ? schematic.EventName : trackingGroup.Trim();
        var reservationOwner = $"schematic:{group}";
        var occupiedTiles = EventTileReservationService.SnapshotWorldOccupiedTiles();

        // Build structure filter set for efficient lookup
        HashSet<string>? filterSet = structureFilter != null && structureFilter.Count > 0
            ? new HashSet<string>(structureFilter, StringComparer.OrdinalIgnoreCase)
            : null;

        if (shouldSpawnStructures)
        {
            foreach (var structure in schematic.Structures)
            {
                // Apply structure type filter if specified
                if (filterSet != null && !filterSet.Contains(structure.Type))
                    continue;
                var relative = structure.Position.ToFloat3();
                if (!WithinRelativeRadius(relative, radiusSq))
                    continue;
                var worldPosition = center + relative;
                var tilePosition = ToSchematicTilePosition(worldPosition);
                var reservationKey = EventTileReservationService.ToKey(tilePosition);
                string? conflictingOwner = null;

                if (occupiedTiles.Contains(reservationKey) ||
                    !EventTileReservationService.TryReserve(reservationOwner, tilePosition, out _, out conflictingOwner))
                {
                    report.FailedStructures++;
                    BattleLuckPlugin.LogWarning($"[SchematicLoader] Skipped overlapping structure '{structure.Prefab}' for '{schematic.EventName}' at tile {tilePosition}; reservation={conflictingOwner ?? "world"}.");
                    continue;
                }

                if (IsRealCastleSchematicKind(structure.Type) &&
                    !CastleTileOwnershipService.IsOwnedCastleGeometryPrefabName(structure.Prefab))
                {
                    report.FailedStructures++;
                    BattleLuckPlugin.LogWarning($"[SchematicLoader] Skipped real castle schematic entry '{structure.Prefab}' for '{schematic.EventName}': real castle mode is limited to floor/tile/wall geometry.");
                    continue;
                }

                if (TrySpawnSchematicPrefab(em, structure.Prefab, structure.PrefabGuid, worldPosition, structure.Rotation, structure.Type, out var entity, out var error))
                {
                    if (IsRealCastleSchematicKind(structure.Type) &&
                        !CastleTileOwnershipService.TryStampOwnedTile(entity, worldPosition, structure.Prefab, out var warning))
                    {
                        try { entity.DestroyWithReason(); } catch { }
                        EventTileReservationService.Release(reservationOwner, reservationKey);
                        report.FailedStructures++;
                        BattleLuckPlugin.LogWarning($"[SchematicLoader] Failed to stamp real castle structure '{structure.Prefab}' for '{schematic.EventName}': {warning}");
                        continue;
                    }

                    report.SpawnedStructures++;
                    occupiedTiles.Add(reservationKey);
                    spawned.Add(new SpawnedSchematicEntity(schematic.EventName, entity, center, structure.Type, DateTime.UtcNow, reservationOwner, reservationKey));
                }
                else
                {
                    EventTileReservationService.Release(reservationOwner, reservationKey);
                    report.FailedStructures++;
                    BattleLuckPlugin.LogWarning($"[SchematicLoader] Failed to spawn structure '{structure.Prefab}' for '{schematic.EventName}': {error}");
                }
            }
        }

        if (shouldSpawnItems)
        {
            foreach (var item in schematic.BuiltItems)
            {
                var relative = item.Position.ToFloat3();
                if (!WithinRelativeRadius(relative, radiusSq))
                    continue;

                if (TrySpawnSchematicPrefab(em, item.Prefab, item.PrefabGuid, center + relative, 0f, "built_item", out var entity, out var error))
                {
                    report.SpawnedBuiltItems++;
                    spawned.Add(new SpawnedSchematicEntity(schematic.EventName, entity, center, "built_item", DateTime.UtcNow, null, 0));
                }
                else
                {
                    report.FailedBuiltItems++;
                    BattleLuckPlugin.LogWarning($"[SchematicLoader] Failed to spawn built item '{item.Prefab}' for '{schematic.EventName}': {error}");
                }
            }
        }

        lock (_lock)
        {
            if (!_spawnedByEvent.TryGetValue(group, out var list))
            {
                list = new List<SpawnedSchematicEntity>();
                _spawnedByEvent[group] = list;
            }
            list.AddRange(spawned);
        }

        // Create map markers alongside entity spawning
        report.SpawnedMapMarkers = SpawnMapMarkers(schematic, center, trackingGroup);

        BattleLuckPlugin.LogInfo($"[SchematicLoader] Loaded schematic '{schematic.EventName}' at ({center.x:F1},{center.y:F1},{center.z:F1}): scope={effectiveScope}, structures={report.SpawnedStructures}, builtItems={report.SpawnedBuiltItems}, mapMarkers={report.SpawnedMapMarkers}, failed={report.FailedStructures + report.FailedBuiltItems}, destroyedOld={report.DestroyedOld}.");
        return OperationResult<SchematicLoadReport>.Ok(report);
    }

    public static OperationResult<SchematicSpawnReport> SpawnPrefabAt(
        string prefabName,
        float3 worldPosition,
        float rotationDegrees = 0f,
        string kind = "prefab",
        string trackingGroup = "manual_build")
    {
        if (!LiveWorldMutationsEnabled)
            return OperationResult<SchematicSpawnReport>.Fail("Live schematic/prefab spawning is disabled by the strict server-stability profile.");
        if (!VRisingCore.IsReady)
            return OperationResult<SchematicSpawnReport>.Fail("VRising core is not ready.");
        if (string.IsNullOrWhiteSpace(prefabName))
            return OperationResult<SchematicSpawnReport>.Fail("prefab is required.");

        var em = VRisingCore.EntityManager;
        var group = string.IsNullOrWhiteSpace(trackingGroup) ? "manual_build" : trackingGroup.Trim();
        var reserveTile = !kind.Equals("built_item", StringComparison.OrdinalIgnoreCase) &&
                          (!kind.Equals("prefab", StringComparison.OrdinalIgnoreCase) || prefabName.StartsWith("TM_", StringComparison.OrdinalIgnoreCase));
        var reservationOwner = reserveTile ? $"schematic:{group}" : null;
        var reservationKey = 0L;
        if (reservationOwner != null)
        {
            var tile = ToSchematicTilePosition(worldPosition);
            reservationKey = EventTileReservationService.ToKey(tile);
            var worldOccupied = EventTileReservationService.SnapshotWorldOccupiedTiles();
            string? conflictingOwner = null;
            if (worldOccupied.Contains(reservationKey) ||
                !EventTileReservationService.TryReserve(reservationOwner, tile, out _, out conflictingOwner))
            {
                return OperationResult<SchematicSpawnReport>.Fail($"Tile overlaps existing geometry ({conflictingOwner ?? "world"}).");
            }
        }

        if (!TrySpawnSchematicPrefab(em, prefabName, null, worldPosition, rotationDegrees, kind, out var entity, out var error))
        {
            if (reservationOwner != null)
                EventTileReservationService.Release(reservationOwner, reservationKey);
            return OperationResult<SchematicSpawnReport>.Fail(error);
        }

        var resolved = entity.Has<PrefabGUID>()
            ? entity.Read<PrefabGUID>()
            : PrefabGUID.Empty;

        lock (_lock)
        {
            if (!_spawnedByEvent.TryGetValue(group, out var list))
            {
                list = new List<SpawnedSchematicEntity>();
                _spawnedByEvent[group] = list;
            }
            list.Add(new SpawnedSchematicEntity(group, entity, worldPosition, kind, DateTime.UtcNow, reservationOwner, reservationKey));
        }

        return OperationResult<SchematicSpawnReport>.Ok(new SchematicSpawnReport
        {
            Entity = entity,
            TrackingGroup = group,
            Prefab = PrefabHelper.GetLivePrefabName(resolved) ?? prefabName,
            PrefabGuid = resolved.GuidHash,
            EntityIndex = entity.Index,
            Position = ToVec3(worldPosition)
        });
    }

    public static OperationResult<SchematicClearReport> ClearByEventName(string eventName)
    {
        if (!LiveWorldMutationsEnabled)
            return OperationResult<SchematicClearReport>.Fail("Live schematic destruction is disabled by the strict server-stability profile.");
        var report = new SchematicClearReport { Target = eventName };
        if (string.IsNullOrWhiteSpace(eventName))
            return OperationResult<SchematicClearReport>.Fail("eventName is required.");

        lock (_lock)
        {
            foreach (var key in _spawnedByEvent.Keys.ToList())
            {
                var list = _spawnedByEvent[key];
                var matching = list.Where(entry => entry.EventName.Equals(eventName, StringComparison.OrdinalIgnoreCase)).ToList();
                report.DestroyedTracked += DestroyTracked(matching);
                list.RemoveAll(entry => matching.Contains(entry) || !entry.Entity.Exists());
                if (list.Count == 0)
                    _spawnedByEvent.Remove(key);
            }
        }

        BattleLuckPlugin.ZoneMap?.ClearSchematicMarkers(eventName);

        BattleLuckPlugin.LogInfo($"[SchematicLoader] Cleared schematic '{eventName}' tracked entities: {report.DestroyedTracked}.");
        return OperationResult<SchematicClearReport>.Ok(report);
    }

    public static OperationResult<SchematicClearReport> ClearTrackingGroup(string trackingGroup)
    {
        if (!LiveWorldMutationsEnabled)
            return OperationResult<SchematicClearReport>.Fail("Live schematic destruction is disabled by the strict server-stability profile.");
        var report = new SchematicClearReport { Target = trackingGroup };
        if (string.IsNullOrWhiteSpace(trackingGroup))
            return OperationResult<SchematicClearReport>.Fail("trackingGroup is required.");

        lock (_lock)
        {
            if (!_spawnedByEvent.TryGetValue(trackingGroup, out var list))
                return OperationResult<SchematicClearReport>.Ok(report);

            report.DestroyedTracked = DestroyTracked(list);
            _spawnedByEvent.Remove(trackingGroup);
        }

        BattleLuckPlugin.ZoneMap?.ClearSchematicMarkers(trackingGroup);

        BattleLuckPlugin.LogInfo($"[SchematicLoader] Cleared tracked schematic group '{trackingGroup}': {report.DestroyedTracked}.");
        return OperationResult<SchematicClearReport>.Ok(report);
    }

    static OperationResult<SchematicClearReport> ClearTrackedEventInGroup(string trackingGroup, string eventName)
    {
        var report = new SchematicClearReport { Target = $"{trackingGroup}:{eventName}" };
        lock (_lock)
        {
            if (!_spawnedByEvent.TryGetValue(trackingGroup, out var list))
                return OperationResult<SchematicClearReport>.Ok(report);

            var matching = list.Where(entry => entry.EventName.Equals(eventName, StringComparison.OrdinalIgnoreCase)).ToList();
            report.DestroyedTracked = DestroyTracked(matching);
            list.RemoveAll(entry => matching.Contains(entry) || !entry.Entity.Exists());
            if (list.Count == 0)
                _spawnedByEvent.Remove(trackingGroup);
        }

        return OperationResult<SchematicClearReport>.Ok(report);
    }

    public static OperationResult<SchematicClearReport> ClearTrackedInRadius(float3 center, float radius)
    {
        if (!LiveWorldMutationsEnabled)
            return OperationResult<SchematicClearReport>.Fail("Live schematic destruction is disabled by the strict server-stability profile.");
        if (radius <= 0f)
            return OperationResult<SchematicClearReport>.Fail("radius must be greater than 0.");

        var report = new SchematicClearReport { Target = $"tracked radius {radius:F1}" };
        var radiusSq = radius * radius;

        lock (_lock)
        {
            foreach (var key in _spawnedByEvent.Keys.ToList())
            {
                var list = _spawnedByEvent[key];
                var matching = list.Where(entry => IsEntityWithinRadius(entry.Entity, center, radiusSq)).ToList();
                report.DestroyedTracked += DestroyTracked(matching);
                list.RemoveAll(entry => matching.Contains(entry) || !entry.Entity.Exists());
                if (list.Count == 0)
                    _spawnedByEvent.Remove(key);
            }
        }

        BattleLuckPlugin.LogInfo($"[SchematicLoader] Cleared tracked schematic entities in radius {radius:F1}: {report.DestroyedTracked}.");
        return OperationResult<SchematicClearReport>.Ok(report);
    }

    public static OperationResult<SchematicClearReport> ClearAllTracked()
    {
        if (!LiveWorldMutationsEnabled)
            return OperationResult<SchematicClearReport>.Fail("Live schematic destruction is disabled by the strict server-stability profile.");
        var report = new SchematicClearReport { Target = "all tracked schematics" };

        lock (_lock)
        {
            foreach (var list in _spawnedByEvent.Values)
                report.DestroyedTracked += DestroyTracked(list);
            _spawnedByEvent.Clear();
        }

        BattleLuckPlugin.LogWarning($"[SchematicLoader] Cleared all tracked schematic entities: {report.DestroyedTracked}.");
        return OperationResult<SchematicClearReport>.Ok(report);
    }

    /// <summary>Compatibility entry point. Radius cleanup is deliberately limited to BattleLuck-tracked entities.</summary>
    public static OperationResult<SchematicClearReport> DestroyWorldEntitiesInRadius(float3 center, float radius, bool includeItems = true)
    {
        if (!LiveWorldMutationsEnabled)
            return OperationResult<SchematicClearReport>.Fail("World entity destruction is disabled by the strict server-stability profile.");
        var result = ClearTrackedInRadius(center, radius);
        if (result.Success && result.Value != null)
            BattleLuckPlugin.LogInfo($"[SchematicLoader] Radius cleanup preserved world entities and cleared BattleLuck-tracked schematic entities only: radius={radius:F1}, tracked={result.Value.DestroyedTracked}.");
        return result;
    }

    static bool TrySpawnSchematicPrefab(
        EntityManager em,
        string prefabName,
        int? prefabGuid,
        float3 worldPosition,
        float rotationDegrees,
        string kind,
        out Entity entity,
        out string error)
    {
        entity = Entity.Null;
        error = "";

        if (!TryResolveSchematicPrefab(prefabName, prefabGuid, out var guid))
        {
            error = "prefab could not be resolved";
            return false;
        }

        var requireTileModel = !kind.Equals("built_item", StringComparison.OrdinalIgnoreCase) &&
                               !kind.Equals("prefab", StringComparison.OrdinalIgnoreCase);
        var isSafe = requireTileModel
            ? EventTileSafety.TryResolveSafeTileModelPrefab(guid, out var prefabEntity, out error)
            : EventTileSafety.TryResolveSafeSchematicPrefab(guid, out prefabEntity, out error);
        if (!isSafe)
        {
            return false;
        }

        try
        {
            entity = em.Instantiate(prefabEntity);
            ConfigureSpawnedSchematicEntity(em, entity, worldPosition, rotationDegrees, kind);
            return em.Exists(entity);
        }
        catch (Exception ex)
        {
            error = ex.Message;
            try { if (entity.Exists()) entity.DestroyWithReason(); } catch { }
            entity = Entity.Null;
            return false;
        }
    }

    static bool TryResolveSchematicPrefab(string prefabName, int? prefabGuid, out PrefabGUID guid)
    {
        if (prefabGuid.HasValue && prefabGuid.Value != 0)
        {
            guid = new PrefabGUID(prefabGuid.Value);
            if (PrefabHelper.ValidatePrefab(guid))
                return true;
        }

        if (int.TryParse(prefabName, out var parsedGuid))
        {
            guid = new PrefabGUID(parsedGuid);
            if (PrefabHelper.ValidatePrefab(guid))
                return true;
        }

        var resolved = PrefabHelper.GetValidPrefabGuidDeep(prefabName);
        if (resolved.HasValue)
        {
            guid = resolved.Value;
            return true;
        }

        guid = PrefabGUID.Empty;
        return false;
    }

    static void ConfigureSpawnedSchematicEntity(EntityManager em, Entity entity, float3 worldPosition, float rotationDegrees, string kind)
    {
        if (!entity.Has<Translation>()) em.AddComponent<Translation>(entity);
        entity.Write(new Translation { Value = worldPosition });

        if (entity.Has<LastTranslation>())
            entity.Write(new LastTranslation { Value = worldPosition });

        if (!entity.Has<Rotation>()) em.AddComponent<Rotation>(entity);
        entity.Write(new Rotation { Value = quaternion.RotateY(math.radians(rotationDegrees)) });

        if (!kind.Equals("built_item", StringComparison.OrdinalIgnoreCase))
        {
            var tilePos = new int2(
                (int)math.floor(worldPosition.x * 2f),
                (int)math.floor(worldPosition.z * 2f));

            if (!entity.Has<TilePosition>()) em.AddComponent<TilePosition>(entity);
            entity.Write(new TilePosition { Tile = tilePos });

            if (!entity.Has<TileBounds>()) em.AddComponent<TileBounds>(entity);
            entity.Write(new TileBounds
            {
                Value = new BoundsMinMax { Min = tilePos, Max = tilePos }
            });

            if (entity.Has<EditableTileModel>())
            {
                var editable = entity.Read<EditableTileModel>();
                editable.CanDismantle = false;
                entity.Write(editable);
            }
        }

        if (!entity.Has<PhysicsCustomTags>())
            em.AddComponent<PhysicsCustomTags>(entity);

        if (ShouldSanitizeSchematicKind(kind))
            EventTileSafety.StripRoomGraphComponents(em, entity, stripTileGrid: true);
    }

    static bool ShouldSanitizeSchematicKind(string kind) =>
        !kind.Equals("built_item", StringComparison.OrdinalIgnoreCase) &&
        !kind.Equals("real_castle", StringComparison.OrdinalIgnoreCase) &&
        !kind.Equals("castle_room", StringComparison.OrdinalIgnoreCase);

    static bool IsRealCastleSchematicKind(string kind) =>
        kind.Equals("real_castle", StringComparison.OrdinalIgnoreCase) ||
        kind.Equals("castle_room", StringComparison.OrdinalIgnoreCase);

    static int DestroyTracked(IEnumerable<SpawnedSchematicEntity> entries)
    {
        var destroyed = 0;
        foreach (var entry in entries.ToList())
        {
            try
            {
                if (!entry.Entity.Exists())
                {
                    ReleaseReservation(entry);
                    continue;
                }
                if (entry.Entity.Has<PlayerCharacter>()) continue;
                if (CastleTileOwnershipService.IsPermanentCastleEntity(entry.Entity)) continue;
                entry.Entity.DestroyWithReason();
                ReleaseReservation(entry);
                destroyed++;
            }
            catch { }
        }
        return destroyed;
    }

    static void ReleaseReservation(SpawnedSchematicEntity entry)
    {
        if (!string.IsNullOrWhiteSpace(entry.ReservationOwner))
            EventTileReservationService.Release(entry.ReservationOwner, entry.ReservationKey);
    }

    static int2 ToSchematicTilePosition(float3 position) => new(
        (int)math.floor(position.x * 2f),
        (int)math.floor(position.z * 2f));

    static bool IsEntityWithinRadius(Entity entity, float3 center, float radiusSq)
    {
        try
        {
            if (!entity.Exists() || entity.Has<PlayerCharacter>() || CastleTileOwnershipService.IsPermanentCastleEntity(entity) || !entity.Has<Translation>())
                return false;

            var point = entity.Read<Translation>().Value;
            return WithinXZ(point, center, radiusSq);
        }
        catch
        {
            return false;
        }
    }

    static bool WithinRelativeRadius(float3 relativePosition, float radiusSq)
    {
        if (float.IsPositiveInfinity(radiusSq) || radiusSq == float.MaxValue)
            return true;

        var dx = relativePosition.x;
        var dz = relativePosition.z;
        return dx * dx + dz * dz <= radiusSq;
    }

    static float EstimateRadius(SchematicConfig schematic)
    {
        var maxSq = 0f;
        foreach (var structure in schematic.Structures)
        {
            var p = structure.Position.ToFloat3();
            maxSq = math.max(maxSq, p.x * p.x + p.z * p.z);
        }
        foreach (var item in schematic.BuiltItems)
        {
            var p = item.Position.ToFloat3();
            maxSq = math.max(maxSq, p.x * p.x + p.z * p.z);
        }
        return maxSq > 0f ? math.sqrt(maxSq) + 2.5f : 50f;
    }

    /// <summary>
    /// Capture nearby castle/tile entities and item pickups into a reusable schematic file.
    /// Structure and item positions are stored relative to <paramref name="center"/>.
    /// </summary>
    public static OperationResult<SchematicConfig> CaptureNearby(string eventName, float3 center, float radius, string? description = null)
    {
        if (!VRisingCore.IsReady)
            return OperationResult<SchematicConfig>.Fail("VRising core is not ready.");
        if (string.IsNullOrWhiteSpace(eventName))
            return OperationResult<SchematicConfig>.Fail("eventName is required.");
        if (radius <= 0f)
            return OperationResult<SchematicConfig>.Fail("radius must be greater than 0.");

        try
        {
            var em = VRisingCore.EntityManager;
            var capturedAt = DateTime.UtcNow;
            var schematic = new SchematicConfig
            {
                EventName = SanitizeEventName(eventName),
                Enabled = true,
                Description = string.IsNullOrWhiteSpace(description) ? $"Runtime capture around {center.x:F1},{center.y:F1},{center.z:F1}" : description,
                Version = "1.1",
                Center = ToVec3(center),
                Metadata = new SchematicMetadata
                {
                    Name = SanitizeEventName(eventName),
                    Created = capturedAt.ToString("O"),
                    Modified = capturedAt.ToString("O"),
                    Tags = new List<string> { "runtime-capture", "castle", "items" }
                },
                CastleDesign = new SchematicCastleDesign
                {
                    Name = SanitizeEventName(eventName),
                    CapturedAtUtc = capturedAt.ToString("O"),
                    Radius = radius,
                    Notes = new List<string>
                    {
                        "Captured from live non-player castle/tile entities and item pickups.",
                        "Positions are relative to the schematic center."
                    }
                }
            };

            var bounds = new BoundsAccumulator();
            CaptureStructures(em, center, radius, schematic, bounds);
            CaptureBuiltItems(em, center, radius, schematic, bounds);

            schematic.CastleDesign.StructureCount = schematic.Structures.Count;
            schematic.CastleDesign.BuiltItemCount = schematic.BuiltItems.Count;
            schematic.CastleDesign.Bounds = bounds.ToBounds();
            schematic.Targets = SchematicTargeting.BuildDeclaredTargets(schematic);

            SaveSchematic(schematic);
            return OperationResult<SchematicConfig>.Ok(schematic);
        }
        catch (Exception ex)
        {
            BattleLuckPlugin.LogError($"[SchematicLoader] Capture failed for '{eventName}': {ex.Message}");
            return OperationResult<SchematicConfig>.Fail(ex.Message);
        }
    }

    static void CaptureStructures(EntityManager em, float3 center, float radius, SchematicConfig schematic, BoundsAccumulator bounds)
    {
        var query = em.CreateEntityQuery(new EntityQueryDesc
        {
            All = new[] { ComponentType.ReadOnly<Translation>(), ComponentType.ReadOnly<PrefabGUID>() },
            Any = new[] { ComponentType.ReadOnly<EditableTileModel>(), ComponentType.ReadOnly<TilePosition>() },
            None = new[] { ComponentType.ReadOnly<PlayerCharacter>() }
        });

        var arr = query.ToEntityArray(Allocator.Temp);
        try
        {
            var radiusSq = radius * radius;
            for (int i = 0; i < arr.Length; i++)
            {
                var entity = arr[i];
                if (!em.Exists(entity)) continue;

                var world = em.GetComponentData<Translation>(entity).Value;
                if (!WithinXZ(world, center, radiusSq)) continue;

                var guid = em.GetComponentData<PrefabGUID>(entity);
                var prefabName = PrefabHelper.GetLivePrefabName(guid) ?? PrefabHelper.GetName(guid) ?? guid.GuidHash.ToString();
                var relative = world - center;
                bounds.Include(relative);

                schematic.Structures.Add(new SchematicStructure
                {
                    Type = PrefabHelper.ClassifyStructure(prefabName, entity),
                    Prefab = prefabName,
                    PrefabGuid = guid.GuidHash,
                    Position = ToVec3(relative),
                    Rotation = PrefabHelper.GetYawDegrees(entity),
                    Scale = 1f,
                    Group = ClassifyStructureGroup(prefabName, entity, em)
                });
            }
        }
        finally
        {
            arr.Dispose();
            query.Dispose();
        }
    }

    static void CaptureBuiltItems(EntityManager em, float3 center, float radius, SchematicConfig schematic, BoundsAccumulator bounds)
    {
        var query = em.CreateEntityQuery(new EntityQueryDesc
        {
            All = new[] { ComponentType.ReadOnly<Translation>(), ComponentType.ReadOnly<ItemPickup>() },
            None = new[] { ComponentType.ReadOnly<PlayerCharacter>() }
        });

        var arr = query.ToEntityArray(Allocator.Temp);
        try
        {
            var radiusSq = radius * radius;
            for (int i = 0; i < arr.Length; i++)
            {
                var entity = arr[i];
                if (!em.Exists(entity)) continue;

                var world = em.GetComponentData<Translation>(entity).Value;
                if (!WithinXZ(world, center, radiusSq)) continue;

                var guid = em.HasComponent<PrefabGUID>(entity)
                    ? em.GetComponentData<PrefabGUID>(entity)
                    : PrefabGUID.Empty;
                var prefabName = guid != PrefabGUID.Empty
                    ? PrefabHelper.GetLivePrefabName(guid) ?? PrefabHelper.GetName(guid) ?? guid.GuidHash.ToString()
                    : "ItemPickup";
                var relative = world - center;
                bounds.Include(relative);

                schematic.BuiltItems.Add(new SchematicBuiltItem
                {
                    Prefab = prefabName,
                    PrefabGuid = guid.GuidHash,
                    Amount = ReadItemPickupAmount(entity, em),
                    Position = ToVec3(relative)
                });
            }
        }
        finally
        {
            arr.Dispose();
            query.Dispose();
        }
    }

    static int ReadItemPickupAmount(Entity entity, EntityManager em)
    {
        try
        {
            if (!em.HasComponent<ItemPickup>(entity))
                return 1;

            var pickup = em.GetComponentData<ItemPickup>(entity);
            var type = pickup.GetType();
            foreach (var name in new[] { "Amount", "Stack", "StackSize", "Quantity", "ItemAmount" })
            {
                var field = type.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (field == null) continue;
                var value = field.GetValue(pickup);
                if (value is int i && i > 0) return i;
                if (value is uint u && u > 0) return (int)System.Math.Min(int.MaxValue, u);
                if (value is short s && s > 0) return s;
                if (value is ushort us && us > 0) return us;
            }
        }
        catch { }

        return 1;
    }

    static string ClassifyStructure(string prefabName, Entity entity, EntityManager em)
    {
        if (prefabName.Contains("Wall", StringComparison.OrdinalIgnoreCase)) return "wall";
        if (prefabName.Contains("Floor", StringComparison.OrdinalIgnoreCase)) return "floor";
        if (prefabName.Contains("Gate", StringComparison.OrdinalIgnoreCase)) return "gate";
        if (prefabName.Contains("Door", StringComparison.OrdinalIgnoreCase)) return "door";
        if (prefabName.Contains("Stair", StringComparison.OrdinalIgnoreCase) || prefabName.Contains("Ramp", StringComparison.OrdinalIgnoreCase)) return "ramp";
        if (em.HasComponent<TilePosition>(entity)) return "tile";
        if (em.HasComponent<EditableTileModel>(entity)) return "castle";
        return "structure";
    }

    static string ClassifyStructureGroup(string prefabName, Entity entity, EntityManager em)
    {
        var type = ClassifyStructure(prefabName, entity, em);
        return type switch
        {
            "wall" => "castle_walls",
            "floor" => "castle_floors",
            "gate" => "castle_gates",
            "door" => "castle_doors",
            "ramp" => "castle_ramps",
            _ => "castle_structures"
        };
    }

    static float GetYawDegrees(Entity entity, EntityManager em)
    {
        try
        {
            if (!em.HasComponent<Rotation>(entity))
                return 0f;
            var rotation = em.GetComponentData<Rotation>(entity).Value;
            var forward = math.mul(rotation, new float3(0f, 0f, 1f));
            return math.degrees(math.atan2(forward.x, forward.z));
        }
        catch
        {
            return 0f;
        }
    }

    static bool WithinXZ(float3 point, float3 center, float radiusSq)
    {
        var dx = point.x - center.x;
        var dz = point.z - center.z;
        return dx * dx + dz * dz <= radiusSq;
    }

    static Vec3Config ToVec3(float3 value) => new() { X = value.x, Y = value.y, Z = value.z };

    static string SanitizeEventName(string value)
    {
        var safe = new string(value.Trim().Select(ch => char.IsLetterOrDigit(ch) || ch is '_' or '-' ? ch : '_').ToArray());
        return string.IsNullOrWhiteSpace(safe) ? "schematic" : safe;
    }

    /// <summary>
    /// Create map marker entities for a schematic's defined markers.
    /// Delegates to ZoneMapIconService for ECS entity creation and lifecycle tracking.
    /// </summary>
    static int SpawnMapMarkers(SchematicConfig schematic, float3 center, string? trackingGroup)
    {
        if (schematic.MapMarkers.Count == 0 || !VRisingCore.IsReady)
            return 0;

        var zoneMap = BattleLuckPlugin.ZoneMap;
        if (zoneMap != null)
            return zoneMap.RegisterSchematicMarkers(
                string.IsNullOrWhiteSpace(trackingGroup) ? schematic.EventName : trackingGroup,
                center,
                schematic.MapMarkers);

        return 0;
    }

    sealed class BoundsAccumulator
    {
        bool _hasAny;
        float3 _min;
        float3 _max;

        public void Include(float3 value)
        {
            if (!_hasAny)
            {
                _min = value;
                _max = value;
                _hasAny = true;
                return;
            }

            _min = math.min(_min, value);
            _max = math.max(_max, value);
        }

        public SchematicBounds ToBounds()
        {
            if (!_hasAny)
                return new SchematicBounds();

            return new SchematicBounds
            {
                Min = ToVec3(_min),
                Max = ToVec3(_max)
            };
        }
    }

    sealed record SpawnedSchematicEntity(
        string EventName,
        Entity Entity,
        float3 Center,
        string Kind,
        DateTime SpawnedAtUtc,
        string? ReservationOwner,
        long ReservationKey);
}
