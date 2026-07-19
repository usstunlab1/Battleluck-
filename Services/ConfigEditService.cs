using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using BattleLuck.Core;
using BattleLuck.Models;
using BattleLuck.Services.Runtime;

namespace BattleLuck.Services;

public sealed class ConfigEditService
{
    const string RedactedValue = "***REDACTED***";

    // v1: supported files only
    static readonly Dictionary<string, string[]> AllowedFilesByScope = new(StringComparer.OrdinalIgnoreCase)
    {
        // global scope relative paths to ConfigLoader.ConfigRoot
        { "global", new[]
            {
                "ai_config.json",
                "discord_bridge.json",
                "webhook.json",
                "ai_logger.json",
                "mcp_config.json",
                "kit_grant_rules.json",
                "special_item.json",
                "merchant_servant_actions.json",
                "custom_sequences.json"
            }
        },
        // per-mode scope relative paths
{ "mode", new[]
             {
                 "session.json",
                 "zones.json",
                 "kit.json",
                 "event.json"
         }
        },
        { "event", new[]
            {
                "event.json"
            }
         }
    };

    static readonly HashSet<string> SecretKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "auth_token",
        "api_token",
        "api_key",
        "auth_key",
        "discord_webhook_url",
        "webhook_url",
        "webhookUrl",
        "password",
        "secret",
        "token"
    };

    public async Task<string> ApplyConfigEditAsync(string scope, string? modeId, string aiFilePath)
    {
        if (string.IsNullOrWhiteSpace(scope))
            return "Missing scope.";

        if (!File.Exists(aiFilePath))
            return $"AI file not found: {aiFilePath}";

        var description = await File.ReadAllTextAsync(aiFilePath);
        if (string.IsNullOrWhiteSpace(description))
            return "AI description file is empty.";

        // Load allowed JSON configs
        var currentConfigs = LoadConfigsForScope(scope, modeId, out var loadErrors);
        if (loadErrors.Count > 0)
            return "Config load errors: " + string.Join("; ", loadErrors);

        if (currentConfigs.Count == 0)
            return "No editable config files found for this scope.";

        // Generate updated JSONs
        var aiAssistant = BattleLuckPlugin.AIAssistant;
        if (aiAssistant == null)
            return "AI Assistant is not initialized.";

        var response = await aiAssistant.GenerateConfigEditAsync(description, currentConfigs);
        if (string.IsNullOrWhiteSpace(response))
            return "AI config editing needs a healthy Google or Cloudflare text provider. Local fallback can answer/help, but it will not write JSON.";

        // Parse AI JSON response (must be JSON object with filename keys)
        JsonDocument? responseDoc;
        try
        {
            responseDoc = JsonDocument.Parse(response);
        }
        catch (Exception ex)
        {
            return $"AI response is not valid JSON: {ex.Message}";
        }

        // Validate against our current key set
        var updated = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
        var errors = new List<string>();

        foreach (var key in currentConfigs.Keys)
        {
            if (!responseDoc.RootElement.TryGetProperty(key, out var updatedElement))
            {
                errors.Add($"Missing updated content for '{key}'.");
                continue;
            }

            var safeElement = PreserveLocalSecrets(scope, modeId, key, updatedElement);

            // Validate by deserializing via ConfigLoader models
            if (!ValidateJsonForFile(scope, key, safeElement))
            {
                errors.Add($"Invalid JSON schema for '{key}'.");
                continue;
            }

            updated[key] = safeElement.Clone();
        }

        if (errors.Count > 0)
            return "AI generated invalid configuration: " + string.Join("; ", errors);

        // Backup + atomic writes
        var writeErrors = new List<string>();
        foreach (var kvp in updated)
        {
            var relativePath = GetRelativeFilePath(scope, modeId, kvp.Key);
            var absolutePath = Path.Combine(ConfigLoader.ConfigRoot, relativePath);

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(absolutePath)!);

                if (File.Exists(absolutePath))
                {
                    var bakPath = absolutePath + ".bak";
                    File.Copy(absolutePath, bakPath, overwrite: true);
                }

                var tmpPath = absolutePath + ".tmp";
                var newJson = JsonSerializer.Serialize(kvp.Value, new JsonSerializerOptions { WriteIndented = true });
                await File.WriteAllTextAsync(tmpPath, newJson);

                // File.Replace is atomic on same volume
                if (File.Exists(absolutePath))
                {
                    File.Replace(tmpPath, absolutePath, absolutePath + ".bak", ignoreMetadataErrors: true);
                }
                else
                {
                    File.Move(tmpPath, absolutePath, overwrite: true);
                }
            }
            catch (Exception ex)
            {
                writeErrors.Add($"Write failed for '{kvp.Key}': {ex.Message}");
            }
        }

        if (writeErrors.Count > 0)
            return "Config write errors: " + string.Join("; ", writeErrors);

        // Reload configs
        try
        {
            if (string.Equals(scope, "global", StringComparison.OrdinalIgnoreCase))
            {
                ConfigLoader.ReloadAIConfig();
                // also reload non-mode global configs users edited that are loaded individually
                // (those loaders don't use a cache in this v1)
            }
            else
            {
                if (!string.IsNullOrWhiteSpace(modeId))
                    ConfigLoader.Reload(modeId);
            }
        }
        catch (Exception ex)
        {
            return "Config written but reload failed: " + ex.Message;
        }

        return "Applied AI config edit successfully.";
    }

    public Dictionary<string, JsonDocument> LoadConfigsForScope(string scope, string? modeId, out List<string> errors)
    {
        errors = new List<string>();
        var result = new Dictionary<string, JsonDocument>(StringComparer.OrdinalIgnoreCase);

        string scopeKey = string.Equals(scope, "global", StringComparison.OrdinalIgnoreCase)
            ? "global"
            : string.Equals(scope, "event", StringComparison.OrdinalIgnoreCase)
                ? "event"
                : "mode";
        var fileList = AllowedFilesByScope[scopeKey];

        foreach (var fileName in fileList)
        {
            var relativePath = GetRelativeFilePath(scope, modeId, fileName);
            var fullPath = Path.Combine(ConfigLoader.ConfigRoot, relativePath);

            if (!File.Exists(fullPath))
            {
                if (string.Equals(fileName, "event.json", StringComparison.OrdinalIgnoreCase) &&
                    !string.IsNullOrWhiteSpace(modeId))
                {
                    var stub = CreateEventStub(modeId);
                    result[fileName] = JsonDocument.Parse(JsonSerializer.Serialize(stub, ConfigLoader.JsonOptions));
                }
                continue;
            }

            try
            {
                var jsonText = File.ReadAllText(fullPath);
                // Parse as JsonDocument so we can redact secrets safely
                using var doc = JsonDocument.Parse(jsonText);
                var redacted = RedactSecrets(doc.RootElement);
                var cloned = redacted.Clone();
                result[fileName] = JsonDocument.Parse(cloned.GetRawText());
            }
            catch (Exception ex)
            {
                errors.Add($"Failed reading '{fileName}': {ex.Message}");
            }
        }

        return result;
    }

    string GetRelativeFilePath(string scope, string? modeId, string fileName)
    {
        if (string.Equals(scope, "global", StringComparison.OrdinalIgnoreCase))
            return fileName;

        if (string.IsNullOrWhiteSpace(modeId))
            throw new ArgumentException("modeId is required for mode scope");

        if (string.Equals(fileName, "event.json", StringComparison.OrdinalIgnoreCase))
            return Path.Combine("events", $"{modeId}.json");

        return Path.Combine(modeId, fileName);
    }

    static UnifiedEventDefinition CreateEventStub(string modeId)
    {
        return new UnifiedEventDefinition
        {
            Metadata = new EventMetadata
            {
                Id = modeId,
                DisplayName = modeId,
                Enabled = true,
                Version = "1"
            }
        };
    }

    JsonElement RedactSecrets(JsonElement element)
    {
        // Work on a DOM clone because JsonDocument is immutable
        var json = element.GetRawText();
        using var doc = JsonDocument.Parse(json);
        var node = System.Text.Json.Nodes.JsonNode.Parse(doc.RootElement.GetRawText());
        if (node == null) return element;

        RedactSecretsRecursive(node);
        using var tmp = JsonDocument.Parse(node.ToJsonString());
        return tmp.RootElement.Clone();
    }

    static void RedactSecretsRecursive(System.Text.Json.Nodes.JsonNode? node)
    {
        if (node == null) return;

        if (node is System.Text.Json.Nodes.JsonObject obj)
        {
            // redact known keys
            foreach (var prop in obj.ToList())
            {
                var key = prop.Key;
                var val = prop.Value;

                if (SecretKeys.Contains(key))
                {
                    if (val is System.Text.Json.Nodes.JsonValue jsonVal)
                    {
                        // Only redact if looks like token/URL-ish
                        var s = jsonVal.ToString() ?? "";
                        if (s.Contains("http", StringComparison.OrdinalIgnoreCase) ||
                            s.Contains("bearer", StringComparison.OrdinalIgnoreCase) ||
                            s.Contains("webhook", StringComparison.OrdinalIgnoreCase) ||
                            s.Contains("token", StringComparison.OrdinalIgnoreCase) ||
                            s.Length > 12)
                        {
                            obj[key] = RedactedValue;
                        }
                    }
                }

                RedactSecretsRecursive(val);
            }
        }
        else if (node is System.Text.Json.Nodes.JsonArray arr)
        {
            foreach (var item in arr)
                RedactSecretsRecursive(item);
        }
    }

    JsonElement PreserveLocalSecrets(string scope, string? modeId, string fileName, JsonElement updatedElement)
    {
        var relativePath = GetRelativeFilePath(scope, modeId, fileName);
        var absolutePath = Path.Combine(ConfigLoader.ConfigRoot, relativePath);
        if (!File.Exists(absolutePath))
            return updatedElement.Clone();

        try
        {
            using var originalDoc = JsonDocument.Parse(File.ReadAllText(absolutePath));
            var node = System.Text.Json.Nodes.JsonNode.Parse(updatedElement.GetRawText());
            if (node == null)
                return updatedElement.Clone();

            PreserveLocalSecretsRecursive(node, originalDoc.RootElement);
            using var mergedDoc = JsonDocument.Parse(node.ToJsonString());
            return mergedDoc.RootElement.Clone();
        }
        catch (Exception ex)
        {
            BattleLuckPlugin.LogWarning($"[ConfigEdit] Could not preserve local secrets for {fileName}: {ex.Message}");
            return updatedElement.Clone();
        }
    }

    static void PreserveLocalSecretsRecursive(System.Text.Json.Nodes.JsonNode? node, JsonElement original)
    {
        if (node == null)
            return;

        if (node is System.Text.Json.Nodes.JsonObject obj && original.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in obj.ToList())
            {
                if (!original.TryGetProperty(prop.Key, out var originalValue))
                    continue;

                if (IsRedactedNode(prop.Value))
                {
                    obj[prop.Key] = System.Text.Json.Nodes.JsonNode.Parse(originalValue.GetRawText());
                    continue;
                }

                PreserveLocalSecretsRecursive(prop.Value, originalValue);
            }
        }
        else if (node is System.Text.Json.Nodes.JsonArray arr && original.ValueKind == JsonValueKind.Array)
        {
            var originals = original.EnumerateArray().ToList();
            for (var i = 0; i < arr.Count && i < originals.Count; i++)
            {
                if (IsRedactedNode(arr[i]))
                {
                    arr[i] = System.Text.Json.Nodes.JsonNode.Parse(originals[i].GetRawText());
                    continue;
                }

                PreserveLocalSecretsRecursive(arr[i], originals[i]);
            }
        }
    }

    static bool IsRedactedNode(System.Text.Json.Nodes.JsonNode? node)
        => node is System.Text.Json.Nodes.JsonValue value &&
           value.TryGetValue<string>(out var text) &&
           string.Equals(text, RedactedValue, StringComparison.Ordinal);

    bool ValidateJsonForFile(string scope, string fileName, JsonElement updatedElement)
    {
        try
        {
            // Validation uses the same models ConfigLoader would deserialize into.
            if (string.Equals(scope, "global", StringComparison.OrdinalIgnoreCase))
            {
                return fileName switch
                {
                    "ai_config.json" => updatedElement.Deserialize<AIConfig>() != null,
                    "discord_bridge.json" => updatedElement.Deserialize<DiscordBridgeConfig>() != null,
                    "webhook.json" => updatedElement.Deserialize<WebhookConfig>() != null,
                    "ai_logger.json" => updatedElement.Deserialize<AiLoggerConfig>() != null,
                    "mcp_config.json" => updatedElement.Deserialize<MCPRuntimeSettings>() != null,
                     "kit_grant_rules.json" => updatedElement.Deserialize<KitGrantRulesConfig>() != null,
                    "special_item.json" => updatedElement.Deserialize<SpecialItemConfig>() != null,
                    "merchant_servant_actions.json" => updatedElement.Deserialize<MerchantServantActionConfig>() != null,
                    "custom_sequences.json" => updatedElement.Deserialize<CustomSequencesConfig>() != null,
                    _ => true
                };
            }

            // mode scope
            if (string.Equals(fileName, "event.json", StringComparison.OrdinalIgnoreCase))
                return ValidateUnifiedEvent(updatedElement);

return fileName switch
             {
                 "session.json" => updatedElement.Deserialize<SessionConfig>() != null,
                 "zones.json" => updatedElement.Deserialize<ZonesConfig>() != null,
                 "kit.json" => updatedElement.Deserialize<KitConfig>() != null,
                 _ => true
             };
        }
        catch
        {
            return false;
        }
    }

    bool ValidateUnifiedEvent(JsonElement updatedElement)
    {
        var definition = updatedElement.Deserialize<UnifiedEventDefinition>(ConfigLoader.JsonOptions);
        if (definition == null)
            return false;

        var aiConfig = ConfigLoader.LoadAIConfig();
        var maxActions = Math.Clamp(aiConfig.EventAuthoring.MaxActionsPerEvent, 1, 1000);
        var result = new EventValidationResult();
        var eventLoader = new EventDefinitionLoader();
        eventLoader.Validate(definition, result, maxActions);
        if (result.Success)
            return true;

        BattleLuckPlugin.LogWarning("[ConfigEditService] Invalid unified event edit: " + string.Join("; ", result.Errors));
        return false;
    }
}

