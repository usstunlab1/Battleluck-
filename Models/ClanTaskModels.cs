namespace BattleLuck.Models;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ClanTaskScope
{
    World,
    Event
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ClanTaskObjectiveType
{
    Manual,
    BossKill,
    GatherItem
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ClanTaskStatus
{
    Active,
    Completed,
    Cancelled,
    Expired
}

public sealed class ClanTaskObjective
{
    public ClanTaskObjectiveType Type { get; set; } = ClanTaskObjectiveType.Manual;
    public int PrefabGuidHash { get; set; }
    public string PrefabName { get; set; } = "";
}

public sealed class ClanTask
{
    public string TaskId { get; set; } = "";
    public string Description { get; set; } = "";
    public string ClanId { get; set; } = "";
    public HashSet<ulong> AssignedSteamIds { get; set; } = new();
    public ClanTaskScope Scope { get; set; } = ClanTaskScope.World;
    public string EventId { get; set; } = "";
    public string SessionId { get; set; } = "";
    public ClanTaskObjective Objective { get; set; } = new();
    public int TargetAmount { get; set; }
    public int CurrentAmount { get; set; }
    public ClanTaskStatus Status { get; set; } = ClanTaskStatus.Active;
    public Dictionary<ulong, int> Contributions { get; set; } = new();
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? ExpiresAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public int RewardPoints { get; set; }
}

public sealed class ClanTaskStore
{
    // v2 binds every event task to one exact mode/session pair.
    public int SchemaVersion { get; set; } = 2;
    public List<ClanTask> Tasks { get; set; } = new();
}

public sealed class CreateClanTaskRequest
{
    public string TaskId { get; init; } = "";
    public string Description { get; init; } = "";
    public string ClanId { get; init; } = "";
    public IReadOnlyCollection<ulong> AssignedSteamIds { get; init; } = Array.Empty<ulong>();
    public ClanTaskScope Scope { get; init; } = ClanTaskScope.World;
    public string EventId { get; init; } = "";
    public string SessionId { get; init; } = "";
    public ClanTaskObjectiveType ObjectiveType { get; init; } = ClanTaskObjectiveType.Manual;
    public int PrefabGuidHash { get; init; }
    public string PrefabName { get; init; } = "";
    public int TargetAmount { get; init; }
    public DateTime? ExpiresAtUtc { get; init; }
    public int RewardPoints { get; init; }
}
