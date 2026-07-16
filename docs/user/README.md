# User Guide

BattleLuck is a V Rising dedicated-server plugin with action-driven events, native ECS-backed rollback snapshots, and optional local LLM assistance. LLM requests run asynchronously; approved game-state changes are dispatched back to the server main thread.

## Installing BattleLuck

### Prerequisites

- V Rising dedicated server
- [BepInEx 5/6](https://thunderstore.io/c/v-rising/p/BepInEx/BepInExPack_V_Rising/) installed on the server
- .NET 6 runtime (included with modern Windows)

### Installation Steps

1. **Install BepInEx** using the Thunderstore package or manual installation guide
2. **Build BattleLuck:**
   ```powershell
   dotnet build BattleLuck.sln -c Release
   ```
3. **Copy files to server:**
   - Copy `bin/Release/net6.0/BattleLuck.dll` to `<VRisingServer>/BepInEx/plugins/`
   - Copying `config/BattleLuck/` is optional when using the DLL defaults; the
     first load extracts missing files automatically.
4. **Restart the V Rising server**. The DLL creates
   `<VRisingServer>/BepInEx/config/BattleLuck/` and its `tools/` subdirectory,
   without overwriting existing server files.

### Thunderstore Installation (Recommended)

Players using Thunderstore can install BattleLuck directly:

1. Open Thunderstore mod manager
2. Search for "BattleLuck"
3. Install with dependencies (BepInEx, VampireCommandFramework)

## Configuration

### AI for Server Owners and Players

BattleLuck's AI runs on the dedicated server, so players do not need a separate
AI installation. The server owner configures `BepInEx/config/BattleLuck/ai_config.json`:

- Local Llama/Ollama-compatible inference is the recommended private setup.
- Cloud providers are optional and require the owner's credentials.
- Without a reachable provider, a simple local fallback still handles basic help
  and catalog guidance.
- `.ai <message>` is available to players as advice-only chat.
- Admin previews, approvals, and the server main-thread dispatcher protect live
  event/NPC/world mutations.

The server owner may optionally enable per-player AI chat backups in
`ai_config.json`. On Windows they default to
`%USERPROFILE%\AppData\LocalLow\Stunlock Studios\VRising\BattleLuck\chat-backups\`;
players do not install a backup client and the plugin does not write to their
local game folders.

Use `.aistatus` to inspect provider health. Admins can reload configuration with
`.ai.reload`. Conversation history is off by default.

### Session Config

Each game mode has its own configuration folder under `BepInEx/config/BattleLuck/`:

```
BepInEx/config/BattleLuck/
├── bloodbath/
│   ├── session.json    # Flow actions for mode entry/exit
│   ├── zones.json      # Zone definitions
│   └── kit.json        # Equipment loadouts
├── colosseum/
├── siege/
├── trials/
├── aievent/
├── ai_config.json      # AI assistant settings
├── discord_bridge.json # Discord integration
├── webhook.json        # Webhook listener
└── special_item.json   # Special item transformations
```

### AI Configuration

BattleLuck is **local-first**. The default profile points at a local endpoint and
falls back to built-in guidance when that endpoint is not reachable. No hosted
credentials are required for installation:

```json
{
  "enabled": true,
  "provider": "llama",
  "llama_api": {
    "enabled": true,
    "base_url": "http://127.0.0.1:11434",
    "model": "llama2:latest"
  }
}
```

For full LLM responses, start a local endpoint and install the configured model
(see [LLM Guide](../LLM_GUIDE.md)), then run `.ai.reload`. To disable the AI
surface completely, set `"enabled": false` and reload the server configuration.

## Game Modes

| Mode | Type | Description |
|------|------|-------------|
| **Bloodbath** | Free-for-all PvP | Last player standing wins |
| **Colosseum** | Duel/ELO | Ranked 1v1 and team matches |
| **Gauntlet** | PvE Wave | Survive increasing enemy waves |
| **Siege** | Objective | Capture and hold objectives |
| **Trials** | Timed PvE | Complete objectives within time limit |
| **AI Event** | Test | Deterministic AI flow testing |

## Commands

`.ai` is the primary BattleLuck command. All commands use the `.` prefix, and
`.help` shows the live permission-aware list. Event, NPC, boss, roadmap, schematic,
and reload commands are optional admin tools and are only available when the
corresponding feature is enabled.

Process: public `.ai` chat is advice-only and allows up to four AI replies per
interactive conversation; use `.ai end` to stop it early. Admin live changes
follow catalog/search → preview → approval → runtime execution. `.ai history`
shows only transient items from the last 24 hours. `.ai tasks <goal>` uses the
catalog-backed planner and stores a proposal without executing it. Rollback
discards pending proposals; it does not reverse an action that already executed.

### Primary AI Commands

| Command | Description |
|---------|-------------|
| `.ai <message>` | Public advice/chat; no direct mutation |
| `.ai end` | End the current four-reply AI conversation |
| `.ai history [items]` | Show transient AI history from the last 24 hours |
| `.ai tasks` | List recent planner tasks |
| `.ai tasks <goal>` | Create a catalog-backed planner task (admin; preview only) |
| `.aistatus` | Public provider/runtime status (read-only) |
| `.ai catalog search <query>` | Search verified action catalog (admin) |
| `.ai action <catalog action>` | Preview a runtime action (admin) |
| `.ai create <eventId> [templateId]` | Clone an event; defaults to Bloodbath (admin) |
| `.ai event request <change>` | Draft a validated event edit (admin) |
| `.ai event review <mode>` | Review an event without writing files (admin) |
| `.ai event preview <id>` | Preview a proposed edit (admin) |
| `.ai event approve <id>` | Apply an approved edit (admin) |
| `.ai approve [id]` | Execute an approved live action (admin) |
| `.ai event rollback <id>` | Roll back a pending event proposal (admin) |
| `.ai rollback [id]` | Discard a pending live-action proposal (admin) |
| `.ai.reload` | Reload AI configuration (admin) |
| `.ai.status` | Check detailed AI provider status (admin) |

### Optional Admin Commands

| Command | Description |
|---------|-------------|
| `.reload` | Reload configuration |
| `.event.create <eventId> [templateId]` | Clone Bloodbath (or another event) into a custom event |
| `.event.start <mode>` | Start a game mode |
| `.event.end <mode>` | End a game mode |
| `.event.status` | Show active events and player counts |
| `.modelist` | List registered game modes |
| `.bstatus` | Show live BattleLuck status |
| `.roadmap.status` | Show roadmap milestones |
| `.npc.near [radius] [limit]` | List nearby controlled NPCs |
| `.npc.spawn <prefab> [count]` | Spawn controlled NPCs |
| `.npc.follow <npcId> [target]` | Make an NPC follow a target |
| `.npc.goto <npcId> [x y z]` | Move a controlled NPC |
| `.npc.despawn <npcId\|all>` | Despawn controlled NPCs |
| `.swapteam.ai [options]` | Balance teams and announce with AI; NPC AI coming soon (admin) |
| `.schematic.list` | List loaded schematics |

Developer-only AI tools include `.ai.sequence.gather`, `.ai.sequence.create`,
`.ai.sequence.preview`, and `.ai.sequence.execute`. Sequence steps can use
`wait:<seconds>` and `tick:<event-second>` markers and are scheduled by the
server event tick after catalog validation.

### Optional Player Event Commands

| Command | Description |
|---------|-------------|
| `.score` | Show current score |
| `.elo` | Show ELO rating |
| `.exit` | Exit current mode |

## Discord Integration

Enable in `discord_bridge.json`:

```json
{
  "enabled": true,
  "port": 8080,
  "channel_id": "your-discord-channel-id"
}
```

The bridge supports slash commands and event relay to Discord channels.

## Troubleshooting

### Mod doesn't load

- Check `BepInEx/LogOutput.log` for errors
- Verify `BattleLuck.dll` is in `BepInEx/plugins/`
- Ensure VCF dependency is installed and loads before BattleLuck

### Commands not registering

- Verify BepInEx version compatibility
- Check server console for plugin load errors
- Ensure `.reload` runs without errors

### AI not responding

- Start local LLM endpoint with `scripts/start_vllm.ps1`
- Check `ai_config.json` base_url and model settings
- Run `.ai.status` to verify connection

## Support

- **Discord**: [V Rising Mod Community](https://vrisingmods.com/discord)
- **Wiki**: [V Rising Mod Wiki](https://wiki.vrisingmods.com/)
- **Issues**: [GitHub Issues](https://github.com/usstunlab1/Battleluck-/issues)
