using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BattleLuck.Models;
using BattleLuck.Services.Runtime;

namespace BattleLuck.Services.DeveloperBridge;

public sealed class AiDeveloperBridge
{
    static readonly HashSet<string> AllowedNamespacePrefixes = new(StringComparer.Ordinal)
    { "ProjectM", "Unity.Entities", "Unity.Transforms", "Unity.Mathematics", "BattleLuck.Services.Npc" };
    static readonly HashSet<string> AllowedCapabilities = new(StringComparer.OrdinalIgnoreCase)
    { "read", "simulate", "execute" };
    static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { PropertyNameCaseInsensitive = true };
    readonly object _gate = new();
    readonly Dictionary<ulong, DeveloperAccessGrant> _grants = new();
    readonly Dictionary<string, DeveloperPlan> _plans = new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<string, DeveloperSnapshot> _snapshots = new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<string, string> _confirmationTokens = new(StringComparer.OrdinalIgnoreCase);

    public OperationResult<DeveloperAccessGrant> Request(ulong requester, string manifestId, string capability)
    {
        if (requester == 0) return OperationResult<DeveloperAccessGrant>.Fail("A connected administrator identity is required.");
        capability = string.IsNullOrWhiteSpace(capability) ? "read" : capability.Trim().ToLowerInvariant();
        if (!AllowedCapabilities.Contains(capability)) return OperationResult<DeveloperAccessGrant>.Fail("Capability must be read, simulate, or execute.");
        var loaded = LoadManifest(manifestId);
        if (!loaded.Success) return OperationResult<DeveloperAccessGrant>.Fail(loaded.Error ?? "Manifest rejected.");
        var (manifest, hash) = loaded.Value;
        var validation = ValidateManifest(manifest);
        if (!validation.Success) return OperationResult<DeveloperAccessGrant>.Fail(validation.Error ?? "Manifest rejected.");
        var grant = new DeveloperAccessGrant("dev_" + Guid.NewGuid().ToString("N"), requester, manifest.Id, hash,
            capability, DateTimeOffset.UtcNow.AddMinutes(10));
        lock (_gate) _grants[requester] = grant;
        return OperationResult<DeveloperAccessGrant>.Ok(grant);
    }

    public OperationResult<DeveloperPlan> Plan(ulong requester, string goal)
    {
        if (!TryGetGrant(requester, out var grant, out var error)) return OperationResult<DeveloperPlan>.Fail(error);
        if (string.IsNullOrWhiteSpace(goal) || goal.Length > 500) return OperationResult<DeveloperPlan>.Fail("Goal must contain 1-500 characters.");
        var loaded = LoadManifest(grant.ManifestId);
        if (!loaded.Success) return OperationResult<DeveloperPlan>.Fail(loaded.Error ?? "Manifest unavailable.");
        var (manifest, hash) = loaded.Value;
        if (!hash.Equals(grant.ManifestSha256, StringComparison.Ordinal)) return OperationResult<DeveloperPlan>.Fail("Manifest changed; request a new grant.");
        var snapshotResult = CaptureSnapshot(grant, manifest);
        if (!snapshotResult.Success) return OperationResult<DeveloperPlan>.Fail(snapshotResult.Error ?? "Snapshot capture failed.");
        var snapshot = snapshotResult.Value!;

        // The bridge deliberately produces catalog-bound templates. Parameters
        // must be supplied/validated before dev-arena execution; no GUID is guessed.
        var steps = manifest.Actions.Take(Math.Min(3, manifest.Limits.MaxActions)).Select((action, index) =>
            new DeveloperPlanStep($"step-{index + 1}", action, new Dictionary<string, string>(),
                $"Validated {action} transition completes inside the tracked dev run")).ToArray();
        var basePlan = new
        {
            schema = 1, requestId = grant.Id, manifestSha256 = hash, snapshotSha256 = snapshot.Sha256, goal = goal.Trim(),
            steps, assertions = new[] { "action_count_within_limit", "all_actions_catalogued", "cleanup_declared" },
            risks = new[] { "NPC actions require validated prefab and target parameters before execution" },
            cleanup = new[] { "dev.entities.destroy", "player.snapshot.restore" }
        };
        var planHash = Sha256(JsonSerializer.Serialize(basePlan, JsonOptions));
        var plan = new DeveloperPlan(1, "plan_" + Guid.NewGuid().ToString("N"), grant.Id, hash, goal.Trim(), steps,
            basePlan.assertions, basePlan.risks, basePlan.cleanup, false, planHash)
            { SnapshotSha256 = snapshot.Sha256 };
        lock (_gate) { _plans[plan.Id] = plan; _snapshots[grant.Id] = snapshot; }
        return OperationResult<DeveloperPlan>.Ok(plan);
    }

    public OperationResult<DeveloperSimulationResult> Simulate(ulong requester, string planId)
    {
        if (!TryGetGrant(requester, out var grant, out var error)) return OperationResult<DeveloperSimulationResult>.Fail(error);
        if (grant.Capability is not ("simulate" or "execute")) return OperationResult<DeveloperSimulationResult>.Fail("A simulate or execute grant is required.");
        if (!TryGetPlan(planId, grant, out var plan, out error)) return OperationResult<DeveloperSimulationResult>.Fail(error);
        var errors = new List<string>();
        var loaded = LoadManifest(grant.ManifestId);
        if (!loaded.Success) errors.Add(loaded.Error ?? "Manifest unavailable.");
        else
        {
            var manifest = loaded.Value.Manifest;
            if (plan.Steps.Count > manifest.Limits.MaxActions) errors.Add("Plan exceeds maxActions.");
            foreach (var step in plan.Steps)
            {
                var canonical = ActionManifestService.Instance.NormalizeActionName(step.Action);
                if (!manifest.Actions.Contains(canonical, StringComparer.OrdinalIgnoreCase)) errors.Add($"Action '{step.Action}' is outside the manifest.");
                if (!ActionManifestService.Instance.IsKnown(canonical)) errors.Add($"Action '{step.Action}' is not in the canonical catalog.");
            }
        }
        return OperationResult<DeveloperSimulationResult>.Ok(new DeveloperSimulationResult(plan.Id, errors.Count == 0, 0,
            plan.Steps.Count, plan.Assertions, errors));
    }

    public OperationResult<string> PrepareExecution(ulong requester, string planId)
    {
        if (!TryGetGrant(requester, out var grant, out var error)) return OperationResult<string>.Fail(error);
        if (!grant.Capability.Equals("execute", StringComparison.OrdinalIgnoreCase)) return OperationResult<string>.Fail("An execute grant is required.");
        if (!TryGetPlan(planId, grant, out var plan, out error)) return OperationResult<string>.Fail(error);
        var simulation = Simulate(requester, planId);
        if (!simulation.Success || simulation.Value?.Success != true)
            return OperationResult<string>.Fail(string.Join("; ", simulation.Value?.Errors ?? new[] { simulation.Error ?? "Simulation failed." }));
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(4)).ToLowerInvariant();
        lock (_gate) _confirmationTokens[$"{requester}:{plan.Id}:{plan.Sha256}"] = token;
        return OperationResult<string>.Ok(token);
    }

    public OperationResult ExecuteDevArena(ulong requester, Unity.Entities.Entity character, string planId, string token)
    {
        if (!TryGetGrant(requester, out var grant, out var error)) return OperationResult.Fail(error);
        if (!grant.Capability.Equals("execute", StringComparison.OrdinalIgnoreCase)) return OperationResult.Fail("An execute grant is required.");
        if (!TryGetPlan(planId, grant, out var plan, out error)) return OperationResult.Fail(error);
        var key = $"{requester}:{plan.Id}:{plan.Sha256}";
        lock (_gate)
        {
            if (!_confirmationTokens.Remove(key, out var expected) || !CryptographicOperations.FixedTimeEquals(
                    Encoding.UTF8.GetBytes(expected), Encoding.UTF8.GetBytes(token ?? "")))
                return OperationResult.Fail("Invalid or consumed confirmation token.");
        }
        var dev = BattleLuckPlugin.DevSession;
        if (dev == null || !dev.IsDevSession(requester)) return OperationResult.Fail("Enter the isolated dev arena first with .dev.enter.");
        foreach (var step in plan.Steps)
        {
            if (step.Parameters.Count == 0)
                return OperationResult.Fail($"Step '{step.Id}' has no validated parameters; execution stopped before mutation.");
            var action = step.Action + ":" + string.Join("|", step.Parameters.Select(pair => $"{pair.Key}={pair.Value}"));
            var validation = new LlmRuntimeActionValidator().ValidateAction(action, sessionIdOverride: $"dev_{requester}");
            if (!validation.Success) return validation;
            var result = dev.ExecuteDevAction(character, requester, action);
            if (!result.Success) return result;
        }
        return OperationResult.Ok();
    }

    public void Revoke(ulong requester)
    {
        lock (_gate)
        {
            if (_grants.Remove(requester, out var revoked))
            {
                _snapshots.Remove(revoked.Id);
                foreach (var planId in _plans.Values.Where(plan => plan.RequestId == revoked.Id)
                             .Select(plan => plan.Id).ToArray())
                    _plans.Remove(planId);
            }
            foreach (var key in _confirmationTokens.Keys.Where(key => key.StartsWith(requester + ":", StringComparison.Ordinal)).ToArray())
                _confirmationTokens.Remove(key);
        }
    }

    static OperationResult<DeveloperSnapshot> CaptureSnapshot(DeveloperAccessGrant grant, DeveloperManifest manifest)
    {
        try
        {
            // This method is called synchronously by the chat command on the server
            // main thread. PlayerDirectory has already disposed every native query;
            // only bounded immutable projections cross into planning.
            var players = BattleLuckPlugin.PlayerDirectory?.GetOnlineProjections(limit: manifest.Limits.MaxEntities)
                          ?? Array.Empty<PlayerProjection>();
            var build = $"battleluck:{BattleLuckPluginInfo.PluginVersion}|projectm:{typeof(ProjectM.Network.User).Assembly.GetName().Version}";
            var basis = new
            {
                schema = 1, requestId = grant.Id, manifestSha256 = grant.ManifestSha256,
                buildFingerprint = build, players,
                systems = manifest.Systems.Take(32).OrderBy(value => value, StringComparer.Ordinal).ToArray(),
                components = manifest.Components.Take(64).OrderBy(value => value, StringComparer.Ordinal).ToArray()
            };
            var json = JsonSerializer.Serialize(basis, JsonOptions);
            if (Encoding.UTF8.GetByteCount(json) > manifest.Limits.MaxSnapshotBytes)
                return OperationResult<DeveloperSnapshot>.Fail("Snapshot exceeds the manifest byte limit.");
            var hash = Sha256(json);
            return OperationResult<DeveloperSnapshot>.Ok(new DeveloperSnapshot(1, "snapshot_" + Guid.NewGuid().ToString("N"),
                grant.Id, grant.ManifestSha256, DateTimeOffset.UtcNow, build, players,
                basis.systems, basis.components, hash));
        }
        catch (Exception ex) { return OperationResult<DeveloperSnapshot>.Fail($"Snapshot capture failed: {ex.Message}"); }
    }

    bool TryGetGrant(ulong requester, out DeveloperAccessGrant grant, out string error)
    {
        lock (_gate)
        {
            if (_grants.TryGetValue(requester, out grant!) && grant.ExpiresUtc > DateTimeOffset.UtcNow)
            { error = ""; return true; }
            _grants.Remove(requester);
        }
        grant = null!; error = "No active developer access grant. Use .bl admin dev request npc-simulation <capability>."; return false;
    }

    bool TryGetPlan(string id, DeveloperAccessGrant grant, out DeveloperPlan plan, out string error)
    {
        lock (_gate)
        {
            if (_plans.TryGetValue(id, out plan!) && plan.RequestId == grant.Id && plan.ManifestSha256 == grant.ManifestSha256)
            { error = ""; return true; }
        }
        plan = null!; error = "Plan was not found or no longer matches the active grant."; return false;
    }

    static OperationResult<(DeveloperManifest Manifest, string Hash)> LoadManifest(string requestedId)
    {
        var id = (requestedId ?? "").Trim().ToLowerInvariant();
        if (id.Length is < 1 or > 64 || id.Any(ch => !char.IsLetterOrDigit(ch) && ch is not '-' and not '_'))
            return OperationResult<(DeveloperManifest, string)>.Fail("Manifest id contains invalid characters.");
        try
        {
            var assembly = typeof(AiDeveloperBridge).Assembly;
            var suffix = $"config.BattleLuck.developer.{id}.json";
            var resource = assembly.GetManifestResourceNames().FirstOrDefault(name => name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase));
            string json;
            if (resource != null)
            {
                using var stream = assembly.GetManifestResourceStream(resource)!;
                using var reader = new StreamReader(stream, Encoding.UTF8);
                json = reader.ReadToEnd();
            }
            else
            {
                var root = Path.GetFullPath(Path.Combine(ConfigLoader.ConfigRoot, "developer"));
                var path = Path.GetFullPath(Path.Combine(root, id + ".json"));
                if (!path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || !File.Exists(path))
                    return OperationResult<(DeveloperManifest, string)>.Fail($"Manifest '{id}' was not found.");
                json = File.ReadAllText(path);
            }
            if (Encoding.UTF8.GetByteCount(json) > 262144) return OperationResult<(DeveloperManifest, string)>.Fail("Manifest exceeds 256 KiB.");
            var manifest = JsonSerializer.Deserialize<DeveloperManifest>(json, JsonOptions);
            return manifest == null ? OperationResult<(DeveloperManifest, string)>.Fail("Manifest is empty.") :
                OperationResult<(DeveloperManifest, string)>.Ok((manifest, Sha256(json)));
        }
        catch (Exception ex) { return OperationResult<(DeveloperManifest, string)>.Fail($"Manifest rejected: {ex.Message}"); }
    }

    static OperationResult ValidateManifest(DeveloperManifest manifest)
    {
        if (manifest.Schema != 1) return OperationResult.Fail("Manifest schema must be 1.");
        if (manifest.Namespaces.Concat(manifest.Usings).Any(value => !AllowedNamespacePrefixes.Any(prefix =>
                value.Equals(prefix, StringComparison.Ordinal) || value.StartsWith(prefix + ".", StringComparison.Ordinal))))
            return OperationResult.Fail("Manifest contains a namespace outside the embedded allowlist.");
        if (manifest.Systems.Count > 32 || manifest.Components.Count > 64 || manifest.Actions.Count > 50)
            return OperationResult.Fail("Manifest catalog limits exceeded.");
        if (manifest.Actions.Any(action => !ActionManifestService.Instance.IsKnown(action)))
            return OperationResult.Fail("Manifest references an unknown action.");
        if (manifest.Limits.MaxEntities is < 1 or > 128 || manifest.Limits.MaxActions is < 1 or > 50 ||
            manifest.Limits.MaxSnapshotBytes is < 1024 or > 1048576 || manifest.Limits.MaxSimulationSeconds is < 1 or > 60)
            return OperationResult.Fail("Manifest limits are outside server safety bounds.");
        return OperationResult.Ok();
    }

    static string Sha256(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}
