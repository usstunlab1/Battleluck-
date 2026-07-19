using System.Diagnostics;
using HarmonyLib;
using ProjectM;

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
    static readonly Stopwatch Stopwatch = Stopwatch.StartNew();
    static double _lastTickSeconds;
    static double _nextInitRetryAtSeconds;
    const double InitRetryIntervalSeconds = 5.0;
    const float MaxDeltaSeconds = 0.5f;

    [HarmonyPatch(typeof(BuffSystem_Spawn_Server), nameof(BuffSystem_Spawn_Server.OnUpdate))]
    [HarmonyPostfix]
    static void OnUpdatePostfix()
    {
        try
        {
            // Retry initialization at a controlled interval, not every frame.
            if (!BattleLuckPlugin.IsInitialized)
            {
                var nowSec = Stopwatch.Elapsed.TotalSeconds;
                if (nowSec >= _nextInitRetryAtSeconds)
                {
                    try
                    {
                        Core.InitializeAfterLoaded();
                    }
                    catch (Exception initEx)
                    {
                        BattleLuckPlugin.LogWarning($"[BattleLuck] Init attempt failed (will retry in {InitRetryIntervalSeconds:F0}s): {initEx.ToString()}");
                    }

                    if (!BattleLuckPlugin.IsInitialized)
                    {
                        _nextInitRetryAtSeconds = nowSec + InitRetryIntervalSeconds;
                        return;
                    }

                    // Initialization succeeded — reset timing baseline so the
                    // first real tick does not report a multi-second delta.
                    _lastTickSeconds = Stopwatch.Elapsed.TotalSeconds;
                }
                else
                {
                    return;
                }
            }

            var elapsedSeconds = Stopwatch.Elapsed.TotalSeconds;
            var rawDelta = elapsedSeconds - _lastTickSeconds;
            _lastTickSeconds = elapsedSeconds;

            // Clamp to avoid huge deltas on first tick or lag spikes,
            // but preserve meaningful pauses up to the cap.
            float delta = (float)Math.Min(rawDelta, MaxDeltaSeconds);
            if (delta < 0f) delta = 0f;

            var nowUtc = DateTime.UtcNow;

            // Process runtime first, then publish tick telemetry so the event
            // describes a tick that BattleLuck actually processed.
            BattleLuckPlugin.ServerTick(delta);

            ProjectMEventRouter.Instance?.RaiseBattleLuckServerTick(
                new BattleLuckServerTickEvent(delta, nowUtc));
        }
        catch (Exception ex)
        {
            BattleLuckPlugin.LogWarning($"[BattleLuck] Tick error: {ex.ToString()}");
        }
    }
}
