# BattleLuck server configuration

All runtime configuration lives in this directory. JSON files are created or updated by the plugin and can be edited while the server is stopped.

When only `BattleLuck.dll` is installed, the first server load extracts missing
defaults here and places the optional developer helper files in `tools/`. Existing
files are never overwritten. Credentials and `.env` files must be created locally;
they are intentionally not embedded in the DLL.

## Main files

- `ai_config.json` — provider, model, safety, and approval settings.
- `roadmap.json` — enabled roadmap phases and progress notes.
- `event_entities.json` — event entities and spatial points.
- `live_system_registry.json` — verified ProjectM/Unity system references.
- `prompts/` — server and developer LLM prompt templates.
- `schematics/` — reusable arena building definitions.
- `sequences/` — verified native and UUID action sequences.

Optional runtime files such as logs, snapshots, backups, and generated catalogs are local server state and should not be uploaded with a release package.

## Safe workflow

1. Stop the server or use the documented reload command.
2. Copy the file you are changing as a local backup.
3. Run `.ai event review <eventId>` and validate JSON before restarting.
4. Test the event in a private arena after review.
5. Keep risky AI actions approval-gated.

Admins can register a verified system without restarting:

`.bl.system.register ProjectM ProjectM.AbilityInputSystem system.projectm.ability_input Ability input reference`

Registrations are saved in `live_system_registry.json`. They are verified references for BattleLuck and AI tooling; they do not instantiate, patch, or invoke arbitrary native ECS systems.

## AI command process and permissions

- Public `.ai <question>` is advice-only and `.aistatus` is read-only.
- Admin `.ai catalog search` finds validated actions; `.ai action` creates a
  preview; `.ai approve` executes an approved operation through the server main
  thread; `.ai rollback` discards a pending proposal.
- Admin `.ai create <eventId> [templateId]` clones an editable event (Bloodbath is
  the default template) and registers it without a restart. Use `.ai event request`
  for preview-first AI edits after cloning.
- The action catalog covers every handler-reachable category. Verified ProjectM or
  Unity systems can be represented as `system.*` aliases after exact lookup and
  registration; aliases are references, not arbitrary native invocation.
- Developer sequences are catalog-backed and can include `wait:<seconds>` and
  `tick:<event-second>` markers. The event tick schedules them and the main-thread
  dispatcher performs approved ECS mutations.

The current command list is always available in game with `.help`.

See the [user guide](../../docs/user/README.md) and [developer guide](../../docs/developer/README.md) for command and schema details.

## Create a custom event

Admins can clone the Bloodbath lifecycle and customize it without compiling a new mode:

```text
.event.create shadow_hunt bloodbath
.ai create shadow_hunt bloodbath
```

### Process overview

The command creates `events/shadow_hunt/` with independent `flow.json`,
`zones.json`, `kits.json`, and `prompt.txt` files, assigns a unique zone hash,
and registers the event immediately. Change the copied zone center and
`teleportSpawn` before using it as a separate arena. The cloned event keeps
Bloodbath's entry/exit kit transaction, rollback snapshot, action validation, and
elimination lifecycle until you edit those files.

### Stability and safe-stage warning

The create command is a hot file-and-registration operation; `.event.start`
executes the edited flow. Invalid JSON, prefabs, actions, or native ECS operations
can hang, crash, or restart the server. Back up `events/`, run `.ai event review
<eventId>`, and test privately. On a busy server, copy and edit the template while
the server is offline or in a low-load/standby window, then use the controlled
reload command after review.

#### AI recovery and safety protocol

BattleLuck records the AI operation/approval trail in `ai_operations.log` while
the process is alive, and AI can recommend a safer approach. `.ai history` and
`.ai tasks` are one-day in-memory views and may be lost by a hard crash. Player
pre-event snapshots persist under `BepInEx/data/BattleLuck/snapshots/` and are
used by normal exit or explicit restore. A hard crash may interrupt cleanup, so
inspect the logs and verify affected-player restoration after restart; automatic
rollback during abrupt process termination is not guaranteed.

### No-code AI controller

Any player may request status through `.ai`; only admins can change event files:

```text
.ai event deploy shadow_hunt https://gist.github.com/owner/gist-id
.ai event status shadow_hunt
.ai event rollback shadow_hunt
```

`deploy` accepts an HTTPS GitHub Gist containing `flow.json`, `zones.json`,
`kits.json`, and `prompt.txt`. BattleLuck downloads into staging, validates JSON,
actions, prompt rules, kit references, and zone-hash uniqueness, backs up the
current folder under `backups/<eventId>/`, then registers the event. It never
starts the match automatically. `rollback` restores the latest known-good backup;
it cannot undo a live native action. KindredExtract IDs must still be verified
with `.dump p`/`.dump eq` before the Gist is published.
