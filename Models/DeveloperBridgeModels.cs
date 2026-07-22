using System.Text.Json.Serialization;

namespace BattleLuck.Models;

public sealed class DeveloperManifest
{
    [JsonPropertyName("schema")] public int Schema { get; set; } = 1;
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("usings")] public List<string> Usings { get; set; } = new();
    [JsonPropertyName("namespaces")] public List<string> Namespaces { get; set; } = new();
    [JsonPropertyName("systems")] public List<string> Systems { get; set; } = new();
    [JsonPropertyName("components")] public List<string> Components { get; set; } = new();
    [JsonPropertyName("actions")] public List<string> Actions { get; set; } = new();
    [JsonPropertyName("limits")] public DeveloperLimits Limits { get; set; } = new();
}

public sealed class DeveloperLimits
{
    [JsonPropertyName("maxEntities")] public int MaxEntities { get; set; } = 32;
    [JsonPropertyName("maxActions")] public int MaxActions { get; set; } = 20;
    [JsonPropertyName("maxSnapshotBytes")] public int MaxSnapshotBytes { get; set; } = 262144;
    [JsonPropertyName("maxSimulationSeconds")] public int MaxSimulationSeconds { get; set; } = 30;
}

public sealed record DeveloperAccessGrant(
    string Id, ulong RequesterSteamId, string ManifestId, string ManifestSha256,
    string Capability, DateTimeOffset ExpiresUtc);

public sealed record DeveloperSnapshot(
    int Schema, string Id, string RequestId, string ManifestSha256, DateTimeOffset CapturedUtc,
    string BuildFingerprint, IReadOnlyList<PlayerProjection> Players,
    IReadOnlyList<string> Systems, IReadOnlyList<string> Components, string Sha256);

public sealed record DeveloperPlanStep(
    string Id, string Action, IReadOnlyDictionary<string, string> Parameters, string Expected);

public sealed record DeveloperPlan(
    int Schema, string Id, string RequestId, string ManifestSha256, string Goal,
    IReadOnlyList<DeveloperPlanStep> Steps, IReadOnlyList<string> Assertions,
    IReadOnlyList<string> Risks, IReadOnlyList<string> Cleanup, bool RequiresLiveExecution,
    string Sha256)
{
    public string SnapshotSha256 { get; init; } = "";
}

public sealed record DeveloperSimulationResult(
    string PlanId, bool Success, int PlannedEntities, int ActionCount,
    IReadOnlyList<string> Assertions, IReadOnlyList<string> Errors);
