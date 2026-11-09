# Refresh the Desktop + Start Menu shortcuts for the installed Shutio and nudge
# the Windows shell icon cache so the NEW app.ico shows (instead of the cached one).
$ErrorActionPreference = 'Stop'
$install = 'I:\Shutio'
$target  = Join-Path $install 'Shutio.exe'
$icon    = (Join-Path $install 'Assets\app.ico') + ',0'

$wsh = New-Object -ComObject WScript.Shell
$sm   = Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs\Shutio.lnk'
$desk = Join-Path ([Environment]::GetFolderPath('Desktop')) 'Shutio.lnk'

foreach ($path in @($sm, $desk)) {
    if (Test-Path $path) {
        $lnk = $wsh.CreateShortcut($path)
        $lnk.TargetPath       = $target
        $lnk.WorkingDirectory = $install
        $lnk.Description      = "Capture, annotate, OCR and screen recording"
        $lnk.IconLocation     = $icon
        $lnk.Save()
        Write-Host ('Re-saved shortcut: ' + $path)
    } else {
        Write-Host ('(no shortcut at) ' + $path)
    }
}

# Nudge the shell icon cache for the install folder and the Assets folder.
try {
    & "$env:WINDIR\System32\ie4uinit.exe" -show $install | Out-Null
    & "$env:WINDIR\System32\ie4uinit.exe" -show (Join-Path $install 'Assets') | Out-Null
    Write-Host 'Requested an icon-cache refresh (ie4uinit).'
} catch {
    Write-Host ('ie4uinit refresh failed (non-fatal): ' + $_.Exception.Message)
}

Write-Host ''
Write-Host ('Shortcut icon location: ' + $icon)
Write-Host ('Icon file exists: ' + (Test-Path (Join-Path $install 'Assets\app.ico')))
