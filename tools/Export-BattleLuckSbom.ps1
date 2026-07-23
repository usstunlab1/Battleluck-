[CmdletBinding()]
param(
    [string]$RepositoryRoot = (Split-Path -Parent $PSScriptRoot),
    [string]$OutputPath = ""
)

$ErrorActionPreference = "Stop"
$root = [System.IO.Path]::GetFullPath($RepositoryRoot)
if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    $OutputPath = Join-Path $root "docs\audit\sbom.cdx.json"
}

$components = @{}
$lockFiles = @(
    (Join-Path $root "packages.lock.json"),
    (Join-Path $root "BattleLuck.Tests\packages.lock.json")
)

foreach ($lockFile in $lockFiles) {
    if (-not (Test-Path -LiteralPath $lockFile -PathType Leaf)) {
        throw "Missing NuGet lockfile: $lockFile"
    }

    $lock = Get-Content -LiteralPath $lockFile -Raw | ConvertFrom-Json
    foreach ($framework in $lock.dependencies.PSObject.Properties) {
        foreach ($package in $framework.Value.PSObject.Properties) {
            $version = [string]$package.Value.resolved
            if ([string]::IsNullOrWhiteSpace($version)) {
                continue
            }

            $key = "$($package.Name.ToLowerInvariant())@$version"
            if (-not $components.ContainsKey($key)) {
                $components[$key] = [ordered]@{
                    type = "library"
                    name = $package.Name
                    version = $version
                    purl = "pkg:nuget/$([Uri]::EscapeDataString($package.Name))@$([Uri]::EscapeDataString($version))"
                    properties = @(
                        [ordered]@{
                            name = "battleluck:dependencyType"
                            value = [string]$package.Value.type
                        }
                    )
                }
            }
        }
    }
}

$manifest = Get-Content -LiteralPath (Join-Path $root "manifest.json") -Raw | ConvertFrom-Json
$sbom = [ordered]@{
    bomFormat = "CycloneDX"
    specVersion = "1.5"
    version = 1
    metadata = [ordered]@{
        component = [ordered]@{
            type = "application"
            name = [string]$manifest.name
            version = [string]$manifest.version_number
        }
        properties = @(
            [ordered]@{
                name = "battleluck:generatedFrom"
                value = "NuGet packages.lock.json files"
            }
        )
    }
    components = @($components.Values | Sort-Object name, version)
}

$parent = Split-Path -Parent $OutputPath
New-Item -ItemType Directory -Path $parent -Force | Out-Null
$sbom | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $OutputPath -Encoding utf8
Write-Output ([System.IO.Path]::GetFullPath($OutputPath))
