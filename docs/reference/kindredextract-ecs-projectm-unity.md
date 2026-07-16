# KindredExtract Reference Intake

Source: https://github.com/Odjit/KindredExtract

Purpose: keep a local reference inventory for V Rising ECS, ProjectM, Unity, Stunlock networking, and platform assemblies that may help future BattleLuck in-game features.

## License Boundary

KindredExtract is AGPL-3.0. Do not paste large source files from it into BattleLuck unless the project intentionally accepts AGPL-compatible obligations. Prefer using it as a reference for names, runtime patterns, and server-only discovery workflows, then implement BattleLuck-native code.

The local checkout lives under `.external/KindredExtract` and is ignored by Git.

## Useful Patterns Found

- World discovery: use `World.s_AllWorlds` and prefer `Server`, falling back only for local/client development.
- Runtime systems: retrieve systems through `TheWorld.GetExistingSystemManaged<T>()`.
- Entity data: access components through `EntityManager`, `ComponentType`, and `Il2CppType.Of<T>()` when normal generic helpers are not enough.
- Prefab names: resolve `PrefabGUID` through `PrefabCollectionSystem`.
- System dumps: walk `TypeManager.GetSystemTypeIndices(WorldSystemFilterFlags.All, 0)`, validate handles against `world.Unmanaged`, then build update trees from `ComponentSystemGroup.GetAllSystems()`.
- Player connectivity: hook `ServerBootstrapSystem.OnUserConnected` and `OnUserDisconnected` when session state must follow network lifecycle.
- Debug extraction: keep entity/component/prefab dumps as admin/dev tooling only. KindredExtract itself warns that this is not for live servers because dumps can be heavy.

## Assembly Families To Track

- EOS/platform/network: `com.stunlock.network.eos`, `Stunlock.Network.EosSdk`, `Stunlock.Network`, `Stunlock.Network.Steam`, `ProjectM.Network`, `ProjectM.Steam`.
- ECS core: `Unity.Entities`, `Unity.Collections`, `Unity.Mathematics`, `Unity.Transforms`, `Unity.Physics`.
- ProjectM gameplay: `ProjectM`, `ProjectM.Gameplay.Systems`, `ProjectM.CastleBuilding.Systems`, `ProjectM.Shared`, `ProjectM.Shared.Systems`, `ProjectM.Terrain`, `ProjectM.Pathfinding`, `ProjectM.ScriptableSystems`.
- BepInEx/IL2CPP: `BepInEx.Unity.IL2CPP`, `Il2CppInterop.Runtime`, `0Harmony`.

## Refresh Command

Run this after updating `.external/KindredExtract`:

```powershell
powershell -ExecutionPolicy Bypass -File tools/extract-kindredextract-reference.ps1
```

It writes `docs/reference/kindredextract-reference.json` with:

- referenced assemblies grouped by family
- component extractor type names
- system query type names
- EOS/network/ProjectM/Unity source hits for quick lookup

## BattleLuck Integration Direction

1. Keep this as a reference inventory, not a direct vendored dependency.
2. Add BattleLuck-native wrappers only for features we actually need.
3. Keep heavy dump commands dev/admin-only and disabled by default.
4. Prefer narrow queries and bounded radius scans for live servers.
5. When adding EOS or platform features, start from assembly/reference availability, then add a small runtime probe before adding behavior.
