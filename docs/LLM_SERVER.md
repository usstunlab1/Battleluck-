# LLM Inside the Server

![LLM server operations](assets/llm-server-header.png)

BattleLuck runs the LLM as a local-first Game Session Director. The model receives verified runtime context, the action catalog, the current roadmap, and a role prompt. It does not own the server and it cannot turn chat text into execution.

## Runtime path

```text
player/admin query
  -> AIAssistant
  -> verified operator prompt + RoadmapService context
  -> provider (local Llama, configured hosted fallback, or sidecar)
  -> explanation or preview
  -> authenticated admin approval
  -> FlowActionExecutor
  -> runtime service result
```

The response must distinguish a suggestion, a preview, an approval, and an actual runtime result. A proposal or operation id is never proof that a game action ran.

## Threading and native safety

LLM and provider I/O run asynchronously so model latency does not block the game loop. The model cannot mutate native ECS state directly. After an administrator approves a verified action, the canonical action pipeline queues the native-world mutation onto the server main thread. Player loadouts and event state are saved as rollback snapshots before changes and restored through the supported native-facing services when the event exits or a rollback is requested.

## Safe operator loop

1. Observe `.director`, active sessions, the action catalog, and roadmap state.
2. Diagnose the request and identify missing facts.
3. Search the catalog before selecting an action.
4. Prepare `.ai action` or `.ai event request` as a preview.
5. Wait for explicit admin approval.
6. Report the exact result or failure and use rollback where supported.

## Event crash and restart boundary

Custom events are declarative, but their actions still execute inside the V Rising
server. A malformed event file or unsafe prefab/native ECS action can crash or
restart the process. BattleLuck writes the AI operation/approval trail to
`BepInEx/config/BattleLuck/ai_operations.log` while it is running, and the AI can
suggest another approach when a request fails; this is diagnostics, not crash
prevention. Interactive history
and planner tasks are in-memory for one day and can be lost when the process dies.

BattleLuck writes a player's pre-event state to
`BepInEx/data/BattleLuck/snapshots/<steamId>.json` before event mutations. Normal
exit and explicit restore can roll that state back, but a hard crash can interrupt
cleanup before it executes. After a restart, inspect the log and restore affected
players before retrying the event. Never report rollback as complete unless the
restore operation returned success. Use `.ai rollback player <name|steamId> <timestamp|runId>` for
one online player or `.ai rollback server players confirm` for all online event
snapshots; offline snapshots remain pending. `.ai event rollback <eventId>` is
only an event-definition rollback. The V Rising SaveFileManager owns full-world
save restore/delete, so BattleLuck does not claim to control that host operation.

The authoritative prompt is `config/BattleLuck/prompts/llm_server.md`; `RoadmapService` appends the current roadmap and role guardrails to the runtime system prompt.
