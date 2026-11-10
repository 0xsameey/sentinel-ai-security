param([switch]$HttpApi, [switch]$ManualTesting)
$ErrorActionPreference = 'Stop'
$previousApi = $env:VITE_API_URL
$previousMode = $env:VITE_MANUAL_TESTING
Push-Location (Join-Path (Split-Path $PSScriptRoot -Parent) 'src/sentinel-dashboard')
try {
    $env:VITE_API_URL = if ($HttpApi) { 'http://localhost:5156' } else { 'https://localhost:7201' }
    $env:VITE_MANUAL_TESTING = if ($ManualTesting) { 'true' } else { 'false' }
    Write-Host "REAL dashboard: http://localhost:5173 ; API: $env:VITE_API_URL ; manual testing restrictions: $ManualTesting"
    & npm.cmd run dev -- --host localhost --port 5173 --strictPort
    if ($LASTEXITCODE -ne 0) { throw 'Dashboard startup failed. Keep port 5173 free; do not silently switch CORS origins.' }
} finally {
    $env:VITE_API_URL = $previousApi
    $env:VITE_MANUAL_TESTING = $previousMode
    Pop-Location
}
