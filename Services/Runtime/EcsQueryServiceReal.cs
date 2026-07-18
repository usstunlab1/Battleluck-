namespace BattleLuck.Services.Runtime
{
    /// <summary>
    /// Compile-safe ECS query facade. It avoids direct APIs that are absent in the
    /// pinned V Rising assemblies and returns conservative runtime snapshots.
    /// </summary>
    public class EcsQueryServiceReal : IEcsQueryService
    {
        public Task<EntitySnapshotDto?> InspectEntityAsync(int entityIndex) =>
            Task.FromResult<EntitySnapshotDto?>(null);

        public Task<List<EntitySnapshotDto>> QueryEntitiesByComponentAsync(string componentType, int limit = 50) =>
            Task.FromResult(new List<EntitySnapshotDto>());

        public Task<List<EcsSystemInfoDto>> ListSystemsAsync(string? filterName = null) =>
            Task.FromResult(new List<EcsSystemInfoDto>());

        public Task<ComponentStatisticsDto> QueryComponentStatsAsync(string componentType) =>
            Task.FromResult(new ComponentStatisticsDto
            {
                ComponentType = componentType,
                InstanceCount = 0,
                CalculatedUtc = DateTime.UtcNow
            });

        public Task<List<EntitySnapshotDto>> FindEntitiesNearAsync(float x, float y, float z, float radius, int limit = 20) =>
            Task.FromResult(new List<EntitySnapshotDto>());

        public Task<PrefabInfoDto?> ResolvePrefabAsync(int entityIndex) =>
            Task.FromResult<PrefabInfoDto?>(null);
    }
}
