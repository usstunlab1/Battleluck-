# BattleLuck

![BattleLuck roadmap](https://raw.githubusercontent.com/usstunlab1/Battleluck-/v1.0.2/docs/assets/roadmap-header.png)

![BattleLuck AI prompt pipeline](https://raw.githubusercontent.com/usstunlab1/Battleluck-/v1.0.2/docs/assets/prompt-pipeline-header.png)

BattleLuck is a server-side BepInEx plugin for V Rising. Its game-event actions run through the native ECS/action pipeline, player state is saved as rollback snapshots, and optional local LLM tools can propose verified event and mod changes. LLM network work runs asynchronously; approved native-world mutations are queued onto the server main thread for safe execution.

## Install

1. Install BepInEx for V Rising on the dedicated server.
2. Install the package with a Thunderstore-compatible mod manager, or copy the package files into the server's `BepInEx` folder.
3. Start the server once, then edit `BepInEx/config/BattleLuck/*.json`.
4. Use `.help` in game to see the commands available to your permission level.

AI is optional and local-first. It is disabled until a server owner configures a provider and explicitly enables the requested features.

## Included features

- Match-ready, action-driven event flow for arena and custom V Rising events.
- NPC control, boss commands, generic actions, and safe action reachability checks.
- Player event sessions, loadouts, progression, death-prevention charges, native-backed rollback snapshots, and restore-on-exit flows.
- Teleport services, spatial points, borders, schematics, and verified data catalogs.
- Optional local LLM prompts for event and mod authoring with approval gates and main-thread-safe execution.

## Commands

All commands use the `.` prefix. Player commands are available to everyone; admin commands require server permissions.

### Player commands

```text
.help                          Show available BattleLuck commands
.toggleenter [modeName]        Join an event zone
.toggleleave                   Leave an event cleanly and restore your state
.exit                          Force-exit the current event
.score                         Show the current scoreboard
.elo                           Show Colosseum rating
.ai <message>                  Ask the optional AI assistant
.aistatus                      Show local AI status
```

### Admin commands

```text
.reload                        Reload BattleLuck configuration
.event.start <mode>            Start and enter an event mode
.event.end <mode>              End a mode's active sessions
.event.status                  Show active events and player counts
.modelist                      List registered modes
.bstatus                       Show live BattleLuck runtime status
.npc.near [radius] [limit]      List nearby controlled NPCs
.npc.spawn <prefab> [count]     Spawn controlled NPCs
.npc.follow <npcId> [target]    Make an NPC follow a target
.npc.goto <npcId> [x y z]       Move an NPC
.npc.despawn <npcId|all>        Despawn controlled NPCs
.boss.spawn <prefab> [id]       Spawn a controlled boss/NPC
.boss.list                      List controlled bosses/NPCs
.ai.reload                      Reload AI configuration
.ai.status                      Show detailed AI provider status
.ai catalog search <text>       Search verified actions and data
.ai event request <change>      Draft an event or mod edit for review
.ai event preview <id>          Preview a proposed edit
.ai event approve <id>          Apply an approved edit
.roadmap.status                 Show roadmap milestones
.roadmap.prompt <llm|developer> Show the active prompt contract
.schematic.list                 List loaded arena schematics
.schematic.capture <name>       Capture a nearby schematic
```

Live AI changes remain preview-first and approval-gated. Use `.ai event rollback <operationId>` when a supported operation needs to be reverted.

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
