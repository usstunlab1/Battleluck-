namespace BattleLuck.Services;

/// <summary>Connects the engine-independent clan task store to ProjectM events.</summary>
public sealed class ClanTaskGameAdapter : IDisposable
{
    readonly ClanTaskService _service;
    readonly PlayerStateController _playerState;
    readonly FlowActionExecutor _actionExecutor;
    readonly HashSet<(int Index, int Version)> _processedDeaths = new();
    bool _disposed;

    public ClanTaskGameAdapter(ClanTaskService service, PlayerStateController playerState, GameModeRegistry? registry)
    {
        _service = service;
        _playerState = playerState;
        _actionExecutor = new FlowActionExecutor(playerState, registry);
        DeathHook.OnDeath += HandleDeath;
        GameEvents.OnModeEnded += HandleModeEnded;
        _service.TaskCompleted += GrantCompletionReward;
    }

    public static string ResolveClanId(Entity player)
    {
        if (!player.Exists()) return "";
        var userEntity = player.GetUserEntity();
        if (userEntity.Exists() && userEntity.Has<ProjectM.Network.User>())
        {
            var user = userEntity.Read<ProjectM.Network.User>();
            var clanEntity = user.ClanEntity.GetEntityOnServer();
            if (clanEntity.Exists() && clanEntity.Has<ClanTeam>())
            {
                var clan = clanEntity.Read<ClanTeam>();
                var clanGuid = clan.ClanGuid.ToString();
                if (!string.IsNullOrWhiteSpace(clanGuid)) return clanGuid;
                return $"team:{clan.TeamValue.ToString(System.Globalization.CultureInfo.InvariantCulture)}";
            }
        }
        var steamId = player.GetSteamId();
        return steamId == 0 ? "" : $"player:{steamId.ToString(System.Globalization.CultureInfo.InvariantCulture)}";
    }

    /// <summary>Only verified harvest/pickup provenance adapters should call this boundary.</summary>
    public OperationResult<ClanTask> RecordGatheredItem(Entity player, PrefabGUID item, int amount)
    {
        var steamId = player.GetSteamId();
        var session = FindPlayerSession(steamId);
        return _service.RecordGatheredItem(
            steamId,
            item.GuidHash,
            amount,
            ResolveClanId(player),
            session?.Context.ModeId,
            session?.Context.SessionId);
    }

    void HandleDeath(Entity died, Entity killer)
    {
        if (!died.Exists() || died.IsPlayer() || !killer.Exists() || !killer.IsPlayer()) return;
        var key = (died.Index, died.Version);
        if (!_processedDeaths.Add(key)) return;
        if (_processedDeaths.Count > 4096) _processedDeaths.Clear();
        var prefab = died.GetPrefabGuid();
        if (prefab == PrefabGUID.Empty) return;
        var steamId = killer.GetSteamId();
        var clanId = ResolveClanId(killer);
        var playerSession = FindPlayerSession(steamId);
        foreach (var task in _service.ListForPlayer(
                     steamId,
                     includeInactive: false,
                     clanId: clanId,
                     callerEventId: playerSession?.Context?.ModeId,
                     callerSessionId: playerSession?.Context?.SessionId,
                     restrictEventTasksToCallerSession: true)
                     .Where(task => task.Objective.Type == ClanTaskObjectiveType.BossKill && task.Objective.PrefabGuidHash == prefab.GuidHash))
        {
            if (task.Scope == ClanTaskScope.Event &&
                (playerSession?.Context == null ||
                 (!string.IsNullOrWhiteSpace(task.EventId) && !task.EventId.Equals(playerSession.Context.ModeId, StringComparison.OrdinalIgnoreCase)) ||
                 (!string.IsNullOrWhiteSpace(task.SessionId) && !task.SessionId.Equals(playerSession.Context.SessionId, StringComparison.OrdinalIgnoreCase))))
                continue;

            _service.AddProgress(
                task.TaskId,
                1,
                steamId,
                trustedGather: true,
                callerEventId: playerSession?.Context.ModeId,
                callerSessionId: playerSession?.Context.SessionId);
        }
    }

    void HandleModeEnded(ModeEndedEvent evt) => _service.ExpireEventTasks(evt.ModeId, evt.SessionId);

    static ActiveSession? FindPlayerSession(ulong steamId) =>
        BattleLuckPlugin.Session?.ActiveSessions?.Values.FirstOrDefault(candidate =>
            candidate.Context?.Players.Contains(steamId) == true);

    void GrantCompletionReward(ClanTask task)
    {
        if (task.RewardPoints <= 0 || task.Scope != ClanTaskScope.Event) return;
        var session = BattleLuckPlugin.Session?.ActiveSessions?.Values.FirstOrDefault(candidate =>
            candidate.Context is { } context &&
            context.SessionId.Equals(task.SessionId, StringComparison.OrdinalIgnoreCase) &&
            context.ModeId.Equals(task.EventId, StringComparison.OrdinalIgnoreCase));
        if (session?.Context == null)
        {
            BattleLuckPlugin.LogWarning($"[ClanTasks] Task '{task.TaskId}' completed, but its {task.RewardPoints}-point reward had no active event session.");
            return;
        }
        // Catalog-backed reward dispatch: fire a reward action string through the first available
        // player's executor context rather than directly mutating session scores.
        // This keeps the reward path decoupled from the task service.
        var recipients = task.AssignedSteamIds.Count > 0 ? task.AssignedSteamIds : task.Contributions.Keys.ToHashSet();
        var rewardAction = $"clan.task.reward:taskId={task.TaskId}|rewardPoints={task.RewardPoints}";
        var notified = new HashSet<ulong>();
        foreach (var player in session.Context.Players)
        {
            if (!recipients.Contains(player)) continue;
            // Try to execute the catalog action for each online recipient
            foreach (var onlineEntity in VRisingCore.GetOnlinePlayers())
            {
                if (onlineEntity.GetSteamId() != player || !onlineEntity.Exists()) continue;
                try
                {
                    var flowContext = new FlowActionContext
                    {
                        PlayerCharacter = onlineEntity,
                        ZoneHash = session.Context.ZoneHash,
                        PlayerState = _playerState,
                        Registry = BattleLuckPlugin.GameModes,
                        Config = session.Config,
                        Zone = session.Config.Zones.Zones.FirstOrDefault(zone => zone.Hash == session.Context.ZoneHash),
                        GameContext = session.Context
                    };
                    var result = _actionExecutor.Execute(rewardAction, flowContext);
                    if (!result.Success)
                    {
                        BattleLuckPlugin.LogWarning($"[ClanTasks] Reward action dispatch rejected for player {player}: {result.UserMessage}");
                        break;
                    }
                    notified.Add(player);
                }
                catch (Exception ex)
                {
                    BattleLuckPlugin.LogWarning($"[ClanTasks] Reward action dispatch failed for player {player}: {ex.Message}");
                }
                break;
            }
            if (notified.Contains(player))
                BattleLuckPlugin.NotifyPlayerBySteamIdOnMainThread(player, $"Clan task complete: {task.Description} (+{task.RewardPoints} points)");
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        DeathHook.OnDeath -= HandleDeath;
        GameEvents.OnModeEnded -= HandleModeEnded;
        _service.TaskCompleted -= GrantCompletionReward;
    }
}
