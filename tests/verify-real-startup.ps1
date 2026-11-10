param([switch]$HttpApi)
$ErrorActionPreference = 'Stop'
$api = if ($HttpApi) { 'http://localhost:5156' } else { 'https://localhost:7201' }
foreach ($path in @('/health','/api/v1/contract/config','/api/v1/contract/balance')) {
    try {
        $response = Invoke-WebRequest -UseBasicParsing -Uri ($api + $path)
        if ([int]$response.StatusCode -ne 200) { throw 'Unexpected status.' }
        if ($path.EndsWith('/config')) {
            $config = $response.Content | ConvertFrom-Json
            if ($config.owner -ne '0xsameey-owner.testnet' -or $config.agent -ne '0xsameey-agent.testnet') {
                throw 'Unexpected live owner or agent. Stop and inspect configuration.'
            }
        }
        Write-Host "PASS GET $path : 200"
    } catch { throw "Real startup check failed at $path. Inspect the API log and RPC connectivity; do not substitute simulated state. $($_.Exception.Message)" }
}
$dashboard = Invoke-WebRequest -UseBasicParsing http://localhost:5173
if ([int]$dashboard.StatusCode -ne 200) { throw 'Dashboard unavailable.' }
Write-Host 'PASS dashboard HTTP. Wallet, visual QA and authenticated flows still require manual verification. No signing or dispatch was attempted.'
