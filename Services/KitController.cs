using ProjectM;
using Stunlock.Core;
using Unity.Entities;
using BattleLuck.Core.Loaders;

/// <summary>
/// Mode of kit application.
/// FullLoadout replaces the entire player loadout (inventory, blood, abilities, etc.).
/// AdditiveReward grants only the configured items without clearing inventory or modifying other state.
/// </summary>
public enum KitApplyMode
{
    FullLoadout,
    AdditiveReward
}

/// <summary>
/// Applies full kit loadouts from kit.json per mode.
/// Uses PrefabHelper for name→GUID resolution.
/// Hard rollback on failure via PlayerStateController snapshot restore.
/// </summary>
public static class KitController
{
    static readonly Dictionary<string, KitConfig> _kitCache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Load kit.json for a mode. Caches result.</summary>
    public static KitConfig? LoadKit(string modeId)
    {
        if (_kitCache.TryGetValue(modeId, out var cached))
            return cached;

        var path = Path.Combine(ModeConfigLoader.KitsRoot, modeId, "kits.json");
        if (!File.Exists(path))
        {
            // Fallback for transition or separate kits folder
            path = Path.Combine(ConfigLoader.ConfigRoot, "kits", $"{modeId}.json");
            if (!File.Exists(path))
            {
                BattleLuckPlugin.LogWarning($"[KitController] Missing kits.json for {modeId} at {path}");
                return null;
            }
        }

        try
        {
            var json = File.ReadAllText(path);
            var kit = JsonSerializer.Deserialize<KitConfig>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                ReadCommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true
            });

            if (kit != null)
                _kitCache[modeId] = kit;

            return kit;
        }
        catch (Exception ex)
        {
            BattleLuckPlugin.LogWarning($"[KitController] Error loading kit.json for {modeId}: {ex.Message}");
            return null;
        }
    }

    /// <summary>Clear the kit cache (used on reload).</summary>
    public static void ClearCache() => _kitCache.Clear();

    /// <summary>
    /// Apply a kit to a player by mode ID.
    /// Uses MutationPipeline with snapshot rollback on failure.
    /// </summary>
    public static OperationResult ApplyKit(Entity playerCharacter, string modeId)
    {
        var kit = LoadKit(modeId);
        if (kit == null)
            return OperationResult.Fail($"Kit not found for mode '{modeId}'");

        var snapshot = new PlayerStateController();
        const int KitRollbackZone = PlayerStateController.KitRollbackZoneHash;
        snapshot.SaveSnapshotIfMissing(playerCharacter, KitRollbackZone);
        var settings = kit.Settings;

         var pipelineResult = InlinePipeline.Run($"KitApply:{modeId}", p =>
         {
             p.Step("ClearAbilityLoadout", () =>
             {
                 AbilityController.ClearAbilitySlots(playerCharacter);
                 AbilityController.ClearPassiveSpells(playerCharacter);
             });

             if (settings.ClearInventoryFirst)
             {
                 p.Step("ClearInventory", () =>
                 {
                     ClearInventory(playerCharacter);
                 });
             }

             if (kit.Armors != null)
             {
                 p.Step("ApplyArmor", () =>
                 {
                     ApplyArmor(playerCharacter, kit.Armors);
                 });
             }

             p.Step("ApplyWeapons", () =>
             {
                 foreach (var weapon in kit.Weapons)
                     ApplyWeapon(playerCharacter, weapon);
             });

             p.Step("ApplyItems", () =>
             {
                 foreach (var item in kit.Items)
                 {
                     TryGiveResolvedItem(playerCharacter, item.Prefab, item.Amount, "item");
                 }
             });

             if (kit.Blood != null)
             {
                 p.Step("ApplyBlood", () =>
                 {
                     ApplyBlood(playerCharacter, kit.Blood);
                 });
             }

             if (kit.Abilities != null)
             {
                 p.Step("ApplyAbilities", () =>
                 {
                     AbilityController.EquipAbilities(playerCharacter, kit.Abilities);
                 });
             }

             if (kit.PassiveSpells.Count > 0)
             {
                 p.Step("ApplyPassiveSpells", () =>
                 {
                     AbilityController.EquipPassiveSpells(playerCharacter, kit.PassiveSpells);
                 });
             }

             if (settings.HealOnApply)
             {
                 p.Step("HealToFull", () =>
                 {
                     playerCharacter.HealToFull();
                 });
             }
        });

        if (!pipelineResult.Success)
        {
            snapshot.RestoreSnapshot(playerCharacter, KitRollbackZone);

            BattleLuckPlugin.LogError(
                $"[KitController] CRITICAL: Kit apply failed for {modeId} at step '{pipelineResult.FailedStep}': {pipelineResult.Error}"
            );

            return OperationResult.Fail(
                $"Kit apply failed at step '{pipelineResult.FailedStep}': {pipelineResult.Error}"
            );
        }

        BattleLuckPlugin.LogInfo(
            $"[KitController] Kit '{modeId}' applied to {playerCharacter.GetSteamId()} with {pipelineResult.Steps.Count} steps."
        );

        return OperationResult.Ok();
    }

    static void ClearInventory(Entity character)
    {
        var em = VRisingCore.EntityManager;
        var sgm = VRisingCore.ServerGameManager;

        if (!sgm.TryGetBuffer<InventoryBuffer>(character, out var inventory))
            return;

        // Collect items first, then remove (avoid modifying buffer during iteration)
        var toRemove = new List<PrefabGUID>();
        for (int i = 0; i < inventory.Length; i++)
        {
            var item = inventory[i];
            if (item.ItemType != PrefabGUID.Empty)
                toRemove.Add(item.ItemType);
        }

        foreach (var prefab in toRemove)
        {
            sgm.TryRemoveInventoryItem(character, prefab, 999);
        }
    }

    static void ApplyArmor(Entity character, ArmorsConfig armors)
    {
        GiveIfResolved(character, armors.Chest, "Chest");
        GiveIfResolved(character, armors.Legs, "Legs");
        GiveIfResolved(character, armors.Gloves, "Gloves");
        GiveIfResolved(character, armors.Boots, "Boots");
        GiveIfResolved(character, armors.Cloak, "Cloak");
        GiveIfResolved(character, armors.Headgear, "Headgear");
        GiveIfResolved(character, armors.MagicSource, "MagicSource");
        GiveIfResolved(character, armors.Bag, "Bag");
    }

    static void ApplyWeapon(Entity character, WeaponConfig weapon)
    {
        TryGiveResolvedItem(character, weapon.Prefab, weapon.Amount, "weapon");
    }

    static void ApplyBlood(Entity character, BloodConfig blood)
    {
        var bloodGuid = PrefabHelper.GetPrefabGuid($"BloodType_{blood.Type}");
        if (!bloodGuid.HasValue)
        {
            BattleLuckPlugin.LogWarning($"[KitController] Unknown blood type: {blood.Type}");
            return;
        }

        if (character.Has<Blood>())
        {
            character.With((ref Blood b) =>
            {
                b.BloodType = bloodGuid.Value;
                b.Quality = blood.Quality;
                b.Value = blood.Value;
            });
        }
    }

    static void GiveIfResolved(Entity character, string? prefabName, string slotName)
    {
        if (string.IsNullOrEmpty(prefabName)) return;

        TryGiveResolvedItem(character, prefabName, 1, slotName);
    }

    static bool TryGiveResolvedItem(Entity character, string prefabName, int amount, string context)
    {
        if (string.IsNullOrWhiteSpace(prefabName) || amount <= 0)
            return false;

        var attempted = new HashSet<int>();
        var attemptedNames = new List<string>();

        foreach (var (candidateName, guid) in ResolveGrantCandidates(prefabName, context).Take(24))
        {
            if (guid == PrefabGUID.Empty || !attempted.Add(guid.GuidHash))
                continue;

            attemptedNames.Add($"{candidateName}({guid.GuidHash})");
            if (character.TryGiveItem(guid, amount))
            {
                if (!candidateName.Equals(prefabName, StringComparison.OrdinalIgnoreCase))
                    BattleLuckPlugin.LogInfo($"[KitController] Granted {context} '{prefabName}' via compatible live prefab '{candidateName}' ({guid.GuidHash}).");
                return true;
            }
        }

        BattleLuckPlugin.LogWarning($"[KitController] TryGiveItem FAILED for {context}: {prefabName}. Tried {attemptedNames.Count} candidate(s): {string.Join(", ", attemptedNames.Take(6))}");
        return false;
    }

    static IEnumerable<(string Name, PrefabGUID Guid)> ResolveGrantCandidates(string prefabName, string context)
    {
        if (PrefabHelper.TryGetValidPrefabGuidStrict(prefabName, out var strictGuid))
        {
            if (TryNormalizeGrantCandidate(prefabName, strictGuid, context, out var candidate))
                yield return candidate;
        }

        if (PrefabHelper.TryGetValidPrefabGuidDeep(prefabName, out var deepGuid))
        {
            if (TryNormalizeGrantCandidate(prefabName, deepGuid, context, out var candidate))
                yield return candidate;
        }

        foreach (var candidate in FindCompatibleLiveItemCandidates(prefabName, context))
            yield return candidate;
    }

    static bool TryNormalizeGrantCandidate(string requestedName, PrefabGUID guid, string context, out (string Name, PrefabGUID Guid) candidate)
    {
        var liveName = PrefabHelper.GetLivePrefabName(guid) ?? requestedName;
        candidate = (liveName, guid);

        if (!IsLikelyGrantableItem(liveName) || !IsCompatibleKitSlot(liveName, requestedName, context))
        {
            BattleLuckPlugin.LogWarning($"[KitController] Skipping non-grantable {context} candidate '{liveName}' ({guid.GuidHash}) for '{requestedName}'.");
            return false;
        }

        return true;
    }

    static IEnumerable<(string Name, PrefabGUID Guid)> FindCompatibleLiveItemCandidates(string prefabName, string context)
    {
        var requested = prefabName.ToLowerInvariant();
        var seen = new HashSet<int>();
        foreach (var kvp in GetCandidateFilters(prefabName, context)
                     .Where(filter => !string.IsNullOrWhiteSpace(filter))
                     .SelectMany(PrefabHelper.FindLive)
                     .Where(kvp => seen.Add(kvp.Value.GuidHash))
                     .Where(kvp => IsLikelyGrantableItem(kvp.Key))
                     .Where(kvp => IsCompatibleKitSlot(kvp.Key, prefabName, context))
                     .OrderByDescending(kvp => ScoreKitCandidate(kvp.Key, requested))
                     .Take(20))
        {
            yield return (kvp.Key, kvp.Value);
        }
    }

    static IEnumerable<string> GetCandidateFilters(string prefabName, string context)
    {
        var ctx = context.ToLowerInvariant();
        if (ctx.Contains("chest")) return new[] { "Item_Armor_Chest", "Armor_Chest" };
        if (ctx.Contains("legs")) return new[] { "Item_Armor_Legs", "Armor_Legs" };
        if (ctx.Contains("gloves")) return new[] { "Item_Armor_Gloves", "Armor_Gloves" };
        if (ctx.Contains("boots")) return new[] { "Item_Armor_Boots", "Armor_Boot", "Boots", "Feet", "Foot" };
        if (ctx.Contains("cloak")) return new[] { "Item_Cloak", "Cloak" };
        if (ctx.Contains("headgear") || ctx.Contains("head")) return new[] { "Item_Headgear", "Headgear" };
        if (ctx.Contains("magicsource") || ctx.Contains("magic")) return new[] { "Item_MagicSource", "MagicSource" };
        if (ctx.Contains("weapon"))
        {
            var marker = "Item_Weapon_";
            var idx = prefabName.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (idx >= 0)
            {
                var rest = prefabName[(idx + marker.Length)..];
                var end = rest.IndexOf('_');
                var weaponType = end > 0 ? rest[..end] : rest;
                return new[] { $"{marker}{weaponType}", $"Weapon_{weaponType}" };
            }
            return new[] { marker, "Weapon_" };
        }
        var fallback = prefabName.StartsWith("Item_", StringComparison.OrdinalIgnoreCase)
            ? prefabName.Split('_').Take(3).Aggregate((a, b) => $"{a}_{b}")
            : prefabName;
        return new[] { fallback };
    }

    static bool IsLikelyGrantableItem(string name)
    {
        if (!name.StartsWith("Item_", StringComparison.OrdinalIgnoreCase))
            return false;

        var blocked = new[]
        {
            "Recipe", "Blueprint", "Journal", "Unlock", "Research", "Tech", "DropTable",
            "VBlood", "Shattered", "Buildable", "CastleHeart"
        };
        return !blocked.Any(term => name.Contains(term, StringComparison.OrdinalIgnoreCase));
    }

    static bool IsCompatibleKitSlot(string liveName, string requestedName, string context)
    {
        var ctx = context.ToLowerInvariant();
        bool Contains(string value) => liveName.Contains(value, StringComparison.OrdinalIgnoreCase);

        if (ctx.Contains("chest")) return Contains("Armor_Chest");
        if (ctx.Contains("legs")) return Contains("Armor_Legs");
        if (ctx.Contains("gloves")) return Contains("Armor_Gloves");
        if (ctx.Contains("boots")) return Contains("Armor_Boots");
        if (ctx.Contains("cloak")) return Contains("Cloak");
        if (ctx.Contains("headgear") || ctx.Contains("head")) return Contains("Headgear");
        if (ctx.Contains("magicsource") || ctx.Contains("magic"))
        {
            if (!Contains("MagicSource")) return false;
            var school = requestedName.Split('_').FirstOrDefault(part =>
                part.Equals("Blood", StringComparison.OrdinalIgnoreCase) ||
                part.Equals("Chaos", StringComparison.OrdinalIgnoreCase) ||
                part.Equals("Frost", StringComparison.OrdinalIgnoreCase) ||
                part.Equals("Storm", StringComparison.OrdinalIgnoreCase) ||
                part.Equals("Unholy", StringComparison.OrdinalIgnoreCase) ||
                part.Equals("Illusion", StringComparison.OrdinalIgnoreCase));
            return string.IsNullOrWhiteSpace(school) || Contains(school);
        }
        if (ctx.Contains("weapon"))
        {
            var requestedType = requestedName.Split('_').SkipWhile(part => !part.Equals("Weapon", StringComparison.OrdinalIgnoreCase)).Skip(1).FirstOrDefault();
            if (string.IsNullOrWhiteSpace(requestedType))
                return Contains("Weapon_");
            return Contains($"Weapon_{requestedType}") ||
                   (requestedType.Equals("Axe", StringComparison.OrdinalIgnoreCase) && Contains("Weapon_Axes")) ||
                   (requestedType.Equals("Axes", StringComparison.OrdinalIgnoreCase) && Contains("Weapon_Axe"));
        }
        return liveName.Contains(requestedName, StringComparison.OrdinalIgnoreCase);
    }

    static int ScoreKitCandidate(string liveName, string requestedLower)
    {
        var lower = liveName.ToLowerInvariant();
        var score = 0;
        if (lower.Equals(requestedLower, StringComparison.Ordinal)) score += 1000;
        if (lower.Contains(requestedLower, StringComparison.Ordinal)) score += 700;
        if (lower.Contains("t09", StringComparison.Ordinal) || lower.Contains("dracula", StringComparison.Ordinal)) score += 90;
        if (lower.Contains("t08", StringComparison.Ordinal) || lower.Contains("bloodmoon", StringComparison.Ordinal)) score += 85;
        if (lower.Contains("sanguine", StringComparison.Ordinal)) score += 75;
        if (lower.Contains("legendary", StringComparison.Ordinal)) score += 65;
        if (lower.Contains("epic", StringComparison.Ordinal)) score += 55;
        if (lower.Contains("merciless", StringComparison.Ordinal)) score += 35;
        if (lower.Contains("broken", StringComparison.Ordinal) || lower.Contains("shattered", StringComparison.Ordinal)) score -= 150;
        return score;
    }

    /// <summary>
    /// Apply a kit to a player with a specified mode.
    /// FullLoadout: replaces entire loadout (clear inventory, apply blood, abilities, etc.).
    /// AdditiveReward: grants only items without clearing inventory or modifying other state.
    /// </summary>
    public static OperationResult ApplyKit(Entity playerCharacter, string modeId, KitApplyMode mode)
    {
        var kit = LoadKit(modeId);
        if (kit == null)
            return OperationResult.Fail($"Kit not found for mode '{modeId}'");

        if (mode == KitApplyMode.FullLoadout)
            return ApplyKit(playerCharacter, modeId);

        // AdditiveReward mode: skip inventory clear, blood, and abilities
        var pipelineResult = InlinePipeline.Run($"KitAdditive:{modeId}", p =>
        {
            if (kit.Armors != null)
            {
                p.Step("ApplyArmor", () =>
                {
                    ApplyArmor(playerCharacter, kit.Armors);
                });
            }

            p.Step("ApplyWeapons", () =>
            {
                foreach (var weapon in kit.Weapons)
                    ApplyWeapon(playerCharacter, weapon);
            });

            p.Step("ApplyItems", () =>
            {
                foreach (var item in kit.Items)
                {
                    TryGiveResolvedItem(playerCharacter, item.Prefab, item.Amount, "item");
                }
            });
        });

        if (!pipelineResult.Success)
        {
            BattleLuckPlugin.LogWarning(
                $"[KitController] Additive kit apply failed for {modeId} at step '{pipelineResult.FailedStep}': {pipelineResult.Error}"
            );
            return OperationResult.Fail(
                $"Additive kit apply failed at step '{pipelineResult.FailedStep}': {pipelineResult.Error}"
            );
        }

        BattleLuckPlugin.LogInfo(
            $"[KitController] Additive kit '{modeId}' applied to {playerCharacter.GetSteamId()} with {pipelineResult.Steps.Count} steps."
        );

        return OperationResult.Ok();
    }

    public static void ApplyFullKit(Entity playerCharacter) => ApplyKit(playerCharacter, "bloodbath");

    public static void SetMaxLevel(Entity playerCharacter) => playerCharacter.SetEquipmentLevel(90f, 90f, 90f);

    public static void ApplyWeaponsKit(Entity playerCharacter)
    {
        var kit = LoadKit("bloodbath");
        if (kit == null) return;
        foreach (var weapon in kit.Weapons)
        {
            var guid = PrefabHelper.GetValidPrefabGuidDeep(weapon.Prefab);
            if (guid.HasValue) playerCharacter.TryGiveItem(guid.Value, weapon.Amount);
        }
    }

    public static void ApplyArmorKit(Entity playerCharacter)
    {
        var kit = LoadKit("bloodbath");
        if (kit == null) return;
        if (kit.Armors != null) ApplyArmor(playerCharacter, kit.Armors);
    }

    public static List<PrefabGUID> GetKitPrefabs(string kitId = "bloodbath")
    {
        var kit = LoadKit(kitId);
        if (kit == null) return new();

        var result = new List<PrefabGUID>();
        foreach (var w in kit.Weapons)
        {
            var g = PrefabHelper.GetValidPrefabGuidDeep(w.Prefab);
            if (g.HasValue) result.Add(g.Value);
        }
        if (kit.Armors != null)
        {
            AddIfResolved(result, kit.Armors.Chest);
            AddIfResolved(result, kit.Armors.Legs);
            AddIfResolved(result, kit.Armors.Gloves);
            AddIfResolved(result, kit.Armors.Boots);
            AddIfResolved(result, kit.Armors.Cloak);
            AddIfResolved(result, kit.Armors.Headgear);
            AddIfResolved(result, kit.Armors.MagicSource);
            AddIfResolved(result, kit.Armors.Bag);
        }
        foreach (var item in kit.Items)
        {
            var g = PrefabHelper.GetValidPrefabGuidDeep(item.Prefab);
            if (g.HasValue) result.Add(g.Value);
        }
        return result;
    }

    /// <summary>Remove event-kit items, ability overrides, and configured passive buffs.</summary>
    public static int RemoveEventKit(Entity playerCharacter, string kitId)
    {
        var removed = 0;
        foreach (var prefab in GetKitPrefabs(kitId).Distinct())
        {
            try
            {
                if (playerCharacter.TryRemoveItem(prefab, 999))
                    removed++;
            }
            catch { }
        }

        removed += AbilityController.ClearAbilitySlots(playerCharacter);

        var kit = LoadKit(kitId);
        if (kit != null)
        {
            foreach (var passive in kit.PassiveSpells)
            {
                var guid = PrefabHelper.GetPrefabGuidDeep(passive.Prefab);
                if (guid.HasValue && playerCharacter.HasBuff(guid.Value))
                {
                    playerCharacter.TryRemoveBuff(guid.Value);
                    removed++;
                }
            }
        }

        BattleLuckPlugin.LogInfo($"[KitController] Removed event kit '{kitId}' from {playerCharacter.GetSteamId()} ({removed} loadout entries).");
        return removed;
    }

    static void AddIfResolved(List<PrefabGUID> list, string? name)
    {
        if (string.IsNullOrEmpty(name)) return;
        var g = PrefabHelper.GetValidPrefabGuidDeep(name);
        if (g.HasValue) list.Add(g.Value);
    }
}
