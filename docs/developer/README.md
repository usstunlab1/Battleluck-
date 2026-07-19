# Developer Guide

This guide follows the standard V Rising mod development patterns from the [V Rising Mod Wiki](https://wiki.vrisingmods.com/dev/).

## Architecture Overview

BattleLuck is a V Rising dedicated-server BepInEx IL2CPP plugin built on:

- **BepInEx** — Plugin loader and base framework
- **Harmony** — Method hooking for game system interception
- **Unity DOTS ECS** — Entity component system for game state management
- **VampireCommandFramework (VCF)** — Chat command registration and parsing

![Architecture Diagram](https://wiki.vrisingmods.com/dev/how-mods-work.html)

## Core Patterns

### BattleLuckPlugin.cs - Actual entry point

`BattleLuckPlugin.Load()` runs before the V Rising server world exists. It deploys
default configuration, loads schematics, discovers and validates configured modes,
initializes the ProjectM event router, applies Harmony patches, scans the custom
command dispatcher, and registers VampireCommandFramework commands. It does not
construct world-bound services or query players during `Load()`.

The relevant startup sequence is:

1. `ConfigLoader.EnsureDefaultsDeployed()`, `ModeConfigLoader.EnsureWatcher()`,
   and `SchematicLoader.LoadAll()`.
2. `GameModeRegistry.LoadAllModes()` plus action, zone, kit, prefab, schematic,
   flow, and analytics validation for every discovered mode.
3. `ProjectMEventRouter.Initialize()` and Harmony patch registration. If the
   assembly-wide patch pass fails, critical patch classes are applied individually.
4. `BattleLuckCommandDispatcher.EnsureScanned()` and VCF command registration.

The server world is initialized later by `InitializationPatch`, with
`ServerTickHook` retrying as a safety net when a game-version-specific boot hook
does not fire. See [Mod Structure](mod-structure.md) for the complete folder
breakdown.

### Core.cs - Static Service Locator

The static service locator holds references to game systems and services after the world has loaded.

`Core` exposes `VRisingCore.Server` and `VRisingCore.EntityManager` after the
world is ready, and stores the live BattleLuck service references used by commands,
patches, and the tick loop. `Core.InitializeAfterLoaded()` delegates to
`BattleLuckPlugin.TryInitializeCore()`, which initializes the player state,
session, progression, teleport, loadout, death-prevention, NPC, map, and AI
services under one initialization lock.

### One-Shot Initialization

A Harmony postfix on `WarEventRegistrySystem.RegisterWarEventEntities` performs
the one-shot initialization. `BuffSystem_Spawn_Server.OnUpdate` is also patched
as a periodic retry and drives `BattleLuckPlugin.ServerTick(deltaSeconds)`, which
processes sessions, the main-thread dispatcher, AI queues, NPCs, and other runtime
services.

### Runtime services and validation

The runtime is intentionally service-oriented. `SessionController` owns player
event sessions; `GameModeRegistry`, `EventDefinitionLoader`, and
`FlowActionExecutor`/`EventRuntimeController` load and execute declarative modes.
Validators reject or warn about invalid actions, zones, kits, prefabs, schematics,
flows, and analytics before a mode is registered. Supporting services include
`NpcControlService`, `PlayerLoadoutService`, `PlayerProgressionService`,
`TeleportService`, `DeathPreventionService`, and `SessionCleanupService`.

Optional `AIAssistant`, `AiGroupProjectMLlmBridge`, and `LocalAiRuntimeManager`
features perform network or model work off-thread. Discord and webhook controllers
also enqueue their game-facing notifications. Any ProjectM or Unity ECS mutation is
approval-gated and queued through the main-thread dispatcher before it touches the
live world.

### Action catalog, systems, ticks, and sequences

`config/BattleLuck/actions_catalog.json` is the canonical action manifest. It
defines registered action names, metadata, required/optional parameters, examples,
risk, handler availability, and reusable sequences. The runtime merges verified
`system.*` aliases from `live_system_registry.json`; those aliases are references
validated against the KindredExtract ProjectM/Unity inventory and are not arbitrary
native ECS reflection calls. Every action proposed by AI or loaded from an event
must be cataloged (or a verified live alias), handler-reachable, and accepted by
the validation pipeline.

The server tick is the execution boundary. `ServerTickHook` calls
`BattleLuckPlugin.ServerTick`, which ticks sessions and services, drains
`MainThreadDispatcher`, and processes queued AI, Discord, and webhook work. Event
runtime injection is hot-read for the next event tick; network/model work remains
off-thread until an approved mutation is dispatched.

`Patches/AiTickSequencePatches.cs` adds read-only telemetry hooks for the ProjectM
gameplay tick and `ProjectM.Sequencer.SequencerUpdateGroup`. These hooks raise
typed router events only; they do not execute native ECS work. `.ai tasks <goal>`
uses `AiActionPlanner` to create a catalog-validated proposal, while `.ai history`
shows transient conversation items retained for one day.

Developers can compose reusable sequences from catalog actions with
`.ai.sequence.gather` or `.ai.sequence.create`. A sequence may contain action steps,
`wait:<seconds>`, and `tick:<event-second>` markers. Preview and validate it with
`.ai.sequence.preview`, then execute it from a phase, timer, trigger, or approved
live operation using `sequence.custom.play:sequenceId=<id>|schedule=true`.

Native `SequenceGUID` hashes have a separate executable catalog at
`config/BattleLuck/sequences/uuid_catalog.json`. KindredExtract allowlists and
the fallback constants in `ActionModels.cs` are reference-only; they are not
in-game verification. Keep `entries` empty until a target-server dump confirms
the hash, then record `verificationStatus: "in_game_verified"`, the UTC time,
and the dump source.

## Project Folders

| Folder | Purpose |
|--------|---------|
| `BattleLuckPlugin.cs` | Plugin entry point, Harmony setup, VCF registration |
| `Core/` | Static service locator, ECS helpers, initialization |
| `Commands/` | VCF chat command classes |
| `Services/` | Business logic layer (ECS queries, game interactions) |
| `Patches/` | Harmony patches for game system interception |
| `Models/` | Plain data structures (records, structs, enums) |
| `Utilities/` | Stateless helper methods and extension methods |
| `Data/` | Static data: PrefabGUID constants, embedded JSON |

See [mod-structure.md](mod-structure.md) for detailed folder descriptions.

## Development Setup

### Prerequisites

- Visual Studio 2022 (Community is free) with .NET 6.0 SDK
- V Rising dedicated server with BepInEx installed
- Git (optional, for version control)

### IDE Installation

1. Download [Visual Studio 2022 Community](https://visualstudio.microsoft.com/) with .NET 6.0 SDK
2. Add **.NET 6.0 Runtime** and **.NET SDK** individual components

For alternatives, see the [Development Setup Wiki](https://wiki.vrisingmods.com/dev/development_setup.html):
- [JetBrains Rider](https://www.jetbrains.com/rider/) — Full Unity/IL2CPP support
- [dnSpy](https://dnspy.org/), [dotPeek](https://www.jetbrains.com/decompiler/), or [ILSpy](https://github.com/icsharpcode/ILSpy) — Decompilers for `BepInEx/interop`

### Build Commands

```powershell
# Build only (deployment is disabled by default)
dotnet build BattleLuck.sln -c Release

# Explicitly disable deployment
dotnet build BattleLuck.sln -c Release /p:DeployBattleLuck=false
```

### Deploy to Server

```powershell
# Build and deploy using the defaults in BattleLuck.csproj
dotnet build BattleLuck.sln -c Release /p:DeployBattleLuck=true

# Or provide explicit plugin/config destinations
dotnet build BattleLuck.sln -c Release `
  /p:DeployBattleLuck=true `
  /p:ServerPluginPath="C:\Path\BepInEx\plugins\BattleLuck" `
  /p:ServerConfigPath="C:\Path\BepInEx\config\BattleLuck"
```

The `.csproj` `BuildToServer` target runs only when `DeployBattleLuck` is exactly
`true`. It copies the plugin DLLs, recursive `config/BattleLuck/` files, and the
server reference inventory to `ServerPluginPath` and `ServerConfigPath`.

### Git Setup (Optional)

Create a GitHub account at [github.com/join](https://github.com/join) and use:

- **GitHub Desktop** — Beginner-friendly GUI
- **TortoiseGit** — Windows Explorer integration
- **Git Extensions** — Advanced features with visual history

## IL2CPP Interop

V Rising uses IL2CPP, so interop assemblies provide managed signatures for game
types. The project resolves its BepInEx, VampireReferenceAssemblies,
Il2CppInterop.Runtime, HookDOTS.API, and VCF dependencies through the NuGet
configuration; a local server install is only needed when you deploy or inspect
live interop data.

**Key considerations:**

- Game APIs return `Il2CppSystem.Collections.Generic.List<T>`
- Use `Il2CppSystem.Action` for callbacks into game code
- Casting exceptions look different in IL2CPP

## ECS Patterns

### Entity Operations

```csharp
// Read a component
Health health = entity.Read<Health>();
health.Value = health.MaxHealth;
entity.Write(health);
```

### Query Access via Patches

ECS systems store queries as private fields. Access them through Harmony patches:

```csharp
[HarmonyPatch(typeof(SomeSystem), nameof(SomeSystem.OnUpdate))]
static class SomeSystemPatch
{
    static void Postfix(SomeSystem __instance)
    {
        var entities = __instance._QueryName.ToEntityArray(Allocator.Temp);
        try
        {
            foreach (var entity in entities)
            {
                // process entity
            }
        }
        finally
        {
            entities.Dispose();
        }
    }
}
```

## More Documentation

- **[Harmony Patching](harmony-patches.md)** — Hooking game systems, prefix/postfix patterns
- **[CI/CD](ci.md)** — Build verification, GitHub Actions workflow
- **[Mod Structure](mod-structure.md)** — Detailed folder and pattern reference

## Contributing

See [Publishing Checklist](../PUBLISHING_CHECKLIST.md) for release requirements.

```powershell
# Run before publishing
dotnet build BattleLuck.sln -c Release
git status --short
