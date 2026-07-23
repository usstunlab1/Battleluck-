$repo = "C:\Users\ahmad\OneDrive\Desktop\BL"
$dest = "$env:USERPROFILE\Desktop\BattleLuck-Full-Audit.zip"

$items = Get-ChildItem -LiteralPath $repo -Force |
    Where-Object {
        $_.Name -notin @(".git", "bin", "obj", ".vs", "BattleLuck-Full-Audit.zip")
    }

Compress-Archive `
    -LiteralPath $items.FullName `
    -DestinationPath $dest `
    -CompressionLevel Optimal `
    -Force

Get-Item $dest | Select-Object FullName, Length, LastWriteTime