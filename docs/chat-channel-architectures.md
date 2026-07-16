# BattleLuck: Chat Channel Architectures (CORRECTED)

> This document replaces the earlier "Chat Channel Types & Architectures" write-up,
> which described components that do **not** exist in the codebase
> (`ChatCommandListener`, `ConsoleBridgeController`, `AIAssistant.QueryAsync`/
> `SummarizeEventsAsync`, a direct Gemini call for Discord, a per-player 30s
> cooldown, and a 5-minute query-response cache). Those claims were stale/aspirational.
> The four channels below are the ones that actually ship, with their real
> class names and entry points.

## 1. Single AI Core

All channels converge on one engine:

- `Services/AI/AIAssistant.cs` — the in-process AI orchestrator
  (`BattleLuckPlugin.AIAssistant`). The real query entry point is
  `AIAssistant.HandleDirectQuery(steamId, query, source, broadcastToInGameChat)`.
- Provider layer (`Services/AI/BaseAiService.cs` + subclasses):
  - `GoogleAIService` (Google AI Studio)
  - `LlamaAIService` (Meta Llama)
  - `CloudflareAiService`
  - Local Ollama (CPU-only by design — see infrastructure notes)
  - genai-stack sidecar (`Services/AI/GenaiStackClient.cs`, HTTP to `localhost:8504`)
  - Superuser sidecar (`Services/AI/BattleAiSidecarService.cs`)
- `Services/AI/GameChatAiBridge.cs` — in-game RAG streaming
  (`StreamQueryAsync`).

There is **no** `AIAssistant.QueryAsync` / `SummarizeEventsAsync` method.
Discord and in-game both funnel into `HandleDirectQuery`.

## 2. In-Game Chat (VCF commands)

| Attribute | Reality |
|---|---|
| Entry point | V Rising `ChatCommandContext` commands (VampireCommandFramework), **not** a `ChatCommandListener` |
| Player command | `.ai.chat <message>` → `DevCommands.AiChat` (`Commands/Admin/DevCommands.cs:99`) |
| Admin command | `.ai` → `PlayerCommands` (`Commands/Player/PlayerCommands.cs:1027`) |
| Live-event review | `.ai event` flow → `PlayerCommands.ReplyEventRequest` / `ReplyEventReview` |
| Rate limit | Provider-level `SemaphoreSlim` in `BaseAiService` (~`maxRequestsPerSecond`, default **10/s, global**), **not** a per-player 30s cooldown |
| Network | None — in-process; only the AI provider call leaves the box |
| Latency | Command handling + context gather is sub-100ms; total latency is dominated by the AI provider (local Ollama on CPU is the slow path) |

```text
Player .ai.chat … → ChatCommandContext → DevCommands.AiChat
        → AIAssistant.HandleDirectQuery(steamId, query, "ingame", …)
        → provider (Ollama / Google / sidecar / …)
        → reply above player
```

Best for: quick questions during gameplay, personal context, live event review.

## 3. Discord (HTTP interactions)

| Attribute | Reality |
|---|---|
| Listener | `Services/Integrations/DiscordBridgeController.cs` — `HttpListener` on **port 25581**, verifies Discord signatures, handles PING (type 1) + APPLICATION_COMMAND (type 2) |
| Commands | Registered manually in the Discord Developer Portal; the bot dispatches by incoming command name |
| AI command | `/ai` (and now `/ask` alias) → special-cased at `DiscordBridgeController.cs:167` → `HandleAiCommandAsync` → `AIAssistant.HandleDirectQuery(…, "discord", broadcastToInGameChat: true)` |
| Other commands | `status`, `join`, `leave`, `kit`, `heal`, `stats`, `leaderboard`, `servant*`, `flow_*` (handled via `ProcessCommand` on the main thread) |
| Response model | Deferred response (type 5) immediately, then `SendFollowUpAsync` with the AI result |
| Rate limit | Provider `SemaphoreSlim` (global ~10/s); plus Discord's own per-guild interaction limits. There is **no** Gemini-only throttle here — Discord uses the same `HandleDirectQuery` provider path as in-game |
| Network | Yes (inbound interaction + outbound AI provider + optional in-game broadcast) |
| Latency | Inbound RTT + AI provider time + follow-up POST; dominated by the AI provider |

```text
Discord /ai (or /ask) → HttpListener:25581 → verify sig
   → deferred (type 5)
   → HandleAiCommandAsync → AIAssistant.HandleDirectQuery(…, "discord")
   → SendFollowUpAsync (Discord) + optional in-game broadcast
```

Best for: team coordination, server-wide questions, broadcasting.

> Note: `/ai` and `/ask` must be registered in the Discord Developer Portal
> before they appear to users. The bot already handles both names in code.

## 4. Event Logger (buffered batch)

| Attribute | Reality |
|---|---|
| Implementation | `Services/AI/AiLoggerController.cs` (NOT a class named `EventLogger`) |
| Config | `config/BattleLuck/ai_logger.json` → `Models/AiLoggerModels.cs` (`AiLoggerConfig`) |
| Buffering | `LogEvent(type, details)` appends `GameEventEntry`; `Tick()` flushes when `buffer.flushIntervalSec` (default **60s**) elapses **or** `buffer.maxSize` (default **100**) is reached |
| Summarization | `FlushToGeminiAsync` (direct Gemini `generateContent` REST) **or** `FlushToSidecarAsync` (`{superuserSidecar.url}/api/summarize`) — selected by enabled provider |
| Delivery | `SendToDiscordAsync` posts a `DiscordWebhookPayload` embed to the configured `discord.webhookUrl` |
| Rate limit | Automatic buffering (no per-player limit) |
| Network | Outbound to AI provider + Discord webhook only |
| Latency | Buffer window (≤ flushIntervalSec) + AI provider time; one API call per batch |

```text
Game events → AiLoggerController.LogEvent → buffer (≤60s or ≤100)
   → FlushBuffer → FlushToGeminiAsync / FlushToSidecarAsync
   → SendToDiscordAsync (webhook embed)
```

Best for: narrative summaries, epic-moment alerts, cutting API calls (N events → 1 call).

## 5. Developer / Admin Console

| Attribute | Reality |
|---|---|
| Implementation | In-game VCF admin commands (no separate `ConsoleBridgeController`) |
| AI commands | `.ai` (chat/event), `.ai.test`, `.ai.reload`, `.ai.status`, `.ai.event`, `.ai.boss.*`, `.ai.group.*`, `.ai.sequence.*`, `.ai.project.*`, `.ai.list.*`, `.ai.actions.review`, `.llm` |
| Entry point | Same `AIAssistant.HandleDirectQuery` / provider layer |
| Rate limit | Provider `SemaphoreSlim` (admin is not exempt from the global provider throttle) |
| Network | Only the AI provider call |
| Latency | Sub-100ms handling + AI provider time |

Best for: admin debugging, instant queries, testing AI flows.

## 6. Where the stale doc was wrong

| Stale claim | Actual |
|---|---|
| `ChatCommandListener`, `!ask` | `.ai.chat` (VCF `ChatCommandContext`) |
| `ConsoleBridgeController`, `ai.ask/context/history/logs` | `.ai` / `.ai.test` / `.llm` admin commands |
| `AIAssistant.QueryAsync` / `SummarizeEventsAsync` | `AIAssistant.HandleDirectQuery` + `GenaiStackClient` / `BattleAiSidecarService` |
| Discord → Gemini direct call | Discord → `HandleDirectQuery` → active provider (same as in-game) |
| Per-player 30s cooldown | Provider `SemaphoreSlim` ~10/s **global** |
| 5-minute similar-query cache (60% fewer calls) | No response cache found. Existing caches are `tools_cache_enabled` (tool-call caching for function-calling providers) plus internal state caches (player snapshots, config, ECS queries) |
| Event Logger 10s flush | `AiLoggerConfig.buffer.flushIntervalSec` default **60s** |
| "Gemini" as the model | Actual providers: Google AI Studio, Llama, Cloudflare, local Ollama (CPU-only), genai-stack sidecar, superuser sidecar |

## 7. Channel interoperability

All four channels share the same AI core and provider layer; they differ only in
delivery mechanism and how the user's identity/context is gathered.

```text
                 ┌──────────────────────────────┐
                 │   AIAssistant (core)        │
                 │   HandleDirectQuery(...)     │
                 └──────────────┬──────────────┘
                                │
            ┌───────────┼───────────┬──────────────┐
            │           │           │          │
        In-Game     Discord     Event Logger   Admin/Console
       (.ai.chat)   (/ai,/ask) (AiLogger…)  (.ai, .llm)
```

A single query can surface in multiple places: e.g., a Discord `/ai` call sets
`broadcastToInGameChat: true`, so the answer both replies in Discord and
appears in the game chat.
