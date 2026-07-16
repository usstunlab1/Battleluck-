/*
// 
// using Il2CppInterop.Runtime;
using ProjectM;
using Stunlock.Core;
using Unity.Collections;
using Unity.Entities;
using BattleLuck.Commands;

public static class DataExportCommands
{
    static readonly string ExportDir = Path.Combine(
        Path.GetDirectoryName(typeof(BattleLuckPlugin).Assembly.Location)!, "exports");

    [BattleLuckCommand("exportmods", description: "Export all loaded mod data (plugins, prefabs, APIs) to JSON", adminOnly: true)]
    public static void ExportAllModData(BattleLuckCommandContext ctx)
    {
        try
        {
            Directory.CreateDirectory(ExportDir);

            var plugins = ExportPlugins();
            var prefabs = ExportPrefabConstants();
            var livePrefabs = ExportLivePrefabs();

            var report = new Dictionary<string, object>
            {
                ["exported_at"] = DateTime.UtcNow.ToString("o"),
                ["plugins"] = plugins,
                ["prefab_constants"] = prefabs,
                ["live_prefab_count"] = livePrefabs.Count,
                ["live_prefabs"] = livePrefabs
            };

            var path = Path.Combine(ExportDir, $"mod_data_{DateTime.UtcNow:yyyyMMdd_HHmmss}.json");
            var json = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(path, json);

            ctx.Reply($"Exported {plugins.Count} plugins, {prefabs.Count} prefab constants, {livePrefabs.Count} live prefabs → exports/");
        }
        catch (Exception ex)
        {
            ctx.Reply($"Export failed: {ex.Message}");
            BattleLuckPlugin.LogError($"[DataExport] {ex}");
        }
    }

    [BattleLuckCommand("exportplugins", description: "Export loaded BepInEx plugin info to JSON", adminOnly: true)]
    public static void ExportPluginInfo(BattleLuckCommandContext ctx)
    {
        try
        {
            Directory.CreateDirectory(ExportDir);
            var plugins = ExportPlugins();
            var path = Path.Combine(ExportDir, $"plugins_{DateTime.UtcNow:yyyyMMdd_HHmmss}.json");
            File.WriteAllText(path, JsonSerializer.Serialize(plugins, new JsonSerializerOptions { WriteIndented = true }));
            ctx.Reply($"Exported {plugins.Count} plugins → exports/");
        }
        catch (Exception ex)
        {
            ctx.Reply($"Export failed: {ex.Message}");
        }
    }

    [BattleLuckCommand("exportprefabs", description: "Export server live prefab collection to JSON", adminOnly: true)]
    public static void ExportPrefabCollection(BattleLuckCommandContext ctx)
    {
        try
        {
            Directory.CreateDirectory(ExportDir);
            var prefabs = ExportLivePrefabs();
            var path = Path.Combine(ExportDir, $"prefabs_{DateTime.UtcNow:yyyyMMdd_HHmmss}.json");
            File.WriteAllText(path, JsonSerializer.Serialize(prefabs, new JsonSerializerOptions { WriteIndented = true }));
            ctx.Reply($"Exported {prefabs.Count} live prefabs → exports/");
        }
        catch (Exception ex)
        {
            ctx.Reply($"Export failed: {ex.Message}");
        }
    }

    [BattleLuckCommand("validateprefabs", description: "Check if BattleLuck prefab GUIDs exist in the game's entity map", adminOnly: true)]
    public static void ValidatePrefabs(BattleLuckCommandContext ctx)
    {
        try
        {
            var pcs = VRisingCore.PrefabCollectionSystem;
            var map = pcs._PrefabGuidToEntityMap;
            int valid = 0, invalid = 0;
            var missing = new List<string>();

            foreach (var field in typeof(Prefabs).GetFields(BindingFlags.Public | BindingFlags.Static))
            {
                if (field.FieldType != typeof(PrefabGUID)) continue;
                var guid = (PrefabGUID)field.GetValue(null)!;
                if (map.ContainsKey(guid))
                    valid++;
                else
                {
                    invalid++;
                    missing.Add($"{field.Name} ({guid.GuidHash})");
                }
            }

            ctx.Reply($"Prefab validation: {valid} valid, {invalid} missing.");
            foreach (var m in missing.Take(10))
                ctx.Reply($"  MISS: {m}");
            if (missing.Count > 10)
                ctx.Reply($"  ...and {missing.Count - 10} more.");

            // Also log to file
            Directory.CreateDirectory(ExportDir);
            var path = Path.Combine(ExportDir, $"prefab_validation_{DateTime.UtcNow:yyyyMMdd_HHmmss}.txt");
            File.WriteAllLines(path, missing.Prepend($"Valid: {valid}, Missing: {invalid}"));
            ctx.Reply($"Full report → exports/");
        }
        catch (Exception ex)
        {
            ctx.Reply($"Validation failed: {ex.Message}");
        }
    }

    [BattleLuckCommand("findtiles", description: "Search live prefabs for tile/wall/floor building pieces", adminOnly: true)]
    public static void FindTilePrefabs(BattleLuckCommandContext ctx, string filter = "TM_Castle")
    {
        SearchAndReplyPrefabs(ctx, filter);
    }

    [BattleLuckCommand("searchprefab", description: "Search ALL live prefabs by name pattern (e.g. 'Item_Weapon_Sword', 'AB_Chaos', 'Item_Armor')", adminOnly: true)]
    public static void SearchPrefab(BattleLuckCommandContext ctx, string filter)
    {
        SearchAndReplyPrefabs(ctx, filter);
    }

    static void SearchAndReplyPrefabs(BattleLuckCommandContext ctx, string filter)
    {
        try
        {
            var pcs = VRisingCore.PrefabCollectionSystem;
            var map = pcs._PrefabGuidToEntityMap;
            var matches = new List<(int guid, string name)>();

            // Search live prefabs (with proper names from _PrefabLookupMap)
            var allLive = PrefabHelper.GetAllLive();
            foreach (var kvp in allLive)
            {
                if (kvp.Key.Contains(filter, StringComparison.OrdinalIgnoreCase))
                    matches.Add((kvp.Value.GuidHash, kvp.Key));
            }

            // Also check Prefabs.cs constants and mark if valid
            foreach (var field in typeof(Prefabs).GetFields(BindingFlags.Public | BindingFlags.Static))
            {
                if (field.FieldType != typeof(PrefabGUID)) continue;
                if (!field.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)) continue;
                var guid = (PrefabGUID)field.GetValue(null)!;
                bool inMap = map.ContainsKey(guid);
                var label = $"[Prefabs.cs] {field.Name} {(inMap ? "✓" : "✗")}";
                if (!matches.Any(m => m.guid == guid.GuidHash))
                    matches.Add((guid.GuidHash, label));
            }

            ctx.Reply($"Found {matches.Count} matches for '{filter}':");
            foreach (var m in matches.Take(20))
                ctx.Reply($"  {m.guid}: {m.name}");
            if (matches.Count > 20)
                ctx.Reply($"  ...and {matches.Count - 20} more.");

            // Export full list
            Directory.CreateDirectory(ExportDir);
            var path = Path.Combine(ExportDir, $"search_{filter}_{DateTime.UtcNow:yyyyMMdd_HHmmss}.txt");
            File.WriteAllLines(path, matches.Select(m => $"{m.guid}\t{m.name}"));
            ctx.Reply($"Full list → exports/");
        }
        catch (Exception ex)
        {
            ctx.Reply($"Search failed: {ex.Message}");
        }
    }

    [BattleLuckCommand("discoverkits", description: "Auto-discover the best weapon/armor/tile/ability prefabs from live game data and export kit.json template", adminOnly: true)]
    public static void DiscoverKits(BattleLuckCommandContext ctx)
    {
        try
        {
            PrefabHelper.ScanLivePrefabs();
            var allLive = PrefabHelper.GetAllLive();
            ctx.Reply($"Scanning {allLive.Count} live prefabs...");

            // Category searches — ordered from best tier to lowest
            var categories = new Dictionary<string, string[]>
            {
                ["Swords"] = new[] { "Item_Weapon_Sword_Legendary", "Item_Weapon_Sword_Epic", "Item_Weapon_Sword_Sanguine", "Item_Weapon_Sword_DarkSilver", "Item_Weapon_Sword_Iron" },
                ["Axes"] = new[] { "Item_Weapon_Axes_Legendary", "Item_Weapon_Axes_Epic", "Item_Weapon_Axes_Sanguine", "Item_Weapon_Axes_DarkSilver", "Item_Weapon_Axes_Iron" },
                ["Maces"] = new[] { "Item_Weapon_Mace_Legendary", "Item_Weapon_Mace_Epic", "Item_Weapon_Mace_Sanguine", "Item_Weapon_Mace_DarkSilver", "Item_Weapon_Mace_Iron" },
                ["Spears"] = new[] { "Item_Weapon_Spear_Legendary", "Item_Weapon_Spear_Epic", "Item_Weapon_Spear_Sanguine", "Item_Weapon_Spear_DarkSilver", "Item_Weapon_Spear_Iron" },
                ["Crossbows"] = new[] { "Item_Weapon_Crossbow_Legendary", "Item_Weapon_Crossbow_Epic", "Item_Weapon_Crossbow_Sanguine", "Item_Weapon_Crossbow_DarkSilver", "Item_Weapon_Crossbow_Iron" },
                ["Slashers"] = new[] { "Item_Weapon_Slashers_Legendary", "Item_Weapon_Slashers_Epic", "Item_Weapon_Slashers_Sanguine", "Item_Weapon_Slashers_DarkSilver", "Item_Weapon_Slashers_Iron" },
                ["Reapers"] = new[] { "Item_Weapon_Reaper_Legendary", "Item_Weapon_Reaper_Epic", "Item_Weapon_Reaper_Sanguine", "Item_Weapon_Reaper_DarkSilver", "Item_Weapon_Reaper_Iron" },
                ["Pistols"] = new[] { "Item_Weapon_Pistols_Legendary", "Item_Weapon_Pistols_Epic", "Item_Weapon_Pistols_Sanguine", "Item_Weapon_Pistols_DarkSilver", "Item_Weapon_Pistols_Iron" },
                ["GreatSwords"] = new[] { "Item_Weapon_GreatSword_Legendary", "Item_Weapon_GreatSword_Epic", "Item_Weapon_GreatSword_Sanguine", "Item_Weapon_GreatSword_DarkSilver", "Item_Weapon_GreatSword_Iron" },
                ["Whips"] = new[] { "Item_Weapon_Whip_Legendary", "Item_Weapon_Whip_Epic", "Item_Weapon_Whip_Sanguine", "Item_Weapon_Whip_DarkSilver", "Item_Weapon_Whip_Iron" },
            };

            var found = new Dictionary<string, (string name, int guid)>();
            var allWeapons = new List<(string name, int guid)>();
            var allArmors = new List<(string name, int guid)>();
            var allTiles = new List<(string name, int guid)>();
            var allAbilities = new List<(string name, int guid)>();

            // Find best-tier weapon for each category
            foreach (var cat in categories)
            {
                foreach (var search in cat.Value)
                {
                    var match = allLive.FirstOrDefault(kvp => kvp.Key.Contains(search, StringComparison.OrdinalIgnoreCase));
                    if (match.Key != null)
                    {
                        found[cat.Key] = (match.Key, match.Value.GuidHash);
                        break;
                    }
                }
            }

            // Broad weapon search — all items containing "Item_Weapon_"
            foreach (var kvp in allLive.Where(kvp => kvp.Key.StartsWith("Item_Weapon_", StringComparison.OrdinalIgnoreCase)))
                allWeapons.Add((kvp.Key, kvp.Value.GuidHash));

            // Armor search
            foreach (var kvp in allLive.Where(kvp => kvp.Key.StartsWith("Item_Armor_", StringComparison.OrdinalIgnoreCase) ||
                                                      kvp.Key.StartsWith("Item_Cloak_", StringComparison.OrdinalIgnoreCase) ||
                                                      kvp.Key.StartsWith("Item_Headgear_", StringComparison.OrdinalIgnoreCase) ||
                                                      kvp.Key.StartsWith("Item_MagicSource_", StringComparison.OrdinalIgnoreCase) ||
                                                      kvp.Key.StartsWith("Item_Bag_", StringComparison.OrdinalIgnoreCase)))
                allArmors.Add((kvp.Key, kvp.Value.GuidHash));

            // Tile search
            foreach (var kvp in allLive.Where(kvp => kvp.Key.StartsWith("TM_Castle_", StringComparison.OrdinalIgnoreCase)))
                allTiles.Add((kvp.Key, kvp.Value.GuidHash));

            // Ability search
            foreach (var kvp in allLive.Where(kvp => kvp.Key.StartsWith("AB_", StringComparison.OrdinalIgnoreCase)))
                allAbilities.Add((kvp.Key, kvp.Value.GuidHash));

            // Report found weapons
            ctx.Reply($"Best weapons found ({found.Count}/{categories.Count}):");
            foreach (var kv in found)
                ctx.Reply($"  {kv.Key}: {kv.Value.name} ({kv.Value.guid})");

            ctx.Reply($"Total: {allWeapons.Count} weapons, {allArmors.Count} armor, {allTiles.Count} tiles, {allAbilities.Count} abilities");

            // Export everything
            Directory.CreateDirectory(ExportDir);
            var lines = new List<string>();
            lines.Add("=== BEST WEAPONS ===");
            foreach (var kv in found) lines.Add($"{kv.Value.guid}\t{kv.Value.name}\t# {kv.Key}");
            lines.Add(""); lines.Add("=== ALL WEAPONS ===");
            foreach (var w in allWeapons.OrderBy(w => w.name)) lines.Add($"{w.guid}\t{w.name}");
            lines.Add(""); lines.Add("=== ALL ARMOR ===");
            foreach (var a in allArmors.OrderBy(a => a.name)) lines.Add($"{a.guid}\t{a.name}");
            lines.Add(""); lines.Add("=== ALL TILES ===");
            foreach (var t in allTiles.OrderBy(t => t.name)) lines.Add($"{t.guid}\t{t.name}");
            lines.Add(""); lines.Add("=== ALL ABILITIES ===");
            foreach (var ab in allAbilities.OrderBy(ab => ab.name)) lines.Add($"{ab.guid}\t{ab.name}");

            var exportPath = Path.Combine(ExportDir, $"discovered_kits_{DateTime.UtcNow:yyyyMMdd_HHmmss}.txt");
            File.WriteAllLines(exportPath, lines);
            ctx.Reply($"Full discovery export → exports/");
        }
        catch (Exception ex)
        {
            ctx.Reply($"Discovery failed: {ex.Message}");
            BattleLuckPlugin.LogError($"[DiscoverKits] {ex}");
        }
    }

    static List<Dictionary<string, object>> ExportPlugins()
    {
        var results = new List<Dictionary<string, object>>();

        // Scan all loaded assemblies for BepInPlugin attributes
        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            try
            {
                foreach (var type in asm.GetTypes())
                {
                    var attr = type.GetCustomAttribute<BepInPlugin>();
                    if (attr == null) continue;

                    var entry = new Dictionary<string, object>
                    {
                        ["guid"] = attr.GUID ?? "",
                        ["name"] = attr.Name ?? "",
                        ["version"] = attr.Version?.ToString() ?? "",
                        ["assembly"] = asm.GetName().Name ?? "",
                        ["assembly_version"] = asm.GetName().Version?.ToString() ?? "",
                        ["location"] = TryGetLocation(asm),
                        ["types_count"] = SafeTypeCount(asm)
                    };

                    // Extract static prefab fields
                    var prefabFields = ExtractPrefabFields(asm);
                    if (prefabFields.Count > 0)
                        entry["prefab_fields"] = prefabFields;

                    // Extract Harmony patch info
                    var patches = ExtractHarmonyPatches(asm);
                    if (patches.Count > 0)
                        entry["harmony_patches"] = patches;

                    // Extract command methods
                    var commands = ExtractCommands(asm);
                    if (commands.Count > 0)
                        entry["commands"] = commands;

                    // Extract config files
                    var configs = FindConfigFiles(attr.GUID);
                    if (configs.Count > 0)
                        entry["config_files"] = configs;

                    results.Add(entry);
                }
            }
            catch
            {
                // Skip assemblies that fail reflection
            }
        }

        return results;
    }

    static List<Dictionary<string, object>> ExportPrefabConstants()
    {
        var results = new List<Dictionary<string, object>>();

        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            try
            {
                foreach (var type in asm.GetTypes())
                {
                    var fields = type.GetFields(BindingFlags.Public | BindingFlags.Static)
                        .Where(f => f.FieldType == typeof(PrefabGUID))
                        .ToList();

                    if (fields.Count == 0) continue;

                    foreach (var field in fields)
                    {
                        try
                        {
                            var guid = (PrefabGUID)field.GetValue(null)!;
                            results.Add(new Dictionary<string, object>
                            {
                                ["source_type"] = type.FullName ?? type.Name,
                                ["source_assembly"] = asm.GetName().Name ?? "",
                                ["field_name"] = field.Name,
                                ["prefab_guid"] = guid.GuidHash
                            });
                        }
                        catch { }
                    }
                }
            }
            catch { }
        }

        return results;
    }

    static List<Dictionary<string, object>> ExportLivePrefabs()
    {
        var results = new List<Dictionary<string, object>>();

        try
        {
            var pcs = VRisingCore.PrefabCollectionSystem;
            var map = pcs._PrefabGuidToEntityMap;
            var enumerator = map.GetEnumerator();

            while (enumerator.MoveNext())
            {
                var kvp = enumerator.Current;
                var guid = kvp.Key;
                var name = PrefabHelper.GetLivePrefabName(guid) ?? string.Empty;

                results.Add(new Dictionary<string, object>
                {
                    ["guid_hash"] = guid.GuidHash,
                    ["name"] = name
                });
            }
        }
        catch (Exception ex)
        {
            BattleLuckPlugin.LogWarning($"[DataExport] Live prefab export partial: {ex.Message}");
        }

        return results;
    }

    static List<Dictionary<string, string>> ExtractPrefabFields(Assembly asm)
    {
        var results = new List<Dictionary<string, string>>();

        try
        {
            foreach (var type in asm.GetTypes())
            {
                var fields = type.GetFields(BindingFlags.Public | BindingFlags.Static)
                    .Where(f => f.FieldType == typeof(PrefabGUID));

                foreach (var field in fields)
                {
                    try
                    {
                        var guid = (PrefabGUID)field.GetValue(null)!;
                        results.Add(new Dictionary<string, string>
                        {
                            ["type"] = type.Name,
                            ["field"] = field.Name,
                            ["guid"] = guid.GuidHash.ToString()
                        });
                    }
                    catch { }
                }
            }
        }
        catch { }

        return results;
    }

    static List<Dictionary<string, string>> ExtractHarmonyPatches(Assembly asm)
    {
        var results = new List<Dictionary<string, string>>();

        try
        {
            foreach (var type in asm.GetTypes())
            {
                var harmonyPatch = type.GetCustomAttribute<HarmonyPatch>();
                if (harmonyPatch == null) continue;

                results.Add(new Dictionary<string, string>
                {
                    ["patch_class"] = type.Name,
                    ["target_type"] = harmonyPatch.info?.declaringType?.Name ?? "unknown",
                    ["target_method"] = harmonyPatch.info?.methodName ?? "unknown"
                });
            }
        }
        catch { }

        return results;
    }

    static List<Dictionary<string, string>> ExtractCommands(Assembly asm)
    {
        var results = new List<Dictionary<string, string>>();

        try
        {
            foreach (var type in asm.GetTypes())
            {
                foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance))
                {
                    var cmdAttr = method.GetCustomAttributes()
                        .FirstOrDefault(a => a.GetType().Name == "CommandAttribute");

                    if (cmdAttr == null) continue;

                    var nameProp = cmdAttr.GetType().GetProperty("Name")?.GetValue(cmdAttr)?.ToString() ?? "";
                    var descProp = cmdAttr.GetType().GetProperty("Description")?.GetValue(cmdAttr)?.ToString() ?? "";
                    var adminProp = cmdAttr.GetType().GetProperty("AdminOnly")?.GetValue(cmdAttr)?.ToString() ?? "";

                    results.Add(new Dictionary<string, string>
                    {
                        ["command"] = nameProp,
                        ["method"] = method.Name,
                        ["description"] = descProp,
                        ["admin_only"] = adminProp,
                        ["declaring_type"] = type.Name
                    });
                }
            }
        }
        catch { }

        return results;
    }

    static List<string> FindConfigFiles(string? pluginGuid)
    {
        var results = new List<string>();
        if (string.IsNullOrEmpty(pluginGuid)) return results;

        try
        {
            // Check BepInEx/config for .cfg files matching the GUID
            var bepInExRoot = Path.GetDirectoryName(
                Path.GetDirectoryName(typeof(BattleLuckPlugin).Assembly.Location));
            if (bepInExRoot == null) return results;

            var configDir = Path.Combine(bepInExRoot, "..", "config");
            if (Directory.Exists(configDir))
            {
                foreach (var file in Directory.GetFiles(configDir, $"*{pluginGuid}*", SearchOption.AllDirectories))
                    results.Add(Path.GetFileName(file));

                // Also check for folders named after the plugin
                foreach (var dir in Directory.GetDirectories(configDir, $"*{pluginGuid}*", SearchOption.TopDirectoryOnly))
                {
                    foreach (var file in Directory.GetFiles(dir, "*", SearchOption.AllDirectories))
                        results.Add(Path.GetRelativePath(configDir, file));
                }
            }
        }
        catch { }

        return results;
    }

    static string TryGetLocation(Assembly asm)
    {
        try { return asm.Location ?? ""; }
        catch { return "dynamic"; }
    }

    static int SafeTypeCount(Assembly asm)
    {
        try { return asm.GetTypes().Length; }
        catch { return -1; }
    }
}
*/
