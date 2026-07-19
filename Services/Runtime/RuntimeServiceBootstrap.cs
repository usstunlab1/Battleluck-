namespace BattleLuck.Services.Runtime
{
    using System.Threading;
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
        private readonly SemaphoreSlim _lifecycleGate = new(1, 1);

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
            await _lifecycleGate.WaitAsync().ConfigureAwait(false);
            try
            {
                if (IsInitialized)
                    return;

                _sessionRuntime.ConnectToSessionController(BattleLuckPlugin.Session);
                IsInitialized = true;
                LastError = null;
                BattleLuckLogger.Log("[Runtime] Service bootstrap initialized");
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                BattleLuckLogger.Error($"Failed to initialize runtime services: {ex.Message}");
            }
            finally
            {
                _lifecycleGate.Release();
            }
        }

        public async Task ShutdownAsync()
        {
            await _lifecycleGate.WaitAsync().ConfigureAwait(false);
            try
            {
                if (!IsInitialized)
                    return;

                _sessionRuntime.ConnectToSessionController(null);
                IsInitialized = false;
                LastError = null;
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
            }
            finally
            {
                _lifecycleGate.Release();
            }
        }
    }
}
