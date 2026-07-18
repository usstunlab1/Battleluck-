namespace BattleLuck.Services.Progression;

public static class ProgressionController
{
    public static OperationResult UnlockAllVBloods(Entity player)
    {
        // Actual implementation would involve finding all VBlood prefabs and adding them to the player's progression buffer
        BattleLuckPlugin.LogInfo($"[Progression] UnlockAllVBloods requested for player {player.GetSteamId()}.");
        return OperationResult.Ok();
    }

    public static OperationResult UnlockAllResearch(Entity player)
    {
        // Actual implementation would involve finding all research prefabs and adding them to the player's research buffer
        BattleLuckPlugin.LogInfo($"[Progression] UnlockAllResearch requested for player {player.GetSteamId()}.");
        return OperationResult.Ok();
    }

    public static OperationResult SetTier(Entity player, int tier)
    {
        BattleLuckPlugin.LogInfo($"[Progression] SetTier requested for player {player.GetSteamId()} (tier={tier}).");
        return OperationResult.Ok();
    }
}
