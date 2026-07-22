# Optional BattleLuck Backtrace reporting

BattleLuck includes a small server-side HTTP reporter for exceptions caught at BattleLuck-owned boundaries. It is disabled by default, does not replace Unity's logger, does not capture unrelated game/mod logs, and never writes reports to disk.

Set the nonsecret options in `BepInEx/config/BattleLuck/battleluck.json`:

```json
{
  "backtrace": {
    "enabled": true,
    "subdomain": "your-backtrace-subdomain"
  }
}
```

Supply the submission token only through the dedicated server process environment:

```powershell
$env:BATTLELUCK_BACKTRACE_SUBMISSION_TOKEN = "your-submission-token"
```

Restart the server after changing the environment. Never put the token or a full submission URL in a tracked JSON file. Use `.bl admin diagnostics errors` to inspect the in-memory queue state and `.bl admin diagnostics errors test` to submit a redacted test exception.

Reports are capped at 50 queued entries, deduplicated for 60 seconds, retried for network/429/5xx failures, and drained for at most two seconds during unload. HTTP 400 is dropped; HTTP 403 disables the reporter until reload. Payloads contain only BattleLuck context and exclude chat transcripts, credentials, inventories, raw ECS dumps, IP/authentication data, and unrelated player data.
