using System;
using System.Reflection;
using ProjectM;
using Unity.Entities;
using VampireCommandFramework;

namespace BattleLuck.Commands;

/// <summary>
/// Compatibility helpers for the VCF command context used by the installed framework version.
/// Keeps sender resolution in one place while legacy VCF commands are migrated.
/// </summary>
public static class CommandContextExtensions
{
    public static Entity GetSenderCharacterEntity(this ChatCommandContext context)
    {
        // Current VCF exposes the authoritative server-side character through
        // the command event. Prefer it over the older reflected Character
        // compatibility property, which can be missing or return Entity.Null.
        try
        {
            var eventCharacter = context.Event.SenderCharacterEntity;
            if (eventCharacter != Entity.Null && eventCharacter.Exists())
                return eventCharacter;
        }
        catch
        {
            // Fall through to compatibility lookup for older VCF builds.
        }

        try
        {
            var characterProperty = context.GetType().GetProperty("Character", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (characterProperty?.GetValue(context) is Entity entity && entity != Entity.Null && entity.Exists())
                return entity;
        }
        catch
        {
            // Fall through to return null entity
        }
        return Entity.Null;
    }

    public static ulong GetSenderSteamId(this ChatCommandContext context)
    {
        try
        {
            var platformId = context.Event.User.PlatformId;
            if (platformId != 0)
                return platformId;
        }
        catch
        {
            // Fall through to the resolved character/user entity.
        }

        return context.GetSenderCharacterEntity().GetSteamId();
    }

    public static bool TryGetSenderIdentity(this ChatCommandContext context, out Entity character, out ulong steamId)
    {
        character = context.GetSenderCharacterEntity();
        steamId = context.GetSenderSteamId();
        return character != Entity.Null && character.Exists() && character.IsPlayer() && steamId != 0;
    }

    public static bool TryGetSenderCharacterEntity(this ChatCommandContext context, out Entity entity)
    {
        entity = context.GetSenderCharacterEntity();
        return entity.Exists();
    }
}
