using System.Text.Json;
using BattleLuck.Models;
using BattleLuck.Services.Npc;
using BattleLuck.Services.Spawn;
using Unity.Entities;
using Unity.Mathematics;

namespace BattleLuck.Services.Adaptive;

public sealed class AdaptiveCombatDrillService
{
    public static AdaptiveCombatDrillService Instance { get; } = new();
    readonly SpawnController _spawner = new();
    AdaptiveDrillCatalog? _catalog;

    public void StartEvent(GameModeContext context, ZoneDefinition zone)
    {
        var catalog = LoadCatalog();
        if (!catalog.Events.TryGetValue(context.ModeId, out var definition) && !catalog.Events.TryGetValue("*", out definition)) return;
        if (!definition.Enabled) return;
        var players = VRisingCore.GetOnlinePlayers().Where(p => p.IsValidPlayer() && context.Players.Contains(p.GetSteamId())).ToList();
        if (players.Count == 0 || BattleLuckPlugin.NpcService == null) return;

        var profiles = players.Select(p =>
        {
            var health = p.Read<Health>();
            var healthRatio = health.MaxHealth > 0 ? health.Value / health.MaxHealth.Value : 1f;
            var combatStrength = CalculateCombatStrength(p);
            return new PlayerCombatProfile(p.GetSteamId(), p.GetUnitLevel(), healthRatio, combatStrength);
        }).ToList();

        var averageStrength = profiles.Any() ? profiles.Average(p => p.CombatStrength) : 50f;
        var profile = new EventParticipantProfile(profiles, averageStrength);

        // Adjust budget based on average strength. A strength of 50 is baseline (1.25x).
        var strengthMultiplier = Math.Clamp(0.75f + (averageStrength / 100f), 0.75f, 2.0f);
        var budget = definition.BaseThreat * players.Count * strengthMultiplier;

        var selected = definition.Npcs.Where(n => n.ThreatCost > 0 && profile.AverageStrength >= n.MinimumStrength && profile.AverageStrength <= n.MaximumStrength && PrefabHelper.GetValidPrefabGuidDeep(n.Prefab).HasValue)
            .OrderBy(n => n.ThreatCost).ToList();
        var plans = new List<SpawnNpcPlan>(); var remaining = budget; var count = 0;
        while (selected.Count > 0 && count < Math.Clamp(definition.MaximumNpcCount, 1, 32))
        {
            var entry = selected.LastOrDefault(n => n.ThreatCost <= remaining) ?? selected.First();
            if (entry.ThreatCost > remaining && count > 0) break;
            plans.Add(new SpawnNpcPlan(entry.Id, entry.Prefab, 1, entry.Behavior)); remaining -= entry.ThreatCost; count++;
        }
        var plan = new AdaptiveSpawnPlan(context.ModeId, budget, plans);
        context.State["adaptiveSpawnPlan"] = plan;
        var target = players[0]; var center = zone.Position.ToFloat3();
        foreach (var (spawn, index) in plan.Npcs.Select((value, index) => (value, index)))
        {
            var prefab = PrefabHelper.GetValidPrefabGuidDeep(spawn.Prefab)!.Value;
            var pos = center + new float3(3 + index * 2, 0, 3);
            _spawner.SpawnNPC(prefab, pos, entity =>
            {
                var id = $"adaptive_{context.SessionId}_{index}";
                var registration = BattleLuckPlugin.NpcService.RegisterNpc(context.SessionId, id, spawn.Prefab, prefab, entity, pos, 80f);
                if (!registration.Success || registration.Value == null) return;
                if (spawn.Behavior.Equals("follow", StringComparison.OrdinalIgnoreCase)) BattleLuckPlugin.NpcService.Follow(id, target, 4f, 100f);
                else BattleLuckPlugin.NpcService.Aggro(id, target, 3f, 100f);
            });
        }
        BattleLuckPlugin.LogInfo($"[AdaptiveDrills] event={context.ModeId} players={players.Count} strength={profile.AverageStrength:F1} budget={budget:F1} spawns={plans.Count}.");
    }

    private float CalculateCombatStrength(Entity player)
    {
        if (!player.IsValidPlayer()) return 0f;

        var gearLevel = (float)player.GetUnitLevel();
        var attackPower = player.GetAttackPower();
        var spellPower = player.GetSpellPower();
        var physicalPower = player.GetPhysicalPower();

        // Weighted formula for a more accurate strength assessment
        var strength = (gearLevel * 0.5f) + (attackPower * 0.2f) + (spellPower * 0.2f) + (physicalPower * 0.1f);
        return (float)Math.Clamp(strength, 1, 150);
    }

    AdaptiveDrillCatalog LoadCatalog()
    {
        if (_catalog != null) return _catalog;
        var path = Path.Combine(ConfigLoader.ConfigRoot, "adaptive_drills.json");
        try { _catalog = File.Exists(path) ? JsonSerializer.Deserialize<AdaptiveDrillCatalog>(File.ReadAllText(path), ConfigLoader.JsonOptions) : null; }
        catch (Exception ex) { BattleLuckPlugin.LogWarning($"[AdaptiveDrills] Catalog load failed: {ex.Message}"); }
        return _catalog ??= new AdaptiveDrillCatalog();
    }
}
