using HarmonyLib;
using ProjectM.Network;
using Unity.Collections;
using Unity.Entities;

[HarmonyPatch]
internal static class BattleLuckMapVisibilityPatches
{
    [HarmonyPatch(typeof(MapIconSpawnSystem), nameof(MapIconSpawnSystem.OnUpdate))]
    [HarmonyPrefix]
    static void MapIconSpawnSystem_OnUpdate_Prefix(MapIconSpawnSystem __instance)
    {
        try
        {
            BattleLuckPlugin.ZoneMap?.EnsureZoneMapIcons();

            var entities = __instance.__query_1050583545_0.ToEntityArray(Allocator.Temp);
            try
            {
                foreach (var entity in entities)
                {
                    if (!entity.Exists() || !entity.Has<Attach>())
                        continue;

                     var attached = entity.Read<Attach>().Parent;
                     if (attached == Entity.Null || !attached.Exists())
                         continue;

                     // Observation is intentionally silent; this system runs every update.
                }
            }
            finally
            {
                entities.Dispose();
            }
        }
        catch (Exception ex)
        {
            BattleLuckPlugin.LogWarning($"[ZoneMap] MapIconSpawnSystem patch skipped: {ex.Message}");
        }
    }

    [HarmonyPatch(typeof(ServerBootstrapSystem), "SendRevealedMapData")]
    [HarmonyPrefix]
    static void ServerBootstrapSystem_SendRevealedMapData_Prefix(ServerBootstrapSystem __instance, Entity userEntity, User user)
    {
        try
        {
            BattleLuckPlugin.ZoneMap?.RevealMapForPlayer(userEntity, "bootstrap");
        }
        catch (Exception ex)
        {
            BattleLuckPlugin.LogWarning($"[ZoneMap] Reveal map bootstrap patch skipped: {ex.Message}");
        }
    }
}
