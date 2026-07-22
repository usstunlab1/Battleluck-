**# BattleLuck Server-Side Revamp Plan

**Review date:** 2026-07-22  
**Product constraint:** BattleLuck remains a dedicated-server plugin. It ships no BattleLuck client DLL. Native chat is the universal fallback; an optional packet bridge may enhance the experience for players who already run ZUI.  
**Goal:** One upload, safe defaults, embedded configuration/data, a small free local-AI path, native chat commands, advanced killfeed, and per-event results.

## Final delivery summary

This is the approved implementation direction:

| Outcome | Final approach | Measurable target |
|---|---|---|
| Simplify architecture | One event spine, one runtime owner, one presentation service, one `.bl` command tree | No duplicate service ownership; no implementation file above 1,000 lines |
| Improve performance | Event-driven processing, bounded collections, cached lookups, batched cleanup, no LLM in the tick path | Under 1 ms average and 5 ms p99 per normal plugin tick |
| Enhance usability | Permission-aware `.bl` pages, concise native notifications, generated help | Common player/admin tasks reachable in two commands or fewer |
| Remove unnecessary features | Archive web/Docker/agent experiments; delete duplicate AI/providers/controllers after parity | One distributable server-plugin product |
| Improve configuration | One versioned owner config, embedded defaults, atomic migration/reload | Fresh install requires no edits; invalid config never becomes live |
| Enhance AI | Offline AI-lite by default; one optional free local LLM adapter | Core help remains available with no model, key, account, or network |
| Improve quality | Unit, contract, migration, integration, packaging, soak, and performance tests | Clean build; release gates pass; no unhandled soak-test errors |

### Non-negotiable constraints

- BattleLuck code and authority remain server-side; players never install a BattleLuck client DLL.
- ZUI is a soft enhancement, not a hard dependency. Unmodified clients retain the complete native-chat experience.
- Normal V Rising chat channels are never replaced or consumed.
- AI cannot directly mutate ECS/world state.
- Event exit/abort always attempts bounded cleanup and player restoration.
- No secrets, provider credentials, or live endpoints in tracked defaults.
- No silent executable or model download during server startup.

## Executive decision

BattleLuck should become a focused **server-side event platform**, not a collection of overlapping game, AI, web, Discord, MCP, and dashboard experiments.

The winning product shape is:

1. One event runtime and one typed event stream.
2. One native chat command root: `.bl`.
3. One presentation service for native chat, announcements, and map markers.
4. One append-only event-data format plus compact snapshots.
5. Embedded defaults that extract on first launch without overwriting owner edits.
6. A zero-config deterministic “AI-lite” assistant available immediately.
7. One optional free local LLM adapter, auto-detected and disabled gracefully when no runtime/model exists.

ZUI supports a useful hybrid boundary. BattleLuck can remain server-side and send `[[ZUI]]` JSON chat packets, while the ZUI plugin installed on a player's client intercepts those packets and renders the window. The supplied `ClientChatSystem`, `UICanvasSystem`, `ProjectM.UI`, and `UniverseLib.UI` patches are client-executed rendering/interception code inside ZUI; they do not run a HUD on a headless dedicated server. Therefore BattleLuck may support ZUI without shipping a BattleLuck client DLL, but rich panels still require that player's client to have ZUI installed. Native chat remains mandatory as the safe fallback.

## Audit snapshot

| Measure | Current result |
|---|---:|
| Tracked C# files | 326 |
| Approximate C# lines | 63,655 |
| Declared commands | 289 |
| Harmony patch files | 13 |
| AI-related service files | 22 |
| Registered action-catalog entries | 309 |
| Tracked test files/projects | 19 files in one test project |
| Current build | **Failed: 3 errors, 16 warnings** |
| Largest implementation file | `FlowActionExecutor.cs`, 3,582 lines |
| Second-largest implementation file | `PlayerCommands.cs`, 2,386 lines |
| Current default local model | `llama3.2:3b`, about 2.0 GB in Ollama |
| Current Thunderstore ZIP in workspace | about 3.4 MB |

### Immediate blockers

1. `Patches/ChatMessageSystemPatch.cs:54` calls `StartsWith` with a `char` and a `StringComparison`; this does not compile on the target API.
2. `Services/Runtime/EventDeploymentService.cs:378` calls a missing `PromptContextLoader.Parse` method.
3. `Services/Runtime/EventDeploymentService.cs:397` compares a method group to an integer, likely a consequence of the missing/incorrect parse result.
4. `config/BattleLuck/ai_config.json` contains a live-looking Discord webhook URL. Revoke it, remove it from the tracked default, and inspect repository history.
5. The workflow builds only branches named `mainss`; normal `main` pushes and pull requests are not protected.
6. Config names do not match model properties: JSON uses `google_ai_studio` and `sidecar`, while `AIConfig` expects `google_ai` and `ai_sidecar`. Those sections are silently ignored.
7. The documented “one upload and AI works” behavior is not real: `LocalAiRuntimeManager` only starts an existing Ollama executable and pulls a model if Ollama is already installed or manually bundled.
8. The shared AI pseudo-channel broadcasts every player question and every AI answer to all members. It is not a real native channel, cancellation is not passed into the provider call, and it risks privacy leaks.

## Comparison with 10 V Rising mods

The package numbers below are Thunderstore values observed on 2026-07-22 and may change.

| Mod | Type | Package data | What it does well | Lesson for BattleLuck |
|---|---|---|---|---|
| [Bloodcraft](https://thunderstore.io/c/v-rising/p/zfolmt/Bloodcraft/) | Server | v1.13.22, 54K downloads, 6.7 MB | Deep progression but grouped into recognizable player systems | Keep depth, but expose it through a small root command and clear domains |
| [BloodyBoss](https://thunderstore.io/c/v-rising/p/Trodi/BloodyBoss/) | Server | v2.1.3, 8.7K downloads, 317 KB | Population scaling, phases, recovery, summon cleanup, focused admin workflow | Add explicit encounter recovery and lifecycle-owned entities |
| [BloodyEncounters](https://thunderstore.io/c/v-rising/p/Trodi/BloodyEncounters/) | Server | focused random encounter loop | Simple trigger → timed encounter → reward loop | BattleLuck events need a short happy path, not 289 commands |
| [KindredCommands](https://thunderstore.io/c/v-rising/p/odjit/KindredCommands/) | Server | v2.5.8, 43K downloads, 1.4 MB | Broad admin coverage with established command grouping and permissions | Use one permission-aware command tree and generated help |
| [KindredLogistics](https://thunderstore.io/c/v-rising/p/Kindred/KindredLogistics/) | Server | v1.6.1, 43K downloads, 533 KB | Server-only interactions feel native; territory scope and short aliases | Prefer native game interactions and strict ownership boundaries |
| [KindredSchematics](https://thunderstore.io/c/v-rising/p/odjit/KindredSchematics/) | Server | v1.5.6, 11K downloads, 227 KB | Strong safety warnings and explicit destructive commands | Keep schematic actions admin-only, staged, bounded, and auditable |
| [Killfeed](https://thunderstore.io/c/v-rising/p/deca/Killfeed/) | Server | v0.3.1, 2.6K downloads, 79 KB | Tiny, understandable kill/streak/death leaderboards | Separate event capture, stats, presentation, and config |
| [CustomKill](https://thunderstore.io/c/v-rising/p/huhwhat/CustomKill/) | Server | v1.1.75, 905 downloads, 322 KB | Assists, kill attribution, anti-grief rules, persistent statistics | Add attribution windows, assists, anti-farm rules, and season stats |
| [ZUI](https://thunderstore.io/c/v-rising/p/Zanakinz/ZUI/) | Client-server hybrid framework | v2.1.0, 862 downloads, 331 KB | Client renderer plus server-originated hidden JSON chat packets | Add a soft packet presenter with native fallback; never make event operation depend on ZUI |
| [Raphael](https://thunderstore.io/c/v-rising/p/TheShadowRealm/Raphael/) | Client | v0.65.5, 1.4K downloads, 804 KB | Unified command UI, overlays, standalone chat window | Confirms that real panels/overlays require a client mod, which BattleLuck forbids |

### Competitive conclusion

BattleLuck already exceeds these mods in scope, action catalog size, rollback thinking, and configurable event ambition. It loses today on reliability, discoverability, package clarity, and ownership boundaries. More features will not fix that. A smaller kernel, clean event data, and polished native presentation will.

## Target server-only architecture

```text
Harmony/game hooks
        |
        v
GameEventNormalizer ----> EventLedger (JSONL + snapshot)
        |
        v
EventRuntime ----> ScoreService ----> ResultService
        |                |                  |
        +----------------+------------------+
                         |
                         v
                NativePresentationService
                - private chat reply
                - global announcement
                - killfeed line
                - map marker / replicated effect

NativeCommandRouter (.bl)
        |
        +--> query runtime/results/config
        +--> stage admin mutation
        +--> AI-lite intent/search
        +--> optional local LLM (advice/authoring only)
```

### Ownership rules

- `GameEventNormalizer` is the only component translating ProjectM/HookDOTS/Harmony signals into BattleLuck events.
- `EventRuntime` is the only owner of event-session lifecycle.
- `ScoreService` is the only writer of scores, streaks, assists, and standings.
- `ResultService` is the only finalizer of event results.
- `NativePresentationService` is the only component formatting player-visible output.
- AI never mutates ECS directly. It produces a typed proposal that passes validation, authorization, confirmation, and the main-thread executor.
- Every spawned entity is registered to an event run and cleaned through that run.

## Canonical event data

Use a versioned envelope for every significant fact. Persist JSONL for recovery/debugging and compact a snapshot at event end. Do not add LiteDB unless measured data volume proves JSONL insufficient.

```json
{
  "schema": 1,
  "event_id": "player.kill",
  "event_run_id": "01J...",
  "mode_id": "bloodbath",
  "sequence": 184,
  "occurred_utc": "2026-07-22T12:30:05.123Z",
  "actor_steam_id": 7656119,
  "target_steam_id": 7656120,
  "team_id": 2,
  "points": 3,
  "reason": "pvp_kill",
  "data": {}
}
```

### Required event IDs

| Domain | Events |
|---|---|
| Lifecycle | `event.created`, `event.started`, `round.started`, `round.ended`, `event.ended`, `event.aborted` |
| Player | `player.joined`, `player.left`, `player.eliminated`, `player.restored` |
| Combat | `player.down`, `player.kill`, `player.assist`, `player.death`, `streak.started`, `streak.ended`, `bounty.claimed` |
| Objectives | `objective.progress`, `objective.captured`, `wave.started`, `wave.cleared`, `boss.phase`, `boss.defeated` |
| Score | `score.changed`, `standing.changed`, `winner.declared` |
| Operations | `config.loaded`, `config.rejected`, `action.staged`, `action.approved`, `action.executed`, `action.failed` |

### Kill record

```json
{
  "killer": 7656119,
  "victim": 7656120,
  "assists": [7656121],
  "downed_by": 7656119,
  "killer_level": 72,
  "victim_level": 75,
  "killer_team": 1,
  "victim_team": 2,
  "streak": 4,
  "bounty": 2,
  "valid_for_score": true,
  "rejection_reason": null
}
```

### Final result record

```json
{
  "schema": 1,
  "event_run_id": "01J...",
  "mode_id": "bloodbath",
  "started_utc": "2026-07-22T12:00:00Z",
  "ended_utc": "2026-07-22T12:20:00Z",
  "end_reason": "score_limit",
  "winner": { "type": "team", "id": "2", "score": 50 },
  "standings": [],
  "awards": [],
  "counters": {
    "kills": 26,
    "assists": 17,
    "objectives": 4,
    "participants": 12
  }
}
```

## Native command UI

ZUI-like discoverability is recreated as concise, permission-aware chat pages.

### Optional ZUI packet presenter

BattleLuck may additionally expose a server-side `ZuiPacketPresenter`. It has no compile-time reference to `ZUI.dll`; it only serializes the documented `[[ZUI]]` packet envelope and sends it privately through the server chat API.

Boundary:

```text
BattleLuck server                    Player client
-----------------                    -------------
Event/result data
      |
ZuiPacketPresenter
      |
private [[ZUI]] JSON message  --->   ZUI ClientChatPatch intercepts message
                                      |
                                      ZUI UIManager renders local canvas
                                      |
button emits .bl command       <---   player clicks button
      |
server permission + validation
```

Safety and compatibility rules:

- A player must opt in with `.bl ui zui`; never send hidden packets blindly because clients without ZUI may see the raw packet in chat.
- `.bl ui native` immediately returns to native-only presentation.
- Native `.bl` commands remain complete and authoritative.
- ZUI buttons contain only allowlisted `.bl` commands. Permission checks always happen again on the server.
- Allowlist packet types and cap packet length, text length, window count, button count, and update frequency.
- Escape all player, event, and LLM text before JSON/rich-text serialization.
- Disable remote image URLs by default; embedded/color-only layouts avoid client downloads and tracking.
- Never place secrets, Steam IDs, admin tokens, or confirmation tokens in a broadcast packet.
- Build event overview, live scoreboard, advanced killfeed, final results, and admin status as projections of the same canonical event data used by native chat.
- Failure to render or acknowledge ZUI never affects gameplay, scoring, cleanup, or results.

Suggested ZUI windows:

| Window | Data | Refresh policy |
|---|---|---|
| `BattleLuck.Home` | Event status and permission-aware navigation | On open/state change |
| `BattleLuck.Event` | Phase, timer, objectives, participants | At most once per second |
| `BattleLuck.Scoreboard` | Top standings and personal row | On score change, coalesced |
| `BattleLuck.Killfeed` | Last 10 validated combat events | On validated kill, burst-limited |
| `BattleLuck.Results` | Final standings, MVP, awards | Once at finalization/on request |
| `BattleLuck.Admin` | Runtime/config/AI health and staged actions | On request only |

### Player surface

```text
.bl                       Home/status and the 5 most relevant actions
.bl event                 Current event, phase, timer, player count
.bl join [event]          Join an available event
.bl leave                 Leave safely and restore snapshot
.bl score                 Personal score and rank
.bl top [kills|score]     Current-event leaderboard
.bl results [last|id]     Final result summary
.bl stats [season]        Persistent kill/death/assist/event stats
.bl ai <question>         Private advice/search; never a shared pseudo-channel
.bl help [topic] [page]   Generated, permission-aware help
```

### Admin surface

```text
.bl admin event start <id>
.bl admin event stop <reason>
.bl admin event validate <id>
.bl admin action preview <action>
.bl admin action confirm <token>
.bl admin config status
.bl admin config reload
.bl admin ai status
.bl admin diagnostics
```

Keep legacy aliases for one deprecation release, log their use, and return the replacement command. Remove aliases in the next major version.

## Advanced server-side killfeed

### Capture and attribution

- Record damage contributors in a bounded 15-second attribution window.
- Credit the downing player if another player performs the finishing hit within the configured window.
- Award assists only when contribution exceeds a configurable percentage or meaningful-control threshold.
- Reject self-kills, same-team kills, event-generated duplicates, repeated farm kills, and invalid level-gap kills from scoring while still recording the death.
- Use event-run IDs to prevent vanilla deaths or another mod's entities from being misclassified.

### Presentation

Examples using native rich-text chat/notification only:

```text
⚔ [4 streak] Clan · Ahmad (72) defeated Raven (75) +1 assist
☠ Raven ended Ahmad's 7-kill streak — bounty +3
🏆 Bloodbath ended: Team 2 wins 50–44 · MVP Ahmad 14K/5D/6A
```

Configurable scopes: `off`, `event`, `global`. Default to `event` to avoid chat spam. Rate-limit multi-kill lines and collapse bursts.

### Results

- Snapshot standings at each round end and event end.
- Deterministic tie-break order: score, objectives, kills, assists, fewer deaths, earliest score timestamp.
- Produce winner, MVP, objective leader, support leader, longest streak, and participation awards.
- Keep the last 20 results plus season aggregates by default.
- Commands query snapshots; they never recompute old results from mutable live state.

## Smaller, free, zero-config AI

The current `llama3.2:3b` model is about 2.0 GB, so it cannot be embedded while also keeping BattleLuck small. A useful LLM always requires model weights and an inference runtime. “Small DLL,” “LLM bundled inside,” and “no external installation/download” cannot all be true simultaneously.

### Recommended two-tier design

#### Tier 1: embedded AI-lite (default, immediate, free)

- Embed command metadata, action catalog, event schemas, short documentation chunks, and validated examples as compressed resources.
- Use deterministic intent classification, token search, synonyms, fuzzy matching, and response templates.
- Answer commands, config help, event status, action discovery, and troubleshooting without a model.
- No network, credentials, model, or config required.
- Keep live mutation behind the same preview/confirm pipeline.

This satisfies one-upload readiness and should handle most in-game admin questions faster and more reliably than a tiny LLM.

#### Tier 2: optional local LLM (free, auto-detected)

- Keep exactly one OpenAI-compatible/Ollama adapter.
- Default model: `qwen2.5:0.5b` (about 398 MB) rather than `llama3.2:3b` (about 2.0 GB).
- On startup, detect a local endpoint. If present but the model is missing, an explicit admin command may download it.
- Do not silently download executables or hundreds of MB during server startup.
- If unavailable, log one concise notice and continue with AI-lite. Gameplay must never depend on LLM health.
- Use the LLM only for private advice and event-authoring proposals, not real-time combat decisions.

### AI deletions/consolidation

After parity tests, consolidate these into `Services/Assistant/`:

- Keep: one assistant facade, AI-lite search, one local-LLM client, proposal validator, confirmation registry.
- Remove: Cloudflare provider, Google provider, GenAI stack client, sidecar service, MCP runtime, hologram service, AI logger provider matrix, duplicate AI spawn/wave controllers, shared AI channel state.
- Move game actions back to the normal action/runtime services; AI must not own spawn or wave controllers.

## AI Developer Bridge and NPC simulation planning

### Purpose

Add an admin-only bridge that lets AI inspect an explicitly approved, read-only projection of selected ProjectM/Unity systems and prepare an NPC simulation plan. It must reuse the normal action catalog, validation pipeline, dev arena, spawned-entity registry, audit ledger, and confirmation system.

The bridge is **not** a C# compiler, arbitrary reflection console, or direct ECS mutation API. JSON `usings` and `namespaces` are type-resolution hints checked against embedded allowlists. They cannot load assemblies, execute methods, patch systems, or access unspecified components.

### Command workflow

```text
.ai.dev request npc-simulation [manifest]
.ai.dev status [requestId]
.ai.dev inspect <requestId>
.ai.dev plan <requestId> <goal>
.ai.dev simulate <requestId>
.ai.dev diff <requestId>
.ai.dev approve <requestId> <confirmationToken>
.ai.dev run <requestId>
.ai.dev export <requestId>
.ai.dev revoke <requestId>
```

These will migrate under the canonical command tree as aliases:

```text
.bl admin dev request npc-simulation [manifest]
.bl admin dev plan <requestId> <goal>
.bl admin dev simulate <requestId>
.bl admin dev approve <requestId> <confirmationToken>
.bl admin dev run <requestId>
```

`.ai.dev request` never grants write access. It creates a short-lived request, validates the manifest, reports the requested capabilities, and issues a request ID. Simulation and live execution are separate permissions and confirmations.

### State machine

```text
Requested
   -> ManifestValidated
   -> AccessApproved
   -> SnapshotCaptured
   -> PlanReady
   -> SimulationReady
   -> Simulated
   -> ReviewRequired
   -> ExecutionApproved
   -> Executed
   -> Verified

Any state -> Rejected | Expired | Failed | Revoked | Reverted
```

Rules:

- Only authenticated server admins may create or approve requests.
- Access expires after 15 minutes by default and is bound to admin Steam ID, request ID, manifest hash, and event/dev-session ID.
- Read, simulate, and execute are distinct capabilities.
- Approval tokens are single-use, short-lived, stored hashed, and never sent through broadcast chat or ZUI packets.
- A changed manifest, plan, snapshot scope, or action set invalidates approval.
- Live execution is unavailable unless every action resolves to an existing allowlisted action and passes the normal runtime validator.
- The LLM receives immutable DTOs, never `EntityManager`, `Entity`, native pointers, component references, delegates, `Type`, or reflection objects.

### Developer manifest

Default manifests live as embedded resources under `developer/manifests/`. Server owners may add manifests under `BepInEx/config/BattleLuck/developer/manifests/`. Owner manifests must pass the same schema and allowlist validation.

Example `npc-simulation.json`:

```json
{
  "schema": 1,
  "id": "npc-simulation",
  "display_name": "NPC Simulation Planner",
  "runtime": "ProjectM.Server",
  "usings": [
    "ProjectM",
    "ProjectM.Network",
    "Unity.Entities",
    "Unity.Mathematics"
  ],
  "assemblies": [
    "ProjectM",
    "Unity.Entities"
  ],
  "systems": [
    {
      "type": "ProjectM.AiMoveSystem",
      "access": "observe",
      "purpose": "Inspect NPC movement state"
    },
    {
      "type": "ProjectM.AggroSystem",
      "access": "observe",
      "purpose": "Inspect target selection and aggro state"
    }
  ],
  "entity_queries": [
    {
      "id": "controlled_npcs",
      "source": "battleluck.spawned_entities",
      "all": ["ProjectM.PrefabGUID", "ProjectM.Health"],
      "optional": ["ProjectM.AggroConsumer", "Unity.Transforms.LocalTransform"],
      "projection": [
        "battleluck_npc_id",
        "prefab_guid",
        "position",
        "health_percent",
        "level",
        "behavior",
        "aggro_target",
        "home_position"
      ],
      "limit": 32
    }
  ],
  "catalogs": [
    "actions.npc",
    "prefabs.npc",
    "simulation.behaviors"
  ],
  "capabilities": [
    "snapshot.read",
    "plan.create",
    "simulation.isolated"
  ],
  "limits": {
    "max_entities": 32,
    "max_components_per_entity": 12,
    "max_snapshot_bytes": 262144,
    "max_simulation_seconds": 120,
    "max_actions": 25,
    "max_spawned_entities": 20
  }
}
```

The exact system and component names in a shipped manifest must be generated from and verified against the target Oakveil server assemblies/KindredExtract inventory before release. An unknown namespace, assembly, system, component, field, catalog, capability, or projection fails closed.

### KindredExtract reference validation

The supplied KindredExtract `EcsSystemHierarchyService` and `PlayerService` are reference sources, not code for the LLM to execute. BattleLuck should import their verified metadata through two narrow adapters:

```csharp
public interface ISystemCatalogProvider
{
    SystemCatalogSnapshot CaptureServerSystems();
}

public interface IPlayerDirectory
{
    bool TryFindBySteamId(ulong steamId, out PlayerDirectoryEntry player);
    bool TryFindByName(string name, out PlayerDirectoryEntry player);
    IReadOnlyList<PlayerDirectoryEntry> GetOnlinePlayers();
}
```

The production implementations run only on the server main thread. The developer bridge receives copied immutable records, never KindredExtract service objects.

#### ECS system hierarchy validation

The attached `EcsSystemHierarchyService` is valuable for discovering managed, unmanaged, and component-group systems and their update ordering. Before its data enters a developer manifest:

- Restrict capture to the verified V Rising server simulation world. Do not scan client, presentation, editor, conversion, or disposed worlds.
- Treat the result as a directed graph, not a guaranteed tree. Systems may be reported in multiple groups, and malformed relationships must not cause recursion loops.
- Apply a visited set plus maximum node, edge, parent, child, and depth limits.
- Replace `nodes.Add` with deterministic duplicate detection and a validation issue; one duplicate must not abort the full catalog build.
- Validate every `SystemHandle` with `world.Unmanaged.IsSystemValid` immediately before reading its type.
- Record missed/unknown handles as unresolved metadata. Do not fabricate a type or permit registration from an unresolved node.
- Never export `World`, `SystemHandle`, `SystemTypeIndex`, `Type`, `ComponentSystemBase`, or `GetExistingSystemInternal` instances to JSON/LLM context.
- Export only stable metadata: full type name, assembly name, category, update-group names, ordering index, resolved flag, and a source-build fingerprint.
- Sort nodes and edges deterministically before hashing or serialization.
- Verify disposal requirements for every native collection returned by the target Unity Entities version; all temporary/native allocations must be released in the same main-thread capture scope.
- Catch errors per system/group, retain the rest of the catalog, and produce structured validation issues rather than log-only warnings.
- Rename the adapter API to the correctly spelled `BuildSystemHierarchyForWorld`; retain any misspelled source method only behind the adapter.

Example safe system projection:

```json
{
  "schema": 1,
  "world": "Server",
  "captured_utc": "2026-07-22T13:00:00Z",
  "source_fingerprint": "sha256:...",
  "counts": {
    "group": 0,
    "managed": 0,
    "unmanaged": 0,
    "unknown": 0,
    "not_used": 0
  },
  "systems": [
    {
      "id": "system:ProjectM.ExampleSystem",
      "type": "ProjectM.ExampleSystem",
      "assembly": "ProjectM",
      "category": "unmanaged",
      "groups": ["ProjectM.ServerSimulationSystemGroup"],
      "resolved": true
    }
  ],
  "issues": []
}
```

Actual system names must come from the target server capture. Example/guessed names never become executable references.

#### Player directory validation

The supplied `PlayerService` demonstrates the correct source component (`ProjectM.Network.User`) and useful name/Steam-ID indexes, but it must be hardened before Developer Bridge use:

- Construct the name dictionary with `StringComparer.OrdinalIgnoreCase`; normalize with `Trim()` and never use culture-sensitive `ToLower()` keys.
- Make `TryFindName` use the same normalization policy as insertion.
- Detect duplicate/empty character names and zero platform IDs explicitly; never silently discard a collision through `TryAdd`.
- Build and update the directory on the server main thread. State this as an invariant and assert it in debug builds rather than adding unsafe off-thread ECS reads.
- Subscribe to authoritative connect, disconnect, character-created, character-destroyed, and rename events. A one-time constructor scan is insufficient.
- Before using cached entities, verify world identity, `EntityManager.Exists`, entity version, required `User` component, and current `LocalCharacter` validity.
- Do not keep a `NativeArray<Entity>` created with `Allocator.Temp` alive across `yield return`. Materialize safe projections inside `try/finally`, dispose the native array immediately, and return a managed list/read-only array.
- Dispose the constructor's `ToEntityArray(Allocator.Temp)` in `finally`. Audit the `EntityQueryBuilder` and query lifetime against the exact Unity Entities version.
- Avoid `IncludeDisabled` for ordinary online-player queries. Use it only for an explicit offline-directory rebuild capability.
- `forceOffline` updates directory state only unless the authoritative `User` component is deliberately written back through an approved action. Name that distinction explicitly.
- Never call `DebugEventsSystem.RenameUser` from a query or AI inspection path. Rename is a separate `player.rename` action requiring admin permission, validation, confirmation, audit, and postcondition checks.
- Validate rename input for length, allowed characters, uniqueness, reserved names, rich-text/control characters, and current connection/entity state.
- Do not log the complete online player-name list during cache creation. Log counts; expose names only to authorized commands.
- Redact Steam IDs before LLM use. Assign stable request-scoped aliases such as `player-1`; retain the real mapping inside the access grant only.

Safe player DTO:

```json
{
  "id": "player-1",
  "display_name": "Ahmad",
  "online": true,
  "has_character": true,
  "event_participant": true,
  "team_id": 2
}
```

Raw user entities, character entities, network IDs, Steam IDs, admin flags, inventory, castle ownership, IP/network information, and authentication state are excluded unless a separate named capability and projection explicitly requires them.

#### Reference promotion pipeline

```text
KindredExtract/target-server capture
        -> source fingerprint + game/build version
        -> structural validation
        -> server-world filtering
        -> namespace/assembly/type allowlist comparison
        -> bounded system/player projections
        -> deterministic serialization + SHA-256
        -> maintainer review
        -> embedded candidate catalog
        -> target-server verification
        -> executable allowlist promotion
```

Discovery never equals permission. A system/component appearing in KindredExtract data makes it searchable; only a separately reviewed action adapter makes it executable.

Add these optional manifest sections:

```json
{
  "reference_sources": [
    {
      "id": "kindredextract.system-hierarchy",
      "required_build": "target-server-fingerprint",
      "access": "metadata.read"
    }
  ],
  "player_queries": [
    {
      "id": "event_players",
      "scope": "active_event",
      "projection": ["alias", "display_name", "online", "team_id"],
      "limit": 32
    }
  ]
}
```

### “All data and entities” boundary

The bridge can make all **approved relevant data** discoverable through catalogs and paged projections; it must never serialize every entity/component in the server world into one prompt.

Reasons:

- A full ECS dump is unbounded and can stall the main thread.
- Raw entities contain irrelevant/internal state and unstable component layouts.
- Entity index/version pairs are transient and cannot be trusted after structural changes.
- Player identifiers, inventories, castles, and network state require privacy and permission boundaries.
- Small local models perform better with focused, typed context.

Implementation rules:

- Query only on the server main thread.
- Copy allowlisted fields into immutable DTOs, then release native containers before asynchronous AI work.
- Default to BattleLuck-owned NPCs in the active dev/event run.
- Require an additional capability for nearby vanilla NPCs; never include every world NPC by default.
- Enforce count, byte, time, component, and recursion limits before allocation.
- Page large catalogs using stable catalog IDs; do not use live entity handles as long-lived IDs.
- Redact Steam IDs in LLM context unless the requested operation specifically requires them; use session-scoped aliases.

### Snapshot contract

```json
{
  "schema": 1,
  "request_id": "dev_01J...",
  "manifest_id": "npc-simulation",
  "manifest_sha256": "...",
  "captured_utc": "2026-07-22T13:00:00Z",
  "event_run_id": "dev_7656119",
  "systems": [
    { "type": "ProjectM.AiMoveSystem", "resolved": true, "enabled": true }
  ],
  "entities": [
    {
      "id": "npc-7",
      "prefab_guid": -1905691330,
      "position": { "x": -3000.0, "y": 5.0, "z": -2992.0 },
      "health_percent": 1.0,
      "level": 80,
      "behavior": "guard",
      "aggro_target": null,
      "home_position": { "x": -3000.0, "y": 5.0, "z": -2992.0 }
    }
  ],
  "catalog_refs": {
    "actions.npc": "sha256:...",
    "prefabs.npc": "sha256:..."
  },
  "truncated": false
}
```

### Ready-plan output

The bridge must require structured JSON from the LLM and validate it before showing it to the admin:

```json
{
  "schema": 1,
  "request_id": "dev_01J...",
  "goal": "Create two guard groups that patrol and engage opposing targets",
  "summary": "Two isolated teams, three NPCs per team, four patrol points",
  "assumptions": [],
  "required_systems": ["ProjectM.AiMoveSystem", "ProjectM.AggroSystem"],
  "steps": [
    {
      "id": "spawn-red-1",
      "action": "npc.spawn",
      "parameters": { "prefab": "...", "level": 80, "team": 1 },
      "expected": "One tracked NPC exists in the dev run"
    }
  ],
  "assertions": [
    { "type": "entity_count", "query": "controlled_npcs", "operator": "eq", "value": 6 }
  ],
  "risks": [],
  "cleanup": ["dev.entities.destroy", "player.snapshot.restore"],
  "requires_live_execution": false
}
```

Every action name and parameter is resolved through the canonical action manifest. Free-form method names, code, shell commands, file paths, URLs, Harmony patches, reflection expressions, and arbitrary component writes are rejected.

### Simulation modes

1. **Static validation:** resolve types, actions, prefab IDs, limits, permissions, and cross-references without touching ECS.
2. **Model simulation:** run behavior/state transitions against pure managed DTOs for fast plan checking.
3. **Isolated dev-arena simulation:** spawn tracked clones inside `DevSessionService` bounds, execute capped actions, observe projected outcomes, then clean up and restore the admin snapshot.
4. **Live event execution:** optional final stage; requires a new confirmation over an unchanged plan and snapshot scope.

The default stops after modes 1 and 2. The admin explicitly requests mode 3. Mode 4 is never implied by `.ai.dev request`, `.ai.dev plan`, or `.ai.dev simulate`.

### Consolidation with existing code

| Existing component | New role |
|---|---|
| `DevSessionService` | Own isolated arena lifecycle, player snapshot, timeout, tracked spawn cleanup |
| `KindredSystemReferenceService` | Resolve searchable system/component reference candidates |
| `LiveSystemRegistryService` | Become read-only verified reference catalog; registration does not imply execution |
| `AiGroupProjectMLlmBridge` | Replace with `AiDeveloperBridge`; remove periodic autonomous snapshots and auto-execute path |
| `NpcActionAuditor` | Emit pre/post assertions and diffs into the canonical event ledger |
| `ActionManifestService` | Sole source of executable action names and parameter schemas |
| `LlmRuntimeActionValidator` | Validate every proposed step before simulation or execution |
| `SpawnedEntityRegistry` | Own all dev-run NPCs and cleanup |

Suggested files:

```text
Services/DeveloperBridge/
  AiDeveloperBridge.cs
  DeveloperAccessService.cs
  DeveloperManifestLoader.cs
  DeveloperReferenceResolver.cs
  DeveloperSnapshotService.cs
  DeveloperPlanService.cs
  DeveloperSimulationService.cs
  DeveloperAuditService.cs
Models/DeveloperBridge/
  DeveloperManifest.cs
  DeveloperAccessGrant.cs
  DeveloperSnapshot.cs
  DeveloperPlan.cs
  DeveloperSimulationResult.cs
Commands/Admin/AiDeveloperCommands.cs
config/BattleLuck/developer/schemas/developer-manifest.schema.json
config/BattleLuck/developer/schemas/developer-plan.schema.json
```

### Developer bridge acceptance tests

- Reject non-admin request and approval.
- Reject unknown assembly, namespace, system, component, projection, catalog, or capability.
- Reject a system catalog captured from the wrong/disposed world or a different game/build fingerprint.
- Validate system-graph duplicates, missing handles, multiple parents, cycles, depth limits, deterministic ordering, and partial per-node failures.
- Verify system JSON contains no live `World`, system instance, type/reflection object, native handle, or pointer representation.
- Validate player-name case-insensitive lookup, whitespace normalization, empty names, duplicate names, zero Steam IDs, and rename collisions.
- Validate connect, disconnect, rename, character destruction/recreation, stale entity versions, invalid local characters, and offline rebuild behavior.
- Verify every temporary player/system native collection is disposed before returning or awaiting; prohibit iterator methods that retain `Allocator.Temp` data.
- Verify ordinary LLM projections redact Steam/network/entity identifiers and that elevated projections require an explicit capability.
- Verify player rename can occur only through the approved `player.rename` action and confirms the authoritative postcondition.
- Reject manifest traversal, absolute paths, external URLs, code strings, reflection expressions, and oversize JSON.
- Verify manifest hashes and invalidate grants after any change.
- Enforce separate read/simulate/execute grants and expiry.
- Cap entity count, component count, snapshot bytes, action count, spawned entities, simulation duration, and chat/ZUI output.
- Confirm native containers are disposed before asynchronous LLM work.
- Confirm entity destruction/recreation cannot reuse a stale handle as the same NPC.
- Confirm dev-arena exit, timeout, disconnect, exception, plugin unload, and server shutdown clean tracked NPCs and restore the player snapshot.
- Confirm failed assertions prevent live execution.
- Confirm LLM output cannot bypass the action manifest or confirmation registry.
- Confirm a complete audit record contains requester, approver, manifest/plan hashes, scope, timestamps, steps, pre/post projections, result, and cleanup result.

## Configuration redesign

### Files visible to owners

```text
BepInEx/config/BattleLuck/
  battleluck.json
  events/
    bloodbath.json
    colosseum.json
  data/
    results.jsonl
    season.json
  logs/
```

`battleluck.json` should contain only stable operator choices:

```json
{
  "schema": 1,
  "events": { "enabled": true, "enabled_ids": ["bloodbath", "colosseum"] },
  "chat": { "prefix": ".bl", "killfeed_scope": "event" },
  "results": { "keep": 20, "season_id": "default" },
  "assistant": { "mode": "auto", "local_url": "http://127.0.0.1:11434" }
}
```

### Embedded resources

Keep these inside the DLL and extract only when owner-editable:

- Default `battleluck.json`
- Built-in event definitions
- Schemas
- Action/command catalog
- AI-lite knowledge index
- Migration manifests

Rules:

- Never overwrite an owner-edited file.
- Add a `schema` number to every owner file.
- Migrate through typed transforms with backup + atomic replace.
- Validate the complete candidate config before swapping it into live state.
- Queue unsafe reload changes until the current event ends.
- Secrets are environment-only and never included in defaults.

## File simplification and deletion plan

No deletion should happen until build parity, migration tests, and a tagged backup exist.

### Delete immediately after security/build recovery

| Path | Reason |
|---|---|
| `Users/ahmad/OneDrive/Desktop/BL/icon.png` | Accidental tracked absolute-user-path copy |
| `config/BattleLuck/.env` | Secrets/config environment files must not be tracked |
| root `package.json` and `package-lock.json` | Unrelated Supabase dependency shell; not part of the C# server plugin |
| `agents/my-first-agent.yaml` | Unrelated experimental agent file |
| `fix_notice.ps1` | One-off maintenance script |
| duplicate root `Config.yaml` | Not part of canonical BepInEx configuration |
| `commands.txt` | Generated/stale command list; help should be generated from metadata |
| `plugininfo.cs` | Duplicate assembly/plugin metadata if not referenced by the build |

### Move to a separate repository or archive branch

| Path | Reason |
|---|---|
| `messaging-app/` | Client/web dashboard; violates focused server-plugin scope |
| `website/` | Website is not the runtime plugin |
| `ai-assets/` and `docker-compose.ai.yml` | Separate deployment product; contradicts one-upload server plugin |
| `AI_RUNTIME.md` | Docker sidecar documentation belongs with the archived sidecar |
| large marketing images under `docs/assets/` | Keep out of the plugin repository/package unless documentation publishing needs them |

### Remove after code consolidation

- Cloud-provider and sidecar AI files listed in the AI section.
- Duplicate `Services/AI/SpawnController.cs` and `Services/AI/WaveController.cs` after callers use canonical services.
- Legacy command classes once `.bl` parity and one-release aliases are verified.
- Obsolete progression/controller adapters after `PlayerProgressionService` owns all calls.
- Old multi-file event fragments after migration to one canonical event definition per event.
- Old schemas, prompts, and catalogs only after the new embedded bundle covers their validated data.

### Keep

- Session snapshots/rollback code.
- Main-thread dispatcher and validation pipeline.
- Event deployment audit and bounded cleanup.
- Verified prefab/action data, but compress/embed it and stop exposing internal catalogs as owner config.
- Focused tests, packaging metadata, license, changelog, README, and operator documentation.

## Delivery phases and gates

### Phase 0 — Recovery and security

Work:

- Revoke/remove the Discord webhook and scan history.
- Fix the 3 compile errors and reduce warnings to zero in touched code.
- Correct CI branch names and run build + tests on every PR.
- Add a packaging smoke test that loads embedded resources from the built DLL.

Gate:

- Clean Release build, all tests pass, no tracked secret candidates, package installs on a clean test server.

### Phase 1 — Product boundary and deletion

Work:

- Tag/zip the current repository.
- Remove or archive unrelated web, Docker, agent, and duplicate metadata files.
- Publish an explicit supported-feature inventory.
- Mark legacy commands and AI providers deprecated.

Gate:

- Repository has one product: the server plugin; package contains only runtime requirements and documentation.

### Phase 2 — Typed event spine and data ledger

Work:

- Add event envelope, monotonic sequence, event-run ID, JSONL ledger, compaction, and result snapshots.
- Route death, score, wave, objective, boss phase, round, and lifecycle events through it.
- Add recovery tests for truncated JSONL and server restart.

Gate:

- Every result can be traced to persisted typed events; duplicate signals are idempotent.

### Phase 3 — Killfeed and results

Work:

- Implement attribution window, assists, streaks, bounties, anti-farm rules, deterministic standings, and awards.
- Add `.bl score`, `.bl top`, `.bl results`, and `.bl stats`.
- Format through one native presentation service.

Gate:

- Test matrix covers self-kill, team-kill, down/finish split, disconnect, simultaneous death, repeated victim, tie, restart, and event abort.

### Phase 4 — Chat/command patch replacement

Work:

- Replace the shared AI pseudo-channel with explicit private `.bl ai` requests.
- Ensure VCF/native commands get one deterministic dispatch path.
- Never consume normal Global/Local/Clan/Whisper messages.
- Pass cancellation tokens end-to-end and enforce per-player cooldown/queue limits.
- Add the soft `ZuiPacketPresenter`, `.bl ui zui`, and `.bl ui native` selection.
- Project the same home, event, scoreboard, killfeed, results, and admin data to both native chat and ZUI packets.
- Test raw-packet suppression with ZUI clients and confirm native clients never receive ZUI packets unless explicitly opted in.

Gate:

- Normal chat is unchanged; AI replies are private; disconnect/unload cannot deliver stale replies; ZUI loss/failure falls back without affecting the event.

### Phase 5 — Config v1

Work:

- Introduce one owner config, schema migration, validation report, backup, and atomic reload.
- Embed internal data/catalogs and built-in events.
- Generate permission-aware help from command metadata.

Gate:

- Fresh installation works with no manual config; upgrades preserve edits; invalid config never replaces live config.

### Phase 6 — AI-lite and optional local LLM

Work:

- Ship the embedded searchable knowledge bundle.
- Reduce to one local adapter and an optional 0.5B model profile.
- Add `.bl admin ai status` and explicit `.bl admin ai install-model` if model download is retained.

Gate:

- All core help works offline with no model; LLM failure has zero gameplay effect; no cloud key is requested.

### Phase 7 — AI Developer Bridge

Work:

- Implement the developer-manifest and developer-plan schemas plus embedded NPC simulation manifest.
- Consolidate the existing dev session, Kindred reference, live registry, AI-group snapshot, NPC audit, action validation, and spawned-entity ownership behind `AiDeveloperBridge`.
- Add validated KindredExtract system-catalog and player-directory adapters with server-world filtering, main-thread capture, deterministic hashes, privacy-safe projections, and strict native-allocation disposal.
- Add time-limited read/simulate/execute capabilities and single-use confirmations.
- Implement static validation, managed-model simulation, isolated dev-arena simulation, plan diff, export, revoke, timeout, and cleanup.
- Expose `.ai.dev` compatibility commands and canonical `.bl admin dev` commands.

Gate:

- The acceptance matrix in the developer-bridge section passes; no LLM path owns live ECS objects; every simulated/live step is allowlisted, audited, bounded, reversible where supported, and tied to an unchanged manifest/plan hash.

### Phase 8 — Dedicated-server soak and release

Work:

- Run 24-hour soak with simulated events, joins/leaves, restarts, and malformed chat/config.
- Measure main-thread time, allocations, ledger growth, and cleanup completeness.
- Build a minimal Thunderstore package and installation checklist.

Gate targets:

- No unhandled exceptions.
- No leaked event-owned entities after abort/end.
- Event hook/presentation work stays below 1 ms average server tick and below 5 ms p99 excluding intentional world mutations.
- Chat output remains below configured burst limits.
- Package operates with BepInEx + VCF only; HookDOTS remains a dependency only if the event normalizer demonstrably needs it.

## Definition of “ChatGPT wins”

The revamp is successful when a server owner can:

1. Install dependencies and upload BattleLuck.
2. Start the server without editing a file.
3. Run `.bl` and understand the product immediately.
4. Start a built-in event with one admin command.
5. See accurate native killfeed, scores, and final results.
6. Restart the server without losing or corrupting event data.
7. Ask private built-in help without cloud credentials.
8. Optionally enable a free local LLM without changing gameplay safety.
9. Upgrade without overwriting owner config.
10. Diagnose any failed action from a stable event/audit record.

## Sources

- [Thunderstore V Rising package catalog](https://thunderstore.io/c/v-rising/)
- [ZUI API/package page](https://thunderstore.io/c/v-rising/p/Zanakinz/ZUI/)
- [Raphael package page](https://thunderstore.io/c/v-rising/p/TheShadowRealm/Raphael/)
- [Ollama llama3.2 model sizes](https://ollama.com/library/llama3.2)
- [Ollama qwen2.5 model sizes](https://ollama.com/library/qwen2.5)
**