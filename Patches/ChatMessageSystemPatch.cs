using HarmonyLib;
using Unity.Collections;
using Unity.Entities;
using BattleLuck.Services.AI;
using BattleLuck.Commands;

namespace BattleLuck.Patches;

[HarmonyPatch(typeof(ProjectM.ChatMessageSystem), nameof(ProjectM.ChatMessageSystem.OnUpdate))]
[HarmonyPriority(Priority.VeryHigh)]
public static class ChatMessageSystemPatch
{
    // Read chat events before the game system consumes/destroys them.
    [HarmonyPrefix]
    static void OnUpdatePrefix(ProjectM.ChatMessageSystem __instance)
    {
        if (!VRisingCore.IsReady)
            return;

        try
        {
            // Use the generated 1.1 interop query directly. Reflection against
            // IL2CPP wrappers is unreliable and discouraged by the modding guide.
            var events = __instance.__query_661171423_0.ToEntityArray(Allocator.Temp);
            try
            {
                foreach (var entity in events)
                {
                    if (!entity.Exists())
                        continue;

                    if (!entity.TryGetComponent(out ChatMessageEvent chatEvent))
                        continue;

                    if (BattleLuckCommandDispatcher.TryDispatchFromChatEvent(entity, chatEvent))
                        continue;

                    if (BattleLuckPlugin.AIAssistant != null)
                        GameChatAiBridge.HandleChatEvent(entity, chatEvent);
                }
            }
            finally
            {
                SafeDispose(events);
            }
        }
        catch (Exception ex)
        {
            BattleLuckPlugin.LogWarning($"[GameChatAI] Chat patch failed: {ex.Message}");
        }
    }

    static void SafeDispose(NativeArray<Entity> events)
    {
        if (!events.IsCreated)
            return;

        try
        {
            events.Dispose();
        }
        catch (EntryPointNotFoundException ex)
        {
            // Some server/interop combinations expose a mismatched native
            // Dispose entry point. Allocator.Temp is reclaimed at frame end.
            BattleLuckPlugin.LogWarning($"[GameChatAI] NativeArray cleanup unavailable: {ex.Message}");
        }
    }

}
