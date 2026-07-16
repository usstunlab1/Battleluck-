using System;
using System.Linq;
using BattleLuck.Services;
using BattleLuck.Services.Flow;
using VampireCommandFramework;

public static class DevCommands
{
    [Command("dev.enter", description: "Enter dev mode sandbox", adminOnly: true)]
    public static void DevEnter(ChatCommandContext ctx)
    {
        var service = BattleLuckPlugin.DevSession;
        if (service == null)
        {
            ctx.Reply("Dev session service is not initialized.");
            return;
        }

        var character = ctx.Event.SenderCharacterEntity;
        if (!character.Exists())
        {
            ctx.Reply("Sender character entity is not available.");
            return;
        }

        var result = service.EnterDevMode(character, character.GetSteamId());
        ctx.Reply(result.Success ? "Entered dev mode sandbox." : result.UserMessage);
    }

    [Command("dev.exit", description: "Exit dev mode sandbox", adminOnly: true)]
    public static void DevExit(ChatCommandContext ctx)
    {
        var service = BattleLuckPlugin.DevSession;
        if (service == null)
        {
            ctx.Reply("Dev session service is not initialized.");
            return;
        }

        var character = ctx.Event.SenderCharacterEntity;
        if (!character.Exists())
        {
            ctx.Reply("Sender character entity is not available.");
            return;
        }

        var result = service.ExitDevMode(character, character.GetSteamId());
        ctx.Reply(result.Success ? "Exited dev mode sandbox." : result.UserMessage);
    }

    [Command("dev.test", description: "Test a flow action in dev mode. Usage: .dev.test <actionString>", adminOnly: true)]
    public static void DevTest(ChatCommandContext ctx, string actionString)
    {
        var service = BattleLuckPlugin.DevSession;
        if (service == null)
        {
            ctx.Reply("Dev session service is not initialized.");
            return;
        }

        var character = ctx.Event.SenderCharacterEntity;
        if (!character.Exists())
        {
            ctx.Reply("Sender character entity is not available.");
            return;
        }

        var result = service.ExecuteDevAction(character, character.GetSteamId(), actionString);
        ctx.Reply(result.Success
            ? $"✔ Test passed: {actionString}"
            : $"✘ Test failed: {result.Error ?? result.UserMessage}");
    }

    [Command("dev.test.all", description: "Test all actions in actions_generation.json", adminOnly: true)]
    public static void DevTestAll(ChatCommandContext ctx)
    {
        var service = BattleLuckPlugin.DevSession;
        if (service == null)
        {
            ctx.Reply("Dev session service is not initialized.");
            return;
        }

        var character = ctx.Event.SenderCharacterEntity;
        if (!character.Exists())
        {
            ctx.Reply("Sender character entity is not available.");
            return;
        }

        var results = service.ExecuteAllActions(character, character.GetSteamId());
        var passed = results.Count(r => r.Success);
        var failed = results.Count - passed;

        ctx.Reply($"Test results: {passed} passed, {failed} failed out of {results.Count} total.");
        
        if (failed > 0)
        {
            ctx.Reply("Failed actions:");
            foreach (var result in results.Where(r => !r.Success).Take(10))
            {
                ctx.Reply($"  ✘ {result.UserMessage}");
            }
            if (failed > 10)
                ctx.Reply($"  ... and {failed - 10} more failures.");
        }
    }

    [Command("dev.flow", description: "Test a complete flow in dev mode. Usage: .dev.flow <modeId> <flowType>", adminOnly: true)]
    public static void DevTestFlow(ChatCommandContext ctx, string modeId, string flowType)
    {
        var service = BattleLuckPlugin.DevSession;
        if (service == null)
        {
            ctx.Reply("Dev session service is not initialized.");
            return;
        }

        var character = ctx.Event.SenderCharacterEntity;
        if (!character.Exists())
        {
            ctx.Reply("Sender character entity is not available.");
            return;
        }

        var result = service.ExecuteDevFlow(character, character.GetSteamId(), modeId, flowType);
        ctx.Reply(result.Success
            ? $"✔ Flow test passed: {modeId}/{flowType}"
            : $"✘ Flow test failed: {result.Error ?? result.UserMessage}");
    }
}
