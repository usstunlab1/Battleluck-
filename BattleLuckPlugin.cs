using BattleLuck.Core;
using BattleLuck.Core.Loaders;
using BattleLuck.Core.Validation;
using BattleLuck.ECS.Queries;
using BattleLuck.Services;
using BattleLuck.Services.AI;
using BattleLuck.Services.Modes;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;

// ─────────────────────────────────────────────────────────────────────────────
// BattleLuckPlugin.cs — main plugin entry point.
// ─────────────────────────────────────────────────────────────────────────────

[BepInPlugin(BattleLuckPluginInfo.PluginGuid, BattleLuckPluginInfo.PluginName, BattleLuckPluginInfo.PluginVersion)]
[BepInDependency("gg.deca.VampireCommandFramework")]
public class BattleLuckPlugin : BasePlugin
{
    public new static ManualLogSource? Log { get; private set; }
    // Service state is owned by the Core static service locator (Core/Core.cs).
    // These forwarding properties keep the public API (BattleLuckPlugin.Session, …)
    // stable while the actual references live in Core, per the V Rising modding
    // guide's recommended structure.
    public static GameModeRegistry? GameModes { get => Core.GameModes; private set => Core.GameModes = value; }
    public static SessionController? Session { get => Core.Session; private set => Core.Session = value; }
    public static DevSessionService? DevSession { get => Core.DevSession; internal set => Core.DevSession = value; }
    public static AIAssistant? AIAssistant { get => Core.AIAssistant; private set => Core.AIAssistant = value; }
    public static AiGroupProjectMLlmBridge? AiGroupProjectMBridge { get => Core.AiGroupProjectMBridge; private set => Core.AiGroupProjectMBridge = value; }
    public static NpcControlService? NpcService { get => Core.NpcService; private set => Core.NpcService = value; }
    public static PlayerLoadoutService? PlayerLoadouts { get => Core.PlayerLoadouts; private set => Core.PlayerLoadouts = value; }
    public static PlayerProgressionService? Progression { get => Core.Progression; private set => Core.Progression = value; }
    public static TeleportService? Teleports { get => Core.Teleports; private set => Core.Teleports = value; }
    public static DeathPreventionService? DeathPrevention { get => Core.DeathPrevention; private set => Core.DeathPrevention = value; }
    public static SessionCleanupService? Cleanup { get => Core.Cleanup; private set => Core.Cleanup = value; }
    public static ZoneMapIconService? ZoneMap { get => Core.ZoneMap; private set => Core.ZoneMap = value; }
    public static PlayerEquipmentTrackingService? EquipmentTracker { get => Core.EquipmentTracker; private set => Core.EquipmentTracker = value; }
    public static MerchantCommandService? MerchantCommands { get => Core.MerchantCommands; private set => Core.MerchantCommands = value; }
    public static ClanTaskService? ClanTasks { get => Core.ClanTasks; private set => Core.ClanTasks = value; }
    public static RoadmapService? Roadmap { get => Core.Roadmap; private set => Core.Roadmap = value; }
    static DiscordBridgeController? _discordBridge;
    static AiLoggerController? _aiLogger;
    static WebhookController? _webhookController;
    static ClanTaskGameAdapter? _clanTaskGameAdapter;

    /// <summary>Broadcast a message to all online players (stub — wire to server API).</summary>
    public static Action<string, string>? BroadcastToSession { get; set; }

    static Harmony? _harmony;
    public static Harmony? HarmonyInstance => _harmony;
    static readonly object _initLock = new();
    static EntityQuery? _playerQuery;
    static AiHologramService? _hologramService;
    static LocalAiRuntimeManager? _localAiRuntime;
    public static bool IsInitialized => Core.IsInitialized;
    public static bool IsDiscordBridgeEnabled => _discordBridge != null;

    // BepInEx-style settings are bound here (moved from plugininfo.cs per request)
    // Enums matching acceptable values
    public enum DetourProvider { Default, Dobby, Funchook }
    [System.Flags]
    public enum LogChannel { None = 0, Info = 1, IL = 2, Warn = 4, Error = 8, Debug = 16, All = 31 }
    public enum ConsoleOutRedirectType { Auto, ConsoleOut, StandardOut }
    [System.Flags]
    public enum LogLevel { None = 0, Fatal = 1, Error = 2, Warning = 4, Message = 8, Info = 16, Debug = 32, All = 63 }
    public enum MonoModBackend { auto, dynamicmethod, methodbuilder, cecil }

    // Section: [Caching]
    static ConfigEntry<bool>? EnableAssemblyCache;
    // Section: [Detours]
    static ConfigEntry<DetourProvider>? DetourProviderType;
    // Section: [Harmony.Logger]
    static ConfigEntry<LogChannel>? HarmonyLogChannels;
    // Section: [IL2CPP]
    static ConfigEntry<bool>? UpdateInteropAssemblies;
    static ConfigEntry<string>? UnityBaseLibrariesSource;
    static ConfigEntry<string>? UnhollowerDeobfuscationRegex;
    static ConfigEntry<bool>? ScanMethodRefs;
    static ConfigEntry<bool>? DumpDummyAssemblies;
    static ConfigEntry<string>? IL2CPPInteropAssembliesPath;
    static ConfigEntry<bool>? PreloadIL2CPPInteropAssemblies;
    static ConfigEntry<string>? GlobalMetadataPath;
    // Section: [Logging]
    static ConfigEntry<bool>? UnityLogListening;
    // Section: [Logging.Console]
    static ConfigEntry<bool>? ConsoleEnabled;
    static ConfigEntry<bool>? ConsolePreventClose;
    static ConfigEntry<bool>? ShiftJisEncoding;
    static ConfigEntry<ConsoleOutRedirectType>? StandardOutType;
    static ConfigEntry<LogLevel>? ConsoleLogLevels;
    // Section: [Logging.Disk]
    static ConfigEntry<bool>? DiskAppendLog;
    static ConfigEntry<bool>? DiskEnabled;
    static ConfigEntry<LogLevel>? DiskLogLevels;
    static ConfigEntry<bool>? InstantFlushing;
    static ConfigEntry<int>? ConcurrentFileLimit;
    static ConfigEntry<bool>? WriteUnityLog;
    // Section: [Preloader]
    static ConfigEntry<MonoModBackend>? HarmonyBackend;
    static ConfigEntry<bool>? DumpAssemblies;
    static ConfigEntry<bool>? LoadDumpedAssemblies;
    static ConfigEntry<bool>? BreakBeforeLoadAssemblies;

    static void BindConfig(ConfigFile cfg)
    {
        // Caching
        EnableAssemblyCache = cfg.Bind("Caching", "EnableAssemblyCache", true,
            "Enable/disable assembly metadata cache. Speeds up discovery by caching metadata.");

        // Detours
        DetourProviderType = cfg.Bind("Detours", "DetourProviderType", DetourProvider.Default,
            "The native provider to use for managed detours.");

        // Harmony.Logger
        HarmonyLogChannels = cfg.Bind("Harmony.Logger", "LogChannels", LogChannel.Warn | LogChannel.Error,
            "Specifies which Harmony log channels to listen to.");

        // IL2CPP
        UpdateInteropAssemblies = cfg.Bind("IL2CPP", "UpdateInteropAssemblies", true,
            "Run Il2CppInterop automatically to generate Il2Cpp support assemblies when outdated.");
        UnityBaseLibrariesSource = cfg.Bind("IL2CPP", "UnityBaseLibrariesSource", "https://unity.bepinex.dev/libraries/{VERSION}.zip",
            "URL to the ZIP of managed Unity base libraries.");
        UnhollowerDeobfuscationRegex = cfg.Bind("IL2CPP", "UnhollowerDeobfuscationRegex", string.Empty,
            "RegEx for Il2CppAssemblyUnhollower to rename obfuscated names.");
        ScanMethodRefs = cfg.Bind("IL2CPP", "ScanMethodRefs", true,
            "If enabled, Il2CppInterop will use xref to find dead methods and generate CallerCount.");
        DumpDummyAssemblies = cfg.Bind("IL2CPP", "DumpDummyAssemblies", false,
            "If enabled, BepInEx will save dummy assemblies generated by a Cpp2IL dumper into BepInEx/dummy.");
        IL2CPPInteropAssembliesPath = cfg.Bind("IL2CPP", "IL2CPPInteropAssembliesPath", "{BepInEx}",
            "Path to the folder where IL2CPPInterop assemblies are stored.");
        PreloadIL2CPPInteropAssemblies = cfg.Bind("IL2CPP", "PreloadIL2CPPInteropAssemblies", true,
            "Automatically load all interop assemblies before loading plugins.");
        GlobalMetadataPath = cfg.Bind("IL2CPP", "GlobalMetadataPath", "{GameDataPath}/il2cpp_data/Metadata/global-metadata.dat",
            "Path to the IL2CPP metadata file.");

        // Logging
        UnityLogListening = cfg.Bind("Logging", "UnityLogListening", true,
            "Enables showing unity log messages in the BepInEx logging system.");

        // Logging.Console
        ConsoleEnabled = cfg.Bind("Logging.Console", "Enabled", true, "Enables showing a console for log output.");
        ConsolePreventClose = cfg.Bind("Logging.Console", "PreventClose", false, "Prevent closing the console window.");
        ShiftJisEncoding = cfg.Bind("Logging.Console", "ShiftJisEncoding", true, "If true, console uses Shift-JIS encoding; otherwise UTF-8.");
        StandardOutType = cfg.Bind("Logging.Console", "StandardOutType", ConsoleOutRedirectType.Auto,
            "Hints console manager which handle to assign as StandardOut.");
        ConsoleLogLevels = cfg.Bind("Logging.Console", "LogLevels",
            LogLevel.Fatal | LogLevel.Error | LogLevel.Warning | LogLevel.Message | LogLevel.Info,
            "Which log levels to show in the console output.");

        // Logging.Disk
        DiskAppendLog = cfg.Bind("Logging.Disk", "AppendLog", true, "Appends to the log file on startup.");
        DiskEnabled = cfg.Bind("Logging.Disk", "Enabled", true, "Enables writing log messages to disk.");
        DiskLogLevels = cfg.Bind("Logging.Disk", "LogLevels",
            LogLevel.Fatal | LogLevel.Error | LogLevel.Warning | LogLevel.Message | LogLevel.Info,
            "Which log levels to write to disk.");
        InstantFlushing = cfg.Bind("Logging.Disk", "InstantFlushing", false,
            "Instantly writes any received log entries to disk (performance impact).");
        ConcurrentFileLimit = cfg.Bind("Logging.Disk", "ConcurrentFileLimit", 5,
            "Maximum number of concurrent log files written to disk.");
        WriteUnityLog = cfg.Bind("Logging.Disk", "WriteUnityLog", false,
            "Include Unity log messages in log file output.");

        // Preloader
        HarmonyBackend = cfg.Bind("Preloader", "HarmonyBackend", MonoModBackend.auto,
            "Which MonoMod backend to use for Harmony patches.");
        DumpAssemblies = cfg.Bind("Preloader", "DumpAssemblies", true,
            "If enabled, save patched assemblies into BepInEx/DumpedAssemblies.");
        LoadDumpedAssemblies = cfg.Bind("Preloader", "LoadDumpedAssemblies", true,
            "If enabled, load patched assemblies from BepInEx/DumpedAssemblies instead of memory.");
        BreakBeforeLoadAssemblies = cfg.Bind("Preloader", "BreakBeforeLoadAssemblies", true,
            "If enabled, call Debugger.Break() once before loading patched assemblies.");
    }

    public static void SetAIAssistant(AIAssistant? assistant)
    {
        AIAssistant = assistant;
        if (assistant == null)
        {
            AiGroupProjectMBridge?.Dispose();
            AiGroupProjectMBridge = null;
            return;
        }

        if (AiGroupProjectMBridge == null)
        {
            AiGroupProjectMBridge = new AiGroupProjectMLlmBridge();
            AiGroupProjectMBridge.Initialize(ProjectMEventRouter.Instance);
        }
    }

    public static void SetHologramService(AiHologramService? service)
    {
        AIAssistant?.SetHologramService(service);
    }

    public static void PostToDiscordLogs(string message)
    {
        _discordBridge?.PostToLogs(message);
    }

    public static void PostToDiscordChatVip(string message)
    {
        _discordBridge?.PostToChatVip(message);
    }

    public static bool TryNotifyPlayerBySteamId(ulong steamId, string message)
    {
        if (!VRisingCore.IsReady)
            return false;

        foreach (var player in VRisingCore.GetOnlinePlayers())
        {
            if (!player.Exists() || !player.IsPlayer() || player.GetSteamId() != steamId)
                continue;

            if (FlowController.TryGetUser(player, out var user))
            {
                NotificationHelper.NotifyPlayer(user, message);
                return true;
            }
        }

        return false;
    }

    public static void NotifyPlayerBySteamIdOnMainThread(ulong steamId, string message)
    {
        if (steamId == 0 || string.IsNullOrWhiteSpace(message))
            return;

        MainThreadDispatcher.Enqueue(() =>
        {
            try
            {
                if (!TryNotifyPlayerBySteamId(steamId, message))
                    Log?.LogWarning($"[BattleLuck] Could not notify player {steamId}: player offline or unavailable.");
            }
            catch (Exception ex)
            {
                Log?.LogWarning($"[BattleLuck] Failed to notify player {steamId}: {ex.Message}");
            }
        });
    }

    public override void Load()
    {
        Log = base.Log;

        // Initialize and bind BepInEx .cfg entries locally
        try { BindConfig(Config); }
        catch (Exception cfgEx) { base.Log.LogWarning($"[BattleLuck] Failed to bind config entries: {cfgEx.Message}"); }

        Log.LogInfo($"[BattleLuck] Loading {BattleLuckPluginInfo.PluginName} v{BattleLuckPluginInfo.PluginVersion} ({BattleLuckPluginInfo.PluginGuid})...");

        // Continue normal boot
        ConfigLoader.EnsureDefaultsDeployed();
        ModeConfigLoader.EnsureWatcher();
        SchematicLoader.LoadAll();

        GameModes = new GameModeRegistry();
        RegisterConfiguredModes(GameModes);

        // Initialize the ProjectM event router (Phase 1: listener-only)
        ProjectMEventRouter.Initialize();

        // Apply Harmony patches for death detection and event routing
        _harmony = new Harmony("gg.battleluck.patches");

        // Apply all Harmony patches. PatchAll processes each method individually
        // and skips methods whose target types don't exist, without failing the
        // entire assembly. If PatchAll itself throws (e.g. due to a missing
        // referenced type in the assembly metadata), fall back to patching the
        // critical classes individually so essential hooks still apply.
        var assembly = typeof(DeathHook).Assembly;
        try
        {
            _harmony?.PatchAll(assembly);
            Log?.LogInfo("[BattleLuck] Harmony PatchAll completed successfully.");
        }
        catch (Exception patchAllEx)
        {
            Log?.LogWarning($"[BattleLuck] Harmony PatchAll failed: {patchAllEx.Message}. Falling back to individual class patching...");

            // Fallback: patch critical classes individually so a missing type
            // in one class doesn't prevent the others from being applied.
            var criticalPatches = new[]
            {
                typeof(ServerTickHook),
                typeof(BattleLuck.Patches.ChatMessageSystemPatch),
                typeof(DeathHook),
                typeof(BattleLuckMapVisibilityPatches),
                typeof(InitializationPatch),
                typeof(PlaceTileModelSystemPatch),
                typeof(UnitSpawnerPatch),
                typeof(BattleLuckInventoryEquipmentPatches),
                typeof(ProjectMEventRouterPatches),
                typeof(BattleLuck.Patches.AiTickSequencePatches)
            };

            foreach (var patchType in criticalPatches)
            {
                try
                {
                    _harmony?.CreateClassProcessor(patchType)?.Patch();
                    Log?.LogInfo($"[BattleLuck] Applied patch class: {patchType.Name}");
                }
                catch (Exception classEx)
                {
                    Log?.LogWarning($"[BattleLuck] Failed to patch class '{patchType.Name}': {classEx.Message}. Continuing...");
                }
            }
        }

        BattleLuck.Commands.BattleLuckCommandDispatcher.EnsureScanned();
        CommandRegistry.RegisterAll(typeof(BattleLuckPlugin).Assembly);

        Log.LogInfo($"[BattleLuck] Loaded - {GameModes.GetRegisteredModes().Count} game modes registered. Waiting for server world...");
    }

    static void RegisterConfiguredModes(GameModeRegistry registry)
    {
        var discovered = GameModeRegistry.LoadAllModes();
        var registered = 0;

        foreach (var info in discovered)
        {
            ModeConfig config;
            GameModeEngine mode;
            try
            {
                config = ConfigLoader.Load(info.ModeId);
                mode = new GameModeEngine(config);
            }
            catch (Exception ex)
            {
                Log?.LogWarning($"[BattleLuck] Failed to create GameModeEngine for mode '{info.ModeId}': {ex.Message}");
                continue;
            }

            try
            {
                var issues = new List<string>();
                issues.AddRange(BattleLuck.Core.Validation.ActionRegistryValidator.Validate(info.ModeId, config));
                issues.AddRange(BattleLuck.Core.Validation.ZoneValidator.Validate(info.ModeId, config));
                issues.AddRange(KitValidator.Validate(info.ModeId, config));
                issues.AddRange(PrefabValidator.Validate(info.ModeId, config));
                issues.AddRange(SchematicValidator.Validate(info.ModeId, config));
                issues.AddRange(FlowValidator.Validate(info.ModeId, config));
                issues.AddRange(AnalyticsValidator.Validate(info.ModeId, config));

                foreach (var issue in issues)
                    Log?.LogWarning($"[BattleLuck] Validation warning for mode '{info.ModeId}': {issue}");
            }
            catch (Exception ex)
            {
                Log?.LogWarning($"[BattleLuck] Validation failed for mode '{info.ModeId}': {ex.Message}");
            }

            registry.Register(mode);
            registered++;
            BattleLuckPlugin.LogInfo($"[BattleLuck] Created GameModeEngine for '{info.ModeId}' from declarative config.");
        }

        if (registered > 0)
            return;

        // Fallback: register default modes using GameModeEngine if no declarative config exists
        BattleLuckPlugin.LogInfo("[BattleLuck] No config-based modes found, registering default built-in modes via GameModeEngine.");
        registry.Register(new GameModeEngine(new RulesConfig { ModeId = "bloodbath", DisplayName = "Bloodbath", EnablePvP = true }));
        registry.Register(new GameModeEngine(new RulesConfig { ModeId = "siege", DisplayName = "Siege", EnablePvP = true }));
        registry.Register(new GameModeEngine(new RulesConfig { ModeId = "trials", DisplayName = "Trials", EnablePvP = false, EnableVBloods = true }));
        registry.Register(new GameModeEngine(new RulesConfig { ModeId = "colosseum", DisplayName = "Colosseum", EnablePvP = true }));
        registry.Register(new GameModeEngine(new RulesConfig { ModeId = "aievent", DisplayName = "AI Event Test" }));
    }

    /// <summary>
    /// Called once each server tick. Initializes VRising core on first ready tick,
    /// then drives the session controller.
    /// Must be called from a BepInEx update hook or coroutine.
    /// </summary>
    public static void ServerTick(float deltaSeconds)
    {
        if (!Core.IsInitialized) return;

        // BUGFIX #1: Verify VRisingCore is ready before accessing EntityManager
        if (!VRisingCore.IsReady)
            return;

        // Reset ECB helper to get a fresh command buffer for this tick
        BattleLuck.Services.Flow.EcbHelper.Reset();

        try
        {
            if (_playerQuery == null)
                _playerQuery = VRisingCore.EntityManager.CreateEntityQuery(
                    ComponentType.ReadOnly<PlayerCharacter>());

            EntityQuery pq = (EntityQuery)_playerQuery;
            var entities = pq.ToEntityArray(Unity.Collections.Allocator.Temp);
            List<Entity> players;
            try
            {
                players = new List<Entity>(entities.Length);
                for (int i = 0; i < entities.Length; i++)
                    players.Add(entities[i]);
            }
            finally
            {
                entities.Dispose();
            }

            // BUGFIX #2: Explicit null check for Session instead of null-coalescing
            if (Session == null)
            {
                Log?.LogWarning("[BattleLuck] Session is null in ServerTick");
                return;
            }

            try
            {
                Session.Tick(players, deltaSeconds);
            }
            catch (Exception ex)
            {
                Log?.LogWarning($"[BattleLuck] Tick error in Session.Tick: {ex.Message}");
            }

            try
            {
                MainThreadDispatcher.ProcessQueue();
            }
            catch (Exception ex)
            {
                Log?.LogWarning($"[BattleLuck] Tick error in MainThreadDispatcher.ProcessQueue: {ex.Message}");
            }

            try
            {
                AIAssistant?.ProcessMainThreadQueue();
            }
            catch (Exception ex)
            {
                Log?.LogWarning($"[BattleLuck] Tick error in AIAssistant.ProcessMainThreadQueue: {ex.Message}");
            }

            try
            {
                AIAssistant?.CleanupOldContexts();
            }
            catch (Exception ex)
            {
                Log?.LogWarning($"[BattleLuck] Tick error in AIAssistant.CleanupOldContexts: {ex.Message}");
            }

            try
            {
                AiTaskService.Instance.Tick();
                ConversationStore.Instance.Prune();
            }
            catch (Exception ex)
            {
                Log?.LogWarning($"[BattleLuck] Tick error in AI task/history cleanup: {ex.Message}");
            }

            try
            {
                _hologramService?.UpdateHolograms();
            }
            catch (Exception ex)
            {
                Log?.LogWarning($"[BattleLuck] Tick error in HologramService.UpdateHolograms: {ex.Message}");
            }

            try
            {
                _discordBridge?.DrainMainThreadQueue();
            }
            catch (Exception ex)
            {
                Log?.LogWarning($"[BattleLuck] Tick error in _discordBridge.DrainMainThreadQueue: {ex.Message}");
            }

            try
            {
                _webhookController?.DrainMainThreadQueue();
            }
            catch (Exception ex)
            {
                Log?.LogWarning($"[BattleLuck] Tick error in _webhookController.DrainMainThreadQueue: {ex.Message}");
            }

            try
            {
                _aiLogger?.Tick();
            }
            catch (Exception ex)
            {
                Log?.LogWarning($"[BattleLuck] Tick error in _aiLogger.Tick: {ex.Message}");
            }

            try
            {
                FloorLockService.Tick(players);
            }
            catch (Exception ex)
            {
                Log?.LogWarning($"[BattleLuck] Tick error in FloorLockService.Tick: {ex.Message}");
            }

            // Tick dev session timeout checker
            try
            {
                DevSession?.Tick();
            }
            catch (Exception ex)
            {
                Log?.LogWarning($"[BattleLuck] Tick error in DevSession.Tick: {ex.Message}");
            }

            try
            {
                NpcService?.Tick(deltaSeconds);
            }
            catch (Exception ex)
            {
                Log?.LogWarning($"[BattleLuck] Tick error in NpcService.Tick: {ex.Message}");
            }

            try
            {
                EquipmentTracker?.Tick();
            }
            catch (Exception ex)
            {
                Log?.LogWarning($"[BattleLuck] Tick error in EquipmentTracker.Tick: {ex.Message}");
            }

            try
            {
                MerchantCommands?.Tick(players);
            }
            catch (Exception ex)
            {
                Log?.LogWarning($"[BattleLuck] Tick error in MerchantCommands.Tick: {ex.Message}");
            }

            try
            {
                ClanTasks?.Tick(deltaSeconds);
            }
            catch (Exception ex)
            {
                Log?.LogWarning($"[BattleLuck] Tick error in ClanTasks.Tick: {ex.Message}");
            }

            // Do not auto-create castle hearts during event ticks. Real castle
            // tile paths may attach to an existing admin castle, but event entry
            // must never queue a CastleHeart BuildTileModelEvent.
        }
        catch (Exception ex)
        {
            Log?.LogWarning($"[BattleLuck] Tick error: {ex.Message}");
        }
    }

    public static bool TryInitializeCore()
    {
        lock (_initLock)
        {
            if (Core.IsInitialized)
                return true;

            try
            {
                // BUGFIX #3: Config/Env loading inside lock to prevent race condition
                Env.LoadFromConfigRoot();

                VRisingCore.Initialize();
                if (!VRisingCore.IsReady)
                    return false;

                BroadcastToSession ??= (_, message) => NotificationHelper.NotifyAll(message, NotificationHelper.NotificationLevel.Info);

                var playerState = new PlayerStateController();
                PlayerLoadouts = new PlayerLoadoutService(playerState);
                Progression = new PlayerProgressionService();
                Teleports = new TeleportService();
                DeathPrevention = new DeathPreventionService();
                GameEvents.OnPlayerLeft += evt => DeathPrevention?.Disarm(evt.SteamId);
                GameEvents.OnModeEnded += _ => DeathPrevention?.Clear();
                var flow = new FlowController(playerState, GameModes!);
                var zoneDetection = new ZoneDetectionSystem();
                zoneDetection.Initialize();

                Session = new SessionController(GameModes!, playerState, flow, zoneDetection);
                Session.Initialize();

                ZoneMap = new ZoneMapIconService();
                ZoneMap.Initialize(GameModes!);
                Log?.LogInfo("[BattleLuck] Zone map icon service initialized.");

                EquipmentTracker = new PlayerEquipmentTrackingService();
                Log?.LogInfo("[BattleLuck] Equipment tracking service initialized.");

                MerchantCommands = new MerchantCommandService();
                MerchantCommands.Configure(ConfigLoader.LoadMerchantCommandConfig());
                Log?.LogInfo("[BattleLuck] Merchant command service initialized.");

                ClanTasks = new ClanTaskService(Path.Combine(ConfigLoader.ConfigRoot, "clan_tasks.json"), LogWarning);
                ClanTasks.Initialize();
                _clanTaskGameAdapter = new ClanTaskGameAdapter(ClanTasks, playerState, GameModes);
                Log?.LogInfo("[BattleLuck] Clan task service initialized.");

                Roadmap = new RoadmapService();
                Roadmap.Initialize();
                Log?.LogInfo("[BattleLuck] Roadmap and role prompt service initialized.");

                // Initialize Dev Session Service
                DevSession = new DevSessionService(playerState, flow);
                Log?.LogInfo("[BattleLuck] Dev session service initialized.");

                // Initialize generic NPC control for live admin commands and event actions.
                NpcService = new NpcControlService();
                Log?.LogInfo("[BattleLuck] NPC control service initialized.");

                // Despawn tracked event NPCs on mode end. Player entities are guarded inside the service.
                GameEvents.OnModeEnded += evt => NpcService?.DespawnSession(evt.SessionId);

                // Initialize the session cleanup service (destroys walls / floors / bosses / NPCs
                // / platforms / items / projectiles / traps in the zone, and strips transient
                // buffs from players, on session/mode end).
                Cleanup = new SessionCleanupService();
                Log?.LogInfo("[BattleLuck] Session cleanup service initialized.");

                // Initialize Discord bridge companion endpoint
                try
                {
                    var discordConfig = ConfigLoader.LoadDiscordBridgeConfig();
                    if (discordConfig?.Enabled == true)
                    {
                        _discordBridge = new DiscordBridgeController();
                        _discordBridge.Configure(discordConfig);
                        _discordBridge.Start();
                        Log?.LogInfo($"[BattleLuck] Discord bridge enabled on port {discordConfig.Port}");
                    }
                    else
                    {
                        Log?.LogInfo("[BattleLuck] Discord bridge disabled in configuration");
                    }
                }
                catch (Exception discordEx)
                {
                    Log?.LogWarning($"[BattleLuck] Failed to initialize Discord bridge: {discordEx.Message}");
                }

                // Initialize external webhook endpoint
                try
                {
                    var webhookConfig = ConfigLoader.LoadWebhookConfig();
                    if (webhookConfig?.Enabled == true)
                    {
                        _webhookController = new WebhookController();
                        _webhookController.Configure(webhookConfig);
                        _webhookController.Start();
                    }
                    else
                    {
                        Log?.LogInfo("[BattleLuck] Webhook endpoint disabled in configuration");
                    }
                }
                catch (Exception webhookEx)
                {
                    Log?.LogWarning($"[BattleLuck] Failed to initialize webhook endpoint: {webhookEx.Message}");
                }

                // Initialize AI Assistant
                try
                {
                    var aiConfig = ConfigLoader.LoadAIConfig();

                    // Wire Discord webhook for full mod log forwarding
                    BattleLuckLogger.SetDiscordWebhook(aiConfig.Messaging.DiscordWebhookUrl);

                    var provider = aiConfig.Provider.ToLowerInvariant();

                    if (aiConfig.Enabled)
                    {
                        _localAiRuntime?.Dispose();
                        _localAiRuntime = new LocalAiRuntimeManager();
                        _localAiRuntime.Start(aiConfig);

                        _hologramService = new AiHologramService(VRisingCore.EntityManager);
                        AIAssistant = new AIAssistant();
                        AIAssistant.Initialize(aiConfig);
                        // Wire hologram service to AIAssistant
                        SetHologramService(_hologramService);

                        AiGroupProjectMBridge?.Dispose();
                        AiGroupProjectMBridge = new AiGroupProjectMLlmBridge();
                        AiGroupProjectMBridge.Initialize(ProjectMEventRouter.Instance);

                        var providerSummary = AIAssistant.ActiveProvider.Equals("local", StringComparison.OrdinalIgnoreCase)
                            ? "Local AI fallback"
                            : provider == "auto"
                                ? $"Auto AI provider ({AIAssistant.ActiveProvider})"
                                : provider == "llama" || provider == "llama_api" || provider == "meta_llama"
                                    ? $"Local Llama ({aiConfig.LlamaAPI.Model} @ {aiConfig.LlamaAPI.BaseUrl})"
                                    : provider == "cloudflare"
                                        ? $"Cloudflare AI Workers ({aiConfig.CloudflareAI.Model})"
                                        : AIAssistant.IsSidecarConfigured
                                                ? $"Google AI Studio + battle sidecar ({AIAssistant.SidecarBaseUrl})"
                                                : "Google AI Studio";

                        var chain = new StringBuilder();
                        chain.Append(providerSummary);
                        if (provider == "cloudflare" && aiConfig.GoogleAIStudio.FallbackModels.Count > 0 && !string.IsNullOrWhiteSpace(aiConfig.GoogleAIStudio.ApiKey))
                            chain.Append(" | failover: Google AI Studio (");
                        if (provider == "google" && aiConfig.GoogleAIStudio.FallbackModels.Count > 0)
                            chain.Append(" | fallbacks: ").Append(string.Join(", ", aiConfig.GoogleAIStudio.FallbackModels));
                        if (AIAssistant.IsSidecarConfigured)
                            chain.Append(" | sidecar: on");
                        if (AIAssistant.IsMCPRuntimeHealthy)
                            chain.Append(" | mcp: ").Append(AIAssistant.MCPServerCount).Append(" server(s)");

                        Log?.LogInfo($"[BattleLuck] AI Assistant initialized successfully with {chain}");
                    }
                    else
                    {
                        Log?.LogInfo("[BattleLuck] AI Assistant disabled in configuration");
                    }
                }
                catch (Exception aiEx)
                {
                    Log?.LogWarning($"[BattleLuck] Failed to initialize AI Assistant: {aiEx.Message}");
                }

                // Initialize AI Logger (game event → AI summary → Discord webhook)
                try
                {
                    var aiLoggerConfig = ConfigLoader.LoadAiLoggerConfig();
                    if (aiLoggerConfig?.Enabled == true)
                    {
                        _aiLogger = new AiLoggerController();
                        _aiLogger.Configure(aiLoggerConfig);
                        Log?.LogInfo("[BattleLuck] AI Logger initialized");
                    }
                }
                catch (Exception loggerEx)
                {
                    Log?.LogWarning($"[BattleLuck] Failed to initialize AI Logger: {loggerEx.Message}");
                }

                // Initialize ECS Query Registry (cached queries for performance)
                try
                {
                    QueryRegistry.Initialize(VRisingCore.EntityManager);
                    Log?.LogInfo("[BattleLuck] ECS Query Registry initialized.");
                }
                catch (Exception ecsEx)
                {
                    Log?.LogWarning($"[BattleLuck] Failed to initialize ECS Query Registry: {ecsEx.Message}");
                }

                // Resolve live buff GUIDs (must run after VRisingCore.Initialize)
                PrefabHelper.ScanLivePrefabs();
                Prefabs.ResolveLiveBuffGuids();
                foreach (var modeId in GameModes?.GetRegisteredModes() ?? Array.Empty<string>())
                {
                    try
                    {
                        var liveConfig = ConfigLoader.Load(modeId);
                        foreach (var issue in PrefabValidator.ValidateLive(modeId, liveConfig))
                            Log?.LogWarning($"[BattleLuck] Live validation warning for mode '{modeId}': {issue}");
                    }
                    catch (Exception prefabAuditEx)
                    {
                        Log?.LogWarning($"[BattleLuck] Live prefab validation failed for mode '{modeId}': {prefabAuditEx.Message}");
                    }
                }

                // Building restrictions are not toggled automatically by events.
                // Manual admin free-build commands remain available when needed.

                // On mode end, sweep the zone and destroy every non-player entity
                // (walls, floors, platforms, bosses, NPCs, items, projectiles, traps)
                // and strip transient buffs from any player still in the zone.
                GameEvents.OnModeEnded += HandleModeEndedCleanup;

                // Wire VFX sequences to action events
                GameEvents.OnActionPerformed += HandleActionVFX;

                Log?.LogInfo("[BattleLuck] V Rising core initialized. Session controller active.");
                Core.IsInitialized = true;
                return true;
            }
            catch (Exception ex)
            {
                Log?.LogError($"[BattleLuck] TryInitializeCore failed: {ex}");
                return false;
            }
        }
    }

    public override bool Unload()
    {
        BuildingRestrictionController.Reset();
        FloorLockService.Reset();
        NpcService?.DespawnAll();
        NpcService = null;
        DeathPrevention?.Dispose();
        DeathPrevention = null;
        PlayerLoadouts = null;
        Progression = null;
        Teleports = null;
        EquipmentTracker?.RestoreAll();
        EquipmentTracker = null;
        MerchantCommands = null;
        _clanTaskGameAdapter?.Dispose();
        _clanTaskGameAdapter = null;
        ClanTasks?.Dispose();
        ClanTasks = null;
        Roadmap?.Dispose();
        Roadmap = null;
        ZoneMap?.Shutdown();
        ZoneMap = null;
        AiGroupProjectMBridge?.Dispose();
        AiGroupProjectMBridge = null;
        AIAssistant?.Shutdown();
        AIAssistant = null;
        _localAiRuntime?.Dispose();
        _localAiRuntime = null;
        _discordBridge?.Dispose();
        _discordBridge = null;
        _webhookController?.Dispose();
        _webhookController = null;
        _aiLogger?.Dispose();
        _aiLogger = null;
        Session?.Shutdown();

        // Do not mutate unrelated world state during plugin unload. Individual
        // services above are responsible for removing entities they own.
        Cleanup = null;

        // Shutdown ECS Query Registry
        try
        {
            QueryRegistry.Shutdown();
        }
        catch (Exception ecsEx)
        {
            Log?.LogWarning($"[BattleLuck] Failed to shutdown ECS Query Registry: {ecsEx.Message}");
        }

        // Shutdown the ProjectM event router
        ProjectMEventRouter.Shutdown();

        CommandRegistry.UnregisterAssembly(typeof(BattleLuckPlugin).Assembly);
        _harmony?.UnpatchSelf();
        GameEvents.Shutdown();
        VRisingCore.Reset();
        _playerQuery = null;
        lock (_initLock)
        {
            Core.IsInitialized = false;
        }
        Log?.LogInfo("[BattleLuck] Unloaded.");
        return true;
    }

    public static void LogInfo(string msg)    { Log?.LogInfo(msg);    BattleLuckLogger.Post_Internal("INFO",    msg); }
    public static void LogWarning(string msg) { Log?.LogWarning(msg); BattleLuckLogger.Post_Internal("WARNING", msg); }
    public static void LogError(string msg)   { Log?.LogError(msg);   BattleLuckLogger.Post_Internal("ERROR",   msg); }

    static void HandleActionVFX(ActionPerformedEvent e)
    {
        try
        {
            if (e.PlayerEntity.HasValue && e.PlayerEntity.Value.Exists())
            {
                var seq = ActionSequences.GetVFX(e.Action);
                e.PlayerEntity.Value.PlaySequence(seq, label: $"action_{e.Action}");
            }
        }
        catch (Exception ex)
        {
            Log?.LogWarning($"[BattleLuck] VFX error for {e.Action}: {ex.Message}");
        }
    }

    /// <summary>Mode-ended safety net for entities explicitly tracked to the event session.</summary>
    static void HandleModeEndedCleanup(ModeEndedEvent evt)
    {
        try
        {
            var result = SchematicLoader.ClearTrackingGroup(evt.SessionId);
            if (!result.Success)
                Log?.LogWarning($"[BattleLuck] Mode-ended tracked schematic cleanup failed for '{evt.SessionId}': {result.Error}");
        }
        catch (Exception ex)
        {
            Log?.LogWarning($"[BattleLuck] Mode-ended cleanup failed: {ex.Message}");
        }
    }
}
