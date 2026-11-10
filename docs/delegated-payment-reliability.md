# Delegated payment reliability investigation

Investigated 2026-11-08. **Local fixes implemented; live execution remains
blocked by signer configuration/attestation.** No contract, policy, delegation,
signing credential or existing application record was modified. No transaction
was signed or submitted. Application database inspection used a read-only
transaction; integration tests use separate `sentinel_e2e_*` schemas, never
`public` application tables. Test schemas are retained, not destructively removed.

## Confirmed causes

### 1. Current payment expired; conversion is correct

Payment `acc8a2d6-be1a-4eb1-bb10-3b9126c9f42e`, delegation
`sentinel-live-test-004`:

| Stored field | Value |
|---|---|
| Created UTC | 2026-11-08 16:33:31.052273 |
| Intent expiry nanoseconds | 1794156506237000000 |
| Intent expiry UTC | 2026-11-08 16:48:26.237 |
| First inspection UTC | 2026-11-08 16:59:35 |
| Decision/status | ALLOW / Proposed |
| Permit issued / consumed / hash reported | No / no / no |

The value has the correct Unix nanosecond scale and approximately 15-minute
lifetime. `GatewayPaymentRequest.Validate()` previously used the same message
for `expiry <= now` and `expiry > now + 24 hours`. Preflight correctly rejected
an expired payment, but the error suggested only an upper-bound violation.
The exact time of the user's earlier click was not available; the stored intent
was demonstrably expired during this investigation. It cannot safely be revived.

Separate `PAYMENT_EXPIRED` and `PAYMENT_EXPIRY_EXCEEDS_24_HOURS` error codes now
preserve HTTP 400 and the original limits. Expired intents require a fresh
proposal, not timestamp modification or reassessment of the old intent.

### 2. Active delegation did not mean payment expiry was eligible

Stored BLOCK payments `651f726c-343a-40b9-b89d-55a03e2a1af6` and
`ba5ae6da-7000-4af5-b9a0-f04fa2f74a5a` belong to
`sentinel-live-test-002`. Their intent expiries were respectively
17:26:56.217 and 17:27:52.647 UTC on November 7. The stored delegation snapshot
expired at **17:18:50.603 UTC**. Thus
`intent.ExpiresAtNs > delegation.ExpiresAtNs` is true even while the delegation
was active and its limits/remaining budget looked sufficient.

This comparison uses persisted intents and the cached delegation observation;
it is not a historical finalized-RPC replay of the original assessment. The
old decision stored only the umbrella reason, so exact historical RPC facts
cannot be reconstructed completely. The snapshot comparison identifies the
expiry condition, while matching agent, recipient, limits, zero spent usage and
unrevoked state explain why the amounts displayed looked eligible.

Root integration gap: every agent proposal previously received `now + 15 min`
without considering delegation expiry. The delegated gateway now reads the
delegation state; the shared workflow sets expiry to the smaller of 15 minutes
and remaining delegation lifetime, using BigInt nanoseconds. The backend still
rejects a malicious proposal that outlives the delegation. No stored intent is
silently shortened or extended after assessment.

All original conditions remain. Specific reason codes supplement the existing
`DELEGATION_LIMIT_OR_AUTHORITY` code:

```text
DELEGATION_REVOKED
DELEGATION_AGENT_MISMATCH
DELEGATION_EXPIRED
PAYMENT_OUTLIVES_DELEGATION
DELEGATION_RECIPIENT_NOT_ALLOWED
DELEGATION_PAYMENT_LIMIT
DELEGATION_BUDGET_EXHAUSTED
DELEGATION_DAILY_LIMIT
```

Structured logs include payment ID, delegation ID and failed condition names;
they exclude credentials, private keys, task bodies and raw runner responses.

### 3. Proposal persisted before dispatch failure was discarded

The previous delegated runner called `runAgent()` (which persisted/verified the
assessment), then dispatched ALLOW and let a dispatch exception escape. The
runner returned an error; the API replaced it with `AGENT_RUN_UNAVAILABLE` even
though the proposal already existed. The observed database rows support the
partial-success symptom. They do not establish the particular downstream error
for each historical run; none of the inspected rows had a permit or hash.

Fixes:

- The runner retains the payment ID and returns `DispatchUnconfirmed` after
  downstream failure; it never automatically retries dispatch.
- A proposal created by a delegated task is now atomically bound to its run ID
  in existing audit storage. Binding validates owner, delegation, recipient and
  ceiling; the model cannot select another run's payment.
- API runner errors/timeouts check that binding and return the persisted payment
  with `proposalCreated:true`, `dispatchStatus`, and actionable status rather
  than falsely reporting creation failure. No association is guessed from time
  windows or recipient alone.
- `GET /api/v1/delegated-agent/tasks/{runId}` recovers a bound proposal for its
  authenticated owner. Reusing an existing run ID returns its bound result
  without invoking the model or signer again. Conflicting/replayed claims fail
  closed. The dashboard retains the run ID when retrying an unchanged task and
  refreshes proposals after action errors.
- Direct dispatch returns structured unconfirmed transport errors. A signer
  response remains submission evidence, never settlement proof.

Old runs without the new binding are not backfilled or reassigned. Inspect
their existing payment IDs through delegation inspection; do not recreate them
blindly. A disconnected request can still require later run lookup/reconciliation.

### 4. Dispatcher and signer need configuration correction before live use

Read-only inspection of saved API configuration and signer `.env` found:

- Dispatcher URL targets loopback port 4301 as expected.
- Dispatch credentials exist but do **not** match between those saved sources.
- Saved signer mode is not `delegated`.
- Existing signer key is present; API URL is HTTPS 7201.
- API delegated public-key pin is present, but does **not** match the public key
  derived from the configured signer private key.

Running terminal environment overrides cannot be proven from saved files alone.
No secret values were printed. No credentials were rotated, overwritten or
copied into the dashboard/model environment. The new signer launch script loads
the API's existing dispatch/worker credentials into the signer process, selects
delegated mode and port 4301, restores its environment on exit and performs
read-only attestation before starting. It does not change the private key or pin.

`-CheckOnly` confirmed the testnet account/contract/mode configuration and the
public-pin mismatch, then failed at RPC access in this execution environment.
Consequently **on-chain key permissions and live deployed artifact compatibility
are not newly verified here**. A full-access key has not been used or accepted.
The permission guard requires exactly one method, `execute_delegated_payment`,
the intended receiver and a finite sufficient allowance. Failure is blocking.
Use the already provisioned restricted credential that matches the reviewed API
pin; if no such credential exists, obtain separate approval for key provisioning
or pin changes. Do not simply replace the pin with a convenient full-access key.

## Execution and evidence invariants

| Stage | Evidence and retry rule |
|---|---|
| Proposed / assessed | Persisted intent and deterministic decision; no execution evidence |
| Permit issued | Single database claim; no automatic reissue, even after timeout |
| Attempted | Permit consumed atomically; not proof a transaction was submitted |
| Dispatch accepted | Worker response; still requires reconciliation |
| Hash reported | Unverified submission report, retained even for failed outcomes |
| Reserved | Matching finalized chain payment is Executing |
| Settled | Matching finalized chain payment is Completed |
| Failed | Matching finalized chain payment is Failed |

Preflight now rejects already-issued/consumed/reported execution claims before
redispatch. Permit expiry cannot exceed payment expiry. The signer validates
the payment's 24-hour bound and remaining delegation lifetime, rechecks permit
expiry just before submission and never signs changed arguments. It reports
available hashes as unverified evidence even if the transaction outcome is a
failure. The signer response label is `SubmittedUnverified`, not `Verified`.
The existing on-chain payment ID uniqueness, atomic quota charging, restrictions
and callback accounting remain unchanged. Ambiguous submission is reconciled,
not retried with a new permit. Missing callbacks remain Reserved rather than
being invented as settlement. On-chain attempt-budget consumption is unchanged.

## Changed files

| File | Purpose |
|---|---|
| `src/Sentinel.Application/Security/GatewayContracts.cs` | Specific expiry exceptions without weaker validation |
| `src/Sentinel.Application/Security/DelegationConstraints.cs` | Independently named original delegation checks |
| `src/Sentinel.Infrastructure/Security/DelegatedSpendingService.cs` | Structured diagnostics, run binding, duplicate dispatch guard, bounded permit expiry |
| `src/Sentinel.Api/Exceptions/GlobalExceptionHandler.cs` | Actionable expiry code in existing error response |
| `src/Sentinel.Api/Controllers/DelegationsController.cs` | Pass optional run binding header |
| `src/Sentinel.Api/Controllers/DelegatedAgentController.cs` | Stage-aware errors, durable result recovery, idempotent run lookup |
| `src/sentinel-agent-client/src/client.ts` | Read delegation expiry and bind proposal to run |
| `src/sentinel-agent-client/src/agent.ts` | Cap expiry; preserve proposal after dispatch failure |
| `src/sentinel-signer/src/execution.ts` | Upper expiry validation and failed-outcome evidence retention |
| `src/sentinel-signer/src/agentExecution.ts` | Final expiry and permit checks |
| `src/sentinel-signer/src/signerApp.ts` | Honest unverified submission status |
| `src/sentinel-signer/scripts/check-delegated-readiness.mjs` | Read-only network/key/pin attestation |
| `tests/start-real-delegated-signer.ps1` | Existing matching service credentials and explicit delegated startup |
| `src/sentinel-dashboard/src/api.ts` | Reusable task run ID and permit-state typing |
| `src/sentinel-dashboard/src/components/DelegationManager.tsx` | Expiry display, prevent stale/claimed dispatch, task retry binding, refresh after errors |
| Unit, integration, agent and signer tests | Boundary, expiry, failure, recovery and security regressions |
| `tests/run-milestone1.ps1`, `tools/Sentinel.Verification/Program.cs` | Optional isolated build output while real API remains running |

No schema migration or smart-contract rebuild/redeployment is required.

## Verification and limits

Evidence is in `.test-artifacts/delegated-investigation/`. The regular API build
was blocked by the running API's locked DLLs. Isolated artifacts compile the API
and tests successfully. Starting a separate PostgreSQL instance was blocked by
Windows restricted-token error 87; the established verifier instead creates
isolated test schemas, explicitly excluding application tables from search path.

Final verification (`verification-final.log`, completed successfully):

| Suite/check | Result |
|---|---|
| .NET unit tests | 53 passed, 0 failed, 0 skipped |
| PostgreSQL HTTP integration tests | 23 passed, 0 failed, 0 skipped |
| Provider-independent agent tests | 81 passed, 0 failed, 0 skipped |
| Signer tests | 31 passed, 0 failed, 0 skipped |
| Frontend tests | 5 passed, 0 failed, 0 skipped |
| Dashboard build and lint | Passed |
| Agent workspace TypeScript check | Passed |
| Auth client TypeScript build | Passed |
| Read-only real signer attestation | BLOCKED: RPC stage; public pin mismatch already detected |

Total: **193 passing tests**, zero failures/skips in the final runs. The 23
integration tests include existing owner-approved regressions and delegated
HTTP execution with simulated chain operations, concurrent single-use permit
consumption, callback/reconciliation states, dispatcher transport rejection,
both provider workflows, runner failure after persisted proposal, bound retry
recovery and unauthorized/forged authority rejection. Unit tests cover each
delegation condition, exact limit boundaries, old-day daily usage and exact
Unix nanosecond conversion/24-hour/expired boundaries. Signer tests cover
restricted permissions, rejection, changed intents and ambiguous reporting.
The final isolated schema is `sentinel_e2e_20261108171607_8ee89197`.

Reproduce the integrated local suite without stopping the real API:

```powershell
dotnet run --no-restore --project tools/Sentinel.Verification -- pg-e2e --artifacts-path .test-artifacts/delegated-investigation/dotnet
npm.cmd --prefix src/sentinel-agent-client test
npm.cmd --prefix src/sentinel-dashboard test
node src/sentinel-signer/node_modules/typescript/bin/tsc --noEmit -p src/sentinel-agent-client/tsconfig.json
npm.cmd --prefix src/sentinel-dashboard run lint
```

Earlier failures are retained in logs: legacy test exception expectation, a
fixture recipient left blocked by the preceding test, and TypeScript's narrow
UUID default-parameter inference. These were corrected, not bypassed.

No live model, chain execution, callback or wallet browser test was performed.
Rust was inspected but not changed or rerun; the contract expiry/receipt guards
remain intact. No new live-chain compatibility claim is made.

Deployment impact: rebuild/restart the API, restart the Claude runner,
build/restart the signer only after attestation passes, and restart/reload Vite
(or rebuild the deployed frontend). Existing running processes were not stopped
by this investigation. No contract deployment or database migration is needed.

## Operator commands and one-payment verification plan

From repository root, safe checks:

```powershell
Set-Location C:\Users\PC\Documents\hackerthon\Sentinel
dotnet run --no-restore --project tools/Sentinel.Verification -- pg-startup-check
npm.cmd --prefix src/sentinel-signer run build
powershell -NoProfile -ExecutionPolicy Bypass -File tests/start-real-delegated-signer.ps1 -CheckOnly
```

The last command makes read-only RPC calls and must report
`DELEGATED_READINESS_PASS`. Resolve the current pin mismatch using reviewed,
already provisioned restricted credentials; do not rotate/overwrite keys or
loosen the verifier as a workaround. If RPC remains blocked, run the command in
normal PowerShell. A passing attestation is necessary, not settlement evidence.

After checks pass, restart the API and dashboard to load the code. Stop only
your own previous processes with Ctrl+C:

```powershell
# Terminal 1 — HTTPS required by signer
powershell -NoProfile -ExecutionPolicy Bypass -File tests/start-real-api.ps1
# Terminal 2
powershell -NoProfile -ExecutionPolicy Bypass -File tests/start-real-dashboard.ps1
```

**The following live execution setup and dashboard action require explicit
approval for one 0.001 NEAR payment to `0xsameey-agent.testnet` under the selected
active delegation. Do not run or click them during read-only checks.**

```powershell
# Terminal 3 — stop the previous signer occupying 4301 first; no other signer
powershell -NoProfile -ExecutionPolicy Bypass -File tests/start-real-delegated-signer.ps1
# Terminal 4 — restart to load runner fixes; HTTPS API is the default
powershell -NoProfile -ExecutionPolicy Bypass -File tests/start-real-agent.ps1
```

1. Open http://localhost:5173, authenticate the owner and select delegated mode.
2. Inspect an existing active delegation: exact agent, allowed recipient,
   >=0.001 NEAR remaining budget/daily allowance/per-payment maximum, enough
   remaining lifetime, unrevoked. The old cached delegation/payment expiries
   may already have elapsed. If no eligible delegation exists, stop and request
   separate approval; do not create one automatically.
3. **After the specific payment approval**, enter recipient
   `0xsameey-agent.testnet`, ceiling `0.001`, task:
   `Propose exactly 0.001 NEAR for Sentinel AI integration testing under this delegation.`
   The delegated task button can dispatch ALLOW automatically; it is not a
   read-only proposal button. Click once and record run/payment IDs.
4. For WARN, inspect evidence and explicitly approve the exact warning as owner;
   execute once before expiry. BLOCK stops. Do not weaken the policy to force
   ALLOW. Expired proposals require a new assessment/intent, not a changed row.
5. Refresh/reconcile the **same payment ID**. A task or HTTP error must show the
   created record when bound; use its run lookup if needed. Do not submit a new
   task to work around uncertain dispatch.
6. Confirm finalized matching on-chain delegation association, recipient,
   amount and expiry. Only Completed/Settled proves payment settlement. A hash,
   DispatchAccepted or Attempted does not. Failed/Reserved/unknown stays
   explicitly unresolved; do not sign again automatically.

Remaining gates: correct existing restricted signer/pin configuration, successful
normal-shell RPC attestation/release checks, eligible active delegation and
specific live-payment approval. No claim of a successful live payment is made.
