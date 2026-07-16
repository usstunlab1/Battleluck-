# Mod Structure

Understanding the role of each folder prevents confusion and keeps BattleLuck organized. This follows the standard V Rising mod structure from the [V Rising Mod Wiki](https://wiki.vrisingmods.com/dev/mod-structure.html).

## `BattleLuckPlugin.cs`

**What this is:** The BepInEx entry point. Every mod has exactly one.

The class decorated with `[BepInPlugin]` is what BepInEx finds and loads. It overrides `Load()` which is called once at startup. This is where you wire everything together: Harmony patches, VCF registration, config file setup.

```csharp
[BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
[BepInDependency("gg.deca.VampireCommandFramework")]
public class Plugin : BasePlugin
{
    internal static Harmony Harmony = new(MyPluginInfo.PLUGIN_GUID);
    internal static Plugin Instance;

    public override void Load()
    {
        Instance = this;
        Harmony.PatchAll(Assembly.GetExecutingAssembly());
        CommandRegistry.RegisterAll();
        Log.LogInfo($"{MyPluginInfo.PLUGIN_NAME} loaded.");
    }

    public override bool Unload()
    {
        CommandRegistry.UnregisterAssembly();
        Harmony.UnpatchSelf();
        return true;
    }
}
```

**Keep `BattleLuckPlugin.cs` thin.** It initialises the framework; it does not contain business logic.

## `Core/` - Static Service Locator

**What this is:** The static service locator for the mod. Referenced everywhere.

`Core` is a static class that holds references to game systems and services after the world has loaded. It exposes them as static properties so commands and patches can reach them without passing instances around.

```csharp
internal static class Core
{
    public static World Server { get; } = GetWorld("Server")
        ?? throw new Exception("Server world not found.");
    public static EntityManager EntityManager { get; } = Server.EntityManager;

    // Game systems, resolved once in InitializeAfterLoaded()
    public static PrefabCollectionSystem PrefabCollectionSystem { get; internal set; }
    public static GameDataSystem GameDataSystem { get; internal set; }

    // Your own services
    public static PlayerStateService Players { get; internal set; }
    public static SessionController Session { get; internal set; }

    internal static void InitializeAfterLoaded()
    {
        PrefabCollectionSystem = Server.GetExistingSystemManaged<PrefabCollectionSystem>();
        GameDataSystem = Server.GetExistingSystemManaged<GameDataSystem>();
        Players = new PlayerStateService();
        Session = new SessionController();
        Plugin.Instance.Log.LogInfo("Core initialised.");
    }

    static World GetWorld(string name)
        => World.s_AllWorlds.ToArray().FirstOrDefault(w => w.Name == name);
}
```

The one-shot init patch that calls this typically lives in `Patches/InitializationPatch.cs`:

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

This pattern ensures services are only set up once and only after the game is ready for them.

## `Commands/` - Chat Commands

**What goes here:** Your chat command classes: the things players type in chat.

Commands are the primary interface between your mod and players. Each command class is decorated with `[CommandGroup]` and each method inside with `[Command]`. VCF automatically registers these through `CommandRegistry.RegisterAll()`.

```csharp
[CommandGroup("battleluck", "bl")]
internal static class BattleLuckCommands
{
    [Command("reload")]
    public static void ReloadCommand(ChatCommandContext ctx)
    {
        ConfigLoader.Reload();
        ctx.Reply("Configuration reloaded.");
    }
}
```

**Rules:**

- Methods must be `public static void`
- First parameter must be `ChatCommandContext ctx` (not `ICommandContext`)
- Parameters after `ctx` become the command's arguments (VCF handles parsing)
- A parameter named `string _remainder` captures everything to end-of-line

## `Converters/` - Custom Argument Parsing

**What goes here:** Custom argument parsers that let VCF understand non-primitive types.

When a command takes a `FoundPlayer` or `FoundItem` parameter, VCF doesn't know how to turn the raw chat string into that type. A converter teaches it.

```csharp
public record FoundPlayer(PlayerContext Value);

internal class FoundPlayerConverter : CommandArgumentConverter<FoundPlayer>
{
    public override FoundPlayer Parse(ICommandContext ctx, string input)
    {
        if (Core.Players.TryFindName(input, out var data))
            return new FoundPlayer(data);
        throw ctx.Error($"'{input}' not found.");
    }
}
```

VCF auto-discovers converters at startup via `CommandRegistry.RegisterAll()`.

## `Services/` - Business Logic Layer

**What goes here:** Classes that do the actual work.

Services encapsulate ECS queries and game interactions so commands stay thin. Instead of writing 50 lines of ECS in a command method, call `Core.Players.Find(...)`.

```csharp
// Core exposes services as static properties
public static PlayerStateService Players { get; internal set; }

// Commands call services cleanly
[Command("kick")]
public static void KickCommand(ChatCommandContext ctx, FoundPlayer target)
{
    Core.Session.KickPlayer(target.Value.UserEntity);
    ctx.Reply($"Kicked {target.Value.CharacterName}");
}
```

**Key rules:**

- Services are instantiated in `Core.InitializeAfterLoaded()`, not in constructors (world may not be ready)
- Constructor creates EntityQueries and reads prefabs
- Methods do the actual ECS reads/writes
- Never put ECS logic directly in command methods

### BattleLuck Services

| Folder | Purpose |
|--------|---------|
| `Services/Flow/` | Flow action execution engine |
| `Services/Zone/` | Zone management (shrink, borders, platforms, objectives) |
| `Services/Spawn/` | Entity spawning and loot |
| `Services/Modes/` | Game mode implementations (Bloodbath, Colosseum, etc.) |
| `Services/Runtime/` | MCP runtime integration and DTOs |

## `Patches/` - Harmony Patches

**What goes here:** Harmony patches: code that hooks into existing game methods.

Harmony lets you inject code before (Prefix) or after (Postfix) any game method without modifying game files.

```csharp
[HarmonyPatch(typeof(ServerBootstrapSystem), nameof(ServerBootstrapSystem.OnUserConnected))]
public static class PlayerConnectPatch
{
    [HarmonyPostfix]
    public static void Postfix(ServerBootstrapSystem __instance, NetConnectionId netConnectionId)
    {
        var userIndex = __instance._NetEndPointToApprovedUserIndex[netConnectionId];
        var serverClient = __instance._ApprovedUsersLookup[userIndex];
        Core.Players.UpdatePlayerCache(serverClient.UserEntity, ...);
    }
}
```

**Patch types:**

- `[HarmonyPrefix]` — runs before the original method
- `[HarmonyPostfix]` — runs after the original. Use `__result` to read/modify the return value

**All patches are applied automatically** via `_harmony.PatchAll(Assembly.GetExecutingAssembly())` in `Load()`.

## `Models/` - Data Structures

**What goes here:** Plain data structures: enums, records, and structs.

No ECS logic. No game calls. Just data.

```csharp
public enum GameModeType
{
    Bloodbath,
    Colosseum,
    Gauntlet,
    Siege,
    Trials
}
```

## `Utilities/` - Helper Methods

**What goes here:** Stateless helper methods and extension methods.

Extension methods provide fluent access to ECS operations:

```csharp
public static class EntityExtensions
{
    public static bool Has<T>(this Entity entity) where T : struct
        => Core.EntityManager.HasComponent<T>(entity);

    public static T Read<T>(this Entity entity) where T : struct
        => Core.EntityManager.GetComponentData<T>(entity);

    public static void Write<T>(this Entity entity, T data) where T : struct
        => Core.EntityManager.SetComponentData(entity, data);
}
```

## `Data/` - Static Data

**What goes here:** Static data: `PrefabGUID` constants and embedded JSON files.

```csharp
// Data/Prefabs.cs
public static class Prefabs
{
    public static PrefabGUID Admin_Invulnerable_Buff = new(-480024072);
    public static PrefabGUID VBlood_Alpha_Wolf = new(-123456789);
}
```

Use named constants instead of raw integers scattered across code.

## `ECS/` - ECS Action Infrastructure

**What goes here:** BattleLuck's custom ECS action system.

The ECS action layer builds on top of the game's ECS:

- `ECS/Actions/Components/` — Action component definitions
- `ECS/Actions/Systems/` — Action processing systems
- `ECS/Flow/` — Flow compiler for action strings
- `ECS/Queries/` — Cached EntityQuery registry

## `config/BattleLuck/` - Runtime Config

**What goes here:** Embedded configuration copied to server at build time.

Each game mode has its own folder with `session.json`, `zones.json`, and `kit.json`. Global configs include `ai_config.json`, `discord_bridge.json`, and `webhook.json`.

## Related Documentation

- [Harmony Patching](harmony-patches.md) — Detailed hooking patterns
- [CI/CD](ci.md) — Build and test workflow