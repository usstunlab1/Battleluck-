using Unity.Entities;
using BattleLuck.Services;
using BattleLuck.Services.AI;
using BattleLuck.Services.Castles;
using BattleLuck.Services.Companion;
using BattleLuck.Services.Encounter;
using BattleLuck.Services.Boss;
using BattleLuck.Services.Portal;
using BattleLuck.Services.Creature;
using BattleLuck.Services.Diagnostics;
using BattleLuck.Services.Planning;

/// <summary>
/// Static service locator for BattleLuck.
///
/// Holds references to game systems and BattleLuck services after the server
/// world has loaded, exposed as static properties so commands and patches can
/// reach them without passing instances around.
///
/// This mirrors the V Rising modding guide's `Core` pattern
/// (https://wiki.vrisingmods.com/dev/mod-structure.html). <see cref="BattleLuckPlugin"/>
/// keeps thin forwarding properties so existing call sites (BattleLuckPlugin.Session,
/// etc.) stay valid while the state actually lives here.
///
/// <see cref="InitializeAfterLoaded"/> is invoked once by the one-shot
/// <c>InitializationPatch</c> after the server world is ready.
/// </summary>
internal static class Core
{
    // ── Game systems ──────────────────────────────────────────────────────────
    // Resolved once the server world is ready (via VRisingCore).
    public static World Server => VRisingCore.Server;
    public static EntityManager EntityManager => VRisingCore.EntityManager;

    // ── BattleLuck services (constructed in BattleLuckPlugin.TryInitializeCore) ─
    public static GameModeRegistry? GameModes { get; internal set; }
    public static SessionController? Session { get; internal set; }
    public static DevSessionService? DevSession { get; internal set; }
    public static AIAssistant? AIAssistant { get; internal set; }
    public static AiGroupProjectMLlmBridge? AiGroupProjectMBridge { get; internal set; }
    public static NpcControlService? NpcService { get; internal set; }
    public static PlayerStateController? PlayerState { get; internal set; }
    public static PlayerLoadoutService? PlayerLoadouts { get; internal set; }
    public static PlayerProgressionService? Progression { get; internal set; }
    public static TeleportService? Teleports { get; internal set; }
    public static DeathPreventionService? DeathPrevention { get; internal set; }
    public static SessionCleanupService? Cleanup { get; internal set; }
    public static ZoneMapIconService? ZoneMap { get; internal set; }
    public static PlayerEquipmentTrackingService? EquipmentTracker { get; internal set; }
    public static MerchantCommandService? MerchantCommands { get; internal set; }
    public static ClanTaskService? ClanTasks { get; internal set; }
    public static RoadmapService? Roadmap { get; internal set; }
    public static CastlePolicyService? CastlePolicy { get; internal set; }
    public static ServerEventPlatform? EventPlatform { get; internal set; }
    public static GameEventNormalizer? EventNormalizer { get; internal set; }
    public static PlayerDirectoryService? PlayerDirectory { get; internal set; }
    public static UnifiedPlannerService? Planner { get; internal set; }
    public static IErrorReporter ErrorReporter { get; internal set; } = NoOpErrorReporter.Instance;

    // ── Wave 1: New expansion services ──────────────────────────────────────────
    public static CompanionService? Companion { get; internal set; }
    public static EncounterService? Encounters { get; internal set; }
    public static BossScalingService? BossScaling { get; internal set; }
    public static PortalService? Portals { get; internal set; }
    public static CreatureCaptureService? CreatureCapture { get; internal set; }

    /// <summary>True once <see cref="InitializeAfterLoaded"/> has completed.</summary>
    public static bool IsInitialized { get; internal set; }

    /// <summary>
    /// Called exactly once after the server world has finished loading.
    /// Resolves game systems and constructs the BattleLuck services.
    /// Triggered by the one-shot <c>InitializationPatch</c>.
    /// </summary>
    internal static void InitializeAfterLoaded()
    {
        BattleLuckPlugin.TryInitializeCore();
    }
}
