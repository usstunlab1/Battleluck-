public static class BuildPaletteService
{
    static readonly object LockObj = new();
    static BuildPaletteFile? _state;

    static string PalettePath => Path.Combine(ConfigLoader.ConfigRoot, "build_palette.json");

    public static IReadOnlyList<BuildPaletteEntry> List(ulong ownerId)
    {
        lock (LockObj)
            return GetOwnerState(ownerId).Entries.ToList();
    }

    public static OperationResult<BuildPaletteEntry> Add(ulong ownerId, string searchTerm)
    {
        if (string.IsNullOrWhiteSpace(searchTerm))
            return OperationResult<BuildPaletteEntry>.Fail("search term is required.");

        PrefabHelper.ScanLivePrefabs();
        var matches = PrefabHelper.FindLive(searchTerm.Trim()).Take(20).ToList();
        if (matches.Count == 0)
            return OperationResult<BuildPaletteEntry>.Fail($"No live prefab matched '{searchTerm}'.");

        var chosen = matches
            .OrderBy(m => ScoreBuildPrefab(m.Key), Comparer<int>.Create((a, b) => b.CompareTo(a)))
            .ThenBy(m => m.Key, StringComparer.OrdinalIgnoreCase)
            .First();

        lock (LockObj)
        {
            var owner = GetOwnerState(ownerId);
            owner.Entries.RemoveAll(e =>
                e.Prefab.Equals(chosen.Key, StringComparison.OrdinalIgnoreCase) ||
                e.PrefabGuid == chosen.Value.GuidHash);

            var entry = new BuildPaletteEntry
            {
                Prefab = chosen.Key,
                PrefabGuid = chosen.Value.GuidHash,
                Search = searchTerm.Trim(),
                AddedAtUtc = DateTime.UtcNow.ToString("O")
            };
            owner.Entries.Add(entry);
            owner.Cursor = owner.Entries.Count - 1;
            SaveNoLock();
            return OperationResult<BuildPaletteEntry>.Ok(entry);
        }
    }

    public static OperationResult Remove(ulong ownerId, string searchTerm)
    {
        if (string.IsNullOrWhiteSpace(searchTerm))
            return OperationResult.Fail("search term is required.");

        lock (LockObj)
        {
            var owner = GetOwnerState(ownerId);
            var before = owner.Entries.Count;
            owner.Entries.RemoveAll(e =>
                e.Prefab.Contains(searchTerm, StringComparison.OrdinalIgnoreCase) ||
                e.Search.Contains(searchTerm, StringComparison.OrdinalIgnoreCase) ||
                e.PrefabGuid.ToString().Equals(searchTerm.Trim(), StringComparison.OrdinalIgnoreCase));

            if (owner.Entries.Count == before)
                return OperationResult.Fail($"No palette entry matched '{searchTerm}'.");

            owner.Cursor = owner.Entries.Count == 0 ? 0 : Math.Clamp(owner.Cursor, 0, owner.Entries.Count - 1);
            SaveNoLock();
            return OperationResult.Ok();
        }
    }

    public static OperationResult Clear(ulong ownerId)
    {
        lock (LockObj)
        {
            var owner = GetOwnerState(ownerId);
            owner.Entries.Clear();
            owner.Cursor = 0;
            SaveNoLock();
            return OperationResult.Ok();
        }
    }

    public static OperationResult<BuildPaletteEntry> Current(ulong ownerId)
    {
        lock (LockObj)
        {
            var owner = GetOwnerState(ownerId);
            if (owner.Entries.Count == 0)
                return OperationResult<BuildPaletteEntry>.Fail("Palette is empty.");
            owner.Cursor = Math.Clamp(owner.Cursor, 0, owner.Entries.Count - 1);
            return OperationResult<BuildPaletteEntry>.Ok(owner.Entries[owner.Cursor]);
        }
    }

    public static OperationResult<BuildPaletteEntry> Cycle(ulong ownerId, int delta)
    {
        lock (LockObj)
        {
            var owner = GetOwnerState(ownerId);
            if (owner.Entries.Count == 0)
                return OperationResult<BuildPaletteEntry>.Fail("Palette is empty.");

            var count = owner.Entries.Count;
            owner.Cursor = ((owner.Cursor + delta) % count + count) % count;
            SaveNoLock();
            return OperationResult<BuildPaletteEntry>.Ok(owner.Entries[owner.Cursor]);
        }
    }

    public static PrefabGUID? ResolveCurrentGuid(ulong ownerId)
    {
        var current = Current(ownerId);
        if (!current.Success || current.Value == null)
            return null;

        var entry = current.Value;
        var guid = new PrefabGUID(entry.PrefabGuid);
        if (entry.PrefabGuid != 0 && PrefabHelper.ValidatePrefab(guid))
            return guid;

        return PrefabHelper.GetValidPrefabGuidDeep(entry.Prefab);
    }

    static BuildPaletteOwner GetOwnerState(ulong ownerId)
    {
        var state = LoadNoLock();
        var key = ownerId.ToString();
        if (!state.Owners.TryGetValue(key, out var owner))
        {
            owner = new BuildPaletteOwner();
            state.Owners[key] = owner;
        }
        return owner;
    }

    static BuildPaletteFile LoadNoLock()
    {
        if (_state != null)
            return _state;

        try
        {
            if (File.Exists(PalettePath))
            {
                var json = File.ReadAllText(PalettePath);
                _state = JsonSerializer.Deserialize<BuildPaletteFile>(json, ConfigLoader.JsonOptions) ?? new BuildPaletteFile();
            }
            else
            {
                _state = new BuildPaletteFile();
            }
        }
        catch (Exception ex)
        {
            BattleLuckPlugin.LogWarning($"[BuildPalette] Failed to load build_palette.json: {ex.Message}");
            _state = new BuildPaletteFile();
        }

        return _state;
    }

    static void SaveNoLock()
    {
        try
        {
            Directory.CreateDirectory(ConfigLoader.ConfigRoot);
            var json = JsonSerializer.Serialize(LoadNoLock(), new JsonSerializerOptions(ConfigLoader.JsonOptions)
            {
                WriteIndented = true
            });
            File.WriteAllText(PalettePath, json);
        }
        catch (Exception ex)
        {
            BattleLuckPlugin.LogWarning($"[BuildPalette] Failed to save build_palette.json: {ex.Message}");
        }
    }

    static int ScoreBuildPrefab(string name)
    {
        var score = 0;
        if (name.Contains("TM_Castle", StringComparison.OrdinalIgnoreCase)) score += 100;
        if (name.Contains("Floor", StringComparison.OrdinalIgnoreCase)) score += 30;
        if (name.Contains("Wall", StringComparison.OrdinalIgnoreCase)) score += 25;
        if (name.Contains("Door", StringComparison.OrdinalIgnoreCase)) score += 20;
        if (name.Contains("Tile", StringComparison.OrdinalIgnoreCase)) score += 15;
        if (name.Contains("Tier02", StringComparison.OrdinalIgnoreCase)) score += 10;
        if (name.Contains("Tier03", StringComparison.OrdinalIgnoreCase)) score += 9;
        return score;
    }

    sealed class BuildPaletteFile
    {
        [JsonPropertyName("owners")]
        public Dictionary<string, BuildPaletteOwner> Owners { get; set; } = new();
    }

    sealed class BuildPaletteOwner
    {
        [JsonPropertyName("cursor")]
        public int Cursor { get; set; }

        [JsonPropertyName("entries")]
        public List<BuildPaletteEntry> Entries { get; set; } = new();
    }
}

public sealed class BuildPaletteEntry
{
    [JsonPropertyName("prefab")]
    public string Prefab { get; set; } = "";

    [JsonPropertyName("prefabGuid")]
    public int PrefabGuid { get; set; }

    [JsonPropertyName("search")]
    public string Search { get; set; } = "";

    [JsonPropertyName("addedAtUtc")]
    public string AddedAtUtc { get; set; } = "";
}
