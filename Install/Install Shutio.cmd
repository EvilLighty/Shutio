@echo off
rem Simple installer launcher for the portable Shutio.
rem Double-click this to open the "choose a folder" installer.
rem No admin rights and no internet are required.
setlocal
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0install.ps1" %*
endlocal
