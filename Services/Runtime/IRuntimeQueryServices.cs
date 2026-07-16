using System;
using System.Collections.Generic;
using System.Threading.Tasks;


namespace BattleLuck.Services.Runtime
{
    /// <summary>
    /// Provides read-only ECS entity queries for AI and admin tooling.
    /// All results are serializable DTOs, never raw ECS objects.
    /// </summary>
    public interface IEcsQueryService
    {
        /// <summary>
        /// Get snapshot of an entity
        /// </summary>
        Task<EntitySnapshotDto?> InspectEntityAsync(int entityIndex);

        /// <summary>
        /// Query entities by component type
        /// </summary>
        Task<List<EntitySnapshotDto>> QueryEntitiesByComponentAsync(string componentType, int limit = 50);

        /// <summary>
        /// List all registered ECS systems
        /// </summary>
        Task<List<EcsSystemInfoDto>> ListSystemsAsync(string? filterName = null);

        /// <summary>
        /// Get component statistics across all entities
        /// </summary>
        Task<ComponentStatisticsDto> QueryComponentStatsAsync(string componentType);

        /// <summary>
        /// Find entities near a position
        /// </summary>
        Task<List<EntitySnapshotDto>> FindEntitiesNearAsync(float x, float y, float z, float radius, int limit = 20);

        /// <summary>
        /// Resolve prefab information from entity
        /// </summary>
        Task<PrefabInfoDto?> ResolvePrefabAsync(int entityIndex);
    }

    /// <summary>
    /// Provides prefab registry queries and analysis
    /// </summary>
    public interface IPrefabRegistryService
    {
        /// <summary>
        /// Scan all available prefabs
        /// </summary>
        Task<List<PrefabInfoDto>> ScanPrefabsAsync(string? categoryFilter = null);

        /// <summary>
        /// Get detailed prefab information
        /// </summary>
        Task<PrefabInfoDto?> GetPrefabAsync(string prefabName);

        /// <summary>
        /// List components in a prefab
        /// </summary>
        Task<List<ComponentDescriptorDto>> GetPrefabComponentsAsync(string prefabName);

        /// <summary>
        /// Analyze prefab for issues
        /// </summary>
        Task<PrefabAnalysisDto> AnalyzePrefabAsync(string prefabName);

        /// <summary>
        /// Find prefabs by tag
        /// </summary>
        Task<List<PrefabInfoDto>> FindPrefabsByTagAsync(string tag);

        /// <summary>
        /// Get prefab dependency graph
        /// </summary>
        Task<PrefabDependencyGraphDto> GetDependencyGraphAsync(string prefabName);
    }

    /// <summary>
    /// Provides active session and player state queries
    /// </summary>
    public interface ISessionRuntimeService
    {
        /// <summary>
        /// Get current active session
        /// </summary>
        Task<SessionStateDto?> GetCurrentSessionAsync();

        /// <summary>
        /// Get session by ID
        /// </summary>
        Task<SessionStateDto?> GetSessionAsync(string sessionId);

        /// <summary>
        /// List all active sessions
        /// </summary>
        Task<List<SessionStateDto>> ListSessionsAsync();

        /// <summary>
        /// Get player statistics in a session
        /// </summary>
        Task<PlayerStatsDto?> GetPlayerStatsAsync(string steamId, string sessionId);

        /// <summary>
        /// Get session leaderboard
        /// </summary>
        Task<List<PlayerLeaderboardEntryDto>> GetLeaderboardAsync(string sessionId);

        /// <summary>
        /// Get current flow state
        /// </summary>
        Task<FlowStateDto?> GetFlowStateAsync(string sessionId);

        /// <summary>
        /// Emit a session event (admin only)
        /// </summary>
        Task<bool> EmitSessionEventAsync(string sessionId, string eventType, Dictionary<string, object>? data = null);

        /// <summary>
        /// List all available actions with metadata
        /// </summary>
        Task<List<ActionDefinitionDto>> ListActionsAsync();

        /// <summary>
        /// Get actions valid for a specific mode
        /// </summary>
        Task<List<ActionDefinitionDto>> ListActionsForModeAsync(string modeId);

        /// <summary>
        /// Get details for a specific action
        /// </summary>
        Task<ActionDefinitionDto?> GetActionAsync(string actionName);

        /// <summary>
        /// Execute an action and return result
        /// </summary>
        Task<ActionResultDto> ExecuteActionAsync(string actionName, string steamId, string sessionId, Dictionary<string, string> parameters);
    }

    /// <summary>
    /// Provides flow configuration validation and analysis
    /// </summary>
    public interface IFlowValidationService
    {
        /// <summary>
        /// Validate flow configuration
        /// </summary>
        Task<FlowValidationResultDto> ValidateFlowAsync(string flowName, string modeId);

        /// <summary>
        /// Analyze flow for optimization opportunities
        /// </summary>
        Task<FlowOptimizationDto> AnalyzeFlowAsync(string flowName);

        /// <summary>
        /// Debug a specific flow transition
        /// </summary>
        Task<FlowTransitionDebugDto> DebugTransitionAsync(string flowName, string fromState, string toState);

        /// <summary>
        /// Generate flow code from description
        /// </summary>
        Task<string> GenerateFlowAsync(string description, string modeId);

        /// <summary>
        /// Get all available flow states
        /// </summary>
        Task<List<FlowStateDefinitionDto>> ListFlowStatesAsync(string flowName);

        /// <summary>
        /// Check for circular dependencies in flows
        /// </summary>
        Task<CircularDependencyCheckDto> CheckCircularDependenciesAsync(string flowName);
    }

    /// <summary>
    /// Snapshot and diff system for runtime state management
    /// </summary>
    public interface ISnapshotService
    {
        /// <summary>
        /// Capture a runtime snapshot
        /// </summary>
        Task<RuntimeSnapshotDto> CaptureSnapshotAsync(string? sessionId = null);

        /// <summary>
        /// Get a snapshot by ID
        /// </summary>
        Task<RuntimeSnapshotDto?> GetSnapshotAsync(string snapshotId);

        /// <summary>
        /// List all snapshots
        /// </summary>
        Task<List<RuntimeSnapshotDto>> ListSnapshotsAsync(string? sessionId = null);

        /// <summary>
        /// Diff two snapshots
        /// </summary>
        Task<SnapshotDiffDto> DiffSnapshotsAsync(string fromSnapshotId, string toSnapshotId);

        /// <summary>
        /// Rollback to a snapshot (admin only)
        /// </summary>
        Task<bool> RollbackToSnapshotAsync(string snapshotId);

        /// <summary>
        /// Delete a snapshot
        /// </summary>
        Task<bool> DeleteSnapshotAsync(string snapshotId);
    }
}
