# Developer Guide

This guide follows the standard V Rising mod development patterns from the [V Rising Mod Wiki](https://wiki.vrisingmods.com/dev/).

## Architecture Overview

BattleLuck is a V Rising server-side mod built on:

- **BepInEx** — Plugin loader and base framework
- **Harmony** — Method hooking for game system interception
- **Unity DOTS ECS** — Entity component system for game state management
- **VampireCommandFramework (VCF)** — Chat command registration and parsing

![Architecture Diagram](https://wiki.vrisingmods.com/dev/how-mods-work.html)

## Core Patterns

### Plugin.cs - Entry Point

The BepInEx entry point. Decorated with `[BepInPlugin]`, overrides `Load()` which is called once at startup.

```csharp
[BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
[BepInDependency("gg.deca.VampireCommandFramework")]
public class BattleLuckPlugin : BasePlugin
{
    internal static Harmony Harmony = new(MyPluginInfo.PLUGIN_GUID);

    public override void Load()
    {
        Harmony.PatchAll(Assembly.GetExecutingAssembly());
        CommandRegistry.RegisterAll();
    }

    public override bool Unload()
    {
        CommandRegistry.UnregisterAssembly();
        Harmony.UnpatchSelf();
        return true;
    }
}
```

**Important:** The game world does not exist at plugin load time. Do not access players, entities, or game data in `Load()`.

See [Mod Structure](mod-structure.md) for complete folder breakdown.

### Core.cs - Static Service Locator

The static service locator holds references to game systems and services after the world has loaded.

```csharp
internal static class Core
{
    public static World Server { get; } = GetWorld("Server");
    public static EntityManager EntityManager { get; } = Server.EntityManager;
    public static PrefabCollectionSystem PrefabCollectionSystem { get; internal set; }
    public static GameDataSystem GameDataSystem { get; internal set; }
    public static PlayerStateService Players { get; internal set; }

    internal static void InitializeAfterLoaded()
    {
        PrefabCollectionSystem = Server.GetExistingSystemManaged<PrefabCollectionSystem>();
        Players = new PlayerStateService();
    }
}
```

### One-Shot Initialization

A Harmony patch fires once when the server world finishes booting:

```csharp
[HarmonyPatch(typeof(SpawnTeamSystem_OnPersistenceLoad), nameof(SpawnTeamSystem_OnPersistenceLoad.OnUpdate))]
static class InitializationPatch
{
    static bool _initialized;

    [HarmonyPostfix]
    static void OneShot_AfterLoad()
    {
        if (_initialized) return;
        _initialized = true;
        Core.InitializeAfterLoaded();
    }
}
```

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

1. Download [Visual Studio 2022 Community](https://visualstudio.microsoft.com/)
2. Select **Game development with Unity** workload
3. Add **.NET 6.0 Runtime** and **.NET SDK** individual components

For alternatives, see the [Development Setup Wiki](https://wiki.vrisingmods.com/dev/development_setup.html):
- [JetBrains Rider](https://www.jetbrains.com/rider/) — Full Unity/IL2CPP support
- [dnSpy](https://dnspy.org/), [dotPeek](https://www.jetbrains.com/decompiler/), or [ILSpy](https://github.com/icsharpcode/ILSpy) — Decompilers for `BepInEx/interop`

### Build Commands

```powershell
# Standard build
dotnet build BattleLuck.sln -c Release

# Build without deploying to server
dotnet build BattleLuck.sln -c Release /p:DeployToServer=false /p:GenerateReadme=false
```

### Deploy to Server

Set the `VRISING_SERVER_ROOT` environment variable:

```powershell
$env:VRISING_SERVER_ROOT = "C:\Path\To\VRisingServer"
dotnet build BattleLuck.sln -c Release
```

The `.csproj` includes a `BuildToServer` target that copies the DLL and config.

### Git Setup (Optional)

Create a GitHub account at [github.com/join](https://github.com/join) and use:

- **GitHub Desktop** — Beginner-friendly GUI
- **TortoiseGit** — Windows Explorer integration
- **Git Extensions** — Advanced features with visual history

## IL2CPP Interop

V Rising uses IL2CPP, so interop assemblies in `BepInEx/interop` provide managed signatures for game types.

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