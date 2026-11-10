$ErrorActionPreference = 'Stop'
Push-Location (Split-Path $PSScriptRoot -Parent)
try {
    $cargo = Join-Path $env:USERPROFILE '.cargo/bin/cargo.exe'
    $rustc = Join-Path $env:USERPROFILE '.cargo/bin/rustc.exe'
    $version = & $rustc --version
    if ($LASTEXITCODE -ne 0 -or $version -notmatch '^rustc 1\.99\.1 ') { throw 'Reviewed build requires Rust 1.99.1.' }
    $contract = 'contracts/sentinel-contract-starter/sentinel-contract'
    $release = Join-Path '.test-artifacts' ('milestone5b-release-' + [guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $release | Out-Null
    $previousFlags = $env:RUSTFLAGS
    try {
        $env:RUSTFLAGS = '-C link-arg=-s --cfg near'
        & $cargo build --target wasm32-unknown-unknown --release --offline --locked --manifest-path "$contract/Cargo.toml"
        if ($LASTEXITCODE -ne 0) { throw 'Contract build failed.' }
    } finally { $env:RUSTFLAGS = $previousFlags }
    Copy-Item -LiteralPath "$contract/target/wasm32-unknown-unknown/release/sentinel_contract.wasm" -Destination "$release/raw.wasm"
    $optimizer = 'tools/wasm-artifact-opt/target/debug/sentinel-wasm-artifact-opt.exe'
    if (!(Test-Path -LiteralPath $optimizer)) { throw 'Build reviewed optimizer with tests/build-artifact-optimizer.cmd first.' }
    & $optimizer "$release/raw.wasm" "$release/corrected.wasm"
    if ($LASTEXITCODE -ne 0) { throw 'Optimization failed.' }
    $files = @("$contract/src/lib.rs", "$contract/src/delegation.rs", "$contract/Cargo.lock", 'tools/wasm-artifact-opt/Cargo.lock', $optimizer, "$release/raw.wasm", "$release/corrected.wasm")
    $manifest = [ordered]@{ network='testnet'; contract='0xsameey-sentinel-v2.testnet'; rustc=$version; flags='-C link-arg=-s --cfg near'; optimizer='wasm-opt 0.116.1; size; TruncSat; BulkMemory'; deployed=$false; files=@($files | ForEach-Object { @{path=$_;sha256=(Get-FileHash -LiteralPath $_ -Algorithm SHA256).Hash.ToLowerInvariant()} }) }
    $manifest | ConvertTo-Json -Depth 5 | Set-Content -Encoding UTF8 "$release/manifest.json"
    Write-Output "LOCAL_ARTIFACT_PREPARED: $release/manifest.json. Deployment requires separate approval."
} finally { Pop-Location }

