namespace BattleLuck.Services.Runtime;

/// <summary>
/// Pure parser for BattleLuck's canonical <c>action:key=value|key=value</c>
/// representation. Kept outside the ECS executor so validation and tooling can
/// run without loading Unity reference assemblies.
/// </summary>
public static class ActionStringParser
{
    public static (string actionName, Dictionary<string, string> parameters) Parse(string? actionString)
    {
        var value = actionString ?? string.Empty;
        var parts = value.Split(':', 2);
        var parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (parts.Length > 1)
        {
            foreach (var parameter in parts[1].Split('|', StringSplitOptions.RemoveEmptyEntries))
            {
                var pair = parameter.Split('=', 2);
                if (pair.Length == 2 && !string.IsNullOrWhiteSpace(pair[0]))
                    parameters[pair[0].Trim()] = pair[1].Trim();
            }
        }

        return (parts[0].Trim(), parameters);
    }
}
