using ProjectM.CastleBuilding;
using ProjectM.Tiles;

namespace BattleLuck.Services;

/// <summary>
/// Keeps BattleLuck real floor/tile/wall spawns attached to the first online admin's castle.
/// The castle heart is permanent: cleanup helpers must skip it and anything with a
/// CastleHeartConnection. BattleLuck only owns/removes visual event entities.
/// </summary>
public static class CastleTileOwnershipService
{
    const float DefaultRadius = 1000f;
    static readonly PrefabGUID CastleHeartPrefab = new(-485435268);
    static readonly object Sync = new();

    static CastleAnchorState? _state;
    static Entity _anchorHeart = Entity.Null;
    static Entity _anchorAdminCharacter = Entity.Null;
    static Entity _anchorAdminUser = Entity.Null;
    static DateTime _nextScanUtc = DateTime.MinValue;
    static DateTime _nextBuildAttemptUtc = DateTime.MinValue;
    static int _stampedTiles;

    static string StatePath => Path.Combine(ConfigLoader.ConfigRoot, "castle_anchor_state.json");

    public static float Radius => LoadState().Radius > 0f ? LoadState().Radius : DefaultRadius;

    public static void Tick(IReadOnlyList<Entity> onlinePlayers)
    {
        // Disabled deliberately: auto-queuing a castle heart during event entry
        // can hard-crash the dedicated server. Only explicit tile actions may
        // attach to an already-existing admin castle.
    }

    public static bool TryEnsureAnchorForCurrentAdmin(out string error)
    {
        var players = VRisingCore.IsReady ? VRisingCore.GetOnlinePlayers() : new List<Entity>();
        return TryEnsureAnchor(players, allowBuildRequest: false, out error);
    }

    public static bool TryStampOwnedTile(Entity entity, float3 position, out string? warning) =>
        TryStampOwnedTile(entity, position, prefabName: null, out warning);

    public static bool TryStampOwnedTile(Entity entity, float3 position, string? prefabName, out string? warning)
    {
        warning = null;
        if (!entity.Exists())
        {
            warning = "Tile entity does not exist.";
            return false;
        }

        if (!string.IsNullOrWhiteSpace(prefabName) && !IsTilePrefabName(prefabName))
        {
            warning = $"'{prefabName}' is not a floor/tile/wall prefab. Real castle ownership is limited to castle arena geometry.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(prefabName) && !LooksLikeCastleGeometryEntity(entity))
        {
            warning = "Entity does not look like a castle floor/tile/wall entity. Real castle ownership is limited to castle arena geometry.";
            return false;
        }

        if (IsPermanentCastleEntity(entity))
            return true;

        if (!TryEnsureAnchorForCurrentAdmin(out var anchorError))
        {
            warning = anchorError;
            return false;
        }

        var em = VRisingCore.EntityManager;
        try
        {
            if (!entity.Has<UserOwner>())
                em.AddComponent<UserOwner>(entity);
            entity.Write(new UserOwner { Owner = NetworkedEntity.ServerEntity(_anchorAdminUser) });

            if (!entity.Has<CastleHeartConnection>())
                em.AddComponent<CastleHeartConnection>(entity);
            entity.Write(new CastleHeartConnection { CastleHeartEntity = NetworkedEntity.ServerEntity(_anchorHeart) });

            if (_anchorAdminCharacter.Has<Team>())
            {
                var adminTeam = _anchorAdminCharacter.Read<Team>();
                if (entity.Has<Team>())
                    entity.Write(adminTeam);
            }

            if (entity.Has<EditableTileModel>())
            {
                var editable = entity.Read<EditableTileModel>();
                editable.CanDismantle = false;
                entity.Write(editable);
            }

            ExpandAnchorRadius(position);
            _stampedTiles++;
            if (_stampedTiles % 50 == 0)
                BattleLuckPlugin.LogInfo($"[CastleTiles] Stamped {_stampedTiles} spawned tile(s) to admin castle owner.");
            return true;
        }
        catch (Exception ex)
        {
            warning = ex.Message;
            return false;
        }
    }

    public static bool IsPermanentCastleEntity(Entity entity)
    {
        try
        {
            return entity.Exists() &&
                   (entity.Has<CastleHeart>() ||
                    entity.Has<CastleTerritory>() ||
                    entity.Has<CastleHeartConnection>());
        }
        catch
        {
            return false;
        }
    }

    public static bool IsTilePrefabName(string? prefabName)
        => IsOwnedCastleGeometryPrefabName(prefabName);

    public static bool IsOwnedCastleGeometryPrefabName(string? prefabName)
    {
        if (string.IsNullOrWhiteSpace(prefabName))
            return false;

        if (prefabName.Contains("Door", StringComparison.OrdinalIgnoreCase) ||
            prefabName.Contains("Gate", StringComparison.OrdinalIgnoreCase) ||
            prefabName.Contains("Pillar", StringComparison.OrdinalIgnoreCase) ||
            prefabName.Contains("Fence", StringComparison.OrdinalIgnoreCase))
            return false;

        return prefabName.Contains("Floor", StringComparison.OrdinalIgnoreCase) ||
               prefabName.Contains("Tile", StringComparison.OrdinalIgnoreCase) ||
               prefabName.Contains("Wall", StringComparison.OrdinalIgnoreCase);
    }

    static bool LooksLikeCastleGeometryEntity(Entity entity)
    {
        try
        {
            return entity.Exists() &&
                   entity.Has<TilePosition>() &&
                   entity.Has<TileBounds>() &&
                   entity.Has<EditableTileModel>();
        }
        catch
        {
            return false;
        }
    }

    static bool TryEnsureAnchor(IReadOnlyList<Entity> onlinePlayers, bool allowBuildRequest, out string error)
    {
        lock (Sync)
        {
            error = "";
            if (CachedAnchorIsValid())
            {
                ExpandAnchorRadius(_anchorHeart.GetPosition());
                return true;
            }

            if (!TryFindAdmin(onlinePlayers, out var adminCharacter, out var adminUser, out var admin))
            {
                error = "No online admin was found for castle tile ownership.";
                return false;
            }

            if (TryFindAdminCastleHeart(adminCharacter, adminUser, out var heart))
            {
                SetAnchor(heart, adminCharacter, adminUser, admin);
                ExpandAnchorRadius(heart.GetPosition());
                return true;
            }

            error = $"No existing castle heart owned by admin {admin.CharacterName} was found; automatic castle-heart creation is disabled.";
            return false;
        }
    }

    static bool CachedAnchorIsValid() =>
        _anchorHeart.Exists() &&
        _anchorAdminCharacter.Exists() &&
        _anchorAdminUser.Exists() &&
        _anchorHeart.Has<CastleHeart>();

    static bool TryFindAdmin(IReadOnlyList<Entity> players, out Entity character, out Entity userEntity, out User user)
    {
        character = Entity.Null;
        userEntity = Entity.Null;
        user = default;

        var preferredSteamId = LoadState().AdminSteamId;
        foreach (var player in players.Where(p => p.Exists() && p.IsPlayer()))
        {
            if (!FlowController.TryGetUser(player, out var candidate))
                continue;
            if (!candidate.IsConnected || !candidate.IsAdmin)
                continue;
            if (preferredSteamId != 0 && candidate.PlatformId != preferredSteamId)
                continue;

            character = player;
            userEntity = player.GetUserEntity();
            user = candidate;
            return true;
        }

        foreach (var player in players.Where(p => p.Exists() && p.IsPlayer()))
        {
            if (!FlowController.TryGetUser(player, out var candidate))
                continue;
            if (!candidate.IsConnected || !candidate.IsAdmin)
                continue;

            character = player;
            userEntity = player.GetUserEntity();
            user = candidate;
            return true;
        }

        return false;
    }

    static bool TryFindAdminCastleHeart(Entity adminCharacter, Entity adminUser, out Entity heart)
    {
        heart = Entity.Null;
        var em = VRisingCore.EntityManager;
        var adminTeam = adminCharacter.Has<Team>() ? adminCharacter.Read<Team>() : default;
        var hasAdminTeam = adminCharacter.Has<Team>();

        var query = em.CreateEntityQuery(new EntityQueryDesc
        {
            All = new[] { ComponentType.ReadOnly<CastleHeart>() }
        });

        var hearts = query.ToEntityArray(Allocator.Temp);
        try
        {
            foreach (var candidate in hearts)
            {
                if (!em.Exists(candidate))
                    continue;

                if (candidate.Has<UserOwner>() &&
                    candidate.Read<UserOwner>().Owner.GetEntityOnServer() == adminUser)
                {
                    heart = candidate;
                    return true;
                }

                var castleHeart = candidate.Read<CastleHeart>();
                if (castleHeart.LastUserOwner.GetEntityOnServer() == adminUser)
                {
                    heart = candidate;
                    return true;
                }

                if (hasAdminTeam && candidate.Has<CastleHeartCurrentTeam>() &&
                    candidate.Read<CastleHeartCurrentTeam>().Team.Value == adminTeam.Value)
                {
                    heart = candidate;
                    return true;
                }
            }
        }
        finally
        {
            hearts.Dispose();
            query.Dispose();
        }

        return false;
    }

    static void SetAnchor(Entity heart, Entity adminCharacter, Entity adminUser, User admin)
    {
        _anchorHeart = heart;
        _anchorAdminCharacter = adminCharacter;
        _anchorAdminUser = adminUser;

        var state = LoadState();
        state.AdminSteamId = admin.PlatformId;
        state.AdminName = admin.CharacterName.ToString();
        state.Radius = DefaultRadius;
        state.HeartEntityIndex = heart.Index;
        state.HeartEntityVersion = heart.Version;
        state.LastSeenUtc = DateTime.UtcNow.ToString("O");
        if (heart.Has<Translation>())
            state.Position = Vec3Config.FromFloat3(heart.Read<Translation>().Value);
        SaveState(state);
    }

    static void QueueCastleHeartBuild(Entity adminCharacter, Entity adminUser, User admin)
    {
        var em = VRisingCore.EntityManager;
        var position = SnapToBuildGrid(adminCharacter.GetPosition() + new float3(3f, 0f, 3f));
        var ev = em.CreateEntity();
        em.AddComponentData(ev, new FromCharacter { Character = adminCharacter, User = adminUser });
        em.AddComponentData(ev, new BuildTileModelEvent
        {
            PrefabGuid = CastleHeartPrefab,
            SpawnTranslation = new Translation { Value = position },
            SpawnTileRotation = TileRotation.None,
            VariationIndex = 0,
            ResourceConsumeType = BuildResourceConsumeType.LocalInventory,
            RebuildUniqueKey = default
        });

        var state = LoadState();
        state.AdminSteamId = admin.PlatformId;
        state.AdminName = admin.CharacterName.ToString();
        state.Radius = DefaultRadius;
        state.Position = Vec3Config.FromFloat3(position);
        state.LastBuildRequestedUtc = DateTime.UtcNow.ToString("O");
        SaveState(state);

        BattleLuckPlugin.LogInfo($"[CastleTiles] Queued one admin-owned castle heart build for {state.AdminName} ({state.AdminSteamId}) at ({position.x:F1},{position.y:F1},{position.z:F1}), radius={DefaultRadius:F0}.");
    }

    static void ExpandAnchorRadius(float3 includePosition)
    {
        if (!_anchorHeart.Exists() || !_anchorHeart.Has<CastleHeart>())
            return;

        var em = VRisingCore.EntityManager;
        var heartPosition = _anchorHeart.Has<Translation>() ? _anchorHeart.Read<Translation>().Value : includePosition;
        var centerTile = ToTilePosition(heartPosition);
        var includeTile = ToTilePosition(includePosition);
        var radiusTiles = Math.Max(1, (int)Math.Ceiling(DefaultRadius * 2f));

        var bounds = new BoundsMinMax
        {
            Min = math.min(centerTile - new int2(radiusTiles, radiusTiles), includeTile),
            Max = math.max(centerTile + new int2(radiusTiles, radiusTiles), includeTile)
        };

        var territoryEntity = _anchorHeart.Read<CastleHeart>().CastleTerritoryEntity;
        if (territoryEntity.Exists() && territoryEntity.Has<CastleTerritory>())
        {
            ExpandTerritory(territoryEntity, bounds);
            return;
        }

        var query = em.CreateEntityQuery(ComponentType.ReadOnly<CastleTerritory>());
        var territories = query.ToEntityArray(Allocator.Temp);
        try
        {
            foreach (var entity in territories)
            {
                if (!entity.Exists())
                    continue;
                var territory = entity.Read<CastleTerritory>();
                if (territory.CastleHeart != _anchorHeart)
                    continue;

                ExpandTerritory(entity, bounds);
                return;
            }
        }
        finally
        {
            territories.Dispose();
            query.Dispose();
        }
    }

    static void ExpandTerritory(Entity territoryEntity, BoundsMinMax bounds)
    {
        var territory = territoryEntity.Read<CastleTerritory>();
        territory.WorldBounds = bounds;
        territory.IsGlobalDebugTerritory = false;
        territoryEntity.Write(territory);
    }

    static int2 ToTilePosition(float3 position) =>
        new((int)math.round(position.x * 2f), (int)math.round(position.z * 2f));

    static float3 SnapToBuildGrid(float3 position) =>
        new(math.round(position.x / 2.5f) * 2.5f, position.y, math.round(position.z / 2.5f) * 2.5f);

    static CastleAnchorState LoadState()
    {
        if (_state != null)
            return _state;

        try
        {
            if (File.Exists(StatePath))
            {
                var json = File.ReadAllText(StatePath);
                _state = JsonSerializer.Deserialize<CastleAnchorState>(json, ConfigLoader.JsonOptions) ?? new CastleAnchorState();
            }
            else
            {
                _state = new CastleAnchorState();
            }
        }
        catch (Exception ex)
        {
            BattleLuckPlugin.LogWarning($"[CastleTiles] Failed to load castle_anchor_state.json: {ex.Message}");
            _state = new CastleAnchorState();
        }

        if (_state.Radius <= 0f)
            _state.Radius = DefaultRadius;
        return _state;
    }

    static void SaveState(CastleAnchorState state)
    {
        _state = state;
        try
        {
            Directory.CreateDirectory(ConfigLoader.ConfigRoot);
            var json = JsonSerializer.Serialize(state, new JsonSerializerOptions(ConfigLoader.JsonOptions)
            {
                WriteIndented = true
            });
            File.WriteAllText(StatePath, json);
        }
        catch (Exception ex)
        {
            BattleLuckPlugin.LogWarning($"[CastleTiles] Failed to save castle_anchor_state.json: {ex.Message}");
        }
    }

    sealed class CastleAnchorState
    {
        [JsonPropertyName("adminSteamId")]
        public ulong AdminSteamId { get; set; }

        [JsonPropertyName("adminName")]
        public string AdminName { get; set; } = "";

        [JsonPropertyName("radius")]
        public float Radius { get; set; } = DefaultRadius;

        [JsonPropertyName("position")]
        public Vec3Config Position { get; set; } = new();

        [JsonPropertyName("heartEntityIndex")]
        public int HeartEntityIndex { get; set; }

        [JsonPropertyName("heartEntityVersion")]
        public int HeartEntityVersion { get; set; }

        [JsonPropertyName("lastSeenUtc")]
        public string LastSeenUtc { get; set; } = "";

        [JsonPropertyName("lastBuildRequestedUtc")]
        public string LastBuildRequestedUtc { get; set; } = "";
    }
}
