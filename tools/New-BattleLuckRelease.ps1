[CmdletBinding()]
param(
    [string]$RepositoryRoot = (Split-Path -Parent $PSScriptRoot),
    [string]$Configuration = "Release",
    [string]$OutputDirectory = ""
)

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.IO.Compression

$root = [System.IO.Path]::GetFullPath($RepositoryRoot)
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $root "dist"
}
$output = [System.IO.Path]::GetFullPath($OutputDirectory)
$manifestPath = Join-Path $root "manifest.json"
$packageManifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
$version = [string]$packageManifest.version_number
$stage = Join-Path $root "artifacts\release-stage\BattleLuck"
$expectedStage = [System.IO.Path]::GetFullPath((Join-Path $root "artifacts\release-stage\BattleLuck"))

if ([System.IO.Path]::GetFullPath($stage) -ne $expectedStage) {
    throw "Refusing to clean unexpected staging directory: $stage"
}

if (Test-Path -LiteralPath $stage) {
    Remove-Item -LiteralPath $stage -Recurse -Force
}
New-Item -ItemType Directory -Path $stage -Force | Out-Null

$payload = @(
    "bin\$Configuration\net6.0\BattleLuck.dll",
    "bin\$Configuration\net6.0\BouncyCastle.Cryptography.dll",
    "bin\$Configuration\net6.0\HookDOTS.API.dll",
    "manifest.json",
    "README.md",
    "CHANGELOG.md",
    "LICENSE",
    "THIRD_PARTY_NOTICES.md",
    "icon.png"
)

foreach ($relativePath in $payload) {
    $source = Join-Path $root $relativePath
    if (-not (Test-Path -LiteralPath $source -PathType Leaf)) {
        throw "Release payload is missing: $source"
    }
    Copy-Item -LiteralPath $source -Destination $stage
}

$sourceRevision = (& git -C $root rev-parse HEAD).Trim()
if ($LASTEXITCODE -ne 0) {
    $sourceRevision = "unknown"
}
$dirty = [bool](& git -C $root status --porcelain)

$payloadEntries = @(
    Get-ChildItem -LiteralPath $stage -File |
        Sort-Object Name |
        ForEach-Object {
            [ordered]@{
                path = $_.Name
                bytes = $_.Length
                sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
            }
        }
)

$centralVersions = [xml](Get-Content -LiteralPath (Join-Path $root "Directory.Packages.props") -Raw)
$referenceVersion = [string](
    $centralVersions.Project.ItemGroup.PackageVersion |
        Where-Object Include -eq "VampireReferenceAssemblies" |
        Select-Object -First 1
).Version

$artifactManifest = [ordered]@{
    formatVersion = 1
    package = [ordered]@{
        name = [string]$packageManifest.name
        version = $version
        targetFramework = "net6.0"
        serverOnly = $true
    }
    source = [ordered]@{
        revision = $sourceRevision
        dirtyWorkingTree = $dirty
    }
    compatibility = [ordered]@{
        vampireReferenceAssemblies = $referenceVersion
        liveServerObserved = "1.1.13"
        runtimeValidationRequired = $true
    }
    files = $payloadEntries
}
$artifactManifest |
    ConvertTo-Json -Depth 10 |
    Set-Content -LiteralPath (Join-Path $stage "artifact-manifest.json") -Encoding utf8

$hashLines = Get-ChildItem -LiteralPath $stage -File |
    Sort-Object Name |
    ForEach-Object {
        "$((Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant())  $($_.Name)"
    }
$hashLines | Set-Content -LiteralPath (Join-Path $stage "SHA256SUMS.txt") -Encoding ascii

$forbidden = Get-ChildItem -LiteralPath $stage -Recurse -File |
    Where-Object Name -Match "(client|universe|node_modules|\.pdb$)"
if ($forbidden) {
    throw "Client, sidecar, debug, or Node content entered the server package: $($forbidden.Name -join ', ')"
}

$zipPath = Join-Path $output "BattleLuck-server-$version.zip"
if (Test-Path -LiteralPath $zipPath) {
    Remove-Item -LiteralPath $zipPath -Force
}

$fixedTimestamp = [DateTimeOffset]::Parse("2020-01-01T00:00:00Z")
$archive = [System.IO.Compression.ZipFile]::Open(
    $zipPath,
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

$zipHash = (Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash.ToLowerInvariant()
"$zipHash  $([System.IO.Path]::GetFileName($zipPath))" |
    Set-Content -LiteralPath "$zipPath.sha256" -Encoding ascii

[ordered]@{
    path = $zipPath
    bytes = (Get-Item -LiteralPath $zipPath).Length
    sha256 = $zipHash
    entries = (Get-ChildItem -LiteralPath $stage -Recurse -File).Count
} | ConvertTo-Json
