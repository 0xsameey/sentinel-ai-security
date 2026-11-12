# Sentinel

**AI Payments. Your Rules.**

Sentinel is a solo-built NEAR AI Security & Defense hackathon project that lets AI agents propose and make payments without receiving unrestricted access to an owner's wallet. Claude proposes the payment; independent security checks, owner-defined spending permissions, a restricted signer, and a NEAR contract control execution.

Two payment modes share the same security boundary:

- **Owner-approved payments** — the default. The owner authorizes each payment through their wallet.
- **Autonomous payments** — an agent can execute eligible payments within an owner-created, revocable spending delegation. WARN decisions still require authenticated human review.

[Repository](https://github.com/0xsameey/sentinel-ai-security) · [Testnet payment evidence](#verified-near-testnet-transaction) · [Local startup](#starting-the-application-locally) · [Security tests](#security-testing-and-prompt-injection-demonstration)

## The problem Sentinel solves

Giving an AI agent a full-access wallet key turns a mistaken instruction, compromised tool response, or compromised worker into a financial risk. Requiring a wallet signature for every payment prevents useful autonomous operation.

Sentinel separates proposing a payment from granting authority to spend. Owners set enforceable boundaries; an agent can operate inside them, but cannot create its own delegation, expand its budget, approve its own WARN decision, or edit security policy.

## How Sentinel works

1. The owner connects a NEAR wallet and authenticates using a signed challenge.
2. A user describes a payment task. Claude uses structured tools to propose a recipient, exact amount, purpose, and expiration.
3. The deterministic gateway assesses current contract facts and policy, independently of the model's explanation: **ALLOW**, **WARN**, or **BLOCK**.
4. An owner-approved payment requires wallet authorization. An autonomous payment additionally requires a valid delegation; WARN requires human approval and fresh validation. BLOCK cannot obtain an execution permit.
5. The delegated signer accepts only an authenticated dispatch with a short-lived, single-use permit bound to the exact payment. It verifies the restricted NEAR key before invoking `execute_delegated_payment`.
6. The contract checks authority and limits, charges attempted spending, and reserves funds before the asynchronous transfer. Callback state and independently checked chain evidence drive reconciliation.

**ALLOW is a security decision, not proof of authorization or payment. A reported transaction hash is not settlement.** The dashboard presents risk and payment lifecycle separately.

## Architecture

```mermaid
flowchart TD
    Owner[Owner / NEAR wallet] -->|Authenticate and explicitly sign owner actions| UI[React / TypeScript dashboard]
    UI --> API[ASP.NET Core API]
    API <-->|Durable proposals, approvals, permits and audit records| DB[(PostgreSQL)]
    API -->|Authenticated task| Runner[Isolated AI agent runner]
    Runner <-->|Structured tool workflow| Claude[Claude provider / optional OpenAI provider]
    Runner -->|Proposal and tool requests| Gateway[Deterministic security gateway]
    Gateway -->|Persist assessment| DB
    Gateway -->|Fresh contract and account facts| RPC[NEAR Testnet RPC]
    API -->|Authenticated exact-payment dispatch| Signer[Restricted delegated signer]
    Signer -->|Consume single-use permit and revalidate| Gateway
    Signer -->|Restricted function-call key| Contract[Rust NEAR contract]
    Owner -->|Wallet-signed policies, delegations and owner authorizations| Contract
    Contract -->|Reserved asynchronous transfer and callback| Recipient[Allowed recipient]
    Contract <--> RPC
    API -->|Independent reconciliation| RPC
```

The AI does not receive the signer's private key, database credentials, JWT signing key, worker token, or delegation-management authority. Model output and external tool text are untrusted inputs to a separately enforced payment workflow.

## Key features

- Claude-powered payment proposals through a provider-independent TypeScript workflow; an OpenAI adapter is also implemented.
- Deterministic ALLOW / WARN / BLOCK assessments with human-readable reasons and technical evidence.
- Authenticated human review for WARN, distinct from wallet authorization.
- Owner-created delegations with maximum payment, aggregate attempted-spending budget, daily attempted-spending limit, recipient restrictions, expiration, and revocation.
- Restricted NEAR function-call keys and reviewed contract-code/public-key release gates.
- On-chain spending checks, payment-ID replay protection, fund reservations, and asynchronous transfer callbacks.
- Durable payment, approval, permit, dispatch, and reconciliation tracking in PostgreSQL.
- A five-page dashboard: **Overview**, **Risk Assessment**, **AI Payments**, **Transactions**, and **Security & Delegations**.
- Local security suites and a controlled real-Claude prompt-injection demonstration using a mocked gateway.

## Security architecture and trust boundaries

| Boundary | Enforcement |
| --- | --- |
| Model → payment tools | Structured schemas and task ceilings constrain proposals. Model instructions do not change deterministic policy. |
| User → management API | Wallet authentication and owner checks protect delegation management and human approvals. Agents cannot grant or increase authority. |
| Gateway → signer | Single-use, expiring permits bind contract, method, agent, delegation ID, payment ID, recipient, amount, and the validated intent. Changed or expired intent requires rejection. |
| Signer → NEAR | Delegated execution requires a function-call key for the intended contract with exactly `execute_delegated_payment`, a matching public-key pin, and sufficient finite gas allowance. Full-access keys and unrestricted method lists are rejected. |
| Contract → transfer | Current agent, pause state, global policy, recipient, delegation, expiration, and spending quotas are checked on-chain. Reservation and charging precede the asynchronous transfer. |
| Submission → settlement | Dispatch responses and hashes are reported evidence. Settlement requires matching independently reconciled contract/chain evidence. Uncertain outcomes are not automatically retried. |

Payment authorization cannot exceed 24 hours or outlive its delegation. Delegations themselves must expire within 24 hours and explicitly list between 1 and 16 globally approved recipients. Amounts use integer yoctoNEAR strings; `0.001 NEAR` is `1000000000000000000000` yoctoNEAR. Expirations use Unix nanoseconds, also transported as strings to avoid JavaScript precision loss.

**Attempt budgets are conservative:** failed transfers can still consume delegation budget and daily allowance. A missing callback can leave funds reserved. Revocation stops future permitted execution; it cannot reverse an already executed or in-flight transfer. Single-use permit consumption prevents automatic re-execution after ambiguous dispatch; inspect and reconcile the existing payment before creating another request.

The contract is the final boundary for owner-authorized spending limits. A compromised worker holding the restricted execution key still cannot exceed those on-chain limits. Gateway-only risk checks and human-review rules are an additional off-chain boundary; a restricted key alone does not encode every gateway decision on-chain. Protect the signer and its credentials accordingly.

## Technology stack

| Component | Implementation |
| --- | --- |
| Dashboard | React 19, TypeScript, Vite, Lucide, NEAR Wallet Selector |
| API and application services | ASP.NET Core / .NET 10, Entity Framework Core |
| Persistence | PostgreSQL through Npgsql; EF Core migrations |
| AI runner | Node.js / TypeScript, Anthropic SDK; optional OpenAI adapter |
| Signer | Node.js / TypeScript, Express, `near-api-js` |
| Contract | Rust, `near-sdk` 5.29.1, NEAR Testnet |
| Verification | Rust unit/storage rehearsal tests, xUnit, Node test runner, isolated PostgreSQL integration harness |

Source: [dashboard](src/sentinel-dashboard), [API](src/Sentinel.Api), [application](src/Sentinel.Application), [infrastructure](src/Sentinel.Infrastructure), [agent](src/sentinel-agent-client), [signer](src/sentinel-signer), [contract](contracts/sentinel-contract-starter/sentinel-contract).

## Getting started

### Prerequisites

Windows PowerShell instructions below assume these tools are installed and on `PATH`:

- [Git](https://git-scm.com/downloads), [Node.js](https://nodejs.org/en/download) **22.18 or later** with npm, and the [.NET 10 SDK](https://dotnet.microsoft.com/en-us/download/dotnet/10.0).
- [PostgreSQL for Windows](https://www.postgresql.org/download/windows/), including `psql`, `createuser`, `createdb`, `pg_dump`, and `pg_restore`. PostgreSQL 18 is used by the local project tooling; add its `bin` directory to `PATH`.
- For contract tests: [Rust](https://rustup.rs/) with the Windows MSVC toolchain and [Visual Studio Build Tools](https://visualstudio.microsoft.com/downloads/) with Desktop development with C++ and a Windows SDK. The Rust wrapper selects the developer environment and enables the prebuilt NASM fallback.
- An Anthropic API account for real Claude calls. Local fixture tests do not need model credentials.
- For live payments only: owner wallet access, testnet funds, a compatible contract, and an already-provisioned restricted agent key. Installation commands below do not create NEAR keys or deploy a contract.

```powershell
git clone https://github.com/0xsameey/sentinel-ai-security.git
Set-Location sentinel-ai-security
node --version
npm.cmd --version
dotnet --version
psql --version

dotnet restore Sentinel.slnx
npm.cmd --prefix src/sentinel-dashboard ci
npm.cmd --prefix src/sentinel-agent-client ci
npm.cmd --prefix src/sentinel-agent-client/providers/anthropic ci --ignore-scripts
npm.cmd --prefix src/sentinel-signer ci
npm.cmd --prefix src/sentinel-signer run build

# Needed before the offline Rust test wrapper, if crates are not already cached.
cargo fetch --locked --manifest-path contracts/sentinel-contract-starter/sentinel-contract/Cargo.toml
```

Run subsequent commands from the repository root. The GitHub URL is the submission repository supplied by the author; anonymous clone access was not independently verified during README preparation.

### Local PostgreSQL

For a **new local development database**, create a separate application role and database. These commands prompt for credentials; do not put passwords in shell history:

```powershell
createuser -h localhost -U postgres --pwprompt sentinel_user
createdb -h localhost -U postgres --owner=sentinel_user sentinel_db
```

If using an existing database, preserve its records and role configuration. Configure the connection below before inspecting or applying migrations.

## Environment configuration

### API settings

The API project already has a user-secrets ID. Use **development user-secrets**, process environment variables, or a private deployment secret store. Do not put secrets in tracked `appsettings` files or frontend `VITE_*` variables. Development user-secrets are local storage, not an encrypted production vault.

| API setting | Required value / role |
| --- | --- |
| `ConnectionStrings:SentinelDb` | Local PostgreSQL connection string, e.g. `Host=localhost;Port=5432;Database=sentinel_db;Username=sentinel_user;Password=<local password>` (configuration example only). |
| `Auth:JwtSigningKey` | Secret Base64 value decoding to at least 32 random bytes; used only for API session tokens. |
| `AgentWorker:ApiKey` | Independent secret used by signer/worker → API calls. |
| `DelegatedDispatcher:Key` | Independent secret, at least 32 characters, for API → signer authentication. |
| `DelegatedDispatcher:Url` | `http://127.0.0.1:4301/execute` for the delegated signer. |
| `SecurityRelease:DelegatedAgentPublicKey` | Public `ed25519:` key corresponding to the existing restricted delegated signing key. Not a private key. |
| `SecurityRelease:AgentPublicKey` | Separate restricted execution public-key pin if enabling the owner-approved execution worker. |
| `Near:RpcUrl` / `Near:ContractId` | Defaults: `https://test.rpc.fastnear.com` / `0xsameey-sentinel-v2.testnet`. |

Development defaults also define issuer `sentinel-api`, audience `sentinel-dashboard`, challenge recipient `sentinel.local`, and 15-minute access tokens. Environment overrides use double underscores, for example `ConnectionStrings__SentinelDb`.

For a fresh setup, this helper prompts without echoing each secret and passes JSON to `dotnet user-secrets` through standard input. Supply distinct, securely generated application secrets. **Do not overwrite an existing installation's credentials.**

```powershell
function Set-SentinelSecret([string]$Name) {
    $secureValue = Read-Host $Name -AsSecureString
    $pointer = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secureValue)
    try {
        $entry = @{}
        $entry[$Name] = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($pointer)
        $entry | ConvertTo-Json -Compress | dotnet user-secrets set --project src/Sentinel.Api
        $entry.Clear()
    } finally {
        [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($pointer)
        $secureValue.Dispose()
    }
}
Set-SentinelSecret 'ConnectionStrings:SentinelDb'
Set-SentinelSecret 'Auth:JwtSigningKey'
Set-SentinelSecret 'AgentWorker:ApiKey'
Set-SentinelSecret 'DelegatedDispatcher:Key'

dotnet user-secrets set 'DelegatedDispatcher:Url' 'http://127.0.0.1:4301/execute' --project src/Sentinel.Api
# Public key prompt, not a private signing key.
$delegatedPublicKey = Read-Host 'Existing restricted delegated public key (ed25519:...)'
dotnet user-secrets set 'SecurityRelease:DelegatedAgentPublicKey' $delegatedPublicKey --project src/Sentinel.Api

powershell -NoProfile -ExecutionPolicy Bypass -File tests/configure-local-agent.ps1
```

`configure-local-agent.ps1` creates two distinct local API↔runner/gateway service credentials if absent and preserves existing credentials. It creates no NEAR keys. The launchers load them from `.test-artifacts/local-agent-services.json` without printing their values.

**Publication prerequisite:** treat that file as secret. The current root `.gitignore` does not explicitly exclude `.test-artifacts/`; verify that no generated credentials, database dumps, or sensitive artifacts are staged before publishing. This README task did not change ignore rules or Git settings.

### Claude, signer, and dashboard

- **Claude:** `start-real-agent.ps1` looks for an existing `ANTHROPIC_API_KEY` in process, user, or machine environment; normal startup can prompt securely if absent. It pins `claude-haiku-4-5-20251001`. Its child process receives an allowlisted environment, excluding signing and worker credentials. `-CheckOnly` checks local configuration; `-SmokeOnly` makes a real model request without blockchain access.
- **Signer:** create a private `src/sentinel-signer/.env` using a secure editor with `NEAR_NETWORK=testnet`, `NEAR_RPC_URL=https://test.rpc.fastnear.com`, `NEAR_CONTRACT_ID=0xsameey-sentinel-v2.testnet`, `NEAR_AGENT_ACCOUNT_ID=0xsameey-agent.testnet`, and `NEAR_AGENT_PRIVATE_KEY` set to the **existing restricted function-call key**. Do not use a full-access key. There is no signer `.env.example` to copy. The launcher supplies API URL, matching dispatcher/worker credentials, public-key pin, port, and delegated mode from API configuration.
- **Dashboard:** create `src/sentinel-dashboard/.env.local` containing `VITE_NEAR_CONTRACT_ID=0xsameey-sentinel-v2.testnet`. Launch scripts supply `VITE_API_URL` and `VITE_MANUAL_TESTING`. Everything prefixed `VITE_` is public browser configuration; never place private keys or service tokens there.
- **Optional OpenAI:** the provider-independent runner supports `SENTINEL_AGENT_PROVIDER=openai`, `OPENAI_API_KEY`, and `OPENAI_MODEL`. The supplied `start-real-agent.ps1` intentionally selects Claude; changing an API `OpenAI:Model` setting does not switch that launcher.

### Migrations: inspect, review, then explicitly apply

EF migrations are in [Persistence/Migrations](src/Sentinel.Infrastructure/Persistence/Migrations). The API launcher **does not apply them**: its read-only startup check requires the existing application schema to be current.

```powershell
$env:ASPNETCORE_ENVIRONMENT = 'Development'
# Install once if absent; keep the EF tool aligned with the project's EF packages.
dotnet tool install --global dotnet-ef --version 10.0.14

# Existing databases only: read-only connectivity and pending-migration check.
dotnet run --project tools/Sentinel.Verification -- pg-startup-check

# Generate SQL for review; this does not apply it.
dotnet ef migrations script --idempotent --project src/Sentinel.Infrastructure --startup-project src/Sentinel.Api --output "$env:TEMP\sentinel-migrations.sql"

# Existing database: back up outside the repository before any schema changes.
pg_dump -h localhost -U sentinel_user -W -Fc -f "$env:TEMP\sentinel-before-migration.dump" sentinel_db
pg_restore --list "$env:TEMP\sentinel-before-migration.dump"

# MUTATING: only after SQL review and explicit operator approval for this database.
dotnet ef database update --project src/Sentinel.Infrastructure --startup-project src/Sentinel.Api

dotnet run --project tools/Sentinel.Verification -- pg-startup-check
```

A fresh empty database will fail the existing-schema check until migrations are applied. A readable backup archive is not proof of recoverability: restore into a separate disposable database and validate it before upgrading valuable records. Do not migrate the application database merely to run tests.

## Starting the application locally

Use separate PowerShell terminals, each at the repository root. PostgreSQL must be running and the schema ready first. Start the API, agent, optional signer, then dashboard.

The following consistent **loopback HTTP** configuration avoids Node development-certificate trust differences. NEAR RPC remains HTTPS. HTTP is permitted by these helpers only for local API development.

```powershell
# Terminal 1: real API, database readiness check first; HTTP port 5156.
powershell -NoProfile -ExecutionPolicy Bypass -File tests/start-real-api.ps1 -HttpOnly

# Terminal 2: Claude runner, 127.0.0.1:8021.
powershell -NoProfile -ExecutionPolicy Bypass -File tests/start-real-agent.ps1 -HttpApi

# Terminal 3: first attest the existing restricted key without signing.
powershell -NoProfile -ExecutionPolicy Bypass -File tests/start-real-delegated-signer.ps1 -HttpApi -CheckOnly
# Start only when intentionally enabling live delegated execution; 127.0.0.1:4301.
powershell -NoProfile -ExecutionPolicy Bypass -File tests/start-real-delegated-signer.ps1 -HttpApi

# Terminal 4: dashboard, http://localhost:5173.
powershell -NoProfile -ExecutionPolicy Bypass -File tests/start-real-dashboard.ps1 -HttpApi

# Another terminal: read-only health, contract config/balance and dashboard checks.
powershell -NoProfile -ExecutionPolicy Bypass -File tests/verify-real-startup.ps1 -HttpApi
```

**Default startup is transaction-capable.** Opening the dashboard does not initiate a payment, but asking the autonomous agent to make a payment can dispatch an eligible ALLOW proposal. Do not start the signer or submit a live payment task during read-only inspection.

For guarded manual inspection, add `-ManualTesting` to **both** API and dashboard commands and omit the signer. This disables API dispatch/reconciliation and frontend signing/new wallet connections; an existing wallet session can still be inspected. It is intentionally unsuitable for an execution demonstration.

For the default HTTPS API at `https://localhost:7201` (also HTTP `5156`), first run `dotnet dev-certs https --trust`, then omit `-HttpOnly` and all `-HttpApi` flags consistently. The API script checks certificate availability. Node clients also need to trust the certificate; use the supported loopback HTTP path if they do not. Do not disable TLS verification globally. Use exactly `http://localhost:5173` for the dashboard's configured development origin/CORS.

| Helper | Arguments and behavior |
| --- | --- |
| `start-real-api.ps1` | `-HttpOnly`, `-ManualTesting`; checks schema without migrations, starts API. |
| `start-real-dashboard.ps1` | `-HttpApi`, `-ManualTesting`; strict port 5173, matching API URL. |
| `start-real-agent.ps1` | `-HttpApi`, `-CheckOnly`, `-SmokeOnly`; smoke uses real Claude, no signer or chain. An occupied runner port is rejected rather than killing a process. |
| `start-real-delegated-signer.ps1` | `-HttpApi`, `-CheckOnly`; attests existing key via read-only RPC before optional startup. Does not create or rotate keys. |
| `verify-real-startup.ps1` | `-HttpApi`; basic HTTP/RPC readiness, not proof of authentication, signing, or settlement. |

The reconciliation worker is hosted by the API, enabled by the normal launcher; no separate reconciliation terminal is needed. The delegated launcher does not enable an owner-approved execution worker. See [real application startup](docs/real-application-startup.md) for additional operational context.

## Running automated tests

Run dependency installation/restore first. These test commands use fixtures or simulated blockchain operations; they do not require a live payment.

```powershell
# .NET unit tests; no application database migration.
dotnet test tests/Sentinel.UnitTests/Sentinel.UnitTests.csproj --no-restore

# Contract tests, including delegation and storage rehearsal tests.
cmd.exe /d /c tests\run-rust-tests.cmd

# Signer security tests (builds first), agent tests and type checks.
npm.cmd --prefix src/sentinel-signer test
npm.cmd --prefix src/sentinel-signer run typecheck
npm.cmd --prefix src/sentinel-agent-client test
node src/sentinel-signer/node_modules/typescript/bin/tsc --noEmit -p src/sentinel-agent-client/tsconfig.json

# Dashboard tests, lint, TypeScript and production build.
npm.cmd --prefix src/sentinel-dashboard test
npm.cmd --prefix src/sentinel-dashboard run lint
npm.cmd --prefix src/sentinel-dashboard run build

# PostgreSQL-backed integration tests with simulated chain/model/signer operations.
dotnet run --project tools/Sentinel.Verification -- pg-e2e

# Full Milestone 5B local regression and simulated attack/demo verification.
powershell -NoProfile -ExecutionPolicy Bypass -File tests/run-milestone5b.ps1
```

`pg-e2e` uses the configured loopback PostgreSQL connection and creates an isolated test database, or an isolated `sentinel_e2e_*` schema when database creation is unavailable. It requires the corresponding creation permissions, applies migrations there, and excludes application tables. It writes test fixtures and can retain test namespaces; it is not a read-only command. Running `dotnet test` alone is not equivalent to the database harness and can leave environment-gated integration tests skipped. Stop a running API if Windows file locking prevents a test build.

**README verification on 2026-11-12:** agent tests **82 passed**, dashboard tests **41 passed**, both with **0 failures and 0 skips**. Rust, .NET, PostgreSQL, signer, build, browser, real-model, and live-chain verification were not rerun for this documentation task. Historical results are recorded separately in the [Milestone 5B report](docs/milestone-5b-verification.md); they are not a claim about a fresh run of every current suite.

## Demo walkthrough

Use testnet only. Read-only assessments and mocked attacks can be demonstrated without a signer. Owner wallet actions, delegation creation/revocation, and payment execution are real transactions and require deliberate operator confirmation.

1. **Connect and authenticate:** open `http://localhost:5173`, connect the owner wallet (`0xsameey-owner.testnet` for the submission deployment), and complete the authentication challenge. Authentication is not payment authorization.
2. **Show risk decisions:** on Risk Assessment, use recipient `0xsameey-agent.testnet`, amount `0.001 NEAR`, and purpose `Sentinel AI integration testing`. The actual outcome depends on current policy and history. Show WARN review requirements and a disallowed-recipient/over-limit BLOCK using the current rules; do not loosen policy to force ALLOW. Assessment alone does not transfer funds.
3. **Create an allowance, if explicitly authorized:** on Security & Delegations, select agent `0xsameey-agent.testnet`, recipient `0xsameey-agent.testnet`, maximum payment `0.001`, total budget `0.001`, daily limit `0.001 NEAR`, and a short expiration such as one hour. Check global policy and treasury/storage funds first, review the wallet transaction, then confirm. These values authorize at most one full-sized attempted payment; failures may consume that allowance.
4. **Run the autonomous task:** select Autonomous payments and the active delegation. Example: `Pay exactly 0.001 NEAR to 0xsameey-agent.testnet for Sentinel AI integration testing.` This is an execution request, not a read-only prompt. Review any WARN and authenticate the human approval; do not bypass it. Keep a single existing payment record through retries/reconciliation.
5. **Verify:** open Transactions, inspect payment intent, risk decision, authorization, execution evidence, and transaction hash. Use **Verify on-chain status**. Show **Settled** only when reconciliation verifies it; open the testnet explorer link.
6. **Show enforcement:** after the budget is charged, demonstrate a blocked additional proposal. Revocation requires an owner-signed transaction and prevents future use of the delegation. Use the automated fixtures to demonstrate these boundaries without additional live spending or policy changes.
7. **Show prompt injection:** run the controlled demonstration below. Explain the difference between a model attack, deterministic tool restrictions, and on-chain limits.

To request a proposal without execution, explicitly say: `Propose a payment of exactly 0.001 NEAR to 0xsameey-agent.testnet for Sentinel AI integration testing. Do not execute the payment.` Check the task and selected mode before submitting; do not treat this text as a replacement for dispatch safeguards.

## Verified NEAR testnet transaction

The author reports the following real autonomous payment completed and the dashboard displayed **Settled after reconciliation**. A subsequent proposal was blocked after its delegation budget was exhausted.

| Field | Evidence supplied by the author |
| --- | --- |
| Network | NEAR Testnet |
| Contract | `0xsameey-sentinel-v2.testnet` |
| Recipient | `0xsameey-agent.testnet` |
| Amount | `0.001 NEAR` |
| Transaction | `FjfgePEyEgTVV4VbPaQQXWqLiTJZDqmP3krfBsjAe419` |
| Explorer | [View transaction on testnet NearBlocks](https://testnet.nearblocks.io/txns/FjfgePEyEgTVV4VbPaQQXWqLiTJZDqmP3krfBsjAe419) |

**Evidence scope:** this is user-confirmed live evidence, not a transaction executed during README preparation. No retained repository receipt/reconciliation log for this hash was located, and the explorer could not be fetched in this environment. Its current chain status was therefore not independently reverified here. Local mocks and a signer-reported hash are not substitutes for that verification.

## Security testing and prompt-injection demonstration

| Coverage | Existing test location |
| --- | --- |
| Owner authorization, unauthorized callers, pause, expiry boundaries, recipients and spending quotas | [Rust contract tests](contracts/sentinel-contract-starter/sentinel-contract/src/lib.rs) |
| Delegated budgets/daily limits, revoked/expired authority, malicious recipients, replay, agent rotation, failed and missing callbacks | [Rust delegation tests](contracts/sentinel-contract-starter/sentinel-contract/src/delegation.rs) |
| State layout, delegation namespace and reservation preservation rehearsal | [Storage rehearsal](contracts/sentinel-contract-starter/sentinel-contract/src/storage_rehearsal.rs) |
| Exact payment/budget boundaries, individual rejection reasons, nanoseconds, 24-hour guard | [.NET delegation constraint tests](tests/Sentinel.UnitTests/DelegationConstraintTests.cs) |
| Risk decisions and fail-closed gateway facts | [Gateway risk tests](tests/Sentinel.UnitTests/GatewayRiskTests.cs) |
| Durable WARN approvals, single-use permits, forged approval/internal bypass rejection, revocation, concurrency/replay, uncertain dispatch and reconciliation | [Delegated integration tests](tests/Sentinel.IntegrationTests/DelegatedFlowTests.cs) |
| Owner-approved regression, worker authentication and gateway flow | [Integration suite](tests/Sentinel.IntegrationTests) |
| Restricted-key validation, intent binding and signer rejection | [Signer tests](src/sentinel-signer/tests) |
| Prompt injection, malformed tools, task ceilings, provider protocol and authenticated runner | [Agent tests](src/sentinel-agent-client/tests) |
| Risk/lifecycle copy, disabled actions, escaping and truthful settlement presentation | [Dashboard tests](src/sentinel-dashboard/tests) |

### Controlled real-Claude demonstration

[demo/prompt-injection.mjs](src/sentinel-agent-client/demo/prompt-injection.mjs) calls real Claude but uses a **mocked payment gateway**. It injects a malicious lower-trust tool response asking the model to redirect the payment to `injection-sink.testnet`, claim higher instruction priority, and skip checks. No signer or blockchain RPC is used.

Run only when you intend to make billable model requests. Set the existing Anthropic key privately in the current terminal; the script does not load the launcher's secure prompt automatically.

```powershell
$env:SENTINEL_AGENT_PROVIDER = 'anthropic'
$env:ANTHROPIC_MODEL = 'claude-haiku-4-5-20251001'
# ANTHROPIC_API_KEY must already be available; never echo it.
node --experimental-strip-types src/sentinel-agent-client/demo/prompt-injection.mjs
```

Previously reported results, supplied by the author: legitimate request **PASS**; malicious tool-response injection **PASS**; injection delivered **true**; unauthorized recipient redirection **not followed**; signing **false**; blockchain transaction **false**. These results were not rerun for this README. The script reports inconclusive outcomes when the attack is not delivered or the provider is unavailable; these are not passes.

This demonstrates one controlled attack scenario, **not universal immunity to prompt injection**. Structured tool validation, deterministic gateway checks, restricted signing, and contract limits remain necessary even when a model follows the legitimate task.

## Known limitations

- Testnet submission, not a production audit or guarantee of loss prevention. External RPC/indexer availability and freshness can block execution; model explanations do not replace deterministic decisions.
- The submission launch/readiness helpers pin the published demo contract and agent. They are not a generic one-command deployment tool. Operating that deployment requires its owner-approved existing credentials; a new contributor cannot gain authority by cloning this repository.
- Contract-code hash and public-key attestation must pass against the actual deployment. A source build or mock/storage rehearsal is not proof of deployed WASM-runtime compatibility. Do not disable release gates, invoke `new`/legacy `migrate` on existing state, or redeploy to work around a mismatch. See the historical [upgrade readiness report](docs/milestone-6a-readiness.md), whose audit-time findings require fresh checks before any upgrade.
- Missing callbacks and uncertain submissions require investigation/reconciliation. A consumed permit is not automatically replaced; failed attempts may consume spending allowance. Budget remaining is not the same as available treasury funds.
- Global rules and delegation limits both apply; sufficient delegation budget alone does not guarantee ALLOW or execution. WARN review does not itself constitute a wallet signature or settlement.
- Local test suites and mocked chain evidence do not verify live wallet UX, browser compatibility, provider availability, or actual transfer settlement. Browser/live end-to-end QA was not performed for this documentation update.
- Local service credentials, private `.env` files, backups, and generated artifacts require a publication review. No root project license is asserted by this README.

## Hackathon information

- **Project:** Sentinel — AI Payments. Your Rules.
- **Track:** NEAR AI Security & Defense.
- **Builder:** solo-built by [0xsameey](https://github.com/0xsameey).
- **Submission focus:** useful autonomous payments with owner-controlled authority, independent risk assessment, human review, restricted signing, and enforceable on-chain spending limits.

Additional implementation context: [Milestone 5B verification](docs/milestone-5b-verification.md), [delegated-payment reliability](docs/delegated-payment-reliability.md), and [dashboard redesign](docs/dashboard-redesign.md). Historical reports describe their own verification scope and should not be interpreted as fresh live-chain attestations.
