$ErrorActionPreference = 'Stop'
Push-Location (Split-Path $PSScriptRoot -Parent)
function Check([string]$step) { if ($LASTEXITCODE -ne 0) { throw "$step failed (exit $LASTEXITCODE)." } }
$previousDemoPort = $env:SENTINEL_DEMO_PORT
try {
    $listener = [System.Net.Sockets.TcpListener]::new([System.Net.IPAddress]::Loopback, 0)
    $listener.Start()
    $env:SENTINEL_DEMO_PORT = [string]$listener.LocalEndpoint.Port
    $listener.Stop()
    & cmd.exe /d /c tests\run-rust-tests.cmd
    Check 'Rust contract and offline storage rehearsal'
    & npm.cmd --prefix src/sentinel-signer test
    Check 'Signer security tests'
    & node src/sentinel-signer/node_modules/typescript/bin/tsc --noEmit -p src/sentinel-agent-client/tsconfig.json
    Check 'Agent TypeScript (workspace compiler)'
    & npm.cmd --prefix src/sentinel-agent-client test
    Check 'Agent and provider security tests'
    & npm.cmd --prefix src/sentinel-dashboard test
    Check 'Dashboard render tests'
    & npm.cmd --prefix src/sentinel-dashboard run lint
    Check 'Dashboard lint'
    # The established runner creates an isolated schema; application tables are excluded.
    & dotnet run --no-restore --project tools/Sentinel.Verification -- pg-e2e
    Check 'PostgreSQL API integration and owner-approved regressions'
    & dotnet run --no-restore --project tools/Sentinel.Verification -- pg-demo --verify
    Check 'Owner-approved and delegated attack demo'
    Write-Output 'MILESTONE5B_LOCAL_VERIFIED: no real model request, deployment, signing or blockchain submission.'
} finally { $env:SENTINEL_DEMO_PORT = $previousDemoPort; Pop-Location }
