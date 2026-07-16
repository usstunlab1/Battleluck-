# BattleLuck server configuration

All runtime configuration lives in this directory. JSON files are created or updated by the plugin and can be edited while the server is stopped.

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
3. Validate JSON before restarting.
4. Run `.validateconfig` and test the event in a private arena.
5. Keep risky AI actions approval-gated.

Admins can register a verified system without restarting:

`.bl.system.register ProjectM ProjectM.AbilityInputSystem system.projectm.ability_input Ability input reference`

Registrations are saved in `live_system_registry.json`. They are verified references for BattleLuck and AI tooling; they do not instantiate, patch, or invoke arbitrary native ECS systems.

See the [user guide](../../docs/user/README.md) and [developer guide](../../docs/developer/README.md) for command and schema details.
