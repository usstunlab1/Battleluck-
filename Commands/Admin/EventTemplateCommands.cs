using BattleLuck.Services.Runtime;
using VampireCommandFramework;

public static class EventTemplateCommands
{
    [Command("event.create", description: "Create a custom event by cloning a template. Usage: .event.create <eventId> [templateId]", adminOnly: true)]
    public static void Create(ChatCommandContext ctx, string eventId, string templateId = "bloodbath")
    {
        var service = new EventTemplateService();
        var result = service.CreateFromTemplate(eventId, templateId);
        if (!result.Success || result.Value == null)
        {
            ctx.Reply(result.Error ?? "Event creation failed.");
            return;
        }

        var registrationError = "Game mode registry is unavailable.";
        var registered = BattleLuckPlugin.GameModes != null &&
                         BattleLuckPlugin.GameModes.TryRegisterConfiguredMode(result.Value.ModeId, out registrationError);
        if (!registered)
        {
            ctx.Reply($"Created '{result.Value.DisplayName}', but it was not registered: {registrationError}");
            return;
        }

        if (BattleLuckPlugin.Session is { } session &&
            !session.TryRegisterModeZones(result.Value.ModeId, out var zoneError))
        {
            ctx.Reply($"Created and registered '{result.Value.ModeId}', but zone detection was not updated: {zoneError}");
            return;
        }

        ctx.Reply($"Created custom event '{result.Value.DisplayName}' as '{result.Value.ModeId}' from '{result.Value.TemplateId}' (zone hash {result.Value.ZoneHash}).");
        ctx.Reply($"Edit config/BattleLuck/events/{result.Value.ModeId}/flow.json, zones.json, kits.json, and prompt.txt, then run .event.start {result.Value.ModeId}.");
        ctx.Reply("The cloned event keeps Bloodbath's kit, entry/exit lifecycle, rollback snapshot flow, and action validation until you customize it. Move the copied zone center/teleportSpawn before use if it should be a separate arena.");
    }
}
