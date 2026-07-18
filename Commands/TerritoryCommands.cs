using BattleLuck.Services;

namespace BattleLuck.Commands;

/// <summary>
/// VCF dot commands for castle territory navigation.
/// BattleLuck's primary syntax is the dot prefix (<c>.command</c>), mirroring
/// the standard VCF dot-command ecosystem used here.
/// </summary>
internal static class TerritoryCommands
{
    [Command(
        "tp",
        usage: ".tp <territoryId>  (omit id to list territories)",
        description: "Teleport to a castle territory by id. Use .tp with no id to list all territories.",
        adminOnly: true)]
    public static void TeleportToTerritory(ChatCommandContext ctx, int id = -1)
    {
        if (!VRisingCore.IsReady)
        {
            ctx.Reply("V Rising core is not ready yet.");
            return;
        }

        if (id < 0)
        {
            ListTerritories(ctx);
            return;
        }

        if (!TerritoryLocationService.TryGetTerritory(id, out var info))
        {
            ctx.Reply($"No territory with id {id}. Use .tp to list territories.");
            return;
        }

        var character = ctx.Event.SenderCharacterEntity;
        if (!character.Exists())
        {
            ctx.Reply("Could not resolve your character entity.");
            return;
        }

        character.SetPosition(info.Center);
        ctx.Reply($"Teleported to territory [{id}] (owner: {info.OwnerName}) at ({info.Center.x:F0}, {info.Center.y:F0}, {info.Center.z:F0}).");
    }

    static void ListTerritories(ChatCommandContext ctx)
    {
        var territories = TerritoryLocationService.GetTerritories();
        if (territories.Count == 0)
        {
            ctx.Reply("No castle territories found.");
            return;
        }

        ctx.Reply($"Castle territories ({territories.Count}) — use .tp <id> to teleport:");
        foreach (var t in territories)
        {
            ctx.Reply($"  [{t.Index}] owner={t.OwnerName} center=({t.Center.x:F0}, {t.Center.y:F0}, {t.Center.z:F0})");
        }
    }
}
