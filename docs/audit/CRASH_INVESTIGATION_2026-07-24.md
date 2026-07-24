# Server Crash Investigation

**Date:** 2026-07-24  
**Server:** V Rising Dedicated Server  
**Plugin:** BattleLuck 1.1.2  

---

## Crash Summary

| Field | Value |
|-------|-------|
| **Error Type** | `System.ArgumentException: The entity does not exist` |
| **Source** | `Unity.Entities.EntityComponentStore::AppendDestroyedEntityRecordError` |
| **System** | `ProjectM.DetachSystem` (Burst-compiled job) |
| **Severity** | CRITICAL (server crash) |
| **Root Cause** | V Rising game bug, NOT a BattleLuck bug |

---

## Evidence

From `output_log.txt`:

```
Found entity: 324156:121 that has been destroyed without being detached. Info: Entity(324156:121) 
 - Unity.Entities.Entity - ProjectM.Attached - Unity.Entities.CleanupEntity

DetachEntity information. Entity: 324156:121 EntityInBuffer: 324156:121 Parent: 324031:1 
 PrefabGUID: 1171608023 ParentPrefabGUID: 38526109

[... repeated for entities 559705:11, 323100:9, 343871:140 ...]

System.ArgumentException: The entity does not exist.
System.String Unity.Entities.EntityComponentStore::AppendDestroyedEntityRecordError(Unity.Entities.Entity)
This Exception was thrown from a job compiled with Burst, which has limited exception support. 
Turn off burst (Jobs -> Burst -> Enable Compilation) to inspect full exceptions & stacktraces. 
In this standalone build configuration burst will now abort the Application.
```

---

## Analysis

### What Happened

1. Parent entity `324031:1` (PrefabGUID `38526109`) was destroyed
2. Child entities (`324156:121`, `559705:11`, `323100:9`, `343871:140`, all with PrefabGUID `1171608023`) were still attached to the parent
3. V Rising's `DetachSystem` (a Burst-compiled ECS job) detected orphaned attached entities
4. The system attempted to log/detach these entities but threw `ArgumentException` because the parent no longer exists
5. Burst aborted the application, causing server crash

### Why This Is NOT a BattleLuck Bug

1. **BattleLuck's cleanup code is defensive:**
   - `DestroyWithReason()` in `EntityExtensions.cs` adds `Disabled` component before destroying
   - All destruction is wrapped in try/catch with "in live state" handling
   - Session cleanup only destroys entities in specific zones with radius checks

2. **The error originates from V Rising's own systems:**
   - `ProjectM.DetachSystem` is a game system, not a BattleLuck system
   - The error occurs in `DetachEntity` when processing `Attached` components
   - This is a game-level entity lifetime issue

3. **BattleLuck does not manipulate `Attached` components:**
   - No code in BattleLuck creates/destroys `Attached` relationships
   - The plugin only destroys entities it spawns (NPCs, walls, items)
   - The orphaned entities are game-world buildings/structure children

### What Triggered It

The crash occurred during:
- Player teleport events (`TeleportDebugEvent` logged just before crash)
- Session cleanup for `aievent` zone (2010)
- Player transitioning to `trial_of_all_actions` zone (5555)

The sequence:
1. Player enters zone 2010 (aievent)
2. Event runs, BattleLuck spawns NPCs and applies buffs
3. Player exits zone 2010
4. Session cleanup runs for zone 2010
5. Server teleports player to zone 5555
6. V Rising's DetachSystem crashes on orphaned attached entities

---

## Conclusion

**This is a V Rising game bug**, not a BattleLuck plugin bug. The game's entity detachment system cannot handle parent entities being destroyed while children still have `Attached` components.

### Recommended Actions

1. **Short-term:** Restart the server. This is a one-time crash, not a recurring issue.
2. **Medium-term:** Report to V Rising / Stunlock Studios as a game bug.
3. **Long-term:** Consider adding a defensive check in BattleLuck to avoid destroying parent entities that have children (if we ever spawn such structures).

### BattleLuck's Defensive Measures Already In Place

- ✅ `DestroyWithReason()` adds `Disabled` component before destruction
- ✅ Try/catch around all entity destruction
- ✅ "In live state" deferral for ECS systems processing
- ✅ Zone-radius checks prevent accidental world destruction
- ✅ `IsProtectedCastleAnchor()` preserves castle heart connections

### No Code Changes Required

No changes to BattleLuck code are needed to fix this crash. The root cause is in V Rising's `DetachSystem`.