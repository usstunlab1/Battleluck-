using HarmonyLib;
using ProjectM.Shared;
using Unity.Transforms;

[HarmonyPatch]
internal static class BattleLuckInventoryEquipmentPatches
{
    [HarmonyPatch(typeof(DropInventorySystem), nameof(DropInventorySystem.DropItem))]
    [HarmonyPostfix]
    static void DropInventorySystem_DropItem_Postfix(
        DropInventorySystem __instance,
        EntityCommandBuffer commandBuffer,
        ref Translation translation,
        PrefabGUID itemHash,
        int amount,
        Entity itemEntity)
    {
        try
        {
            BattleLuckPlugin.Session?.AutoTrash.HandleDrop(itemHash, amount, itemEntity, translation.Value);
        }
        catch (Exception ex)
        {
            BattleLuckPlugin.LogWarning($"[AutoTrash] Drop hook failed: {ex.Message}");
        }
    }

    [HarmonyPatch(typeof(EquipItemSystem), nameof(EquipItemSystem.OnUpdate))]
    [HarmonyPrefix]
    static void EquipItemSystem_OnUpdate_Prefix(EquipItemSystem __instance)
    {
        if (!VRisingCore.IsReady)
            return;

        NativeArray<Entity> entities;
        try
        {
            entities = __instance._EventQuery.ToEntityArray(Allocator.Temp);
        }
        catch
        {
            return;
        }

        try
        {
            foreach (var entity in entities)
            {
                if (!entity.Exists() || !entity.Has<FromCharacter>() || !entity.Has<EquipItemEvent>())
                    continue;

                var fromCharacter = entity.Read<FromCharacter>();
                var character = fromCharacter.Character;
                if (!character.Exists() || character.Has<VampireAttributeCaps>())
                    continue;

                var equipEvent = entity.Read<EquipItemEvent>();
                if (!TryGetInventoryItem(character, equipEvent.SlotIndex, out var itemEntity))
                    continue;

                if (!itemEntity.Exists() || !itemEntity.Has<LegendaryItemGeneratorTemplate>())
                    continue;

                RestoreVampireAttributeCapsTemporarily(character);
            }
        }
        finally
        {
            if (entities.IsCreated)
            {
                try
                {
                    entities.Dispose();
                }
                catch (EntryPointNotFoundException)
                {
                    // Allocator.Temp memory is reclaimed at frame end. Avoid
                    // throwing through the native-to-managed trampoline.
                }
            }
        }
    }

    static bool TryGetInventoryItem(Entity character, int slotIndex, out Entity itemEntity)
    {
        itemEntity = Entity.Null;
        var em = VRisingCore.EntityManager;
        if (!InventoryUtilities.TryGetInventoryEntity(em, character, out var inventoryEntity) ||
            !inventoryEntity.Exists() ||
            !em.HasBuffer<InventoryBuffer>(inventoryEntity))
        {
            return false;
        }

        var inventory = em.GetBuffer<InventoryBuffer>(inventoryEntity);
        if (slotIndex < 0 || slotIndex >= inventory.Length)
            return false;

        itemEntity = inventory[slotIndex].ItemEntity.GetEntityOnServer();
        return itemEntity.Exists();
    }

    static void RestoreVampireAttributeCapsTemporarily(Entity character)
    {
        try
        {
            var em = VRisingCore.EntityManager;
            if (!character.Has<PrefabGUID>())
                return;

            var prefabGuid = character.Read<PrefabGUID>();
            if (!VRisingCore.PrefabCollectionSystem._PrefabGuidToEntityMap.TryGetValue(prefabGuid, out var prefab) ||
                !prefab.Exists() ||
                !prefab.Has<VampireAttributeCaps>())
            {
                return;
            }

            em.AddComponentData(character, prefab.Read<VampireAttributeCaps>());
            MainThreadDispatcher.Enqueue(() =>
            {
                try
                {
                    if (character.Exists() && character.Has<VampireAttributeCaps>())
                        em.RemoveComponent<VampireAttributeCaps>(character);
                }
                catch { }
            });
        }
        catch (Exception ex)
        {
            BattleLuckPlugin.LogWarning($"[EquipPatch] VampireAttributeCaps repair failed: {ex.Message}");
        }
    }
}
