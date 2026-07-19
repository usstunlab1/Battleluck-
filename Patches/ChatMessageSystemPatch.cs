using HarmonyLib;
using Unity.Collections;
using Unity.Entities;
using BattleLuck.Commands;
using BattleLuck.Models.Chat;
using BattleLuck.Services.AI;
using BattleLuck.Services.Chat;
using HarmonyLib;
using ProjectM.Network;
using System;
using System.Threading.Tasks;
using static ProjectM.ChatMessageSystem;

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
                    if (!eventEntity.Exists() ||
                        !eventEntity.TryGetComponent(out ChatMessageEvent chatEvent) ||
                        !eventEntity.TryGetComponent(out FromCharacter fromCharacter) ||
                        !fromCharacter.User.TryGetComponent(out User user))
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

                    // Copy everything required before command dispatch or event removal.
                    // The dispatcher may consume ChatMessageEvent, and AI routing destroys
                    // only this specific event entity.
                    var copiedChatEvent = chatEvent;
                    var copiedFromCharacter = fromCharacter;
                    var copiedUser = user;
                    var message = GameChatAiBridge.ExtractMessage(copiedChatEvent);
                    var steamId = copiedUser.PlatformId;
                    var playerName = copiedUser.CharacterName.ToString();

                    _ = copiedFromCharacter;

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
        _ = playerName;

        if (!AiChannelState.TryBeginRequest(steamId))
        {
            AiChannelMessageService.SendStatus(
                steamId,
                "Your previous request is still processing.");
            return;
        }

        var cancellationToken = AiChannelState.GetRequestCancellationToken(steamId);

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

            // HandleDirectQuery does not currently accept a cancellation token. The
            // channel still owns one so disconnect/off/shutdown can suppress stale
            // continuations and future provider overloads can receive it directly.
            if (cancellationToken.IsCancellationRequested)
                return;

            if (!string.IsNullOrWhiteSpace(reply))
                AiChannelMessageService.BroadcastAiReply(reply);
        }
        catch (Exception ex)
        {
            BattleLuckPlugin.LogWarning(
                $"[AI Channel] Request failed for {steamId}: {ex.Message}");

            if (!cancellationToken.IsCancellationRequested)
            {
                AiChannelMessageService.SendError(
                    steamId,
                    "The AI provider is temporarily unavailable.");
            }
        }
        finally
        {
            AiChannelState.EndRequest(steamId);
        }
    }
}
