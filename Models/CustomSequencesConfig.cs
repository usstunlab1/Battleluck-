using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BattleLuck.Models;

public sealed class CustomSequencesConfig
{
    [JsonPropertyName("version")]
    public int Version { get; set; } = 1;

    [JsonPropertyName("updatedUtc")]
    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;

    [JsonPropertyName("sequences")]
    public Dictionary<string, CustomSequenceDefinition> Sequences { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

[JsonConverter(typeof(CustomSequenceDefinitionJsonConverter))]
public sealed class CustomSequenceDefinition
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("displayName")]
    public string DisplayName { get; set; } = "";

    [JsonPropertyName("description")]
    public string Description { get; set; } = "";

    /// <summary>
    /// Optional event definition identifier that scopes this sequence.
    /// Historically named ModeId for compatibility.
    /// Empty means the sequence is not restricted to one event.
    /// </summary>
    [JsonPropertyName("modeId")]
    public string ModeId { get; set; } = "";

    [JsonPropertyName("riskLevel")]
    public string RiskLevel { get; set; } = "controlled";

    [JsonPropertyName("requiresApproval")]
    public bool RequiresApproval { get; set; } = true;

    [JsonPropertyName("createdBy")]
    public string CreatedBy { get; set; } = "";

    [JsonPropertyName("createdUtc")]
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

    [JsonPropertyName("updatedUtc")]
    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;

    [JsonPropertyName("tags")]
    public List<string> Tags { get; set; } = new();

    [JsonPropertyName("variables")]
    public Dictionary<string, string> Variables { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    [JsonPropertyName("steps")]
    public List<CustomSequenceStep> Steps { get; set; } = new();

    [JsonIgnore]
    public int EnabledActionCount => Steps
        .Where(s => s.Enabled && s.Kind.Equals("action", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(s.Action))
        .Sum(s => Math.Clamp(s.Repeat, 1, 1000));

    [JsonIgnore]
    public bool HasTiming => Steps.Any(s =>
        s.Enabled &&
        (s.Kind.Equals("wait", StringComparison.OrdinalIgnoreCase) ||
         s.Kind.Equals("tick", StringComparison.OrdinalIgnoreCase) ||
         s.DelaySeconds > 0 ||
         s.AtSecond.HasValue));
}

public sealed class CustomSequenceStep
{
    [JsonPropertyName("stepId")]
    public string StepId { get; set; } = "";

    [JsonPropertyName("kind")]
    public string Kind { get; set; } = "action";

    [JsonPropertyName("action")]
    public string Action { get; set; } = "";

    [JsonPropertyName("delaySeconds")]
    public double DelaySeconds { get; set; }

    [JsonPropertyName("atSecond")]
    public double? AtSecond { get; set; }

    [JsonPropertyName("repeat")]
    public int Repeat { get; set; } = 1;

    [JsonPropertyName("intervalSeconds")]
    public double IntervalSeconds { get; set; }

    [JsonPropertyName("note")]
    public string Note { get; set; } = "";

    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = true;
}

public sealed class CustomSequenceRuntimeRun
{
    public string RunId { get; set; } = "";
    public string SequenceId { get; set; } = "";
    public string Reason { get; set; } = "";
    public double StartedAtElapsedSeconds { get; set; }
    public List<CustomSequenceRuntimeStep> Steps { get; set; } = new();

    [JsonIgnore]
    public bool Complete => Steps.All(s => s.Executed);
}

public sealed class CustomSequenceRuntimeStep
{
    public string StepId { get; set; } = "";
    public string Action { get; set; } = "";
    public double DueElapsedSeconds { get; set; }
    public bool Executed { get; set; }
    public int StepIndex { get; set; }
    public string StepLabel { get; set; } = "";
}

public sealed class CustomSequenceExecutionReport
{
    public string SequenceId { get; set; } = "";
    public int Executed { get; set; }
    public int Failed { get; set; }
    public int SkippedTimingMarkers { get; set; }
    public List<string> Errors { get; set; } = new();
}

sealed class CustomSequenceDefinitionJsonConverter : JsonConverter<CustomSequenceDefinition>
{
    public override CustomSequenceDefinition Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var doc = JsonDocument.ParseValue(ref reader);
        var root = doc.RootElement;

        return root.ValueKind switch
        {
            JsonValueKind.String => FromActionText(root.GetString() ?? ""),
            JsonValueKind.Array => FromArray(root, options),
            JsonValueKind.Object => FromObject(root, options),
            _ => new CustomSequenceDefinition()
        };
    }

    public override void Write(Utf8JsonWriter writer, CustomSequenceDefinition value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        WriteString(writer, "id", value.Id);
        WriteString(writer, "displayName", value.DisplayName);
        WriteString(writer, "description", value.Description);
        WriteString(writer, "modeId", value.ModeId);
        WriteString(writer, "riskLevel", value.RiskLevel);
        writer.WriteBoolean("requiresApproval", value.RequiresApproval);
        WriteString(writer, "createdBy", value.CreatedBy);
        writer.WriteString("createdUtc", value.CreatedUtc);
        writer.WriteString("updatedUtc", value.UpdatedUtc);
        writer.WritePropertyName("tags");
        JsonSerializer.Serialize(writer, value.Tags, options);
        writer.WritePropertyName("variables");
        JsonSerializer.Serialize(writer, value.Variables, options);
        writer.WritePropertyName("steps");
        JsonSerializer.Serialize(writer, value.Steps, options);
        writer.WriteEndObject();
    }

    static CustomSequenceDefinition FromActionText(string actions)
    {
        var definition = new CustomSequenceDefinition();
        foreach (var action in SplitActionText(actions))
            definition.Steps.Add(ActionStep(action));
        return definition;
    }

    static CustomSequenceDefinition FromArray(JsonElement array, JsonSerializerOptions options)
    {
        var definition = new CustomSequenceDefinition();
        foreach (var item in array.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String)
            {
                var action = item.GetString();
                if (!string.IsNullOrWhiteSpace(action))
                    definition.Steps.Add(ActionStep(action!));
            }
            else if (item.ValueKind == JsonValueKind.Object)
            {
                var step = JsonSerializer.Deserialize<CustomSequenceStep>(item.GetRawText(), options);
                if (step != null)
                    definition.Steps.Add(step);
            }
        }
        return definition;
    }

    static CustomSequenceDefinition FromObject(JsonElement obj, JsonSerializerOptions options)
    {
        var definition = new CustomSequenceDefinition
        {
            Id = ReadString(obj, "id"),
            DisplayName = ReadString(obj, "displayName"),
            Description = ReadString(obj, "description"),
            ModeId = ReadString(obj, "modeId"),
            RiskLevel = string.IsNullOrWhiteSpace(ReadString(obj, "riskLevel")) ? "controlled" : ReadString(obj, "riskLevel"),
            RequiresApproval = ReadBool(obj, "requiresApproval", true),
            CreatedBy = ReadString(obj, "createdBy"),
            CreatedUtc = ReadDate(obj, "createdUtc", DateTime.UtcNow),
            UpdatedUtc = ReadDate(obj, "updatedUtc", DateTime.UtcNow)
        };

        if (obj.TryGetProperty("tags", out var tags) && tags.ValueKind == JsonValueKind.Array)
            definition.Tags = tags.EnumerateArray()
                .Select(t => t.GetString())
                .Where(t => !string.IsNullOrWhiteSpace(t))
                .Select(t => t!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

        if (obj.TryGetProperty("variables", out var vars) && vars.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in vars.EnumerateObject())
                definition.Variables[property.Name] = property.Value.ValueKind == JsonValueKind.String
                    ? property.Value.GetString() ?? ""
                    : property.Value.ToString();
        }

        if (obj.TryGetProperty("steps", out var steps) && steps.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in steps.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String)
                {
                    var action = item.GetString();
                    if (!string.IsNullOrWhiteSpace(action))
                        definition.Steps.Add(ActionStep(action!));
                }
                else if (item.ValueKind == JsonValueKind.Object)
                {
                    var step = JsonSerializer.Deserialize<CustomSequenceStep>(item.GetRawText(), options);
                    if (step != null)
                        definition.Steps.Add(step);
                }
            }
        }

        if (obj.TryGetProperty("actions", out var actions))
        {
            if (actions.ValueKind == JsonValueKind.String)
            {
                foreach (var action in SplitActionText(actions.GetString() ?? ""))
                    definition.Steps.Add(ActionStep(action));
            }
            else if (actions.ValueKind == JsonValueKind.Array)
            {
                foreach (var action in actions.EnumerateArray().Select(a => a.GetString()).Where(a => !string.IsNullOrWhiteSpace(a)))
                    definition.Steps.Add(ActionStep(action!));
            }
        }

        return definition;
    }

    static CustomSequenceStep ActionStep(string action) => new()
    {
        Kind = "action",
        Action = action.Trim(),
        Repeat = 1,
        Enabled = true
    };

    static IEnumerable<string> SplitActionText(string actions) =>
        (actions ?? "")
            .Split(new[] { ';', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(a => !string.IsNullOrWhiteSpace(a));

    static string ReadString(JsonElement obj, string name) =>
        obj.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? ""
            : "";

    static bool ReadBool(JsonElement obj, string name, bool fallback) =>
        obj.TryGetProperty(name, out var value) && (value.ValueKind == JsonValueKind.True || value.ValueKind == JsonValueKind.False)
            ? value.GetBoolean()
            : fallback;

    static DateTime ReadDate(JsonElement obj, string name, DateTime fallback)
    {
        var text = ReadString(obj, name);
        return DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed)
            ? parsed.ToUniversalTime()
            : fallback;
    }

    static void WriteString(Utf8JsonWriter writer, string name, string value)
    {
        if (!string.IsNullOrWhiteSpace(value))
            writer.WriteString(name, value);
    }
}
