using ProjectM.Terrain;
using Unity.Transforms;

namespace BattleLuck.Services.Zone;

public sealed class ZoneMapIconService
{
    readonly Dictionary<int, ZoneDefinition> _zones = new();
    readonly Dictionary<int, Entity> _zoneIcons = new();
    readonly Dictionary<string, List<Entity>> _schematicIcons = new(StringComparer.OrdinalIgnoreCase);
    readonly HashSet<ulong> _revealedPlayers = new();
    bool _initialized;
    bool _globalRevealSent;
    DateTime _lastEnsureUtc = DateTime.MinValue;

    public int ZoneCount => _zones.Count;
    public int IconCount => _zoneIcons.Count(kv => kv.Value.Exists());
    public int SchematicMarkerCount => _schematicIcons.Sum(kv => kv.Value.Count(e => e.Exists()));

    public void Initialize(GameModeRegistry registry)
    {
        if (!VRisingCore.IsReady)
            return;

        _zones.Clear();
        foreach (var modeId in registry.GetRegisteredModes())
        {
            var config = ConfigLoader.Load(modeId);
            foreach (var zone in config.Zones.Zones)
                _zones[zone.Hash] = zone;
        }

        _initialized = true;
        EnsureZoneMapIcons(force: true);
        BattleLuckPlugin.LogInfo($"[ZoneMap] Initialized {_zones.Count} zone map marker(s).");
    }

    public void RegisterZone(ZoneDefinition zone)
    {
        _zones[zone.Hash] = zone;
        EnsureZoneMapIcon(zone);
    }

    public void HandlePlayerEnteredZone(ulong steamId, Entity playerEntity, ZoneDefinition zone)
    {
        if (!_initialized)
            return;

        RegisterZone(zone);
        RevealMapForPlayer(playerEntity.GetUserEntity(), $"zone_enter:{zone.Hash}");

        if (!_globalRevealSent)
        {
            _globalRevealSent = true;
            RevealMapForAllPlayers($"first_zone_enter:{zone.Hash}");
        }
    }

    public void EnsureZoneMapIcons(bool force = false)
    {
        if (!_initialized || !VRisingCore.IsReady)
            return;

        if (!force && (DateTime.UtcNow - _lastEnsureUtc).TotalSeconds < 10)
            return;

        _lastEnsureUtc = DateTime.UtcNow;
        foreach (var zone in _zones.Values)
            EnsureZoneMapIcon(zone);
    }

    public void RevealMapForAllPlayers(string reason)
    {
        foreach (var player in VRisingCore.GetOnlinePlayers())
        {
            if (!player.Exists() || !player.IsPlayer())
                continue;

            RevealMapForPlayer(player.GetUserEntity(), reason);
        }
    }

    public void RevealMapForPlayer(Entity userEntity, string reason)
    {
        if (!userEntity.Exists() || !userEntity.Has<User>())
            return;

        var user = userEntity.Read<User>();
        if (user.PlatformId != 0 && !_revealedPlayers.Add(user.PlatformId) && !reason.StartsWith("bootstrap", StringComparison.OrdinalIgnoreCase))
            return;

        var added = DiscoverAllMapZones(userEntity);
        BattleLuckPlugin.LogInfo($"[ZoneMap] RevealMapForPlayer {user.CharacterName} ({user.PlatformId}) reason={reason} addedZones={added}.");
    }

    public void Shutdown()
    {
        var em = VRisingCore.IsReady ? VRisingCore.EntityManager : default;
        foreach (var icon in _zoneIcons.Values)
        {
            try
            {
                if (VRisingCore.IsReady && icon.Exists() && !icon.Has<PlayerCharacter>())
                    em.DestroyEntity(icon);
            }
            catch { }
        }

        foreach (var icons in _schematicIcons.Values)
        {
            foreach (var icon in icons)
            {
                try
                {
                    if (VRisingCore.IsReady && icon.Exists() && !icon.Has<PlayerCharacter>())
                        em.DestroyEntity(icon);
                }
                catch { }
            }
        }

        _zoneIcons.Clear();
        _schematicIcons.Clear();
        _zones.Clear();
        _revealedPlayers.Clear();
        _initialized = false;
        _globalRevealSent = false;
    }

    /// <summary>
    /// Register map markers for a loaded schematic. Creates ECS map icon entities
    /// and tracks them for cleanup when the schematic is unloaded.
    /// </summary>
    public int RegisterSchematicMarkers(string eventName, float3 center, IReadOnlyList<SchematicMapMarker> markers)
    {
        if (!VRisingCore.IsReady || markers.Count == 0)
            return 0;

        ClearSchematicMarkers(eventName);

        var em = VRisingCore.EntityManager;
        var created = new List<Entity>();

        foreach (var marker in markers)
        {
            try
            {
                var worldPos = center + marker.Position.ToFloat3();
                var icon = em.CreateEntity();

                em.AddComponentData(icon, new Translation { Value = worldPos });
                em.AddComponentData(icon, new LocalToWorld { Value = float4x4.Translate(worldPos) });
                em.AddComponentData(icon, new MapIconPosition
                {
                    TilePosition = new int2((int)MathF.Round(worldPos.x), (int)MathF.Round(worldPos.z))
                });
                em.AddComponentData(icon, new MapIconData
                {
                    RenderOrder = marker.RenderOrder,
                    TargetUser = Entity.Null,
                    IsSiegeWeapon = false,
                    ShowOnMinimap = marker.ShowOnMinimap,
                    ClampOnMinimap = true,
                    ShowOutsideVision = true,
                    RequiresReveal = false,
                    CustomImplementation = false,
                    AllySetting = MapIconShowSettings.Global,
                    EnemySetting = MapIconShowSettings.Global
                });

                created.Add(icon);
                BattleLuckPlugin.LogInfo($"[ZoneMap] Created schematic marker '{marker.Label}' for '{eventName}' at ({worldPos.x:F1},{worldPos.y:F1},{worldPos.z:F1}).");
            }
            catch (Exception ex)
            {
                BattleLuckPlugin.LogWarning($"[ZoneMap] Failed to create schematic marker '{marker.Label}': {ex.Message}");
            }
        }

        if (created.Count > 0)
            _schematicIcons[eventName] = created;

        return created.Count;
    }

    /// <summary>Clear all map markers registered for a specific schematic event.</summary>
    public void ClearSchematicMarkers(string eventName)
    {
        if (!_schematicIcons.TryGetValue(eventName, out var icons))
            return;

        if (VRisingCore.IsReady)
        {
            var em = VRisingCore.EntityManager;
            foreach (var icon in icons)
            {
                try
                {
                    if (icon.Exists() && !icon.Has<PlayerCharacter>())
                        em.DestroyEntity(icon);
                }
                catch { }
            }
        }

        _schematicIcons.Remove(eventName);
    }

    void EnsureZoneMapIcon(ZoneDefinition zone)
    {
        if (!VRisingCore.IsReady)
            return;

        if (_zoneIcons.TryGetValue(zone.Hash, out var existing) && existing.Exists())
            return;

        try
        {
            var em = VRisingCore.EntityManager;
            var pos = zone.TeleportSpawn?.ToFloat3() ?? zone.Position.ToFloat3();
            var icon = em.CreateEntity();

            em.AddComponentData(icon, new Translation { Value = pos });
            em.AddComponentData(icon, new LocalToWorld { Value = float4x4.Translate(pos) });
            em.AddComponentData(icon, new MapIconPosition
            {
                TilePosition = new int2((int)MathF.Round(pos.x), (int)MathF.Round(pos.z))
            });
            em.AddComponentData(icon, new MapIconData
            {
                RenderOrder = 90,
                TargetUser = Entity.Null,
                IsSiegeWeapon = false,
                ShowOnMinimap = true,
                ClampOnMinimap = true,
                ShowOutsideVision = true,
                RequiresReveal = false,
                CustomImplementation = false,
                AllySetting = MapIconShowSettings.Global,
                EnemySetting = MapIconShowSettings.Global
            });

            _zoneIcons[zone.Hash] = icon;
            BattleLuckPlugin.LogInfo($"[ZoneMap] Created map marker for zone {zone.Hash} '{zone.Name}' at {pos}.");
        }
        catch (Exception ex)
        {
            BattleLuckPlugin.LogWarning($"[ZoneMap] Failed to create map marker for zone {zone.Hash}: {ex.Message}");
        }
    }

    static int DiscoverAllMapZones(Entity userEntity)
    {
        var em = VRisingCore.EntityManager;
        if (!userEntity.Exists())
            return 0;

        if (!em.HasBuffer<DiscoveredMapZoneElement>(userEntity))
            return 0;

        var discovered = em.GetBuffer<DiscoveredMapZoneElement>(userEntity);
        var known = new HashSet<MapZoneId>();
        for (var i = 0; i < discovered.Length; i++)
            known.Add(discovered[i].ZoneId);

        var query = em.CreateEntityQuery(ComponentType.ReadOnly<MapZoneData>());
        var entities = query.ToEntityArray(Allocator.Temp);
        var added = 0;
        try
        {
            for (var i = 0; i < entities.Length; i++)
            {
                var entity = entities[i];
                if (!entity.Exists() || !entity.Has<MapZoneData>())
                    continue;

                var data = entity.Read<MapZoneData>();
                var zoneId = data.ZoneId;
                if (known.Contains(zoneId))
                    continue;

                discovered.Add(new DiscoveredMapZoneElement { ZoneId = zoneId });
                known.Add(zoneId);
                added++;
            }
        }
        finally
        {
            entities.Dispose();
            query.Dispose();
        }

        return added;
    }
}
