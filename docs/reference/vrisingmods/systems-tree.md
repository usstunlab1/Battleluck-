# System Update Tree

The ECS system update hierarchy for the V Rising server world (1.1). Systems within a group run in the order shown.

## Data Source

The full system tree data is available in `systemsTree.json` (75KB, 1,075 systems across 72 groups).

## System Types

| Type      | Description                                        |
| --------- | -------------------------------------------------- |
| `Group`   | A container that holds child systems with ordering |
| `CSB`     | Component System Base - standard Unity ECS system  |
| `ISystem` | Interface-based ECS system (modern ECS approach)   |

## Top-Level Structure

```json
[
  {"name": "ProjectM.MapZoneCollectionSystem", "type": "CSB"},
  {"name": "ProjectM.Contest.ContestIdManagerSystem", "type": "CSB"},
  {"name": "ProjectM.TravelBuffCollectionSystem", "type": "CSB"},
  {"name": "ProjectM.Shared.Systems.PhysicsBugfixSystem", "type": "CSB"},
  {"name": "Unity.Entities.SimulationSystemGroup", "type": "Group", "descendants": 927, "children": [...]},
  ...
]
```

## Major Groups

- **SimulationSystemGroup** (927 systems) - Main game logic
  - StartSimulationGroup (25 systems)
  - InitializationGroup
  - EarlySimulationGroup
  - PhysicsGroup
  - LateSimulationGroup
  - etc.

## Usage

For an explanation of system types (Group, CSB, ISystem), see [Identifying Systems](/VRising-Mod-Wiki/dev/reading-game-code#identifying-systems).

## Related Pages

- [Entity Query Descriptions](/dev/query-descriptions.html)
