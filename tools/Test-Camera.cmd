@echo off
rem Runs Test-Camera.ps1 whatever the PowerShell execution policy; arguments are passed to it.
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Test-Camera.ps1" %*
pause
