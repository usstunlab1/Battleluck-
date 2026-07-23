# BattleLuck release and server-validation runbook

## Local reproducible audit

```powershell
.\tools\Invoke-BattleLuckAudit.ps1
.\tools\New-BattleLuckAuditedZip.ps1
```

The audit stops on a failed locked restore, build, test, vulnerability scan,
configuration parse, credential scan, whitespace check, package
reproducibility check, or payload checksum check.

## Clean dedicated-server gate

1. Back up the V Rising save, BepInEx configuration, and current plugin DLL.
2. Use a clean server matching the compile-time reference version.
3. Install the declared BepInEx and VampireCommandFramework dependencies.
4. Extract `BattleLuck-server-1.1.2.zip` into the server plugin directory.
5. Verify every line in `SHA256SUMS.txt`.
6. Start the server and require one successful BattleLuck initialization with
   no Harmony, IL2CPP generic-system, missing component, or command
   registration exception.
7. Join with an admin and a non-admin account. Check permission boundaries,
   `.ai ui`, event start/end, late join, disconnect/reconnect, snapshot restore,
   NPC cleanup, and plugin unload/reload.
8. Run the two skipped castle tests against the live world.
9. Run an instrumented four-hour soak and compare entity counts, managed
   memory, queue depth, tick time, and log rate at start and end.

## Rollback

1. Stop the server.
2. Restore the prior DLL and configuration backup.
3. Restore the world save only when the tested rollback procedure requires it.
4. Start the server and confirm the prior version initializes.
5. Preserve failing logs and hashes; do not overwrite the audit evidence.

## Go/no-go rule

Any P0 finding, live startup exception, state-loss defect, permission bypass,
checksum mismatch, or unresolved game-reference mismatch is a no-go.
