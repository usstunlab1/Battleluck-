using Unity.Entities;
using Unity.Collections;
using Unity.Mathematics;
using BattleLuck.ECS.Actions.Components;
using BattleLuck.Core;
using BattleLuck.Utilities;
using ProjectM;
using ProjectM.Network;

namespace BattleLuck.ECS.Actions.Systems;

/// <summary>
/// Work implementation for processing teleport actions.
/// </summary>
public sealed class TeleportActionWork : ISystemWork
{
    public void Build(ref EntityQueryBuilder builder)
    {
        builder.WithAll<TeleportPlayerAction>();
    }

    public void OnUpdate(SystemContext context)
    {
        var ecbSystem = context.System.World.GetExistingSystemManaged<EndSimulationEntityCommandBufferSystem>();
        if (ecbSystem == null) return;
        var ecb = ecbSystem.CreateCommandBuffer();
        
        context.ForEachEntity(context.Query, entity =>
        {
            var action = context.EntityManager.GetComponentData<TeleportPlayerAction>(entity);
            
            if (action.TargetEntity.Exists())
            {
                // We use the extension method for stability as it handles 
                // network events and waypoint buffs correctly.
                action.TargetEntity.SetPosition(action.Position);
            }
            
            ecb.RemoveComponent<TeleportPlayerAction>(entity);
            ecb.AddComponent<ActionCompletedEvent>(entity);
        });
    }
}

public sealed class TeleportActionSystem : VSystemBase<TeleportActionWork> { }

/// <summary>
/// Work implementation for processing event-specific actions (chests, teams, entry).
/// </summary>
public sealed class EventActionWork : ISystemWork
{
    public void Build(ref EntityQueryBuilder builder)
    {
        builder.WithAny<ChestSpawnAction, TeamSwapAction, EventEntryToggleAction, EventFinalizeAction, TeamCornerTeleportAction>();
    }

    public void OnUpdate(SystemContext context)
    {
        var ecbSystem = context.System.World.GetExistingSystemManaged<EndSimulationEntityCommandBufferSystem>();
        if (ecbSystem == null) return;
        var ecb = ecbSystem.CreateCommandBuffer();

        // Use context.Query which is already built in Build() for these components
        context.ForEachEntity(context.Query, entity =>
        {
            if (context.EntityManager.HasComponent<ChestSpawnAction>(entity))
            {
                var action = context.EntityManager.GetComponentData<ChestSpawnAction>(entity);
                // Stable spawn via SchematicLoader
                SchematicLoader.SpawnPrefabAt("TM_Chest_Standard", action.Position, 0f, "chest", "event_chest");
                ecb.RemoveComponent<ChestSpawnAction>(entity);
                ecb.AddComponent<ActionCompletedEvent>(entity);
            }
            else if (context.EntityManager.HasComponent<TeamSwapAction>(entity))
            {
                var action = context.EntityManager.GetComponentData<TeamSwapAction>(entity);
                if (action.TargetEntity.Exists())
                {
                    action.TargetEntity.SetTeam(action.NewTeamId);
                }
                ecb.RemoveComponent<TeamSwapAction>(entity);
                ecb.AddComponent<ActionCompletedEvent>(entity);
            }
            else if (context.EntityManager.HasComponent<TeamCornerTeleportAction>(entity))
            {
                var action = context.EntityManager.GetComponentData<TeamCornerTeleportAction>(entity);
                if (action.TargetEntity.Exists())
                {
                    var session = BattleLuckPlugin.Session?.GetSession(action.ZoneHash);
                    var zone = session?.Config.Zones.Zones.FirstOrDefault(item => item.Hash == action.ZoneHash);
                    if (zone != null)
                    {
                        var center = zone.Position.ToFloat3();
                        if (math.lengthsq(center) < 0.0001f)
                            center = zone.TeleportSpawn.ToFloat3();

                        var steamId = action.TargetEntity.GetSteamId();
                        var team = session!.Context.Teams.TryGetValue(steamId, out var configuredTeam)
                            ? configuredTeam
                            : 0;
                        var signX = team % 2 == 0 ? -1f : 1f;
                        var signZ = team / 2 % 2 == 0 ? -1f : 1f;
                        action.TargetEntity.SetPosition(center + new float3(
                            signX * action.Radius,
                            0f,
                            signZ * action.Radius));
                    }
                }
                ecb.RemoveComponent<TeamCornerTeleportAction>(entity);
                ecb.AddComponent<ActionCompletedEvent>(entity);
            }
            else if (context.EntityManager.HasComponent<EventEntryToggleAction>(entity))
            {
                var action = context.EntityManager.GetComponentData<EventEntryToggleAction>(entity);
                var steamId = action.TargetEntity.GetSteamId();
                if (steamId != 0 && BattleLuckPlugin.Session != null)
                {
                    if (action.Enabled)
                        BattleLuckPlugin.Session.ToggleEnter(steamId, action.TargetEntity);
                    else
                        BattleLuckPlugin.Session.ToggleLeave(steamId, action.TargetEntity);
                }
                ecb.RemoveComponent<EventEntryToggleAction>(entity);
                ecb.AddComponent<ActionCompletedEvent>(entity);
            }
            else if (context.EntityManager.HasComponent<EventFinalizeAction>(entity))
            {
                var action = context.EntityManager.GetComponentData<EventFinalizeAction>(entity);
                BattleLuckPlugin.Session?.FinalizeSession(
                    action.SessionEntity,
                    action.WinnerNames.ToString());
                ecb.RemoveComponent<EventFinalizeAction>(entity);
                ecb.AddComponent<ActionCompletedEvent>(entity);
            }
        });
    }
}

public sealed class EventActionSystem : VSystemBase<EventActionWork> { }

/// <summary>
/// Work implementation for processing schematic load/clear actions.
/// </summary>
public sealed class SchematicActionWork : ISystemWork
{
    public void Build(ref EntityQueryBuilder builder)
    {
        builder.WithAny<SchematicLoadAction, SchematicClearAction>();
    }

    public void OnUpdate(SystemContext context)
    {
        var ecbSystem = context.System.World.GetExistingSystemManaged<EndSimulationEntityCommandBufferSystem>();
        if (ecbSystem == null) return;
        var ecb = ecbSystem.CreateCommandBuffer();

        // Process SchematicLoadAction
        context.ForEachEntity(context.Query, entity => {
            if (context.EntityManager.HasComponent<SchematicLoadAction>(entity))
            {
                var action = context.EntityManager.GetComponentData<SchematicLoadAction>(entity);
                EventSchematicService.Load(action.SchematicId.ToString(), action.Center, true);
            }
            else if (context.EntityManager.HasComponent<SchematicClearAction>(entity))
            {
                var action = context.EntityManager.GetComponentData<SchematicClearAction>(entity);
                EventSchematicService.Clear(action.SchematicId.ToString());
            }

            ecb.RemoveComponent<SchematicLoadAction>(entity);
            ecb.RemoveComponent<SchematicClearAction>(entity);
            ecb.AddComponent<ActionCompletedEvent>(entity);
        });
    }
}

public sealed class SchematicActionSystem : VSystemBase<SchematicActionWork> { }

/// <summary>
/// Work implementation for processing stun actions.
/// </summary>
public sealed class StunActionWork : ISystemWork
{
    public void Build(ref EntityQueryBuilder builder)
    {
        builder.WithAll<PlayerStunAction>();
    }

    public void OnUpdate(SystemContext context)
    {
        var ecbSystem = context.System.World.GetExistingSystemManaged<EndSimulationEntityCommandBufferSystem>();
        if (ecbSystem == null) return;
        var ecb = ecbSystem.CreateCommandBuffer();

        context.ForEachEntity(context.Query, entity => {
            var action = context.EntityManager.GetComponentData<PlayerStunAction>(entity);
            if (action.TargetEntity.Exists())
            {
                if (PrefabHelper.TryGetPrefabGuid("Buff_General_Stun", out var stunGuid))
                {
                    action.TargetEntity.TryApplyBuff(stunGuid);
                }
            }
            ecb.RemoveComponent<PlayerStunAction>(entity);
            ecb.AddComponent<ActionCompletedEvent>(entity);
        });
    }
}

public sealed class StunActionSystem : VSystemBase<StunActionWork> { }
