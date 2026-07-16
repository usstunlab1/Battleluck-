using System.Text.Json;
using BattleLuck.Models;

namespace BattleLuck.Services;

/// <summary>Server-authoritative persistent world/event clan task state.</summary>
public sealed class ClanTaskService : IDisposable
{
    const int CurrentSchemaVersion = 2;
    readonly object _sync = new();
    readonly Dictionary<string, ClanTask> _tasks = new(StringComparer.OrdinalIgnoreCase);
    readonly string _path;
    readonly Action<string>? _warning;
    bool _dirty;
    long _changeVersion;
    float _saveElapsed;
    bool _disposed;

    public ClanTaskService(string path, Action<string>? warning = null)
    {
        _path = path;
        _warning = warning;
    }

    public event Action<ClanTask>? TaskCompleted;

    public int Count
    {
        get { lock (_sync) return _tasks.Count; }
    }

    public void Initialize()
    {
        LoadFromDisk();
    }

    public void LoadFromDisk() => Load();

    public OperationResult<ClanTask> Create(CreateClanTaskRequest request)
    {
        var validation = Validate(request);
        if (validation != null)
            return OperationResult<ClanTask>.Fail(validation);

        var now = DateTime.UtcNow;
        var task = new ClanTask
        {
            TaskId = NormalizeId(request.TaskId),
            Description = SanitizeText(request.Description, 180),
            ClanId = SanitizeText(request.ClanId, 80),
            AssignedSteamIds = request.AssignedSteamIds.Where(id => id != 0).ToHashSet(),
            Scope = request.Scope,
            EventId = SanitizeText(request.EventId, 80),
            SessionId = SanitizeText(request.SessionId, 120),
            Objective = new ClanTaskObjective
            {
                Type = request.ObjectiveType,
                PrefabGuidHash = request.PrefabGuidHash,
                PrefabName = SanitizeText(request.PrefabName, 160)
            },
            TargetAmount = request.TargetAmount,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            ExpiresAtUtc = request.ExpiresAtUtc?.ToUniversalTime(),
            RewardPoints = Math.Max(0, request.RewardPoints)
        };

        lock (_sync)
        {
            var storageKey = StorageKey(task);
            if (_tasks.ContainsKey(storageKey))
                return OperationResult<ClanTask>.Fail($"Clan task '{task.TaskId}' already exists.");
            _tasks.Add(storageKey, task);
            MarkDirtyLocked();
        }

        SaveNow();
        return OperationResult<ClanTask>.Ok(Clone(task));
    }

    public OperationResult<ClanTask> AddProgress(
        string taskId,
        int amount,
        ulong actorSteamId = 0,
        bool trustedGather = false,
        string? callerEventId = null,
        string? callerSessionId = null,
        bool bypassEventIsolation = false,
        bool bypassAssigneeCheck = false)
    {
        if (amount <= 0)
            return OperationResult<ClanTask>.Fail("Progress amount must be greater than zero.");

        ClanTask snapshot;
        lock (_sync)
        {
            if (!TryResolveStorageKeyLocked(taskId, callerEventId, callerSessionId, bypassEventIsolation, out var storageKey, out var resolveError))
                return OperationResult<ClanTask>.Fail(resolveError);
            var task = _tasks[storageKey];
            if (task.Status != ClanTaskStatus.Active)
                return OperationResult<ClanTask>.Fail($"Clan task '{task.TaskId}' is {task.Status}.");
            if (task.ExpiresAtUtc is { } expires && expires <= DateTime.UtcNow)
            {
                task.Status = ClanTaskStatus.Expired;
                task.UpdatedAtUtc = DateTime.UtcNow;
                MarkDirtyLocked();
                return OperationResult<ClanTask>.Fail($"Clan task '{task.TaskId}' has expired.");
            }
            // Event progress must carry its source context. A missing context is not
            // treated as a wildcard; only explicit admin operations may bypass this.
            if (task.Scope == ClanTaskScope.Event && !bypassEventIsolation)
            {
                var taskEventId = NormalizeId(task.EventId);
                var callEventId = NormalizeId(callerEventId ?? "");
                if (!string.IsNullOrWhiteSpace(taskEventId) && string.IsNullOrWhiteSpace(callEventId))
                    return OperationResult<ClanTask>.Fail($"Event task '{task.TaskId}' requires event context '{task.EventId}'.");
                if (!string.IsNullOrWhiteSpace(taskEventId) &&
                    !taskEventId.Equals(callEventId, StringComparison.OrdinalIgnoreCase))
                    return OperationResult<ClanTask>.Fail($"Event task '{task.TaskId}' belongs to event '{task.EventId}', not '{callerEventId}'.");

                var taskSessionId = NormalizeId(task.SessionId);
                var callSessionId = NormalizeId(callerSessionId ?? "");
                if (!string.IsNullOrWhiteSpace(taskSessionId) && string.IsNullOrWhiteSpace(callSessionId))
                    return OperationResult<ClanTask>.Fail($"Event task '{task.TaskId}' requires session context '{task.SessionId}'.");
                if (!string.IsNullOrWhiteSpace(taskSessionId) &&
                    !taskSessionId.Equals(callSessionId, StringComparison.OrdinalIgnoreCase))
                    return OperationResult<ClanTask>.Fail($"Event task '{task.TaskId}' belongs to session '{task.SessionId}', not '{callSessionId}'.");
            }
            if (task.Objective.Type == ClanTaskObjectiveType.GatherItem && !trustedGather)
                return OperationResult<ClanTask>.Fail("Gather progress requires a verified gather event; generic inventory changes are not counted.");
            if (!bypassAssigneeCheck && actorSteamId != 0 && task.AssignedSteamIds.Count > 0 && !task.AssignedSteamIds.Contains(actorSteamId))
                return OperationResult<ClanTask>.Fail("This player is not assigned to the task.");
            var remaining = Math.Max(0, task.TargetAmount - task.CurrentAmount);
            var accepted = Math.Min(amount, remaining);
            task.CurrentAmount += accepted;
            if (actorSteamId != 0 && accepted > 0)
                task.Contributions[actorSteamId] = checked(task.Contributions.GetValueOrDefault(actorSteamId) + accepted);
            task.UpdatedAtUtc = DateTime.UtcNow;
            if (task.CurrentAmount >= task.TargetAmount)
            {
                task.Status = ClanTaskStatus.Completed;
                task.CompletedAtUtc = task.UpdatedAtUtc;
            }
            MarkDirtyLocked();
            snapshot = Clone(task);
        }

        if (snapshot.Status == ClanTaskStatus.Completed)
        {
            SaveNow();
            TaskCompleted?.Invoke(snapshot);
        }
        return OperationResult<ClanTask>.Ok(snapshot);
    }

    public OperationResult Cancel(string taskId, string? callerEventId = null, string? callerSessionId = null, bool bypassEventIsolation = false)
    {
        lock (_sync)
        {
            if (!TryResolveStorageKeyLocked(taskId, callerEventId, callerSessionId, bypassEventIsolation, out var storageKey, out var resolveError))
                return OperationResult.Fail(resolveError);
            var task = _tasks[storageKey];
            if (task.Status == ClanTaskStatus.Completed)
                return OperationResult.Fail("A completed task cannot be cancelled.");
            task.Status = ClanTaskStatus.Cancelled;
            task.UpdatedAtUtc = DateTime.UtcNow;
            MarkDirtyLocked();
        }
        SaveNow();
        return OperationResult.Ok();
    }

    public OperationResult Complete(
        string taskId,
        ulong actorSteamId = 0,
        string? callerEventId = null,
        string? callerSessionId = null,
        bool bypassEventIsolation = false,
        bool bypassAssigneeCheck = false)
    {
        ClanTask task;
        lock (_sync)
        {
            if (!TryResolveStorageKeyLocked(taskId, callerEventId, callerSessionId, bypassEventIsolation, out var storageKey, out var resolveError))
                return OperationResult.Fail(resolveError);
            task = _tasks[storageKey];
            if (task.Status == ClanTaskStatus.Completed)
                return OperationResult.Ok();
            if (task.Status != ClanTaskStatus.Active)
                return OperationResult.Fail($"Clan task '{task.TaskId}' is {task.Status}.");
        }
        return AddProgress(
                task.TaskId,
                Math.Max(1, task.TargetAmount - task.CurrentAmount),
                actorSteamId,
                trustedGather: true,
                callerEventId: callerEventId,
                callerSessionId: callerSessionId,
                bypassEventIsolation: bypassEventIsolation,
                bypassAssigneeCheck: bypassAssigneeCheck).Success
            ? OperationResult.Ok()
            : OperationResult.Fail($"Could not complete clan task '{task.TaskId}'.");
    }

    public IReadOnlyList<ClanTask> ListForPlayer(
        ulong steamId,
        ClanTaskScope? scope = null,
        bool includeInactive = false,
        string clanId = "",
        string? callerEventId = null,
        string? callerSessionId = null,
        bool restrictEventTasksToCallerSession = false)
    {
        lock (_sync)
        {
            return _tasks.Values
                .Where(task => (includeInactive || task.Status == ClanTaskStatus.Active) &&
                                (!scope.HasValue || task.Scope == scope.Value) &&
                                (string.IsNullOrWhiteSpace(task.ClanId) || task.ClanId.Equals(clanId, StringComparison.OrdinalIgnoreCase)) &&
                                (!restrictEventTasksToCallerSession || task.Scope == ClanTaskScope.World ||
                                 MatchesEventContext(task, callerEventId, callerSessionId)) &&
                                (task.AssignedSteamIds.Count == 0 || task.AssignedSteamIds.Contains(steamId)))
                .OrderBy(task => task.Scope)
                .ThenBy(task => task.Status)
                .ThenBy(task => task.CreatedAtUtc)
                .Select(Clone)
                .ToList();
        }
    }

    public OperationResult<ClanTask> RecordGatheredItem(
        ulong steamId,
        int prefabGuidHash,
        int amount,
        string clanId = "",
        string? callerEventId = null,
        string? callerSessionId = null)
    {
        List<(string TaskId, ClanTaskScope Scope)> matches;
        lock (_sync)
        {
            matches = _tasks.Values
                .Where(task => task.Status == ClanTaskStatus.Active &&
                               task.Objective.Type == ClanTaskObjectiveType.GatherItem &&
                               task.Objective.PrefabGuidHash == prefabGuidHash &&
                               (task.Scope == ClanTaskScope.World || MatchesEventContext(task, callerEventId, callerSessionId)) &&
                               (string.IsNullOrWhiteSpace(task.ClanId) || task.ClanId.Equals(clanId, StringComparison.OrdinalIgnoreCase)) &&
                               (task.AssignedSteamIds.Count == 0 || task.AssignedSteamIds.Contains(steamId)))
                .Select(task => (task.TaskId, task.Scope))
                .ToList();
        }
        if (matches.Count == 0)
            return OperationResult<ClanTask>.Fail("No active clan gathering task matched this item.");
        OperationResult<ClanTask>? last = null;
        foreach (var match in matches)
            last = AddProgress(
                match.TaskId,
                amount,
                steamId,
                trustedGather: true,
                callerEventId: match.Scope == ClanTaskScope.Event ? callerEventId : null,
                callerSessionId: match.Scope == ClanTaskScope.Event ? callerSessionId : null);
        return last!;
    }

    public void Tick(float deltaSeconds)
    {
        var shouldSave = false;
        lock (_sync)
        {
            var now = DateTime.UtcNow;
            foreach (var task in _tasks.Values.Where(t => t.Status == ClanTaskStatus.Active && t.ExpiresAtUtc is not null && t.ExpiresAtUtc <= now))
            {
                task.Status = ClanTaskStatus.Expired;
                task.UpdatedAtUtc = now;
                MarkDirtyLocked();
            }
            _saveElapsed += Math.Max(0, deltaSeconds);
            shouldSave = _dirty && _saveElapsed >= 2f;
        }
        if (shouldSave) SaveNow();
    }

    public void SaveNow()
    {
        ClanTaskStore store;
        long savedVersion;
        lock (_sync)
        {
            if (!_dirty && File.Exists(_path)) return;
            store = new ClanTaskStore
            {
                SchemaVersion = CurrentSchemaVersion,
                Tasks = _tasks.Values.Select(Clone).OrderBy(t => t.TaskId).ToList()
            };
            savedVersion = _changeVersion;
        }

        var directory = Path.GetDirectoryName(_path);
        if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
        var temporary = _path + ".tmp";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(store, JsonOptions(writeIndented: true)));
            File.Move(temporary, _path, overwrite: true);
            lock (_sync)
            {
                if (_changeVersion == savedVersion) _dirty = false;
                _saveElapsed = 0;
            }
        }
        catch (Exception ex)
        {
            _warning?.Invoke($"[ClanTasks] Save failed: {ex.Message}");
        }
    }

    void Load()
    {
        if (!File.Exists(_path))
        {
            lock (_sync) MarkDirtyLocked();
            SaveNow();
            return;
        }
        try
        {
            var store = JsonSerializer.Deserialize<ClanTaskStore>(File.ReadAllText(_path), JsonOptions()) ?? new ClanTaskStore();
            lock (_sync)
            {
                _tasks.Clear();
                var legacyIndex = 0;
                foreach (var task in store.Tasks.Where(IsStoredTaskValid))
                {
                    if (task.Scope == ClanTaskScope.Event && !HasBoundEventContext(task))
                    {
                        // Schema v1 had mode-scoped event tasks. Keeping one active
                        // would let concurrent sessions share state, so retain it only
                        // as an expired audit record.
                        task.Status = ClanTaskStatus.Expired;
                        task.UpdatedAtUtc = DateTime.UtcNow;
                        _tasks[$"legacy-event:{NormalizeId(task.EventId)}:{NormalizeId(task.TaskId)}:{legacyIndex++}"] = task;
                        _warning?.Invoke($"[ClanTasks] Expired legacy unbound event task '{task.TaskId}' during schema migration.");
                        MarkDirtyLocked();
                        continue;
                    }

                    _tasks[StorageKey(task)] = task;
                }
            }
        }
        catch (Exception ex)
        {
            var quarantine = _path + $".invalid-{DateTime.UtcNow:yyyyMMddHHmmss}";
            try { File.Move(_path, quarantine, overwrite: false); } catch { }
            _warning?.Invoke($"[ClanTasks] Invalid store quarantined: {ex.Message}");
            lock (_sync) MarkDirtyLocked();
            SaveNow();
        }
    }

    public void ExpireEventTasks(string modeId, string sessionId)
    {
        var changed = false;
        lock (_sync)
        {
            foreach (var task in _tasks.Values.Where(task => task.Status == ClanTaskStatus.Active && task.Scope == ClanTaskScope.Event &&
                MatchesEventContext(task, modeId, sessionId)))
            {
                task.Status = ClanTaskStatus.Expired;
                task.UpdatedAtUtc = DateTime.UtcNow;
                MarkDirtyLocked();
                changed = true;
            }
        }
        if (changed) SaveNow();
    }

    static string StorageKey(ClanTask task)
    {
        var taskId = NormalizeId(task.TaskId);
        return task.Scope == ClanTaskScope.Event
            ? $"event:{NormalizeId(task.EventId)}:{NormalizeId(task.SessionId)}:{taskId}"
            : taskId;
    }

    static bool HasBoundEventContext(ClanTask task) =>
        task.Scope != ClanTaskScope.Event ||
        (!string.IsNullOrWhiteSpace(task.EventId) && !string.IsNullOrWhiteSpace(task.SessionId));

    static bool MatchesEventContext(ClanTask task, string? callerEventId, string? callerSessionId)
    {
        return task.Scope == ClanTaskScope.Event &&
               HasBoundEventContext(task) &&
               !string.IsNullOrWhiteSpace(callerEventId) &&
               !string.IsNullOrWhiteSpace(callerSessionId) &&
               NormalizeId(task.EventId).Equals(NormalizeId(callerEventId), StringComparison.OrdinalIgnoreCase) &&
               NormalizeId(task.SessionId).Equals(NormalizeId(callerSessionId), StringComparison.OrdinalIgnoreCase);
    }

    bool TryResolveStorageKeyLocked(
        string taskId,
        string? callerEventId,
        string? callerSessionId,
        bool bypassEventIsolation,
        out string storageKey,
        out string error)
    {
        storageKey = "";
        error = "";
        var normalizedTaskId = NormalizeId(taskId);
        var candidates = _tasks
            .Where(pair => pair.Value.TaskId.Equals(normalizedTaskId, StringComparison.OrdinalIgnoreCase) &&
                           HasBoundEventContext(pair.Value))
            .ToList();
        if (candidates.Count == 0)
        {
            error = $"Clan task '{taskId}' was not found.";
            return false;
        }

        var hasEventContext = !string.IsNullOrWhiteSpace(callerEventId);
        var hasSessionContext = !string.IsNullOrWhiteSpace(callerSessionId);
        var eventCandidates = candidates.Where(pair => pair.Value.Scope == ClanTaskScope.Event).ToList();
        var exactEventCandidates = eventCandidates
            .Where(pair => MatchesEventContext(pair.Value, callerEventId, callerSessionId))
            .ToList();
        if (exactEventCandidates.Count == 1)
        {
            storageKey = exactEventCandidates[0].Key;
            return true;
        }
        if (exactEventCandidates.Count > 1)
        {
            error = $"Clan task '{taskId}' has duplicate records for this event session.";
            return false;
        }

        // A caller that supplied event context must never fall back to another
        // same-id event task or a world task when an event task exists. This is
        // the boundary that prevents session A from mutating session B.
        if (eventCandidates.Count > 0 && (hasEventContext || hasSessionContext))
        {
            if (!hasEventContext)
                error = $"Event task '{taskId}' requires event context.";
            else if (!hasSessionContext)
                error = $"Event task '{taskId}' requires session context.";
            else
            {
                var expected = eventCandidates[0].Value;
                error = $"Event task '{taskId}' belongs to event '{expected.EventId}' and session '{expected.SessionId}', not event '{callerEventId}' and session '{callerSessionId}'.";
            }
            return false;
        }

        var worldCandidates = candidates.Where(pair => pair.Value.Scope == ClanTaskScope.World).ToList();
        if (worldCandidates.Count == 1)
        {
            storageKey = worldCandidates[0].Key;
            return true;
        }

        if (eventCandidates.Count == 1 && bypassEventIsolation)
        {
            // An explicit administrative force operation can address one uniquely
            // identifiable event task, but never an ambiguous same-id task.
            storageKey = eventCandidates[0].Key;
            return true;
        }

        if (eventCandidates.Count > 0)
        {
            error = $"Event task '{taskId}' requires both event and session context.";
            return false;
        }

        error = $"Clan task '{taskId}' is ambiguous.";
        return false;
    }

    static JsonSerializerOptions JsonOptions(bool writeIndented = false) => new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        WriteIndented = writeIndented
    };

    void MarkDirtyLocked()
    {
        _dirty = true;
        _changeVersion++;
    }

    static string? Validate(CreateClanTaskRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.TaskId)) return "taskId is required.";
        if (NormalizeId(request.TaskId).Length > 80) return "taskId must be 80 characters or fewer.";
        if (string.IsNullOrWhiteSpace(request.Description)) return "description is required.";
        if (request.TargetAmount <= 0 || request.TargetAmount > 100_000_000) return "targetAmount must be between 1 and 100000000.";
        if (request.Scope == ClanTaskScope.Event &&
            (string.IsNullOrWhiteSpace(request.EventId) || string.IsNullOrWhiteSpace(request.SessionId)))
            return "Event tasks require both eventId and sessionId.";
        if (request.ObjectiveType is ClanTaskObjectiveType.BossKill or ClanTaskObjectiveType.GatherItem && request.PrefabGuidHash == 0)
            return $"{request.ObjectiveType} tasks require a prefab.";
        if (request.ExpiresAtUtc is { } expires && expires.ToUniversalTime() <= DateTime.UtcNow) return "expiresAt must be in the future.";
        return null;
    }

    static bool IsStoredTaskValid(ClanTask task) =>
        !string.IsNullOrWhiteSpace(task.TaskId) && !string.IsNullOrWhiteSpace(task.Description) &&
        task.TargetAmount > 0 && task.CurrentAmount >= 0 && task.CurrentAmount <= task.TargetAmount;

    static string NormalizeId(string value) =>
        new(value.Trim().ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' or '.' ? c : '_').ToArray());

    static string SanitizeText(string value, int maxLength)
    {
        var safe = (value ?? "").Replace('\r', ' ').Replace('\n', ' ').Replace("<", "").Replace(">", "").Trim();
        return safe.Length <= maxLength ? safe : safe[..maxLength];
    }

    static ClanTask Clone(ClanTask task) => new()
    {
        TaskId = task.TaskId, Description = task.Description, ClanId = task.ClanId,
        AssignedSteamIds = new HashSet<ulong>(task.AssignedSteamIds), Scope = task.Scope,
        EventId = task.EventId, SessionId = task.SessionId,
        Objective = new ClanTaskObjective { Type = task.Objective.Type, PrefabGuidHash = task.Objective.PrefabGuidHash, PrefabName = task.Objective.PrefabName },
        TargetAmount = task.TargetAmount, CurrentAmount = task.CurrentAmount, Status = task.Status,
        Contributions = new Dictionary<ulong, int>(task.Contributions), CreatedAtUtc = task.CreatedAtUtc,
        UpdatedAtUtc = task.UpdatedAtUtc, ExpiresAtUtc = task.ExpiresAtUtc, CompletedAtUtc = task.CompletedAtUtc,
        RewardPoints = task.RewardPoints
    };

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        SaveNow();
    }
}
