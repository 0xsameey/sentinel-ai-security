param([switch]$HttpOnly, [switch]$ManualTesting)
$ErrorActionPreference = 'Stop'
$agentEnvironment = @{}
Push-Location (Split-Path $PSScriptRoot -Parent)
try {
    . (Join-Path $PSScriptRoot 'local-agent-settings.ps1')
    if (Test-Path -LiteralPath (Get-LocalAgentSettingsPath)) {
        $agentSettings = Get-LocalAgentSettings
        $overrides = @{ AgentRunner__Url=$agentSettings.runnerUrl; AgentRunner__ApiKey=$agentSettings.runnerKey; AgentGateway__ApiKey=$agentSettings.gatewayKey }
        foreach ($name in $overrides.Keys) {
            $agentEnvironment[$name] = [Environment]::GetEnvironmentVariable($name,'Process')
            [Environment]::SetEnvironmentVariable($name,$overrides[$name],'Process')
        }
        Write-Host 'Local AI runner configured on 127.0.0.1:8021; service credentials loaded without display.'
    } else {
        Write-Host 'AI runner not configured by local setup. To enable proposals: run tests/configure-local-agent.ps1 and restart API.'
    }
    if (!$HttpOnly) {
        & dotnet dev-certs https --check --trust
        if ($LASTEXITCODE -ne 0) { throw 'A valid trusted HTTPS dev certificate is required. No certificate was created. Use -HttpOnly explicitly for loopback testing, or configure HTTPS in normal PowerShell.' }
    }
    & dotnet run --no-restore --project tools/Sentinel.Verification -- pg-startup-check
    if ($LASTEXITCODE -ne 0) { throw 'Read-only PostgreSQL readiness inspection failed.' }
    $url = if ($HttpOnly) { 'http://localhost:5156' } else { 'https://localhost:7201;http://localhost:5156' }
    $disableDispatch = if ($ManualTesting) { 'true' } else { 'false' }
    $reconciliationEnabled = if ($ManualTesting) { 'false' } else { 'true' }
    Write-Host "REAL application, testnet RPC: $url ; manual testing restrictions: $ManualTesting"
    & dotnet run --no-restore --no-launch-profile --project src/Sentinel.Api -- `
        --environment Development --urls $url `
        --Safety:DisablePaymentDispatch=$disableDispatch --Workers:ReconciliationEnabled=$reconciliationEnabled
    if ($LASTEXITCODE -ne 0) { throw 'Real API startup failed; inspect the preceding error.' }
} finally {
    foreach ($name in $agentEnvironment.Keys) { [Environment]::SetEnvironmentVariable($name,$agentEnvironment[$name],'Process') }
    Pop-Location
}
