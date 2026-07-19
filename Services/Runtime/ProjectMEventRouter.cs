using System;
using BattleLuck.ECS.Events;
using BattleLuck.Services.Chat;
using Unity.Entities;

namespace BattleLuck.Services.Runtime;
// ─────────────────────────────────────────────────────────────────────────────
// ProjectMEventRouter.cs — single source of typed ProjectM events.
//
// The router is the *only* place where BattleLuck subscribes to ProjectM ECS
// systems. It exposes strongly-typed C# events (one group per domain) and is
// fed by Harmony Postfix patches defined in `Patches/ProjectMEventRouterPatches.cs`.
//
// The patches are auto-applied via `_harmony.PatchAll(typeof(DeathHook).Assembly)`
// in BattleLuckPlugin.Load(), so this class itself does not need to install
// patches at runtime. It only needs to provide the event surface and the
// raise methods called by the patch code.
//
// Migration path:
// Phase 1 (this phase): router exists, listens, but no consumers subscribe.
// Phase 2: typed command records are defined; consumers appear.
// Phase 3: FlowActionExecutor migrates to subscribe to router events.
// Phase 4: old `DeathHook` static event is removed; router is sole source.
// ─────────────────────────────────────────────────────────────────────────────
/// <summary>
/// Strongly-typed re-emission of ProjectM ECS system updates. Listener-only at
/// this phase — it does not dispatch any commands back into the game.
/// </summary>
public sealed class ProjectMEventRouter
{
    private static ProjectMEventRouter? _instance;
    private static readonly object _initLock = new();
    private const int MaxTrackedDeathEvents = 4096;
    private readonly HashSet<Entity> _processedDeathEvents = new();
    private readonly Queue<Entity> _processedDeathEventOrder = new();
    /// <summary>Singleton accessor. Null until <see cref="Initialize"/> runs.</summary>
    public static ProjectMEventRouter? Instance => _instance;
    /// <summary>True once <see cref="Initialize"/> has been called.</summary>
    public bool IsInitialized { get; private set; }
    private ProjectMEventRouter() { }
    /// <summary>
    /// Construct the singleton. Idempotent — safe to call from BattleLuckPlugin.Load.
    /// </summary>
    public static ProjectMEventRouter Initialize()
    {
        if (_instance != null) return _instance;
        lock (_initLock)
        {
            if (_instance != null) return _instance;
            var router = new ProjectMEventRouter();
            router.IsInitialized = true;
            _instance = router;

            // GameEvents already owns the actual player-disconnect hook. Keep AI
            // channel cleanup attached to that authoritative lifecycle instead of
            // adding another player-state dictionary or Harmony disconnect patch.
            GameEvents.OnPlayerLeft += evt => AiChannelState.Remove(evt.SteamId);

            BattleLuckPlugin.LogInfo("[ProjectMEventRouter] Initialized - listening to ProjectM ECS systems (Phase 1, no dispatch).");
            return router;
        }
    }
    /// <summary>
    /// Drop the singleton. Harmony patches are removed by `_harmony.UnpatchSelf()` in
    /// BattleLuckPlugin.Unload, so the only thing this method does is clear the static
    /// reference so a subsequent Load starts fresh.
    /// </summary>
    public static void Shutdown()
    {
        lock (_initLock)
        {
            // The plugin unload path calls ProjectMEventRouter.Shutdown before
            // GameEvents.Shutdown. Cancel every outstanding request and clear all
            // AI memberships here so no channel state survives a reload.
            AiChannelState.Clear();

            if (_instance == null) return;
            // Null out all subscribers so we don't leak references.
            _instance.OnPlayerDeath = null;
            _instance.OnDamageDealt = null;
            _instance.OnKill = null;
            _instance.OnDeathReaction = null;
            _instance.OnVampireDowned = null;
            _instance.OnBuffSpawned = null;
            _instance.OnAbilityCastStarted = null;
            _instance.OnMinionSpawned = null;
            _instance.OnBuffApplied = null;
            _instance.OnBuffRemoved = null;
            _instance.OnItemEquipped = null;
            _instance.OnItemDropped = null;
            _instance.OnItemMoved = null;
            _instance.OnItemPickedUp = null;
            _instance.OnItemUnequipped = null;
            _instance.OnTeleport = null;
            _instance.OnMoveTowardsPosition = null;
            _instance.OnPlayerLocationTeleport = null;
            _instance.OnPlayerTeleportCommand = null;
            _instance.OnUnitSpawnerReact = null;
            _instance.OnPrefabSpawned = null;
            _instance.OnMinionSpawnSlot = null;
            _instance.OnCharacterRespawn = null;
            _instance.OnCastleBuff = null;
            _instance.OnCastleHeartState = null;
            _instance.OnSequencer = null;
            _instance.OnDoorState = null;
            _instance.OnCastleFloorWalls = null;
            _instance.OnAiGroupProjectMTick = null;
            _instance.OnBattleLuckServerTick = null;
            _instance.OnProjectMRuntimeTick = null;
            _instance.OnSequenceStarted = null;
            _instance.OnSequenceStepStarted = null;
            _instance.OnSequenceStepCompleted = null;
            _instance.OnSequenceFailed = null;
            _instance.OnSequenceCompleted = null;
            _instance._processedDeathEvents.Clear();
            _instance._processedDeathEventOrder.Clear();
            _instance.IsInitialized = false;
            _instance = null;
        }
    }
    #region Group 1 — Death & Damage
    public event Action<PlayerDeathEvent>? OnPlayerDeath;
    public event Action<DamageDealtEvent>? OnDamageDealt;
    public event Action<BattleLuck.ECS.Events.KillEvent>? OnKill;
    public event Action<DeathReactionEvent>? OnDeathReaction;
    public event Action<VampireDownedEvent>? OnVampireDowned;
    #endregion
    #region Group 2 — Buffs & Abilities
    public event Action<BuffSpawnedEvent>? OnBuffSpawned;
    public event Action<BattleLuck.ECS.Events.AbilityCastStartedEvent>? OnAbilityCastStarted;
    public event Action<MinionSpawnedEvent>? OnMinionSpawned;
    public event Action<BuffAppliedEvent>? OnBuffApplied;
    public event Action<BuffRemovedEvent>? OnBuffRemoved;
    #endregion
    #region Group 3 — Inventory & Equip
    public event Action<ItemEquippedEvent>? OnItemEquipped;
    public event Action<ItemDroppedEvent>? OnItemDropped;
    public event Action<ItemMovedEvent>? OnItemMoved;
    public event Action<ItemPickedUpEvent>? OnItemPickedUp;
    public event Action<ItemUnequippedEvent>? OnItemUnequipped;
    #endregion
    #region Group 4 — Teleport & Move
    public event Action<TeleportEvent>? OnTeleport;
    public event Action<MoveTowardsPositionEvent>? OnMoveTowardsPosition;
    public event Action<PlayerLocationTeleportEvent>? OnPlayerLocationTeleport;
    public event Action<PlayerTeleportCommandEvent>? OnPlayerTeleportCommand;
    #endregion
    #region Group 5 — Spawn & NPC
    public event Action<UnitSpawnerReactEvent>? OnUnitSpawnerReact;
    public event Action<PrefabSpawnedEvent>? OnPrefabSpawned;
    public event Action<MinionSpawnSlotEvent>? OnMinionSpawnSlot;
    public event Action<BattleLuck.ECS.Events.CharacterRespawnEvent>? OnCharacterRespawn;
    #endregion
    #region Group 6 — Castle & Sequencer
    public event Action<CastleBuffEvent>? OnCastleBuff;
    public event Action<CastleHeartStateEvent>? OnCastleHeartState;
    public event Action<SequencerEvent>? OnSequencer;
    public event Action<DoorStateEvent>? OnDoorState;
    public event Action<CastleFloorWallsEvent>? OnCastleFloorWalls;
    #endregion
    #region Group 7 — ProjectM AI
    public event Action<AiGroupProjectMTickEvent>? OnAiGroupProjectMTick;
    #endregion
    #region Group 8 — Runtime tick and sequence hooks
    public event Action<BattleLuckServerTickEvent>? OnBattleLuckServerTick;
    public event Action<ProjectMRuntimeTickEvent>? OnProjectMRuntimeTick;
    #endregion
    #region Group 9 — Sequence lifecycle events
    public event Action<SequenceStartedEvent>? OnSequenceStarted;
    public event Action<SequenceStepStartedEvent>? OnSequenceStepStarted;
    public event Action<SequenceStepCompletedEvent>? OnSequenceStepCompleted;
    public event Action<SequenceFailedEvent>? OnSequenceFailed;
    public event Action<SequenceCompletedEvent>? OnSequenceCompleted;
    #endregion
    // ── Raise helpers ─────────────────────────────────────────────────────
    // Called by Patches/ProjectMEventRouterPatches.cs. We never log from these
    // paths — a throwing subscriber must not break the patch chain. The patch
    // wrappers log on their own; this layer is silent.
    internal void RaisePlayerDeath(Entity eventEntity, PlayerDeathEvent e)
    {
        // DeathEventListenerSystem queries can retain the same event entity across
        // more than one update. Deduplicate by the ECS event identity rather than
        // by victim/time, so two legitimate rapid deaths are never collapsed.
        if (eventEntity != Entity.Null)
        {
            if (!_processedDeathEvents.Add(eventEntity))
                return;

            _processedDeathEventOrder.Enqueue(eventEntity);
            while (_processedDeathEventOrder.Count > MaxTrackedDeathEvents)
                _processedDeathEvents.Remove(_processedDeathEventOrder.Dequeue());
        }

        SafeInvoke(OnPlayerDeath, e);
    }
    internal void RaiseDamageDealt(DamageDealtEvent e) => SafeInvoke(OnDamageDealt, e);
    internal void RaiseKill(BattleLuck.ECS.Events.KillEvent e) => SafeInvoke(OnKill, e);
    internal void RaiseDeathReaction(DeathReactionEvent e) => SafeInvoke(OnDeathReaction, e);
    internal void RaiseVampireDowned(VampireDownedEvent e) => SafeInvoke(OnVampireDowned, e);
    internal void RaiseBuffSpawned(BuffSpawnedEvent e) => SafeInvoke(OnBuffSpawned, e);
    internal void RaiseAbilityCastStarted(BattleLuck.ECS.Events.AbilityCastStartedEvent e) => SafeInvoke(OnAbilityCastStarted, e);
    internal void RaiseMinionSpawned(MinionSpawnedEvent e) => SafeInvoke(OnMinionSpawned, e);
    internal void RaiseBuffApplied(BuffAppliedEvent e) => SafeInvoke(OnBuffApplied, e);
    internal void RaiseBuffRemoved(BuffRemovedEvent e) => SafeInvoke(OnBuffRemoved, e);
    internal void RaiseItemEquipped(ItemEquippedEvent e) => SafeInvoke(OnItemEquipped, e);
    internal void RaiseItemDropped(ItemDroppedEvent e) => SafeInvoke(OnItemDropped, e);
    internal void RaiseItemMoved(ItemMovedEvent e) => SafeInvoke(OnItemMoved, e);
    internal void RaiseItemPickedUp(ItemPickedUpEvent e) => SafeInvoke(OnItemPickedUp, e);
    internal void RaiseItemUnequipped(ItemUnequippedEvent e) => SafeInvoke(OnItemUnequipped, e);
    internal void RaiseTeleport(TeleportEvent e) => SafeInvoke(OnTeleport, e);
    internal void RaiseMoveTowardsPosition(MoveTowardsPositionEvent e) => SafeInvoke(OnMoveTowardsPosition, e);
    internal void RaisePlayerLocationTeleport(PlayerLocationTeleportEvent e) => SafeInvoke(OnPlayerLocationTeleport, e);
    internal void RaisePlayerTeleportCommand(PlayerTeleportCommandEvent e) => SafeInvoke(OnPlayerTeleportCommand, e);
    internal void RaiseUnitSpawnerReact(UnitSpawnerReactEvent e) => SafeInvoke(OnUnitSpawnerReact, e);
    internal void RaisePrefabSpawned(PrefabSpawnedEvent e) => SafeInvoke(OnPrefabSpawned, e);
    internal void RaiseMinionSpawnSlot(MinionSpawnSlotEvent e) => SafeInvoke(OnMinionSpawnSlot, e);
    internal void RaiseCharacterRespawn(BattleLuck.ECS.Events.CharacterRespawnEvent e) => SafeInvoke(OnCharacterRespawn, e);
    internal void RaiseCastleBuff(CastleBuffEvent e) => SafeInvoke(OnCastleBuff, e);
    internal void RaiseCastleHeartState(CastleHeartStateEvent e) => SafeInvoke(OnCastleHeartState, e);
    internal void RaiseSequencer(SequencerEvent e) => SafeInvoke(OnSequencer, e);
    internal void RaiseDoorState(DoorStateEvent e) => SafeInvoke(OnDoorState, e);
    internal void RaiseCastleFloorWalls(CastleFloorWallsEvent e) => SafeInvoke(OnCastleFloorWalls, e);
    internal void RaiseAiGroupProjectMTick(AiGroupProjectMTickEvent e) => SafeInvoke(OnAiGroupProjectMTick, e);
    internal void RaiseBattleLuckServerTick(BattleLuckServerTickEvent e) => SafeInvoke(OnBattleLuckServerTick, e);
    internal void RaiseProjectMRuntimeTick(ProjectMRuntimeTickEvent e) => SafeInvoke(OnProjectMRuntimeTick, e);
    internal void RaiseSequenceStarted(SequenceStartedEvent e) => SafeInvoke(OnSequenceStarted, e);
    internal void RaiseSequenceStepStarted(SequenceStepStartedEvent e) => SafeInvoke(OnSequenceStepStarted, e);
    internal void RaiseSequenceStepCompleted(SequenceStepCompletedEvent e) => SafeInvoke(OnSequenceStepCompleted, e);
    internal void RaiseSequenceFailed(SequenceFailedEvent e) => SafeInvoke(OnSequenceFailed, e);
    internal void RaiseSequenceCompleted(SequenceCompletedEvent e) => SafeInvoke(OnSequenceCompleted, e);
    static void SafeInvoke<T>(Action<T>? handler, T arg)
    {
        if (handler == null) return;
        foreach (Action<T> subscriber in handler.GetInvocationList())
        {
            try { subscriber.Invoke(arg); }
            catch (Exception ex)
            {
                BattleLuckPlugin.LogWarning($"[ProjectMEventRouter] Subscriber threw on {typeof(T).Name}: {ex.ToString()}");
            }
        }
    }
}
