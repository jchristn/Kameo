@echo off
setlocal
powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%~dp0..\common\lifecycle.ps1" -Action remove-from-startup
exit /b %errorlevel%
