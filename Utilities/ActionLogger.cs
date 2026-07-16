using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace BattleLuck.Utilities;

/// <summary>
/// Detailed action logger for tracking execution flow, validation, and state changes.
/// Provides structured logging with timestamps, context, and performance metrics.
/// </summary>
public sealed class ActionLogger
{
    private static readonly object _lock = new();
    private readonly string _source;
    private readonly List<ActionLogEntry> _entries = new();
    private readonly Stopwatch _sessionTimer = Stopwatch.StartNew();

    public ActionLogger(string source = "ActionRuntime")
    {
        _source = source;
    }

    public sealed class ActionLogEntry
    {
        public DateTime Timestamp { get; set; }
        public string Source { get; set; } = "";
        public string Phase { get; set; } = ""; // Parse, Validate, Execute, Complete, Error
        public string ActionName { get; set; } = "";
        public string ActionId { get; set; } = "";
        public string Status { get; set; } = ""; // Pending, InProgress, Success, Failed, Skipped
        public string Message { get; set; } = "";
        public Dictionary<string, object> Context { get; set; } = new();
        public long ElapsedMs { get; set; }
        public string? Error { get; set; }
        public int? PlayerId { get; set; }

        public override string ToString()
        {
            var ctx = string.Join(" | ", Context);
            var contextStr = !string.IsNullOrWhiteSpace(ctx) ? $" [{ctx}]" : "";
            var errorStr = !string.IsNullOrWhiteSpace(Error) ? $" ERROR: {Error}" : "";
            return $"[{Timestamp:HH:mm:ss.fff}] {Phase,-10} {Status,-10} {ActionName,-25} {Message}{contextStr}{errorStr} ({ElapsedMs}ms)";
        }
    }

    public void LogParse(string actionString, string actionName, Dictionary<string, string>? parameters = null)
    {
        Log(new ActionLogEntry
        {
            Phase = "Parse",
            Status = "InProgress",
            ActionName = actionName,
            Message = "Parsing action string",
            Context = new()
            {
                { "input", actionString },
                { "paramCount", parameters?.Count ?? 0 }
            }
        });
    }

    public void LogValidation(string actionName, string actionId, bool valid, string? error = null)
    {
        Log(new ActionLogEntry
        {
            Phase = "Validate",
            Status = valid ? "Success" : "Failed",
            ActionName = actionName,
            ActionId = actionId,
            Message = valid ? "Validation passed" : "Validation failed",
            Error = error,
            Context = new()
            {
                { "registry_check", "passed" },
                { "parameter_check", valid ? "passed" : "failed" }
            }
        });
    }

    public void LogSequenceResolution(string sequenceId, int stepCount, bool found, string? error = null)
    {
        Log(new ActionLogEntry
        {
            Phase = "Validate",
            Status = found ? "Success" : "Failed",
            ActionName = "sequence.step.run",
            ActionId = sequenceId,
            Message = found ? $"Sequence found with {stepCount} steps" : "Sequence not found",
            Error = error,
            Context = new()
            {
                { "sequenceId", sequenceId },
                { "stepCount", stepCount }
            }
        });
    }

    public void LogTechResolution(List<string> requestedTechs, int activeTechs, int suspendedTechs, string? error = null)
    {
        Log(new ActionLogEntry
        {
            Phase = "Validate",
            Status = activeTechs > 0 ? "Success" : "Skipped",
            ActionName = "tech.resolve",
            Message = $"Tech resolution: {activeTechs} active, {suspendedTechs} suspended",
            Error = error,
            Context = new()
            {
                { "requested", requestedTechs.Count },
                { "active", activeTechs },
                { "suspended", suspendedTechs }
            }
        });
    }

    public void LogZoneEntry(int zoneHash, string zoneName, bool success, string? error = null)
    {
        Log(new ActionLogEntry
        {
            Phase = "Execute",
            Status = success ? "Success" : "Failed",
            ActionName = "zone.enter",
            Message = success ? $"Entered zone {zoneName}" : "Zone entry failed",
            Error = error,
            Context = new()
            {
                { "zoneHash", zoneHash },
                { "zoneName", zoneName }
            }
        });
    }

    public void LogActionExecution(
        string actionName,
        string actionId,
        Dictionary<string, string> parameters,
        bool inProgress = true)
    {
        var status = inProgress ? "InProgress" : "Complete";
        var paramStr = string.Join("|", new List<string>(parameters.Keys).Take(3));
        
        Log(new ActionLogEntry
        {
            Phase = "Execute",
            Status = status,
            ActionName = actionName,
            ActionId = actionId,
            Message = $"Action execution {(inProgress ? "started" : "completed")}",
            Context = new()
            {
                { "params", paramStr },
                { "paramCount", parameters.Count }
            }
        });
    }

    public void LogActionResult(string actionName, bool success, string? result = null, string? error = null, long elapsedMs = 0)
    {
        Log(new ActionLogEntry
        {
            Phase = "Complete",
            Status = success ? "Success" : "Failed",
            ActionName = actionName,
            Message = success ? "Action completed" : "Action failed",
            Error = error,
            Context = new()
            {
                { "result", result ?? "none" }
            },
            ElapsedMs = elapsedMs
        });
    }

    public void LogFlowExecution(string flowName, int actionCount, bool started = true)
    {
        Log(new ActionLogEntry
        {
            Phase = "Execute",
            Status = started ? "InProgress" : "Complete",
            ActionName = "flow.execute",
            Message = $"Flow {flowName} {(started ? "started" : "completed")} with {actionCount} actions",
            Context = new()
            {
                { "flowName", flowName },
                { "actionCount", actionCount }
            }
        });
    }

    public void LogConditionEvaluation(string conditionOp, string left, string right, bool result)
    {
        Log(new ActionLogEntry
        {
            Phase = "Validate",
            Status = "Success",
            ActionName = "condition.eval",
            Message = $"Condition {conditionOp}({left}, {right}) = {result}",
            Context = new()
            {
                { "operator", conditionOp },
                { "result", result }
            }
        });
    }

    public void LogConflictResolution(string incomingTech, string activeTech, string conflictMode, bool accepted)
    {
        Log(new ActionLogEntry
        {
            Phase = "Validate",
            Status = accepted ? "Success" : "Skipped",
            ActionName = "tech.conflict",
            Message = $"Conflict {incomingTech} vs {activeTech} ({conflictMode}): {(accepted ? "accepted" : "rejected")}",
            Context = new()
            {
                { "incoming", incomingTech },
                { "active", activeTech },
                { "mode", conflictMode }
            }
        });
    }

    public void LogPipelineStage(string stage, bool passed, string? details = null)
    {
        Log(new ActionLogEntry
        {
            Phase = "Validate",
            Status = passed ? "Success" : "Failed",
            ActionName = "pipeline.stage",
            Message = $"Pipeline stage {stage}: {(passed ? "passed" : "failed")} {details}",
            Context = new()
            {
                { "stage", stage }
            }
        });
    }

    public void LogError(string actionName, string errorMessage, Exception? ex = null)
    {
        Log(new ActionLogEntry
        {
            Phase = "Error",
            Status = "Failed",
            ActionName = actionName,
            Message = "Action error",
            Error = errorMessage,
            Context = new()
            {
                { "exceptionType", ex?.GetType().Name ?? "none" },
                { "stackTrace", ex?.StackTrace?.Split('\n').FirstOrDefault() ?? "none" }
            }
        });
    }

    public void LogNpcCommand(string npcId, string actionName, string mode, bool success, string? error = null)
    {
        Log(new ActionLogEntry
        {
            Phase = "Execute",
            Status = success ? "Success" : "Failed",
            ActionName = actionName,
            Message = $"NPC command {actionName} npc={npcId} mode={mode}",
            Error = error,
            Context = new()
            {
                { "npcId", npcId },
                { "mode", mode }
            }
        });
    }

    public void LogNpcStateChange(string npcId, string fromMode, string toMode, Dictionary<string, object>? changes = null)
    {
        Log(new ActionLogEntry
        {
            Phase = "Execute",
            Status = "Success",
            ActionName = "npc.state_change",
            Message = $"NPC state change npc={npcId} {fromMode} -> {toMode}",
            Context = new()
            {
                { "npcId", npcId },
                { "fromMode", fromMode },
                { "toMode", toMode },
                { "changes", changes?.Count ?? 0 }
            }
        });
    }

    public void LogNpcAudit(string correlationId, string actionName, string npcId, bool preValid, bool postValid, long elapsedMs)
    {
        Log(new ActionLogEntry
        {
            Phase = "Complete",
            Status = (preValid && postValid) ? "Success" : "Failed",
            ActionName = "npc.audit",
            Message = $"NPC audit {correlationId} {actionName} npc={npcId}",
            Context = new()
            {
                { "correlationId", correlationId },
                { "preValid", preValid },
                { "postValid", postValid },
                { "elapsedMs", elapsedMs }
            },
            ElapsedMs = elapsedMs
        });
    }

    private void Log(ActionLogEntry entry)
    {
        lock (_lock)
        {
            entry.Timestamp = DateTime.UtcNow;
            entry.Source = _source;
            if (entry.ElapsedMs == 0)
                entry.ElapsedMs = _sessionTimer.ElapsedMilliseconds;

            _entries.Add(entry);

            // Also log to console/plugin logger
            BattleLuckPlugin.LogInfo(entry.ToString());
        }
    }

    public List<ActionLogEntry> GetEntries(int limit = 100)
    {
        lock (_lock)
        {
            return _entries.TakeLast(limit).ToList();
        }
    }

    public string GetSummary()
    {
        lock (_lock)
        {
            var success = _entries.Count(e => e.Status == "Success");
            var failed = _entries.Count(e => e.Status == "Failed");
            var skipped = _entries.Count(e => e.Status == "Skipped");
            var totalMs = _entries.Sum(e => e.ElapsedMs);

            return $"[{_source}] Total: {_entries.Count} | Success: {success} | Failed: {failed} | Skipped: {skipped} | Duration: {totalMs}ms";
        }
    }

    public void Clear()
    {
        lock (_lock)
        {
            _entries.Clear();
            _sessionTimer.Restart();
        }
    }

    public string ExportJson()
    {
        lock (_lock)
        {
            var exported = _entries.Select(e => new
            {
                e.Timestamp,
                e.Phase,
                e.Status,
                e.ActionName,
                e.ActionId,
                e.Message,
                e.Error,
                e.ElapsedMs,
                Context = string.Join("|", e.Context.Select(kv => $"{kv.Key}={kv.Value}"))
            });
            return System.Text.Json.JsonSerializer.Serialize(exported, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
        }
    }
}
