using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using BattleLuck.Models;

namespace BattleLuck.Services.Runtime;

/// <summary>One-release migration from split kits.json into events/&lt;id&gt;.json.</summary>
public static class UnifiedEventMigrationService
{
    public static int MigrateSplitDefinitions(string configRoot, Action<string>? info = null,
        Action<string>? warning = null)
    {
        var eventsRoot = Path.Combine(configRoot, "events");
        if (!Directory.Exists(eventsRoot)) return 0;
        var migrated = 0;
        foreach (var eventPath in Directory.EnumerateFiles(eventsRoot, "*.json", SearchOption.TopDirectoryOnly))
        {
            var eventId = Path.GetFileNameWithoutExtension(eventPath);
            var kitPath = Path.Combine(eventsRoot, eventId, "kits.json");
            if (!File.Exists(kitPath)) continue;
            try
            {
                var root = JsonNode.Parse(File.ReadAllText(eventPath), documentOptions: new JsonDocumentOptions
                { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip })?.AsObject();
                if (root == null || root.ContainsKey("kit")) continue;
                var kitText = File.ReadAllText(kitPath);
                var kit = JsonSerializer.Deserialize<KitConfig>(kitText, ConfigLoader.JsonOptions);
                if (kit == null) throw new JsonException("kits.json was empty.");

                root["kit"] = JsonNode.Parse(kitText, documentOptions: new JsonDocumentOptions
                { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
                var candidate = root.ToJsonString(new JsonSerializerOptions(ConfigLoader.JsonOptions) { WriteIndented = true });
                _ = JsonSerializer.Deserialize<UnifiedEventDefinition>(candidate, ConfigLoader.JsonOptions)
                    ?? throw new JsonException("Migrated event was empty.");

                var backup = eventPath + ".split-v1.bak";
                if (!File.Exists(backup)) File.Copy(eventPath, backup, overwrite: false);
                var temporary = eventPath + ".tmp-" + Guid.NewGuid().ToString("N");
                File.WriteAllText(temporary, candidate, new UTF8Encoding(false));
                File.Move(temporary, eventPath, overwrite: true);
                migrated++;
                info?.Invoke($"[ConfigMigration] Embedded kit into events/{eventId}.json; legacy split files are compatibility-only.");
            }
            catch (Exception ex)
            {
                warning?.Invoke($"[ConfigMigration] Kept '{eventId}' unchanged because split migration failed: {ex.Message}");
            }
        }
        return migrated;
    }
}
