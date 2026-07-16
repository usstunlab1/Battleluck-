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

## Safe operator loop

1. Observe `.director`, active sessions, the action catalog, and roadmap state.
2. Diagnose the request and identify missing facts.
3. Search the catalog before selecting an action.
4. Prepare `.ai action` or `.ai event request` as a preview.
5. Wait for explicit admin approval.
6. Report the exact result or failure and use rollback where supported.

The authoritative prompt is `config/BattleLuck/prompts/llm_server.md`; `RoadmapService` appends the current roadmap and role guardrails to the runtime system prompt.
