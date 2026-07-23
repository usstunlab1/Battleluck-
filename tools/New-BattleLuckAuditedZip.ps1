[CmdletBinding()]
param(
    [string]$RepositoryRoot = (Split-Path -Parent $PSScriptRoot),
    [string]$EvidenceDirectory = "",
    [string]$DestinationPath = ""
)

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.IO.Compression

$root = [System.IO.Path]::GetFullPath($RepositoryRoot)
$manifest = Get-Content -LiteralPath (Join-Path $root "manifest.json") -Raw | ConvertFrom-Json
$version = [string]$manifest.version_number
if ([string]::IsNullOrWhiteSpace($EvidenceDirectory)) {
    $EvidenceDirectory = Join-Path $root "artifacts\audit\latest"
}
if ([string]::IsNullOrWhiteSpace($DestinationPath)) {
    $DestinationPath = Join-Path $root "dist\BattleLuck-Audited-$version.zip"
}

$evidence = [System.IO.Path]::GetFullPath($EvidenceDirectory)
$destination = [System.IO.Path]::GetFullPath($DestinationPath)
$stage = [System.IO.Path]::GetFullPath((Join-Path $root "artifacts\audited-stage"))
$expectedStage = [System.IO.Path]::GetFullPath((Join-Path $root "artifacts\audited-stage"))
if ($stage -ne $expectedStage) {
    throw "Refusing to clean unexpected audit staging directory: $stage"
}
if (-not (Test-Path -LiteralPath (Join-Path $evidence "evidence-manifest.json"))) {
    throw "Run tools/Invoke-BattleLuckAudit.ps1 before creating the audited ZIP."
}

if (Test-Path -LiteralPath $stage) {
    Remove-Item -LiteralPath $stage -Recurse -Force
}
$repositoryStage = Join-Path $stage "repository"
$evidenceStage = Join-Path $stage "evidence"
$releaseStage = Join-Path $stage "release"
New-Item -ItemType Directory -Path $repositoryStage, $evidenceStage, $releaseStage -Force | Out-Null

$excludedSegments = @(
    ".git", ".idea", ".vs", "bin", "obj", "obj-readme", "obj-tests",
    "dist", "artifacts", "PJ", "node_modules", "%CONFIG_PATH%"
)
$sourceFiles = Get-ChildItem -LiteralPath $root -Recurse -Force -File |
    Where-Object {
        $relative = [System.IO.Path]::GetRelativePath($root, $_.FullName)
        $segments = $relative -split "[\\/]"
        -not ($segments | Where-Object { $excludedSegments -contains $_ }) -and
        $_.Extension -ne ".zip" -and
        $_.Extension -ne ".log"
    }

foreach ($file in $sourceFiles) {
    $relative = [System.IO.Path]::GetRelativePath($root, $file.FullName)
    $target = Join-Path $repositoryStage $relative
    New-Item -ItemType Directory -Path (Split-Path -Parent $target) -Force | Out-Null
    Copy-Item -LiteralPath $file.FullName -Destination $target
}

Copy-Item -Path (Join-Path $evidence "*") -Destination $evidenceStage -Recurse -Force
$releaseZip = Join-Path $root "dist\BattleLuck-server-$version.zip"
$releaseHash = "$releaseZip.sha256"
Copy-Item -LiteralPath $releaseZip, $releaseHash -Destination $releaseStage

$archiveEntries = Get-ChildItem -LiteralPath $stage -Recurse -File |
    Sort-Object FullName |
    ForEach-Object {
        [ordered]@{
            path = [System.IO.Path]::GetRelativePath($stage, $_.FullName).Replace("\", "/")
            bytes = $_.Length
            sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
        }
    }
[ordered]@{
    formatVersion = 1
    package = "BattleLuck audited handoff"
    version = $version
    excludedGeneratedRoots = $excludedSegments
    files = @($archiveEntries)
} | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $stage "AUDIT-MANIFEST.json") -Encoding utf8

if (Test-Path -LiteralPath $destination) {
    Remove-Item -LiteralPath $destination -Force
}
New-Item -ItemType Directory -Path (Split-Path -Parent $destination) -Force | Out-Null
$fixedTimestamp = [DateTimeOffset]::Parse("2020-01-01T00:00:00Z")
$archive = [System.IO.Compression.ZipFile]::Open(
    $destination,
    [System.IO.Compression.ZipArchiveMode]::Create)
try {
    foreach ($file in Get-ChildItem -LiteralPath $stage -Recurse -File | Sort-Object FullName) {
        $relative = [System.IO.Path]::GetRelativePath($stage, $file.FullName).Replace("\", "/")
        $entry = $archive.CreateEntry($relative, [System.IO.Compression.CompressionLevel]::Optimal)
        $entry.LastWriteTime = $fixedTimestamp
        $entryStream = $entry.Open()
        $fileStream = [System.IO.File]::OpenRead($file.FullName)
        try {
            $fileStream.CopyTo($entryStream)
        }
        finally {
            $fileStream.Dispose()
            $entryStream.Dispose()
        }
    }
}
finally {
    $archive.Dispose()
}

$hash = (Get-FileHash -LiteralPath $destination -Algorithm SHA256).Hash.ToLowerInvariant()
"$hash  $([System.IO.Path]::GetFileName($destination))" |
    Set-Content -LiteralPath "$destination.sha256" -Encoding ascii
[ordered]@{
    path = $destination
    bytes = (Get-Item -LiteralPath $destination).Length
    sha256 = $hash
    sourceFiles = $sourceFiles.Count
    evidenceFiles = (Get-ChildItem -LiteralPath $evidenceStage -Recurse -File).Count
} | ConvertTo-Json
