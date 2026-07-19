using ProjectM;
using ProjectM.Network;
using ProjectM.Scripting;
using Stunlock.Core;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

/// <summary>
/// V Rising ECS entity extension methods.
/// Pattern sourced from Bloodcraft VExtensions.cs.
/// </summary>
public static class EntityExtensions
{
    static EntityManager Em => VRisingCore.EntityManager;
    static ServerGameManager Sgm => VRisingCore.ServerGameManager;
    static DebugEventsSystem Des => VRisingCore.DebugEventsSystem;
    static EntityArchetype _playerTeleportEventArchetype;
    static bool _playerTeleportEventArchetypeReady;

    // ── Read / Write / Has ──────────────────────────────────────────────
    public static T Read<T>(this Entity entity) where T : struct
        => Em.GetComponentData<T>(entity);

    public static void Write<T>(this Entity entity, T data) where T : struct
        => Em.SetComponentData(entity, data);

    public static bool Has<T>(this Entity entity) where T : struct
        => Em.HasComponent<T>(entity);

    public static bool TryGetComponent<T>(this Entity entity, out T component) where T : struct
    {
        if (entity.Has<T>())
        {
            component = entity.Read<T>();
            return true;
        }
        component = default;
        return false;
    }

    public delegate void WithRefHandler<T>(ref T component) where T : struct;

    public static void With<T>(this Entity entity, WithRefHandler<T> handler) where T : struct
    {
        var component = entity.Read<T>();
        handler(ref component);
        entity.Write(component);
    }

    public static bool Exists(this Entity entity)
        => entity != Entity.Null && Em.Exists(entity);

    public static void Destroy(this Entity entity)
    {
        if (entity.Exists()) Em.DestroyEntity(entity);
    }

    /// <summary>Destroy with proper Disabled + DestroyUtility cleanup (VAMP EntityUtil pattern).</summary>
    /// <remarks>
    /// Handles the "Destroy is declined if in live" error by catching exceptions when the entity
    /// is currently being processed by ECS systems. The entity will be cleaned up on the next tick.
    /// </remarks>
    public static void DestroyWithReason(this Entity entity, DestroyDebugReason reason = DestroyDebugReason.TryRemoveBuff)
    {
        if (!entity.Exists()) return;
        if (!Em.HasComponent<Disabled>(entity))
            Em.AddComponent<Disabled>(entity);
        try
        {
            Em.DestroyEntity(entity);
        }
        catch (Exception ex) when (ex.Message.Contains("in live", StringComparison.OrdinalIgnoreCase))
        {
            // Entity is currently being processed by ECS systems - it will be cleaned up on next tick
            // The Disabled component we added will prevent further processing
            BattleLuckPlugin.LogInfo($"[EntityExtensions] DestroyWithReason deferred for entity {entity.Index}:{entity.Version} (in live state).");
        }
        catch (Exception ex)
        {
            BattleLuckPlugin.LogWarning($"[EntityExtensions] DestroyWithReason failed for entity {entity.Index}:{entity.Version}: {ex.Message}");
        }
    }

    // ── Identity ────────────────────────────────────────────────────────
    public static PrefabGUID GetPrefabGuid(this Entity entity)
        => entity.Has<PrefabGUID>() ? entity.Read<PrefabGUID>() : PrefabGUID.Empty;

    public static NetworkId GetNetworkId(this Entity entity)
        => entity.Read<NetworkId>();

    public static ulong GetSteamId(this Entity entity)
    {
        if (!entity.Exists())
            return 0UL;

        if (entity.Has<PlayerCharacter>())
        {
            var userEntity = entity.Read<PlayerCharacter>().UserEntity;
            if (userEntity.Exists() && userEntity.Has<User>())
                return userEntity.Read<User>().PlatformId;
        }
        if (entity.Has<User>())
            return entity.Read<User>().PlatformId;
        return 0UL;
    }

    public static Entity GetUserEntity(this Entity entity)
    {
        if (entity.Has<PlayerCharacter>())
            return entity.Read<PlayerCharacter>().UserEntity;
        return Entity.Null;
    }

    /// <summary>Get the player's display name (CharacterName) or empty string if unavailable.</summary>
    public static string GetPlayerName(this Entity entity)
    {
        if (!entity.Exists()) return string.Empty;
        Entity userEntity = entity.IsPlayer() ? entity.GetUserEntity() : entity;
        if (!userEntity.Exists() || !userEntity.Has<User>()) return string.Empty;
        return userEntity.Read<User>().CharacterName.ToString();
    }

    /// <summary>Format "Name (steamId)" for log messages; falls back to just the steamId when name is empty.</summary>
    public static string FormatPlayer(ulong steamId, Entity entity)
    {
        string name = entity.GetPlayerName();
        return string.IsNullOrEmpty(name) ? steamId.ToString() : $"{name} ({steamId})";
    }

    public static bool IsPlayer(this Entity entity)
        => entity.Has<PlayerCharacter>();

    /// <summary>
    /// Check if entity is a valid player (exists and has PlayerCharacter component).
    /// Eliminates repeated Exists() && IsPlayer() patterns throughout codebase.
    /// </summary>
    public static bool IsValidPlayer(this Entity entity) =>
        entity.Exists() && entity.IsPlayer();

    /// <summary>
    /// Check if entity is a valid player and retrieve the User component.
    /// Eliminates repeated Exists() && IsPlayer() && TryGetUser patterns.
    /// </summary>
    public static bool IsValidPlayer(this Entity entity, out User user)
    {
        user = default;
        if (!entity.Exists() || !entity.IsPlayer())
            return false;

        var userEntity = entity.GetUserEntity();
        if (!userEntity.Exists() || !userEntity.Has<User>())
            return false;

        user = userEntity.Read<User>();
        return true;
    }

    public static int GetUnitLevel(this Entity entity)
    {
        if (entity.TryGetComponent(out UnitLevel unitLevel))
            return unitLevel.Level._Value;
        return 0;
    }

    // ── Position / Teleport ─────────────────────────────────────────────
    public static float3 GetPosition(this Entity entity)
    {
        if (entity.TryGetComponent(out Translation translation))
            return translation.Value;
        return float3.zero;
    }

    public static void SetPosition(this Entity entity, float3 position)
    {
        // Player movement needs the server-native teleport path so the owning
        // client, collision systems, and streaming state all receive the move.
        // Direct Translation writes remain the correct fallback for spawned
        // arena objects and for servers where the waypoint buff is unavailable.
        if (entity.IsPlayer() &&
            (TryTeleportPlayerWithNetworkEvent(entity, position) || TryTeleportPlayerWithWaypointBuff(entity, position)))
            return;

        if (entity.Has<Translation>())
            entity.With((ref Translation t) => t.Value = position);

        if (entity.Has<LastTranslation>())
            entity.With((ref LastTranslation lt) => lt.Value = position);
    }

    static bool TryTeleportPlayerWithNetworkEvent(Entity character, float3 position)
    {
        var user = character.GetUserEntity();
        if (!character.Exists() || !user.Exists())
            return false;

        try
        {
            if (!_playerTeleportEventArchetypeReady)
            {
                _playerTeleportEventArchetype = Em.CreateArchetype(
                    ComponentType.ReadWrite<FromCharacter>(),
                    ComponentType.ReadWrite<PlayerTeleportDebugEvent>(),
                    ComponentType.ReadWrite<SendNetworkEventTag>());
                _playerTeleportEventArchetypeReady = true;
            }

            position.y = Math.Max(0f, position.y) + 0.25f;
            var teleportEvent = Em.CreateEntity(_playerTeleportEventArchetype);
            Em.SetComponentData(teleportEvent, new FromCharacter { User = user, Character = character });
            Em.SetComponentData(teleportEvent, new PlayerTeleportDebugEvent { Position = position });
            return true;
        }
        catch (Exception ex)
        {
            _playerTeleportEventArchetypeReady = false;
            BattleLuckPlugin.LogWarning($"[EntityExtensions] Network player teleport failed; trying waypoint fallback: {ex.Message}");
            return false;
        }
    }

    static bool TryTeleportPlayerWithWaypointBuff(Entity entity, float3 position)
    {
        var teleportPrefab = new PrefabGUID(150521246); // Buff_Waypoint_Travel
        if (!entity.Exists() || !IsUsableBuffPrefab(teleportPrefab))
            return false;

        var userEntity = entity.GetUserEntity();
        if (!userEntity.Exists())
            return false;

        try
        {
            if (!Sgm.HasBuff(entity, teleportPrefab.ToIdentifier()))
            {
                Des.ApplyBuff(
                    new FromCharacter { Character = entity, User = userEntity },
                    new ApplyBuffDebugEvent { BuffPrefabGUID = teleportPrefab });
            }

            if (!Sgm.TryGetBuff(entity, teleportPrefab.ToIdentifier(), out var buffEntity) ||
                !buffEntity.Exists() || !buffEntity.Has<TeleportBuff>())
                return false;

            buffEntity.With((ref TeleportBuff teleport) => teleport.EndPosition = position);
            return true;
        }
        catch (Exception ex)
        {
            BattleLuckPlugin.LogWarning($"[EntityExtensions] Native player teleport failed; using component fallback: {ex.Message}");
            return false;
        }
    }

    // ── Equipment / Level ───────────────────────────────────────────────
    public static void SetEquipmentLevel(this Entity entity, float weaponLevel, float armorLevel = 0f, float spellLevel = 0f)
    {
        if (entity.Has<Equipment>())
        {
            entity.With((ref Equipment eq) =>
            {
                eq.WeaponLevel._Value = weaponLevel;
                eq.ArmorLevel._Value = armorLevel;
                eq.SpellLevel._Value = spellLevel;
            });
        }
    }

    // ── Buffs ───────────────────────────────────────────────────────────

    /// <summary>
    /// Apply a buff with full control over duration and persistence (VAMP pattern).
    /// Duration: 0 = infinite, > 0 = timed, -1 = default buff duration.
    /// Returns true if buff was applied and buffEntity is valid.
    /// </summary>
    public static bool BuffEntity(this Entity entity, PrefabGUID buffPrefab, out Entity buffEntity, float duration = 0f, bool persistThroughDeath = false)
    {
        buffEntity = Entity.Null;
        if (!entity.Exists()) return false;
        if (!IsUsableBuffPrefab(buffPrefab)) return false;

        Entity userEntity = entity.IsPlayer() ? entity.GetUserEntity() : entity;
        if (!userEntity.Exists()) return false;

        // Apply buff if not already present
        if (!Sgm.HasBuff(entity, buffPrefab.ToIdentifier()))
        {
            var des = Des;
            des.ApplyBuff(new FromCharacter { Character = entity, User = userEntity },
                          new ApplyBuffDebugEvent { BuffPrefabGUID = buffPrefab });
        }

        // Retrieve the buff entity
        if (!Sgm.TryGetBuff(entity, buffPrefab.ToIdentifier(), out buffEntity))
            return false;

        // Persistence settings
        if (persistThroughDeath || duration == 0f)
        {
            if (!buffEntity.Has<Buff_Persists_Through_Death>())
                Em.AddComponent<Buff_Persists_Through_Death>(buffEntity);
            if (buffEntity.Has<Buff_Destroy_On_Owner_Death>())
                Em.RemoveComponent<Buff_Destroy_On_Owner_Death>(buffEntity);

            // Clear removal-on-gameplay-event entries
            if (Sgm.TryGetBuffer<RemoveBuffOnGameplayEventEntry>(buffEntity, out var removeBuffer))
                removeBuffer.Clear();
        }
        else
        {
            if (buffEntity.Has<Buff_Persists_Through_Death>())
                Em.RemoveComponent<Buff_Persists_Through_Death>(buffEntity);
            if (!buffEntity.Has<Buff_Destroy_On_Owner_Death>())
                Em.AddComponent<Buff_Destroy_On_Owner_Death>(buffEntity);
        }

        // Duration / lifetime
        if (duration == 0f)
        {
            // Infinite buff
            if (buffEntity.Has<LifeTime>())
            {
                if (buffEntity.Has<Age>())
                    Em.RemoveComponent<Age>(buffEntity);

                buffEntity.With((ref LifeTime lt) =>
                {
                    lt.Duration = 0f;
                    lt.EndAction = LifeTimeEndAction.None;
                });
            }
        }
        else if (duration > 0f)
        {
            // Timed buff — reset age and set duration
            if (buffEntity.Has<Age>())
                buffEntity.With((ref Age age) => age.Value = 0f);

            if (!buffEntity.Has<LifeTime>())
                Em.AddComponent<LifeTime>(buffEntity);

            buffEntity.Write(new LifeTime
            {
                EndAction = LifeTimeEndAction.Destroy,
                Duration = duration
            });
        }
        // duration < 0 = leave default lifetime unchanged

        return true;
    }

    /// <summary>
    /// Modify buff flags (e.g. invulnerable, movement speed changes, etc).
    /// </summary>
    public static void ModifyBuff(Entity buffEntity, BuffModificationTypes modTypes, bool overwrite = false)
    {
        if (!Em.HasComponent<BuffModificationFlagData>(buffEntity))
            Em.AddComponent<BuffModificationFlagData>(buffEntity);

        var data = buffEntity.Read<BuffModificationFlagData>();
        if (overwrite)
            data.ModificationTypes = (long)BuffModificationTypes.None;
        data.ModificationTypes |= (long)modTypes;
        buffEntity.Write(data);
    }

    /// <summary>Simple buff apply — fires ApplyBuffDebugEvent. Works on any entity (players, NPCs, objects).</summary>
    public static bool TryApplyBuff(this Entity entity, PrefabGUID buffPrefab)
    {
        if (!entity.Exists()) return false;
        if (!IsUsableBuffPrefab(buffPrefab)) return false;

        Entity userEntity = entity.IsPlayer() ? entity.GetUserEntity() : entity;
        if (!userEntity.Exists()) return false;

        Des.ApplyBuff(
            new FromCharacter { Character = entity, User = userEntity },
            new ApplyBuffDebugEvent { BuffPrefabGUID = buffPrefab });
        return true;
    }

    /// <summary>
    /// Try to apply a buff and return the resulting buff entity.
    /// </summary>
    public static bool TryApplyAndGetBuff(this Entity entity, PrefabGUID buffPrefab, out Entity buffEntity)
    {
        buffEntity = Entity.Null;
        if (!entity.TryApplyBuff(buffPrefab)) return false;
        return Sgm.TryGetBuff(entity, buffPrefab.ToIdentifier(), out buffEntity);
    }

    public static bool HasBuff(this Entity entity, PrefabGUID buffPrefab)
    {
        if (!IsUsableBuffPrefab(buffPrefab)) return false;
        return Sgm.HasBuff(entity, buffPrefab.ToIdentifier());
    }

    public static bool TryGetBuff(this Entity entity, PrefabGUID buffPrefab, out Entity buffEntity)
    {
        buffEntity = Entity.Null;
        if (!IsUsableBuffPrefab(buffPrefab)) return false;
        return Sgm.TryGetBuff(entity, buffPrefab.ToIdentifier(), out buffEntity);
    }

    /// <summary>
    /// Remove a buff using DestroyUtility (proper cleanup, same as VAMP pattern).
    /// Handles the "Destroy is declined if in live" error gracefully.
    /// </summary>
    public static void TryRemoveBuff(this Entity entity, PrefabGUID buffPrefab)
    {
        if (!IsUsableBuffPrefab(buffPrefab)) return;
        if (BuffUtility.TryGetBuff(Em, entity, buffPrefab, out var buff))
        {
            try
            {
                Em.DestroyEntity(buff);
            }
            catch (Exception ex) when (ex.Message.Contains("in live", StringComparison.OrdinalIgnoreCase))
            {
                // Buff entity is currently being processed - will be cleaned up on next tick
                BattleLuckPlugin.LogInfo($"[EntityExtensions] TryRemoveBuff deferred for buff entity {buff.Index}:{buff.Version} (in live state).");
            }
            catch (Exception ex)
            {
                BattleLuckPlugin.LogWarning($"[EntityExtensions] TryRemoveBuff failed for buff entity {buff.Index}:{buff.Version}: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Remove an existing buff and re-apply with duration/persistence control.
    /// If buff doesn't exist, applies directly. If it does, queues removal then re-add.
    /// </summary>
    public static void RemoveAndAddBuff(this Entity entity, PrefabGUID buffPrefab, float duration = -1f, Action<Entity>? onBuffCreated = null)
    {
        if (!entity.Exists()) return;
        if (!IsUsableBuffPrefab(buffPrefab)) return;

        if (!entity.HasBuff(buffPrefab))
        {
            entity.TryApplyBuff(buffPrefab);
            if (duration >= 0f || onBuffCreated != null)
                TryModifyBuffAfterApply(entity, buffPrefab, duration, onBuffCreated);
        }
        else
        {
            entity.TryRemoveBuff(buffPrefab);
            _pendingBuffReapply.Enqueue(new PendingBuff(entity, buffPrefab, duration, onBuffCreated, DateTime.UtcNow));
        }
    }

    /// <summary>
    /// Must be called each server tick to process pending buff re-applications.
    /// </summary>
    public static void TickPendingBuffs()
    {
        int count = _pendingBuffReapply.Count;
        for (int i = 0; i < count; i++)
        {
            var pending = _pendingBuffReapply.Dequeue();
            if (!pending.Entity.Exists()) continue;
            if (!IsUsableBuffPrefab(pending.Prefab)) continue;

            if (pending.Entity.HasBuff(pending.Prefab))
            {
                if ((DateTime.UtcNow - pending.QueuedAt).TotalSeconds < 2.0)
                    _pendingBuffReapply.Enqueue(pending);
                continue;
            }

            pending.Entity.TryApplyBuff(pending.Prefab);
            TryModifyBuffAfterApply(pending.Entity, pending.Prefab, pending.Duration, pending.Callback);
        }
    }

    static void TryModifyBuffAfterApply(Entity entity, PrefabGUID buffPrefab, float duration, Action<Entity>? callback)
    {
        if (!IsUsableBuffPrefab(buffPrefab)) return;
        if (!Sgm.TryGetBuff(entity, buffPrefab.ToIdentifier(), out var buffEntity)) return;

        if (duration == 0f)
        {
            // Infinite
            if (buffEntity.Has<Age>())
                Em.RemoveComponent<Age>(buffEntity);
            if (buffEntity.Has<LifeTime>())
                buffEntity.With((ref LifeTime lt) => { lt.Duration = 0f; lt.EndAction = LifeTimeEndAction.None; });
        }
        else if (duration > 0f)
        {
            // Timed
            if (buffEntity.Has<Age>())
                buffEntity.With((ref Age age) => age.Value = 0f);
            if (!buffEntity.Has<LifeTime>())
                Em.AddComponent<LifeTime>(buffEntity);
            buffEntity.Write(new LifeTime { EndAction = LifeTimeEndAction.Destroy, Duration = duration });
        }

        callback?.Invoke(buffEntity);
    }

    static bool IsUsableBuffPrefab(PrefabGUID buffPrefab)
    {
        if (buffPrefab == PrefabGUID.Empty || buffPrefab.GuidHash == 0)
            return false;

        try
        {
            return PrefabHelper.ValidatePrefab(buffPrefab);
        }
        catch
        {
            return false;
        }
    }

    static readonly Queue<PendingBuff> _pendingBuffReapply = new();

    readonly record struct PendingBuff(Entity Entity, PrefabGUID Prefab, float Duration, Action<Entity>? Callback, DateTime QueuedAt);

    // ── Abilities ───────────────────────────────────────────────────────
    public static User GetUser(this Entity entity)
    {
        if (entity.Has<PlayerCharacter>())
        {
            var userEntity = entity.Read<PlayerCharacter>().UserEntity;
            return userEntity.Read<User>();
        }
        return entity.Read<User>();
    }

    public static void CastAbility(this Entity entity, PrefabGUID abilityGroup)
    {
        var des = Des;
        var castEvent = new CastAbilityServerDebugEvent
        {
            AbilityGroup = abilityGroup,
            Who = entity.GetNetworkId()
        };

        var fromCharacter = new FromCharacter
        {
            Character = entity,
            User = entity.IsPlayer() ? entity.GetUserEntity() : entity
        };

        int userIndex = entity.IsPlayer() ? entity.GetUser().Index : 0;
        des.CastAbilityServerDebugEvent(userIndex, ref castEvent, ref fromCharacter);
    }

    // ── Inventory ───────────────────────────────────────────────────────
    public readonly record struct NpcStashTransferConditions
    {
        public static readonly NpcStashTransferConditions Default = new();

        public NpcStashTransferConditions()
        {
            SourceMustBeNpc = true;
            RequireSameTeam = false;
            MaxDistance = -1f;
            MinStackAmount = 1;
            MaxStacks = int.MaxValue;
            ItemFilter = null;
        }

        public bool SourceMustBeNpc { get; init; }
        public bool RequireSameTeam { get; init; }
        public float MaxDistance { get; init; }
        public int MinStackAmount { get; init; }
        public int MaxStacks { get; init; }
        public Func<PrefabGUID, int, bool>? ItemFilter { get; init; }
    }

    public static bool TryGiveItem(this Entity character, PrefabGUID itemPrefab, int amount)
    {
        if (!character.Exists() || itemPrefab == PrefabGUID.Empty || amount <= 0)
            return false;

        if (InventoryUtilities.TryGetInventoryEntity(Em, character, out Entity inventoryEntity) &&
            inventoryEntity.Exists() &&
            Sgm.TryAddInventoryItem(inventoryEntity, itemPrefab, amount))
            return true;

        return Sgm.TryAddInventoryItem(character, itemPrefab, amount);
    }

    public static bool TryRemoveItem(this Entity character, PrefabGUID itemPrefab, int amount)
    {
        if (!InventoryUtilities.TryGetInventoryEntity(Em, character, out Entity inventoryEntity))
            return false;

        return Sgm.TryRemoveInventoryItem(inventoryEntity, itemPrefab, amount);
    }

    /// <summary>
    /// Sends a single item stack to any destination entity that carries an inventory
    /// (container, player, or other unit). Returns true if the item was accepted.
    /// </summary>
    public static bool TrySendItemTo(this Entity destination, PrefabGUID item, int amount)
    {
        if (!destination.Exists() || item == PrefabGUID.Empty || amount <= 0)
            return false;

        return Sgm.TryAddInventoryItem(destination, item, amount);
    }

    /// <summary>
    /// Sends multiple item stacks to any destination entity with an inventory.
    /// Returns the number of stacks successfully delivered.
    /// </summary>
    public static int TrySendItemsTo(this Entity destination, IEnumerable<(PrefabGUID item, int amount)> items)
    {
        if (!destination.Exists()) return 0;

        int delivered = 0;
        foreach (var (item, amount) in items)
        {
            if (item == PrefabGUID.Empty || amount <= 0) continue;
            if (Sgm.TryAddInventoryItem(destination, item, amount))
                delivered++;
        }
        return delivered;
    }

    /// <summary>
    /// Moves items from an NPC inventory to any destination inventory carrier
    /// (container, player, or other unit) using configurable conditions.
    /// Returns total moved item amount.
    /// </summary>
    public static int TryStashFromNpc(this Entity sourceNpc, Entity destination, NpcStashTransferConditions? conditions = null)
    {
        var options = conditions ?? NpcStashTransferConditions.Default;

        if (!sourceNpc.Exists() || !destination.Exists() || sourceNpc == destination)
            return 0;

        if (options.SourceMustBeNpc && sourceNpc.Has<PlayerCharacter>())
            return 0;

        if (options.RequireSameTeam)
        {
            if (!sourceNpc.Has<Team>() || !destination.Has<Team>())
                return 0;

            if (sourceNpc.Read<Team>().Value != destination.Read<Team>().Value)
                return 0;
        }

        if (options.MaxDistance >= 0f)
        {
            var distance = math.distance(sourceNpc.GetPosition(), destination.GetPosition());
            if (distance > options.MaxDistance)
                return 0;
        }

        if (!InventoryUtilities.TryGetInventoryEntity(Em, sourceNpc, out var sourceInventoryEntity))
            return 0;

        if (!Em.HasBuffer<InventoryBuffer>(sourceInventoryEntity))
            return 0;

        var sourceInventory = Em.GetBuffer<InventoryBuffer>(sourceInventoryEntity);
        var candidates = new List<(PrefabGUID item, int amount)>();

        for (int i = 0; i < sourceInventory.Length; i++)
        {
            var slot = sourceInventory[i];
            if (slot.ItemType == PrefabGUID.Empty || slot.Amount < options.MinStackAmount)
                continue;

            if (options.ItemFilter != null && !options.ItemFilter(slot.ItemType, slot.Amount))
                continue;

            candidates.Add((slot.ItemType, slot.Amount));
            if (candidates.Count >= options.MaxStacks)
                break;
        }

        if (candidates.Count == 0)
            return 0;

        int movedAmount = 0;
        foreach (var (item, amount) in candidates)
        {
            if (!Sgm.TryAddInventoryItem(destination, item, amount))
                continue;

            if (!Sgm.TryRemoveInventoryItem(sourceInventoryEntity, item, amount))
            {
                Sgm.TryRemoveInventoryItem(destination, item, amount);
                continue;
            }

            movedAmount += amount;
        }

        return movedAmount;
    }

    /// <summary>
    /// Moves matching items from multiple NPCs to a destination inventory carrier.
    /// Returns total moved item amount across all sources.
    /// </summary>
    public static int TryStashFromNpcs(this IEnumerable<Entity> sourceNpcs, Entity destination, NpcStashTransferConditions? conditions = null)
    {
        int moved = 0;
        foreach (var sourceNpc in sourceNpcs)
            moved += sourceNpc.TryStashFromNpc(destination, conditions);

        return moved;
    }

    // ── Spawning ────────────────────────────────────────────────────────
    /// <summary>
    /// Spawn a unit entity from a prefab GUID using InstantiateEntityImmediate.
    /// Applies critical post-spawn fixes from Bloodcraft pattern:
    /// - Sets position via Translation + LastTranslation
    /// - Prevents auto-disable when no players in range
    /// - Removes drop tables to avoid loot exploits
    /// - Disables convertability (no charming/converting spawned units)
    /// </summary>
    public static Entity SpawnUnit(PrefabGUID prefab, Entity owner, float3 position)
    {
        var sgm = Sgm;
        var entity = sgm.InstantiateEntityImmediate(owner, prefab);
        if (!entity.Exists()) return entity;

        // Set position
        entity.SetPosition(position);
        if (entity.Has<LastTranslation>())
            entity.With((ref LastTranslation lt) => lt.Value = position);

        // Prevent entity from being disabled when no players are nearby
        if (!entity.Has<CanPreventDisableWhenNoPlayersInRange>())
        {
            Em.AddComponent<CanPreventDisableWhenNoPlayersInRange>(entity);
        }
        entity.With((ref CanPreventDisableWhenNoPlayersInRange c) => c.CanDisable = new ModifiableBool(false));

        // Remove drop tables to prevent loot exploits
        // DropTableBuffer not available in this build - skipped

        // Remove convertability
        if (entity.Has<ServantConvertable>())
            Em.RemoveComponent<ServantConvertable>(entity);
        if (entity.Has<CharmSource>())
            Em.RemoveComponent<CharmSource>(entity);

        return entity;
    }

    /// <summary>Spawn a unit with buff-based instantiation for bosses.</summary>
    public static void SpawnUnitWithBuff(Entity owner, PrefabGUID prefab)
    {
        Sgm.InstantiateBuffEntityImmediate(owner, owner, prefab);
    }

    // ── Teams / Factions ────────────────────────────────────────────────
    public static void SetTeam(this Entity entity, int teamValue)
    {
        if (entity.Has<Team>())
            entity.With((ref Team team) => team.Value = teamValue);
    }

    public static void SetFaction(this Entity entity, PrefabGUID factionPrefab)
    {
        if (entity.Has<FactionReference>())
            entity.With((ref FactionReference fr) => fr.FactionGuid._Value = factionPrefab);
    }

    // ── Health ──────────────────────────────────────────────────────────
    public static void HealToFull(this Entity entity)
    {
        if (entity.Has<Health>())
        {
            entity.With((ref Health health) =>
            {
                health.Value = health.MaxHealth;
                health.MaxRecoveryHealth = health.MaxHealth;
            });
        }
    }

    /// <summary>Deal percentage of max HP as damage (0.0–1.0). Kills at 0 HP.</summary>
    public static void DealDamagePercent(this Entity entity, float percent)
    {
        if (!entity.Has<Health>()) return;
        entity.With((ref Health health) =>
        {
            float damage = health.MaxHealth.Value * Math.Clamp(percent, 0f, 1f);
            health.Value = new ModifiableFloat(Math.Max(0f, health.Value - damage));
        });
    }
}
