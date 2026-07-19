using HarmonyLib;
using Unity.Collections;
using Unity.Entities;
using BattleLuck.Commands;
using BattleLuck.Models.Chat;
using BattleLuck.Services.Chat;
using ProjectM.Network;
using System;
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
            // A void-returning prefix is equivalent to `return true`, allowing the original method to run.
            // We must not block the original OnUpdate, or native chat will break.
            if (!VRisingCore.IsReady) return;

            // The query name is from decompiling the game.
            var entities = __instance.__query_661171423_0.ToEntityArray(Allocator.Temp);
            try
            {
                foreach (var entity in entities)
                {
                    if (!entity.TryGetComponent(out ChatMessageEvent chatEvent) || !entity.TryGetComponent(out FromCharacter fromCharacter)) continue;
                    if (!fromCharacter.User.TryGetComponent(out User user)) continue;

                    var steamId = user.PlatformId;
                    var message = chatEvent.MessageText.ToString();

                    // Highest priority: intercept non-command messages for players in the AI channel.
                    if (AiChannelState.IsInAiChannel(steamId) && !message.StartsWith("."))
                    {
                        HandleAiChannelMessage(steamId, user, message, entity);
                        // HandleAiChannelMessage destroys the entity, so we are done with it.
                        continue;
                    }

                    // Next: intercept all commands (e.g., .ai, .blch) for any player.
                    if (message.StartsWith("."))
                    {
                        BattleLuckCommandDispatcher.TryDispatchFromChatEvent(entity, chatEvent);
                        // Destroy the entity so the original system doesn't process it, preventing duplicate command handling or chat spam.
                        VRisingCore.EntityManager.DestroyEntity(entity);
                        continue;
                    }

                    // If we reach here, it's a native, non-command message.
                    // We do nothing and let the original OnUpdate handle it.
                }
            }
            finally
            {
                entities.Dispose();
            }
        }

        private static void HandleAiChannelMessage(ulong steamId, User sender, string message, Entity originalEntity)
        {
            // Check for active request to prevent spamming the AI provider.
            if (!AiChannelState.TryBeginRequest(steamId))
            {
                NotificationHelper.NotifyPlayer(sender, "Your previous AI request is still processing.", NotificationHelper.NotificationLevel.Warning);
                VRisingCore.EntityManager.DestroyEntity(originalEntity);
                return;
            }

            // Broadcast player message to all AI channel members (blue).
            // This is safe to do directly as we are on the main thread here.
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

            // Handle the AI query asynchronously on a background thread.
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
                        // Queue the response to be sent on the main thread to avoid race conditions with ECS.
                        MainThreadDispatcher.Enqueue(() =>
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
                        });
                    }
                }
                catch (Exception ex)
                {
                    // Also queue error notifications to the main thread.
                    MainThreadDispatcher.Enqueue(() =>
                    {
                        BattleLuckPlugin.LogWarning($"[AI Channel] Request failed for {steamId}: {ex.Message}");
                        if (VRisingCore.GetPlayerEntityBySteamId(steamId).IsValidPlayer(out var userToNotify))
                        {
                            NotificationHelper.NotifyPlayer(userToNotify, "Sorry, I encountered an error processing your request.", NotificationHelper.NotificationLevel.Error);
                        }
                    });
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