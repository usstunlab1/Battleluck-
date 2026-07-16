using ProjectM;
using ProjectM.Network;
using Stunlock.Core;
using Unity.Entities;
using Unity.Mathematics;

namespace BattleLuck.Services
{
    /// <summary>
    /// Full 13-category player state save/restore with JSON file persistence.
    /// Categories: position, health, energy, blood, equipment levels, equipment slots,
    /// inventory, weapons, abilities, jewels, passives, buffs, progression.
    /// Snapshots stored at BepInEx/data/BattleLuck/snapshots/{playerId}.json
    /// </summary>
    public sealed class PlayerStateController
    {
        public const int KitRollbackZoneHash = -999;

        static readonly string SnapshotDir = Path.Combine(
            BepInEx.Paths.BepInExRootPath, "data", "BattleLuck", "snapshots");

    static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    readonly Dictionary<ulong, PlayerSnapshot> _cache = new();

    public PlayerStateController()
    {
        Directory.CreateDirectory(SnapshotDir);
    }

    /// <summary>Save full 13-category entity state for a player.</summary>
    public void SaveSnapshot(Entity character, int zoneHash, float3? returnPositionOverride = null, ulong steamIdOverride = 0, string eventRunId = "", string eventModeId = "")
    {
        ulong steamId = steamIdOverride != 0 ? steamIdOverride : character.GetSteamId();
        if (steamId == 0) return;

        var em = VRisingCore.EntityManager;
        var snap = new PlayerSnapshot
        {
            Version = 2,
            GameVersion = Application.version,
            PlayerId = steamId.ToString(),
            Timestamp = DateTime.UtcNow,
            ZoneHash = zoneHash,
            EventRunId = eventRunId?.Trim() ?? "",
            EventModeId = eventModeId?.Trim() ?? ""
        };

        // 1. Name
        if (character.TryGetComponent(out PlayerCharacter pc))
        {
            var userEntity = pc.UserEntity;
            if (userEntity.Exists() && userEntity.TryGetComponent(out User user))
                snap.Name = user.CharacterName.ToString();
        }

        // 2. Position
        var pos = returnPositionOverride ?? character.GetPosition();
        snap.Position = new Vec3Snapshot { X = pos.x, Y = pos.y, Z = pos.z };

        // 3. Health
        if (character.TryGetComponent(out Health health))
            snap.Health = new HealthSnapshot { Current = health.Value, Max = health.MaxHealth };

        // 4. Energy — V Rising doesn't expose a standalone Energy component;
        // placeholder for future use if discovered.
        snap.Energy = new EnergySnapshot { Current = 0, Max = 0 };

        // 5. Blood
        if (character.TryGetComponent(out Blood blood))
        {
            snap.Blood = new BloodSnapshot
            {
                TypeGuid = blood.BloodType.GuidHash,
                Type = PrefabHelper.GetName(blood.BloodType) ?? blood.BloodType.GuidHash.ToString(),
                Quality = blood.Quality,
                Value = blood.Value
            };
        }

        // 6. Equipment Levels
        if (character.TryGetComponent(out Equipment equipment))
        {
            snap.EquipmentLevels = new EquipmentLevelsSnapshot
            {
                Weapon = equipment.WeaponLevel._Value,
                Armor = equipment.ArmorLevel._Value,
                Spell = equipment.SpellLevel._Value
            };

            CaptureEquipmentSlots(snap, equipment);
        }

        // 7. Inventory
        if (InventoryUtilities.TryGetInventoryEntity(em, character, out Entity invEntity)
            && em.HasBuffer<InventoryBuffer>(invEntity))
        {
            var buffer = em.GetBuffer<InventoryBuffer>(invEntity);
            for (int i = 0; i < buffer.Length; i++)
            {
                var slot = buffer[i];
                if (slot.ItemType != PrefabGUID.Empty && slot.Amount > 0)
                {
                    snap.Inventory.Add(new InventoryItemSnapshot
                    {
                        Guid = slot.ItemType.GuidHash,
                        Prefab = PrefabHelper.GetName(slot.ItemType) ?? "",
                        Amount = slot.Amount,
                        Slot = i
                    });

                    CaptureDerivedEquipmentAndWeapons(snap, slot.ItemType, i);
                }
            }
        }

        // 8. Buffs
        if (em.HasBuffer<BuffBuffer>(character))
        {
            var buffs = em.GetBuffer<BuffBuffer>(character);
            for (int i = 0; i < buffs.Length; i++)
            {
                var b = buffs[i];
                var buffEntity = b.Entity;
                var prefab = b.PrefabGuid;
                float remaining = 0f;
                int stacks = 1;

                if (buffEntity.Exists() && buffEntity.TryGetComponent(out LifeTime lifeTime))
                    remaining = lifeTime.Duration;

                snap.Buffs.Add(new BuffSnapshot
                {
                    Guid = prefab.GuidHash,
                    Prefab = PrefabHelper.GetName(prefab) ?? "",
                    Stacks = stacks,
                    RemainingDuration = remaining
                });

                CapturePassiveBuffSnapshot(snap, prefab);

                if (buffEntity.Exists() && em.HasBuffer<ReplaceAbilityOnSlotBuff>(buffEntity))
                {
                    var replaceBuffer = em.GetBuffer<ReplaceAbilityOnSlotBuff>(buffEntity);
                    for (int r = 0; r < replaceBuffer.Length; r++)
                    {
                        var entry = replaceBuffer[r];
                        CaptureAbilitySlotSnapshot(snap, entry.Slot, entry.NewGroupId, entry.CopyCooldown, entry.Priority);
                    }
                }
            }
        }

        // Cache + persist to disk
        _cache[steamId] = snap;
        WriteToDisk(steamId, snap);

        BattleLuckPlugin.LogInfo(
            $"[PlayerState] Saved full snapshot for {steamId} " +
            $"({snap.Inventory.Count} items, {PlayerSnapshotMetrics.EquipmentCount(snap)} equipped, {snap.Weapons.Count} weapons, " +
            $"{PlayerSnapshotMetrics.AbilityCount(snap)} abilities, {snap.Passives.Count} passives, {snap.Buffs.Count} buffs) in zone {zoneHash}."
        );
    }

    /// <summary>
    /// Save the player's pre-event state only when no rollback snapshot exists yet.
    /// Event enter flows call this before kit/actions so old configs cannot overwrite
    /// the real return position with an already-teleported arena state.
    /// </summary>
    public bool SaveSnapshotIfMissing(Entity character, int zoneHash, float3? returnPositionOverride = null, ulong steamIdOverride = 0, string eventRunId = "", string eventModeId = "")
    {
        ulong steamId = steamIdOverride != 0 ? steamIdOverride : character.GetSteamId();
        if (steamId == 0) return false;

        var existing = GetSnapshot(steamId);
        if (existing != null && existing.ZoneHash != KitRollbackZoneHash)
            return false;

        SaveSnapshot(character, zoneHash, returnPositionOverride, steamId, eventRunId, eventModeId);
        return true;
    }

    /// <summary>
    /// Hard event-entry boundary: preserve rollback state first, then remove the
    /// player's current inventory/equipment items before event kits or actions run.
    /// </summary>
    public OperationResult PrepareForEventEntry(Entity character, int zoneHash, float3? returnPositionOverride = null, ulong steamIdOverride = 0, string eventRunId = "", string eventModeId = "")
    {
        ulong steamId = steamIdOverride != 0 ? steamIdOverride : character.GetSteamId();
        if (steamId == 0)
            return OperationResult.Fail("Player SteamID not found.");

        var savedNow = SaveSnapshotIfMissing(character, zoneHash, returnPositionOverride, steamId, eventRunId, eventModeId);
        var removedAbilities = AbilityController.ClearAbilitySlots(character);
        var removedPassives = AbilityController.ClearPassiveSpells(character);
        var removed = ClearInventory(character);

        BattleLuckPlugin.LogInfo(
            $"[PlayerState] Prepared {steamId} for event zone {zoneHash}: " +
            $"snapshot={(savedNow ? "created" : "kept")}, cleared={removed} item/equipment stack(s), " +
            $"abilityOverrides={removedAbilities}, passives={removedPassives}.");

        return OperationResult.Ok();
    }

    /// <summary>Restore full entity state from snapshot in 13-step order.</summary>
    public bool RestoreSnapshot(Entity character, int zoneHash)
    {
        ulong steamId = character.GetSteamId();
        if (steamId == 0) return false;

        var snap = GetSnapshot(steamId);
        if (snap == null)
        {
            BattleLuckPlugin.LogWarning($"[PlayerState] No snapshot found for {steamId}.");
            return false;
        }

        var em = VRisingCore.EntityManager;

        // Step 1: remove the complete event loadout before restoring anything.
        // This deletes event ability replacements and any event-only buffs.
        AbilityController.ClearAbilitySlots(character);
        ClearBuffsNotInSnapshot(character, snap.Buffs);
        ClearInventory(character);

        // Step 2: restore inventory in original slot order. The native API
        // chooses the first available slot, so ordered grants retain layout as
        // closely as the server API allows.
        var restoredItemGuids = new HashSet<int>();
        foreach (var item in snap.Inventory.OrderBy(item => item.Slot))
        {
            var guid = new PrefabGUID(item.Guid);
            if (character.TryGiveItem(guid, item.Amount))
                restoredItemGuids.Add(item.Guid);
        }

        // Step 3: restore missing equipment/weapon items without duplicating
        // prefabs already present in the captured inventory.
        RestoreEquipmentItems(character, snap.Equipment, restoredItemGuids);

        // Step 4: restore legacy weapon snapshot entries, de-duplicated.
        RestoreWeapons(character, snap.Weapons, restoredItemGuids);

        // Step 4b: restore the exact equipped prefab assignments.
        RestoreEquipmentSlotAssignments(character, snap.Equipment);

        // Step 5: Restore equipment levels
        character.SetEquipmentLevel(
            snap.EquipmentLevels.Weapon,
            snap.EquipmentLevels.Armor,
            snap.EquipmentLevels.Spell);

        // Step 6: Restore blood type + quality
        if (character.TryGetComponent(out Blood blood))
        {
            blood.BloodType = new PrefabGUID(snap.Blood.TypeGuid);
            blood.Quality = snap.Blood.Quality;
            blood.Value = snap.Blood.Value;
            character.Write(blood);
        }

        // Step 7: restore all captured ability replacement slots (1-7),
        // including priority/cooldown behavior.
        RestoreAbilitySlots(character, snap.Abilities);

        // Step 8: Restore passive buff spells
        RestorePassives(character, snap.Passives);

        // Step 10: Restore buffs
        foreach (var buff in snap.Buffs)
        {
            var guid = new PrefabGUID(buff.Guid);
            if (!character.HasBuff(guid))
                character.TryApplyBuff(guid);
        }

        // Step 11: Restore health + energy
        if (character.TryGetComponent(out Health hp))
        {
            hp.MaxHealth._Value = snap.Health.Max;
            hp.Value = snap.Health.Current;
            character.Write(hp);
        }

        // Step 12: Restore position
        character.SetPosition(new float3(snap.Position.X, snap.Position.Y, snap.Position.Z));

        // Cleanup
        _cache.Remove(steamId);
        DeleteFromDisk(steamId);

        BattleLuckPlugin.LogInfo($"[PlayerState] Restored snapshot for {steamId} ({snap.Inventory.Count} items).");
        return true;
    }

    static void CaptureDerivedEquipmentAndWeapons(PlayerSnapshot snap, PrefabGUID itemType, int slot)
    {
        var itemName = PrefabHelper.GetName(itemType) ?? string.Empty;
        if (string.IsNullOrEmpty(itemName))
            return;

        if (itemName.StartsWith("Item_Weapon_", StringComparison.OrdinalIgnoreCase))
        {
            snap.Weapons.Add(new WeaponSnapshot
            {
                Guid = itemType.GuidHash,
                Prefab = itemName,
                Slot = slot
            });
            return;
        }

        var equipmentSlot = new EquipmentSlotSnapshot
        {
            Guid = itemType.GuidHash,
            Prefab = itemName
        };

        if (itemName.StartsWith("Item_Armor_Chest_", StringComparison.OrdinalIgnoreCase) && snap.Equipment.Chest == null)
            snap.Equipment.Chest = equipmentSlot;
        else if (itemName.StartsWith("Item_Armor_Legs_", StringComparison.OrdinalIgnoreCase) && snap.Equipment.Legs == null)
            snap.Equipment.Legs = equipmentSlot;
        else if (itemName.StartsWith("Item_Armor_Boots_", StringComparison.OrdinalIgnoreCase) && snap.Equipment.Boots == null)
            snap.Equipment.Boots = equipmentSlot;
        else if (itemName.StartsWith("Item_Armor_Gloves_", StringComparison.OrdinalIgnoreCase) && snap.Equipment.Gloves == null)
            snap.Equipment.Gloves = equipmentSlot;
        else if (itemName.StartsWith("Item_Headgear_", StringComparison.OrdinalIgnoreCase) && snap.Equipment.Head == null)
            snap.Equipment.Head = equipmentSlot;
        else if (itemName.StartsWith("Item_Cloak_", StringComparison.OrdinalIgnoreCase) && snap.Equipment.Cloak == null)
            snap.Equipment.Cloak = equipmentSlot;
        else if (itemName.StartsWith("Item_MagicSource_", StringComparison.OrdinalIgnoreCase) && snap.Equipment.MagicSource == null)
            snap.Equipment.MagicSource = equipmentSlot;
        else if (itemName.StartsWith("Item_Bag_", StringComparison.OrdinalIgnoreCase) && snap.Equipment.Bag == null)
            snap.Equipment.Bag = equipmentSlot;
    }

    static void CaptureEquipmentSlots(PlayerSnapshot snap, Equipment equipment)
    {
        snap.Equipment.Weapon = CaptureEquipmentSlot(equipment.WeaponSlot);
        snap.Equipment.Head = CaptureEquipmentSlot(equipment.ArmorHeadgearSlot);
        snap.Equipment.Chest = CaptureEquipmentSlot(equipment.ArmorChestSlot);
        snap.Equipment.Gloves = CaptureEquipmentSlot(equipment.ArmorGlovesSlot);
        snap.Equipment.Legs = CaptureEquipmentSlot(equipment.ArmorLegsSlot);
        snap.Equipment.Boots = CaptureEquipmentSlot(equipment.ArmorFootgearSlot);
        snap.Equipment.Cloak = CaptureEquipmentSlot(equipment.CloakSlot);
        snap.Equipment.MagicSource = CaptureEquipmentSlot(equipment.GrimoireSlot);
        snap.Equipment.Bag = CaptureEquipmentSlot(equipment.BagSlot);

        var weapon = snap.Equipment.Weapon;
        if (weapon != null)
        {
            snap.Weapons.Add(new WeaponSnapshot
            {
                Guid = weapon.Guid,
                Prefab = weapon.Prefab,
                Slot = -1
            });
        }
    }

    static EquipmentSlotSnapshot? CaptureEquipmentSlot(EquipmentSlot slot)
    {
        if (slot.SlotId == PrefabGUID.Empty)
            return null;

        return new EquipmentSlotSnapshot
        {
            Guid = slot.SlotId.GuidHash,
            Prefab = PrefabHelper.GetLivePrefabName(slot.SlotId)
                ?? PrefabHelper.GetName(slot.SlotId)
                ?? slot.SlotId.GuidHash.ToString()
        };
    }

    static void CaptureAbilitySlotSnapshot(
        PlayerSnapshot snap,
        int slot,
        PrefabGUID abilityGuid,
        bool copyCooldown,
        int priority)
    {
        if (abilityGuid == PrefabGUID.Empty)
            return;

        var ability = new AbilitySlotSnapshot
        {
            Slot = slot,
            Guid = abilityGuid.GuidHash,
            Prefab = PrefabHelper.GetLivePrefabName(abilityGuid) ?? PrefabHelper.GetName(abilityGuid) ?? string.Empty,
            CopyCooldown = copyCooldown,
            Priority = priority
        };

        snap.Abilities.Slots.Add(ability);

        // Legacy named fields keep existing snapshot files and tooling readable.
        switch (slot)
        {
            case 1:
                snap.Abilities.Primary = ability;
                break;
            case 2:
                snap.Abilities.Veil = ability;
                break;
            case 3:
                snap.Abilities.Travel = ability;
                break;
            case 4:
                snap.Abilities.Counter = ability;
                break;
            case 5:
                snap.Abilities.Spell1 = ability;
                break;
            case 6:
                snap.Abilities.Spell2 = ability;
                break;
            case 7:
                snap.Abilities.Ultimate = ability;
                break;
        }
    }

    static void CapturePassiveBuffSnapshot(PlayerSnapshot snap, PrefabGUID buffGuid)
    {
        if (buffGuid == PrefabGUID.Empty)
            return;

        var buffName = PrefabHelper.GetLivePrefabName(buffGuid) ?? PrefabHelper.GetName(buffGuid) ?? string.Empty;
        if (!buffName.Contains("Passive", StringComparison.OrdinalIgnoreCase))
            return;

        if (snap.Passives.Any(p => p.Guid == buffGuid.GuidHash))
            return;

        snap.Passives.Add(new PassiveSlotSnapshot
        {
            Slot = snap.Passives.Count,
            Guid = buffGuid.GuidHash,
            Prefab = buffName
        });
    }

    static void RestoreEquipmentItems(Entity character, EquipmentSlotsSnapshot equipment, HashSet<int> restoredGuids)
    {
        TryGiveSlot(character, equipment.Weapon, restoredGuids);
        TryGiveSlot(character, equipment.Chest, restoredGuids);
        TryGiveSlot(character, equipment.Legs, restoredGuids);
        TryGiveSlot(character, equipment.Boots, restoredGuids);
        TryGiveSlot(character, equipment.Gloves, restoredGuids);
        TryGiveSlot(character, equipment.Head, restoredGuids);
        TryGiveSlot(character, equipment.Cloak, restoredGuids);
        TryGiveSlot(character, equipment.MagicSource, restoredGuids);
        TryGiveSlot(character, equipment.Bag, restoredGuids);
    }

    static void RestoreWeapons(Entity character, List<WeaponSnapshot> weapons, HashSet<int> restoredGuids)
    {
        foreach (var weapon in weapons)
        {
            if (weapon.Guid == 0 || !restoredGuids.Add(weapon.Guid))
                continue;
            character.TryGiveItem(new PrefabGUID(weapon.Guid), 1);
        }
    }

    static void RestoreAbilitySlots(Entity character, AbilitiesSnapshot abilities)
    {
        if (abilities.Slots.Count > 0)
        {
            foreach (var ability in abilities.Slots.Where(a => a.Guid != 0 && a.Slot is >= 1 and <= 7))
            {
                AbilityController.SetSpellOnSlot(
                    character,
                    ability.Slot,
                    new PrefabGUID(ability.Guid),
                    ability.CopyCooldown,
                    ability.Priority);
            }
            return;
        }

        // Version-1 snapshot compatibility.
        if (abilities.Primary != null)
            AbilityController.SetSpellOnSlot(character, 1, new PrefabGUID(abilities.Primary.Guid));
        if (abilities.Veil != null)
            AbilityController.SetSpellOnSlot(character, 2, new PrefabGUID(abilities.Veil.Guid));
        if (abilities.Travel != null)
            AbilityController.SetSpellOnSlot(character, 3, new PrefabGUID(abilities.Travel.Guid));
        if (abilities.Counter != null)
            AbilityController.SetSpellOnSlot(character, 4, new PrefabGUID(abilities.Counter.Guid));
        if (abilities.Spell1 != null)
            AbilityController.SetSpellOnSlot(character, 5, new PrefabGUID(abilities.Spell1.Guid));
        if (abilities.Spell2 != null)
            AbilityController.SetSpellOnSlot(character, 6, new PrefabGUID(abilities.Spell2.Guid));
        if (abilities.Ultimate != null)
            AbilityController.SetSpellOnSlot(character, 7, new PrefabGUID(abilities.Ultimate.Guid));
    }

    static void RestorePassives(Entity character, List<PassiveSlotSnapshot> passives)
    {
        foreach (var passive in passives)
        {
            var guid = new PrefabGUID(passive.Guid);
            if (!character.HasBuff(guid))
                character.TryApplyBuff(guid);
        }
    }

    static void TryGiveSlot(Entity character, EquipmentSlotSnapshot? slot, HashSet<int> restoredGuids)
    {
        if (slot == null || slot.Guid == 0 || !restoredGuids.Add(slot.Guid))
            return;

        character.TryGiveItem(new PrefabGUID(slot.Guid), 1);
    }

    static void RestoreEquipmentSlotAssignments(Entity character, EquipmentSlotsSnapshot saved)
    {
        if (!character.Has<Equipment>())
            return;

        var equipment = character.Read<Equipment>();
        equipment.WeaponSlot.SlotId = ToGuid(saved.Weapon);
        equipment.ArmorHeadgearSlot.SlotId = ToGuid(saved.Head);
        equipment.ArmorChestSlot.SlotId = ToGuid(saved.Chest);
        equipment.ArmorGlovesSlot.SlotId = ToGuid(saved.Gloves);
        equipment.ArmorLegsSlot.SlotId = ToGuid(saved.Legs);
        equipment.ArmorFootgearSlot.SlotId = ToGuid(saved.Boots);
        equipment.CloakSlot.SlotId = ToGuid(saved.Cloak);
        equipment.GrimoireSlot.SlotId = ToGuid(saved.MagicSource);
        equipment.BagSlot.SlotId = ToGuid(saved.Bag);
        character.Write(equipment);

        static PrefabGUID ToGuid(EquipmentSlotSnapshot? slot) =>
            slot == null || slot.Guid == 0 ? PrefabGUID.Empty : new PrefabGUID(slot.Guid);
    }

    static void ClearBuffsNotInSnapshot(Entity character, IReadOnlyCollection<BuffSnapshot> savedBuffs)
    {
        var em = VRisingCore.EntityManager;
        if (!em.HasBuffer<BuffBuffer>(character))
            return;

        var allowed = savedBuffs.Select(buff => buff.Guid).Where(guid => guid != 0).ToHashSet();
        var current = em.GetBuffer<BuffBuffer>(character);
        var remove = new HashSet<int>();
        for (var i = 0; i < current.Length; i++)
        {
            var guid = current[i].PrefabGuid.GuidHash;
            if (guid != 0 && !allowed.Contains(guid))
                remove.Add(guid);
        }

        foreach (var guid in remove)
            character.TryRemoveBuff(new PrefabGUID(guid));
    }

    /// <summary>Clear all items from player inventory and equipped-slot item prefabs.</summary>
    static int ClearInventory(Entity character)
    {
        var em = VRisingCore.EntityManager;
        var sgm = VRisingCore.ServerGameManager;
        if (!InventoryUtilities.TryGetInventoryEntity(em, character, out Entity invEntity)) return 0;
        if (!em.HasBuffer<InventoryBuffer>(invEntity)) return 0;

        var buffer = em.GetBuffer<InventoryBuffer>(invEntity);
        var toRemove = new List<(PrefabGUID prefab, int amount)>();
        for (int i = 0; i < buffer.Length; i++)
        {
            var slot = buffer[i];
            if (slot.ItemType != PrefabGUID.Empty && slot.Amount > 0)
                toRemove.Add((slot.ItemType, slot.Amount));
        }

        foreach (var prefab in GetEquippedItemPrefabs(character))
        {
            if (prefab != PrefabGUID.Empty && !toRemove.Any(x => x.prefab.GuidHash == prefab.GuidHash))
                toRemove.Add((prefab, 999));
        }

        var removed = 0;
        foreach (var (prefab, amount) in toRemove)
        {
            var amountToRemove = Math.Max(1, amount);
            var ok = character.TryRemoveItem(prefab, amountToRemove);

            if (!ok)
            {
                try { ok = sgm.TryRemoveInventoryItem(character, prefab, amountToRemove); }
                catch { ok = false; }
            }

            if (ok)
                removed++;
        }

        return removed;
    }

    static IEnumerable<PrefabGUID> GetEquippedItemPrefabs(Entity character)
    {
        if (!character.Has<Equipment>())
            yield break;

        var equipment = character.Read<Equipment>();
        foreach (var prefab in new[]
        {
            equipment.ArmorHeadgearSlot.SlotId,
            equipment.ArmorChestSlot.SlotId,
            equipment.ArmorGlovesSlot.SlotId,
            equipment.ArmorLegsSlot.SlotId,
            equipment.ArmorFootgearSlot.SlotId,
            equipment.CloakSlot.SlotId,
            equipment.WeaponSlot.SlotId,
            equipment.GrimoireSlot.SlotId,
            equipment.BagSlot.SlotId
        })
        {
            if (prefab != PrefabGUID.Empty)
                yield return prefab;
        }
    }

    public bool HasSnapshot(ulong steamId) => _cache.ContainsKey(steamId) || FileExists(steamId);

    public PlayerSnapshot? GetSnapshot(ulong steamId)
    {
        if (_cache.TryGetValue(steamId, out var cached))
            return cached;
        return ReadFromDisk(steamId);
    }

    /// <summary>Enumerate valid persisted snapshots without changing state.</summary>
    public IReadOnlyList<PlayerSnapshot> ListSnapshots()
    {
        var snapshots = new List<PlayerSnapshot>();
        if (!Directory.Exists(SnapshotDir))
            return snapshots;

        foreach (var path in Directory.GetFiles(SnapshotDir, "*.json"))
        {
            var name = Path.GetFileNameWithoutExtension(path);
            if (!ulong.TryParse(name, out var steamId))
                continue;
            var snapshot = GetSnapshot(steamId);
            if (snapshot != null)
                snapshots.Add(snapshot);
        }

        return snapshots;
    }

    public void ClearSnapshot(ulong steamId)
    {
        _cache.Remove(steamId);
        DeleteFromDisk(steamId);
    }

    public void ClearAll()
    {
        _cache.Clear();
        if (Directory.Exists(SnapshotDir))
        {
            foreach (var file in Directory.GetFiles(SnapshotDir, "*.json"))
            {
                try { File.Delete(file); } catch { }
            }
        }
    }

    // ── File persistence ────────────────────────────────────────────────

    static string GetPath(ulong steamId) => Path.Combine(SnapshotDir, $"{steamId}.json");

    static bool FileExists(ulong steamId) => File.Exists(GetPath(steamId));

    void WriteToDisk(ulong steamId, PlayerSnapshot snap)
    {
        try
        {
            var json = JsonSerializer.Serialize(snap, JsonOpts);
            File.WriteAllText(GetPath(steamId), json);
        }
        catch (Exception ex)
        {
            BattleLuckPlugin.LogError($"[PlayerState] Failed to write snapshot for {steamId}: {ex.Message}");
        }
    }

    static PlayerSnapshot? ReadFromDisk(ulong steamId)
    {
        var path = GetPath(steamId);
        if (!File.Exists(path)) return null;
        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<PlayerSnapshot>(json, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });
        }
        catch (Exception ex)
        {
            BattleLuckPlugin.LogError($"[PlayerState] Failed to read snapshot for {steamId}: {ex.Message}");
            return null;
        }
    }

    static void DeleteFromDisk(ulong steamId)
    {
        var path = GetPath(steamId);
        if (File.Exists(path))
        {
            try { File.Delete(path); } catch { }
        }
    }

    static class PlayerSnapshotMetrics
    {
        public static int EquipmentCount(PlayerSnapshot snapshot)
        {
            var count = 0;
            if (snapshot.Equipment.Weapon != null) count++;
            if (snapshot.Equipment.Chest != null) count++;
            if (snapshot.Equipment.Legs != null) count++;
            if (snapshot.Equipment.Boots != null) count++;
            if (snapshot.Equipment.Gloves != null) count++;
            if (snapshot.Equipment.Head != null) count++;
            if (snapshot.Equipment.Cloak != null) count++;
            if (snapshot.Equipment.MagicSource != null) count++;
            if (snapshot.Equipment.Bag != null) count++;
            return count;
        }

        public static int AbilityCount(PlayerSnapshot snapshot)
        {
            if (snapshot.Abilities.Slots.Count > 0)
                return snapshot.Abilities.Slots.Count;

            var count = 0;
            if (snapshot.Abilities.Primary != null) count++;
            if (snapshot.Abilities.Veil != null) count++;
            if (snapshot.Abilities.Travel != null) count++;
            if (snapshot.Abilities.Counter != null) count++;
            if (snapshot.Abilities.Spell1 != null) count++;
            if (snapshot.Abilities.Spell2 != null) count++;
            if (snapshot.Abilities.Ultimate != null) count++;
            return count;
        }
    }
}

}
