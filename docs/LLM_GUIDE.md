# BattleLuck LLM Guide

BattleLuck ships with a local-first Game Session Director workflow. The LLM is not an autonomous server owner; it is a director assistant that observes sessions, searches the action catalog, proposes safe changes, and waits for admin approval before risky execution or config writes.

The recommended published setup is a local OpenAI-compatible Llama endpoint, with hosted providers disabled unless a server owner intentionally configures them.

## How AI works after installation

AI is server-side. Players only install BattleLuck; they do not install a second
plugin, model, or API client. Every installation exposes `.ai` and `.aistatus`.

- The server reads `BepInEx/config/BattleLuck/ai_config.json` at startup.
- A configured local Llama/Ollama-compatible endpoint provides full LLM replies.
- A hosted provider (Cloudflare or Google) is optional and requires the owner to
  supply credentials through config/environment variables.
- If no provider is reachable, BattleLuck keeps a simple local fallback for basic
  command/catalog guidance; it does not pretend that a full model is available.
- Player `.ai` chat is advice-only. Admin previews and approvals are required for
  live actions, event edits, NPC/boss control, and config writes.
- A normal `.ai <message>` opens a bounded interactive conversation with up to
  four AI replies. `.ai end` closes it early; ordinary chat is never forwarded
  while no session is active.
- `.ai history [items]` exposes transient in-memory items from the last 24 hours;
  `.ai tasks <goal>` runs the catalog-backed planner and stores a reviewable task,
  but never executes the proposed actions.
- Network calls run asynchronously. Approved ProjectM/Unity mutations are queued
  to the server main thread. Conversation history is disabled by default.

The owner can inspect the active provider with `.aistatus` and reload settings with
`.ai.reload` (admin). A typical local setup is: start the endpoint, ensure the
configured model is installed, then run `.ai.reload` and `.ai.status`.

## Optional per-player chat backups

Chat backup is controlled by the server owner, not by a client installer. Set
`chat_backup.enabled` to `true` in `BepInEx/config/BattleLuck/ai_config.json` to
write per-player JSONL backups. On Windows, an empty `chat_backup.path` uses
`%USERPROFILE%\AppData\LocalLow\Stunlock Studios\VRising\BattleLuck\chat-backups\<steamId>\`;
Linux hosts fall back to `BepInEx/config/BattleLuck/chat-backups/`. The plugin
rotates files by day, prunes them after `retention_days`, and caps each file with
`max_file_size_mb`. The server never writes these files to a player's local game
folder; keep the server directory private and disclose retention to players.

## Supported Runtime

Default endpoint:

```
http://127.0.0.1:11434
```

This is the loopback address of the V Rising server and is correct when the
provider runs on that same host. For a provider on another machine, set
`llama_api.base_url` to `http://<AI-SERVER-PRIVATE-IP>:11434` and restrict the
firewall to the game server. The provider endpoint is server-to-server; players
do not connect to it.

Default model label:

```
llama2
```

Start local inference before launching the server:

```powershell
.\scripts\start_vllm.ps1
```

Fallback profile (llama.cpp):

```powershell
.\scripts\start_local_llama.ps1
```

Then use:

```
.ai.reload
.ai.status
```

The mod talks to the local server through OpenAI-compatible chat completions. Do not bundle model weights in the mod package. Server owners must download models separately and follow the model license.

## Model Choice

Use the smallest model that reliably follows BattleLuck JSON rules:

| Hardware | Suggested model class | Use case |
|----------|---------------------|----------|
| CPU / low VRAM | 1B-3B instruct, Q4 | Player help, catalog search, short admin guidance |
| 8-12 GB VRAM | 7B-8B instruct, Q4/Q5 | Event authoring previews, action selection, config edits |
| Larger GPU | 17B+ instruct or hosted private endpoint | Long unified event edits, richer reasoning, many actions |

For public releases, keep `config/BattleLuck/ai_config.json` local-first and secret-free:

```json
{
  "enabled": false,
  "provider": "llama",
  "llama_api": {
    "enabled": false,
    "base_url": "http://127.0.0.1:11434",
    "model": "llama2"
  },
  "cloudflare_ai": {
    "enabled": false,
    "api_token": ""
  },
  "privacy": {
    "store_conversation_history": false,
    "max_conversation_history_size": 0
  }
}
```

## Prompt Pattern

The in-server prompt contract is extended by `RoadmapService`, which appends the active milestones and role guardrails from `config/BattleLuck/roadmap.json`. See [LLM server operations](LLM_SERVER.md), [developer server operations](DEVELOPER_SERVER.md), and [the prompt system](PROMPT_SYSTEM.md) for the two server roles.

BattleLuck prompts should stay boring and strict:

1. Identity: BattleLuck Game Session Director AI for a V Rising BepInEx server mod.
2. Scope: unified events, actions catalog, zones, bosses, sessions, cleanup, config help.
3. Hard rules: no invented actions, no secrets, no player destruction, preview before risky writes.
4. Context: director snapshot, active mode/session, relevant config excerpts, catalog matches, runtime health.
5. Output contract: plain chat for help, or exact JSON only inside the event-authoring pipeline.

## Director Loop

The LLM should follow this loop:

1. **Observe** — read `.director`, active sessions, event runtime, action catalog, bosses, objects, players, timers, and cleanup state.
2. **Diagnose** — name the risk or missing piece in one short sentence.
3. **Plan** — choose existing catalog actions, a verified system alias, a reusable sequence, or a unified event JSON edit.
4. **Create (optional)** — an admin may run `.ai create <eventId> [templateId]` to clone Bloodbath or another event template.
5. **Preview** — use `.ai event request <change>` or `.ai action <catalog action>`.
6. **Approve** — wait for `.ai approve`, `.ai event approve <operationId>`, or another explicit admin command.
7. **Execute** — the server validates the approved operation and dispatches native-world work on its main thread.
8. **Rollback** — use `.ai rollback` / `.ai event rollback <operationId>` for pending proposals; rollback cannot undo an action that already ran.

### Good Admin Prompt

```
.ai event request bloodbath: add a boss gate, green ready announcement, 10 second warmup stun buff, and cleanup actions. Use only actions from catalog.
```

### Bad Admin Prompt

```
.ai make everything better and run it now
```

## Event Authoring Rules

The AI may search `actions_catalog.json` and propose up to `event_authoring.max_actions_per_event`, capped at `1000`.

It must not write config directly from normal chat. The safe flow is:

```
.ai event request <change>
.ai create <eventId> [templateId]     # admin: clone an editable event template
.ai event preview <operationId>
.ai event approve <operationId>
.ai event rollback <operationId>
```

Actions are selected from the verified catalog and may target any catalog category.
ProjectM/Unity system actions must first be found and registered as exact verified
`system.*` references; they are not arbitrary native calls. Developer sequences can
combine catalog actions with `wait:<seconds>` and `tick:<event-second>` markers.

The following actions are controlled or destructive and need admin approval:

- Boss, NPC, player spawn/placement
- Cleanup operations
- Object/wall/floor changes
- Zone modifications
- Merchant changes
- Progression unlocks
- Logistics and inventory mutations
- Config writes

Safe actions (read-only, notifications, timers):
- Timer actions
- Notification actions
- Score queries
- Catalog search

## Security Rules

Never publish:

- API keys or bearer tokens
- Discord webhooks
- `.env` files
- AI operation logs
- Local chat history
- Downloaded model weights
- Server-specific player snapshots

### Pre-Publish Scan

```powershell
rg -n "cfat[_]" .
rg -n "cfut[_]" .
rg -n "discord[.]com/api/webhooks" .
rg -n "CLOUDFLARE_AI_API_TOKEN\s*[^\s#]+"
rg -n "GOOGLE_AI_API_KEY\s*[^\s#]+"
git status --short
dotnet build .\BattleLuck.sln --no-restore /p:DeployBattleLuck=false
```

If anything real appears, remove it and rotate the exposed credential.

## Troubleshooting

| Symptom | Fix |
|---------|-----|
| `.ai` says provider unavailable | Start `scripts/start_vllm.ps1` (or fallback `scripts/start_local_llama.ps1`), then run `.ai reload` |
| AI answers like a Cloudflare support bot | Check `ai_operator_prompt.md` and ensure provider is `llama` |
| AI invents action names | Use `.ai catalog search <text>` and keep `use_actions_catalog=true` |
| AI tries to apply risky changes directly | Reject the proposal; all risky writes must go through preview/approve |
| Local model is too weak for large JSON | Use a larger local model or split the request into smaller event edits |

## References

- Meta Llama model and prompt format docs: https://www.llama.com/docs/model-cards-and-prompt-formats/
- Running Meta Llama on Windows: https://www.llama.com/docs/llama-everywhere/running-meta-llama-on-windows/
- llama.cpp server docs: https://github.com/ggml-org/llama.cpp/blob/master/tools/server/README.md
- OpenAI prompt engineering guide: https://help.openai.com/en/articles/6654000-best-practices-for-prompt-engineering-with-openai-api
- Anthropic prompt engineering guide: https://platform.claude.com/docs/en/build-with-claude/prompt-engineering/claude-prompting-best-practices
- OWASP LLM Top 10: https://owasp.org/www-project-top-10-for-large-language-model-applications/

## Prompt Artifacts

BattleLuck provides three prompt artifacts for different AI audiences:

| Artifact | Audience | Purpose |
|---|---|---|
| `config/BattleLuck/ai_operator_prompt.md` | Game Session Director LLM | System prompt for the in-game AI that observes sessions and proposes actions |
| `docs/DEVELOPER_AI_PROMPT.md` | AI coding assistants (Claude, Cursor, Copilot) | Architecture, conventions, and safety rules for modifying the C# codebase |
| `docs/AI_PROMPT_REFERENCE.md` | All AI contexts | Knowledge bank with prefab/buff/sequence samples, glossary, format references, troubleshooting |

### Prompt Pipeline

The `ai_operator_prompt.md` is loaded at runtime by `AIAssistant.LoadOperatorPrompt()` which injects dynamic context:

- `{prefabSample}` — Top 40 prefab names from the `Prefabs` class
- `{buffSample}` — Top 40 buff prefab names (prefixed with `Buff_`)
- `{sequenceSample}` — Top 40 sequence names from `ActionSequences` cache
- `{maxActionsPerEvent}` — Config value from `ai_config.json` (default: 1000)
- `{actionsCatalogSummary}` — Categorized action list from `actions_catalog.json`

If the file is missing or fails to load, `AIAssistant` falls back to a hardcoded system prompt in `GetSystemPrompt()`.

### Security Considerations

- The operator prompt contains placeholders that are populated at runtime. Do not edit the placeholders themselves.
- Never publish the operator prompt with sensitive data — it reads from config files only.
- The developer prompt is intended for local AI coding tools, not for production deployment.

## Related Documentation

- [Developer Guide](developer/README.md) — Architecture and ECS patterns
- [Developer AI Prompt](DEVELOPER_AI_PROMPT.md) — AI coding assistant prompt for C# development
- [AI Prompt Reference](AI_PROMPT_REFERENCE.md) — Knowledge bank with glossary, examples, and troubleshooting
- [Publishing Checklist](PUBLISHING_CHECKLIST.md) — Release requirements and secret scan
- [Deployments](deployments/README.md) — AI service setup and configuration
