using ProjectM;
using ProjectM.Shared;
using Stunlock.Core;
using Unity.Entities;

/// <summary>
/// Manages player ability/spell slots.
/// Uses runtime discovery from PrefabCollectionSystem for valid ability GUIDs.
/// Slot-correct assignment via EquipAbilities for kit application.
/// </summary>
public static class AbilityController
{
    // Schools used for UI/grouping — populated at runtime
    static readonly Dictionary<string, List<PrefabGUID>> _abilitiesBySchool = new(StringComparer.OrdinalIgnoreCase);
    static List<PrefabGUID> _allAbilities = new();
    static bool _discovered;

    public static IReadOnlyDictionary<string, List<PrefabGUID>> AbilitiesBySchool => _abilitiesBySchool;

    // School prefixes used to discover abilities from the game's prefab names
    static readonly string[] SchoolPrefixes = { "AB_Blood_", "AB_Frost_", "AB_Unholy_", "AB_Chaos_", "AB_Storm_", "AB_Illusion_" };
    static readonly string[] SchoolNames    = { "Blood",     "Frost",     "Unholy",     "Chaos",     "Storm",     "Illusion" };

    // ── Required Combat Keys (always loaded for events) ─────────────────
    // Resolved at runtime via PrefabHelper since base ability groups aren't in Prefabs.cs
    static PrefabGUID _keyLeftClick; // PrimaryAttack
    static PrefabGUID _keySpaceBar;  // Dash
    static PrefabGUID _keyQ;         // Core ranged (Shadowbolt)
    static PrefabGUID _keyE;         // Core projectile (FrostBat)
    static PrefabGUID _keyR;         // Core AoE (Chaos Volley)
    static PrefabGUID _keyC;         // Defensive counter (BloodRite)
    static PrefabGUID _keyT;         // Veil / movement (VeilOfBlood)
    static bool _combatKeysResolved;

    // Runtime names for base abilities not in Prefabs.cs
    const string PrimaryAttackName = "AB_Vampire_PrimaryAttack_AbilityGroup";
    const string DashName          = "AB_Vampire_VampireDash_AbilityGroup";
    const string VeilOfBloodName   = "AB_Vampire_VeilOfBlood_AbilityGroup";

    // ── Optional School Spell Sets (Q/E/R per school for event overrides) ──
    public struct SchoolSpellSet
    {
        public PrefabGUID Q;
        public PrefabGUID E;
        public PrefabGUID R;
    }

    static readonly Dictionary<string, SchoolSpellSet> _schoolSpellSets = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Blood"]    = new SchoolSpellSet { Q = Prefabs.AB_Blood_Shadowbolt,       E = Prefabs.AB_Blood_BloodRite,       R = Prefabs.AB_Blood_BloodStorm },
        ["Frost"]    = new SchoolSpellSet { Q = Prefabs.AB_Frost_FrostBat,         E = Prefabs.AB_Frost_CrystalLance,    R = Prefabs.AB_Frost_IceNova },
        ["Chaos"]    = new SchoolSpellSet { Q = Prefabs.AB_Chaos_Volley,           E = Prefabs.AB_Chaos_ChaosBarrage,    R = Prefabs.AB_Chaos_Void },
        ["Unholy"]   = new SchoolSpellSet { Q = Prefabs.AB_Unholy_CorruptedSkull,  E = Prefabs.AB_Unholy_Soulburn,       R = Prefabs.AB_Unholy_WardOfTheDamned },
        ["Storm"]    = new SchoolSpellSet { Q = Prefabs.AB_Storm_BallLightning,    E = Prefabs.AB_Storm_Discharge,       R = Prefabs.AB_Storm_EyeOfTheStorm },
        ["Illusion"] = new SchoolSpellSet { Q = Prefabs.AB_Illusion_SpectralWolf,  E = Prefabs.AB_Illusion_MistTrance,   R = Prefabs.AB_Illusion_PhantomAegis },
    };

    // Event → school mapping for LoadEventSpellSet
    static readonly Dictionary<string, string> _eventSchoolMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["bloodbath"]  = "Blood",
        ["colosseum"]  = "Chaos",
        ["siege"]      = "Unholy",
        ["trials"]     = "Frost",
    };

    public static IReadOnlyDictionary<string, SchoolSpellSet> SchoolSpellSets => _schoolSpellSets;

    /// <summary>
    /// Scan the game's PrefabCollectionSystem for valid ability group prefabs.
    /// Uses _PrefabGuidToEntityMap and checks for AbilityGroupState component.
    /// Call once after server is fully loaded (VRisingCore.IsReady must be true).
    /// </summary>
    public static void DiscoverAbilities()
    {
        if (_discovered) return;

        if (!VRisingCore.IsReady)
        {
            BattleLuckPlugin.LogWarning("[AbilityController] DiscoverAbilities called before VRisingCore ready — deferring.");
            return;
        }

        // Ensure live prefab names are indexed first
        PrefabHelper.ScanLivePrefabs();

        _abilitiesBySchool.Clear();
        _allAbilities.Clear();

        for (int i = 0; i < SchoolNames.Length; i++)
            _abilitiesBySchool[SchoolNames[i]] = new List<PrefabGUID>();

        try
        {
            var em = VRisingCore.EntityManager;
            var prefabCollection = VRisingCore.PrefabCollectionSystem;
            var guidToEntity = prefabCollection._PrefabGuidToEntityMap;

            // Use LIVE scan (not just Prefabs.cs) so runtime-only ability groups are found
            var allPrefabs = PrefabHelper.GetAllLive();
            foreach (var kvp in allPrefabs)
            {
                var name = kvp.Key;
                var guid = kvp.Value;

                // Match ability prefab names by school prefix
                for (int i = 0; i < SchoolPrefixes.Length; i++)
                {
                    if (name.StartsWith(SchoolPrefixes[i], StringComparison.OrdinalIgnoreCase))
                    {
                        // Validate the GUID actually exists in the game
                        if (guidToEntity.ContainsKey(guid))
                        {
                            _abilitiesBySchool[SchoolNames[i]].Add(guid);
                            _allAbilities.Add(guid);
                        }
                        break;
                    }
                }
            }

            // If Prefabs.cs yielded nothing valid, scan entity map for AbilityGroup entities
            if (_allAbilities.Count == 0)
            {
                BattleLuckPlugin.LogWarning("[AbilityController] No abilities found via Prefabs.cs — scanning entity map for AbilityGroup components...");

                foreach (var kvp in guidToEntity)
                {
                    var entity = kvp.Value;
                    try
                    {
                        if (em.HasComponent<AbilityGroupState>(entity))
                        {
                            _allAbilities.Add(kvp.Key);
                        }
                    }
                    catch { }
                }

                BattleLuckPlugin.LogInfo($"[AbilityController] Entity scan found {_allAbilities.Count} AbilityGroup entities (unschooled).");
            }
        }
        catch (Exception ex)
        {
            BattleLuckPlugin.LogWarning($"[AbilityController] Discovery failed: {ex.Message}");
        }

        _discovered = true;
        int total = _allAbilities.Count;
        BattleLuckPlugin.LogInfo($"[AbilityController] Discovered {total} ability prefabs across {_abilitiesBySchool.Count(s => s.Value.Count > 0)} schools.");
        foreach (var kv in _abilitiesBySchool.Where(k => k.Value.Count > 0))
            BattleLuckPlugin.LogInfo($"[AbilityController]   {kv.Key}: {kv.Value.Count} abilities");

        ResolveCombatKeys();
    }

    /// <summary>
    /// Resolve required combat key bindings from runtime prefab names.
    /// Falls back to known Prefabs.cs constants where available.
    /// </summary>
    static void ResolveCombatKeys()
    {
        if (_combatKeysResolved) return;

        // Base abilities — resolve from LIVE PrefabCollectionSystem (not in Prefabs.cs)
        _keyLeftClick = PrefabHelper.GetPrefabGuidDeep(PrimaryAttackName) ?? PrefabGUID.Empty;
        _keySpaceBar  = PrefabHelper.GetPrefabGuidDeep(DashName) ?? PrefabGUID.Empty;
        _keyT         = PrefabHelper.GetPrefabGuidDeep(VeilOfBloodName) ?? PrefabGUID.Empty;

        // Core spells — use known constants from Prefabs.cs
        _keyQ = Prefabs.AB_Blood_Shadowbolt;
        _keyE = Prefabs.AB_Frost_FrostBat;
        _keyR = Prefabs.AB_Chaos_Volley;
        _keyC = Prefabs.AB_Blood_BloodRite;

        // Fallback: if Veil not found by exact name, try partial match
        if (_keyT == PrefabGUID.Empty)
        {
            _keyT = PrefabHelper.GetLivePrefabGuid("VeilOfBlood") ?? PrefabGUID.Empty;
            if (_keyT != PrefabGUID.Empty)
                BattleLuckPlugin.LogInfo($"[AbilityController] Veil resolved via partial match: {_keyT.GuidHash}");
        }
        if (_keyLeftClick == PrefabGUID.Empty)
        {
            _keyLeftClick = PrefabHelper.GetLivePrefabGuid("PrimaryAttack") ?? PrefabGUID.Empty;
            if (_keyLeftClick != PrefabGUID.Empty)
                BattleLuckPlugin.LogInfo($"[AbilityController] PrimaryAttack resolved via partial match: {_keyLeftClick.GuidHash}");
        }
        if (_keySpaceBar == PrefabGUID.Empty)
        {
            _keySpaceBar = PrefabHelper.GetLivePrefabGuid("VampireDash") ?? PrefabGUID.Empty;
            if (_keySpaceBar != PrefabGUID.Empty)
                BattleLuckPlugin.LogInfo($"[AbilityController] Dash resolved via partial match: {_keySpaceBar.GuidHash}");
        }

        int resolved = 0;
        if (_keyLeftClick != PrefabGUID.Empty) resolved++;
        if (_keySpaceBar != PrefabGUID.Empty) resolved++;
        if (_keyT != PrefabGUID.Empty) resolved++;
        resolved += 4; // Q/E/R/C always resolve from constants

        _combatKeysResolved = true;
        BattleLuckPlugin.LogInfo($"[AbilityController] Combat keys resolved: {resolved}/7 " +
            $"(PrimaryAttack={(_keyLeftClick != PrefabGUID.Empty ? "OK" : "MISS")}, " +
            $"Dash={(_keySpaceBar != PrefabGUID.Empty ? "OK" : "MISS")}, " +
            $"Veil={(_keyT != PrefabGUID.Empty ? "OK" : "MISS")})");
    }

    /// <summary>
    /// Equip the required combat keys (LeftClick→KeyT) onto a player.
    /// Always applied before event participation.
    /// </summary>
    public static void EquipCombatKeys(Entity playerCharacter)
    {
        if (!_combatKeysResolved) ResolveCombatKeys();

        // Slot mapping: Travel=3, Spell1=5, Spell2=6, Ultimate=7
        if (_keyLeftClick != PrefabGUID.Empty) SetSpellOnSlot(playerCharacter, 1, _keyLeftClick);
        if (_keySpaceBar != PrefabGUID.Empty)  SetSpellOnSlot(playerCharacter, SlotVeil, _keySpaceBar);
        if (_keyQ != PrefabGUID.Empty)         SetSpellOnSlot(playerCharacter, SlotSpell1, _keyQ);
        if (_keyE != PrefabGUID.Empty)         SetSpellOnSlot(playerCharacter, SlotSpell2, _keyE);
        if (_keyR != PrefabGUID.Empty)         SetSpellOnSlot(playerCharacter, SlotUltimate, _keyR);
        if (_keyC != PrefabGUID.Empty)         SetSpellOnSlot(playerCharacter, SlotCounter, _keyC);
        if (_keyT != PrefabGUID.Empty)         SetSpellOnSlot(playerCharacter, SlotTravel, _keyT);

        BattleLuckPlugin.LogInfo($"[AbilityController] Equipped combat keys for {playerCharacter.GetSteamId()}.");
    }

    /// <summary>
    /// Load the school-specific spell set for an event, overriding Q/E/R slots.
    /// Keeps base combat keys (LeftClick, Space, C, T) intact.
    /// </summary>
    public static void LoadEventSpellSet(Entity playerCharacter, string eventName)
    {
        if (!_combatKeysResolved) ResolveCombatKeys();

        // Resolve event → school
        if (!_eventSchoolMap.TryGetValue(eventName, out var school))
        {
            BattleLuckPlugin.LogWarning($"[AbilityController] No school mapped for event: {eventName}");
            return;
        }

        LoadSchoolSpellSet(playerCharacter, school);
    }

    /// <summary>
    /// Load a specific school's Q/E/R spell set onto a player.
    /// </summary>
    public static void LoadSchoolSpellSet(Entity playerCharacter, string school)
    {
        if (!_schoolSpellSets.TryGetValue(school, out var spells))
        {
            BattleLuckPlugin.LogWarning($"[AbilityController] Unknown school: {school}");
            return;
        }

        SetSpellOnSlot(playerCharacter, SlotSpell1, spells.Q);
        SetSpellOnSlot(playerCharacter, SlotSpell2, spells.E);
        SetSpellOnSlot(playerCharacter, SlotUltimate, spells.R);

        BattleLuckPlugin.LogInfo($"[AbilityController] Loaded {school} spell set for {playerCharacter.GetSteamId()} (Q/E/R overridden).");
    }

    /// <summary>
    /// Grant all discovered abilities to a player (bulk unlock).
    /// </summary>
    public static void UnlockAllAbilities(Entity playerCharacter)
    {
        if (!_discovered) DiscoverAbilities();

        int succeeded = 0;
        int failed = 0;

        foreach (var ability in _allAbilities)
        {
            try
            {
                playerCharacter.TryApplyBuff(ability);
                succeeded++;
            }
            catch
            {
                failed++;
            }
        }

        BattleLuckPlugin.LogInfo($"[AbilityController] Unlocked {succeeded} abilities for {playerCharacter.GetSteamId()} ({failed} failed).");
    }

    /// <summary>Unlock abilities for a specific spell school only.</summary>
    public static void UnlockSchool(Entity playerCharacter, string school)
    {
        if (!_discovered) DiscoverAbilities();

        if (!_abilitiesBySchool.TryGetValue(school, out var abilities) || abilities.Count == 0)
        {
            BattleLuckPlugin.LogWarning($"[AbilityController] Unknown or empty spell school: {school}");
            return;
        }

        int succeeded = 0;
        foreach (var ability in abilities)
        {
            try { playerCharacter.TryApplyBuff(ability); succeeded++; }
            catch { }
        }

        BattleLuckPlugin.LogInfo($"[AbilityController] Unlocked {succeeded} {school} abilities for {playerCharacter.GetSteamId()}.");
    }

    /// <summary>Reset all ability cooldowns for a player.</summary>
    public static void ResetCooldowns(Entity playerCharacter)
    {
        if (!_discovered) DiscoverAbilities();

        var sgm = VRisingCore.ServerGameManager;
        foreach (var ability in _allAbilities)
        {
            try { sgm.SetAbilityGroupCooldown(playerCharacter, ability, 0f); }
            catch { }
        }

        BattleLuckPlugin.LogInfo($"[AbilityController] Reset cooldowns for {playerCharacter.GetSteamId()}.");
    }

    /// <summary>
    /// Set a specific spell on a specific slot by applying a VBloodAbilityReplace buff
    /// and configuring its ReplaceAbilityOnSlotBuff buffer.
    /// In V Rising 1.1, ReplaceAbilityOnSlotBuff lives on BUFF ENTITIES, not on the character.
    /// </summary>
    public static void SetSpellOnSlot(
        Entity playerCharacter,
        int slot,
        PrefabGUID abilityGroup,
        bool copyCooldown = true,
        int priority = 0)
    {
        var em = VRisingCore.EntityManager;
        var userEntity = playerCharacter.GetUserEntity();

        if (!userEntity.Exists())
        {
            BattleLuckPlugin.LogWarning($"[AbilityController] No user entity for slot {slot} = {abilityGroup.GuidHash}.");
            return;
        }

        // Apply VBloodAbilityReplace buff — this creates a buff entity with ReplaceAbilityOnSlotBuff
        var des = VRisingCore.DebugEventsSystem;
        var buffEvent = new ApplyBuffDebugEvent { BuffPrefabGUID = Prefabs.VBloodAbilityReplace };
        var fromCharacter = new FromCharacter { User = userEntity, Character = playerCharacter };
        des.ApplyBuff(fromCharacter, buffEvent);

        // Find the newly created buff entity and configure it
        if (VRisingCore.ServerGameManager.TryGetBuffer<BuffBuffer>(playerCharacter, out var buffs))
        {
            for (int i = buffs.Length - 1; i >= 0; i--)
            {
                if (buffs[i].PrefabGuid.GuidHash == Prefabs.VBloodAbilityReplace.GuidHash)
                {
                    var buffEntity = buffs[i].Entity;
                    if (!buffEntity.Exists()) continue;

                    if (em.HasBuffer<ReplaceAbilityOnSlotBuff>(buffEntity))
                    {
                        var replaceBuffer = em.GetBuffer<ReplaceAbilityOnSlotBuff>(buffEntity);
                        replaceBuffer.Clear();
                        replaceBuffer.Add(new ReplaceAbilityOnSlotBuff
                        {
                            Slot = slot,
                            NewGroupId = abilityGroup,
                            CopyCooldown = copyCooldown,
                            Priority = priority
                        });

                        // Make buff persistent
                        if (em.HasComponent<CreateGameplayEventsOnSpawn>(buffEntity))
                            em.RemoveComponent<CreateGameplayEventsOnSpawn>(buffEntity);
                        if (em.HasComponent<GameplayEventListeners>(buffEntity))
                            em.RemoveComponent<GameplayEventListeners>(buffEntity);

                        BattleLuckPlugin.LogInfo($"[AbilityController] Set slot {slot} = {abilityGroup.GuidHash} via VBloodAbilityReplace buff entity.");
                        return;
                    }
                    else
                    {
                        BattleLuckPlugin.LogWarning($"[AbilityController] VBloodAbilityReplace buff entity has no ReplaceAbilityOnSlotBuff buffer.");
                    }
                }
            }
        }

        BattleLuckPlugin.LogWarning($"[AbilityController] Failed to find VBloodAbilityReplace buff entity for slot {slot}.");
    }

    /// <summary>
    /// Remove every BattleLuck-style VBlood ability replacement buff from a
    /// player. The native/base loadout becomes active again immediately; saved
    /// replacements can then be restored without overlapping event slots.
    /// </summary>
    public static int ClearAbilitySlots(Entity playerCharacter)
    {
        var em = VRisingCore.EntityManager;
        if (!playerCharacter.Exists() || !em.HasBuffer<BuffBuffer>(playerCharacter))
            return 0;

        var buffs = em.GetBuffer<BuffBuffer>(playerCharacter);
        var toDestroy = new List<Entity>();
        for (var i = 0; i < buffs.Length; i++)
        {
            var entry = buffs[i];
            if (entry.PrefabGuid.GuidHash != Prefabs.VBloodAbilityReplace.GuidHash)
                continue;
            if (entry.Entity.Exists())
                toDestroy.Add(entry.Entity);
        }

        var removed = 0;
        foreach (var buffEntity in toDestroy.Distinct())
        {
            try
            {
                buffEntity.DestroyWithReason();
                removed++;
            }
            catch (Exception ex)
            {
                BattleLuckPlugin.LogWarning($"[AbilityController] Failed to clear ability replacement buff {buffEntity.Index}:{buffEntity.Version}: {ex.Message}");
            }
        }

        if (removed > 0)
            BattleLuckPlugin.LogInfo($"[AbilityController] Cleared {removed} custom ability slot replacement(s) for {playerCharacter.GetSteamId()}.");
        return removed;
    }

    /// <summary>Remove passive ability buffs so an event kit cannot stack with the player's old passives.</summary>
    public static int ClearPassiveSpells(Entity playerCharacter)
    {
        var em = VRisingCore.EntityManager;
        if (!playerCharacter.Exists() || !em.HasBuffer<BuffBuffer>(playerCharacter))
            return 0;

        var buffs = em.GetBuffer<BuffBuffer>(playerCharacter);
        var prefabs = new HashSet<PrefabGUID>();
        for (var i = 0; i < buffs.Length; i++)
        {
            var guid = buffs[i].PrefabGuid;
            var name = PrefabHelper.GetLivePrefabName(guid) ?? PrefabHelper.GetName(guid) ?? string.Empty;
            if (name.Contains("Passive", StringComparison.OrdinalIgnoreCase))
                prefabs.Add(guid);
        }

        foreach (var prefab in prefabs)
            playerCharacter.TryRemoveBuff(prefab);

        if (prefabs.Count > 0)
            BattleLuckPlugin.LogInfo($"[AbilityController] Cleared {prefabs.Count} passive spell buff(s) for {playerCharacter.GetSteamId()}.");
        return prefabs.Count;
    }

    // ── Kit-driven ability assignment ────────────────────────────────────

    // Slot mapping: LeftClick=1, Space/Veil=2, Travel/T=3, Counter/C=4, Spell1/Q=5, Spell2/E=6, Ultimate/R=7
    const int SlotVeil     = 2;
    const int SlotTravel   = 3;
    const int SlotCounter  = 4;
    const int SlotSpell1   = 5;
    const int SlotSpell2   = 6;
    const int SlotUltimate = 7;

    /// <summary>
    /// Equip specific abilities from kit.json config into correct slots.
    /// Uses ReplaceAbilityOnSlotBuff via SetSpellOnSlot.
    /// </summary>
    public static void EquipAbilities(Entity playerCharacter, AbilitiesKitConfig config)
    {
        // Assign configured abilities to specific slots
        AssignSlot(playerCharacter, config.Travel, SlotTravel, "Travel");       // T key
        AssignSlot(playerCharacter, config.Spell1, SlotSpell1, "Spell1");       // Q key
        AssignSlot(playerCharacter, config.Spell2, SlotSpell2, "Spell2");       // E key
        AssignSlot(playerCharacter, config.Ultimate, SlotUltimate, "Ultimate"); // R key
        AssignSlot(playerCharacter, config.Counter, SlotCounter, "Counter");    // C key
        AssignSlot(playerCharacter, config.Veil, SlotVeil, "Veil");             // Space key

        BattleLuckPlugin.LogInfo($"[AbilityController] Equipped kit abilities for {playerCharacter.GetSteamId()}.");
    }

    /// <summary>Equip passive spells from kit config.</summary>
    public static void EquipPassiveSpells(Entity playerCharacter, List<PassiveSpellConfig> passives)
    {
        foreach (var passive in passives)
        {
            var guid = PrefabHelper.GetPrefabGuidDeep(passive.Prefab);
            if (guid.HasValue)
                playerCharacter.TryApplyBuff(guid.Value);
            else
                BattleLuckPlugin.LogWarning($"[AbilityController] Unknown passive prefab: {passive.Prefab}");
        }
    }

    static void AssignSlot(Entity playerCharacter, AbilityKitSlot? slot, int slotIndex, string slotName)
    {
        if (slot == null || string.IsNullOrEmpty(slot.Prefab)) return;

        var guid = PrefabHelper.GetPrefabGuidDeep(slot.Prefab);
        if (!guid.HasValue)
        {
            BattleLuckPlugin.LogWarning($"[AbilityController] Unknown {slotName} ability prefab: {slot.Prefab}");
            return;
        }

        SetSpellOnSlot(playerCharacter, slotIndex, guid.Value);
    }
}
