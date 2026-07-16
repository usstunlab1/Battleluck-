# BattleLuck Roadmap

![BattleLuck roadmap](assets/roadmap-header.png)

The live roadmap is stored in `config/BattleLuck/roadmap.json` and loaded by `RoadmapService`. It is shared by the in-server LLM director and authenticated developer operators so plans, prompts, and runtime behavior use one source of truth.

## Current milestones

| Milestone | Status | Owner | Purpose |
| --- | --- | --- | --- |
| Canonical runtime architecture | completed | runtime | One action pipeline, one NPC owner, and explicit services. |
| Player state and safety services | completed | runtime | Loadouts, progression boundaries, teleportation, and death-prevention charges. |
| Events, spatial points, and schematics | completed | runtime | Final event schema, spatial points, catalogs, and schematic-backed construction. |
| LLM and developer operations inside the server | active | ai-dev | Shared roadmap context, role prompts, preview-first admin workflows. |
| Live server verification | planned | operations | Exercise permissions, cleanup, rollback, NPC control, and prompt safety live. |

## Server commands

All roadmap commands are admin-only:

- `.roadmap.status` — list milestone state.
- `.roadmap.show <milestoneId>` — inspect acceptance criteria and dependencies.
- `.roadmap.prompt <llm|developer>` — inspect the active role contract.
- `.roadmap.reload` — reload `roadmap.json` and role prompts.

## Change workflow

Roadmap edits are configuration changes, not hidden runtime mutations. Review the JSON, validate it, reload it with `.roadmap.reload`, and verify the resulting prompt context with `.roadmap.prompt`. The LLM can summarize or propose roadmap work but cannot silently rewrite it.
