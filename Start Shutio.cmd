@echo off
rem Portable launcher: starts the self-contained build next to this file.
rem No installation, admin rights, or working-directory assumptions.
setlocal
set "APP=%~dp0Shutio.exe"
if not exist "%APP%" (
  echo Shutio.exe was not found next to this launcher.
  echo Expected: "%APP%"
  pause
  exit /b 1
)
start "" "%APP%"
endlocal
