param(
    [string]$SystemsCsv = "docs/reference/kindredextract-systems.csv",
    [string]$TicksCsv = "docs/reference/kindredextract-ticks.csv",
    [string]$OutDir = "docs/audit/systems/allowlists"
)

$ErrorActionPreference = "Stop"
$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = (Resolve-Path (Join-Path $scriptRoot "..")).Path

function Resolve-InputPath([string]$path) {
    if ([System.IO.Path]::IsPathRooted($path)) { return $path }
    return Join-Path $repoRoot $path
}

function Get-UniqueValues([object[]]$values) {
    @($values |
        ForEach-Object { [string]$_ } |
        Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
        Sort-Object -Unique)
}

function Write-List([string]$path, [string[]]$values) {
    $ordered = @(Get-UniqueValues $values)
    $header = "# BattleLuck reference allowlist; not verified in-game. Confirm against a target-server KindredExtract dump before runtime use.`n"
    $body = if ($ordered.Count -gt 0) { ($ordered -join "`n") + "`n" } else { "" }
    $text = $header + $body
    [System.IO.File]::WriteAllText($path, $text, [System.Text.UTF8Encoding]::new($false))
    return $ordered
}

function Read-JsonEntries([string]$path) {
    if (-not (Test-Path -LiteralPath $path)) { return @() }
    try {
        $json = Get-Content -Raw -LiteralPath $path | ConvertFrom-Json
        return @(Get-UniqueValues @($json.entries))
    } catch {
        return @()
    }
}

$systemsPath = Resolve-InputPath $SystemsCsv
$ticksPath = Resolve-InputPath $TicksCsv
$outputPath = Resolve-InputPath $OutDir
if (-not (Test-Path -LiteralPath $systemsPath)) { throw "Systems CSV not found: $systemsPath" }
if (-not (Test-Path -LiteralPath $ticksPath)) { throw "Ticks CSV not found: $ticksPath" }
New-Item -ItemType Directory -Force -Path $outputPath | Out-Null

$systemRows = @(Import-Csv -LiteralPath $systemsPath)
$tickRows = @(Import-Csv -LiteralPath $ticksPath)
$systems = Get-UniqueValues @($systemRows | ForEach-Object { $_.system_name })
if ($systems.Count -eq 0) { throw "No system_name values were found in $systemsPath" }

$referencePath = Join-Path (Split-Path $systemsPath -Parent) "kindredextract-reference.json"
if (-not (Test-Path -LiteralPath $referencePath)) {
    $referencePath = Join-Path $repoRoot "docs/reference/kindredextract-reference.json"
}
$components = @()
if (Test-Path -LiteralPath $referencePath) {
    $reference = Get-Content -Raw -LiteralPath $referencePath | ConvertFrom-Json
    $components = Get-UniqueValues @($reference.componentExtractorTypes)
}
if ($components.Count -eq 0) {
    $components = Read-JsonEntries (Join-Path $outputPath "components.json")
}
if ($components.Count -eq 0) { throw "No componentExtractorTypes were found in $referencePath or the existing component allowlist." }

$prefabSource = Join-Path $repoRoot ".external/KindredExtract/Data/Prefabs.cs"
$prefabs = @()
if (Test-Path -LiteralPath $prefabSource) {
    $prefabText = Get-Content -Raw -LiteralPath $prefabSource
    $prefabs = Get-UniqueValues @([regex]::Matches($prefabText, 'PrefabGUID\s+([A-Za-z_][A-Za-z0-9_]*)\s*=') |
        ForEach-Object { $_.Groups[1].Value })
}
if ($prefabs.Count -eq 0) {
    $prefabs = Read-JsonEntries (Join-Path $outputPath "prefabs.json")
}
if ($prefabs.Count -eq 0) {
    throw "No prefab names were found. Provide KindredExtract/Data/Prefabs.cs or a captured prefabs.json allowlist."
}

$systems = Write-List (Join-Path $outputPath "systems.allowlist.txt") $systems
$components = Write-List (Join-Path $outputPath "components.allowlist.txt") $components
$prefabs = Write-List (Join-Path $outputPath "prefabs.allowlist.txt") $prefabs

function Write-JsonAllowlist([string]$name, [string[]]$entries, [string]$sourcePath, [string]$sourceProperty) {
    [ordered]@{
        schemaVersion = 1
        generatedAtUtc = [DateTime]::UtcNow.ToString("o")
        source = "KindredExtract"
        sourcePath = $sourcePath
        sourceProperty = $sourceProperty
        enforcement = "candidate"
        verificationStatus = "reference_unverified"
        verifiedInGame = $false
        verificationNote = "Names/types came from source or reference exports; confirm against a target-server in-game dump before treating them as runtime verified."
        entries = @($entries)
    } | ConvertTo-Json -Depth 3 | Set-Content -LiteralPath (Join-Path $outputPath $name) -Encoding utf8
}

Write-JsonAllowlist "systems.json" $systems $SystemsCsv "system_name"
Write-JsonAllowlist "components.json" $components $referencePath "componentExtractorTypes"
Write-JsonAllowlist "prefabs.json" $prefabs ".external/KindredExtract/Data/Prefabs.cs" "PrefabGUID fields"

$hashInput = @(
    "systems", ($systems -join "`n"),
    "components", ($components -join "`n"),
    "prefabs", ($prefabs -join "`n"),
    "ticks", (Get-Content -Raw -LiteralPath $ticksPath)
) -join "`n"
$sha = [Security.Cryptography.SHA256]::Create()
try {
    $versionHash = ([BitConverter]::ToString($sha.ComputeHash([Text.Encoding]::UTF8.GetBytes($hashInput)))).Replace("-", "").ToLowerInvariant()
} finally {
    $sha.Dispose()
}
[System.IO.File]::WriteAllText((Join-Path $outputPath "version.hash"), "$versionHash`n", [Text.UTF8Encoding]::new($false))

Write-Output ("Generated systems.allowlist.txt ({0}), components.allowlist.txt ({1}), prefabs.allowlist.txt ({2})" -f $systems.Count, $components.Count, $prefabs.Count)
Write-Output ("KindredExtract allowlist version: $versionHash")
Write-Output ("Tick CSV rows included in version: $($tickRows.Count)")
