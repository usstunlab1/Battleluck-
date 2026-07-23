using System.Text;
using System.Text.Json;

namespace BattleLuck.Services.Runtime
{
    /// <summary>
    /// In-memory runtime snapshots for AI/MCP diagnostics.
    /// Entity-level capture can be added later; session and flow state are live.
    /// </summary>
    public class SnapshotServiceImpl : ISnapshotService
    {
        private readonly ConcurrentDictionary<string, RuntimeSnapshotDto> _snapshots = new();
        private readonly ISessionRuntimeService? _sessions;

        public SnapshotServiceImpl(ISessionRuntimeService? sessions = null)
        {
            _sessions = sessions;
        }

        public async Task<RuntimeSnapshotDto> CaptureSnapshotAsync(string? sessionId = null)
        {
            var requestedSessionId = string.IsNullOrWhiteSpace(sessionId) ? null : sessionId;
            SessionStateDto? session = null;

            if (_sessions != null)
            {
                if (requestedSessionId != null)
                    session = await _sessions.GetSessionAsync(requestedSessionId);

                session ??= await _sessions.GetCurrentSessionAsync();
            }

            var effectiveSessionId = session?.Id ?? requestedSessionId ?? "current";
            var flow = _sessions != null && !string.IsNullOrWhiteSpace(effectiveSessionId)
                ? await _sessions.GetFlowStateAsync(effectiveSessionId)
                : null;

            var snapshot = new RuntimeSnapshotDto
            {
                Id = Guid.NewGuid().ToString(),
                SessionId = effectiveSessionId,
                CapturedUtc = DateTime.UtcNow,
                Entities = new List<EntitySnapshotDto>(),
                SessionState = session ?? new SessionStateDto { Id = effectiveSessionId, ModeId = "unknown", Phase = SessionPhaseDto.Failed },
                FlowState = flow ?? new FlowStateDto { FlowName = session?.ModeId ?? "unknown", CurrentState = "unknown" },
                GlobalState = new Dictionary<string, object>
                {
                    ["source"] = "runtime-services",
                    ["sessionFound"] = session != null,
                    ["entityCapture"] = "not-connected"
                }
            };

            snapshot.SizeBytes = Encoding.UTF8.GetByteCount(JsonSerializer.Serialize(snapshot));
            _snapshots[snapshot.Id] = snapshot;
            return snapshot;
        }

        public Task<RuntimeSnapshotDto?> GetSnapshotAsync(string snapshotId)
        {
            _snapshots.TryGetValue(snapshotId, out var snapshot);
            return Task.FromResult(snapshot);
        }

        public Task<List<RuntimeSnapshotDto>> ListSnapshotsAsync(string? sessionId = null)
        {
            var snapshots = _snapshots.Values
                .Where(s => sessionId == null || s.SessionId == sessionId)
                .ToList();
            return Task.FromResult(snapshots);
        }

        public Task<SnapshotDiffDto> DiffSnapshotsAsync(string fromSnapshotId, string toSnapshotId)
        {
            var diff = new SnapshotDiffDto
            {
                FromSnapshotId = fromSnapshotId,
                ToSnapshotId = toSnapshotId,
                GeneratedUtc = DateTime.UtcNow,
                EntityChanges = new List<EntityChangeDto>(),
                StateChanges = new List<StateChangeDto>(),
                FlowChanges = new List<FlowChangeDto>(),
                Summary = new SummaryStatisticsDto()
            };

            if (!_snapshots.TryGetValue(fromSnapshotId, out var from) ||
                !_snapshots.TryGetValue(toSnapshotId, out var to))
            {
                diff.StateChanges.Add(new StateChangeDto
                {
                    Path = "snapshot",
                    OldValue = _snapshots.ContainsKey(fromSnapshotId) ? fromSnapshotId : $"missing:{fromSnapshotId}",
                    NewValue = _snapshots.ContainsKey(toSnapshotId) ? toSnapshotId : $"missing:{toSnapshotId}"
                });
                diff.Summary.StateChanges = diff.StateChanges.Count;
                return Task.FromResult(diff);
            }

            AddChange(diff, "session.id", from.SessionState.Id, to.SessionState.Id);
            AddChange(diff, "session.modeId", from.SessionState.ModeId, to.SessionState.ModeId);
            AddChange(diff, "session.phase", from.SessionState.Phase, to.SessionState.Phase);
            AddChange(diff, "session.playerCount", from.SessionState.PlayerCount, to.SessionState.PlayerCount);
            AddChange(diff, "session.currentBoss", from.SessionState.CurrentBoss, to.SessionState.CurrentBoss);
            AddChange(diff, "flow.name", from.FlowState.FlowName, to.FlowState.FlowName);
            AddChange(diff, "flow.currentState", from.FlowState.CurrentState, to.FlowState.CurrentState);

            if (!string.Equals(from.FlowState.CurrentState, to.FlowState.CurrentState, StringComparison.OrdinalIgnoreCase))
            {
                diff.FlowChanges.Add(new FlowChangeDto
                {
                    FlowName = to.FlowState.FlowName,
                    OldState = from.FlowState.CurrentState,
                    NewState = to.FlowState.CurrentState
                });
            }

            diff.Summary.StateChanges = diff.StateChanges.Count;
            diff.Summary.FlowTransitions = diff.FlowChanges.Count;
            return Task.FromResult(diff);
        }

        public Task<bool> RollbackToSnapshotAsync(string snapshotId)
        {
            if (!_snapshots.ContainsKey(snapshotId))
                return Task.FromResult(false);

            BattleLuckPlugin.LogWarning("[SnapshotService] Rollback requested, but only capture/diff is wired. No runtime state was mutated.");
            return Task.FromResult(false);
        }

        public Task<bool> DeleteSnapshotAsync(string snapshotId)
        {
            return Task.FromResult(_snapshots.TryRemove(snapshotId, out _));
        }

        static void AddChange(SnapshotDiffDto diff, string path, object? oldValue, object? newValue)
        {
            if (Equals(oldValue, newValue))
                return;

            diff.StateChanges.Add(new StateChangeDto
            {
                Path = path,
                OldValue = oldValue,
                NewValue = newValue
            });
        }
    }
}
