using Unity.Entities;

namespace BattleLuck.ECS.Queries;

/// <summary>
/// Defines a cached EntityQuery for Battleluck ECS systems.
/// Prevents repeated world scans and ensures query ownership discipline.
/// </summary>
public sealed class QueryDefinition
{
    public string Name = "";
    public ComponentType[] All = Array.Empty<ComponentType>();
    public ComponentType[] Any = Array.Empty<ComponentType>();
    public ComponentType[] None = Array.Empty<ComponentType>();
    public EntityQueryOptions Options;
}
