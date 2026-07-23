[CmdletBinding()]
param(
    [string]$RepositoryRoot = (Split-Path -Parent $PSScriptRoot),
    [string]$EvidenceDirectory = ""
)

$ErrorActionPreference = "Stop"
$root = [System.IO.Path]::GetFullPath($RepositoryRoot)
if ([string]::IsNullOrWhiteSpace($EvidenceDirectory)) {
    $EvidenceDirectory = Join-Path $root "artifacts\audit\latest"
}
$evidence = [System.IO.Path]::GetFullPath($EvidenceDirectory)
New-Item -ItemType Directory -Path $evidence -Force | Out-Null

$reproductionRoot = Join-Path ([System.IO.Path]::GetTempPath()) (
    "BattleLuck-Reproduction-" + [Guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Path $reproductionRoot | Out-Null

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
    $target = Join-Path $reproductionRoot $relative
    New-Item -ItemType Directory -Path (Split-Path -Parent $target) -Force | Out-Null
    Copy-Item -LiteralPath $file.FullName -Destination $target
}

$results = [System.Collections.Generic.List[object]]::new()
function Invoke-ReproductionStep {
    param([string]$Name, [string[]]$Arguments)

    Push-Location $reproductionRoot
    try {
        $output = & dotnet @Arguments 2>&1
        $exitCode = $LASTEXITCODE
    }
    finally {
        Pop-Location
    }

    $logName = "clean-reproduction-$($Name.ToLowerInvariant()).log"
    @($output | ForEach-Object { $_.ToString() }) |
        Set-Content -LiteralPath (Join-Path $evidence $logName) -Encoding utf8
    $results.Add([ordered]@{
        name = $Name
        exitCode = $exitCode
        status = $(if ($exitCode -eq 0) { "passed" } else { "failed" })
        log = $logName
    })
    if ($exitCode -ne 0) {
        throw "Clean reproduction step '$Name' failed."
    }
}

Invoke-ReproductionStep "restore" @("restore", "BattleLuck.sln", "--locked-mode")
Invoke-ReproductionStep "build" @("build", "BattleLuck.sln", "-c", "Release", "--no-restore")
Invoke-ReproductionStep "test" @(
    "test", "BattleLuck.Tests/BattleLuck.Tests.csproj",
    "-c", "Release", "--no-build", "--no-restore",
    "--logger", "console;verbosity=minimal"
)

$report = [ordered]@{
    formatVersion = 1
    cleanDirectory = $reproductionRoot
    sourceFiles = $sourceFiles.Count
    result = "passed"
    steps = @($results)
    limitation = "Separate directory on the same Windows host; not independent hardware or OS."
}
$reportPath = Join-Path $evidence "clean-reproduction.json"
$report | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $reportPath -Encoding utf8
Write-Output $reportPath
