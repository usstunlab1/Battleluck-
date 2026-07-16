using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BattleLuck.Models;

namespace BattleLuck.Services.Runtime
{
    /// <summary>
    /// Real implementation of session runtime service.
    /// </summary>
    public class SessionRuntimeServiceReal : ISessionRuntimeService
    {
        private SessionController? _sessionController;
        private readonly object _lock = new();

        /// <summary>
        /// Connects to the global SessionController singleton.
        /// </summary>
        public void ConnectToSessionController(SessionController? controller)
        {
            lock (_lock)
            {
                _sessionController = controller;
            }
        }

        public Task<SessionStateDto?> GetCurrentSessionAsync()
        {
            lock (_lock)
            {
                var session = _sessionController?.ActiveSessions.Values.FirstOrDefault();
                if (session?.Context == null)
                    return Task.FromResult<SessionStateDto?>(null);

                return Task.FromResult<SessionStateDto?>(MapSessionToDto(session.Context));
            }
        }

        public Task<SessionStateDto?> GetSessionAsync(string sessionId)
        {
            lock (_lock)
            {
                var session = _sessionController?.ActiveSessions.Values
                    .FirstOrDefault(s => s.Context?.SessionId == sessionId);

                if (session?.Context == null)
                    return Task.FromResult<SessionStateDto?>(null);

                return Task.FromResult<SessionStateDto?>(MapSessionToDto(session.Context));
            }
        }

        public Task<List<SessionStateDto>> ListSessionsAsync()
        {
            lock (_lock)
            {
                var sessions = _sessionController?.ActiveSessions.Values
                    .Where(s => s.Context != null)
                    .Select(s => MapSessionToDto(s.Context))
                    .ToList() ?? new List<SessionStateDto>();

                return Task.FromResult(sessions);
            }
        }

        public Task<PlayerStatsDto?> GetPlayerStatsAsync(string steamId, string sessionId)
        {
            lock (_lock)
            {
                if (!ulong.TryParse(steamId, out var playerSteamId))
                    return Task.FromResult<PlayerStatsDto?>(null);

                var session = _sessionController?.ActiveSessions.Values
                    .FirstOrDefault(s => s.Context?.SessionId == sessionId);

                if (session?.Context == null)
                    return Task.FromResult<PlayerStatsDto?>(null);

                return Task.FromResult<PlayerStatsDto?>(new PlayerStatsDto
                {
                    SteamId = steamId,
                    Score = session.Context.Scores.GetPlayerScore(playerSteamId),
                    TeamId = session.Context.Teams.TryGetValue(playerSteamId, out var team) ? team : null
                });
            }
        }

        public Task<List<PlayerLeaderboardEntryDto>> GetLeaderboardAsync(string sessionId)
        {
            lock (_lock)
            {
                var session = _sessionController?.ActiveSessions.Values
                    .FirstOrDefault(s => s.Context?.SessionId == sessionId);

                if (session?.Context == null)
                    return Task.FromResult(new List<PlayerLeaderboardEntryDto>());

                var leaderboard = session.Context.Scores.GetLeaderboard()
                    .Select((steamId, rank) => new PlayerLeaderboardEntryDto
                    {
                        SteamId = steamId.ToString(),
                        Score = session.Context.Scores.GetPlayerScore(steamId),
                        Rank = rank + 1
                    })
                    .ToList();

                return Task.FromResult(leaderboard);
            }
        }

        public Task<FlowStateDto?> GetFlowStateAsync(string sessionId)
        {
            lock (_lock)
            {
                var session = _sessionController?.ActiveSessions.Values
                    .FirstOrDefault(s => s.Context?.SessionId == sessionId);

                if (session?.Context == null)
                    return Task.FromResult<FlowStateDto?>(null);

                return Task.FromResult<FlowStateDto?>(new FlowStateDto
                {
                    FlowName = session.Context.ModeId ?? "",
                    CurrentState = session.IsStarted ? (session.Context.State.ContainsKey("result") ? "completed" : "active") : "waiting",
                    LastTransitionUtc = session.Context.StartTimeUtc
                });
            }
        }

        public Task<bool> EmitSessionEventAsync(string sessionId, string eventType, Dictionary<string, object>? data = null)
        {
            lock (_lock)
            {
                var session = _sessionController?.ActiveSessions.Values
                    .FirstOrDefault(s => s.Context?.SessionId == sessionId);

                if (session?.Context?.Broadcast != null)
                {
                    var message = data != null && data.TryGetValue("message", out var msgObj)
                        ? msgObj?.ToString() ?? $"{eventType} event triggered"
                        : $"{eventType} event triggered";
                    session.Context.Broadcast(message);
                    return Task.FromResult(true);
                }
                return Task.FromResult(false);
            }
        }

        public Task<List<ActionDefinitionDto>> ListActionsAsync()
        {
            var manifest = new ActionManifestService();
            var actions = manifest.Entries.Values
                .OrderBy(e => e.Category, StringComparer.OrdinalIgnoreCase)
                .ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
                .Select(MapActionManifestEntry)
                .ToList();

            return Task.FromResult(actions);
        }

        public Task<List<ActionDefinitionDto>> ListActionsForModeAsync(string modeId)
        {
            return ListActionsAsync();
        }

        public Task<ActionDefinitionDto?> GetActionAsync(string actionName)
        {
            var manifest = new ActionManifestService();
            var normalized = manifest.NormalizeActionName(actionName);
            return manifest.Entries.TryGetValue(normalized, out var entry)
                ? Task.FromResult<ActionDefinitionDto?>(MapActionManifestEntry(entry))
                : Task.FromResult<ActionDefinitionDto?>(null);
        }

        public Task<ActionResultDto> ExecuteActionAsync(string actionName, string steamId, string sessionId, Dictionary<string, string> parameters)
        {
            // This method would be called from the main thread via MCP
            // For now, we just record the request - actual execution happens via FlowActionExecutor
            return Task.FromResult(new ActionResultDto
            {
                Success = false,
                Error = "Action execution via MCP requires main-thread entity resolution",
                Message = $"Action '{actionName}' queued for execution (requires player context)",
                ExecutedUtc = DateTime.UtcNow
            });
        }

        private static SessionStateDto MapSessionToDto(GameModeContext ctx)
        {
            var elapsedSeconds = ctx.ElapsedSeconds;

            var phase = SessionPhaseDto.InProgress;
            if (ctx.State?.ContainsKey("result") == true)
                phase = SessionPhaseDto.Completed;
            else if (!ctx.IsTimeUp && ctx.StartTimeUtc == DateTime.UtcNow)
                phase = SessionPhaseDto.Initializing;

            return new SessionStateDto
            {
                Id = ctx.SessionId ?? "",
                ModeId = ctx.ModeId ?? "",
                StartedUtc = ctx.StartTimeUtc,
                ElapsedSeconds = elapsedSeconds,
                Phase = phase,
                PlayerCount = ctx.Players.Count,
                Metadata = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
                {
                    ["zoneHash"] = ctx.ZoneHash,
                    ["timeLimitSeconds"] = ctx.TimeLimitSeconds,
                    ["isTimeUp"] = ctx.IsTimeUp
                }
            };
        }

        private static ActionDefinitionDto MapActionManifestEntry(ActionManifestEntry entry) => new()
        {
            Name = entry.Name,
            Category = entry.Category,
            Description = string.IsNullOrWhiteSpace(entry.Description)
                ? $"Execute {entry.Name}"
                : entry.Description,
            ColoredLabel = entry.Name,
            DefaultPoints = 0,
            IsEnabled = entry.HandlerAvailable
        };
    }
}
