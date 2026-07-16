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
   - Copy `config/BattleLuck/` to `<VRisingServer>/BepInEx/config/BattleLuck/`
4. **Restart the V Rising server**

### Thunderstore Installation (Recommended)

Players using Thunderstore can install BattleLuck directly:

1. Open Thunderstore mod manager
2. Search for "BattleLuck"
3. Install with dependencies (BepInEx, VampireCommandFramework)

## Configuration

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

By default, BattleLuck is **local-only** and AI is optional:

```json
{
  "enabled": false,
  "provider": "llama",
  "llama_api": {
    "enabled": false,
    "base_url": "http://127.0.0.1:11434",
    "model": "llama2"
  }
}
```

To enable AI:
1. Start a local LLM endpoint (see [LLM Guide](../LLM_GUIDE.md))
2. Set `"enabled": true` in `ai_config.json`
3. Restart the server

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

### Primary AI Commands

| Command | Description |
|---------|-------------|
| `.ai <message>` | Chat with the AI assistant or request a change |
| `.aistatus` | Check AI assistant status and settings |
| `.ai catalog search <query>` | Search verified action catalog |
| `.ai event request <change>` | Draft an event or mod edit |
| `.ai event preview <id>` | Preview a proposed edit |
| `.ai event approve <id>` | Apply an approved edit |
| `.ai event rollback <id>` | Roll back a supported operation |
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
| `.swapteam.ai [options]` | NPC-directed team AI — coming soon; currently announces swaps (admin) |
| `.schematic.list` | List loaded schematics |

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
