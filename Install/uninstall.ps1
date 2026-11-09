#requires -version 3
<#
  Shutio — uninstall (per-user, no admin).
  Removes shortcuts + the Apps & features entry, and (with your OK) the folder.
  Data (snips / recordings) is preserved by default; use -RemoveData to delete it.
#>
[CmdletBinding()]
param(
    [string]$InstallPath,   # the folder the app was installed to (auto from .cmd)
    [switch]$RemoveData,    # also delete the app's Data folder
    [switch]$SkipPrompts    # non-interactive: remove shortcuts/entry and delete the folder
)

$ErrorActionPreference = 'Stop'

# The .cmd sets -InstallPath to its own folder; otherwise fall back to the known locations.
if (-not $InstallPath) {
    $candidates = @(
        (Join-Path $env:LOCALAPPDATA 'Programs\Shutio'),
        'I:\Shutio', 'I:\Apps\Shutio', 'I:\Apps\SnippingTool'
    )
    $InstallPath = $candidates | Where-Object { Test-Path (Join-Path $_ 'Shutio.exe') } | Select-Object -First 1
}
if (-not $InstallPath -or -not (Test-Path (Join-Path $InstallPath 'Shutio.exe'))) {
    Write-Host "Could not locate an installed Shutio. Nothing to do." -ForegroundColor Yellow
    return 0
}

Write-Host ""
Write-Host "  Shutio — Uninstall" -ForegroundColor Cyan
Write-Host ("  Installed to: {0}" -f $InstallPath)
Write-Host ""

# Close a running instance so files are not locked.
Get-Process -Name 'Shutio' -ErrorAction SilentlyContinue | ForEach-Object {
    Write-Host "  Closing a running Shutio (PID $($_.Id))..."
    try { $_ | Stop-Process -Force } catch {}
}
Start-Sleep -Milliseconds 400

# Remove shortcuts.
$sm   = Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs\Shutio.lnk'
$desk = Join-Path ([Environment]::GetFolderPath('Desktop')) 'Shutio.lnk'
foreach ($s in @($sm,$desk)) { if (Test-Path $s) { Remove-Item -LiteralPath $s -Force; Write-Host "  Removed shortcut: $s" } }

# Remove the Apps & features entry.
$reg = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\Shutio'
if (Test-Path $reg) { Remove-Item -Path $reg -Recurse -Force; Write-Host "  Removed Apps & features entry." }

# Handle the install folder.
if ($SkipPrompts) {
    Write-Host "  Deleting $InstallPath (including Data)..." -ForegroundColor Yellow
    Remove-Item -LiteralPath $InstallPath -Recurse -Force -ErrorAction SilentlyContinue
}
else {
    $confirm = Read-Host "  Delete the install folder (and the app)? (Y/N)"
    if ($confirm -match '^[Yy]') {
        if ($RemoveData -or (Read-Host "  Also delete your snips/recordings in Data\? (Y/N)" -match '^[Yy]')) {
            Write-Host "  Deleting $InstallPath (including Data)..."
            Remove-Item -LiteralPath $InstallPath -Recurse -Force -ErrorAction SilentlyContinue
        }
        else {
            # Move the app out but keep Data so their pictures are safe.
            Write-Host "  Removing the app files, keeping Data\..."
            Get-ChildItem -LiteralPath $InstallPath -Force | Where-Object { $_.Name -ne 'Data' } | ForEach-Object {
                Remove-Item -LiteralPath $_.FullName -Recurse -Force -ErrorAction SilentlyContinue
            }
        }
    }
    else {
        Write-Host "  Keeping the folder. Only the shortcuts / Apps & features entry were removed." -ForegroundColor Yellow
    }
}

Write-Host ""
Write-Host "  Uninstall finished." -ForegroundColor Cyan
if (-not $SkipPrompts) { Write-Host "  Press Enter to close." | Out-Null; Read-Host }
