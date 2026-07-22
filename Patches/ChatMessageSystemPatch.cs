using BattleLuck.Commands;
using HarmonyLib;
using ProjectM.Network;
using Unity.Collections;
using Unity.Entities;

namespace BattleLuck.Patches;

[HarmonyPatch(typeof(ProjectM.ChatMessageSystem), nameof(ProjectM.ChatMessageSystem.OnUpdate))]
public static class ChatMessageSystemPatch
{
    [HarmonyPrefix]
    static void OnUpdatePrefix(ProjectM.ChatMessageSystem __instance)
    {
        if (!VRisingCore.IsReady)
            return;

        var entities = __instance.__query_661171423_0.ToEntityArray(Allocator.Temp);

        try
        {
            foreach (var eventEntity in entities)
            {
                try
                {
                    if (!eventEntity.Exists() ||
                        !eventEntity.TryGetComponent(out ChatMessageEvent chatEvent) ||
                        !eventEntity.TryGetComponent(out FromCharacter fromCharacter) ||
                        !fromCharacter.User.TryGetComponent(out User user))
                    {
                        continue;
                    }

                    // Copy everything required before command dispatch or event removal.
                    // The dispatcher may consume ChatMessageEvent, and AI routing destroys
                    // only this specific event entity.
                    var copiedChatEvent = chatEvent;
                    var copiedFromCharacter = fromCharacter;
                    var copiedUser = user;
                    var message = copiedChatEvent.MessageText.ToString();
                    var steamId = copiedUser.PlatformId;
                    _ = copiedFromCharacter;

                    if (steamId == 0 || string.IsNullOrWhiteSpace(message))
                        continue;

                    // Dot commands always get first refusal. A handled command is consumed
                    // by BattleLuckCommandDispatcher and must not also reach either AI path.
                    if (message.StartsWith(".", StringComparison.Ordinal) &&
                        BattleLuckCommandDispatcher.TryDispatchFromChatEvent(eventEntity, copiedChatEvent))
                    {
                        continue;
                    }

                    // Normal chat is intentionally untouched. Only registered dot
                    // commands can enter BattleLuck or the assistant.
                }
                catch (Exception ex)
                {
                    BattleLuckPlugin.LogWarning($"[Chat] Failed to process one chat event: {ex.Message}");
                }
            }
        }
        finally
        {
            entities.Dispose();
        }
    }

}
