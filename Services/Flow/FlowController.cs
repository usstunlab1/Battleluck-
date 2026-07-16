using ProjectM;
using Unity.Entities;
using Unity.Mathematics;
using BattleLuck.Services.Flow;

/// <summary>
/// Executes configured BattleLuck enter/exit/action flows on the main thread.
/// The concrete action behavior lives in FlowActionExecutor so config, commands,
/// and AI-triggered actions share one production path.
/// </summary>
public sealed class FlowController
{
    readonly PlayerStateController _playerState;
    readonly GameModeRegistry _registry;
    readonly FlowActionExecutor _executor;

    public FlowController(PlayerStateController playerState, GameModeRegistry registry)
    {
        _playerState = playerState;
        _registry = registry;
        _executor = new FlowActionExecutor(playerState, registry);
    }

    public OperationResult ExecuteEnter(ModeConfig config, Entity playerCharacter, ZoneDefinition zone, GameModeContext? ctx = null, ulong steamIdOverride = 0)
    {
        var steamId = steamIdOverride != 0 ? steamIdOverride : playerCharacter.GetSteamId();
        var prepKey = $"entryPrepared:{steamId}";
        var returnPositionKey = $"returnPosition:{steamId}";
        try
        {
            float3? returnPosition = null;
            if (ctx?.State.TryGetValue(returnPositionKey, out var returnValue) == true && returnValue is float3 savedReturnPosition)
                returnPosition = savedReturnPosition;

            var prepareResult = _playerState.PrepareForEventEntry(playerCharacter, zone.Hash, returnPosition, steamId, ctx?.SessionId ?? "", ctx?.ModeId ?? "");
            if (!prepareResult.Success)
                return prepareResult;

            if (ctx != null)
                ctx.State[prepKey] = true;

            var actionContext = CreateContext(config, playerCharacter, zone, ctx);
            var result = HasFlow(config.FlowEnter)
                ? _executor.ExecuteFlow(config.FlowEnter, actionContext, rollbackOnFailure: true)
                : ExecuteFallbackEnter(config, playerCharacter, zone);

            if (!result.Success)
            {
                TryRestorePreparedEnter(playerCharacter, zone.Hash);
                if (TryGetUser(playerCharacter, out var failUser))
                    NotificationHelper.NotifyPlayer(failUser, $"<color=#ff3333>Entry to {zone.Name} failed and was rolled back: {result.Error}</color>");
                return result;
            }

            GameEvents.OnZoneEnter?.Invoke(new ZoneEnterEvent
            {
                PlayerEntity = playerCharacter,
                SteamId = steamId,
                ZoneId = zone.Name,
                SessionId = ctx?.SessionId ?? ""
            });

            if (TryGetUser(playerCharacter, out var user))
                NotificationHelper.NotifyPlayer(user, $"Entered {zone.Name}. Loadout applied.");

            return OperationResult.Ok();
        }
        catch (Exception ex)
        {
            BattleLuckPlugin.LogError($"[FlowController] ExecuteEnter failed for {steamId} in zone {zone.Hash}: {ex.Message}");
            TryRestorePreparedEnter(playerCharacter, zone.Hash);
            return OperationResult.Fail(ex.Message);
        }
        finally
        {
            ctx?.State.Remove(prepKey);
        }
    }

    public OperationResult ExecuteExit(ModeConfig config, Entity playerCharacter, ZoneDefinition zone, GameModeContext? ctx = null)
    {
        var steamId = playerCharacter.GetSteamId();
        try
        {
            var kitId = string.IsNullOrWhiteSpace(zone.KitId) ? config.KitId : zone.KitId;
            KitController.RemoveEventKit(playerCharacter, kitId);

            var actionContext = CreateContext(config, playerCharacter, zone, ctx);
            var result = HasFlow(config.FlowExit)
                ? _executor.ExecuteFlow(config.FlowExit, actionContext, rollbackOnFailure: false)
                : ExecuteFallbackExit(config, playerCharacter, zone);

            if (!result.Success)
                return result;

            GameEvents.OnZoneExit?.Invoke(new ZoneExitEvent
            {
                PlayerEntity = playerCharacter,
                SteamId = steamId,
                ZoneId = zone.Name,
                SessionId = ctx?.SessionId ?? ""
            });

            if (TryGetUser(playerCharacter, out var user))
                NotificationHelper.NotifyPlayer(user, $"Exited {zone.Name}. Original state restored.");

            return OperationResult.Ok();
        }
        catch (Exception ex)
        {
            BattleLuckPlugin.LogError($"[FlowController] ExecuteExit failed for {steamId} in zone {zone.Hash}: {ex.Message}");
            return OperationResult.Fail(ex.Message);
        }
    }

    public OperationResult ExecuteFlow(FlowConfig flowConfig, ModeConfig config, Entity playerCharacter, ZoneDefinition zone, GameModeContext? ctx = null, bool rollbackOnFailure = true)
    {
        return _executor.ExecuteFlow(flowConfig, CreateContext(config, playerCharacter, zone, ctx), rollbackOnFailure);
    }

    public OperationResult ExecuteStart(ModeConfig config, Entity playerCharacter, ZoneDefinition zone, GameModeContext? ctx = null)
    {
        return ExecutePhase(config.Session.Flow.Start, "start", config, playerCharacter, zone, ctx);
    }

    public OperationResult ExecuteTracking(ModeConfig config, Entity playerCharacter, ZoneDefinition zone, GameModeContext? ctx = null)
    {
        return ExecutePhase(config.Session.Flow.Tracking, "tracking", config, playerCharacter, zone, ctx);
    }

    public OperationResult ExecuteWinner(ModeConfig config, Entity playerCharacter, ZoneDefinition zone, GameModeContext? ctx = null)
    {
        return ExecutePhase(config.Session.Flow.Winner, "winner", config, playerCharacter, zone, ctx);
    }

    public OperationResult ExecuteEnding(ModeConfig config, Entity playerCharacter, ZoneDefinition zone, GameModeContext? ctx = null)
    {
        return ExecutePhase(config.Session.Flow.Ending, "ending", config, playerCharacter, zone, ctx);
    }

    public OperationResult ExecuteAction(string actionString, Entity playerCharacter, int zoneHash, GameModeContext? ctx = null)
    {
        var config = ResolveConfig(zoneHash, ctx);
        var zone = ResolveZone(config, zoneHash);
        return _executor.Execute(actionString, CreateContext(config, playerCharacter, zone, ctx));
    }

    FlowActionContext CreateContext(ModeConfig config, Entity playerCharacter, ZoneDefinition zone, GameModeContext? ctx)
    {
        return new FlowActionContext
        {
            PlayerCharacter = playerCharacter,
            ZoneHash = zone.Hash,
            PlayerState = _playerState,
            Registry = _registry,
            Config = config,
            Zone = zone,
            GameContext = ctx
        };
    }

    OperationResult ExecuteFallbackEnter(ModeConfig config, Entity playerCharacter, ZoneDefinition zone)
    {
        var steamId = playerCharacter.GetSteamId();
        var kitId = string.IsNullOrWhiteSpace(zone.KitId) ? config.KitId : zone.KitId;

        _playerState.SaveSnapshotIfMissing(playerCharacter, zone.Hash, eventRunId: "", eventModeId: config.ModeId);
        var kitResult = KitController.ApplyKit(playerCharacter, kitId);
        if (!kitResult.Success)
        {
            TryRestorePreparedEnter(playerCharacter, zone.Hash);
            return OperationResult.Fail(kitResult.Error ?? "kit.apply failed");
        }

        if (config.Session.Rules.EnablePvP)
            playerCharacter.SetTeam(zone.Hash + (int)(steamId % 1000));

        playerCharacter.HealToFull();
        playerCharacter.SetPosition(zone.TeleportSpawn.ToFloat3());
        return OperationResult.Ok();
    }

    void TryRestorePreparedEnter(Entity playerCharacter, int zoneHash)
    {
        var steamId = playerCharacter.GetSteamId();
        if (steamId == 0 || !_playerState.HasSnapshot(steamId))
            return;

        try { _playerState.RestoreSnapshot(playerCharacter, zoneHash); }
        catch (Exception restoreEx) { BattleLuckPlugin.LogError($"[FlowController] Enter rollback failed for {steamId}: {restoreEx.Message}"); }
    }

    OperationResult ExecuteFallbackExit(ModeConfig config, Entity playerCharacter, ZoneDefinition zone)
    {
        if (!_playerState.RestoreSnapshot(playerCharacter, zone.Hash))
            return OperationResult.Fail($"No snapshot found for {playerCharacter.GetSteamId()}.");

        if (config.Session.Rules.EnablePvP)
            playerCharacter.SetTeam(0);

        return OperationResult.Ok();
    }

    ModeConfig ResolveConfig(int zoneHash, GameModeContext? ctx)
    {
        if (ctx?.State.TryGetValue("config", out var cfg) == true && cfg is ModeConfig modeConfig)
            return modeConfig;

        if (!string.IsNullOrWhiteSpace(ctx?.ModeId))
            return ConfigLoader.Load(ctx.ModeId);

        foreach (var modeId in _registry.GetRegisteredModes())
        {
            var config = ConfigLoader.Load(modeId);
            if (config.Zones.Zones.Any(z => z.Hash == zoneHash))
                return config;
        }

        return ConfigLoader.Load("bloodbath");
    }

    static ZoneDefinition ResolveZone(ModeConfig config, int zoneHash)
    {
        return config.Zones.Zones.FirstOrDefault(z => z.Hash == zoneHash)
            ?? new ZoneDefinition { Hash = zoneHash, Name = zoneHash.ToString() };
    }

    static bool HasFlow(FlowConfig flow) =>
        flow.ExecutionOrder?.Count > 0 || flow.Flows?.Count > 0;

    OperationResult ExecutePhase(FlowConfig flowConfig, string phaseName, ModeConfig config, Entity playerCharacter, ZoneDefinition zone, GameModeContext? ctx)
    {
        if (!HasFlow(flowConfig))
            return OperationResult.Ok();

        var result = ExecuteFlow(flowConfig, config, playerCharacter, zone, ctx, rollbackOnFailure: false);
        if (!result.Success)
            BattleLuckPlugin.LogWarning($"[FlowController] {phaseName} flow failed for mode '{config.ModeId}': {result.Error}");
        return result;
    }

    public static bool TryGetUser(Entity playerCharacter, out ProjectM.Network.User user)
    {
        user = default;
        if (!playerCharacter.Has<PlayerCharacter>())
            return false;

        var userEntity = playerCharacter.Read<PlayerCharacter>().UserEntity;
        if (!userEntity.Exists() || !userEntity.Has<ProjectM.Network.User>())
            return false;

        user = userEntity.Read<ProjectM.Network.User>();
        return true;
    }
}
