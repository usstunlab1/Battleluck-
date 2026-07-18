using System.IO;

namespace BattleLuck.Services.Runtime
{
    /// <summary>
    /// Prefab registry facade backed by BattleLuck's reflection/live-prefab helper and the external prefab archive.
    /// </summary>
    public class PrefabRegistryServiceReal : IPrefabRegistryService
    {
        private static PrefabArchive? _archive;
        private static readonly object _archiveLock = new();

        private static void EnsureArchiveLoaded()
        {
            if (_archive != null) return;
            lock (_archiveLock)
            {
                if (_archive != null) return;
                try
                {
                    var path = Path.Combine("Data", "render-prefabs.json");
                    if (File.Exists(path))
                    {
                        var json = File.ReadAllText(path);
                        _archive = JsonSerializer.Deserialize<PrefabArchive>(json);
                        BattleLuckPlugin.LogInfo($"[PrefabRegistry] Loaded archive with {_archive?.Prefabs.Count} entries.");
                    }
                }
                catch (Exception ex)
                {
                    BattleLuckPlugin.LogError($"[PrefabRegistry] Failed to load archive: {ex.Message}");
                }
            }
        }

        public static string GetCategory(PrefabGUID guid)
        {
            EnsureArchiveLoaded();
            var hashStr = guid.GuidHash.ToString();
            if (_archive != null && _archive.Prefabs.TryGetValue(hashStr, out var entry))
                return entry.Category;

            var name = PrefabHelper.GetName(guid);
            return name != null ? CategorizePrefab(name) : "Other";
        }

        public Task<List<PrefabInfoDto>> ScanPrefabsAsync(string? categoryFilter = null)
        {
            EnsureArchiveLoaded();
            
            // Merge static registry with archive data
            var allNames = PrefabHelper.GetAll().Keys.ToList();
            var infos = new List<PrefabInfoDto>();

            foreach (var name in allNames)
            {
                var info = ToInfo(name);
                if (string.IsNullOrWhiteSpace(categoryFilter) || string.Equals(info.Category, categoryFilter, StringComparison.OrdinalIgnoreCase))
                {
                    infos.Add(info);
                }
            }

            return Task.FromResult(infos);
        }

        public Task<PrefabInfoDto?> GetPrefabAsync(string prefabName)
        {
            EnsureArchiveLoaded();
            return Task.FromResult(PrefabHelper.TryGetPrefabGuid(prefabName, out _) ? ToInfo(prefabName) : null);
        }

        public Task<List<ComponentDescriptorDto>> GetPrefabComponentsAsync(string prefabName)
        {
            return Task.FromResult(new List<ComponentDescriptorDto>
            {
                new() { Name = "PrefabGUID", Description = "BattleLuck-resolved prefab identifier" }
            });
        }

        public Task<PrefabAnalysisDto> AnalyzePrefabAsync(string prefabName)
        {
            EnsureArchiveLoaded();
            var valid = PrefabHelper.TryGetPrefabGuid(prefabName, out var guid);
            var errors = new List<string>();
            if (!valid) errors.Add("Prefab was not found in BattleLuck static registry.");
            
            // Check if archive has it
            var archiveEntry = FindArchiveEntry(prefabName);
            if (archiveEntry == null && valid)
            {
                // We have it in code but not in archive
            }

            return Task.FromResult(new PrefabAnalysisDto
            {
                PrefabName = prefabName,
                Errors = errors,
                Performance = new PrefabPerformanceMetricsDto { IsOptimized = true }
            });
        }

        public async Task<List<PrefabInfoDto>> FindPrefabsByTagAsync(string tag)
        {
            var all = await ScanPrefabsAsync();
            return all.Where(p => p.Tags.Contains(tag, StringComparer.OrdinalIgnoreCase)).ToList();
        }

        public Task<PrefabDependencyGraphDto> GetDependencyGraphAsync(string prefabName)
        {
            return Task.FromResult(new PrefabDependencyGraphDto
            {
                RootPrefab = prefabName,
                HasCircularDependency = false
            });
        }

        PrefabInfoDto ToInfo(string name)
        {
            var archiveEntry = FindArchiveEntry(name);
            var category = archiveEntry?.Category ?? CategorizePrefab(name);
            
            var info = new PrefabInfoDto
            {
                Name = name,
                Category = category,
                IsValid = true,
                ComponentCount = 0,
                LastModifiedUtc = DateTime.UtcNow,
                Tags = new List<string> { category }
            };

            if (archiveEntry != null)
            {
                foreach (var flag in archiveEntry.Flags)
                {
                    if (flag.Value) info.Tags.Add(flag.Key);
                }
            }

            return info;
        }

        PrefabArchiveEntry? FindArchiveEntry(string name)
        {
            if (_archive == null) return null;
            
            // Try by name directly
            foreach (var entry in _archive.Prefabs.Values)
            {
                if (string.Equals(entry.Name, name, StringComparison.OrdinalIgnoreCase))
                    return entry;
            }

            // Try by hash if possible
            if (PrefabHelper.TryGetPrefabGuid(name, out var guid))
            {
                var hashStr = guid.GuidHash.ToString();
                if (_archive.Prefabs.TryGetValue(hashStr, out var entry))
                    return entry;
            }

            return null;
        }

        static string CategorizePrefab(string prefabName)
        {
            if (prefabName.StartsWith("CHAR_", StringComparison.OrdinalIgnoreCase)) return "Characters";
            if (prefabName.StartsWith("ITEM_", StringComparison.OrdinalIgnoreCase)) return "Items";
            if (prefabName.StartsWith("VBLOOD_", StringComparison.OrdinalIgnoreCase)) return "VBoss";
            if (prefabName.Contains("BUFF", StringComparison.OrdinalIgnoreCase)) return "Buffs";
            return "Other";
        }
    }
}
