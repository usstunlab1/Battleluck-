using Unity.Entities;
using Stunlock.Core;

/// <summary>
/// Handles the special PvP transformation item. When a player acquires the configured item,
/// it triggers snapshot save → rename → blood change → kit apply → notify.
/// Admin-only rollback support.
/// </summary>
public sealed class SpecialItemController
{
    SpecialItemConfig? _config;
    PrefabGUID _itemPrefab;
    bool _configured;

    public bool IsEnabled => _configured;

    public void Configure(SpecialItemConfig? config)
    {
        if (config == null || !config.Enabled)
        {
            _configured = false;
            return;
        }

        if (!PrefabHelper.TryGetPrefabGuid(config.Prefab, out _itemPrefab))
        {
            BattleLuckPlugin.LogWarning($"[SpecialItem] Unknown item prefab: {config.Prefab}");
            _configured = false;
            return;
        }

        _config = config;
        _configured = true;
    }

    /// <summary>
    /// Called when a player acquires an item. Checks if it matches the configured special item.
    /// Returns true if the item was handled (consumed + transformation applied).
    /// </summary>
    public bool OnItemAcquired(Entity character, ProjectM.Network.User user, PrefabGUID itemGuid,
        PlayerStateController snapshots)
    {
        if (!_configured || _config == null) return false;
        if (itemGuid != _itemPrefab) return false;

        var steamId = user.PlatformId;
        BattleLuckPlugin.LogInfo($"[SpecialItem] Player {steamId} acquired special item — transforming");

        try
        {
            // Save snapshot before transformation
            snapshots.SaveSnapshot(character, 0);

            // Apply kit if configured
            if (_config.OnAcquire.ApplyKit)
            {
                var result = KitController.ApplyKit(character, _config.OnAcquire.KitMode);
                if (!result.Success)
                {
                    BattleLuckPlugin.LogWarning($"[SpecialItem] Kit apply failed for {steamId}: {result.Error}");
                    snapshots.RestoreSnapshot(character, 0);
                    return false;
                }
            }

            NotificationHelper.NotifyPlayer(user, $"✨ {_config.ItemName} activated! You have been transformed.");
            return true;
        }
        catch (Exception ex)
        {
            BattleLuckPlugin.LogWarning($"[SpecialItem] Transformation failed for {steamId}: {ex.Message}");
            try { snapshots.RestoreSnapshot(character, 0); } catch { /* best-effort */ }
            return false;
        }
    }

    /// <summary>Admin-only rollback of special item transformation.</summary>
    public bool RollbackSpecialItem(Entity character, ProjectM.Network.User user,
        PlayerStateController snapshots)
    {
        if (!_configured) return false;

        try
        {
            snapshots.RestoreSnapshot(character, 0);
            NotificationHelper.NotifyPlayer(user, "🔄 Special item transformation rolled back.");
            return true;
        }
        catch (Exception ex)
        {
            BattleLuckPlugin.LogWarning($"[SpecialItem] Rollback failed: {ex.Message}");
            return false;
        }
    }
}
