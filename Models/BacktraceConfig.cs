using System.Text.Json.Serialization;

namespace BattleLuck.Models;

public sealed class BacktraceSettings
{
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; }

    [JsonPropertyName("subdomain")]
    public string Subdomain { get; set; } = "";

    [JsonIgnore]
    public bool IsConfigured => Enabled && !string.IsNullOrWhiteSpace(Subdomain);
}

public sealed record ErrorReportContext
{
    public ulong? AdminSteamId { get; init; }
    public string? CharacterName { get; init; }
    public string? Command { get; init; }
    public string? Action { get; init; }
    public string? ModeId { get; init; }
    public string? EventRunId { get; init; }
    public string? SessionId { get; init; }
    public string? RequestId { get; init; }
    public bool Critical { get; init; }
}

public sealed record ErrorReporterDiagnostics(
    bool Enabled,
    int Queued,
    long Submitted,
    long Dropped,
    long Deduplicated,
    long Retried,
    string? DisabledReason);
