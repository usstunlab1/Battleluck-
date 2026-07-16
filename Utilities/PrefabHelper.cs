using System.Collections;
using System.Reflection;
using System.IO;
using System.Text.Json;
using Stunlock.Core;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using BattleLuck.Models;

/// <summary>
/// Reflection-based prefab name → PrefabGUID resolver with 3-tier fallback:
/// exact match → case-insensitive → single partial match → FAIL HARD.
/// Also provides unified helper methods for prefab-related operations.
/// </summary>
public static class PrefabHelper
{
    static readonly Dictionary<string, PrefabGUID> _exactCache = new();
    static readonly Dictionary<string, PrefabGUID> _lowerCache = new(StringComparer.OrdinalIgnoreCase);
    static bool _initialized;

    static void EnsureInitialized()
    {
        if (_initialized) return;
        _initialized = true;

        foreach (var field in typeof(Prefabs).GetFields(BindingFlags.Public | BindingFlags.Static))
        {
            if (field.FieldType != typeof(PrefabGUID)) continue;
            var value = (PrefabGUID)field.GetValue(null)!;
            _exactCache[field.Name] = value;
            _lowerCache[field.Name] = value;
        }
    }

    /// <summary>
    /// 3-tier lookup: exact → case-insensitive → single partial → fail.
    /// </summary>
    public static bool TryGetPrefabGuid(string name, out PrefabGUID guid)
    {
        EnsureInitialized();

        // Tier 1: exact match
        if (_exactCache.TryGetValue(name, out guid))
            return true;

        // Tier 2: case-insensitive
        if (_lowerCache.TryGetValue(name, out guid))
            return true;

        // Tier 3: single partial match
        PrefabGUID? found = null;
        foreach (var kvp in _exactCache)
        {
            if (kvp.Key.Contains(name, StringComparison.OrdinalIgnoreCase))
            {
                if (found.HasValue)
                {
                    // Multiple matches — ambiguous, fail
                    guid = default;
                    return false;
                }
                found = kvp.Value;
            }
        }

        if (found.HasValue)
        {
            guid = found.Value;
            return true;
        }

        guid = default;
        return false;
    }

    /// <summary>Nullable version of TryGetPrefabGuid.</summary>
    public static PrefabGUID? GetPrefabGuid(string name)
        => TryGetPrefabGuid(name, out var guid) ? guid : null;

    /// <summary>
    /// Validates that a PrefabGUID exists in the game's prefab collection.
    /// </summary>
    public static bool ValidatePrefab(PrefabGUID guid)
    {
        var pcs = VRisingCore.PrefabCollectionSystem;
        return pcs._PrefabGuidToEntityMap.ContainsKey(guid);
    }

    /// <summary>Get prefab name from GUID by reverse lookup in registry.</summary>
    public static string? GetName(PrefabGUID guid)
    {
        EnsureInitialized();
        foreach (var kvp in _exactCache)
        {
            if (kvp.Value.GuidHash == guid.GuidHash)
                return kvp.Key;
        }
        return null;
    }

    /// <summary>Get all registered prefab names.</summary>
    public static IReadOnlyDictionary<string, PrefabGUID> GetAll()
    {
        EnsureInitialized();
        return _exactCache;
    }

    // ── Live PrefabCollectionSystem scanner ──────────────────────────────

    static readonly Dictionary<string, PrefabGUID> _liveCache = new(StringComparer.OrdinalIgnoreCase);
    static readonly Dictionary<PrefabGUID, string> _liveNameCache = new();
    static bool _liveScanned;
    static readonly string[] _prefabNameMapMembers =
    {
        "PrefabGuidToNameDictionary",
        "_PrefabGuidToNameDictionary",
        "PrefabGuidToNameMap",
        "_PrefabGuidToNameMap",
        "GuidToNameDictionary",
        "_GuidToNameDictionary",
        "PrefabNameDictionary",
        "_PrefabNameDictionary"
    };

    /// <summary>
    /// Scan PrefabCollectionSystem for all runtime prefab names.
    /// Must be called after VRisingCore.IsReady.
    /// Falls back to indexing GUIDs from entity map if name dictionary is unavailable.
    /// </summary>
    public static void ScanLivePrefabs()
    {
        if (_liveScanned) return;

        // Eagerly load from the archive first (Data/render-prefabs.json)
        LoadArchiveNames(Path.Combine("Data", "render-prefabs.json"));

        try
        {
            var pcs = VRisingCore.PrefabCollectionSystem;

            // Try name dictionary first (reflection)
            foreach (var kvp in EnumerateLivePrefabNames(pcs))
            {
                _liveCache.TryAdd(kvp.Value, kvp.Key);
                _liveNameCache[kvp.Key] = kvp.Value;
            }

            // Fallback: use _PrefabLookupMap.GetName() (KindredCommands approach)
            if (_liveCache.Count == 0)
            {
                try
                {
                    var lookupMap = pcs._PrefabLookupMap;
                    var entityMap = pcs._PrefabGuidToEntityMap;
                    var enumerator = entityMap.GetEnumerator();
                    int named = 0, unnamed = 0;
                    while (enumerator.MoveNext())
                    {
                        var guid = enumerator.Current.Key;
                        string? prefabName;
                        try
                        {
                            prefabName = lookupMap.GetName(guid);
                        }
                        catch
                        {
                            prefabName = null;
                        }
                        if (string.IsNullOrEmpty(prefabName))
                        {
                            prefabName = $"GUID_{guid.GuidHash}";
                            unnamed++;
                        }
                        else
                        {
                            named++;
                        }
                        _liveCache.TryAdd(prefabName, guid);
                        _liveNameCache.TryAdd(guid, prefabName);
                    }
                    BattleLuckPlugin.LogInfo($"[PrefabHelper] _PrefabLookupMap scan: {named} named, {unnamed} unnamed prefabs indexed.");
                }
                catch (Exception mapEx)
                {
                    BattleLuckPlugin.LogWarning($"[PrefabHelper] _PrefabLookupMap failed: {mapEx.Message}, falling back to GUID-only indexing.");
                    var map = pcs._PrefabGuidToEntityMap;
                    var enumerator = map.GetEnumerator();
                    int count = 0;
                    while (enumerator.MoveNext())
                    {
                        var guid = enumerator.Current.Key;
                        var hashName = $"GUID_{guid.GuidHash}";
                        _liveCache.TryAdd(hashName, guid);
                        _liveNameCache.TryAdd(guid, hashName);
                        count++;
                    }
                    BattleLuckPlugin.LogInfo($"[PrefabHelper] Fallback indexed {count} prefab GUIDs from entity map.");
                }
            }

            _liveScanned = true;
            BattleLuckPlugin.LogInfo($"[PrefabHelper] Live scan complete: {_liveCache.Count} prefabs indexed.");
        }
        catch (Exception ex)
        {
            BattleLuckPlugin.LogWarning($"[PrefabHelper] Live scan failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Loads name mappings from the external prefab archive to enrich the live registry.
    /// </summary>
    public static void LoadArchiveNames(string jsonPath)
    {
        try
        {
            if (!File.Exists(jsonPath)) return;
            var json = File.ReadAllText(jsonPath);
            var archive = JsonSerializer.Deserialize<PrefabArchive>(json);
            if (archive == null) return;

            int added = 0;
            foreach (var kvp in archive.Prefabs)
            {
                if (int.TryParse(kvp.Key, out int hash))
                {
                    var guid = new PrefabGUID(hash);
                    var name = kvp.Value.Name;
                    if (!string.IsNullOrEmpty(name))
                    {
                        if (_liveCache.TryAdd(name, guid))
                        {
                            _liveNameCache.TryAdd(guid, name);
                            added++;
                        }
                    }
                }
            }
            BattleLuckPlugin.LogInfo($"[PrefabHelper] Loaded {added} names from archive.");
        }
        catch (Exception ex)
        {
            BattleLuckPlugin.LogWarning($"[PrefabHelper] Failed to load archive names: {ex.Message}");
        }
    }

    /// <summary>
    /// Resolve a prefab name from live PrefabCollectionSystem.
    /// Falls back to partial matching. Requires ScanLivePrefabs() first.
    /// </summary>
    public static PrefabGUID? GetLivePrefabGuid(string name)
    {
        if (!_liveScanned) ScanLivePrefabs();

        // Exact match
        if (_liveCache.TryGetValue(name, out var guid))
            return guid;

        // Single partial match
        PrefabGUID? found = null;
        foreach (var kvp in _liveCache)
        {
            if (kvp.Key.Contains(name, StringComparison.OrdinalIgnoreCase))
            {
                if (found.HasValue) return null; // ambiguous
                found = kvp.Value;
            }
        }

        return found;
    }

    public static string? GetLivePrefabName(PrefabGUID guid)
    {
        if (!_liveScanned) ScanLivePrefabs();
        return _liveNameCache.TryGetValue(guid, out var name) ? name : null;
    }

    /// <summary>
    /// Combined lookup that only returns GUIDs present in the live prefab map.
    /// Prefers Prefabs.cs constants when still valid, then live exact/partial matches,
    /// then deprecated ArenaBuilds-style fallback heuristics.
    /// </summary>
    public static PrefabGUID? GetPrefabGuidDeep(string name)
        => GetValidPrefabGuidDeep(name);

    public static PrefabGUID? GetValidPrefabGuidDeep(string name)
        => TryGetValidPrefabGuidDeep(name, out var guid) ? guid : null;

    public static bool TryGetValidPrefabGuidDeep(string name, out PrefabGUID guid)
    {
        foreach (var candidate in ExpandPrefabCandidates(name))
        {
            if (TryGetPrefabGuid(candidate, out guid) && ValidatePrefab(guid))
                return true;

            var liveGuid = GetLivePrefabGuid(candidate);
            if (liveGuid.HasValue && ValidatePrefab(liveGuid.Value))
            {
                guid = liveGuid.Value;
                return true;
            }
        }

        var bestMatch = FindBestLivePrefab(name);
        if (bestMatch.HasValue && ValidatePrefab(bestMatch.Value))
        {
            guid = bestMatch.Value;
            return true;
        }

        guid = default;
        return false;
    }

    /// <summary>
    /// Strict lookup for safety-critical paths (no partial/fuzzy matching).
    /// Only accepts exact static names or exact live names.
    /// </summary>
    public static bool TryGetValidPrefabGuidStrict(string name, out PrefabGUID guid)
    {
        EnsureInitialized();

        if (_exactCache.TryGetValue(name, out guid) && ValidatePrefab(guid))
            return true;

        if (!_liveScanned) ScanLivePrefabs();
        if (_liveCache.TryGetValue(name, out var liveGuid) && ValidatePrefab(liveGuid))
        {
            guid = liveGuid;
            return true;
        }

        guid = default;
        return false;
    }

    /// <summary>Get all live prefab entries (for debug/export).</summary>
    public static IReadOnlyDictionary<string, PrefabGUID> GetAllLive()
    {
        if (!_liveScanned) ScanLivePrefabs();
        return _liveCache;
    }

    /// <summary>
    /// Get all live prefabs matching a filter (e.g., "AbilityGroup").
    /// </summary>
    public static IEnumerable<KeyValuePair<string, PrefabGUID>> FindLive(string filter)
    {
        if (!_liveScanned) ScanLivePrefabs();
        return _liveCache.Where(kvp => kvp.Key.Contains(filter, StringComparison.OrdinalIgnoreCase));
    }

    static IEnumerable<string> ExpandPrefabCandidates(string name)
    {
        var candidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void Add(string? candidate)
        {
            if (!string.IsNullOrWhiteSpace(candidate))
                candidates.Add(candidate);
        }

        Add(name);
        Add(name.Replace("_T09", "_", StringComparison.OrdinalIgnoreCase));
        Add(name.Replace("_T09", string.Empty, StringComparison.OrdinalIgnoreCase));
        Add(name.Replace("_Tier01_", "_", StringComparison.OrdinalIgnoreCase));
        Add(name.Replace("_Tier02_", "_", StringComparison.OrdinalIgnoreCase));
        Add(name.Replace("_Tier03_", "_", StringComparison.OrdinalIgnoreCase));
        Add(name.Replace("_Tier04_", "_", StringComparison.OrdinalIgnoreCase));
        Add(name.Replace("_Axe_", "_Axes_", StringComparison.OrdinalIgnoreCase));
        Add(name.Replace("_Axes_", "_Axe_", StringComparison.OrdinalIgnoreCase));
        Add(name.Replace("TM_Castle_", string.Empty, StringComparison.OrdinalIgnoreCase));
        Add(name.Replace("TM_", string.Empty, StringComparison.OrdinalIgnoreCase));

        return candidates;
    }

    static PrefabGUID? FindBestLivePrefab(string requestedName)
    {
        if (!_liveScanned) ScanLivePrefabs();

        int bestScore = 0;
        PrefabGUID bestGuid = default;

        foreach (var kvp in _liveCache)
        {
            int score = ScoreLivePrefab(requestedName, kvp.Key);
            if (score > bestScore)
            {
                bestScore = score;
                bestGuid = kvp.Value;
            }
        }

        return bestScore >= 160 ? bestGuid : null;
    }

    static int ScoreLivePrefab(string requestedName, string liveName)
    {
        int bestScore = 0;

        foreach (var candidate in ExpandPrefabCandidates(requestedName))
        {
            int score = 0;

            if (liveName.Equals(candidate, StringComparison.OrdinalIgnoreCase))
                score += 1000;
            else if (liveName.StartsWith(candidate, StringComparison.OrdinalIgnoreCase))
                score += 700;
            else if (liveName.Contains(candidate, StringComparison.OrdinalIgnoreCase))
                score += 500;

            var tokens = TokenizePrefabName(candidate);
            int matchedTokens = 0;
            foreach (var token in tokens)
            {
                if (liveName.Contains(token, StringComparison.OrdinalIgnoreCase))
                {
                    matchedTokens++;
                    score += 60;
                }
            }

            if (tokens.Count > 0 && matchedTokens == tokens.Count)
                score += 120;
            else if (tokens.Count > 1 && matchedTokens < 2)
                score -= 250;

            score += GetTierPreference(liveName);
            bestScore = Math.Max(bestScore, score);
        }

        return bestScore;
    }

    static List<string> TokenizePrefabName(string name)
    {
        var tokens = new List<string>();
        var parts = name.Split(new[] { '_', '-', ' ' }, StringSplitOptions.RemoveEmptyEntries);

        foreach (var part in parts)
        {
            if (part.Length < 3)
                continue;

            if (part.Equals("Item", StringComparison.OrdinalIgnoreCase)
                || part.Equals("TM", StringComparison.OrdinalIgnoreCase)
                || part.Equals("Prefab", StringComparison.OrdinalIgnoreCase)
                || part.Equals("GUID", StringComparison.OrdinalIgnoreCase))
                continue;

            if (part.StartsWith("Tier", StringComparison.OrdinalIgnoreCase)
                || part.StartsWith("T0", StringComparison.OrdinalIgnoreCase))
                continue;

            tokens.Add(part);
        }

        return tokens;
    }

    static int GetTierPreference(string prefabName)
    {
        var value = prefabName.ToLowerInvariant();

        if (value.Contains("legendary", StringComparison.Ordinal)) return 90;
        if (value.Contains("epic", StringComparison.Ordinal)) return 80;
        if (value.Contains("dracula", StringComparison.Ordinal)) return 75;
        if (value.Contains("bloodmoon", StringComparison.Ordinal)) return 74;
        if (value.Contains("ancestral", StringComparison.Ordinal)) return 73;
        if (value.Contains("spectral", StringComparison.Ordinal)) return 72;
        if (value.Contains("sanguine", StringComparison.Ordinal)) return 70;
        if (value.Contains("darksilver", StringComparison.Ordinal)) return 60;
        if (value.Contains("silver", StringComparison.Ordinal)) return 50;
        if (value.Contains("iron", StringComparison.Ordinal)) return 40;
        if (value.Contains("merciless", StringComparison.Ordinal)) return 35;
        if (value.Contains("copper", StringComparison.Ordinal)) return 30;
        if (value.Contains("tier04", StringComparison.Ordinal)) return 24;
        if (value.Contains("tier03", StringComparison.Ordinal)) return 23;
        if (value.Contains("tier02", StringComparison.Ordinal)) return 22;
        if (value.Contains("tier01", StringComparison.Ordinal)) return 21;

        return 0;
    }

    static IEnumerable<KeyValuePair<PrefabGUID, string>> EnumerateLivePrefabNames(PrefabCollectionSystem pcs)
    {
        var nameMap = GetPrefabNameMap(pcs);
        if (nameMap == null)
            yield break;

        foreach (var entry in EnumerateEntries(nameMap))
        {
            if (TryExtractPrefabNameEntry(entry, out var guid, out var name))
                yield return new KeyValuePair<PrefabGUID, string>(guid, name);
        }
    }

    static object? GetPrefabNameMap(PrefabCollectionSystem pcs)
    {
        foreach (var memberName in _prefabNameMapMembers)
        {
            var value = GetMemberValue(pcs, memberName);
            if (value != null)
                return value;
        }

        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        var type = pcs.GetType();

        foreach (var field in type.GetFields(flags))
        {
            if (LooksLikePrefabNameMap(field.Name))
                return field.GetValue(pcs);
        }

        foreach (var property in type.GetProperties(flags))
        {
            if (property.GetIndexParameters().Length == 0 && LooksLikePrefabNameMap(property.Name))
                return property.GetValue(pcs);
        }

        return null;
    }

    static IEnumerable<object> EnumerateEntries(object dictionaryLike)
    {
        if (dictionaryLike is IEnumerable enumerable)
        {
            foreach (var entry in enumerable)
            {
                if (entry != null)
                    yield return entry;
            }

            yield break;
        }

        var getEnumerator = dictionaryLike.GetType().GetMethod("GetEnumerator", BindingFlags.Instance | BindingFlags.Public);
        if (getEnumerator == null)
            yield break;

        var enumerator = getEnumerator.Invoke(dictionaryLike, null);
        if (enumerator == null)
            yield break;

        var enumeratorType = enumerator.GetType();
        var moveNext = enumeratorType.GetMethod("MoveNext", BindingFlags.Instance | BindingFlags.Public);
        var currentProperty = enumeratorType.GetProperty("Current", BindingFlags.Instance | BindingFlags.Public);
        if (moveNext == null || currentProperty == null)
            yield break;

        while (moveNext.Invoke(enumerator, null) is bool hasNext && hasNext)
        {
            var current = currentProperty.GetValue(enumerator);
            if (current != null)
                yield return current;
        }
    }

    static bool TryExtractPrefabNameEntry(object entry, out PrefabGUID guid, out string name)
    {
        guid = default;
        name = string.Empty;

        if (GetMemberValue(entry, "Key") is not PrefabGUID key)
            return false;

        var value = GetMemberValue(entry, "Value");
        var text = value?.ToString();
        if (string.IsNullOrWhiteSpace(text))
            return false;

        guid = key;
        name = text;
        return true;
    }

    static object? GetMemberValue(object instance, string memberName)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        var type = instance.GetType();

        var property = type.GetProperty(memberName, flags);
        if (property != null && property.GetIndexParameters().Length == 0)
            return property.GetValue(instance);

        var field = type.GetField(memberName, flags);
        return field?.GetValue(instance);
    }

    static bool LooksLikePrefabNameMap(string memberName)
    {
        var normalized = memberName.Replace("_", string.Empty, StringComparison.Ordinal).ToLowerInvariant();
        return normalized.Contains("prefab", StringComparison.Ordinal)
            && normalized.Contains("guid", StringComparison.Ordinal)
            && normalized.Contains("name", StringComparison.Ordinal);
    }

    // ── Unified Prefab Helper Methods ─────────────────────────────────────

    /// <summary>
    /// Get the yaw rotation in degrees from an entity's Rotation component.
    /// Consolidated from SchematicLoader and EventBlueprintService.
    /// </summary>
    public static float GetYawDegrees(Entity entity)
    {
        try
        {
            var em = VRisingCore.EntityManager;
            if (!em.HasComponent<Rotation>(entity))
                return 0f;
            var rotation = em.GetComponentData<Rotation>(entity).Value;
            var forward = math.mul(rotation, new float3(0f, 0f, 1f));
            return math.degrees(math.atan2(forward.x, forward.z));
        }
        catch
        {
            return 0f;
        }
    }

    /// <summary>
    /// Read the amount from an ItemPickup component using reflection.
    /// Consolidated from SchematicLoader and EventBlueprintService.
    /// </summary>
    public static int ReadItemPickupAmount(Entity entity)
    {
        try
        {
            var em = VRisingCore.EntityManager;
            if (!em.HasComponent<ItemPickup>(entity))
                return 1;

            var pickup = em.GetComponentData<ItemPickup>(entity);
            var type = pickup.GetType();
            foreach (var name in new[] { "Amount", "Stack", "StackSize", "Quantity", "ItemAmount" })
            {
                var field = type.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (field == null) continue;
                var value = field.GetValue(pickup);
                if (value is int i && i > 0) return i;
                if (value is uint u && u > 0) return (int)Math.Min(int.MaxValue, u);
                if (value is short s && s > 0) return s;
                if (value is ushort us && us > 0) return us;
            }
        }
        catch { }

        return 1;
    }

    /// <summary>
    /// Classify a structure prefab name into a structural kind (wall, floor, gate, door, ramp, tile, object).
    /// Consolidated from SchematicLoader and EventBlueprintService.
    /// </summary>
    public static string ClassifyStructure(string prefabName, Entity entity)
    {
        var em = VRisingCore.EntityManager;
        if (prefabName.Contains("Wall", StringComparison.OrdinalIgnoreCase)) return "wall";
        if (prefabName.Contains("Floor", StringComparison.OrdinalIgnoreCase)) return "floor";
        if (prefabName.Contains("Gate", StringComparison.OrdinalIgnoreCase)) return "gate";
        if (prefabName.Contains("Door", StringComparison.OrdinalIgnoreCase)) return "door";
        if (prefabName.Contains("Stair", StringComparison.OrdinalIgnoreCase) || prefabName.Contains("Ramp", StringComparison.OrdinalIgnoreCase)) return "ramp";
        if (em.HasComponent<TilePosition>(entity)) return "tile";
        return "object";
    }

    /// <summary>
    /// Check if a point is within XZ radius squared of a center point.
    /// Consolidated from SchematicLoader and EventBlueprintService.
    /// </summary>
    public static bool WithinXZ(float3 point, float3 center, float radiusSq)
    {
        var dx = point.x - center.x;
        var dz = point.z - center.z;
        return dx * dx + dz * dz <= radiusSq;
    }
}
