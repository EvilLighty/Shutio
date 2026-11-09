#requires -version 3
<#
  Rebuilds the OFFLINE installer + standalone uninstaller from the app folder,
  then refreshes the share folder. Everything stays under I:\ (offline).

  Produces:
     I:\Apps\Shutio-Share\Shutio-setup.exe      (complete offline installer)
     I:\Apps\Shutio-Share\UninstallShutio.exe   (standalone uninstaller)

  Run:  powershell -NoProfile -ExecutionPolicy Bypass -File rebuild.ps1 [-ShareDir "C:\Where"]
#>
[CmdletBinding()]
param(
    [string]$ShareDir = 'I:\Apps\Shutio-Share'
)
$ErrorActionPreference = 'Stop'

$proj   = 'I:\Apps\SnippingTool'
$src    = Join-Path $proj 'Install\src\offline-installer'
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

# Never bundle AI handover docs, the dumper, debug symbols, or stale installer
# executables into the payload (defensive against old-name files lingering).
$exclude = @('createdump.exe','AGENTS.md','HANDOFF.md','TODO.md','CHANGELOG.md',
             'InstallSnippingTool.exe','UninstallSnippingTool.exe',
             'Shutio-setup.exe','UninstallShutio.exe')
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

Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip = Join-Path $build 'app-payload.zip'
[System.IO.Compression.ZipFile]::CreateFromDirectory($payload, $zip, [System.IO.Compression.CompressionLevel]::Optimal, $false) | Out-Null
Write-Host ("  payload zip: {0:N1} MB" -f ((Get-Item $zip).Length/1MB))

# ----------------------------------------------------------- 2) uninstaller --
Write-Host "Building the standalone uninstaller..."
& $dotnet publish (Join-Path $src 'uninstaller\Uninstaller.csproj') -c Release -r win-x64 --self-contained true `
    @common -o $build -v q -nologo
$uninst = Join-Path $build 'UninstallShutio.exe'

# ------------------------------------------------------------- 3) installer --
Write-Host "Building the offline installer (embedding payload + uninstaller)..."
& $dotnet publish (Join-Path $src 'installer\Installer.csproj') -c Release -r win-x64 --self-contained true `
    @common -p:PayloadZip="$zip" -p:UninstExe="$uninst" -o $build -v q -nologo
$instal = Join-Path $build 'Shutio-setup.exe'

if (-not (Test-Path $instal) -or -not (Test-Path $uninst)) {
    throw "Build did not produce the expected exe files."
}

# --------------------------------------------------------------- 4) publish --
New-Item -ItemType Directory -Force -Path $ShareDir | Out-Null
Copy-Item -LiteralPath $instal -Destination (Join-Path $ShareDir 'Shutio-setup.exe')   -Force
Copy-Item -LiteralPath $uninst -Destination (Join-Path $ShareDir 'UninstallShutio.exe') -Force

Write-Host ""
Write-Host ("  Done. Share folder: {0}" -f $ShareDir)
Get-ChildItem $ShareDir -File | Select-Object Name, @{N='MB';E={[math]::Round($_.Length/1MB,1)}} | Format-Table -AutoSize
