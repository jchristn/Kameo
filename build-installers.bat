@echo off
rem Builds the Windows installers (.exe via Inno Setup, portable .zip) into installers\<version>\.
rem Usage: build-installers.bat [version]   (default: the version in Directory.Build.props)
setlocal
cd /d "%~dp0"
dotnet build src\Kameo.Publisher -c Release --nologo -v quiet || exit /b 1
if "%~1"=="" (
  dotnet run --project src\Kameo.Publisher -c Release --no-build -- --all
) else (
  dotnet run --project src\Kameo.Publisher -c Release --no-build -- --all --version %1
)
exit /b %ERRORLEVEL%
