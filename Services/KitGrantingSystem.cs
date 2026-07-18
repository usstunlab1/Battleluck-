/// <summary>
/// Routes completed crafting rules to the kit application system.
///
/// This class is only the crafting-event adapter.
/// Kit loading and mutation logic remain owned by KitController.
/// </summary>
public static class KitGrantingSystem
{
    /// <summary>
    /// Called when a crafting job completes. Resolves the player by steamId on the main thread
    /// to avoid capturing stale User references across the queued callback.
    /// </summary>
    public static void OnCraftCompleted(ulong steamId, PrefabGUID craftedItem)
    {
        if (steamId == 0)
            return;

        if (!KitRules.TryGetKitForItem(craftedItem, out var kitId) ||
            string.IsNullOrWhiteSpace(kitId))
        {
            return;
        }

        MainThreadDispatcher.Enqueue(() =>
        {
            try
            {
                if (!VRisingCore.IsReady)
                {
                    BattleLuckPlugin.LogWarning(
                        $"[KitGranting] Server world unavailable; " +
                        $"skipping kit '{kitId}' for {steamId}.");
                    return;
                }

                var character = VRisingCore.GetOnlinePlayers()
                    .FirstOrDefault(entity =>
                        entity.Exists() &&
                        entity.GetSteamId() == steamId);

                if (!character.IsValidPlayer(out User user))
                {
                    BattleLuckPlugin.LogWarning(
                        $"[KitGranting] Player {steamId} is no longer online; " +
                        $"skipping kit '{kitId}'.");
                    return;
                }

                // Use ApplyKit with AdditiveReward mode for crafting rewards
                // (grants items without replacing the full loadout)
                var result = KitController.ApplyKit(character, kitId, KitApplyMode.AdditiveReward);
                if (!result.Success)
                {
                    BattleLuckPlugin.LogWarning(
                        $"[KitGranting] Failed to apply kit '{kitId}' " +
                        $"to {steamId}: {result.Error}");
                    return;
                }

                var itemName =
                    PrefabHelper.GetLivePrefabName(craftedItem) ??
                    craftedItem.GuidHash.ToString();

                NotificationHelper.NotifyPlayer(
                    user,
                    $"Kit '{kitId}' granted for crafting {itemName}.");

                BattleLuckPlugin.LogInfo(
                    $"[KitGranting] Applied kit '{kitId}' to {steamId} " +
                    $"after crafting '{itemName}'.");
            }
            catch (Exception ex)
            {
                BattleLuckPlugin.LogWarning(
                    $"[KitGranting] Unexpected failure for {steamId}: {ex}");
            }
        });
    }
}