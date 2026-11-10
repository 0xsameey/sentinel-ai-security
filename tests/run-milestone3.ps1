$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent
Push-Location $repoRoot
try {
    & powershell -NoProfile -ExecutionPolicy Bypass -File tests/run-milestone2.ps1
    if ($LASTEXITCODE -ne 0) { throw 'Baseline/gateway verification failed.' }
    & node --experimental-strip-types --test src/sentinel-agent-client/tests/*.test.mjs
    if ($LASTEXITCODE -ne 0) { throw 'Agent/adversarial verification failed.' }
    & node --experimental-strip-types src/sentinel-agent-client/demo/fixture.mjs
    if ($LASTEXITCODE -ne 0) { throw 'Deterministic agent demo failed.' }
    Write-Output 'Milestone 3 deterministic verification passed. Real-model smoke is a separate explicit command.'
} finally { Pop-Location }
exit 0
