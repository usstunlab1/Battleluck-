# BattleLuck deployment audit contract

The authoritative writer is `Services/Runtime/EventDeploymentAuditService.cs`.
Each operation appends one compact JSON object to:

```text
BepInEx/config/BattleLuck/logs/event_audit.jsonl
```

The file is local to the dedicated server, append-only during normal operation,
and rotated to `event_audit.jsonl.1` at 10 MiB. Audit records are diagnostic;
they never grant AI authority and recommendations are not executed automatically.

## Record shape

The current record uses these exact camelCase fields:

| Field | Meaning |
| --- | --- |
| `timestamp` | UTC time of the operation. |
| `command` | `deploy`, `deploy_dry_run`, `status`, `rollback`, `backup_purge`, `player_rollback`, `server_player_rollback`, or `event.start`. |
| `eventId` | Normalized event id, `all` for aggregate status, or `invalid` when input was rejected. |
| `gist` | Source URL for a deploy, or the backup source label for rollback. |
| `files` | Presence map for `flow.json`, `zones.json`, `kits.json`, and `prompt.txt`. |
| `fileHashes` | Lowercase SHA-256 hash per file that was readable after the operation. |
| `validation` | `{ "json": bool, "schema": bool, "ids": bool }`. |
| `server` | `{ "registerOk": bool, "startOk": bool, "error": string|null }`. Deploy and rollback do not start an event, so `startOk` is normally false. |
| `rollback` | Whether the operation is a recovery/purge operation. |
| `exit` | `0` for success, `1` for failure. |
| `errorCode` | Stable code from [error-codes.md](error-codes.md), or null on success. |
| `error` | Human-readable failure detail, or null on success. |
| `backup` | Latest/created BattleLuck backup path or source label. |
| `restoredPlayers` | Number of player snapshots restored by a state rollback. |
| `skippedPlayers` | Offline or failed player snapshots not restored. |

The machine-readable contract is [audit-record.schema.json](audit-record.schema.json).
For a compact weekly summary, run
`pwsh tools/audit/summarize-audit.ps1 -Path <server>/BepInEx/config/BattleLuck/logs/event_audit.jsonl`.

An `event.start` safety decision uses the same record shape. A blocked start has
`command: "event.start"`, `errorCode: "START_WINDOW_BLOCKED"`, `exit: 1`, and
the measured load reason in `error`. An explicitly forced start records
`START_WINDOW_FORCED` with `exit: 0`; the force token is an administrator
decision, not an AI decision.

Use `.ai event deploy <eventId> <https-gist-url> --dry-run` to download and
validate a bundle without creating a backup or registering a mode. The audit
record is written as `command: "deploy_dry_run"`, `server.registerOk: false`,
and `exit: 0` on success.

The contract intentionally does not contain `version.hashes`,
`snapshot.manifest_hash`, `server.create_ok`, or an `errors[]` array. Those names
belong to a future schema and must not be used by tooling against the current
writer. Backup integrity is represented by the separate `manifest.json` file in
each deployment backup; it contains `schemaVersion`, `modeId`, `source`,
`createdUtc`, and per-file `bytes`/`sha256` values.

## Real record policy

No live dedicated-server audit record is committed to the repository: doing so
would disclose server event names, URLs, hashes, or player recovery metadata.
After a real deployment, an owner may copy one redacted line from
`event_audit.jsonl` into an internal runbook. Do not fabricate a “real deploy”
example or commit a production Gist URL. The repository validator and CI use
the schema above instead.

## Allowlists

KindredExtract-derived component and system reference candidates live under
`docs/audit/systems/allowlists/` and are generated with:

```powershell
pwsh tools/export-kindredextract-system-csv.ps1
pwsh tools/export-kindredextract-allowlists.ps1
```

The allowlist exporter writes line-oriented `systems.allowlist.txt`,
`components.allowlist.txt`, and `prefabs.allowlist.txt` files, JSON copies for
the runtime, and `version.hash`. These are not in-game verification; they are
candidate names/types and must be checked against a target-server dump. The
separate `config/BattleLuck/sequences/uuid_catalog.json` starts with no
executable entries and accepts only values marked `in_game_verified`. The
generated `prefabs.json` is sourced from KindredExtract's
`.external/KindredExtract/Data/Prefabs.cs` when that checkout is available.
Because prefab catalogs are game-version specific, regenerate it from a target
server's `.dump p` output whenever the V Rising build changes; if no prefab dump
is available the generator deliberately emits a runtime-only placeholder rather
than claiming an exact offline list.

Production deployment can opt into `E_NO_SNAPSHOT` with
`config/BattleLuck/operator_safety.json` by setting both `productionMode` and
`requireSnapshotBeforeProductionDeploy` to `true`. The required snapshot is a
verified BattleLuck deployment backup manifest, not the native V Rising world
save.

## Per-player rollback scope

Snapshots are stored at `BepInEx/data/BattleLuck/snapshots/<steamId>.json`.
The current restore path applies position, health, blood, equipment levels and
slots, inventory, weapons, abilities, passives, and buffs. Energy is a documented
placeholder; jewel and progression fields are retained for schema compatibility
but are not currently replayed by `RestoreSnapshot`. Event participant/session
flags are managed by `SessionController` and are cleared when the active session
is exited; they are not serialized into the snapshot. A successful restore
consumes the snapshot. Offline snapshots remain pending and are never deleted by
a failed restore.

Use an exact snapshot selector to prevent cross-session restoration:

```text
.ai rollback player <name|steamId> <snapshot-utc-timestamp-or-event-run-id>
```

The timestamp must match `timestamp` exactly (ISO-8601), or the value must
match `eventRunId` when present. A selector is required; the command does not
guess which historical event a player meant.
