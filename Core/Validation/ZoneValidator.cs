namespace BattleLuck.Core.Validation;

/// <summary>
/// Validates zone definitions including AI rules and schematic configuration.
/// </summary>
public static class ZoneValidator
{
    public static IReadOnlyList<string> Validate(string modeId, ModeConfig config)
    {
        var issues = new List<string>();
        var hashes = new HashSet<int>();

        // Validate rules
        if (config.Rules != null)
        {
            if (config.Rules.MaxDeathsPerParticipant < 1 || config.Rules.MaxDeathsPerParticipant > 10)
                issues.Add($"Mode '{modeId}' MaxDeathsPerParticipant must be between 1 and 10 (got {config.Rules.MaxDeathsPerParticipant}).");
            
            if (config.Rules.SafetyMode != "event_tracked_zone_only" && modeId.Contains("event", StringComparison.OrdinalIgnoreCase))
                issues.Add($"Mode '{modeId}' is an event but SafetyMode is not 'event_tracked_zone_only'.");

            if (config.Rules.SpawnRateLimitPerSecond <= 0)
                issues.Add($"Mode '{modeId}' must have a SpawnRateLimitPerSecond > 0.");
        }

        foreach (var zone in config.Zones.Zones)
        {
            if (zone.Hash == 0)
                issues.Add($"Zone '{zone.Name}' has hash=0.");
            if (!hashes.Add(zone.Hash))
                issues.Add($"Duplicate zone hash '{zone.Hash}' in mode '{modeId}'.");
            if (zone.Radius <= 0)
                issues.Add($"Zone '{zone.Name}' has non-positive radius.");
            if (zone.ExitRadius > 0 && zone.ExitRadius < zone.Radius)
                issues.Add($"Zone '{zone.Name}' has exitRadius < radius.");

            // Validate AI rules
            if (zone.AiRules != null)
            {
                if (zone.AiRules.AllowAutonomousExecution &&
                    (zone.AiRules.AllowedActions?.Count == 0))
                {
                    issues.Add($"Zone '{zone.Name}' has AI rules with autonomous execution but no allowedActions list.");
                }
            }

            // Validate schematic
            if (zone.Schematic != null)
            {
                if (string.IsNullOrWhiteSpace(zone.Schematic.Id))
                    issues.Add($"Zone '{zone.Name}' has schematic with empty id.");
                if (zone.Schematic.LoadOnEnter && zone.Schematic.ClearOnExit &&
                    zone.Schematic.Id == null)
                    issues.Add($"Zone '{zone.Name}' schematic load/clear specified but no id given.");
            }
        }

        return issues;
    }
}