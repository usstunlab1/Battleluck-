# BattleLuck Developer AI Prompt — For AI Coding Assistants

This prompt is designed for AI coding assistants (Claude, Cursor, Copilot, etc.) working on the BattleLuck V Rising plugin C# codebase. It provides the architectural context, coding conventions, and safety rules needed to make correct changes.

---

## Project Overview

BattleLuck is a **BepInEx plugin** for V Rising (net6.0, Unity DOTS ECS) that adds competitive game modes, AI integration, Discord/Webhook/Lark bridges, and a config-driven event system.

| Aspect | Detail |
|---|---|
| **Language** | C# 10, net6.0 |
| **Framework** | BepInEx 5.x (Unity plugin loader) |
| **Game** | V Rising (Stunlock Studios) |
| **ECS** | Unity DOTS (EntityManager, EntityQuery, PrefabGUID) |
| **Patches** | Harmony 2.x (transpilers, postfixes) |
| **Build** | `dotnet build BattleLuck.sln` |
| **Test** | xUnit (minimal coverage — add tests for new code) |

---

## Architecture

The server-owned roadmap and prompt contracts live in `config/BattleLuck/roadmap.json` and `config/BattleLuck/prompts/`. `RoadmapService` supplies read-only roadmap context to the in-server LLM; developer operators can inspect it with `.roadmap.status`, `.roadmap.show`, and `.roadmap.prompt`. Keep roadmap edits reviewed and configuration-driven.

### Design Patterns
- **Plugin Architecture:** BepInEx mod with Harmony patches for hooking V Rising game events
- **Event-Driven:** `ProjectMEventRouter` broadcasts game events (death, action, mode changes)
- **ECS Integration:** Uses Unity DOTS `EntityManager` for efficient queries and mutations
- **Configuration-Driven:** Mode definitions, flows, and kits stored in JSON under `config/BattleLuck/`
- **Main Thread Dispatch:** Off-thread work (AI, HTTP) queued back to main thread via `MainThreadDispatcher`

### Core Components

| Component | File | Role |
|---|---|---|
| **BattleLuckPlugin** | `BattleLuckPlugin.cs` | Entry point; initializes all services (40+ static fields — refactor target) |
| **SessionController** | `Core/SessionController.cs` | Manages active game sessions & round state |
| **GameModeRegistry** | `Core/GameModeRegistry.cs` | Registry of 6 game modes (Bloodbath, Colosseum, Gauntlet, Siege, Trials, AIEvent) |
| **FlowActionExecutor** | `Services/Flow/FlowActionExecutor.cs` | Interprets action strings, applies kits, teleports, buffs |
| **AIAssistant** | `Services/AI/AIAssistant.cs` | Cloudflare/Google/Llama AI query handler (~1941 lines) |
| **NpcControlService** | `Services/Npc/` | Sole registry and behavior owner for NPCs, elites, VBloods, servants, companions, and waves |
| **SessionCleanupService** | `Services/Cleanup/` | Zone cleanup on session end |
| **Discord/Webhook/Lark Bridges** | `Services/Integrations/` | External HTTP endpoints |

### Key Namespaces
- `BattleLuck.Core` — Core systems (SessionController, RoundManager, ScoreTracker, etc.)
- `BattleLuck.Models` — All data models (config, events, kits, bosses, etc.)
- `BattleLuck.Services` — Business logic (AI, Flow, NPC, Cleanup, Spawn, Zone, etc.)
- `BattleLuck.Services.Runtime` — Runtime services (ActionManifestService, EventDefinitionLoader, etc.)
- `BattleLuck.Commands` — Admin/player chat commands
- `BattleLuck.Patches` — Harmony patches for game hooks
- `BattleLuck.ECS` — ECS system wrappers
- `BattleLuck.Events` — Event bus and gameplay event definitions
- `BattleLuck.Utilities` — Logging utilities

---

## Coding Conventions

### Naming
- **Classes:** PascalCase (`SessionController`, `FlowActionExecutor`)
- **Methods:** PascalCase (`Initialize()`, `ExecuteAction()`)
- **Local variables:** camelCase (`sessionId`, `playerCount`)
- **Private fields:** `_camelCase` with underscore prefix (`_initialized`, _playerQuery`)
- **Constants:** PascalCase (`MaxRetries`, `DefaultTimeout`)
- **Interfaces:** `I` prefix (`ISessionController`, `IAiService`)
- **Async methods:** `Async` suffix (`LoadAsync()`, `ExecuteAsync()`)

### Code Style
- Use `var` when type is obvious
- Prefer expression-bodied members for simple methods
- Use `StringBuilder` for string concatenation in loops
- Use `nameof()` for argument names in exceptions
- Use `is null` / `is not null` pattern matching (not `== null` / `!= null`)
- Use `JsonSerializer` / `JsonDocument` for JSON (not Newtonsoft.Json)
- Use `System.Text.Json` attributes: `[JsonPropertyName("name")]`

### Exception Handling
- **DO NOT** catch and only log `ex.Message` — this loses the stack trace
- **DO** log the full exception: `Log?.LogWarning($"[Component] Error: {ex}")` (calls `ex.ToString()`)
- Use specific exception types where possible
- Avoid empty catch blocks

### Thread Safety
- All game-state mutations must happen on the Unity main thread
- Use `MainThreadDispatcher.Enqueue(() => ...)` for off-thread work
- Be aware that `EntityManager` operations are not thread-safe
- Use `lock` statements sparingly and document the locking strategy
- Prefer `ConcurrentDictionary` / `ConcurrentQueue` for shared state

### Harmony Patches
- Use `[HarmonyPatch]` attributes for declarative patching
- Prefix patches for early interception, Postfix for observation
- Transpiler patches only when IL-level changes are needed
- Always check `__instance` and `__result` for null
- Log patch entry/exit in debug builds only

---

## AI Integration Architecture

### Prompt Pipeline
1. `AIAssistant.GetSystemPrompt()` loads `%CONFIG_PATH%/ai_operator_prompt.md`
2. `LoadOperatorPrompt()` performs string replacements:
   - `{prefabSample}` → top 40 prefab names from `Prefabs` class
   - `{buffSample}` → top 40 buff prefab names (prefixed with `Buff_`)
   - `{sequenceSample}` → top 40 sequence names from `ActionSequences` cache
   - `{maxActionsPerEvent}` → `EventAuthoringMaxActions` config value
   - `{actionsCatalogSummary}` → `LoadActionsCatalogSummary()` (categorized action list)
3. The assembled prompt is sent as a `ChatMessage.System()` message
4. Additional context messages are appended (player context, runtime state, etc.)

### Provider Architecture
- `BaseAiService` — Abstract base with HTTP client, rate limiting, credential validation
- `LlamaAIService` — Ollama/Llama API via `/api/chat` endpoint
- `CloudflareAiService` — Cloudflare Workers AI (currently stub — returns null)
- `GoogleAIService` — Google AI Studio via `generateContent` endpoint
- `BattleAiSidecarService` — External sidecar service for complex AI operations
- `MCPRuntimeService` — Embedded MCP runtime for tool execution

### Key AI Files
| File | Purpose |
|---|---|
| `Services/AI/AIAssistant.cs` | Central orchestrator (~1941 lines) |
| `Services/AI/BaseAiService.cs` | Abstract base for AI providers |
| `Services/AI/LlamaAIService.cs` | Ollama/Llama client |
| `Services/AI/CloudflareAiService.cs` | Cloudflare client (stub) |
| `Services/AI/GoogleAIService.cs` | Google AI Studio client |
| `Services/AI/IntentActionRouter.cs` | Routes AI intents to actions |
| `Services/AI/IntentActionConfirmRegistry.cs` | Manages pending operation approvals |
| `Services/AI/LiveEventOperatorService.cs` | Live event operator AI |
| `Services/AI/GameChatAiBridge.cs` | Bridges in-game chat to AI |
| `Services/AI/ConversationStore.cs` | Stores conversation history |
| `Services/AI/AiActionPlanner.cs` | Plans multi-step action sequences |
| `Services/AI/AiGroupProjectMLlmBridge.cs` | Bridges ProjectM AI group to LLM |
| `Services/AI/LocalAiRuntimeManager.cs` | Manages local AI runtime |

---

## Config Structure

All config files live under `%CONFIG_PATH%/` (resolved at runtime):

| File | Purpose |
|---|---|
| `ai_config.json` | AI provider config, tuning, limits |
| `ai_operator_prompt.md` | System prompt for the LLM |
| `actions_catalog.json` | Single source of truth for all flow actions |
| `kit_grant_rules.json` | Kit grant rules |
| `webhook.json` | Webhook configuration |
| `ai_logger.json` | AI logger configuration |
| `events/*.json` | Event definitions per mode |
| `kits/*.json` | Kit definitions |
| `schematics/*.json` | Schematic definitions |

---

## Safety Rules for AI Code Changes

### 1. Never Invent Action Names
All action names must exist in `actions_catalog.json` under the `"registered"` array. If you add a new action, you must:
- Add it to `FlowActionExecutor.SupportedActions`
- Add it to `actions_catalog.json` `"registered"` array
- Add metadata entry with risk level, category, params, examples
- Add example strings to the appropriate category

### 2. Understand the Approval Pipeline
- `IntentActionRouter` routes AI-generated intents to actions
- `IntentActionConfirmRegistry` tracks pending operations that need admin approval
- Actions with `requiresApproval: true` in catalog metadata must go through preview → approve flow
- Never bypass the approval pipeline in code

### 3. Thread Safety for AI Operations
- AI HTTP calls happen off the main thread
- Results must be dispatched to main thread via `MainThreadDispatcher`
- Never call `EntityManager` methods from AI callback code directly

### 4. Exception Logging
- Always log full exceptions: `Logger.Warning($"[Component] Error: {ex}")`
- Never log only `ex.Message` — you lose the stack trace
- Never use empty catch blocks

### 5. Config Changes
- Config hot-reload is supported via `.reload` command
- Validate config before applying
- Queue changes for next session end if a session is active
- Always create a backup before writing config changes

### 6. Secrets
- Never hardcode API keys, tokens, or webhook URLs
- Read secrets from environment variables or `dotnet user-secrets`
- Use `Env.TryGetEnv("KEY")` pattern for environment variable access

---

## Build & Test

```bash
# Build
dotnet build BattleLuck.sln

# Build with specific properties
dotnet build BattleLuck.sln --no-restore /p:GenerateReadme=false /p:DeployToServer=false

# Run tests
dotnet test BattleLuck.sln

# Pre-publish security scan
rg -n "cfat[_]" .
rg -n "cfut[_]" .
rg -n "discord[.]com/api/webhooks" .
rg -n "CLOUDFLARE_AI_API_TOKEN\s*[^\s#]+"
rg -n "GOOGLE_AI_API_KEY\s*[^\s#]+"
```

---

## Common Patterns

### Adding a New Action
1. Add the action name string to `FlowActionExecutor.SupportedActions`
2. Add the action name to `actions_catalog.json` → `"registered"` array
3. Add metadata entry in `actions_catalog.json` → `"metadata"` object
4. Add example strings in `actions_catalog.json` → `"examples"` → appropriate category
5. Implement the handler in `FlowActionExecutor` (switch case or dictionary dispatch)
6. Add validation in `ActionManifestService.Validate()` if needed
7. Add tests for the new action

### Adding a New Game Mode
1. Create mode config JSON in `config/BattleLuck/events/`
2. Register mode in `GameModeRegistry`
3. Create mode-specific services if needed
4. Add mode to `actions_catalog.json` `"runtime_inject"` section
5. Add mode to `ai_operator_prompt.md` game modes list

### Adding a New AI Provider
1. Create a new class extending `BaseAiService`
2. Implement `GetChatCompletionAsync()` and health check
3. Add config settings to `AIConfig.cs` and `ai_config.json`
4. Register in `AIAssistant.Initialize()`
5. Add provider detection in `AIAssistant` (IsXxxProvider, NormalizeProviderName)
6. Add to the query routing in `GetChatCompletionAsync()`

---

## Key Technical Concepts

### PrefabGUID
V Rising uses `PrefabGUID` (wraps an int hash) to identify all game entities, items, buffs, etc. The `Prefabs` class contains static fields for known prefabs. Use `PrefabHelper.GetPrefabGuid(name)` for runtime resolution.

### EntityManager
Unity DOTS `EntityManager` is the primary API for creating, modifying, and destroying entities. All operations must happen on the main thread. Use `EntityQuery` for efficient filtering.

### ZoneHash
Events are tied to zones identified by a `zoneHash` (int). Players join zones, and zone-scoped operations (snapshots, cleanup, teleport) use this hash.

### Action Strings vs Structured JSON
Actions can be expressed as:
- **String format:** `action:key=value|key2=value2` (legacy, deprecated)
- **Structured JSON:** `{ "type": "action.name", "params": { "key": "value" } }` (preferred)

### Risk Levels
- **safe:** No approval needed (announce, notification, timer, score, glow, search)
- **controlled:** Preview + admin approval (spawn, teleport, buff, kit, merchant)
- **destructive:** Preview + explicit approval + rollback capable (destroy, clear, restore, mode.end)

---

## Related Documentation

- `docs/DEEP_REVIEW.md` — Full architecture and security review
- `docs/LLM_GUIDE.md` — LLM setup and configuration guide
- `docs/AI_PROMPT_REFERENCE.md` — Knowledge bank with prefab/buff/sequence samples and glossary
- `docs/PUBLISHING_CHECKLIST.md` — Release requirements and secret scan
- `docs/CI_SECRETS.md` — CI/CD secrets management
- `docs/developer/README.md` — Developer guide with ECS patterns
- `AI_RUNTIME.md` — Docker AI runtime setup
- `%CONFIG_PATH%/ai_operator_prompt.md` — Game Session Director system prompt
- `%CONFIG_PATH%/actions_catalog.json` — Action definitions and metadata
