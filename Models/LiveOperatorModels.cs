namespace BattleLuck.Models;

public sealed class CatalogActionSearchResult
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("category")]
    public string Category { get; set; } = "";

    [JsonPropertyName("riskLevel")]
    public string RiskLevel { get; set; } = "controlled";

    [JsonPropertyName("requiresApproval")]
    public bool RequiresApproval { get; set; } = true;

    [JsonPropertyName("examples")]
    public List<string> Examples { get; set; } = new();

    [JsonPropertyName("score")]
    public int Score { get; set; }
}

public sealed class OperatorProposal
{
    [JsonPropertyName("kind")]
    public string Kind { get; set; } = "event_config";

    [JsonPropertyName("operationId")]
    public string OperationId { get; set; } = "";

    [JsonPropertyName("modeId")]
    public string ModeId { get; set; } = "";

    [JsonPropertyName("sessionId")]
    public string SessionId { get; set; } = "";

    [JsonPropertyName("riskLevel")]
    public string RiskLevel { get; set; } = "controlled";

    [JsonPropertyName("requestedBy")]
    public ulong RequestedBy { get; set; }

    [JsonPropertyName("request")]
    public string Request { get; set; } = "";

    [JsonPropertyName("reason")]
    public string Reason { get; set; } = "";

    [JsonPropertyName("actions")]
    public List<string> Actions { get; set; } = new();

    [JsonPropertyName("jsonDiff")]
    public string JsonDiff { get; set; } = "";

    [JsonPropertyName("eventPath")]
    public string EventPath { get; set; } = "";

    [JsonPropertyName("backupPath")]
    public string BackupPath { get; set; } = "";

    [JsonIgnore]
    public string OriginalJson { get; set; } = "";

    [JsonIgnore]
    public string ProposedJson { get; set; } = "";

    [JsonPropertyName("createdAtUtc")]
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    [JsonPropertyName("expiresAtUtc")]
    public DateTime ExpiresAtUtc { get; set; } = DateTime.UtcNow.AddMinutes(30);

    [JsonIgnore]
    public bool IsExpired => DateTime.UtcNow > ExpiresAtUtc;
}
