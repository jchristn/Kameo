param([Parameter(Mandatory=$true)][ValidateSet('start','stop','update','run-at-startup','remove-from-startup')][string]$Action)
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$cli = Join-Path $repo 'artifacts\cli\kameo.exe'
$desktop = Join-Path $repo 'artifacts\desktop\Kameo.Desktop.exe'
function Build-Kameo {
    $restart = $false
    if (Test-Path $cli) {
        & $cli desktop-status
        $restart = $LASTEXITCODE -eq 0
        & $cli desktop-stop
        if ($LASTEXITCODE -ne 0) { throw 'Close Kameo before rebuilding.' }
    }
    $rid = if ($env:PROCESSOR_ARCHITECTURE -eq 'ARM64') { 'win-arm64' } else { 'win-x64' }
    & dotnet publish (Join-Path $repo 'src\Kameo.Cli') -c Release -r $rid --self-contained false -o (Join-Path $repo 'artifacts\cli')
    if ($LASTEXITCODE -ne 0) { throw 'CLI build failed.' }
    & dotnet publish (Join-Path $repo 'src\Kameo.Desktop') -c Release -r $rid --self-contained false -o (Join-Path $repo 'artifacts\desktop')
    if ($LASTEXITCODE -ne 0) { throw 'Desktop build failed.' }
    if ($restart) { Start-Process -FilePath $desktop -ArgumentList '--tray' }
}
try {
    switch ($Action) {
        'update' { Build-Kameo }
        'start' {
            if (!(Test-Path $desktop) -or !(Test-Path $cli)) { Build-Kameo }
            Start-Process -FilePath $desktop
        }
        'stop' {
            if (Test-Path $cli) { & $cli desktop-stop; if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE } }
        }
        'run-at-startup' {
            if (!(Test-Path $desktop) -or !(Test-Path $cli)) { Build-Kameo }
            & $cli startup windows $desktop
            if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
            Write-Host 'Kameo will start at your next Windows login.'
        }
        'remove-from-startup' {
            if (!(Test-Path $cli)) { Build-Kameo }
            & $cli remove-startup windows
            if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
            Write-Host 'Kameo startup registration removed.'
        }
    }
    exit 0
} catch { Write-Error $_; exit 1 }
