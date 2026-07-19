// Services/Runtime/ActionCapabilityMatrix.cs
// Per-action metadata used by the validation pipeline to apply permission
// and safety rules without hard-coding them at each call site.
//
// Pure C# — no BepInEx/VRising/Unity references.

namespace BattleLuck.Services.Runtime;

// ── Permission flags ──────────────────────────────────────────────────────────

/// <summary>
/// Bitmask of sources that are permitted to invoke an action.
/// Combine flags to allow multiple sources:
///   e.g. <c>Admin | AI</c> permits admins and the AI assistant.
/// </summary>
[Flags]
public enum ActionSourcePermissions
{
    None      = 0,
    Admin     = 1 << 0,
    Player    = 1 << 1,
    AI        = 1 << 2,
    Webhook   = 1 << 3,
    MCP       = 1 << 4,
    EventRuntime = 1 << 5,
    DevConsole = 1 << 6,
    System    = 1 << 7,

    /// <summary>Any authenticated source.</summary>
    AllAuthenticated = Admin | Webhook | MCP | System,

    /// <summary>Only privileged automation sources (no direct player input).</summary>
    AutomationOnly = AI | Webhook | MCP | EventRuntime | System,

    /// <summary>Every source.</summary>
    All = ~None,
}

// ── Session phase allowance ───────────────────────────────────────────────────

/// <summary>
/// Bitmask of <see cref="BattleLuck.Models.SessionPhase"/> values in which an
/// action may be executed. Stored as an int so this file has no Models reference.
/// Populated from <see cref="SessionPhaseAllowance"/> helpers.
/// </summary>
[Flags]
public enum SessionPhaseAllowance
{
    None       = 0,
    Preparing  = 1 << 0,
    ArenaReady = 1 << 1,
    Warmup     = 1 << 2,
    Active     = 1 << 3,
    Ending     = 1 << 4,
    Completed  = 1 << 5,
    Failed     = 1 << 6,
    NoSession  = 1 << 7,   // Global actions that don't require a session.

    /// <summary>All live in-match phases.</summary>
    AnyLive = Warmup | Active,

    /// <summary>All pre-match setup phases.</summary>
    AnySetup = Preparing | ArenaReady,

    /// <summary>Any phase where a session exists.</summary>
    AnySession = Preparing | ArenaReady | Warmup | Active | Ending,

    /// <summary>Any phase including no-session global context.</summary>
    Any = ~None,
}

// ── Capability descriptor ─────────────────────────────────────────────────────

/// <summary>
/// Full capability descriptor for a registered action.
/// Registered in <see cref="CapabilityRegistry"/>;
/// consumed by <see cref="ActionValidationPipeline"/>.
/// </summary>
public sealed class ActionCapabilityDescriptor
{
    /// <summary>Canonical action name (must match the key in the registry).</summary>
    public string ActionName { get; init; } = string.Empty;

    /// <summary>Human-readable description used in admin confirmations and docs.</summary>
    public string Description { get; init; } = string.Empty;

    // ── Mutation characteristics ──────────────────────────────────────────────

    /// <summary>True when the action changes server/world state.</summary>
    public bool IsMutating { get; init; } = true;

    /// <summary>True when the action can be undone with a compensating call.</summary>
    public bool IsReversible { get; init; } = false;

    /// <summary>True when running the action N times produces the same result as running it once.</summary>
    public bool IsIdempotent { get; init; } = false;

    // ── Risk and permissions ──────────────────────────────────────────────────

    public ActionRiskLevel RiskLevel { get; init; } = ActionRiskLevel.Medium;

    /// <summary>Sources that may call this action without an explicit confirmation.</summary>
    public ActionSourcePermissions AllowedSources { get; init; } = ActionSourcePermissions.Admin;

    /// <summary>
    /// Sources that may call this action ONLY after explicit confirmation.
    /// If a source appears in <see cref="AllowedSources"/> it does not need
    /// confirmation.  If it appears here it does.
    /// </summary>
    public ActionSourcePermissions ConfirmationRequiredSources { get; init; } = ActionSourcePermissions.None;

    // ── Session phase allowance ───────────────────────────────────────────────

    /// <summary>Phases in which this action may be executed.</summary>
    public SessionPhaseAllowance AllowedPhases { get; init; } = SessionPhaseAllowance.Any;

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns true when <paramref name="source"/> is permitted to call this
    /// action without a confirmation prompt.
    /// </summary>
    public bool IsSourcePermitted(ActionSource source)
    {
        var flag = SourceToFlag(source);
        return (AllowedSources & flag) != ActionSourcePermissions.None;
    }

    /// <summary>
    /// Returns true when <paramref name="source"/> requires confirmation before
    /// this action may be executed.
    /// </summary>
    public bool RequiresConfirmation(ActionSource source)
    {
        var flag = SourceToFlag(source);
        return (ConfirmationRequiredSources & flag) != ActionSourcePermissions.None;
    }

    /// <summary>
    /// Returns true when the action may run from the given source at all
    /// (permitted OR requires confirmation — either way the source is acknowledged).
    /// </summary>
    public bool IsSourceAcknowledged(ActionSource source) =>
        IsSourcePermitted(source) || RequiresConfirmation(source);

    /// <summary>Returns true when the action may run in <paramref name="phaseFlag"/>.</summary>
    public bool IsPhaseAllowed(SessionPhaseAllowance phaseFlag) =>
        (AllowedPhases & phaseFlag) != SessionPhaseAllowance.None;

    private static ActionSourcePermissions SourceToFlag(ActionSource source) => source switch
    {
        ActionSource.Admin        => ActionSourcePermissions.Admin,
        ActionSource.Player       => ActionSourcePermissions.Player,
        ActionSource.AI           => ActionSourcePermissions.AI,
        ActionSource.Webhook      => ActionSourcePermissions.Webhook,
        ActionSource.MCP          => ActionSourcePermissions.MCP,
        ActionSource.EventRuntime => ActionSourcePermissions.EventRuntime,
        ActionSource.DevConsole   => ActionSourcePermissions.DevConsole,
        ActionSource.System       => ActionSourcePermissions.System,
        _                         => ActionSourcePermissions.None,
    };
}

// ── Built-in defaults ─────────────────────────────────────────────────────────

/// <summary>
/// Factory helpers for the most common capability profiles.
/// </summary>
public static class ActionCapabilityDefaults
{
    public static ActionCapabilityDescriptor ReadOnly(string actionName, string description = "") =>
        new()
        {
            ActionName = actionName,
            Description = description,
            IsMutating = false,
            IsReversible = true,
            IsIdempotent = true,
            RiskLevel = ActionRiskLevel.ReadOnly,
            AllowedSources = ActionSourcePermissions.All,
            AllowedPhases = SessionPhaseAllowance.Any,
        };

    public static ActionCapabilityDescriptor SafeMutation(string actionName, string description = "") =>
        new()
        {
            ActionName = actionName,
            Description = description,
            IsMutating = true,
            IsReversible = true,
            IsIdempotent = false,
            RiskLevel = ActionRiskLevel.Low,
            AllowedSources = ActionSourcePermissions.AllAuthenticated | ActionSourcePermissions.AI,
            AllowedPhases = SessionPhaseAllowance.AnyLive | SessionPhaseAllowance.AnySetup,
        };

    public static ActionCapabilityDescriptor AdminOnly(string actionName, string description = "") =>
        new()
        {
            ActionName = actionName,
            Description = description,
            IsMutating = true,
            IsReversible = false,
            IsIdempotent = false,
            RiskLevel = ActionRiskLevel.High,
            AllowedSources = ActionSourcePermissions.Admin | ActionSourcePermissions.System,
            AllowedPhases = SessionPhaseAllowance.Any,
        };

    public static ActionCapabilityDescriptor AiSafe(string actionName, string description = "") =>
        new()
        {
            ActionName = actionName,
            Description = description,
            IsMutating = true,
            IsReversible = true,
            IsIdempotent = false,
            RiskLevel = ActionRiskLevel.Low,
            AllowedSources = ActionSourcePermissions.AI | ActionSourcePermissions.Admin | ActionSourcePermissions.EventRuntime,
            AllowedPhases = SessionPhaseAllowance.AnyLive,
        };
}
