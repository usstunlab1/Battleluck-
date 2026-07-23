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

$steps = [System.Collections.Generic.List[object]]::new()

function Invoke-AuditProcess {
    param(
        [string]$Name,
        [string]$Executable,
        [string[]]$Arguments,
        [int[]]$AcceptedExitCodes = @(0)
    )

    $safeName = $Name.ToLowerInvariant().Replace(" ", "-")
    $logPath = Join-Path $evidence "$safeName.log"
    Push-Location $root
    try {
        $output = & $Executable @Arguments 2>&1
        $exitCode = $LASTEXITCODE
    }
    finally {
        Pop-Location
    }

    @($output | ForEach-Object { $_.ToString() }) | Set-Content -LiteralPath $logPath -Encoding utf8
    $passed = $AcceptedExitCodes -contains $exitCode
    $steps.Add([ordered]@{
        name = $Name
        status = $(if ($passed) { "passed" } else { "failed" })
        exitCode = $exitCode
        log = [System.IO.Path]::GetRelativePath($evidence, $logPath).Replace("\", "/")
    })
    if (-not $passed) {
        throw "Audit step '$Name' failed with exit code $exitCode. See $logPath"
    }
}

Invoke-AuditProcess "locked restore" "dotnet" @(
    "restore", "BattleLuck.sln", "--locked-mode"
)
Invoke-AuditProcess "release build" "dotnet" @(
    "build", "BattleLuck.sln", "-c", "Release", "--no-restore"
)

$testResults = Join-Path $evidence "test-results"
if (Test-Path -LiteralPath $testResults) {
    $resolvedTestResults = [System.IO.Path]::GetFullPath($testResults)
    if (-not $resolvedTestResults.StartsWith($evidence + [System.IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to clean test results outside the evidence directory: $resolvedTestResults"
    }
    Remove-Item -LiteralPath $resolvedTestResults -Recurse -Force
}
New-Item -ItemType Directory -Path $testResults -Force | Out-Null
Invoke-AuditProcess "tests and coverage" "dotnet" @(
    "test", "BattleLuck.Tests/BattleLuck.Tests.csproj",
    "-c", "Release", "--no-build", "--no-restore",
    "--results-directory", $testResults,
    "--logger", "trx;LogFileName=battleluck-tests.trx",
    "--collect", "XPlat Code Coverage"
)

$vulnerabilityPath = Join-Path $evidence "nuget-vulnerabilities.json"
Push-Location $root
try {
    $vulnerabilityJson = & dotnet list BattleLuck.sln package --vulnerable --include-transitive --format json
    $vulnerabilityExit = $LASTEXITCODE
}
finally {
    Pop-Location
}
$vulnerabilityJson | Set-Content -LiteralPath $vulnerabilityPath -Encoding utf8
if ($vulnerabilityExit -ne 0) {
    throw "NuGet vulnerability scan failed with exit code $vulnerabilityExit."
}
$vulnerabilityDocument = Get-Content -LiteralPath $vulnerabilityPath -Raw | ConvertFrom-Json
$vulnerablePackages = @(
    foreach ($project in $vulnerabilityDocument.projects) {
        foreach ($framework in @($project.frameworks)) {
            @($framework.topLevelPackages) + @($framework.transitivePackages) |
                Where-Object { $_.vulnerabilities }
        }
    }
)
$steps.Add([ordered]@{
    name = "NuGet vulnerability scan"
    status = $(if ($vulnerablePackages.Count -eq 0) { "passed" } else { "failed" })
    findings = $vulnerablePackages.Count
    log = "nuget-vulnerabilities.json"
})
if ($vulnerablePackages.Count -ne 0) {
    throw "NuGet reported $($vulnerablePackages.Count) vulnerable package entries."
}

$jsonFailures = [System.Collections.Generic.List[string]]::new()
$jsonFiles = Get-ChildItem -LiteralPath (Join-Path $root "config") -Recurse -File -Filter "*.json"
foreach ($jsonFile in $jsonFiles) {
    try {
        Get-Content -LiteralPath $jsonFile.FullName -Raw | ConvertFrom-Json | Out-Null
    }
    catch {
        $jsonFailures.Add([System.IO.Path]::GetRelativePath($root, $jsonFile.FullName))
    }
}
[ordered]@{
    filesChecked = $jsonFiles.Count
    failures = @($jsonFailures)
} | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $evidence "config-json-validation.json") -Encoding utf8
$steps.Add([ordered]@{
    name = "configuration JSON parse"
    status = $(if ($jsonFailures.Count -eq 0) { "passed" } else { "failed" })
    filesChecked = $jsonFiles.Count
    findings = $jsonFailures.Count
    log = "config-json-validation.json"
})
if ($jsonFailures.Count -ne 0) {
    throw "Configuration JSON validation failed for $($jsonFailures.Count) files."
}

Invoke-AuditProcess "webhook credential scan" "rg" @(
    "-n",
    "--glob", "!.git/**",
    "--glob", "!bin/**",
    "--glob", "!obj/**",
    "--glob", "!dist/**",
    "--glob", "!PJ/**",
    "--glob", "!docs/**",
    "https?://(discord(app)?\.com|discord\.com)/api/webhooks/[A-Za-z0-9_./-]+",
    "."
) @(1)

Invoke-AuditProcess "whitespace validation" "git" @("diff", "--check")

$sbomPath = Join-Path $evidence "sbom.cdx.json"
& (Join-Path $root "tools\Export-BattleLuckSbom.ps1") -RepositoryRoot $root -OutputPath $sbomPath | Out-Null
$steps.Add([ordered]@{
    name = "CycloneDX dependency inventory"
    status = "passed"
    log = "sbom.cdx.json"
})

$releaseJson = & (Join-Path $root "tools\New-BattleLuckRelease.ps1") -RepositoryRoot $root
$release = $releaseJson | ConvertFrom-Json
$release | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $evidence "release-artifact.json") -Encoding utf8
$steps.Add([ordered]@{
    name = "deterministic server package"
    status = "passed"
    sha256 = $release.sha256
    bytes = $release.bytes
    log = "release-artifact.json"
})

$secondReleaseJson = & (Join-Path $root "tools\New-BattleLuckRelease.ps1") -RepositoryRoot $root
$secondRelease = $secondReleaseJson | ConvertFrom-Json
$reproducible = $release.sha256 -eq $secondRelease.sha256
$steps.Add([ordered]@{
    name = "release reproducibility"
    status = $(if ($reproducible) { "passed" } else { "failed" })
    firstSha256 = $release.sha256
    secondSha256 = $secondRelease.sha256
})
if (-not $reproducible) {
    throw "Two consecutive server packages produced different SHA-256 hashes."
}

$cleanInstall = Join-Path $evidence "clean-install"
if (Test-Path -LiteralPath $cleanInstall) {
    Remove-Item -LiteralPath $cleanInstall -Recurse -Force
}
Expand-Archive -LiteralPath $release.path -DestinationPath $cleanInstall
$checksumFailures = [System.Collections.Generic.List[string]]::new()
foreach ($line in Get-Content -LiteralPath (Join-Path $cleanInstall "SHA256SUMS.txt")) {
    if ($line -notmatch "^([a-f0-9]{64})  (.+)$") {
        $checksumFailures.Add("Malformed checksum line: $line")
        continue
    }

    $payloadFile = Join-Path $cleanInstall $Matches[2]
    if (-not (Test-Path -LiteralPath $payloadFile -PathType Leaf)) {
        $checksumFailures.Add("Missing payload: $($Matches[2])")
        continue
    }

    $actualHash = (Get-FileHash -LiteralPath $payloadFile -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($actualHash -ne $Matches[1]) {
        $checksumFailures.Add("Checksum mismatch: $($Matches[2])")
    }
}
[ordered]@{
    archive = $release.path
    entries = (Get-ChildItem -LiteralPath $cleanInstall -Recurse -File).Count
    failures = @($checksumFailures)
} | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $evidence "clean-install-validation.json") -Encoding utf8
$steps.Add([ordered]@{
    name = "clean package extraction and checksums"
    status = $(if ($checksumFailures.Count -eq 0) { "passed" } else { "failed" })
    findings = $checksumFailures.Count
    log = "clean-install-validation.json"
})
if ($checksumFailures.Count -ne 0) {
    throw "Clean package validation failed."
}

$environment = [ordered]@{
    os = [System.Runtime.InteropServices.RuntimeInformation]::OSDescription
    architecture = [System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture.ToString()
    framework = [System.Runtime.InteropServices.RuntimeInformation]::FrameworkDescription
    dotnet = (& dotnet --version).Trim()
    powershell = $PSVersionTable.PSVersion.ToString()
}
$environment | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $evidence "environment.json") -Encoding utf8

$summary = [ordered]@{
    formatVersion = 1
    repository = $root
    result = $(if (@($steps | Where-Object status -eq "failed").Count -eq 0) { "passed" } else { "failed" })
    steps = @($steps)
    limitations = @(
        "Live V Rising world tests are not executed by this local audit.",
        "Multiplayer load, reconnect, and soak scenarios require a controlled dedicated-server run."
    )
}
$summary | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath (Join-Path $evidence "audit-summary.json") -Encoding utf8

& (Join-Path $root "tools\Write-BattleLuckEvidenceManifest.ps1") -EvidenceDirectory $evidence | Out-Null

Write-Output (Join-Path $evidence "audit-summary.json")
