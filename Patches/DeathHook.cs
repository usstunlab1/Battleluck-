using HarmonyLib;
using ProjectM;
using Unity.Collections;
using Unity.Entities;

/// <summary>
/// Harmony patch on DeathEventListenerSystem.OnUpdate to detect kills
/// inside BattleLuck zones and route them to active game modes.
/// </summary>
[HarmonyPatch]
internal static class DeathHook
{
    /// <summary>Event raised when any entity dies on the server.</summary>
    public static event Action<Entity, Entity>? OnDeath; // (died, killer)

    [HarmonyPatch(typeof(DeathEventListenerSystem), nameof(DeathEventListenerSystem.OnUpdate))]
    [HarmonyPostfix]
    static void OnUpdatePostfix(DeathEventListenerSystem __instance)
    {
        if (!VRisingCore.IsReady) return;

        var entities = __instance._DeathEventQuery.ToEntityArray(Allocator.Temp);
        try
        {
            foreach (var entity in entities)
            {
                if (!entity.TryGetComponent(out DeathEvent deathEvent)) continue;

                Entity died = deathEvent.Died;
                Entity killer = deathEvent.Killer;

                if (!died.Exists()) continue;

                try
                {
                    OnDeath?.Invoke(died, killer);
                }
                catch (Exception ex)
                {
                    BattleLuckPlugin.LogWarning($"[DeathHook] Handler error: {ex.Message}");
                }
            }
        }
        finally
        {
            entities.Dispose();
        }
    }
}
