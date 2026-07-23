# BattleLuck audit matrix — phases 31 through 100

Audit date: 2026-07-24  
Release: 1.1.2  
Decision: **NO-GO for production; GO for controlled live validation**

## Result

The repeatable local gate passes: locked restore, zero-warning Release build,
139 tests discovered (137 passed, 2 skipped, 0 failed), dependency
vulnerability scan, 37 JSON configuration parses, credential scan, whitespace
validation, deterministic packaging, clean extraction, and payload checksums.

This matrix does not convert missing live evidence into a pass. V Rising
version alignment, a clean dedicated-server start, multiplayer lifecycle,
castle ownership, Linux behavior, and a long live soak remain release gates.
Automated coverage is 7.04% line and 3.81% branch.

Status meanings:

- **Complete** — repeatable evidence exists in this repository or audit bundle.
- **Partial** — the local/static part is complete, but a named external gate remains.
- **Pending live** — requires a running V Rising dedicated server.
- **N/A** — not applicable to a dedicated-server-only plugin.

## Phases 31–40

| Phase | Scope | Status | Evidence / result |
|---:|---|---|---|
| 31 | Abuse prevention, rate limits, permission boundaries | Complete | Added a 2,048-character/control-character request boundary, one active query per player, four global direct-query slots, and a serialized provider request-rate gate. Admin approval boundaries remain before live actions. |
| 32 | Save compatibility, corruption detection, migrations | Complete (local) | Atomic persistence, legacy flat-event migration, invalid-data quarantine, and migration tests pass. Whole-world save compatibility remains under the server vendor. |
| 33 | Hot reload, shutdown, disposal | Complete (static/tested) | Shutdown now clears AI provider state, conversations, dedup entries, contexts, and queued work. Provider streaming always releases its limiter. Live reload is in phase 90. |
| 34 | Dedicated startup and clean-room installation | Partial | The release ZIP extracts into a clean directory and every payload hash passes. Starting a clean V Rising process remains pending. |
| 35 | Multiplayer load, reconnect, late join, host migration | Pending live / N/A | Join, reconnect, and late join need two live clients. Host migration is not applicable to a dedicated-server plugin. |
| 36 | Game, loader, framework, runtime versions | Partial | BepInEx/VCF dependencies are declared and locked. BL-037 and BL-046 track the game-reference and VCF version mismatches. |
| 37 | Platform and operating-system compatibility | Partial | Windows build, tests, packaging, and deployed-server log are evidenced. Linux is unverified. |
| 38 | Determinism, replay, seeded randomness, timing drift | Partial | Release archives are byte-reproducible and persistence uses UTC. NPC wander uses unseeded shared randomness; exact gameplay replay is unsupported. |
| 39 | Boundaries, malformed input, fault injection | Complete (local) | Validators, safe identifiers, malformed JSON behavior, request boundaries, Unicode packet boundaries, and failure isolation are tested. Disk-full/access-denied injection remains BL-047. |
| 40 | Long-duration soak and degradation | Partial | A 10,000-message in-memory test proves bounded conversation retention. Live entity/memory/tick degradation requires the four-hour runbook. |

## Phases 41–50

| Phase | Scope | Status | Evidence / result |
|---:|---|---|---|
| 41 | Upgrade, downgrade, uninstall, rollback | Partial | Event/config backup and rollback paths are tested; the operational rollback procedure is documented. Whole-plugin rollback needs a live server rehearsal. |
| 42 | Packaging, integrity, checksums, provenance | Complete | Deterministic server-only ZIP, payload `SHA256SUMS.txt`, ZIP `.sha256`, artifact manifest, and source revision/dirty flag are generated. |
| 43 | License, attribution, third-party assets | Partial | Added third-party inventory and CycloneDX SBOM. Missing license metadata for VCF/reference packages needs release-owner review. |
| 44 | Public API and extension compatibility | Partial | Runtime action compatibility tests pass; no versioned public .NET API baseline exists (BL-048). |
| 45 | Localization, encoding, culture, timezone | Complete (local) | Numeric condition evaluation now uses invariant culture; persistence uses UTC; ZUI chunking preserves UTF-8/Unicode. |
| 46 | Administrative UX, messages, runbooks | Complete | Server-side `.ai ui`, actionable validation messages, known-issues file, and operator runbook are present. |
| 47 | CI/CD, reproducible builds, release gates | Complete | CI uses locked restore, no-restore build/test, coverage, parsed vulnerability enforcement, deterministic packaging, checksums, and artifact upload. |
| 48 | Coverage mapping and untested paths | Complete | Coverlet evidence and `test-coverage-map.md` identify measured rates and P0/P1 gaps. The low rate remains a release blocker. |
| 49 | Deduplication, severity, risk acceptance | Complete | `findings.json` and `findings.csv` contain stable IDs, severity, status, priority, owner, effort, and evidence. No open risk is silently accepted. |
| 50 | Remediation owners, effort, priorities | Complete | Every finding has an owner class, effort estimate, priority, and release impact. |

## Phases 51–60

| Phase | Scope | Status | Evidence / result |
|---:|---|---|---|
| 51 | Post-remediation verification | Complete (local) | Full audit rerun passes after AI, reload, culture, CI, and packaging fixes. |
| 52 | Release-readiness decision and summary | Complete | Decision is NO-GO, with exact P0 blockers and no claimed signature. |
| 53 | Machine-readable JSON/CSV findings | Complete | `findings.json` and `findings.csv`. |
| 54 | Dependency, call-flow, lifecycle diagrams | Complete | `diagrams.md` contains Mermaid request, lifecycle, and evidence-chain diagrams. |
| 55 | Evidence/log/hash/report archive | Complete | Audited ZIP contains `evidence/`, `release/`, and a per-file `AUDIT-MANIFEST.json`. |
| 56 | Known issues and technical-debt backlog | Complete | `known-issues.md` and the findings records define residual work. |
| 57 | Final audited ZIP and byte integrity | Complete | `dist/BattleLuck-Audited-1.1.2.zip` and its SHA-256 sidecar are generated. |
| 58 | Compare final repository to original ZIP | Complete | The `.comparison.json` sidecar records original/final hashes and added, removed, changed, and unchanged paths. |
| 59 | Independent clean-directory reproduction | Complete (local) | Source is copied without build output to a new temporary directory; locked restore, Release build, and tests are rerun there. Independent hardware/OS remains unverified. |
| 60 | Final go/no-go and handoff | Complete | `release-handoff.md` communicates NO-GO for production and GO only for controlled live validation. |

## Phases 61–70

| Phase | Scope | Status | Evidence / result |
|---:|---|---|---|
| 61 | ECS component lifetime invariants | Partial | Server-thread ownership and component guards were reviewed; runtime lifetime assertions require live ECS. |
| 62 | System ordering and tick sequencing | Partial | Hook order and bounded dispatch are documented; profiler confirmation requires a live world. |
| 63 | Entity query and native-container disposal | Complete (static) | Reviewed query/array disposal paths and retained the existing `finally`/`Dispose` patterns. |
| 64 | Command-buffer playback invariants | Partial | Custom managed systems were removed from startup; remaining native playback needs live observation. |
| 65 | Main-thread enforcement | Complete | ECS action routers execute synchronously on the hooked server thread; background work queues results back through the bounded dispatcher. |
| 66 | Prompt injection and AI trust isolation | Complete | Player text is untrusted, bounded, and cannot bypass explicit admin/approval gates for catalog mutations. |
| 67 | Runtime action-manifest drift | Complete (local) | Catalog completeness and runtime-effect coverage tests pass; live system entries enable only after world readiness. |
| 68 | Configuration contract drift | Complete (local) | All 37 JSON files parse, schemas are archived, migration/validator tests pass, and unsafe identifiers are rejected. |
| 69 | Audit-log integrity | Complete | Evidence, payload, release, and archive manifests carry SHA-256 hashes; no cryptographic signature is claimed. |
| 70 | Resource budgets and backpressure | Complete (static/tested) | Main-thread and AI tick queues are bounded; provider/direct-query gates and bounded conversation history limit amplification. |

## Phases 71–80

| Phase | Scope | Status | Evidence / result |
|---:|---|---|---|
| 71 | Event-definition functional matrix | Complete (local) | Default bundles and unified migration tests validate event definitions, modes, actions, and flat/split compatibility. |
| 72 | Kit/item functional matrix | Complete (local) | Kit validators and controller tests cover identifiers, deferred live prefab resolution, application, and cleanup boundaries. |
| 73 | Zone/schematic/castle matrix | Partial | Validators and policy rules pass; castle ownership and physical placement remain live gates. |
| 74 | Action parameter matrix | Complete (local) | Catalog completeness, parser, runtime-effect registration, and parameter validation tests pass across 5,384 prefab action records. |
| 75 | Permission and approval matrix | Complete (static/tested) | Admin-only practice/actions and preview/approval routing are enforced before mutation. Live role tests remain in the multiplayer runbook. |
| 76 | ZUI Unicode and packetization | Complete | ZUI output is server-side, opt-in, UTF-8 byte-bounded, and tested not to split Unicode. |
| 77 | Chat parsing and channel isolation | Complete (local) | `.ai`, aliases, explicit prefixes, bounded sessions, stop commands, deduplication, and request policy are covered. |
| 78 | Admin workflow usability | Complete (documented) | Dashboard sections, errors, approval paths, rollback, and operator commands are documented. |
| 79 | Accessibility and readability | Partial | Text UI avoids client assets and preserves Unicode; color contrast/screen-reader behavior cannot be certified through server packets alone. |
| 80 | Operational workflow rehearsal | Partial | Local audit/package workflow is rehearsed; live install/rollback rehearsal remains pending. |

## Phases 81–90

| Phase | Scope | Status | Evidence / result |
|---:|---|---|---|
| 81 | Exception and subscriber chaos | Complete (local) | Event subscribers and queued actions isolate failures; tests verify subscriber exceptions do not stop later handlers. |
| 82 | AI provider outage/degradation | Complete (local) | Missing providers produce a local fallback; timeouts/errors are recorded; Ollama is unavailable on this audit host. |
| 83 | Disk and filesystem failure | Partial | Atomic replacement, root containment, and concurrent writes are tested; disk-full/access-denied injection remains BL-047. |
| 84 | Malformed JSON/configuration | Complete (local) | Parsers reject/quarantine invalid content and all shipped JSON parses successfully. |
| 85 | Concurrent persistence writes | Complete (local) | Per-normalized-path serialization and atomic file tests cover concurrent writes. |
| 86 | Reconnect churn | Pending live | Requires automated clients or a two-player controlled server run. |
| 87 | Deployment idempotency | Complete (local) | Repeated release creation is byte-identical; repeated clean extraction and checksums pass. |
| 88 | Backup restoration | Complete (local) | Event deployment and snapshot restoration paths have automated coverage; world-save restore remains operational. |
| 89 | Long-run state growth | Complete (local) | Conversation history stays capped at 200 after 10,000 appends; live ECS/entity growth remains in phase 40. |
| 90 | Clean shutdown under load | Partial | Static cleanup is comprehensive and transient AI state is cleared; shutdown during a live event/provider stream remains pending. |

## Phases 91–100

| Phase | Scope | Status | Evidence / result |
|---:|---|---|---|
| 91 | Software bill of materials | Complete | CycloneDX 1.5 SBOM contains 131 unique resolved NuGet components from both lockfiles. |
| 92 | Dependency/source provenance | Partial | Lockfiles, source revision, dirty flag, and package hashes are recorded. A clean committed and signed source state is still required. |
| 93 | Artifact inventory | Complete | Server package and audited bundle carry sorted per-file manifests with bytes and SHA-256. |
| 94 | Source-to-artifact mapping | Complete | Artifact manifest maps the tested DLL/dependencies/docs to hashes and the source revision. |
| 95 | Configuration hash manifest | Complete | The audited bundle manifest hashes every included configuration file. |
| 96 | Test evidence index | Complete | TRX, Cobertura, command logs, environment data, audit summary, and evidence manifest are archived. |
| 97 | Residual-risk register | Complete | Open findings are preserved in JSON, CSV, known issues, coverage map, and handoff. |
| 98 | Final regression checklist | Complete (local) | The one-command audit reruns all local release gates and stops on failure. |
| 99 | Final go/no-go adjudication | Complete | NO-GO for production because P0 live/version/coverage gates remain open. |
| 100 | Release handoff package | Complete | Audited ZIP, server ZIP, checksums, evidence, SBOM, findings, diagrams, runbook, and decision are assembled for the release owner. |

## Reproduction

```powershell
.\tools\Invoke-BattleLuckAudit.ps1
.\tools\New-BattleLuckAuditedZip.ps1
.\tools\Compare-BattleLuckArchives.ps1 `
  -OriginalArchive C:\Users\ahmad\Desktop\BattleLuck-Full-Audit.zip `
  -AuditedArchive .\dist\BattleLuck-Audited-1.1.2.zip
```
