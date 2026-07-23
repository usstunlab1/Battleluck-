# Flat Config Migration — Verification Checklist

## Migration Summary

The flat config migration makes `config/BattleLuck/events/<eventId>.json` the canonical event definition source, replacing the legacy `config/BattleLuck/<modeId>/` subdirectory pattern.

## Files Changed

| File | Change | Status |
|------|--------|--------|
| `Services/Flow/FlowPersistence.cs` | Redirected `session.json` writes from `config/{modeId}/` to `events/{modeId}/` | ✅ |
| `Services/Runtime/EventDefinitionLoader.cs` | Removed legacy zones fallback to `config/{modeId}/zones.json`; uses only `events/{modeId}/zones.json` | ✅ |
| `Core/ModeConfigLoader.cs` | Updated `LoadLegacy()` to scan `events/{modeId}/` instead of `config/{modeId}/` | ✅ |

## Verification Checklist

### Build Verification
- [x] `dotnet build` succeeds (13 pre-existing errors in unrelated files remain)
- [x] No new errors introduced by migration changes

### Runtime Behavior
- [ ] Flow overrides persist to `events/{modeId}/session.json` (not `config/{modeId}/session.json`)
- [ ] `EventDefinitionLoader.LoadLegacyZones()` reads from `events/{modeId}/zones.json` only
- [ ] `ModeConfigLoader.LoadLegacy()` fallback scans `events/{modeId}/` for zones
- [ ] `GameModeRegistry.LoadAllModes()` discovers events from `events/*.json` flat files
- [ ] `EventDeploymentService.InstallBundle()` writes `events/{modeId}.json` flat + `events/{modeId}/` sidecars

### Legacy Path Cleanup (No Longer Read)
- [ ] `config/BattleLuck/{modeId}/zones.json` — no longer read
- [ ] `config/BattleLuck/{modeId}/session.json` — no longer written or read
- [ ] `config/BattleLuck/{modeId}/flow.json` — no longer read (if it existed)

### Data Migration (If Legacy Events Exist)
- [ ] Existing `config/BattleLuck/{modeId}/` directories should be moved to `events/{modeId}/`
- [ ] Existing `config/BattleLuck/{modeId}/event.json` should be moved to `events/{modeId}.json`
- [ ] Backups created under `.migration-backup/` before any destructive operations

### Remaining Legacy References (Intentional — Global Config)
These are NOT event-specific and should NOT be migrated:
- `build_palette.json` — global build palette
- `castle_anchor_state.json` — runtime state
- `custom_sequences.json` — global custom sequences
- `roadmap.json` — global roadmap config
- `operator_safety.json` — global operator safety
- `adaptive_drills.json` — global adaptive drills
- `clan_tasks.json` — global clan tasks
- `actions_catalog.json` — global action catalog
- `kit_grant_rules.json` — global kit grant rules
- `ai_operator_prompt.md` — global AI prompt
- `ai_operations.log` — runtime log file
- `ai_config.json` — global AI config
- `battleluck.json` — global plugin config
- `tech_catalog.json` — global tech catalog
- `action_config.json` — global action config
- `merchant_servant_actions.json` — global merchant config
- `schematics/` — global schematics directory
- `runtime/` — runtime state directory
- `logs/` — log directory
- `backups/` — deployment backup directory
- `audit/` — audit/allowlist directory
- `developer/` — developer bridge directory
- `.env` — environment file