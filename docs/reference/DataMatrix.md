# BattleLuck Final Data Matrix

This is the source-of-truth matrix for configuration, catalogs, runtime state, and AI memory.

## Configuration Sources

| Source | Path | Purpose |
|--------|------|---------|
| `actions_catalog.json` | `config/BattleLuck/actions_catalog.json` | Canonical action definitions with capability metadata |
| `custom_sequences.json` | `config/BattleLuck/custom_sequences.json` | Custom sequence definitions |
| `events/{modeId}.json` | `config/BattleLuck/events/{modeId}.json` | Event definitions (zones, objects, phases, triggers) |
| `gg.battleluck.cfg` | `config/gg.battleluck.cfg` | Plugin settings and feature flags |

## Runtime State

| Component | Location | Description |
|-----------|----------|-------------|
| `PlayerEventSession` | `Models/PlayerEventSession.cs` | Per-player session lifecycle state |
| `ActiveSession` | `Core/SessionController.cs` | Per-zone session state |
| `EventRuntimeSession` | `Services/Runtime/EventRuntimeController.cs` | Event runtime state |
| `CustomSequenceRuntimeRun` | `Models/CustomSequencesConfig.cs` | Active custom sequence run |

## Event Types

### ProjectM Events (via ProjectMEventRouter)

| Group | Event | Source System |
|-------|-------|---------------|
| Death & Damage | `PlayerDeathEvent` | DeathEventListenerSystem |
| Death & Damage | `DamageDealtEvent` | DealDamageOnGameplayEvents |
| Death & Damage | `KillEvent` | KillEventSystem |
| Death & Damage | `DeathReactionEvent` | OnDeathSystem |
| Death & Damage | `VampireDownedEvent` | VampireDownedServerEventSystem |
| Buffs & Abilities | `BuffSpawnedEvent` | BuffSystem_Spawn_Server |
| Buffs & Abilities | `AbilityCastStartedEvent` | AbilityCastStarted_SpawnPrefabSystem_Server |
| Buffs & Abilities | `MinionSpawnedEvent` | SpawnMinionOnGameplayEvents |
| Buffs & Abilities | `BuffAppliedEvent` | ApplyBuffOnGameplayEvents |
| Buffs & Abilities | `BuffRemovedEvent` | RemoveBuffOnGameplayEvents |
| Inventory & Equip | `ItemEquippedEvent` | EquipItemSystem |
| Inventory & Equip | `ItemDroppedEvent` | DropInventorySystem |
| Inventory & Equip | `ItemMovedEvent` | MoveItemBetweenInventoriesSystem |
| Inventory & Equip | `ItemPickedUpEvent` | ItemPickupSystem |
| Inventory & Equip | `ItemUnequippedEvent` | UnEquipItemSystem |
| Teleport & Move | `TeleportEvent` | TeleportSystem |
| Teleport & Move | `MoveTowardsPositionEvent` | MoveTowardsPositionSystem_Server_Update |
| Teleport & Move | `PlayerLocationTeleportEvent` | TeleportPlayerLocationSystem |
| Teleport & Move | `PlayerTeleportCommandEvent` | PlayerTeleportCommandSystem |
| Spawn & NPC | `UnitSpawnerReactEvent` | UnitSpawnerReactSystem |
| Spawn & NPC | `PrefabSpawnedEvent` | SpawnPrefabOnGameplayEvents |
| Spawn & NPC | `MinionSpawnSlotEvent` | MinionSpawnSystem |
| Spawn & NPC | `CharacterRespawnEvent` | RespawnCharacterSystem |
| Castle & Sequencer | `CastleBuffEvent` | CastleBuffsSystem |
| Castle & Sequencer | `CastleHeartStateEvent` | CastleHeartStateUpdateSystem |
| Castle & Sequencer | `SequencerEvent` | SequencerUpdateGroup |
| Castle & Sequencer | `DoorStateEvent` | DoorSystem_Server |
| Castle & Sequencer | `CastleFloorWallsEvent` | CastleFloorAndWallsUpdateSystem |
| ProjectM AI | `AiGroupProjectMTickEvent` | AI update systems |
| Runtime Tick | `BattleLuckServerTickEvent` | ServerTickHook |
| Runtime Tick | `ProjectMRuntimeTickEvent` | AiTickSequencePatches |
| Sequence Lifecycle | `SequenceStartedEvent` | EventRuntimeController |
| Sequence Lifecycle | `SequenceStepStartedEvent` | EventRuntimeController |
| Sequence Lifecycle | `SequenceStepCompletedEvent` | EventRuntimeController |
| Sequence Lifecycle | `SequenceFailedEvent` | EventRuntimeController |
| Sequence Lifecycle | `SequenceCompletedEvent` | EventRuntimeController |

## Action Catalog Metadata

| Property | Type | Description |
|----------|------|-------------|
| `ActionId` | string | Unique action identifier |
| `Action` | string | Action string template |
| `Category` | string | Action category (e.g., "runtime", "build", "npc") |
| `RiskLevel` | string | "safe", "controlled", "destructive", "critical" |
| `RequiresApproval` | bool | Whether admin approval is required |
| `Description` | string | Human-readable description |
| `Handler` | string | Handler type name |
| `MainThreadRequired` | bool | Whether action must run on main thread |
| `UsesNativeReplication` | bool | Whether action uses native server replication |
| `Availability` | string | "server_only", "unsupported", "client_required" |
| `Executable` | bool | Whether action passes registration gate |
| `ClientRequired` | bool | Whether client mod is required |
| `Reversible` | bool | Whether action can be reversed |
| `EventAllowed` | bool | Whether action is allowed in event contexts |
| `RollbackAction` | string | Action to reverse this action |
| `SideEffects` | List<string> | Known side effects |
| `AllowedSources` | ActionSourcePermissions | Sources permitted without confirmation |
| `ConfirmationRequiredSources` | ActionSourcePermissions | Sources requiring confirmation |
| `AllowedPhases` | SessionPhaseAllowance | Session phases where action may execute |

## Session Lifecycle

| State | Description | Entry Conditions |
|-------|-------------|----------------|
| `Reserved` | Player has entered zone, not yet active | Zone entry detected |
| `Active` | Player is actively participating | `Activate()` called |
| `Leaving` | Player is in leave process | `BeginLeaving()` called |
| `Left` | Player has left the event | `MarkLeft()` called |
| `Failed` | Session failed during setup | `MarkFailed()` called |

## Exit Reasons

| Reason | Description |
|--------|-------------|
| `None` | No exit reason set |
| `Voluntary` | Player chose to leave |
| `Eliminated` | Player was eliminated (death limit reached) |
| `EventEnded` | Event ended normally |
| `AdminRemoved` | Admin removed player |
| `ServerDisconnected` | Server connection lost |

## Action Source Permissions

| Source | Flag |
|--------|------|
| Admin | `Admin` |
| Player | `Player` |
| AI | `AI` |
| Webhook | `Webhook` |
| MCP | `MCP` |
| EventRuntime | `EventRuntime` |
| DevConsole | `DevConsole` |
| System | `System` |

## Session Phase Allowance

| Phase | Flag |
|-------|------|
| Any | `Any` |
| Setup | `Setup` |
| Active | `Active` |
| Ending | `Ending` |
| Cleanup | `Cleanup` |