# Entity Query Descriptions

All `EntityQuery` objects registered by game systems, generated from 1.1 game data. Use this to find which system watches a given component, or to identify the query property to access in a Harmony patch.

## Data Source

The full query data is available in `queryDescriptions.json` (370KB, ~1000+ queries).

## Format

Each query entry contains:

| Field      | Description                                    |
| ---------- | ---------------------------------------------- |
| `system`   | Full system class name                         |
| `property` | Query property name in the system              |
| `all`      | Components that must be present                |
| `none`     | Components that must NOT be present            |
| `any`      | Components where at least one must be present  |
| `options`  | Query options (Default, IncludeDisabled, etc.) |

## Sample Entries

```json
{
  "system": "Network.Systems.TeleportIncorrectPositionSystem",
  "property": "__query_524969957_0",
  "all": [
    "ProjectM.PlayerCharacter",
    "Unity.Transforms.Translation",
    "ProjectM.Network.IsConnected"
  ],
  "none": [],
  "any": [],
  "options": "IncludeDisabled"
}
```

```json
{
  "system": "ProjectM.AbilityCastStarted_SetupAbilityTargetSystem_Shared",
  "property": "_BuffsQuery",
  "all": [
    "ProjectM.EntityOwner",
    "ProjectM.Buff",
    "Stunlock.Core.PrefabGUID",
    "ProjectM.AbilityTargetSource"
  ],
  "none": [],
  "any": [],
  "options": "IncludeDisabled"
}
```

## Usage

Use this data to:

- Find which system processes a specific component
- Identify query property names for Harmony patches
- Understand component dependencies in game systems

## Related Pages

- [Entities and Components](/dev/ecs-entities.html)
- [System Update Tree](/dev/systems-tree.html)
