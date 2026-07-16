# Deployment and rollback error codes

These are the codes currently emitted or classified by
`EventDeploymentAuditService`. Codes are prefixes in the `error` text and the
normalized `errorCode` field.

| Code | Meaning | Operator response |
| --- | --- | --- |
| `EJSONPARSE` | Required JSON could not be parsed. | Run the offline validator and fix the named file. |
| `ESCHEMA` | A required field, shape, or cross-file contract is invalid. | Compare with `config/BattleLuck/events/schemas/`. |
| `EMISSINGFILE` | A required event file is missing or empty. | Supply all four event files. |
| `EINVALIDID` | Event id or selector is unsafe/invalid. | Use lowercase letters, numbers, `_`, or `-`. |
| `EUNKNOWNID` | A referenced id is not in the verified runtime catalog. | Use reference candidates for research, then confirm the id with a live dump before promotion. |
| `E_IDS` | A system/component/prefab reference failed the reference candidate list or a runtime lookup. | Fix the file and JSONPath reported in the error, then confirm the target build in-game. |
| `E_TICK` | A tick/sequence marker is malformed or unverifiable. | Use validated `wait:<seconds>`/`tick:<event-second>` markers. |
| `E_UUID_UNVERIFIED` | A sequence UUID catalog entry is not marked as verified in-game. | Remove it from `entries` until a target-server dump confirms the hash. |
| `EGIST` | HTTPS Gist URL or download failed. | Use a public HTTPS Gist containing all required files. |
| `ERUNTIMEREGISTER` | Game mode or zone registration failed. | Use Safe-Stage and inspect the V Rising log. |
| `EACTIVE` | The event is active during replacement, rollback, or purge. | End the event first. |
| `EBACKUP` | No verified backup exists or its manifest/files failed verification. | Create a fresh known-good deployment backup. |
| `E_BACKUP_TAMPERED` | A backup manifest hash/size does not match. | Do not restore it; retain it for forensic review. |
| `E_NO_SNAPSHOT` | Production deployment requires a valid snapshot but none exists. | Create/verify a BattleLuck backup before deploying. |
| `EZONEHASH` | A zone hash is missing, non-positive, or collides with another event. | Choose a unique positive hash in both flow and zones files. |
| `E_RATE` | An operation was blocked by an operator safety rate/load guard. | Retry during a low-load window or explicitly force it. |
| `START_WINDOW_BLOCKED` | `.event.start` was blocked during a high-load window. | Retry with the explicit force argument after review. |
| `START_WINDOW_FORCED` | Informational audit marker for an explicitly forced start; `exit` remains 0. | Review the server load decision recorded in `error`. |
| `EPURGE_CONFIRM` | A destructive backup purge lacks the final confirmation token. | Repeat with `confirm`. |
| `EPURGE` | A BattleLuck backup could not be deleted. | Check the backup id and filesystem permissions. |
| `EUNKNOWN` | Failure did not match a known code. | Inspect the full server log and audit record. |

`E_IDS`, `E_TICK`, `E_UUID_UNVERIFIED`, `E_BACKUP_TAMPERED`, `E_NO_SNAPSHOT`, `E_RATE`, and
`START_WINDOW_BLOCKED` are reserved for the validation/safety extensions in
this release. A CI failure must stop on `EJSONPARSE`, `ESCHEMA`, or `E_IDS`.
