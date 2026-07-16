# BattleLuck open-source and self-hosted operations

BattleLuck is designed for a V Rising dedicated server that you control. The
plugin does not require a BattleLuck SaaS account, remote orchestration service,
or bundled model weights. The server owner chooses which integrations to enable.

## 🌟 Featured projects

| Project | BattleLuck role | Required? |
|---|---|---:|
| [BepInEx](https://github.com/BepInEx/BepInEx) | Loads the V Rising server plugin | Yes |
| [Harmony](https://github.com/pardeike/Harmony) | Applies compatibility-safe runtime patches | Bundled dependency |
| [VampireCommandFramework](https://thunderstore.io/c/v-rising/p/deca/VampireCommandFramework/) | Registers `.ai`, event, NPC, and player commands | Yes |
| [Ollama](https://github.com/ollama/ollama) | Local HTTP LLM endpoint for private AI replies | Optional |
| [llama.cpp](https://github.com/ggml-org/llama.cpp) | Alternative local inference server | Optional |
| [Docker Compose](https://docs.docker.com/compose/) | Runs the repository's optional Ollama stack | Optional |

BattleLuck-specific capabilities remain in this repository: declarative V Rising
events, the verified action catalog, native-backed rollback snapshots, tick and
sequence scheduling, NPC control, and approval-gated AI operations.

## ☁️ Multi-cloud and on-premise

The plugin has no cloud-provider SDK or required hosted control plane. The same
release can run on a Windows or Linux V Rising dedicated server in any cloud or
on your own hardware. Moving environments normally requires only deployment path,
firewall, storage-backup, and optional AI-provider credential changes—not a code
change to BattleLuck.

Portable operational files live under `BepInEx/config/BattleLuck/`, including event
definitions, action catalogs, custom sequences, and provider settings. Treat
snapshots, logs, credentials, and player data as private server state and move
them only through your protected backup process.

## ⚔️ Install from the same repository

Install BepInEx and VampireCommandFramework on the V Rising server first, then
build BattleLuck from a clean checkout:

```powershell
git clone https://github.com/usstunlab1/Battleluck.git
Set-Location Battleluck
dotnet restore
dotnet build .\BattleLuck.sln -c Release /p:DeployBattleLuck=false
```

Deploy to a specific server directory:

```powershell
dotnet build .\BattleLuck.sln -c Release `
  /p:DeployBattleLuck=true `
  /p:ServerPluginPath="C:\Path\to\VRising_Server\BepInEx\plugins\BattleLuck" `
  /p:ServerConfigPath="C:\Path\to\VRising_Server\BepInEx\config\BattleLuck"
```

The deploy target copies `BattleLuck.dll` and the `config/BattleLuck/` files. Stop
the server before replacing a loaded plugin, then start it and run `.help`.

## 🧠 Optional private AI

The repository includes a local Ollama Compose profile:

```powershell
docker compose -f docker-compose.ai.yml up -d
docker compose -f docker-compose.ai.yml logs -f llama2-init
```

The default endpoint is `http://127.0.0.1:11434`. Configure
`BepInEx/config/BattleLuck/ai_config.json`, then run `.ai.reload` and
`.aistatus`. If no provider is reachable, BattleLuck keeps its deterministic
local fallback for basic help and catalog guidance. Players do not need to run
Ollama or install a model; only the server owner does.

Hosted providers, Discord, MCP, and sidecars are opt-in. Keep credentials outside
the repository and do not publish model files, player snapshots, or AI logs.

## 🧩 Game adapter roadmap

| Adapter | Status | Scope |
|---|---|---|
| V Rising / ProjectM / Unity ECS | **Shipped** | Current plugin, commands, events, NPCs, sequences, and rollback |
| Other Unity ECS/BepInEx games | **Planned** | Reuse catalog, planner, approval, and task contracts through a game adapter |
| Unreal Engine games | **Exploratory** | Requires a separate native/game integration; no runtime is shipped |
| Source/other engines | **Exploratory** | Roadmap only; no compatibility claim |

Only the V Rising adapter is supported today. Future adapters must provide their
own entity, tick, sequence, and rollback implementations before they can be
listed as supported.

## Related docs

- [User guide](user/README.md)
- [LLM guide](LLM_GUIDE.md)
- [Deployment guide](deployments/README.md)
- [Developer guide](developer/README.md)
