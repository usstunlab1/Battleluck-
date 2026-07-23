using System.Linq;
using System.Globalization;
using System.Text.Json;
using BattleLuck.Utilities;

namespace BattleLuck.Services.Npc;

public sealed class NpcActionAuditor
{
    static readonly object _lock = new();
    static readonly List<NpcAuditEntry> _entries = new();
    static readonly List<NpcConsistencyRule> _rules = new();
    static int _sequence;

    public static void RegisterRules(IEnumerable<NpcConsistencyRule> rules)
    {
        lock (_lock)
        {
            _rules.Clear();
            _rules.AddRange(rules);
        }
    }

    public static NpcCommandConsistencyResult Validate(NpcAuditEntry entry, NpcControlService? service)
    {
        var result = new NpcCommandConsistencyResult { IsConsistent = true };

        if (string.IsNullOrWhiteSpace(entry.NpcId))
        {
            result.IsConsistent = false;
            result.Violation = "NPC id is required.";
            result.SuggestedFix = "Provide npcId or use selector=near to match nearby NPCs.";
            result.Severity = NpcConsistencySeverity.Error;
            return result;
        }

        if (service == null)
        {
            result.IsConsistent = false;
            result.Violation = "NPC control service is not initialized.";
            result.Severity = NpcConsistencySeverity.Critical;
            return result;
        }

        if (!service.TryGet(entry.NpcId, out var existing))
        {
            result.IsConsistent = false;
            result.Violation = $"NPC '{entry.NpcId}' is not tracked.";
            result.SuggestedFix = "Spawn the NPC first with npc.spawn, or use selector=near.";
            result.Severity = NpcConsistencySeverity.Error;
            return result;
        }

        if (!existing.IsAlive)
        {
            result.IsConsistent = false;
            result.Violation = $"NPC '{entry.NpcId}' is not alive.";
            result.SuggestedFix = "Wait for respawn or spawn a new NPC.";
            result.Severity = NpcConsistencySeverity.Error;
            return result;
        }

        if (entry.ActionName.Equals("npc.despawn", StringComparison.OrdinalIgnoreCase))
        {
            result.IsConsistent = true;
            return result;
        }

        if (entry.Parameters.TryGetValue("speed", out var speedObj) &&
            double.TryParse(
                Convert.ToString(speedObj, CultureInfo.InvariantCulture),
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out var speed))
        {
            if (speed < 0.5f || speed > 120f)
            {
                result.IsConsistent = false;
                result.Violation = $"Speed {speed} is outside safe range [0.5, 120].";
                result.SuggestedFix = "Use speed between 0.5 and 120.";
                result.Severity = NpcConsistencySeverity.Warning;
                return result;
            }
        }

        lock (_lock)
        {
            foreach (var rule in _rules)
            {
                if (rule.Match(entry, existing))
                {
                    result.IsConsistent = false;
                    result.Violation = rule.Message;
                    result.SuggestedFix = rule.SuggestedFix;
                    result.Severity = rule.Severity;
                    return result;
                }
            }
        }

        return result;
    }

    public static NpcAuditEntry RecordPre(NpcAuditEntry entry, NpcControlService? service)
    {
        entry.PreValidationPassed = true;
        if (service != null && service.TryGet(entry.NpcId, out var existing))
            entry.BeforeSnapshot = Snapshot(existing);

        var consistency = Validate(entry, service);
        entry.ConsistencyWarnings.AddRange(consistency.Violation != null
            ? new[] { $"[{consistency.Severity}] {consistency.Violation}" }
            : Array.Empty<string>());

        return entry;
    }

    public static NpcAuditEntry RecordPost(NpcAuditEntry entry, NpcControlService? service, bool executed, string? error = null)
    {
        entry.Executed = executed;
        entry.Error = error;
        entry.PostValidationPassed = executed && string.IsNullOrEmpty(error);

        if (service != null && service.TryGet(entry.NpcId, out var existing))
            entry.AfterSnapshot = Snapshot(existing);

        if (!entry.PreValidationPassed)
            entry.PostValidationPassed = false;

        lock (_lock)
            _entries.Add(entry);

        BattleLuckLogger.Info($"[NpcAudit] {entry.CorrelationId} {entry.ActionName} npc={entry.NpcId} executed={executed} pre={entry.PreValidationPassed} post={entry.PostValidationPassed} elapsed={entry.ElapsedMs}ms");

        return entry;
    }

    public static NpcAuditEntry Start(string actionName, string npcId, string source, string playerId, string sessionId)
    {
        return new NpcAuditEntry
        {
            CorrelationId = $"{DateTime.UtcNow:yyyyMMddHHmmss}_{++_sequence:D4}",
            TimestampUtc = DateTime.UtcNow,
            Source = source,
            PlayerId = playerId,
            ActionName = actionName,
            NpcId = npcId,
            SessionId = sessionId
        };
    }

    public static List<NpcAuditEntry> GetRecent(int limit = 100)
    {
        lock (_lock)
            return _entries.TakeLast(limit).ToList();
    }

    public static List<NpcAuditEntry> GetForNpc(string npcId, int limit = 50)
    {
        lock (_lock)
            return _entries.Where(e => e.NpcId.Equals(npcId, StringComparison.OrdinalIgnoreCase)).TakeLast(limit).ToList();
    }

    public static string ExportJson(int limit = 200)
    {
        lock (_lock)
        {
            var export = _entries.TakeLast(limit).Select(e => new
            {
                e.CorrelationId,
                e.TimestampUtc,
                e.Source,
                e.PlayerId,
                e.ActionName,
                e.NpcId,
                e.SessionId,
                e.PreviousMode,
                e.NewMode,
                Parameters = e.Parameters.Select(kv => $"{kv.Key}={kv.Value}").ToArray(),
                e.PreValidationPassed,
                e.PostValidationPassed,
                e.Executed,
                e.Error,
                e.ElapsedMs,
                Warnings = e.ConsistencyWarnings.ToArray()
            });
            return JsonSerializer.Serialize(export, new JsonSerializerOptions { WriteIndented = true });
        }
    }

    public static string GetSummary()
    {
        lock (_lock)
        {
            var total = _entries.Count;
            var passed = _entries.Count(e => e.PreValidationPassed && e.PostValidationPassed);
            var failed = _entries.Count(e => !e.PostValidationPassed);
            return $"[NpcAudit] Total={total} Passed={passed} Failed={failed}";
        }
    }

    public static void Clear()
    {
        lock (_lock) _entries.Clear();
    }

    static NpcStateSnapshot Snapshot(ControlledNpcEntry entry)
    {
        return new NpcStateSnapshot
        {
            NpcId = entry.NpcId,
            Mode = (NpcBehaviorMode)entry.Mode,
            Position = entry.Entity.GetPosition(),
            HomePosition = entry.HomePosition,
            TargetPosition = entry.TargetPosition,
            TargetEntity = entry.TargetEntity,
            MoveSpeed = entry.MoveSpeed,
            ForcedTeamId = entry.ForcedTeamId,
            ForcedFactionId = entry.ForcedFactionId,
            DisplayName = entry.DisplayName
        };
    }
}

public sealed class NpcConsistencyRule
{
    public string Name { get; set; } = "";
    public Func<NpcAuditEntry, ControlledNpcEntry, bool> Match { get; set; } = (_, _) => false;
    public string Message { get; set; } = "";
    public string? SuggestedFix { get; set; }
    public NpcConsistencySeverity Severity { get; set; } = NpcConsistencySeverity.Warning;
}
