# BattleLuck audit matrix — phases 1 through 30

Audit date: 2026-07-23  
Repository: `C:\Users\ahmad\OneDrive\Desktop\BL`  
Source archive: `C:\Users\ahmad\Desktop\BattleLuck-Full-Audit.zip`  
Archive size: 158,668,969 bytes (151.32 MiB)

## Result

Phases 1–30 are complete within the repository/static-test scope. The solution
restores from lockfiles, builds in Release with zero warnings and errors, and
passes 130 automated tests. Two explicitly skipped castle-policy tests require
a live V Rising dedicated server with BepInEx and interop assemblies; they are
release gates for the later clean-server and multiplayer phases, not simulated
as successful here.

Repository assessment: **conditional GO for packaging and live-server
validation**. Production release remains conditional on the live-runtime gates
listed below.

## Audit matrix

| Phase | Scope | Status | Evidence and remediation |
|---:|---|---|---|
| 1 | Extract ZIP and recursive tree | Complete | Extracted to a timestamped temporary directory; `repository-tree.txt` records 11,561 entries from the extracted archive. |
| 2 | Repository hygiene and build files | Complete | Added the test project to `BattleLuck.sln`; corrected compile-time planner and developer-plan mismatches; locked restore and Release build pass. |
| 3 | Plugin bootstrap and initialization | Complete | Bootstrap initializes planner, castle services, server-thread action handlers, diagnostic sinks, and failure/unload cleanup in a defined order. Live testing identified and removed an IL2CPP-incompatible custom managed-system bootstrap. |
| 4 | Harmony patches and server tick hooks | Complete | Existing tick-hook path was traced; retry/initialization boundaries were retained and maintenance/dispatcher workloads were bounded. |
| 5 | ECS access and command buffers | Complete | Action execution no longer moves Unity ECS work to `Task.Run`. Event, team, chest, and schematic handlers execute directly on the hooked server thread, avoiding IL2CPP `GetOrCreateSystemManaged<T>` for plugin-defined systems. |
| 6 | Player/session lifecycle | Complete | Added explicit session finalization, preserved disconnect/cleanup paths, and connected event finalization to the session controller. |
| 7 | Events, modes, waves, triggers, objectives | Complete | Event actions now honor enter/leave state, event-bus subscriber failures are isolated, and mode/event loaders participate in the tested runtime path. |
| 8 | Flat event configuration migration | Complete | Corrected flat event path resolution, preserved legacy compatibility, and validated mode identifiers before file access. |
| 9 | Zones, schematics, tiles, castles | Complete | Team-corner teleport resolves configured zone centers; castle resolver now discovers owned hearts and linked objects; quota/key rules were separated for deterministic testing. |
| 10 | NPC spawn, control, navigation, combat AI | Complete | NPC operations use the existing server-thread `NpcControlService` and action catalog routes. Plugin-defined managed ECS systems remain dormant because Unity's IL2CPP generic bridge cannot instantiate them safely. Live navigation quality remains part of later server scenarios. |
| 11 | Kits, items, weapons, buffs, abilities, cleanup | Complete | Kit lookup is constrained to safe identifiers; action-system registration and cleanup paths cover the item/buff/action surface. |
| 12 | Snapshots, rollback, persistence, disconnects | Complete | Snapshot and player-transaction writes are atomic, flushed, root-contained, and serialized per target path; snapshot tracking is concurrency-safe. |
| 13 | AI providers, tools, permissions, approvals | Complete | Server configuration enforces the local Llama provider and clears remote OpenAI/Google/Cloudflare/sidecar/MCP credentials; existing approval boundaries remain active. |
| 14 | Chat channels and command framework | Complete | Added the server-side ZUI dashboard under `.ai ui` with events, players, world, AI, audit, and disable views; kept admin/command filtering explicit. |
| 15 | Runtime action registry and implementations | Complete | Added pure action-string parsing, made manifest construction safe before live-world initialization, dispatches runtime-effect handlers without relying on Harmony fallback coverage, and executes event actions directly on the server thread. |
| 16 | Prefab catalogs and server-visible world data | Complete | Existing prefab/component/system allowlists and runtime catalog paths were traced; live-system manifest injection is enabled only after the world is ready. |
| 17 | Deployment, backups, imports, exports, validation | Complete | Existing deployment/export paths were reviewed; new persistence writes use recoverable atomic replacement. Full clean installation is intentionally scheduled for phase 34. |
| 18 | Tests, docs, examples, configs, patches, generated files | Complete | Test project is part of the solution; audit tree generator and this evidence document are included; automated suite passes. |
| 19 | Cross-file dependencies and dead code | Complete | Fixed missing planner wiring, stale record constructors, dormant ECS systems, placeholder castle discovery, and static initializers that pulled reference-only assemblies into tests. |
| 20 | Working mod pattern comparison | Complete (static) | Compared ECS/system/bootstrap patterns with the included KindredExtract reference and the repository's HookDOTS/VCF conventions. Runtime behavior is gated below. |
| 21 | Release-blocking regression checklist | Complete | Checklist below records restore, build, tests, dependency scan, diff validation, secret-pattern scan, and live-runtime exceptions. |
| 22 | Final audit matrix | Complete | This document is the phase-by-phase evidence matrix. |
| 23 | Dependency versions, lockfiles, supply chain | Complete | Central package versions and both lockfiles restore in locked mode. Test dependency override upgrades `System.Text.RegularExpressions` to 4.3.1; NuGet reports no vulnerable direct or transitive packages. |
| 24 | Configuration schema and compatibility | Complete | Safe mode/event identifier validation was added at loader/editor boundaries; flat event paths and legacy migration behavior are covered by existing configuration tests. |
| 25 | Logging, diagnostics, metrics, audit trails | Complete | Diagnostic and warning sinks avoid unsafe static plugin initialization; async queue failures are observed and logged; audit artifacts are reproducible. |
| 26 | Exception handling, crash recovery, fail-safe behavior | Complete | Event subscribers and queued async work are isolated; failed bootstrap and unload clear dispatcher/action/diagnostic state; atomic writes retain the prior destination until replacement. |
| 27 | Thread safety, races, deadlocks | Complete | ECS actions remain on the server thread, queues are bounded, snapshot state uses `ConcurrentDictionary`, and atomic writes are guarded per normalized target path. |
| 28 | Performance, allocations, leaks | Complete (static) | Dispatcher is capped at 256 actions or 5 ms per tick, AI work at 16 entries per tick, and maintenance cleanup runs every 30 seconds instead of every frame. Live profiling is deferred to phase 40 soak testing. |
| 29 | Network, serialization, synchronization, bandwidth | Complete (static) | ZUI output is chunked by the 500-byte UTF-8 server packet boundary without splitting Unicode; ECS mutations continue through native replicated components. Live packet/load validation remains a later multiplayer gate. |
| 30 | Security, auth, authorization, input validation | Complete | Root-contained path helpers reject unsafe identifiers, remote AI providers are disabled server-side, admin command filtering is explicit, no Discord webhook literals were found, and NuGet reports no vulnerable packages. |

## Release-blocking regression checklist

- [x] Original audit archive located and size recorded.
- [x] Archive extracted independently and complete recursive tree generated.
- [x] `dotnet restore BattleLuck.sln --locked-mode` succeeds.
- [x] `dotnet build BattleLuck.sln -c Release --no-restore` succeeds with
  0 warnings and 0 errors.
- [x] `dotnet test BattleLuck.Tests/BattleLuck.Tests.csproj -c Release
  --no-build --no-restore` succeeds: 130 passed, 0 failed, 2 skipped.
- [x] `dotnet list BattleLuck.sln package --vulnerable
  --include-transitive` reports no vulnerable packages.
- [x] `git diff --check` reports no whitespace errors (Git only reports the
  repository's existing LF-to-CRLF conversion notices).
- [x] No Discord webhook URL literal was found outside excluded audit/reference
  material.
- [x] Credential scan findings are configuration variable/property references,
  not embedded credential values.
- [ ] Run `RemovePolicy_Requires_Existing_Record` against a live V Rising world.
- [ ] Run `GrantPermission_Rejects_When_Live_Ownership_Cannot_Be_Verified`
  against a live V Rising world.
- [ ] Execute the later clean-room installation, multiplayer load, and soak
  phases before production release.

## Material patches

- Bounded server-thread dispatcher and AI queues.
- Server-thread-only action execution without IL2CPP-incompatible custom
  `SystemBase` creation.
- Session-aware action requests and functional event finalization.
- Functional zone-corner teleport and castle ownership discovery.
- Atomic, flushed, root-contained persistence writes.
- Safe mode/event identifiers and corrected flat configuration paths.
- Local-only server AI provider enforcement.
- Server-side ZUI audit dashboard with Unicode-safe byte chunking.
- Runtime-independent parser, event, diagnostics, and manifest code paths for
  deterministic tests.
- Central dependency override removing the vulnerable legacy regular-expression
  package from the test graph.

## Reproduction commands

```powershell
dotnet restore BattleLuck.sln --locked-mode
dotnet build BattleLuck.sln -c Release --no-restore
dotnet test BattleLuck.Tests/BattleLuck.Tests.csproj -c Release --no-build --no-restore
dotnet list BattleLuck.sln package --vulnerable --include-transitive
git diff --check
```
