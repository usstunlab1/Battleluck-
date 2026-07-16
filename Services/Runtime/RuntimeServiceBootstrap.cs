namespace BattleLuck.Services.Runtime
{
    using System.Threading.Tasks;
    using Services.Runtime;

    /// <summary>
    /// Runtime service composition root used by AI, MCP, and admin tooling.
    /// </summary>
    public class RuntimeServiceBootstrapImpl : IRuntimeServiceBootstrap
    {
        private readonly EcsQueryServiceReal _ecsQueryService = new();
        private readonly PrefabRegistryServiceReal _prefabRegistry = new();
        private readonly SessionRuntimeServiceReal _sessionRuntime = new();
        private readonly FlowValidationServiceReal _flowValidation = new();
        private readonly CapabilityRegistry _capabilityRegistry = new();
        private readonly McpRuntimeEventBus _eventBus = new();
        private readonly SnapshotServiceImpl _snapshotService;

        public IEcsQueryService EcsQueryService => _ecsQueryService;
        public IPrefabRegistryService PrefabRegistry => _prefabRegistry;
        public ISessionRuntimeService SessionRuntime => _sessionRuntime;
        public IFlowValidationService FlowValidation => _flowValidation;
        public ICapabilityRegistry CapabilityRegistry => _capabilityRegistry;
        public IMcpRuntimeEventBus EventBus => _eventBus;
        public ISnapshotService SnapshotService => _snapshotService;

        public bool IsInitialized { get; private set; }
        public string? LastError { get; private set; }

        public RuntimeServiceBootstrapImpl()
        {
            _snapshotService = new SnapshotServiceImpl(_sessionRuntime);
        }

        public async Task InitializeAsync()
        {
            try
            {
                _sessionRuntime.ConnectToSessionController(BattleLuckPlugin.Session);
                await Task.CompletedTask;
                IsInitialized = true;
                LastError = null;
                BattleLuckLogger.Log("[Runtime] Service bootstrap initialized");
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                BattleLuckLogger.Error($"Failed to initialize runtime services: {ex.Message}");
            }
        }

        public async Task ShutdownAsync()
        {
            try
            {
                _sessionRuntime.ConnectToSessionController(null);
                await Task.CompletedTask;
                IsInitialized = false;
                LastError = null;
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
            }
        }
    }
}
