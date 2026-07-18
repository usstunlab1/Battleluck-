// Services/Runtime/InMemoryRuntimeEventBus.cs
//
// Lightweight in-process pub/sub bus for runtime events.
// Pure C# — no BepInEx/VRising/Unity references.

namespace BattleLuck.Services.Runtime;

// ── Event envelope ────────────────────────────────────────────────────────────

/// <summary>
/// Envelope that wraps every event published on the bus.
/// </summary>
public sealed class RuntimeBusEvent
{
    public string EventName { get; init; } = string.Empty;
    public string? SessionId { get; init; }
    public int ZoneHash { get; init; }
    public string? CorrelationId { get; init; }
    public IReadOnlyDictionary<string, object?> Payload { get; init; }
        = new Dictionary<string, object?>();
    public DateTimeOffset OccurredAt { get; init; } = DateTimeOffset.UtcNow;
}

// ── Bus interface ─────────────────────────────────────────────────────────────

/// <summary>
/// In-process publish/subscribe bus for BattleLuck runtime events.
/// </summary>
public interface IRuntimeEventBus
{
    /// <summary>Publish an event to all registered handlers.</summary>
    void Publish(RuntimeBusEvent evt);

    /// <summary>
    /// Subscribe to events matching <paramref name="eventName"/>.
    /// Returns a disposable token; disposing it unsubscribes.
    /// </summary>
    IDisposable Subscribe(string eventName, Action<RuntimeBusEvent> handler);

    /// <summary>Subscribe to ALL events regardless of name.</summary>
    IDisposable SubscribeAll(Action<RuntimeBusEvent> handler);
}

// ── Implementation ────────────────────────────────────────────────────────────

/// <summary>
/// Thread-safe in-memory implementation of <see cref="IRuntimeEventBus"/>.
/// </summary>
public class InMemoryRuntimeEventBus : IRuntimeEventBus
{
    private readonly object _lock = new();
    private readonly Dictionary<string, List<(Guid id, Action<RuntimeBusEvent> handler)>>
        _named = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<(Guid id, Action<RuntimeBusEvent> handler)>
        _catchAll = new();

    public void Publish(RuntimeBusEvent evt)
    {
        ArgumentNullException.ThrowIfNull(evt);

        List<Action<RuntimeBusEvent>> snapshot;

        lock (_lock)
        {
            snapshot = new List<Action<RuntimeBusEvent>>();

            if (_named.TryGetValue(evt.EventName, out var named))
                snapshot.AddRange(named.Select(x => x.handler));

            snapshot.AddRange(_catchAll.Select(x => x.handler));
        }

        foreach (var handler in snapshot)
        {
            try { handler(evt); }
            catch { /* isolate handler failures from each other */ }
        }
    }

    public IDisposable Subscribe(string eventName, Action<RuntimeBusEvent> handler)
    {
        if (string.IsNullOrWhiteSpace(eventName))
            throw new ArgumentException("Event name cannot be null or whitespace.", nameof(eventName));
        ArgumentNullException.ThrowIfNull(handler);

        var id = Guid.NewGuid();

        lock (_lock)
        {
            if (!_named.TryGetValue(eventName, out var list))
                _named[eventName] = list = new();

            list.Add((id, handler));
        }

        return new Subscription(() =>
        {
            lock (_lock)
            {
                if (_named.TryGetValue(eventName, out var list))
                    list.RemoveAll(x => x.id == id);
            }
        });
    }

    public IDisposable SubscribeAll(Action<RuntimeBusEvent> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);

        var id = Guid.NewGuid();

        lock (_lock)
        {
            _catchAll.Add((id, handler));
        }

        return new Subscription(() =>
        {
            lock (_lock)
            {
                _catchAll.RemoveAll(x => x.id == id);
            }
        });
    }

    // ── Internals ─────────────────────────────────────────────────────────────

    private sealed class Subscription : IDisposable
    {
        private readonly Action _unsubscribe;
        private bool _disposed;

        public Subscription(Action unsubscribe) => _unsubscribe = unsubscribe;

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _unsubscribe();
        }
    }
}



/// <summary>
/// Implementation of IMcpRuntimeEventBus for MCP server integration.
/// </summary>
public sealed class McpRuntimeEventBus : IMcpRuntimeEventBus
{
    private readonly object _lock = new();
    private readonly Dictionary<string, List<Func<RuntimeEventDto, Task>>> _handlers = new();

    public void Subscribe(string eventType, Func<RuntimeEventDto, Task> handler)
    {
        if (string.IsNullOrWhiteSpace(eventType))
            throw new ArgumentException("Event type cannot be null or whitespace.", nameof(eventType));
        ArgumentNullException.ThrowIfNull(handler);

        lock (_lock)
        {
            if (!_handlers.TryGetValue(eventType, out var list))
                _handlers[eventType] = list = new();

            list.Add(handler);
        }
    }

    public void Unsubscribe(string eventType, Func<RuntimeEventDto, Task> handler)
    {
        if (string.IsNullOrWhiteSpace(eventType))
            throw new ArgumentException("Event type cannot be null or whitespace.", nameof(eventType));
        ArgumentNullException.ThrowIfNull(handler);

        lock (_lock)
        {
            if (_handlers.TryGetValue(eventType, out var list))
                list.Remove(handler);
        }
    }

    public Task PublishAsync(RuntimeEventDto @event)
    {
        ArgumentNullException.ThrowIfNull(@event);

        List<Func<RuntimeEventDto, Task>> handlers;

        lock (_lock)
        {
            handlers = _handlers.TryGetValue(@event.Type, out var list) 
                ? new List<Func<RuntimeEventDto, Task>>(list) 
                : new List<Func<RuntimeEventDto, Task>>();
        }

        var tasks = handlers.Select(h => h(@event));
        return Task.WhenAll(tasks);
    }

    public List<string> GetEventTypes()
    {
        lock (_lock)
        {
            return _handlers.Keys.ToList();
        }
    }
}
