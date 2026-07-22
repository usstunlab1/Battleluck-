using BepInEx;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using ProjectM;
using Unity.Entities;
using Unity.Mathematics;
using BattleLuck.Core;
using BattleLuck.Models;
using BattleLuck.Utilities;
using BattleLuck.ECS.Queries;
using BattleLuck.Services;
using BattleLuck.Services.Castles;
using BattleLuck.Services.Runtime;
using BattleLuck.Services.Flow;
using BattleLuck.Services.AI;
using BattleLuck.Services.Modes;
using BattleLuck.Core.Loaders;
using BattleLuck.Core.Validation;
using BattleLuck.Services.Companion;
using BattleLuck.Services.Encounter;
using BattleLuck.Services.Boss;
using BattleLuck.Services.Portal;
using BattleLuck.Services.Creature;
using BattleLuck.Services.Diagnostics;
using VampireCommandFramework;

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
    public static PlayerStateController? PlayerState { get => Core.PlayerState; private set => Core.PlayerState = value; }
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
    public static CastlePolicyService? CastlePolicy { get => Core.CastlePolicy; private set => Core.CastlePolicy = value; }
    public static ServerEventPlatform? EventPlatform { get => Core.EventPlatform; private set => Core.EventPlatform = value; }
    public static PlayerDirectoryService? PlayerDirectory { get => Core.PlayerDirectory; private set => Core.PlayerDirectory = value; }
    public static IErrorReporter ErrorReporter { get => Core.ErrorReporter; private set => Core.ErrorReporter = value; }
    // ── Wave 1: New expansion services ──────────────────────────────────────────
    public static CompanionService? Companion { get => Core.Companion; private set => Core.Companion = value; }
    public static EncounterService? Encounters { get => Core.Encounters; private set => Core.Encounters = value; }
    public static BossScalingService? BossScaling { get => Core.BossScaling; private set => Core.BossScaling = value; }
    public static PortalService? Portals { get => Core.Portals; private set => Core.Portals = value; }
    public static CreatureCaptureService? CreatureCapture { get => Core.CreatureCapture; private set => Core.CreatureCapture = value; }
    static ClanTaskGameAdapter? _clanTaskGameAdapter;

    /// <summary>Broadcast a message to all online players (stub — wire to server API).</summary>
    public static Action<string, string>? BroadcastToSession { get; set; }

    static Harmony? _harmony;
    public static Harmony? HarmonyInstance => _harmony;
    static readonly object _initLock = new();
    static bool _coreInitializationInProgress;
    static EntityQuery? _playerQuery;
    public static bool IsInitialized => Core.IsInitialized;
    public static bool IsDiscordBridgeEnabled => BattleLuckLogger.IsDiscordForwardingEnabled;

    public static void SetAIAssistant(AIAssistant? assistant)
    {
        AIAssistant = assistant;
    }

    public static void PostToDiscordLogs(string message) => BattleLuckLogger.Forward("INFO", message);
    public static void PostToDiscordChatVip(string message) { }

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
        Log.LogInfo("[BattleLuck] Loading...");
        PluginSettings.Initialize(Config);
        ConfigLoader.EnsureDefaultsDeployed();
        // Secrets remain outside owner JSON and the plugin binary. Load the
        // optional local environment file early enough for startup diagnostics.
        Env.LoadFromConfigRoot();
        BattleLuckLogger.SetDiscordWebhook(Env.Get("BATTLELUCK_DISCORD_WEBHOOK_URL"));
        var ownerConfig = ConfigLoader.LoadBattleLuckConfig();
        ErrorReporter = ownerConfig.Backtrace.Enabled
            ? new BacktraceHttpErrorReporter(ownerConfig.Backtrace,
                Env.Get("BATTLELUCK_BACKTRACE_SUBMISSION_TOKEN"))
            : NoOpErrorReporter.Instance;
        ModeConfigLoader.EnsureWatcher();
        SchematicLoader.LoadAll();

        var gameModes = new GameModeRegistry();
        GameModes = gameModes;
        RegisterConfiguredModes(gameModes);

        // Initialize the typed router and bridge death events to legacy
        // DeathHook subscribers. The router owns the sole death-system postfix.
        var projectMEventRouter = ProjectMEventRouter.Initialize();
        DeathHook.Initialize(projectMEventRouter);

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
                // HarmonyX may throw a message containing "Could not find method" when it fails.
                // Log it as a warning but proceed to fallback.
                Log?.LogWarning($"[BattleLuck] Harmony PatchAll encountered issues: {patchAllEx.Message}");
                Log?.LogInfo("[BattleLuck] Falling back to individual class patching...");

                // PatchAll may install several classes before a later target
                // fails. Clear only this plugin's Harmony owner before applying
                // the fallback set so successful partial patches do not run twice.
                var fallbackCanProceed = true;
                try
                {
                    _harmony?.UnpatchSelf();
                }
                catch (Exception unpatchEx)
                {
                    fallbackCanProceed = false;
                    Log?.LogError($"[BattleLuck] Could not clear partial Harmony patches; fallback skipped to avoid duplicate hooks: {unpatchEx.Message}");
                }

                // Fallback: patch critical classes individually so a missing type
                // in one class doesn't prevent the others from being applied.
                var criticalPatches = new[]
                {
                    typeof(ServerTickHook),
                    typeof(BattleLuck.Patches.ChatMessageSystemPatch),
                    typeof(BattleLuckMapVisibilityPatches),
                    typeof(InitializationPatch),
                    typeof(PlaceTileModelSystemPatch),
                    typeof(UnitSpawnerPatch),
                    typeof(BattleLuckInventoryEquipmentPatches),
                    typeof(ProjectMEventRouterPatches)
                };

                foreach (var patchType in fallbackCanProceed ? criticalPatches : Array.Empty<Type>())
                {
                    try
                    {
                        _harmony?.CreateClassProcessor(patchType)?.Patch();
                        Log?.LogInfo($"[BattleLuck] Applied patch class: {patchType.Name}");
                    }
                    catch (Exception classEx)
                    {
                        // If it's the known failing method, log it as an info/debug message instead of a warning
                        // if we want to be less noisy, but for now warning is fine as it's an actual failure.
                        Log?.LogWarning($"[BattleLuck] Failed to patch class '{patchType.Name}': {classEx.Message}");
                    }
                }
            }

        BattleLuck.Commands.BattleLuckCommandDispatcher.EnsureScanned();

        Log?.LogInfo($"[BattleLuck] Loaded - {gameModes.GetRegisteredModes().Count} game modes registered. Waiting for server world...");
    }

    static void RegisterConfiguredModes(GameModeRegistry registry)
    {
        var discovered = GameModeRegistry.LoadAllModes();
        var registered = 0;

        foreach (var info in discovered)
        {
            if (!PluginSettings.IsEventModeEnabled(info.ModeId))
            {
                Log?.LogInfo($"[BattleLuck] Event mode '{info.ModeId}' disabled by gg.battleluck.cfg.");
                continue;
            }

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
                issues.AddRange(ZoneValidator.Validate(info.ModeId, config));
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

        if (registered > 0 || !PluginSettings.EventsEnabled)
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

            try
            {
                CastlePolicy?.Tick(deltaSeconds);
            }
            catch (Exception ex)
            {
                Log?.LogWarning($"[BattleLuck] Tick error in CastlePolicy.Tick: {ex.Message}");
            }

            // Do not auto-create castle hearts during event ticks. Real castle
            // tile paths may attach to an existing admin castle, but event entry
            // must never queue a CastleHeart BuildTileModelEvent.
        }
        catch (Exception ex)
        {
            Log?.LogWarning($"[BattleLuck] Tick error: {ex.Message}");
            ErrorReporter.Report(ex, new ErrorReportContext { Action = "server.tick", Critical = true });
        }
    }

    public static bool TryInitializeCore()
    {
        lock (_initLock)
        {
            if (Core.IsInitialized)
                return true;
            if (_coreInitializationInProgress)
                return false;

            _coreInitializationInProgress = true;

            try
            {
                // BUGFIX #3: Config/Env loading inside lock to prevent race condition
                Env.LoadFromConfigRoot();

                VRisingCore.Initialize();
                if (!VRisingCore.IsReady)
                    return false;

                BroadcastToSession ??= (_, message) => NotificationHelper.NotifyAll(message, NotificationHelper.NotificationLevel.Info);

                var playerState = new PlayerStateController();
                PlayerState = playerState;
                PlayerLoadouts = new PlayerLoadoutService(playerState);
                Progression = new PlayerProgressionService();
                Teleports = new TeleportService();
                DeathPrevention = new DeathPreventionService();
                GameEvents.OnPlayerLeft += HandlePlayerLeftDeathPrevention;
                GameEvents.OnModeEnded += HandleModeEndedDeathPrevention;
                var flow = new FlowController(playerState, GameModes!);
                var zoneDetection = new ZoneDetectionSystem();
                zoneDetection.Initialize(GameModes!);

                Session = new SessionController(GameModes!, playerState, flow, zoneDetection);
                Session.Initialize();

                var ownerConfig = ConfigLoader.LoadBattleLuckConfig();
                EventPlatform = new ServerEventPlatform(Path.Combine(ConfigLoader.ConfigRoot, "runtime"), ownerConfig.Results.Keep);
                Core.EventNormalizer?.Dispose();
                Core.EventNormalizer = new GameEventNormalizer(EventPlatform, ProjectMEventRouter.Instance);
                PlayerDirectory = new PlayerDirectoryService();
                PlayerDirectory.Rebuild(VRisingCore.EntityManager);
                Log?.LogInfo("[BattleLuck] Canonical event ledger, scoring, and results platform initialized.");

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
                GameEvents.OnModeEnded += HandleModeEndedNpcDespawn;

                // ── Wave 1: New expansion services ──────────────────────────────────────
                Companion = new CompanionService(NpcService);
                Log?.LogInfo("[BattleLuck] Companion service initialized.");

                Encounters = new EncounterService(NpcService);
                Log?.LogInfo("[BattleLuck] Encounter service initialized.");

                BossScaling = new BossScalingService();
                Log?.LogInfo("[BattleLuck] Boss scaling service initialized.");

                Portals = new PortalService();
                Log?.LogInfo("[BattleLuck] Portal service initialized.");

                CreatureCapture = new CreatureCaptureService(NpcService);
                Log?.LogInfo("[BattleLuck] Creature capture service initialized.");

                // Initialize the session cleanup service (destroys walls / floors / bosses / NPCs
                // / platforms / items / projectiles / traps in the zone, and strips transient
                // buffs from players, on session/mode end).
                Cleanup = new SessionCleanupService();
                Log?.LogInfo("[BattleLuck] Session cleanup service initialized.");

                // Initialize AI Assistant
                try
                {
                    var aiConfig = ConfigLoader.LoadAIConfig();

                    if (aiConfig.Enabled)
                    {
                        AIAssistant = new AIAssistant();
                        AIAssistant.Initialize(aiConfig);
                        Log?.LogInfo($"[BattleLuck] Optional local assistant initialized: {aiConfig.LlamaAPI.Model} at loopback endpoint; AI-lite remains authoritative fallback.");
                    }
                    else
                    {
                        Log?.LogInfo("[BattleLuck] AI Assistant disabled in configuration");
                    }
                }
                catch (Exception aiEx)
                {
                    try { AIAssistant?.Shutdown(); } catch { }
                    AIAssistant = null;
                    Log?.LogWarning($"[BattleLuck] Failed to initialize AI Assistant: {aiEx.Message}");
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
                ErrorReporter.Report(ex, new ErrorReportContext { Action = "core.initialize", Critical = true });
                CleanupFailedCoreInitialization();
                return false;
            }
            finally
            {
                _coreInitializationInProgress = false;
            }
        }
    }

    static void HandlePlayerLeftDeathPrevention(PlayerLeftEvent evt) => DeathPrevention?.Disarm(evt.SteamId);

    static void HandleModeEndedDeathPrevention(ModeEndedEvent _) => DeathPrevention?.Clear();

    static void HandleModeEndedNpcDespawn(ModeEndedEvent evt) => NpcService?.DespawnSession(evt.SessionId);

    static void UnsubscribeCoreEvents()
    {
        GameEvents.OnPlayerLeft -= HandlePlayerLeftDeathPrevention;
        GameEvents.OnModeEnded -= HandleModeEndedDeathPrevention;
        GameEvents.OnModeEnded -= HandleModeEndedNpcDespawn;
        GameEvents.OnModeEnded -= HandleModeEndedCleanup;
        GameEvents.OnActionPerformed -= HandleActionVFX;
    }

    static void CleanupFailedCoreInitialization()
    {
        UnsubscribeCoreEvents();

        try { Session?.Shutdown(); } catch { }
        Session = null;

        try { ZoneMap?.Shutdown(); } catch { }
        ZoneMap = null;

        try { EquipmentTracker?.RestoreAll(); } catch { }
        EquipmentTracker = null;

        try { _clanTaskGameAdapter?.Dispose(); } catch { }
        _clanTaskGameAdapter = null;
        try { ClanTasks?.Dispose(); } catch { }
        ClanTasks = null;
        try { Roadmap?.Dispose(); } catch { }
        Roadmap = null;


        try { NpcService?.DespawnAll(); } catch { }
        NpcService = null;
        try { DeathPrevention?.Dispose(); } catch { }
        DeathPrevention = null;

        try { AiGroupProjectMBridge?.Dispose(); } catch { }
        AiGroupProjectMBridge = null;
        try { AIAssistant?.Shutdown(); } catch { }
        AIAssistant = null;
        BattleLuckLogger.SetDiscordWebhook(null);

        PlayerLoadouts = null;
        Progression = null;
        Teleports = null;
        MerchantCommands = null;
        DevSession = null;
        Cleanup = null;

        try { QueryRegistry.Shutdown(); } catch { }
        _playerQuery = null;
        try { VRisingCore.Reset(); } catch { }
        Core.IsInitialized = false;
    }

    public override bool Unload()
    {
        try { ErrorReporter.DisposeAsync().AsTask().GetAwaiter().GetResult(); } catch { }
        ErrorReporter = NoOpErrorReporter.Instance;
        Core.EventNormalizer?.Dispose();
        Core.EventNormalizer = null;
        EventPlatform?.Dispose();
        EventPlatform = null;
        PlayerDirectory = null;
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
        CastlePolicy?.Dispose();
        CastlePolicy = null;
        ZoneMap?.Shutdown();
        ZoneMap = null;
        AiGroupProjectMBridge?.Dispose();
        AiGroupProjectMBridge = null;
        AIAssistant?.Shutdown();
        AIAssistant = null;
        BattleLuckLogger.SetDiscordWebhook(null);
        Session?.Shutdown();
        DeathHook.Shutdown();

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
