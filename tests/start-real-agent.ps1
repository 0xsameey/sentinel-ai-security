param([switch]$HttpApi, [switch]$CheckOnly, [switch]$SmokeOnly)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'local-agent-settings.ps1')
$settings = Get-LocalAgentSettings
$root = Split-Path $PSScriptRoot -Parent
$directory = Join-Path $root 'src/sentinel-agent-client'
$providerKey = [Environment]::GetEnvironmentVariable('ANTHROPIC_API_KEY','Process')
if (!$providerKey) { $providerKey = [Environment]::GetEnvironmentVariable('ANTHROPIC_API_KEY','User') }
if (!$providerKey) { $providerKey = [Environment]::GetEnvironmentVariable('ANTHROPIC_API_KEY','Machine') }
$sdk = Test-Path (Join-Path $directory 'providers/anthropic/node_modules/@anthropic-ai/sdk/package.json')
Write-Host ('Anthropic API key available: ' + [bool]$providerKey)
Write-Host ('Claude SDK installed: ' + $sdk)
Write-Host 'Local runner/gateway credentials: configured; signing environment excluded.'
$busy=$false
if (!$SmokeOnly) {
    $probe=New-Object Net.Sockets.TcpClient
    try { $connection=$probe.ConnectAsync('127.0.0.1',8021);$busy=$connection.Wait(750) -and $probe.Connected }
    catch { $busy=$false } finally { $probe.Dispose() }
    Write-Host ('Runner port 8021 already in use: '+$busy)
    if($busy -and !$CheckOnly){throw 'Port 8021 is already in use. Stop the existing agent with Ctrl+C in its terminal, then rerun this command. No process was stopped automatically.'}
}
Write-Host ('Agent API endpoint: '+$(if($HttpApi){'http://localhost:5156'}else{'https://localhost:7201'}))
if (!$sdk) { throw 'Install the pinned SDK: npm.cmd --prefix src/sentinel-agent-client/providers/anthropic ci --ignore-scripts' }
if ($CheckOnly) {
    if (!$providerKey) { throw 'Anthropic key is unavailable here. Use the PowerShell terminal where it is set, or run without -CheckOnly for a secure prompt.' }
    exit 0
}
if (!$providerKey) {
    $secure = Read-Host 'Enter your existing Anthropic API key (hidden; not stored)' -AsSecureString
    $pointer = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secure)
    try { $providerKey = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($pointer) }
    finally { [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($pointer); $secure.Dispose() }
}
if ([string]::IsNullOrWhiteSpace($providerKey)) { throw 'An existing Anthropic API key is required.' }
$node = (Get-Command node -ErrorAction Stop).Source
$start = New-Object Diagnostics.ProcessStartInfo
$start.FileName = $node
$start.WorkingDirectory = $directory
$start.UseShellExecute = $false
$start.CreateNoWindow = $true
$start.Arguments = if ($SmokeOnly) { '--experimental-strip-types demo/smoke.mjs' } else { '--experimental-strip-types src/server.mjs' }
# Do not inherit signing keys, worker tokens, JWT keys, database credentials,
# NODE_OPTIONS hooks or arbitrary environment variables into the AI process.
$start.EnvironmentVariables.Clear()
foreach ($name in @('SystemRoot','WINDIR','PATH','TEMP','TMP','USERPROFILE','LOCALAPPDATA')) {
    $value = [Environment]::GetEnvironmentVariable($name,'Process')
    if ($value) { $start.EnvironmentVariables[$name] = $value }
}
$start.EnvironmentVariables['SENTINEL_AGENT_PROVIDER'] = 'anthropic'
$start.EnvironmentVariables['ANTHROPIC_API_KEY'] = $providerKey
$start.EnvironmentVariables['ANTHROPIC_MODEL'] = 'claude-haiku-4-5-20251001'
if (!$SmokeOnly) {
    $start.EnvironmentVariables['SENTINEL_RUNNER_KEY'] = $settings.runnerKey
    $start.EnvironmentVariables['SENTINEL_AGENT_API_KEY'] = $settings.gatewayKey
    $start.EnvironmentVariables['SENTINEL_AGENT_PORT'] = '8021'
    $start.EnvironmentVariables['SENTINEL_API_URL'] = if ($HttpApi) { 'http://localhost:5156' } else { 'https://localhost:7201' }
    $start.EnvironmentVariables['SENTINEL_ALLOW_LOOPBACK_HTTP'] = if ($HttpApi) { 'true' } else { 'false' }
}
$process = $null
try {
    $process = [Diagnostics.Process]::Start($start)
    $providerKey = $null
    $start.EnvironmentVariables.Clear()
    $process.WaitForExit()
    if ($process.ExitCode -ne 0) { throw 'Claude agent failed. Review the sanitized error above; do not enable signing as a workaround.' }
} finally {
    $providerKey = $null
    $start.EnvironmentVariables.Clear()
    if ($process) {
        if (!$process.HasExited) { $process.Kill() }
        $process.Dispose()
    }
}
