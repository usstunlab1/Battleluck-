# BattleLuck

![BattleLuck roadmap](docs/assets/roadmap-header.png)

BattleLuck is a server-side BepInEx IL2CPP plugin for V Rising dedicated servers. It provides configurable competitive and cooperative game events, managed player sessions, rollback-safe player state and loadouts, NPC and boss control, progression and death-prevention systems, teleports, zones, schematics, and an ECS-backed action pipeline.

Event behavior is defined through configuration and validated before execution. Optional local AI tools can assist with verified action discovery, event authoring, runtime announcements, and approval-gated configuration changes. Network-based AI work runs asynchronously, while ProjectM and Unity ECS mutations are dispatched to the server main thread.

## Install

1. Install BepInEx for V Rising on the dedicated server.
2. Install the package with a Thunderstore-compatible mod manager, or copy the package files into the server's `BepInEx` folder.
3. Start the server once, then edit files under `BepInEx/config/BattleLuck/`, including event definitions inside `BepInEx/config/BattleLuck/events/<eventId>/`.
4. Use `.help` in game to see the commands available to your permission level.

AI is optional and local-first. It is disabled until a server owner configures a provider and explicitly enables the requested features.

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

```text
.help                          Show available BattleLuck commands
.ai <message>                  Ask the AI assistant or request an event/mod change
.aistatus                      Show local AI status
.ai catalog search <text>      Search verified actions and data
.ai event request <change>     Draft an event or mod edit for review
.ai event preview <id>         Preview a proposed edit
.ai event approve <id>         Apply an approved edit
.ai event rollback <id>        Roll back a supported operation
.ai.status                     Show detailed AI provider status (admin)
.ai.reload                     Reload AI configuration (admin)
```

Live AI changes remain preview-first and approval-gated. Use `.help` to discover
the full `.ai` subcommand surface for the installed configuration.

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
.swapteam.ai [options]         NPC-directed team AI — coming soon (admin)
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
