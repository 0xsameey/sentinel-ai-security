# Real application startup and manual verification

## Final submission launch defaults

At the owner's request, `start-real-api.ps1` and `start-real-dashboard.ps1`
now launch the full application by default. The manual-only banner, disabled
delegated option, wallet-connection block, transaction-signing block and API
dispatch block no longer apply with the normal commands. Owner-approved remains
the initial payment mode. Contract policy, owner authentication, WARN review,
BLOCK rejection, restricted signer permissions and execution permits still apply.
No deployment verification is implied by this configuration change; the owner
reports the new contract deployed. No signer was started or transaction sent.

Restart API and dashboard terminals to pick up these defaults:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tests/start-real-api.ps1 -HttpOnly
powershell -NoProfile -ExecutionPolicy Bypass -File tests/start-real-dashboard.ps1 -HttpApi
```

Run these in separate terminals. Claude runner command remains
`powershell -NoProfile -ExecutionPolicy Bypass -File tests/start-real-agent.ps1 -HttpApi`.
HTTP is for local application use; the signer still requires HTTPS API
configuration and its existing restricted credentials. These scripts do not
start signing workers. Add `-ManualTesting` to **both** API and dashboard commands
to explicitly restore the earlier restricted inspection mode. Historical
statements below about manual restrictions describe that earlier verification
session, not the current launch defaults.

## Local AI setup update (2026-11-05)

The `AGENT_RUNNER_UNCONFIGURED` failure was caused by missing API-to-runner
settings. With explicit owner permission, two distinct 32-byte local service
tokens were created in `.test-artifacts/local-agent-services.json`. This ignored
file contains credentials: do not commit, share, display or attach it to reports.
They are not NEAR keys. No signing key, transaction or application database was
changed. `configure-local-agent.ps1` preserves valid existing tokens on rerun.

Both real startup scripts now use that file. The API script temporarily sets
only `AgentRunner__Url`, `AgentRunner__ApiKey`, and `AgentGateway__ApiKey` in its
process environment, restoring previous values on exit. Its configured runner
is `http://127.0.0.1:8021`. Dispatch remains disabled. The agent child process
uses an environment allowlist, excluding signing credentials, worker tokens,
database/JWT settings and Node injection hooks. Tokens are transport credentials,
never model inputs. HTTPS remains the default; `-HttpApi` explicitly allows HTTP
only to literal loopback hosts, with redirects still rejected.

The Claude SDK is installed. Anthropic key presence was checked without showing
values in process/user/machine environment, API development user-secrets and
standard workspace `.env` locations. No key was found in this execution session.
A key configured only in an operator's open terminal is not inherited here.
The launcher checks process, user and machine environment and otherwise prompts
for the existing key without echo or persistence. Never paste it into chat.

From repository root, restart the API in its terminal (Ctrl+C first):

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tests/start-real-api.ps1 -HttpOnly
```

In another terminal, preferably the one already holding `ANTHROPIC_API_KEY`:

```powershell
# Optional presence check; reports booleans, not secrets:
powershell -NoProfile -ExecutionPolicy Bypass -File tests/start-real-agent.ps1 -HttpApi -CheckOnly
# Optional minimal paid Claude call; no gateway or NEAR access:
powershell -NoProfile -ExecutionPolicy Bypass -File tests/start-real-agent.ps1 -SmokeOnly
# Keep this running for dashboard proposals:
powershell -NoProfile -ExecutionPolicy Bypass -File tests/start-real-agent.ps1 -HttpApi
```

Expected `AGENT_LISTENING` on port 8021. Model is explicitly
`claude-haiku-4-5-20251001`. Keep the dashboard on 5173; retry the task with a new
run ID. Do not start a signer. The prior 503 occurred before a run was claimed in
the database; it does not represent a model or transaction failure.

Verification: settings created, validated and re-run without rotation; all four
PowerShell scripts parse; 73 agent tests and workspace TypeScript check pass,
including rejection of remote HTTP and credential-bearing URLs. Launcher
`-CheckOnly` confirms SDK and service tokens but fails honestly for the absent
Anthropic key in this execution session. No real model call or authenticated
dashboard task is claimed verified. Earlier configuration/dependency observations
below are historical where superseded by this update.

Checked 2026-11-05. This uses the actual React application, ASP.NET Core API,
existing PostgreSQL database and configured HTTPS NEAR testnet RPC. It does not
use the Milestone 5B simulated demo. **Local startup is verified; full live
testnet and authenticated browser verification remains blocked. Payment
execution remains NO-GO.** The Milestone 6A contract upgrade decision is unchanged.

## Startup order and commands

Run separate normal Windows PowerShell terminals from:

```powershell
Set-Location C:\Users\PC\Documents\hackerthon\Sentinel
```

1. PostgreSQL must already be running. Inspect the existing database without DDL:

```powershell
dotnet run --no-restore --project tools/Sentinel.Verification -- pg-startup-check
```

Expected `DATABASE_READY`, exit 0. Any pending migration stops API startup.
Do not run `database update`, `pg-e2e`, `pg-demo`, or the simulated demo startup
against the application database as a readiness workaround.

2. API terminal, explicit loopback HTTP fallback:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tests/start-real-api.ps1 -HttpOnly
```

API: `http://localhost:5156`. The script sets Development explicitly, checks the
database, disables payment dispatch and the reconciliation hosted worker, and
uses process-memory ASP.NET Data Protection. It does not change persistent
profile keys. Ordinary database writes such as authentication challenges and
security assessments are still real application writes. No migrations run.

3. Dashboard terminal:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tests/start-real-dashboard.ps1 -HttpApi
```

Open **http://localhost:5173/**, not `/demo`. Port 5173 is strict: do not accept an
automatic switch to 5174. The script overrides `VITE_API_URL` in this process to
HTTP 5156 and sets `VITE_MANUAL_TESTING=true`; `.env.local` remains unchanged.
The normal configured API URL is HTTPS 7201. CORS permits exactly
`http://localhost:5173`; using `127.0.0.1:5173` is a different origin.

4. Read-only verification terminal:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tests/verify-real-startup.ps1 -HttpApi
```

Expected health, contract config, balance and dashboard HTTP 200. The script
checks the expected owner/agent without printing credentials. RPC failure is a
failure, not a simulated success. Ctrl+C stops each owned server. Do not stop
unrelated processes to free ports; an existing local demo was using 8040.

### HTTPS option

```powershell
dotnet dev-certs https --check --trust
```

This is inspection only. Here it returned exit 6: no valid certificate found.
No certificate was generated or trusted. Do not bypass browser certificate
warnings. After separately authorized certificate setup, omit `-HttpOnly` and
`-HttpApi` from the two startup commands; API HTTPS is 7201 and HTTP 5156.
The signer requires an HTTPS API URL, so the HTTP fallback is deliberately
unsuitable for signer startup. External RPC remains HTTPS in either mode.

## Database and recovery readiness

Read-only connection succeeded: PostgreSQL 18.2, loopback `sentinel_db`, role
`sentinel_user`. All seven repository migrations are already in public history:

```text
20261029173437_InitialCreate
20261030115009_AddAuthChallenges
20261031235955_AddPaymentTracking
20261101002600_UniqueTransactionHash
20261101154640_AddPaymentRiskAssessment
20261103102602_SecurityGateway
20261104095505_DelegatedSpending
```

All ten required application tables and the additional security payment columns
were found. **No migration was applied in this session.** The startup check uses
a read-only transaction and never creates a database/schema. The prior pending
SQL is now historical, not an instruction to reapply it. Regeneration command:

```powershell
dotnet ef migrations script 20261101154640_AddPaymentRiskAssessment 20261104095505_DelegatedSpending --idempotent --no-build --project src/Sentinel.Infrastructure --startup-project src/Sentinel.Api --output .test-artifacts/real-startup-migration-review.sql
```

Generated script and `.test-artifacts/milestone6a-pending-migrations.sql` both
SHA256 `45b1a4892170155a3f818c0d2ae4a8c426b2062930c51bf905a770517eb74547`.
Reviewed transaction boundaries, migration-history guards, additive tables,
columns, indexes and restrictive foreign keys; no DROP/TRUNCATE/data-delete
statements. This is generation/review, not executing SQL. EF CLI 10.0.3 warns
that runtime is 10.0.14; align tooling before future migration work.

No recoverable database dump was found in the workspace search or a backup
directory immediately above it. SQL migration scripts are **not data backups**.
No restore was attempted and machine-wide backup absence is not established.
Obtain a verified PostgreSQL custom-format dump and rehearse restoration into a
separately approved isolated database before any future schema change. Example
operator backup command (not run; prompts for password):

```powershell
& 'C:\Program Files\PostgreSQL\18\bin\pg_dump.exe' -h localhost -U sentinel_user -d sentinel_db -W -Fc -f .test-artifacts\sentinel-before-change.dump
& 'C:\Program Files\PostgreSQL\18\bin\pg_restore.exe' --list .test-artifacts\sentinel-before-change.dump
```

A successful archive listing alone is not a restore test. Never put passwords
in command arguments or paste connection strings into reports.

## Configuration, agent and workers

API database connection, JWT signing configuration, issuer, audience, NEP-413
recipient, contract and HTTPS RPC are configured. Values of secrets were not
printed. Contract is `0xsameey-sentinel-v2.testnet`; RPC host is
`test.rpc.fastnear.com`. API challenge recipient is a consistent authentication
identifier; it need not equal the frontend CORS origin.

The agent gateway credential, runner URL/credential, delegated dispatcher
URL/credential and release signer public-key pins are missing. Worker credential
exists, but its value was not inspected in output. No worker was started.
Ports reserved for later use: agent 8021, owner signer 4301, delegated signer
4302 (explicit override). Owner-approved is the dashboard default; delegated
mode is disabled in manual mode. No signer keys are provisioned or rotated.

### AI agent terminal (not started; configuration prerequisites outstanding)

Node 22.19.0 is installed. OpenAI SDK is installed. Official Claude SDK is absent
in its provider package. `ANTHROPIC_API_KEY`, `ANTHROPIC_MODEL`,
`SENTINEL_AGENT_API_KEY`, and `SENTINEL_RUNNER_KEY` are absent from this agent's
execution environment; local credentials in another terminal are not inherited.
No provider request was made. Normal PowerShell commands:

```powershell
Set-Location C:\Users\PC\Documents\hackerthon\Sentinel\src\sentinel-agent-client
'ANTHROPIC_API_KEY','ANTHROPIC_MODEL' | ForEach-Object {
    '{0}: {1}' -f $_, [bool][Environment]::GetEnvironmentVariable($_, 'Process')
}
# Install the pinned provider dependency only if it is missing:
npm.cmd --prefix providers/anthropic install --ignore-scripts
$env:SENTINEL_AGENT_PROVIDER = 'anthropic'
$env:ANTHROPIC_MODEL = 'claude-haiku-4-5-20251001'
npm.cmd run smoke:real
```

Smoke performs one model/tool-selection call without executing tools or accessing
NEAR/gateway. Expected `REAL_MODEL_SMOKE_PASS`, `signed:false`. Never print the
API key. Before the runner command below, supply existing service credentials in
this terminal: `SENTINEL_AGENT_API_KEY` must match API `AgentGateway:ApiKey`;
`SENTINEL_RUNNER_KEY` must match `AgentRunner:ApiKey`; configure API
`AgentRunner:Url=http://127.0.0.1:8021`. Do not pass owner JWTs, signing keys,
worker tokens or dispatch tokens to the model process. Its startup rejects known
signing/worker environment variables.

```powershell
$env:SENTINEL_API_URL = 'http://localhost:5156'
$env:SENTINEL_AGENT_PORT = '8021'
npm.cmd start
```

For OpenAI use `SENTINEL_AGENT_PROVIDER=openai` and existing `OPENAI_API_KEY` /
`OPENAI_MODEL`; the workflow is shared. Do not start with placeholder secrets.
The agent's own `npm run typecheck` currently fails because optional native
`@typescript/typescript-win32-x64` is missing. The established workspace compiler
passes:

```powershell
node src/sentinel-signer/node_modules/typescript/bin/tsc --noEmit -p src/sentinel-agent-client/tsconfig.json
```

Run that last command from repository root.

### Signer terminals — reference only, DO NOT RUN in this phase

```powershell
Set-Location C:\Users\PC\Documents\hackerthon\Sentinel\src\sentinel-signer
npm.cmd run build
# Owner signer, only after a separate execution approval and key attestation:
$env:SENTINEL_PAYMENT_MODE = 'owner'
$env:SENTINEL_DISPATCH_PORT = '4301'
npm.cmd start
# Delegated signer, separate terminal and separate approval AFTER safe upgrade:
$env:SENTINEL_PAYMENT_MODE = 'delegated'
$env:SENTINEL_DISPATCH_PORT = '4302'
npm.cmd start
```

Required existing configuration: `NEAR_NETWORK=testnet`, `NEAR_CONTRACT_ID`,
`NEAR_AGENT_ACCOUNT_ID`, `NEAR_RPC_URL` (HTTPS), `SENTINEL_API_URL` (HTTPS),
`NEAR_AGENT_PRIVATE_KEY`, `SENTINEL_WORKER_KEY`, `SENTINEL_DISPATCH_KEY`.
Restricted key permission must name the exact contract and only `execute_payment`
for owner mode or only `execute_delegated_payment` for delegated mode, with finite
gas allowance. No wildcard/full-access key. These commands are not authorization
to start dispatch, create keys or submit a transaction. API dispatch remains 503.

## Verified results and outstanding manual checks

Evidence is under `.test-artifacts/real-startup-*` and `real-api.log` /
`real-dashboard.log`.

| Check | Observed result |
|---|---|
| Existing PostgreSQL / startup preflight | Connected; all seven migrations applied; DATABASE_READY |
| Actual API and Vite startup | Succeeded, 5156 and 5173 |
| Database health | HTTP 200 |
| CORS preflight | 204; allowed origin localhost:5173 |
| Anonymous identity | 401 |
| Real auth challenge | 200; 32-byte nonce and future expiry; one normal challenge row written |
| Forged wallet signature | 401; no JWT minted |
| Owner dispatch / delegated tasks | 503 with manual safety code |
| Live config and balance | Initially 500; fixed sanitized transport handling, now 502 |
| RPC cause | Socket error 10013: outbound HTTPS denied in agent execution environment |
| Frontend HTTP / transformed module | 200; HTTP API override and manual mode served |
| Unit tests | 42 passed, zero failures/skips |
| Targeted WorkerHttpTests | 3 passed, zero failures/skips |
| Agent tests | 72 passed, zero failures/skips |
| Frontend tests | 5 passed, zero failures/skips, including wallet/signing guards |
| Frontend production build / lint | Passed; dependency direct-eval build warning remains |
| Agent workspace / signer typecheck | Passed; standalone agent compiler failure noted above |
| Browser automation | Chrome tool exited; native computer-use pipe unavailable |

These are 122 passing tests in the listed suites, not a new full milestone-suite
claim. Rust and full PostgreSQL integration suites were not rerun: no contract
changes, and this task concerns the existing real application database.

User chose to start the API in normal PowerShell later. The restricted-session
API is stopped after checks to release 5156; the Vite process launched here
remains available while this session is active. Use the startup commands for a
repeatable operator-owned session. Do not claim live testnet connectivity until
`verify-real-startup.ps1` passes from that normal shell.

Manual steps still required:

1. Run the normal-shell API and GET verification; confirm contract identity and
   live values. Do not change RPC to fixtures when a read fails.
2. Inspect dashboard layout, banner, tabs, error handling and payment lookup in a
   real browser. Verify owner mode selected, delegated disabled and transaction
   signing/dispatch disabled. Do not click transaction or policy actions.
3. **Use an existing wallet session only.** Installed Meteor SDK
   `requestSignIn` creates a random access key and logs its key object. New
   connection is blocked before the SDK in manual mode. Do not use a new login,
   approve AddKey, or copy console wallet output. Dependency behavior requires a
   separate security fix/review before enabling ordinary new connections.
4. With an existing session, manually sign only the NEP-413 authentication
   message. Never approve a transaction. Confirm `/auth/me` identifies the owner.
   No existing session means authentication is blocked under the no-new-key
   constraint; do not forge tokens as a workaround.
5. Assess an owner-approved proposal through the deterministic gateway; inspect
   ALLOW/WARN/BLOCK and the persisted assessment. Human WARN review must use the
   authenticated owner and an explicit review action. No approvals were forged
   or issued in this audit. Preparation and dispatch buttons remain disabled.
6. Inspect an existing payment's tracking/audit timeline and its real transaction
   evidence. Do not label attempted/reserved records as settled. Authenticated
   risk, human-review and audit-history flows remain unverified here because a
   real wallet session and reachable live RPC were unavailable.

No deployment, chain transaction, key change, database migration or payment
approval was performed. No dispatch or signing worker was enabled. Separate
specific approvals remain necessary for any future certificate/key setup,
schema change, contract upgrade, worker enablement or payment.
