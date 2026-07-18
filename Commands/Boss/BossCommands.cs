using BattleLuck.Commands;
using BattleLuck.Services.Npc;

/// <summary>
/// Compatibility command names for controlled VBloods and other boss-like units.
/// Every mutation routes through the canonical NPC action pipeline.
/// </summary>
public static class BossCommands
{
    [Command("boss.spawn", description: "Spawn a controlled VBlood/NPC. Usage: .boss.spawn <prefab> [id] [homeRadius]", adminOnly: true)]
    public static void BossSpawn(ChatCommandContext ctx, string prefabName, string bossId = "", float homeRadius = 40f) =>
        Run(ctx, $"npc.spawn:prefab={prefabName}|npcId={bossId}|homeRadius={F(homeRadius)}|count=1");

    [Command("ai.boss.aggro", description: "Force a controlled NPC to target/chase.", adminOnly: true)]
    public static void Aggro(ChatCommandContext ctx, string bossId = "self", string targetSelector = "self", float aggroRange = 50f, float leashRange = 60f) =>
        Run(ctx, $"npc.aggro:npcId={bossId}|target={targetSelector}|aggroRange={F(aggroRange)}|leashRange={F(leashRange)}");

    [Command("ai.boss.deaggro", description: "Clear forced aggression for a controlled NPC.", adminOnly: true)]
    public static void Deaggro(ChatCommandContext ctx, string bossId = "self") =>
        Run(ctx, $"npc.release:npcId={bossId}");

    [Command("boss.follow_target", description: "Force a controlled NPC to follow a target.", adminOnly: true)]
    public static void Follow(ChatCommandContext ctx, string bossId = "self", string targetSelector = "self", float followRange = 10f, float leashRange = 60f) =>
        Run(ctx, $"npc.follow:npcId={bossId}|target={targetSelector}|followRange={F(followRange)}|leashRange={F(leashRange)}");

    [Command("boss.clear_follow", description: "Clear forced follow for a controlled NPC.", adminOnly: true)]
    public static void ClearFollow(ChatCommandContext ctx, string bossId = "self") =>
        Run(ctx, $"npc.release:npcId={bossId}");

    [Command("ai.set_behavior", description: "Set a controlled NPC behavior.", adminOnly: true)]
    public static void SetBehavior(ChatCommandContext ctx, string bossId = "self", string behavior = "guard", float radius = 40f)
    {
        var action = behavior.Trim().ToLowerInvariant() switch
        {
            "aggro" or "attack" or "chase" => $"npc.aggro:npcId={bossId}|target=self|leashRange={F(radius)}",
            "follow" => $"npc.follow:npcId={bossId}|target=self|leashRange={F(radius)}",
            "guard" or "hold" or "stay" => $"npc.hold:npcId={bossId}|holdRadius={F(radius)}",
            "wander" => $"npc.wander:npcId={bossId}|radius={F(radius)}",
            _ => $"npc.release:npcId={bossId}"
        };
        Run(ctx, action);
    }

    [Command("boss.goto", description: "Move a controlled NPC to a position.", adminOnly: true)]
    public static void BossGoto(ChatCommandContext ctx, string bossId = "self", string position = "self", float arrivalRange = 2f, bool hold = true) =>
        Run(ctx, $"npc.goto:npcId={bossId}|destination={position}|arrivalRange={F(arrivalRange)}");

    [Command("boss.goto.pos", description: "Move a controlled NPC to your position.", adminOnly: true)]
    public static void BossGotoPos(ChatCommandContext ctx, string bossId = "self", float arrivalRange = 2f) =>
        BossGoto(ctx, bossId, "self", arrivalRange);

    [Command("boss.return_home", description: "Move a controlled NPC back to its registered home.", adminOnly: true)]
    public static void BossReturnHome(ChatCommandContext ctx, string bossId = "self")
    {
        var entry = Resolve(ctx, bossId);
        if (entry == null) return;
        var p = entry.HomePosition;
        Run(ctx, $"npc.goto:npcId={entry.NpcId}|destination={F(p.x)},{F(p.y)},{F(p.z)}|arrivalRange=2");
    }

    [Command("boss.list", description: "List controlled NPCs in the current session.", adminOnly: true)]
    public static void BossList(ChatCommandContext ctx)
    {
        var entries = BattleLuckPlugin.NpcService?.List(ResolveSessionId(ctx)) ?? Array.Empty<ControlledNpcEntry>();
        if (entries.Count == 0) { ctx.Reply("No controlled NPCs are tracked for this session."); return; }
        var sb = new StringBuilder($"Controlled NPCs ({entries.Count}):\n");
        foreach (var entry in entries.Take(20))
            sb.AppendLine($"  • {entry.NpcId} prefab={entry.PrefabName} mode={entry.Mode} {(entry.IsAlive ? "alive" : "dead")}");
        ctx.Reply(sb.ToString());
    }

    [Command("boss.despawn", description: "Despawn a controlled NPC.", adminOnly: true)]
    public static void BossDespawn(ChatCommandContext ctx, string bossId = "self") =>
        Run(ctx, $"npc.despawn:npcId={bossId}");

    [Command("boss.despawn_all", description: "Despawn all controlled NPCs in the current session.", adminOnly: true)]
    public static void BossDespawnAll(ChatCommandContext ctx) =>
        Run(ctx, "npc.despawn:selector=all|limit=100");

    static void Run(ChatCommandContext ctx, string action)
    {
        var player = ctx.GetSenderCharacterEntity();
        if (!player.Exists()) { ctx.Reply("Sender character entity is unavailable."); return; }

        var session = FindSession(player.GetSteamId());
        var result = new FlowActionExecutor(new PlayerStateController(), BattleLuckPlugin.GameModes)
            .ExecuteViaRuntime(action, new FlowActionContext
            {
                PlayerCharacter = player,
                GameContext = session?.Context,
                Config = session?.Config,
                ZoneHash = session?.Context.ZoneHash ?? 0
            });
        ctx.Reply(result.Success ? $"✔ {action}" : $"✘ {result.Error ?? result.UserMessage}");
    }

    static ControlledNpcEntry? Resolve(ChatCommandContext ctx, string selector)
    {
        var service = BattleLuckPlugin.NpcService;
        if (service == null) { ctx.Reply("NPC control service is not initialized."); return null; }
        var sessionId = ResolveSessionId(ctx);
        if (!string.IsNullOrWhiteSpace(selector) && !selector.Equals("self", StringComparison.OrdinalIgnoreCase) && service.TryGet(selector, out var exact))
            return exact;
        var entry = service.GetLatest(sessionId);
        if (entry == null) ctx.Reply("No controlled NPC matches that selector.");
        return entry;
    }

    static ActiveSession? FindSession(ulong steamId) => BattleLuckPlugin.Session?.ActiveSessions.Values
        .FirstOrDefault(s => s.Context.Players.Contains(steamId));

    static string ResolveSessionId(ChatCommandContext ctx) =>
        FindSession(ctx.GetSenderCharacterEntity().GetSteamId())?.Context.SessionId ?? "_dev_";

    static string F(float value) => value.ToString(CultureInfo.InvariantCulture);
}
