/// <summary>
/// Configuration model for kit_grant_rules.json.
/// Maps crafted item prefabs to kit IDs that should be granted on craft completion.
/// </summary>
public static class KitRules
{
    static readonly object _lock = new();
    static KitGrantRulesConfig? _config;

    /// <summary>
    /// Load the kit grant rules configuration.
    /// </summary>
    public static KitGrantRulesConfig Load()
    {
        lock (_lock)
        {
            if (_config != null)
                return _config;

            var path = System.IO.Path.Combine(ConfigLoader.ConfigRoot, "kit_grant_rules.json");
            if (!System.IO.File.Exists(path))
            {
                BattleLuckPlugin.LogInfo($"[KitRules] No kit_grant_rules.json found at {path}, returning empty config.");
                _config = new KitGrantRulesConfig();
                return _config;
            }

            try
            {
                var json = System.IO.File.ReadAllText(path);
                _config = System.Text.Json.JsonSerializer.Deserialize<KitGrantRulesConfig>(json, ConfigLoader.JsonOptions)
                    ?? new KitGrantRulesConfig();
            }
            catch (System.Exception ex)
            {
                BattleLuckPlugin.LogWarning($"[KitRules] Failed to load kit_grant_rules.json: {ex.Message}");
                _config = new KitGrantRulesConfig();
            }

            return _config;
        }
    }

    /// <summary>
    /// Check if a crafted item grants a kit, and return the kit ID if so.
    /// </summary>
    public static bool TryGetKitForItem(PrefabGUID craftedItem, out string kitId)
    {
        kitId = "";
        var config = Load();

        if (config.Rules == null)
            return false;

        foreach (var rule in config.Rules)
        {
            if (rule.ItemPrefabGuid == craftedItem.GuidHash)
            {
                kitId = rule.KitId;
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Clear the cached configuration (used on reload).
    /// </summary>
    public static void ClearCache()
    {
        lock (_lock)
        {
            _config = null;
        }
    }
}

public sealed class KitGrantRulesConfig
{
    [JsonPropertyName("rules")]
    public List<KitGrantRule> Rules { get; set; } = new();
}

public sealed class KitGrantRule
{
    [JsonPropertyName("itemPrefabGuid")]
    public int ItemPrefabGuid { get; set; }

    [JsonPropertyName("kitId")]
    public string KitId { get; set; } = "";
}