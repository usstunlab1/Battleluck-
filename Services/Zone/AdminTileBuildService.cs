using ProjectM.Tiles;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace BattleLuck.Services.Zone;

/// <summary>
/// Queues real V Rising build events from an online admin so castle tiles are
/// owned by that admin/castle path instead of anonymous direct instantiation.
/// </summary>
public static class AdminTileBuildService
{
    const float BattleLuckCastleRadius = 1000f;

    public static float CastleRadius => BattleLuckCastleRadius;

    public static bool TryQueueTileBuild(PrefabGUID prefab, float3 position, out string error)
    {
        return TryQueueTileBuildInternal(prefab, position, allowNonAdminFallback: true, out error);
    }

    public static bool TryQueueTileBuildAdminOnly(PrefabGUID prefab, float3 position, out string error)
    {
        return TryQueueTileBuildInternal(prefab, position, allowNonAdminFallback: false, out error);
    }

    static bool TryQueueTileBuildInternal(PrefabGUID prefab, float3 position, bool allowNonAdminFallback, out string error)
    {
        error = string.Empty;

        if (prefab == PrefabGUID.Empty || prefab.GuidHash == 0)
        {
            error = "empty prefab";
            return false;
        }

        // BuildTileModelEvent expects a real tile-model prefab. Feeding it a
        // BP_ construction blueprint can terminate the native placement system
        // before managed code has a chance to log an exception.
        if (!EventTileSafety.TryResolveSafeTileModelPrefab(prefab, out _, out error))
            return false;

        if (!TryGetOnlineBuildCharacter(out var adminCharacter, out var adminUser, allowNonAdminFallback))
        {
            error = allowNonAdminFallback ? GetNoBuilderError() : "no online admin character";
            return false;
        }

        try
        {
            var em = VRisingCore.EntityManager;
            var buildEvent = em.CreateEntity();
            em.AddComponentData(buildEvent, new FromCharacter
            {
                Character = adminCharacter,
                User = adminUser
            });
            em.AddComponentData(buildEvent, new BuildTileModelEvent
            {
                PrefabGuid = prefab,
                SpawnTranslation = new Translation { Value = position },
                SpawnTileRotation = TileRotation.None,
                ResourceConsumeType = BuildResourceConsumeType.SharedInventory
            });

            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    public static bool CanQueueTileBuild(out string error)
    {
        if (TryGetOnlineBuildCharacter(out _, out _, allowNonAdminFallback: true))
        {
            error = string.Empty;
            return true;
        }

        error = GetNoBuilderError();
        return false;
    }

    public static bool CanQueueTileBuildAdminOnly(out string error)
    {
        if (TryGetOnlineBuildCharacter(out _, out _, allowNonAdminFallback: false))
        {
            error = string.Empty;
            return true;
        }

        error = "no online admin character";
        return false;
    }

    static bool TryGetOnlineBuildCharacter(out Entity character, out Entity userEntity, bool allowNonAdminFallback)
    {
        character = Entity.Null;
        userEntity = Entity.Null;
        Entity fallbackCharacter = Entity.Null;
        Entity fallbackUserEntity = Entity.Null;

        foreach (var player in VRisingCore.GetOnlinePlayers())
        {
            if (!player.Exists() || !player.IsPlayer())
                continue;

            var candidateUser = player.GetUserEntity();
            if (!candidateUser.Exists() || !candidateUser.Has<User>())
                continue;

            var user = candidateUser.Read<User>();
            if (!fallbackCharacter.Exists())
            {
                fallbackCharacter = player;
                fallbackUserEntity = candidateUser;
            }

            if (!user.IsAdmin)
                continue;

            character = player;
            userEntity = candidateUser;
            return true;
        }

        if (allowNonAdminFallback && BuildingRestrictionController.RestrictionsDisabled && fallbackCharacter.Exists())
        {
            character = fallbackCharacter;
            userEntity = fallbackUserEntity;
            return true;
        }

        return false;
    }

    static string GetNoBuilderError()
    {
        return BuildingRestrictionController.RestrictionsDisabled
            ? "no online player character"
            : "no online admin character";
    }
}
