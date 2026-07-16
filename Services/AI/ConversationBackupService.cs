using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using BattleLuck.Models;

namespace BattleLuck.Services.AI;

/// <summary>
/// Optional server-owner backup for per-player AI chat turns.
/// Files stay on the server host; the plugin never writes to a player's PC.
/// </summary>
public sealed class ConversationBackupService : IDisposable
{
    static readonly UTF8Encoding Utf8NoBom = new(false);
    static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false
    };

    readonly object _writeLock = new();
    readonly int _retentionDays;
    readonly long _maxFileBytes;
    readonly Action<string>? _logWarning;
    bool _disposed;
    DateTime _lastPruneUtc;

    public ConversationBackupService(ChatBackupSettings settings, string configRoot, Action<string>? logWarning = null)
    {
        _retentionDays = Math.Clamp(settings.RetentionDays, 1, 3650);
        _maxFileBytes = Math.Clamp(settings.MaxFileSizeMb, 1, 256) * 1024L * 1024L;
        _logWarning = logWarning;
        RootPath = ResolveRootPath(settings.Path, configRoot);
        Directory.CreateDirectory(RootPath);
        PruneOldFiles();
        ConversationStore.Instance.TurnAppended += OnTurnAppended;
        Enabled = true;
    }

    public bool Enabled { get; }
    public string RootPath { get; }

    public static string ResolveRootPath(string? configuredPath, string configRoot)
    {
        if (!string.IsNullOrWhiteSpace(configuredPath))
        {
            var trimmed = configuredPath.Trim();
            return Path.GetFullPath(Path.IsPathRooted(trimmed)
                ? trimmed
                : Path.Combine(configRoot, trimmed));
        }

        // Unity's Windows persistent-data location is LocalLow. Use the same
        // root requested by the server owner when BattleLuck runs on Windows.
        if (OperatingSystem.IsWindows())
        {
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var appData = Directory.GetParent(localAppData)?.FullName;
            if (!string.IsNullOrWhiteSpace(appData))
            {
                return Path.GetFullPath(Path.Combine(
                    appData,
                    "LocalLow",
                    "Stunlock Studios",
                    "VRising",
                    "BattleLuck",
                    "chat-backups"));
            }
        }

        // Portable fallback for Linux hosts and unusual service accounts.
        return Path.GetFullPath(Path.Combine(configRoot, "chat-backups"));
    }

    void OnTurnAppended(ConversationTurn turn)
    {
        if (_disposed || turn.SteamId == 0 || turn.Speaker == ConversationSpeaker.System)
            return;

        // Chat/file I/O is intentionally off the game thread.
        _ = Task.Run(() => WriteTurn(turn));
    }

    void WriteTurn(ConversationTurn turn)
    {
        try
        {
            lock (_writeLock)
            {
                if (_disposed)
                    return;

                var playerRoot = Path.Combine(RootPath, turn.SteamId.ToString(System.Globalization.CultureInfo.InvariantCulture));
                Directory.CreateDirectory(playerRoot);
                var stamp = turn.Timestamp.ToUniversalTime();
                var baseName = stamp.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
                var path = FindWritablePath(playerRoot, baseName);
                if (path == null)
                    return;

                var record = new BackupTurn
                {
                    TimestampUtc = stamp,
                    Speaker = turn.Speaker.ToString(),
                    SteamId = turn.SteamId,
                    DisplayName = turn.DisplayName,
                    Text = turn.Text,
                    ActionResults = turn.ActionResults.ToArray()
                };
                var line = JsonSerializer.Serialize(record, JsonOptions) + Environment.NewLine;
                File.AppendAllText(path, line, Utf8NoBom);

                if (DateTime.UtcNow - _lastPruneUtc > TimeSpan.FromHours(1))
                    PruneOldFilesUnsafe(DateTime.UtcNow);
            }
        }
        catch (Exception ex)
        {
            _logWarning?.Invoke($"[AI] Chat backup write failed: {ex.Message}");
        }
    }

    string? FindWritablePath(string playerRoot, string baseName)
    {
        for (var part = 0; part < 1000; part++)
        {
            var suffix = part == 0 ? "" : $"-{part:000}";
            var path = Path.Combine(playerRoot, $"{baseName}{suffix}.jsonl");
            if (!File.Exists(path) || new FileInfo(path).Length < _maxFileBytes)
                return path;
        }

        _logWarning?.Invoke($"[AI] Chat backup reached the daily file limit for {playerRoot}; new turns are not persisted.");
        return null;
    }

    void PruneOldFiles()
    {
        lock (_writeLock)
            PruneOldFilesUnsafe(DateTime.UtcNow);
    }

    void PruneOldFilesUnsafe(DateTime now)
    {
        _lastPruneUtc = now;
        var cutoff = now.AddDays(-_retentionDays);
        if (!Directory.Exists(RootPath))
            return;

        foreach (var file in Directory.EnumerateFiles(RootPath, "*.jsonl", SearchOption.AllDirectories))
        {
            try
            {
                if (File.GetLastWriteTimeUtc(file) < cutoff)
                    File.Delete(file);
            }
            catch (Exception ex)
            {
                _logWarning?.Invoke($"[AI] Chat backup cleanup failed for {Path.GetFileName(file)}: {ex.Message}");
            }
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        ConversationStore.Instance.TurnAppended -= OnTurnAppended;
    }

    sealed class BackupTurn
    {
        [JsonPropertyName("timestamp_utc")]
        public DateTime TimestampUtc { get; init; }
        [JsonPropertyName("speaker")]
        public string Speaker { get; init; } = "";
        [JsonPropertyName("steam_id")]
        public ulong SteamId { get; init; }
        [JsonPropertyName("display_name")]
        public string DisplayName { get; init; } = "";
        [JsonPropertyName("text")]
        public string Text { get; init; } = "";
        [JsonPropertyName("action_results")]
        public string[] ActionResults { get; init; } = Array.Empty<string>();
    }
}
