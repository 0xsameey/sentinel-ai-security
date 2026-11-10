param([switch]$CheckOnly,[switch]$HttpApi)
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
$project=Join-Path $root 'src/Sentinel.Api'
$id=([xml](Get-Content (Join-Path $project 'Sentinel.Api.csproj'))).Project.PropertyGroup.UserSecretsId | Where-Object {$_}
$values=@{}
function Read-Values($object,[string]$prefix='') {
    foreach($property in $object.PSObject.Properties) {
        $name=$prefix+$property.Name
        if($property.Value -is [System.Management.Automation.PSCustomObject]) { Read-Values $property.Value ($name+':') }
        elseif($null -ne $property.Value) { $values[$name]=[string]$property.Value }
    }
}
foreach($file in @((Join-Path $project 'appsettings.json'),(Join-Path $project 'appsettings.Development.json'),(Join-Path $env:APPDATA "Microsoft/UserSecrets/$id/secrets.json"))) {
    if(Test-Path -LiteralPath $file){Read-Values (Get-Content -LiteralPath $file -Raw | ConvertFrom-Json)}
}
function Setting([string]$name) {
    $environment=[Environment]::GetEnvironmentVariable($name.Replace(':','__'),'Process')
    if($environment){return $environment}; return $values[$name]
}
$url=Setting 'DelegatedDispatcher:Url'
if($url -and $url.TrimEnd('/') -notin @('http://127.0.0.1:4301','http://127.0.0.1:4301/execute')){throw 'API delegated dispatcher must target http://127.0.0.1:4301/execute. No configuration changed.'}
if(!$url){throw 'DelegatedDispatcher:Url is missing from API configuration.'}
$dispatchKey=Setting 'DelegatedDispatcher:Key'
$workerKey=Setting 'AgentWorker:ApiKey'
$pin=Setting 'SecurityRelease:DelegatedAgentPublicKey'
if(!$dispatchKey -or $dispatchKey.Length -lt 32 -or !$workerKey -or !$pin){throw 'Existing API dispatch/worker credential or delegated public-key pin is missing. No credentials created.'}
$previous=@{}
$apiUrl=if($HttpApi){'http://localhost:5156'}else{'https://localhost:7201'}
$overrides=@{SENTINEL_PAYMENT_MODE='delegated';SENTINEL_DISPATCH_PORT='4301';SENTINEL_DISPATCH_KEY=$dispatchKey;SENTINEL_WORKER_KEY=$workerKey;SENTINEL_API_URL=$apiUrl;SENTINEL_EXPECTED_PUBLIC_KEY=$pin;SENTINEL_ALLOW_LOOPBACK_HTTP=([string][bool]$HttpApi).ToLowerInvariant()}
Push-Location (Join-Path $root 'src/sentinel-signer')
try {
    foreach($name in $overrides.Keys){$previous[$name]=[Environment]::GetEnvironmentVariable($name,'Process');[Environment]::SetEnvironmentVariable($name,$overrides[$name],'Process')}
    & node scripts/check-delegated-readiness.mjs
    if($LASTEXITCODE -ne 0){throw 'Read-only delegated key attestation failed. Signer was not started; no transaction sent.'}
    if(!$CheckOnly){
        Write-Host 'Starting delegated signer on 127.0.0.1:4301 with matching existing API credentials. No keys changed.'
        & npm.cmd start
        if($LASTEXITCODE -ne 0){throw 'Signer startup failed; inspect sanitized output.'}
    }
} finally {
    foreach($name in $previous.Keys){[Environment]::SetEnvironmentVariable($name,$previous[$name],'Process')}
    Pop-Location
}
