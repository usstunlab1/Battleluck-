using System;
using HarmonyLib;
using BattleLuck.Services.Runtime;
using BattleLuck.ECS.Events;
using ProjectM;
using Unity.Collections;
using Unity.Entities;

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
// These patches only listen — they never modify the original behavior.
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

                // Filter to player deaths only — NPC deaths do not raise PlayerDeathEvent.
                if (!died.Exists() || !died.IsPlayer()) continue;

                // Use Unix milliseconds for a stable, timezone-safe timestamp
                // that does not break across midnight.
                long timeUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

                ProjectMEventRouter.Instance?.RaisePlayerDeath(entity, new PlayerDeathEvent(died, killer, timeUnixMs));
            }
        }
        finally
        {
            entities.Dispose();
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Group 2 — ProjectM AI (DISABLED)
    //
    // AI frame-level telemetry patches are intentionally disabled.
    // BehaviourTreeSystem and AggroSystem fire every server frame and produce
    // only heartbeat events without actionable payloads. AI planning runs
    // through BattleLuck's controlled server tick instead.
    // ─────────────────────────────────────────────────────────────────────────
}
