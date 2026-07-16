using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using BattleLuck.Core;
using BattleLuck.Models;
using ActionModelsRegistry = BattleLuck.Models.ActionRegistry;
using BattleLuck.Services;
using BattleLuck.Services.AI;
using BattleLuck.Services.Flow;
using BattleLuck.Services.Modes;
using BattleLuck.Services.Runtime;
using Unity.Mathematics;
using BattleLuck.Commands;
using VampireCommandFramework;

public static class PlayerCommands
{
    static readonly LiveEventOperatorService LiveOperator = new();
    static readonly CustomSequenceService CustomSequences = new();
    static readonly AiTaskService AiTasks = AiTaskService.Instance;
    static readonly string[] TeamColorBuffs =
    {
        "VBlood_Aura_Champion_Red",
        "VBlood_Aura_Champion_Blue",
        "VBlood_Aura_Champion_Green",
        "VBlood_Aura_Champion_Yellow",
        "VBlood_Aura_Champion_Purple",
        "VBlood_Aura_Champion_Orange"
    };

    [Command("toggleenter", description: "Enter a zone session. Use: .toggleenter [modeName]")]
    public static void ToggleEnter(ChatCommandContext ctx, string modeId = "")
    {
        var session = BattleLuckPlugin.Session;
        if (session == null) { ctx.Reply("Session controller not initialized."); return; }

        if (!ctx.TryGetSenderIdentity(out var entity, out var steamId))
        {
            ctx.Reply("Your connected player identity is not ready yet. Rejoin the server or retry in a moment.");
            return;
        }

        var result = session.ToggleEnter(steamId, entity, string.IsNullOrEmpty(modeId) ? null : modeId);
        ctx.Reply(result.Success ? "Entered queue. Arena will prepare, teleport joined players, stun 10s, then auto-start." : result.UserMessage);
    }

    [Command("start", description: "Force-start your prepared event session after build checks", adminOnly: true)]
    public static void StartMatch(ChatCommandContext ctx)
    {
        var session = BattleLuckPlugin.Session;
        if (session == null) { ctx.Reply("Session controller not initialized."); return; }

        var entity = ctx.GetSenderCharacterEntity();
        var steamId = entity.GetSteamId();
        var result = session.ForceStartForPlayer(steamId);
        ctx.Reply(result.Success
            ? "Start requested. The match will begin after any build queue clears and the 10s stun countdown finishes."
            : result.UserMessage);
    }

    [Command("rollback", description: "Discard a pending AI proposal; use .ai rollback player/server for event-state recovery", adminOnly: true)]
    public static void RollbackEvent(ChatCommandContext ctx, string operationId = "")
    {
        if (string.IsNullOrWhiteSpace(operationId))
        {
            ctx.Reply("Usage: .rollback <operationId> or .ai event rollback <operationId>");
            return;
        }

        ReplyEventRollback(ctx, ctx.GetSenderCharacterEntity().GetSteamId(), operationId.Trim());
    }

    [Command("swapteam", description: "Make closest enemy friendly, balance teams, or move target. Usage: .swapteam [closest] OR .swapteam balance [layout] OR .swapteam <name> <teamId>", adminOnly: true)]
    public static void SwapTeam(
        ChatCommandContext ctx,
        string a1 = "",
        string a2 = "",
        string a3 = "",
        string a4 = "",
        string a5 = "",
        string a6 = "",
        string a7 = "",
        string a8 = "",
        string a9 = "",
        string a10 = "",
        string a11 = "",
        string a12 = "")
    {
        HandleSwapTeam(ctx, NonEmptyWords(a1, a2, a3, a4, a5, a6, a7, a8, a9, a10, a11, a12));
    }

    [Command("swapteam.ai", description: "Balance teams and announce with AI. NPC-directed AI team control is coming soon. Usage: .swapteam.ai [closest|balance|name teamId]", adminOnly: true)]
    public static void SwapTeamAi(
        ChatCommandContext ctx,
        string a1 = "",
        string a2 = "",
        string a3 = "",
        string a4 = "",
        string a5 = "",
        string a6 = "",
        string a7 = "",
        string a8 = "",
        string a9 = "",
        string a10 = "",
        string a11 = "",
        string a12 = "")
    {
        // Same swap logic as .swapteam
        var args = NonEmptyWords(a1, a2, a3, a4, a5, a6, a7, a8, a9, a10, a11, a12);
        HandleSwapTeam(ctx, args);

        // Current behavior is an AI-generated in-game announcement. NPC-directed
        // team control is intentionally reserved for a future implementation.
        var ai = BattleLuckPlugin.AIAssistant;
        if (ai == null)
            return;

        var steamId = ctx.GetSenderSteamId();
        if (steamId == 0)
        {
            ctx.Reply("Your connected player identity is not ready yet. Retry in a moment.");
            return;
        }
        var argText = args.Count > 0 ? string.Join(" ", args) : "closest-enemy";
        var prompt = $"You are the BattleLuck arena AI. The admin just ran a team-swap command with arguments: '{argText}'. " +
                      "In 1-2 short, in-character sentences, announce this team change to the players in chat.";

        _ = Task.Run(async () =>
        {
            try
            {
                await ai.HandleDirectQuery(steamId, prompt, "swapteam.ai", broadcastToInGameChat: true).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                BattleLuckPlugin.LogWarning($"[SwapTeamAi] AI chat failed: {ex.Message}");
            }
        });
    }

    static void HandleSwapTeam(ChatCommandContext ctx, IReadOnlyList<string> args)
    {
        var session = BattleLuckPlugin.Session;
        if (session == null) { ctx.Reply("Session controller not initialized."); return; }

        var senderSteamId = ctx.GetSenderCharacterEntity().GetSteamId();
        var first = args.Count > 0 ? args[0] : "";
        var second = args.Count > 1 ? args[1] : "";

        if (args.Count == 0 || IsClosestEnemyRequest(first))
        {
            MakeClosestEnemyFriendly(ctx, senderSteamId);
            return;
        }

        var balanceArgs = IsBalanceRequest(first)
            ? args.Skip(1).ToList()
            : args;

        if (IsBalanceRequest(first) || IsTeamLayoutRequest(first, second))
        {
            var active = GetActiveSessionFor(senderSteamId) ?? session.ActiveSessions.Values.FirstOrDefault();
            if (active?.Context == null)
            {
                ctx.Reply("No active session found to balance.");
                return;
            }

            var layoutToken = balanceArgs.Count > 0 ? balanceArgs[0] : "2";
            var targetTokens = balanceArgs.Count > 0 && IsLayoutToken(layoutToken)
                ? balanceArgs.Skip(1).ToList()
                : balanceArgs.ToList();

            if (!TryParseTeamLayout(IsLayoutToken(layoutToken) ? layoutToken : "2", out var teamCount, out var layout, out var layoutError))
            {
                ctx.Reply(layoutError ?? "Invalid team layout.");
                return;
            }

            BalanceSessionTeams(ctx, active, teamCount, layout, targetTokens);
            return;
        }

        if (!byte.TryParse(second, out var teamId) || teamId < 1 || teamId > 6)
        {
            ctx.Reply("Team must be 1-6.");
            return;
        }

        var activeSession = ResolveSessionForSwap(senderSteamId);
        if (!TryMoveNamedTargetToTeam(first, teamId, activeSession, out var moved, out var moveError))
        {
            ctx.Reply(moveError ?? "Target not found.");
            return;
        }

        ctx.Reply($"{moved} moved to Team{teamId}.");
    }

    static bool IsClosestEnemyRequest(string value)
    {
        value = NormalizeSelector(value);
        return value is "closest" or "closestest" or "nearest" or "near" or "enemy" or "anemi" or "friend" or "ally" or "auto";
    }

    static bool IsBalanceRequest(string value) =>
        NormalizeSelector(value) is "balance" or "balanceteams" or "teams";

    static void MakeClosestEnemyFriendly(ChatCommandContext ctx, ulong senderSteamId)
    {
        var sender = ctx.GetSenderCharacterEntity();
        if (!sender.Exists() || !sender.IsPlayer())
        {
            ctx.Reply("Player entity not found.");
            return;
        }

        var active = ResolveSessionForSwap(senderSteamId);
        var senderTeam = ResolvePlayerTeam(sender, senderSteamId, active);
        if (!senderTeam.HasValue)
        {
            ctx.Reply("Your team could not be resolved.");
            return;
        }

        var candidates = FindClosestEnemyCandidates(sender, senderSteamId, senderTeam.Value, active);

        if (candidates.Count == 0)
        {
            ctx.Reply(active == null
                ? "No nearby enemy found."
                : "No nearby enemy found in your active session.");
            return;
        }

        var target = candidates[0];
        if (target.IsPlayer && active != null)
            AssignPlayerToTeam(active, target.Entity, senderTeam.Value);
        else if (!target.IsPlayer && active != null && TryResolveBossByEntity(active, target.Entity, out var boss))
            AssignBossToTeam(boss, senderTeam.Value);
        else
        {
            target.Entity.SetTeam(senderTeam.Value);
            ApplyTeamColor(target.Entity, senderTeam.Value);
        }

        ctx.Reply($"{target.Label} is now friendly with you (Team{senderTeam.Value}, {target.Distance:F1}m away).");
    }

    static int? ResolvePlayerTeam(Entity player, ulong steamId, ActiveSession? session)
    {
        if (session?.Context?.Teams != null &&
            session.Context.Teams.TryGetValue(steamId, out var sessionTeam))
            return sessionTeam;

        return player.Exists() && player.Has<Team>()
            ? player.Read<Team>().Value
            : null;
    }

    static List<ClosestEnemyCandidate> FindClosestEnemyCandidates(Entity sender, ulong senderSteamId, int senderTeam, ActiveSession? session)
    {
        var senderPos = sender.GetPosition();
        var candidates = new List<ClosestEnemyCandidate>();

        candidates.AddRange(VRisingCore.GetOnlinePlayers()
            .Where(e => e.Exists() && e.IsPlayer())
            .Select(e => new ClosestEnemyCandidate
            {
                Entity = e,
                SteamId = e.GetSteamId(),
                Team = ResolvePlayerTeam(e, e.GetSteamId(), session),
                Distance = math.distance(senderPos, e.GetPosition()),
                IsPlayer = true,
                Label = e.GetPlayerName()
            })
            .Where(e =>
                e.SteamId != 0 &&
                e.SteamId != senderSteamId &&
                (session == null || session.Context.Players.Contains(e.SteamId)) &&
                e.Team.HasValue &&
                e.Team.Value != senderTeam));

        candidates.AddRange(FindNearbyEnemyNpcs(senderPos, senderTeam, radius: 80f, limit: 20));
        return candidates
            .OrderBy(e => e.Distance)
            .ThenBy(e => e.IsPlayer ? 0 : 1)
            .ToList();
    }

    static List<ClosestEnemyCandidate> FindNearbyEnemyNpcs(float3 center, int senderTeam, float radius, int limit)
    {
        var em = VRisingCore.EntityManager;
        var query = em.CreateEntityQuery(new EntityQueryDesc
        {
            All = new[] { ComponentType.ReadOnly<Translation>(), ComponentType.ReadOnly<PrefabGUID>(), ComponentType.ReadOnly<Team>() },
            Any = new[] { ComponentType.ReadOnly<UnitLevel>(), ComponentType.ReadOnly<UnitStats>(), ComponentType.ReadOnly<Aggroable>() },
            None = new[] { ComponentType.ReadOnly<PlayerCharacter>() }
        });

        var entities = query.ToEntityArray(Allocator.Temp);
        var candidates = new List<ClosestEnemyCandidate>();
        try
        {
            foreach (var entity in entities)
            {
                if (!entity.Exists())
                    continue;

                var team = entity.Read<Team>().Value;
                if (team == senderTeam)
                    continue;

                var distance = math.distance(center, entity.GetPosition());
                if (distance > radius)
                    continue;

                candidates.Add(new ClosestEnemyCandidate
                {
                    Entity = entity,
                    Team = team,
                    Distance = distance,
                    IsPlayer = false,
                    Label = FormatClosestNpcLabel(entity)
                });
            }
        }
        finally
        {
            if (entities.IsCreated)
                entities.Dispose();
            query.Dispose();
        }

        return candidates
            .OrderBy(e => e.Distance)
            .Take(limit)
            .ToList();
    }

    static bool TryResolveBossByEntity(ActiveSession session, Entity entity, out ControlledNpcEntry boss)
    {
        boss = null!;
        var service = BattleLuckPlugin.NpcService;
        if (service == null)
            return false;

        return service.TryGetByEntity(entity, out boss);
    }

    static string FormatClosestNpcLabel(Entity entity)
    {
        var prefab = entity.GetPrefabGuid();
        var name = PrefabHelper.GetLivePrefabName(prefab) ?? PrefabHelper.GetName(prefab);
        return string.IsNullOrWhiteSpace(name) ? $"NPC {entity.Index}:{entity.Version}" : name;
    }

    sealed class ClosestEnemyCandidate
    {
        public Entity Entity { get; init; }
        public ulong SteamId { get; init; }
        public int? Team { get; init; }
        public float Distance { get; init; }
        public bool IsPlayer { get; init; }
        public string Label { get; init; } = "";
    }

    static bool IsTeamLayoutRequest(string targetOrLayout, string team)
    {
        if (!string.IsNullOrWhiteSpace(team))
            return !byte.TryParse(team, out _);

        if (string.IsNullOrWhiteSpace(targetOrLayout))
            return true;

        var value = targetOrLayout.Trim();
        if (value.Contains("vs", StringComparison.OrdinalIgnoreCase))
            return true;

        return IsLayoutToken(value);
    }

    static bool IsLayoutToken(string value)
    {
        value = (value ?? "").Trim();
        if (string.IsNullOrWhiteSpace(value))
            return true;
        if (value.Contains("vs", StringComparison.OrdinalIgnoreCase))
            return true;
        return int.TryParse(value, out var teamCount) && teamCount is >= 2 and <= 6;
    }

    static bool TryParseTeamLayout(string value, out int teamCount, out string layout, out string? error)
    {
        teamCount = 2;
        layout = "teamvsteam";
        error = null;

        value = (value ?? "").Trim();
        if (string.IsNullOrWhiteSpace(value) || value.Equals("2", StringComparison.OrdinalIgnoreCase))
            return true;

        if (int.TryParse(value, out var parsed))
        {
            if (parsed < 2 || parsed > 6)
            {
                error = "Team count must be 2-6.";
                return false;
            }

            teamCount = parsed;
            layout = string.Join("vs", Enumerable.Range(1, parsed).Select(_ => "team"));
            return true;
        }

        var slots = value.Split("vs", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (slots.Length == 0)
        {
            error = "Layout must look like teamvsteam or bossvsteamvsteamvsboss.";
            return false;
        }

        teamCount = slots.Count(slot =>
            slot.Contains("team", StringComparison.OrdinalIgnoreCase) ||
            slot.Contains("player", StringComparison.OrdinalIgnoreCase));

        if (teamCount < 2 || teamCount > 6)
        {
            error = "Layout must contain 2-6 player team slots.";
            return false;
        }

        layout = string.Join("vs", slots.Select(s => s.ToLowerInvariant()));
        return true;
    }

    static void BalanceSessionTeams(ChatCommandContext ctx, ActiveSession session, int teamCount, string layout, IReadOnlyList<string> targetTokens)
    {
        var online = VRisingCore.GetOnlinePlayers()
            .Where(e => e.Exists() && e.IsPlayer() && session.Context.Players.Contains(e.GetSteamId()))
            .OrderBy(e => e.GetSteamId())
            .ToList();

        if (online.Count == 0)
        {
            ctx.Reply("No online players in this session.");
            return;
        }

        session.Context.Teams.Clear();
        session.Context.State["team_layout"] = layout;
        for (var teamId = 1; teamId <= teamCount; teamId++)
            session.Context.State[$"team_label_{teamId}"] = $"Team{teamId}";

        var counts = new Dictionary<int, int>();
        var assignedPlayers = new HashSet<ulong>();
        var assignedBosses = 0;
        var explicitMessages = new List<string>();

        for (var i = 0; i < targetTokens.Count; i++)
        {
            var teamId = (i % teamCount) + 1;
            var token = targetTokens[i];
            if (TryResolvePlayer(token, online, out var player, out var playerError))
            {
                var steamId = player.GetSteamId();
                if (!assignedPlayers.Add(steamId))
                    continue;
                AssignPlayerToTeam(session, player, teamId);
                counts[teamId] = counts.TryGetValue(teamId, out var current) ? current + 1 : 1;
                explicitMessages.Add($"{player.GetPlayerName()}->Team{teamId}");
                continue;
            }

            if (TryResolveBoss(token, session, out var boss, out var bossError))
            {
                AssignBossToTeam(boss, teamId);
                assignedBosses++;
                explicitMessages.Add($"{boss.NpcId}->Team{teamId}");
                continue;
            }

            ctx.Reply(playerError ?? bossError ?? $"Target '{token}' was not found.");
            return;
        }

        if (online.Count % teamCount != 0)
        {
            ctx.Reply($"Cannot split {online.Count} player(s) into {teamCount} equal teams. Add/remove players or choose another team count.");
            return;
        }

        var targetPerTeam = online.Count / teamCount;
        if (counts.Any(kv => kv.Value > targetPerTeam))
        {
            ctx.Reply($"Named players overfill a team. Need exactly {targetPerTeam} player(s) per team.");
            return;
        }

        foreach (var player in online.Where(p => !assignedPlayers.Contains(p.GetSteamId())))
        {
            var teamId = Enumerable.Range(1, teamCount)
                .OrderBy(id => counts.GetValueOrDefault(id))
                .ThenBy(id => id)
                .First(id => counts.GetValueOrDefault(id) < targetPerTeam);

            AssignPlayerToTeam(session, player, teamId);
            assignedPlayers.Add(player.GetSteamId());
            counts[teamId] = counts.TryGetValue(teamId, out var current) ? current + 1 : 1;
        }

        var summary = string.Join(", ", Enumerable.Range(1, teamCount).Select(id => $"Team{id}={counts.GetValueOrDefault(id)}"));
        var explicitSummary = explicitMessages.Count > 0 ? $" named=[{string.Join(", ", explicitMessages.Take(8))}]" : "";
        ctx.Reply($"Teams balanced for {session.Context.ModeId}: {layout} ({summary}, bosses={assignedBosses}).{explicitSummary}");
    }

    static List<string> NonEmptyWords(params string[] words) =>
        words.Where(w => !string.IsNullOrWhiteSpace(w)).Select(w => w.Trim()).ToList();

    static List<string> SplitCommandWords(string value) =>
        (value ?? "")
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();

    static ActiveSession? ResolveSessionForSwap(ulong requesterSteamId) =>
        GetActiveSessionFor(requesterSteamId) ?? BattleLuckPlugin.Session?.ActiveSessions.Values.FirstOrDefault();

    static bool TryMoveNamedTargetToTeam(string selector, int teamId, ActiveSession? session, out string moved, out string? error)
    {
        moved = "";
        error = null;

        var online = VRisingCore.GetOnlinePlayers()
            .Where(e => e.Exists() && e.IsPlayer() && (session == null || session.Context.Players.Contains(e.GetSteamId())))
            .ToList();

        if (TryResolvePlayer(selector, online, out var player, out error))
        {
            if (session != null)
                AssignPlayerToTeam(session, player, teamId);
            else
            {
                player.SetTeam(teamId);
                ApplyTeamColor(player, teamId);
            }
            moved = player.GetPlayerName();
            return true;
        }

        if (TryResolveBoss(selector, session, out var boss, out error))
        {
            AssignBossToTeam(boss, teamId);
            moved = boss.NpcId;
            return true;
        }

        error ??= $"Target '{selector}' was not found by player name, boss id, boss alias, or boss number.";
        return false;
    }

    static void AssignPlayerToTeam(ActiveSession session, Entity player, int teamId)
    {
        var steamId = player.GetSteamId();
        player.SetTeam(teamId);
        ApplyTeamColor(player, teamId);
        session.Context.Teams[steamId] = teamId;
        session.Context.State[$"team_label_{teamId}"] = $"Team{teamId}";
    }

    static void AssignBossToTeam(ControlledNpcEntry boss, int teamId)
    {
        var service = BattleLuckPlugin.NpcService;
        if (service == null)
            return;

        var result = service.SetTeam(boss.NpcId, teamId);
        if (!result.Success)
            BattleLuckPlugin.LogWarning($"[PlayerCommands] Failed to assign NPC '{boss.NpcId}' to team {teamId}: {result.Error}");

        try
        {
            if (boss.Entity.Exists())
                ApplyTeamColor(boss.Entity, teamId);
        }
        catch { }
    }

    static void ApplyTeamColor(Entity entity, int teamId)
    {
        if (!entity.Exists())
            return;

        foreach (var name in TeamColorBuffs)
        {
            try
            {
                if (PrefabHelper.TryGetValidPrefabGuidDeep(name, out var oldBuff))
                    entity.TryRemoveBuff(oldBuff);
            }
            catch { }
        }

        var index = Math.Clamp(teamId - 1, 0, TeamColorBuffs.Length - 1);
        try
        {
            if (PrefabHelper.TryGetValidPrefabGuidDeep(TeamColorBuffs[index], out var buff))
                entity.BuffEntity(buff, out _, 0f, persistThroughDeath: true);
        }
        catch { }
    }

    static bool TryResolvePlayer(string selector, IReadOnlyList<Entity> candidates, out Entity player, out string? error)
    {
        player = Entity.Null;
        error = null;
        selector = (selector ?? "").Trim();
        if (string.IsNullOrWhiteSpace(selector))
        {
            error = "Player name is empty.";
            return false;
        }

        if (ulong.TryParse(selector, out var steamId))
        {
            player = candidates.FirstOrDefault(e => e.GetSteamId() == steamId);
            if (player.Exists())
                return true;
        }

        var normalized = NormalizeSelector(selector);
        var matches = candidates
            .Select(e => (Entity: e, Name: e.GetPlayerName(), Key: NormalizeSelector(e.GetPlayerName())))
            .Where(e =>
                e.Name.Equals(selector, StringComparison.OrdinalIgnoreCase) ||
                e.Key.Equals(normalized, StringComparison.OrdinalIgnoreCase) ||
                e.Key.StartsWith(normalized, StringComparison.OrdinalIgnoreCase) ||
                e.Key.Contains(normalized, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (matches.Count == 1)
        {
            player = matches[0].Entity;
            return true;
        }

        if (matches.Count > 1)
            error = $"Player name '{selector}' is ambiguous: {string.Join(", ", matches.Select(m => m.Name).Take(5))}";

        return false;
    }

    static bool TryResolveBoss(string selector, ActiveSession? session, out ControlledNpcEntry boss, out string? error)
    {
        boss = null!;
        error = null;
        selector = (selector ?? "").Trim();
        if (session == null)
        {
            error = "No active session found for NPC lookup.";
            return false;
        }

        var service = BattleLuckPlugin.NpcService;
        if (service == null)
        {
            error = "NPC control service is not available.";
            return false;
        }

        var npcs = service.List(session.Context.SessionId)
            .Where(n => n.IsAlive)
            .OrderBy(n => n.CreatedAtUtc)
            .ToList();

        if (npcs.Count == 0)
        {
            error = "No alive tracked NPCs in this session.";
            return false;
        }

        var lower = selector.ToLowerInvariant();
        if (lower.StartsWith("boss", StringComparison.OrdinalIgnoreCase) &&
            int.TryParse(lower[4..], out var bossNumber) &&
            bossNumber >= 1 &&
            bossNumber <= npcs.Count)
        {
            boss = npcs[bossNumber - 1];
            return true;
        }

        var normalized = NormalizeSelector(selector);
        var matches = npcs
            .Where(n => NpcSearchKeys(n).Any(key =>
                key.Equals(normalized, StringComparison.OrdinalIgnoreCase) ||
                key.StartsWith(normalized, StringComparison.OrdinalIgnoreCase) ||
                key.Contains(normalized, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        if (matches.Count == 1)
        {
            boss = matches[0];
            return true;
        }

        if (matches.Count > 1)
            error = $"NPC name '{selector}' is ambiguous: {string.Join(", ", matches.Select(n => n.NpcId).Take(5))}";

        return false;
    }

    static IEnumerable<string> NpcSearchKeys(ControlledNpcEntry npc)
    {
        var raw = new[] { npc.NpcId, npc.PrefabName, npc.DisplayName }
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .ToList();

        foreach (var value in raw)
        {
            var normalized = NormalizeSelector(value);
            if (!string.IsNullOrWhiteSpace(normalized))
                yield return normalized;

            foreach (var part in value.Split(new[] { '_', '-', '.', ' ' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var key = NormalizeSelector(part);
                if (key is "" or "char" or "vblood" or "v")
                    continue;
                yield return key;
            }
        }

        foreach (var alias in BossAliasHints)
        {
            if (raw.Any(value => alias.Value.Any(hint => value.Contains(hint, StringComparison.OrdinalIgnoreCase))))
                yield return alias.Key;
        }
    }

    static string NormalizeSelector(string value) =>
        new string((value ?? "")
            .Where(char.IsLetterOrDigit)
            .Select(char.ToLowerInvariant)
            .ToArray());

    static readonly Dictionary<string, string[]> BossAliasHints = new(StringComparer.OrdinalIgnoreCase)
    {
        ["alpha"] = new[] { "AlphaWolf", "Alpha_Wolf" },
        ["wolf"] = new[] { "AlphaWolf", "Alpha_Wolf" },
        ["dracula"] = new[] { "Dracula", "HighLord" },
        ["solarus"] = new[] { "Solarus" },
        ["winged"] = new[] { "Winged" },
        ["horror"] = new[] { "Winged" },
        ["morian"] = new[] { "Morian" },
        ["styx"] = new[] { "Styx", "Nightmarshal" },
        ["octavian"] = new[] { "Octavian" },
        ["tristan"] = new[] { "Tristan" },
        ["gorecrusher"] = new[] { "Gorecrusher" },
        ["ungora"] = new[] { "Ungora" },
        ["foulrot"] = new[] { "Foulrot" },
        ["leandra"] = new[] { "Leandra" },
        ["meredith"] = new[] { "Meredith" }
    };

    [Command("toggleleave", description: "Properly leave the current zone session")]
    public static void ToggleLeave(ChatCommandContext ctx)
    {
        var session = BattleLuckPlugin.Session;
        if (session == null) { ctx.Reply("Session controller not initialized."); return; }

        var entity = ctx.GetSenderCharacterEntity();
        var result = session.ToggleLeave(entity.GetSteamId(), entity);
        ctx.Reply(result.Success ? "✓ Left zone — gear and position restored." : result.UserMessage);
    }

    [Command("exit", description: "Force exit current zone session")]
    public static void ForceExit(ChatCommandContext ctx)
    {
        var session = BattleLuckPlugin.Session;
        if (session == null) { ctx.Reply("Session controller not initialized."); return; }

        var entity = ctx.GetSenderCharacterEntity();
        ulong steamId = entity.GetSteamId();

        if (!session.ForceExitPlayer(steamId, entity))
        {
            ctx.Reply("You are not in any active zone session.");
            return;
        }

        ctx.Reply("Exited zone — gear and position restored.");
    }

    [Command("score", description: "Show current scoreboard")]
    public static void ShowScore(ChatCommandContext ctx)
    {
        var session = BattleLuckPlugin.Session;
        if (session == null || session.ActiveSessions.Count == 0)
        {
            ctx.Reply("No active sessions.");
            return;
        }

        foreach (var kv in session.ActiveSessions)
        {
            var s = kv.Value;
            var leaderboard = s.Context.Scores.GetLeaderboard();
            ctx.Reply($"{s.Context.ModeId} (zone {kv.Key}):");
            int rank = 1;
            foreach (var steamId in leaderboard.Take(5))
            {
                ctx.Reply($"  #{rank++}. {steamId} — {s.Context.Scores.GetPlayerScore(steamId)} pts");
            }
        }
    }

    [Command("bstatus", description: "Show BattleLuck live status: actions, flows, zones, bosses, objects, players.", adminOnly: true)]
    public static void BattleStatus(ChatCommandContext ctx, string modeId = "")
    {
        ReplyBattleStatus(ctx, modeId);
    }

    [Command("director", description: "Show the BattleLuck game-session director view for admins and AI.", adminOnly: true)]
    public static void DirectorStatus(ChatCommandContext ctx, string modeId = "")
    {
        var report = GameSessionDirectorService.Build(modeId);
        foreach (var line in report.ToChatLines())
            ctx.Reply(line);
    }

    [Command("sessiondirector", description: "Alias for .director.", adminOnly: true)]
    public static void SessionDirectorStatus(ChatCommandContext ctx, string modeId = "")
    {
        DirectorStatus(ctx, modeId);
    }

    [Command("eventstatus", description: "Show unified event runtime status.", adminOnly: true)]
    public static void EventStatus(ChatCommandContext ctx, string modeId = "")
    {
        ReplyBattleStatus(ctx, modeId);
    }

    [Command("actions.status", description: "Show action catalog and live runtime status.", adminOnly: true)]
    public static void ActionsStatus(ChatCommandContext ctx, string modeId = "")
    {
        ReplyBattleStatus(ctx, modeId);
    }

    static void ReplyBattleStatus(ChatCommandContext ctx, string modeId)
    {
        var session = BattleLuckPlugin.Session;
        var online = VRisingCore.GetOnlinePlayers().Where(e => e.Exists() && e.IsPlayer()).ToList();
        var manifest = new ActionManifestService();
        var entries = manifest.Entries.Values.ToList();
        var safe = entries.Count(e => e.RiskLevel.Equals("safe", StringComparison.OrdinalIgnoreCase));
        var controlled = entries.Count(e => e.RiskLevel.Equals("controlled", StringComparison.OrdinalIgnoreCase));
        var destructive = entries.Count(e => e.RiskLevel.Equals("destructive", StringComparison.OrdinalIgnoreCase));
        var handlers = entries.Count(e => e.HandlerAvailable);

        ctx.Reply($"BattleLuck status: prefabs named={PrefabHelper.GetAllLive().Count}, actions={entries.Count} handlers={handlers}, risk safe/control/destructive={safe}/{controlled}/{destructive}");

        if (session == null)
        {
            ctx.Reply("Session controller not initialized.");
            return;
        }

        var active = session.ActiveSessions.Values
            .Where(s => string.IsNullOrWhiteSpace(modeId) || s.Context.ModeId.Equals(modeId, StringComparison.OrdinalIgnoreCase))
            .ToList();

        ctx.Reply($"Sessions: active={active.Count}, enteredPlayers={session.EnteredPlayerCount}, burningPenalty={session.BurningPlayerCount}, onlinePlayers={online.Count}");
        if (active.Count == 0)
        {
            ctx.Reply(string.IsNullOrWhiteSpace(modeId)
                ? "No active BattleLuck sessions."
                : $"No active BattleLuck session for mode '{modeId}'.");
            return;
        }

        foreach (var s in active.Take(5))
            ReplySessionStatus(ctx, session, s, online);
    }

    static void ReplySessionStatus(ChatCommandContext ctx, SessionController controller, ActiveSession s, List<Entity> online)
    {
        var zone = s.Config.Zones.Zones.FirstOrDefault(z => z.Hash == s.Context.ZoneHash);
        var center = zone?.TeleportSpawn?.ToFloat3() ?? zone?.Position?.ToFloat3() ?? float3.zero;
        var radius = zone?.Radius ?? 0f;
        var exitRadius = zone?.ExitRadius > 0 ? zone.ExitRadius : radius + 5f;
        var rules = s.Config.Session.Rules;
        var flowEnterActions = CountFlowActions(s.Config.FlowEnter) + CountFlowActions(s.Config.Session.Flow.Enter);
        var flowExitActions = CountFlowActions(s.Config.FlowExit) + CountFlowActions(s.Config.Session.Flow.Exit);
        var stateKeys = string.Join(",", s.Context.State.Keys.Take(8));

        var sessionId = s.Context.SessionId;
        ctx.Reply($"{s.Context.ModeId}/{s.Context.ZoneHash}: started={s.IsStarted} paused={s.IsPaused} elapsed={s.Context.ElapsedSeconds:F0}/{s.Context.TimeLimitSeconds}s players={s.Context.Players.Count}/{rules.MaxPlayers} zone={zone?.Name ?? "?"} r={radius:F0}/{exitRadius:F0}");
        ctx.Reply($"  flows: enterActions={flowEnterActions}, exitActions={flowExitActions}, states=[{stateKeys}] arenaInit={s.ArenaInitialized}");

        var runtime = controller.EventRuntime.GetStatus(sessionId);
        if (runtime == null)
        {
            ctx.Reply("  unified: no runtime session active; using legacy/split mode config only.");
        }
        else
        {
            var phases = runtime.ExecutedTimedPhases.Count == 0 ? "none" : string.Join(",", runtime.ExecutedTimedPhases.Take(5));
            var byKind = FormatCounts(runtime.TrackedEntities.AliveByKind);
            var byGroup = FormatCounts(runtime.TrackedEntities.AliveByGroup, 4);
            ctx.Reply($"  unified: event={runtime.EventId} enabled={runtime.Enabled} started={runtime.Started} actions={runtime.TotalConfiguredActions} phases={runtime.Phases} triggers={runtime.Triggers} executedTimed=[{phases}]");
            ctx.Reply($"  objects: configured objects/glows/entities={runtime.Objects}/{runtime.Glows}/{runtime.EntityDefinitions}, trackedAlive={runtime.TrackedEntities.AliveNonPlayers}, kinds=[{byKind}], groups=[{byGroup}]");
        }

        ctx.Reply($"  spawned: units={s.Spawner.AliveCount}, walls={s.Border?.WallCount ?? 0}, floors={s.Border?.FloorCount ?? 0}, platformTiles={s.Platform?.TileCount ?? 0}, platformSpawned={s.Platform?.IsSpawned ?? false}");

        ReplyNpcStatus(ctx, sessionId, center);
        ReplyPlayerRuleStatus(ctx, controller, s, online, center, exitRadius);
    }

    static void ReplyNpcStatus(ChatCommandContext ctx, string sessionId, float3 zoneCenter)
    {
        var service = BattleLuckPlugin.NpcService;
        var entries = service?.List(sessionId) ?? Array.Empty<ControlledNpcEntry>();
        if (entries.Count == 0)
        {
            ctx.Reply("  npcs: none tracked for this session.");
            return;
        }

        var alive = entries.Count(e => e.IsAlive);
        ctx.Reply($"  npcs: tracked={entries.Count}, alive={alive}");
        foreach (var npc in entries.Take(4))
        {
            var pos = npc.IsAlive ? npc.Entity.GetPosition() : float3.zero;
            var homeDist = npc.IsAlive ? math.distance(pos.xz, npc.HomePosition.xz) : -1f;
            var zoneDist = npc.IsAlive ? math.distance(pos.xz, zoneCenter.xz) : -1f;
            var rule = !npc.IsAlive ? "dead" :
                npc.Mode is NpcControlMode.Follow or NpcControlMode.Aggro or NpcControlMode.GoTo || homeDist <= npc.HomeRadius * 1.75f
                    ? "OK"
                    : "LEASH?";
            ctx.Reply($"    {npc.NpcId}: {npc.Mode} {rule} prefab={npc.PrefabName} homeDist={homeDist:F0}/{npc.HomeRadius:F0} zoneDist={zoneDist:F0}");
        }
    }

    static void ReplyPlayerRuleStatus(ChatCommandContext ctx, SessionController controller, ActiveSession s, List<Entity> online, float3 center, float exitRadius)
    {
        var inside = 0;
        var offline = 0;
        var outside = 0;
        var burning = 0;
        var missingTeam = 0;

        foreach (var steamId in s.Context.Players)
        {
            var player = online.FirstOrDefault(e => e.GetSteamId() == steamId);
            if (!player.Exists())
            {
                offline++;
                continue;
            }

            if (controller.IsPlayerBurning(steamId))
                burning++;

            if (!s.Context.Teams.ContainsKey(steamId) && s.IsStarted)
                missingTeam++;

            if (exitRadius > 0f && math.lengthsq(center) > 0.0001f)
            {
                var pos = player.GetPosition();
                if (math.distance(pos.xz, center.xz) <= exitRadius) inside++;
                else outside++;
            }
            else
            {
                inside++;
            }
        }

        var rule = outside == 0 && burning == 0 && offline == 0 ? "OK" : "CHECK";
        ctx.Reply($"  players: rule={rule} inside={inside}, outside={outside}, offline={offline}, burning={burning}, missingTeam={missingTeam}, scores={s.Context.Scores.GetAllPlayerScores().Count}");
    }

    static int CountFlowActions(FlowConfig? flow)
    {
        if (flow == null)
            return 0;
        return flow.Flows.Values.Sum(f => f.Actions.Count);
    }

    static string FormatCounts(Dictionary<string, int> counts, int max = 5)
    {
        if (counts.Count == 0)
            return "none";
        return string.Join(",", counts.OrderByDescending(kv => kv.Value).Take(max).Select(kv => $"{kv.Key}:{kv.Value}"));
    }

    [Command("elo", description: "Show Elo ratings for Colosseum mode")]
    public static void ShowElo(ChatCommandContext ctx)
    {
        var registry = BattleLuckPlugin.GameModes;
        if (registry?.Resolve("colosseum") is GameModeEngine colosseum)
        {
            ctx.Reply("Colosseum Elo: use GameModeEngine lifecycle (Elo removed).");
            return;
        }

        ctx.Reply("Colosseum mode not available.");
    }

    [Command("help", description: "Show available BattleLuck commands")]
    public static void ShowHelp(ChatCommandContext ctx)
    {
        ctx.Reply("BattleLuck Commands:");
        ctx.Reply("  help — This help");
        ctx.Reply("  toggleenter — Join a zone session queue");
        ctx.Reply("  start — Admin force-start prepared session");
        ctx.Reply("  toggleleave — Leave zone session");
        ctx.Reply("  exit — Force exit (admin bypass)");
        ctx.Reply("  score — View scoreboard");
        ctx.Reply("  elo — View Elo leaderboard");
        ctx.Reply("  ai <question> — AI chat; up to four replies, .ai end closes the session");
        ctx.Reply("  ai tasks [goal] — View tasks or (admin) create a catalog-backed plan");
        ctx.Reply("  ai history [items] — View your in-memory AI history from the last 24 hours");
        ctx.Reply("Admin commands (admin only):");
        ctx.Reply("  modelist — List modes");
        ctx.Reply("  modeinfo <id> — Mode details");
        ctx.Reply("  modeend <id> — End mode");
        ctx.Reply("  force <mode> — Admin enter mode and request forced start");
        ctx.Reply("  reload — Reload configs");
        ctx.Reply("  pause/resume — Pause sessions");
        ctx.Reply("  zoneinfo — Zone stats");
        ctx.Reply("  bstatus — Live status for actions, flows, zones, bosses, objects, players");
    }

    [Command("grid", description: "Show your map grid or convert a grid cell. Usage: .grid [column,row] [height]")]
    public static void ShowWorldGrid(ChatCommandContext ctx, string cell = "", float height = 0f)
    {
        if (string.IsNullOrWhiteSpace(cell))
        {
            if (!ctx.GetSenderCharacterEntity().Exists())
            {
                ctx.Reply("Player character is not available.");
                return;
            }

            var world = ctx.GetSenderCharacterEntity().GetPosition();
            var grid = WorldGridCoordinate.FromWorld(world);
            ctx.Reply($"Grid {grid} (nearest {grid.NearestCell}); world X={world.x:F1}, Y={world.y:F1}, Z={world.z:F1}");
            return;
        }

        if (!WorldGridCoordinate.TryParse(cell, out var coordinate))
        {
            ctx.Reply("Invalid grid cell. Use .grid 10,15 or .grid G10:15.");
            return;
        }

        var position = coordinate.ToWorld(height);
        ctx.Reply($"Grid {coordinate} = world X={position.X:F1}, Y={position.Y:F1}, Z={position.Z:F1}");
    }

    [Command("ai", description: "Primary AI interface. Public chat is advice-only; admin changes require preview and approval.")]
    public static async Task AskAI(
        ChatCommandContext ctx,
        string query = "",
        string a2 = "",
        string a3 = "",
        string a4 = "",
        string a5 = "",
        string a6 = "",
        string a7 = "",
        string a8 = "",
        string a9 = "",
        string a10 = "",
        string a11 = "",
        string a12 = "",
        string a13 = "",
        string a14 = "",
        string a15 = "",
        string a16 = "",
        string a17 = "",
        string a18 = "")
    {
        query = JoinCommandWords(query, a2, a3, a4, a5, a6, a7, a8, a9, a10, a11, a12, a13, a14, a15, a16, a17, a18);
        var aiAssistant = BattleLuckPlugin.AIAssistant;
        var steamId = ctx.GetSenderCharacterEntity().GetSteamId();

        if (await TryHandleAiUtilityCommand(ctx, steamId, query))
            return;

        if (aiAssistant == null)
        {
            ctx.Reply("AI Assistant is not initialized. Check ai_config.json, then run -ai.reload.");
            return;
        }

        if (string.IsNullOrWhiteSpace(query))
        {
            ctx.Reply("Please provide a question. Example: -ai How do I improve in Colosseum mode?");
            return;
        }

        if (IsAdminSender(ctx) && await TryHandleLiveOperatorCommand(ctx, aiAssistant, steamId, query))
            return;

        // Fall back to existing chat behavior
        try
        {
            // A normal .ai question opens a bounded four-reply conversation. The
            // player can stop it early with .ai end.
            GameChatAiBridge.BeginSession(steamId);
            var response = await aiAssistant.HandleDirectQuery(steamId, query, source: IsAdminSender(ctx) ? "admin_command" : "player_command");
            if (!string.IsNullOrWhiteSpace(response))
                GameChatAiBridge.RecordReply(steamId);
            
            // Check if response is JSON action
            var actionString = FlowActionExecutor.ParseJsonToActionString(response ?? "");
            if (!string.IsNullOrEmpty(actionString))
            {
                ReplyLiveActionPreview(ctx, steamId, "AI JSON action response", new[] { actionString });
            }
            else
            {
                // Regular text response
                ctx.Reply(aiAssistant.FormatInGameResponse(query, response));
            }
        }
        catch (Exception ex)
        {
            BattleLuckPlugin.LogWarning($"AI command error: {ex.Message}");
            ctx.Reply("Sorry, I encountered an error processing your request.");
        }
    }

    static async Task<bool> TryHandleAiUtilityCommand(ChatCommandContext ctx, ulong steamId, string query)
    {
        var words = SplitCommandWords(query);
        if (words.Count == 0)
            return false;

        // Read-only deployment status is safe for every player. Mutating deploy
        // and rollback commands remain in the admin-gated operator path below.
        if (words.Count >= 2 &&
            words[0].Equals("event", StringComparison.OrdinalIgnoreCase) &&
            words[1].Equals("status", StringComparison.OrdinalIgnoreCase))
        {
            EventDeploymentCommands.Status(ctx, words.Count > 2 ? words[2] : "");
            return true;
        }

        if (words[0].Equals("event", StringComparison.OrdinalIgnoreCase) &&
            words.Count >= 2 && words[1].Equals("deploy", StringComparison.OrdinalIgnoreCase))
        {
            if (!IsAdminSender(ctx))
            {
                ctx.Reply("🚫 Event deployment requires admin privileges. Use `.ai event status [eventId]` to inspect a deployment.");
                return true;
            }

            if (words.Count < 4)
            {
                ctx.Reply("Usage: .ai event deploy <eventId> <https-gist-url> (admin only)");
                return true;
            }

            await EventDeploymentCommands.DeployFromGist(ctx, words[2], words[3], words.Skip(4).Any(word => word.Equals("--dry-run", StringComparison.OrdinalIgnoreCase) || word.Equals("dry-run", StringComparison.OrdinalIgnoreCase)));
            return true;
        }

        if (words[0].Equals("event", StringComparison.OrdinalIgnoreCase) &&
            words.Count >= 2 && words[1].Equals("audit", StringComparison.OrdinalIgnoreCase))
        {
            if (!IsAdminSender(ctx))
            {
                ctx.Reply("🚫 Event audit details are admin-only. Use `.ai event status [eventId]` for public deployment status.");
                return true;
            }

            EventDeploymentCommands.AuditSummary(ctx, words.Count > 2 ? words[2] : "");
            return true;
        }

        if (words[0].Equals("event", StringComparison.OrdinalIgnoreCase) &&
            words.Count >= 3 && words[1].Equals("rollback", StringComparison.OrdinalIgnoreCase) &&
            LooksLikeDeploymentEventId(words[2]))
        {
            if (!IsAdminSender(ctx))
            {
                ctx.Reply("🚫 Event deployment rollback requires admin privileges.");
                return true;
            }

            EventDeploymentCommands.Rollback(ctx, words[2]);
            return true;
        }

        if (words[0].Equals("rollback", StringComparison.OrdinalIgnoreCase) &&
            words.Count >= 2 && words[1].Equals("player", StringComparison.OrdinalIgnoreCase))
        {
            if (!IsAdminSender(ctx))
            {
                ctx.Reply("🚫 Per-player event rollback requires admin privileges.");
                return true;
            }

            PlayerRollbackCommands.RollbackPlayer(ctx, words.Count > 2 ? words[2] : "self", words.Count > 3 ? words[3] : "");
            return true;
        }

        if (words[0].Equals("rollback", StringComparison.OrdinalIgnoreCase) &&
            words.Count >= 2 && words[1].Equals("server", StringComparison.OrdinalIgnoreCase))
        {
            if (!IsAdminSender(ctx))
            {
                ctx.Reply("🚫 Server/player-state rollback requires admin privileges.");
                return true;
            }

            if (words.Count >= 3 && words[2].Equals("status", StringComparison.OrdinalIgnoreCase))
                PlayerRollbackCommands.Status(ctx);
            else if (words.Count >= 4 && words[2].Equals("players", StringComparison.OrdinalIgnoreCase))
                PlayerRollbackCommands.RollbackAllEventPlayers(ctx, words[3].Equals("confirm", StringComparison.OrdinalIgnoreCase));
            else if (words.Count >= 5 && words[2].Equals("purge", StringComparison.OrdinalIgnoreCase))
                EventDeploymentCommands.PurgeBackup(ctx, words[3], words[4].Equals("confirm", StringComparison.OrdinalIgnoreCase) ? "" : words[4], words[^1].Equals("confirm", StringComparison.OrdinalIgnoreCase));
            else
                ctx.Reply("Use `.ai rollback server status`, `.ai rollback server players confirm`, or `.ai rollback server purge <eventId> [backupId] confirm`. These commands do not touch the V Rising world save.");
            return true;
        }

        if (words[0].Equals("end", StringComparison.OrdinalIgnoreCase) && words.Count == 1)
        {
            var ended = GameChatAiBridge.EndSession(steamId);
            ctx.Reply(ended
                ? "🤖 AI conversation ended. Future normal chat will not be sent to AI."
                : "🤖 No active AI conversation was found.");
            return true;
        }

        if (words[0].Equals("history", StringComparison.OrdinalIgnoreCase))
        {
            var includeAll = words.Skip(1).Any(word => word.Equals("all", StringComparison.OrdinalIgnoreCase));
            var count = words.Skip(1)
                .Select(word => int.TryParse(word, out var parsed) ? parsed : 20)
                .FirstOrDefault(value => value != 20);
            if (count <= 0)
                count = 20;

            ReplyAiHistory(ctx, steamId, count, includeAll && IsAdminSender(ctx));
            return true;
        }

        if (!words[0].Equals("tasks", StringComparison.OrdinalIgnoreCase))
            return false;

        var goal = string.Join(" ", words.Skip(1));
        if (string.IsNullOrWhiteSpace(goal))
        {
            ReplyAiTasks(ctx, steamId, includeAll: IsAdminSender(ctx));
            return true;
        }

        if (!IsAdminSender(ctx))
        {
            ctx.Reply("🚫 Creating AI planning tasks requires admin privileges. Use .ai tasks to view your recent tasks.");
            return true;
        }

        await CreateAiTask(ctx, steamId, goal);
        return true;
    }

    static void ReplyAiHistory(ChatCommandContext ctx, ulong steamId, int count, bool includeAll)
    {
        var turns = ConversationStore.Instance.RecentWithin(
            ConversationStore.HistoryRetention,
            Math.Clamp(count, 1, 50),
            includeAll ? null : steamId);

        ctx.Reply($"🕘 AI history (last 24 hours, {turns.Count} item(s)){(includeAll ? "; all players" : "; you only")}:" );
        if (turns.Count == 0)
        {
            ctx.Reply("No AI conversation items are available in the one-day in-memory window.");
            return;
        }

        foreach (var turn in turns)
            ctx.Reply($"[{turn.Timestamp.ToLocalTime():MM-dd HH:mm}] {turn.Speaker}: {TrimReply(turn.Text, 240)}");
    }

    static void ReplyAiTasks(ChatCommandContext ctx, ulong steamId, bool includeAll)
    {
        var tasks = AiTasks.List(steamId, includeAll, 20);
        ctx.Reply($"🧭 AI tasks (last 24 hours, {tasks.Count} item(s)){(includeAll ? "; all admins" : "; you only")}:" );
        if (tasks.Count == 0)
        {
            ctx.Reply("No planning tasks yet. Admins can create one with .ai tasks <goal>.");
            return;
        }

        foreach (var task in tasks)
        {
            ctx.Reply($"- {task.TaskId} [{task.Status}] {TrimReply(task.Goal, 180)} ({task.Steps.Count} step(s))");
            foreach (var step in task.Steps.Take(3))
                ctx.Reply($"  {step.Confidence:P0} {TrimReply(step.Action, 180)} — {TrimReply(step.Reason, 140)}");
        }
    }

    static async Task CreateAiTask(ChatCommandContext ctx, ulong steamId, string goal)
    {
        ctx.Reply("🧭 Planning AI task from the verified action catalog; no action has been executed.");
        var result = await AiTasks.CreatePlanAsync(steamId, goal).ConfigureAwait(false);
        if (!result.Success || result.Value == null)
        {
            ctx.Reply($"AI task failed: {result.Error}");
            return;
        }

        var task = result.Value;
        ctx.Reply($"Task {task.TaskId} [{task.Status}] created. Preview/approval is still required before execution.");
        if (task.Steps.Count == 0)
        {
            ctx.Reply("No structured steps were returned. Check the provider with .aistatus and try again.");
            return;
        }

        foreach (var step in task.Steps.Take(10))
            ctx.Reply($"{step.Confidence:P0} {TrimReply(step.Action, 220)} — {TrimReply(step.Reason, 160)}");
    }

    static async Task<bool> TryHandleLiveOperatorCommand(ChatCommandContext ctx, AIAssistant aiAssistant, ulong steamId, string query)
    {
        var trimmed = query.Trim();
        if (TryHandleSwapTeamOperatorCommand(ctx, trimmed))
            return true;

        if (TryHandleBossOperatorCommand(ctx, trimmed))
            return true;

        if (TryHandleKitAbilityEmptyCommand(ctx, trimmed))
            return true;

        if (IsQuickApprove(trimmed))
        {
            ReplyEventApprove(ctx, steamId, "");
            return true;
        }

        if (IsQuickRollback(trimmed))
        {
            ReplyEventRollback(ctx, steamId, "");
            return true;
        }

        if (trimmed.StartsWith("catalog search ", StringComparison.OrdinalIgnoreCase))
        {
            ReplyCatalogSearch(ctx, trimmed["catalog search ".Length..]);
            return true;
        }

        if (trimmed.Equals("create", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("create ", StringComparison.OrdinalIgnoreCase))
        {
            var createText = trimmed.Length == "create".Length
                ? ""
                : trimmed["create ".Length..];
            var createArgs = SplitCommandWords(createText);
            if (createArgs.Count == 0)
            {
                ctx.Reply("Usage: .ai create <eventId> [templateId]. Admin only; defaults to the Bloodbath template.");
                return true;
            }

            EventTemplateCommands.CreateFromTemplate(
                ctx,
                createArgs[0],
                createArgs.Count > 1 ? createArgs[1] : "bloodbath");
            return true;
        }

        if (trimmed.StartsWith("action ", StringComparison.OrdinalIgnoreCase))
        {
            ReplyLiveActionPreview(ctx, steamId, "manual live action", new[] { trimmed["action ".Length..].Trim() });
            return true;
        }

        if (trimmed.StartsWith("event ", StringComparison.OrdinalIgnoreCase))
        {
            var rest = trimmed["event ".Length..].Trim();
            if (rest.StartsWith("deploy ", StringComparison.OrdinalIgnoreCase))
            {
                var deployArgs = SplitCommandWords(rest["deploy ".Length..]);
                if (deployArgs.Count < 2)
                {
                    ctx.Reply("Usage: .ai event deploy <eventId> <https-gist-url>. Admin only; files are staged, validated, backed up, then registered.");
                    return true;
                }

                await EventDeploymentCommands.DeployFromGist(ctx, deployArgs[0], deployArgs[1], deployArgs.Skip(2).Any(word => word.Equals("--dry-run", StringComparison.OrdinalIgnoreCase) || word.Equals("dry-run", StringComparison.OrdinalIgnoreCase)));
                return true;
            }
            if (rest.Equals("status", StringComparison.OrdinalIgnoreCase) ||
                rest.StartsWith("status ", StringComparison.OrdinalIgnoreCase))
            {
                EventDeploymentCommands.Status(ctx, rest.Length == "status".Length ? "" : rest["status ".Length..].Trim());
                return true;
            }
            if (rest.Equals("audit", StringComparison.OrdinalIgnoreCase) ||
                rest.StartsWith("audit ", StringComparison.OrdinalIgnoreCase))
            {
                EventDeploymentCommands.AuditSummary(ctx, rest.Length == "audit".Length ? "" : rest["audit ".Length..].Trim());
                return true;
            }
            if (rest.StartsWith("review ", StringComparison.OrdinalIgnoreCase))
            {
                await ReplyEventReview(ctx, aiAssistant, steamId, rest["review ".Length..]);
                return true;
            }
            if (rest.Equals("review", StringComparison.OrdinalIgnoreCase))
            {
                await ReplyEventReview(ctx, aiAssistant, steamId, "");
                return true;
            }
            if (rest.StartsWith("request ", StringComparison.OrdinalIgnoreCase))
            {
                await ReplyEventRequest(ctx, aiAssistant, steamId, rest["request ".Length..]);
                return true;
            }
            if (rest.Equals("create", StringComparison.OrdinalIgnoreCase) ||
                rest.StartsWith("create ", StringComparison.OrdinalIgnoreCase))
            {
                var createText = rest.Length == "create".Length
                    ? ""
                    : rest["create ".Length..];
                var createArgs = SplitCommandWords(createText);
                if (createArgs.Count == 0)
                {
                    ctx.Reply("Usage: .ai event create <eventId> [templateId]. Admin only; defaults to Bloodbath.");
                    return true;
                }

                EventTemplateCommands.CreateFromTemplate(
                    ctx,
                    createArgs[0],
                    createArgs.Count > 1 ? createArgs[1] : "bloodbath");
                return true;
            }
            if (rest.StartsWith("preview ", StringComparison.OrdinalIgnoreCase))
            {
                ReplyEventPreview(ctx, rest["preview ".Length..].Trim());
                return true;
            }
            if (rest.StartsWith("approve ", StringComparison.OrdinalIgnoreCase))
            {
                ReplyEventApprove(ctx, steamId, rest["approve ".Length..].Trim());
                return true;
            }
            if (rest.Equals("approve", StringComparison.OrdinalIgnoreCase) ||
                rest.Equals("yes", StringComparison.OrdinalIgnoreCase) ||
                rest.Equals("ok", StringComparison.OrdinalIgnoreCase))
            {
                ReplyEventApprove(ctx, steamId, "");
                return true;
            }
            if (rest.StartsWith("rollback ", StringComparison.OrdinalIgnoreCase))
            {
                var rollbackTarget = rest["rollback ".Length..].Trim();
                if (LooksLikeDeploymentEventId(rollbackTarget))
                    EventDeploymentCommands.Rollback(ctx, rollbackTarget);
                else
                    ReplyEventRollback(ctx, steamId, rollbackTarget);
                return true;
            }
            if (rest.Equals("rollback", StringComparison.OrdinalIgnoreCase) ||
                rest.Equals("cancel", StringComparison.OrdinalIgnoreCase))
            {
                ReplyEventRollback(ctx, steamId, "");
                return true;
            }

            await ReplyEventRequest(ctx, aiAssistant, steamId, rest);
            return true;
        }

        if (trimmed.StartsWith("review event", StringComparison.OrdinalIgnoreCase))
        {
            await ReplyEventReview(ctx, aiAssistant, steamId, trimmed["review event".Length..]);
            return true;
        }

        if (LooksLikeEventAuthoringRequest(trimmed))
        {
            await ReplyEventRequest(ctx, aiAssistant, steamId, trimmed);
            return true;
        }

        return false;
    }

    static bool IsQuickApprove(string value)
    {
        var normalized = NormalizeSelector(value);
        return normalized is "approve" or "approved" or "yes" or "y" or "ok" or "okay" or "go" or "goahead" or "doit" or "run" or "execute" or "confirm";
    }

    static bool IsQuickRollback(string value)
    {
        var normalized = NormalizeSelector(value);
        return normalized is "rollback" or "cancel" or "no" or "n" or "stop" or "discard";
    }

    static bool LooksLikeDeploymentEventId(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.StartsWith("aio_", StringComparison.OrdinalIgnoreCase))
            return false;

        var trimmed = value.Trim();
        return trimmed.Length is >= 2 and <= 32 &&
            trimmed.All(ch => char.IsLetterOrDigit(ch) || ch is '_' or '-') &&
            char.IsLetterOrDigit(trimmed[0]);
    }

    static bool TryHandleKitAbilityEmptyCommand(ChatCommandContext ctx, string trimmed)
    {
        var lower = trimmed.ToLowerInvariant();
        var mentionsAbility = lower.Contains("abilit") || lower.Contains("spell slot") || lower.Contains("slot prefab");
        var wantsEmpty = lower.Contains("empty") || lower.Contains("clear") || lower.Contains("remove") || lower.Contains("blank");
        var mentionsKitOrPrefab = lower.Contains("kit") || lower.Contains("prefab") || lower.Contains("slot");
        var exceptAiEvent = lower.Contains("except ai event") || lower.Contains("except aievent") || lower.Contains("not aievent") || lower.Contains("keep aievent");

        if (!mentionsAbility || !wantsEmpty || !mentionsKitOrPrefab)
            return false;

        var modes = BattleLuckPlugin.GameModes?.GetRegisteredModes().ToList()
            ?? new List<string> { "bloodbath", "colosseum", "siege", "trials", "aievent" };

        if (exceptAiEvent)
            modes = modes.Where(m => !m.Equals("aievent", StringComparison.OrdinalIgnoreCase)).ToList();

        var changed = new List<string>();
        var alreadyEmpty = new List<string>();
        var errors = new List<string>();
        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        };

        foreach (var modeId in modes.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var path = Path.Combine(ConfigLoader.ConfigRoot, modeId, "kit.json");
            if (!File.Exists(path))
                continue;

            try
            {
                var json = File.ReadAllText(path);
                var node = JsonNode.Parse(json, documentOptions: new JsonDocumentOptions
                {
                    CommentHandling = JsonCommentHandling.Skip,
                    AllowTrailingCommas = true
                }) as JsonObject;

                if (node == null)
                {
                    errors.Add($"{modeId}: kit.json root is not an object");
                    continue;
                }

                if (node["abilities"] is JsonObject abilityObj && abilityObj.Count == 0)
                {
                    alreadyEmpty.Add(modeId);
                    continue;
                }

                var backupPath = $"{path}.{DateTime.UtcNow:yyyyMMddHHmmss}.bak";
                File.Copy(path, backupPath, overwrite: true);
                node["abilities"] = new JsonObject();
                File.WriteAllText(path, node.ToJsonString(options));
                ConfigLoader.Reload(modeId);
                changed.Add(modeId);
            }
            catch (Exception ex)
            {
                errors.Add($"{modeId}: {ex.Message}");
            }
        }

        KitController.ClearCache();

        if (changed.Count > 0)
            ctx.Reply(NotificationHelper.ColorizeText($"AI config edit applied: emptied ability slots for {string.Join(", ", changed)}. aievent kept={exceptAiEvent}.", "#47FF8A"));
        if (alreadyEmpty.Count > 0)
            ctx.Reply(NotificationHelper.ColorizeText($"Already empty: {string.Join(", ", alreadyEmpty)}.", "#5CC8FF"));
        if (errors.Count > 0)
            ctx.Reply(NotificationHelper.ColorizeText($"Kit ability edit errors: {TrimReply(string.Join("; ", errors), 260)}", "#FF5C7A"));

        if (changed.Count == 0 && alreadyEmpty.Count == 0 && errors.Count == 0)
            ctx.Reply(NotificationHelper.ColorizeText("No kit files matched this ability-slot edit request.", "#FFD166"));

        return true;
    }

    static bool TryHandleBossOperatorCommand(ChatCommandContext ctx, string trimmed)
    {
        var lower = trimmed.ToLowerInvariant();
        var bossIntent =
            lower.StartsWith("boss ", StringComparison.Ordinal) ||
            lower.StartsWith("move boss", StringComparison.Ordinal) ||
            lower.StartsWith("send boss", StringComparison.Ordinal) ||
            lower.StartsWith("return boss", StringComparison.Ordinal);

        if (!bossIntent)
            return false;

        var words = SplitCommandWords(trimmed);
        if (words.Count == 0)
            return false;

        string action;
        if (words[0].Equals("boss", StringComparison.OrdinalIgnoreCase))
        {
            var op = words.Count > 1 ? words[1].ToLowerInvariant() : "goto";
            var bossId = words.Count > 2 ? words[2] : "self";
            var extra = words.Count > 3 ? words[3] : "";
            action = op switch
            {
                "goto" or "move" or "pos" or "here" => BuildBossGotoAction(bossId, extra),
                "home" or "return" or "return_home" => $"boss.return_home:bossId={bossId}",
                "deaggro" or "stop" or "idle" => $"ai.boss.deaggro:bossId={bossId}",
                "aggro" or "attack" => $"ai.boss.aggro:bossId={bossId}|target={(string.IsNullOrWhiteSpace(extra) ? "self" : extra)}",
                "follow" => $"boss.follow:bossId={bossId}|target={(string.IsNullOrWhiteSpace(extra) ? "self" : extra)}",
                "behavior" or "behaviour" => $"ai.set_behavior:bossId={bossId}|behavior={(string.IsNullOrWhiteSpace(extra) ? "guard" : extra)}",
                _ => ""
            };
        }
        else if ((words[0].Equals("move", StringComparison.OrdinalIgnoreCase) ||
                  words[0].Equals("send", StringComparison.OrdinalIgnoreCase)) &&
                 words.Count >= 2 &&
                 words[1].StartsWith("boss", StringComparison.OrdinalIgnoreCase))
        {
            var bossId = words[1];
            var position = words.Count > 2 ? words[2] : "self";
            action = BuildBossGotoAction(bossId, position);
        }
        else if (words[0].Equals("return", StringComparison.OrdinalIgnoreCase) &&
                 words.Count >= 2 &&
                 words[1].StartsWith("boss", StringComparison.OrdinalIgnoreCase))
        {
            action = $"boss.return_home:bossId={words[1]}";
        }
        else
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(action))
        {
            ctx.Reply("Boss AI command not understood. Try: .ai boss goto boss1, .ai boss deaggro boss1, .ai boss behavior boss1 guard");
            return true;
        }

        ReplyLiveActionPreview(ctx, ctx.GetSenderCharacterEntity().GetSteamId(), "boss operator command", new[] { action });
        return true;
    }

    static string BuildBossGotoAction(string bossId, string position)
    {
        if (!string.IsNullOrWhiteSpace(position) &&
            !position.Equals("self", StringComparison.OrdinalIgnoreCase) &&
            !position.Equals("here", StringComparison.OrdinalIgnoreCase) &&
            position.Contains(','))
        {
            return $"boss.goto:bossId={bossId}|position={position}|arrivalRange=2|hold=true";
        }

        return $"boss.goto.pos:bossId={bossId}|arrivalRange=2|hold=true";
    }

    static OperationResult ExecuteAiActionInActiveSession(ChatCommandContext ctx, string actionString, string? sessionId = null)
    {
        actionString = BattleLuck.Commands.Converters.ActionParameterConverter.NormalizeActionString(actionString);
        var entity = ctx.GetSenderCharacterEntity();
        var steamId = entity.GetSteamId();
        ActiveSession? sessionForAction;
        if (!string.IsNullOrWhiteSpace(sessionId))
        {
            // A proposal is bound to the exact session that was present when it
            // was previewed. Never redirect a stale proposal to another arena.
            sessionForAction = BattleLuckPlugin.Session?.ActiveSessions?.Values
                .FirstOrDefault(s => s.Context?.SessionId.Equals(sessionId, StringComparison.OrdinalIgnoreCase) == true);
            if (sessionForAction?.Context == null)
                return OperationResult.Fail($"Requested session '{sessionId}' is no longer active; the action was not redirected to another session.");
        }
        else
        {
            // Keep the existing world/no-session behavior for actions that were
            // not previewed against a specific live event session.
            sessionForAction = BattleLuckPlugin.Session?.ActiveSessions?.Values.FirstOrDefault(s => s.Context?.Players?.Contains(steamId) == true)
                ?? BattleLuckPlugin.Session?.ActiveSessions?.Values.FirstOrDefault();
        }

        if (sessionForAction == null)
        {
            if (!actionString.TrimStart().StartsWith("clan.task.", StringComparison.OrdinalIgnoreCase))
                return OperationResult.Fail("No active session found for live action.");

            var standaloneState = new PlayerStateController();
            return new FlowActionExecutor(standaloneState, BattleLuckPlugin.GameModes).ExecuteViaRuntime(actionString, new FlowActionContext
            {
                PlayerCharacter = entity,
                PlayerState = standaloneState,
                Registry = BattleLuckPlugin.GameModes
            });
        }

        var playerState = new PlayerStateController();
        var executor = new FlowActionExecutor(playerState, BattleLuckPlugin.GameModes);
        var zone = sessionForAction.Config.Zones.Zones.FirstOrDefault(z => z.Hash == sessionForAction.Context.ZoneHash);
        var context = new FlowActionContext
        {
            PlayerCharacter = entity,
            ZoneHash = sessionForAction.Context.ZoneHash,
            PlayerState = playerState,
            Registry = BattleLuckPlugin.GameModes,
            Config = sessionForAction.Config,
            Zone = zone,
            GameContext = sessionForAction.Context
        };

        return executor.ExecuteViaRuntime(actionString, context);
    }

    static string BuildImmediateActionSolution(string actionString, string? error)
    {
        var manifest = new ActionManifestService();
        var (name, parameters) = FlowActionExecutor.ParseActionString(actionString ?? "");
        name = string.IsNullOrWhiteSpace(name) ? actionString ?? "" : name;

        var canonical = manifest.NormalizeActionName(name);
        if (!canonical.Equals(name, StringComparison.OrdinalIgnoreCase))
        {
            var suffix = parameters.Count == 0
                ? ""
                : ":" + string.Join("|", parameters.Select(kv => $"{kv.Key}={kv.Value}"));
            return $"Try canonical action: {canonical}{suffix}";
        }

        var matches = manifest.Search($"{name} {error}", 3);
        if (matches.Count == 0)
            matches = manifest.Search(name, 3);

        var suggestion = matches.SelectMany(m => m.Examples.DefaultIfEmpty(m.Name)).FirstOrDefault();
        return string.IsNullOrWhiteSpace(suggestion)
            ? $"Search valid replacements with: .ai catalog search {name}"
            : $"Immediate solution: .ai action {suggestion}";
    }

    static bool TryHandleSwapTeamOperatorCommand(ChatCommandContext ctx, string trimmed)
    {
        var prefixes = new[]
        {
            "swapteam",
            "swap team",
            "swap teams",
            "team swap",
            "balance teams",
            "make teams",
            "set teams"
        };

        foreach (var prefix in prefixes)
        {
            if (!trimmed.Equals(prefix, StringComparison.OrdinalIgnoreCase) &&
                !trimmed.StartsWith(prefix + " ", StringComparison.OrdinalIgnoreCase))
                continue;

            var rest = trimmed.Length == prefix.Length
                ? ""
                : trimmed[prefix.Length..].Trim();
            var args = SplitCommandWords(rest);
            if (args.Count == 1 &&
                (args[0].Equals("evenly", StringComparison.OrdinalIgnoreCase) ||
                 args[0].Equals("equal", StringComparison.OrdinalIgnoreCase)))
                args.Clear();

            HandleSwapTeam(ctx, args);
            return true;
        }

        return false;
    }

    static void ReplyCatalogSearch(ChatCommandContext ctx, string query)
    {
        var results = LiveOperator.SearchCatalog(query, 8);
        if (results.Count == 0)
        {
            ctx.Reply("No matching actions found in actions_catalog.json.");
            return;
        }

        ctx.Reply($"Catalog matches for '{query}':");
        foreach (var result in results)
        {
            var example = result.Examples.FirstOrDefault() ?? result.Name;
            ctx.Reply($"- {result.Name} [{result.Category}/{result.RiskLevel}] ex: {TrimReply(example, 90)}");
        }
    }

    static void ReplyLiveActionPreview(ChatCommandContext ctx, ulong steamId, string request, IEnumerable<string> actions)
    {
        var actionList = actions.Where(action => !string.IsNullOrWhiteSpace(action)).ToList();
        var worldScopedClanTask = actionList.Count > 0 &&
            actionList.All(action => action.TrimStart().StartsWith("clan.task.", StringComparison.OrdinalIgnoreCase));
        // Clan-task actions from a player outside an event are world-scoped. Do
        // not bind their proposal to an unrelated arena just because one exists.
        var activeSession = GetActiveSessionFor(steamId)
            ?? (worldScopedClanTask ? null : BattleLuckPlugin.Session?.ActiveSessions.Values.FirstOrDefault());
        if (activeSession?.Context == null)
        {
            if (!worldScopedClanTask)
            {
                ctx.Reply(NotificationHelper.ColorizeText("No active session found for live action approval.", "#FFD166"));
                return;
            }

            var worldResult = LiveOperator.PreviewLiveActions(steamId, "world", "", request, actionList);
            if (!worldResult.Success || worldResult.Value == null)
            {
                ctx.Reply(NotificationHelper.ColorizeText(worldResult.Error ?? "Clan task action preview failed.", "#FF5C7A"));
                return;
            }
            ctx.Reply(NotificationHelper.ColorizeText("Clan task action preview ready.", "#FFD166"));
            foreach (var action in worldResult.Value.Actions.Take(5))
                ctx.Reply(NotificationHelper.ColorizeText($"- {TrimReply(action, 110)}", "#D7E3FF"));
            ctx.Reply(NotificationHelper.ColorizeText("Reply `.ai approve` or `.ai yes` to run it.", "#C77DFF"));
            return;
        }

        var result = LiveOperator.PreviewLiveActions(
            steamId,
            activeSession.Context.ModeId,
            activeSession.Context.SessionId,
            request,
            actionList);

        if (!result.Success || result.Value == null)
        {
            ctx.Reply(NotificationHelper.ColorizeText(result.Error ?? "Live action preview failed.", "#FF5C7A"));
            foreach (var action in actionList.Take(2))
                ctx.Reply(NotificationHelper.ColorizeText(BuildImmediateActionSolution(action, result.Error), "#FFD166"));
            return;
        }

        var proposal = result.Value;
        ctx.Reply(NotificationHelper.ColorizeText("Live action preview ready.", "#FFD166"));
        ctx.Reply(NotificationHelper.ColorizeText($"Mode={proposal.ModeId} Risk={proposal.RiskLevel} Actions={proposal.Actions.Count}", "#D7E3FF"));
        foreach (var action in proposal.Actions.Take(5))
            ctx.Reply(NotificationHelper.ColorizeText($"- {TrimReply(action, 110)}", "#D7E3FF"));
        ctx.Reply(NotificationHelper.ColorizeText("Reply `.ai approve` or `.ai yes` to run it. Use `.ai rollback` to discard.", "#C77DFF"));
    }

    static async Task ReplyEventRequest(ChatCommandContext ctx, AIAssistant aiAssistant, ulong steamId, string request)
    {
        var (modeId, cleanRequest) = ResolveModeAndRequest(steamId, request);
        if (string.IsNullOrWhiteSpace(modeId))
        {
            ctx.Reply("No active event found. Use: .ai event request <modeId> <request>");
            return;
        }

        var activeSession = GetActiveSessionFor(steamId)
            ?? BattleLuckPlugin.Session?.ActiveSessions.Values.FirstOrDefault(s => s.Context.ModeId.Equals(modeId, StringComparison.OrdinalIgnoreCase));
        var result = await LiveOperator.PreviewEventRequestAsync(aiAssistant, steamId, modeId, cleanRequest, activeSession);
        if (!result.Success || result.Value == null)
        {
            ctx.Reply($"AI Event Preview failed: {result.Error}");
            return;
        }

        var proposal = result.Value;
        ctx.Reply(aiAssistant.FormatInGameResponse(cleanRequest, "AI Event Preview ready."));
        ctx.Reply(NotificationHelper.ColorizeText($"Mode={proposal.ModeId} Risk={proposal.RiskLevel} Actions={proposal.Actions.Count}", "#FFD166"));
        ctx.Reply(NotificationHelper.ColorizeText(TrimReply(proposal.JsonDiff, 220), "#D7E3FF"));
        ctx.Reply(NotificationHelper.ColorizeText("Reply `.ai approve` or `.ai yes` to apply it. Use `.ai rollback` to discard.", "#C77DFF"));
    }

    static async Task ReplyEventReview(ChatCommandContext ctx, AIAssistant aiAssistant, ulong steamId, string request)
    {
        var (modeId, cleanRequest) = ResolveModeAndRequest(steamId, request);
        if (string.IsNullOrWhiteSpace(modeId))
        {
            ctx.Reply("No active event found. Use: .ai event review <modeId> [focus]");
            return;
        }

        var activeSession = GetActiveSessionFor(steamId)
            ?? BattleLuckPlugin.Session?.ActiveSessions.Values.FirstOrDefault(s => s.Context.ModeId.Equals(modeId, StringComparison.OrdinalIgnoreCase));
        var result = await LiveOperator.ReviewEventAsync(aiAssistant, steamId, modeId, cleanRequest, activeSession);
        if (!result.Success || string.IsNullOrWhiteSpace(result.Value))
        {
            ctx.Reply($"AI Event Review failed: {result.Error}");
            return;
        }

        var response = result.Value.Replace('\r', ' ').Trim();
        foreach (var chunk in SplitReply(response, 420).Take(6))
            ctx.Reply(aiAssistant.FormatInGameResponse(cleanRequest, chunk));
    }

    static void ReplyEventPreview(ChatCommandContext ctx, string operationId)
    {
        var result = LiveOperator.GetProposal(operationId);
        if (!result.Success || result.Value == null)
        {
            ctx.Reply(result.Error ?? "Preview not found.");
            return;
        }

        var p = result.Value;
        ctx.Reply($"AI Operation {p.OperationId} ({p.Kind})");
        ctx.Reply($"Mode={p.ModeId} Session={p.SessionId} Risk={p.RiskLevel} Expires={p.ExpiresAtUtc:HH:mm:ss} UTC");
        ctx.Reply(TrimReply(p.Request, 220));
        ctx.Reply(TrimReply(p.JsonDiff, 220));
        foreach (var action in p.Actions.Take(8))
            ctx.Reply($"- {TrimReply(action, 110)}");
    }

    static void ReplyEventApprove(ChatCommandContext ctx, ulong steamId, string operationId)
    {
        var preview = string.IsNullOrWhiteSpace(operationId)
            ? LiveOperator.GetLatestProposalFor(steamId)
            : LiveOperator.GetProposal(operationId);
        if (!preview.Success || preview.Value == null)
        {
            ctx.Reply(preview.Error ?? "Approve failed.");
            return;
        }

        operationId = preview.Value.OperationId;
        var result = preview.Value.Kind.Equals("live_action", StringComparison.OrdinalIgnoreCase)
            ? LiveOperator.ApproveLiveActions(operationId, steamId, action => ExecuteAiActionInActiveSession(ctx, action, preview.Value.SessionId))
            : LiveOperator.Approve(operationId, steamId);

        if (!result.Success || result.Value == null)
        {
            ctx.Reply(result.Error ?? "Approve failed.");
            if (preview.Value.Kind.Equals("live_action", StringComparison.OrdinalIgnoreCase))
            {
                var failedAction = preview.Value.Actions.FirstOrDefault() ?? "";
                ctx.Reply(BuildImmediateActionSolution(failedAction, result.Error));
            }
            return;
        }

        ctx.Reply(result.Value.Kind.Equals("live_action", StringComparison.OrdinalIgnoreCase)
            ? $"AI live action approved and executed: {result.Value.ModeId} ({result.Value.OperationId})"
            : $"AI Event approved and reloaded: {result.Value.ModeId} ({result.Value.OperationId})");
    }

    static void ReplyEventRollback(ChatCommandContext ctx, ulong steamId, string operationId)
    {
        var preview = string.IsNullOrWhiteSpace(operationId)
            ? LiveOperator.GetLatestProposalFor(steamId)
            : LiveOperator.GetProposal(operationId);
        if (!preview.Success || preview.Value == null)
        {
            ctx.Reply(preview.Error ?? "Rollback failed.");
            return;
        }

        var result = LiveOperator.Rollback(preview.Value.OperationId, steamId);
        if (!result.Success || result.Value == null)
        {
            ctx.Reply(result.Error ?? "Rollback failed.");
            return;
        }

        ctx.Reply(result.Value.Kind.Equals("live_action", StringComparison.OrdinalIgnoreCase)
            ? $"AI live action discarded: {result.Value.ModeId}"
            : $"AI Event rolled back and reloaded: {result.Value.ModeId}");
    }

    static (string modeId, string request) ResolveModeAndRequest(ulong steamId, string request)
    {
        var activeSession = GetActiveSessionFor(steamId);
        var knownModes = BattleLuckPlugin.GameModes?.GetRegisteredModes().ToHashSet(StringComparer.OrdinalIgnoreCase)
            ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var trimmed = request.Trim();
        foreach (var mode in knownModes.OrderByDescending(value => value.Length))
        {
            var displayName = BattleLuckPlugin.GameModes?.Resolve(mode)?.DisplayName ?? mode;
            if (!ContainsWholeReference(trimmed, mode) &&
                !ContainsWholeReference(trimmed, displayName) &&
                !(mode.Equals("aievent", StringComparison.OrdinalIgnoreCase) && ContainsWholeReference(trimmed, "ai event")))
                continue;

            // Preserve natural-language requests when the mode appears later
            // ("move X from bloodbath to Y"). Only strip a leading mode token,
            // keeping compatibility with `.ai event request bloodbath ...`.
            var leading = new[] { mode, displayName }
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .OrderByDescending(value => value.Length)
                .FirstOrDefault(value => trimmed.StartsWith(value, StringComparison.OrdinalIgnoreCase) &&
                    (trimmed.Length == value.Length || !char.IsLetterOrDigit(trimmed[value.Length])));
            if (!string.IsNullOrWhiteSpace(leading))
            {
                var clean = trimmed[leading.Length..].TrimStart(' ', '\t', ':', '-', ',');
                return (mode, clean);
            }

            return (mode, trimmed);
        }

        return (activeSession?.Context.ModeId ?? "", trimmed);
    }

    static bool IsAdminSender(ChatCommandContext ctx)
    {
        var userEntity = ctx.GetSenderCharacterEntity().GetUserEntity();
        return userEntity.Exists() && userEntity.TryGetComponent(out User user) && user.IsAdmin;
    }

    static bool ContainsWholeReference(string text, string value)
    {
        if (string.IsNullOrWhiteSpace(text) || string.IsNullOrWhiteSpace(value))
            return false;

        var start = 0;
        while (start < text.Length)
        {
            var index = text.IndexOf(value, start, StringComparison.OrdinalIgnoreCase);
            if (index < 0)
                return false;

            var beforeOk = index == 0 || !char.IsLetterOrDigit(text[index - 1]);
            var end = index + value.Length;
            var afterOk = end == text.Length || !char.IsLetterOrDigit(text[end]);
            if (beforeOk && afterOk)
                return true;

            start = index + 1;
        }

        return false;
    }

    static ActiveSession? GetActiveSessionFor(ulong steamId) =>
        BattleLuckPlugin.Session?.ActiveSessions?.Values
            .FirstOrDefault(s => s.Context?.Players?.Contains(steamId) == true);

    static bool LooksLikeEventAuthoringRequest(string query)
    {
        var editVerbs = new[]
        {
            "add", "change", "create", "remove", "update", "spawn", "place", "build",
            "make", "set", "configure", "edit", "delete", "cleanup", "move", "control", "order"
        };
        var eventTerms = new[]
        {
            "event", "action", "actions",
            "boss", "wall", "walls", "zone", "zones", "glow", "glows", "kit", "kits",
            "trigger", "phase", "servant", "trap", "platform", "timer", "gate",
            "schematic", "object", "objects", "floor", "floors", "door", "doors"
        };
        return editVerbs.Any(term => query.Contains(term, StringComparison.OrdinalIgnoreCase)) &&
               eventTerms.Any(term => query.Contains(term, StringComparison.OrdinalIgnoreCase));
    }

    static AiProjectOrderResult ParseAiProjectOrder(string projectId, string response)
    {
        try
        {
            var json = ExtractJsonObject(response);
            var result = JsonSerializer.Deserialize<AiProjectOrderResult>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (result != null)
            {
                result.ProjectId = string.IsNullOrWhiteSpace(result.ProjectId) ? projectId : result.ProjectId.Trim();
                result.Summary = TrimReply(result.Summary, 900);
                result.Risk = NormalizeRisk(result.Risk);
                result.RecommendedActions = result.RecommendedActions
                    .Where(a => !string.IsNullOrWhiteSpace(a))
                    .Select(a => a.Trim())
                    .Take(12)
                    .ToList();
                return result;
            }
        }
        catch (Exception ex)
        {
            BattleLuckPlugin.LogWarning($"AI project order parse failed: {ex.Message}");
        }

        return new AiProjectOrderResult
        {
            ProjectId = projectId,
            Summary = TrimReply(response, 900),
            Risk = "medium"
        };
    }

    static string ExtractJsonObject(string text)
    {
        var start = text.IndexOf('{');
        var end = text.LastIndexOf('}');
        if (start < 0 || end <= start)
            return text;
        return text[start..(end + 1)];
    }

    static string NormalizeRisk(string? risk)
    {
        var value = (risk ?? "low").Trim().ToLowerInvariant();
        return value is "low" or "medium" or "high" ? value : "medium";
    }

    static AiActionModernizationReview ParseActionModernizationReview(string response)
    {
        try
        {
            var json = ExtractJsonObject(response);
            var result = JsonSerializer.Deserialize<AiActionModernizationReview>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (result != null)
            {
                result.Summary = TrimReply(result.Summary, 900);
                result.CanonicalActions = CleanReviewList(result.CanonicalActions);
                result.LegacyActions = CleanReviewList(result.LegacyActions);
                result.LlmRecommendations = CleanReviewList(result.LlmRecommendations);
                result.ConfigPolicySuggestions = CleanReviewList(result.ConfigPolicySuggestions);
                return result;
            }
        }
        catch (Exception ex)
        {
            BattleLuckPlugin.LogWarning($"AI action modernization review parse failed: {ex.Message}");
        }

        return new AiActionModernizationReview
        {
            Summary = TrimReply(response, 900)
        };
    }

    static List<string> CleanReviewList(IEnumerable<string>? values) =>
        (values ?? Array.Empty<string>())
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Select(v => TrimReply(v.Trim(), 220))
            .Take(12)
            .ToList();

    static string TrimReply(string value, int max)
    {
        if (string.IsNullOrWhiteSpace(value)) return "";
        var oneLine = value.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return oneLine.Length <= max ? oneLine : oneLine[..Math.Max(1, max - 3)] + "...";
    }

    static IEnumerable<string> SplitReply(string value, int max)
    {
        if (string.IsNullOrWhiteSpace(value))
            yield break;

        var normalized = value.Replace("\r\n", "\n").Replace('\r', '\n').Trim();
        while (normalized.Length > max)
        {
            var split = normalized.LastIndexOf('\n', Math.Min(max, normalized.Length - 1));
            if (split < max / 2)
                split = normalized.LastIndexOf(' ', Math.Min(max, normalized.Length - 1));
            if (split < max / 2)
                split = max;

            yield return normalized[..split].Trim();
            normalized = normalized[split..].Trim();
        }

        if (normalized.Length > 0)
            yield return normalized;
    }

    static string JoinCommandWords(params string[] words)
        => string.Join(" ", words.Where(w => !string.IsNullOrWhiteSpace(w))).Trim();

    [Command("aistatus", description: "Read-only AI provider/runtime status (public)")]
    public static void AIStatus(ChatCommandContext ctx)
    {
        var aiAssistant = BattleLuckPlugin.AIAssistant;
        if (aiAssistant == null)
        {
            ctx.Reply("AI Assistant is not initialized.");
            return;
        }

        var status = aiAssistant.IsEnabled ? "Enabled" : "Disabled";
        ctx.Reply($"🤖 AI Assistant Status: {status}");
        ctx.Reply("Process: players may ask .ai for advice; only admins can create previews or approve live changes.");
        ctx.Reply("Live change flow: catalog/search → preview → admin approval → main-thread execution → rollback/discard if still pending.");
        ctx.Reply("Public/read-only: .ai <question>, .aistatus");
        ctx.Reply("Admin event flow: .ai create <eventId> [templateId] (clone), .ai event deploy <eventId> <https-gist-url>, .ai event request/review, .ai event preview, .ai approve, .ai rollback");
        ctx.Reply("Public deployment status: .ai event status [eventId]. Admin recovery: .ai event rollback <eventId> restores the latest known-good file backup; .ai rollback player <name|steamId> <timestamp|runId> restores one exact event snapshot; .ai rollback server players confirm restores all online event snapshots. Admin learning: .ai event audit [eventId].");
        ctx.Reply("Admin runtime actions: .ai catalog search <text>, .ai action <catalog action>, then .ai approve to execute");
        ctx.Reply("Admin system references: .ai action system.search/system.find, then system.register for a verified ProjectM/Unity alias");
        ctx.Reply("Admin developer tools: .ai.sequence.create/gather/preview/show/list/add/delete/execute; use wait:<seconds> and tick:<event-second> markers");
        ctx.Reply("Conversation: .ai <question> opens up to four replies; .ai end closes it; .ai history [items] shows one-day items; .ai tasks [goal] uses the planner (creation is admin-only)");
        ctx.Reply("Admin status: .ai.actions.review, .ai.project.list/order, .director [modeId]");
        ctx.Reply("Runtime boundary: cataloged actions and sequences run through BattleLuck validators and the server main-thread dispatcher; system.* references are verified aliases, not arbitrary native invocation.");
        
        if (aiAssistant.IsEnabled)
        {
            var aiConfig = ConfigLoader.LoadAIConfig();
            ctx.Reply($"Provider: requested={aiAssistant.Provider}, active={aiAssistant.ActiveProvider}");
            if (aiAssistant.Provider.Equals("llama", StringComparison.OrdinalIgnoreCase) ||
                aiAssistant.Provider.Equals("llama_api", StringComparison.OrdinalIgnoreCase) ||
                aiAssistant.Provider.Equals("meta_llama", StringComparison.OrdinalIgnoreCase))
            {
                ctx.Reply($"Local Llama: {aiConfig.LlamaAPI.BaseUrl} ({aiConfig.LlamaAPI.Model})");
                ctx.Reply("Old providers: Google/Cloudflare/sidecar disabled for Llama-only mode");
            }
            else if (aiAssistant.Provider.Equals("qwen", StringComparison.OrdinalIgnoreCase) ||
                aiAssistant.Provider.Equals("cloudflare_qwen", StringComparison.OrdinalIgnoreCase) ||
                aiAssistant.Provider.Equals("qwen_cloudflare", StringComparison.OrdinalIgnoreCase) ||
                aiAssistant.Provider.Equals("cloudflare", StringComparison.OrdinalIgnoreCase) ||
                aiAssistant.Provider.Equals("cloudflare_ai", StringComparison.OrdinalIgnoreCase) ||
                aiAssistant.Provider.Equals("workers_ai", StringComparison.OrdinalIgnoreCase) ||
                aiAssistant.Provider.Equals("workers-ai", StringComparison.OrdinalIgnoreCase))
            {
                ctx.Reply($"Cloudflare AI: {aiConfig.CloudflareAI.Model}");
            }
            ctx.Reply($"Provider status: {aiAssistant.ProviderStatus}");
            if (aiAssistant.DisabledProviders.Count > 0)
                ctx.Reply($"Disabled providers: {string.Join("; ", aiAssistant.DisabledProviders)}");
            ctx.Reply($"Event authoring: {(aiAssistant.EventAuthoringEnabled ? "enabled" : "disabled")}, max actions={aiAssistant.EventAuthoringMaxActions}");
            var groupBridge = BattleLuckPlugin.AiGroupProjectMBridge;
            ctx.Reply($"ProjectM AiGroup LLM: {(groupBridge != null ? "wired" : "not wired")}, auto-execute={(groupBridge?.AutoExecuteEnabled == true ? "on" : "off")}, directives={groupBridge?.DirectiveCount ?? 0}");
            var colors = aiConfig.Messaging.AiColors;
            ctx.Reply($"AI colors: {(colors.Enabled ? "enabled" : "disabled")} good=<color={colors.Good}>green</color> info=<color={colors.Info}>blue</color> event=<color={colors.Event}>event</color> admin=<color={colors.Admin}>admin</color>");
            ctx.Reply($"Catalog actions: {LiveOperator.SearchCatalog("", 1000).Count}; pending ops: {LiveOperator.PendingOperations.Count}");
            if (aiAssistant.IsSidecarConfigured)
                ctx.Reply($"Sidecar: {aiAssistant.SidecarBaseUrl}");
            ctx.Reply($"MCP: {(aiAssistant.IsMCPRuntimeHealthy ? "healthy" : "not active")} ({aiAssistant.MCPServerCount} server(s))");
            ctx.Reply("• Game mode strategies • Commands help • Performance advice • ELO improvement");
        }
    }

    [Command("ai.group.status", description: "Show ProjectM AiGroup LLM bridge status", adminOnly: true)]
    public static void AIGroupStatus(ChatCommandContext ctx)
    {
        var bridge = BattleLuckPlugin.AiGroupProjectMBridge;
        if (bridge == null)
        {
            ctx.Reply("ProjectM AiGroup LLM bridge is not initialized. Check ai_config.json and run .ai.reload.");
            return;
        }

        ctx.Reply($"ProjectM AiGroup LLM: autoExecute={(bridge.AutoExecuteEnabled ? "ON" : "OFF")}, directives={bridge.DirectiveCount}, executed={bridge.ExecutedCount}, skipped={bridge.SkippedExecutionCount}");
        ctx.Reply($"Last snapshot: {(bridge.LastSnapshot == null ? "none" : $"{bridge.LastSnapshot.SourceSystem} units={bridge.LastSnapshot.Units.Count}/{bridge.LastSnapshot.AggroConsumerCount} players={bridge.LastSnapshot.OnlinePlayerCount}")}");
        if (bridge.LastDirective != null)
        {
            var d = bridge.LastDirective;
            ctx.Reply($"Last directive: {d.Directive} confidence={d.Confidence:F2} target={TrimReply(d.Target, 80)}");
            ctx.Reply($"Reason: {TrimReply(d.Reason, 220)}");
            if (!string.IsNullOrWhiteSpace(d.Action))
                ctx.Reply($"Action: {TrimReply(d.Action, 220)}");
        }
        if (!string.IsNullOrWhiteSpace(bridge.LastExecutionResult))
            ctx.Reply($"Execution: {TrimReply(bridge.LastExecutionResult, 220)}");
        if (!string.IsNullOrWhiteSpace(bridge.LastError))
            ctx.Reply($"Last error: {TrimReply(bridge.LastError, 220)}");
    }

    [Command("ai.group.auto", description: "Toggle ProjectM AiGroup LLM auto-execution. Usage: .ai.group.auto <true|false>", adminOnly: true)]
    public static void AIGroupAuto(ChatCommandContext ctx, bool enabled = false)
    {
        var bridge = BattleLuckPlugin.AiGroupProjectMBridge;
        if (bridge == null)
        {
            ctx.Reply("ProjectM AiGroup LLM bridge is not initialized.");
            return;
        }

        bridge.SetAutoExecute(enabled);
        ctx.Reply($"ProjectM AiGroup LLM auto-execute is now {(enabled ? "ON" : "OFF")}.");
        if (enabled)
            ctx.Reply("Only allow-listed actions execute: announce, notification, npc.*, ai.boss.*, boss.goto/return_home.");
    }

    [Command("ai.group.policy", description: "Show ProjectM AiGroup per-action policy. Usage: .ai.group.policy [action]", adminOnly: true)]
    public static void AIGroupPolicy(ChatCommandContext ctx, string action = "")
    {
        var settings = ConfigLoader.LoadAIConfig().ProjectMAiGroup;
        ctx.Reply($"ProjectM AiGroup config: enabled={settings.Enabled}, autoExecute={settings.AutoExecute}, interval={settings.SnapshotIntervalSeconds}s, minConfidence={settings.MinConfidence:F2}");
        ctx.Reply($"Allowed actions: {string.Join(", ", settings.AllowedActions.Take(16))}");

        var policies = settings.ActionPolicies;
        if (!string.IsNullOrWhiteSpace(action))
        {
            var match = policies.FirstOrDefault(kv => kv.Key.Equals(action, StringComparison.OrdinalIgnoreCase));
            if (string.IsNullOrWhiteSpace(match.Key))
            {
                ctx.Reply($"No explicit policy for '{action}'.");
                return;
            }

            ReplyPolicy(ctx, match.Key, match.Value);
            return;
        }

        foreach (var kvp in policies.OrderBy(k => k.Key, StringComparer.OrdinalIgnoreCase).Take(12))
            ReplyPolicy(ctx, kvp.Key, kvp.Value);
    }

    static void ReplyPolicy(ChatCommandContext ctx, string name, ProjectMAiGroupActionPolicy policy)
    {
        ctx.Reply($"{name}: enabled={policy.Enabled}, auto={policy.AutoExecute}, min={policy.MinConfidence:F2}, cooldown={policy.CooldownSeconds}s, requireSession={policy.RequireActiveSession}");
    }

    [Command("ai.actions.review", description: "Ask the LLM to review legacy/old actions and canonical mappings", adminOnly: true)]
    public static async Task AIActionsReview(
        ChatCommandContext ctx,
        string focus = "",
        string a2 = "",
        string a3 = "",
        string a4 = "",
        string a5 = "",
        string a6 = "",
        string a7 = "",
        string a8 = "",
        string a9 = "",
        string a10 = "")
    {
        var aiAssistant = BattleLuckPlugin.AIAssistant;
        if (aiAssistant == null)
        {
            ctx.Reply("AI Assistant is not initialized. Run .ai.reload after configuring ai_config.json.");
            return;
        }

        var response = await aiAssistant.GenerateActionModernizationReviewAsync(JoinCommandWords(focus, a2, a3, a4, a5, a6, a7, a8, a9, a10));
        if (string.IsNullOrWhiteSpace(response))
        {
            ctx.Reply("AI action review returned no result.");
            return;
        }

        var review = ParseActionModernizationReview(response);
        foreach (var chunk in SplitReply(review.Summary, 420).Take(3))
            ctx.Reply(chunk);

        ReplyReviewSection(ctx, "Canonical", review.CanonicalActions);
        ReplyReviewSection(ctx, "Legacy", review.LegacyActions);
        ReplyReviewSection(ctx, "LLM", review.LlmRecommendations);
        ReplyReviewSection(ctx, "Policy", review.ConfigPolicySuggestions);
    }

    static void ReplyReviewSection(ChatCommandContext ctx, string label, IReadOnlyList<string> values)
    {
        if (values.Count == 0)
            return;

        ctx.Reply($"{label}:");
        foreach (var value in values.Take(6))
            ctx.Reply($"- {value}");
    }

    [Command("ai.group.ask", description: "Ask the LLM for a ProjectM AiGroup directive now", adminOnly: true)]
    public static async Task AIGroupAsk(
        ChatCommandContext ctx,
        string focus = "",
        string a2 = "",
        string a3 = "",
        string a4 = "",
        string a5 = "",
        string a6 = "",
        string a7 = "",
        string a8 = "",
        string a9 = "",
        string a10 = "")
    {
        var bridge = BattleLuckPlugin.AiGroupProjectMBridge;
        if (bridge == null)
        {
            ctx.Reply("ProjectM AiGroup LLM bridge is not initialized.");
            return;
        }

        var directive = await bridge.RequestDirectiveAsync(JoinCommandWords(focus, a2, a3, a4, a5, a6, a7, a8, a9, a10));
        if (directive == null)
        {
            ctx.Reply($"No directive returned. {TrimReply(bridge.LastError, 220)}");
            return;
        }

        ctx.Reply($"Directive: {directive.Directive} confidence={directive.Confidence:F2}");
        ctx.Reply($"Reason: {TrimReply(directive.Reason, 360)}");
        if (!string.IsNullOrWhiteSpace(directive.Action))
            ctx.Reply($"Action: {TrimReply(directive.Action, 300)}");
        if (!string.IsNullOrWhiteSpace(bridge.LastExecutionResult))
            ctx.Reply($"Execution: {TrimReply(bridge.LastExecutionResult, 220)}");
    }

    [Command("ai.project.list", description: "List available AI projects", adminOnly: true)]
    public static void AIProjectList(ChatCommandContext ctx)
    {
        ctx.Reply("📋 Available AI Projects:");
        ctx.Reply("  boss_aggro — Configure boss aggression patterns");
        ctx.Reply("  spawn_waves — Configure spawn wave patterns");
        ctx.Reply("  difficulty_scaling — Configure dynamic difficulty");
        ctx.Reply("  player_tracking — Configure player behavior tracking");
        ctx.Reply("  loot_distribution — Configure loot drop patterns");
        ctx.Reply("\nUse: ai.project.order <projectId> <action> to execute project actions");
    }

    [Command("ai.project.order", description: "Order AI project action", adminOnly: true)]
    public static async Task AIProjectOrder(
        ChatCommandContext ctx,
        string projectId,
        string action = "plan",
        string p1 = "",
        string p2 = "",
        string p3 = "",
        string p4 = "",
        string p5 = "",
        string p6 = "",
        string p7 = "",
        string p8 = "",
        string p9 = "",
        string p10 = "")
    {
        var aiAssistant = BattleLuckPlugin.AIAssistant;
        if (aiAssistant == null)
        {
            ctx.Reply("AI Assistant is not initialized. Run .ai.reload after configuring ai_config.json.");
            return;
        }

        var details = JoinCommandWords(p1, p2, p3, p4, p5, p6, p7, p8, p9, p10);
        var response = await aiAssistant.GenerateAiProjectOrderAsync(projectId, action, details);
        if (string.IsNullOrWhiteSpace(response))
        {
            ctx.Reply($"AI project planner returned no result for {projectId}.{action}.");
            return;
        }

        var order = ParseAiProjectOrder(projectId, response);
        ctx.Reply($"AI Project: {order.ProjectId} risk={order.Risk}");
        foreach (var chunk in SplitReply(order.Summary, 420).Take(3))
            ctx.Reply(chunk);
        if (order.RecommendedActions.Count == 0)
        {
            ctx.Reply("No concrete actions recommended.");
            return;
        }

        ctx.Reply("Recommended actions:");
        foreach (var recommended in order.RecommendedActions.Take(8))
            ctx.Reply($"- {TrimReply(recommended, 150)}");
    }

    [Command("ai.sequence.create", description: "Create custom action sequence", adminOnly: true)]
    public static void AISequenceCreate(ChatCommandContext ctx, string name, string steps = "")
    {
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(steps))
        {
            ctx.Reply("Usage: .ai.sequence.create <name> <action; wait:5; tick:30; action>");
            ctx.Reply("Example: .ai.sequence.create arena_intro announce:message=Get ready|color=#5CC8FF; wait:10; spawn.boss:prefab=CHAR_Manticore_VBlood|bossId=boss1");
            return;
        }

        var steamId = ctx.GetSenderCharacterEntity().GetSteamId();
        var active = GetActiveSessionFor(steamId);
        var result = CustomSequences.UpsertFromText(name, steps, steamId, active?.Context?.ModeId ?? "");
        if (!result.Success || result.Value == null)
        {
            ctx.Reply($"Sequence create failed: {result.Error}");
            return;
        }

        ctx.Reply($"Sequence '{result.Value.Id}' saved: {result.Value.EnabledActionCount} action(s), {result.Value.Steps.Count} step(s).");
        foreach (var chunk in SplitReply(CustomSequences.RenderPreview(result.Value), 420).Take(3))
            ctx.Reply(chunk);
    }

    [Command("ai.sequence.list", description: "List custom sequences", adminOnly: true)]
    public static void AISequenceList(ChatCommandContext ctx)
    {
        var sequences = CustomSequences.List();
        if (sequences.Count == 0)
        {
            ctx.Reply("Custom sequences: none yet. Use .ai.sequence.create <name> <steps>.");
            return;
        }

        ctx.Reply($"Custom sequences ({sequences.Count}):");
        foreach (var seq in sequences.Take(20))
            ctx.Reply($"- {seq.Id}: {seq.Actions} action(s), {seq.Steps} step(s), timing={(seq.HasTiming ? "yes" : "no")}, risk={seq.RiskLevel}");
    }

    [Command("ai.sequence.show", description: "Show a custom action sequence", adminOnly: true)]
    public static void AISequenceShow(ChatCommandContext ctx, string name)
    {
        var result = CustomSequences.Get(name);
        if (!result.Success || result.Value == null)
        {
            ctx.Reply(result.Error ?? "Sequence not found.");
            return;
        }

        foreach (var chunk in SplitReply(CustomSequences.RenderPreview(result.Value, 20), 420).Take(6))
            ctx.Reply(chunk);
    }

    [Command("ai.sequence.add", description: "Append actions/timing steps to a custom sequence", adminOnly: true)]
    public static void AISequenceAdd(ChatCommandContext ctx, string name, string steps = "")
    {
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(steps))
        {
            ctx.Reply("Usage: .ai.sequence.add <name> <action; wait:5; tick:30; action>");
            return;
        }

        var result = CustomSequences.AppendFromText(name, steps);
        if (!result.Success || result.Value == null)
        {
            ctx.Reply($"Sequence append failed: {result.Error}");
            return;
        }

        ctx.Reply($"Sequence '{result.Value.Id}' updated: {result.Value.EnabledActionCount} action(s), {result.Value.Steps.Count} step(s).");
    }

    [Command("ai.sequence.gather", description: "Create a sequence by gathering matching catalog actions", adminOnly: true)]
    public static void AISequenceGather(ChatCommandContext ctx, string name, string request = "")
    {
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(request))
        {
            ctx.Reply("Usage: .ai.sequence.gather <name> <catalog search/request>");
            ctx.Reply("Example: .ai.sequence.gather boss_intro boss glow timer wait:5");
            return;
        }

        var steamId = ctx.GetSenderCharacterEntity().GetSteamId();
        var active = GetActiveSessionFor(steamId);
        var result = CustomSequences.GatherFromCatalog(name, request, steamId, active?.Context?.ModeId ?? "");
        if (!result.Success || result.Value == null)
        {
            ctx.Reply($"Sequence gather failed: {result.Error}");
            return;
        }

        ctx.Reply($"Sequence '{result.Value.Id}' gathered from catalog: {result.Value.EnabledActionCount} action(s), {result.Value.Steps.Count} step(s).");
        foreach (var chunk in SplitReply(CustomSequences.RenderPreview(result.Value), 420).Take(4))
            ctx.Reply(chunk);
    }

    [Command("ai.sequence.preview", description: "Preview unsaved sequence steps", adminOnly: true)]
    public static void AISequencePreview(ChatCommandContext ctx, string name, string steps = "")
    {
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(steps))
        {
            ctx.Reply("Usage: .ai.sequence.preview <name> <action; wait:5; tick:30; action>");
            return;
        }

        var sequence = new CustomSequenceDefinition
        {
            Id = name,
            DisplayName = name,
            Steps = CustomSequences.ParseSteps(steps)
        };

        var validation = CustomSequences.Validate(sequence);
        ctx.Reply(validation.Success
            ? $"Sequence preview valid: {sequence.EnabledActionCount} action(s), {sequence.Steps.Count} step(s)."
            : $"Sequence preview invalid: {validation.Error}");
        foreach (var chunk in SplitReply(CustomSequences.RenderPreview(sequence), 420).Take(4))
            ctx.Reply(chunk);
    }

    [Command("ai.sequence.execute", description: "Execute custom sequence", adminOnly: true)]
    public static void AISequenceExecute(ChatCommandContext ctx, string name)
    {
        var entity = ctx.GetSenderCharacterEntity();
        var steamId = entity.GetSteamId();
        var session = GetActiveSessionFor(steamId)
            ?? BattleLuckPlugin.Session?.ActiveSessions?.Values.FirstOrDefault();

        if (session?.Context == null)
        {
            ctx.Reply("No active session found. Custom sequence execution needs a live event context.");
            return;
        }

        var playerState = new PlayerStateController();
        var executor = new FlowActionExecutor(playerState, BattleLuckPlugin.GameModes);
        var zone = session.Config.Zones.Zones.FirstOrDefault(z => z.Hash == session.Context.ZoneHash)
                   ?? new ZoneDefinition { Hash = session.Context.ZoneHash };
        var context = new FlowActionContext
        {
            PlayerCharacter = entity,
            ZoneHash = session.Context.ZoneHash,
            PlayerState = playerState,
            Registry = BattleLuckPlugin.GameModes,
            Config = session.Config,
            Zone = zone,
            GameContext = session.Context
        };

        var result = CustomSequences.ExecuteImmediate(name, executor, context);
        if (!result.Success || result.Value == null)
        {
            ctx.Reply($"Sequence execute failed: {result.Error}");
            return;
        }

        ctx.Reply($"Sequence '{result.Value.SequenceId}' executed: {result.Value.Executed} action(s), failed={result.Value.Failed}, timing markers skipped={result.Value.SkippedTimingMarkers}.");
    }

    [Command("ai.sequence.delete", description: "Delete custom sequence", adminOnly: true)]
    public static void AISequenceDelete(ChatCommandContext ctx, string name)
    {
        var result = CustomSequences.Delete(name);
        ctx.Reply(result.Success ? $"Sequence '{name}' deleted." : $"Sequence delete failed: {result.Error}");
    }

    [Command("sequence.run", description: "Run custom sequence shortcut", adminOnly: true)]
    public static void SequenceRun(ChatCommandContext ctx, string name) => AISequenceExecute(ctx, name);

    [Command("sequence.list", description: "List custom sequences shortcut", adminOnly: true)]
    public static void SequenceList(ChatCommandContext ctx) => AISequenceList(ctx);

    [Command("sequence.show", description: "Show custom sequence shortcut", adminOnly: true)]
    public static void SequenceShow(ChatCommandContext ctx, string name) => AISequenceShow(ctx, name);

    [Command("sequence.create", description: "Create custom sequence shortcut", adminOnly: true)]
    public static void SequenceCreate(ChatCommandContext ctx, string name, string steps = "") => AISequenceCreate(ctx, name, steps);

    [Command("sequence.add", description: "Append custom sequence shortcut", adminOnly: true)]
    public static void SequenceAdd(ChatCommandContext ctx, string name, string steps = "") => AISequenceAdd(ctx, name, steps);

    [Command("sequence.gather", description: "Gather catalog actions into custom sequence shortcut", adminOnly: true)]
    public static void SequenceGather(ChatCommandContext ctx, string name, string request = "") => AISequenceGather(ctx, name, request);

    [Command("sequence.preview", description: "Preview unsaved custom sequence shortcut", adminOnly: true)]
    public static void SequencePreview(ChatCommandContext ctx, string name, string steps = "") => AISequencePreview(ctx, name, steps);

    [Command("sequence.delete", description: "Delete custom sequence shortcut", adminOnly: true)]
    public static void SequenceDelete(ChatCommandContext ctx, string name) => AISequenceDelete(ctx, name);

    [Command("actions", description: "Show valid actions for the current mode")]
    public static void ShowActions(ChatCommandContext ctx, string modeId = "")
    {
        if (string.IsNullOrEmpty(modeId))
        {
            var session = BattleLuckPlugin.Session;
            var steamId = ctx.GetSenderCharacterEntity().GetSteamId();
            if (session != null)
            {
                foreach (var kv in session.ActiveSessions)
                {
                    if (kv.Value.Context.Players.Contains(steamId))
                    {
                        modeId = kv.Value.Context.ModeId;
                        break;
                    }
                }
            }
        }

        if (string.IsNullOrEmpty(modeId))
        {
            ctx.Reply("Specify a mode: .actions <modeId>");
            return;
        }

        var actions = ActionModelsRegistry.GetActionsForMode(modeId).ToList();
        if (actions.Count == 0)
        {
            ctx.Reply($"No actions defined for mode '{modeId}'.");
            return;
        }

        ctx.Reply($"Actions for {modeId}:");
        foreach (var action in actions)
        {
            var info = ActionModelsRegistry.Actions[action];
            var pts = info.DefaultPoints > 0 ? $" (+{info.DefaultPoints} pts)" : "";
            ctx.Reply($"  {info.ColoredLabel}{pts}");
        }
    }

    [Command("ai.list.prefabs", description: "List known prefab names", adminOnly: true)]
    public static void AIListPrefabs(ChatCommandContext ctx)
    {
        var names = AIAssistant.GetKnownPrefabNames().Take(200).ToList();
        ctx.Reply($"Known prefabs ({names.Count}):");
        foreach (var name in names)
            ctx.Reply($"  {name}");
    }

    [Command("ai.list.buffs", description: "List known buff prefabs", adminOnly: true)]
    public static void AIListBuffs(ChatCommandContext ctx)
    {
        var names = AIAssistant.GetKnownBuffPrefabNames().Take(200).ToList();
        ctx.Reply($"Known buff prefabs ({names.Count}):");
        foreach (var name in names)
            ctx.Reply($"  {name}");
    }

    [Command("ai.list.sequences", description: "List known sequence names", adminOnly: true)]
    public static void AIListSequences(ChatCommandContext ctx)
    {
        var names = AIAssistant.GetKnownSequenceNames().Take(200).ToList();
        ctx.Reply($"Known sequences ({names.Count}):");
        foreach (var name in names)
            ctx.Reply($"  {name}");
    }
}
