using HarmonyLib;
using Unity.Collections;
using Unity.Entities;
using BattleLuck.Services.AI;
using BattleLuck.Commands;
using BattleLuck.Models.Chat;
using BattleLuck.Services.Chat;
using ProjectM.Network;
using System.Threading.Tasks;
using static ProjectM.ChatMessageSystem;

namespace BattleLuck.Patches
{
    [HarmonyPatch(typeof(ProjectM.ChatMessageSystem), nameof(ProjectM.ChatMessageSystem.OnUpdate))]
    public static class ChatMessageSystemPatch
    {
        [HarmonyPrefix]
        public static void OnUpdatePrefix(ProjectM.ChatMessageSystem __instance)
        {
            if (!VRisingCore.IsReady) return;

            var entities = __instance.__query_661171423_0.ToEntityArray(Allocator.Temp);

            foreach (var entity in entities)
            {
                if (!entity.TryGetComponent(out ChatMessageEvent chatEvent) || !entity.TryGetComponent(out FromCharacter fromCharacter)) continue;
                if (!fromCharacter.User.TryGetComponent(out User user)) continue;

                var steamId = user.PlatformId;
                var message = chatEvent.MessageText.ToString();

                // Handle .blch commands first
                if (message.StartsWith(".blch"))
                {
                    BattleLuckCommandDispatcher.TryDispatchFromChatEvent(entity, chatEvent);
                    continue;
                }

                // Check if player is in AI channel
                if (AiChannelState.GetChannel(steamId) == BattleLuckChatChannel.AI)
                {
                    // For AI channel: handle non-command messages
                    if (!message.StartsWith("."))
                    {
                        HandleAiChannelMessage(steamId, user, message, entity);
                        // Entity is destroyed in HandleAiChannelMessage
                        continue;
                    }
                    // Commands in AI channel still go through dispatcher
                    else
                    {
                        BattleLuckCommandDispatcher.TryDispatchFromChatEvent(entity, chatEvent);
                        continue;
                    }
                }

                // For Native channel: let vanilla system handle the message unchanged
                // Also handle other dot commands
                if (message.StartsWith(".") && !message.StartsWith(".blch"))
                {
                    BattleLuckCommandDispatcher.TryDispatchFromChatEvent(entity, chatEvent);
                }
            }

            entities.Dispose();
        }

        private static void HandleAiChannelMessage(ulong steamId, User sender, string message, Entity originalEntity)
        {
            // Check for active request
            if (!AiChannelState.TryBeginRequest(steamId))
            {
                NotificationHelper.NotifyPlayer(sender, "Your previous AI request is still processing.", NotificationHelper.NotificationLevel.Warning);
                VRisingCore.EntityManager.DestroyEntity(originalEntity);
                return;
            }

            // Broadcast player message to all AI channel members (blue)
            var playerName = sender.CharacterName.ToString();
            var formattedMessage = NotificationHelper.ColorizeText($"[AI] {playerName}: {message}", "#66E3FF");
            foreach (var memberId in AiChannelState.GetAiChannelMembers())
            {
                var memberEntity = VRisingCore.GetPlayerEntityBySteamId(memberId);
                if (memberEntity.Exists() && memberEntity.IsValidPlayer(out var memberUser))
                {
                    NotificationHelper.NotifyPlayerRaw(memberUser, formattedMessage);
                }
            }

            // Handle the AI query asynchronously
            _ = Task.Run(async () =>
            {
                try
                {
                    var response = await BattleLuckPlugin.AIAssistant.HandleDirectQuery(
                        steamId,
                        message,
                        source: "native_ai_channel",
                        broadcastToInGameChat: false);

                    if (!string.IsNullOrWhiteSpace(response))
                    {
                        var formattedResponse = NotificationHelper.ColorizeText($"[AI] BattleLuck: {response}", "#4DA6FF");
                        foreach (var memberId in AiChannelState.GetAiChannelMembers())
                        {
                            var memberEntity = VRisingCore.GetPlayerEntityBySteamId(memberId);
                            if (memberEntity.Exists() && memberEntity.IsValidPlayer(out var memberUser))
                            {
                                NotificationHelper.NotifyPlayerRaw(memberUser, formattedResponse);
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    BattleLuckPlugin.LogWarning($"[AI Channel] Request failed for {steamId}: {ex.Message}");
                    NotificationHelper.NotifyPlayer(sender, "Sorry, I encountered an error processing your request.", NotificationHelper.NotificationLevel.Error);
                }
                finally
                {
                    AiChannelState.EndRequest(steamId);
                }
            });

            // Destroy the original message to prevent native chat broadcast
            VRisingCore.EntityManager.DestroyEntity(originalEntity);
        }
    }
}