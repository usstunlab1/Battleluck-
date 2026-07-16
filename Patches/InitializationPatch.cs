using HarmonyLib;
using ProjectM;
using ProjectM.Gameplay.WarEvents;

/// <summary>
/// One-shot initialization patch.
///
/// Hooks <see cref="WarEventRegistrySystem.RegisterWarEventEntities"/>, which
/// fires after the server world is fully ready (same pattern as Bloodcraft).
/// This is the single, earliest-safe place to construct BattleLuck services and
/// mirrors the guide's recommended one-shot <c>InitializationPatch</c> that calls
/// <c>Core.InitializeAfterLoaded()</c>.
/// </summary>
[HarmonyPatch]
internal static class InitializationPatch
{
    static bool _initialized;

    [HarmonyPatch(typeof(WarEventRegistrySystem), nameof(WarEventRegistrySystem.RegisterWarEventEntities))]
    [HarmonyPostfix]
    static void RegisterWarEventEntitiesPostfix()
    {
        if (_initialized)
            return;

        try
        {
            Core.InitializeAfterLoaded();
            _initialized = BattleLuckPlugin.IsInitialized;
        }
        catch (Exception ex)
        {
            BattleLuckPlugin.LogWarning($"[BattleLuck] Init failed: {ex.Message}");
        }
    }
}
