using BattleLuck.ECS.Actions.Components;
using BattleLuck.Services;

namespace BattleLuck.Services.Modes;

/// <summary>
/// Generic, data-driven game mode engine that replaces the procedural, multi-class system.
/// Reads rules.json and creates ECS Action entities for any game mode.
/// All mode behavior is driven by declarative configuration.
/// </summary>
public sealed class GameModeEngine : GameModeBase
{
    private void ScoreAction(GameModeContext ctx, ulong steamId, ActionType action, int? pointsOverride = null, Unity.Entities.Entity? playerEntity = null)
    {
        int points = pointsOverride ?? Models.ActionRegistry.GetDefaultPoints(action);
        if (points != 0)
            ctx.Scores.AddPlayerScore(steamId, points);

        GameEvents.OnPlayerScored?.Invoke(new PlayerScoredEvent
        {
            SessionId = ctx.SessionId,
            SteamId = steamId,
            Points = points,
            TotalScore = ctx.Scores.GetPlayerScore(steamId),
            Reason = action.ToString().ToLowerInvariant(),
            Action = action
        });

        GameEvents.OnActionPerformed?.Invoke(new ActionPerformedEvent
        {
            SessionId = ctx.SessionId,
            SteamId = steamId,
            Action = action,
            ModeId = ModeId,
            Points = points,
            PlayerEntity = playerEntity
        });
    }

    private readonly RulesConfig _rules;
    private readonly ZonesConfig _zones;
    private readonly KitConfig _kit;
    private readonly FlowConfig _flowEnter;
    private readonly FlowConfig _flowExit;

    private readonly ShrinkZoneController _shrink = new();
    private readonly TimerController _timer = new();
    private readonly LootCrateController _lootCrates = new();
    private readonly SpawnController _spawner = new();
    private readonly WaveController _waves = new();
    private readonly ObjectiveController _objectives = new();

    private int _initialPlayerCount;
    private readonly Dictionary<ulong, int> _lives = new();
    private readonly Dictionary<ulong, int> _playerKills = new();
    private float3 _center;
    private int _objectiveTarget;
    private int _objectivesCompleted;
    private bool _bossSpawned;
    private BossesConfig? _bossConfig;
    private float _bossSpawnTimer;

    public GameModeEngine(RulesConfig rules, ZonesConfig zones, KitConfig kit, FlowConfig flowEnter, FlowConfig flowExit)
    {
        _rules = rules ?? new RulesConfig();
        _zones = zones ?? new ZonesConfig();
        _kit = kit ?? new KitConfig();
        _flowEnter = flowEnter ?? new FlowConfig();
        _flowExit = flowExit ?? new FlowConfig();
    }

    public GameModeEngine(ModeConfig config) : this(config.Rules, config.Zones, config.KitConfig, config.FlowEnter, config.FlowExit)
    {
        _rules = config.Rules ?? new RulesConfig();
    }

    public GameModeEngine(RulesConfig rules) : this(rules, new ZonesConfig(), new KitConfig(), new FlowConfig(), new FlowConfig())
    {
        _rules = rules ?? new RulesConfig();
    }

    public string ModeId => _rules.ModeId ?? string.Empty;
    public override string DisplayName => _rules.DisplayName ?? ModeId;
    public ZonesConfig Zones => _zones;

    public override void OnStart(GameModeContext ctx)
    {
        ctx.State["_started"] = true;

        int timeLimitSec = _rules.MatchDurationMinutes * 60;
        if (timeLimitSec <= 0) timeLimitSec = GetDefaultTimeLimit();

        var zone = _zones.Zones.FirstOrDefault();
        _center = zone?.Position.ToFloat3() ?? float3.zero;
        float startRadius = zone?.Radius > 0 ? zone.Radius : GetDefaultStartRadius();
        float minRadius = zone?.ExitRadius > 0 ? zone.ExitRadius : GetDefaultMinRadius();
        float shrinkDuration = timeLimitSec * 0.8f;

        _timer.Start(timeLimitSec);
        ctx.TimeLimitSeconds = timeLimitSec;

        _shrink.Configure(startRadius, minRadius, shrinkDuration);
        _shrink.Start();

        _lives.Clear();
        _playerKills.Clear();
        _livesPerPlayer = GetLivesPerPlayer();
        _allowLateJoin = _rules.AllowLateJoin;

        foreach (var steamId in ctx.Players)
            _lives[steamId] = _livesPerPlayer;
        ctx.State["respawnsRemaining"] = _lives;
        ctx.State["respawnAllowancePerPlayer"] = _livesPerPlayer;
        _initialPlayerCount = ctx.Players.Count;

        if (zone?.LootCrates != null)
            _lootCrates.Configure(zone.LootCrates);

        _bossConfig = zone?.Bosses;
        ctx.State["spawner"] = _spawner;

        if (_rules.EnableVBloods || _rules.EnableEliteMobs)
        {
            int waveCount = Math.Max(3, _rules.MatchDurationMinutes);
            _objectiveTarget = waveCount;
            var waves = new List<WaveDefinition>();
            for (int i = 1; i <= waveCount; i++)
            {
                waves.Add(new WaveDefinition
                {
                    WaveNumber = i,
                    EnemyCount = 2 + i,
                    DelaySeconds = 2
                });
            }
            _waves.Configure(waves);
        }

        ctx.Broadcast?.Invoke($"{DisplayName.ToUpper()} — Fight! {(_livesPerPlayer > 1 ? $"{_livesPerPlayer} lives each. " : "")}Time: {_timer.FormatRemaining()}");
        GameEvents.OnModeStarted?.Invoke(new ModeStartedEvent
        {
            SessionId = ctx.SessionId,
            ModeId = ModeId,
        });
    }

    public override void OnTick(GameModeContext ctx, float deltaSeconds)
    {
        TickShrink(ctx);
        TickLootCrates(ctx, deltaSeconds);
        TickBossSpawn(ctx, deltaSeconds);
        TickWaves(ctx);
        CheckWinConditions(ctx);
    }

    public override void OnPlayerJoin(GameModeContext ctx, ulong steamId)
    {
        if (!_allowLateJoin && ctx.State.ContainsKey("_started"))
        {
            BattleLuckPlugin.LogInfo($"[GameModeEngine:{ModeId}] Late join denied for {steamId}");
            return;
        }

        if (!_lives.ContainsKey(steamId))
            _lives[steamId] = _livesPerPlayer;

        ctx.Players.Add(steamId);

        if (_initialPlayerCount == 0)
            _initialPlayerCount = ctx.Players.Count;

        ctx.Broadcast?.Invoke($"A new challenger approaches! ({ctx.Players.Count} players)");
    }

    public override void OnPlayerLeave(GameModeContext ctx, ulong steamId)
    {
        ctx.Players.Remove(steamId);
        _lives.Remove(steamId);

        GameEvents.OnPlayerLeft?.Invoke(new PlayerLeftEvent
        {
            SessionId = ctx.SessionId,
            SteamId = steamId,
            ModeId = ModeId
        });
    }

    public void OnPlayerDowned(GameModeContext ctx, ulong victimSteamId, ulong? killerSteamId)
    {
        if (killerSteamId.HasValue && killerSteamId.Value != victimSteamId)
        {
            _playerKills[killerSteamId.Value] = _playerKills.GetValueOrDefault(killerSteamId.Value) + 1;
            ScoreAction(ctx, killerSteamId.Value, ActionType.Kill, 1);
            ctx.Broadcast?.Invoke($"Kill! ({ctx.Scores.GetPlayerScore(killerSteamId.Value)} total)");
        }

        HandleDownedOrEliminated(ctx, victimSteamId, killerSteamId);
    }

    public void OnRoundEnd(GameModeContext ctx, int roundNumber)
    {
        GameEvents.OnRoundEnded?.Invoke(new RoundEndedEvent
        {
            SessionId = ctx.SessionId,
            ModeId = ModeId,
            RoundNumber = roundNumber,
            WinnerId = GetRoundWinner(ctx, roundNumber)
        });
    }

    public override void OnEnd(GameModeContext ctx)
    {
        _timer.Stop();
        _shrink.Reset();
        _spawner.DespawnAll();
        ctx.State.Remove("_started");

        ResolveWinner(ctx);

        var winnerId = ctx.State.TryGetValue("winner", out var winnerValue) && winnerValue is ulong resolvedWinner
            ? resolvedWinner
            : 0UL;
        var winnerKills = winnerId == 0 ? 0 : _playerKills.GetValueOrDefault(winnerId);
        var meetsChestThreshold = winnerKills >= _lootCrates.LockedUntilKills;
        var pendingRewards = meetsChestThreshold
            ? _lootCrates.GetAllRewards()
            : new List<(PrefabGUID Item, int Amount)>();
        var rewardedItems = pendingRewards.Sum(reward => reward.Amount);
        if (winnerId != 0 && pendingRewards.Count > 0)
        {
            ctx.State["pendingWinnerRewardSteamId"] = winnerId;
            ctx.State["pendingWinnerRewards"] = pendingRewards;
        }
        if (rewardedItems > 0)
            ctx.Broadcast?.Invoke($"Winner chest secured: {rewardedItems} item(s) will be granted after loadout rollback.");
        else if (_lootCrates.WinnerOnly && _lootCrates.ActiveCount > 0 && !meetsChestThreshold)
            ctx.Broadcast?.Invoke($"Winner chest remained locked: {winnerKills}/{_lootCrates.LockedUntilKills} kills.");
        _lootCrates.ClearAllCrates();

        var leaderboard = ctx.Scores.GetLeaderboard();
        var topPlayer = winnerId != 0 ? winnerId : (leaderboard.Count > 0 ? leaderboard[0] : 0UL);
        var topEntity = topPlayer == 0 ? Entity.Null : GetPlayerEntity(topPlayer);
        var winnerName = topEntity.Exists() ? EntityExtensions.FormatPlayer(topPlayer, topEntity) : topPlayer.ToString();

        ctx.Broadcast?.Invoke($"{DisplayName} over! Winner: {winnerName}");
        GameEvents.OnModeEnded?.Invoke(new ModeEndedEvent
        {
            SessionId = ctx.SessionId,
            ModeId = ModeId,
            WinnerSteamId = topPlayer != 0 ? topPlayer : null
        });
    }

    public override void OnReset(GameModeContext ctx)
    {
        _timer.Reset();
        _shrink.Reset();
        _lootCrates.ClearAllCrates();
        _spawner.Reset();
        _waves.Reset();
        _objectives.Reset();
        ctx.Scores.Reset();
        _lives.Clear();
        _initialPlayerCount = 0;
        _bossSpawned = false;
        _bossSpawnTimer = 0f;
        _objectivesCompleted = 0;
        ctx.State.Remove("_started");
    }

    public bool RecordEnemyKill(GameModeContext ctx, ulong killerSteamId, int points = 0) => false;

    internal void CreateEcsActions(EntityManager entityManager, GameModeContext ctx)
    {
        var flow = _flowEnter;
        var playerEntities = GetPlayerEntities(ctx);

        foreach (var flowName in flow.ExecutionOrder)
        {
            if (!flow.Flows.TryGetValue(flowName, out var flowDef)) continue;

            foreach (var actionStr in flowDef.Actions)
            {
                foreach (var targetEntity in playerEntities)
                {
                    CreateEcsActionEntity(entityManager, actionStr, ctx, targetEntity);
                }
            }
        }
    }

    private void CreateEcsActionEntity(EntityManager em, string actionString, GameModeContext ctx, Entity targetEntity)
    {
        var parts = actionString.Split(':', 2);
        var actionName = parts[0].Trim();

        switch (actionName)
        {
            case "kit.apply":
                var kitId = ParseParameter(actionString, "kitId", _rules.ModeId ?? ModeId);
                em.AddComponentData(targetEntity, new KitApplyAction
                {
                    TargetEntity = targetEntity,
                    KitId = new FixedString64Bytes(kitId),
                    SessionEntity = Entity.Null
                });
                break;

            case "teleport":
                em.AddComponentData(targetEntity, new TeleportPlayerAction
                {
                    TargetEntity = targetEntity,
                    Position = _center,
                    TargetZoneHash = ctx.ZoneHash,
                    SessionEntity = Entity.Null
                });
                break;

            case "notification":
            case "announce":
            case "send_message":
                var message = ParseParameter(actionString, "message", "");
                em.AddComponentData(targetEntity, new NotificationAction
                {
                    TargetEntity = targetEntity,
                    Message = new FixedString512Bytes(message),
                    Type = new FixedString64Bytes("info"),
                    SessionEntity = Entity.Null
                });
                break;

            case "shrink.zone":
                em.AddComponentData(targetEntity, new ShrinkZoneAction
                {
                    TargetEntity = targetEntity,
                    ZoneHash = ctx.ZoneHash,
                    TargetRadius = float.Parse(ParseParameter(actionString, "targetRadius", "10")),
                    ShrinkRate = float.Parse(ParseParameter(actionString, "shrinkRate", "0.5")),
                    SessionEntity = Entity.Null
                });
                break;

            case "score.add":
                var points = int.Parse(ParseParameter(actionString, "points", "1"));
                em.AddComponentData(targetEntity, new ScoreAddAction
                {
                    TargetEntity = targetEntity,
                    Points = points,
                    Reason = new FixedString128Bytes(ParseParameter(actionString, "reason", "action")),
                    SessionEntity = Entity.Null
                });
                break;
        }
    }

    private int _livesPerPlayer = 3;
    private bool _allowLateJoin = false;

    private int GetLivesPerPlayer() => Math.Max(0, _rules.LivesPerPlayer);
    private int GetDefaultTimeLimit() => _rules.EnablePvP ? 300 : 180;
    private float GetDefaultStartRadius() => _rules.EnablePvP ? 100f : 80f;
    private float GetDefaultMinRadius() => _rules.EnablePvP ? 15f : 10f;

    private int GetRoundWinner(GameModeContext ctx, int roundNumber) => 0;

    private void TickShrink(GameModeContext ctx)
    {
        _shrink.Tick(ctx, 1f / 20f);
    }

    private void TickLootCrates(GameModeContext ctx, float deltaSeconds)
    {
        var playerEntities = GetPlayerEntities(ctx);
        var zone = _zones.Zones.FirstOrDefault();
        var gridHalfExtent = Math.Max(2f, (zone?.Radius ?? 10f) * 0.5f);
        var collected = _lootCrates.Tick(
            _center,
            gridHalfExtent,
            deltaSeconds,
            playerEntities,
            ctx.SessionId,
            steamId => !_lootCrates.WinnerOnly && _playerKills.GetValueOrDefault(steamId) >= _lootCrates.LockedUntilKills);

        foreach (var (player, crate) in collected)
        {
            ulong steamId = player.GetSteamId();
            ScoreAction(ctx, steamId, ActionType.LootCrate, 5);
            ctx.Broadcast?.Invoke($"Loot collected! +5 pts");
        }
    }

    private void TickBossSpawn(GameModeContext ctx, float deltaSeconds)
    {
        if (_bossSpawned || _bossConfig == null || !_bossConfig.Enabled) return;

        var trigger = _bossConfig.SpawnTrigger ?? "timed";
        if (trigger == "timed")
        {
            _bossSpawnTimer -= deltaSeconds;
            if (_bossSpawnTimer <= 0) SpawnConfiguredBoss(ctx);
        }
        else if (trigger == "player_count")
        {
            if (_initialPlayerCount > 2 && ctx.Players.Count <= _initialPlayerCount / 2)
                SpawnConfiguredBoss(ctx);
        }
    }

    private void SpawnConfiguredBoss(GameModeContext ctx)
    {
        if (_bossConfig?.Bosses == null || _bossConfig.Bosses.Count == 0) return;

        var rng = new System.Random();
        var bossDef = _bossConfig.Bosses[rng.Next(_bossConfig.Bosses.Count)];
        var prefab = PrefabHelper.GetPrefabGuidDeep(bossDef.Prefab);
        if (!prefab.HasValue) return;

        var offset = bossDef.SpawnOffset?.ToFloat3() ?? float3.zero;
        var level = bossDef.Level > 0 ? bossDef.Level : 0;

        _spawner.SpawnBoss(prefab.Value, _center + offset, level, entity =>
        {
            ctx.Broadcast?.Invoke($"⚠️ BOSS SPAWNED: {bossDef.Name} (Level {level})! Kill it for bonus points!");
            _bossSpawned = true;
        });
    }

    private void TickWaves(GameModeContext ctx)
    {
        if (!_rules.EnableVBloods && !_rules.EnableEliteMobs) return;

        if (!_waves.IsWaveActive && !_waves.IsComplete)
        {
            DoSpawnWave(ctx);
        }
    }

    private void DoSpawnWave(GameModeContext ctx)
    {
        var waveDef = _waves.StartNextWave(ctx);
        if (waveDef == null) return;

        if (!string.IsNullOrWhiteSpace(waveDef.KitId))
        {
            foreach (var online in VRisingCore.GetOnlinePlayers())
            {
                if (!online.Exists() || !online.IsPlayer())
                    continue;

                var steamId = online.GetSteamId();
                if (ctx.Players.Contains(steamId))
                {
                    var kitResult = KitController.ApplyKit(online, waveDef.KitId);
                    if (kitResult.Success)
                        ctx.Broadcast?.Invoke($"🎒 Wave {waveDef.WaveNumber} loadout applied for {steamId}.");
                }
            }
        }

        if (ctx.State.TryGetValue("spawner", out var sp) && sp is SpawnController spawner)
        {
            var prefabs = SpawnController.GetEnemiesForWave(waveDef.WaveNumber);
            spawner.SpawnWave(prefabs, waveDef.EnemyCount, _center, 6f);
        }
    }

    private void HandleDownedOrEliminated(GameModeContext ctx, ulong victimSteamId, ulong? killerSteamId)
    {
        if (_lives.TryGetValue(victimSteamId, out int remaining) && remaining > 0)
        {
            remaining--;
            _lives[victimSteamId] = remaining;
            ctx.Broadcast?.Invoke($"Down! Immediate arena respawn; {remaining} {(remaining == 1 ? "respawn" : "respawns")} remaining after this one.");
            return;
        }

        ctx.Players.Remove(victimSteamId);
        ScoreAction(ctx, victimSteamId, ActionType.Elimination, 0);
        GameEvents.OnPlayerEliminated?.Invoke(new PlayerEliminatedEvent
        {
            SessionId = ctx.SessionId,
            SteamId = victimSteamId,
            EliminatedBy = killerSteamId
        });
        ctx.Broadcast?.Invoke($"ELIMINATED! ({ctx.Players.Count} remain)");
    }

    private void CheckWinConditions(GameModeContext ctx)
    {
        if (_timer.IsExpired)
        {
            ctx.State["result"] = "time_up";
            var leaderboard = ctx.Scores.GetLeaderboard();
            if (leaderboard.Count > 0)
                ctx.State["winner"] = leaderboard[0];
            return;
        }

        int alivePlayers = ctx.Players.Count;

        if (alivePlayers <= 1 && _initialPlayerCount > 1)
        {
            ctx.State["result"] = "last_standing";
            if (alivePlayers > 0)
            {
                ulong? winner = ctx.Players.FirstOrDefault();
                if (winner.HasValue && winner.Value != 0)
                    ctx.State["winner"] = winner.Value;
            }
        }
    }

    private void ResolveWinner(GameModeContext ctx)
    {
        var result = ctx.State.TryGetValue("winner", out var w) && w is ulong winnerId ? winnerId : 0UL;

        if (result == 0 && _objectivesCompleted >= _objectiveTarget)
        {
            var leaderboard = ctx.Scores.GetLeaderboard();
            result = leaderboard.Count > 0 ? leaderboard[0] : 0UL;

            foreach (var steamId in ctx.Players)
            {
                int timeBonus = (int)_timer.RemainingSeconds;
                ctx.Scores.AddPlayerScore(steamId, timeBonus);
            }
        }

        ctx.State["winner"] = result;
    }

    private static List<Unity.Entities.Entity> GetPlayerEntities(GameModeContext ctx)
    {
        var entities = new List<Unity.Entities.Entity>();
        foreach (var steamId in ctx.Players)
        {
            var onlinePlayers = VRisingCore.GetOnlinePlayers();
            foreach (var player in onlinePlayers)
            {
                if (player.GetSteamId() == steamId)
                {
                    entities.Add(player);
                    break;
                }
            }
        }
        return entities;
    }

    private static Entity GetPlayerEntity(ulong steamId) =>
        VRisingCore.GetOnlinePlayers().FirstOrDefault(player =>
            player.Exists() && player.IsPlayer() && player.GetSteamId() == steamId);

    private static string ParseParameter(string actionString, string key, string fallback)
    {
        var parts = actionString.Split(':', 2);
        if (parts.Length < 2) return fallback;

        foreach (var param in parts[1].Split('|'))
        {
            var kv = param.Split('=', 2);
            if (kv.Length == 2 && kv[0].Trim() == key)
                return kv[1].Trim();
        }
        return fallback;
    }

    public ShrinkZoneController Shrink => _shrink;
    public TimerController Timer => _timer;
    public LootCrateController LootCrates => _lootCrates;
    public SpawnController Spawner => _spawner;
    public WaveController Waves => _waves;
    public ObjectiveController Objectives => _objectives;
    public IReadOnlyDictionary<ulong, int> Lives => _lives;
}
