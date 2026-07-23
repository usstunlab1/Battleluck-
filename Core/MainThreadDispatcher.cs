/// <summary>
/// Global main-thread action dispatcher for systems that detect events off-pipeline
/// (e.g., Harmony patches) but must mutate ECS safely on the server tick thread.
/// </summary>
public static class MainThreadDispatcher
{
    static readonly ConcurrentQueue<Action> _queue = new();
    const int DefaultMaxActionsPerTick = 256;
    static readonly TimeSpan DefaultTimeBudget = TimeSpan.FromMilliseconds(5);

    public static int PendingCount => _queue.Count;

    public static void Enqueue(Action action)
    {
        if (action == null) return;
        _queue.Enqueue(action);
    }

    public static int ProcessQueue(
        int maxActions = DefaultMaxActionsPerTick,
        TimeSpan? timeBudget = null)
    {
        if (maxActions <= 0)
            return 0;

        var processed = 0;
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var budget = timeBudget ?? DefaultTimeBudget;
        while (processed < maxActions &&
               stopwatch.Elapsed < budget &&
               _queue.TryDequeue(out var action))
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                BattleLuckPlugin.LogWarning($"[MainThreadDispatcher] Action failed: {ex.Message}");
            }

            processed++;
        }

        return processed;
    }

    public static void Clear()
    {
        while (_queue.TryDequeue(out _)) { }
    }
}
