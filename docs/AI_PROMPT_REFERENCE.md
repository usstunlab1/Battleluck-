# BattleLuck AI Operator Reference

This is the canonical human reference for the runtime operator prompt at `config/BattleLuck/ai_operator_prompt.md`. It describes the current implementation, not an aspirational API.

## Authority and execution

Player and unverified external chat is advice-only. It cannot authorize an action, approval, config edit, or rollback.

Authenticated admins use these paths:

```text
.ai create <eventId> [templateId]  # clone an editable event, default Bloodbath
.ai action <catalog action>          # validate and preview a live action
.ai approve [operationId]            # execute the latest or named live-action preview
.ai event request <change>           # generate a proposed events/<eventId>.json
.ai event preview <operationId>      # inspect the pending proposal
.ai event approve <operationId>      # write and reload the proposed event.json
.ai rollback [operationId]           # discard a pending live action or restore a pending config proposal
.ai catalog search <text>            # search the registered catalog
.aistatus                            # inspect configured providers and runtime health
```

Public `.ai <question>` and `.aistatus` are advice/status paths; they do not
authorize mutations. Admin live operations use the process
`catalog/search -> preview -> approve -> runtime execution`. An `operationId` is a
pending proposal, not evidence that a change ran. Only a successful command result
confirms an execution or config reload. Rollback does not reverse a live action
that already executed.

The action catalog is the source of truth for names, parameters, examples, risk,
and handler availability. Any catalog category can be proposed, and verified
ProjectM/Unity system references can be registered as `system.*` aliases after an
exact reference search. Those aliases are safe references for BattleLuck's runtime
state; they do not dynamically invoke arbitrary native ECS systems.

Developer sequence steps can combine catalog actions with `wait:<seconds>` and
`tick:<event-second>` markers. Use `.ai.sequence.gather` to select catalog actions,
`.ai.sequence.preview` to validate, and `.ai.sequence.execute` only for an approved
admin operation. The unified event runtime schedules named sequences on the session
clock.

## Action formats

New event actions use a structured object:

```json
{ "type": "announce", "params": { "message": "Ready", "level": "success" } }
```

Existing legacy actions may remain as strings:

```text
announce:title=Ready|message=Match starts soon.|color=#47FF8A|level=success
```

Use only actions and parameters accepted by the current `actions_catalog.json` and runtime validator. Do not infer a handler from an action-like name.

## Unified event flow schema

The runtime loads the canonical file at:

```text
config/BattleLuck/events/<eventId>.json
```

The important shapes are:

```json
{
  "metadata": {
    "id": "bloodbath",
    "displayName": "Bloodbath",
    "enabled": true,
    "version": "1"
  },
  "rules": {
    "minPlayers": 2,
    "maxPlayers": 8,
    "enablePvP": true,
    "matchDurationMinutes": 5,
    "allowLateJoin": false,
    "eliminationMode": true,
    "livesPerPlayer": 3
  },
  "zones": [
    {
      "name": "Arena",
      "type": "bloodbath",
      "hash": 2002,
      "center": { "x": -2000, "y": 5, "z": -2800 },
      "teleportSpawn": { "x": -2000, "y": 5, "z": -2800 },
      "radius": 80,
      "exitRadius": 100,
      "safe": false,
      "boundaryPolicy": "none"
    }
  ],
  "objects": [],
  "glows": [],
  "bosses": [],
  "phases": [
    {
      "name": "setup",
      "durationSeconds": 0,
      "actions": [
        {
          "type": "spawn.boss",
          "params": {
            "prefab": "CHAR_Manticore_VBlood",
            "bossId": "boss1",
            "position": "-2000,5,-2800"
          }
        }
      ]
    }
  ],
  "timers": [
    {
      "timerId": "match",
      "durationSeconds": 300,
      "startPhase": "active",
      "announceStart": true,
      "announceComplete": true,
      "onCompleteActions": [
        { "type": "announce", "params": { "message": "Time is up" } }
      ]
    }
  ],
  "triggers": [],
  "actions": [
    { "type": "announce", "params": { "message": "Arena prepared" } }
  ]
}
```

`zones`, `objects`, `glows`, `bosses`, `phases`, `timers`, and `triggers` are arrays. Phase durations are elapsed-time triggers, not a sequential workflow. `setup` runs when the runtime initializes; `active` runs when the session becomes active.

Root `actions` run only `announce`, `notification`, `notify`, and `send_message`. Put gameplay mutations in a phase, timer completion, trigger, or object action list. `bosses[]` is metadata and validation input; it does not spawn a boss by itself.

## Safety limits

- Do not propose `build.free`, `build.spawn`, `structure.spawn`, `tile.place`, `wall.build`, `floor.place`, `wall.destroy`, or `zone.border.*`; the strict stability profile blocks them.
- A schematic action must pass the catalog and include `safetyMode=event_tracked_zone_only`.
- Do not propose `tech.apply` or `progression.*` mutations.
- A registered `system.*` alias is a verified ProjectM/Unity reference. It does not dynamically invoke or inject a native ECS system.
- Runtime feedback is the only source for success/failure claims.

## Runtime threading

Entity and player notification work must be routed to the Unity main-thread dispatcher. AI/network calls may run asynchronously, but they must not call ProjectM or Unity entity APIs directly from a background continuation.
