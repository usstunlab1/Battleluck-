using System.Collections.Concurrent;
using Unity.Entities;
using Unity.Mathematics;

namespace BattleLuck.Services.Placement;

/// <summary>
/// Bounded work queue for operations that must not monopolize a single server tick.
/// Operations are executed in controlled batches per tick to prevent main-thread stalls.
///
/// Supported operation types:
/// - Placement (floor/tile/schematic)
/// - Spawn (entities)
/// - Destroy (entities)
/// - Rollback (snapshot component writes)
/// - Wave cleanup
/// </summary>
public static class BoundedWorkQueue
{
    public enum WorkType { Placement, Spawn, Destroy, Rollback, Cleanup }

    public sealed class WorkItem
    {
        public WorkType Type { get; init; }
        public string SessionId { get; init; } = "";
        public int ZoneHash { get; init; }
        public string PrefabName { get; init; } = "";
        public int PrefabGuid { get; init; }
        public float3 Position { get; init; }
        public string BatchId { get; init; } = "";
        public int Priority { get; init; }
        public Func<bool>? Execute { get; init; }
    }

    static readonly ConcurrentQueue<WorkItem> _queue = new();
    static readonly ConcurrentDictionary<string, int> _pendingCounts = new();
    static PlacementConfig _config = new();

    /// <summary>
    /// Configure batch size limits. Call once at startup.
    /// </summary>
    public static void Configure(PlacementConfig config)
    {
        _config = config ?? new PlacementConfig();
    }

    /// <summary>
    /// Enqueue a placement work item.
    /// </summary>
    public static void EnqueuePlacement(string sessionId, int zoneHash, string prefabName, int prefabGuid, float3 position, string batchId)
    {
        Enqueue(new WorkItem
        {
            Type = WorkType.Placement,
            SessionId = sessionId,
            ZoneHash = zoneHash,
            PrefabName = prefabName,
            PrefabGuid = prefabGuid,
            Position = position,
            BatchId = batchId,
            Priority = 2
        });
    }

    /// <summary>
    /// Enqueue a spawn work item.
    /// </summary>
    public static void EnqueueSpawn(string sessionId, int zoneHash, string prefabName, int prefabGuid, float3 position, string batchId)
    {
        Enqueue(new WorkItem
        {
            Type = WorkType.Spawn,
            SessionId = sessionId,
            ZoneHash = zoneHash,
            PrefabName = prefabName,
            PrefabGuid = prefabGuid,
            Position = position,
            BatchId = batchId,
            Priority = 1
        });
    }

    /// <summary>
    /// Enqueue a destroy work item.
    /// </summary>
    public static void EnqueueDestroy(string sessionId, int zoneHash, string prefabName, int prefabGuid, float3 position, string batchId)
    {
        Enqueue(new WorkItem
        {
            Type = WorkType.Destroy,
            SessionId = sessionId,
            ZoneHash = zoneHash,
            PrefabName = prefabName,
            PrefabGuid = prefabGuid,
            Position = position,
            BatchId = batchId,
            Priority = 3
        });
    }

    /// <summary>
    /// Enqueue a rollback work item.
    /// </summary>
    public static void EnqueueRollback(string sessionId, int zoneHash, Func<bool> execute)
    {
        Enqueue(new WorkItem
        {
            Type = WorkType.Rollback,
            SessionId = sessionId,
            ZoneHash = zoneHash,
            BatchId = $"rollback_{Guid.NewGuid():N}"[..20],
            Execute = execute,
            Priority = 10 // Rollbacks execute first
        });
    }

    /// <summary>
    /// Enqueue a cleanup work item (wave cleanup, entity cleanup, etc.)
    /// </summary>
    public static void EnqueueCleanup(string sessionId, int zoneHash, Func<bool> execute, string batchId)
    {
        Enqueue(new WorkItem
        {
            Type = WorkType.Cleanup,
            SessionId = sessionId,
            ZoneHash = zoneHash,
            BatchId = batchId,
            Execute = execute,
            Priority = 4
        });
    }

    /// <summary>
    /// Process a batch of work items. Called once per server tick.
    /// Returns the number of items processed.
    /// </summary>
    public static int ProcessBatch()
    {
        var processed = 0;
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        // Process rollback items first (highest priority)
        processed += ProcessWorkType(WorkType.Rollback, _config.MaxRollbackWritesPerTick);

        // Then spawns
        processed += ProcessWorkType(WorkType.Spawn, _config.MaxSpawnsPerTick);

        // Then placements
        processed += ProcessWorkType(WorkType.Placement, _config.MaxPlacementsPerTick);

        // Then cleanups
        processed += ProcessWorkType(WorkType.Cleanup, _config.MaxRollbackWritesPerTick);

        // Then destroys
        processed += ProcessWorkType(WorkType.Destroy, _config.MaxDestroysPerTick);

        stopwatch.Stop();

        if (stopwatch.ElapsedMilliseconds > 25)
        {
            BattleLuckPlugin.LogWarning(
                $"[PlacementQueue] Tick duration exceeded threshold: {stopwatch.ElapsedMilliseconds:F0}ms " +
                $"(processed {processed} items, {_queue.Count} remaining)");
        }

        return processed;
    }

    /// <summary>
    /// Process a specific work type up to its max per tick limit.
    /// </summary>
    static int ProcessWorkType(WorkType type, int maxPerTick)
    {
        var processed = 0;
        var tempItems = new List<WorkItem>();

        // Dequeue up to maxPerTick items of the requested type
        while (processed < maxPerTick && _queue.TryDequeue(out var item))
        {
            if (item.Type == type)
            {
                try
                {
                    var success = item.Execute?.Invoke() ?? ExecuteWorkItem(item);
                    if (success)
                    {
                        BattleLuckPlugin.LogInfo(
                            $"[PlacementQueue] batch-{type}: {item.BatchId} pos=({item.Position.x:F1},{item.Position.z:F1}) " +
                            $"session={item.SessionId[..Math.Min(8, item.SessionId.Length)]}");
                    }
                }
                catch (Exception ex)
                {
                    BattleLuckPlugin.LogWarning($"[PlacementQueue] Failed {type} item {item.BatchId}: {ex.Message}");
                }
                processed++;
            }
            else
            {
                // Not the type we're processing now — hold for next batch
                tempItems.Add(item);
            }
        }

        // Re-queue items that were skipped
        foreach (var temp in tempItems)
            _queue.Enqueue(temp);

        return processed;
    }

    /// <summary>
    /// Execute a standard work item (placement, spawn, destroy).
    /// </summary>
    static bool ExecuteWorkItem(WorkItem item)
    {
        // This is a placeholder for actual execution logic.
        // The actual entity spawn/place/destroy is specific to the game engine.
        BattleLuckPlugin.LogInfo($"[PlacementQueue] executing {item.Type} prefab={item.PrefabName} at ({item.Position.x:F1},{item.Position.z:F1})");
        return true;
    }

    static void Enqueue(WorkItem item)
    {
        _queue.Enqueue(item);
        _pendingCounts.AddOrUpdate(item.BatchId, 1, (_, count) => count + 1);
    }

    /// <summary>
    /// Get the number of pending work items.
    /// </summary>
    public static int PendingCount => _queue.Count;

    /// <summary>
    /// Get total pending for a specific batch.
    /// </summary>
    public static int GetBatchPending(string batchId) =>
        _pendingCounts.TryGetValue(batchId, out var count) ? count : 0;

    /// <summary>
    /// Cancel all pending work for a session (e.g. on event end).
    /// </summary>
    public static int CancelSession(string sessionId)
    {
        var cancelled = 0;
        var remaining = new ConcurrentQueue<WorkItem>();

        while (_queue.TryDequeue(out var item))
        {
            if (item.SessionId.Equals(sessionId, StringComparison.Ordinal))
            {
                cancelled++;
                // Decrement pending count
                _pendingCounts.AddOrUpdate(item.BatchId, 0, (_, count) => Math.Max(0, count - 1));
            }
            else
            {
                remaining.Enqueue(item);
            }
        }

        // Re-queue remaining items
        while (remaining.TryDequeue(out var item))
            _queue.Enqueue(item);

        if (cancelled > 0)
            BattleLuckPlugin.LogInfo($"[PlacementQueue] Cancelled {cancelled} pending work items for session {sessionId}.");

        return cancelled;
    }

    /// <summary>
    /// Clear all pending work.
    /// </summary>
    public static void Clear()
    {
        while (_queue.TryDequeue(out _)) { }
        _pendingCounts.Clear();
    }
}