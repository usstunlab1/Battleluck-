using Unity.Entities;

namespace BattleLuck.ECS.Actions.Components;

/// <summary>
/// Marker component indicating that this action requires visual validation
/// before execution. Validation systems will ensure entity exists, linked
/// entities exist, visual buffers exist, prefab is valid, and network
/// ownership is safe before mutation.
/// </summary>
public struct RequiresVisualValidation
{
}
