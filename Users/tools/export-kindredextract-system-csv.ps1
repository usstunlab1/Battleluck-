param(
    # Compatibility names used by the pinned KindredExtract regeneration
    # command in tools/README.md. The older parameter names remain supported
    # for existing local scripts.
    [string]$KindredPath = "",
    [string]$OutCsv = "",
    [string]$OutTicks = "",
    [string]$ReferencePath = "docs/reference/kindredextract-reference.json",
    [string]$TemplatePath = ".external/KindredExtract/SystemsQueryExtraction.tt",
    [string]$CsvPath = "docs/reference/kindredextract-systems.csv",
    [string]$TickCsvPath = "docs/reference/kindredextract-ticks.csv",
    [string]$PromptPath = "docs/reference/kindredextract-systems-prompt.md"
)

$ErrorActionPreference = "Stop"

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = (Resolve-Path (Join-Path $scriptRoot "..")).Path

function Resolve-RepoPath([string]$path) {
    if ([System.IO.Path]::IsPathRooted($path)) { return $path }
    return Join-Path $repoRoot $path
}

if ($PSBoundParameters.ContainsKey("OutCsv") -and -not [string]::IsNullOrWhiteSpace($OutCsv)) {
    $CsvPath = $OutCsv
}
if ($PSBoundParameters.ContainsKey("OutTicks") -and -not [string]::IsNullOrWhiteSpace($OutTicks)) {
    $TickCsvPath = $OutTicks
}
if ($PSBoundParameters.ContainsKey("KindredPath") -and -not [string]::IsNullOrWhiteSpace($KindredPath)) {
    $kindredRoot = Resolve-RepoPath $KindredPath
    $ttCandidate = Join-Path $kindredRoot "SystemsQueryExtraction.tt"
    $csCandidate = Join-Path $kindredRoot "SystemsQueryExtraction.cs"
    if (Test-Path -LiteralPath $ttCandidate) {
        $TemplatePath = $ttCandidate
    } elseif (Test-Path -LiteralPath $csCandidate) {
        $TemplatePath = $csCandidate
    } else {
        throw "KindredExtract checkout at '$kindredRoot' has no SystemsQueryExtraction.tt or SystemsQueryExtraction.cs."
    }
}

$referenceFullPath = Resolve-RepoPath $ReferencePath
$templateFullPath = Resolve-RepoPath $TemplatePath
$csvFullPath = Resolve-RepoPath $CsvPath
$tickCsvFullPath = Resolve-RepoPath $TickCsvPath
$promptFullPath = Resolve-RepoPath $PromptPath

$sourceLabel = "docs/reference/kindredextract-reference.json"
$systems = @()
if (Test-Path $templateFullPath) {
    $template = Get-Content $templateFullPath -Raw
    $systemBlock = ([regex]::Match($template, 'string\[\] systemTypes\s*=\s*\{(?s)(.*?)\};')).Groups[1].Value
    if (-not [string]::IsNullOrWhiteSpace($systemBlock)) {
        $systems = @([regex]::Matches($systemBlock, '"([^"]+)"') |
            ForEach-Object { $_.Groups[1].Value } | Sort-Object -Unique)
    }

    # The generated .cs file has one DumpSystemQueries<T> call per type rather
    # than the string[] list used by the .tt template. Support both forms so a
    # developer can point this exporter at either upstream artifact.
    if ($systems.Count -eq 0) {
        $systems = @([regex]::Matches($template, 'DumpSystemQueries\s*<\s*([^>]+)\s*>') |
            ForEach-Object { $_.Groups[1].Value.Trim() } | Sort-Object -Unique)
    }
    if ($systems.Count -gt 0) {
        $sourceLabel = "KindredExtract/$(Split-Path $templateFullPath -Leaf)"
    }
} elseif (Test-Path $referenceFullPath) {
    $snapshot = Get-Content $referenceFullPath -Raw | ConvertFrom-Json
    $systems = @($snapshot.systemTypes | ForEach-Object { [string]$_ } | Sort-Object -Unique)
} else {
    throw "Neither SystemsQueryExtraction.tt/SystemsQueryExtraction.cs nor the reference snapshot exists. Clone KindredExtract or run the reference extractor first."
}

if ($systems.Count -eq 0) {
    throw "No systems were found in the KindredExtract template or reference snapshot."
}

function Get-PurposeHint([string]$name) {
    $value = $name.ToLowerInvariant()
    if ($value -match 'ability|cast|spell') { return "ability/casting" }
    if ($value -match 'combat|damage|attack|hit|projectile|weapon') { return "combat/damage" }
    if ($value -match 'ai|behaviour|behavior|path|move|navigation') { return "AI/navigation" }
    if ($value -match 'buff|debuff|status|effect') { return "buffs/effects" }
    if ($value -match 'inventory|item|equipment|loot|container|slot') { return "inventory/items" }
    if ($value -match 'castle|building|territory|door|tile|room|servant') { return "castle/building" }
    if ($value -match 'network|steam|eos|connection|user|serverbootstrap|teleport') { return "networking" }
    if ($value -match 'save|persist|serialize|deserialize|load') { return "persistence" }
    if ($value -match 'bake|conversion|transform') { return "baking/conversion" }
    if ($value -match 'render|presentation|camera|audio|visual|ui') { return "presentation" }
    if ($value -match 'spawn|prefab|vblood|blood|unit') { return "entities/spawn" }
    if ($value -match 'sequence|event|trigger|update|timer') { return "events/timing" }
    if ($value -match 'group|barrier') { return "scheduling/group boundary" }
    return "unknown; research required"
}

function Get-TickHint([string]$name) {
    $value = $name.ToLowerInvariant()
    if ($value -match 'fixedstep') { return "fixed-step hint" }
    if ($value -match 'presentation|render|camera|audio') { return "presentation hint" }
    if ($value -match 'initializ|bake|conversion') { return "initialization/baking hint" }
    if ($value -match 'destroy|cleanup|ondestroy') { return "destroy/cleanup hint" }
    if ($value -match 'spawn|oncreate') { return "spawn lifecycle hint" }
    if ($value -match 'server') { return "server-world hint" }
    if ($value -match 'client') { return "client-world hint" }
    if ($value -match 'group|barrier') { return "system-group boundary hint" }
    if ($value -match 'update|simulation|tick|event') { return "simulation/update hint" }
    return "unknown; inspect UpdateInGroup/runtime schedule"
}

$tickDefinitions = @(
    [pscustomobject]@{ tick_semantics = "initialization"; tick_hint = "initialization/baking hint"; description = "World/entity initialization, conversion, or baking"; evidence = "UpdateInGroup attributes, world bootstrap, or live trace" },
    [pscustomobject]@{ tick_semantics = "simulation"; tick_hint = "simulation/update hint"; description = "Regular simulation/update work; frequency is not implied by the name"; evidence = "UpdateInGroup/order plus measured server interval" },
    [pscustomobject]@{ tick_semantics = "fixed_step"; tick_hint = "fixed-step hint"; description = "Fixed-step group scheduling; timestep must be measured or read from configuration"; evidence = "FixedStepSimulationSystemGroup and runtime timestep" },
    [pscustomobject]@{ tick_semantics = "presentation"; tick_hint = "presentation hint"; description = "Presentation/render/UI work; usually not a server mutation boundary"; evidence = "Presentation group and server/client world check" },
    [pscustomobject]@{ tick_semantics = "destroy_cleanup"; tick_hint = "destroy/cleanup hint"; description = "Entity destruction, cleanup, or OnDestroy lifecycle"; evidence = "cleanup system/group and entity lifecycle trace" },
    [pscustomobject]@{ tick_semantics = "spawn_lifecycle"; tick_hint = "spawn lifecycle hint"; description = "Entity creation/spawn lifecycle work"; evidence = "spawn system/group and live entity creation trace" },
    [pscustomobject]@{ tick_semantics = "server_world"; tick_hint = "server-world hint"; description = "Name suggests server ownership; verify the actual world"; evidence = "WorldSystemFilter and live server-world lookup" },
    [pscustomobject]@{ tick_semantics = "client_world"; tick_hint = "client-world hint"; description = "Name suggests client ownership; do not use for server actions without proof"; evidence = "WorldSystemFilter and live client-world lookup" },
    [pscustomobject]@{ tick_semantics = "group_barrier"; tick_hint = "system-group boundary hint"; description = "System group or barrier boundary rather than an independently ticking system"; evidence = "group membership, ordering, and barrier type" },
    [pscustomobject]@{ tick_semantics = "unknown"; tick_hint = "unknown; inspect UpdateInGroup/runtime schedule"; description = "No safe timing classification from the name"; evidence = "assembly metadata, KindredExtract dump, or measured runtime trace" }
)

function Get-TickSemantics([string]$hint) {
    $definition = $tickDefinitions | Where-Object { $_.tick_hint -eq $hint } | Select-Object -First 1
    if ($null -ne $definition) { return $definition.tick_semantics }
    return "unknown"
}

$rows = foreach ($system in $systems) {
    $lastDot = $system.LastIndexOf('.')
    $namespace = if ($lastDot -gt 0) { $system.Substring(0, $lastDot) } else { "" }
    $typeName = if ($lastDot -gt 0) { $system.Substring($lastDot + 1) } else { $system }
    $lower = $system.ToLowerInvariant()
    $side = if ($lower -match '(^|[_\.])server($|[_\.])|server$') { "server" }
        elseif ($lower -match '(^|[_\.])client($|[_\.])|client$') { "client" }
        else { "shared/unknown" }
    $kind = if ($typeName -match 'Group$') { "group" }
        elseif ($typeName -match 'Barrier$') { "barrier" }
        elseif ($typeName -match 'System$') { "system" }
        else { "type/reference" }

    [pscustomobject]@{
        system_name = $system
        namespace = $namespace
        type_name = $typeName
        system_kind = $kind
        side_hint = $side
        purpose_hint = Get-PurposeHint $system
        tick_hint = Get-TickHint $system
        tick_semantics = Get-TickSemantics (Get-TickHint $system)
        evidence = "type name heuristic only; verify Unity UpdateInGroup/order and live world"
        needs_runtime_verification = $true
        source = "https://github.com/Odjit/KindredExtract/blob/main/SystemsQueryExtraction.tt"
    }
}

$csvDirectory = Split-Path $csvFullPath -Parent
$tickCsvDirectory = Split-Path $tickCsvFullPath -Parent
$promptDirectory = Split-Path $promptFullPath -Parent
New-Item -ItemType Directory -Force -Path $csvDirectory, $tickCsvDirectory, $promptDirectory | Out-Null
$rows | Export-Csv -Path $csvFullPath -NoTypeInformation -Encoding UTF8

$tickRows = foreach ($definition in $tickDefinitions) {
    $matching = @($rows | Where-Object { $_.tick_hint -eq $definition.tick_hint })
    [pscustomobject]@{
        tick_semantics = $definition.tick_semantics
        tick_hint = $definition.tick_hint
        system_count = $matching.Count
        example_systems = (($matching | Select-Object -First 8 | ForEach-Object { $_.system_name }) -join "; ")
        description = $definition.description
        verification_required = $definition.evidence
        source = "https://github.com/Odjit/KindredExtract/blob/main/SystemsQueryExtraction.tt"
    }
}
$tickRows | Export-Csv -Path $tickCsvFullPath -NoTypeInformation -Encoding UTF8

$tickMarkdown = ($tickDefinitions | ForEach-Object {
    "| ``$($_.tick_semantics)`` | $($_.tick_hint) | $($_.description) | $($_.evidence) |"
}) -join "`n"

$prompt = @'
# KindredExtract Unity system/tick research prompt

You are reviewing `kindredextract-systems.csv` and `kindredextract-ticks.csv`, generated from the
[Odjit/KindredExtract](https://github.com/Odjit/KindredExtract) reference list
for a V Rising Unity ECS server. The CSV contains SYSTEM_COUNT_PLACEHOLDER system/type names and
name-based hints only; it is not proof of runtime scheduling. The tick CSV is a
classification list, not a measured frequency table.

## Tick semantics list

Use these labels as research categories only. An exact rate must come from Unity
group metadata, server configuration, or a live measurement:

| semantics | name hint | meaning | evidence required |
|---|---|---|---|
TICK_LIST_PLACEHOLDER

For each system, research and verify:

1. The full Unity/ProjectM type and whether it exists in the server world.
2. The actual purpose and the components/queries it reads or writes.
3. The real update group (`InitializationSystemGroup`, simulation group,
   fixed-step group, presentation group, or a ProjectM group), ordering, and
   whether it runs server, client, or shared.
4. The tick semantics label from the tick list, the measured or configured
   interval (if known), and whether work is once-only, per-frame, fixed-step,
   event-driven, spawn/cleanup lifecycle, or unknown.
5. Whether it is safe to observe from a server plugin and the correct tick/main
   thread boundary for an approved BattleLuck action.

Do not infer an exact tick rate from a name. Mark unknown values as `unknown` and
cite the assembly/source or a live KindredExtract dump. Keep the output bounded:
return a corrected CSV with the original `system_name`, verified purpose,
`world`, `update_group`, `order`, `tick_semantics`, `tick_rate_hz`,
`tick_source`, `observed_interval_ms`, `evidence`, and `confidence` columns.
Never propose arbitrary reflection or direct mutation of an unverified native
system.

Timing for BattleLuck sequences must use validated `wait:<seconds>` and
`tick:<event-second>` markers; the server main-thread dispatcher remains the
mutation boundary.
'@
$prompt = $prompt.Replace("SYSTEM_COUNT_PLACEHOLDER", $systems.Count.ToString())
$prompt = $prompt.Replace("TICK_LIST_PLACEHOLDER", $tickMarkdown)
Set-Content -Path $promptFullPath -Value $prompt -Encoding UTF8

Write-Output "Wrote $csvFullPath ($($rows.Count) systems from $sourceLabel)"
Write-Output "Wrote $tickCsvFullPath ($($tickRows.Count) tick classifications)"
Write-Output "Wrote $promptFullPath"
