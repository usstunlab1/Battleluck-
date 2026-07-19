using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using BattleLuck.Models;
using BattleLuck.Utilities;

namespace BattleLuck.Services.Runtime;

public static class ConfigController
{
    public static OperationResult Reload(string modeId)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(modeId) || modeId.Equals("all", StringComparison.OrdinalIgnoreCase))
            {
                ConfigLoader.ReloadAll();
                BattleLuckPlugin.LogInfo("[ConfigController] Reloaded all mode configurations.");
                return OperationResult.Ok();
            }

            ConfigLoader.Reload(modeId);
            BattleLuckPlugin.LogInfo($"[ConfigController] Reloaded configuration for mode '{modeId}'.");
            return OperationResult.Ok();
        }
        catch (Exception ex)
        {
            BattleLuckPlugin.LogError($"[ConfigController] Failed to reload config: {ex.Message}");
            return OperationResult.Fail($"Reload failed: {ex.Message}");
        }
    }

    public static OperationResult SetRule(string modeId, string ruleName, string value)
    {
        try
        {
            var path = Path.Combine(BattleLuck.Core.Loaders.ModeConfigLoader.EventsRoot, modeId, "flow.json");
            if (!File.Exists(path))
            {
                path = Path.Combine(BattleLuck.Core.Loaders.ModeConfigLoader.EventsRoot, $"{modeId}.json");
                if (!File.Exists(path))
                    return OperationResult.Fail($"Event '{modeId}' not found.");
            }

            var json = File.ReadAllText(path);
            var definition = JsonSerializer.Deserialize<UnifiedEventDefinition>(json, ConfigLoader.JsonOptions);
            if (definition == null) return OperationResult.Fail("Failed to parse event definition.");

            var rules = definition.Rules;
            var property = typeof(EventRulesDefinition).GetProperty(ruleName, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.IgnoreCase);
            
            if (property == null)
                return OperationResult.Fail($"Rule '{ruleName}' not found on EventRulesDefinition.");

            object? typedValue = null;
            if (property.PropertyType == typeof(int?) || property.PropertyType == typeof(int))
                typedValue = int.Parse(value);
            else if (property.PropertyType == typeof(bool?) || property.PropertyType == typeof(bool))
                typedValue = bool.Parse(value);
            else if (property.PropertyType == typeof(string))
                typedValue = value;
            else
                return OperationResult.Fail($"Unsupported rule type: {property.PropertyType.Name}");

            property.SetValue(rules, typedValue);
            
            var updatedJson = JsonSerializer.Serialize(definition, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(path, updatedJson);

            ConfigLoader.Reload(modeId);
            BattleLuckPlugin.LogInfo($"[ConfigController] Rule '{ruleName}' updated to '{value}' for mode '{modeId}'.");
            return OperationResult.Ok();
        }
        catch (Exception ex)
        {
            BattleLuckPlugin.LogError($"[ConfigController] Failed to set rule: {ex.Message}");
            return OperationResult.Fail($"SetRule failed: {ex.Message}");
        }
    }

    public static OperationResult SetMetadata(string modeId, string key, string value)
    {
        try
        {
            var path = Path.Combine(BattleLuck.Core.Loaders.ModeConfigLoader.EventsRoot, modeId, "flow.json");
            if (!File.Exists(path))
            {
                path = Path.Combine(BattleLuck.Core.Loaders.ModeConfigLoader.EventsRoot, $"{modeId}.json");
                if (!File.Exists(path))
                    return OperationResult.Fail($"Event '{modeId}' not found.");
            }

            var json = File.ReadAllText(path);
            var definition = JsonSerializer.Deserialize<UnifiedEventDefinition>(json, ConfigLoader.JsonOptions);
            if (definition == null) return OperationResult.Fail("Failed to parse event definition.");

            var metadata = definition.Metadata;
            var property = typeof(EventMetadata).GetProperty(key, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.IgnoreCase);

            if (property == null)
                return OperationResult.Fail($"Metadata key '{key}' not found on EventMetadata.");

            property.SetValue(metadata, value);

            var updatedJson = JsonSerializer.Serialize(definition, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(path, updatedJson);

            ConfigLoader.Reload(modeId);
            BattleLuckPlugin.LogInfo($"[ConfigController] Metadata '{key}' updated to '{value}' for mode '{modeId}'.");
            return OperationResult.Ok();
        }
        catch (Exception ex)
        {
            BattleLuckPlugin.LogError($"[ConfigController] Failed to set metadata: {ex.Message}");
            return OperationResult.Fail($"SetMetadata failed: {ex.Message}");
        }
    }

    public static OperationResult SetPrompt(string modeId, string promptText)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(modeId) && !modeId.Equals("global", StringComparison.OrdinalIgnoreCase))
            {
                return SetMetadata(modeId, "Prompt", promptText);
            }

            var path = Path.Combine(ConfigLoader.ConfigRoot, "ai_operator_prompt.md");
            File.WriteAllText(path, promptText);
            BattleLuckPlugin.LogInfo("[ConfigController] Global AI operator prompt updated.");
            return OperationResult.Ok();
        }
        catch (Exception ex)
        {
            BattleLuckPlugin.LogError($"[ConfigController] Failed to set prompt: {ex.Message}");
            return OperationResult.Fail($"SetPrompt failed: {ex.Message}");
        }
    }
}
