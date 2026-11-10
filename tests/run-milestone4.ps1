$ErrorActionPreference = 'Stop'
Push-Location (Split-Path $PSScriptRoot -Parent)
try {
    & powershell -NoProfile -ExecutionPolicy Bypass -File tests/run-milestone3.ps1
    if ($LASTEXITCODE -ne 0) { throw 'Baseline and provider/security verification failed.' }
    & dotnet restore tools/Sentinel.Demo --ignore-failed-sources -p:NuGetAudit=false
    if ($LASTEXITCODE -ne 0) { throw 'Demo dependency restore failed.' }
    & dotnet run --no-restore --project tools/Sentinel.Verification -- pg-demo --verify
    if ($LASTEXITCODE -ne 0) { throw 'Dashboard demo HTTP verification failed.' }
    Write-Output 'Milestone 4 verified locally. No billable model calls, key provisioning, deployment, signing or broadcasts.'
} finally { Pop-Location }
exit 0
