$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent
function Check-Exit([string]$step) {
    if ($LASTEXITCODE -ne 0) { throw "$step failed (exit $LASTEXITCODE)." }
}
Push-Location $repoRoot
try {
    # Creates fresh isolated PostgreSQL schemas; never migrates the application schema.
    # Includes .NET, signer tests and both existing frontend builds.
    & dotnet run --no-restore --project tools/Sentinel.Verification -- pg-e2e
    Check-Exit 'PostgreSQL, API, signer and frontend verification'
    & node src/sentinel-signer/node_modules/typescript/bin/tsc --noEmit -p src/sentinel-agent-client/tsconfig.json
    Check-Exit 'Agent client typecheck'
    & node --experimental-strip-types --test src/sentinel-agent-client/tests/client.test.mjs
    Check-Exit 'Structured agent client tests'
    & node --test tests/verification-checks.test.mjs
    Check-Exit 'Read-only diagnostic logic tests'
    & cmd.exe /d /c tests\run-rust-tests.cmd
    Check-Exit 'Rust contract tests'
    Write-Output 'Milestone 2 local verification passed. No live signing, submission, deployment or key changes.'
} finally { Pop-Location }
exit 0
