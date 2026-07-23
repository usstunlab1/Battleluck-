# Live server log remediation — 2026-07-24

Source: user-provided V Rising 1.1.13.99712 dedicated-server startup log.

## BattleLuck failures found

1. `aievent` and `bloodbath` were rejected because thirteen runtime-effect
   actions were absent from the action manifest during pre-world mode
   registration.
2. Core initialization repeatedly failed in
   `World.GetOrCreateSystemManaged<T>()` while attempting to instantiate a
   plugin-defined `SystemBase` through the IL2CPP generic bridge.
3. Named kit prefabs produced false offline warnings before the live
   `PrefabCollectionSystem` had been scanned.
4. Unified event zone `center` values populated `Position` but not the
   compatibility `Center` field, causing false analytics warnings.
5. Failed initialization did not clear every partially created service.

## Remediation

- Runtime-effect descriptors are injected directly whenever
  `ActionManifestService` reloads, independent of Harmony and server-world
  readiness.
- Runtime-effect execution and registration are built into
  `FlowActionExecutor`; Harmony remains only a compatibility adapter.
- Removed live registration of plugin-defined managed ECS systems. Event entry,
  corner teleport, chest spawn, team swap, event finalization, and schematic
  load now execute synchronously on the existing server-tick path.
- Offline kit validation accepts non-empty named prefabs and defers
  authoritative resolution to `KitValidator.ValidateLive` after the live prefab
  scan.
- Unified zone projection now sets both `Position` and `Center`.
- Removed analytics warnings for optional armor slots and for optional
  spawn/region capabilities that are not required by every event.
- Failed initialization now disposes the event platform/normalizer and clears
  all partially initialized service references before retrying.

## Verification

- Release build: 0 warnings, 0 errors.
- Automated tests: 130 passed, 0 failed, 2 live-world tests skipped.
- Added tests proving:
  - all thirteen runtime-effect actions exist before a live world;
  - `aievent` and `bloodbath` pass unified-event validation before startup;
  - named kit prefabs are deferred rather than rejected offline;
  - unified zone centers populate both runtime fields.

## Log entries outside BattleLuck

The save remapping messages, ProjectM lookup-performance warning, EOS/Crashpad
403 response, and Unity `JobTempAlloc` warning originate from the game/server
runtime. The BattleLuck fatal initialization loop could contribute to temporary
allocation pressure; a fresh server run with this build is required to
determine whether the Unity allocation warning persists independently.
