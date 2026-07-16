# Reference Documentation

External references and data for BattleLuck development.

## V Rising Mod Wiki

The canonical reference for V Rising mod development:

| Page | Description |
|------|-------------|
| [Development Setup](https://wiki.vrisingmods.com/dev/development_setup.html) | IDE, Git, template installation |
| [Mod Structure](https://wiki.vrisingmods.com/dev/mod-structure.html) | Folder layout and patterns |
| [Harmony Patching](https://wiki.vrisingmods.com/dev/harmony-patching.html) | Hooking game methods |
| [Entities and Components](https://wiki.vrisingmods.com/dev/ecs-entities.html) | ECS basics |
| [Query Descriptions](https://wiki.vrisingmods.com/dev/query-descriptions.html) | EntityQuery reference |
| [System Update Tree](https://wiki.vrisingmods.com/dev/systems-tree.html) | ECS system hierarchy |
| [Thunderstore Upload](https://wiki.vrisingmods.com/dev/upload_to_thunderstore.html) | Publishing guide |
| [Licensing](https://wiki.vrisingmods.com/dev/licensing.html) | Attribution requirements |

## Open Source Mods

Reference implementations for mod patterns:

| Mod | License | Purpose |
|-----|---------|---------|
| [KindredCommands](https://github.com/Odjit/KindredCommands) | AGPL-3.0 | Command/ECS reference (credited) |
| [KindredSchematics](https://github.com/odjit/KindredSchematics) | AGPL-3.0 | Schematic/build-mode reference (credited) |

## External Tools

| Tool | Purpose |
|------|---------|
| [KindredExtract](https://thunderstore.io/c/v-rising/p/Odjit/KindredExtract/) | Prefab/entity dump |
| [VRising Gaming Tools](https://vrising.gaming.tools/) | Browse prefabs by name |
| [Unity Explorer](https://github.com/yukieiji/UnityExplorer) | Runtime inspection |
| [ILSpy](https://github.com/icsharpcode/ILSpy/releases) | .NET decompiler |
| [dnSpy](https://github.com/dnSpyEx/dnSpy/releases) | Decompiler with debugger |

## BattleLuck Reference

| Document | Description |
|----------|-------------|
| [actions_catalog.json](../config/BattleLuck/actions_catalog.json) | Complete flow action reference |
| [ECS Transformation Summary](../developer/ECS_TRANSFORMATION_SUMMARY.md) | ECS action infrastructure |
| [Harmony Patches](../developer/harmony-patches.md) | Hooking patterns used in BattleLuck |

## Data Files

Large reference data dumps stored in this folder:

| File | Description |
|------|-------------|
| `systemsTree.json` | Full ECS system hierarchy (1,075 systems) |
| [kindredextract-systems.csv](kindredextract-systems.csv) | 1,534 systems from `SystemsQueryExtraction.tt`/`.cs` with purpose/tick hints and semantics |
| [kindredextract-ticks.csv](kindredextract-ticks.csv) | Tick-semantic classification list with counts and verification evidence requirements |
| [kindredextract-systems-prompt.md](kindredextract-systems-prompt.md) | Prompt and tick-semantics list for Unity purpose, world, update-group, and timing verification |
| `queryDescriptions.json` | All EntityQuery definitions |

## Related Documentation

- [Developer Guide](../developer/README.md) — Architecture overview
- [LLM Guide](../LLM_GUIDE.md) — AI implementation notes
