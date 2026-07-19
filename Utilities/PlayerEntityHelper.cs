using ProjectM;
using ProjectM.Network;
using Unity.Entities;

namespace BattleLuck.Utilities;

/// <summary>
/// Helper for resolving player entities by Steam ID.
/// Queries the active player character entities via the ECS world.
/// </summary>
public static class PlayerEntityHelper
{
    /// <summary>
    /// Find a player's character entity by their Steam ID.
    /// Returns Entity.Null if the player is not online or has no active character.
    /// </summary>
    public static Entity GetEntityBySteamId(ulong steamId)
    {
        if (steamId == 0) return Entity.Null;

        var em = VRisingCore.EntityManager;
        // Query all entities with PlayerCharacter + User to find matching Steam ID
        var query = em.CreateEntityQuery(
            ComponentType.ReadOnly<PlayerCharacter>(),
            ComponentType.ReadOnly<User>()
        );

        try
        {
            var entities = query.ToEntityArray(Unity.Collections.Allocator.Temp);
            try
            {
                for (int i = 0; i < entities.Length; i++)
                {
                    var userEntity = entities[i];
                    if (!userEntity.Exists()) continue;
                    if (!userEntity.Has<User>()) continue;

                    var user = userEntity.Read<User>();
                    if (user.PlatformId == steamId)
                    {
                        // The entity with PlayerCharacter is the character entity itself
                        return userEntity;
                    }
                }
            }
            finally
            {
                entities.Dispose();
            }
        }
        catch
        {
            // Query may fail during world shutdown
        }

        return Entity.Null;
    }
}
