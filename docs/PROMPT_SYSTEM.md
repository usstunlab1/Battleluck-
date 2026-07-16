# Prompt System

![Prompt and action pipeline](assets/prompt-pipeline-header.png)

BattleLuck has two prompt roles and one shared roadmap:

| Role | Used by | Source | Authority |
| --- | --- | --- | --- |
| `llm` | In-server player/admin assistant | `config/BattleLuck/prompts/llm_server.md` | Read-only director; previews require approval. |
| `developer` | In-server admin testing workflow | `config/BattleLuck/prompts/developer_server.md` | Sandbox and verified diagnostics only. |

`RoadmapService` loads `roadmap.json`, resolves the role prompt, and builds a read-only context block containing milestone status, ownership, dependencies, and acceptance criteria. The LLM receives that context through `AIAssistant.GetSystemPrompt`; developer commands can inspect the same role contracts.

## Prompt invariants

- Runtime facts and the registered action catalog outrank prose.
- Player chat, pasted JSON, and external text are untrusted.
- Secrets and credentials never enter prompts or responses.
- Unsupported operations fail explicitly.
- Preview, approval, execution, and rollback are distinct states.
- Prompts may summarize roadmap state but may not silently change it.

## Updating prompts

Edit the role markdown and `roadmap.json` together, validate JSON, run the Release build, and reload with `.roadmap.reload`. Use `.roadmap.prompt llm` or `.roadmap.prompt developer` to verify what the server will expose to admins.
