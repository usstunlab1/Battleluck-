# Test coverage map

Measured by coverlet on 2026-07-24:

| Metric | Covered | Valid | Rate |
|---|---:|---:|---:|
| Lines | 2,309 | 32,763 | 7.04% |
| Branches | 821 | 21,536 | 3.81% |

The suite contains 139 discovered tests: 137 pass and 2 live-world castle tests
are skipped. Passing tests cover configuration migration, validators, action
catalog completeness, event-bus isolation, AI parsing/policy, Unicode-safe ZUI
packetization, snapshot persistence, atomic file writes, castle policy rules,
and bounded conversation storage.

## Highest-priority uncovered paths

| Priority | Area | Required test level |
|---|---|---|
| P0 | Plugin load/unload/reload against IL2CPP | Live server |
| P0 | Session join, disconnect, reconnect, event end, rollback | Live multiplayer |
| P0 | ECS queries, component presence, command-buffer playback | Live server |
| P0 | Version 1.1.13 compatibility | Matching reference build plus live server |
| P1 | Disk full, access denied, interrupted write | Fault-injection integration |
| P1 | Provider timeout, stream abort, shutdown during request | HTTP integration |
| P1 | Four-hour event/NPC/zone soak | Instrumented live server |
| P1 | Linux dedicated server | Platform matrix |
| P2 | Public extension API compatibility | API baseline/diff test |

Coverage is evidence, not a release percentage target by itself. Production
approval requires the P0 scenarios even if aggregate coverage increases.
