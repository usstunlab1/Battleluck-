using Unity.Entities;
using Unity.Mathematics;
using Unity.Collections;

namespace BattleLuck.ECS.Actions.Components;

/// <summary>
/// ECS action component for loading a schematic into the world.
/// Replaces blueprint-based world mutation.
/// </summary>
public struct SchematicLoadAction
{
    public FixedString64Bytes SchematicId;
    public float3 Center;
    public float Rotation;
    public Entity SessionEntity;
}

/// <summary>
/// ECS action component for clearing a spawned schematic.
/// </summary>
public struct SchematicClearAction
{
    public FixedString64Bytes SchematicId;
    public Entity SessionEntity;
}
