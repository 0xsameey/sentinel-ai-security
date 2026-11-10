# Milestone 6A — read-only deployment readiness

2026-11-04. Testnet target: **0xsameey-sentinel-v2.testnet**.

## Decision: NO-GO pending NEAR runtime proof and current revalidation

**The existing contract is not yet cleared for deployment.** A complete saved capture
is now available for finalized-request block **276211664**. Its code, root, payment
inventory, namespace layout and balances have been verified locally. The prepared
deployed-state decoder passes, and actual deployed/proposed WASM view exports ran in
Node/V8 without changing storage. These are stronger results than the earlier audit.

They are **not metered NEAR VM, receipt or gas proof**. The NEAR engine is unavailable
offline, independent block/finality revalidation here still fails with `EACCES`, and
the captured contract was unpaused. Refresh and verify a quiescent snapshot under a
separately approved maintenance plan before any deployment. No deployment approval
or chain mutation is requested or implied by this follow-up.

No deployment, signing/submission, key generation/change, live migration or
application-data write was performed. Database inspection used SELECT/metadata
queries, including an explicitly READ ONLY transaction. Production sources and
reviewed artifacts were unchanged. New files are audit/rehearsal tools and this report.

## Verified checks

| Check | Result |
| --- | --- |
| Prior Milestone 5B | 202 distinct passes, zero failures/skips; simulated chain/model boundary documented |
| Manifest integrity | All seven file hashes match `docs/milestone-5b-artifact.json` |
| Optimized proposed WASM | 200,146 bytes; SHA-256 `3fdb4299956bbd008fe4851bf0dede4354253c2f4c9b9d0a3974d6010d9ae645` |
| Proposed NEAR code hash | `5JGbnZu5NUppt922dqjtAH6oTixkVkXEKcynUqjjHTyE`, matches delegated release pin |
| Proposed exports | Existing methods retained; five delegation/payment-binding methods added |
| Captured deployed WASM (also matches historical backup) | 168,322 bytes; SHA-256 `f32867464baa1f9e74a7d7506e474e3dc0ab07f1424bc646cb132dd154782834`; differs from proposal; delegation methods absent |
| Historical expiry differential | Saved evidence reproduces that old WASM without the current 24-hour guard; not fresh chain proof |
| Supplied capture / independent recheck | Supplied audit completed at block 276211664; follow-up independent `status` recheck still failed with `EACCES` |
| Saved deployed WASM/state/balances | 32-entry snapshot and WASM verified at the captured block; no independent current-head revalidation |
| Rust | 35 passed, including prior policy-state preservation and callback tests |
| .NET unit/security | 40 passed |
| Signer | 31 passed |
| Agent/provider/adversarial | 72 passed |
| Dashboard render checks | 3 passed |
| Read-only diagnostic logic | 18 passed |
| Initial 6A local total (not rerun in this follow-up) | **199 passed; zero failed/skipped** |
| Earlier read-only PostgreSQL history (not refreshed in this follow-up) | Last applied: `20261101154640_AddPaymentRiskAssessment` |
| Pending migrations | `20261103102602_SecurityGateway`, then `20261104095505_DelegatedSpending` |
| Pending-only SQL | Generated, not applied; 10,416 bytes; SHA-256 `45b1a4892170155a3f818c0d2ae4a8c426b2062930c51bf905a770517eb74547` |
| CLI | Installed `near-cli-rs 0.30.1`; relevant syntax checked using `--help` only |
| Earlier Claude SDK check (not refreshed here) | Import fails `ERR_MODULE_NOT_FOUND`; provider package pins official SDK 0.71.2 |
| Earlier Claude environment check (not refreshed here) | API key/model variables absent here; values not displayed |

Evidence: `.test-artifacts/milestone6a/audit.json`, `milestone6a-live-audit.log`,
`milestone6a-db-inspect.log`, `milestone6a-db-metadata.log`,
`milestone6a-{rust,unit,signer,agent,dashboard,diagnostics}.log`.
Pending SQL: `.test-artifacts/milestone6a-pending-migrations.sql`.

The 21 PostgreSQL integration tests and interactive demo were **not rerun** because
their runners create/write isolated schemas. Their prior 5B passes remain historical
evidence. No runner skips were counted as successes. NuGet vulnerability-feed access
was unavailable; no fresh vulnerability-feed result is claimed.

## Deployed-snapshot follow-up evidence

The prior RPC-blocked findings are superseded **only for the saved capture now
provided**. Other database/deployment-configuration/SDK findings below remain earlier audit findings,
not newly refreshed checks. This follow-up made no database connection or write.

| Captured fact | Verified result |
| --- | --- |
| Network / account | Audit tool required testnet; target `0xsameey-sentinel-v2.testnet` |
| Capture block | 276211664; `ChNENKfyxqoT1MocmA2bYS5wogpdGj89k3624qeiyJkJ` |
| Block timestamp | 2026-11-04T15:12:43.105616604Z |
| Finality provenance | Capture tool requests `finality:final`, pins subsequent queries to its hash; snapshot/account/key responses agree on hash and height |
| Independent finality proof | Not completed: follow-up RPC `status` failed with EACCES; raw block header/consensus validation unavailable |
| Deployed code | File SHA-256 `f32867464baa1f9e74a7d7506e474e3dc0ab07f1424bc646cb132dd154782834`; base58 hash `HNBoc7jcQkCUfRib2veoAZw4R12RcN9y4vzKjZ3j7C99` agrees with audit and account |
| Owner / agent | `0xsameey-owner.testnet` / `0xsameey-agent.testnet` |
| Paused | false at capture; no pause/unpause was attempted |
| Policy | 0.1 NEAR maximum payment; 0.5 NEAR daily limit |
| State completeness | 32 unique entries from empty-prefix `view_state`; root, 10 indexes, 10 keys, 10 values and one recipient entry; no unclassified/orphan keys |
| State proof boundary | No Merkle proof was requested; internal inventory completeness and pinned response consistency are verified, not independent cryptographic state-root proof |
| Payments | 10 unchanged records: one Completed (0.1 NEAR), nine Authorized (0.05 NEAR each) |
| Authorization validity | All nine Authorized records were expired at the captured timestamp; zero active authorizations; expired total 0.45 NEAR is not a pending reservation |
| Pending / callbacks | pending_yocto=0; no Executing payments; no execution-day mappings. No tracked in-flight callback reservation at capture; external receipt history/queue was not independently queried |
| Recipient map | Agent account is allowed; the captured map has only that one entry |
| Namespace compatibility | `pi/pk/pv`, `a`, `d` metadata match; zero delegation/payment-binding namespace entries |
| Stored quota counters | Authorization day 20759 retains 0.45 NEAR; execution day 20757 retains 0.1 NEAR completed and zero executing; these past-day counters are preserved |
| Current-day view | Day 20761 reports authorization/completed/executing usage all zero, matching the actual WASM view at the captured timestamp |
| Manifest | Seven of seven hashes independently recomputed and unchanged |

Snapshot file SHA-256:
`f128877ae1f8bd2e6baaf73dd15eabe31c488fd6e36a1662290ec4efe2d4c10c`.
The audit, snapshot and deployed WASM hashes are recorded together in
`.test-artifacts/milestone6a/snapshot-verification.json`. Original capture files were
not overwritten. The decoder evidence includes every payment ID/recipient/amount/
expiry/status in `.test-artifacts/milestone6a/deployed-state-rehearsal.json`.

### Rehearsal results and boundaries

The prepared wrapper built/ran the decoder successfully. Its canonicalization warning
on stderr caused a PowerShell NativeCommandError wrapper status; directly running the
same compiled executable returned **exit 0** and produced the JSON evidence above.
It confirms `layoutCompatible`, `rootBytesPreserved`, `allStoragePreserved` and
`quiescentForUpgrade` are true for this snapshot. Native execution/source mocks do
not establish gas, protocol validation or asynchronous receipt behavior.

An actual binary execution rehearsal was also completed with
`tools/rehearse-milestone6a-wasm-views.mjs`: **46 actual WASM invocations** in Node
v22.19.0 / V8 12.4.254.21-node.29. Seventeen old-versus-proposed view cases match:
configuration, policy, daily usage, available balance, allowed/disallowed recipient,
all ten payments and an unknown payment. Proposed-only delegation/binding reads
return null for the absent namespaces. All storage stays byte-identical. Mutating
storage/promise imports and any method outside the read-only whitelist throw;
`new`, `migrate`, pause/unpause and execution methods are never invoked.

**This is actual V8 WASM execution with a custom read-only host adapter, not NEAR VM
proof.** Evidence `.test-artifacts/milestone6a/wasm-readonly-rehearsal.json` explicitly
records `nearMeteredVm:false` and `gasAndReceiptsVerified:false`. It does not prove
NEAR instrumentation/feature acceptance, 30 Tgas sufficiency, receipt rollback,
promise transfers, private callback execution, or code-deployment semantics.

The actual NEAR runtime availability probe failed offline resolving `finite-wasm`
for `near-vm-runner` 0.37.4; Wasmtime 45.0.0 source is not cached and no standalone
sandbox/wasmtime executable is on PATH. See
`.test-artifacts/milestone6a/vm-runtime-availability.log`. A real metered NEAR
snapshot-clone rehearsal therefore remains a hard blocker. No dependency download,
key provisioning or testnet deployment was used as a shortcut.

### Storage collateral and funds at the captured block

| Item | NEAR / bytes |
| --- | --- |
| Account amount | 9.8985996987477071 NEAR |
| Current storage usage | 171,916 bytes |
| Storage collateral implied by contract view | 1.71916 NEAR |
| Additional 4,096-byte contract buffer | 0.04096 NEAR |
| Contract gas reserve | 0.2 NEAR |
| Pending reservations | 0 NEAR |
| Captured available balance | 7.9384796987477071 NEAR |
| Proposed code size | 200,146 bytes; growth 31,824 bytes |
| Additional collateral for code growth | 0.31824 NEAR at the inferred contract byte price |
| Projected storage usage | 203,740 bytes, assuming only code size changes |
| Projected available after code growth | 7.6202396987477071 NEAR |
| After one 0.001 transfer | 7.6192396987477071 NEAR before new record collateral and fees |

The captured account amount, available view, buffer, reserve and zero pending imply
an exact **10^19 yoctoNEAR/byte** contract calculation. Old/proposed actual WASM
views reproduce the captured available balance. The proposed WASM with the projected
storage-usage context reproduces the projected available balance. This is conditional
arithmetic, not a post-deployment observation or independently fetched protocol config.
No upgrade fee, delegation/payment storage or concurrent activity has been deducted.
Treasury liquidity at capture appears ample for code growth and 0.001 NEAR; it is not
the current blocker. Agent gas-account balance, current protocol parameters and
current treasury state still require read-only revalidation before approval.

The capture shows an owner-approved `execute_payment` key with 0.25 NEAR gas
allowance, but no delegated-method key. Its presence does not prove that a configured
signer holds the corresponding private key. Do not alter that existing key. All earlier separate deployment, key, migration,
runtime and payment approvals remain required; the successful local rehearsals grant
none of them.

## Upgrade compatibility and required evidence

`Sentinel` root fields, `Payment` fields and payment-status variant order are unchanged.
Existing prefixes are `pi/pk/pv` for payment collection metadata, `a` for recipients,
and `d` for execution-day mappings. Added namespaces are:

- `sentinel:delegation:v1:<id>`
- `sentinel:payment-delegation:v1:<payment-id>`

Code replacement needs no initializer **only if actual state has the reviewed
policy-bearing layout**. The five-field legacy migration is incompatible with that
layout. **Never call `migrate`, `new`, or replace STATE during this upgrade.** The old
source comment suggesting deploy-then-migrate does not apply to policy-bearing state.

Before deployment approval, obtain a fresh finalized snapshot establishing:

1. Testnet identity, block hash/time, actual code hash and backup bytes.
2. Exact root roundtrip, expected collection prefixes and every payment record;
   unchanged owner/agent, policy, recipients, quotas and counters.
3. No occupied delegation namespaces, or a separately reviewed compatibility plan.
4. `pending_yocto` equals all Executing amounts and each has an execution-day mapping.
   Pause first under specific approval; let callbacks finish; require **zero pending
   and zero Executing** in a new snapshot before code replacement. Never unlock funds
   merely because a callback is old or missing.
5. Inventory Authorized payments without changing/cancelling their IDs, recipients,
   amounts or expiries. The new creation guard does not shorten old approvals.
6. Current account/storage/available balances and quota headroom, including code growth.

The proposal is 31,824 bytes larger than the now-verified captured binary. The
collateral calculation above uses the byte price inferred from its available-balance
view, not a fresh protocol-config response. Revalidate parameters and actual
post-upgrade liquidity separately from fees and the 0.001 transfer, preserving
the contract's 0.2 NEAR reserve, 4,096-byte buffer and all pending funds.

## Prepared capture and offline rehearsal

Run in normal PowerShell at the repository root, without signing credentials:

```powershell
node .\tools\audit-milestone6a.mjs
$audit = Get-Content .\.test-artifacts\milestone6a\audit.json -Raw | ConvertFrom-Json
if ($audit.live -ne 'read-only snapshot complete') { throw 'STOP: incomplete fresh snapshot.' }
if ($audit.network -ne 'testnet') { throw 'STOP: wrong network.' }
if ($audit.files.Where({ -not $_.matches }).Count) { throw 'STOP: artifact drift.' }
cmd /d /c tests\rehearse-milestone6a-state.cmd `
  .test-artifacts\milestone6a\state.json `
  $audit.blockTimestampNs $audit.account.amount
```

The capture tool implements only status/block/query/gas_price, loads no signing key,
saves complete storage and WASM at one finalized block, and cross-checks code hashes.
Blocked RPC exits 2. Preserve each capture in a new dated evidence directory before
another run; never overwrite the sole pre-upgrade backup. Partial state/provider-limit
responses are blockers, not usable snapshots.

`tools/sentinel-state-rehearsal` built with the established VS environment and AWS-LC
prebuilt NASM fallback. It imports a snapshot into a local near-sdk mock, independently
decodes the prior root, checks prefixes/root bytes/every payment/pending totals and
namespace absence. It executes no initializer, migration, callback, transfer or RPC.
Expected: `layoutCompatible=true`, `rootBytesPreserved=true`, `quiescentForUpgrade=true`.
Any panic, occupied namespace or nonzero pending amount stops upgrade review. The
rehearsal also returns decoded payment inventory and checks every storage key/value
is preserved, including unrelated keys.

Four synthetic self-checks passed: compatible root accepted, malformed root rejected,
occupied namespace rejected, pending-transfer mismatch rejected. They are separate from the 199-test count; evidence is
`.test-artifacts/milestone6a/rehearsal-self-check.json`. The existing Rust fixture
rehearsal additionally preserves all statuses and completes an old in-flight callback.

**The saved deployed-snapshot decoder and V8 view rehearsal now pass; a metered
NEAR VM rehearsal and current finalized revalidation remain outstanding.**
Execute old/new WASM in a local NEAR VM/sandbox clone to verify receipt
rollback, transfer/callback failure, 30 Tgas sufficiency and state/balance preservation.
The source/mock decoder does not prove these VM properties. No standalone
`near-sandbox` or `wasmtime` executable was found on PATH in this environment. Isolated testnet account
provisioning/deployment instead requires separate approval naming accounts, funds,
keys and fees. No infrastructure or existing deployment was modified here.

## Critical findings

- Captured layout/payment/reservation compatibility now passes; current finalized
  revalidation and metered NEAR gas/receipt proof remain hard upgrade blockers.
- Six security/delegation tables and three `payment_requests` security columns are
  absent. Do not start live flows or bypass the gateway to avoid migration failures.
- Presence-only configuration audit: API contract matches target; both
  `SecurityRelease:AgentPublicKey` and `SecurityRelease:DelegatedAgentPublicKey` are
  missing; `AgentGateway:ApiKey`, runner URL/key and delegated-dispatcher URL/key are
  missing. Worker key is present and the existing owner dispatcher URL is valid.
  No credential values were displayed.
- At the captured block, deployed code and the existing owner-approved key/price are
  recorded; no delegated key is present. Current permissions/allowance/price and
  deployed state must be revalidated before release.
- Claude SDK/environment and browser-level authenticated QA remain unavailable/unverified.
- No new on-chain authority escalation/budget bypass was identified. However, a stolen
  restricted key can call the allowed method directly, bypassing off-chain WARN/permit
  decisions. On-chain caps/recipients/expiry/revocation are the financial boundary;
  the contract does not enforce gateway human-review semantics.
- Amount quotas do not bound record-count storage collateral or signer gas costs.
  Use a brief, single-demo delegation and finite gas allowance; high-volume autonomy
  needs a separate resource-cost review. Lost callbacks and ambiguous signing stay
  fail-closed; never reset a permit or refund quota based on timeout alone.
- Existing database payments without canonical gateway bindings must remain intact
  and fail closed. Do not fabricate historical ALLOW records during migration;
  outstanding owner-approved payments need individual reconciliation/review.


## Restricted function-call key — setup plan only

Required delegated permission is an ordinary **FunctionCall** key, not FullAccess,
a wildcard or gas-key variant:

```json
{"FunctionCall":{"receiver_id":"0xsameey-sentinel-v2.testnet","method_names":["execute_delegated_payment"],"allowance":"10000000000000000000000"}}
```

Proposed allowance: **0.01 NEAR for gas**, separate from the transfer budget. Fresh
price must cover 30,000,000,000,000 gas. No automatic increase is authorized. The
owner-approved signer needs a different key scoped to exactly `execute_payment`;
combining both methods is rejected. Do not modify/delete its existing key.

Only after specific approval names the actual public key, agent, receiver, method,
allowance and setup fee, plus separate secure private-key provisioning approval:

```powershell
# NOT APPROVED TO RUN. Public key only; never place a private key on the CLI.
$Agent = '0xsameey-agent.testnet' # Must match fresh get_config.
$Contract = '0xsameey-sentinel-v2.testnet'
$DelegatedPublicKey = $env:SENTINEL_DELEGATED_PUBLIC_KEY
if ([string]::IsNullOrWhiteSpace($DelegatedPublicKey)) { throw 'Missing approved public key.' }
near.cmd account add-key $Agent grant-function-call-access `
  --allowance '0.01 NEAR' --contract-account-id $Contract `
  --function-names execute_delegated_payment `
  use-manually-provided-public-key $DelegatedPublicKey `
  network-config testnet sign-with-keychain send
```

Expected: one AddKey on the agent account; direct `view_access_key` and a complete
key list both show the exact permission above. If the account rotated, the key already
exists or allowance is insufficient, stop for another specific plan. Only the signer
gets its private key. API `SecurityRelease:DelegatedAgentPublicKey` must match it.
The contract-account deployment key and the field-level owner are separate authorities.

## Database requirements — no application performed

Read-only history lists migrations through AddPaymentRiskAssessment only.
SecurityGateway adds three nullable bindings on existing payment rows, four security
tables, indexes and restricted-delete foreign keys. DelegatedSpending adds two tables
and indexes. Their Up migrations do not delete payment rows. Pending-only SQL:
`.test-artifacts/milestone6a-pending-migrations.sql`, hash recorded above.

Before application: obtain specific approval for backup/export to a protected local
destination; verify restoration on an isolated database; review locks/constraints;
approve a maintenance window and stopping writers. Do not run demo/test runners
against the application connection as a read-only check. Never run Down on ledgers.

```powershell
# Local SQL generation only; safe to review, not a database application:
dotnet ef migrations script 20261101154640_AddPaymentRiskAssessment `
  20261104095505_DelegatedSpending --idempotent `
  --project src/Sentinel.Infrastructure --startup-project src/Sentinel.Api `
  --configuration Verification --output .test-artifacts/milestone6a-pending-migrations.sql
```

After separate approval fixes `sentinel_db`, schema `public`, both migration IDs,
backup, SQL hash and maintenance window:

```powershell
# NOT APPROVED TO RUN. Populate through the operator's secret manager; never echo.
if ([string]::IsNullOrWhiteSpace($env:SENTINEL_MIGRATION_CONNECTION)) {
  throw 'Missing approved target; do not use the design-only default.'
}
dotnet ef database update 20261104095505_DelegatedSpending `
  --project src/Sentinel.Infrastructure --startup-project src/Sentinel.Api `
  --configuration Verification
```

The design-time factory reads `SENTINEL_MIGRATION_CONNECTION`, not the application
connection-string variable. Its fallback is deliberately an unused design database.
Verify the approved secret's database/schema target without printing it. Expected:
two migration-history additions, six tables, three nullable columns and required
indexes/FKs; existing payment IDs/amounts/statuses/rows unchanged. Reinspect metadata
and preservation counts in a read-only transaction afterward.

## Contract upgrade commands — each mutation requires its own approval

These are prepared commands, **not authorization**. CLI forms were checked with
installed `near-cli-rs 0.30.1 --help`; no signing command was executed. First require
the fresh state/VM rehearsal, code backup, deploy authority and fee/storage budgets.

```powershell
$Contract = '0xsameey-sentinel-v2.testnet'
$Wasm = (Resolve-Path '.test-artifacts/milestone5b-release-68507c54eae64259b97277e4e0b5de1e/corrected.wasm').Path
if ((Get-FileHash -LiteralPath $Wasm -Algorithm SHA256).Hash.ToLowerInvariant() -ne `
    '3fdb4299956bbd008fe4851bf0dede4354253c2f4c9b9d0a3974d6010d9ae645') {
  throw 'STOP: artifact mismatch.'
}
$Owner = [string]$audit.config[0] # Fresh verified get_config, never a guessed owner.
```

**Gate: pause.** Approve exact owner/contract, set_paused(true), zero deposit,
30 Tgas and fee ceiling. Approve freezing off-chain dispatch separately. Pausing
cannot cancel a transfer already scheduled.

```powershell
'{"paused":true}' | Set-Content -Encoding ascii .test-artifacts/milestone6a/pause.json
near.cmd contract call-function as-transaction $Contract set_paused `
  file-args .test-artifacts/milestone6a/pause.json prepaid-gas '30 Tgas' `
  attached-deposit '0 NEAR' sign-as $Owner `
  network-config testnet sign-with-keychain send
```

Expected: finalized paused=true. Let callbacks finish, recapture an immutable snapshot,
require zero pending/Executing and rerun snapshot/VM rehearsal. Preserve all Authorized
payments and backups. No cancellation or withdrawal is implied.

**Gate: deploy code only.** Approval must name this target, exact SHA-256/code hash,
quiescent snapshot hash, contract-account deploy authority, no init/migrate, fee ceiling
and storage-collateral budget. Field-level owner credentials alone do not grant
DeployContract authority.

```powershell
near.cmd contract deploy $Contract use-file $Wasm without-init-call `
  network-config testnet sign-with-keychain send
```

Expected: one DeployContract transaction; finalized code hash becomes
`5JGbnZu5NUppt922dqjtAH6oTixkVkXEKcynUqjjHTyE`. Capture post-upgrade state in a
separate directory. Root bytes, owner/agent, policy, recipients, counters, payment
records and reservations must match the quiescent paused snapshot. Storage usage
can increase with code size; account amount can reflect approved deployment fees.
No delegation should appear merely from code replacement. Any unexpected difference
is a stop. Do not initialize or run migration to repair it.

Finish separately approved database, runtime and key setup, and read-only attestations.
Both signers remain stopped until exact key/receiver/method/allowance/code checks pass.

**Gate: unpause.** Approve exact owner/contract, set_paused(false), zero deposit,
30 Tgas and fee ceiling only after preceding checks pass:

```powershell
'{"paused":false}' | Set-Content -Encoding ascii .test-artifacts/milestone6a/unpause.json
near.cmd contract call-function as-transaction $Contract set_paused `
  file-args .test-artifacts/milestone6a/unpause.json prepaid-gas '30 Tgas' `
  attached-deposit '0 NEAR' sign-as $Owner `
  network-config testnet sign-with-keychain send
```

Policy/recipient changes are not included. If needed, prepare and approve each exact
change separately; preserve the existing owner-approved mode/key and authorizations.

## Claude checks — normal Windows PowerShell

SDK import is missing and process credentials/model are unavailable. The previous
SDK fetch was blocked by sandbox npm `EACCES`; this read-only audit did not reinstall
packages or make a billable model request. In a separate provider-only shell:

```powershell
Push-Location .\src\sentinel-agent-client\providers\anthropic
npm.cmd install --ignore-scripts
if ($LASTEXITCODE -ne 0) { throw 'Official SDK installation failed.' }
Pop-Location
node --input-type=module -e "try { await import('./src/sentinel-agent-client/providers/anthropic/runtime.mjs'); console.log('Claude SDK import available'); } catch(e) { console.log('Claude SDK unavailable: '+e.code); process.exitCode=1; }"
$env:SENTINEL_AGENT_PROVIDER = 'anthropic'
$env:ANTHROPIC_MODEL = 'claude-haiku-4-5-20251001'
foreach ($name in @('ANTHROPIC_API_KEY','ANTHROPIC_MODEL')) {
  $present = -not [string]::IsNullOrWhiteSpace([Environment]::GetEnvironmentVariable($name))
  Write-Output "$name available: $present"
}
```

Never paste/display credentials or inherit the signer's environment. Installation,
import and presence checks make no inference call. After specific approval for **one
Claude no-blockchain smoke request**, up to 1,200 output tokens with the current adapter:

```powershell
npm.cmd --prefix src/sentinel-agent-client run smoke:real
```

Expected: `REAL_MODEL_SMOKE_PASS`, provider anthropic, `read_spending_policy` tool,
`signed:false`. This validates a model tool request only: the script does not execute
that tool against a live policy or construct a signer/gateway. SDK-double tests do
not establish real inference; no real-model success is claimed here.


## Dashboard/demo/runtime readiness

Dashboard `.env.local` matches the target, uses an HTTPS loopback API and has no
sensitive VITE variable names. VITE variables are public build-time configuration:
never put model/worker/signing credentials there. TLS trust, API reachability and
CORS for the actual UI origin still need browser QA.

- Owner-approved mode remains default; delegation management uses owner-wallet calls.
  ALLOW dispatches autonomously, WARN requires exact authenticated human review,
  and Attempted/Reserved/Settled/Failed are distinguished.
- API startup runs a database-writing reconciliation worker. **Do not start it against
  the application connection as a read-only check.** Runtime activation needs approval
  after schema/configuration checks.
- Use separate owner-approved and delegated signer processes. Delegated mode needs
  `SENTINEL_PAYMENT_MODE=delegated`, dedicated dispatch port/key and private key,
  matching testnet agent/contract/RPC and an HTTPS API URL. Owner mode keeps its own
  exact `execute_payment` key. Neither signer was started in this audit.
- Model process gets provider credentials, proposal-only gateway key and runner key,
  never signing/worker/dispatch secrets. It has no management/signing tools and refuses
  known signing/worker environment names. Use separate process environments/stores.
- Trusted service URLs must resolve correctly to `/execute` and `/delegated-tasks`,
  including base paths/trailing slashes. Redirects are rejected.
- `tests/start-milestone5b-demo.ps1` starts a simulated lab and writes isolated schemas;
  it is not a live deployment or read-only DB audit. Its ephemeral owner session URL
  is a credential and must not be published.
- Component tests passed. Prior native browser checks covered only default mode;
  selection timed out and authenticated/mobile/console QA was incomplete. Before
  activation, test responsive mode switching, wallet management, expiry/revocation,
  warnings, TLS/CORS, console/network failures, attempted-versus-settled display and
  timeout recovery against isolated fixtures first. No fresh full browser QA is claimed.
- Gateway calls time out after 20 seconds while dispatch may wait 60 seconds. A live
  transaction may outlast its caller. Reconcile consumed permits/proposals; never
  infer failure from timeout or automatically resubmit.

## Planned 0.001 NEAR live verification — NOT authorized

Proposed recipient: `0xsameey-agent.testnet`, previously selected as user-controlled.
Current existence, control and allowlisting are **not verified**. A different recipient
must be fixed in a new approval. No allowlist or policy change is included.

After release checks, obtain a specific owner approval for a single new delegation:
unique ID; fresh configured agent; only that recipient; per-payment, aggregate and
daily limits each **1000000000000000000000 yoctoNEAR (0.001 NEAR)**; exact expiry
proposed 30 minutes after creation; zero deposit, 30 Tgas and agreed fee ceiling.
Freeze the actual ID/expiry and creation-file hash before requesting approval.
Keep enough remaining lifetime for the 15-minute payment authorization.

```powershell
# NOT APPROVED TO RUN: owner creates authority, not the agent.
# $DelegationId, $Agent, $Recipient and $ExpiryNs must be fixed in the approval.
@{id=$DelegationId;agent=$Agent;max_payment='1000000000000000000000';
  budget='1000000000000000000000';daily_limit='1000000000000000000000';
  expires_at_ns=$ExpiryNs;recipients=@($Recipient)} |
  ConvertTo-Json -Depth 5 | Set-Content -Encoding ascii .test-artifacts/milestone6a/delegation.json
near.cmd contract call-function as-transaction $Contract create_delegation `
  file-args .test-artifacts/milestone6a/delegation.json prepaid-gas '30 Tgas' `
  attached-deposit '0 NEAR' sign-as $Owner `
  network-config testnet sign-with-keychain send
```

Expected: one owner delegation-creation transaction and a finalized delegation with
exact caps/recipient/expiry/agent and zero usage. No individual `authorize_payment`
transaction is needed.

For the first financial verification, prepare an **exact structured agent proposal**
so the amount is deterministic. Claude smoke is separate: do not label this proposal
as live-Claude-generated. A later live-model auto-dispatch requires its own bounded
approval because that workflow may dispatch before returning a proposal to the caller.

After approved database/runtime activation, the following writes a proposal record
but sends no blockchain transaction. `$Api` must be the reviewed HTTPS origin. Headers
must never be printed; verify the proposal-only credential exists first.

```powershell
if ([string]::IsNullOrWhiteSpace($env:SENTINEL_AGENT_API_KEY)) { throw 'Missing proposal credential.' }
$AgentHeaders = @{ 'X-Sentinel-Agent-Key' = $env:SENTINEL_AGENT_API_KEY }
$future = [DateTimeOffset]::UtcNow.AddMinutes(15)
$PaymentExpiry = ([System.Numerics.BigInteger]$future.ToUnixTimeMilliseconds() * 1000000).ToString()
$body = @{delegationId=$DelegationId;intent=@{recipient=$Recipient;
  amountYocto='1000000000000000000000';expiresAtNs=$PaymentExpiry;
  purpose='Sentinel Milestone 6 controlled testnet verification'}} | ConvertTo-Json -Depth 5
$proposal = Invoke-RestMethod -Method Post -Uri "$Api/api/v1/delegations/proposals" `
  -Headers $AgentHeaders -ContentType 'application/json' -Body $body
if ($proposal.decision -ne 'ALLOW') {
  throw 'STOP. WARN needs separate authenticated owner review; BLOCK never dispatches.'
}
```

Record actual payment ID, intent hash/fingerprint, exact amount/recipient/expiry and
delegation, fresh quotas/balance/code/key checks, recipient baseline and fee estimate.
Proposed payment network-fee ceiling is **0.01 NEAR**, separate from the 0.001 transfer
and deployment/storage/key-setup costs. This ceiling needs specific approval and
fresh gas-price confirmation; it is not an automatic CLI-enforced fee setting.

Only after a **specific single-payment approval naming that actual proposal ID**:

```powershell
# NOT APPROVED TO RUN. Use protected dispatch, not raw contract-call bypass.
Invoke-RestMethod -Method Post `
  -Uri "$Api/api/v1/delegated-agent/payments/$($proposal.id)/dispatch" `
  -Headers $AgentHeaders -ContentType 'application/json' -Body '{}'
```

Expected evidence: exactly one consumed intent-bound permit; one agent transaction
using the delegated method; successful 0.001 NEAR transfer receipt; callback completion;
on-chain payment Completed with matching delegation binding; spent/daily use 0.001;
reconciled database Settled. Retain hashes, final block references, receipt outcomes
and before/after state. HTTP success or an unverified hash report is not settlement.
On timeout/failure/missing callback, stop and reconcile; no replacement or permit reset.

The proposed recipient also pays agent transaction gas, so its net balance delta may
not be +0.001 NEAR. Verify the actual incoming receipt and account for fees/other
activity; do not invent a simple positive delta. Revoke the delegation or delete the
dedicated key after the demonstration only under separately named approvals. Neither
can recall a transfer already scheduled.

## Rollback and recovery

- Keep exact pre-upgrade code and full state as evidence. **Never restore old STATE,
  counters, IDs, balances or consumed permits after transfers.** This can enable replay.
- On unexpected behavior, stop dispatch, reconcile receipts and obtain a specific
  owner pause approval. Key revocation is a separately approved transaction. Never
  release reservations based on elapsed time alone.
- Code rollback requires a separately approved, compatible, known-safe artifact and
  quiescent state. The historical binary lacks the 24-hour creation guard and is not
  an automatic rollback target. A reviewed 5A artifact can be considered only after
  its layout/callback behavior and release pins are verified.
- Roll back code only, preserving delegation namespaces, payment bindings and all
  consumption history. No init/migrate. Old code ignoring new records must not erase them.
- Keep additive database tables and migration history during application rollback.
  No Down/drop-ledger operations. Database restore needs a separate reconciliation
  plan and approval and cannot undo an on-chain payment.
- Failed transfers retain delegation charges. Consumed/expired/ambiguous permits are
  not reissued. Any new authority/replacement proposal requires understood prior outcomes.

## Specific approvals still needed

This report requests/implies none. Unknown values must be filled before approval:

1. Optional isolated testnet rehearsal: exact separate account, funds, keys and fees.
2. Maintenance/pause: exact owner call and off-chain services to stop.
3. Code replacement: this target, proposed artifact hash, quiescent snapshot hash,
   deploy authority, no init/migrate, and fee/storage budgets; only after proof passes.
4. Protected backup/export, then database migration: destination, target/schema,
   both migration IDs, SQL hash and maintenance window.
5. Dedicated signing-key provisioning, then AddKey: actual public key, agent,
   receiver, exact method, finite 0.01 NEAR allowance and setup fee. No other key change.
6. Runtime configuration/activation: named processes, origins/ports, credential roles,
   both release public-key pins, and model/signer isolation.
7. One Claude no-blockchain smoke request with the stated model and output limit.
8. Exact owner unpause call after all release checks.
9. One delegation creation with frozen ID/agent/recipient/caps/expiry/gas/fee.
10. One actual proposal/payment ID, exact 0.001 amount, recipient, delegation,
    expiry, method and fee ceiling. No retry or second payment implied.
11. Each subsequent revocation, key deletion, cancellation, policy change or rollback.

Take a fresh finalized attestation immediately before each approved transaction;
retain dated snapshots and compare the approved authority/budget context. If any
account, artifact, policy or amount changes, stop for a revised specific approval.
**Keep live execution disabled until the evidence and gates above are satisfied.**

## References

Local: `docs/milestone-5b-verification.md`, `docs/milestone-5b-artifact.json`,
`contracts/sentinel-contract-starter/sentinel-contract/src/storage_rehearsal.rs`.
CLI syntax was checked locally, with upstream reference:
[NEAR CLI guide](https://github.com/near/near-cli-rs/blob/main/docs/GUIDE.en.md).
Classic restricted-key/allowance semantics:
[NEAR access-key specification](https://near.github.io/nearcore/DataStructures/AccessKey.html).

