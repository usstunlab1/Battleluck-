param(
    [Parameter(Mandatory = $true)]
    [string]$Root,

    [Parameter(Mandatory = $true)]
    [string]$Output
)

$resolvedRoot = (Resolve-Path -LiteralPath $Root).Path
$resolvedOutput = [System.IO.Path]::GetFullPath($Output)
$outputDirectory = [System.IO.Path]::GetDirectoryName($resolvedOutput)
if (-not [string]::IsNullOrWhiteSpace($outputDirectory)) {
    [System.IO.Directory]::CreateDirectory($outputDirectory) | Out-Null
}

$entries = Get-ChildItem -LiteralPath $resolvedRoot -Force -Recurse |
    Sort-Object FullName |
    ForEach-Object {
        $relative = $_.FullName.Substring($resolvedRoot.Length).TrimStart('\', '/')
        if ($_.PSIsContainer) { "$relative/" } else { $relative }
    }

$header = @(
    "# BattleLuck recursive repository tree"
    "# Source: $resolvedRoot"
    "# Generated UTC: $([DateTime]::UtcNow.ToString('O'))"
    "# Entries: $($entries.Count)"
    ""
)

[System.IO.File]::WriteAllLines(
    $resolvedOutput,
    [System.Linq.Enumerable]::Concat([string[]]$header, [string[]]$entries),
    [System.Text.UTF8Encoding]::new($false))

Get-Item -LiteralPath $resolvedOutput |
    Select-Object FullName, Length, LastWriteTime
