@echo off
setlocal
if not exist "%~dp0..\..\artifacts\cli\kameo.exe" (
  echo Build Kameo first with scripts\windows\update.bat. 1>&2
  exit /b 1
)
"%~dp0..\..\artifacts\cli\kameo.exe" %*
exit /b %errorlevel%
