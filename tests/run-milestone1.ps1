param([switch]$UsePostman, [switch]$ExistingDatabase, [string]$ArtifactsPath)
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent
$runRoot = Join-Path $repoRoot ('.test-artifacts/milestone1-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $runRoot -Force | Out-Null
function Free-Port {
    $listener = [System.Net.Sockets.TcpListener]::new([System.Net.IPAddress]::Loopback, 0)
    $listener.Start()
    $port = $listener.LocalEndpoint.Port
    $listener.Stop()
    return $port
}
function Check-Exit([string]$step) {
    if ($LASTEXITCODE -ne 0) { throw "$step failed (exit $LASTEXITCODE). Logs: $runRoot" }
}
$pgBin = Split-Path (Get-Command pg_ctl.exe -ErrorAction Stop).Source -Parent
$pgData = Join-Path $runRoot 'postgres'
$pgPort = Free-Port
$nearPort = Free-Port
$apiPort = Free-Port
$signerPort = Free-Port
$pgStarted = $false
$fixture = $null
$testNames = @('SENTINEL_E2E_DB','SENTINEL_E2E_ROOT','SENTINEL_E2E_NEAR','SENTINEL_E2E_API','SENTINEL_E2E_SIGNER_PORT','PORT')
$previous = @{}
foreach ($name in $testNames) { $previous[$name] = [Environment]::GetEnvironmentVariable($name) }
Push-Location $repoRoot
try {
    & npm.cmd --prefix src/sentinel-signer test
    Check-Exit 'Signer tests'
    if ($ExistingDatabase) {
        if (!$env:SENTINEL_E2E_DB -or $env:SENTINEL_E2E_DB -notmatch '(?i)(?:Database|Search Path)=sentinel_e2e_[a-z0-9_]+(?:;|$)') {
            throw 'Existing service tests require a newly created sentinel_e2e_ database or schema. Use tools/Sentinel.Verification pg-e2e.'
        }
    } else {
        & (Join-Path $pgBin 'initdb.exe') -D $pgData -A trust -U sentinel_test --encoding=UTF8 --locale=C *> (Join-Path $runRoot 'initdb.log')
        Check-Exit 'Isolated PostgreSQL initialization'
        & (Join-Path $pgBin 'pg_ctl.exe') -D $pgData -l (Join-Path $runRoot 'postgres.log') -o "-p $pgPort -h 127.0.0.1" -w start *> (Join-Path $runRoot 'pg-start.log')
        Check-Exit 'Isolated PostgreSQL startup'
        $pgStarted = $true
        $env:SENTINEL_E2E_DB = "Host=127.0.0.1;Port=$pgPort;Database=postgres;Username=sentinel_test;Pooling=false;Timeout=5"
    }
    $env:PORT = [string]$nearPort
    if ($UsePostman) {
        $fixture = Start-Process -FilePath (Get-Command postman.exe).Source -ArgumentList @('simulation','run','postman/simulations/milestone1.sim.yaml','--port',"$nearPort",'--no-history','--output','ndjson') -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $runRoot 'near.ndjson') -RedirectStandardError (Join-Path $runRoot 'near.err')
    } else {
        $fixturePath = Join-Path $repoRoot 'tests/run-near-fixture.cjs'
        $fixture = Start-Process -FilePath (Get-Command node.exe).Source -ArgumentList @('"' + $fixturePath + '"') -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $runRoot 'near.log') -RedirectStandardError (Join-Path $runRoot 'near.err')
    }
    $env:SENTINEL_E2E_ROOT = $repoRoot
    $env:SENTINEL_E2E_NEAR = "http://127.0.0.1:$nearPort/near"
    $env:SENTINEL_E2E_API = "http://127.0.0.1:$apiPort"
    $env:SENTINEL_E2E_SIGNER_PORT = [string]$signerPort
    $ready = $false
    for ($i = 0; $i -lt 50; $i++) {
        if ($fixture.HasExited) { throw "NEAR fixture exited. See $runRoot/near.err" }
        try {
            $health = Invoke-RestMethod "$env:SENTINEL_E2E_NEAR/health" -TimeoutSec 1
            if ($health.ok) { $ready = $true; break }
        } catch { Start-Sleep -Milliseconds 200 }
    }
    if (!$ready) { throw "NEAR fixture did not become ready. Logs: $runRoot" }
    if ($ArtifactsPath) {
        & dotnet test Sentinel.slnx --artifacts-path $ArtifactsPath -p:NuGetAudit=false -p:RestoreIgnoreFailedSources=true --verbosity minimal
    } else {
        & dotnet test Sentinel.slnx --no-restore --verbosity minimal
    }
    Check-Exit '.NET unit and HTTP end-to-end tests'
    & npm.cmd --prefix src/sentinel-dashboard run build
    Check-Exit 'Dashboard build'
    & npm.cmd --prefix src/sentinel-auth-client run build
    Check-Exit 'Auth client build'
    Write-Output "Milestone 1 local integration passed. NEAR and signing are simulated. Logs: $runRoot"
} finally {
    if ($null -ne $fixture -and !$fixture.HasExited) { Stop-Process -Id $fixture.Id -Force }
    if ($pgStarted) {
        & (Join-Path $pgBin 'pg_ctl.exe') -D $pgData -m fast -w stop *> (Join-Path $runRoot 'pg-stop.log')
    }
    foreach ($name in $testNames) { [Environment]::SetEnvironmentVariable($name, $previous[$name]) }
    Pop-Location
}
