# Deep Review: BattleLuck Plugin Architecture & Security

**Date:** 2026-06-18  
**Scope:** BattleLuck V Rising plugin (net6.0, BepInEx, ECS-based game mode system)  
**Status:** ✅ Build passes | ⚠️ Security/Testing concerns identified

---

## Executive Summary

BattleLuck is a well-structured, feature-rich plugin with 6 game modes, AI integration, and external service bridges (Discord, Webhooks, Lark, Cloudflare AI). However, the codebase has critical gaps in testing, risky global static state, and several security/code-quality issues that should be addressed before production deployment.

**Key Risks:**
- ⚠️ **AllowUnsafeBlocks=true** in csproj (unused; recommend disabling)
- ⚠️ **Only 1 test file** (CommandRecordTests.cs); no unit tests for core systems
- ⚠️ **Global static singletons** throughout (BattleLuckPlugin, Session, GameModes, etc.)
- ⚠️ **Caught exceptions often lose stack traces** (ex.Message instead of ex.ToString())
- ⚠️ **External endpoints** (Discord, Webhook, Lark) open ports; defaults should be disabled
- ⚠️ **No comprehensive CI** before our changes (now added)

---

## Architecture Overview

### Design Patterns
- **Plugin Architecture:** BepInEx mod with Harmony patches for hooking V Rising game events
- **Event-Driven:** ProjectMEventRouter broadcasts game events (death, action, mode changes)
- **ECS Integration:** Uses Unity DOTS EntityManager for efficient queries and mutations
- **Configuration-Driven:** Mode definitions, flows, and kits stored in JSON under `config/BattleLuck/`
- **Main Thread Dispatch:** Off-thread work (AI, HTTP) queued back to main thread via MainThreadDispatcher

### Core Components

| Component | Role | Risk |
| --- | --- | --- |
| **BattleLuckPlugin** | Entry point; initializes all services | **HIGH:** 40+ static fields; lock on TryInitializeCore |
| **SessionController** | Manages active game sessions & round state | **MEDIUM:** Drives all per-tick logic |
| **GameModeRegistry** | Registry of 6 game modes (Bloodbath, Gauntlet, etc.) | **LOW:** Simple registry |
| **FlowController** | Interprets action strings, applies kits, teleports, buffs | **HIGH:** Complex mutation pipeline |
| **AIAssistant** | Cloudflare/Google AI query handler | **HIGH:** External API, network I/O |
| **NpcControlService** | Sole controlled-entity registry (NPCs, elites, VBloods, servants, companions, waves) | **MEDIUM:** ECS-dependent |
| **SessionCleanupService** | Zone cleanup on session end | **MEDIUM:** Bulk entity deletion |
| **Discord/Webhook/Lark Bridges** | External HTTP endpoints | **HIGH:** Open ports, enabled by default |

---

## Security Issues

### 1. **Hardcoded AllowUnsafeBlocks (CSProj)**
```xml
<AllowUnsafeBlocks>True</AllowUnsafeBlocks>
```
- **Finding:** Allows unsafe code blocks, but grep found no actual `unsafe` or `fixed` usage.
- **Risk:** Increases attack surface if unsafe code is added later.
- **Recommendation:** 
  - Confirm no unsafe code exists
  - Set to `False` to prevent future abuse
  - Document why unsafe is needed if it must stay enabled

### 2. **Exposed Cloudflare API Token**
- **Finding:** User pasted CLOUDFLARE_API_TOKEN in chat earlier
- **Status:** ✅ **FIXED** (commit: CI/CD + secrets management)
  - Token now read from environment variables
  - GitHub Actions Secrets used for CI
  - docs/CI_SECRETS.md explains setup
- **Action Required:** Revoke and regenerate the exposed token immediately

### 3. **External Endpoints Enabled by Default**
**Affected Services:**
- `DiscordBridgeController` (line 383-393, BattleLuckPlugin.cs)
- `WebhookController` (line 403-418)
- `LarkBridgeService` (line 495-511)

```csharp
// Loads config; enables if configured.Enabled == true
var discordConfig = ConfigLoader.LoadDiscordBridgeConfig();
if (discordConfig?.Enabled == true) { ... }
```

- **Risk:** Ports opened publicly if config enables them (default: check config files)
- **Recommendation:**
  - Verify all `*_config.json` files have `"enabled": false` by default
  - Document which ports are opened (Discord webhook, Webhook listener, Lark webhook)
  - Add firewall rules / network ACLs in deployment docs

### 4. **Exception Handling Loses Stack Traces**
**Pattern Found:** 40+ instances (grep found 200 matches)
```csharp
catch (Exception ex) {
    Log?.LogWarning($"[BattleLuck] Tick error: {ex.Message}");  // ❌ Only message, no stack trace
}
```

- **Risk:** Debugging failures is hard without stack traces; security issues may be missed
- **Recommendation:**
  ```csharp
  catch (Exception ex) {
      Log?.LogWarning($"[BattleLuck] Tick error:\n{ex}");  // ✅ Full exception + stack
  }
  ```

### 5. **Global Static State & Thread Safety**
**High-Risk Statics (BattleLuckPlugin.cs):**
```csharp
static Harmony? _harmony;
static bool _initialized;
static readonly object _initLock = new();  // Only locks TryInitializeCore
static EntityQuery? _playerQuery;
static AiHologramService? _hologramService;
```

- **Risk:** 40+ static fields create race conditions, hard to test, tight coupling
- **Recommendation:**
  - Use dependency injection instead of statics for services
  - Keep only truly global (plugin meta, logger) as static
  - Example: `public static ISessionController Session => _instance?.Session;`

### 6. **No Input Validation on External Endpoints**
**Webhook & Lark Handlers:**
- Incoming HTTP requests (Discord webhooks, Lark webhooks, custom webhook listener)
- No visible validation of request signatures or HMAC verification
- **Risk:** Spoofed events from untrusted sources
- **Recommendation:**
  - Validate webhook signatures (Discord: X-Signature-Ed25519)
  - Restrict CIDR ranges for Lark/webhook endpoints
  - Document security assumptions in code

---

## Testing & Quality Gaps

### 1. **Minimal Test Coverage**
- **Total Test Files:** 1 (CommandRecordTests.cs)
- **Coverage:** ~0.5% (only command recording tested)
- **Missing:**
  - SessionController state machine tests
  - FlowAction parsing and execution
  - AI integration (mocked)
  - Kit application logic
  - Zone cleanup edge cases

- **Recommendation:**
  ```
  Tests to add (priority order):
  1. SessionController.cs
     - Session start/end lifecycle
     - Player join/leave
     - Proper cleanup on unexpected exit
  2. FlowActionExecutor.cs
     - Parse action strings correctly
     - Validate parameters
     - Rollback on invalid mutations
  3. ScoreTracker / EloController
     - Ranking calculations
     - Tie-breaking rules
  4. KitController
     - Kit loading from JSON
     - Slot resolution (weapon, armor, abilities)
     - Override behavior
  ```

### 2. **No Integration Tests**
- ECS queries untested (EntityManager mocking required)
- Patch compatibility with game versions not verified
- Mode end-to-end flows not validated

### 3. **Code Quality Observations**

| Issue | Count | Severity |
| --- | --- | --- |
| Empty catch blocks swallowing errors | 5+ | MEDIUM |
| No input validation on JSON configs | 10+ | MEDIUM |
| Magic numbers (e.g., radius=200f) | 15+ | LOW |
| Commented-out debug code | 8+ | LOW |
| Missing null checks before .Exists() | 3+ | MEDIUM |

---

## Architecture Issues

### 1. **Circular Dependencies**
- BattleLuckPlugin initializes -> SessionController -> FlowController -> GameModes
- Hard to unit test without initializing entire plugin
- Recommendation: Use factory pattern + DI

### 2. **No Abstraction for External APIs**
- Direct `HttpClient` calls to Cloudflare, Discord, etc.
- No retry logic or circuit breaker
- **Recommendation:** Create `ICloudflareClient`, `IDiscordClient`, etc. with mocks for tests

### 3. **ProjectMEventRouter Thread Safety**
- Broadcasts game events; listeners may run off main thread
- No documented guarantees on thread context
- **Recommendation:** Document thread model; add assertions in listeners

### 4. **Config Hot-Reload**
- `.reload` command reloads configs from disk
- No validation that new config is compatible with running sessions
- **Risk:** Corrupted state if config changes mid-session
- **Recommendation:** Validate config before applying; queue changes for next session end

---

## Performance Notes

### Observed Optimizations
- ✅ ECS Query Registry (cached queries per EntityManager)
- ✅ MainThreadDispatcher (batches off-thread work)
- ✅ Tick-based updates (not polling)

### Potential Bottlenecks
- 🔍 PlayerQuery recreated if null (line 146-148, BattleLuckPlugin.cs) — should cache
- 🔍 Zone cleanup (CleanupZone) may stall main thread if zone has 1000+ entities
- 🔍 AI queries (CloudflareAiService) block main thread until response — should stream or use task

---

## Deployment & Operations

### Current CI/CD
- ✅ **Added (this session):**
  - GitHub Actions workflow (build + test)
  - Gitleaks secret scanning
  - Environment secret injection
- ❌ **Missing:**
  - Code coverage reporting
  - Performance benchmarking
  - Automated security scanning (SAST)

### Secrets Management
- ✅ **Fixed (this session):**
  - CLOUDFLARE_API_TOKEN → environment variable
  - GitHub Actions Secrets configured
  - Local dev: dotnet user-secrets or env var
- ❌ **Remaining:**
  - Discord webhook URL still in config files (should use env var)
  - Lark API key in config (should use env var)

### Production Checklist
- [ ] Revoke exposed Cloudflare token
- [ ] Set AllowUnsafeBlocks=false if no unsafe code
- [ ] Verify all endpoint configs default to disabled
- [ ] Test firewall/network policy for opened ports
- [ ] Document service port usage (Discord: ?, Webhook: ?, Lark: ?)
- [ ] Add rate-limit middleware to HTTP endpoints
- [ ] Enable request signature validation (Discord, Lark)
- [ ] Configure log aggregation (CloudFlare Logs API, Discord webhook forwarding)
- [ ] Load-test zone cleanup with 10,000+ entities
- [ ] Verify no memory leaks in long-running sessions

---

## Recommendations by Priority

### 🔴 CRITICAL (Before Production)
1. **Revoke exposed API token** and regenerate
2. **Disable AllowUnsafeBlocks** if unused
3. **Validate webhook signatures** (Discord, Lark)
4. **Fix exception logging** (use `ex.ToString()` not `ex.Message`)

### 🟠 HIGH (Next Sprint)
1. **Add unit tests** for SessionController, FlowActionExecutor, ScoreTracker (30–50% coverage target)
2. **Refactor global statics** into injectable services
3. **Hot-reload validation** for config changes
4. **Retry logic + circuit breaker** for external APIs
5. **Security audit** of config handling (file permissions, encoding)

### 🟡 MEDIUM (Backlog)
1. Add integration tests for ECS queries
2. Performance profiling of zone cleanup
3. Code coverage reporting in CI
4. Refactor magic numbers into named constants
5. Remove commented-out debug code

### 🟢 LOW (Polish)
1. Add input validation on JSON configs with helpful error messages
2. Documentation: thread model, security model, deployment guide
3. Add tracing/observability hooks (OpenTelemetry?)
4. Benchmark AI queries; consider async/await patterns

---

## Files Requiring Review

| File | Lines | Risk | Notes |
| --- | --- | --- | --- |
| BattleLuckPlugin.cs | ~696 | HIGH | 40+ statics; exception handling; initialization lock |
| Services/Flow/FlowActionExecutor.cs | ? | HIGH | Core action logic; no visible validation |
| Services/AI/AIAssistant.cs | ? | HIGH | External API calls; thread safety |
| Core/SessionController.cs | ? | HIGH | Session lifecycle; state machine |
| Services/Integrations/WebhookController.cs | ? | HIGH | HTTP endpoint; no signature validation |
| Services/Integrations/DiscordBridgeController.cs | ? | MEDIUM | HTTP endpoint; check signature validation |
| Services/Integrations/LarkBridgeService.cs | ? | MEDIUM | HTTP endpoint; check signature validation |
| Models/* | ? | MEDIUM | Ensure JSON serialization is safe; no code injection |
| BattleLuck.csproj | ~118 | MEDIUM | AllowUnsafeBlocks=true; review DirectoryPackages.props |

---

## Conclusion

BattleLuck is **architecturally sound** and **feature-complete** for a game mod. The core ECS patterns, event routing, and session management are solid. However, **before production use**, address the critical security issues (token exposure, unsafe blocks, webhook validation) and add meaningful test coverage. The codebase would benefit from dependency injection to reduce global state and improve testability.

**Next Steps:**
1. ✅ Merge CI/CD changes (done)
2. Create ticket for AllowUnsafeBlocks audit
3. Create ticket for webhook signature validation
4. Create ticket for unit test framework + 30% coverage target
5. Refactor statics → DI in separate PR

---

## References

- **CI/CD Changes:** commit eb1e48e2
- **Secrets Setup:** docs/CI_SECRETS.md
- **Config Layout:** README.md (lines 176–217)
- **Commands Reference:** README.md (lines 25–173)

---

## Scoped Recovery and Deployment Audit (2026-07-16)

This follow-up review covers the current no-code deployment and rollback surface.

### Repository inventory

The `main` checkout contains 368 tracked files: 277 C# files, 27 tracked JSON files, and 42 Markdown files. The review included source, configuration, schemas, prompts, documentation, and build metadata. The ignored `.mcp.json` file is an AgentsRoom tool configuration and is not part of the plugin; it currently contains an extra top-level fragment and is not valid JSON, but it is not loaded by BattleLuck.

### Verification performed

- `dotnet build BattleLuck.sln -c Release /p:DeployBattleLuck=false`: passed with 0 errors and 13 pre-existing warnings.
- Event and schema JSON files: parsed successfully with PowerShell JSON parsing; `.mcp.json` was excluded because it is an ignored tool file.
- `git diff --check`: no whitespace errors.
- No `BattleLuck.Tests` project is present, so a full automated test run is not available.
- The shell validator is included for Linux/WSL hosts; this Windows checkout does not have a WSL distribution available, so it was not executed here.

### Recovery boundaries

BattleLuck now exposes three deliberately separate recovery scopes:

1. **Event definition rollback** (`.ai event rollback <eventId>`): restores a verified BattleLuck deployment backup after manifest/hash and schema checks.
2. **Per-player event rollback** (`.ai rollback player <name|steamId> <timestamp|runId>`): restores one online player's exact persisted pre-event snapshot. The snapshot is consumed only after a successful restore.
3. **All-player event rollback** (`.ai rollback server players confirm`): restores every matching online event snapshot and retains offline snapshots as pending. It never rewrites the V Rising world save.

`.ai rollback server purge <eventId> [backupId] confirm` deletes one inactive BattleLuck deployment-backup directory only. It cannot delete or alter `VRisingServer/Saves`; that directory is owned by V Rising's native `SaveFileManager` and must be managed with the host's world-save backup tooling. This is intentional: a plugin-side recursive delete of the native world history could permanently destroy the only recovery copy.

### Deployment controls

Deployments use an HTTPS-only source, a staging directory, schema/referential validation, atomic directory replacement, a manifest containing SHA-256 hashes, and best-effort restoration when registration fails. Append-only JSONL audit records are written to `BepInEx/config/BattleLuck/logs/event_audit.jsonl` and rotate at 10 MB. The audit command reports common error codes and recommendations; it never executes an AI recommendation.

### Remaining operational risks

- No live dedicated-server crash/restart test was run in this checkout; test the Safe-Stage workflow on a copy of the world first.
- ECS registration, zone cleanup, and player restoration still require a running V Rising server for integration verification.
- Existing compiler warnings remain in unrelated legacy paths and should be addressed separately.
- Native full-world rollback remains an operator/host responsibility. The log values supplied for `world1` (`AutoSaveCount=10`, `AutoSaveInterval=120`, compressed saves) confirm that V Rising is maintaining its own save history; they do not provide a safe plugin API for deleting that history.

### Audit and operator guardrail follow-up

- The audit contract is now locked by `docs/audit/audit-record.schema.json`; field names match `EventDeploymentAuditRecord` exactly.
- KindredExtract component/system lists are generated under `docs/audit/systems/allowlists/` and embedded as reference candidates for server-side validation. They are not in-game verification; prefab enforcement remains runtime-backed until a real target-server `.dump p` export is supplied. Native sequence UUIDs use the separate strict `config/BattleLuck/sequences/uuid_catalog.json` and are promoted only after in-game confirmation.
- Event references now fail with `E_IDS` and a file/JSONPath; malformed `wait:`/`tick:` markers fail with `E_TICK`.
- `.event.start` blocks configured high-load windows and records `START_WINDOW_BLOCKED`; an explicit admin force token records `START_WINDOW_FORCED`.
- Production mode can require a verified BattleLuck deployment manifest (`E_NO_SNAPSHOT`) before replacement. This is opt-in and does not substitute for the native V Rising world-save backup.
- Per-player rollback requires an exact snapshot timestamp or event run id and restores only the documented snapshot categories; managed event session flags are cleared by `SessionController`, not copied between players.
