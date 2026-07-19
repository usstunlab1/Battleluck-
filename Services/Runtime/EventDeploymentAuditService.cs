using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace BattleLuck.Services.Runtime;

/// <summary>
/// Append-only, local audit trail for declarative event operations. It is
/// intentionally deterministic: records explain what happened and feed admin
/// recommendations, but never grant authority or execute a new action.
/// </summary>
public sealed class EventDeploymentAuditService
{
    public static string AuditPath => Path.Combine(ConfigLoader.ConfigRoot, "logs", "event_audit.jsonl");

    static readonly object WriteLock = new();
    const long MaxAuditBytes = 10 * 1024 * 1024;
    static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false
    };

    public void Record(
        string command,
        string eventId,
        string? source,
        OperationResult<EventDeploymentResult> result,
        bool rollback = false,
        bool dryRun = false)
    {
        try
        {
            ConfigLoader.EnsureDefaultsDeployed();
            var files = CaptureFiles(eventId);
            var error = result.Error;
            var record = new EventDeploymentAuditRecord
            {
                Timestamp = DateTime.UtcNow,
                Command = dryRun ? $"{command}_dry_run" : command,
                EventId = SafeEventId(eventId),
                Gist = source,
                Files = EventDeploymentService.RequiredFiles.ToDictionary(
                    key => key,
                    key => files.ContainsKey(key),
                    StringComparer.OrdinalIgnoreCase),
                FileHashes = files,
                Validation = new EventValidationAudit
                {
                    Json = result.Success || !HasCode(error, "EJSONPARSE"),
                    Schema = result.Success || !HasCode(error, "ESCHEMA"),
                    Ids = result.Success || !HasCode(error, "EINVALIDID", "EUNKNOWNID", "E_IDS")
                },
                Server = new EventServerAudit
                {
                    RegisterOk = result.Success && !dryRun,
                    StartOk = false,
                    Error = dryRun ? "Dry run: registration was not attempted." : result.Success ? null : error
                },
                Rollback = rollback,
                Exit = result.Success ? 0 : 1,
                ErrorCode = result.Success ? null : ClassifyError(error),
                Error = result.Success ? null : error,
                Backup = result.Value?.BackupPath ?? FindLatestBackup(eventId)
            };

            var line = JsonSerializer.Serialize(record, JsonOptions);
            AppendLine(line);
        }
        catch (Exception ex)
        {
            // A logging failure must never turn a successful deployment into a
            // failed deployment. Keep a concise diagnostic in the normal log.
            BattleLuckPlugin.LogWarning($"[EventDeploymentAudit] Write failed: {ex.Message}");
        }
    }

    public void RecordStatus(string eventId, OperationResult<EventDeploymentStatus> result)
    {
        try
        {
            var status = result.Value;
            var record = new EventDeploymentAuditRecord
            {
                Timestamp = DateTime.UtcNow,
                Command = "status",
                EventId = string.IsNullOrWhiteSpace(eventId) ? "all" : SafeEventId(eventId),
                Files = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase)
                {
                    ["flow.json"] = status?.HasAllFiles == true,
                    ["zones.json"] = status?.HasAllFiles == true,
                    ["kits.json"] = status?.HasAllFiles == true,
                    ["prompt.txt"] = status?.HasAllFiles == true
                },
                Validation = new EventValidationAudit
                {
                    Json = status?.FlowValid == true,
                    Schema = status?.FlowValid == true,
                    Ids = status?.FlowValid == true
                },
                Server = new EventServerAudit { RegisterOk = status?.Registered == true, StartOk = false },
                Exit = result.Success && status?.FlowValid == true ? 0 : 1,
                ErrorCode = result.Success && status?.FlowValid == true ? null : ClassifyError(result.Error ?? string.Join("; ", status?.Errors ?? new List<string>())),
                Error = result.Success ? string.Join("; ", status?.Errors ?? new List<string>()) : result.Error,
                Backup = status?.LatestBackup
            };
            AppendLine(JsonSerializer.Serialize(record, JsonOptions));
        }
        catch (Exception ex)
        {
            BattleLuckPlugin.LogWarning($"[EventDeploymentAudit] Status write failed: {ex.Message}");
        }
    }

    public void RecordStateRollback(
        string command,
        string target,
        bool success,
        string? error,
        int restored,
        int skipped)
    {
        try
        {
            var record = new EventDeploymentAuditRecord
            {
                Timestamp = DateTime.UtcNow,
                Command = command,
                EventId = SafeEventId(target),
                Validation = new EventValidationAudit { Json = true, Schema = true, Ids = true },
                Server = new EventServerAudit { RegisterOk = success, StartOk = false, Error = error },
                Rollback = true,
                Exit = success ? 0 : 1,
                ErrorCode = success ? null : ClassifyError(error),
                Error = error,
                RestoredPlayers = restored,
                SkippedPlayers = skipped
            };
            AppendLine(JsonSerializer.Serialize(record, JsonOptions));
        }
        catch (Exception ex)
        {
            BattleLuckPlugin.LogWarning($"[EventDeploymentAudit] State rollback write failed: {ex.Message}");
        }
    }

    /// <summary>Record a deterministic operator safety decision.</summary>
    public void RecordGuard(string command, string eventId, string? code, string message, bool forced = false)
    {
        try
        {
            var record = new EventDeploymentAuditRecord
            {
                Timestamp = DateTime.UtcNow,
                Command = command,
                EventId = SafeEventId(eventId),
                Validation = new EventValidationAudit { Json = true, Schema = true, Ids = true },
                Server = new EventServerAudit { RegisterOk = false, StartOk = false, Error = message },
                Exit = forced ? 0 : 1,
                ErrorCode = code,
                Error = message
            };
            AppendLine(JsonSerializer.Serialize(record, JsonOptions));
        }
        catch (Exception ex)
        {
            BattleLuckPlugin.LogWarning($"[EventDeploymentAudit] Guard write failed: {ex.Message}");
        }
    }

    static void AppendLine(string line)
    {
        lock (WriteLock)
        {
            var directory = Path.GetDirectoryName(AuditPath)!;
            Directory.CreateDirectory(directory);
            if (File.Exists(AuditPath) && new FileInfo(AuditPath).Length >= MaxAuditBytes)
                File.Move(AuditPath, AuditPath + ".1", overwrite: true);
            File.AppendAllText(AuditPath, line + Environment.NewLine, Encoding.UTF8);
        }
    }

    public EventDeploymentAuditSummary Summarize(string? eventId = null, int maxRecords = 500)
    {
        var records = ReadRecords(maxRecords)
            .Where(record => string.IsNullOrWhiteSpace(eventId) ||
                record.EventId.Equals(eventId.Trim(), StringComparison.OrdinalIgnoreCase))
            .ToList();

        var failures = records.Where(record => record.Exit != 0).ToList();
        var codes = failures
            .Where(record => !string.IsNullOrWhiteSpace(record.ErrorCode))
            .GroupBy(record => record.ErrorCode!, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(group => group.Count())
            .ThenBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
            .Take(5)
            .Select(group => $"{group.Key}={group.Count()}")
            .ToList();

        var recommendations = new List<string>();
        foreach (var code in failures
            .Where(record => !string.IsNullOrWhiteSpace(record.ErrorCode))
            .Select(record => record.ErrorCode!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(5))
        {
            recommendations.Add(RecommendationFor(code));
        }

        return new EventDeploymentAuditSummary
        {
            EventId = string.IsNullOrWhiteSpace(eventId) ? "all" : eventId.Trim(),
            Records = records.Count,
            Failures = failures.Count,
            SuccessfulDeployments = records.Count(record =>
                record.Command.Equals("deploy", StringComparison.OrdinalIgnoreCase) && record.Exit == 0),
            SuccessfulRollbacks = records.Count(record =>
                record.Command.Equals("rollback", StringComparison.OrdinalIgnoreCase) && record.Exit == 0),
            CommonErrors = codes,
            Recommendations = recommendations,
            Recent = records.OrderByDescending(record => record.Timestamp).Take(5).ToList()
        };
    }

    List<EventDeploymentAuditRecord> ReadRecords(int maxRecords)
    {
        if (!File.Exists(AuditPath))
            return new List<EventDeploymentAuditRecord>();

        var result = new List<EventDeploymentAuditRecord>();
        try
        {
            foreach (var line in File.ReadLines(AuditPath).Reverse().Take(Math.Clamp(maxRecords, 1, 5000)))
            {
                try
                {
                    var record = JsonSerializer.Deserialize<EventDeploymentAuditRecord>(line, JsonOptions);
                    if (record != null)
                        result.Add(record);
                }
                catch
                {
                    // A truncated final line must not hide the rest of the audit.
                }
            }
        }
        catch (Exception ex)
        {
            BattleLuckPlugin.LogWarning($"[EventDeploymentAudit] Read failed: {ex.Message}");
        }

        return result;
    }

    static Dictionary<string, string> CaptureFiles(string eventId)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var directory = Path.Combine(ConfigLoader.ConfigRoot, "events", SafeEventId(eventId));
        foreach (var file in EventDeploymentService.RequiredFiles)
        {
            var path = Path.Combine(directory, file);
            if (!File.Exists(path))
                continue;
            try
            {
                using var stream = File.OpenRead(path);
                result[file] = Convert.ToHexString(SHA256.Create().ComputeHash(stream)).ToLowerInvariant();
            }
            catch
            {
                // Hashes are diagnostic only; do not hide the operation result.
            }
        }
        return result;
    }

    static string? FindLatestBackup(string eventId)
    {
        var root = Path.Combine(ConfigLoader.ConfigRoot, "backups", SafeEventId(eventId));
        if (!Directory.Exists(root))
            return null;
        return Directory.EnumerateDirectories(root)
            .OrderByDescending(Directory.GetLastWriteTimeUtc)
            .FirstOrDefault();
    }

    static bool HasCode(string? error, params string[] codes) =>
        !string.IsNullOrWhiteSpace(error) && codes.Any(code => error.Contains(code, StringComparison.OrdinalIgnoreCase));

    static string SafeEventId(string? value)
    {
        var normalized = (value ?? "").Trim().ToLowerInvariant();
        return normalized.Length is >= 2 and <= 32 &&
            char.IsLetterOrDigit(normalized[0]) &&
            normalized.All(ch => char.IsLetterOrDigit(ch) || ch is '_' or '-')
            ? normalized
            : "invalid";
    }

    static string ClassifyError(string? error)
    {
        if (string.IsNullOrWhiteSpace(error)) return "EUNKNOWN";
        var known = new[] { "EJSONPARSE", "ESCHEMA", "EMISSINGFILE", "EINVALIDID", "EUNKNOWNID", "E_IDS", "E_TICK", "E_UUID_UNVERIFIED", "EGIST", "ERUNTIMEREGISTER", "EACTIVE", "EBACKUP", "E_BACKUP_TAMPERED", "E_NO_SNAPSHOT", "EZONEHASH", "E_RATE", "START_WINDOW_BLOCKED", "EPURGE", "EPURGE_CONFIRM" };
        var direct = known.FirstOrDefault(code => error.Contains(code, StringComparison.OrdinalIgnoreCase));
        if (direct != null) return direct;
        if (error.Contains("Gist", StringComparison.OrdinalIgnoreCase) || error.Contains("download", StringComparison.OrdinalIgnoreCase)) return "EGIST";
        if (error.Contains("active", StringComparison.OrdinalIgnoreCase)) return "EACTIVE";
        if (error.Contains("zone hash", StringComparison.OrdinalIgnoreCase)) return "EZONEHASH";
        if (error.Contains("backup", StringComparison.OrdinalIgnoreCase) || error.Contains("rolled back", StringComparison.OrdinalIgnoreCase)) return "EBACKUP";
        if (error.Contains("register", StringComparison.OrdinalIgnoreCase) || error.Contains("load event", StringComparison.OrdinalIgnoreCase)) return "ERUNTIMEREGISTER";
        return "EUNKNOWN";
    }

    static string RecommendationFor(string code) => code switch
    {
        "EJSONPARSE" => "Run tools/validators/validate-json.sh and fix the JSON syntax before deploying.",
        "ESCHEMA" => "Compare the event files with config/BattleLuck/events/schemas and add the missing required fields.",
        "E_IDS" or "EUNKNOWNID" => "Check the file and JSONPath against docs/audit/systems/allowlists and a target-server KindredExtract dump.",
        "E_TICK" => "Use validated wait:<seconds> and tick:<event-second> markers.",
        "E_UUID_UNVERIFIED" => "Remove the sequence UUID from the executable catalog until a target-server dump confirms it.",
        "EGIST" => "Use a public HTTPS gist.github.com URL containing all four required files.",
        "EZONEHASH" => "Choose a unique positive zone hash and keep flow.json and zones.json identical.",
        "EACTIVE" => "End the active event before replacing or restoring its definition.",
        "ERUNTIMEREGISTER" => "Use the Safe-Stage workflow, inspect the server log, and verify KindredExtract references.",
        "EBACKUP" => "Check the latest backup manifest and restore only a verified known-good snapshot.",
        "E_BACKUP_TAMPERED" => "Do not restore the backup; retain it for forensic review and create a new snapshot.",
        "E_NO_SNAPSHOT" => "Create a verified BattleLuck deployment snapshot before production deployment.",
        "E_RATE" or "START_WINDOW_BLOCKED" => "Retry during a low-load window or explicitly force the operation after review.",
        "EPURGE" or "EPURGE_CONFIRM" => "Use the exact BattleLuck backup id and add the final confirm token; never target the host world save.",
        _ => "Review the deployment error and run .ai event status <eventId> before retrying."
    };
}

public sealed class EventDeploymentAuditRecord
{
    public DateTime Timestamp { get; set; }
    public string Command { get; set; } = "";
    public string EventId { get; set; } = "";
    public string? Gist { get; set; }
    public Dictionary<string, bool> Files { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, string> FileHashes { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public EventValidationAudit Validation { get; set; } = new();
    public EventServerAudit Server { get; set; } = new();
    public bool Rollback { get; set; }
    public int Exit { get; set; }
    public string? ErrorCode { get; set; }
    public string? Error { get; set; }
    public string? Backup { get; set; }
    public int RestoredPlayers { get; set; }
    public int SkippedPlayers { get; set; }
}

public sealed class EventValidationAudit
{
    public bool Json { get; set; }
    public bool Schema { get; set; }
    public bool Ids { get; set; }
}

public sealed class EventServerAudit
{
    public bool RegisterOk { get; set; }
    public bool StartOk { get; set; }
    public string? Error { get; set; }
}

public sealed class EventDeploymentAuditSummary
{
    public string EventId { get; set; } = "all";
    public int Records { get; set; }
    public int Failures { get; set; }
    public int SuccessfulDeployments { get; set; }
    public int SuccessfulRollbacks { get; set; }
    public List<string> CommonErrors { get; set; } = new();
    public List<string> Recommendations { get; set; } = new();
    public List<EventDeploymentAuditRecord> Recent { get; set; } = new();
}
