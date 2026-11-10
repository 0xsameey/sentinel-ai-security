$ErrorActionPreference = 'Stop'
Push-Location (Split-Path $PSScriptRoot -Parent)
$previousPort = $env:SENTINEL_DEMO_PORT
try {
    $listener = [System.Net.Sockets.TcpListener]::new([System.Net.IPAddress]::Loopback, 0)
    $listener.Start()
    $env:SENTINEL_DEMO_PORT = [string]$listener.LocalEndpoint.Port
    $listener.Stop()
    & powershell -NoProfile -ExecutionPolicy Bypass -File tests/run-milestone4.ps1
    if ($LASTEXITCODE -ne 0) { throw 'Milestone 5A local suite failed.' }
    & npm.cmd --prefix src/sentinel-dashboard run lint
    if ($LASTEXITCODE -ne 0) { throw 'Dashboard lint failed.' }
    Write-Output 'MILESTONE5A_LOCAL_VERIFIED: fixture chain/model only. Live gates remain separate.'
} finally {
    $env:SENTINEL_DEMO_PORT = $previousPort
    Pop-Location
}
exit 0
