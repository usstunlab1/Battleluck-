# BattleLuck Runtime Integration

## Phase 1: Managed Player-Session Lifecycle

**File: `Models/PlayerEventSession.cs`**

1. Add `Left` to `PlayerSessionState` enum (currently missing).
2. Add `EventExitReason` enum in the same file:
   ```csharp
   public enum EventExitReason { None, Voluntary, Eliminated, EventEnded, AdminRemoved, ServerDisconnected }
   ```
3. Make `State`, `FailedStage`, `FailureReason`, `TeamIndex`, `DeathCount`, `Eliminated` privately settable (use `private set`).
4. Default `TeamIndex` to `-1`.
5. Add controlled transition methods:
   - `Activate()` -- Reserved -> Active, sets ActivatedAtUtc
   - `BeginLeaving()` -- Active -> Leaving
   - `MarkLeft()` -- Leaving -> Left
   - `MarkFailed(string stage, string reason)` -- any -> Failed
6. Keep `RegisterDeath(int)` idempotent (already is).
7. Add `ExitReason` property (privately set).

---

## Phase 2: Single Authoritative Server Tick

**File: `Patches/ServerTickHook.cs`**

1. Replace `DateTime.UtcNow` delta with `System.Diagnostics.Stopwatch`.
2. Clamp delta to max (e.g., `Math.Min(delta, 0.5f)`) instead of replacing with `0.016f`.
3. Add `_initRetryAt` timestamp; retry init at controlled interval (e.g., 5 seconds), not every frame.
4. After successful init, reset stopwatch baseline.
5. Log full exception (`ex.ToString()`) instead of `ex.Message`.
6. Swap order: call `BattleLuckPlugin.ServerTick(delta)` first, then raise router event.

**File: `BattleLuckPlugin.cs`**

7. Prevent concurrent init in `TryInitializeCore` (already has `_coreInitializationInProgress` -- verify it works with the new retry interval).

---

## Phase 3: Router Ownership Rules

**File: `Services/Runtime/ProjectMEventRouter.cs`**

1. Add sequence lifecycle event regions:
   ```csharp
   public event Action<SequenceStartedEvent>? OnSequenceStarted;
   public event Action<SequenceStepStartedEvent>? OnSequenceStepStarted;
   public event Action<SequenceStepCompletedEvent>? OnSequenceStepCompleted;
   public event Action<SequenceFailedEvent>? OnSequenceFailed;
   public event Action<SequenceCompletedEvent>? OnSequenceCompleted;
   ```
2. Add corresponding `Raise*` internal methods.
3. Log full exception in `SafeInvoke` (`ex.ToString()` instead of `ex.Message`).

**File: `Patches/ProjectMEventRouterPatches.cs`**

4. Add `IsPlayer()` filter in death patch:
   ```csharp
   if (!died.Exists() || !died.IsPlayer()) continue;
   ```
5. Replace `DateTime.UtcNow.TimeOfDay.TotalSeconds` with Unix milliseconds or `DateTime.UtcNow`.
6. Remove `BehaviourTreeSystem_OnUpdate` and `AggroSystem_OnUpdate` patches (or comment them out) -- AI tick patches stay disabled per Phase 4.

---

## Phase 4: Keep AI Tick Patches Disabled

**File: `Patches/AiTickSequencePatches.cs`**

1. Already has `[HarmonyPatch]` commented out -- no code change needed.
2. Add a comment marking the class as experimental/disabled:
   ```csharp
   // DISABLED: AI frame-level telemetry generates events every ProjectM frame
   // without actionable payloads. AI planning runs through BattleLuck's
   // controlled server tick instead.
   ```

---

## Phase 5: Death-Event Route to Session Controller

**File: `Core/SessionController.cs`**

1. In `HandleSessionParticipantDeath`, after elimination is reached, use the new lifecycle methods:
   ```csharp
   participant.BeginLeaving();
   // ... rollback, teleport ...
   participant.MarkLeft();
   ```
2. Set `ExitReason = EventExitReason.Eliminated` when elimination triggers.
3. Move `RegisterDeath` call so it goes through the session controller (already does -- verify no direct calls from Harmony patches).
4. Ensure `HandleDeath` checks `_playerSessions` for Active state before processing.

**File: `Patches/DeathHook.cs`**

5. No structural changes needed -- it already subscribes to the router and forwards.

---

## Phase 6: Safe Event Cleanup Integration

**File: `Services/Cleanup/SessionCleanupService.cs`**

1. Rename `SkippedPlayers` to `PlayersAffected` in `CleanupReport`.
2. Add `ElapsedMilliseconds` to `CleanupReport`; measure cleanup duration with `Stopwatch`.
3. Count actual removed buffs (check return of `TryRemoveBuff` if possible) rather than attempts.
4. Add `RebuildQueries()` method to rebuild cached ECS queries when server world changes.

**File: `Core/SessionController.cs`**

5. Wire `BeginLeaving` flow to call cleanup in order:
   - Restore player state
   - Remove transient buffs (via `SessionCleanupService`)
   - Teleport outside event
   - Remove participant from active session
   - If session empty: `CleanupZone(zoneCenter, radius, zoneHash)`
   - `MarkLeft()`

---

## Phase 7: World-Grid Semantic Cleanup

**File: `Models/WorldGridCoordinate.cs`**

1. Rename `ToWorld` to `ToWorldPoint` (keep old name as obsolete wrapper or rename directly).
2. Add `ToWorldCellCenter(float height)`:
   ```csharp
   public WorldCoordinatePosition ToWorldCellCenter(float height = 0f) => new(
       (Column - OriginIndex) * CellSize + CellSize / 2f,
       height,
       (Row - OriginIndex) * CellSize + CellSize / 2f);
   ```
3. Rename `NearestCell` to `NearestPoint` in `WorldGridPosition`.
4. Add `ContainingCell` using `MathF.Floor`:
   ```csharp
   public WorldGridCoordinate ContainingCell => new(
       (int)MathF.Floor(Column),
       (int)MathF.Floor(Row));
   ```
5. Use `MidpointRounding.AwayFromZero` in `NearestPoint`.
6. Add `WorldCoordinatePosition.FromFloat3(float3)`:
   ```csharp
   public static WorldCoordinatePosition FromFloat3(float3 v) => new(v.x, v.y, v.z);
   ```
7. Add optional map-bound validation method.

---

## Phase 8: Typed Sequence Runtime Events

**File: `Models/EventDefinitionModels.cs` or new `Models/SequenceEvents.cs`**

1. Define the sequence event records:
   ```csharp
   public record SequenceStartedEvent(string SessionId, string SequenceId, DateTime StartedAtUtc);
   public record SequenceStepStartedEvent(string SessionId, string SequenceId, int StepIndex, string StepLabel);
   public record SequenceStepCompletedEvent(string SessionId, string SequenceId, int StepIndex, bool Success);
   public record SequenceFailedEvent(string SessionId, string SequenceId, string Reason);
   public record SequenceCompletedEvent(string SessionId, string SequenceId);
   ```

**File: `Services/Runtime/EventRuntimeController.cs`**

2. Publish sequence events through the router at the appropriate lifecycle points.

---

## Phase 9: Verification

1. Build the project (`dotnet build`).
2. Verify no compile errors.
3. Deploy to BepInEx plugin directory.
4. Run regression tests per the user's test checklist.

---

## Implementation Order

1. `PlayerEventSession` lifecycle (Phase 1)
2. `ServerTickHook` safety (Phase 2)
3. Router ownership + death filtering (Phases 3 + 5)
4. `SessionController` death/leave integration (Phase 5)
5. `SessionCleanupService` lifecycle integration (Phase 6)
6. World-grid semantic cleanup (Phase 7)
7. Typed sequence runtime events (Phase 8)
8. AI patches comment (Phase 4 -- trivial)
9. Build and deploy (Phase 9)
