// Services/Runtime/ActionCapabilityMatrix.cs
// Permission and phase enums used by the validation pipeline.
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
