using BattleLuck.Commands;

/// <summary>
/// Admin console commands that expose every action in actions_generation.json
/// as a typed chat command. All commands route through <see cref="FlowActionExecutor"/>
/// so parameter parsing, validation, and logging behave identically to flow/event actions.
/// </summary>
public static class ActionsConsoleCommands
{
    // ── Generic dispatcher ────────────────────────────────────────────────────

    /// <summary>Run any registered action string. Format: actionName:key=value|key2=value2</summary>
    [Command("action", description: "Run any BattleLuck action string against a player. Usage: .action <actionString> [player=self]", adminOnly: true)]
    public static void RunAction(ChatCommandContext ctx, string actionString,
        string p2 = "", string p3 = "", string p4 = "", string p5 = "",
        string p6 = "", string p7 = "", string p8 = "", string player = "self")
    {
        var full = JoinArgs(actionString, p2, p3, p4, p5, p6, p7, p8);
        if (!TryResolveTarget(ctx, player, out var target)) return;
        RunAndReply(ctx, full, target);
    }

    // ── Player ────────────────────────────────────────────────────────────────

    [Command("heal", description: "Heal player to full health. Usage: .heal [player=self]", adminOnly: true)]
    public static void Heal(ChatCommandContext ctx, string player = "self")
    {
        if (!TryResolveTarget(ctx, player, out var target)) return;
        RunAndReply(ctx, "heal", target);
    }

    [Command("level.max", description: "Set player to max level. Usage: .level.max [player=self]", adminOnly: true)]
    public static void SetMaxLevel(ChatCommandContext ctx, string player = "self")
    {
        if (!TryResolveTarget(ctx, player, out var target)) return;
        RunAndReply(ctx, "level.set_max", target);
    }

    [Command("buff.clear", description: "Clear all known buffs. Usage: .buff.clear [player=self]", adminOnly: true)]
    public static void BuffClear(ChatCommandContext ctx, string player = "self")
    {
        if (!TryResolveTarget(ctx, player, out var target)) return;
        RunAndReply(ctx, "buff.clear_all", target);
    }

    [Command("ability.slot", description: "Set ability slot (5=Q,6=E,7=R). Usage: .ability.slot <slot> <abilityPrefab> [player=self]", adminOnly: true)]
    public static void AbilitySetSlot(ChatCommandContext ctx, int slot, string abilityPrefab, string player = "self")
    {
        if (!TryResolveTarget(ctx, player, out var target)) return;
        RunAndReply(ctx, $"ability.set_slot:slot={slot}|abilityPrefab={abilityPrefab}", target);
    }

    [Command("stun", description: "Stun player for duration. Usage: .stun [duration=3] [player=self]", adminOnly: true)]
    public static void Stun(ChatCommandContext ctx, float duration = 3f, string player = "self")
    {
        if (!TryResolveTarget(ctx, player, out var target)) return;
        RunAndReply(ctx, $"player.stun:durationSeconds={duration.ToString(CultureInfo.InvariantCulture)}", target);
    }

    // ── Kit ───────────────────────────────────────────────────────────────────

    [Command("kit", description: "Apply kit loadout to player. Usage: .kit <kitId> [player=self]", adminOnly: true)]
    public static void KitApply(ChatCommandContext ctx, string kitId, string player = "self")
    {
        if (!TryResolveTarget(ctx, player, out var target)) return;
        RunAndReply(ctx, $"kit.apply:kitId={kitId}", target);
    }

    [Command("kit.weapons", description: "Apply weapons kit only. Usage: .kit.weapons [player=self]", adminOnly: true)]
    public static void KitWeapons(ChatCommandContext ctx, string player = "self")
    {
        if (!TryResolveTarget(ctx, player, out var target)) return;
        RunAndReply(ctx, "kit.apply_weapons", target);
    }

    [Command("kit.armor", description: "Apply armor kit only. Usage: .kit.armor [player=self]", adminOnly: true)]
    public static void KitArmor(ChatCommandContext ctx, string player = "self")
    {
        if (!TryResolveTarget(ctx, player, out var target)) return;
        RunAndReply(ctx, "kit.apply_armor", target);
    }

    [Command("kit.clear", description: "Clear kit items from inventory. Usage: .kit.clear <kitId> [player=self]", adminOnly: true)]
    public static void KitClear(ChatCommandContext ctx, string kitId, string player = "self")
    {
        if (!TryResolveTarget(ctx, player, out var target)) return;
        RunAndReply(ctx, $"inventory.clear_kit:kitId={kitId}", target);
    }

    // ── Inventory & Logistics ─────────────────────────────────────────────────

    [Command("stash", description: "Stash inventory to container. Usage: .stash [container] [itemIds=all] [player=self]", adminOnly: true)]
    public static void InventoryStash(ChatCommandContext ctx, string container = "", string itemIds = "all", string player = "self")
    {
        if (!TryResolveTarget(ctx, player, out var target)) return;
        RunAndReply(ctx, $"inventory.stash:container={container}|itemIds={itemIds}", target);
    }

    [Command("salvage", description: "Salvage items from inventory. Usage: .salvage [itemIds=all] [salvageAll=true] [player=self]", adminOnly: true)]
    public static void InventorySalvage(ChatCommandContext ctx, string itemIds = "all", bool salvageAll = true, string player = "self")
    {
        if (!TryResolveTarget(ctx, player, out var target)) return;
        RunAndReply(ctx, $"inventory.salvage:itemIds={itemIds}|salvageAll={salvageAll}", target);
    }

    [Command("pull", description: "Pull items from nearby containers. Usage: .pull [itemIds=all] [player=self]", adminOnly: true)]
    public static void InventoryPull(ChatCommandContext ctx, string itemIds = "all", string player = "self")
    {
        if (!TryResolveTarget(ctx, player, out var target)) return;
        RunAndReply(ctx, $"inventory.pull:itemIds={itemIds}", target);
    }

    [Command("craftpull", description: "Pull items for crafting. Usage: .craftpull [player=self]", adminOnly: true)]
    public static void InventoryCraftPull(ChatCommandContext ctx, string player = "self")
    {
        if (!TryResolveTarget(ctx, player, out var target)) return;
        RunAndReply(ctx, "inventory.craftpull", target);
    }

    [Command("sort", description: "Sort inventory. Usage: .sort [player=self]", adminOnly: true)]
    public static void InventorySort(ChatCommandContext ctx, string player = "self")
    {
        if (!TryResolveTarget(ctx, player, out var target)) return;
        RunAndReply(ctx, "inventory.sort", target);
    }

    [Command("emptytrash", description: "Empty trash containers. Usage: .emptytrash [player=self]", adminOnly: true)]
    public static void InventoryEmptyTrash(ChatCommandContext ctx, string player = "self")
    {
        if (!TryResolveTarget(ctx, player, out var target)) return;
        RunAndReply(ctx, "inventory.emptytrash", target);
    }

    [Command("logistics.toggle", description: "Toggle logistics setting. Usage: .logistics.toggle <setting> [enabled=true]", adminOnly: true)]
    public static void LogisticsToggle(ChatCommandContext ctx, string setting, bool enabled = true)
    {
        if (!TryResolveTarget(ctx, "self", out var target)) return;
        RunAndReply(ctx, $"logistics.setting:setting={setting}|enabled={enabled}", target);
    }

    // ── Blood ─────────────────────────────────────────────────────────────────

    [Command("blood", description: "Set blood type. Usage: .blood <type> [quality=100] [player=self]", adminOnly: true)]
    public static void SetBlood(ChatCommandContext ctx, string bloodType, int quality = 100, string player = "self")
    {
        if (!TryResolveTarget(ctx, player, out var target)) return;
        RunAndReply(ctx, $"blood.set:bloodType={bloodType}|quality={quality}", target);
    }

    // ── PvP ───────────────────────────────────────────────────────────────────

    [Command("pvp.enable", description: "Enable PvP for player. Usage: .pvp.enable [zoneHash=0] [player=self]", adminOnly: true)]
    public static void PvpEnable(ChatCommandContext ctx, int zoneHash = 0, string player = "self")
    {
        if (!TryResolveTarget(ctx, player, out var target)) return;
        RunAndReply(ctx, $"pvp.enable:zoneHash={zoneHash}", target);
    }

    [Command("pvp.disable", description: "Disable PvP for player. Usage: .pvp.disable [player=self]", adminOnly: true)]
    public static void PvpDisable(ChatCommandContext ctx, string player = "self")
    {
        if (!TryResolveTarget(ctx, player, out var target)) return;
        RunAndReply(ctx, "pvp.disable", target);
    }

    // ── Teleport ──────────────────────────────────────────────────────────────

    [Command("tp", description: "Teleport player to position. Usage: .tp <x> <y> <z> [player=self]", adminOnly: true)]
    public static void Teleport(ChatCommandContext ctx, float x, float y, float z, string player = "self")
    {
        if (!TryResolveTarget(ctx, player, out var target)) return;
        RunAndReply(ctx, $"teleport.position:position={F(x)},{F(y)},{F(z)}", target);
    }

    [Command("tp.zone", description: "Teleport player to zone waypoint. Usage: .tp.zone <zoneHash> [player=self]", adminOnly: true)]
    public static void TeleportToZone(ChatCommandContext ctx, int zoneHash, string player = "self")
    {
        if (!TryResolveTarget(ctx, player, out var target)) return;
        RunAndReply(ctx, $"teleport:targetZoneHash={zoneHash}", target);
    }

    [Command("tp.dev", description: "Teleport player to dev arena. Usage: .tp.dev [player=self]", adminOnly: true)]
    public static void TeleportToDev(ChatCommandContext ctx, string player = "self")
    {
        if (!TryResolveTarget(ctx, player, out var target)) return;
        RunAndReply(ctx, $"teleport:targetZoneHash={DevSessionService.DevZoneHash}", target);
    }

    // ── Buffs ─────────────────────────────────────────────────────────────────

    [Command("buff.apply", description: "Apply buff to player. Usage: .buff.apply <buffPrefab> [duration=-1] [player=self]", adminOnly: true)]
    public static void BuffApply(ChatCommandContext ctx, string buffPrefab, float duration = -1f, string player = "self")
    {
        if (!TryResolveTarget(ctx, player, out var target)) return;
        RunAndReply(ctx, $"player.buff.apply:buffPrefab={buffPrefab}|duration={F(duration)}", target);
    }

    [Command("buff.remove", description: "Remove buff from player. Usage: .buff.remove <buffPrefab> [player=self]", adminOnly: true)]
    public static void BuffRemove(ChatCommandContext ctx, string buffPrefab, string player = "self")
    {
        if (!TryResolveTarget(ctx, player, out var target)) return;
        RunAndReply(ctx, $"player.buff.remove:buffPrefab={buffPrefab}", target);
    }

    [Command("zone.buff.apply", description: "Apply buff to all zone players. Usage: .zone.buff.apply <buffPrefab> [duration=-1]", adminOnly: true)]
    public static void ZoneBuffApply(ChatCommandContext ctx, string buffPrefab, float duration = -1f)
    {
        if (!TryResolveTarget(ctx, "self", out var target)) return;
        RunAndReply(ctx, $"zone.buff.apply:buffPrefab={buffPrefab}|duration={F(duration)}", target);
    }

    [Command("zone.buff.remove", description: "Remove buff from all zone players. Usage: .zone.buff.remove <buffPrefab>", adminOnly: true)]
    public static void ZoneBuffRemove(ChatCommandContext ctx, string buffPrefab)
    {
        if (!TryResolveTarget(ctx, "self", out var target)) return;
        RunAndReply(ctx, $"zone.buff.remove:buffPrefab={buffPrefab}", target);
    }

    // ── Spawn ─────────────────────────────────────────────────────────────────

    [Command("spawn.wave", description: "Spawn an enemy wave. Usage: .spawn.wave <waveId> <count> <x> <y> <z>", adminOnly: true)]
    public static void SpawnWave(ChatCommandContext ctx, int waveId, int count, float x, float y, float z)
    {
        if (!TryResolveTarget(ctx, "self", out var target)) return;
        RunAndReply(ctx, $"spawn.wave:waveId={waveId}|count={count}|position={F(x)},{F(y)},{F(z)}", target);
    }

    [Command("spawn.boss", description: "Spawn a boss. Usage: .spawn.boss <prefab> <x> <y> <z>", adminOnly: true)]
    public static void SpawnBoss(ChatCommandContext ctx, string prefab, float x, float y, float z)
    {
        if (!TryResolveTarget(ctx, "self", out var target)) return;
        RunAndReply(ctx, $"spawn.boss:prefab={prefab}|position={F(x)},{F(y)},{F(z)}", target);
    }

    [Command("spawn.npc", description: "Spawn NPC group. Usage: .spawn.npc <prefab> <count> <x> <y> <z>", adminOnly: true)]
    public static void SpawnNpc(ChatCommandContext ctx, string prefab, int count, float x, float y, float z)
    {
        if (!TryResolveTarget(ctx, "self", out var target)) return;
        RunAndReply(ctx, $"spawn.npc:prefab={prefab}|count={count}|position={F(x)},{F(y)},{F(z)}", target);
    }

    [Command("spawn.structure", description: "Spawn a structure prefab. Usage: .spawn.structure <prefab> <x> <y> <z>", adminOnly: true)]
    public static void SpawnStructure(ChatCommandContext ctx, string prefab, float x, float y, float z)
    {
        if (!TryResolveTarget(ctx, "self", out var target)) return;
        RunAndReply(ctx, $"structure.spawn:prefab={prefab}|position={F(x)},{F(y)},{F(z)}", target);
    }

    // ── Mode ──────────────────────────────────────────────────────────────────

    [Command("mode.start", description: "Start a game mode. Usage: .mode.start <modeId>", adminOnly: true)]
    public static void ModeStart(ChatCommandContext ctx, string modeId)
    {
        if (!TryResolveTarget(ctx, "self", out var target)) return;
        RunAndReply(ctx, $"mode.start:modeId={modeId}", target);
    }

    [Command("mode.end", description: "End the active game mode. Usage: .mode.end <modeId>", adminOnly: true)]
    public static void ModeEnd(ChatCommandContext ctx, string modeId)
    {
        if (!TryResolveTarget(ctx, "self", out var target)) return;
        RunAndReply(ctx, $"mode.end:modeId={modeId}", target);
    }

    [Command("warevent_start", description: "Legacy war event alias for mode.start. Usage: .warevent_start <modeId>", adminOnly: true)]
    public static void WarEventStart(ChatCommandContext ctx, string modeId)
    {
        if (!TryResolveTarget(ctx, "self", out var target)) return;
        RunAndReply(ctx, $"warevent_start:modeId={modeId}", target);
    }

    [Command("warevent_end", description: "Legacy war event alias for mode.end. Usage: .warevent_end <modeId>", adminOnly: true)]
    public static void WarEventEnd(ChatCommandContext ctx, string modeId)
    {
        if (!TryResolveTarget(ctx, "self", out var target)) return;
        RunAndReply(ctx, $"warevent_end:modeId={modeId}", target);
    }

    // ── Visual / Sequence ─────────────────────────────────────────────────────

    [Command("sequence.play", description: "Play a VFX sequence. Usage: .sequence.play <sequencePrefab> <x> <y> <z> [duration=-1]", adminOnly: true)]
    public static void SequencePlay(ChatCommandContext ctx, string sequencePrefab, float x, float y, float z, float duration = -1f)
    {
        if (!TryResolveTarget(ctx, "self", out var target)) return;
        RunAndReply(ctx, $"sequence.play:sequencePrefab={sequencePrefab}|position={F(x)},{F(y)},{F(z)}|duration={F(duration)}", target);
    }

    [Command("sequence.stop", description: "Stop a VFX sequence. Usage: .sequence.stop <sequencePrefab>", adminOnly: true)]
    public static void SequenceStop(ChatCommandContext ctx, string sequencePrefab)
    {
        if (!TryResolveTarget(ctx, "self", out var target)) return;
        RunAndReply(ctx, $"sequence.stop:sequencePrefab={sequencePrefab}", target);
    }

    [Command("glow.enable", description: "Enable glow on entity. Usage: .glow.enable [color=white] [radius=5] [duration=-1] [player=self]", adminOnly: true)]
    public static void GlowEnable(ChatCommandContext ctx, string color = "white", float radius = 5f, float duration = -1f, string player = "self")
    {
        if (!TryResolveTarget(ctx, player, out var target)) return;
        RunAndReply(ctx, $"glow.enable:color={color}|radius={F(radius)}|duration={F(duration)}", target);
    }

    [Command("glow.disable", description: "Disable glow on entity. Usage: .glow.disable [player=self]", adminOnly: true)]
    public static void GlowDisable(ChatCommandContext ctx, string player = "self")
    {
        if (!TryResolveTarget(ctx, player, out var target)) return;
        RunAndReply(ctx, "glow.disable", target);
    }

    // ── Revive ────────────────────────────────────────────────────────────────

    [Command("revive.grant", description: "Grant revive lives. Usage: .revive.grant <lives> [player=self]", adminOnly: true)]
    public static void ReviveGrant(ChatCommandContext ctx, int lives, string player = "self")
    {
        if (!TryResolveTarget(ctx, player, out var target)) return;
        RunAndReply(ctx, $"revive.grant:maxLives={lives}", target);
    }

    [Command("revive.reset", description: "Reset revive lives. Usage: .revive.reset [player=self]", adminOnly: true)]
    public static void ReviveReset(ChatCommandContext ctx, string player = "self")
    {
        if (!TryResolveTarget(ctx, player, out var target)) return;
        RunAndReply(ctx, "revive.reset", target);
    }

    // ── Score ─────────────────────────────────────────────────────────────────

    [Command("score.add", description: "Add score to player. Usage: .score.add <points> [reason=admin] [player=self]", adminOnly: true)]
    public static void ScoreAdd(ChatCommandContext ctx, int points, string reason = "admin", string player = "self")
    {
        if (!TryResolveTarget(ctx, player, out var target)) return;
        RunAndReply(ctx, $"score.add:points={points}|reason={reason}", target);
    }

    [Command("score.reset", description: "Reset player score. Usage: .score.reset [player=self]", adminOnly: true)]
    public static void ScoreReset(ChatCommandContext ctx, string player = "self")
    {
        if (!TryResolveTarget(ctx, player, out var target)) return;
        RunAndReply(ctx, "score.reset", target);
    }

    // ── Death ─────────────────────────────────────────────────────────────────

    [Command("death.prevent", description: "Grant charge-based death prevention. Usage: .death.prevent [charges=1] [windowSeconds=0] [cooldownSeconds=0] [player=self]", adminOnly: true)]
    public static void DeathPrevent(ChatCommandContext ctx, int charges = 1, float windowSeconds = 0f, float cooldownSeconds = 0f, string player = "self")
    {
        if (!TryResolveTarget(ctx, player, out var target)) return;
        RunAndReply(ctx, $"death.prevent:initialCharges={charges}|activeWindowSeconds={F(windowSeconds)}|triggerCooldownSeconds={F(cooldownSeconds)}", target);
    }

    [Command("death.allow", description: "Re-enable death after prevention. Usage: .death.allow [player=self]", adminOnly: true)]
    public static void DeathAllow(ChatCommandContext ctx, string player = "self")
    {
        if (!TryResolveTarget(ctx, player, out var target)) return;
        RunAndReply(ctx, "death.allow", target);
    }

    // ── Progression ───────────────────────────────────────────────────────────

    [Command("progression.vbloods", description: "Unlock all VBlood progression. Usage: .progression.vbloods [player=self]", adminOnly: true)]
    public static void ProgressionVBloods(ChatCommandContext ctx, string player = "self")
    {
        if (!TryResolveTarget(ctx, player, out var target)) return;
        RunAndReply(ctx, "progression.unlock.all_vbloods", target);
    }

    [Command("progression.research", description: "Unlock all research. Usage: .progression.research [player=self]", adminOnly: true)]
    public static void ProgressionResearch(ChatCommandContext ctx, string player = "self")
    {
        if (!TryResolveTarget(ctx, player, out var target)) return;
        RunAndReply(ctx, "progression.unlock.all_research", target);
    }

    // ── Notify ────────────────────────────────────────────────────────────────

    [Command("notify", description: "Send notification to player. Usage: .notify <message> [type=info] [player=self]", adminOnly: true)]
    public static void Notify(ChatCommandContext ctx, string message,
        string a2 = "", string a3 = "", string a4 = "", string a5 = "",
        string type = "info", string player = "self")
    {
        if (!TryResolveTarget(ctx, player, out var target)) return;
        var full = JoinArgs(message, a2, a3, a4, a5);
        RunAndReply(ctx, $"notify:message={full}|type={type}", target);
    }

    // ── AI / Boss ─────────────────────────────────────────────────────────────

    [Command("ai.boss.aggro", description: "Set boss aggro target. Usage: .ai.boss.aggro <prefab> [aggroRange=40] [leashRange=80] [player=self]", adminOnly: true)]
    public static void BossAggro(ChatCommandContext ctx, string prefab, float aggroRange = 40f, float leashRange = 80f, string player = "self")
    {
        if (!TryResolveTarget(ctx, player, out var target)) return;
        RunAndReply(ctx, $"npc.aggro:prefab={prefab}|aggroRange={F(aggroRange)}|leashRange={F(leashRange)}", target);
    }

    [Command("ai.boss.deaggro", description: "De-aggro boss. Usage: .ai.boss.deaggro <prefab>", adminOnly: true)]
    public static void BossDeaggro(ChatCommandContext ctx, string prefab)
    {
        if (!TryResolveTarget(ctx, "self", out var target)) return;
        RunAndReply(ctx, $"npc.hold:prefab={prefab}", target);
    }

    [Command("ai.behavior", description: "Set AI behavior. Usage: .ai.behavior <prefab> <behavior> [radius=40]", adminOnly: true)]
    public static void SetBehavior(ChatCommandContext ctx, string prefab, string behavior, float radius = 40f)
    {
        if (!TryResolveTarget(ctx, "self", out var target)) return;
        RunAndReply(ctx, $"npc.{behavior}:prefab={prefab}|radius={F(radius)}", target);
    }

    // ── Zone ──────────────────────────────────────────────────────────────────

    // ── Mount ─────────────────────────────────────────────────────────────────

    [Command("mount.summon", description: "Summon a mount. Usage: .mount.summon [type=horse] [player=self]", adminOnly: true)]
    public static void MountSummon(ChatCommandContext ctx, string type = "horse", string player = "self")
    {
        if (!TryResolveTarget(ctx, player, out var target)) return;
        RunAndReply(ctx, $"mount.summon:mountType={type}", target);
    }

    [Command("mount.dismiss", description: "Dismiss current mount. Usage: .mount.dismiss [player=self]", adminOnly: true)]
    public static void MountDismiss(ChatCommandContext ctx, string player = "self")
    {
        if (!TryResolveTarget(ctx, player, out var target)) return;
        RunAndReply(ctx, "mount.dismiss", target);
    }

    // ── Snapshot ──────────────────────────────────────────────────────────────

    [Command("snapshot.save", description: "Save player state snapshot. Usage: .snapshot.save [zoneHash=0] [player=self]", adminOnly: true)]
    public static void SnapshotSave(ChatCommandContext ctx, int zoneHash = 0, string player = "self")
    {
        if (!TryResolveTarget(ctx, player, out var target)) return;
        RunAndReply(ctx, $"snapshot.save:zoneHash={zoneHash}", target);
    }

    [Command("snapshot.restore", description: "Restore player state snapshot. Usage: .snapshot.restore [zoneHash=0] [player=self]", adminOnly: true)]
    public static void SnapshotRestore(ChatCommandContext ctx, int zoneHash = 0, string player = "self")
    {
        if (!TryResolveTarget(ctx, player, out var target)) return;
        RunAndReply(ctx, $"snapshot.restore:zoneHash={zoneHash}", target);
    }

    // ── Equip ─────────────────────────────────────────────────────────────────

    [Command("equip.restrict", description: "Restrict max gear level. Usage: .equip.restrict <maxGearLevel> [player=self]", adminOnly: true)]
    public static void EquipRestrict(ChatCommandContext ctx, int maxGearLevel, string player = "self")
    {
        if (!TryResolveTarget(ctx, player, out var target)) return;
        RunAndReply(ctx, $"equip.restrict:maxGearLevel={maxGearLevel}", target);
    }

    [Command("equip.unrestrict", description: "Remove equip restrictions. Usage: .equip.unrestrict [player=self]", adminOnly: true)]
    public static void EquipUnrestrict(ChatCommandContext ctx, string player = "self")
    {
        if (!TryResolveTarget(ctx, player, out var target)) return;
        RunAndReply(ctx, "equip.unrestrict", target);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    static void RunAndReply(ChatCommandContext ctx, string actionString, Unity.Entities.Entity target)
    {
        var context = new FlowActionContext
        {
            PlayerCharacter = target,
            GameContext = null
        };

        var executor = new FlowActionExecutor(new PlayerStateController());
        var result = executor.ExecuteViaRuntime(actionString, context);

        ctx.Reply(result.Success
            ? $"✔ {actionString}"
            : $"✘ {actionString} — {result.Error ?? result.UserMessage}");
    }

    static bool TryResolveTarget(ChatCommandContext ctx, string selector, out Unity.Entities.Entity player)
    {
        player = Unity.Entities.Entity.Null;

        if (string.IsNullOrWhiteSpace(selector) || selector.Equals("self", StringComparison.OrdinalIgnoreCase))
        {
            player = ctx.GetSenderCharacterEntity();
            if (player.Exists())
                return true;
            ctx.Reply("Sender character entity is not available.");
            return false;
        }

        foreach (var candidate in VRisingCore.GetOnlinePlayers())
        {
            if (!candidate.Exists() || !candidate.IsPlayer())
                continue;

            var name = candidate.GetPlayerName();
            var steamId = candidate.GetSteamId();

            if ((ulong.TryParse(selector, System.Globalization.NumberStyles.Integer,
                     System.Globalization.CultureInfo.InvariantCulture, out var sid) && steamId == sid) ||
                name.Equals(selector, StringComparison.OrdinalIgnoreCase) ||
                name.Contains(selector, StringComparison.OrdinalIgnoreCase))
            {
                player = candidate;
                return true;
            }
        }

        ctx.Reply($"No online player matched '{selector}'.");
        return false;
    }

    /// <summary>Join variadic string args, skipping blanks, to rebuild a multi-word action string.</summary>
    static string JoinArgs(params string[] args) =>
        string.Join(" ", args.Where(a => !string.IsNullOrWhiteSpace(a)));

    /// <summary>Format a float with invariant culture for action strings.</summary>
    static string F(float v) => v.ToString(CultureInfo.InvariantCulture);
}
