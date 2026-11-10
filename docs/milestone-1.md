# Milestone 1: payment integration

This milestone keeps owner authorization mandatory. It adds no LLM, autonomous
spending method, key provisioning, or contract-state migration. Contract edits
are limited to test timestamps; production contract checks are unchanged.

## Stable wire contract

| Endpoint | Response / request |
| --- | --- |
| `POST /api/v1/payments/prepare` | Returns `{ paymentId, transaction: ContractCall }`; requires owner JWT and GUID `Idempotency-Key` |
| `POST /api/internal/agent/payments/{id}/prepare-execution` | Returns a **direct** `ContractCall`, with receiver, `execute_payment`, only `{ id }`, 30 Tgas, zero deposit |
| `POST /api/v1/payments/{id}/transactions` | Owner reports `{ transactionHash, operation: "Authorization" }` |
| `POST /api/internal/agent/payments/{id}/report-transaction` | Worker reports `{ transactionHash, operation: "Execution" }` |
| Payment reads | `expiresAtNs` is a decimal string, preserving nanosecond precision |

Worker endpoints require `X-Sentinel-Worker-Key`. Worker and dispatcher keys are
separate. Invalid or changed execution calls must fail before submission. A
reporting failure after a successful submission returns `202 ReportingPending`
with the existing hash; it must not trigger automatic resubmission.

## Signer configuration

CLI and server share these variable names:

- `NEAR_NETWORK=testnet`
- `SENTINEL_API_URL` (HTTPS)
- `SENTINEL_WORKER_KEY` (same Base64 value as API `AgentWorker:ApiKey`, at least 32 decoded bytes)
- `NEAR_RPC_URL` (trusted testnet HTTPS endpoint)
- `NEAR_CONTRACT_ID` and `NEAR_AGENT_ACCOUNT_ID` (named `.testnet` accounts)
- `NEAR_AGENT_PRIVATE_KEY` (signer process only)
- Server only: `SENTINEL_DISPATCH_KEY`, `SENTINEL_DISPATCH_PORT` (default 4301)

The API's `AgentDispatcher:Key` must match `SENTINEL_DISPATCH_KEY`, and its
`AgentDispatcher:Url` must reach the signer's loopback listener. Build with
`npm.cmd --prefix src/sentinel-signer run build`, then use `npm.cmd --prefix
src/sentinel-signer start` or `npm.cmd --prefix src/sentinel-signer run execute
-- PAYMENT_ID`. The signer still assumes its provisioned key is restricted;
on-chain permission attestation and provisioning remain an explicit next gate.

## Reproduce tests

Prerequisites: .NET 10 SDK, Node 22+, installed npm dependencies, and restored
NuGet packages. No real keys are required. The auth client's TypeScript build
targets the CLI entry point; its unused legacy wallet-selector helper is excluded.

```powershell
npm.cmd --prefix src/sentinel-signer test
dotnet test Sentinel.slnx --no-restore
npm.cmd --prefix src/sentinel-dashboard run build
npm.cmd --prefix src/sentinel-auth-client run build
```

The normal .NET run executes risk tests, worker-key tests, and a real
TypeScript-to-ASP.NET TCP integration test. That integration test substitutes
chain state and persistence; it does not prove transaction settlement. Build
the signer first so its compiled test host is available.

For the database end-to-end scenario, install PostgreSQL (`initdb`, `pg_ctl`
on PATH), then run:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File tests/run-milestone1.ps1
# On machines where Postman local storage is accessible:
powershell.exe -NoProfile -ExecutionPolicy Bypass -File tests/run-milestone1.ps1 -UsePostman
```

ExecutionPolicy applies only to this process; no machine policy is changed.
The script initializes a unique PostgreSQL cluster under `.test-artifacts`,
binds it to loopback on a free port, applies migrations there, and generates
ephemeral JWT/worker credentials. It does not use appsettings database credentials
or any existing database. Processes it starts are stopped in `finally`; logs and
test data remain for diagnosis. Local trust authentication is confined to this
temporary loopback cluster. The script fails if provisioning fails; it does not
silently substitute an in-memory database.

The database test exercises actual API middleware/controllers, PostgreSQL,
risk/intent persistence, idempotency, owner permissions, synthetic owner
authorization verification, real signer dispatch/executor code, execution
reporting and reconciliation. It checks blocked/review decisions, forged reports,
pause, wrong worker credentials, duplicate dispatch and saved attempts/audit rows.
Wallet signing and the NEAR submit boundary are synthetic; RPC responses use
`postman/mocks/near/default.js`, not a NEAR VM. No production switch enables the
test submission adapter. The normal .NET run explicitly skips this database test
unless the runner provisions its environment.

Postman owner-mock discovery was skipped because the CLI is signed out. The
fixture models the repository's consumed RPC shapes. Unknown operations return
501. The Node fallback executes the same handler with in-memory `pm.state`; it
does not establish that Postman simulation itself ran successfully.

## Verification and remaining gates

Recorded on 2026-11-03:

- Signer tests pass, covering response shape, call tampering, preparation errors,
  timeout, failed execution, reporting failure and concurrent dispatch.
- Risk tests demonstrate that purpose text and AI explanation cannot change
  deterministic decisions. This is not a general prompt-injection certification.
- Dashboard and auth-client builds pass. Browser wallet interaction is unverified.
- PostgreSQL E2E provisioning is blocked in this session: `initdb` cannot create
  a restricted token (Windows error 87); Docker's engine is unavailable. The
  scenario compiles but has not passed against PostgreSQL here.
- Postman simulation startup is blocked by permission to its local dataset
  directory. Local Node fixture health/config/call recording were checked.
- `cargo test --offline` is blocked by the Windows native dependency toolchain:
  NASM is missing and native compiler feature probes fail. Contract tests have
  not executed. Repair the toolchain or use a supported Linux CI environment.
- No live testnet transfer or restricted-key permission check was performed.

Before autonomy: pass PostgreSQL E2E, Rust/NEAR sandbox tests, then verify a
restricted execution key and one small live testnet payment. Remaining design
work includes recipient intelligence, remaining-budget assessment, durable
submission/report recovery, and defining whether all executions require an
approved off-chain risk record. The current contract remains owner-authorized.
Limited autonomy will require a separate reviewed delegation design with
on-chain budgets, recipient/method constraints, expiry, revocation, replay
protection, and atomic accounting; simply allowing the agent to call the current
owner-only authorization method would remove a security boundary.

## Milestone 1B verification update — 2026-11-03

This section supersedes the earlier local PostgreSQL/Rust blockers. No live
transaction, deployment, key rotation, production setting change, or destructive
operation was performed. No autonomous AI functionality was added.

### PostgreSQL: existing service verified; isolated integration passes

`postgresql-x64-18` is running on `127.0.0.1:5432`. A read-only connection using
the existing development user-secrets configuration succeeded:

| Item | Verified value |
| --- | --- |
| Server | PostgreSQL 18.2 |
| Development database / role | `sentinel_db` / `sentinel_user` |
| Role can create databases | No |
| Role can create schemas in the development database | Yes |

Error 87 arose in PostgreSQL's `CreateRestrictedToken` path before cluster
initialization. Windows defines 87 as `ERROR_INVALID_PARAMETER`. The exact
Windows token incompatibility was not isolated; a restricted automation-token
interaction is an inference. It is not evidence of a corrupt database or a bad
database password. The already-running service avoids this initialization path.
No token restriction, service configuration, password or database ACL was changed.
References: [PostgreSQL restricted-token implementation](https://github.com/postgres/postgres/blob/REL_18_STABLE/src/common/restricted_token.c),
[Windows error codes](https://learn.microsoft.com/en-us/windows/win32/debug/system-error-codes--0-499-).

The new verification helper reads credentials in memory and never prints a
connection string. It prefers a newly created database when the role has that
permission; otherwise it creates a unique `sentinel_e2e_...` schema. The test
connection's search path contains only that schema, with **no public fallback**.
The test asserts the effective schema and verifies it is empty before migrating.
EF migration history is also explicitly placed in that schema. Existing
application tables are not migrated or reset. Test namespaces are retained;
there is no drop or cleanup command.

```powershell
# From repository root; no credentials appear in command arguments.
dotnet run --no-restore --project tools/Sentinel.Verification -- pg-inspect
dotnet run --no-restore --project tools/Sentinel.Verification -- pg-e2e
```

If restoring the new helper cannot reach NuGet, the installed local cache was
sufficient in this session:

```powershell
dotnet restore tools/Sentinel.Verification --source "$env:USERPROFILE/.nuget/packages" --ignore-failed-sources
```

The final passing run used schema
`sentinel_e2e_20261103092157_c5ed2079`. Read-only aggregate evidence afterward:

| Persisted evidence | Result |
| --- | --- |
| Payment requests | 2, both `Completed` |
| Transaction attempts | 4, all `Verified` |
| Audit events | 12 |
| Applied migrations | 5 |

Four isolated test schemas were retained across diagnostic runs, including the
initial failed fixture run. No existing schema or database was dropped/reset.

```powershell
dotnet run --no-restore --project tools/Sentinel.Verification -- pg-evidence sentinel_e2e_20261103092157_c5ed2079
```

The full run passed **8 .NET unit tests, 6 .NET integration tests (none skipped),
and 12 signer tests**, plus dashboard/auth-client builds. The database flow
includes real API routing/authentication, actual PostgreSQL migrations and
persistence, owner preparation, policy rejection, transaction verification,
agent dispatch, reconciliation, and a lost-report recovery scenario. The NEAR
RPC and submission boundary remain synthetic: this is not live settlement.

The first database run caught a fixture bug: `PostAsJsonAsync`'s web defaults
changed `FunctionCall`/`SuccessValue` to camel case, violating the real NEAR
response shape. Fixture seeding now preserves exact names. The production
transaction verifier was not weakened.

Recovery evidence: the second synthetic payment completed without its execution
report; a fresh request/scope synced `Completed` from chain state, dispatch was
rejected afterward, and reporting the existing transaction hash twice produced
only one verified attempt. This proves manual sync and idempotent reporting
under the fixture. A whole-process crash/restart and automatic 30-second worker
recovery against live NEAR have not been demonstrated.

### Rust: reliable Windows solution verified

All **10 Rust unit tests pass**, and a fresh release WASM build succeeds offline.
The existing Visual Studio 18 Build Tools installation and Windows SDK
10.0.26100.0 were sufficient. The native dependency chain is
`near-sdk 5.29.1 -> near-crypto 0.37.4 -> aws-lc-rs 1.16.2 -> aws-lc-sys 0.39.1`.
The earlier shell lacked the Visual Studio developer environment and NASM.

The smallest working solution is `tests/run-rust-tests.cmd`: it discovers the
installed C++ tools with `vswhere`, calls `VsDevCmd.bat` for x64, and sets
`AWS_LC_SYS_PREBUILT_NASM=1` inside `setlocal`. AWS-LC documents prebuilt assembly
objects for non-FIPS x64 Windows when NASM is absent. No installation or
persistent environment change is needed. CMake/Go/Ninja are not required for
this non-FIPS path. See [AWS-LC Windows requirements](https://aws.github.io/aws-lc-rs/requirements/windows.html).

```powershell
cmd.exe /d /c tests\run-rust-tests.cmd
& "$env:USERPROFILE/.cargo/bin/cargo.exe" build --target wasm32-unknown-unknown --release --offline --locked --manifest-path contracts/sentinel-contract-starter/sentinel-contract/Cargo.toml
```

If the installed C++ workload is missing on another machine, install the official
Visual Studio C++ build tools plus a Windows SDK, then use the same script;
installing NASM is an alternative to the documented prebuilt-object setting.
WSL2 is unnecessary for the passing Windows path. `wsl --status` itself was
denied in this session, so WSL availability is unverified. Rust warnings about
unused test promises remain; they do not prevent the passing tests. No contract
production code or state layout changed.

### Contract and agent key: local evidence; live checks blocked

| Item | Local evidence |
| --- | --- |
| Configured contract | `0xsameey-sentinel-v2.testnet` |
| Configured agent | `0xsameey-agent.testnet` |
| Canonical signer's derived **public** key | `ed25519:6a1kBiLSJpgViwFC8ZKZyW4N4cev9DbDXTy5Uz6k6jZA` |
| Legacy/canonical local key values | Equal; values not printed |
| Fresh local WASM SHA-256 | `aecc58e5d781025311662c861cdc24b31a693530cd98eaaa1363f727aa9ecb3a` |
| Fresh local WASM NEAR-style code hash | `CmLfabonVGpJ7BYaR8nA3XFKFjsiTUrNqgzvVcEQ2fwo` |

The fresh local WASM exports all 16 expected source methods, including
`authorize_payment`, `execute_payment`, `get_policy`, `get_daily_usage`,
`set_recipient_allowed`, pause/cancel controls, and `on_payment_complete`.
This verifies the local build surface only. Export presence alone does not
establish runtime behavior, access restrictions, or deployed source identity.

`tools/verify-testnet.mjs` is a read-only verifier. It derives the signer's public
key without signing, checks `status.chain_id`, pins all queries to one finalized
block, fetches deployed WASM/exports, compares bytes to the fresh local build,
reads policy/configuration/budget, and looks up the specific configured key's
receiver, method restrictions and allowance. Its only RPC methods are
`status`, `block`, `gas_price`, and `query`; view calls are explicitly whitelisted. It has no
transaction, deployment or key-management operation. Reports contain only
public metadata and hashes under `.test-artifacts/milestone1b/`.
The selected recipient is checked with `view_account` and
`is_recipient_allowed`; budget checks include consumed authorization quota and
completed plus in-flight execution usage. The specific signer's permission is
queried directly, independently of access-key-list pagination. Gas figures are
estimates at the observed block price and must be refreshed before approval.
The policy preview does not replace the API's fresh risk assessment.

```powershell
node tools/verify-testnet.mjs --local-only
# Run when testnet network access is available; remains read-only:
node tools/verify-testnet.mjs
node --test tests/verification-checks.test.mjs
```

All 12 pure preflight-check tests pass: they cover full-access/wrong-receiver/
extra-method rejection, unlimited or insufficient gas allowance, remaining daily
usage, review thresholds, recipient approval, treasury coverage and malformed
amounts. These are fixture-based checks, not evidence of live key permissions.

Exit 1 indicates a blocked/failed check; exit 2 indicates live checks completed
but the approval gate failed. Byte differences can be caused by optimization or
build provenance, so a mismatch requires investigation, not automatic redeploy.
The current compiler's raw release artifact is used; the older `target/near`
artifact is not accepted as proof of current-source identity.
RPC shape reference: [NEAR account/contract RPC](https://docs.near.org/api/rpc/contracts).

Exact remaining live blockers:

1. Direct HTTPS testnet RPC calls fail with `EACCES` / Windows socket error
   10013 in this execution environment. Public explorer/indexer retrieval through
   the web tool also failed. No finalized block or deployed code hash was read.
2. Dedicated browser access reported its host unavailable. Native browser
   automation then stopped because it could not determine the current browser
   URL confidently enough to enforce policy. Browser interaction ended; no
   wallet connection or approval was attempted.
3. Therefore the deployed contract/source match, actual method surface, current
   owner/agent/pause/policy/budget, and agent access-key receiver/method/allowance
   are **unverified**. A local `.env` value or public-key derivation cannot prove
   an on-chain restriction. Do not run the live signer before these checks pass.
4. The configured local frontend/API/signer ports (5173/7201/4301) had no
   listeners during preflight. No production-configured service was started;
   doing so would also start reconciliation against its configured database.
5. The user selected `0xsameey-agent.testnet` as recipient. Its account existence,
   allowlist status, spending eligibility, treasury coverage, and signing-key
   restrictions still need live verification. Transaction approval has not been
   requested/received; preparation is gated on those read-only checks.

### Controlled browser-to-blockchain trial: prepared, not submitted

The unsigned plan is in `docs/milestone-1b-payment-plan.json`. Proposed transfer:
**0.001 testnet NEAR** (`1000000000000000000000` yoctoNEAR), single payment,
15-minute authorization lifetime, no recurring authorization. The user selected
`0xsameey-agent.testnet`; account existence and allowlist eligibility remain
unverified. No transaction has been prepared. No allowlist, policy or key change
is part of this plan. Because the recipient is also the execution signer, its
observed net balance increase can be smaller than 0.001 NEAR after gas; reconcile
the transfer receipt and gas accounting rather than expecting an exact net delta.

Before requesting transaction approval, complete the read-only verifier, confirm
the recipient is allowlisted, require an `ALLOW` assessment for this exact amount
and purpose, verify the treasury's available balance and remaining daily limits,
and estimate owner/agent network fees. The proposed combined fee ceiling is
0.01 testnet NEAR; stop if the verified estimate exceeds it. Both calls use
zero attached deposit and 30 Tgas. The 0.001 NEAR transfer comes from the treasury,
not an attached deposit. Verify owner-wallet signing and the worker's restricted
key independently. Do not approve a full-access worker key or change key rights
as part of the trial.

After those gates, stage the dashboard assessment and prepared payment, capture
the immutable payment ID/recipient/amount/expiry, and request explicit approval
for those exact details **before owner authorization or agent dispatch**. Only
then would the owner sign `authorize_payment`, report the authorization hash,
confirm `Authorized` in the contract and database, and dispatch the agent's
`execute_payment` call. Record both hashes and inspect final receipts, contract
`Completed`, recipient balance change, database `Completed`, two verified
attempts and corresponding audit events. Gas spending must be distinguished
from transfer value. Do not treat transaction submission as settlement.

For reporting loss after a confirmed transfer: retain the existing hash, do not
sign again, call the owner's sync endpoint, and let the signer/operator report
that same hash through the worker-authenticated execution-report endpoint.
Owner JWTs cannot report agent execution. Repeat the report to check idempotency;
the attempt count must remain unchanged and subsequent dispatch must be rejected.
For an ambiguous RPC timeout, first inspect the original transaction and contract
state; do not execute again just because the UI timed out. For a confirmed failed
transfer, preserve the failed payment ID and audit trail; do not recycle the ID.

No live fault injection or service shutdown is proposed during this one small
trial. Lost-report behavior is already exercised locally against PostgreSQL;
live recovery evidence can first be collected with read-only rechecks and
duplicate reporting of an existing verified hash. A more disruptive live
crash/restart experiment requires its own reviewed plan.

## Saved live snapshot review and next step

The user's read-only run completed at `2026-11-03T09:31:58.072Z`, finalized
testnet block **276030703** (`5ULn6eA6dkxfy4TMoXREo2ahoUbRcRjENrjTYDHmi93x`).
This replaces the earlier network-blocked evidence for that snapshot only; fresh
checks will still be required before transaction approval.

Verified in the saved report: all expected deployed exports are present; owner
is `0xsameey-owner.testnet`, configured agent/recipient is
`0xsameey-agent.testnet`, and the contract is unpaused. The recipient exists and
is allowlisted. Maximum payment is **0.1 NEAR**, daily limit **0.5 NEAR**, and
reported daily usage is zero. Available treasury is **7.9384796987477071 NEAR**.
The proposed 0.001 NEAR payment passes the policy preview. Combined 60 Tgas at
the observed gas price is **0.006 NEAR**, within the proposed 0.01 fee ceiling;
this estimate must be refreshed and is not a guaranteed final fee.

Two blockers remain:

1. **Signer permission evidence is incomplete.** The saved report has
   `signerPermission: null`; this cannot establish receiver/method restrictions
   or a usable allowance. The account has two keys and one full-access key, but
   this does not identify which permission belongs to the configured signer.
   Do not infer that its key is full-access, or rotate it based on this report.
   The old `signerKeyPresent` field merely tested for a non-null RPC result.
2. **Current-source build provenance remains unverified.** Downloaded deployed
   WASM has code hash `HNBoc7jcQkCUfRib2veoAZw4R12RcN9y4vzKjZ3j7C99` and
   SHA-256 `f32867464baa1f9e74a7d7506e474e3dc0ab07f1424bc646cb132dd154782834`.
   It is byte-identical to the existing 168,322-byte `target/near` artifact.
   Current source and lockfile hashes match the snapshot, but the fresh raw
   Cargo release differs. Differences extend beyond custom/debug sections;
   optimization/build flags may explain them, but that has not been proven.
   Existing artifact equality is not proof it was built from current source.

Prepared the next **read-only** step:

```powershell
node tools/verify-testnet.mjs --diagnostics
```

This saves `testnet-diagnostics.json` and `diagnostic-deployed.wasm` separately,
preserving the original snapshot. It captures the public direct access-key RPC
result and matching key-list entry, cross-checks permissions, and refuses to
pass missing or conflicting evidence. It does not sign, submit, deploy, rotate
keys, or change policy. The format is based on the
[NEAR access-key RPC contract](https://docs.near.org/api/rpc/contracts#view-access-key).
All **16 preflight-check tests** pass, including new missing/conflicting/wrong-key
cases; the verifier also passes Node's syntax check.

Offline artifact comparison is repeatable with:

```powershell
node tools/compare-contract-artifacts.mjs
```

Its report is `.test-artifacts/milestone1b/artifact-comparison.json`. The old
artifact was also preserved as `prior-target-near.wasm`. A same-source optimized
local build was attempted with cargo-near `--no-abi --locked` and a separate
output directory. It stopped before compilation because cargo-near attempted
to initialize/migrate its local near-cli configuration and file access was
denied. No deployment or configuration change was performed. Reproducing the
optimized build in an appropriate writable build environment remains the next
provenance check; do not redeploy simply to eliminate the hash mismatch.

The payment plan now contains the verified snapshot values and these blockers.
Its payment ID and expiry remain unset. No actual payment transaction has been
prepared, signed, or submitted.

## Diagnostic resolution: missing key and a security-relevant source difference

The saved diagnostic completed at `2026-11-03T09:48:00.502Z`, finalized testnet
block **276032369** (`E77g4xWpd2ksUY9yoGUFJfVGUGaMD2ZAxFz9mhxunV6V`). This
section supersedes the earlier statements that the permission response and
WASM mismatch were unexplained.

### Configured signing key is missing

The direct query's public result contains:

```text
access key ed25519:6a1kBiLSJpgViwFC8ZKZyW4N4cev9DbDXTy5Uz6k6jZA does not exist while viewing
```

The key list is complete (two entries, no pagination continuation), and has no
matching entry. Therefore this configured key is **absent** at the diagnostic
block. It is not a confirmed FunctionCall key, and it has no demonstrated gas
allowance. The response carries an error inside `result`, explaining the prior
null permission. The verifier now explicitly marks such a query as failed and
cannot confirm permissions even if an errored response also includes fields.
Diagnostics additionally retain the public key-list result for future reviews.
All **18 preflight-check tests** pass, including these error cases.

One full-access key exists on the account, but it is not the configured missing
key. The other key's public permissions were not retained in this saved report;
its existence alone does not prove it is suitable for the signer.

Safest correction proposal, **not performed**:

1. First inspect the existing restricted key. If its scope is exactly the target
   contract and `execute_payment`, its allowance is adequate, and its private key
   is already held securely, propose using it in the isolated signer. Updating
   signer configuration still requires approval; no files were changed here.
2. If there is no suitable securely held key, have the **agent account's human
   administrator** add a fresh dedicated FunctionCall key after explicit approval.
   Proposed receiver: `0xsameey-sentinel-v2.testnet`; methods:
   `["execute_payment"]`; finite example allowance **0.005 NEAR**
   (`5000000000000000000000` yoctoNEAR). At the diagnostic price, 30 Tgas costs
   0.003 NEAR, so this example covers the proposed execution gas allocation.
   Refresh the estimate before approving the value.
3. Preserve existing access/recovery keys. Do not use a full-access key in the
   worker or blindly re-add the missing key: its removal history is unknown.
   Keep new private material confined to the isolated signer, outside version
   control and model access. No key generation or configuration replacement has
   occurred. An allowance limits gas spending; it neither funds the agent nor
   replaces the contract's treasury spending rules.

The approval-free proposal is recorded in `docs/milestone-1b-corrections.json`.
It is not a serialized/signed on-chain transaction.

### Exact differential reproduction resolves the WASM mismatch

The build investigation was completed **without changing the actual contract
source**, near-cli configuration, or deployed contract:

- Current source SHA-256 remains
  `091edcefc0f45acf612b40ebd18de37d2d379d30aa7e044af8b2466c45527538`.
- Current and differential builds use the same contract Cargo.lock, Rust
  **1.99.1**, release profile, wasm32 target, and compiler flags
  `-C link-arg=-s --cfg near`, matching installed cargo-near-build 0.11.5's
  default WASM compiler flags.
- A tiny local-only helper uses the **same wasm-opt 0.116.1** optimizer as
  cargo-near 0.22.0: `new_optimize_for_size`, with `TruncSat` and `BulkMemory`
  enabled. Debug-info parsing is disabled for the already-stripped input.
  It has no RPC, wallet, near-cli config or deployment functionality and refuses
  to overwrite an existing output. This avoids cargo-near's blocked CLI config
  initialization while reproducing its artifact-processing step.
- A scratch copy under `.test-artifacts/milestone1b/expiry-differential` differs
  from real source only by removing the constant and `require!` implementing the
  **24-hour authorization expiry ceiling**. This copy and its artifact are
  comparison-only, explicitly marked **never deploy**. No actual source file or
  security check was removed from the project contract.

| Artifact | Bytes | SHA-256 | Matches deployment |
| --- | ---: | --- | --- |
| Downloaded deployed WASM | 168322 | `f32867464baa1f9e74a7d7506e474e3dc0ab07f1424bc646cb132dd154782834` | Yes |
| Optimized current source | 168397 | `34d9ad95449b50a9ad4005d14a950b9d79b2b677ac5588aefc8640f61d15784f` | No |
| Optimized comparison copy without 24-hour guard | 168322 | `f32867464baa1f9e74a7d7506e474e3dc0ab07f1424bc646cb132dd154782834` | **Byte-identical** |

**Conclusion: this is a security-relevant code difference, not merely stripping
or optimization.** The deployed binary exactly reproduces current production
code with that authorization-lifetime guard removed. Its remaining compiled
guards are those in the same source copy: owner authorization, recipient approval,
transaction/daily limits, execution expiry, pause, IDs and callback accounting.
This is a differential proof of the missing cap, not a general contract audit.
An owner can authorize an expiry beyond 24 hours on this deployed version; the
current source rejects it. The proposed 15-minute payment itself fits both
versions, but that does not restore the advertised deployed safeguard.

Reproducibility remains weaker than a release-grade build: the contract has a
lockfile but no pinned rust-toolchain file, reproducible-build metadata/config or
Git-backed reproducible build context. Local execution of only the public
`contract_source_metadata` export, in a WASM sandbox with other host calls
trapped, returned `build_info: null` and `link: null` for both artifacts. No
blockchain method was invoked. Future release work should pin the full toolchain
and record source/build provenance; no such repository/config migration was
made during this investigation.

Evidence files under `.test-artifacts/milestone1b/`:

- `artifact-comparison.json`: validates unchanged current-source/lock hashes,
  the exact guard-only source delta, matching lockfiles and matching deployed bytes.
- `current-source-cfg-near.wasm` and `current-source-optimized.wasm`: preserved
  fresh current-source builds.
- `expiry-differential-change.txt`, scratch source and
  `expiry-differential-optimized.wasm`: comparison-only proof.
- `wasm-metadata.json`: sandbox-read metadata; no keys or live state.

Repeat the offline artifact assertions with
`node tools/compare-contract-artifacts.mjs`. Build the pinned local optimizer
with `cmd.exe /d /c tests\build-artifact-optimizer.cmd`; no machine settings are
changed. It uses `setlocal` for the installed Visual Studio environment.

### Payment prerequisites: not all satisfied

| Prerequisite | Finding at diagnostic snapshot |
| --- | --- |
| Testnet, recipient exists and is allowlisted | Pass |
| Configured agent, unpaused contract | Pass |
| 0.001 NEAR within 0.1 per-payment / 0.5 daily limits | Pass; usage zero |
| Treasury coverage | Pass; 7.9384796987477071 NEAR available |
| Observed combined gas allocation within 0.01 ceiling | Pass estimate: 0.006 NEAR |
| Configured restricted signing key and sufficient allowance | **Fail: key absent** |
| Deployment implements all current-source safeguards | **Fail: 24-hour authorization cap absent** |
| Owner/agent gas-paying balances, fresh API assessment and browser session | Still require pre-transaction checks |
| Explicit transaction/key/deployment approval | Not received |

Recommended next work is a reviewed key correction and reviewed contract upgrade
plan, each with separate explicit approval before external changes. The reproduced
contract delta has **no state field/layout change**; do not invoke the legacy
`migrate` method automatically. Validate deployed state and upgrade authority,
pause/in-flight handling and post-upgrade hashes before proposing deployment.
No upgrade, key change, signing, payment preparation or submission was performed.

### Milestone 2 continuation

The local deterministic Security Gateway and its tests are documented in
[Milestone 2](milestone-2.md). The missing restricted key and missing deployed
24-hour expiry guard remain mandatory release blockers and are now checked at
execution permit issuance and consumption. Local fixture results do not clear
either blocker. No live keys, contract deployment or transactions were changed.
