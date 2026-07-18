namespace BattleLuck.Services;

/// <summary>Canonical player teleport boundary using the native network-event path in EntityExtensions.</summary>
public sealed class TeleportService
{
    public OperationResult Teleport(Entity player, float3 destination)
    {
        if (!player.Exists() || !player.IsPlayer()) return OperationResult.Fail("Teleport target is not a live player.");
        player.SetPosition(destination);
        return OperationResult.Ok();
    }
}
