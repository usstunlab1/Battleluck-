# BattleLuck Runtime Integration

## Overview

This document describes the BattleLuck runtime integration for managed player-session lifecycle, server tick handling, and event routing.

## Phase 1: Managed Player-Session Lifecycle

### PlayerEventSession.cs

The `PlayerEventSession` class represents a player's session within an event, tracked outside ECS. It provides a canonical managed session lifecycle state.

**Key Features:**
- `PlayerSessionState` enum with states: `Reserved`, `Active`, `Leaving`, `Left`, `Failed`
- `EventExitReason` enum: `None`, `Voluntary`, `Eliminated`, `EventEnded`, `AdminRemoved`, `ServerDisconnected`
- All mutable lifecycle properties are privately settable
- Controlled transition methods: `Activate()`, `BeginLeaving()`, `MarkLeft()`, `MarkFailed()`

**State Transitions:**
```
Reserved → Active (via Activate)
Active → Leaving (via BeginLeaving)
Leaving → Left (via MarkLeft)
Any → Failed (via MarkFailed)
```

## Phase 2: Single Authoritative Server Tick

### ServerTickHook.cs

The server tick hook uses `System.Diagnostics.Stopwatch` for accurate delta timing:

- Delta is clamped to max 0.5 seconds to handle lag spikes
- Initialization retry at controlled 5-second interval
- Stopwatch baseline resets after successful init
- Full exception logging via `ex.ToString()`

### BattleLuckPlugin.cs

- `TryInitializeCore` prevents concurrent initialization
- Uses `_coreInitializationInProgress` flag for thread safety

## Phase 3: Router Ownership Rules

### ProjectMEventRouter.cs

The router is the sole source of typed ProjectM events. It exposes strongly-typed C# events grouped by domain:

- **Group 1**: Death & Damage events
- **Group 2**: Buffs & Abilities events
- **Group 3**: Inventory & Equip events
- **Group 4**: Teleport & Move events
- **Group 5**: Spawn & NPC events
- **Group 6**: Castle & Sequencer events
- **Group 7**: ProjectM AI events
- **Group 8**: Runtime tick and sequence hooks
- **Group 9**: Sequence lifecycle events

### ProjectMEventRouterPatches.cs

- Death patch includes `IsPlayer()` filter
- Uses Unix milliseconds for timezone-safe timestamps
- AI tick patches are disabled (see Phase 4)

## Phase 4: AI Tick Patches Disabled

### AiTickSequencePatches.cs

Marked as experimental/disabled. AI frame-level telemetry generates events every ProjectM frame without actionable payloads. AI planning runs through BattleLuck's controlled server tick instead.

## Phase 5: Death-Event Route to Session Controller

### DeathHook.cs

Compatibility event for consumers that have not yet migrated to `ProjectMEventRouter.OnPlayerDeath`. The typed router owns the sole Harmony death-system postfix and forwards each event here once.

## Phase 6: Safe Event Cleanup Integration

### SessionCleanupService.cs

- `CleanupReport` includes `PlayersAffected` and `ElapsedMilliseconds`
- `RebuildQueries()` method to rebuild cached ECS queries when server world changes
- Full exception logging via `ex.ToString()`

## Phase 7: World-Grid Semantic Cleanup

### WorldGridCoordinate.cs

- `ToWorldPoint()` - Convert grid point to world-space position
- `ToWorldCellCenter()` - Convert to center of the corresponding cell
- `ToWorld()` - Obsolete wrapper for backward compatibility
- `NearestPoint` - Round to nearest grid point using `MidpointRounding.AwayFromZero`
- `ContainingCell` - Floor to containing cell
- `FromFloat3()` - Construct from Unity float3

## Phase 8: Typed Sequence Runtime Events

### ProjectMEvents.cs

Sequence lifecycle event records:
- `SequenceStartedEvent` - Fired when a custom sequence starts
- `SequenceStepStartedEvent` - Fired when a step begins
- `SequenceStepCompletedEvent` - Fired when a step completes
- `SequenceFailedEvent` - Fired when a sequence fails
- `SequenceCompletedEvent` - Fired when a sequence completes

### EventRuntimeController.cs

Publishes sequence events through the router at appropriate lifecycle points:
- `RaiseSequenceStarted` when sequence is queued
- `RaiseSequenceStepStarted` before each step executes
- `RaiseSequenceStepCompleted` after each step completes
- `RaiseSequenceFailed` when sequence cannot be queued
- `RaiseSequenceCompleted` when sequence finishes

## Implementation Order

1. PlayerEventSession lifecycle (Phase 1)
2. ServerTickHook safety (Phase 2)
3. Router ownership + death filtering (Phases 3 + 5)
4. SessionController death/leave integration (Phase 5)
5. SessionCleanupService lifecycle integration (Phase 6)
6. World-grid semantic cleanup (Phase 7)
7. Typed sequence runtime events (Phase 8)
8. AI patches comment (Phase 4)
9. Build and deploy (Phase 9)