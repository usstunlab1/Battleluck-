# Known issues and residual risk

Release decision: **NO-GO for production**. The local build and package are
healthy, but the following gates cannot be represented as successful without a
controlled dedicated-server run.

## Release blockers

1. Compile-time game references are `1.1.11-r96495-b8`; the observed server is
   V Rising `1.1.13`. Rebuild against the matching reference set and repeat the
   live regression suite.
2. Two castle ownership tests are intentionally skipped outside a live ECS
   world.
3. Clean server startup, two-player join/reconnect, event start/end, rollback,
   and four-hour soak tests remain unexecuted.
4. Automated coverage is 7.04% line / 3.81% branch. High-risk ECS, session,
   combat, persistence-failure, and disconnect paths need focused coverage.

## Non-blocking or conditional issues

- `manifest.json` requests VampireCommandFramework 0.11.0 while compilation
  uses 0.10.4. The observed live server loaded the plugin, but the versions
  should be aligned.
- Linux has not been tested. Current evidence is Windows-only.
- NPC wander behavior uses shared, unseeded randomness and cannot reproduce an
  exact movement sequence.
- Disk-full and permission-denied fault behavior has not been exercised.
- VCF and VampireReferenceAssemblies NuGet metadata do not declare SPDX license
  expressions; confirm their source licenses before public distribution.
- No public API signature baseline exists for extension authors.
- The local Ollama endpoint at `127.0.0.1:11434` was unavailable. BattleLuck
  falls back locally, but model-backed answers require an operator-managed
  provider installation.
- Current artifact provenance records a dirty working tree. Commit or otherwise
  freeze the reviewed source before a signed release.

Machine-readable records: `findings.json` and `findings.csv`.
