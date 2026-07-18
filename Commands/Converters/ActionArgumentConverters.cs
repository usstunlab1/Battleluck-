namespace BattleLuck.Commands.Converters;

public static class ActionParameterConverter
{
    static readonly Dictionary<string, string> ActionAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["setblood"] = "blood.change",
        ["set_blood_type"] = "blood.change",
        ["spawnnpc"] = "npc.spawn",
        ["spawn.npc"] = "npc.spawn",
        ["spawnvblood"] = "spawn.boss",
        ["spawn.vblood"] = "spawn.boss",
        ["message"] = "notify",
        ["announce"] = "notify",
        ["tp"] = "teleport",
        ["goto"] = "teleport.position",
        ["clear.inventory"] = "inventory.clear_all",
        ["count.inventory"] = "inventory.count"
    };

    static readonly Dictionary<string, string> BossAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["dracula"] = "CHAR_Vampire_Dracula_VBlood",
        ["cassius"] = "CHAR_Vampire_HighLord_VBlood",
        ["highlord"] = "CHAR_Vampire_HighLord_VBlood",
        ["manticore"] = "CHAR_Manticore_VBlood",
        ["winged horror"] = "CHAR_Manticore_VBlood",
        ["talzur"] = "CHAR_Manticore_VBlood",
        ["morgana"] = "CHAR_Blackfang_Morgana_VBlood",
        ["megara"] = "CHAR_Blackfang_Morgana_VBlood",
        ["leandra"] = "CHAR_Undead_BishopOfShadows_VBlood",
        ["frostmaw"] = "CHAR_Winter_Yeti_VBlood",
        ["yeti"] = "CHAR_Winter_Yeti_VBlood",
        ["alpha wolf"] = "CHAR_Forest_Wolf_VBlood",
        ["wolf"] = "CHAR_Forest_Wolf_VBlood"
    };

    public static string NormalizeActionString(string actionString)
    {
        var (actionName, parameters) = FlowActionExecutor.ParseActionString(actionString);
        var normalized = Normalize(actionName, parameters);
        if (string.IsNullOrWhiteSpace(normalized.ActionName))
            return "";

        if (normalized.Parameters.Count == 0)
            return normalized.ActionName;

        return $"{normalized.ActionName}:{string.Join("|", normalized.Parameters.Select(kv => $"{kv.Key}={kv.Value}"))}";
    }

    public static (string ActionName, Dictionary<string, string> Parameters) Normalize(
        string actionName,
        Dictionary<string, string> parameters)
    {
        var name = NormalizeActionName(actionName);
        var p = new Dictionary<string, string>(parameters, StringComparer.OrdinalIgnoreCase);

        NormalizeCommonAliases(name, p);
        NormalizePrefabParameters(name, p);
        NormalizeItemParameters(name, p);
        NormalizeBuffParameters(name, p);
        NormalizeTimerParameters(name, p);
        NormalizeDamageParameters(name, p);
        NormalizeBossParameters(name, p);

        return (name, p);
    }

    static string NormalizeActionName(string actionName)
    {
        var trimmed = (actionName ?? "").Trim();
        return ActionAliases.TryGetValue(trimmed, out var canonical) ? canonical : trimmed;
    }

    static void NormalizeCommonAliases(string actionName, Dictionary<string, string> p)
    {
        CopyIfMissing(p, "zone", "zoneHash");
        CopyIfMissing(p, "zoneId", "zoneHash");
        CopyIfMissing(p, "mode", "modeId");
        CopyIfMissing(p, "event", "modeId");
        CopyIfMissing(p, "qty", "amount");
        CopyIfMissing(p, "quantity", "amount");
        CopyIfMissing(p, "durationSeconds", "duration");

        if (actionName == "player.stun" || actionName == "player.freeze")
            CopyIfMissing(p, "duration", "durationSeconds");

        if (actionName == "blood.change" || actionName == "blood.set" || actionName == "set_blood")
        {
            CopyIfMissing(p, "blood", "bloodType");
            CopyIfMissing(p, "type", "bloodType");
        }

        if (actionName.StartsWith("sequence.custom.", StringComparison.OrdinalIgnoreCase))
        {
            CopyIfMissing(p, "id", "sequenceId");
            CopyIfMissing(p, "name", "sequenceId");
            CopyIfMissing(p, "sequence", "sequenceId");
            CopyIfMissing(p, "seq", "sequenceId");
        }
    }

    static void NormalizePrefabParameters(string actionName, Dictionary<string, string> p)
    {
        if ((actionName == "structure.spawn" || actionName == "tile.place" || actionName == "prefab.spawn") &&
            !p.ContainsKey("prefab"))
        {
            CopyIfMissing(p, "wallType", "prefab");
            CopyIfMissing(p, "floorType", "prefab");
            CopyIfMissing(p, "tileType", "prefab");
        }

        if (actionName == "wall.build")
            CopyIfMissing(p, "wallPrefab", "wallType");

        if (actionName == "floor.place")
            CopyIfMissing(p, "floorPrefab", "floorType");

        foreach (var key in new[] { "prefab", "wallType", "wallPrefab", "floorType", "floorPrefab", "abilityPrefab", "factionPrefab", "factionId" })
        {
            if (p.TryGetValue(key, out var value))
                p[key] = ResolveFriendlyPrefabName(value);
        }
    }

    static void NormalizeItemParameters(string actionName, Dictionary<string, string> p)
    {
        if (actionName != "inventory.send" && actionName != "inventory.count")
            return;

        if (p.ContainsKey("itemId"))
            return;

        var itemName = Text(p, "item", Text(p, "itemName", Text(p, "itemPrefab", Text(p, "prefab", ""))));
        if (string.IsNullOrWhiteSpace(itemName))
            return;

        if (TryResolveItemGuid(itemName, out var guid))
            p["itemId"] = guid.GuidHash.ToString();
    }

    static void NormalizeBuffParameters(string actionName, Dictionary<string, string> p)
    {
        if (actionName is "player.buff.apply" or "player.buff.remove" or "zone.buff.apply" or "zone.buff.remove")
        {
            CopyIfMissing(p, "buff", "buffPrefab");
            CopyIfMissing(p, "buffName", "buffPrefab");
            CopyIfMissing(p, "prefab", "buffPrefab");
        }

        if (p.TryGetValue("buffPrefab", out var buffPrefab))
            p["buffPrefab"] = ResolveFriendlyPrefabName(buffPrefab);
    }

    static void NormalizeTimerParameters(string actionName, Dictionary<string, string> p)
    {
        if (actionName != "timer.start")
            return;

        CopyIfMissing(p, "seconds", "duration");
        CopyIfMissing(p, "time", "duration");
    }

    static void NormalizeDamageParameters(string actionName, Dictionary<string, string> p)
    {
        if (actionName == "entity.damage_percent")
            CopyIfMissing(p, "percent", "damage");

        if (actionName == "entity.heal_percent")
            CopyIfMissing(p, "percent", "percent");
    }

    static void NormalizeBossParameters(string actionName, Dictionary<string, string> p)
    {
        if (actionName != "spawn.boss" && actionName != "boss.spawn")
            return;

        CopyIfMissing(p, "boss", "prefab");
        CopyIfMissing(p, "bossName", "prefab");
        if (p.TryGetValue("prefab", out var prefab))
            p["prefab"] = ResolveFriendlyPrefabName(prefab);
    }

    public static bool TryResolveItemGuid(string input, out PrefabGUID guid)
    {
        guid = PrefabGUID.Empty;
        if (string.IsNullOrWhiteSpace(input))
            return false;

        var value = input.Trim();
        if (int.TryParse(value, out var parsed))
        {
            guid = new PrefabGUID(parsed);
            return true;
        }

        foreach (var candidate in ExpandItemCandidates(value))
        {
            var resolved = PrefabHelper.GetPrefabGuidDeep(candidate);
            if (resolved.HasValue)
            {
                guid = resolved.Value;
                return true;
            }
        }

        var itemMatches = PrefabHelper.GetAllLive()
            .Where(kv => kv.Key.StartsWith("Item_", StringComparison.OrdinalIgnoreCase) &&
                         ContainsAllTerms(kv.Key, value))
            .Take(2)
            .ToList();

        if (itemMatches.Count == 1)
        {
            guid = itemMatches[0].Value;
            return true;
        }

        return false;
    }

    static IEnumerable<string> ExpandItemCandidates(string input)
    {
        yield return input;
        if (!input.StartsWith("Item_", StringComparison.OrdinalIgnoreCase))
        {
            yield return $"Item_{input}";
            yield return $"Item_Ingredient_{input}";
            yield return $"Item_Ingredient_{input}_Standard";
        }
    }

    static string ResolveFriendlyPrefabName(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return input;

        var trimmed = input.Trim();
        if (BossAliases.TryGetValue(trimmed, out var bossPrefab))
            return bossPrefab;

        return trimmed;
    }

    static bool ContainsAllTerms(string text, string query)
    {
        var terms = query
            .Split(new[] { ' ', '_', '-', '.', ',' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(t => t.Length > 1);

        return terms.All(term => text.Contains(term, StringComparison.OrdinalIgnoreCase));
    }

    static void CopyIfMissing(Dictionary<string, string> p, string from, string to)
    {
        if (!p.ContainsKey(to) && p.TryGetValue(from, out var value) && !string.IsNullOrWhiteSpace(value))
            p[to] = value;
    }

    static string Text(Dictionary<string, string> p, string key, string fallback) =>
        p.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value) ? value : fallback;
}
