namespace BattleLuck.Services;

/// <summary>Canonical progression boundary. Unsupported native mutations fail explicitly.</summary>
public sealed class PlayerProgressionService
{
    public OperationResult UnlockAll(Entity player) => OperationResult.Fail(
        "Unlocking every progression, achievement, and VBlood is not available through a verified server API on this build.");

    public OperationResult UnlockAllVBloods(Entity player) => OperationResult.Fail(
        "VBlood progression mutation is not available through a verified server API on this build.");

    public OperationResult UnlockAllResearch(Entity player) => OperationResult.Fail(
        "Research progression mutation is not available through a verified server API on this build.");
}
