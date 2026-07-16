using System.Globalization;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;
using Unity.Mathematics;
using BattleLuck.Models;
using BattleLuck.Commands;
using VampireCommandFramework;

public static class NpcCommands
{
    const string SessionDevKey = "_dev_";

    static readonly FieldInfo[] NetworkIdNumericFields = typeof(NetworkId)
        .GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
        .Where(field =>
            field.FieldType == typeof(byte) || field.FieldType == typeof(sbyte) ||
            field.FieldType == typeof(short) || field.FieldType == typeof(ushort) ||
            field.FieldType == typeof(int) || field.FieldType == typeof(uint) ||
            field.FieldType == typeof(long) || field.FieldType == typeof(ulong))
        .ToArray();

    [Command("npc.near", description: "List nearby NPCs with control ids. Usage: .npc.near [radius] [limit]", adminOnly: true)]
    public static void Near(ChatCommandContext ctx, float radius = 25f, int limit = 10)
    {
        var sender = ctx.GetSenderCharacterEntity();
        if (!sender.Exists())
        {
            ctx.Reply("Sender character is not available.");
            return;
        }

        radius = System.Math.Clamp(radius, 1f, 250f);
        limit = System.Math.Clamp(limit, 1, 30);
        PrefabHelper.ScanLivePrefabs();

        var rows = FindNearbyNpcEntities(sender.GetPosition(), radius, limit);
        if (rows.Count == 0)
        {
            ctx.Reply($"No NPCs found within {radius:F0}m.");
            return;
        }

        ctx.Reply($"NPCs within {radius:F0}m ({rows.Count} shown):");
        foreach (var row in rows)
            ctx.Reply("  " + FormatNpcLine(row.Entity, row.Distance));
    }

    [Command("npc.spawn", description: "Spawn controlled NPCs. Usage: .npc.spawn <prefabName> [count] [radius] [npcIdPrefix] [homeRadius]", adminOnly: true)]
    public static void Spawn(
        ChatCommandContext ctx,
        string prefabName,
        int count = 1,
        float radius = 3f,
        string npcIdPrefix = "",
        float homeRadius = 35f)
    {
        var service = BattleLuckPlugin.NpcService;
        if (service == null)
        {
            ctx.Reply("NPC control service is not initialized.");
            return;
        }

        var sender = ctx.GetSenderCharacterEntity();
        if (!sender.Exists())
        {
            ctx.Reply("Sender character is not available.");
            return;
        }

        if (KindredSpawnSafety.IsSpawnBanned(prefabName, out var bannedReason))
        {
            ctx.Reply($"Prefab '{prefabName}' is blocked by spawn safety: {bannedReason}");
            return;
        }

        PrefabHelper.ScanLivePrefabs();
        var prefab = PrefabHelper.GetLivePrefabGuid(prefabName) ?? PrefabHelper.GetPrefabGuidDeep(prefabName);
        if (!prefab.HasValue)
        {
            ctx.Reply($"Prefab '{prefabName}' not found. Use .scanprefabs {prefabName}");
            return;
        }

        count = System.Math.Clamp(count, 1, 50);
        radius = System.Math.Clamp(radius, 0f, 60f);
        homeRadius = System.Math.Clamp(homeRadius, 1f, 250f);

        var center = sender.GetPosition();
        var sessionId = ResolveSessionId(ctx);
        var spawner = new SpawnController();

        for (var i = 0; i < count; i++)
        {
            var spawnPos = center + CircleOffset(i, count, radius);
            var id = string.IsNullOrWhiteSpace(npcIdPrefix)
                ? null
                : count == 1 ? npcIdPrefix : $"{npcIdPrefix}_{i + 1}";

            spawner.SpawnNPC(prefab.Value, spawnPos, entity =>
            {
                var result = service.RegisterNpc(sessionId, id, prefabName, prefab.Value, entity, spawnPos, homeRadius);
                if (!result.Success)
                    BattleLuckPlugin.LogWarning($"[NpcCommands] Register spawned NPC failed: {result.Error}");
            });
        }

        ctx.Reply($"NPC spawn requested: prefab={prefabName} count={count} radius={radius:F0} session={sessionId}.");
    }

    [Command("npc.follow", description: "Make an NPC follow a target. Usage: .npc.follow <npcId|near|near:radius|pos:x,y,z:radius|entityId> [target=self] [followRange] [leashRange]", adminOnly: true)]
    public static void Follow(ChatCommandContext ctx, string selector = "near", string targetSelector = "self", float followRange = 6f, float leashRange = 80f)
    {
        if (!TryResolveControlledNpc(ctx, selector, out var entry, out var error))
        {
            ctx.Reply(error);
            return;
        }

        if (!TryResolveTarget(ctx, targetSelector, out var target, out error))
        {
            ctx.Reply(error);
            return;
        }

        var result = BattleLuckPlugin.NpcService!.Follow(entry.NpcId, target, followRange, leashRange);
        ctx.Reply(result.Success
            ? $"NPC follow: {entry.NpcId} -> {DescribeTarget(targetSelector, target)} range={followRange:F1}/{leashRange:F1}"
            : result.UserMessage);
    }

    [Command("npc.aggro", description: "Make an NPC pressure/chase a target. Usage: .npc.aggro <npcId|near|near:radius|pos:x,y,z:radius|entityId> [target=self] [range] [leashRange]", adminOnly: true)]
    public static void Aggro(ChatCommandContext ctx, string selector = "near", string targetSelector = "self", float range = 3f, float leashRange = 80f)
    {
        if (!TryResolveControlledNpc(ctx, selector, out var entry, out var error))
        {
            ctx.Reply(error);
            return;
        }

        if (!TryResolveTarget(ctx, targetSelector, out var target, out error))
        {
            ctx.Reply(error);
            return;
        }

        var result = BattleLuckPlugin.NpcService!.Aggro(entry.NpcId, target, range, leashRange);
        ctx.Reply(result.Success
            ? $"NPC aggro: {entry.NpcId} -> {DescribeTarget(targetSelector, target)} range={range:F1}/{leashRange:F1}"
            : result.UserMessage);
    }

    [Command("npc.goto", description: "Move an NPC to your position or x y z. Usage: .npc.goto <npcId|near|near:radius|pos:x,y,z:radius|entityId> [x] [y] [z]", adminOnly: true)]
    public static void GoTo(ChatCommandContext ctx, string selector = "near", string x = "", string y = "", string z = "")
    {
        if (!TryResolveControlledNpc(ctx, selector, out var entry, out var error))
        {
            ctx.Reply(error);
            return;
        }

        if (!TryResolvePositionArgs(ctx, x, y, z, out var target, out error))
        {
            ctx.Reply(error);
            return;
        }

        var result = BattleLuckPlugin.NpcService!.GoTo(entry.NpcId, target);
        ctx.Reply(result.Success
            ? $"NPC goto: {entry.NpcId} -> ({target.x:F1},{target.y:F1},{target.z:F1})"
            : result.UserMessage);
    }

    [Command("npc.goto.pos", description: "Move an NPC to your cursor/mouse world position. Usage: .npc.goto.pos <npcId|near|near:radius|pos:x,y,z:radius|entityId>", adminOnly: true)]
    public static void GoToCursor(ChatCommandContext ctx, string selector = "near")
    {
        if (!TryResolveControlledNpc(ctx, selector, out var entry, out var error))
        {
            ctx.Reply(error);
            return;
        }

        if (!TryGetCommandWorldPosition(ctx, out var target, out error))
        {
            ctx.Reply(error);
            return;
        }

        var result = BattleLuckPlugin.NpcService!.GoTo(entry.NpcId, target);
        ctx.Reply(result.Success
            ? $"NPC goto cursor: {entry.NpcId} -> ({target.x:F1},{target.y:F1},{target.z:F1})"
            : result.UserMessage);
    }

    [Command("npc.hold", description: "Make an NPC hold its current spot. Usage: .npc.hold <npcId|near|near:radius|pos:x,y,z:radius|entityId> [holdRadius]", adminOnly: true)]
    public static void Hold(ChatCommandContext ctx, string selector = "near", float radius = 8f)
    {
        if (!TryResolveControlledNpc(ctx, selector, out var entry, out var error))
        {
            ctx.Reply(error);
            return;
        }

        var result = BattleLuckPlugin.NpcService!.Hold(entry.NpcId, radius);
        ctx.Reply(result.Success ? $"NPC hold: {entry.NpcId} radius={radius:F1}" : result.UserMessage);
    }

    [Command("npc.stay", description: "Alias for npc.hold. Usage: .npc.stay <npcId|near|near:radius|pos:x,y,z:radius|entityId> [holdRadius]", adminOnly: true)]
    public static void Stay(ChatCommandContext ctx, string selector = "near", float radius = 8f)
        => Hold(ctx, selector, radius);

    [Command("npc.release", description: "Release an NPC back to native AI. Usage: .npc.release <npcId|near|near:radius|pos:x,y,z:radius|entityId>", adminOnly: true)]
    public static void Release(ChatCommandContext ctx, string selector = "near")
    {
        if (!TryResolveControlledNpc(ctx, selector, out var entry, out var error))
        {
            ctx.Reply(error);
            return;
        }

        var result = BattleLuckPlugin.NpcService!.Release(entry.NpcId);
        ctx.Reply(result.Success ? $"NPC released: {entry.NpcId}" : result.UserMessage);
    }

    [Command("npc.despawn", description: "Despawn a tracked/selected NPC. Usage: .npc.despawn <npcId|near|near:radius|pos:x,y,z:radius|entityId|all>", adminOnly: true)]
    public static void Despawn(ChatCommandContext ctx, string selector = "near")
    {
        var service = BattleLuckPlugin.NpcService;
        if (service == null)
        {
            ctx.Reply("NPC control service is not initialized.");
            return;
        }

        if (selector.Equals("all", StringComparison.OrdinalIgnoreCase))
        {
            var count = service.DespawnSession(ResolveSessionId(ctx));
            ctx.Reply($"Despawned {count} tracked NPC(s) for this session.");
            return;
        }

        if (!TryResolveControlledNpc(ctx, selector, out var entry, out var error))
        {
            ctx.Reply(error);
            return;
        }

        var result = service.Despawn(entry.NpcId);
        ctx.Reply(result.Success ? $"NPC despawned: {entry.NpcId}" : result.UserMessage);
    }

    [Command("npc.team", description: "Set controlled NPC team id. Usage: .npc.team <npcId|near|near:radius|pos:x,y,z:radius|entityId> <teamId>", adminOnly: true)]
    public static void Team(ChatCommandContext ctx, string selector, int teamId)
    {
        if (!TryResolveControlledNpc(ctx, selector, out var entry, out var error))
        {
            ctx.Reply(error);
            return;
        }

        var result = BattleLuckPlugin.NpcService!.SetTeam(entry.NpcId, teamId);
        ctx.Reply(result.Success ? $"NPC team: {entry.NpcId} -> {teamId}" : result.UserMessage);
    }

    [Command("npc.faction", description: "Set controlled NPC faction prefab. Usage: .npc.faction <npcId|near|near:radius|pos:x,y,z:radius|entityId> <factionPrefabNameOrGuid>", adminOnly: true)]
    public static void Faction(ChatCommandContext ctx, string selector, string factionPrefab)
    {
        if (!TryResolveControlledNpc(ctx, selector, out var entry, out var error))
        {
            ctx.Reply(error);
            return;
        }

        var faction = ResolvePrefabOrHash(factionPrefab);
        if (faction == PrefabGUID.Empty)
        {
            ctx.Reply($"Faction prefab '{factionPrefab}' was not found.");
            return;
        }

        var result = BattleLuckPlugin.NpcService!.SetFaction(entry.NpcId, faction);
        ctx.Reply(result.Success ? $"NPC faction: {entry.NpcId} -> {faction.GuidHash}" : result.UserMessage);
    }

    [Command("npc.speed", description: "Set controlled NPC movement speed. Usage: .npc.speed <npcId|near|near:radius|pos:x,y,z:radius|entityId> <speed>", adminOnly: true)]
    public static void Speed(ChatCommandContext ctx, string selector, float speed)
    {
        if (!TryResolveControlledNpc(ctx, selector, out var entry, out var error))
        {
            ctx.Reply(error);
            return;
        }

        var result = BattleLuckPlugin.NpcService!.SetSpeed(entry.NpcId, speed);
        ctx.Reply(result.Success ? $"NPC speed: {entry.NpcId} -> {speed:F1}" : result.UserMessage);
    }

    [Command("npc.rename", description: "Rename a tracked NPC label. Usage: .npc.rename <npcId|near|near:radius|pos:x,y,z:radius|entityId> <newName...>", adminOnly: true)]
    public static void RenameNpc(ChatCommandContext ctx, string selector, string name, string a2 = "", string a3 = "", string a4 = "", string a5 = "", string a6 = "", string a7 = "", string a8 = "")
    {
        if (!TryResolveControlledNpc(ctx, selector, out var entry, out var error))
        {
            ctx.Reply(error);
            return;
        }

        var newName = JoinWords(name, a2, a3, a4, a5, a6, a7, a8);
        var result = BattleLuckPlugin.NpcService!.Rename(entry.NpcId, newName);
        ctx.Reply(result.Success ? $"NPC renamed: {entry.NpcId} -> {newName}" : result.UserMessage);
    }

    [Command("npc.buffs", description: "List buffs on a nearby/selected NPC. Usage: .npc.buffs [npcId|near|near:radius|pos:x,y,z:radius|entityId] [limit]", adminOnly: true)]
    public static void Buffs(ChatCommandContext ctx, string selector = "near", int limit = 12)
    {
        if (!TryResolveNpcEntity(ctx, selector, out var npc, out var error))
        {
            ctx.Reply(error);
            return;
        }

        var buffs = GetEntityBuffs(npc);
        if (buffs.Count == 0)
        {
            ctx.Reply($"NPC {npc.Index}:{npc.Version} has no visible BuffBuffer entries.");
            return;
        }

        limit = System.Math.Clamp(limit, 1, 40);
        ctx.Reply($"NPC buffs {npc.Index}:{npc.Version} total={buffs.Count}:");
        foreach (var buff in buffs.Take(limit))
            ctx.Reply($"  [{buff.Index}] {buff.Name} guid={buff.Prefab.GuidHash} dur={buff.Duration:F1}s abilities={buff.Abilities.Count}");
    }

    [Command("list.buffs", description: "Alias for npc.buffs. Usage: .list.buffs [npcId|near|near:radius|pos:x,y,z:radius|entityId] [limit]", adminOnly: true)]
    public static void ListBuffs(ChatCommandContext ctx, string selector = "near", int limit = 12)
        => Buffs(ctx, selector, limit);

    [Command("npc.copybuff", description: "Copy a buff from an NPC to yourself. Usage: .npc.copybuff [npcId|near|near:radius|pos:x,y,z:radius|entityId] [random|index|name] [duration=-1]", adminOnly: true)]
    public static void CopyBuff(ChatCommandContext ctx, string selector = "near", string pick = "random", float duration = -1f)
    {
        if (!TryResolveNpcEntity(ctx, selector, out var npc, out var error))
        {
            ctx.Reply(error);
            return;
        }

        var buffs = GetEntityBuffs(npc)
            .Where(b => b.Prefab != PrefabGUID.Empty)
            .ToList();
        if (buffs.Count == 0)
        {
            ctx.Reply("No copyable buffs found on that NPC.");
            return;
        }

        var selected = PickBuff(buffs, pick);
        if (selected == null)
        {
            ctx.Reply($"Buff '{pick}' not found. Use .npc.buffs {selector}");
            return;
        }

        var player = ctx.GetSenderCharacterEntity();
        player.BuffEntity(selected.Prefab, out _, duration);
        ctx.Reply($"Copied buff to you: {selected.Name} ({selected.Prefab.GuidHash}) duration={duration:F1}");
    }

    [Command("npc.copyability", description: "Copy one/all ability replacements from NPC buffs to yourself. Usage: .npc.copyability [npcId|near|near:radius|pos:x,y,z:radius|entityId] [random|all|slot]", adminOnly: true)]
    public static void CopyAbility(ChatCommandContext ctx, string selector = "near", string pick = "random")
    {
        if (!TryResolveNpcEntity(ctx, selector, out var npc, out var error))
        {
            ctx.Reply(error);
            return;
        }

        var abilities = GetEntityBuffs(npc).SelectMany(b => b.Abilities).ToList();
        if (abilities.Count == 0)
        {
            ctx.Reply("No ReplaceAbilityOnSlotBuff entries found on that NPC. Use .npc.components to inspect native ability components.");
            return;
        }

        var player = ctx.GetSenderCharacterEntity();
        var selected = SelectAbilities(abilities, pick);
        foreach (var ability in selected)
            AbilityController.SetSpellOnSlot(player, ability.Slot, ability.Ability);

        ctx.Reply($"Copied {selected.Count} NPC ability replacement(s) to you.");
    }

    [Command("npc.components", description: "List components on an NPC. Usage: .npc.components [npcId|near|near:radius|pos:x,y,z:radius|entityId] [filter] [limit]", adminOnly: true)]
    public static void Components(ChatCommandContext ctx, string selector = "near", string filter = "", int limit = 80)
    {
        if (!TryResolveNpcEntity(ctx, selector, out var npc, out var error))
        {
            ctx.Reply(error);
            return;
        }

        var names = GetComponentNames(npc);
        if (!string.IsNullOrWhiteSpace(filter))
            names = names.Where(n => n.Contains(filter, StringComparison.OrdinalIgnoreCase)).ToList();

        limit = System.Math.Clamp(limit, 1, 160);
        ctx.Reply($"NPC components {npc.Index}:{npc.Version} count={names.Count}:");
        foreach (var chunk in ChunkText(string.Join(", ", names.Take(limit)), 420).Take(4))
            ctx.Reply(chunk);
    }

    [Command("swapcharacter", description: "Rename an online player or tracked NPC label. Usage: .swapcharacter <fromName|steamId|npcId|near> <toName...>", adminOnly: true)]
    public static void SwapCharacter(ChatCommandContext ctx, string from, string to, string a2 = "", string a3 = "", string a4 = "", string a5 = "", string a6 = "", string a7 = "", string a8 = "")
    {
        var newName = JoinWords(to, a2, a3, a4, a5, a6, a7, a8);
        if (TryRenameOnlinePlayer(from, newName, out var message))
        {
            ctx.Reply(message);
            return;
        }

        if (TryResolveControlledNpc(ctx, from, out var entry, out var error))
        {
            var result = BattleLuckPlugin.NpcService!.Rename(entry.NpcId, newName);
            ctx.Reply(result.Success ? $"NPC label renamed: {entry.NpcId} -> {newName}" : result.UserMessage);
            return;
        }

        ctx.Reply($"No online player or tracked NPC matched '{from}'. NPC lookup: {error}");
    }

    [Command("npc.status", description: "Show tracked NPC control status. Usage: .npc.status [radius]", adminOnly: true)]
    public static void Status(ChatCommandContext ctx, float radius = 40f)
    {
        var service = BattleLuckPlugin.NpcService;
        if (service == null)
        {
            ctx.Reply("NPC control service is not initialized.");
            return;
        }

        var sessionId = ResolveSessionId(ctx);
        var entries = service.List(sessionId);
        ctx.Reply($"NPC control: tracked={entries.Count} session={sessionId}");
        foreach (var entry in entries.Take(10))
        {
            var pos = entry.IsAlive ? entry.Entity.GetPosition() : float3.zero;
            var dist = entry.IsAlive ? math.distance(pos, entry.HomePosition) : -1f;
            ctx.Reply($"  {entry.NpcId}: name={entry.DisplayName} {entry.Mode} alive={entry.IsAlive} prefab={entry.PrefabName} entity={entry.Entity.Index}:{entry.Entity.Version} homeDist={dist:F1}/{entry.HomeRadius:F1} speed={entry.MoveSpeed:F1}");
        }

        if (entries.Count > 10)
            ctx.Reply($"  ... {entries.Count - 10} more tracked NPCs.");

        var sender = ctx.GetSenderCharacterEntity();
        if (sender.Exists())
        {
            radius = System.Math.Clamp(radius, 1f, 250f);
            var nearby = FindNearbyNpcEntities(sender.GetPosition(), radius, 1).FirstOrDefault();
            if (nearby.Entity.Exists())
                ctx.Reply($"Nearest live NPC: {FormatNpcLine(nearby.Entity, nearby.Distance)}");
        }
    }

    static bool TryResolveControlledNpc(ChatCommandContext ctx, string selector, out ControlledNpcEntry entry, out string error)
    {
        entry = null!;
        error = "";

        var service = BattleLuckPlugin.NpcService;
        if (service == null)
        {
            error = "NPC control service is not initialized.";
            return false;
        }

        var sessionId = ResolveSessionId(ctx);
        if (selector.Equals("last", StringComparison.OrdinalIgnoreCase) ||
            selector.Equals("self", StringComparison.OrdinalIgnoreCase))
        {
            var latest = service.GetLatest(sessionId);
            if (latest != null)
            {
                entry = latest;
                return true;
            }
        }

        if (service.TryGet(selector, out entry))
            return true;

        if (TryResolveNpcEntity(ctx, selector, out var entity, out error))
        {
            if (service.TryGetByEntity(entity, out entry))
                return true;

            var prefab = entity.GetPrefabGuid();
            var name = PrefabHelper.GetLivePrefabName(prefab) ?? PrefabHelper.GetName(prefab) ?? prefab.GuidHash.ToString();
            var result = service.RegisterNpc(sessionId, null, name, prefab, entity, entity.GetPosition());
            if (!result.Success || result.Value == null)
            {
                error = result.UserMessage;
                return false;
            }

            entry = result.Value;
            return true;
        }

        if (string.IsNullOrWhiteSpace(error))
            error = $"NPC selector '{selector}' was not found. Use .npc.near to list entity ids.";
        return false;
    }

    static bool TryResolveNpcEntity(ChatCommandContext ctx, string selector, out Entity entity, out string error)
    {
        entity = Entity.Null;
        error = "";

        var sender = ctx.GetSenderCharacterEntity();
        if (!sender.Exists())
        {
            error = "Sender character is not available.";
            return false;
        }

        if (TryParseNpcAreaSelector(selector, sender.GetPosition(), out var center, out var radius))
        {
            var row = FindNearbyNpcEntities(center, radius, 1).FirstOrDefault();
            if (row.Entity.Exists())
            {
                entity = row.Entity;
                return true;
            }
            error = $"No NPC found within {radius:F0}m of ({center.x:F1},{center.y:F1},{center.z:F1}).";
            return false;
        }

        if (TryResolveEntityId(selector, out entity) && IsNpcLike(entity))
            return true;

        if (ulong.TryParse(selector, NumberStyles.Integer, CultureInfo.InvariantCulture, out var netId) &&
            TryResolveEntityByNetworkNumeric(netId, out entity) &&
            IsNpcLike(entity))
            return true;

        error = $"NPC selector '{selector}' was not found.";
        return false;
    }

    static bool TryParseNpcAreaSelector(string selector, float3 fallbackCenter, out float3 center, out float radius)
    {
        center = fallbackCenter;
        radius = 35f;

        if (selector.Equals("near", StringComparison.OrdinalIgnoreCase))
            return true;

        if (selector.StartsWith("near:", StringComparison.OrdinalIgnoreCase))
        {
            if (float.TryParse(selector[5..], NumberStyles.Float, CultureInfo.InvariantCulture, out var parsedRadius))
                radius = System.Math.Clamp(parsedRadius, 1f, 250f);
            return true;
        }

        if (selector.StartsWith("pos:", StringComparison.OrdinalIgnoreCase))
        {
            var payload = selector[4..];
            var parts = payload.Split(':', 2, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0 || !TryParseFloat3(parts[0], out center))
                return false;

            if (parts.Length > 1 && float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var parsedRadius))
                radius = System.Math.Clamp(parsedRadius, 1f, 250f);
            return true;
        }

        return false;
    }

    static bool TryResolveTarget(ChatCommandContext ctx, string selector, out Entity target, out string error)
    {
        target = Entity.Null;
        error = "";

        if (selector.Equals("self", StringComparison.OrdinalIgnoreCase))
        {
            target = ctx.GetSenderCharacterEntity();
            return target.Exists();
        }

        if (ulong.TryParse(selector, NumberStyles.Integer, CultureInfo.InvariantCulture, out var steamId))
        {
            foreach (var player in VRisingCore.GetOnlinePlayers())
            {
                if (player.Exists() && player.IsPlayer() && player.GetSteamId() == steamId)
                {
                    target = player;
                    return true;
                }
            }
        }

        if (BattleLuckPlugin.NpcService?.TryGet(selector, out var npc) == true && npc.IsAlive)
        {
            target = npc.Entity;
            return true;
        }

        if (TryResolveEntityId(selector, out target) && target.Exists())
            return true;

        if (ulong.TryParse(selector, NumberStyles.Integer, CultureInfo.InvariantCulture, out var netId) &&
            TryResolveEntityByNetworkNumeric(netId, out target) &&
            target.Exists())
            return true;

        error = $"Target '{selector}' was not found. Use self, SteamID, npcId, netId, or entity index:version.";
        return false;
    }

    static bool TryResolvePositionArgs(ChatCommandContext ctx, string x, string y, string z, out float3 position, out string error)
    {
        error = "";
        var sender = ctx.GetSenderCharacterEntity();
        position = sender.Exists() ? sender.GetPosition() : float3.zero;

        if (string.IsNullOrWhiteSpace(x))
            return true;

        if (TryParseFloat3(x, out position))
            return true;

        if (float.TryParse(x, NumberStyles.Float, CultureInfo.InvariantCulture, out var px) &&
            float.TryParse(y, NumberStyles.Float, CultureInfo.InvariantCulture, out var py) &&
            float.TryParse(z, NumberStyles.Float, CultureInfo.InvariantCulture, out var pz))
        {
            position = new float3(px, py, pz);
            return true;
        }

        error = "Invalid position. Use .npc.goto <npc> or .npc.goto <npc> x y z.";
        return false;
    }

    static List<(Entity Entity, float Distance)> FindNearbyNpcEntities(float3 center, float radius, int limit)
    {
        var em = VRisingCore.EntityManager;
        var query = em.CreateEntityQuery(new EntityQueryDesc
        {
            All = new[] { ComponentType.ReadOnly<Translation>(), ComponentType.ReadOnly<PrefabGUID>() },
            Any = new[] { ComponentType.ReadOnly<UnitLevel>(), ComponentType.ReadOnly<UnitStats>() },
            None = new[] { ComponentType.ReadOnly<PlayerCharacter>() }
        });

        var entities = query.ToEntityArray(Allocator.Temp);
        var rows = new List<(Entity Entity, float Distance)>();
        try
        {
            foreach (var entity in entities)
            {
                if (!IsNpcLike(entity))
                    continue;

                var distance = math.distance(center, entity.GetPosition());
                if (distance <= radius)
                    rows.Add((entity, distance));
            }
        }
        finally
        {
            if (entities.IsCreated)
                entities.Dispose();
            query.Dispose();
        }

        return rows.OrderBy(r => r.Distance).Take(limit).ToList();
    }

    static bool IsNpcLike(Entity entity)
        => entity.Exists()
           && !entity.Has<PlayerCharacter>()
           && entity.Has<Translation>()
           && entity.Has<PrefabGUID>()
           && (entity.Has<UnitLevel>() || entity.Has<UnitStats>() || entity.Has<Aggroable>());

    static string FormatNpcLine(Entity entity, float distance)
    {
        var prefab = entity.GetPrefabGuid();
        var name = PrefabHelper.GetLivePrefabName(prefab) ?? PrefabHelper.GetName(prefab) ?? prefab.GuidHash.ToString();
        var level = entity.GetUnitLevel();
        var team = entity.Has<Team>() ? entity.Read<Team>().Value.ToString(CultureInfo.InvariantCulture) : "-";
        var controlled = BattleLuckPlugin.NpcService?.TryGetByEntity(entity, out var entry) == true
            ? $"{entry.NpcId}/{entry.Mode}"
            : "no";
        return $"{entity.Index}:{entity.Version} {name} guid={prefab.GuidHash} lvl={level} team={team} dist={distance:F1} controlled={controlled} net={NetworkSummary(entity)}";
    }

    static string DescribeTarget(string selector, Entity target)
    {
        if (target.Has<PlayerCharacter>())
            return $"{target.GetPlayerName()} ({target.GetSteamId()})";
        return $"{selector} entity={target.Index}:{target.Version}";
    }

    static float3 CircleOffset(int index, int count, float radius)
    {
        if (count <= 1 || radius <= 0.01f)
            return float3.zero;
        var angle = 2f * math.PI * index / count;
        return new float3(math.cos(angle) * radius, 0f, math.sin(angle) * radius);
    }

    sealed record NpcBuffInfo(int Index, PrefabGUID Prefab, string Name, float Duration, Entity BuffEntity, List<NpcAbilityInfo> Abilities);
    sealed record NpcAbilityInfo(int Slot, PrefabGUID Ability);

    static List<NpcBuffInfo> GetEntityBuffs(Entity entity)
    {
        var result = new List<NpcBuffInfo>();
        if (!entity.Exists())
            return result;

        var em = VRisingCore.EntityManager;
        if (!em.HasBuffer<BuffBuffer>(entity))
            return result;

        var buffs = em.GetBuffer<BuffBuffer>(entity);
        for (var i = 0; i < buffs.Length; i++)
        {
            var entry = buffs[i];
            var prefab = entry.PrefabGuid;
            var buffEntity = entry.Entity;
            var duration = 0f;
            var abilities = new List<NpcAbilityInfo>();

            if (buffEntity.Exists())
            {
                if (buffEntity.TryGetComponent(out LifeTime lifeTime))
                    duration = lifeTime.Duration;

                if (em.HasBuffer<ReplaceAbilityOnSlotBuff>(buffEntity))
                {
                    var replace = em.GetBuffer<ReplaceAbilityOnSlotBuff>(buffEntity);
                    for (var r = 0; r < replace.Length; r++)
                        abilities.Add(new NpcAbilityInfo(replace[r].Slot, replace[r].NewGroupId));
                }
            }

            var name = PrefabHelper.GetLivePrefabName(prefab) ?? PrefabHelper.GetName(prefab) ?? prefab.GuidHash.ToString(CultureInfo.InvariantCulture);
            result.Add(new NpcBuffInfo(i, prefab, name, duration, buffEntity, abilities));
        }

        return result;
    }

    static NpcBuffInfo? PickBuff(List<NpcBuffInfo> buffs, string pick)
    {
        if (buffs.Count == 0)
            return null;
        if (string.IsNullOrWhiteSpace(pick) || pick.Equals("random", StringComparison.OrdinalIgnoreCase))
            return buffs[System.Random.Shared.Next(buffs.Count)];
        if (int.TryParse(pick, NumberStyles.Integer, CultureInfo.InvariantCulture, out var index))
            return buffs.FirstOrDefault(b => b.Index == index);
        return buffs.FirstOrDefault(b =>
            b.Name.Contains(pick, StringComparison.OrdinalIgnoreCase) ||
            b.Prefab.GuidHash.ToString(CultureInfo.InvariantCulture).Equals(pick, StringComparison.OrdinalIgnoreCase));
    }

    static List<NpcAbilityInfo> SelectAbilities(List<NpcAbilityInfo> abilities, string pick)
    {
        if (abilities.Count == 0)
            return new List<NpcAbilityInfo>();
        if (pick.Equals("all", StringComparison.OrdinalIgnoreCase))
            return abilities;
        if (int.TryParse(pick, NumberStyles.Integer, CultureInfo.InvariantCulture, out var slot))
            return abilities.Where(a => a.Slot == slot).Take(1).ToList();
        return new List<NpcAbilityInfo> { abilities[System.Random.Shared.Next(abilities.Count)] };
    }

    static List<string> GetComponentNames(Entity entity)
    {
        var names = new List<string>();
        if (!entity.Exists())
            return names;

        var types = VRisingCore.EntityManager.GetComponentTypes(entity, Allocator.Temp);
        try
        {
            for (var i = 0; i < types.Length; i++)
                names.Add(types[i].ToString());
        }
        finally
        {
            if (types.IsCreated)
                types.Dispose();
        }

        return names.OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();
    }

    static bool TryGetCommandWorldPosition(ChatCommandContext ctx, out float3 position, out string error)
    {
        position = float3.zero;
        error = "";

        if (ctx.GetSenderCharacterEntity().Exists() && ctx.GetSenderCharacterEntity().Has<Translation>())
        {
            position = ctx.GetSenderCharacterEntity().Read<Translation>().Value;
            return true;
        }

        error = "Could not resolve world position from sender entity. Use .npc.goto <npc> x y z instead.";
        return false;
    }

    static bool TryExtractFloat3(object? value, out float3 position)
    {
        position = float3.zero;
        if (value == null)
            return false;
        if (value is float3 f3)
        {
            position = f3;
            return true;
        }
        if (value is Vector3 v3)
        {
            position = new float3(v3.x, v3.y, v3.z);
            return true;
        }

        var type = value.GetType();
        if (TryReadFloatMember(value, type, "x", out var x) &&
            TryReadFloatMember(value, type, "y", out var y) &&
            TryReadFloatMember(value, type, "z", out var z))
        {
            position = new float3(x, y, z);
            return true;
        }
        if (TryReadFloatMember(value, type, "X", out x) &&
            TryReadFloatMember(value, type, "Y", out y) &&
            TryReadFloatMember(value, type, "Z", out z))
        {
            position = new float3(x, y, z);
            return true;
        }

        return false;
    }

    static bool TryReadFloatMember(object value, Type type, string name, out float result)
    {
        result = 0f;
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.IgnoreCase;
        try
        {
            var field = type.GetField(name, flags);
            if (field?.GetValue(value) is { } fv && TryConvertFloat(fv, out result))
                return true;
            var property = type.GetProperty(name, flags);
            if (property?.GetIndexParameters().Length == 0 && property.GetValue(value) is { } pv && TryConvertFloat(pv, out result))
                return true;
        }
        catch { }
        return false;
    }

    static bool TryConvertFloat(object value, out float result)
    {
        switch (value)
        {
            case float f:
                result = f;
                return true;
            case double d:
                result = (float)d;
                return true;
            case int i:
                result = i;
                return true;
            default:
                return float.TryParse(value.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out result);
        }
    }

    static bool TryRenameOnlinePlayer(string selector, string newName, out string message)
    {
        message = "";
        var player = FindOnlinePlayer(selector);
        if (!player.Exists())
            return false;

        var userEntity = player.GetUserEntity();
        if (!userEntity.Exists() || !userEntity.Has<User>())
        {
            message = "Player user entity was not found.";
            return true;
        }

        var oldName = player.GetPlayerName();
        userEntity.With((ref User user) => user.CharacterName = ToFixed64(newName));
        message = $"Player renamed: {oldName} -> {newName}";
        return true;
    }

    static Entity FindOnlinePlayer(string selector)
    {
        foreach (var player in VRisingCore.GetOnlinePlayers())
        {
            if (!player.Exists() || !player.IsPlayer())
                continue;
            if (ulong.TryParse(selector, NumberStyles.Integer, CultureInfo.InvariantCulture, out var steamId) && player.GetSteamId() == steamId)
                return player;
            var name = player.GetPlayerName();
            if (name.Equals(selector, StringComparison.OrdinalIgnoreCase) ||
                name.Contains(selector, StringComparison.OrdinalIgnoreCase))
                return player;
        }
        return Entity.Null;
    }

    static FixedString64Bytes ToFixed64(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return default;
        value = value.Trim();
        return value.Length <= 61 ? new FixedString64Bytes(value) : new FixedString64Bytes(value[..61]);
    }

    static string JoinWords(params string[] words)
        => string.Join(" ", words.Where(w => !string.IsNullOrWhiteSpace(w))).Trim();

    static IEnumerable<string> ChunkText(string value, int max)
    {
        if (string.IsNullOrWhiteSpace(value))
            yield break;
        while (value.Length > max)
        {
            var split = value.LastIndexOf(' ', Math.Min(max, value.Length - 1));
            if (split < max / 2)
                split = max;
            yield return value[..split].Trim();
            value = value[split..].Trim();
        }
        if (value.Length > 0)
            yield return value;
    }

    static string ResolveSessionId(ChatCommandContext ctx)
    {
        var sessionCtrl = BattleLuckPlugin.Session;
        var sender = ctx.GetSenderCharacterEntity();
        if (sessionCtrl != null && sender.Exists())
        {
            var steamId = sender.GetSteamId();
            foreach (var kv in sessionCtrl.ActiveSessions)
            {
                if (kv.Value?.Context?.Players.Contains(steamId) == true)
                    return kv.Value.Context.SessionId;
            }
        }
        return SessionDevKey;
    }

    static PrefabGUID ResolvePrefabOrHash(string value)
    {
        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var hash))
            return new PrefabGUID(hash);
        return PrefabHelper.GetLivePrefabGuid(value) ?? PrefabHelper.GetPrefabGuidDeep(value) ?? PrefabGUID.Empty;
    }

    static bool TryResolveEntityId(string selector, out Entity entity)
    {
        entity = Entity.Null;

        var parts = selector.Split(':', 2, StringSplitOptions.TrimEntries);
        if (parts.Length == 2 &&
            int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var index) &&
            int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var version))
        {
            entity = new Entity { Index = index, Version = version };
            return entity.Exists();
        }

        if (int.TryParse(selector, NumberStyles.Integer, CultureInfo.InvariantCulture, out var entityIndex))
        {
            var em = VRisingCore.EntityManager;
            var query = em.CreateEntityQuery(ComponentType.ReadOnly<Translation>(), ComponentType.ReadOnly<PrefabGUID>());
            var entities = query.ToEntityArray(Allocator.Temp);
            try
            {
                foreach (var candidate in entities)
                {
                    if (candidate.Index == entityIndex && candidate.Exists())
                    {
                        entity = candidate;
                        return true;
                    }
                }
            }
            finally
            {
                if (entities.IsCreated)
                    entities.Dispose();
                query.Dispose();
            }
        }

        return false;
    }

    static bool TryParseFloat3(string text, out float3 value)
    {
        value = float3.zero;
        var parts = text.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 3)
            return false;
        if (!float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var x) ||
            !float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var y) ||
            !float.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var z))
            return false;
        value = new float3(x, y, z);
        return true;
    }

    static bool TryResolveEntityByNetworkNumeric(ulong networkIdValue, out Entity resolved)
    {
        resolved = Entity.Null;
        var em = VRisingCore.EntityManager;
        var query = em.CreateEntityQuery(ComponentType.ReadOnly<NetworkId>());
        var entities = query.ToEntityArray(Allocator.Temp);

        try
        {
            foreach (var entity in entities)
            {
                if (!entity.Exists())
                    continue;

                var networkId = entity.Read<NetworkId>();
                if (TryExtractNetworkIdKeys(networkId, out var keys) && keys.Contains(networkIdValue))
                {
                    resolved = entity;
                    return true;
                }
            }
        }
        finally
        {
            if (entities.IsCreated)
                entities.Dispose();
            query.Dispose();
        }

        return false;
    }

    static string NetworkSummary(Entity entity)
    {
        if (!entity.Exists() || !entity.Has<NetworkId>())
            return "-";
        return TryExtractNetworkIdKeys(entity.Read<NetworkId>(), out var keys)
            ? string.Join(",", keys.Take(2))
            : "-";
    }

    [Command("npc.status.session", description: "Show tracked NPC status for a session. Usage: .npc.status.session [sessionId|all]", adminOnly: true)]
    public static void Status(ChatCommandContext ctx, string sessionId = "")
    {
        var service = BattleLuckPlugin.NpcService;
        if (service == null)
        {
            ctx.Reply("NPC control service is not initialized.");
            return;
        }

        var resolvedSession = string.IsNullOrWhiteSpace(sessionId) ? ResolveSessionId(ctx) : sessionId;
        var entries = service.List(resolvedSession);
        if (entries.Count == 0)
        {
            ctx.Reply($"No tracked NPCs for session '{resolvedSession}'.");
            return;
        }

        ctx.Reply($"NPCs for session '{resolvedSession}' ({entries.Count}):");
        foreach (var entry in entries)
        {
            var pos = entry.Entity.GetPosition();
            ctx.Reply($"  {entry.NpcId} mode={entry.Mode} prefab={entry.PrefabName} pos=({pos.x:F1},{pos.y:F1},{pos.z:F1}) alive={entry.IsAlive}");
        }
    }

    [Command("npc.patrol", description: "Set NPC patrol waypoints. Usage: .npc.patrol <npcId> <x1,y1,z1;x2,y2,z2;...>", adminOnly: true)]
    public static void Patrol(ChatCommandContext ctx, string selector, string waypoints, string a2 = "", string a3 = "", string a4 = "", string a5 = "", string a6 = "", string a7 = "", string a8 = "")
    {
        if (!TryResolveControlledNpc(ctx, selector, out var entry, out var error))
        {
            ctx.Reply(error);
            return;
        }

        var points = new List<BattleLuck.Models.NpcPatrolWaypoint>();
        foreach (var segment in waypoints.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (TryParseFloat3(segment, out var pos))
                points.Add(new BattleLuck.Models.NpcPatrolWaypoint { Position = pos });
        }

        if (points.Count == 0)
        {
            ctx.Reply("No valid waypoints parsed. Use format: x,y,z;x,y,z");
            return;
        }

        var result = BattleLuckPlugin.NpcService!.Patrol(entry.NpcId, points);
        ctx.Reply(result.Success ? $"Patrol set for {entry.NpcId} with {points.Count} waypoints." : result.UserMessage);
    }

    [Command("npc.guard", description: "Set NPC guard post. Usage: .npc.guard <npcId> <x,y,z> [detectionRadius] [chaseRange]", adminOnly: true)]
    public static void Guard(ChatCommandContext ctx, string selector, string position, float detectionRadius = 15f, float chaseRange = 25f)
    {
        if (!TryResolveControlledNpc(ctx, selector, out var entry, out var error))
        {
            ctx.Reply(error);
            return;
        }

        if (!TryParseFloat3(position, out var pos))
        {
            ctx.Reply("Invalid position. Use format: x,y,z");
            return;
        }

        var config = new BattleLuck.Models.NpcGuardPost
        {
            Position = pos,
            DetectionRadius = detectionRadius,
            ChaseRange = chaseRange
        };
        var result = BattleLuckPlugin.NpcService!.Guard(entry.NpcId, config);
        ctx.Reply(result.Success ? $"Guard post set for {entry.NpcId} at ({pos.x:F1},{pos.y:F1},{pos.z:F1})." : result.UserMessage);
    }

    [Command("npc.flee", description: "Make NPC flee. Usage: .npc.flee <npcId> [targetEntity|position] [safeDistance] [duration]", adminOnly: true)]
    public static void Flee(ChatCommandContext ctx, string selector, string target = "", float safeDistance = 20f, float duration = 10f)
    {
        if (!TryResolveControlledNpc(ctx, selector, out var entry, out var error))
        {
            ctx.Reply(error);
            return;
        }

        var config = new BattleLuck.Models.NpcFleeConfig
        {
            SafeDistance = safeDistance,
            DurationSeconds = duration
        };

        if (!string.IsNullOrWhiteSpace(target) && int.TryParse(target, out var entityIndex))
        {
            var em = VRisingCore.EntityManager;
            var query = em.CreateEntityQuery(ComponentType.ReadOnly<Translation>(), ComponentType.ReadOnly<PrefabGUID>());
            var entities = query.ToEntityArray(Allocator.Temp);
            try
            {
                foreach (var entity in entities)
                {
                    if (entity.Index == entityIndex && entity.Exists())
                    {
                        config.FromEntity = entity;
                        break;
                    }
                }
            }
            finally
            {
                if (entities.IsCreated) entities.Dispose();
                query.Dispose();
            }
        }

        var result = BattleLuckPlugin.NpcService!.Flee(entry.NpcId, config);
        ctx.Reply(result.Success ? $"{entry.NpcId} is fleeing." : result.UserMessage);
    }

    [Command("npc.wander", description: "Set NPC wander radius. Usage: .npc.wander <npcId> [radius]", adminOnly: true)]
    public static void Wander(ChatCommandContext ctx, string selector, float radius = 15f)
    {
        if (!TryResolveControlledNpc(ctx, selector, out var entry, out var error))
        {
            ctx.Reply(error);
            return;
        }

        var config = new BattleLuck.Models.NpcWanderConfig { Radius = radius };
        var result = BattleLuckPlugin.NpcService!.Wander(entry.NpcId, config);
        ctx.Reply(result.Success ? $"{entry.NpcId} set to wander within {radius:F1}m." : result.UserMessage);
    }

    [Command("npc.formation", description: "Set NPC formation. Usage: .npc.formation <npcId> <slot1_npcId,dx,dy,dz;slot2_npcId,dx,dy,dz;...> [leaderId]", adminOnly: true)]
    public static void Formation(ChatCommandContext ctx, string selector, string slots, string leaderId = "")
    {
        if (!TryResolveControlledNpc(ctx, selector, out var entry, out var error))
        {
            ctx.Reply(error);
            return;
        }

        var slotList = new List<BattleLuck.Models.NpcFormationSlot>();
        foreach (var segment in slots.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var parts = segment.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var npcId = parts.Length > 0 ? parts[0] : "";
            var offset = parts.Length > 3 && TryParseFloat3(string.Join(",", parts.Skip(1).Take(3)), out var parsedOffset)
                ? parsedOffset
                : new Unity.Mathematics.float3();
            slotList.Add(new BattleLuck.Models.NpcFormationSlot { NpcId = npcId, Offset = offset });
        }

        if (slotList.Count == 0)
        {
            ctx.Reply("No valid formation slots parsed. Use format: npcId,dx,dy,dz;npcId,dx,dy,dz");
            return;
        }

        var center = entry.Entity.GetPosition();
        var result = BattleLuckPlugin.NpcService!.Formation(entry.NpcId, slotList, string.IsNullOrWhiteSpace(leaderId) ? null : leaderId, center);
        ctx.Reply(result.Success ? $"Formation set for {entry.NpcId} with {slotList.Count} slots." : result.UserMessage);
    }

    static bool TryExtractNetworkIdKeys(NetworkId networkId, out HashSet<ulong> keys)
    {
        keys = new HashSet<ulong>();

        var directText = networkId.ToString();
        if (ulong.TryParse(directText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var direct))
            keys.Add(direct);

        var digitOnly = new string(directText.Where(char.IsDigit).ToArray());
        if (ulong.TryParse(digitOnly, NumberStyles.Integer, CultureInfo.InvariantCulture, out var digits))
            keys.Add(digits);

        object boxed = networkId;
        foreach (var field in NetworkIdNumericFields)
        {
            var value = field.GetValue(boxed);
            switch (value)
            {
                case byte b:
                    keys.Add(b);
                    break;
                case sbyte sb when sb >= 0:
                    keys.Add((ulong)sb);
                    break;
                case short s when s >= 0:
                    keys.Add((ulong)s);
                    break;
                case ushort us:
                    keys.Add(us);
                    break;
                case int i when i >= 0:
                    keys.Add((ulong)i);
                    break;
                case uint ui:
                    keys.Add(ui);
                    break;
                case long l when l >= 0:
                    keys.Add((ulong)l);
                    break;
                case ulong ul:
                    keys.Add(ul);
                    break;
            }
        }

        return keys.Count > 0;
    }
}
