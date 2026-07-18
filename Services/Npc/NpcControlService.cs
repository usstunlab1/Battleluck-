using System.Threading;

namespace BattleLuck.Services.Npc;

public enum NpcControlMode
{
    Idle,
    Hold,
    Follow,
    GoTo,
    Aggro,
    Patrol,
    Guard,
    Flee,
    Wander,
    Formation
}

public sealed class ControlledNpcEntry
{
    public string NpcId { get; init; } = "";
    public string DisplayName { get; set; } = "";
    public string SessionId { get; init; } = "";
    public string PrefabName { get; init; } = "";
    public PrefabGUID Prefab { get; init; }
    public Entity Entity { get; set; }
    public float3 HomePosition { get; set; }
    public float3 TargetPosition { get; set; }
    public Entity TargetEntity { get; set; } = Entity.Null;
    public NpcControlMode Mode { get; set; } = NpcControlMode.Idle;
    public float HomeRadius { get; set; } = 35f;
    public float FollowRange { get; set; } = 6f;
    public float LeashRange { get; set; } = 80f;
    public float MoveSpeed { get; set; } = 9f;
    public int? ForcedTeamId { get; set; }
    public int? ForcedFactionId { get; set; }
    public bool PreventDisable { get; set; } = true;
    public DateTime CreatedAtUtc { get; init; } = DateTime.UtcNow;
    public bool IsAlive => Entity.Exists() && !Entity.Has<PlayerCharacter>();

    public List<NpcPatrolWaypoint> PatrolWaypoints { get; set; } = new();
    public int PatrolCurrentIndex { get; set; }
    public float PatrolPauseUntilUtc { get; set; }
    public NpcGuardPost? GuardConfig { get; set; }
    public NpcFleeConfig? FleeConfig { get; set; }
    public NpcControlMode? PreviousModeBeforeFlee { get; set; }
    public NpcWanderConfig? WanderConfig { get; set; }
    public float WanderNextChangeUtc { get; set; }
    public List<NpcFormationSlot> FormationSlots { get; set; } = new();
    public string? FormationLeaderId { get; set; }
    public float3 FormationCenter { get; set; }
}

public sealed class NpcControlService
{
    readonly object _lock = new();
    readonly Dictionary<string, ControlledNpcEntry> _entries = new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<string, List<string>> _byPrefab = new(StringComparer.OrdinalIgnoreCase);
    int _nextId;

    public OperationResult<ControlledNpcEntry> RegisterNpc(
        string sessionId,
        string? npcId,
        string prefabName,
        PrefabGUID prefab,
        Entity entity,
        float3 homePosition,
        float homeRadius = 35f,
        bool preventDisable = true)
    {
        if (!entity.Exists())
            return OperationResult<ControlledNpcEntry>.Fail("NPC entity is invalid or already destroyed.");
        if (entity.Has<PlayerCharacter>())
            return OperationResult<ControlledNpcEntry>.Fail("Refusing to register a player as an NPC.");

        var resolvedPrefabName = string.IsNullOrWhiteSpace(prefabName)
            ? PrefabHelper.GetLivePrefabName(prefab) ?? PrefabHelper.GetName(prefab) ?? prefab.GuidHash.ToString()
            : prefabName.Trim();

        lock (_lock)
        {
            if (TryGetByEntityLocked(entity, out var existing))
                return OperationResult<ControlledNpcEntry>.Ok(existing);

            var baseId = string.IsNullOrWhiteSpace(npcId)
                ? $"{SanitizeId(resolvedPrefabName)}_{++_nextId}"
                : SanitizeId(npcId!);
            var id = baseId;
            if (_entries.ContainsKey(id))
                id = $"{baseId}_{entity.Index}_{entity.Version}";

            var entry = new ControlledNpcEntry
            {
                NpcId = id,
                DisplayName = id,
                SessionId = string.IsNullOrWhiteSpace(sessionId) ? "_dev_" : sessionId,
                PrefabName = resolvedPrefabName,
                Prefab = prefab,
                Entity = entity,
                HomePosition = homePosition,
                TargetPosition = homePosition,
                HomeRadius = System.Math.Clamp(homeRadius, 1f, 250f),
                PreventDisable = preventDisable
            };

            _entries[id] = entry;
            if (!_byPrefab.TryGetValue(resolvedPrefabName, out var list))
            {
                list = new List<string>();
                _byPrefab[resolvedPrefabName] = list;
            }
            list.Insert(0, id);

            ApplyStableControls(entry);
            BattleLuckLogger.Info($"[NpcControl] Registered npc '{id}' prefab={resolvedPrefabName} entity={entity.Index}:{entity.Version} session={entry.SessionId}.");
            return OperationResult<ControlledNpcEntry>.Ok(entry);
        }
    }

    public bool TryGet(string npcId, out ControlledNpcEntry entry)
    {
        lock (_lock)
            return _entries.TryGetValue(npcId, out entry!);
    }

    public bool TryGetByEntity(Entity entity, out ControlledNpcEntry entry)
    {
        lock (_lock)
            return TryGetByEntityLocked(entity, out entry);
    }

    public ControlledNpcEntry? GetLatest(string? sessionId = null)
    {
        lock (_lock)
        {
            CleanupDeadLocked();
            return _entries.Values
                .Where(e => string.IsNullOrWhiteSpace(sessionId) || e.SessionId.Equals(sessionId, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(e => e.CreatedAtUtc)
                .FirstOrDefault();
        }
    }

    public IReadOnlyList<ControlledNpcEntry> List(string? sessionId = null)
    {
        lock (_lock)
        {
            CleanupDeadLocked();
            return _entries.Values
                .Where(e => string.IsNullOrWhiteSpace(sessionId) || e.SessionId.Equals(sessionId, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(e => e.CreatedAtUtc)
                .ToList();
        }
    }

    public OperationResult Follow(string npcId, Entity target, float followRange = 6f, float leashRange = 80f)
    {
        if (!target.Exists())
            return OperationResult.Fail("Follow target is invalid or destroyed.");

        var entry = ResolveEntry(npcId);
        if (entry == null)
            return OperationResult.Fail($"NPC '{npcId}' is not tracked.");
        if (!entry.IsAlive)
            return OperationResult.Fail($"NPC '{npcId}' is not alive.");

        lock (_lock)
        {
            entry.TargetEntity = target;
            entry.TargetPosition = target.GetPosition();
            entry.FollowRange = System.Math.Clamp(followRange, 1f, 40f);
            entry.LeashRange = System.Math.Clamp(leashRange, entry.FollowRange + 1f, 500f);
            entry.Mode = NpcControlMode.Follow;
        }

        BattleLuckLogger.Info($"[NpcControl] Follow npc='{npcId}' target={target.Index}:{target.Version} follow={followRange:F1} leash={leashRange:F1}.");
        return OperationResult.Ok();
    }

    public OperationResult Aggro(string npcId, Entity target, float pressureRange = 3f, float leashRange = 80f)
    {
        if (!target.Exists())
            return OperationResult.Fail("Aggro target is invalid or destroyed.");

        var entry = ResolveEntry(npcId);
        if (entry == null)
            return OperationResult.Fail($"NPC '{npcId}' is not tracked.");
        if (!entry.IsAlive)
            return OperationResult.Fail($"NPC '{npcId}' is not alive.");

        lock (_lock)
        {
            entry.TargetEntity = target;
            entry.TargetPosition = target.GetPosition();
            entry.FollowRange = System.Math.Clamp(pressureRange, 1f, 30f);
            entry.LeashRange = System.Math.Clamp(leashRange, entry.FollowRange + 1f, 500f);
            entry.Mode = NpcControlMode.Aggro;
        }

        TryTouchAggro(entry);
        BattleLuckLogger.Info($"[NpcControl] Aggro npc='{npcId}' target={target.Index}:{target.Version} range={pressureRange:F1} leash={leashRange:F1}.");
        return OperationResult.Ok();
    }

    public OperationResult Hold(string npcId, float radius = 8f)
    {
        var entry = ResolveEntry(npcId);
        if (entry == null)
            return OperationResult.Fail($"NPC '{npcId}' is not tracked.");
        if (!entry.IsAlive)
            return OperationResult.Fail($"NPC '{npcId}' is not alive.");

        lock (_lock)
        {
            entry.HomePosition = entry.Entity.GetPosition();
            entry.TargetPosition = entry.HomePosition;
            entry.TargetEntity = Entity.Null;
            entry.HomeRadius = System.Math.Clamp(radius, 1f, 150f);
            entry.Mode = NpcControlMode.Hold;
        }

        BattleLuckLogger.Info($"[NpcControl] Hold npc='{npcId}' radius={radius:F1}.");
        return OperationResult.Ok();
    }

    public OperationResult GoTo(string npcId, float3 targetPosition, float arrivalRange = 2f)
    {
        var entry = ResolveEntry(npcId);
        if (entry == null)
            return OperationResult.Fail($"NPC '{npcId}' is not tracked.");
        if (!entry.IsAlive)
            return OperationResult.Fail($"NPC '{npcId}' is not alive.");

        lock (_lock)
        {
            entry.TargetEntity = Entity.Null;
            entry.TargetPosition = targetPosition;
            entry.FollowRange = System.Math.Clamp(arrivalRange, 0.5f, 20f);
            entry.Mode = NpcControlMode.GoTo;
        }

        BattleLuckLogger.Info($"[NpcControl] GoTo npc='{npcId}' target=({targetPosition.x:F1},{targetPosition.y:F1},{targetPosition.z:F1}).");
        return OperationResult.Ok();
    }

    public OperationResult Release(string npcId)
    {
        var entry = ResolveEntry(npcId);
        if (entry == null)
            return OperationResult.Fail($"NPC '{npcId}' is not tracked.");

        lock (_lock)
        {
            entry.TargetEntity = Entity.Null;
            entry.TargetPosition = entry.Entity.Exists() ? entry.Entity.GetPosition() : entry.TargetPosition;
            entry.Mode = NpcControlMode.Idle;
            entry.PatrolWaypoints.Clear();
            entry.GuardConfig = null;
            entry.FleeConfig = null;
            entry.PreviousModeBeforeFlee = null;
            entry.WanderConfig = null;
            entry.FormationSlots.Clear();
            entry.FormationLeaderId = null;
        }

        BattleLuckLogger.Info($"[NpcControl] Release npc='{npcId}'.");
        return OperationResult.Ok();
    }

    public OperationResult SetTeam(string npcId, int teamId)
    {
        var entry = ResolveEntry(npcId);
        if (entry == null)
            return OperationResult.Fail($"NPC '{npcId}' is not tracked.");

        lock (_lock)
        {
            entry.ForcedTeamId = teamId;
            ApplyStableControls(entry);
        }
        return OperationResult.Ok();
    }

    public OperationResult SetFaction(string npcId, PrefabGUID factionPrefab)
    {
        if (factionPrefab == PrefabGUID.Empty)
            return OperationResult.Fail("Faction prefab is empty or unknown.");

        var entry = ResolveEntry(npcId);
        if (entry == null)
            return OperationResult.Fail($"NPC '{npcId}' is not tracked.");

        lock (_lock)
        {
            entry.ForcedFactionId = factionPrefab.GuidHash;
            ApplyStableControls(entry);
        }
        return OperationResult.Ok();
    }

    public OperationResult SetSpeed(string npcId, float speed)
    {
        var entry = ResolveEntry(npcId);
        if (entry == null)
            return OperationResult.Fail($"NPC '{npcId}' is not tracked.");

        lock (_lock)
        {
            entry.MoveSpeed = System.Math.Clamp(speed, 0.5f, 60f);
        }
        return OperationResult.Ok();
    }

    public OperationResult Rename(string npcId, string displayName)
    {
        if (string.IsNullOrWhiteSpace(displayName))
            return OperationResult.Fail("Display name is required.");

        var entry = ResolveEntry(npcId);
        if (entry == null)
            return OperationResult.Fail($"NPC '{npcId}' is not tracked.");

        lock (_lock)
        {
            entry.DisplayName = displayName.Trim();
        }
        BattleLuckLogger.Info($"[NpcControl] Rename npc='{npcId}' display='{entry.DisplayName}'.");
        return OperationResult.Ok();
    }

    public OperationResult Patrol(string npcId, List<NpcPatrolWaypoint> waypoints, bool loop = true)
    {
        var entry = ResolveEntry(npcId);
        if (entry == null)
            return OperationResult.Fail($"NPC '{npcId}' is not tracked.");
        if (!entry.IsAlive)
            return OperationResult.Fail($"NPC '{npcId}' is not alive.");
        if (waypoints == null || waypoints.Count == 0)
            return OperationResult.Fail("Patrol requires at least one waypoint.");

        lock (_lock)
        {
            entry.PatrolWaypoints = waypoints;
            entry.PatrolCurrentIndex = 0;
            entry.PatrolPauseUntilUtc = 0f;
            entry.TargetEntity = Entity.Null;
            entry.TargetPosition = waypoints[0].Position;
            entry.HomePosition = waypoints[0].Position;
            entry.Mode = NpcControlMode.Patrol;
        }

        BattleLuckLogger.Info($"[NpcControl] Patrol npc='{npcId}' waypoints={waypoints.Count} loop={loop}.");
        return OperationResult.Ok();
    }

    public OperationResult Guard(string npcId, NpcGuardPost config)
    {
        var entry = ResolveEntry(npcId);
        if (entry == null)
            return OperationResult.Fail($"NPC '{npcId}' is not tracked.");
        if (!entry.IsAlive)
            return OperationResult.Fail($"NPC '{npcId}' is not alive.");

        lock (_lock)
        {
            entry.HomePosition = config.Position;
            entry.TargetPosition = config.Position;
            entry.TargetEntity = config.TargetEntity ?? Entity.Null;
            entry.FollowRange = System.Math.Clamp(config.DetectionRadius, 1f, 100f);
            entry.LeashRange = System.Math.Clamp(config.ChaseRange, config.DetectionRadius + 1f, 500f);
            entry.GuardConfig = config;
            entry.Mode = NpcControlMode.Guard;
        }

        BattleLuckLogger.Info($"[NpcControl] Guard npc='{npcId}' pos=({config.Position.x:F1},{config.Position.y:F1},{config.Position.z:F1}) detection={config.DetectionRadius:F1}.");
        return OperationResult.Ok();
    }

    public OperationResult Flee(string npcId, NpcFleeConfig config)
    {
        var entry = ResolveEntry(npcId);
        if (entry == null)
            return OperationResult.Fail($"NPC '{npcId}' is not tracked.");
        if (!entry.IsAlive)
            return OperationResult.Fail($"NPC '{npcId}' is not alive.");

        lock (_lock)
        {
            entry.PreviousModeBeforeFlee = entry.Mode;
            entry.FleeConfig = config;
            entry.MoveSpeed = System.Math.Clamp(entry.MoveSpeed * config.FleeSpeedMultiplier, 0.5f, 120f);
            entry.Mode = NpcControlMode.Flee;
        }

        BattleLuckLogger.Info($"[NpcControl] Flee npc='{npcId}' safe={config.SafeDistance:F1} duration={config.DurationSeconds:F1}s.");
        return OperationResult.Ok();
    }

    public OperationResult Wander(string npcId, NpcWanderConfig config)
    {
        var entry = ResolveEntry(npcId);
        if (entry == null)
            return OperationResult.Fail($"NPC '{npcId}' is not tracked.");
        if (!entry.IsAlive)
            return OperationResult.Fail($"NPC '{npcId}' is not alive.");

        lock (_lock)
        {
            entry.HomePosition = entry.Entity.GetPosition();
            entry.WanderConfig = config;
            entry.WanderNextChangeUtc = (float)DateTime.UtcNow.ToOADate();
            entry.TargetEntity = Entity.Null;
            entry.Mode = NpcControlMode.Wander;
        }

        BattleLuckLogger.Info($"[NpcControl] Wander npc='{npcId}' radius={config.Radius:F1}.");
        return OperationResult.Ok();
    }

    public OperationResult Formation(string npcId, List<NpcFormationSlot> slots, string? leaderId, float3 center)
    {
        var entry = ResolveEntry(npcId);
        if (entry == null)
            return OperationResult.Fail($"NPC '{npcId}' is not tracked.");
        if (!entry.IsAlive)
            return OperationResult.Fail($"NPC '{npcId}' is not alive.");

        lock (_lock)
        {
            entry.FormationSlots = slots;
            entry.FormationLeaderId = leaderId;
            entry.FormationCenter = center;
            entry.Mode = NpcControlMode.Formation;
        }

        BattleLuckLogger.Info($"[NpcControl] Formation npc='{npcId}' slots={slots.Count} leader={leaderId ?? "center"}.");
        return OperationResult.Ok();
    }

    public OperationResult Despawn(string npcId)
    {
        ControlledNpcEntry? entry;
        lock (_lock)
        {
            if (!_entries.TryGetValue(npcId, out entry))
                return OperationResult.Fail($"NPC '{npcId}' is not tracked.");
            RemoveIndexesLocked(entry);
            _entries.Remove(npcId);
        }

        return DestroyNpc(entry);
    }

    public int DespawnSession(string sessionId)
    {
        List<ControlledNpcEntry> entries;
        lock (_lock)
        {
            entries = _entries.Values
                .Where(e => e.SessionId.Equals(sessionId, StringComparison.OrdinalIgnoreCase))
                .ToList();
            foreach (var entry in entries)
            {
                RemoveIndexesLocked(entry);
                _entries.Remove(entry.NpcId);
            }
        }

        foreach (var entry in entries)
            DestroyNpc(entry);
        return entries.Count;
    }

    public int DespawnAll()
    {
        List<ControlledNpcEntry> entries;
        lock (_lock)
        {
            entries = _entries.Values.ToList();
            _entries.Clear();
            _byPrefab.Clear();
        }

        foreach (var entry in entries)
            DestroyNpc(entry);
        return entries.Count;
    }

    public void ClearSession(string sessionId)
    {
        lock (_lock)
        {
            var ids = _entries.Values
                .Where(e => e.SessionId.Equals(sessionId, StringComparison.OrdinalIgnoreCase))
                .Select(e => e.NpcId)
                .ToList();
            foreach (var id in ids)
            {
                if (_entries.TryGetValue(id, out var entry))
                    RemoveIndexesLocked(entry);
                _entries.Remove(id);
            }
        }
    }

    public void Tick(float deltaSeconds)
    {
        ControlledNpcEntry[] entries;
        lock (_lock)
        {
            CleanupDeadLocked();
            entries = _entries.Values.ToArray();
        }

        foreach (var entry in entries)
        {
            try { TickEntry(entry, deltaSeconds); }
            catch (Exception ex) { BattleLuckLogger.Warning($"[NpcControl] Tick failed for '{entry.NpcId}': {ex.Message}"); }
        }
    }

    ControlledNpcEntry? ResolveEntry(string npcId)
    {
        lock (_lock)
        {
            CleanupDeadLocked();
            if (_entries.TryGetValue(npcId, out var entry))
                return entry;

            var prefabMatch = _entries.Values
                .Where(e => e.PrefabName.Contains(npcId, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(e => e.CreatedAtUtc)
                .FirstOrDefault();
            return prefabMatch;
        }
    }

    bool TryGetByEntityLocked(Entity entity, out ControlledNpcEntry entry)
    {
        entry = _entries.Values.FirstOrDefault(e => e.Entity == entity)!;
        return entry != null;
    }

    void TickEntry(ControlledNpcEntry entry, float deltaSeconds)
    {
        if (!entry.IsAlive)
            return;

        ApplyStableControls(entry);

        switch (entry.Mode)
        {
            case NpcControlMode.Follow:
                TickFollow(entry, deltaSeconds, aggressive: false);
                break;
            case NpcControlMode.Aggro:
                TickFollow(entry, deltaSeconds, aggressive: true);
                break;
            case NpcControlMode.GoTo:
                TickGoTo(entry, deltaSeconds);
                break;
            case NpcControlMode.Hold:
                TickHold(entry, deltaSeconds);
                break;
            case NpcControlMode.Patrol:
                TickPatrol(entry, deltaSeconds);
                break;
            case NpcControlMode.Guard:
                TickGuard(entry, deltaSeconds);
                break;
            case NpcControlMode.Flee:
                TickFlee(entry, deltaSeconds);
                break;
            case NpcControlMode.Wander:
                TickWander(entry, deltaSeconds);
                break;
            case NpcControlMode.Formation:
                TickFormation(entry, deltaSeconds);
                break;
            case NpcControlMode.Idle:
            default:
                break;
        }
    }

    void TickFollow(ControlledNpcEntry entry, float deltaSeconds, bool aggressive)
    {
        if (entry.TargetEntity == Entity.Null || !entry.TargetEntity.Exists())
            return;

        var pos = entry.Entity.GetPosition();
        var targetPos = entry.TargetEntity.GetPosition();
        var dist = math.distance(pos, targetPos);

        if (dist > entry.LeashRange)
        {
            entry.Entity.SetPosition(StandNear(targetPos, pos, entry.FollowRange));
            return;
        }

        if (dist > entry.FollowRange)
            MoveToward(entry, targetPos, deltaSeconds);

        if (aggressive)
            TryTouchAggro(entry);
    }

    void TickGoTo(ControlledNpcEntry entry, float deltaSeconds)
    {
        var pos = entry.Entity.GetPosition();
        var dist = math.distance(pos, entry.TargetPosition);
        if (dist <= entry.FollowRange)
        {
            entry.HomePosition = entry.TargetPosition;
            entry.Mode = NpcControlMode.Hold;
            return;
        }

        MoveToward(entry, entry.TargetPosition, deltaSeconds);
    }

    void TickHold(ControlledNpcEntry entry, float deltaSeconds)
    {
        var pos = entry.Entity.GetPosition();
        var dist = math.distance(pos, entry.HomePosition);
        if (dist > entry.LeashRange)
        {
            entry.Entity.SetPosition(entry.HomePosition);
            return;
        }
        if (dist > entry.HomeRadius)
            MoveToward(entry, entry.HomePosition, deltaSeconds);
    }

    void TickPatrol(ControlledNpcEntry entry, float deltaSeconds)
    {
        if (entry.PatrolWaypoints.Count == 0)
        {
            entry.Mode = NpcControlMode.Idle;
            return;
        }

        var pos = entry.Entity.GetPosition();
        var target = entry.PatrolWaypoints[entry.PatrolCurrentIndex].Position;
        var dist = math.distance(pos, target);

        if (dist <= entry.FollowRange)
        {
            var now = DateTime.UtcNow.ToOADate();
            if (now < entry.PatrolPauseUntilUtc)
                return;

            var wp = entry.PatrolWaypoints[entry.PatrolCurrentIndex];
            entry.PatrolCurrentIndex++;
            if (entry.PatrolCurrentIndex >= entry.PatrolWaypoints.Count)
            {
                entry.PatrolCurrentIndex = 0;
                if (!entry.PatrolWaypoints.Any(w => w.PauseSeconds > 0))
                {
                    entry.Mode = NpcControlMode.Hold;
                    entry.HomePosition = target;
                    return;
                }
            }

            var next = entry.PatrolWaypoints[entry.PatrolCurrentIndex];
            entry.TargetPosition = next.Position;
            entry.PatrolPauseUntilUtc = (float)(now + next.PauseSeconds);
            return;
        }

        MoveToward(entry, target, deltaSeconds);
    }

    void TickGuard(ControlledNpcEntry entry, float deltaSeconds)
    {
        var config = entry.GuardConfig;
        if (config == null)
        {
            entry.Mode = NpcControlMode.Hold;
            return;
        }

        var pos = entry.Entity.GetPosition();
        var distToPost = math.distance(pos, config.Position);

        if (entry.TargetEntity != Entity.Null && entry.TargetEntity.Exists())
        {
            var targetPos = entry.TargetEntity.GetPosition();
            var distToTarget = math.distance(pos, targetPos);
            if (distToTarget <= config.ChaseRange)
            {
                entry.Entity.SetPosition(StandNear(targetPos, pos, config.DetectionRadius));
                if (distToTarget > config.DetectionRadius)
                    MoveToward(entry, targetPos, deltaSeconds);
                return;
            }
        }

        if (distToPost > config.ReturnRadius)
        {
            entry.Entity.SetPosition(config.Position);
            return;
        }

        if (distToPost > config.DetectionRadius)
            MoveToward(entry, config.Position, deltaSeconds);
    }

    void TickFlee(ControlledNpcEntry entry, float deltaSeconds)
    {
        var config = entry.FleeConfig;
        if (config == null)
        {
            ResumePreviousMode(entry);
            return;
        }

        var startUtc = DateTime.FromOADate(entry.FleeConfig != null ? 0f : entry.WanderNextChangeUtc);
        if (DateTime.UtcNow - startUtc > TimeSpan.FromSeconds(config.DurationSeconds))
        {
            ResumePreviousMode(entry);
            return;
        }

        var pos = entry.Entity.GetPosition();
        float3 fleeTarget;
        if (config.FromEntity != null && config.FromEntity.Value.Exists())
        {
            var fromPos = config.FromEntity.Value.GetPosition();
            fleeTarget = pos + math.normalizesafe(pos - fromPos) * config.SafeDistance;
        }
        else if (config.FromPosition.HasValue)
        {
            fleeTarget = pos + math.normalizesafe(pos - config.FromPosition.Value) * config.SafeDistance;
        }
        else
        {
            ResumePreviousMode(entry);
            return;
        }

        var dist = math.distance(pos, fleeTarget);
        if (dist > entry.FollowRange)
            MoveToward(entry, fleeTarget, deltaSeconds);
    }

    void TickWander(ControlledNpcEntry entry, float deltaSeconds)
    {
        var config = entry.WanderConfig;
        if (config == null)
        {
            entry.Mode = NpcControlMode.Hold;
            return;
        }

        var now = DateTime.UtcNow.ToOADate();
        if (now < entry.WanderNextChangeUtc)
            return;

        var pos = entry.Entity.GetPosition();
        var angle = (float)(System.Random.Shared.NextDouble() * System.Math.PI * 2);
        var radius = (float)(System.Random.Shared.NextDouble() * (config.Radius - 1f) + 1f);
        var target = new float3(
            pos.x + math.cos(angle) * radius,
            pos.y,
            pos.z + math.sin(angle) * radius);

        entry.TargetPosition = target;
        entry.HomePosition = pos;
        entry.WanderNextChangeUtc = (float)(now + System.Random.Shared.NextDouble() * (config.MaxPauseSeconds - config.MinPauseSeconds) + config.MinPauseSeconds);
        MoveToward(entry, target, deltaSeconds);
    }

    void TickFormation(ControlledNpcEntry entry, float deltaSeconds)
    {
        var pos = entry.Entity.GetPosition();
        float3 target;

        if (!string.IsNullOrWhiteSpace(entry.FormationLeaderId))
        {
            if (!TryGet(entry.FormationLeaderId, out var leader) || !leader.Entity.Exists())
            {
                entry.Mode = NpcControlMode.Hold;
                return;
            }
            var leaderPos = leader.Entity.GetPosition();
            var slot = entry.FormationSlots.FirstOrDefault(s => s.NpcId.Equals(entry.NpcId, StringComparison.OrdinalIgnoreCase));
            target = slot != default ? leaderPos + slot.Offset : leaderPos;
        }
        else
        {
            target = entry.FormationCenter + entry.FormationSlots
                .Where(s => s.NpcId.Equals(entry.NpcId, StringComparison.OrdinalIgnoreCase))
                .Select(s => s.Offset)
                .FirstOrDefault();
        }

        var dist = math.distance(pos, target);
        if (dist > entry.FollowRange)
            MoveToward(entry, target, deltaSeconds);
    }

    void ResumePreviousMode(ControlledNpcEntry entry)
    {
        lock (_lock)
        {
            entry.FleeConfig = null;
            entry.Mode = entry.PreviousModeBeforeFlee ?? NpcControlMode.Hold;
            entry.PreviousModeBeforeFlee = null;
        }
    }

    void MoveToward(ControlledNpcEntry entry, float3 target, float deltaSeconds)
    {
        var pos = entry.Entity.GetPosition();
        var delta = target - pos;
        var len = math.length(delta);
        if (len < 0.05f)
            return;

        var step = System.Math.Clamp(deltaSeconds * entry.MoveSpeed, 0.2f, 1.25f);
        entry.Entity.SetPosition(pos + math.normalizesafe(delta) * math.min(step, len));
    }

    static float3 StandNear(float3 target, float3 current, float range)
    {
        var away = current - target;
        if (math.lengthsq(away) < 0.01f)
            away = new float3(1f, 0f, 0f);
        return target + math.normalizesafe(away) * math.max(1.5f, range);
    }

    void ApplyStableControls(ControlledNpcEntry entry)
    {
        if (!entry.Entity.Exists() || entry.Entity.Has<PlayerCharacter>())
            return;

        try
        {
            if (entry.PreventDisable)
            {
                var entity = entry.Entity;
                if (entity.Has<CanPreventDisableWhenNoPlayersInRange>())
                {
                    // Component already present: a data-only write (SetComponentData) is
                    // NOT a structural change and is safe to run inline on the tick thread.
                    entity.With((ref CanPreventDisableWhenNoPlayersInRange c) => c.CanDisable = new ModifiableBool(false));
                }
                else
                {
                    // AddComponent is a STRUCTURAL change. Running it inline here would
                    // execute mid DOTS system OnUpdate (this service ticks from a Harmony
                    // postfix on BuffSystem_Spawn_Server.OnUpdate) and natively hard-crash
                    // the server. Defer it to the MainThreadDispatcher safe sync point.
                    MainThreadDispatcher.Enqueue(() =>
                    {
                        try
                        {
                            if (entity.Exists() && !entity.Has<PlayerCharacter>() &&
                                !entity.Has<CanPreventDisableWhenNoPlayersInRange>())
                            {
                                VRisingCore.EntityManager.AddComponent<CanPreventDisableWhenNoPlayersInRange>(entity);
                                entity.With((ref CanPreventDisableWhenNoPlayersInRange c) => c.CanDisable = new ModifiableBool(false));
                            }
                        }
                        catch (Exception ex)
                        {
                            BattleLuckLogger.Warning($"[NpcControl] Deferred PreventDisable failed for '{entry.NpcId}': {ex.Message}");
                        }
                    });
                }
            }
        }
        catch (Exception ex)
        {
            BattleLuckLogger.Warning($"[NpcControl] PreventDisable failed for '{entry.NpcId}': {ex.Message}");
        }

        try
        {
            if (entry.ForcedTeamId.HasValue && entry.Entity.Has<Team>())
                entry.Entity.SetTeam(entry.ForcedTeamId.Value);
        }
        catch (Exception ex)
        {
            BattleLuckLogger.Warning($"[NpcControl] SetTeam failed for '{entry.NpcId}': {ex.Message}");
        }

        try
        {
            if (entry.ForcedFactionId.HasValue)
                entry.Entity.SetFaction(new PrefabGUID(entry.ForcedFactionId.Value));
        }
        catch (Exception ex)
        {
            BattleLuckLogger.Warning($"[NpcControl] SetFaction failed for '{entry.NpcId}': {ex.Message}");
        }
    }

    static void TryTouchAggro(ControlledNpcEntry entry)
    {
        try
        {
            if (entry.Entity.Exists() && entry.Entity.Has<Aggroable>())
                _ = entry.Entity.Read<Aggroable>();
        }
        catch
        {
            // Native AI details vary between game builds. Movement control remains active.
        }
    }

    OperationResult DestroyNpc(ControlledNpcEntry entry)
    {
        if (entry.Entity == Entity.Null || !entry.Entity.Exists())
            return OperationResult.Ok();
        if (entry.Entity.Has<PlayerCharacter>())
            return OperationResult.Fail("Refusing to destroy a player entity.");

        try
        {
            entry.Entity.DestroyWithReason();
            BattleLuckLogger.Info($"[NpcControl] Despawned npc '{entry.NpcId}' entity={entry.Entity.Index}:{entry.Entity.Version}.");
            return OperationResult.Ok();
        }
        catch (Exception ex)
        {
            return OperationResult.Fail($"Destroy failed: {ex.Message}");
        }
    }

    void CleanupDeadLocked()
    {
        var dead = _entries.Values.Where(e => !e.IsAlive).Select(e => e.NpcId).ToList();
        foreach (var id in dead)
        {
            if (_entries.TryGetValue(id, out var entry))
                RemoveIndexesLocked(entry);
            _entries.Remove(id);
        }
    }

    void RemoveIndexesLocked(ControlledNpcEntry entry)
    {
        if (_byPrefab.TryGetValue(entry.PrefabName, out var list))
        {
            list.Remove(entry.NpcId);
            if (list.Count == 0)
                _byPrefab.Remove(entry.PrefabName);
        }
    }

    static string SanitizeId(string value)
    {
        var chars = value.Trim()
            .Select(ch => char.IsLetterOrDigit(ch) || ch == '_' || ch == '-' ? ch : '_')
            .ToArray();
        var id = new string(chars).Trim('_');
        return string.IsNullOrWhiteSpace(id) ? "npc" : id;
    }
}
