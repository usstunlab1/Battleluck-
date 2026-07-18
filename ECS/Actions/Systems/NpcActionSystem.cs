using BattleLuck.ECS.Actions.Components;
using BattleLuck.Services.Npc;

namespace BattleLuck.ECS.Actions.Systems;

/// <summary>Executes generic NPC ECS intents through the single NpcControlService registry.</summary>
public sealed class NpcActionWork : ISystemWork
{
    public void Build(ref EntityQueryBuilder builder) => builder.WithAny<
        NpcSpawnAction, NpcDespawnAction, NpcFollowAction, NpcHoldAction,
        NpcGotoAction, NpcAggroAction, NpcReleaseAction>();

    public void OnUpdate(SystemContext context)
    {
        var service = BattleLuckPlugin.NpcService;
        var ecbSystem = context.System.World.GetExistingSystemManaged<EndSimulationEntityCommandBufferSystem>();
        if (service == null || ecbSystem == null) return;
        var ecb = ecbSystem.CreateCommandBuffer();

        context.ForEachEntity(context.Query, request =>
        {
            if (context.EntityManager.HasComponent<NpcSpawnAction>(request))
            {
                var action = context.EntityManager.GetComponentData<NpcSpawnAction>(request);
                var name = action.PrefabName.ToString();
                var prefab = PrefabHelper.GetPrefabGuidDeep(name);
                if (prefab.HasValue)
                {
                    var count = Math.Clamp(action.Count, 1, 50);
                    for (var i = 0; i < count; i++)
                    {
                        var position = action.Position + new Unity.Mathematics.float3(i * 2f, 0f, 0f);
                        new SpawnController().SpawnWithCallback(prefab.Value, position, 0f, spawned =>
                            service.RegisterNpc("_ecs_", null, name, prefab.Value, spawned, position, action.HomeRadius));
                    }
                }
            }
            else if (TryResolveTarget(context, service, request, out var npcId))
            {
                if (context.EntityManager.HasComponent<NpcDespawnAction>(request))
                    service.Despawn(npcId);
                else if (context.EntityManager.HasComponent<NpcFollowAction>(request))
                {
                    var action = context.EntityManager.GetComponentData<NpcFollowAction>(request);
                    service.Follow(npcId, action.FollowTarget, action.FollowRange, action.LeashRange);
                }
                else if (context.EntityManager.HasComponent<NpcHoldAction>(request))
                    service.Hold(npcId, context.EntityManager.GetComponentData<NpcHoldAction>(request).Radius);
                else if (context.EntityManager.HasComponent<NpcGotoAction>(request))
                {
                    var action = context.EntityManager.GetComponentData<NpcGotoAction>(request);
                    service.GoTo(npcId, action.TargetPosition, action.ArrivalRange);
                }
                else if (context.EntityManager.HasComponent<NpcAggroAction>(request))
                {
                    var action = context.EntityManager.GetComponentData<NpcAggroAction>(request);
                    service.Aggro(npcId, action.AggroTarget, action.AggroRange, action.LeashRange);
                }
                else if (context.EntityManager.HasComponent<NpcReleaseAction>(request))
                    service.Release(npcId);
            }

            ecb.RemoveComponent<NpcSpawnAction>(request);
            ecb.RemoveComponent<NpcDespawnAction>(request);
            ecb.RemoveComponent<NpcFollowAction>(request);
            ecb.RemoveComponent<NpcHoldAction>(request);
            ecb.RemoveComponent<NpcGotoAction>(request);
            ecb.RemoveComponent<NpcAggroAction>(request);
            ecb.RemoveComponent<NpcReleaseAction>(request);
            ecb.AddComponent<ActionCompletedEvent>(request);
        });
    }

    static bool TryResolveTarget(SystemContext context, NpcControlService service, Entity request, out string npcId)
    {
        npcId = "";
        Entity target = Entity.Null;
        if (context.EntityManager.HasComponent<NpcDespawnAction>(request))
        {
            var action = context.EntityManager.GetComponentData<NpcDespawnAction>(request);
            npcId = action.NpcId.ToString();
            target = action.TargetEntity;
        }
        else if (context.EntityManager.HasComponent<NpcFollowAction>(request)) target = context.EntityManager.GetComponentData<NpcFollowAction>(request).TargetEntity;
        else if (context.EntityManager.HasComponent<NpcHoldAction>(request)) target = context.EntityManager.GetComponentData<NpcHoldAction>(request).TargetEntity;
        else if (context.EntityManager.HasComponent<NpcGotoAction>(request)) target = context.EntityManager.GetComponentData<NpcGotoAction>(request).TargetEntity;
        else if (context.EntityManager.HasComponent<NpcAggroAction>(request)) target = context.EntityManager.GetComponentData<NpcAggroAction>(request).TargetEntity;
        else if (context.EntityManager.HasComponent<NpcReleaseAction>(request))
        {
            var action = context.EntityManager.GetComponentData<NpcReleaseAction>(request);
            npcId = action.NpcId.ToString();
            target = action.TargetEntity;
        }

        if (!string.IsNullOrWhiteSpace(npcId) && service.TryGet(npcId, out _)) return true;
        if (target.Exists() && service.TryGetByEntity(target, out var entry))
        {
            npcId = entry.NpcId;
            return true;
        }
        return false;
    }
}

public sealed class NpcActionSystem : VSystemBase<NpcActionWork> { }
