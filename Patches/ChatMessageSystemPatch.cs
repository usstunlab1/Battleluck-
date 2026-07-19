using BattleLuck.Commands;
using BattleLuck.Models.Chat;
using BattleLuck.Services.AI;
using BattleLuck.Services.Chat;
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
                    var message = GameChatAiBridge.ExtractMessage(copiedChatEvent);
                    var steamId = copiedUser.PlatformId;
                    var playerName = copiedUser.CharacterName.ToString();

                    _ = copiedFromCharacter; // Documents that FromCharacter was copied intentionally.

                    if (steamId == 0 || string.IsNullOrWhiteSpace(message))
                        continue;

                    // Dot commands always get first refusal. A handled command is consumed
                    // by BattleLuckCommandDispatcher and must not also reach either AI path.
                    if (message.StartsWith('.', StringComparison.Ordinal) &&
                        BattleLuckCommandDispatcher.TryDispatchFromChatEvent(eventEntity, copiedChatEvent))
                    {
                        continue;
                    }

                    if (AiChannelState.GetChannel(steamId) != BattleLuckChatChannel.AI)
                    {
                        // Preserve .ai, @ai, ai:, interactive sessions, and every existing
                        // GameChatAiBridge behavior. Do not destroy or rewrite native chat.
                        if (BattleLuckPlugin.AIAssistant != null)
                            GameChatAiBridge.HandleChatEvent(eventEntity, copiedChatEvent);

                        continue;
                    }

                    // Dedicated AI mode consumes only this event. Native OnUpdate remains
                    // enabled and continues processing every other Global/Local/Team/Whisper
                    // event normally.
                    if (VRisingCore.EntityManager.Exists(eventEntity))
                        VRisingCore.EntityManager.DestroyEntity(eventEntity);

                    if (string.IsNullOrWhiteSpace(playerName))
                        playerName = steamId.ToString();

                    AiChannelMessageService.BroadcastPlayerEcho(playerName, message);
                    _ = HandleAiRequestAsync(steamId, playerName, message);
                }
                catch (Exception ex)
                {
                    BattleLuckPlugin.LogWarning(
                        $"[AI Channel] Failed to process one chat event: {ex.Message}");
                }
            }
        }
        finally
        {
            entities.Dispose();
        }
    }

    static async Task HandleAiRequestAsync(
        ulong steamId,
        string playerName,
        string request)
    {
        _ = playerName; // Kept in the observed signature for diagnostics and future audit context.

        if (!AiChannelState.TryBeginRequest(steamId))
        {
            AiChannelMessageService.SendStatus(
                steamId,
                "Your previous request is still processing.");
            return;
        }

        try
        {
            var ai = BattleLuckPlugin.AIAssistant;
            if (ai == null)
            {
                AiChannelMessageService.SendError(
                    steamId,
                    "The AI provider is not available.");
                return;
            }

            var reply = await ai.HandleDirectQuery(
                steamId,
                request,
                source: "native_ai_channel",
                broadcastToInGameChat: false).ConfigureAwait(false);

            if (!string.IsNullOrWhiteSpace(reply))
                AiChannelMessageService.BroadcastAiReply(reply);
        }
        catch (Exception ex)
        {
            BattleLuckPlugin.LogWarning(
                $"[AI Channel] Request failed for {steamId}: {ex.Message}");

            AiChannelMessageService.SendError(
                steamId,
                "The AI provider is temporarily unavailable.");
        }
        finally
        {
            AiChannelState.EndRequest(steamId);
        }
    }
}
