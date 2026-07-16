using Unity.Entities;

namespace BattleLuck.Services;

/// <summary>Canonical owner for event loadout mutation and snapshot rollback.</summary>
public sealed class PlayerLoadoutService
{
    readonly PlayerStateController _snapshots;
    public PlayerLoadoutService(PlayerStateController snapshots) => _snapshots = snapshots;

    public OperationResult Prepare(Entity player, int zoneHash, Unity.Mathematics.float3? returnPosition = null) =>
        _snapshots.PrepareForEventEntry(player, zoneHash, returnPosition);

    public OperationResult Apply(Entity player, string kitId) => KitController.ApplyKit(player, kitId);

    public OperationResult Clear(Entity player, string kitId)
    {
        AbilityController.ClearAbilitySlots(player);
        AbilityController.ClearPassiveSpells(player);
        KitController.RemoveEventKit(player, kitId);
        return OperationResult.Ok();
    }

    public OperationResult Restore(Entity player, int zoneHash) =>
        _snapshots.RestoreSnapshot(player, zoneHash) ? OperationResult.Ok() : OperationResult.Fail("No player snapshot exists.");

    public void ClearSnapshot(ulong steamId) => _snapshots.ClearSnapshot(steamId);
}
