param(
    [string]$Path = "BepInEx/config/BattleLuck/logs/event_audit.jsonl",
    [int]$MaxRecords = 5000
)

$ErrorActionPreference = "Stop"
if (-not (Test-Path -LiteralPath $Path)) {
    Write-Output "No audit file found: $Path"
    exit 0
}

$records = Get-Content -LiteralPath $Path -Tail $MaxRecords | ForEach-Object {
    try { $_ | ConvertFrom-Json } catch { $null }
} | Where-Object { $_ -ne $null }

if (-not $records) {
    Write-Output "No valid audit records found."
    exit 0
}

$records |
    Group-Object -Property {
        $date = ([DateTime]$_.timestamp).ToUniversalTime()
        "{0}-W{1:00}" -f $date.Year, [Globalization.ISOWeek]::GetWeekOfYear($date)
    } |
    Sort-Object Name |
    ForEach-Object {
        $week = $_.Name
        $failures = @($_.Group | Where-Object { $_.exit -ne 0 })
        $codes = @($failures | Where-Object { $_.errorCode } | Group-Object errorCode | Sort-Object Count -Descending | Select-Object -First 5 | ForEach-Object { "$($_.Name)=$($_.Count)" })
        [pscustomobject]@{
            Week = $week
            Records = $_.Count
            Failures = $failures.Count
            TopErrorCodes = if ($codes.Count -gt 0) { $codes -join ", " } else { "none" }
        }
    } | Format-Table -AutoSize
