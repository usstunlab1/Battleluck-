namespace BattleLuck.ECS.Actions.Components;

/// <summary>
/// ECS action component for clearing kit items from player inventory.
/// Replaces the "inventory.clear_kit" flow action string.
/// </summary>
public struct InventoryClearKitAction
{
    public Entity TargetEntity;
    public FixedString64Bytes KitId;
    public Entity SessionEntity;
}
