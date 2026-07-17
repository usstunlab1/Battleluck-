param(
    [string]$KindredPath = ".external/KindredExtract",
    [string]$OutputPath = "docs/reference/kindredextract-reference.json",
    [switch]$CloneIfMissing
)

$ErrorActionPreference = "Stop"

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = (Resolve-Path (Join-Path $scriptRoot "..")).Path

if (-not [System.IO.Path]::IsPathRooted($KindredPath)) {
    $KindredPath = Join-Path $repoRoot $KindredPath
}
if (-not [System.IO.Path]::IsPathRooted($OutputPath)) {
    $OutputPath = Join-Path $repoRoot $OutputPath
}

if (-not (Test-Path $KindredPath)) {
    if (-not $CloneIfMissing) {
        throw "KindredExtract checkout not found at '$KindredPath'. Run this script with -CloneIfMissing, or clone https://github.com/Odjit/KindredExtract there first."
    }

    if (-not (Get-Command git -ErrorAction SilentlyContinue)) {
        throw "Git is required to clone KindredExtract automatically. Install Git or clone https://github.com/Odjit/KindredExtract manually."
    }

    $parent = Split-Path -Parent $KindredPath
    New-Item -ItemType Directory -Force -Path $parent | Out-Null
    Write-Output "Cloning KindredExtract to '$KindredPath'..."
    & git clone --depth 1 https://github.com/Odjit/KindredExtract.git $KindredPath
    if ($LASTEXITCODE -ne 0) {
        throw "KindredExtract clone failed with exit code $LASTEXITCODE."
    }
}

$projectPath = Join-Path $KindredPath "KindredExtract.csproj"
if (-not (Test-Path $projectPath)) {
    throw "KindredExtract.csproj not found under '$KindredPath'."
}

[xml]$project = Get-Content $projectPath -Raw
$references = @($project.Project.ItemGroup.Reference | Where-Object { $_.Include } | ForEach-Object {
    [pscustomobject]@{
        name = [string]$_.Include
        hintPath = [string]$_.HintPath
        family = if ($_.Include -like "ProjectM*") {
            "ProjectM"
        } elseif ($_.Include -like "Unity*" -or $_.Include -like "UnityEngine*") {
            "Unity"
        } elseif ($_.Include -like "*Eos*" -or $_.Include -like "*eos*" -or $_.Include -like "*Steam*" -or $_.Include -like "*Network*") {
            "NetworkPlatform"
        } elseif ($_.Include -like "Stunlock*") {
            "Stunlock"
        } elseif ($_.Include -like "Il2Cpp*" -or $_.Include -like "BepInEx*" -or $_.Include -like "0Harmony") {
            "BepInExIl2Cpp"
        } else {
            "Other"
        }
    }
} | Sort-Object family, name)

$componentExtractorPath = Join-Path $KindredPath "ComponentExtractors.cs"
$componentTypes = @()
if (Test-Path $componentExtractorPath) {
    $componentTypes = Select-String -Path $componentExtractorPath -Pattern 'RegisterExtractor<([^>]+)>' | ForEach-Object {
        $_.Matches[0].Groups[1].Value
    } | Sort-Object -Unique
}

$systemQueryPath = Join-Path $KindredPath "SystemsQueryExtraction.cs"
$systemTypes = @()
if (Test-Path $systemQueryPath) {
    $systemTypes = Select-String -Path $systemQueryPath -Pattern 'typeof\(([^)]+)\)|GetExistingSystemManaged<([^>]+)>' | ForEach-Object {
        foreach ($match in $_.Matches) {
            if ($match.Groups[1].Success -and $match.Groups[1].Value) {
                $match.Groups[1].Value
            } elseif ($match.Groups[2].Success -and $match.Groups[2].Value) {
                $match.Groups[2].Value
            }
        }
    } | Sort-Object -Unique
}

$kindredRoot = (Resolve-Path $KindredPath).Path
$sourcePathForSnapshot = $kindredRoot
if ($kindredRoot.StartsWith($repoRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
    $sourcePathForSnapshot = $kindredRoot.Substring($repoRoot.Length).TrimStart('\', '/')
}
$sourceHits = Get-ChildItem $kindredRoot -Recurse -File -Include *.cs,*.csproj,*.md |
    Where-Object { $_.FullName -notmatch '\\.git\\' } |
    Select-String -Pattern 'EOS|Eos|Epic|Steam|Network|ServerBootstrap|ProjectM|Unity\.Entities|PrefabGUID' |
    ForEach-Object {
        [pscustomobject]@{
            file = $_.Path.Substring($kindredRoot.Length).TrimStart('\', '/')
            line = $_.LineNumber
            text = $_.Line.Trim()
        }
    }

$snapshot = [pscustomobject]@{
    source = "https://github.com/Odjit/KindredExtract"
    sourcePath = $sourcePathForSnapshot.Replace('\', '/')
    extractedAtUtc = (Get-Date).ToUniversalTime().ToString("o")
    license = "AGPL-3.0; keep source as reference unless BattleLuck accepts compatible licensing obligations."
    counts = [pscustomobject]@{
        references = $references.Count
        componentExtractorTypes = $componentTypes.Count
        systemTypes = $systemTypes.Count
        sourceHits = @($sourceHits).Count
    }
    references = $references
    componentExtractorTypes = $componentTypes
    systemTypes = $systemTypes
    sourceHits = $sourceHits
}

$outputDir = Split-Path $OutputPath -Parent
if ($outputDir) {
    New-Item -ItemType Directory -Force -Path $outputDir | Out-Null
}

$snapshot | ConvertTo-Json -Depth 8 | Set-Content -Path $OutputPath -Encoding UTF8
Write-Output "Wrote $OutputPath"
