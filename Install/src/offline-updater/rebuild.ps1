#requires -version 3
<#
  Builds the OFFLINE UPDATER from the app folder, then refreshes the share folder.
  Everything stays under I:\ (offline). No admin, no internet.

  The updater is a single self-contained .exe that has the whole (new) app embedded.
  Run it on any machine where Shutio is already installed and it repairs that copy
  IN PLACE, preserving the user's Data\ folder (snips, recordings, settings).

  Produces:
      I:\Apps\Shutio-Share\Shutio-Updater.exe      (complete offline updater)

  Run:  powershell -NoProfile -ExecutionPolicy Bypass -File rebuild.ps1 [-ShareDir "C:\Where"]
#>
[CmdletBinding()]
param(
    [string]$ShareDir = 'I:\Apps\Shutio-Share'
)
$ErrorActionPreference = 'Stop'

$proj   = 'I:\Apps\SnippingTool'
$src    = Join-Path $proj 'Install\src\offline-updater'
$build  = Join-Path $src '_build'
$dotnet = if (Test-Path 'I:\DevTools\dotnet\dotnet.exe') { 'I:\DevTools\dotnet\dotnet.exe' } else { 'dotnet' }
$env:DOTNET_CLI_HOME = 'I:\DevTools\dotnet-home'
$env:NUGET_PACKAGES  = 'I:\DevTools\nuget'
$common = @('-p:PublishSingleFile=true','-p:IncludeNativeLibrariesForSelfExtract=true',
            '-p:EnableCompressionInSingleFile=true','-p:DebugType=none','-p:DebugSymbols=false')

if (Test-Path $build) { Remove-Item -LiteralPath $build -Recurse -Force }
New-Item -ItemType Directory -Force -Path $build | Out-Null

# ---------------------------------------------------------------- 1) payload --
Write-Host "Building the app payload (clean app, no personal data)..."
$payload = Join-Path $build 'payload'
New-Item -ItemType Directory -Force -Path (Join-Path $payload 'Assets') | Out-Null
New-Item -ItemType Directory -Force -Path (Join-Path $payload 'Tools')  | Out-Null

# Never bundle AI handover docs, the dumper, debug symbols, or the installer/
# updater executables into the payload (defensive against old-name files lingering).
$exclude = @('createdump.exe','AGENTS.md','HANDOFF.md','TODO.md','CHANGELOG.md',
             'InstallSnippingTool.exe','UninstallSnippingTool.exe',
             'Shutio-setup.exe','UninstallShutio.exe','Shutio-Updater.exe')
Get-ChildItem -LiteralPath $proj -File |
    Where-Object { $exclude -notcontains $_.Name -and $_.Extension -ne '.pdb' } |
    ForEach-Object { Copy-Item -LiteralPath $_.FullName -Destination (Join-Path $payload $_.Name) }

foreach ($a in (Get-ChildItem (Join-Path $proj 'Assets') -Recurse -File)) {
    $rel = $a.FullName.Substring($proj.Length).TrimStart('\')
    $t   = Join-Path $payload $rel
    New-Item -ItemType Directory -Force -Path (Split-Path $t) | Out-Null
    Copy-Item -LiteralPath $a.FullName -Destination $t
}
Copy-Item -LiteralPath (Join-Path $proj 'Tools\ffmpeg.exe') -Destination (Join-Path $payload 'Tools\ffmpeg.exe')

# Sanity: the payload must contain the app and must NOT contain user data.
$mustHave = @(
    (Join-Path $payload 'Shutio.exe'),
    (Join-Path $payload 'Tools\ffmpeg.exe')
)
foreach ($m in $mustHave) { if (-not (Test-Path $m)) { throw "Payload is missing $m" } }
if (Test-Path (Join-Path $payload 'Data')) { throw "Payload must not contain a Data folder" }

Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip = Join-Path $build 'app-payload.zip'
[System.IO.Compression.ZipFile]::CreateFromDirectory($payload, $zip, [System.IO.Compression.CompressionLevel]::Optimal, $false) | Out-Null
Write-Host ("  payload zip: {0:N1} MB" -f ((Get-Item $zip).Length/1MB))

# ---------------------------------------------------------------- 2) updater --
Write-Host "Building the offline updater (embedding the payload)..."
& $dotnet publish (Join-Path $src 'updater\Updater.csproj') -c Release -r win-x64 --self-contained true `
    @common -p:PayloadZip="$zip" -o $build -v q -nologo
$updater = Join-Path $build 'Shutio-Updater.exe'

if (-not (Test-Path $updater)) {
    throw "Build did not produce Shutio-Updater.exe."
}

# ---------------------------------------------------------------- 3) publish --
New-Item -ItemType Directory -Force -Path $ShareDir | Out-Null
Copy-Item -LiteralPath $updater -Destination (Join-Path $ShareDir 'Shutio-Updater.exe') -Force

Write-Host ""
Write-Host ("  Done. Updater written to:")
Write-Host ("    {0}" -f (Join-Path $ShareDir 'Shutio-Updater.exe'))
Get-ChildItem $ShareDir -File | Select-Object Name, @{N='MB';E={[math]::Round($_.Length/1MB,1)}} | Format-Table -AutoSize
