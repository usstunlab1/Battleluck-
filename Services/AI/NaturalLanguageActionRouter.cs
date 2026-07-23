using System.Globalization;
using System.Text.RegularExpressions;
using BattleLuck.Models;
using BattleLuck.Services.Flow;
using BattleLuck.Services.Runtime;
using Unity.Entities;
using Unity.Mathematics;

namespace BattleLuck.Services.AI;

/// <summary>
/// Resolves imperative <c>.ai &lt;text&gt;</c> requests against the canonical
/// action manifest. The router never asks an LLM to execute ECS code: it builds
/// one fixed catalog action, validates it, binds it to an active session, and
/// requires player-owned confirmation before any mutation runs.
/// </summary>
public static class NaturalLanguageActionRouter
{
    static readonly Regex KeyValuePattern = new(
        "\\b(?<key>[A-Za-z][A-Za-z0-9_.-]*)\\s*=\\s*(?<value>\"[^\"]+\"|'[^']+'|[^\\s|]+)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    static readonly Regex PrefabPattern = new(
        @"\bCHAR_[A-Za-z0-9_]+\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    static readonly HashSet<string> ImperativeTerms = new(StringComparer.OrdinalIgnoreCase)
    {
        "add", "apply", "assign", "build", "change", "clear", "create", "delete",
        "despawn", "disable", "do", "enable", "end", "execute", "give", "grant",
        "kill", "move", "pause", "place", "remove", "rename", "reset", "resume",
        "revive", "run", "set", "spawn", "start", "stop", "summon", "swap",
        "teleport", "update"
    };

    public static bool TryHandle(ulong steamId, string query)
    {
        if (steamId == 0 || string.IsNullOrWhiteSpace(query) || !VRisingCore.IsReady)
            return false;

        var manifest = ActionManifestService.Instance;
        if (!TryResolveEntry(query, manifest, out var entry, out var resolutionError))
        {
            if (!string.IsNullOrWhiteSpace(resolutionError))
            {
                Reply(steamId, resolutionError);
                return true;
            }
            return false;
        }

        if (!IsAdmin(steamId))
        {
            Reply(steamId, $"Action '{entry.Name}' requires server-admin permission.");
            return true;
        }

        if (!entry.HandlerAvailable || !entry.IsServerContractValid)
        {
            Reply(steamId, entry.ServerContractViolationReason ??
                $"Action '{entry.Name}' has no server-side handler.");
            return true;
        }

        var character = ResolveCharacter(steamId);
        var session = ResolveSession(steamId, query);
        if (character == Entity.Null || session == null)
        {
            Reply(steamId, "Catalog actions need one active event session so spawned entities and rollback state have an owner.");
            return true;
        }

        if (!TryBuildAction(query, entry, character, session, out var action, out var buildError))
        {
            Reply(steamId, buildError);
            return true;
        }

        if (!TryCreateExecutionContext(character, session, out var executor, out var context))
        {
            Reply(steamId, "The active event context is not ready for actions.");
            return true;
        }

        var validator = new LlmRuntimeActionValidator(manifest);
        var validation = validator.ValidateAction(action, context, session.Context.SessionId);
        if (!validation.Success)
        {
            Reply(steamId, $"Action preview rejected: {validation.Error}");
            return true;
        }

        var summary = Summarize(action);
        if (!entry.IsMutating && !entry.RequiresApproval &&
            entry.RiskLevel.Equals("safe", StringComparison.OrdinalIgnoreCase))
        {
            var immediate = executor.ExecuteViaRuntime(action, context);
            Reply(steamId, immediate.Success ? $"Executed: {summary}." : $"Action failed: {immediate.Error}");
            return true;
        }

        var boundSessionId = session.Context.SessionId;
        var token = IntentActionConfirmRegistry.Register(
            steamId,
            entry.Name,
            summary,
            () => ExecuteConfirmed(steamId, boundSessionId, action, manifest));

        Reply(steamId,
            $"Preview: {summary}. Confirm with `.ai yes` or `.ai confirm {token}` within 60 seconds; `.ai no` cancels.");
        return true;
    }

    internal static bool TryResolveEntry(
        string query,
        ActionManifestService manifest,
        out ActionManifestEntry entry,
        out string error)
    {
        entry = null!;
        error = string.Empty;
        var candidateText = StripExecutionPrefix(query.Trim());
        var (explicitName, _) = ActionStringParser.Parse(candidateText);
        var canonical = manifest.NormalizeActionName(explicitName);
        if (!string.IsNullOrWhiteSpace(canonical) && manifest.Entries.TryGetValue(canonical, out entry!))
            return true;

        var tokens = candidateText
            .Split(new[] { ' ', '\t', ',', ';', ':', '|', '.', '_', '-' },
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (!tokens.Any(ImperativeTerms.Contains))
            return false;

        var exactAlias = manifest.Entries.Values.FirstOrDefault(item =>
            item.Aliases.Any(alias => candidateText.Contains(alias, StringComparison.OrdinalIgnoreCase)));
        if (exactAlias != null)
        {
            entry = exactAlias;
            return true;
        }

        var matches = manifest.Search(candidateText, 3)
            .Where(match => match.Score >= 10)
            .ToArray();
        if (matches.Length == 0)
            return false;

        if (matches.Length > 1 && matches[0].Score == matches[1].Score &&
            !matches[0].Name.Equals(matches[1].Name, StringComparison.OrdinalIgnoreCase))
        {
            error = "Action description is ambiguous. Try one canonical name: " +
                    string.Join(", ", matches.Take(3).Select(match => match.Name)) + ".";
            return false;
        }

        return manifest.Entries.TryGetValue(matches[0].Name, out entry!);
    }

    static bool TryBuildAction(
        string query,
        ActionManifestEntry entry,
        Entity character,
        ActiveSession session,
        out string action,
        out string error)
    {
        action = string.Empty;
        error = string.Empty;
        var candidateText = StripExecutionPrefix(query.Trim());
        var (explicitName, explicitParameters) = ActionStringParser.Parse(candidateText);
        var explicitCanonical = ActionManifestService.Instance.NormalizeActionName(explicitName);
        var parameters = explicitCanonical.Equals(entry.Name, StringComparison.OrdinalIgnoreCase)
            ? new Dictionary<string, string>(explicitParameters, StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, string>(entry.Defaults, StringComparer.OrdinalIgnoreCase);

        foreach (Match match in KeyValuePattern.Matches(candidateText))
        {
            var key = match.Groups["key"].Value;
            var value = match.Groups["value"].Value.Trim('"', '\'');
            if (entry.Required.Contains(key, StringComparer.OrdinalIgnoreCase) ||
                entry.Optional.Contains(key, StringComparer.OrdinalIgnoreCase) ||
                entry.Defaults.ContainsKey(key))
            {
                parameters[key] = value;
            }
        }

        var wantsRandom = candidateText.Contains("random", StringComparison.OrdinalIgnoreCase) ||
                          candidateText.Contains("randum", StringComparison.OrdinalIgnoreCase);

        if (entry.Name.Equals("spawn.boss", StringComparison.OrdinalIgnoreCase))
        {
            if (!parameters.TryGetValue("prefab", out var prefab) || string.IsNullOrWhiteSpace(prefab))
            {
                var prefabMatch = PrefabPattern.Match(candidateText);
                prefab = prefabMatch.Success ? prefabMatch.Value : PickRandomBossPrefab(entry);
                if (!string.IsNullOrWhiteSpace(prefab))
                    parameters["prefab"] = prefab;
            }

            if (!parameters.ContainsKey("bossId"))
                parameters["bossId"] = $"ai_boss_{Guid.NewGuid():N}"[..15];
        }

        if (wantsRandom &&
            (entry.Name.Equals("spawn.boss", StringComparison.OrdinalIgnoreCase) ||
             entry.Required.Contains("position", StringComparer.OrdinalIgnoreCase) ||
             entry.Optional.Contains("position", StringComparer.OrdinalIgnoreCase)))
        {
            parameters["position"] = FormatPosition(RandomPosition(character, session));
        }

        var missing = entry.Required
            .Where(name => !parameters.TryGetValue(name, out var value) || string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (missing.Length > 0)
        {
            var example = entry.Examples.FirstOrDefault();
            error = $"Action '{entry.Name}' needs: {string.Join(", ", missing)}." +
                    (string.IsNullOrWhiteSpace(example) ? string.Empty : $" Example: .ai execute {example}");
            return false;
        }

        var ordered = entry.Required
            .Concat(parameters.Keys.OrderBy(key => key, StringComparer.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(parameters.ContainsKey)
            .Select(key => $"{key}={parameters[key]}")
            .ToArray();
        action = ordered.Length == 0 ? entry.Name : $"{entry.Name}:{string.Join("|", ordered)}";
        return true;
    }

    static (bool ok, string message) ExecuteConfirmed(
        ulong steamId,
        string sessionId,
        string action,
        ActionManifestService manifest)
    {
        if (!IsAdmin(steamId))
            return (false, "Admin permission was revoked before confirmation.");

        var character = ResolveCharacter(steamId);
        var session = BattleLuckPlugin.Session?.ActiveSessions.Values.FirstOrDefault(active =>
            active.Context.SessionId.Equals(sessionId, StringComparison.Ordinal));
        if (character == Entity.Null || session == null)
            return (false, "The bound event session ended or the player entity changed. Create a new preview.");

        if (!TryCreateExecutionContext(character, session, out var executor, out var context))
            return (false, "The bound event context is no longer ready.");

        var validation = new LlmRuntimeActionValidator(manifest)
            .ValidateAction(action, context, sessionId);
        if (!validation.Success)
            return (false, validation.Error ?? "Action validation failed after confirmation.");

        var result = executor.ExecuteViaRuntime(action, context);
        return result.Success
            ? (true, $"Executed {Summarize(action)}.")
            : (false, result.Error ?? "Action execution failed.");
    }

    static bool TryCreateExecutionContext(
        Entity character,
        ActiveSession session,
        out FlowActionExecutor executor,
        out FlowActionContext context)
    {
        executor = null!;
        context = null!;
        if (!character.Exists() || session.Context == null)
            return false;

        var playerState = BattleLuckPlugin.PlayerState ?? new PlayerStateController();
        var zone = session.Config.Zones.Zones.FirstOrDefault(item => item.Hash == session.Context.ZoneHash);
        executor = new FlowActionExecutor(playerState, BattleLuckPlugin.GameModes);
        context = new FlowActionContext
        {
            PlayerCharacter = character,
            ZoneHash = session.Context.ZoneHash,
            PlayerState = playerState,
            Registry = BattleLuckPlugin.GameModes,
            Config = session.Config,
            Zone = zone,
            GameContext = session.Context
        };
        return true;
    }

    static ActiveSession? ResolveSession(ulong steamId, string query)
    {
        var sessions = BattleLuckPlugin.Session?.ActiveSessions.Values
            .Where(session => session.Context != null)
            .ToArray() ?? Array.Empty<ActiveSession>();
        if (sessions.Length == 0)
            return null;

        var participant = sessions.FirstOrDefault(session => session.Context.Players.Contains(steamId));
        if (participant != null)
            return participant;

        var named = sessions.Where(session =>
            query.Contains(session.Context.ModeId, StringComparison.OrdinalIgnoreCase) ||
            query.Contains(session.Config.DisplayName ?? string.Empty, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (named.Length == 1)
            return named[0];

        return sessions.Length == 1 ? sessions[0] : null;
    }

    static string PickRandomBossPrefab(ActionManifestEntry entry)
    {
        var candidates = entry.Examples
            .Select(example => ActionStringParser.Parse(example).parameters)
            .Where(parameters => parameters.TryGetValue("prefab", out var prefab) &&
                                 !string.IsNullOrWhiteSpace(prefab))
            .Select(parameters => parameters["prefab"])
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(prefab => PrefabHelper.TryGetValidPrefabGuidDeep(prefab, out _))
            .ToArray();
        return candidates.Length == 0 ? string.Empty : candidates[System.Random.Shared.Next(candidates.Length)];
    }

    static float3 RandomPosition(Entity character, ActiveSession session)
    {
        var zone = session.Config.Zones.Zones.FirstOrDefault(item => item.Hash == session.Context.ZoneHash);
        var fallback = character.GetPosition();
        if (zone == null)
            return fallback;

        var center = zone.Position.ToFloat3();
        if (math.lengthsq(center) < 0.0001f)
            center = zone.TeleportSpawn.ToFloat3();
        if (math.lengthsq(center) < 0.0001f)
            center = fallback;

        var radius = Math.Clamp(zone.Radius * 0.65f, 8f, 40f);
        var distance = MathF.Sqrt((float)System.Random.Shared.NextDouble()) * radius;
        var angle = (float)(System.Random.Shared.NextDouble() * Math.PI * 2d);
        return new float3(
            center.x + MathF.Cos(angle) * distance,
            Math.Abs(center.y) > 0.001f ? center.y : fallback.y,
            center.z + MathF.Sin(angle) * distance);
    }

    static string FormatPosition(float3 position) => string.Create(
        CultureInfo.InvariantCulture,
        $"{position.x:F1},{position.y:F1},{position.z:F1}");

    static string StripExecutionPrefix(string value)
    {
        foreach (var prefix in new[] { "execute ", "run ", "do " })
            if (value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return value[prefix.Length..].Trim();
        return value;
    }

    static string Summarize(string action)
    {
        var (name, parameters) = ActionStringParser.Parse(action);
        var visible = parameters
            .Where(kv => !kv.Key.Contains("token", StringComparison.OrdinalIgnoreCase) &&
                         !kv.Key.Contains("password", StringComparison.OrdinalIgnoreCase))
            .Take(6)
            .Select(kv => $"{kv.Key}={kv.Value}");
        return parameters.Count == 0 ? name : $"{name} ({string.Join(", ", visible)})";
    }

    static bool IsAdmin(ulong steamId)
    {
        var character = ResolveCharacter(steamId);
        return character != Entity.Null && FlowController.TryGetUser(character, out var user) && user.IsAdmin;
    }

    static Entity ResolveCharacter(ulong steamId)
    {
        if (!VRisingCore.IsReady)
            return Entity.Null;
        return VRisingCore.GetOnlinePlayers()
            .FirstOrDefault(player => player.Exists() && player.IsPlayer() && player.GetSteamId() == steamId);
    }

    static void Reply(ulong steamId, string message)
    {
        var character = ResolveCharacter(steamId);
        if (character != Entity.Null && FlowController.TryGetUser(character, out var user))
            NotificationHelper.NotifyPlayer(user, $"[AI] {message}");
    }
}
