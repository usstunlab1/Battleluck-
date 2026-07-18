namespace BattleLuck.Services.Runtime
{
    /// <summary>
    /// Core DTO Models for MCP Transport
    /// All DTOs are serializable and safe for cross-boundary transport
    /// Never expose raw ECS objects or native references
    /// </summary>

    #region ECS DTOs

    public class EntitySnapshotDto
    {
        public int Index { get; set; }
        public string? PrefabName { get; set; }
        public bool IsAlive { get; set; }
        public float3Dto Position { get; set; } = new();
        public Dictionary<string, object> Components { get; set; } = new();
        public DateTime CapturedUtc { get; set; }
    }

    public class EntityQueryResultDto
    {
        public string Query { get; set; } = "";
        public int TotalCount { get; set; }
        public List<EntitySnapshotDto> Entities { get; set; } = new();
        public DateTime ExecutedUtc { get; set; } = DateTime.UtcNow;
    }

    public class SystemInfoDto
    {
        public string Name { get; set; } = "";
        public string? Group { get; set; }
        public bool IsEnabled { get; set; }
        public int EntityCount { get; set; }
    }

    public class float3Dto
    {
        public float X { get; set; }
        public float Y { get; set; }
        public float Z { get; set; }

        public static float3Dto FromUnity(float x, float y, float z)
        {
            return new float3Dto { X = x, Y = y, Z = z };
        }
    }

    public class EcsSystemInfoDto
    {
        public string Name { get; set; } = "";
        public string? Description { get; set; }
        public bool IsActive { get; set; }
        public int EntityCount { get; set; }
        public List<string> ProcessedComponentTypes { get; set; } = new();
    }

    public class ComponentStatisticsDto
    {
        public string ComponentType { get; set; } = "";
        public int InstanceCount { get; set; }
        public Dictionary<string, object> Properties { get; set; } = new();
        public DateTime CalculatedUtc { get; set; }
    }

    #endregion

    #region Prefab DTOs

    public class PrefabScanResultDto
    {
        public int TotalCount { get; set; }
        public string? CategoryFilter { get; set; }
        public List<PrefabInfoDto> Prefabs { get; set; } = new();
        public DateTime ScannedUtc { get; set; } = DateTime.UtcNow;
    }

    public class PrefabInfoDto
    {
        public string Name { get; set; } = "";
        public string? Category { get; set; }
        public long SizeBytes { get; set; }
        public List<string> Tags { get; set; } = new();
        public DateTime LastModifiedUtc { get; set; }
        public int ComponentCount { get; set; }
        public bool IsValid { get; set; }
    }

    public class ComponentDescriptorDto
    {
        public string Name { get; set; } = "";
        public string? Description { get; set; }
        public Dictionary<string, string> Properties { get; set; } = new();
    }

    public class PrefabAnalysisDto
    {
        public string PrefabName { get; set; } = "";
        public List<string> Warnings { get; set; } = new();
        public List<string> Errors { get; set; } = new();
        public PrefabPerformanceMetricsDto Performance { get; set; } = new();
    }

    public class PrefabPerformanceMetricsDto
    {
        public int PolygonCount { get; set; }
        public long TextureMemoryBytes { get; set; }
        public int ColliderCount { get; set; }
        public bool IsOptimized { get; set; }
    }

    public class PrefabDependencyGraphDto
    {
        public string RootPrefab { get; set; } = "";
        public List<DependencyNodeDto> Dependencies { get; set; } = new();
        public bool HasCircularDependency { get; set; }
    }

    public class DependencyNodeDto
    {
        public string PrefabName { get; set; } = "";
        public string DependencyType { get; set; } = "";
        public List<DependencyNodeDto> Children { get; set; } = new();
    }

    #endregion

    #region Session DTOs

    public class PlayerStateDto
    {
        public string SteamId { get; set; } = "";
        public string? CharacterName { get; set; }
        public float3Dto Position { get; set; } = new();
        public float Health { get; set; }
        public float MaxHealth { get; set; }
        public string? SessionId { get; set; }
        public int Score { get; set; }
        public DateTime LastUpdatedUtc { get; set; } = DateTime.UtcNow;
    }

    public class SessionStateDto
    {
        public string Id { get; set; } = "";
        public string ModeId { get; set; } = "";
        public DateTime StartedUtc { get; set; }
        public double ElapsedSeconds { get; set; }
        public SessionPhaseDto Phase { get; set; }
        public int PlayerCount { get; set; }
        public string? CurrentBoss { get; set; }
        public Dictionary<string, object> Metadata { get; set; } = new();
    }

    public enum SessionPhaseDto
    {
        Initializing,
        InProgress,
        BossActive,
        Completed,
        Failed
    }

    public class PlayerStatsDto
    {
        public string SteamId { get; set; } = "";
        public string? CharacterName { get; set; }
        public int Level { get; set; }
        public double GearScore { get; set; }
        public int Kills { get; set; }
        public int Deaths { get; set; }
        public int Score { get; set; }
        public int? TeamId { get; set; }
        public Dictionary<string, object> Equipment { get; set; } = new();
        public DateTime LastUpdatedUtc { get; set; }
    }

    public class PlayerLeaderboardEntryDto
    {
        public string SteamId { get; set; } = "";
        public string? CharacterName { get; set; }
        public int Score { get; set; }
        public int Rank { get; set; }
    }

    #endregion

    #region Flow DTOs

    public class FlowStateDto
    {
        public string FlowName { get; set; } = "";
        public string CurrentState { get; set; } = "";
        public List<FlowTransitionDto> AvailableTransitions { get; set; } = new();
        public Dictionary<string, object> StateData { get; set; } = new();
        public DateTime LastTransitionUtc { get; set; }
    }

    public class FlowTransitionDto
    {
        public string FromState { get; set; } = "";
        public string ToState { get; set; } = "";
        public string? TriggerEvent { get; set; }
        public Dictionary<string, object> Conditions { get; set; } = new();
    }

    public class FlowValidationResultDto
    {
        public string FlowName { get; set; } = "";
        public bool IsValid { get; set; }
        public bool CanTransition { get; set; }
        public List<ValidationErrorDto> Errors { get; set; } = new();
        public List<ValidationWarningDto> Warnings { get; set; } = new();
        public List<string> Suggestions { get; set; } = new();
        public DateTime ValidatedUtc { get; set; }
    }

    public class ValidationErrorDto
    {
        public string Code { get; set; } = "";
        public string Message { get; set; } = "";
        public string? Location { get; set; }
        public Dictionary<string, object> Context { get; set; } = new();
    }

    public class ValidationWarningDto
    {
        public string Code { get; set; } = "";
        public string Message { get; set; } = "";
        public string? Location { get; set; }
    }

    public class FlowOptimizationDto
    {
        public string FlowName { get; set; } = "";
        public List<OptimizationSuggestionDto> Suggestions { get; set; } = new();
        public double EstimatedPerformanceGain { get; set; }
    }

    public class OptimizationSuggestionDto
    {
        public string Type { get; set; } = "";
        public string Description { get; set; } = "";
        public string? ApplyLocation { get; set; }
        public int Priority { get; set; }
    }

    public class FlowTransitionDebugDto
    {
        public string FlowName { get; set; } = "";
        public string FromState { get; set; } = "";
        public string ToState { get; set; } = "";
        public bool IsValid { get; set; }
        public bool CanTransition { get; set; }
        public List<string> MissingConditions { get; set; } = new();
        public List<string> Blockers { get; set; } = new();
        public Dictionary<string, object> DebugInfo { get; set; } = new();
    }

    public class FlowStateDefinitionDto
    {
        public string Name { get; set; } = "";
        public string? Description { get; set; }
        public bool IsInitial { get; set; }
        public List<string> EnterActions { get; set; } = new();
        public List<string> ExitActions { get; set; } = new();
        public List<FlowTransitionDto> Transitions { get; set; } = new();
    }

    public class CircularDependencyCheckDto
    {
        public string FlowName { get; set; } = "";
        public bool HasCircularDependency { get; set; }
        public List<string> CircularPath { get; set; } = new();
        public List<string> AffectedStates { get; set; } = new();
    }

    #endregion

    #region Snapshot DTOs

    public class RuntimeSnapshotDto
    {
        public string Id { get; set; } = "";
        public string SessionId { get; set; } = "";
        public DateTime CapturedUtc { get; set; }
        public List<EntitySnapshotDto> Entities { get; set; } = new();
        public SessionStateDto SessionState { get; set; } = new();
        public FlowStateDto FlowState { get; set; } = new();
        public Dictionary<string, object> GlobalState { get; set; } = new();
        public long SizeBytes { get; set; }
    }

    public class SnapshotDiffDto
    {
        public string FromSnapshotId { get; set; } = "";
        public string ToSnapshotId { get; set; } = "";
        public DateTime GeneratedUtc { get; set; }
        public List<EntityChangeDto> EntityChanges { get; set; } = new();
        public List<StateChangeDto> StateChanges { get; set; } = new();
        public List<FlowChangeDto> FlowChanges { get; set; } = new();
        public SummaryStatisticsDto Summary { get; set; } = new();
    }

    public class EntityChangeDto
    {
        public int EntityIndex { get; set; }
        public ChangeTypeDto ChangeType { get; set; }
        public string? PrefabName { get; set; }
        public Dictionary<string, ComponentChangeDto> ComponentChanges { get; set; } = new();
    }

    public enum ChangeTypeDto
    {
        Added,
        Removed,
        Modified
    }

    public class ComponentChangeDto
    {
        public string ComponentName { get; set; } = "";
        public ChangeTypeDto ChangeType { get; set; }
        public Dictionary<string, PropertyChangeDto> PropertyChanges { get; set; } = new();
    }

    public class PropertyChangeDto
    {
        public string PropertyName { get; set; } = "";
        public object? OldValue { get; set; }
        public object? NewValue { get; set; }
    }

    public class StateChangeDto
    {
        public string Path { get; set; } = "";
        public object? OldValue { get; set; }
        public object? NewValue { get; set; }
    }

    public class FlowChangeDto
    {
        public string FlowName { get; set; } = "";
        public string? OldState { get; set; }
        public string? NewState { get; set; }
        public string? TriggerEvent { get; set; }
    }

    public class SummaryStatisticsDto
    {
        public int EntitiesAdded { get; set; }
        public int EntitiesRemoved { get; set; }
        public int EntitiesModified { get; set; }
        public int StateChanges { get; set; }
        public int FlowTransitions { get; set; }
    }

    #endregion

    #region Action DTOs

    public class ActionDefinitionDto
    {
        public string Name { get; set; } = "";
        public string Category { get; set; } = "";
        public string Description { get; set; } = "";
        public string ColoredLabel { get; set; } = "";
        public int DefaultPoints { get; set; }
        public bool IsEnabled { get; set; } = true;
    }

    public class ActionResultDto
    {
        public bool Success { get; set; }
        public string? Error { get; set; }
        public string? Message { get; set; }
        public DateTime ExecutedUtc { get; set; } = DateTime.UtcNow;
        public string? PlayerSteamId { get; set; }
        public string? SessionId { get; set; }
    }

    public class ActionHistoryDto
    {
        public string ActionName { get; set; } = "";
        public string PlayerSteamId { get; set; } = "";
        public DateTime ExecutedUtc { get; set; }
        public bool Success { get; set; }
        public string? Error { get; set; }
        public Dictionary<string, string> Parameters { get; set; } = new();
    }

    #endregion

    #region Action Catalog Types

    /// <summary>
    /// Action definition loaded from actions_catalog.json for action resolution.
    /// </summary>
    public class ActionDefinition
    {
        public string ActionId { get; set; } = "";
        public string Action { get; set; } = "";
        public Dictionary<string, JsonElement> Params { get; set; } = new();
        public string Category { get; set; } = "";
        public string RiskLevel { get; set; } = "";
    }

    /// <summary>
    /// Sequence definition loaded from actions_catalog.json.
    /// </summary>
    public class SequenceDefinition
    {
        public string SequenceId { get; set; } = "";
        public string Name { get; set; } = "";
        public string Description { get; set; } = "";
        public List<SequenceStep> Steps { get; set; } = new();
    }

    /// <summary>
    /// Single step within a sequence.
    /// </summary>
    public class SequenceStep
    {
        public string Id { get; set; } = "";
        public string ActionId { get; set; } = "";
        public string Action { get; set; } = "";
        public double DelaySeconds { get; set; }
        public Dictionary<string, JsonElement>? Params { get; set; }
    }

    #endregion

    #region Permission DTOs

    public class ToolPermissionDto
    {
        public string ToolId { get; set; } = "";
        public PermissionLevelDto Level { get; set; }
        public List<string> RequiredCapabilities { get; set; } = new();
        public bool RequiresAdmin { get; set; }
    }

    public enum PermissionLevelDto
    {
        Readonly,
        Mutation,
        Admin,
        Dangerous
    }

    #endregion
}
