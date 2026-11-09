$ErrorActionPreference = 'Stop'
$dev = 'I:\DevTools'; $env:DOTNET_CLI_HOME = "$dev\dotnet-home"; $env:NUGET_PACKAGES = "$dev\nuget"
$dotnet = "$dev\dotnet\dotnet.exe"
$app = 'I:\Apps\SnippingTool'

Write-Host ('BEFORE: {0} top-level files in {1}' -f (Get-ChildItem $app -File).Count, $app)

# 1) Remove stale multi-file leftovers: top-level runtime DLLs + generated json.
#    (Only top-level files are touched; Source\, Install\, Tools\, Assets\ are subfolders and untouched.)
$dlls  = @(Get-ChildItem $app -File -Filter '*.dll')
$jsons = @(Get-ChildItem $app -File | Where-Object { $_.Name -like 'Shutio.deps.json' -or $_.Name -like 'Shutio.runtimeconfig.json' })
Write-Host ('removing {0} stale top-level .dll + {1} json' -f $dlls.Count, $jsons.Count)
$dlls  | Remove-Item -Force
$jsons | Remove-Item -Force

# 2) Publish single-file into the app folder.
Write-Host '=== PUBLISH single-file into app folder ==='
& $dotnet publish 'I:\Apps\SnippingTool\Source\SnippingTool\SnippingTool.csproj' `
  -c Release -r win-x64 --self-contained true -o $app `
  -p:DebugType=None -p:DebugSymbols=false -v q -nologo

# 3) Belt-and-suspenders cleanup of any loose runtime DLLs/json.
Get-ChildItem $app -File -Filter '*.dll' | Remove-Item -Force
Get-ChildItem $app -File | Where-Object { $_.Name -like 'Shutio.deps.json' -or $_.Name -like 'Shutio.runtimeconfig.json' } | Remove-Item -Force

Write-Host ''
Write-Host '=== AFTER: app folder top-level ==='
Get-ChildItem $app -File | Sort-Object Length -Descending | ForEach-Object {
    Write-Host ('  {0,-30} {1,9:N2} MB' -f $_.Name, ($_.Length/1MB))
}
Write-Host ('  folders: ' + ((Get-ChildItem $app -Directory | ForEach-Object { $_.Name }) -join ', '))

Write-Host ''
Write-Host '=== SELF-TEST (app folder) ==='
$p = Start-Process -FilePath (Join-Path $app 'Shutio.exe') -ArgumentList '--selftest' -WorkingDirectory $app -Wait -PassThru -WindowStyle Hidden
Write-Host ('self-test exit: ' + $p.ExitCode)
Get-Content (Join-Path $app 'Data\Tests\selftest-report.txt') | Where-Object { $_ -match 'Summary:|Exit code:' }

Write-Host ''
Write-Host '=== GUI SMOKE TEST (app folder) ==='
Stop-Process -Name Shutio -Force -ErrorAction SilentlyContinue
Start-Sleep -Milliseconds 600
$proc = Start-Process -FilePath (Join-Path $app 'Shutio.exe') -WorkingDirectory $app -PassThru
Start-Sleep -Seconds 6
if ($proc.HasExited) { Write-Host ('APP EXITED early (exit ' + $proc.ExitCode + ')') } else { Write-Host 'APP alive 6s -> WPF OK' }
Stop-Process -Name Shutio -Force -ErrorAction SilentlyContinue
Write-Host 'done'
