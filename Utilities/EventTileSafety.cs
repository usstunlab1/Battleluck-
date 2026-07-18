using ProjectM.CastleBuilding;
using ProjectM.Gameplay.Scripting;

namespace BattleLuck.Utilities;

public static class EventTileSafety
{
    static readonly string[] RoomGraphMarkers =
    {
        "CastleFloor",
        "CastleWall",
        "CastleRoom",
        "CastleBlock",
        "FloorRoom",
        "FloorBlock",
        "WallBlock",
        "RoomConnection",
        "RoomSearch",
        "CastleHeartConnection",
        "CastleTerritory",
        "SpawnGroup"
    };

    static readonly string[] SpawnTeamComponents =
    {
        "ProjectM.Team",
        "ProjectM.TeamReference",
        "TeamReference",
        "Team"
    };

    /// <summary>
    /// Resolves only tile-model prefabs that are safe to instantiate directly.
    /// This follows KindredSchematics' tile allow-list rules: require a live
    /// TM_ prefab and reject prefabs with world-transition, portal, waypoint,
    /// castle-heart, or Lucie potion script components.
    /// </summary>
    public static bool TryResolveSafeTileModelPrefab(
        PrefabGUID prefabGuid,
        out Entity prefabEntity,
        out string error)
        => TryResolveSafePrefab(prefabGuid, requireTileModel: true, out prefabEntity, out error);

    /// <summary>
    /// Applies KindredSchematics' dangerous-component exclusions to a general
    /// schematic entity without requiring it to be a TM_ tile model.
    /// </summary>
    public static bool TryResolveSafeSchematicPrefab(
        PrefabGUID prefabGuid,
        out Entity prefabEntity,
        out string error)
        => TryResolveSafePrefab(prefabGuid, requireTileModel: false, out prefabEntity, out error);

    static bool TryResolveSafePrefab(
        PrefabGUID prefabGuid,
        bool requireTileModel,
        out Entity prefabEntity,
        out string error)
    {
        prefabEntity = Entity.Null;
        error = string.Empty;

        if (prefabGuid == PrefabGUID.Empty || prefabGuid.GuidHash == 0)
        {
            error = "empty prefab";
            return false;
        }

        var prefabName = PrefabHelper.GetLivePrefabName(prefabGuid) ??
                         PrefabHelper.GetName(prefabGuid);
        if (string.IsNullOrWhiteSpace(prefabName))
        {
            error = $"prefab {prefabGuid.GuidHash} has no live prefab name";
            return false;
        }

        if (requireTileModel && !prefabName.StartsWith("TM_", StringComparison.OrdinalIgnoreCase))
        {
            error = $"prefab {prefabGuid.GuidHash} is not a TM_ tile model ({prefabName})";
            return false;
        }

        var prefabs = VRisingCore.PrefabCollectionSystem;
        if (!prefabs._PrefabGuidToEntityMap.TryGetValue(prefabGuid, out prefabEntity) &&
            !prefabs._PrefabLookupMap.TryGetValueWithoutLogging(prefabGuid, out prefabEntity))
        {
            prefabEntity = Entity.Null;
        }

        if (prefabEntity == Entity.Null || !VRisingCore.EntityManager.Exists(prefabEntity))
        {
            prefabEntity = Entity.Null;
            error = $"tile prefab {prefabName} ({prefabGuid.GuidHash}) is not present in the live prefab map";
            return false;
        }

        string? unsafeComponent = null;
        if (prefabEntity.Has<TransitionWhenInventoryIsEmpty>())
            unsafeComponent = "TransitionWhenInventoryIsEmpty";
        else if (prefabEntity.Has<ChunkWaypoint>())
            unsafeComponent = "ChunkWaypoint";
        else if (prefabEntity.Has<ChunkPortal>())
            unsafeComponent = "ChunkPortal";
        else if (prefabEntity.Has<CastleHeart>())
            unsafeComponent = "CastleHeart";
        else if (prefabEntity.Has<Script_Lucie_Potion_DataServer>())
            unsafeComponent = "Script_Lucie_Potion_DataServer";

        if (unsafeComponent != null)
        {
            prefabEntity = Entity.Null;
            error = $"tile prefab {prefabName} contains unsafe component {unsafeComponent}";
            return false;
        }

        return true;
    }

    public static int StripRoomGraphComponents(EntityManager em, Entity entity, bool stripTileGrid = true)
    {
        if (entity == Entity.Null || !em.Exists(entity))
            return 0;

        var toRemove = new List<ComponentType>();
        var componentTypes = em.GetComponentTypes(entity, Allocator.Temp);
        try
        {
            foreach (var componentType in componentTypes)
            {
                var typeName = GetTypeName(componentType);
                if (ShouldRemove(typeName, stripTileGrid))
                    toRemove.Add(componentType);
            }
        }
        finally
        {
            componentTypes.Dispose();
        }

        var removed = 0;
        foreach (var componentType in toRemove)
        {
            try
            {
                if (em.HasComponent(entity, componentType))
                {
                    em.RemoveComponent(entity, componentType);
                    removed++;
                }
            }
            catch
            {
                // Some generated IL2CPP component wrappers are not removable at runtime.
            }
        }

        return removed;
    }

    static string GetTypeName(ComponentType componentType)
    {
        try
        {
            var type = componentType.GetManagedType();
            return type?.FullName ?? type?.Name ?? componentType.ToString();
        }
        catch
        {
            return componentType.ToString();
        }
    }

    static bool ShouldRemove(string typeName, bool stripTileGrid)
    {
        if (string.IsNullOrWhiteSpace(typeName))
            return false;

        if (stripTileGrid &&
            (typeName.EndsWith(".TilePosition", StringComparison.Ordinal) ||
             typeName.EndsWith(".TileBounds", StringComparison.Ordinal) ||
             typeName.EndsWith("TilePosition", StringComparison.Ordinal) ||
             typeName.EndsWith("TileBounds", StringComparison.Ordinal)))
        {
            return true;
        }

        return RoomGraphMarkers.Any(marker =>
            typeName.Contains(marker, StringComparison.OrdinalIgnoreCase)) ||
            SpawnTeamComponents.Any(component =>
                typeName.Equals(component, StringComparison.Ordinal) ||
                typeName.EndsWith("." + component, StringComparison.Ordinal) ||
                typeName.EndsWith("+" + component, StringComparison.Ordinal));
    }
}
