@echo off
setlocal
powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%~dp0..\common\lifecycle.ps1" -Action run-at-startup
exit /b %errorlevel%
