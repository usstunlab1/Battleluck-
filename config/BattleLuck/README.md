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

## Per-player AI chat backup

The server owner can opt in through `ai_config.json`:

```json
"chat_backup": {
  "enabled": true,
  "path": "",
  "retention_days": 30,
  "max_file_size_mb": 8
}
```

When `path` is empty on Windows, enabled backups are written to
`%USERPROFILE%\AppData\LocalLow\Stunlock Studios\VRising\BattleLuck\chat-backups\<steamId>\YYYY-MM-DD.jsonl`.
Linux and other hosts use `BepInEx/config/BattleLuck/chat-backups/` by default.
They are not sent to players or installed in client game folders. Keep the
directory private and follow the server owner's retention and consent policy.

## Safe workflow

1. Stop the server or use the documented reload command.
2. Copy the file you are changing as a local backup.
3. Validate JSON before restarting.
4. Run `.validateconfig` and test the event in a private arena.
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

The command creates `events/shadow_hunt/` with independent `flow.json`, `zones.json`, `kits.json`, and `prompt.txt` files, assigns a unique zone hash, and registers the event immediately. Change the copied zone center and `teleportSpawn` before using it as a separate arena. The cloned event keeps Bloodbath's entry/exit kit transaction, rollback snapshot, action validation, and elimination lifecycle until you edit those files.
