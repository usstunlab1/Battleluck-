using System;
using HarmonyLib;
using BattleLuck.Services.Runtime;
using BattleLuck.ECS.Events;
using ProjectM;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

// ─────────────────────────────────────────────────────────────────────────────
// ProjectMEventRouterPatches.cs — Harmony postfix patches that feed the router.
//
// Each patch targets the OnUpdate method of a ProjectM ECS system. After the
// original OnUpdate runs, we read the relevant data from the system's instance
// (or from static lookups) and raise the corresponding typed event on the
// ProjectMEventRouter.
//
// This file is auto-applied via `_harmony.PatchAll(typeof(ProjectMEventRouterPatches).Assembly)`
// in BattleLuckPlugin.Load(), after the router has been initialized.
//
// Phase 1: these patches only listen — they never modify the original behavior.
// ─────────────────────────────────────────────────────────────────────────────

[HarmonyPatch]
public static class ProjectMEventRouterPatches
{
    // ─────────────────────────────────────────────────────────────────────────
    // Group 1 — Death & Damage
    // ─────────────────────────────────────────────────────────────────────────

    // DeathEventListenerSystem
    [HarmonyPatch(typeof(DeathEventListenerSystem), nameof(DeathEventListenerSystem.OnUpdate))]
    [HarmonyPostfix]
    private static void DeathEventListenerSystem_OnUpdate(DeathEventListenerSystem __instance)
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
                float time = (float)DateTime.UtcNow.TimeOfDay.TotalSeconds; // Approximate time of event

                if (!died.Exists()) continue;

                ProjectMEventRouter.Instance?.RaisePlayerDeath(new PlayerDeathEvent(died, killer, time));
            }
        }
        finally
        {
            entities.Dispose();
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Group 2 — ProjectM AI
    // ─────────────────────────────────────────────────────────────────────────

    [HarmonyPatch(typeof(ProjectM.Behaviours.BehaviourTreeSystem), nameof(ProjectM.Behaviours.BehaviourTreeSystem.OnUpdate))]
    [HarmonyPostfix]
    private static void BehaviourTreeSystem_OnUpdate()
    {
        if (!VRisingCore.IsReady) return;

        ProjectMEventRouter.Instance?.RaiseAiGroupProjectMTick(
            new AiGroupProjectMTickEvent("ProjectM.Behaviours.BehaviourTreeSystem", DateTime.UtcNow));
    }

    [HarmonyPatch(typeof(ProjectM.AggroSystem), nameof(ProjectM.AggroSystem.OnUpdate))]
    [HarmonyPostfix]
    private static void AggroSystem_OnUpdate()
    {
        if (!VRisingCore.IsReady) return;

        ProjectMEventRouter.Instance?.RaiseAiGroupProjectMTick(
            new AiGroupProjectMTickEvent("ProjectM.AggroSystem", DateTime.UtcNow));
    }
}
