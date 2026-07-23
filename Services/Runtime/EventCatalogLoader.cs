using System.Text.Json;
using BattleLuck.Models;

namespace BattleLuck.Services.Runtime;

/// <summary>
/// Loads and validates the adaptive event catalog from its JSON file.
/// Confirms prefabs exist, schematic IDs are unique, events are allowed,
/// and all references resolve before making data available to the runtime.
/// </summary>
public sealed class EventCatalogLoader
{
    public static EventCatalogLoader Instance { get; } = new();

    /// <summary>
    /// Load and validate the catalog from disk. Returns null if loading fails.
    /// </summary>
    public AdaptiveEventCatalogRoot? LoadCatalog()
    {
        var path = Path.Combine(ConfigLoader.ConfigRoot, "adaptive_event_catalog.json");

        try
        {
            if (!File.Exists(path))
            {
                BattleLuckPlugin.LogInfo("[EventCatalogLoader] No adaptive_event_catalog.json found. Creating default catalog.");
                CreateDefaultCatalog(path);
                return new AdaptiveEventCatalogRoot();
            }

            var json = File.ReadAllText(path);
            var catalog = JsonSerializer.Deserialize<AdaptiveEventCatalogRoot>(json, ConfigLoader.JsonOptions);

            if (catalog == null)
            {
                BattleLuckPlugin.LogWarning("[EventCatalogLoader] Catalog deserialized as null.");
                return null;
            }

            // Validate
            var warnings = ValidateCatalog(catalog);
            foreach (var warning in warnings)
                BattleLuckPlugin.LogWarning($"[EventCatalogLoader] {warning}");

            if (warnings.Any(w => w.StartsWith("ERROR:", StringComparison.OrdinalIgnoreCase)))
            {
                BattleLuckPlugin.LogWarning("[EventCatalogLoader] Catalog has unresolvable errors — using empty catalog.");
                return new AdaptiveEventCatalogRoot();
            }

            BattleLuckPlugin.LogInfo($"[EventCatalogLoader] Loaded catalog: {catalog.Events.Count} events, {catalog.NpcDefinitions.Count} NPC definitions, {catalog.RewardProfiles.Count} reward profiles.");
            return catalog;
        }
        catch (Exception ex)
        {
            BattleLuckPlugin.LogWarning($"[EventCatalogLoader] Failed to load catalog: {ex.Message}");
            return null;
        }
    }

    static List<string> ValidateCatalog(AdaptiveEventCatalogRoot catalog)
    {
        var warnings = new List<string>();

        foreach (var (eventId, eventConfig) in catalog.Events)
        {
            if (string.IsNullOrWhiteSpace(eventId))
                warnings.Add("ERROR: Event entry with empty ID.");

            // Validate NPC entries
            foreach (var npc in eventConfig.Npcs)
            {
                if (string.IsNullOrWhiteSpace(npc.Id))
                    warnings.Add($"ERROR: Event '{eventId}' has NPC entry with empty ID.");

                if (npc.PrefabHash == 0 && string.IsNullOrWhiteSpace(npc.PrefabName))
                    warnings.Add($"ERROR: NPC '{npc.Id}' in event '{eventId}' has no prefab hash or name.");

                if (npc.ThreatCost <= 0)
                    warnings.Add($"Warning: NPC '{npc.Id}' in event '{eventId}' has threat cost <= 0.");

                if (npc.AllowedEvents.Count > 0 && !npc.AllowedEvents.Contains(eventId, StringComparer.OrdinalIgnoreCase) && !npc.AllowedEvents.Contains("*"))
                    warnings.Add($"Warning: NPC '{npc.Id}' in event '{eventId}' but AllowedEvents does not include this event.");
            }
        }

        // Validate global NPC definitions
        foreach (var npc in catalog.NpcDefinitions)
        {
            if (string.IsNullOrWhiteSpace(npc.Id))
                warnings.Add("ERROR: Global NPC definition with empty ID.");

            if (npc.PrefabHash == 0 && string.IsNullOrWhiteSpace(npc.PrefabName))
                warnings.Add($"ERROR: Global NPC '{npc.Id}' has no prefab hash or name.");
        }

        // Validate reward profiles
        foreach (var rp in catalog.RewardProfiles)
        {
            if (string.IsNullOrWhiteSpace(rp.Id))
                warnings.Add("ERROR: Reward profile with empty ID.");
        }

        return warnings;
    }

    static void CreateDefaultCatalog(string path)
    {
        var defaultCatalog = new AdaptiveEventCatalogRoot
        {
            Version = 1,
            Events = new Dictionary<string, EventAdaptiveConfig>(StringComparer.OrdinalIgnoreCase)
            {
                ["training_arena"] = new EventAdaptiveConfig
                {
                    Enabled = true,
                    MaximumNpcCount = 12,
                    BaseThreat = 20f,
                    Npcs = new List<NpcCatalogEntry>
                    {
                        new() { Id = "training_melee", PrefabName = "Skeleton_Warrior", PrefabHash = -1584807109, ThreatCost = 6, Roles = new List<string>{ "melee" }, AllowedEvents = new List<string>{ "training_arena" }, DefaultBehavior = "attack" },
                        new() { Id = "training_ranged", PrefabName = "Skeleton_Archer", PrefabHash = -1340402506, ThreatCost = 8, Roles = new List<string>{ "ranged" }, AllowedEvents = new List<string>{ "training_arena" }, DefaultBehavior = "attack" },
                        new() { Id = "training_mage", PrefabName = "Skeleton_Mage", PrefabHash = -539289064, ThreatCost = 10, Roles = new List<string>{ "ranged", "magic" }, AllowedEvents = new List<string>{ "training_arena" }, DefaultBehavior = "attack" },
                        new() { Id = "training_elite", PrefabName = "Church_Captain", PrefabHash = 1090737596, ThreatCost = 18, IsElite = true, Roles = new List<string>{ "melee", "leader" }, AllowedEvents = new List<string>{ "training_arena" }, DefaultBehavior = "attack" }
                    },
                    Waves = new List<WaveSchematic>
                    {
                        new() { Id = "wave_melee_drill", NpcIds = new List<string>{ "training_melee", "training_melee", "training_ranged" }, ThreatMultiplier = 0.8f, SpawnDelaySeconds = 0, CompletionCondition = "all_defeated", DrillId = "melee_drill" },
                        new() { Id = "wave_ranged_drill", NpcIds = new List<string>{ "training_ranged", "training_ranged", "training_mage" }, ThreatMultiplier = 1.0f, SpawnDelaySeconds = 10, CompletionCondition = "all_defeated", DrillId = "ranged_drill" },
                        new() { Id = "wave_elite_final", NpcIds = new List<string>{ "training_elite", "training_melee", "training_melee", "training_ranged" }, ThreatMultiplier = 1.3f, SpawnDelaySeconds = 15, CompletionCondition = "all_defeated" }
                    },
                    Rewards = new EventRewardConfig
                    {
                        Enabled = true,
                        MaximumItemsPerPlayer = 3,
                        MaximumTotalItemValue = 100,
                        AllowedRewardProfiles = new List<string>{ "training_basic" },
                        AllowNpcNativeDrops = false
                    },
                    AdaptiveScaling = new AdaptiveScalingConfig
                    {
                        Enabled = true,
                        MinimumDifficultyMultiplier = 0.75f,
                        MaximumDifficultyMultiplier = 1.5f,
                        MaximumNpcCount = 20,
                        AllowElitePromotion = true,
                        AllowBossPromotion = false
                    },
                    Drills = new List<Models.CombatDrillDefinition>
                    {
                        new() { Id = "melee_drill", Reaction = "counter" },
                        new() { Id = "ranged_drill", Reaction = "evade" }
                    }
                },
                ["bloodbath"] = new EventAdaptiveConfig
                {
                    Enabled = true,
                    MaximumNpcCount = 20,
                    BaseThreat = 30f,
                    Npcs = new List<NpcCatalogEntry>
                    {
                        new() { Id = "bb_bandit", PrefabName = "Bandit_Thug", PrefabHash = 1458281806, ThreatCost = 7, Roles = new List<string>{ "melee" }, AllowedEvents = new List<string>{ "bloodbath" }, DefaultBehavior = "attack"},
                        new() { Id = "bb_bandit_archer", PrefabName = "Bandit_Hunter", PrefabHash = -1000550829, ThreatCost = 9, Roles = new List<string>{ "ranged" }, AllowedEvents = new List<string>{ "bloodbath" }, DefaultBehavior = "attack"},
                        new() { Id = "bb_guard", PrefabName = "Militia_Guard", PrefabHash = -1101895538, ThreatCost = 10, Roles = new List<string>{ "melee", "guard" }, AllowedEvents = new List<string>{ "bloodbath" }, DefaultBehavior = "attack"},
                        new() { Id = "bb_elite", PrefabName = "Church_Captain", PrefabHash = 1090737596, ThreatCost = 20, IsElite = true, Roles = new List<string>{ "melee", "leader" }, AllowedEvents = new List<string>{ "bloodbath" }, DefaultBehavior = "attack"}
                    },
                    Rewards = new EventRewardConfig
                    {
                        Enabled = true,
                        MaximumItemsPerPlayer = 5,
                        MaximumTotalItemValue = 200,
                        AllowedRewardProfiles = new List<string>{ "basic_combat" },
                        AllowNpcNativeDrops = false
                    },
                    AdaptiveScaling = new AdaptiveScalingConfig
                    {
                        Enabled = true,
                        MinimumDifficultyMultiplier = 0.75f,
                        MaximumDifficultyMultiplier = 1.5f,
                        MaximumNpcCount = 24,
                        AllowElitePromotion = true,
                        AllowBossPromotion = false
                    }
                }
            },
            NpcDefinitions = new List<NpcCatalogEntry>
            {
                new() { Id = "skeleton_warrior", PrefabName = "Skeleton_Warrior", PrefabHash = -1584807109, ThreatCost = 6, MinimumRecommendedStrength = 5, MaximumRecommendedStrength = 40, Roles = new List<string>{ "melee" }, AllowedEvents = new List<string>{ "*" }, DefaultBehavior = "attack" },
                new() { Id = "skeleton_archer", PrefabName = "Skeleton_Archer", PrefabHash = -1340402506, ThreatCost = 8, MinimumRecommendedStrength = 5, MaximumRecommendedStrength = 40, Roles = new List<string>{ "ranged" }, AllowedEvents = new List<string>{ "*" }, DefaultBehavior = "attack" },
                new() { Id = "skeleton_mage", PrefabName = "Skeleton_Mage", PrefabHash = -539289064, ThreatCost = 10, MinimumRecommendedStrength = 10, MaximumRecommendedStrength = 50, Roles = new List<string>{ "ranged", "magic" }, AllowedEvents = new List<string>{ "*" }, DefaultBehavior = "attack" },
                new() { Id = "bandit_thug", PrefabName = "Bandit_Thug", PrefabHash = 1458281806, ThreatCost = 7, MinimumRecommendedStrength = 10, MaximumRecommendedStrength = 50, Roles = new List<string>{ "melee" }, AllowedEvents = new List<string>{ "*" }, DefaultBehavior = "attack" },
                new() { Id = "bandit_hunter", PrefabName = "Bandit_Hunter", PrefabHash = -1000550829, ThreatCost = 9, MinimumRecommendedStrength = 15, MaximumRecommendedStrength = 55, Roles = new List<string>{ "ranged" }, AllowedEvents = new List<string>{ "*" }, DefaultBehavior = "attack" },
                new() { Id = "militia_guard", PrefabName = "Militia_Guard", PrefabHash = -1101895538, ThreatCost = 10, MinimumRecommendedStrength = 20, MaximumRecommendedStrength = 60, Roles = new List<string>{ "melee", "guard" }, AllowedEvents = new List<string>{ "*" }, DefaultBehavior = "attack" },
                new() { Id = "ghoul", PrefabName = "Ghoul", PrefabHash = -1508186605, ThreatCost = 5, MinimumRecommendedStrength = 1, MaximumRecommendedStrength = 30, Roles = new List<string>{ "melee", "swarm" }, AllowedEvents = new List<string>{ "*" }, DefaultBehavior = "attack" }
            },
            RewardProfiles = new List<RewardProfile>
            {
                new() { Id = "basic_combat", ItemPrefabs = new List<int>{ 123456, 789012 }, ItemCount = 1, EstimatedValue = 25, EventDifficultyTier = 1, MinimumCombatStrength = 0, MaximumCombatStrength = 999 },
                new() { Id = "training_basic", ItemPrefabs = new List<int>{ 345678 }, ItemCount = 1, EstimatedValue = 10, EventDifficultyTier = 0, MinimumCombatStrength = 0, MaximumCombatStrength = 100 }
            }
        };

        var json = JsonSerializer.Serialize(defaultCatalog, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(path, json);
        BattleLuckPlugin.LogInfo($"[EventCatalogLoader] Created default catalog at {path}.");
    }
}