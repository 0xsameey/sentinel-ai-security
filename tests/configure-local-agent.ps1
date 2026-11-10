$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'local-agent-settings.ps1')
$path = Get-LocalAgentSettingsPath
if (Test-Path -LiteralPath $path) {
    $null = Get-LocalAgentSettings
    Write-Host 'Existing local service credentials are valid; preserved without rotation.'
    exit 0
}
function New-ServiceToken {
    $bytes = New-Object byte[] 32
    $rng = [Security.Cryptography.RandomNumberGenerator]::Create()
    try { $rng.GetBytes($bytes); return [Convert]::ToBase64String($bytes) }
    finally { $rng.Dispose(); [Array]::Clear($bytes,0,$bytes.Length) }
}
$settings = [ordered]@{ runnerUrl='http://127.0.0.1:8021'; runnerKey=(New-ServiceToken); gatewayKey=(New-ServiceToken) }
$null = New-Item -ItemType Directory -Force -Path (Split-Path $path -Parent)
# CreateNew avoids overwriting or rotating credentials during concurrent setup.
$stream = [IO.File]::Open($path,[IO.FileMode]::CreateNew,[IO.FileAccess]::Write,[IO.FileShare]::None)
try {
    $data = [Text.Encoding]::UTF8.GetBytes(($settings | ConvertTo-Json))
    $stream.Write($data,0,$data.Length)
} finally { $stream.Dispose() }
$null = Get-LocalAgentSettings
Write-Host 'Configured two local API/agent service credentials. Values were not displayed. No signing keys or database changes.'
