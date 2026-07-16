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
