using BattleLuck.Core;
using BattleLuck.ECS.Actions.Components;
using BattleLuck.Services;

namespace BattleLuck.ECS.Actions.Systems;

// [Unity.Entities.UpdateInGroup(typeof(Unity.Entities.SimulationSystemGroup))]
public partial class IntentExecutorSystem : SystemBase
{
    private EntityQuery _intentQuery;
    private SessionController _sessionController;

    public override void OnCreate()
    {
        _intentQuery = GetEntityQuery(new EntityQueryDesc
        {
            All = new ComponentType[] { ComponentType.ReadWrite<ActionIntent>() },
            None = new ComponentType[] { ComponentType.ReadOnly<ConsumedTag>() }
        });
        
        // In a real project, SessionController would be injected or accessed via a singleton/service locator
        // For this task, I'll assume it's available via BattleLuckPlugin or similar if needed,
        // but I'll try to use available static helpers where possible.
    }

    public override void OnUpdate()
    {
        var ecb = new EntityCommandBuffer(Allocator.Temp);
        var em = EntityManager;

        var intents = _intentQuery.ToEntityArray(Allocator.Temp);
        try
        {
            foreach (var entity in intents)
            {
                var intent = em.GetComponentData<ActionIntent>(entity);
                var actionId = intent.ActionId.ToString();
                
                if (actionId == "act:event.respawn_in_arena")
                {
                    ExecuteRespawn(intent.Caller, intent.EventEntity);
                    ecb.AddComponent<ConsumedTag>(entity);
                }
                else if (actionId == "act:snapshot.restore")
                {
                    ExecuteRollbackSnapshot(intent.Caller, intent.EventEntity);
                    ecb.AddComponent<ConsumedTag>(entity);
                }
                else if (actionId == "act:event.rollback.teleport_out")
                {
                    ExecuteTeleportOut(intent.Caller, intent.EventEntity);
                    ecb.AddComponent<ConsumedTag>(entity);
                }
                else if (actionId == "act:event.chest.unlock")
                {
                    ExecuteChestUnlock(intent.EventEntity, intent.Caller);
                    ecb.AddComponent<ConsumedTag>(entity);
                }
                else if (actionId == "act:prefab.spawn")
                {
                    ExecutePrefabSpawn(intent);
                    ecb.AddComponent<ConsumedTag>(entity);
                }
            }
        }
        finally
        {
            intents.Dispose();
        }

        ecb.Playback(em);
        ecb.Dispose();
    }

    private void ExecutePrefabSpawn(ActionIntent intent)
    {
        var ecb = new EntityCommandBuffer(Allocator.Temp);
        var prefabGuid = new Stunlock.Core.PrefabGUID(int.Parse(intent.PrefabGuid.ToString()));
        
        SafeSpawnHelper.SafeSpawn(ecb, prefabGuid, intent.Position, intent.EventEntity, intent.ActionId.ToString());
        
        ecb.Playback(EntityManager);
        ecb.Dispose();
    }

    private void ExecuteChestUnlock(Entity sessionEntity, Entity caller)
    {
        var em = EntityManager;
        // Logic to find and unlock the chest associated with this session/caller
        // This is a simplified version
        var chestQuery = em.CreateEntityQuery(ComponentType.ReadWrite<EventChest>());
        var chests = chestQuery.ToEntityArray(Allocator.Temp);
        try
        {
            foreach (var chestEnt in chests)
            {
                var chest = em.GetComponentData<EventChest>(chestEnt);
                if (chest.SessionEntity == sessionEntity && chest.Locked)
                {
                    chest.Locked = false;
                    em.SetComponentData(chestEnt, chest);
                    BattleLuckPlugin.LogInfo($"[IntentExecutor] Chest unlocked for session {sessionEntity.Index}");
                    
                    // Award gold or items if applicable
                    if (caller.Exists() && caller.IsPlayer())
                    {
                         // Using a placeholder reward for now
                         caller.TryGiveItem(new PrefabGUID(-774640153), 100); // Gold
                    }
                }
            }
        }
        finally
        {
            chests.Dispose();
        }
    }

    private void ExecuteRespawn(Entity player, Entity sessionEntity)
    {
        if (!player.Exists()) return;
        
        var steamId = player.GetSteamId();
        var session = BattleLuckPlugin.Session?.GetSessionByEntity(sessionEntity);
        if (session != null)
        {
            BattleLuckPlugin.Session.QueueImmediateArenaRespawn(session, steamId, player);
        }
    }

    private void ExecuteRollbackSnapshot(Entity player, Entity sessionEntity)
    {
        if (!player.Exists()) return;
        var steamId = player.GetSteamId();
        var session = BattleLuckPlugin.Session?.GetSessionByEntity(sessionEntity);
        if (session != null)
        {
            // The existing ForceExitEliminatedPlayer handles snapshot restore logic
            // but we want to do it via intent.
            // For now, we call the existing logic.
            BattleLuckPlugin.Session.ForceExitEliminatedPlayer(session, steamId, player);
        }
    }

    private void ExecuteTeleportOut(Entity player, Entity sessionEntity)
    {
        // Handled by ForceExitEliminatedPlayer in this implementation
    }
}

// [Unity.Entities.UpdateAfter(typeof(IntentExecutorSystem))]
public partial class IntentCleanupSystem : SystemBase
{
    public override void OnUpdate()
    {
        var ecb = new EntityCommandBuffer(Allocator.Temp);
        var query = GetEntityQuery(ComponentType.ReadOnly<ConsumedTag>());
        var entities = query.ToEntityArray(Allocator.Temp);
        try
        {
            foreach (var entity in entities)
            {
                ecb.DestroyEntity(entity);
            }
        }
        finally
        {
            entities.Dispose();
        }
        ecb.Playback(EntityManager);
        ecb.Dispose();
    }
}
