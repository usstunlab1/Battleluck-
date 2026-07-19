using BattleLuck.Services;
using BattleLuck.Services.Runtime;
using BattleLuck.Commands;
using Unity.Entities;
using VampireCommandFramework;

/// <summary>
/// Per-player event rollback commands. These invoke the existing authoritative
/// rollback flow in SessionController, which handles:
/// - Rollback via PlayerStateController
/// - Snapshot deletion on success
/// - Snapshot retention on failure
/// </summary>
public static class PlayerRollbackCommands
{
    public static void RollbackPlayer(ChatCommandContext ctx, string selector)
    {
        if (string.IsNullOrWhiteSpace(selector) || selector.Equals("self", StringComparison.OrdinalIgnoreCase))
            selector = ctx.GetSenderCharacterEntity().GetSteamId().ToString();

        if (!TryResolveOnline(selector, out var player))
        {
            ctx.Reply($"❌ Player '{selector}' must be online for rollback.");
            return;
        }

        var steamId = player.GetSteamId();
        var result = BattleLuckPlugin.Session?.RollbackPlayer(steamId, player)
            ?? BattleLuckPlugin.PlayerLoadouts?.Restore(player, 0) == true
                ? OperationResult.Ok()
                : OperationResult.Fail("Session controller is not initialized and no loadout service available.");

        if (!result.Success)
        {
            ctx.Reply($"❌ Player rollback failed: {result.Error}");
            return;
        }

        ctx.Reply($"✅ Rolled back {player.GetPlayerName()} and cleared the player snapshot.");
    }

    public static void RollbackAllEventPlayers(ChatCommandContext ctx, bool confirmed)
    {
        if (!confirmed)
        {
            ctx.Reply("⚠️ This restores every online player with an active event session. Repeat `.ai rollback server players confirm` to proceed.");
            return;
        }

        var session = BattleLuckPlugin.Session;
        if (session == null)
        {
            ctx.Reply("❌ Session controller is not initialized.");
            return;
        }

        var online = VRisingCore.GetOnlinePlayers()
            .Where(player => player.Exists() && player.IsPlayer())
            .ToList();

        var restored = 0;
        var failed = 0;

        foreach (var player in online)
        {
            var steamId = player.GetSteamId();
            var result = session.RollbackPlayer(steamId, player);
            if (result.Success)
                restored++;
            else
                failed++;
        }

        ctx.Reply($"🛡️ Server player-state rollback: restored={restored}, failed={failed}.");
    }

    static bool TryResolveOnline(string selector, out Entity player)
    {
        player = Entity.Null;
        var online = VRisingCore.GetOnlinePlayers()
            .Where(candidate => candidate.Exists() && candidate.IsPlayer())
            .ToList();

        if (ulong.TryParse(selector, out var steamId))
        {
            player = online.FirstOrDefault(candidate => candidate.GetSteamId() == steamId);
            return player.Exists();
        }

        player = online.FirstOrDefault(candidate =>
            candidate.GetPlayerName().Equals(selector, StringComparison.OrdinalIgnoreCase) ||
            candidate.GetPlayerName().Contains(selector, StringComparison.OrdinalIgnoreCase));
        return player.Exists();
    }
}