using BattleLuck.Services.Npc;
using BattleLuck.Services.Spawn;
using Unity.Entities;
using Unity.Mathematics;

namespace BattleLuck.Services.Practice;

/// <summary>
/// Creates one temporary, native-AI opponent for an administrator's solo practice.
/// The unit is always an NPC: no PlayerCharacter components or player loadout
/// components are copied onto it.
/// </summary>
public sealed class SoloPracticeService
{
    public static SoloPracticeService Instance { get; } = new();

    readonly SpawnController _spawner = new();
    readonly Dictionary<ulong, string> _npcByOwner = new();

    public string Status(ulong steamId) => _npcByOwner.TryGetValue(steamId, out var npcId)
        ? $"Solo practice active: {npcId}." : "No solo practice NPC is active.";

    public OperationResult Start(Entity player, ulong steamId, string mode)
    {
        if (!player.Exists() || !player.Has<PlayerCharacter>())
            return OperationResult.Fail("Solo practice requires an online player character.");

        var npcService = BattleLuckPlugin.NpcService;
        if (npcService == null)
            return OperationResult.Fail("NPC control is not initialized.");

        mode = mode.Trim().ToLowerInvariant();
        if (mode is not ("follow" or "fight" or "mirror"))
            return OperationResult.Fail("Usage: .ai practice <follow|fight|mirror|status|stop>");

        StopInternal(steamId, writeAudit: false);
        var spawnPosition = player.GetPosition() + new float3(3f, 0f, 3f);
        const string sessionId = "_solo_practice_";
        var npcId = $"practice_{steamId}";

        _spawner.SpawnNPC(SpawnController.Bandit_Thug, spawnPosition, entity =>
        {
            var registration = npcService.RegisterNpc(sessionId, npcId, "CHAR_Bandit_Thug", SpawnController.Bandit_Thug,
                entity, spawnPosition, homeRadius: 60f);
            if (!registration.Success || registration.Value == null)
            {
                BattleLuckPlugin.LogWarning($"[SoloPractice] NPC registration failed for {steamId}: {registration.Error}");
                return;
            }

            _npcByOwner[steamId] = registration.Value.NpcId;
            var result = mode == "fight"
                ? npcService.Aggro(registration.Value.NpcId, player, pressureRange: 3f, leashRange: 100f)
                : npcService.Follow(registration.Value.NpcId, player, followRange: mode == "mirror" ? 2f : 4f, leashRange: 100f);
            if (!result.Success)
                BattleLuckPlugin.LogWarning($"[SoloPractice] {mode} setup failed for {steamId}: {result.Error}");
        });

        // One intentional admin audit line per manually requested practice action.
        BattleLuckPlugin.LogInfo($"[SoloPractice] admin={steamId} action=start mode={mode} npc={npcId}");
        return OperationResult.Ok();
    }

    public OperationResult Stop(ulong steamId)
        => StopInternal(steamId, writeAudit: true);

    OperationResult StopInternal(ulong steamId, bool writeAudit)
    {
        if (_npcByOwner.Remove(steamId, out var npcId))
        {
            BattleLuckPlugin.NpcService?.Despawn(npcId);
            if (writeAudit)
                BattleLuckPlugin.LogInfo($"[SoloPractice] admin={steamId} action=stop npc={npcId}");
        }
        return OperationResult.Ok();
    }
}
