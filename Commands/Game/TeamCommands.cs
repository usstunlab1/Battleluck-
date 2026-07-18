public static class TeamCommands
{
    static readonly Dictionary<string, BattleTeam> _teams = new();
    static readonly Dictionary<ulong, string> _playerTeam = new();
    static readonly Dictionary<ulong, List<ulong>> _invites = new();

    [Command("teamcreate", description: "Create a team")]
    public static void CreateTeam(ChatCommandContext ctx, string teamName)
    {
        var entity = ctx.Event.SenderCharacterEntity;
        ulong steamId = entity.GetSteamId();

        if (_playerTeam.ContainsKey(steamId))
        {
            ctx.Reply("You are already in a team. Leave first.");
            return;
        }

        if (_teams.ContainsKey(teamName))
        {
            ctx.Reply("Team name already exists.");
            return;
        }

        var team = new BattleTeam { Name = teamName, Leader = steamId };
        team.Members.Add(steamId);
        _teams[teamName] = team;
        _playerTeam[steamId] = teamName;

        ctx.Reply($"Team '{teamName}' created.");
    }

    [Command("teaminvite", description: "Invite player to your team")]
    public static void InvitePlayer(ChatCommandContext ctx, string steamIdStr)
    {
        if (!ulong.TryParse(steamIdStr, out var targetId))
        {
            ctx.Reply("Invalid Steam ID.");
            return;
        }

        var entity = ctx.Event.SenderCharacterEntity;
        ulong steamId = entity.GetSteamId();

        if (!_playerTeam.TryGetValue(steamId, out var teamName))
        {
            ctx.Reply("You are not in a team.");
            return;
        }

        var team = _teams[teamName];
        if (team.Leader != steamId)
        {
            ctx.Reply("Only the team leader can invite players.");
            return;
        }

        if (!_invites.ContainsKey(targetId))
            _invites[targetId] = new List<ulong>();

        _invites[targetId].Add(steamId);
        ctx.Reply($"Invited {targetId} to your team.");
    }

    [Command("teamaccept", description: "Accept team invite")]
    public static void AcceptInvite(ChatCommandContext ctx)
    {
        var entity = ctx.Event.SenderCharacterEntity;
        ulong steamId = entity.GetSteamId();

        if (!_invites.TryGetValue(steamId, out var invites) || invites.Count == 0)
        {
            ctx.Reply("No pending invites.");
            return;
        }

        var leaderId = invites[0];
        invites.RemoveAt(0);

        foreach (var kv in _teams)
        {
            if (kv.Value.Leader == leaderId)
            {
                kv.Value.Members.Add(steamId);
                _playerTeam[steamId] = kv.Key;
                ctx.Reply($"Joined team '{kv.Key}'.");
                return;
            }
        }

        ctx.Reply("Team no longer exists.");
    }

    [Command("teamleave", description: "Leave your team")]
    public static void LeaveTeam(ChatCommandContext ctx)
    {
        var entity = ctx.Event.SenderCharacterEntity;
        ulong steamId = entity.GetSteamId();

        if (!_playerTeam.TryGetValue(steamId, out var teamName))
        {
            ctx.Reply("You are not in a team.");
            return;
        }

        var team = _teams[teamName];
        team.Members.Remove(steamId);
        _playerTeam.Remove(steamId);

        if (team.Members.Count == 0)
        {
            _teams.Remove(teamName);
        }
        else if (team.Leader == steamId)
        {
            team.Leader = team.Members[0];
        }

        ctx.Reply("Left team.");
    }

    [Command("teamlist", description: "List all teams")]
    public static void ListTeams(ChatCommandContext ctx)
    {
        if (_teams.Count == 0)
        {
            ctx.Reply("No teams exist.");
            return;
        }

        ctx.Reply($"Teams ({_teams.Count}):");
        foreach (var kv in _teams)
        {
            ctx.Reply($"  {kv.Key} — {kv.Value.Members.Count} members (leader: {kv.Value.Leader})");
        }
    }
}

public class BattleTeam
{
    public string Name { get; set; } = "";
    public ulong Leader { get; set; }
    public List<ulong> Members { get; } = new();
}