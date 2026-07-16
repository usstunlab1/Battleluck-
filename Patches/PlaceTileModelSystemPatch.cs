using HarmonyLib;
using ProjectM;
using ProjectM.CastleBuilding;
using ProjectM.Network;
using Stunlock.Core;
using Unity.Collections;
using Unity.Entities;

/// <summary>
/// Patches PlaceTileModelSystem only to veto castle heart placement while
/// event free-build mode is enabled.
///
/// Actual restriction bypass is handled globally by BuildingRestrictionController
/// through SetDebugSettingEvent flags. This patch exists only to re-block castle
/// hearts while those global bypass flags are active.
/// </summary>
[HarmonyPatch]
internal static class PlaceTileModelSystemPatch
{
    // Castle heart prefab GUIDs — block these even in free-build mode
    static readonly PrefabGUID CastleHeart = new(-485435268);
    static readonly PrefabGUID CastleHeartRebuilding = new(-1994745735);

    [HarmonyPatch(typeof(PlaceTileModelSystem), nameof(PlaceTileModelSystem.OnUpdate))]
    [HarmonyPrefix]
    static void OnUpdatePrefix(PlaceTileModelSystem __instance)
    {
        if (!BuildingRestrictionController.RestrictionsDisabled) return;

        var em = VRisingCore.EntityManager;
        var buildEvents = __instance._BuildTileQuery.ToEntityArray(Allocator.Temp);

        try
        {
            foreach (var buildEvent in buildEvents)
            {
                var btme = buildEvent.Read<BuildTileModelEvent>();

                // Block castle hearts even during events
                if (btme.PrefabGuid == CastleHeart || btme.PrefabGuid == CastleHeartRebuilding)
                {
                    if (buildEvent.Has<FromCharacter>())
                    {
                        var fromCharacter = buildEvent.Read<FromCharacter>();
                        if (fromCharacter.User.Has<User>())
                        {
                            var user = fromCharacter.User.Read<User>();
                            var message = new FixedString512Bytes(
                                "<color=red>Castle Hearts cannot be placed during events.</color>");
                            ServerChatUtils.SendSystemMessageToClient(em, user, ref message);
                        }
                    }

                    em.DestroyEntity(buildEvent);
                }
            }
        }
        catch (Exception ex)
        {
            BattleLuckPlugin.LogWarning($"[BuildPatch] Error: {ex.Message}");
        }
        finally
        {
            buildEvents.Dispose();
        }
    }
}
