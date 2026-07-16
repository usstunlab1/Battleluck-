using System;
using ProjectM;
using ProjectM.Network;
using Unity.Entities;
using Unity.Mathematics;

namespace BattleLuck.Commands;

public sealed class BattleLuckCommandContext
{
    public Entity SenderCharacterEntity { get; }
    public ulong SenderSteamId { get; }
    public string RawInput { get; }
    public string[] Args { get; }
    public bool IsAdmin { get; }
    public bool IsConsole { get; }
    public object? Event { get; }

    public BattleLuckCommandContext(Entity senderEntity, ulong steamId, string rawInput, string[] args, bool isAdmin = false, bool isConsole = false, object? @event = null)
    {
        SenderCharacterEntity = senderEntity;
        SenderSteamId = steamId;
        RawInput = rawInput ?? "";
        Args = args ?? Array.Empty<string>();
        IsAdmin = isAdmin;
        IsConsole = isConsole;
        Event = @event;
    }

    public string GetArg(int index) => index >= 0 && index < Args.Length ? Args[index] : "";
    public string GetArg(int index, string fallback) => index >= 0 && index < Args.Length && !string.IsNullOrEmpty(Args[index]) ? Args[index] : fallback;

    public void Reply(string message)
    {
        try
        {
            if (IsConsole)
            {
                BattleLuckPlugin.LogInfo($"[Cmd] {message}");
                return;
            }

            if (!SenderCharacterEntity.Exists())
            {
                BattleLuckPlugin.LogInfo($"[Cmd] (entity-gone) {message}");
                return;
            }

            var userEntity = SenderCharacterEntity.Has<PlayerCharacter>()
                ? SenderCharacterEntity.Read<PlayerCharacter>().UserEntity
                : SenderCharacterEntity;
            if (userEntity.Exists())
            {
                var user = userEntity.Read<User>();
                NotificationHelper.NotifyPlayerRaw(user, message);
            }
            else
            {
                BattleLuckPlugin.LogInfo($"[Cmd] -> {SenderSteamId}: {message}");
            }
        }
        catch (Exception ex)
        {
            BattleLuckPlugin.LogWarning($"[Cmd] Reply failed: {ex.Message}");
        }
    }
}
