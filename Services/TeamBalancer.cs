using BattleLuck.ECS.Actions.Components;

namespace BattleLuck.Services;

/// <summary>
/// Auto-assigns session teams based on the number of participating players:
///   - Odd player count  → free-for-all (every player on a unique team).
///   - Even player count → two balanced teams (alternating assignment, e.g. 4 → 2v2).
/// Writes both the live ECS team (<c>SetTeam</c>) and the session <see cref="GameModeContext.Teams"/> map.
/// Only operates pre-start/warmup; mid-match reshushes require admin confirmation.
/// </summary>
public static class TeamBalancer
{
    const int FfaTeamBase = 5000;
    const int TeamA = 1;
    const int TeamB = 2;

    public static OperationResult AssignTeams(GameModeContext ctx, bool forceMidMatch = false)
    {
        // Do not reshuffle teams mid-match unless explicitly forced by admin
        if (ctx.State.TryGetValue("matchReady", out var ready) && ready is true && !forceMidMatch)
        {
            return OperationResult.Fail("team.autobalance: cannot reshuffle teams after match has started. Use forceMidMatch=true for deliberate mid-match reshuffle.");
        }

        var players = ResolvePlayerEntities(ctx);
        if (players.Count == 0)
            return OperationResult.Fail("team.autobalance: no online players in session to assign.");

        ctx.Teams.Clear();

        bool ffa = players.Count % 2 != 0;
        for (int i = 0; i < players.Count; i++)
        {
            var (steamId, entity) = players[i];
            int teamId = ffa ? FfaTeamBase + i : (i % 2 == 0 ? TeamA : TeamB);
            entity.SetTeam(teamId);
            ctx.Teams[steamId] = teamId;

            // Sync EventParticipant TeamIndex
            var em = VRisingCore.EntityManager;
            if (em.HasComponent<EventParticipant>(entity))
            {
                var participant = em.GetComponentData<EventParticipant>(entity);
                participant.TeamIndex = teamId;
                em.SetComponentData(entity, participant);
            }
        }

        ctx.Broadcast?.Invoke(ffa
            ? $"Teams: FREE-FOR-ALL ({players.Count} players)."
            : $"Teams balanced: {players.Count / 2}v{players.Count / 2}.");

        BattleLuckPlugin.LogInfo($"[TeamBalancer] Assigned {players.Count} players ({(ffa ? "FFA" : "2 teams")}).");
        return OperationResult.Ok();
    }

    static List<(ulong steamId, Entity entity)> ResolvePlayerEntities(GameModeContext ctx)
    {
        var list = new List<(ulong, Entity)>();
        foreach (var online in VRisingCore.GetOnlinePlayers())
        {
            if (!online.Exists() || !online.IsPlayer())
                continue;

            var steamId = online.GetSteamId();
            if (ctx.Players.Contains(steamId))
                list.Add((steamId, online));
        }
        return list;
    }
}
