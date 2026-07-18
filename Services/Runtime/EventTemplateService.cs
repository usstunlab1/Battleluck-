using System.Text.RegularExpressions;

namespace BattleLuck.Services.Runtime;

/// <summary>
/// Creates an editable event by cloning an existing event folder. The default
/// template is Bloodbath, so a new event gets the same lifecycle, kit,
/// rollback-safe entry/exit flow, and prompt policy before customization.
/// </summary>
public sealed class EventTemplateService
{
    static readonly Regex ValidId = new("^[a-z0-9][a-z0-9_-]{1,31}$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    static readonly string[] TemplateFiles = { "flow.json", "zones.json", "kits.json", "prompt.txt" };

    public OperationResult<CustomEventResult> CreateFromTemplate(
        string modeId,
        string templateId = "bloodbath",
        string? displayName = null)
    {
        modeId = NormalizeId(modeId);
        templateId = NormalizeId(templateId);

        if (modeId.Length == 0)
            return OperationResult<CustomEventResult>.Fail("Event id is required. Use lowercase letters, numbers, '_' or '-'.");
        if (templateId.Length == 0)
            return OperationResult<CustomEventResult>.Fail("Template id is required.");
        if (modeId.Equals(templateId, StringComparison.OrdinalIgnoreCase))
            return OperationResult<CustomEventResult>.Fail("The new event id must be different from its template.");

        var eventsRoot = Path.Combine(ConfigLoader.ConfigRoot, "events");
        var sourceDirectory = Path.Combine(eventsRoot, templateId);
        var targetDirectory = Path.Combine(eventsRoot, modeId);
        var sourceFlow = Path.Combine(sourceDirectory, "flow.json");

        if (!File.Exists(sourceFlow))
            return OperationResult<CustomEventResult>.Fail($"Template event '{templateId}' was not found at '{sourceFlow}'.");
        if (Directory.Exists(targetDirectory) || File.Exists(Path.Combine(eventsRoot, $"{modeId}.json")))
            return OperationResult<CustomEventResult>.Fail($"Event '{modeId}' already exists.");

        var resolvedDisplayName = string.IsNullOrWhiteSpace(displayName)
            ? ToDisplayName(modeId)
            : displayName.Trim();
        var sourceZoneHash = ReadFirstZoneHash(sourceFlow);
        var targetZoneHash = FindUniqueZoneHash(eventsRoot);
        var stagingDirectory = Path.Combine(eventsRoot, $".{modeId}.creating-{Guid.NewGuid():N}");

        try
        {
            Directory.CreateDirectory(stagingDirectory);
            foreach (var fileName in TemplateFiles)
            {
                var source = Path.Combine(sourceDirectory, fileName);
                if (File.Exists(source))
                    File.Copy(source, Path.Combine(stagingDirectory, fileName));
            }

            RewriteFlow(Path.Combine(stagingDirectory, "flow.json"), modeId, templateId, resolvedDisplayName, sourceZoneHash, targetZoneHash);
            RewriteZones(Path.Combine(stagingDirectory, "zones.json"), modeId, templateId, sourceZoneHash, targetZoneHash);
            RewritePrompt(Path.Combine(stagingDirectory, "prompt.txt"), modeId, templateId, resolvedDisplayName);

            Directory.Move(stagingDirectory, targetDirectory);
            KitController.ClearCache();
            ConfigLoader.InvalidateCache();

            return OperationResult<CustomEventResult>.Ok(new CustomEventResult(
                modeId,
                resolvedDisplayName,
                templateId,
                Path.Combine(targetDirectory, "flow.json"),
                targetZoneHash));
        }
        catch (Exception ex)
        {
            try
            {
                if (Directory.Exists(stagingDirectory))
                    Directory.Delete(stagingDirectory, recursive: true);
            }
            catch
            {
                // Preserve the original creation error; the staging folder is
                // harmless and can be removed by the next maintenance pass.
            }

            return OperationResult<CustomEventResult>.Fail($"Could not create event '{modeId}': {ex.Message}");
        }
    }

    static void RewriteFlow(string path, string modeId, string templateId, string displayName, int sourceZoneHash, int targetZoneHash)
    {
        var definition = JsonSerializer.Deserialize<UnifiedEventDefinition>(File.ReadAllText(path), ConfigLoader.JsonOptions)
            ?? throw new InvalidDataException("Template flow.json is empty or invalid.");

        definition.Metadata.Id = modeId;
        definition.Metadata.DisplayName = displayName;
        definition.Metadata.Version = "1";

        foreach (var zone in definition.Zones)
        {
            if (zone.KitId.Equals(templateId, StringComparison.OrdinalIgnoreCase))
                zone.KitId = modeId;
            if (sourceZoneHash == 0 || zone.Hash == sourceZoneHash)
                zone.Hash = targetZoneHash;
        }

        foreach (var action in EnumerateActions(definition))
        {
            if (!action.Type.Equals("kit.apply", StringComparison.OrdinalIgnoreCase) &&
                !action.Action.Equals("kit.apply", StringComparison.OrdinalIgnoreCase))
                continue;

            if (action.Params.TryGetValue("kitId", out var kitValue) &&
                kitValue.ValueKind == JsonValueKind.String &&
                kitValue.GetString()?.Equals(templateId, StringComparison.OrdinalIgnoreCase) == true)
            {
                action.Params["kitId"] = JsonDocument.Parse(JsonSerializer.Serialize(modeId)).RootElement.Clone();
            }

            ReplaceZoneHashParameter(action, "zoneHash", sourceZoneHash, targetZoneHash);
            ReplaceZoneHashParameter(action, "targetZoneHash", sourceZoneHash, targetZoneHash);
        }

        File.WriteAllText(path, JsonSerializer.Serialize(definition, ConfigLoader.JsonOptions));
    }

    static void RewriteZones(string path, string modeId, string templateId, int sourceZoneHash, int targetZoneHash)
    {
        if (!File.Exists(path))
            return;

        var zones = JsonSerializer.Deserialize<ZonesConfig>(File.ReadAllText(path), ConfigLoader.JsonOptions);
        if (zones == null)
            return;

        foreach (var zone in zones.Zones)
        {
            if (zone.KitId.Equals(templateId, StringComparison.OrdinalIgnoreCase))
                zone.KitId = modeId;
            if (zone.Type.Equals(templateId, StringComparison.OrdinalIgnoreCase))
                zone.Type = modeId;
            if (sourceZoneHash == 0 || zone.Hash == sourceZoneHash)
                zone.Hash = targetZoneHash;
        }

        File.WriteAllText(path, JsonSerializer.Serialize(zones, ConfigLoader.JsonOptions));
    }

    static void RewritePrompt(string path, string modeId, string templateId, string displayName)
    {
        if (!File.Exists(path))
            return;

        var text = File.ReadAllText(path);
        text = Regex.Replace(text, $"(?im)^eventId:\\s*{Regex.Escape(templateId)}\\s*$", $"eventId: {modeId}");
        text = Regex.Replace(text, $"\\b{Regex.Escape(ToDisplayName(templateId))}\\b", displayName, RegexOptions.IgnoreCase);
        File.WriteAllText(path, text);
    }

    static IEnumerable<EventActionDefinition> EnumerateActions(UnifiedEventDefinition definition)
    {
        foreach (var action in definition.Actions)
            yield return action;
        foreach (var item in definition.Objects)
            foreach (var action in item.Actions)
                yield return action;
        foreach (var item in definition.Glows)
            foreach (var action in item.Actions)
                yield return action;
        foreach (var phase in definition.Phases)
            foreach (var action in phase.Actions)
                yield return action;
        foreach (var timer in definition.Timers)
            foreach (var action in timer.OnCompleteActions)
                yield return action;
        foreach (var trigger in definition.Triggers)
            foreach (var action in trigger.Actions)
                yield return action;
    }

    static void ReplaceZoneHashParameter(EventActionDefinition action, string key, int sourceZoneHash, int targetZoneHash)
    {
        if (!action.Params.TryGetValue(key, out var value))
            return;

        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number) &&
            (sourceZoneHash == 0 || number == sourceZoneHash))
        {
            action.Params[key] = JsonDocument.Parse(targetZoneHash.ToString(System.Globalization.CultureInfo.InvariantCulture)).RootElement.Clone();
        }
        else if (value.ValueKind == JsonValueKind.String && int.TryParse(value.GetString(), out number) &&
                 (sourceZoneHash == 0 || number == sourceZoneHash))
        {
            action.Params[key] = JsonDocument.Parse(JsonSerializer.Serialize(targetZoneHash.ToString(System.Globalization.CultureInfo.InvariantCulture))).RootElement.Clone();
        }
    }

    static int ReadFirstZoneHash(string flowPath)
    {
        try
        {
            var definition = JsonSerializer.Deserialize<UnifiedEventDefinition>(File.ReadAllText(flowPath), ConfigLoader.JsonOptions);
            return definition?.Zones.FirstOrDefault()?.Hash ?? 0;
        }
        catch
        {
            return 0;
        }
    }

    static int FindUniqueZoneHash(string eventsRoot)
    {
        var used = new HashSet<int>();
        if (Directory.Exists(eventsRoot))
        {
            var paths = Directory.EnumerateFiles(eventsRoot, "*.json", SearchOption.TopDirectoryOnly)
                .Concat(Directory.EnumerateDirectories(eventsRoot)
                    .Select(directory => Path.Combine(directory, "flow.json"))
                    .Where(File.Exists));

            foreach (var path in paths)
            {
                try
                {
                    using var document = JsonDocument.Parse(File.ReadAllText(path));
                    if (!document.RootElement.TryGetProperty("zones", out var zones) || zones.ValueKind != JsonValueKind.Array)
                        continue;
                    foreach (var zone in zones.EnumerateArray())
                    {
                        if (zone.TryGetProperty("hash", out var hash) && hash.TryGetInt32(out var value))
                            used.Add(value);
                    }
                }
                catch
                {
                    // A separate validator reports malformed existing files.
                }
            }
        }

        var candidate = 10000;
        while (used.Contains(candidate))
            candidate++;
        return candidate;
    }

    static string NormalizeId(string value)
    {
        var trimmed = (value ?? "").Trim().ToLowerInvariant();
        return ValidId.IsMatch(trimmed) ? trimmed : "";
    }

    static string ToDisplayName(string modeId)
    {
        return string.Join(" ", modeId.Split(new[] { '-', '_' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(part => part.Length == 0 ? part : char.ToUpperInvariant(part[0]) + part[1..]));
    }
}

public sealed record CustomEventResult(string ModeId, string DisplayName, string TemplateId, string FlowPath, int ZoneHash);
