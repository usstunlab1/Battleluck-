using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using Stunlock.Core;
using System.Linq;
using ProjectM;

namespace BattleLuck.Utilities;

public static class SafeSpawnHelper
{
    public static Entity SafeSpawn(EntityCommandBuffer ecb, PrefabGUID prefabGuid, float3 pos, Entity parent, string actionId = "")
    {
        var em = VRisingCore.EntityManager;
        var prefabEntity = VRisingCore.GetPrefabEntityByGuid(prefabGuid);
        
        if (prefabEntity == Entity.Null)
        {
            BattleLuckPlugin.LogError($"SafeSpawn failed: prefab entity not found for {prefabGuid.GuidHash}");
            return Entity.Null;
        }

        bool isNpcIntent =
            actionId.Contains("npc", StringComparison.OrdinalIgnoreCase) ||
            actionId.Contains("boss", StringComparison.OrdinalIgnoreCase);
        bool isChestIntent =
            actionId.Contains("chest", StringComparison.OrdinalIgnoreCase);

        var category = BattleLuck.Services.Runtime.PrefabRegistryServiceReal.GetCategory(prefabGuid) ?? string.Empty;
        bool isUnitCategory =
            string.Equals(category, "Characters", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(category, "VBoss", StringComparison.OrdinalIgnoreCase);

        if (isNpcIntent && !em.HasComponent<UnitLevel>(prefabEntity) && !isUnitCategory)
        {
            BattleLuckPlugin.LogError($"SafeSpawn blocked: intent is '{actionId}' but prefab {prefabGuid.GuidHash} is not a unit (Category: {category}).");
            return Entity.Null;
        }

        if (isChestIntent && (em.HasComponent<UnitLevel>(prefabEntity) || isUnitCategory))
        {
            BattleLuckPlugin.LogError($"SafeSpawn blocked: intent is '{actionId}' (chest expected) but prefab {prefabGuid.GuidHash} is a unit (Category: {category}).");
            return Entity.Null;
        }

        var ent = ecb.Instantiate(prefabEntity);
        ecb.SetComponent(ent, new Translation { Value = pos });
        if (parent != Entity.Null)
        {
            ecb.AddComponent(ent, new Parent { Value = parent });
            ecb.AddComponent<LocalToParent>(ent);
        }
        return ent;
    }
}
