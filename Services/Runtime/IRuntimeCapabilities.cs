using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace BattleLuck.Services.Runtime
{
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
        IMcpRuntimeEventBus EventBus { get; }
        ISnapshotService SnapshotService { get; }

        bool IsInitialized { get; }
        string? LastError { get; }
    }
}
