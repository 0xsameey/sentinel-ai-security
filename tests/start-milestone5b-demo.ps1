param(
    [ValidateRange(1024, 65535)]
    [Nullable[int]]$Port,
    [switch]$Verify
)

$ErrorActionPreference = 'Stop'
$previousDemoPort = $env:SENTINEL_DEMO_PORT
Push-Location (Split-Path $PSScriptRoot -Parent)
try {
    # Check before creating any test schema. Never stop an existing listener.
    $candidate = 8040
    if ($null -ne $Port) {
        $candidate = [int]$Port
    } elseif ($env:SENTINEL_DEMO_PORT) {
        $parsedPort = 0
        if (![int]::TryParse($env:SENTINEL_DEMO_PORT, [ref]$parsedPort) -or $parsedPort -lt 1024 -or $parsedPort -gt 65535) {
            throw 'SENTINEL_DEMO_PORT must be an integer between 1024 and 65535.'
        }
        $candidate = $parsedPort
    }
    $listener = [System.Net.Sockets.TcpListener]::new([System.Net.IPAddress]::Loopback, $candidate)
    try {
        $listener.Start()
    } catch {
        $listener.Stop()
        if ($null -ne $Port) {
            throw "Demo port $candidate is unavailable. Choose another -Port; no existing process was stopped."
        }
        Write-Host "Demo port $candidate is unavailable; selecting a free local port."
        $listener = [System.Net.Sockets.TcpListener]::new([System.Net.IPAddress]::Loopback, 0)
        $listener.Start()
    }
    try {
        $env:SENTINEL_DEMO_PORT = [string]$listener.LocalEndpoint.Port
    } finally {
        $listener.Stop()
    }
    Write-Host "Starting the simulated demo on http://127.0.0.1:$env:SENTINEL_DEMO_PORT/demo"

    & dotnet restore tools/Sentinel.Demo --ignore-failed-sources -p:NuGetAudit=false
    if ($LASTEXITCODE -ne 0) { throw 'Demo dependency restore failed.' }
    & npm.cmd --prefix src/sentinel-dashboard run build
    if ($LASTEXITCODE -ne 0) { throw 'Dashboard build failed.' }
    $verificationArgs = @('run', '--project', 'tools/Sentinel.Verification', '-p:NuGetAudit=false', '--', 'pg-demo')
    if ($Verify) { $verificationArgs += '--verify' }
    & dotnet @verificationArgs
    if ($LASTEXITCODE -ne 0) { throw 'Local demo failed. Review the preceding error; no blockchain transaction was submitted.' }
} finally {
    $env:SENTINEL_DEMO_PORT = $previousDemoPort
    Pop-Location
}
exit 0
