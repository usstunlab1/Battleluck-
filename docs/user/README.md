# User Guide

BattleLuck is a V Rising dedicated-server plugin with action-driven events, rollback-safe player state, NPC/boss control, and optional local AI assistance.

## Configuration

```
BepInEx/config/BattleLuck/
├── events/<eventId>/          # Event definitions
│   ├── event.json             # Action phases and timers
│   ├── zones.json             # Zone centers and radii
│   ├── kits.json              # Equipment loadouts
│   └── prompt.txt             # AI prompt for this event
├── actions_catalog.json       # Action source of truth
├── ai_config.json             # AI provider settings
├── discord_bridge.json        # Discord integration
├── webhook.json               # Webhook listener
├── special_item.json          # Special item transformations
└── schematics/                # Arena schematic configs
```

The DLL extracts defaults on first boot and never overwrites existing files.

### AI Configuration

```json
{
  "enabled": true,
  "provider": "llama",
  "llama_api": {
    "base_url": "http://127.0.0.1:11434",
    "model": "llama2:latest"
  }
}
```

Local Llama/Ollama is the recommended private setup. Cloud providers are optional. Without a reachable provider, a local fallback handles basic catalog guidance.

## Commands

All commands use the `.` prefix in chat. `[A]` = admin-only. `.help` shows the live permission-aware list.

### AI Commands

| Command | Description |
|---------|-------------|
| `.ai <message>` | Advice-only chat; cannot mutate state (public) |
| `.ai end` | End the 4-reply AI conversation |
| `.ai history [items]` | Show transient AI history (last 24h) |
| `.ai tasks` | List recent planner tasks |
| `.ai tasks <goal>` | Create a catalog-backed plan (admin, preview only) |
| `.aistatus` | Provider/runtime status (public, read-only) |
| `.ai catalog search <text>` | Search the verified action catalog [A] |
| `.ai action <action>` | Preview one runtime action [A] |
| `.ai create <eventId> [template]` | Clone an event from template [A] |
| `.ai event deploy <id> <gist-url>` | Stage, validate, backup, register [A] |
| `.ai event deploy <id> <gist-url> --dry-run` | Validate without writing [A] |
| `.ai event status [id]` | Deployment status (public) |
| `.ai event audit [id]` | Deployment audit summary [A] |
| `.ai event review <mode>` | Review event without writing files [A] |
| `.ai event request <change>` | Draft a validated event edit [A] |
| `.ai event preview <id>` | Inspect a pending proposal [A] |
| `.ai event approve <id>` | Apply an approved proposal [A] |
| `.ai event rollback <id>` | Restore latest known-good event [A] |
| `.ai rollback player <name> <ts>` | Restore one player snapshot [A] |
| `.ai rollback server status` | Count online/offline snapshots [A] |
| `.ai rollback server players confirm` | Restore all online snapshots [A] |
| `.ai rollback server purge <id> [backup] confirm` | Delete a backup [A] |
| `.ai.reload` | Reload AI configuration [A] |
| `.ai.status` | Detailed provider status [A] |
| `.aiadmin` | AI diagnostics, reload, recover, permissions [A] |

### Session & Events

| Command | Description |
|---------|-------------|
| `.toggleenter [mode]` | Enter a zone session |
| `.toggleleave` | Leave current session and restore state |
| `.exit` | Force exit current session |
| `.start` | Force-start prepared session [A] |
| `.pause` / `.resume` | Pause or resume session |
| `.autoend` | Auto-end session |
| `.setwinner` | Set winner and end session |
| `.rollback <opId>` | Discard pending AI proposal [A] |
| `.event.create <id> [template]` | Clone event from template [A] |
| `.event.start <mode> [force=true]` | Start event [A] |
| `.event.end <mode>` | End event sessions [A] |
| `.event.endall` | End ALL active sessions [A] |
| `.event.status` | Active events and player counts [A] |
| `.event.forceenter <mode> <steamId>` | Force player into event [A] |
| `.event.forceexit` | Force player out of event [A] |
| `.event.clearburning` | Remove burning penalty [A] |

### NPC & Boss

| Command | Description |
|---------|-------------|
| `.npc.spawn <prefab> [count]` | Spawn controlled NPCs [A] |
| `.npc.despawn <id\|all>` | Despawn NPCs [A] |
| `.npc.follow <id> [target]` | NPC follow target [A] |
| `.npc.goto <id> [x y z]` | Move NPC [A] |
| `.npc.goto.pos <id>` | Move NPC to your position [A] |
| `.npc.hold` / `.npc.stay` | NPC hold position [A] |
| `.npc.patrol <id> <waypoints>` | Set patrol route [A] |
| `.npc.guard <id> <x,y,z>` | Set guard post [A] |
| `.npc.flee` / `.npc.wander` | Flee or wander behavior [A] |
| `.npc.aggro <id> [target]` | NPC aggro target [A] |
| `.npc.near [radius]` | List nearby NPCs [A] |
| `.npc.status` | NPC control status [A] |
| `.npc.buffs` / `.npc.components` | Inspect NPC [A] |
| `.npc.rename` / `.npc.team` / `.npc.faction` / `.npc.speed` | Configure NPC [A] |
| `.boss.spawn <prefab> [id]` | Spawn controlled boss [A] |
| `.boss.list` | List controlled bosses [A] |
| `.boss.despawn` / `.boss.despawn_all` | Despawn boss(es) [A] |
| `.boss.follow_target` / `.boss.clear_follow` | Boss follow control [A] |
| `.boss.goto` / `.boss.goto.pos` / `.boss.return_home` | Boss movement [A] |

### Player & Combat

| Command | Description |
|---------|-------------|
| `.heal` | Heal to full [A] |
| `.blood <type> [quality]` | Set blood type [A] |
| `.level.max` | Set max level [A] |
| `.stun [duration]` | Stun player [A] |
| `.pvp.enable` / `.pvp.disable` | Toggle PvP [A] |
| `.death.prevent` / `.death.allow` | Death prevention [A] |
| `.revive.grant <lives>` / `.revive.reset` | Manage revive lives [A] |
| `.swapteam [closest\|balance]` | Balance teams [A] |
| `.swapteam.ai [options]` | Balance + AI announce [A] |
| `.kick` | Kick player [A] |
| `.tp` / `.tp.dev` / `.tp.zone` | Teleport [A] |

### Kit & Inventory

| Command | Description |
|---------|-------------|
| `.kit <kitId>` | Apply kit loadout [A] |
| `.kit.weapons` / `.kit.armor` / `.kit.clear` | Kit variants [A] |
| `.equip.restrict` / `.equip.unrestrict` | Gear restrictions [A] |
| `.ability.slot <slot> <prefab>` | Set ability slot [A] |
| `.pull` / `.stash` / `.sort` / `.salvage` | Inventory ops [A] |
| `.craftpull` / `.emptytrash` / `.autotrash` | Inventory ops [A] |

### Buffs & Sequences

| Command | Description |
|---------|-------------|
| `.buff.apply <prefab> [duration]` | Apply buff [A] |
| `.buff.remove <prefab>` / `.buff.clear` | Remove buffs [A] |
| `.zone.buff.apply` / `.zone.buff.remove` | Zone-wide buffs [A] |
| `.sequence.play <prefab> <x> <y> <z>` | Play VFX sequence [A] |
| `.sequence.stop <prefab>` | Stop sequence [A] |
| `.action <actionString>` | Run any action [A] |
| `.actions` / `.actions.status` | List actions [A] |

### Schematics & Building

| Command | Description |
|---------|-------------|
| `.schematic.load <name>` | Load schematic at position [A] |
| `.schematic.loadat <name> <x y z>` | Load at coordinates [A] |
| `.schematic.capture <name>` | Capture nearby design [A] |
| `.schematic.list` / `.schematic.info` | Inspect schematics [A] |
| `.schematic.clear` / `.schematic.clear.radius` | Clear schematics [A] |
| `.build.search <filter>` | Search build pieces [A] |
| `.findtiles` / `.grid` | Tile and grid tools [A] |
| Shortcuts: `.sc.i` `.sc.l` `.sc.la` `.sc.lp` | Schematic aliases [A] |

### Castle Policy

| Command | Description |
|---------|-------------|
| `.castlepolicy` | Manage castle policies [A] |
| `.castlepolicy.target <id> <kind>` | Bind policy to object [A] |
| `.castlepolicy.public` / `.private` | Set access level |
| `.castlepolicy.allow` / `.deny` | Player access [A] |
| `.castlepolicy.territory.apply <level> confirm` | Territory-wide apply [A] |

### Other Commands

| Command | Description |
|---------|-------------|
| `.score` / `.score.add` / `.score.reset` | Scoreboard [A] |
| `.elo` | ELO ratings |
| `.modelist` / `.modeinfo` / `.modepolicy` | Mode info [A] |
| `.teamcreate` / `.teaminvite` / `.teamaccept` / `.teamleave` / `.teamlist` | Teams |
| `.merchant.run` / `.merchant.list` / `.merchant.reload` | Merchant [A] |
| `.activityclan` (`.ac`) | View clan tasks |
| `.snapshot.save` / `.snapshot.restore` | State snapshots [A] |
| `.bstatus` / `.director` | Runtime status views [A] |
| `.zoneinfo` | Zone stats [A] |
| `.reload` | Reload all config [A] |
| `.help` | Show available commands |
| `.mutatorenable` / `.mutatordisable` / `.mutatorlist` / `.mutatorclear` | Mutators [A] |
| `.roadmap.show` / `.roadmap.status` / `.roadmap.prompt` | Roadmap [A] |
| `.exportmods` / `.exportplugins` / `.exportprefabs` | Data export [A] |

## Event Creation

Admins can create a complete editable event without C# code:

```text
.event.create shadow_hunt bloodbath
```

This creates `config/BattleLuck/events/shadow_hunt.json` with supporting files in `events/shadow_hunt/` (`zones.json`, `kits.json`, and `prompt.txt`). Customize the files, then:

```text
.event.start shadow_hunt
```

### Safety

- Back up the server before starting custom events.
- Run `.ai event review <eventId>` to validate before inviting players.
- Invalid JSON, prefabs, or actions may crash the server.
- Test in a private arena first.

### AI Recovery

- AI operations are logged to `ai_operations.log`.
- Player snapshots are saved to `data/BattleLuck/snapshots/<steamId>.json`.
- Normal exit restores automatically; hard crashes may require manual restore.
- Use `.ai rollback player <name> <timestamp>` after a crash.

## Troubleshooting

| Problem | Fix |
|---------|-----|
| Mod doesn't load | Check `BepInEx/LogOutput.log`; verify DLL is in `plugins/` |
| Commands not registering | Verify VCF dependency; check console for load errors |
| AI not responding | Start local endpoint; check `ai_config.json`; run `.ai.status` |
| Server crash after event | Restore from backup; check event JSON for invalid prefabs/actions |
