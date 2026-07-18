// Services/Runtime/CapabilityRegistry.cs
//
// Thread-safe registry of ActionCapabilityDescriptors.
// Pure C# — no BepInEx/VRising/Unity references.

namespace BattleLuck.Services.Runtime;

/// <summary>
/// Central registry for <see cref="ActionCapabilityDescriptor"/> entries.
///
/// <para>Register descriptors during plugin initialization.  The validation
/// pipeline, AI safety layer, and admin confirmation prompts all read from
/// this registry.</para>
/// </summary>
public class CapabilityRegistry : ICapabilityRegistry
{
    private readonly Dictionary<string, ActionCapabilityDescriptor> _entries =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly object _lock = new();

    // ── Registration ──────────────────────────────────────────────────────────

    /// <summary>
    /// Register a capability descriptor.  Overwrites any existing entry
    /// with the same action name (last-writer-wins during startup).
    /// </summary>
    public void Register(ActionCapabilityDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);

        if (string.IsNullOrWhiteSpace(descriptor.ActionName))
            throw new ArgumentException("ActionName must not be empty.", nameof(descriptor));

        lock (_lock)
        {
            _entries[descriptor.ActionName] = descriptor;
        }
    }

    /// <summary>Register multiple descriptors in one call.</summary>
    public void RegisterAll(IEnumerable<ActionCapabilityDescriptor> descriptors)
    {
        foreach (var d in descriptors)
            Register(d);
    }

    // ── Lookup ────────────────────────────────────────────────────────────────

    /// <summary>Returns the descriptor for <paramref name="actionName"/>, or null if not found.</summary>
    public ActionCapabilityDescriptor? TryGet(string actionName)
    {
        lock (_lock)
        {
            return _entries.GetValueOrDefault(actionName);
        }
    }

    /// <summary>Returns true when an action with <paramref name="actionName"/> is registered.</summary>
    public bool IsRegistered(string actionName)
    {
        lock (_lock)
        {
            return _entries.ContainsKey(actionName);
        }
    }

    /// <summary>Returns a snapshot of all registered descriptors.</summary>
    public IReadOnlyList<ActionCapabilityDescriptor> GetAll()
    {
        lock (_lock)
        {
            return _entries.Values.ToList();
        }
    }

    /// <summary>Removes all entries (primarily for test isolation).</summary>
    public void Clear()
    {
        lock (_lock)
        {
            _entries.Clear();
        }
    }

    // ── ICapabilityRegistry implementation ─────────────────────────────────────

    void ICapabilityRegistry.Register(IRuntimeCapability capability)
    {
        ArgumentNullException.ThrowIfNull(capability);
        // For now, this is a no-op since we use ActionCapabilityDescriptor
        // This can be expanded later if needed
    }

    void ICapabilityRegistry.Unregister(string capabilityId)
    {
        lock (_lock)
        {
            _entries.Remove(capabilityId);
        }
    }

    IRuntimeCapability? ICapabilityRegistry.Get(string capabilityId)
    {
        // Return null for now - this interface is for MCP integration
        return null;
    }

    List<CapabilityDescriptorDto> ICapabilityRegistry.ListCapabilities(string? category)
    {
        lock (_lock)
        {
            return _entries.Values.Select(d => new CapabilityDescriptorDto
            {
                Id = d.ActionName,
                Category = "Runtime",
                Description = d.Description,
                IsAvailable = true
            }).ToList();
        }
    }

    Task<object?> ICapabilityRegistry.ExecuteAsync(string capabilityId, Dictionary<string, object> payload)
    {
        // This is for MCP integration - return not implemented for now
        return Task.FromResult<object?>(null);
    }

    void ICapabilityRegistry.Subscribe(string capabilityId, Func<CapabilityChangeEvent, Task> handler)
    {
        // Event subscription for MCP - no-op for now
    }
}


