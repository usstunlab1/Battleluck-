using BattleLuck.Models;

namespace BattleLuck.Services.Runtime;

/// <summary>
/// Builds a fully resolved AdaptiveSpawnPlan from the participant profile and
/// event catalog. Uses a threat budget system to select NPCs whose configured
/// costs fit within the calculated budget, producing mixed groups instead of
/// simply multiplying NPC count by player count.
/// </summary>
public sealed class AdaptiveSpawnPlanner
{
    public static AdaptiveSpawnPlanner Instance { get; } = new();

    /// <summary>
    /// Build the complete spawn plan for an event given participants and catalog.
    /// </summary>
    public AdaptiveSpawnPlan BuildPlan(
        string modeId,
        EventParticipantProfile participants,
        EventCatalogContext catalog,
        EventAdaptiveConfig config)
    {
        if (participants.PlayerCount == 0 || !config.Enabled)
        {
            return new AdaptiveSpawnPlan
            {
                EventId = modeId,
                CalculatedDifficulty = 0,
                Waves = Array.Empty<SpawnWavePlan>(),
                RewardBudget = BuildDefaultRewardBudget(config),
                SafetyLimits = new SpawnSafetyLimits
                {
                    MaximumTotalNpcs = config.MaximumNpcCount,
                    MaximumNpcsPerWave = Math.Min(config.MaximumNpcCount, 16),
                    MaximumElitesPerWave = 0,
                    MaximumBossesPerEvent = 0,
                    MinimumSpawnSpacing = 3f
                }
            };
        }

        // Calculate group threat budget
        var avgStrength = participants.AverageCombatStrength;
        var baseDifficulty = config.BaseThreat;
        var playerCountFactor = 1f + (participants.PlayerCount - 1) * 0.6f;
        var strengthFactor = 0.75f + (avgStrength / 100f) * 0.75f;
        var coordinationFactor = participants.PlayerCount > 1 ? 1.15f : 1f;
        var progressionFactor = 1f;

        var totalBudget = baseDifficulty * playerCountFactor * strengthFactor * coordinationFactor * progressionFactor;

        // Build safety limits from config
        var scaling = config.AdaptiveScaling;
        var maxNpcCount = Math.Min(config.MaximumNpcCount, scaling.MaximumNpcCount);
        var safetyLimits = new SpawnSafetyLimits
        {
            MaximumTotalNpcs = maxNpcCount,
            MaximumNpcsPerWave = Math.Min(maxNpcCount, 16),
            MaximumElitesPerWave = scaling.AllowElitePromotion ? Math.Min(maxNpcCount / 4, 4) : 0,
            MaximumBossesPerEvent = scaling.AllowBossPromotion ? 1 : 0,
            MinimumSpawnSpacing = 3f
        };

        // Determine budget split
        var eliteBudget = totalBudget * 0.25f;
        var standardBudget = totalBudget * 0.55f;
        var supportBudget = totalBudget * 0.20f;

        // Build waves
        var waves = new List<SpawnWavePlan>();
        var waveCount = config.Waves.Count > 0 ? config.Waves.Count : 1;

        for (int waveIdx = 0; waveIdx < waveCount; waveIdx++)
        {
            var waveConfig = waveIdx < config.Waves.Count ? config.Waves[waveIdx] : null;
            var waveThreatMultiplier = waveConfig?.ThreatMultiplier ?? 1f;
            var waveBudget = (totalBudget / waveCount) * waveThreatMultiplier;

            // If wave schematics are defined, use them
            if (waveConfig != null && waveConfig.NpcIds.Count > 0)
            {
                var waveNpcs = BuildWaveFromSchematic(waveConfig, catalog, avgStrength, safetyLimits);
                waves.Add(new SpawnWavePlan
                {
                    WaveIndex = waveIdx,
                    ThreatBudget = waveBudget,
                    Npcs = waveNpcs,
                    StartDelaySeconds = waveConfig.SpawnDelaySeconds,
                    CompletionCondition = waveConfig.CompletionCondition,
                    DrillId = waveConfig.DrillId ?? ""
                });
                continue;
            }

            // Otherwise, auto-select NPCs from catalog
            var npcs = SelectNpcsForWave(waveBudget, catalog, avgStrength, safetyLimits, eliteBudget, standardBudget, supportBudget, waveIdx);
            waves.Add(new SpawnWavePlan
            {
                WaveIndex = waveIdx,
                ThreatBudget = waveBudget,
                Npcs = npcs,
                StartDelaySeconds = waveConfig?.SpawnDelaySeconds ?? (waveIdx * 5f),
                CompletionCondition = "all_defeated",
                DrillId = waveConfig?.DrillId ?? ""
            });
        }

        // Build reward budget from config
        var rewardBudget = BuildRewardBudget(config, catalog);

        return new AdaptiveSpawnPlan
        {
            EventId = modeId,
            CalculatedDifficulty = avgStrength,
            Waves = waves,
            RewardBudget = rewardBudget,
            SafetyLimits = safetyLimits
        };
    }

    static IReadOnlyList<SpawnNpcPlan> BuildWaveFromSchematic(
        WaveSchematic schematic,
        EventCatalogContext catalog,
        float avgStrength,
        SpawnSafetyLimits safetyLimits)
    {
        var plans = new List<SpawnNpcPlan>();
        var remainingSlots = safetyLimits.MaximumNpcsPerWave;

        foreach (var npcId in schematic.NpcIds)
        {
            if (remainingSlots <= 0) break;

            if (!catalog.Npcs.TryGetValue(npcId, out var entry))
            {
                BattleLuckPlugin.LogWarning($"[AdaptiveSpawnPlanner] Wave schematic references unknown NPC '{npcId}'.");
                continue;
            }

            if (avgStrength < entry.MinimumRecommendedStrength || avgStrength > entry.MaximumRecommendedStrength)
                continue;

            // Resolve prefab GUID
            var prefabGuid = ResolvePrefab(entry);
            if (prefabGuid == PrefabGUID.Empty)
                continue;

            var count = Math.Min(1, remainingSlots);
            plans.Add(new SpawnNpcPlan
            {
                CatalogId = entry.Id,
                PrefabGuid = prefabGuid,
                Count = count,
                BehaviorProfileId = entry.DefaultBehavior,
                SpawnZoneId = "",
                HealthScale = 1f,
                DamageScale = 1f
            });
            remainingSlots -= count;
        }

        return plans;
    }

    static IReadOnlyList<SpawnNpcPlan> SelectNpcsForWave(
        float waveBudget,
        EventCatalogContext catalog,
        float avgStrength,
        SpawnSafetyLimits safetyLimits,
        float eliteBudget,
        float standardBudget,
        float supportBudget,
        int waveIndex)
    {
        var plans = new List<SpawnNpcPlan>();
        var eligible = catalog.Npcs.Values
            .Where(n => avgStrength >= n.MinimumRecommendedStrength && avgStrength <= n.MaximumRecommendedStrength)
            .ToList();

        if (eligible.Count == 0)
        {
            BattleLuckPlugin.LogWarning("[AdaptiveSpawnPlanner] No eligible NPCs for current player strength, using any available.");
            eligible = catalog.Npcs.Values.Take(3).ToList();
            if (eligible.Count == 0) return plans;
        }

        var remaining = waveBudget;
        var count = 0;
        var maxCount = safetyLimits.MaximumNpcsPerWave;
        var elitesSpawned = 0;
        var maxElites = safetyLimits.MaximumElitesPerWave;

        // Phase 1: Select elites from budget
        if (eliteBudget > 0 && maxElites > 0)
        {
            var elites = eligible.Where(n => n.IsElite).OrderByDescending(n => n.ThreatCost).ToList();
            foreach (var elite in elites)
            {
                if (elitesSpawned >= maxElites || count >= maxCount) break;
                if (elite.ThreatCost > remaining && count > 0) break;

                var prefabGuid = ResolvePrefab(elite);
                if (prefabGuid == PrefabGUID.Empty) continue;

                plans.Add(new SpawnNpcPlan
                {
                    CatalogId = elite.Id,
                    PrefabGuid = prefabGuid,
                    Count = 1,
                    BehaviorProfileId = elite.DefaultBehavior,
                    SpawnZoneId = "",
                    HealthScale = 1.25f + waveIndex * 0.05f,
                    DamageScale = 1.15f + waveIndex * 0.05f
                });
                remaining -= elite.ThreatCost;
                elitesSpawned++;
                count++;
            }
        }

        // Phase 2: Select standards (mix of melee/ranged)
        var standards = eligible
            .Where(n => !n.IsElite && !n.IsBoss)
            .OrderBy(n => n.ThreatCost)
            .ToList();

        var meleePool = standards.Where(n => n.Roles.Contains("melee")).ToList();
        var rangedPool = standards.Where(n => n.Roles.Contains("ranged")).ToList();

        // Alternate between pools for variety
        bool useMelee = true;
        while (remaining > 0 && count < maxCount)
        {
            var pool = useMelee ? meleePool : rangedPool;
            if (pool.Count == 0) pool = standards;
            if (pool.Count == 0) break;

            // Pick the most expensive NPC that fits, or the cheapest if nothing fits
            var entry = pool.LastOrDefault(n => n.ThreatCost <= remaining && !plans.Any(p => p.CatalogId.Equals(n.Id, StringComparison.OrdinalIgnoreCase) && p.Count >= 3))
                        ?? pool.FirstOrDefault();

            if (entry == null) break;
            if (entry.ThreatCost > remaining && count > 0) break;

            var prefabGuid = ResolvePrefab(entry);
            if (prefabGuid == PrefabGUID.Empty) { useMelee = !useMelee; continue; }

            // Check if we already have this NPC and can stack
            var existing = plans.FirstOrDefault(p => p.CatalogId.Equals(entry.Id, StringComparison.OrdinalIgnoreCase));
            if (existing != null && existing.Count < 3)
            {
                // Use a mutable approach - rebuild the list with updated count
                var idx = plans.IndexOf(existing);
                var updated = new List<SpawnNpcPlan>(plans);
                updated[idx] = existing with { Count = existing.Count + 1 };
                plans = updated;
            }
            else
            {
                plans.Add(new SpawnNpcPlan
                {
                    CatalogId = entry.Id,
                    PrefabGuid = prefabGuid,
                    Count = 1,
                    BehaviorProfileId = entry.DefaultBehavior,
                    SpawnZoneId = "",
                    HealthScale = 1f + waveIndex * 0.03f,
                    DamageScale = 1f + waveIndex * 0.03f
                });
            }

            remaining -= entry.ThreatCost;
            count++;
            useMelee = !useMelee;
        }

        return plans;
    }

    static PrefabGUID ResolvePrefab(NpcCatalogEntry entry)
    {
        if (entry.PrefabHash != 0)
        {
            var guid = new PrefabGUID(entry.PrefabHash);
            if (guid != PrefabGUID.Empty)
                return guid;
        }

        if (!string.IsNullOrWhiteSpace(entry.PrefabName))
        {
            var resolved = PrefabHelper.GetValidPrefabGuidDeep(entry.PrefabName);
            if (resolved.HasValue)
                return resolved.Value;
        }

        BattleLuckPlugin.LogWarning($"[AdaptiveSpawnPlanner] Cannot resolve prefab for NPC '{entry.Id}' (hash={entry.PrefabHash}, name='{entry.PrefabName}').");
        return PrefabGUID.Empty;
    }

    static RewardBudget BuildRewardBudget(EventAdaptiveConfig config, EventCatalogContext catalog)
    {
        var allowedItems = new HashSet<PrefabGUID>();
        var blockedItems = new HashSet<PrefabGUID>();

        foreach (var hash in config.Rewards.BlockedItemPrefabs)
        {
            if (hash != 0)
                blockedItems.Add(new PrefabGUID(hash));
        }

        // Include items from allowed reward profiles
        foreach (var rp in catalog.Rewards.Values)
        {
            foreach (var hash in rp.ItemPrefabs)
            {
                if (hash != 0 && !blockedItems.Contains(new PrefabGUID(hash)))
                    allowedItems.Add(new PrefabGUID(hash));
            }
        }

        return new RewardBudget
        {
            MaximumItemsPerPlayer = config.Rewards.MaximumItemsPerPlayer,
            MaximumItemsTotal = Math.Max(config.Rewards.MaximumItemsPerPlayer * 5, 20),
            MaximumValuePerPlayer = config.Rewards.MaximumTotalItemValue,
            AllowedItems = allowedItems,
            BlockedItems = blockedItems
        };
    }

    static RewardBudget BuildDefaultRewardBudget(EventAdaptiveConfig config)
    {
        return new RewardBudget
        {
            MaximumItemsPerPlayer = config.Rewards.MaximumItemsPerPlayer,
            MaximumItemsTotal = 0,
            MaximumValuePerPlayer = 0,
            AllowedItems = new HashSet<PrefabGUID>(),
            BlockedItems = new HashSet<PrefabGUID>(config.Rewards.BlockedItemPrefabs
                .Where(h => h != 0)
                .Select(h => new PrefabGUID(h)))
        };
    }
}