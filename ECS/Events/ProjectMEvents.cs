namespace BattleLuck.ECS.Events;
// ─────────────────────────────────────────────────────────────────────────────
// ProjectMEvents.cs — typed event surface emitted by ProjectMEventRouter.
//
// These are the strongly-typed C# records that the router raises after reading
// the relevant ProjectM ECS system state via Harmony Postfix patches. They are
// deliberately value types (readonly record struct) so they cross the
// managed/native boundary cheaply and never hold native references beyond the
// patch's lifetime.
//
// The events are grouped by domain so a single GameMode or service can
// subscribe to one group without paying the cost of every typed event in the
// system. Each event is intentionally self-contained — no shared mutable state.
// ─────────────────────────────────────────────────────────────────────────────
#region Group 1 — Death & Damage
/// <summary>Fired when an entity dies on the server (DeathEventListenerSystem).</summary>
public readonly record struct PlayerDeathEvent(Entity Died, Entity Killer, float Time);
/// <summary>Fired when damage is dealt through DealDamageOnGameplayEvents.</summary>
public readonly record struct DamageDealtEvent(Entity Source, Entity Target, float Amount, string Category);
/// <summary>Fired when a kill credit is finalized (KillEventSystem).</summary>
public readonly record struct KillEvent(Entity Killer, Entity Victim, Entity? Assistant);
/// <summary>Fired when the OnDeathSystem processes a death reaction chain.</summary>
public readonly record struct DeathReactionEvent(Entity Died, FixedString64Bytes ReactionId);
/// <summary>Fired when a vampire is downed but not yet killed (VampireDownedServerEventSystem).</summary>
public readonly record struct VampireDownedEvent(Entity Vampire, float DownedHealthFraction);
#endregion
#region Group 2 — Buffs & Abilities
/// <summary>Fired when a buff entity is spawned in the world (BuffSystem_Spawn_Server).</summary>
public readonly record struct BuffSpawnedEvent(Entity BuffEntity, PrefabGUID BuffPrefab, Entity Source);
/// <summary>Fired when an ability cast begins spawning its prefab (AbilityCastStarted_SpawnPrefabSystem_Server).</summary>
public readonly record struct AbilityCastStartedEvent(Entity Caster, PrefabGUID AbilityGroup, PrefabGUID SpawnPrefab);
/// <summary>Fired when a minion is spawned from a gameplay event (SpawnMinionOnGameplayEvents).</summary>
public readonly record struct MinionSpawnedEvent(Entity Owner, Entity Minion, PrefabGUID MinionPrefab);
/// <summary>Fired when a buff is applied to an entity (ApplyBuffOnGameplayEvents).</summary>
public readonly record struct BuffAppliedEvent(Entity Target, PrefabGUID BuffPrefab, Entity Source, float Duration);
/// <summary>Fired when a buff is removed from an entity (RemoveBuffOnGameplayEvents).</summary>
public readonly record struct BuffRemovedEvent(Entity Target, PrefabGUID BuffPrefab);
#endregion
#region Group 3 — Inventory & Equip
/// <summary>Fired when an item is equipped (EquipItemSystem).</summary>
public readonly record struct ItemEquippedEvent(Entity Character, Entity ItemEntity, int SlotIndex);
/// <summary>Fired when an item is dropped into the world (DropInventorySystem).</summary>
public readonly record struct ItemDroppedEvent(Entity Character, Entity ItemEntity, float3 DropPosition);
/// <summary>Fired when an item is moved between inventories (MoveItemBetweenInventoriesSystem).</summary>
public readonly record struct ItemMovedEvent(Entity Item, Entity FromInventory, Entity ToInventory, int Amount);
/// <summary>Fired when an item is picked up from the world (ItemPickupSystem).</summary>
public readonly record struct ItemPickedUpEvent(Entity Character, Entity ItemEntity, int Amount);
/// <summary>Fired when an item is unequipped (UnEquipItemSystem).</summary>
public readonly record struct ItemUnequippedEvent(Entity Character, Entity ItemEntity, int SlotIndex);
#endregion
#region Group 4 — Teleport & Move
/// <summary>Fired when the generic teleport system moves an entity (TeleportSystem).</summary>
public readonly record struct TeleportEvent(Entity Target, float3 OldPosition, float3 NewPosition);
/// <summary>Fired when an entity's AI moves toward a position (MoveTowardsPositionSystem_Server_Update).</summary>
public readonly record struct MoveTowardsPositionEvent(Entity Entity, float3 Target, float Speed);
/// <summary>Fired when a player location teleport resolves (TeleportPlayerLocationSystem).</summary>
public readonly record struct PlayerLocationTeleportEvent(Entity Player, float3 NewPosition);
/// <summary>Fired when a player teleport command is executed (PlayerTeleportCommandSystem).</summary>
public readonly record struct PlayerTeleportCommandEvent(Entity Player, Entity TargetPlayer, float3? TargetPosition);
#endregion
#region Group 5 — Spawn & NPC
/// <summary>Fired when the unit spawner react system fires (UnitSpawnerReactSystem).</summary>
public readonly record struct UnitSpawnerReactEvent(PrefabGUID Prefab, float3 Position, int ReactionId);
/// <summary>Fired when a prefab is spawned via a gameplay event (SpawnPrefabOnGameplayEvents).</summary>
public readonly record struct PrefabSpawnedEvent(PrefabGUID Prefab, float3 Position, Entity Source);
/// <summary>Fired when a minion spawn slot is filled (MinionSpawnSystem).</summary>
public readonly record struct MinionSpawnSlotEvent(Entity Owner, int SlotIndex, Entity Minion);
/// <summary>Fired when a character is respawned (RespawnCharacterSystem).</summary>
public readonly record struct CharacterRespawnEvent(Entity Character, float3 SpawnPosition);
#endregion
#region Group 6 — Castle & Sequencer
/// <summary>Fired when castle buffs are updated (CastleBuffsSystem).</summary>
public readonly record struct CastleBuffEvent(Entity CastleHeart, PrefabGUID BuffPrefab, bool Applied);
/// <summary>Fired when a castle heart state transitions (CastleHeartStateUpdateSystem).</summary>
public readonly record struct CastleHeartStateEvent(Entity CastleHeart, int OldState, int NewState);
/// <summary>Fired when a sequencer update group ticks (SequencerUpdateGroup).</summary>
public readonly record struct SequencerEvent(FixedString64Bytes SequencerId, int CurrentStep, int TotalSteps);
/// <summary>Fired when a door state changes on the server (DoorSystem_Server).</summary>
public readonly record struct DoorStateEvent(Entity Door, int OldState, int NewState, Entity? User);
/// <summary>Fired when castle floor and walls are updated (CastleFloorAndWallsUpdateSystem).</summary>
public readonly record struct CastleFloorWallsEvent(Entity CastleHeart, int TileChangeCount);
#endregion

#region Group 7 - ProjectM AI
/// <summary>Fired from ProjectM AI update systems so BattleLuck can sample NPC/aggro state.</summary>
public readonly record struct AiGroupProjectMTickEvent(string SourceSystem, DateTime TimestampUtc);
#endregion

#region Group 8 - Runtime tick and sequence hooks
/// <summary>Fired by the canonical BattleLuck server tick boundary.</summary>
public readonly record struct BattleLuckServerTickEvent(float DeltaSeconds, DateTime TimestampUtc);
/// <summary>Fired after ProjectM's gameplay tick system updates.</summary>
public readonly record struct ProjectMRuntimeTickEvent(string SourceSystem, DateTime TimestampUtc);
#endregion
