using HarmonyLib;

/// <summary>
/// Periodic tick: hooks BuffSystem_Spawn_Server.OnUpdate which fires every server frame.
/// Drives zone detection, session ticks, and mode logic.
///
/// The one-shot initialization now lives in <see cref="InitializationPatch"/>; this
/// hook also acts as a safety-net fallback in case that patch did not fire.
/// </summary>
[HarmonyPatch]
internal static class ServerTickHook
{
    static DateTime _lastTick = DateTime.UtcNow;

    [HarmonyPatch(typeof(BuffSystem_Spawn_Server), nameof(BuffSystem_Spawn_Server.OnUpdate))]
    [HarmonyPostfix]
    static void OnUpdatePostfix()
    {
        try
        {
            // Retry initialization every tick until it succeeds.
            // This handles cases where the InitializationPatch target method
            // does not exist in the current game version, or the server world
            // is not yet ready when the patch first fires.
            if (!BattleLuckPlugin.IsInitialized)
            {
                try
                {
                    Core.InitializeAfterLoaded();
                }
                catch (Exception initEx)
                {
                    BattleLuckPlugin.LogWarning($"[BattleLuck] Init attempt failed (will retry): {initEx.Message}");
                }
                if (!BattleLuckPlugin.IsInitialized)
                    return;
            }

            var now = DateTime.UtcNow;
            float delta = (float)(now - _lastTick).TotalSeconds;
            _lastTick = now;

            // Clamp to avoid huge deltas on first tick or lag spikes
            if (delta > 2f) delta = 0.016f;

            ProjectMEventRouter.Instance?.RaiseBattleLuckServerTick(
                new BattleLuckServerTickEvent(delta, now));
            BattleLuckPlugin.ServerTick(delta);
        }
        catch (Exception ex)
        {
            BattleLuckPlugin.LogWarning($"[BattleLuck] Tick error: {ex.Message}");
        }
    }
}
