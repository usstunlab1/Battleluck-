using HarmonyLib;
using BattleLuck.ECS.Events;
using BattleLuck.Services.Runtime;
using ProjectM.Gameplay.Systems;
using ProjectM.Sequencer;

namespace BattleLuck.Patches;

/// <summary>
/// DISABLED: AI frame-level telemetry generates events every ProjectM frame
/// without actionable payloads. AI planning runs through BattleLuck's
/// controlled server tick instead.
/// </summary>
// [HarmonyPatch]
internal static class AiTickSequencePatches
{
    // [HarmonyPatch(typeof(CreateGameplayEventOnTickSystem), nameof(CreateGameplayEventOnTickSystem.OnUpdate))]
    // [HarmonyPostfix]
    static void CreateGameplayEventOnTickSystem_OnUpdate()
    {
        if (!VRisingCore.IsReady)
            return;

        ProjectMEventRouter.Instance?.RaiseProjectMRuntimeTick(
            new ProjectMRuntimeTickEvent(
                "ProjectM.Gameplay.Systems.CreateGameplayEventOnTickSystem",
                DateTime.UtcNow));
    }

    // [HarmonyPatch(typeof(SequencerUpdateGroup), nameof(SequencerUpdateGroup.OnUpdate))]
    // [HarmonyPostfix]
    static void SequencerUpdateGroup_OnUpdate()
    {
        if (!VRisingCore.IsReady)
            return;

        // The update group does not expose a stable step payload across server
        // builds. Emit a typed heartbeat; event sequences carry their own
        // validated step/tick state through EventRuntimeController.
        ProjectMEventRouter.Instance?.RaiseSequencer(
            new SequencerEvent("ProjectM.Sequencer.SequencerUpdateGroup", 0, 0));
    }
}