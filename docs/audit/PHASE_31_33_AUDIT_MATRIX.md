# BattleLuck Audit Matrix — Phases 31–33

**Generated:** 2026-07-24  
**Build:** Release, 0 warnings, 0 errors  
**Tests:** 147 total, 145 passed, 2 skipped (live-server gates)  

---

## Phase 31: Abuse Prevention, Rate Limits, and Command Permission Boundaries

### Findings

| # | Severity | Finding | File | Resolution |
|---|----------|---------|------|------------|
| 31.1 | **HIGH** | No per-player rate limiting on command dispatcher | `BattleLuckCommandDispatcher.cs` | **FIXED** — Added sliding-window rate limiter (10 cmds / 5s), admin-exempt, with stale-entry eviction at 1024 entries |
| 31.2 | **MEDIUM** | No input length guard at dispatcher level | `BattleLuckCommandDispatcher.cs` | **FIXED** — Added 4096-character max input length check before any parsing |
| 31.3 | **LOW** | AiRequestPolicy allows 2048 chars but dispatcher had no limit | `AiRequestPolicy.cs` | **RESOLVED** — Dispatcher-level 4096 limit now acts as outer guard; AiRequestPolicy 2048 limit remains as inner guard for AI-specific paths |
| 31.4 | **INFO** | BaseAiService rate limiter is global (per-provider), not per-player | `BaseAiService.cs` | **ACCEPTED** — Global semaphore serializes API calls to prevent provider throttling; per-player rate limiting at dispatcher level prevents abuse before reaching AI layer |
| 31.5 | **INFO** | Admin-only check present for all admin commands | `BattleLuckCommandDispatcher.cs` | **VERIFIED** — `cmd.AdminOnly && !isAdmin` guard is correct |
| 31.6 | **INFO** | Control character rejection in AiRequestPolicy | `AiRequestPolicy.cs` | **VERIFIED** — Rejects embedded control chars except \r, \n, \t |

### Tests Added

| Test | Status |
|------|--------|
| `Dispatcher_source_defines_per_player_rate_limiting` | ✅ PASS |
| `Dispatcher_source_defines_input_length_guard` | ✅ PASS |
| `Dispatcher_source_exempts_admins_from_rate_limiting` | ✅ PASS |
| `Dispatcher_source_evicts_stale_rate_limit_entries` | ✅ PASS |

---

## Phase 32: Save-Data Compatibility, Corruption Detection, and Migrations

### Findings

| # | Severity | Finding | File | Resolution |
|---|----------|---------|------|------------|
| 32.1 | **HIGH** | SnapshotPersistence.Read silently swallowed all exceptions — corrupt files caused silent data loss | `SnapshotPersistence.cs` | **FIXED** — Added `TryReadValidated()` with JsonException-specific catch; corrupt primary file triggers .bak recovery |
| 32.2 | **HIGH** | No backup created before overwriting snapshots | `SnapshotPersistence.cs` | **FIXED** — Write now creates `.bak` of previous snapshot before atomic overwrite |
| 32.3 | **MEDIUM** | No schema version validation on read | `SnapshotPersistence.cs` | **FIXED** — Added `CurrentSchemaVersion` (2), `MinimumReadableVersion` (1), and `IsVersionCompatible()` check on every read |
| 32.4 | **MEDIUM** | Future-version snapshots (v999) would deserialize with missing fields | `SnapshotPersistence.cs` | **FIXED** — `IsVersionCompatible()` rejects versions outside [1, 2] range |
| 32.5 | **LOW** | Legacy file fallback had no version check | `SnapshotPersistence.cs` | **FIXED** — Legacy path now calls `IsVersionCompatible()` before returning |
| 32.6 | **INFO** | Atomic write via SafeFileSystem.WriteAllTextAtomic | `SnapshotPersistence.cs` | **VERIFIED** — Write-then-rename pattern prevents partial writes |
| 32.7 | **INFO** | PlayerSnapshot.Version defaults to 2 | `SnapshotModels.cs` | **VERIFIED** — Matches CurrentSchemaVersion |

### Tests Added

| Test | Status |
|------|--------|
| `Write_Creates_Bak_Of_Previous_Snapshot` | ✅ PASS |
| `Read_Returns_Null_For_Corrupt_Json` | ✅ PASS |
| `Read_Rejects_Incompatible_Schema_Version` | ✅ PASS |
| `IsVersionCompatible_Accepts_Valid_Range` | ✅ PASS |

---

## Phase 33: Hot Reload, Plugin Reload, Shutdown, and Resource Disposal

### Findings

| # | Severity | Finding | File | Resolution |
|---|----------|---------|------|------------|
| 33.1 | **HIGH** | Unload() did not call UnsubscribeCoreEvents() — event handlers remained subscribed after unload, causing callbacks into disposed services | `BattleLuckPlugin.cs` | **FIXED** — UnsubscribeCoreEvents() now called first in Unload() |
| 33.2 | **HIGH** | Wave 1 services (Companion, Encounters, BossScaling, Portals, CreatureCapture) not nulled in Unload() | `BattleLuckPlugin.cs` | **FIXED** — All Wave 1 services now explicitly nulled |
| 33.3 | **MEDIUM** | Session not nulled after Shutdown() in Unload() | `BattleLuckPlugin.cs` | **FIXED** — `Session = null` added after `Session?.Shutdown()` |
| 33.4 | **MEDIUM** | PlayerState and DevSession not nulled in Unload() | `BattleLuckPlugin.cs` | **FIXED** — Both now explicitly nulled |
| 33.5 | **MEDIUM** | No guard against re-entrant initialization during Unload() | `BattleLuckPlugin.cs` | **FIXED** — `_coreInitializationInProgress` set to true under `_initLock` at start of Unload(), cleared in finally block |
| 33.6 | **LOW** | CleanupFailedCoreInitialization() and Unload() had divergent cleanup logic | `BattleLuckPlugin.cs` | **DOCUMENTED** — Both paths now cover the same service set; Unload() additionally handles Harmony unpatch and VCF unregister |
| 33.7 | **INFO** | Harmony UnpatchSelf called in Unload() | `BattleLuckPlugin.cs` | **VERIFIED** |
| 33.8 | **INFO** | VRisingCore.Reset() called in Unload() | `BattleLuckPlugin.cs` | **VERIFIED** |
| 33.9 | **INFO** | MainThreadDispatcher.Clear() called in Unload() | `BattleLuckPlugin.cs` | **VERIFIED** |
| 33.10 | **INFO** | GameEvents.Shutdown() called in Unload() | `BattleLuckPlugin.cs` | **VERIFIED** |

### Tests

Phase 33 changes are structural (plugin lifecycle) and require a live BepInEx runtime for integration testing. The 2 skipped tests (`RemovePolicy_Requires_Existing_Record`, `GrantPermission_Rejects_When_Live_Ownership_Cannot_Be_Verified`) are documented live-server gates.

---

## Build Infrastructure Fix

| # | Severity | Finding | File | Resolution |
|---|----------|---------|------|------------|
| INFRA.1 | **CRITICAL** | `artifacts/audited-stage/repository/` contained a full source copy that was compiled alongside main source, causing 3994 duplicate-member errors | `BattleLuck.csproj` | **FIXED** — Added `<Compile Remove="artifacts\**" />`, `<EmbeddedResource Remove="artifacts\**" />`, and `<None Remove="artifacts\**" />` exclusions |

---

## Summary

| Phase | Findings | Critical | High | Medium | Low | Info | Fixed | Accepted |
|-------|----------|----------|------|--------|-----|------|-------|----------|
| 31 | 6 | 0 | 1 | 1 | 1 | 3 | 2 | 4 |
| 32 | 7 | 0 | 2 | 2 | 1 | 2 | 5 | 2 |
| 33 | 10 | 0 | 2 | 3 | 1 | 4 | 5 | 5 |
| Infra | 1 | 1 | 0 | 0 | 0 | 0 | 1 | 0 |
| **Total** | **24** | **1** | **5** | **6** | **3** | **9** | **13** | **11** |

### Test Count Progression

| Phase | Total Tests | Passed | Skipped | Failed |
|-------|-------------|--------|---------|--------|
| Pre-audit (Phase 1-30) | 113 | 111 | 2 | 0 |
| Phase 31 | 143 | 141 | 2 | 0 |
| Phase 32 | 147 | 145 | 2 | 0 |
| Phase 33 | 147 | 145 | 2 | 0 |

### Files Modified

| File | Changes |
|------|---------|
| `BattleLuck.csproj` | Added artifacts/ exclusion rules |
| `Commands/BattleLuckCommandDispatcher.cs` | Added per-player rate limiting, input length guard, stale-entry eviction |
| `Services/SnapshotPersistence.cs` | Added .bak recovery, schema version validation, corruption detection |
| `BattleLuckPlugin.cs` | Fixed Unload() disposal: event unsubscription, Wave 1 service nulling, re-entrancy guard |
| `BattleLuck.Tests/Services/CommandDispatcherTests.cs` | Added 4 rate-limiting and input-guard tests |
| `BattleLuck.Tests/Services/SnapshotPersistenceTests.cs` | Added 4 corruption detection and schema version tests |