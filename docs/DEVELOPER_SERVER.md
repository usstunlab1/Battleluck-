# Developer Operations Inside the Server

![Developer server operations](assets/developer-server-header.png)

Authenticated administrators can inspect and test BattleLuck without bypassing the canonical action pipeline. Developer operations are isolated, preview-first, and observable in server chat/logs.

## Workflow

1. `.roadmap.status` — confirm the milestone and acceptance target.
2. `.director` and `.actions.status` — inspect live session and catalog state.
3. `.dev.enter` — enter the developer sandbox.
4. `.dev.test <action>` — test one verified action.
5. `.dev.flow <modeId> <flowType>` — exercise a complete flow in the sandbox.
6. `.roadmap.prompt developer` — inspect the active developer contract.
7. Exit the sandbox and record the observed result before proposing a production change.

Never invent prefabs or GUIDs, never claim a preview executed, and never use a second command bus. Production mutations remain behind the existing admin approval and FlowActionExecutor paths.

The server prompt is `config/BattleLuck/prompts/developer_server.md`. It is deliberately separate from the local coding guide in `docs/DEVELOPER_AI_PROMPT.md`.
