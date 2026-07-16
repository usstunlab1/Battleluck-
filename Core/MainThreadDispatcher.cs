/// <summary>
/// Global main-thread action dispatcher for systems that detect events off-pipeline
/// (e.g., Harmony patches) but must mutate ECS safely on the server tick thread.
/// </summary>
public static class MainThreadDispatcher
{
    static readonly ConcurrentQueue<Action> _queue = new();

    public static void Enqueue(Action action)
    {
        if (action == null) return;
        _queue.Enqueue(action);
    }

    public static void ProcessQueue()
    {
        while (_queue.TryDequeue(out var action))
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                BattleLuckPlugin.LogWarning($"[MainThreadDispatcher] Action failed: {ex.Message}");
            }
        }
    }
}
