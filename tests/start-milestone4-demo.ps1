$ErrorActionPreference = 'Stop'
Push-Location (Split-Path $PSScriptRoot -Parent)
try {
    & dotnet restore tools/Sentinel.Demo --ignore-failed-sources -p:NuGetAudit=false
    if ($LASTEXITCODE -ne 0) { throw 'Demo dependency restore failed.' }
    & npm.cmd --prefix src/sentinel-dashboard run build
    if ($LASTEXITCODE -ne 0) { throw 'Dashboard build failed.' }
    & dotnet run --project tools/Sentinel.Verification -- pg-demo
    if ($LASTEXITCODE -ne 0) { throw 'Local demo failed.' }
} finally { Pop-Location }
exit 0
