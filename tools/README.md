# BattleLuck helper tools

These files are optional developer helpers. When `BattleLuck.dll` is loaded for
the first time, the plugin copies missing files from its embedded resources to
`BepInEx/config/BattleLuck/tools/` and leaves existing files untouched.

`extract-kindredextract-reference.ps1` refreshes the checked-in ProjectM/Unity
reference snapshot. It is not executed by the server and does not install the
KindredExtract mod. Run it from any directory with `-CloneIfMissing` to download
the ignored upstream checkout automatically:

```powershell
powershell -ExecutionPolicy Bypass -File tools/extract-kindredextract-reference.ps1 -CloneIfMissing
```

Use the actual [Odjit/KindredExtract](https://github.com/Odjit/KindredExtract)
release separately when you need its in-game dump commands.

To generate the Unity system list, tick-semantics list, and research prompt:

```powershell
powershell -ExecutionPolicy Bypass -File tools/export-kindredextract-system-csv.ps1
```

This writes `docs/reference/kindredextract-systems.csv`,
`docs/reference/kindredextract-ticks.csv`, and
`docs/reference/kindredextract-systems-prompt.md`. Tick labels are research
categories only; exact rates still require Unity group metadata or a live dump.

### Pin KindredExtract and regenerate exports + allowlists

Pin the developer-only KindredExtract checkout before regenerating the
canonical CSV exports and the reference candidate allowlists:

```plaintext
KECOMMIT=<commit-hash>; git -C ../KindredExtract fetch && git -C ../KindredExtract checkout $KECOMMIT && pwsh -File tools/export-kindredextract-system-csv.ps1 -KindredPath ../KindredExtract -OutCsv docs/reference/kindredextract-systems.csv -OutTicks docs/reference/kindredextract-ticks.csv; pwsh -File tools/export-kindredextract-allowlists.ps1 -SystemsCsv docs/reference/kindredextract-systems.csv -TicksCsv docs/reference/kindredextract-ticks.csv -OutDir docs/audit/systems/allowlists
```

Replace `<commit-hash>` with the validated KindredExtract commit. The first
script writes the canonical system and tick CSVs (and refreshes the research
prompt). The second writes `prefabs.allowlist.txt`,
`components.allowlist.txt`, and `systems.allowlist.txt`, keeps the JSON copies
used by the runtime validator, and echoes a deterministic version hash to the
terminal (also saved as `version.hash`). These files are not in-game
verification: confirm IDs on the target server before promotion to
`config/BattleLuck/sequences/uuid_catalog.json`.

The example assumes a sibling checkout at `../KindredExtract`; use the actual
checkout path when needed. Allowlists are developer/reference data and must be
reviewed against the target V Rising build before enabling strict production
validation. Sequence UUID promotion rules are documented in
[`docs/reference/sequence-uuid-catalog.md`](../docs/reference/sequence-uuid-catalog.md);
an allowlist entry alone never qualifies as `in_game_verified`.
