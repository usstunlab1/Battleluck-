namespace BattleLuck.Services.Runtime;

/// <summary>
/// Small, dependency-free checks for the public event file contract. The JSON
/// schema files under config/BattleLuck/events/schemas are useful offline
/// documentation; these checks keep the server safe when jq/AJV is unavailable.
/// </summary>
public static class EventSchemaValidator
{
    public static EventSchemaValidationResult Validate(IReadOnlyDictionary<string, string> files)
    {
        var result = new EventSchemaValidationResult();
        ValidateJson(files, "flow.json", result, root =>
        {
            RequireObject(root, "metadata", "flow.json", result);
            if (TryRequireArray(root, "zones", "flow.json", result, out var zones))
                ValidateZoneObjects(zones, "center", "flow.json", result);
        });
        ValidateJson(files, "zones.json", result, root =>
        {
            if (TryRequireArray(root, "zones", "zones.json", result, out var zones))
                ValidateZoneObjects(zones, "position", "zones.json", result);
        });
        ValidateJson(files, "kits.json", result, root => RequireObject(root, "settings", "kits.json", result));

        if (!files.TryGetValue("prompt.txt", out var prompt) || string.IsNullOrWhiteSpace(prompt))
            result.Errors.Add("ESCHEMA: prompt.txt must contain a non-empty prompt contract.");

        result.Errors.AddRange(EventReferenceAllowlistValidator.Validate(files).Errors);

        return result;
    }

    static void ValidateJson(
        IReadOnlyDictionary<string, string> files,
        string file,
        EventSchemaValidationResult result,
        Action<JsonElement> validateRoot)
    {
        if (!files.TryGetValue(file, out var text) || string.IsNullOrWhiteSpace(text))
        {
            result.Errors.Add($"EMISSINGFILE: {file} is missing or empty.");
            return;
        }

        try
        {
            using var document = JsonDocument.Parse(text);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                result.Errors.Add($"ESCHEMA: {file} root must be a JSON object.");
                return;
            }

            validateRoot(document.RootElement);
        }
        catch (JsonException ex)
        {
            result.Errors.Add($"EJSONPARSE: {file} is invalid JSON ({ex.Message}).");
        }
    }

    static void RequireObject(JsonElement root, string property, string file, EventSchemaValidationResult result)
    {
        if (!root.TryGetProperty(property, out var value) || value.ValueKind != JsonValueKind.Object)
            result.Errors.Add($"ESCHEMA: {file} requires object property '{property}'.");
    }

    static bool TryRequireArray(JsonElement root, string property, string file, EventSchemaValidationResult result, out JsonElement array)
    {
        array = default;
        if (!root.TryGetProperty(property, out var value) || value.ValueKind != JsonValueKind.Array || value.GetArrayLength() == 0)
        {
            result.Errors.Add($"ESCHEMA: {file} requires a non-empty array property '{property}'.");
            return false;
        }

        array = value;
        return true;
    }

    static void ValidateZoneObjects(JsonElement zones, string positionProperty, string file, EventSchemaValidationResult result)
    {
        var index = 0;
        foreach (var zone in zones.EnumerateArray())
        {
            if (zone.ValueKind != JsonValueKind.Object)
            {
                result.Errors.Add($"ESCHEMA: {file} zones[{index}] must be an object.");
                index++;
                continue;
            }

            if (!zone.TryGetProperty("hash", out var hash) || hash.ValueKind != JsonValueKind.Number || !hash.TryGetInt32(out var hashValue) || hashValue <= 0)
                result.Errors.Add($"ESCHEMA: {file} zones[{index}].hash must be a positive integer.");
            if (!zone.TryGetProperty(positionProperty, out var position) || position.ValueKind != JsonValueKind.Object)
                result.Errors.Add($"ESCHEMA: {file} zones[{index}] requires object property '{positionProperty}'.");
            else
            {
                foreach (var axis in new[] { "x", "y", "z" })
                {
                    if (!position.TryGetProperty(axis, out var value) || value.ValueKind != JsonValueKind.Number)
                        result.Errors.Add($"ESCHEMA: {file} zones[{index}].{positionProperty}.{axis} must be numeric.");
                }
            }
            index++;
        }
    }
}

public sealed class EventSchemaValidationResult
{
    public List<string> Errors { get; } = new();
    public bool Success => Errors.Count == 0;
}
