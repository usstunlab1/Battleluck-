using Unity.Entities;

namespace BattleLuck.ECS.Actions.Components;

/// <summary>
/// ECS action component for sending items to player inventories.
/// Replaces the "inventory.send" flow action string.
/// </summary>
public struct InventorySendAction
{
    public Entity TargetEntity;
    public int ItemId;
    public int Amount;
    public Entity DestinationEntity; // Optional, defaults to TargetEntity
    public Entity SessionEntity;
}
