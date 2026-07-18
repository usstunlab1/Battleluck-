using System.Threading;
using BattleLuck.ECS.Events;

namespace BattleLuck.Services.AI;

public sealed class AiGroupProjectMLlmBridge : IDisposable
{
    ProjectMEventRouter? _router;
    ProjectMAiGroupSettings _settings = new();
    readonly ActionManifestService _manifest = new();
    readonly LlmRuntimeActionValidator _runtimeValidator = new();
    readonly Dictionary<string, DateTime> _actionCooldowns = new(StringComparer.OrdinalIgnoreCase);
    DateTime _lastSnapshotUtc = DateTime.MinValue;
    int _inFlight;
    bool _disposed;

    public bool AutoExecuteEnabled { get; private set; }
    public AiGroupProjectMSnapshot? LastSnapshot { get; private set; }
    public AiGroupProjectMDirective? LastDirective { get; private set; }
    public DateTime? LastDirectiveUtc { get; private set; }
    public string LastRawResponse { get; private set; } = "";
    public string LastExecutedAction { get; private set; } = "";
    public string LastExecutionResult { get; private set; } = "";
    public string LastError { get; private set; } = "";
    public int DirectiveCount { get; private set; }
    public int ExecutedCount { get; private set; }
    public int SkippedExecutionCount { get; private set; }

    public void Initialize(ProjectMEventRouter? router)
    {
        _settings = ConfigLoader.LoadAIConfig().ProjectMAiGroup ?? new ProjectMAiGroupSettings();
        AutoExecuteEnabled = _settings.AutoExecute;

        _router = router;
        if (_router == null)
        {
            BattleLuckLogger.Warning("[AiGroupProjectM] Router unavailable; LLM bridge not subscribed.");
            return;
        }

        _router.OnAiGroupProjectMTick += OnAiGroupProjectMTick;
        BattleLuckLogger.Info($"[AiGroupProjectM] LLM bridge subscribed to ProjectM AI updates. enabled={_settings.Enabled} autoExecute={AutoExecuteEnabled} policies={_settings.ActionPolicies.Count}.");
    }

    public void Dispose()
    {
        _disposed = true;
        if (_router != null)
            _router.OnAiGroupProjectMTick -= OnAiGroupProjectMTick;
        _router = null;
    }

    public void SetAutoExecute(bool enabled)
    {
        AutoExecuteEnabled = enabled;
        BattleLuckLogger.Warning($"[AiGroupProjectM] Auto-execute {(enabled ? "enabled" : "disabled")}.");
    }

    public async Task<AiGroupProjectMDirective?> RequestDirectiveAsync(string focus = "", bool executeOverride = false)
    {
        if (_disposed || !VRisingCore.IsReady || !_settings.Enabled)
            return null;

        var snapshot = CaptureSnapshot(string.IsNullOrWhiteSpace(focus) ? "admin.manual" : $"admin.manual:{focus}");
        return await RequestDirectiveForSnapshotAsync(snapshot, focus, executeOverride);
    }

    void OnAiGroupProjectMTick(AiGroupProjectMTickEvent evt)
    {
        if (_disposed || !_settings.Enabled || !VRisingCore.IsReady || BattleLuckPlugin.AIAssistant?.IsEnabled != true)
            return;

        var now = DateTime.UtcNow;
        if (now - _lastSnapshotUtc < TimeSpan.FromSeconds(Math.Clamp(_settings.SnapshotIntervalSeconds, 5, 300)))
            return;

        if (Interlocked.CompareExchange(ref _inFlight, 1, 0) != 0)
            return;

        _lastSnapshotUtc = now;

        AiGroupProjectMSnapshot snapshot;
        try
        {
            snapshot = CaptureSnapshot(evt.SourceSystem);
        }
        catch (Exception ex)
        {
            Interlocked.Exchange(ref _inFlight, 0);
            BattleLuckLogger.Warning($"[AiGroupProjectM] Snapshot failed: {ex.Message}");
            return;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                await RequestDirectiveForSnapshotAsync(snapshot);
            }
            catch (Exception ex)
            {
                BattleLuckLogger.Warning($"[AiGroupProjectM] LLM directive failed: {ex.Message}");
            }
            finally
            {
                Interlocked.Exchange(ref _inFlight, 0);
            }
        });
    }

    async Task<AiGroupProjectMDirective?> RequestDirectiveForSnapshotAsync(
        AiGroupProjectMSnapshot snapshot,
        string focus = "",
        bool executeOverride = false)
    {
        var assistant = BattleLuckPlugin.AIAssistant;
        if (assistant?.IsEnabled != true)
            return null;

        LastSnapshot = snapshot;
        LastError = "";

        var response = await assistant.GenerateAiGroupDirectiveAsync(snapshot, focus);
        if (string.IsNullOrWhiteSpace(response))
        {
            LastError = "LLM returned no ProjectM AI directive.";
            return null;
        }

        LastRawResponse = response.Trim();
        var directive = ParseDirective(LastRawResponse);
        LastDirective = directive;
        LastDirectiveUtc = DateTime.UtcNow;
        DirectiveCount++;

        var compact = JsonSerializer.Serialize(directive);
        BattleLuckLogger.Info($"[AiGroupProjectM] LLM directive: {compact}");
        BattleLuckPlugin.PostToDiscordLogs($"[AiGroupProjectM] {compact}");

        if (ShouldAutoExecute(directive, executeOverride))
            TryExecuteDirective(directive);
        else if (!string.IsNullOrWhiteSpace(directive.Action))
        {
            SkippedExecutionCount++;
            LastExecutionResult = DescribeSkip(directive);
        }

        return directive;
    }

    static AiGroupProjectMDirective ParseDirective(string response)
    {
        try
        {
            var json = ExtractJsonObject(response);
            var directive = JsonSerializer.Deserialize<AiGroupProjectMDirective>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (directive != null)
            {
                directive.Directive = NormalizeDirective(directive.Directive);
                directive.Action = directive.Action?.Trim() ?? "";
                directive.Reason = Trim(directive.Reason, 300);
                directive.Target = Trim(directive.Target, 120);
                directive.Confidence = Math.Clamp(directive.Confidence, 0f, 1f);
                directive.CooldownSeconds = Math.Clamp(directive.CooldownSeconds, 5, 120);
                return directive;
            }
        }
        catch (Exception ex)
        {
            BattleLuckLogger.Warning($"[AiGroupProjectM] Directive JSON parse failed: {ex.Message}");
        }

        return new AiGroupProjectMDirective
        {
            Directive = "observe",
            Reason = Trim(response.Replace('\r', ' ').Replace('\n', ' '), 300),
            Confidence = 0f
        };
    }

    void TryExecuteDirective(AiGroupProjectMDirective directive)
    {
        LastExecutedAction = "";

        if (string.IsNullOrWhiteSpace(directive.Action))
        {
            SkippedExecutionCount++;
            LastExecutionResult = "Skipped: directive contained no action.";
            return;
        }

        var actionName = GetActionName(directive.Action);
        var policy = FindPolicy(actionName);
        if (!IsAllowedAction(actionName, policy))
        {
            SkippedExecutionCount++;
            LastExecutionResult = $"Skipped: action not allow-listed ({directive.Action}).";
            BattleLuckLogger.Warning($"[AiGroupProjectM] {LastExecutionResult}");
            return;
        }

        if (policy?.RequireActiveSession != false && !HasActiveSession())
        {
            SkippedExecutionCount++;
            LastExecutionResult = "Skipped: action requires an active session.";
            return;
        }

        if (!TryBuildActionContext(out var executor, out var context, out var error))
        {
            SkippedExecutionCount++;
            LastExecutionResult = $"Skipped: {error}";
            return;
        }

        var validation = _runtimeValidator.ValidateAction(directive.Action, context);
        if (!validation.Success)
        {
            SkippedExecutionCount++;
            LastExecutionResult = $"Skipped: {validation.Error}";
            BattleLuckLogger.Warning($"[AiGroupProjectM] {LastExecutionResult}");
            return;
        }

        if (!PassesCooldown(actionName, policy, out var cooldownError))
        {
            SkippedExecutionCount++;
            LastExecutionResult = $"Skipped: {cooldownError}";
            return;
        }

        var result = executor.ExecuteViaRuntime(directive.Action, context);
        LastExecutedAction = directive.Action;
        LastExecutionResult = result.Success ? "Executed." : $"Failed: {result.Error}";
        if (result.Success)
            ExecutedCount++;
        else
            SkippedExecutionCount++;

        BattleLuckLogger.Info($"[AiGroupProjectM] Auto-execute {LastExecutedAction}: {LastExecutionResult}");
    }

    // Free-form chat directives intentionally have no execution path. All live
    // actions must originate from the admin preview/approval workflow.

    bool ShouldAutoExecute(AiGroupProjectMDirective directive, bool executeOverride)
    {
        if (string.IsNullOrWhiteSpace(directive.Action))
            return false;

        var actionName = GetActionName(directive.Action);
        var policy = FindPolicy(actionName);
        if (policy?.Enabled == false)
            return false;

        if (!IsSafeAutoExecuteAction(actionName))
            return false;

        var minConfidence = Math.Clamp(policy?.MinConfidence ?? _settings.MinConfidence, 0f, 1f);
        if (directive.Confidence < minConfidence)
            return false;

        // An explicit request may bypass the global toggle, but never catalog risk or
        // approval requirements. Controlled/destructive directives stay preview-only.
        return executeOverride || (AutoExecuteEnabled && (policy?.AutoExecute ?? true));
    }

    bool IsSafeAutoExecuteAction(string actionName)
    {
        var canonical = _manifest.NormalizeActionName(actionName);
        if (!_manifest.Entries.TryGetValue(canonical, out var entry))
            return false;

        return entry.HandlerAvailable &&
               entry.RiskLevel.Equals("safe", StringComparison.OrdinalIgnoreCase) &&
               !entry.RequiresApproval;
    }

    string DescribeSkip(AiGroupProjectMDirective directive)
    {
        var actionName = GetActionName(directive.Action);
        var policy = FindPolicy(actionName);
        if (policy?.Enabled == false)
            return $"Skipped: policy disabled for {actionName}.";

        if (!IsSafeAutoExecuteAction(actionName))
            return $"Skipped: {actionName} is not a catalog-safe, no-approval action.";

        var minConfidence = Math.Clamp(policy?.MinConfidence ?? _settings.MinConfidence, 0f, 1f);
        if (directive.Confidence < minConfidence)
            return $"Skipped: confidence {directive.Confidence:F2} below {minConfidence:F2} for {actionName}.";

        if (!AutoExecuteEnabled)
            return "Skipped: global auto-execute disabled.";

        if (policy?.AutoExecute == false)
            return $"Skipped: per-action auto-execute disabled for {actionName}.";

        return "Skipped: policy did not permit execution.";
    }

    bool IsAllowedAction(string actionName, ProjectMAiGroupActionPolicy? policy)
    {
        if (policy?.Enabled == false)
            return false;

        var allowed = _settings.AllowedActions.Count == 0
            ? new ProjectMAiGroupSettings().AllowedActions
            : _settings.AllowedActions;

        return allowed.Any(pattern => MatchesActionPattern(actionName, pattern)) || policy != null;
    }

    ProjectMAiGroupActionPolicy? FindPolicy(string actionName)
    {
        if (_settings.ActionPolicies.TryGetValue(actionName, out var exact))
            return exact;

        foreach (var kvp in _settings.ActionPolicies)
        {
            if (MatchesActionPattern(actionName, kvp.Key))
                return kvp.Value;
        }

        return null;
    }

    bool PassesCooldown(string actionName, ProjectMAiGroupActionPolicy? policy, out string error)
    {
        error = "";
        var seconds = Math.Clamp(policy?.CooldownSeconds ?? 30, 0, 3600);
        if (seconds <= 0)
            return true;

        if (_actionCooldowns.TryGetValue(actionName, out var last))
        {
            var elapsed = DateTime.UtcNow - last;
            if (elapsed.TotalSeconds < seconds)
            {
                error = $"{actionName} cooldown active ({seconds - elapsed.TotalSeconds:F0}s remaining)";
                return false;
            }
        }

        _actionCooldowns[actionName] = DateTime.UtcNow;
        return true;
    }

    static string GetActionName(string action) => action.Split(':', 2)[0].Trim();

    static bool MatchesActionPattern(string actionName, string pattern)
    {
        pattern = (pattern ?? "").Trim();
        if (string.IsNullOrWhiteSpace(pattern))
            return false;

        if (pattern.EndsWith("*", StringComparison.Ordinal))
            return actionName.StartsWith(pattern[..^1], StringComparison.OrdinalIgnoreCase);

        return actionName.Equals(pattern, StringComparison.OrdinalIgnoreCase);
    }

    static bool HasActiveSession() => BattleLuckPlugin.Session?.ActiveSessions?.Values.Any(s => s.Context != null) == true;

    static bool TryBuildActionContext(out FlowActionExecutor executor, out FlowActionContext context, out string error)
    {
        executor = null!;
        context = null!;
        error = "";

        var active = BattleLuckPlugin.Session?.ActiveSessions?.Values.FirstOrDefault(s => s.Context != null);
        if (active == null)
        {
            error = "no active session";
            return false;
        }

        var player = VRisingCore.GetOnlinePlayers()
            .FirstOrDefault(p => p.Exists() && active.Context.Players.Contains(p.GetSteamId()));
        if (!player.Exists())
            player = VRisingCore.GetOnlinePlayers().FirstOrDefault(p => p.Exists());

        if (!player.Exists())
        {
            error = "no online player entity available for action context";
            return false;
        }

        var playerState = new PlayerStateController();
        executor = new FlowActionExecutor(playerState, BattleLuckPlugin.GameModes);
        var zone = active.Config.Zones.Zones.FirstOrDefault(z => z.Hash == active.Context.ZoneHash);
        context = new FlowActionContext
        {
            PlayerCharacter = player,
            ZoneHash = active.Context.ZoneHash,
            PlayerState = playerState,
            Registry = BattleLuckPlugin.GameModes,
            Config = active.Config,
            Zone = zone,
            GameContext = active.Context
        };
        return true;
    }

    public AiGroupProjectMSnapshot CaptureSnapshot(string sourceSystem)
    {
        var em = VRisingCore.EntityManager;
        var snapshot = new AiGroupProjectMSnapshot
        {
            CapturedUtc = DateTime.UtcNow,
            SourceSystem = sourceSystem,
            ActiveSession = DescribeActiveSession()
        };

        var maxPlayers = Math.Clamp(_settings.MaxPlayers, 1, 40);
        var maxUnits = Math.Clamp(_settings.MaxUnits, 1, 100);
        var players = VRisingCore.GetOnlinePlayers().Take(maxPlayers).ToList();
        snapshot.OnlinePlayerCount = players.Count;
        foreach (var player in players)
        {
            snapshot.Players.Add(new AiGroupPlayerSnapshot
            {
                SteamId = player.GetSteamId(),
                Name = player.GetPlayerName(),
                Entity = FormatEntity(player),
                Position = FormatPosition(player.GetPosition()),
                Health = FormatHealth(player)
            });
        }

        var query = em.CreateEntityQuery(ComponentType.ReadOnly<AggroConsumer>());
        snapshot.AggroConsumerCount = query.CalculateEntityCount();
        var entities = query.ToEntityArray(Allocator.Temp);
        try
        {
            foreach (var entity in entities)
            {
                if (snapshot.Units.Count >= maxUnits)
                    break;

                if (!entity.Exists() || entity.IsPlayer())
                    continue;

                var consumer = entity.Read<AggroConsumer>();
                snapshot.Units.Add(new AiGroupUnitSnapshot
                {
                    Entity = FormatEntity(entity),
                    Prefab = FormatPrefab(entity),
                    Position = FormatPosition(entity.GetPosition()),
                    Level = entity.GetUnitLevel(),
                    Health = FormatHealth(entity),
                    AggroTarget = FormatAggroTarget(consumer),
                    AggroReason = consumer.AggroReason.ToString()
                });
            }
        }
        finally
        {
            entities.Dispose();
            query.Dispose();
        }

        return snapshot;
    }

    static string DescribeActiveSession()
    {
        var sessions = BattleLuckPlugin.Session?.ActiveSessions?.Values;
        var session = sessions?.FirstOrDefault(s => s.Context != null);
        if (session?.Context == null)
            return "none";

        return $"{session.Context.ModeId}/{session.Context.SessionId} players={session.Context.Players.Count} started={session.IsStarted}";
    }

    static string FormatAggroTarget(AggroConsumer consumer)
    {
        try
        {
            var target = consumer.AggroTarget.GetEntityOnServer();
            if (!target.Exists())
                target = consumer.AlertTarget.GetEntityOnServer();

            if (!target.Exists())
                return "none";

            var label = target.IsPlayer()
                ? $"player:{target.GetPlayerName()}:{target.GetSteamId()}"
                : FormatPrefab(target);

            return $"{label}@{FormatEntity(target)}";
        }
        catch
        {
            return "none";
        }
    }

    static string FormatPrefab(Entity entity)
    {
        var guid = entity.GetPrefabGuid();
        if (guid == PrefabGUID.Empty || guid.GuidHash == 0)
            return "unknown";

        return PrefabHelper.GetLivePrefabName(guid) ?? PrefabHelper.GetName(guid) ?? guid.GuidHash.ToString();
    }

    static string FormatHealth(Entity entity)
    {
        if (!entity.TryGetComponent(out Health health))
            return "unknown";

        float value = health.Value;
        float max = health.MaxHealth.Value;
        return $"{value:F0}/{max:F0}";
    }

    static string FormatPosition(float3 position) => $"{position.x:F1},{position.y:F1},{position.z:F1}";

    static string FormatEntity(Entity entity) => $"{entity.Index}:{entity.Version}";

    static string ExtractJsonObject(string text)
    {
        var start = text.IndexOf('{');
        var end = text.LastIndexOf('}');
        if (start < 0 || end <= start)
            return text;
        return text[start..(end + 1)];
    }

    static string NormalizeDirective(string? directive)
    {
        var value = (directive ?? "observe").Trim().ToLowerInvariant();
        return value is "observe" or "pressure_target" or "deaggro" or "hold" or "reposition" or "spawn_support" or "announce"
            ? value
            : "observe";
    }

    static string Trim(string? value, int max)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "";

        var normalized = value.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return normalized.Length <= max ? normalized : normalized[..Math.Max(1, max - 3)] + "...";
    }
}
