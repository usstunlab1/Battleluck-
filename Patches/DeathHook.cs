using BattleLuck.ECS.Events;
using BattleLuck.Services.Runtime;
using Unity.Entities;

/// <summary>
/// Compatibility event for consumers that have not yet migrated to
/// <see cref="ProjectMEventRouter.OnPlayerDeath"/>. The typed router owns the
/// sole Harmony death-system postfix and forwards each event here once.
/// </summary>
internal static class DeathHook
{
    static readonly object Sync = new();
    static ProjectMEventRouter? _router;

    /// <summary>Event raised when any entity dies on the server.</summary>
    public static event Action<Entity, Entity>? OnDeath; // (died, killer)

    public static void Initialize(ProjectMEventRouter router)
    {
        ArgumentNullException.ThrowIfNull(router);
        lock (Sync)
        {
            if (ReferenceEquals(_router, router))
                return;

            if (_router != null)
                _router.OnPlayerDeath -= HandlePlayerDeath;

            _router = router;
            _router.OnPlayerDeath += HandlePlayerDeath;
        }
    }

    public static void Shutdown()
    {
        lock (Sync)
        {
            if (_router != null)
                _router.OnPlayerDeath -= HandlePlayerDeath;

            _router = null;
            OnDeath = null;
        }
    }

    static void HandlePlayerDeath(PlayerDeathEvent deathEvent)
    {
        var handlers = OnDeath;
        if (handlers == null)
            return;

        foreach (Action<Entity, Entity> handler in handlers.GetInvocationList())
        {
            try
            {
                handler(deathEvent.Died, deathEvent.Killer);
            }
            catch (Exception ex)
            {
                BattleLuckPlugin.LogWarning($"[DeathHook] Handler error: {ex.ToString()}");
            }
        }
    }
}
