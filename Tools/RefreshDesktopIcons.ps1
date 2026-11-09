# Refresh the desktop icon cache by clearing it and restarting Windows Explorer.
# (Explorer holds the cached .ico images; this forces it to re-read the real files.)
$ErrorActionPreference = 'SilentlyContinue'

Write-Host 'Stopping explorer...'
Stop-Process -Name explorer -Force -ErrorAction SilentlyContinue
Start-Sleep -Seconds 1

Write-Host 'Clearing icon caches (best effort)...'
$caches = @(
    (Join-Path $env:LOCALAPPDATA 'Microsoft\Windows\Explorer'),
    $env:LOCALAPPDATA
)
$removed = 0
foreach ($dir in $caches) {
    if (Test-Path $dir) {
        Get-ChildItem $dir -Filter 'iconcache*.db' -ErrorAction SilentlyContinue |
            ForEach-Object { Remove-Item $_.FullName -Force -ErrorAction SilentlyContinue; $removed++ }
    }
}
Get-ChildItem $env:LOCALAPPDATA -Filter 'IconCache.db' -ErrorAction SilentlyContinue |
    ForEach-Object { Remove-Item $_.FullName -Force -ErrorAction SilentlyContinue; $removed++ }
Write-Host ("Removed {0} cache file(s)." -f $removed)

Start-Sleep -Seconds 1
Write-Host 'Starting explorer...'
Start-Process explorer.exe
Start-Sleep -Seconds 3

$running = Get-Process -Name explorer -ErrorAction SilentlyContinue
Write-Host ('Explorer running again: ' + ($running -ne $null))
Write-Host 'The desktop should have refreshed - the Shutio icon should now show the new emblem.'
