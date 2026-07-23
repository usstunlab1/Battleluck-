[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$OriginalArchive,
    [Parameter(Mandatory = $true)]
    [string]$AuditedArchive,
    [string]$OutputPath = ""
)

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.Security

$originalPath = [System.IO.Path]::GetFullPath($OriginalArchive)
$auditedPath = [System.IO.Path]::GetFullPath($AuditedArchive)
if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    $OutputPath = Join-Path (Split-Path -Parent $PSScriptRoot) "docs\audit\archive-comparison.json"
}

function Get-ZipEntryInventory {
    param(
        [string]$Path,
        [string]$RequiredPrefix = ""
    )

    $result = @{}
    $archive = [System.IO.Compression.ZipFile]::OpenRead($Path)
    try {
        foreach ($entry in $archive.Entries) {
            if ([string]::IsNullOrEmpty($entry.Name)) {
                continue
            }

            $normalized = $entry.FullName.Replace("\", "/")
            if (-not [string]::IsNullOrEmpty($RequiredPrefix)) {
                if (-not $normalized.StartsWith($RequiredPrefix, [StringComparison]::Ordinal)) {
                    continue
                }
                $normalized = $normalized.Substring($RequiredPrefix.Length)
            }

            $hasher = [System.Security.Cryptography.SHA256]::Create()
            $stream = $entry.Open()
            try {
                $hash = [Convert]::ToHexString($hasher.ComputeHash($stream)).ToLowerInvariant()
            }
            finally {
                $stream.Dispose()
                $hasher.Dispose()
            }

            $result[$normalized] = [ordered]@{
                bytes = $entry.Length
                sha256 = $hash
            }
        }
    }
    finally {
        $archive.Dispose()
    }
    return $result
}

$original = Get-ZipEntryInventory -Path $originalPath
$auditedRepository = Get-ZipEntryInventory -Path $auditedPath -RequiredPrefix "repository/"
$originalNames = @($original.Keys | Sort-Object)
$auditedNames = @($auditedRepository.Keys | Sort-Object)
$added = @($auditedNames | Where-Object { -not $original.ContainsKey($_) })
$removed = @($originalNames | Where-Object { -not $auditedRepository.ContainsKey($_) })
$changed = @(
    $originalNames |
        Where-Object { $auditedRepository.ContainsKey($_) -and $original[$_].sha256 -ne $auditedRepository[$_].sha256 } |
        ForEach-Object {
            [ordered]@{
                path = $_
                originalSha256 = $original[$_].sha256
                auditedSha256 = $auditedRepository[$_].sha256
            }
        }
)
$unchanged = @(
    $originalNames |
        Where-Object { $auditedRepository.ContainsKey($_) -and $original[$_].sha256 -eq $auditedRepository[$_].sha256 }
)

$report = [ordered]@{
    formatVersion = 1
    original = [ordered]@{
        path = $originalPath
        bytes = (Get-Item -LiteralPath $originalPath).Length
        sha256 = (Get-FileHash -LiteralPath $originalPath -Algorithm SHA256).Hash.ToLowerInvariant()
        repositoryEntries = $original.Count
    }
    audited = [ordered]@{
        path = $auditedPath
        bytes = (Get-Item -LiteralPath $auditedPath).Length
        sha256 = (Get-FileHash -LiteralPath $auditedPath -Algorithm SHA256).Hash.ToLowerInvariant()
        repositoryEntries = $auditedRepository.Count
    }
    comparison = [ordered]@{
        added = $added.Count
        removed = $removed.Count
        changed = $changed.Count
        unchanged = $unchanged.Count
        addedPaths = $added
        removedPaths = $removed
        changedPaths = $changed
    }
    interpretation = @(
        "The audited archive intentionally removes generated build output, prior ZIP files, PJ content, IDE state, and transient logs.",
        "The audited archive adds evidence and release payload sections outside repository/; those sections are not counted in the source-path comparison."
    )
}

New-Item -ItemType Directory -Path (Split-Path -Parent $OutputPath) -Force | Out-Null
$report | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $OutputPath -Encoding utf8
Write-Output ([System.IO.Path]::GetFullPath($OutputPath))
