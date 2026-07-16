# V Rising Mod Wiki Reference

This directory contains documentation generated from the [V Rising Mod Wiki](https://wiki.vrisingmods.com/) and aligned with BattleLuck's implementation.

## Fetched Documentation

| Source | Description |
| --- | --- |
| [Development Setup](https://wiki.vrisingmods.com/dev/development_setup.html) | IDE, Git, .NET setup |
| [How Mods Work](https://wiki.vrisingmods.com/dev/how-mods-work.html) | BepInEx, IL2CPP, DOTS/ECS explanation |
| [Mod Structure](https://wiki.vrisingmods.com/dev/mod-structure.html) | Project folder conventions |
| [Harmony Patching](https://wiki.vrisingmods.com/dev/harmony-patching.html) | Prefix/Postfix, query access, one-shot patches |
| [Entities and Components](https://wiki.vrisingmods.com/dev/ecs-entities.html) | ECS component read/write, queries, ECB |
| [Understanding Prefabs](https://wiki.vrisingmods.com/dev/prefabs.html) | PrefabGUID, spawning, finding IDs |
| [Exploring Game Code](https://wiki.vrisingmods.com/dev/reading-game-code.html) | ILSpy, system identification |
| [Migration Guide 1.1](https://wiki.vrisingmods.com/dev/migration_guide.html) | API changes for V Rising 1.1 |
| [Template](https://wiki.vrisingmods.com/dev/template.html) | `dotnet new vrisingmod` template |
| [Resources](https://wiki.vrisingmods.com/dev/resources.html) | Mod tools, web resources |
| [Thunderstore Upload](https://wiki.vrisingmods.com/dev/upload_to_thunderstore.html) | Packaging and publishing |
| [Licensing](https://wiki.vrisingmods.com/dev/licensing.html) | Open source requirements |

## BattleLuck Alignment Notes

### ECS Compatibility

BattleLuck uses the 1.1 API patterns documented in the Migration Guide:

```csharp
// Entity queries use EntityQueryBuilder with Allocator.Temp
var entityQueryBuilder = new EntityQueryBuilder(Allocator.Temp)
    .AddAll(new(Il2CppType.Of<PlayerCharacter>(), ComponentType.AccessMode.ReadWrite))
    .WithOptions(options);
var query = Core.EntityManager.CreateEntityQuery(ref entityQueryBuilder);
```

### ServerChatUtils Parameter Change

Per the 1.1 migration guide, string messages require `FixedString512Bytes`:

```csharp
public static void SendSystemMessage(this User user, string message)
{
    FixedString512Bytes unityMessage = message;
    ServerChatUtils.SendSystemMessageToClient(Server.EntityManager, user, ref unityMessage);
}
```

### PrefabGuid Names

The wiki documents that `PrefabCollectionSystem.PrefabGuidToNameDictionary` was replaced with `PrefabCollectionSystem._PrefabLookupMap.GetName()` in 1.1. BattleLuck's `EntityExtensions.cs` provides `.LookupName()` that handles both paths.

## System Update Tree & Query Descriptions

Raw data files from the wiki's GitHub repository:

- `systemsTree.json` — ~1,075 ECS systems across 72 groups
- `queryDescriptions.json` — 1,000+ EntityQuery objects with component filters

The canonical enumeration of the **server-world** systems is the list in KindredExtract's
`SystemsQueryExtraction` T4 template (`DumpSystemQueries<TSystem>` per system type). These are
useful for Harmony patch development to identify which system and query to target.

### BattleLuck patch-target verification

BattleLuck's active Harmony patches target the systems below. All resolve as valid V Rising
types (the build enforces existence via `typeof`); the last two are absent from the extraction
list and should be confirmed as live server systems before relying on those patches
(`Patches/ProjectMEventRouterPatches.cs`).

| Patched system | In extraction list |
| --- | --- |
| `ProjectM.ChatMessageSystem` | ✅ |
| `ProjectM.DeathEventListenerSystem` | ✅ |
| `ProjectM.UnitSpawnerReactSystem` | ✅ |
| `ProjectM.BuffSystem_Spawn_Server` | ✅ |
| `ProjectM.Gameplay.WarEvents.WarEventRegistrySystem` | ✅ |
| `ProjectM.PlaceTileModelSystem` | ✅ |
| `ProjectM.MapIconSpawnSystem` | ✅ |
| `ProjectM.ServerBootstrapSystem` | ✅ |
| `ProjectM.DropInventorySystem` | ✅ |
| `ProjectM.EquipItemSystem` | ✅ |
| `ProjectM.Behaviours.BehaviourTreeSystem` | ⚠️ not listed — confirm it runs on this version |
| `ProjectM.AggroSystem` | ⚠️ not listed — confirm it runs on this version |

## Additional Resources

- [KindredExtract](https://thunderstore.io/c/v-rising/p/Odjit/KindredExtract/) — prefab/entity dump tool
- [VRising Gaming Tools](https://vrising.gaming.tools/) — browse prefabs by in-game name
- [Unity Explorer](https://github.com/yukieiji/UnityExplorer) — runtime game inspection
- [KindredCommands](https://github.com/Odjit/KindredCommands) — reference mod (AGPL-3.0)
- [KindredSchematics](https://github.com/odjit/KindredSchematics) — reference schematic/build-mode mod (AGPL-3.0)
