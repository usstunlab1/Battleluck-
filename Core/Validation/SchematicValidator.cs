namespace BattleLuck.Core.Validation;

public static class SchematicValidator
{
    public static IReadOnlyList<string> Validate(string modeId, ModeConfig config)
    {
        var issues = new List<string>();
        foreach (var schematic in config.Schematics)
        {
            if (schematic.Structures.Count == 0)
                issues.Add($"Schematic '{schematic.EventName}' has no structures in mode '{modeId}'.");

            if (modeId.Contains("arena", StringComparison.OrdinalIgnoreCase))
            {
                if (schematic.ChestPositions.Count == 0)
                    issues.Add($"Arena schematic '{schematic.EventName}' is missing 'chestPositions'.");
                if (schematic.CornerPositions.Count == 0)
                    issues.Add($"Arena schematic '{schematic.EventName}' is missing 'cornerPositions'.");
            }

            foreach (var structure in schematic.Structures)
            {
                if (string.IsNullOrWhiteSpace(structure.Prefab) && structure.PrefabGuid is null)
                    issues.Add($"Schematic '{schematic.EventName}' has structure missing prefab and prefabGuid.");
            }
        }

        return issues;
    }
}
