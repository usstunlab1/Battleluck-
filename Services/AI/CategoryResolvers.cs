using System.Text.RegularExpressions;
using BattleLuck.Services.Runtime;

namespace BattleLuck.Services.AI;

/// <summary>
/// Category-specific resolvers for natural-language action targets.
/// Each resolver knows its own catalog and returns a structured result
/// rather than falling back to a random default.
/// </summary>
public static class CategoryResolvers
{
    public enum ResolutionKind { Resolved, Ambiguous, NotFound }

    public sealed class Resolution
    {
        public ResolutionKind Kind { get; init; }
        public string CanonicalName { get; init; } = "";
        public int PrefabGuid { get; init; }
        public string Category { get; init; } = "";
        public string[] Choices { get; init; } = Array.Empty<string>();
        public string Error { get; init; } = "";
    }

    /// <summary>
    /// Resolve a boss/VBlood name to a canonical prefab.
    /// Priority: exact name > exact alias > normalized > unique token > unique fuzzy.
    /// Never returns a default/random entry.
    /// </summary>
    public static Resolution ResolveBoss(string requestedName)
    {
        if (string.IsNullOrWhiteSpace(requestedName))
            return new Resolution { Kind = ResolutionKind.NotFound, Error = "No boss name provided." };

        var normalized = NormalizeName(requestedName);
        var candidates = GetBossCandidates();
        return ResolveFromCandidates(requestedName, normalized, candidates, "boss");
    }

    /// <summary>
    /// Resolve an NPC name to a canonical prefab.
    /// </summary>
    public static Resolution ResolveNpc(string requestedName)
    {
        if (string.IsNullOrWhiteSpace(requestedName))
            return new Resolution { Kind = ResolutionKind.NotFound, Error = "No NPC name provided." };

        var normalized = NormalizeName(requestedName);
        var candidates = GetNpcCandidates();
        return ResolveFromCandidates(requestedName, normalized, candidates, "npc");
    }

    /// <summary>
    /// Resolve a VBlood name to a canonical prefab.
    /// </summary>
    public static Resolution ResolveVBlood(string requestedName)
    {
        if (string.IsNullOrWhiteSpace(requestedName))
            return new Resolution { Kind = ResolutionKind.NotFound, Error = "No VBlood name provided." };

        var normalized = NormalizeName(requestedName);
        var candidates = GetVBloodCandidates();
        return ResolveFromCandidates(requestedName, normalized, candidates, "vblood");
    }

    /// <summary>
    /// Resolve a schematic name to a registered schematic ID.
    /// </summary>
    public static Resolution ResolveSchematic(string requestedName)
    {
        if (string.IsNullOrWhiteSpace(requestedName))
            return new Resolution { Kind = ResolutionKind.NotFound, Error = "No schematic name provided." };

        var normalized = NormalizeName(requestedName);
        var candidates = GetSchematicCandidates();
        return ResolveFromCandidates(requestedName, normalized, candidates, "schematic");
    }

    /// <summary>
    /// Resolve a floor schematic specifically.
    /// </summary>
    public static Resolution ResolveFloorSchematic(string requestedName)
    {
        if (string.IsNullOrWhiteSpace(requestedName))
            return new Resolution { Kind = ResolutionKind.NotFound, Error = "No floor schematic name provided." };

        var normalized = NormalizeName(requestedName);
        var allSchematics = GetSchematicCandidates();
        var floorSchematics = allSchematics
            .Where(s => s.Category.Contains("floor", StringComparison.OrdinalIgnoreCase) ||
                        s.CanonicalName.Contains("floor", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        if (floorSchematics.Length == 0)
        {
            // Fall back to all schematics if no floor-specific ones exist
            return ResolveFromCandidates(requestedName, normalized, allSchematics, "schematic");
        }

        return ResolveFromCandidates(requestedName, normalized, floorSchematics, "floor_schematic");
    }

    static Resolution ResolveFromCandidates(
        string rawName,
        string normalized,
        IReadOnlyList<PrefabCandidate> candidates,
        string category)
    {
        if (candidates.Count == 0)
            return new Resolution { Kind = ResolutionKind.NotFound, Error = $"No {category} entries are registered in the local catalog." };

        // 1. Exact canonical name match
        var exact = candidates.FirstOrDefault(c =>
            c.CanonicalName.Equals(rawName, StringComparison.OrdinalIgnoreCase));
        if (exact != null)
            return Resolved(exact, category);

        // 2. Exact alias match
        exact = candidates.FirstOrDefault(c =>
            c.Aliases.Any(a => a.Equals(rawName, StringComparison.OrdinalIgnoreCase)));
        if (exact != null)
            return Resolved(exact, category);

        // 3. Exact normalized match
        exact = candidates.FirstOrDefault(c =>
            NormalizeName(c.CanonicalName) == normalized);
        if (exact != null)
            return Resolved(exact, category);

        // 4. Unique token match (the raw name is a substring of exactly one canonical name or alias)
        var tokenMatches = candidates
            .Where(c => c.CanonicalName.Contains(rawName, StringComparison.OrdinalIgnoreCase) ||
                        c.Aliases.Any(a => a.Contains(rawName, StringComparison.OrdinalIgnoreCase)))
            .ToArray();

        if (tokenMatches.Length == 1)
            return Resolved(tokenMatches[0], category);

        // 5. Unique fuzzy match above threshold
        var fuzzyMatches = candidates
            .Select(c => (candidate: c, score: FuzzyScore(rawName, c.CanonicalName, c.Aliases)))
            .Where(x => x.score >= 0.5f)
            .OrderByDescending(x => x.score)
            .ToArray();

        if (fuzzyMatches.Length == 1)
            return Resolved(fuzzyMatches[0].candidate, category);

        // 6. Ambiguous or not found
        if (fuzzyMatches.Length > 1 || tokenMatches.Length > 1)
        {
            string[] choices;
            if (fuzzyMatches.Length > 1)
            {
                choices = fuzzyMatches
                    .Take(3)
                    .Select(x => x.candidate.CanonicalName)
                    .Distinct()
                    .ToArray();
            }
            else
            {
                choices = tokenMatches
                    .Take(3)
                    .Select(x => x.CanonicalName)
                    .Distinct()
                    .ToArray();
            }

            return new Resolution
            {
                Kind = ResolutionKind.Ambiguous,
                Category = category,
                Choices = choices,
                Error = $"Multiple {category} entries match '{rawName}'. Choices: {string.Join(", ", choices)}."
            };
        }

        return new Resolution
        {
            Kind = ResolutionKind.NotFound,
            Category = category,
            Error = $"'{rawName}' is not registered as a {category} in the local prefab catalog. Use .ai search {category} {rawName}."
        };
    }

    static Resolution Resolved(PrefabCandidate candidate, string category) => new()
    {
        Kind = ResolutionKind.Resolved,
        CanonicalName = candidate.CanonicalName,
        PrefabGuid = candidate.PrefabGuid,
        Category = category
    };

    static string NormalizeName(string name)
    {
        // Remove common prefixes, normalize whitespace and case
        var result = name.Trim();
        foreach (var prefix in new[] { "CHAR_", "Prefab_", "TM_", "AB_", "Item_", "VBlood_" })
        {
            if (result.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                result = result[prefix.Length..];
                break;
            }
        }
        result = Regex.Replace(result, @"[\s_-]+", "");
        return result.ToLowerInvariant();
    }

    static float FuzzyScore(string query, string canonicalName, IReadOnlyList<string> aliases)
    {
        var q = query.ToLowerInvariant();
        var haystack = canonicalName.ToLowerInvariant();

        // Check all aliases too
        foreach (var alias in aliases)
        {
            if (!string.IsNullOrWhiteSpace(alias))
                haystack += " " + alias.ToLowerInvariant();
        }

        // Simple containment score
        if (haystack.Contains(q))
            return 0.9f;

        // Token overlap
        var queryTokens = q.Split(new[] { ' ', '_', '-' }, StringSplitOptions.RemoveEmptyEntries);
        var haystackTokens = haystack.Split(new[] { ' ', '_', '-' }, StringSplitOptions.RemoveEmptyEntries);

        if (queryTokens.Length == 0)
            return 0f;

        var matches = queryTokens.Count(qt => haystackTokens.Any(ht => ht.Contains(qt) || qt.Contains(ht)));
        return (float)matches / queryTokens.Length * 0.8f;
    }

    sealed class PrefabCandidate
    {
        public string CanonicalName { get; init; } = "";
        public int PrefabGuid { get; init; }
        public List<string> Aliases { get; init; } = new();
        public string Category { get; init; } = "";
    }

    static List<PrefabCandidate> GetBossCandidates()
    {
        var candidates = new List<PrefabCandidate>();
        try
        {
            // Try to load from the prefab catalog if available
            var manifest = ActionManifestService.Instance;
            if (manifest.Entries.TryGetValue("spawn.boss", out var bossEntry))
            {
                foreach (var example in bossEntry.Examples)
                {
                    var (_, parameters) = FlowActionExecutor.ParseActionString(example);
                    if (parameters.TryGetValue("prefab", out var prefab) && !string.IsNullOrWhiteSpace(prefab))
                    {
                        var guid = ResolvePrefabGuid(prefab);
                        candidates.Add(new PrefabCandidate
                        {
                            CanonicalName = prefab,
                            PrefabGuid = guid,
                            Aliases = GenerateAliases(prefab),
                            Category = "boss"
                        });
                    }
                }
            }

            // Add known boss aliases from the alias registry
            foreach (var kvp in GetKnownBossAliases())
            {
                if (!candidates.Any(c => c.CanonicalName.Equals(kvp.Value, StringComparison.OrdinalIgnoreCase)))
                {
                    var guid = ResolvePrefabGuid(kvp.Value);
                    candidates.Add(new PrefabCandidate
                    {
                        CanonicalName = kvp.Value,
                        PrefabGuid = guid,
                        Aliases = new List<string> { kvp.Key },
                        Category = "boss"
                    });
                }
                else
                {
                    var existing = candidates.First(c => c.CanonicalName.Equals(kvp.Value, StringComparison.OrdinalIgnoreCase));
                    if (!existing.Aliases.Contains(kvp.Key, StringComparer.OrdinalIgnoreCase))
                        existing.Aliases.Add(kvp.Key);
                }
            }
        }
        catch
        {
            // If catalog isn't available, return empty
        }

        return candidates;
    }

    static List<PrefabCandidate> GetNpcCandidates()
    {
        var candidates = new List<PrefabCandidate>();
        try
        {
            var manifest = ActionManifestService.Instance;
            if (manifest.Entries.TryGetValue("spawn.npc", out var npcEntry))
            {
                foreach (var example in npcEntry.Examples)
                {
                    var (_, parameters) = FlowActionExecutor.ParseActionString(example);
                    if (parameters.TryGetValue("prefab", out var prefab) && !string.IsNullOrWhiteSpace(prefab))
                    {
                        var guid = ResolvePrefabGuid(prefab);
                        candidates.Add(new PrefabCandidate
                        {
                            CanonicalName = prefab,
                            PrefabGuid = guid,
                            Aliases = GenerateAliases(prefab),
                            Category = "npc"
                        });
                    }
                }
            }
        }
        catch { }
        return candidates;
    }

    static List<PrefabCandidate> GetVBloodCandidates()
    {
        var candidates = new List<PrefabCandidate>();
        try
        {
            var manifest = ActionManifestService.Instance;
            if (manifest.Entries.TryGetValue("spawn.vblood", out var vbloodEntry))
            {
                foreach (var example in vbloodEntry.Examples)
                {
                    var (_, parameters) = FlowActionExecutor.ParseActionString(example);
                    if (parameters.TryGetValue("prefab", out var prefab) && !string.IsNullOrWhiteSpace(prefab))
                    {
                        var guid = ResolvePrefabGuid(prefab);
                        candidates.Add(new PrefabCandidate
                        {
                            CanonicalName = prefab,
                            PrefabGuid = guid,
                            Aliases = GenerateAliases(prefab),
                            Category = "vblood"
                        });
                    }
                }
            }

            // Also check boss examples for VBlood entries
            if (manifest.Entries.TryGetValue("spawn.boss", out var bossEntry))
            {
                foreach (var example in bossEntry.Examples)
                {
                    var (_, parameters) = FlowActionExecutor.ParseActionString(example);
                    if (parameters.TryGetValue("prefab", out var prefab) && !string.IsNullOrWhiteSpace(prefab) &&
                        prefab.Contains("VBlood", StringComparison.OrdinalIgnoreCase))
                    {
                        if (!candidates.Any(c => c.CanonicalName.Equals(prefab, StringComparison.OrdinalIgnoreCase)))
                        {
                            var guid = ResolvePrefabGuid(prefab);
                            candidates.Add(new PrefabCandidate
                            {
                                CanonicalName = prefab,
                                PrefabGuid = guid,
                                Aliases = GenerateAliases(prefab),
                                Category = "vblood"
                            });
                        }
                    }
                }
            }
        }
        catch { }
        return candidates;
    }

    static List<PrefabCandidate> GetSchematicCandidates()
    {
        var candidates = new List<PrefabCandidate>();
        try
        {
            var manifest = ActionManifestService.Instance;
            // Check for schematic-related actions
            foreach (var actionName in new[] { "schematic.load", "schematic.loadat", "schematic.loadatpos" })
            {
                if (manifest.Entries.TryGetValue(actionName, out var schematicEntry))
                {
                    foreach (var example in schematicEntry.Examples)
                    {
                        var (_, parameters) = FlowActionExecutor.ParseActionString(example);
                        if (parameters.TryGetValue("schematicId", out var schematicId) && !string.IsNullOrWhiteSpace(schematicId))
                        {
                            if (!candidates.Any(c => c.CanonicalName.Equals(schematicId, StringComparison.OrdinalIgnoreCase)))
                            {
                                candidates.Add(new PrefabCandidate
                                {
                                    CanonicalName = schematicId,
                                    PrefabGuid = 0,
                                    Aliases = GenerateAliases(schematicId),
                                    Category = "schematic"
                                });
                            }
                        }
                    }
                }
            }
        }
        catch { }
        return candidates;
    }

    static int ResolvePrefabGuid(string prefabName)
    {
        if (int.TryParse(prefabName, out var hash))
            return hash;

        try
        {
            if (PrefabHelper.TryGetPrefabGuid(prefabName, out var guid))
                return guid.GuidHash;
        }
        catch { }

        return 0;
    }

    static List<string> GenerateAliases(string prefabName)
    {
        var aliases = new List<string>();
        if (string.IsNullOrWhiteSpace(prefabName))
            return aliases;

        // Strip common prefixes
        var stripped = prefabName;
        foreach (var prefix in new[] { "CHAR_", "Prefab_", "TM_", "AB_", "Item_", "VBlood_" })
        {
            if (stripped.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                stripped = stripped[prefix.Length..];
                break;
            }
        }

        // Add the stripped version
        if (stripped != prefabName && !string.IsNullOrWhiteSpace(stripped))
            aliases.Add(stripped);

        // Add human-readable variants
        var readable = Regex.Replace(stripped, "([a-z])([A-Z])", "$1 $2");
        readable = readable.Replace('_', ' ');
        if (readable != stripped)
            aliases.Add(readable);

        // Add lowercase version
        aliases.Add(stripped.ToLowerInvariant());

        return aliases;
    }

    static Dictionary<string, string> GetKnownBossAliases()
    {
        // These are populated from the local prefab catalog at runtime.
        // Static defaults are provided for common V Rising bosses.
        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            // These are examples - actual values come from the catalog
            { "alpha wolf", "CHAR_Beast_WolfAlpha" },
            { "keely", "CHAR_Frost_KeelyTheFrostArcher" },
            { "frost archer", "CHAR_Frost_KeelyTheFrostArcher" },
            { "vincent", "CHAR_Frost_VincentTheFrostBringer" },
            { "frost bringer", "CHAR_Frost_VincentTheFrostBringer" },
            { "morgana", "CHAR_Blackfang_Morgana_VBlood" },
            { "blackfang", "CHAR_Blackfang_Morgana_VBlood" },
            { "solarus", "CHAR_Solarus" },
            { "dracula", "CHAR_Vampire_HighLord_VBlood" },
            { "count dracula", "CHAR_Vampire_HighLord_VBlood" },
            { "vampire high lord", "CHAR_Vampire_HighLord_VBlood" },
            { "high lord", "CHAR_Vampire_HighLord_VBlood" },
            { "tristan", "CHAR_Tristan" },
            { "octavian", "CHAR_Octavian" },
            { "leandra", "CHAR_Leandra" },
            { "beatrice", "CHAR_Beatrice" },
            { "maja", "CHAR_Maja" },
            { "foulrot", "CHAR_Foulrot" },
            { "gorecrusher", "CHAR_Gorecrusher" },
            { "putrid rat", "CHAR_PutridRat" },
            { "putrid rat king", "CHAR_PutridRatKing" },
            { "terah", "CHAR_Terah" },
            { "azariel", "CHAR_Azariel" },
            { "ziva", "CHAR_Ziva" },
            { "jadon", "CHAR_Jadon" },
            { "mairwyn", "CHAR_Mairwyn" },
            { "kriig", "CHAR_Kriig" },
            { "ulf", "CHAR_Ulf" },
            { "ulf the berserker", "CHAR_Ulf" },
            { "willfred", "CHAR_Willfred" },
            { "willfred the iron", "CHAR_Willfred" },
            { "nicholaus", "CHAR_Nicholaus" },
            { "nicholaus the fallen", "CHAR_Nicholaus" },
            { "grayson", "CHAR_Grayson" },
            { "grayson the armorer", "CHAR_Grayson" },
            { "errol", "CHAR_Errol" },
            { "errol the stone", "CHAR_Errol" },
            { "lidia", "CHAR_Lidia" },
            { "lidia the chaos", "CHAR_Lidia" },
            { "angram", "CHAR_Angram" },
            { "angram the purifier", "CHAR_Angram" },
            { "goreswine", "CHAR_Goreswine" },
            { "goreswine the ravager", "CHAR_Goreswine" },
            { "matka", "CHAR_Matka" },
            { "matka the curse", "CHAR_Matka" },
            { "styx", "CHAR_Styx" },
            { "styx the shadow", "CHAR_Styx" },
            { "raziel", "CHAR_Raziel" },
            { "raziel the shepherd", "CHAR_Raziel" },
            { "dominator", "CHAR_Dominator" },
            { "the dominator", "CHAR_Dominator" },
            { "winged horror", "CHAR_WingedHorror" },
            { "the winged horror", "CHAR_WingedHorror" },
            { "monster", "CHAR_Monster" },
            { "the monster", "CHAR_Monster" },
            { "creature", "CHAR_Creature" },
            { "the creature", "CHAR_Creature" },
            { "behemoth", "CHAR_Behemoth" },
            { "the behemoth", "CHAR_Behemoth" },
        };
    }
}