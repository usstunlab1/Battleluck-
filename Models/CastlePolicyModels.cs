using System.Text.Json.Serialization;

namespace BattleLuck.Models;

// ─────────────────────────────────────────────────────────────────────────────
// Castle Policy Models
//
// BattleLuck-native domain types for the castle policy sub-system. These types
// are intentionally separate from the existing "flow" sub-system (FlowController,
// FlowActionExecutor, flow.json) so that:
//   - Event lifecycle actions remain clearly "flow" objects.
//   - Castle object access permissions remain clearly "policy" objects.
//
// All persistent state uses schema-versioned JSON stores, matching the patterns
// used by ClanTaskService and CastleTileOwnershipService.
// ─────────────────────────────────────────────────────────────────────────────

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum CastleObjectKind
{
    None = 0,
    Storage = 1,
    RestingPoint = 2,
    Gate = 3,
    Structure = 4
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum CastleAccessLevel
{
    Private = 0,
    Public = 1,
    Clan = 2
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum PermissionEffect
{
    Allow = 0,
    Deny = 1
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ScheduleMode
{
    Disabled = 0,
    AllowedHours = 1,
    DeniedHours = 2
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum CostKind
{
    None = 0,
    CurrencyItem = 1
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum QuotaKind
{
    None = 0,
    OperationsPerWindow = 1,
    StacksPerWindow = 2,
    ItemsPerWindow = 3
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum PaymentState
{
    Prepared = 0,
    Reserved = 1,
    Committed = 2,
    Cancelled = 3,
    Expired = 4
}

/// <summary>
/// Stable, persistent identity for a castle object. MUST NOT contain live
/// Entity index or version values (those are runtime handles and are
/// invalid across server restarts and world reloads). The resolver turns a
/// CastleObjectKey into a live Entity after the world is ready.
/// </summary>
public sealed class CastleObjectKey
{
    [JsonPropertyName("ownerSteamId")]
    public ulong OwnerSteamId { get; set; }

    [JsonPropertyName("castleHeartPrefabHash")]
    public int CastleHeartPrefabHash { get; set; }

    [JsonPropertyName("objectPrefabHash")]
    public int ObjectPrefabHash { get; set; }

    [JsonPropertyName("mapIndex")]
    public int MapIndex { get; set; } = -1;

    [JsonPropertyName("localPosition")]
    public QuantizedPosition LocalPosition { get; set; } = new();

    public bool IsValid() => OwnerSteamId != 0 && ObjectPrefabHash != 0;
}

/// <summary>
/// Tile-snapped position. Used to relocate a castle object after the world
/// reloads. Quantized to 2-decimal precision (V Rising build grid is 2.5m).
/// </summary>
public sealed class QuantizedPosition
{
    [JsonPropertyName("x")]
    public float X { get; set; }

    [JsonPropertyName("y")]
    public float Y { get; set; }

    [JsonPropertyName("z")]
    public float Z { get; set; }
}

/// <summary>
/// Allowed or denied hours-of-day window. Overnight windows (e.g. 22-06) work
/// by wrapping around midnight.
/// </summary>
public sealed class CastleHoursWindow
{
    [JsonPropertyName("startHour")]
    public int StartHour { get; set; }

    [JsonPropertyName("endHour")]
    public int EndHour { get; set; }

    public bool Contains(DateTime utc)
    {
        if (StartHour == EndHour) return true;
        var hour = utc.Hour;
        return StartHour < EndHour
            ? hour >= StartHour && hour < EndHour
            : hour >= StartHour || hour < EndHour;
    }

    public bool IsValid()
    {
        if (StartHour < 0 || StartHour > 23) return false;
        if (EndHour < 0 || EndHour > 23) return false;
        return true;
    }
}

/// <summary>
/// Per-policy access schedule. Disabled = always open (modulo other checks).
/// </summary>
public sealed class CastleAccessSchedule
{
    [JsonPropertyName("mode")]
    public ScheduleMode Mode { get; set; } = ScheduleMode.Disabled;

    [JsonPropertyName("windows")]
    public List<CastleHoursWindow> Windows { get; set; } = new();

    public bool Enabled => Mode != ScheduleMode.Disabled && Windows.Count > 0;

    public bool IsWithinWindow(DateTime utc)
    {
        if (!Enabled) return true;
        var matched = Windows.Any(w => w.IsValid() && w.Contains(utc));
        return Mode == ScheduleMode.AllowedHours ? matched : !matched;
    }
}

/// <summary>
/// Per-policy payment configuration. The PaymentTargetKey is a stable locator
/// for the chest (or other sink) that should receive payment.
/// </summary>
public sealed class CastleAccessCost
{
    [JsonPropertyName("kind")]
    public CostKind Kind { get; set; } = CostKind.None;

    [JsonPropertyName("prefabHash")]
    public int PrefabHash { get; set; }

    [JsonPropertyName("prefabName")]
    public string PrefabName { get; set; } = "";

    /// <summary>Per-use cost (gates, resting points) or per-operation cost (storage).</summary>
    [JsonPropertyName("amount")]
    public int Amount { get; set; }

    /// <summary>Per-castle payment target. Required when Kind != None.</summary>
    [JsonPropertyName("paymentTarget")]
    public CastleObjectKey? PaymentTarget { get; set; }

    public bool Enabled => Kind != CostKind.None && Amount > 0 && PrefabHash != 0 && PaymentTarget != null && PaymentTarget.IsValid();
}

/// <summary>
/// Per-policy usage quota. Resets on a configurable window (in hours).
/// </summary>
public sealed class CastleUsageQuota
{
    [JsonPropertyName("kind")]
    public QuotaKind Kind { get; set; } = QuotaKind.None;

    [JsonPropertyName("maxAmount")]
    public int MaxAmount { get; set; }

    [JsonPropertyName("windowHours")]
    public float WindowHours { get; set; } = 24f;

    public bool Enabled => Kind != QuotaKind.None && MaxAmount > 0 && WindowHours > 0f;
}

/// <summary>
/// One explicit allow/deny rule for a single player or clan.
/// </summary>
public sealed class CastlePermissionRule
{
    [JsonPropertyName("subjectSteamId")]
    public ulong SubjectSteamId { get; set; }

    [JsonPropertyName("subjectName")]
    public string SubjectName { get; set; } = "";

    [JsonPropertyName("clanTag")]
    public string ClanTag { get; set; } = "";

    [JsonPropertyName("effect")]
    public PermissionEffect Effect { get; set; } = PermissionEffect.Allow;

    [JsonPropertyName("grantedAtUtc")]
    public DateTime GrantedAtUtc { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// One quota counter for a single player against a single policy.
/// </summary>
public sealed class CastleQuotaCounter
{
    [JsonPropertyName("subjectSteamId")]
    public ulong SubjectSteamId { get; set; }

    [JsonPropertyName("count")]
    public int Count { get; set; }

    [JsonPropertyName("windowStartUtc")]
    public DateTime WindowStartUtc { get; set; } = DateTime.UtcNow;

    [JsonPropertyName("totalCount")]
    public long TotalCount { get; set; }
}

/// <summary>
/// One policy record for one castle object. OwnerSteamId is recorded for
/// fast lookups but is NOT trusted at evaluation time: the service resolves
/// the live entity and re-derives the owner from the castle heart.
/// </summary>
public sealed class CastleObjectPolicy
{
    [JsonPropertyName("policyId")]
    public string PolicyId { get; set; } = "";

    [JsonPropertyName("target")]
    public CastleObjectKey Target { get; set; } = new();

    [JsonPropertyName("ownerSteamId")]
    public ulong OwnerSteamId { get; set; }

    [JsonPropertyName("ownerName")]
    public string OwnerName { get; set; } = "";

    [JsonPropertyName("kind")]
    public CastleObjectKind Kind { get; set; } = CastleObjectKind.None;

    [JsonPropertyName("access")]
    public CastleAccessLevel Access { get; set; } = CastleAccessLevel.Private;

    [JsonPropertyName("schedule")]
    public CastleAccessSchedule Schedule { get; set; } = new();

    [JsonPropertyName("cost")]
    public CastleAccessCost Cost { get; set; } = new();

    [JsonPropertyName("quota")]
    public CastleUsageQuota Quota { get; set; } = new();

    [JsonPropertyName("permissions")]
    public List<CastlePermissionRule> Permissions { get; set; } = new();

    [JsonPropertyName("quotaCounters")]
    public List<CastleQuotaCounter> QuotaCounters { get; set; } = new();

    [JsonPropertyName("createdAtUtc")]
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    [JsonPropertyName("updatedAtUtc")]
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;

    /// <summary>When true, territory-wide apply skips this policy.</summary>
    [JsonPropertyName("excludeFromTerritoryApply")]
    public bool ExcludeFromTerritoryApply { get; set; }

    /// <summary>Optional short label shown in list output.</summary>
    [JsonPropertyName("label")]
    public string Label { get; set; } = "";
}

/// <summary>
/// Request DTO for creating or updating a policy. Ownership is verified
/// independently by the service from the live entity, not from the request.
/// </summary>
public sealed class CastlePolicyRequest
{
    public string PolicyId { get; init; } = "";
    public CastleObjectKey Target { get; init; } = new();
    public CastleObjectKind Kind { get; init; } = CastleObjectKind.None;
    public CastleAccessLevel Access { get; init; } = CastleAccessLevel.Private;
    public CastleAccessSchedule Schedule { get; init; } = new();
    public CastleAccessCost Cost { get; init; } = new();
    public CastleUsageQuota Quota { get; init; } = new();
    public string Label { get; init; } = "";
}

/// <summary>
/// Result of evaluating a policy for a specific requester at a specific time.
/// Constructed only by the service; commands and patches read fields but never
/// mutate state. A decision is "allow" only when the player passes every gate.
/// </summary>
public sealed class CastleAccessDecision
{
    [JsonPropertyName("allowed")]
    public bool Allowed { get; init; }

    [JsonPropertyName("reason")]
    public string Reason { get; init; } = "";

    [JsonPropertyName("cost")]
    public CastleAccessCost? Cost { get; init; }

    [JsonPropertyName("errorLabel")]
    public string? ErrorLabel { get; init; }

    [JsonPropertyName("troubleshooting")]
    public string? Troubleshooting { get; init; }

    public static CastleAccessDecision Allow(CastleAccessCost? cost = null) =>
        new() { Allowed = true, Reason = "ok", Cost = cost };

    public static CastleAccessDecision Deny(string reason, string? label = null, string? help = null) =>
        new() { Allowed = false, Reason = reason, ErrorLabel = label, Troubleshooting = help };
}

/// <summary>
/// In-memory record of an in-flight payment reservation. Persisted counters
/// live on the policy; this is a transient handle returned to a hook so it
/// can later commit or cancel the reservation.
/// </summary>
public sealed class CastlePaymentReservation
{
    public string ReservationId { get; init; } = Guid.NewGuid().ToString("N");
    public string PolicyId { get; init; } = "";
    public ulong RequesterSteamId { get; init; }
    public CastleAccessCost Cost { get; init; } = new();
    public DateTime ReservedAtUtc { get; init; } = DateTime.UtcNow;
    public DateTime ExpiresAtUtc { get; init; } = DateTime.UtcNow.AddSeconds(30);
    public PaymentState State { get; set; } = PaymentState.Prepared;
}
