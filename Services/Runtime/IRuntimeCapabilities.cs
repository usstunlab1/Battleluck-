using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace BattleLuck.Services.Runtime
{
    /// <summary>
    /// Defines a discoverable runtime capability that MCP servers can query
    /// </summary>
    public interface IRuntimeCapability
    {
        string Id { get; }
        string Category { get; }
        string Description { get; }
        bool IsAvailable { get; }
        Task<object?> ExecuteAsync(Dictionary<string, object> payload);
    }

    /// <summary>
    /// Registry of all runtime capabilities accessible to MCP
    /// </summary>
    public interface ICapabilityRegistry
    {
        /// <summary>
        /// Register a capability
        /// </summary>
        void Register(IRuntimeCapability capability);

        /// <summary>
        /// Unregister a capability
        /// </summary>
        void Unregister(string capabilityId);

        /// <summary>
        /// Get capability by ID
        /// </summary>
        IRuntimeCapability? Get(string capabilityId);

        /// <summary>
        /// List all available capabilities
        /// </summary>
        List<CapabilityDescriptorDto> ListCapabilities(string? category = null);

        /// <summary>
        /// Execute a capability
        /// </summary>
        Task<object?> ExecuteAsync(string capabilityId, Dictionary<string, object> payload);

        /// <summary>
        /// Subscribe to capability changes
        /// </summary>
        void Subscribe(string capabilityId, Func<CapabilityChangeEvent, Task> handler);
    }

    public class CapabilityDescriptorDto
    {
        public string Id { get; set; } = "";
        public string Category { get; set; } = "";
        public string Description { get; set; } = "";
        public bool IsAvailable { get; set; }
        public Dictionary<string, string> InputSchema { get; set; } = new();
        public string? OutputType { get; set; }
    }

    public class CapabilityChangeEvent
    {
        public string CapabilityId { get; set; } = "";
        public CapabilityChangeType ChangeType { get; set; }
        public DateTime OccurredUtc { get; set; }
    }

    public enum CapabilityChangeType
    {
        Registered,
        Unregistered,
        AvailabilityChanged
    }

    /// <summary>
    /// Runtime event bus for MCP servers to subscribe to runtime events
    /// </summary>
    public interface IMcpRuntimeEventBus
    {
        /// <summary>
        /// Subscribe to events of a type
        /// </summary>
        void Subscribe(string eventType, Func<RuntimeEventDto, Task> handler);

        /// <summary>
        /// Unsubscribe from events
        /// </summary>
        void Unsubscribe(string eventType, Func<RuntimeEventDto, Task> handler);

        /// <summary>
        /// Publish an event
        /// </summary>
        Task PublishAsync(RuntimeEventDto @event);

        /// <summary>
        /// Get available event types
        /// </summary>
        List<string> GetEventTypes();
    }

    public class RuntimeEventDto
    {
        public string Type { get; set; } = "";
        public string Source { get; set; } = "";
        public DateTime OccurredUtc { get; set; }
        public Dictionary<string, object> Payload { get; set; } = new();
    }

    /// <summary>
    /// Bootstrap and lifecycle management for runtime services
    /// </summary>
    public interface IRuntimeServiceBootstrap
    {
        Task InitializeAsync();
        Task ShutdownAsync();
        
        IEcsQueryService EcsQueryService { get; }
        IPrefabRegistryService PrefabRegistry { get; }
        ISessionRuntimeService SessionRuntime { get; }
        IFlowValidationService FlowValidation { get; }
        ICapabilityRegistry CapabilityRegistry { get; }
        IMcpRuntimeEventBus EventBus { get; }
        ISnapshotService SnapshotService { get; }

        bool IsInitialized { get; }
        string? LastError { get; }
    }
}
