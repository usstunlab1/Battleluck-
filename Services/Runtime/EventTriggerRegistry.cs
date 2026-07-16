using System.Reflection;

namespace BattleLuck.Services.Runtime;

public static class EventTriggerRegistry
{
    static readonly Dictionary<string, string> Aliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["mode.start"] = "battleluck.mode.started",
        ["mode.started"] = "battleluck.mode.started",
        ["mode.end"] = "battleluck.mode.ended",
        ["mode.ended"] = "battleluck.mode.ended",
        ["zone.enter"] = "battleluck.zone.enter",
        ["zone.exit"] = "battleluck.zone.exit",
        ["boss.spawned"] = "battleluck.boss.spawned",
        ["boss.death"] = "projectm.player.death",
        ["player.death"] = "projectm.player.death",
        ["player.kill"] = "projectm.kill",
        ["objective.captured"] = "battleluck.objective.captured",
        ["discord.command"] = "battleluck.discord.command",
        ["webhook"] = "battleluck.webhook.action"
    };

    static readonly HashSet<string> Known = new(StringComparer.OrdinalIgnoreCase)
    {
        "battleluck.zone.enter",
        "battleluck.zone.exit",
        "battleluck.mode.started",
        "battleluck.mode.ended",
        "battleluck.round.ended",
        "battleluck.player.scored",
        "battleluck.wave.started",
        "battleluck.wave.cleared",
        "battleluck.wave.final",
        "battleluck.objective.captured",
        "battleluck.zone.shrink",
        "battleluck.reality.changed",
        "battleluck.boss.spawned",
        "battleluck.platform.state.changed",
        "battleluck.crate.collected",
        "battleluck.player.eliminated",
        "battleluck.player.left",
        "battleluck.action.performed",
        "battleluck.elo.update",
        "battleluck.webhook.action",
        "battleluck.discord.command",
        "projectm.player.death",
        "projectm.damage.dealt",
        "projectm.kill",
        "projectm.death.reaction",
        "projectm.vampire.downed",
        "projectm.buff.spawned",
        "projectm.ability.cast.started",
        "projectm.minion.spawned",
        "projectm.buff.applied",
        "projectm.buff.removed",
        "projectm.item.equipped",
        "projectm.item.dropped",
        "projectm.item.moved",
        "projectm.item.picked.up",
        "projectm.item.unequipped",
        "projectm.teleport",
        "projectm.move.towards.position",
        "projectm.player.location.teleport",
        "projectm.player.teleport.command",
        "projectm.unit.spawner.react",
        "projectm.prefab.spawned",
        "projectm.minion.spawn.slot",
        "projectm.character.respawn",
        "projectm.castle.buff",
        "projectm.castle.heart.state",
        "projectm.sequencer",
        "projectm.door.state",
        "projectm.castle.floor.walls"
    };

    public static IReadOnlyCollection<string> All => Known;

    public static string Normalize(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return "";

        var trimmed = name.Trim();
        return Aliases.TryGetValue(trimmed, out var canonical) ? canonical : trimmed;
    }

    public static bool IsKnown(string name) => Known.Contains(Normalize(name));

    public static IReadOnlyCollection<string> ProjectMRouterEventNames()
    {
        return typeof(ProjectMEventRouter)
            .GetEvents(BindingFlags.Instance | BindingFlags.Public)
            .Select(e => e.Name)
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}
