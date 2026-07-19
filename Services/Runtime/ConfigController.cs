using System;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text.Json;
using BattleLuck.Models;
using BattleLuck.Utilities;

namespace BattleLuck.Services.Runtime;

public static class ConfigController
{
    private static readonly object ConfigWriteLock = new();

    public static OperationResult Reload(string modeId)
    {
        try
        {
            var normalized = modeId?.Trim();

            if (string.IsNullOrWhiteSpace(normalized) ||
                normalized.Equals("all", StringComparison.OrdinalIgnoreCase))
            {
                ConfigLoader.ReloadAll();

                BattleLuckPlugin.LogInfo(
                    "[ConfigController] Reloaded all event configurations.");

                return OperationResult.Ok();
            }

            if (!TryValidateEventId(normalized, out var error))
                return OperationResult.Fail(error);

            ConfigLoader.Reload(normalized);

            BattleLuckPlugin.LogInfo(
                $"[ConfigController] Reloaded event configuration '{normalized}'.");

            return OperationResult.Ok();
        }
        catch (Exception ex)
        {
            BattleLuckPlugin.LogError(
                $"[ConfigController] Failed to reload configuration: {ex}");

            return OperationResult.Fail($"Reload failed: {ex.Message}");
        }
    }

    public static OperationResult SetRule(
        string modeId,
        string ruleName,
        string value)
    {
        return MutateEvent(
            modeId,
            definition =>
            {
                if (definition.Rules == null)
                    return "Event definition has no rules object.";

                var property = typeof(EventRulesDefinition).GetProperty(
                    ruleName,
                    BindingFlags.Public |
                    BindingFlags.Instance |
                    BindingFlags.IgnoreCase);

                if (property == null || !property.CanWrite)
                {
                    return
                        $"Rule '{ruleName}' was not found or cannot be modified.";
                }

                if (!TryConvertValue(
                        value,
                        property.PropertyType,
                        out var typedValue,
                        out var conversionError))
                {
                    return conversionError;
                }

                property.SetValue(definition.Rules, typedValue);
                return null;
            },
            $"Updated rule '{ruleName}' to '{value}'");
    }

    public static OperationResult SetMetadata(
        string modeId,
        string key,
        string value)
    {
        if (key.Equals("Id", StringComparison.OrdinalIgnoreCase))
        {
            return OperationResult.Fail(
                "metadata.id is immutable because it identifies the event.");
        }

        if (key.Equals("Prompt", StringComparison.OrdinalIgnoreCase))
        {
            return OperationResult.Fail(
                "Event prompts must be updated through SetPrompt.");
        }

        return MutateEvent(
            modeId,
            definition =>
            {
                if (definition.Metadata == null)
                    return "Event definition has no metadata object.";

                var property = typeof(EventMetadata).GetProperty(
                    key,
                    BindingFlags.Public |
                    BindingFlags.Instance |
                    BindingFlags.IgnoreCase);

                if (property == null || !property.CanWrite)
                {
                    return
                        $"Metadata property '{key}' was not found or cannot be modified.";
                }

                if (!TryConvertValue(
                        value,
                        property.PropertyType,
                        out var typedValue,
                        out var conversionError))
                {
                    return conversionError;
                }

                property.SetValue(definition.Metadata, typedValue);
                return null;
            },
            $"Updated metadata '{key}' to '{value}'");
    }

    public static OperationResult SetPrompt(
        string modeId,
        string promptText)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(modeId) ||
                modeId.Equals("global", StringComparison.OrdinalIgnoreCase))
            {
                var globalPath = Path.Combine(
                    ConfigLoader.ConfigRoot,
                    "ai_operator_prompt.md");

                lock (ConfigWriteLock)
                {
                    WriteAtomic(globalPath, promptText);
                }

                BattleLuckPlugin.LogInfo(
                    "[ConfigController] Updated global AI operator prompt.");

                return OperationResult.Ok();
            }

            var eventId = modeId.Trim();

            if (!TryValidateEventId(eventId, out var error))
                return OperationResult.Fail(error);

            var definitionPath = GetEventDefinitionPath(eventId);

            if (!File.Exists(definitionPath))
                return OperationResult.Fail($"Event '{eventId}' not found.");

            var promptPath = Path.Combine(
                BattleLuck.Core.Loaders.ModeConfigLoader.EventsRoot,
                eventId,
                "prompt.txt");

            lock (ConfigWriteLock)
            {
                WriteAtomic(promptPath, promptText);
            }

            ConfigLoader.Reload(eventId);

            BattleLuckPlugin.LogInfo(
                $"[ConfigController] Updated prompt for event '{eventId}'.");

            return OperationResult.Ok();
        }
        catch (Exception ex)
        {
            BattleLuckPlugin.LogError(
                $"[ConfigController] Failed to update prompt: {ex}");

            return OperationResult.Fail($"SetPrompt failed: {ex.Message}");
        }
    }

    private static OperationResult MutateEvent(
        string modeId,
        Func<UnifiedEventDefinition, string?> mutation,
        string successMessage)
    {
        try
        {
            var eventId = modeId?.Trim() ?? string.Empty;

            if (!TryValidateEventId(eventId, out var validationError))
                return OperationResult.Fail(validationError);

            var path = GetEventDefinitionPath(eventId);

            if (!File.Exists(path))
                return OperationResult.Fail($"Event '{eventId}' not found.");

            lock (ConfigWriteLock)
            {
                var json = File.ReadAllText(path);

                var definition =
                    JsonSerializer.Deserialize<UnifiedEventDefinition>(
                        json,
                        ConfigLoader.JsonOptions);

                if (definition == null)
                {
                    return OperationResult.Fail(
                        $"Failed to parse event '{eventId}'.");
                }

                if (definition.Metadata == null ||
                    !string.Equals(
                        definition.Metadata.Id,
                        eventId,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return OperationResult.Fail(
                        $"Event metadata ID must match filename '{eventId}'.");
                }

                var mutationError = mutation(definition);

                if (!string.IsNullOrWhiteSpace(mutationError))
                    return OperationResult.Fail(mutationError);

                var writeOptions =
                    new JsonSerializerOptions(ConfigLoader.JsonOptions)
                    {
                        WriteIndented = true
                    };

                var updatedJson =
                    JsonSerializer.Serialize(definition, writeOptions);

                WriteAtomic(path, updatedJson);
            }

            ConfigLoader.Reload(eventId);

            BattleLuckPlugin.LogInfo(
                $"[ConfigController] {successMessage} for event '{eventId}'.");

            return OperationResult.Ok();
        }
        catch (Exception ex)
        {
            BattleLuckPlugin.LogError(
                $"[ConfigController] Event mutation failed: {ex}");

            return OperationResult.Fail(
                $"Configuration update failed: {ex.Message}");
        }
    }

    private static string GetEventDefinitionPath(string eventId)
    {
        return Path.Combine(
            BattleLuck.Core.Loaders.ModeConfigLoader.EventsRoot,
            $"{eventId}.json");
    }

    private static bool TryValidateEventId(
        string eventId,
        out string error)
    {
        if (string.IsNullOrWhiteSpace(eventId))
        {
            error = "Event ID is required.";
            return false;
        }

        foreach (var character in eventId)
        {
            if (!char.IsLetterOrDigit(character) &&
                character != '_' &&
                character != '-')
            {
                error =
                    $"Invalid event ID '{eventId}'. Use letters, numbers, underscores or hyphens.";
                return false;
            }
        }

        error = string.Empty;
        return true;
    }

    private static bool TryConvertValue(
        string value,
        Type propertyType,
        out object? result,
        out string error)
    {
        var targetType =
            Nullable.GetUnderlyingType(propertyType) ?? propertyType;

        if (Nullable.GetUnderlyingType(propertyType) != null &&
            value.Equals("null", StringComparison.OrdinalIgnoreCase))
        {
            result = null;
            error = string.Empty;
            return true;
        }

        if (targetType == typeof(string))
        {
            result = value;
            error = string.Empty;
            return true;
        }

        if (targetType == typeof(bool))
        {
            if (bool.TryParse(value, out var boolValue))
            {
                result = boolValue;
                error = string.Empty;
                return true;
            }

            result = null;
            error = $"'{value}' is not a valid boolean.";
            return false;
        }

        if (targetType.IsEnum)
        {
            if (Enum.TryParse(
                    targetType,
                    value,
                    ignoreCase: true,
                    out var enumValue))
            {
                result = enumValue;
                error = string.Empty;
                return true;
            }

            result = null;
            error = $"'{value}' is not valid for {targetType.Name}.";
            return false;
        }

        try
        {
            result = Convert.ChangeType(
                value,
                targetType,
                CultureInfo.InvariantCulture);

            error = string.Empty;
            return true;
        }
        catch
        {
            result = null;
            error =
                $"'{value}' cannot be converted to {targetType.Name}.";

            return false;
        }
    }

    private static void WriteAtomic(string path, string content)
    {
        var directory = Path.GetDirectoryName(path);

        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        var temporaryPath = $"{path}.{Guid.NewGuid():N}.tmp";

        try
        {
            File.WriteAllText(temporaryPath, content);
            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }
    }
}
