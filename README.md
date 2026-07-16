# BattleLuck

![BattleLuck roadmap](docs/assets/roadmap-header.png)

BattleLuck is a server-side BepInEx IL2CPP plugin for V Rising dedicated servers. It provides configurable competitive and cooperative game events, managed player sessions, rollback-safe player state and loadouts, NPC and boss control, progression and death-prevention systems, teleports, zones, schematics, and an ECS-backed action pipeline.

Event behavior is defined through configuration and validated before execution. Optional local AI tools can assist with verified action discovery, event authoring, runtime announcements, and approval-gated configuration changes. Network-based AI work runs asynchronously, while ProjectM and Unity ECS mutations are dispatched to the server main thread.

## Install

1. Install BepInEx for V Rising on the dedicated server.
2. Install the package with a Thunderstore-compatible mod manager, or copy the package files into the server's `BepInEx` folder.
3. Start the server once, then edit files under `BepInEx/config/BattleLuck/`, including event definitions inside `BepInEx/config/BattleLuck/events/<eventId>/`.
4. Use `.help` in game to see the commands available to your permission level.

AI is server-owned and local-first. Every installation includes the `.ai` command
surface, but no model weights or provider credentials are bundled. A server owner
can run a local Llama/Ollama-compatible endpoint or explicitly configure a hosted
provider; without one, BattleLuck uses its simple local fallback for basic help and
catalog guidance. Live actions and event edits still require admin approval.

### How AI works for every installation

1. Install BattleLuck on the dedicated server; players do not need a separate AI
   client or model download.
2. The server reads `BepInEx/config/BattleLuck/ai_config.json` and starts the
   configured provider or local fallback. The owner can run `.ai.status` or
   `.aistatus` to see the active provider and health.
3. Players can use `.ai <question>` for server-side advice. Player chat is
   advice-only and cannot change events, NPCs, inventory, or world state.
4. Admins use catalog search and preview commands. Only an explicit approval sends
   a validated operation to the server main-thread runtime.
5. Hosted AI, Discord, MCP, and sidecar integrations are opt-in. If enabled, the
   configured provider receives the prompt; conversation history remains off by
   default and no credentials are stored in the mod package.

## 📣 Coming soon — next week

🚧 **BattleLuck AI Chat channel** is planned for next week. It will be a new,
dedicated channel in the existing BattleLuck chat/server, so players can keep AI
questions and responses separate from match play without joining a second server
or installing another mod. It will use the same `.ai` permissions, provider, and
approval pipeline already described above.

The channel remains part of the same chat integration, but uses a separate
route so messages can be filtered independently. Planned message markers start
each message and make the channel easy to scan:

- `📣 [ANNOUNCE]` — server and event notices.
- `🤖 [AI]` — questions, guidance, and approved AI previews.
- `⚔️ [EVENT]` — match starts, phases, and results.
- `🛡️ [ADMIN]` — approval and rollback status (admin-only details).

This is a roadmap item, not a current command or configuration option. Until the
channel ships, use `.ai` in the normal game chat and `.aistatus` for provider
health.

## Included features

- Match-ready, action-driven event flow for arena and custom V Rising events.
- NPC control, boss commands, generic actions, and safe action reachability checks.
- Player event sessions, loadouts, progression, death-prevention charges, native-backed rollback snapshots, and restore-on-exit flows.
- Teleport services, spatial points, borders, schematics, and verified data catalogs.
- Optional local LLM prompts for event and mod authoring with approval gates and main-thread-safe execution.

## Commands

`.ai` is the primary BattleLuck interface. All commands use the `.` prefix, and
`.help` is the live permission-aware source of truth. Other commands are optional,
admin-only tools for servers that enable the corresponding event, NPC, schematic,
roadmap, or integration features.

### Primary AI commands

Permission labels: **public** commands are read-only/advice-oriented;
**admin** commands create previews or perform approved runtime/config operations.

```text
.help                          Show available commands [public]
.ai <message>                  Ask for advice; player chat cannot mutate state [public]
.aistatus                      Show provider/runtime status [public, read-only]
.ai catalog search <text>      Search the verified action catalog [admin]
.ai action <catalog action>    Preview one runtime action [admin]
.ai create <eventId> [template] Clone an event, default Bloodbath [admin]
.ai event request <change>     Draft a validated event edit [admin]
.ai event review <mode>        Review an event without writing files [admin]
.ai event preview <id>         Inspect a pending proposal [admin]
.ai event approve <id>         Apply an approved event proposal [admin]
.ai approve [id]               Execute an approved live-action proposal [admin]
.ai event rollback <id>        Roll back a pending event proposal [admin]
.ai rollback [id]              Discard a pending live-action proposal [admin]
.ai.status                     Show detailed AI provider status [admin]
.ai.reload                     Reload AI configuration [admin]
```

Process: search the catalog, request a preview, inspect the operation id, approve
it, then let the server execute approved actions through its main-thread pipeline.
Rollback/discard only applies to pending proposals; it cannot undo an action that
already ran. Use `.help` to discover the full `.ai` surface for the installation.

### Action catalog and system reachability

`config/BattleLuck/actions_catalog.json` is the action source of truth. It contains
registered names, metadata, parameters, examples, categories, and reusable
sequences; the runtime also adds verified aliases from the live system registry.
Use `.ai catalog search <text>` before proposing an action. Actions may come from
any catalog category or verified ProjectM/Unity system reference, but every action
must have a runtime handler, pass validation, and remain approval-gated. A
`system.*` alias records a verified system reference; it does not invoke arbitrary
native ECS code through reflection.

The current catalog includes 202 registered action names, 34 example categories,
and built-in sequence definitions. The live catalog/search result is authoritative
when the server is running.

### Developer ticks and sequences

Developers can build reusable action sequences from catalog actions. Sequence steps
may include `wait:<seconds>` and `tick:<event-second>` markers; the unified event
runtime schedules them on the session clock. The server tick drains queued AI and
ECS work on the main thread, and `runtime_inject` entries are checked on the next
event tick.

```text
.ai.sequence.gather <name> <search text>                 [admin]
.ai.sequence.create <name> <action; wait:5; tick:30; action> [admin]
.ai.sequence.preview <name> <steps>                      [admin]
.ai.sequence.execute <name>                              [admin]
```

Use `.ai.sequence.show/list/add/delete` to maintain named sequences, then invoke a
validated sequence with `sequence.custom.play:sequenceId=<id>|schedule=true` from
an event phase, timer, trigger, or approved live action.

### Optional event and server commands

These are not required for the primary AI workflow and may be unavailable to
non-admins or when a feature is disabled:

```text
.toggleenter [modeName]        Join an event zone
.toggleleave                   Leave an event and restore your state
.exit                          Force-exit the current event
.score                         Show the current scoreboard
.elo                           Show Colosseum rating
.reload                        Reload all BattleLuck configuration (admin)
.start                         Force-start a prepared event session (admin)
.rollback <operationId>        Roll back a pending operation (admin)
.swapteam [closest|balance]    Balance or move event teams (admin)
.swapteam.ai [options]         Balance teams + AI announcement; NPC AI coming soon (admin)
.event.create <eventId>        Clone Bloodbath into a custom event (admin)
.event.start <mode>            Start and enter an event mode (admin)
.event.end <mode>              End a mode's active sessions (admin)
.event.status                  Show active events and player counts (admin)
.modelist                      List registered modes (admin)
.bstatus                       Show live runtime status (admin)
.npc.near [radius] [limit]      List nearby controlled NPCs (admin)
.npc.spawn <prefab> [count]     Spawn controlled NPCs (admin)
.npc.follow <npcId> [target]    Make an NPC follow a target (admin)
.npc.goto <npcId> [x y z]       Move an NPC (admin)
.npc.despawn <npcId|all>        Despawn controlled NPCs (admin)
.boss.spawn <prefab> [id]       Spawn a controlled boss/NPC (admin)
.boss.list                      List controlled bosses/NPCs (admin)
.roadmap.status                 Show roadmap milestones (admin)
.roadmap.prompt <llm|developer> Show the active prompt contract (admin)
.schematic.list                 List loaded arena schematics (admin)
.schematic.capture <name>       Capture a nearby schematic (admin)
```

### Create your own Bloodbath-style event

Admins can create a complete editable event without adding C# code:

```text
.event.create shadow_hunt bloodbath
```

This creates `config/BattleLuck/events/shadow_hunt/` with its own `flow.json`, `zones.json`, `kits.json`, and `prompt.txt`, assigns a unique zone hash, and registers the event immediately. Customize the copied zone center, kit, actions, phases, timers, and prompt, then run `.event.start shadow_hunt`.

## Support

Maintainer: **coyoteq1 — Ahmadtllal**  
Discord support: <https://discord.gg/uJ2ehWv4gR>

## Documentation

- [User guide](docs/user/README.md)
- [Developer guide](docs/developer/README.md)
- [AI and prompt guide](docs/LLM_GUIDE.md)
- [Publishing checklist](docs/PUBLISHING_CHECKLIST.md)
- [V Rising Mod Wiki](https://wiki.vrisingmods.com/)
- [V Rising Mod Wiki: Thunderstore upload](https://wiki.vrisingmods.com/dev/upload_to_thunderstore.html)
- [V Rising Mod Wiki: licensing](https://wiki.vrisingmods.com/dev/licensing.html)

## License

BattleLuck is licensed under the GNU Affero General Public License, version 3 or any later version. See [LICENSE](LICENSE) and [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md).
