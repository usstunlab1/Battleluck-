param(
    [string]$Reference = "docs/reference/kindredextract-reference.json",
    [string]$Output = "docs/audit/systems/allowlists",
    [string]$PrefabSource = ".external/KindredExtract/Data/Prefabs.cs"
)

$ErrorActionPreference = "Stop"
if (-not (Test-Path -LiteralPath $Reference)) {
    throw "KindredExtract reference was not found: $Reference"
}

$referenceObject = Get-Content -Raw -LiteralPath $Reference | ConvertFrom-Json
New-Item -ItemType Directory -Force -Path $Output | Out-Null

function Write-Allowlist([string]$name, [string[]]$values, [string]$sourceProperty, [string]$sourcePath) {
    $payload = [ordered]@{
        schemaVersion = 1
        generatedAtUtc = [DateTime]::UtcNow.ToString("o")
        source = "KindredExtract"
        sourcePath = $sourcePath
        sourceProperty = $sourceProperty
        enforcement = "candidate"
        verificationStatus = "reference_unverified"
        verifiedInGame = $false
        verificationNote = "Reference/source names are candidates only; confirm against a target-server KindredExtract dump before runtime use."
        entries = @($values | Where-Object { $_ } | Sort-Object -Unique)
    }
    $payload | ConvertTo-Json -Depth 3 | Set-Content -LiteralPath (Join-Path $Output $name) -Encoding utf8
}

Write-Allowlist "components.json" @($referenceObject.componentExtractorTypes) "componentExtractorTypes" $Reference
Write-Allowlist "systems.json" @($referenceObject.systemTypes) "systemTypes" $Reference

# Prefer KindredExtract's generated Prefabs.cs when the external checkout is
# available. The checked-in reference alone contains the dump command but no
# captured prefab list, so keep a truthful runtime-only placeholder otherwise.
$prefabNames = @()
if (Test-Path -LiteralPath $PrefabSource) {
    $prefabText = Get-Content -Raw -LiteralPath $PrefabSource
    $prefabNames = [regex]::Matches($prefabText, 'PrefabGUID\s+(\w+)') |
        ForEach-Object { $_.Groups[1].Value } |
        Where-Object { $_ } | Sort-Object -Unique
}
$prefabPayload = [ordered]@{
    schemaVersion = 1
    generatedAtUtc = [DateTime]::UtcNow.ToString("o")
    source = "KindredExtract"
    sourcePath = if ($prefabNames.Count -gt 0) { "KindredExtract/Data/Prefabs.cs" } else { $Reference }
    sourceProperty = if ($prefabNames.Count -gt 0) { "Data/Prefabs.cs" } else { "runtime .dump p export required" }
    enforcement = "candidate"
    verificationStatus = "reference_unverified"
    verifiedInGame = $false
    verificationNote = "Prefab source names are candidates only; confirm against a target-server .dump p export before runtime use."
    entries = @($prefabNames)
    note = if ($prefabNames.Count -gt 0) { "Generated from the KindredExtract prefab catalog." } else { "No prefab-name dump is present in the checked-in reference. Run KindredExtract .dump p on the target server and replace this file before enabling exact offline prefab checks." }
}
$prefabPayload | ConvertTo-Json -Depth 3 | Set-Content -LiteralPath (Join-Path $Output "prefabs.json") -Encoding utf8

Write-Output ("Generated {0} allowlists under {1}" -f 3, $Output)
