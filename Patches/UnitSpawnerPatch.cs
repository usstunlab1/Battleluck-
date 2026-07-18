using HarmonyLib;
using UnityEngine;

/// <summary>
/// Harmony patch on UnitSpawnerReactSystem.OnUpdate to catch spawned units
/// and execute post-spawn callbacks. Pattern from VAMP UnitSpawnerPatch.
///
/// SpawnService assigns a unique durationKey as the LifeTime.Duration,
/// which this patch matches to execute the correct callback and set the real duration.
/// </summary>
[HarmonyPatch]
internal static class UnitSpawnerPatch
{
    internal static readonly Dictionary<long, (float actualDuration, Action<Entity> Actions)> PostActions = new();

    [HarmonyPatch(typeof(UnitSpawnerReactSystem), nameof(UnitSpawnerReactSystem.OnUpdate))]
    [HarmonyPrefix]
    static void Prefix(UnitSpawnerReactSystem __instance)
    {
        if (PostActions.Count == 0) return;

        try
        {
            if (__instance.EntityQueries == null || __instance.EntityQueries.Length == 0)
                return;

            // Collect matched (entity, callback) pairs WHILE iterating the live query
            // array, but DO NOT run the callbacks here. Post-spawn callbacks perform
            // structural ECS changes (AddComponent/RemoveComponent); doing that while a
            // ToEntityArray(Temp) from the spawner system's own query is still alive
            // invalidates the chunk layout the array points into -> native use-after-free
            // crash (uncatchable by managed try/catch). Setting LifeTime is a non-structural
            // SetComponentData and is safe to do inline.
            var pending = new List<(Entity entity, Action<Entity> actions)>();
            var entities = __instance.EntityQueries[0].ToEntityArray(Allocator.Temp);
            try
            {
                foreach (var entity in entities)
                {
                    if (!entity.Has<LifeTime>()) continue;

                    var lifetimeComp = entity.Read<LifeTime>();
                    var durationKey = (long)Mathf.Round(lifetimeComp.Duration);

                    if (PostActions.TryGetValue(durationKey, out var unitData))
                    {
                        var (actualDuration, actions) = unitData;
                        PostActions.Remove(durationKey);

                        var endAction = actualDuration <= 0 ? LifeTimeEndAction.None : LifeTimeEndAction.Destroy;
                        entity.Write(new LifeTime
                        {
                            Duration = actualDuration,
                            EndAction = endAction
                        });

                        pending.Add((entity, actions));
                    }
                }
            }
            finally
            {
                entities.Dispose();
            }

            // Now that the live query array is disposed, it is safe to run the callbacks
            // that perform structural changes on the freshly spawned entities.
            foreach (var (entity, actions) in pending)
            {
                try
                {
                    actions(entity);
                }
                catch (Exception ex)
                {
                    BattleLuckPlugin.LogWarning($"[UnitSpawnerPatch] Post-spawn callback error for {entity.Index}:{entity.Version}: {ex}");
                }
            }
        }
        catch (Exception e)
        {
            BattleLuckPlugin.LogWarning($"[UnitSpawnerPatch] Error: {e.Message}");
        }
    }

    /// <summary>Generate a unique key that won't collide with existing entries.</summary>
    internal static long NextKey()
    {
        var rng = new System.Random();
        long key;
        int attempts = 10;
        do
        {
            key = rng.Next(10000) * 3;
            attempts--;
            if (attempts < 0)
                throw new Exception("Failed to generate unique UnitSpawner key");
        } while (PostActions.ContainsKey(key));
        return key;
    }
}
