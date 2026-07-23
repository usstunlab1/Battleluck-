using System.Text.Json;
using BattleLuck.Models;

namespace BattleLuck.Services;

/// <summary>
/// File-based persistence for <see cref="PlayerSnapshot"/> that is independent of Unity/BepInEx runtime.
/// Produces two files per Steam ID:
/// - "{steamId}_event.json" when the snapshot is captured inside an event (zoneHash > 0)
/// - "{steamId}_regular.json" for regular play (zoneHash <= 0)
/// Also supports a backward-compatibility fallback to old-style "{steamId}.json" files.
/// </summary>
public static class SnapshotPersistence
{
    /// <summary>Current schema version. Increment when breaking changes are made to PlayerSnapshot.</summary>
    public const int CurrentSchemaVersion = 2;

    /// <summary>Oldest schema version that can be safely deserialized into the current model.</summary>
    public const int MinimumReadableVersion = 1;

    static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    static string GetBepInExRootPath()
    {
        try
        {
            return BepInEx.Paths.BepInExRootPath ?? AppContext.BaseDirectory;
        }
        catch
        {
            return AppContext.BaseDirectory;
        }
    }

    static readonly string SnapshotDir = Path.Combine(GetBepInExRootPath(), "data", "BattleLuck", "snapshots");

    public static string DirectoryPath => SnapshotDir;

    public static string GetPath(ulong steamId, bool isEvent)
        => Path.Combine(SnapshotDir, isEvent ? $"{steamId}_event.json" : $"{steamId}_regular.json");

    public static bool Exists(ulong steamId, bool isEvent)
        => File.Exists(GetPath(steamId, isEvent));

    public static void Write(ulong steamId, PlayerSnapshot snapshot, bool isEvent)
    {
        Directory.CreateDirectory(SnapshotDir);
        var path = GetPath(steamId, isEvent);

        // Create a .bak of the previous snapshot before overwriting, so a
        // corrupt write can be recovered by the operator.
        if (File.Exists(path))
        {
            var backupPath = path + ".bak";
            try { File.Copy(path, backupPath, overwrite: true); }
            catch { /* best-effort; do not block the write */ }
        }

        snapshot.Version = CurrentSchemaVersion;
        var json = JsonSerializer.Serialize(snapshot, JsonOpts);
        SafeFileSystem.WriteAllTextAtomic(path, json);
    }

    public static PlayerSnapshot? Read(ulong steamId, bool isEvent)
    {
        var path = GetPath(steamId, isEvent);
        if (File.Exists(path))
        {
            var result = TryReadValidated(path);
            if (result != null) return result;

            // Primary file is corrupt — attempt .bak recovery.
            var backupPath = path + ".bak";
            if (File.Exists(backupPath))
            {
                var recovered = TryReadValidated(backupPath);
                if (recovered != null)
                {
                    BattleLuckLogger.Warning(
                        $"[SnapshotPersistence] Primary snapshot '{path}' is corrupt; recovered from .bak");
                    return recovered;
                }
            }
            return null;
        }

        // Fallback to legacy single file {steamId}.json, but only when it matches requested kind
        var oldPath = Path.Combine(SnapshotDir, $"{steamId}.json");
        if (!File.Exists(oldPath)) return null;

        try
        {
            var json = File.ReadAllText(oldPath);
            var snap = JsonSerializer.Deserialize<PlayerSnapshot>(json, JsonOpts);
            if (snap == null) return null;
            if (!IsVersionCompatible(snap.Version)) return null;
            var oldIsEvent = snap.ZoneHash > 0;
            return oldIsEvent == isEvent ? snap : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Attempt to read and validate a snapshot from <paramref name="path"/>.
    /// Returns null if the file is unreadable, corrupt, or has an incompatible schema version.
    /// </summary>
    static PlayerSnapshot? TryReadValidated(string path)
    {
        try
        {
            var json = File.ReadAllText(path);
            var snap = JsonSerializer.Deserialize<PlayerSnapshot>(json, JsonOpts);
            if (snap == null) return null;
            if (!IsVersionCompatible(snap.Version))
            {
                BattleLuckLogger.Warning(
                    $"[SnapshotPersistence] Snapshot '{path}' has incompatible version {snap.Version} " +
                    $"(min={MinimumReadableVersion}, current={CurrentSchemaVersion}).");
                return null;
            }
            return snap;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>True when a snapshot version can be safely deserialized into the current model.</summary>
    public static bool IsVersionCompatible(int version)
        => version >= MinimumReadableVersion && version <= CurrentSchemaVersion;

    public static void Delete(ulong steamId, bool isEvent)
    {
        var path = GetPath(steamId, isEvent);
        if (File.Exists(path))
        {
            try { File.Delete(path); } catch { /* ignore */ }
        }

        // Also remove legacy file if it corresponds to the same category
        var oldPath = Path.Combine(SnapshotDir, $"{steamId}.json");
        if (!File.Exists(oldPath)) return;
        try
        {
            var json = File.ReadAllText(oldPath);
            var snap = JsonSerializer.Deserialize<PlayerSnapshot>(json, JsonOpts);
            if (snap == null) return;
            var oldIsEvent = snap.ZoneHash > 0;
            if (oldIsEvent == isEvent)
                File.Delete(oldPath);
        }
        catch { /* ignore */ }
    }

    public static IReadOnlyList<PlayerSnapshot> ListAll()
    {
        Directory.CreateDirectory(SnapshotDir);
        var list = new List<PlayerSnapshot>();
        foreach (var file in Directory.GetFiles(SnapshotDir, "*.json"))
        {
            try
            {
                var json = File.ReadAllText(file);
                var snap = JsonSerializer.Deserialize<PlayerSnapshot>(json, JsonOpts);
                if (snap != null)
                    list.Add(snap);
            }
            catch
            {
                // ignore unreadable files
            }
        }
        return list;
    }
}
