[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$EvidenceDirectory
)

$ErrorActionPreference = "Stop"
$evidence = [System.IO.Path]::GetFullPath($EvidenceDirectory)
if (-not (Test-Path -LiteralPath $evidence -PathType Container)) {
    throw "Evidence directory does not exist: $evidence"
}

$manifestEntries = Get-ChildItem -LiteralPath $evidence -Recurse -File |
    Where-Object Name -ne "evidence-manifest.json" |
    Sort-Object FullName |
    ForEach-Object {
        [ordered]@{
            path = [System.IO.Path]::GetRelativePath($evidence, $_.FullName).Replace("\", "/")
            bytes = $_.Length
            sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
        }
    }
$output = Join-Path $evidence "evidence-manifest.json"
[ordered]@{
    formatVersion = 1
    files = @($manifestEntries)
} | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $output -Encoding utf8
Write-Output $output
