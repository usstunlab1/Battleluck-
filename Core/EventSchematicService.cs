using BattleLuck.Models;
using BattleLuck.Utilities;
using Unity.Entities;
using Unity.Mathematics;
using BattleLuck.Core.Validation;

namespace BattleLuck.Core;

/// <summary>
/// Primary service for managing event schematics.
/// Replaces EventBlueprintService as the canonical way to load world designs.
/// </summary>
public static class EventSchematicService
{
    /// <summary>
    /// Loads a schematic by name and prepares its world manifestation.
    /// Includes AI validation if requested.
    /// </summary>
    public static OperationResult<SchematicLoadReport> Load(
        string eventName, 
        float3 center, 
        bool validateWithAi = true)
    {
        if (string.IsNullOrWhiteSpace(eventName))
            return OperationResult<SchematicLoadReport>.Fail("Event name is required.");

        var schematic = SchematicLoader.GetSchematic(eventName);
        if (schematic == null)
            return OperationResult<SchematicLoadReport>.Fail($"Schematic '{eventName}' not found.");

        if (validateWithAi)
        {
            var issues = SchematicValidator.Validate(eventName, new ModeConfig { Schematics = new[] { schematic } });
            if (issues.Count > 0)
                return OperationResult<SchematicLoadReport>.Fail($"Schematic validation failed: {string.Join(", ", issues)}");
        }

        return SchematicLoader.LoadIntoWorld(eventName, center, allowLiveWorldMutations: true);
    }

    /// <summary>
    /// Captures a world area into a new schematic.
    /// </summary>
    public static OperationResult<SchematicConfig> Capture(string eventName, float3 center, float radius)
    {
        return SchematicLoader.CaptureNearby(eventName, center, radius);
    }

    /// <summary>
    /// Clears a previously spawned schematic.
    /// </summary>
    public static OperationResult<SchematicClearReport> Clear(string eventName)
    {
        return SchematicLoader.ClearByEventName(eventName);
    }
}
