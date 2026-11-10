function Get-LocalAgentSettingsPath {
    Join-Path (Split-Path $PSScriptRoot -Parent) '.test-artifacts/local-agent-services.json'
}

function Get-LocalAgentSettings {
    $path = Get-LocalAgentSettingsPath
    if (!(Test-Path -LiteralPath $path)) { throw 'Local agent services are not configured. Run tests/configure-local-agent.ps1 first.' }
    try { $settings = Get-Content -LiteralPath $path -Raw | ConvertFrom-Json } catch { throw 'Local agent settings are invalid; no values were displayed.' }
    if ($settings.runnerUrl -ne 'http://127.0.0.1:8021') { throw 'Unexpected local runner URL.' }
    foreach ($name in @('runnerKey','gatewayKey')) {
        try { $bytes = [Convert]::FromBase64String($settings.$name) } catch { throw "Invalid local service credential: $name" }
        if ($bytes.Length -lt 32) { throw "Local service credential too short: $name" }
        [Array]::Clear($bytes,0,$bytes.Length)
    }
    if ($settings.runnerKey -eq $settings.gatewayKey) { throw 'Runner and gateway credentials must be separate.' }
    return $settings
}
