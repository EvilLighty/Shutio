#requires -version 3
<#
  Shutio — simple installer (per-user, no admin, no extra software).
  Copies the lean, self-contained app to a folder YOU choose, then creates
  Start Menu + Desktop shortcuts and an entry in "Apps & features".

  Run it by double-clicking "Install Shutio.cmd", or:
      powershell -ExecutionPolicy Bypass -File install.ps1 [-InstallPath "C:\Somewhere"]
#>
[CmdletBinding()]
param(
    [string]$InstallPath,  # optional: skip the folder dialog and install here
    [switch]$SkipPrompts   # skip the final "launch now?" / "press enter" prompts
)

$ErrorActionPreference = 'Stop'
$AppName  = 'Shutio'
$Version  = '1.1.1'
$Publisher= 'SalsaNOW Edition'
$Source   = Split-Path -Parent $PSScriptRoot   # the app folder that contains Install\

function Write-Banner {
    Write-Host ''
    Write-Host ("  {0} — Installer v{1}" -f $AppName, $Version) -ForegroundColor Cyan
    Write-Host ("  Source: {0}" -f $Source)
    Write-Host ''
}

function Test-Installable([string]$p) {
    # The destination may not exist yet; we need the PARENT to be writable to create it.
    $checkDir = if (Test-Path $p) { $p } else { Split-Path -Parent $p }
    if (-not $checkDir -or -not (Test-Path $checkDir)) { $checkDir = 'C:\' }
    try {
        $t = Join-Path $checkDir ("probe_" + [guid]::NewGuid().ToString("N"))
        [System.IO.File]::WriteAllText($t, 'ok') | Out-Null
        Remove-Item -LiteralPath $t -Force -ErrorAction SilentlyContinue
        return $true
    } catch { return $false }
}

Write-Banner

# ---------------------------------------------------------------- sanity ----
$exe = Join-Path $Source 'Shutio.exe'
if (-not (Test-Path $exe)) {
    Write-Host ("ERROR: {0} was not found. Keep the whole app folder intact and run the installer from inside it." -f $exe) -ForegroundColor Red
    return 1
}
$ffmpeg = Join-Path $Source 'Tools\ffmpeg.exe'
$hasFfmpeg = Test-Path $ffmpeg
if (-not $hasFfmpeg) { Write-Host "NOTE: ffmpeg not found — recording will be disabled after install." -ForegroundColor Yellow }

# --------------------------------------------------------- choose path ------
$dest = $InstallPath
if (-not $dest) {
    try {
        Add-Type -AssemblyName System.Windows.Forms
        Add-Type -AssemblyName System.Drawing
        $dlg = New-Object System.Windows.Forms.FolderBrowserDialog
        $dlg.Description = "Choose where to install the $AppName"
        $dlg.ShowNewFolderButton = $true
        foreach ($init in @('I:\', $env:LOCALAPPDATA, 'C:\')) {
            if (Test-Path $init) { $dlg.SelectedPath = $init; break }
        }
        $result = $dlg.ShowDialog()
        if ($result -ne [System.Windows.Forms.DialogResult]::OK) {
            Write-Host "Installation cancelled." -ForegroundColor Yellow
            return 0
        }
        $dest = $dlg.SelectedPath
    }
    catch {
        Write-Host "Could not open the folder picker (using default)." -ForegroundColor Yellow
        $dest = Join-Path $env:LOCALAPPDATA 'Programs\Shutio'
    }
}
$dest = ($dest -replace '\s+$','').TrimEnd('\')

if (-not (Test-Installable $dest)) {
    Write-Host ("ERROR: that location is not writable: {0}" -f $dest) -ForegroundColor Red
    Write-Host "Pick a folder under your user profile or on the I: drive, then try again." -ForegroundColor Yellow
    return 1
}
Write-Host ("Installing to: {0}" -f $dest) -ForegroundColor Cyan

# ----------------------------------------------------------------- copy ----
$failures = 0
function Copy-File([string]$from, [string]$to) {
    $global:failures
    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $to) | Out-Null
    try { Copy-Item -LiteralPath $from -Destination $to -Force }
    catch { Write-Host ("  ! could not copy {0}" -f (Split-Path -Leaf $to)) -ForegroundColor Red; $global:failures++ }
}

Write-Host "  Copying the application files..."
# 1) All root files (exe, runtime DLLs, json, docs, launcher) except the debug dumper
$deny = @('createdump.exe')
Get-ChildItem -LiteralPath $Source -File |
    Where-Object { $deny -notcontains $_.Name } |
    ForEach-Object { Copy-File $_.FullName (Join-Path $dest $_.Name) }

# 2) Assets folder
$assetsSrc = Join-Path $Source 'Assets'
if (Test-Path $assetsSrc) {
    Copy-Item -LiteralPath $assetsSrc -Destination $dest -Recurse -Force
}

# 3) The single ffmpeg.exe the app needs
if ($hasFfmpeg) { Copy-File $ffmpeg (Join-Path $dest 'Tools\ffmpeg.exe') }

# 4) Preserve existing user work (settings, history, snips, recordings) if any
$srcData = Join-Path $Source 'Data'
if (Test-Path $srcData) {
    foreach ($sub in @('Screenshots','Thumbnails','Recordings')) {
        $s = Join-Path $srcData $sub
        if (Test-Path $s) {
            $d = Join-Path $dest "Data\$sub"
            New-Item -ItemType Directory -Force -Path $d | Out-Null
            Copy-Item -LiteralPath $s -Destination $d -Recurse -Force
        }
    }
    foreach ($f in @('settings.json','history.json')) {
        $s = Join-Path $srcData $f
        if (Test-Path $s) { Copy-Item -LiteralPath $s -Destination (Join-Path $dest "Data\$f") -Force }
    }
}
Write-Host "  File copy complete." -ForegroundColor Green

if ($failures -gt 0) {
    Write-Host ("ERROR: {0} file(s) failed to copy. The install may be incomplete." -f $failures) -ForegroundColor Red
    return 1
}

# ------------------------------------------------------------ shortcuts -----
$wsh = New-Object -ComObject WScript.Shell
$target = Join-Path $dest 'Shutio.exe'
$icon   = Join-Path $dest 'Assets\app.ico'
$makeShortcut = {
    param($path)
    $sc = $wsh.CreateShortcut($path)
    $sc.TargetPath = $target
    $sc.WorkingDirectory = $dest
    $sc.Description = "Capture, annotate, OCR and screen recording"
    if (Test-Path $icon) { $sc.IconLocation = "$icon,0" }
    $sc.Save()
}
$smPath  = Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs\Shutio.lnk'
$deskPath= Join-Path ([Environment]::GetFolderPath('Desktop')) 'Shutio.lnk'
New-Item -ItemType Directory -Force -Path (Split-Path -Parent $smPath) | Out-Null
& $makeShortcut $smPath
& $makeShortcut $deskPath
Write-Host "  Created Start Menu and Desktop shortcuts." -ForegroundColor Green

# ---------------------------------------------------------- uninstall entry -
$reg = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\Shutio'
New-Item -Path $reg -Force | Out-Null
Set-ItemProperty $reg -Name 'DisplayName'       -Value $AppName
Set-ItemProperty $reg -Name 'DisplayVersion'    -Value $Version
Set-ItemProperty $reg -Name 'Publisher'         -Value $Publisher
Set-ItemProperty $reg -Name 'InstallLocation'   -Value $dest
Set-ItemProperty $reg -Name 'UninstallString'   -Value ('"{0}\Uninstall Shutio.cmd"' -f $dest)
Set-ItemProperty $reg -Name 'NoModify'          -Value 1
Set-ItemProperty $reg -Name 'NoChange'          -Value 1
Write-Host "  Registered an entry in Apps & features." -ForegroundColor Green

# ------------------------------------------------------- uninstall launcher -
Copy-Item -LiteralPath (Join-Path $Source 'Install\uninstall.ps1') -Destination (Join-Path $dest 'uninstall.ps1') -Force
$uninstCmd = Join-Path $dest 'Uninstall Shutio.cmd'
@"
@echo off
rem Removes the $AppName (and optionally its data folder).
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0uninstall.ps1" -InstallPath "%~dp0"
"@ | Set-Content -LiteralPath $uninstCmd -Encoding ASCII

# --------------------------------------------------------------- done -------
Write-Host ''
Write-Host "  ==============  DONE  =============" -ForegroundColor Cyan
Write-Host ("  Installed to : {0}" -f $dest)
Write-Host ("  Recording    : {0}" -f $(if ($hasFfmpeg) {'enabled (ffmpeg included)'} else {'DISABLED — no ffmpeg'}))
Write-Host "  Launch       : Start Menu / Desktop icon: Shutio"
Write-Host "  Uninstall    : Uninstall Shutio.cmd  (or Apps & features)"
Write-Host ''

if ($SkipPrompts) {
    Write-Host "  (Interactive prompts skipped.)" -ForegroundColor DarkGray
    return 0
}
$launch = Read-Host "  Launch the Shutio now? (Y/N)"
if ($launch -match '^[Yy]') {
    Start-Process -FilePath $target
    Write-Host "  Launched." -ForegroundColor Green
}
Write-Host '  Press Enter to close this window.' | Out-Null
Read-Host
return 0
