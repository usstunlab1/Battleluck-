using BattleLuck.Core;

/// <summary>
/// Static validator helper for action registry validation.
/// Provides extension methods for parsing action strings and checking if actions are known.
/// </summary>
public static class ActionRegistryValidator
{
    /// <summary>
    /// Parses an action string into name and parameters.
    /// Format: actionName:key=value|key2=value2
    /// </summary>
    public static (string actionName, Dictionary<string, string> parameters) ParseActionString(string actionString)
    {
        var parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        
        if (string.IsNullOrWhiteSpace(actionString))
            return (string.Empty, parameters);

        var colonIdx = actionString.IndexOf(':');
        string actionName;
        
        if (colonIdx >= 0)
        {
            actionName = actionString[..colonIdx].Trim();
            var paramPart = actionString[(colonIdx + 1)..];
            
            foreach (var pair in paramPart.Split('|', StringSplitOptions.RemoveEmptyEntries))
            {
                var eqIdx = pair.IndexOf('=');
                if (eqIdx > 0)
                {
                    var key = pair[..eqIdx].Trim();
                    var value = pair[(eqIdx + 1)..].Trim();
                    parameters[key] = value;
                }
            }
        }
        else
        {
            actionName = actionString.Trim();
        }

        return (actionName, parameters);
    }

    /// <summary>
    /// Normalizes action name using ActionRegistry alias mappings.
    /// </summary>
    public static string NormalizeActionName(string actionName)
    {
        return BattleLuck.Services.Runtime.ActionRegistry.Normalize(actionName);
    }

    /// <summary>
    /// Checks if an action is registered/known.
    /// </summary>
    public static bool IsKnown(string actionName)
    {
        return BattleLuck.Services.Runtime.ActionRegistry.IsKnown(actionName);
    }

    /// <summary>
    /// Validates actions in a mode config and returns any issues found.
    /// </summary>
    public static IReadOnlyList<string> Validate(string modeId, ModeConfig config)
    {
        var issues = new List<string>();
        // Validation is done by FlowValidator now
        // This method exists for compatibility
        return issues;
    }

    /// <summary>
    /// Tries to parse a float parameter value.
    /// </summary>
    public static bool TryGetFloat(Dictionary<string, string> parameters, string key, out float value)
    {
        value = 0f;
        if (!parameters.TryGetValue(key, out var str))
            return false;
        
        return float.TryParse(str, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }

    /// <summary>
    /// Gets a float parameter with fallback.
    /// </summary>
    public static float GetFloat(Dictionary<string, string> parameters, string key, float fallback = 0f)
    {
        return TryGetFloat(parameters, key, out var value) ? value : fallback;
    }
}
