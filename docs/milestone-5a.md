# Milestone 5A — real-model and testnet readiness

2026-11-03. Scope: dashboard validation, official Claude transport, corrected
contract readiness, restricted agent permissions and actual end-to-end evidence.
See [verification report](milestone-5a-verification.md). Simulation is never live
settlement. No approval-gated operation below has been executed.

## Phase 1 — application and dashboard

Run `powershell -NoProfile -ExecutionPolicy Bypass -File tests/run-milestone5a.ps1`.
It runs the complete previous suite, actual API/PostgreSQL demo and dashboard lint.
Verification uses an available loopback port and separate build configuration to
avoid an existing Debug demo locking files. Interactive launch remains
`tests/start-milestone4-demo.ps1`; set `SENTINEL_DEMO_PORT` if 8040 is occupied.
Keep the printed local owner session link private.

Follow the [Milestone 4 walkthrough](milestone-4.md): ALLOW; separate WARN
authorization/execution approvals; expiry and changed-fingerprint refusal;
BLOCK and spending limits; one-use permit replay; audit and synthetic completion.
HTTP routing, gateway, auth and PostgreSQL are real. Models and chain facts are
fixtures; there is no signer in the demo. Simulated Completed is not settlement.

Implemented UI corrections: normalized request headers; keyboard focus styling;
long-identifier wrapping; session input sizing; empty audit and audit-refresh
failure states; malformed audit JSON fallback; stale decision clearing when a
new scenario starts. Visual sign-off still requires desktop, 768px and 375px
inspection, keyboard navigation, pending/error/session states, WARN stages,
history/audit detail and browser console/network error checks. Builds cannot
substitute for that review.

## Phase 2 — official Claude integration

Install the existing pinned official SDK from the ordinary development shell:

```powershell
Push-Location src/sentinel-agent-client/providers/anthropic
npm.cmd install --ignore-scripts --no-audit --no-fund
Pop-Location
$env:SENTINEL_AGENT_PROVIDER = 'anthropic'
# Supply ANTHROPIC_API_KEY through the local secret/environment mechanism.
node --experimental-strip-types src/sentinel-agent-client/demo/claude-readiness.mjs
$env:ANTHROPIC_MODEL = 'claude-haiku-4-5-20251001'
node --experimental-strip-types src/sentinel-agent-client/demo/claude-readiness.mjs
npm.cmd --prefix src/sentinel-agent-client run smoke:real
```

The user selected `claude-haiku-4-5-20251001`; account availability remains
unverified until authenticated discovery and inference succeed. Review npm's
generated lockfile before release; no fabricated lockfile or SDK.
The readiness command lists model IDs and retrieves a configured model using
the SDK, without inference or gateway/signing imports. The smoke requests one
read-policy tool call: 30-second timeout, no retries, 1,200-token output cap.
It cannot propose/sign payments. Missing SDK/credentials/model fails explicitly.
See official [SDK instructions](https://platform.claude.com/docs/en/cli-sdks-libraries/sdks/typescript)
and [model endpoints](https://platform.claude.com/docs/en/api/typescript).

For structured payment assessment use existing `demo/real.mjs`, configuring
`SENTINEL_API_URL`, assessment-only `SENTINEL_AGENT_API_KEY`,
`SENTINEL_DEMO_RECIPIENT`, `SENTINEL_DEMO_MAX_YOCTO`, `SENTINEL_DEMO_TASK`.
Use an isolated migrated database until the application migration is approved.
Verify natural-language task → policy/recipient tools → structured proposal →
canonical gateway assessment. Both OpenAI and Claude share the same agent loop,
immutable task constraints and gateway; adapters have no approval/signing tools.

Real-model cases: legitimate payment; independent WARN facts; controlled
blocklisted recipient; task-text recipient redirect; excessive amount; forged
approval; request for signing or policy changes. Record model behavior separately
from gateway decisions. A model refusal does not prove independent enforcement:
also assess a malicious structured proposal, confirm BLOCK, zero permits and
zero signer requests. Do not submit an on-chain attack. Use controlled fixture
evidence; do not relax real limits or allowlists.

## Phase 3 — exact operations requiring approval

Each action needs its own explicit approval for final parameters. No deployment,
key generation/registration/revocation, application migration or blockchain
transaction is authorized by this plan. Fresh checks must establish finalized
testnet chain ID, owner, agent, current code, key permissions, allowlist, policy,
balances and quotas. Fresh RPC access failed in this session; old findings are
historical. Mainnet is never a fallback.

### A. Corrected deployment

Network: NEAR testnet. Contract: `0xsameey-sentinel-v2.testnet`.
Prepared artifact:
`.test-artifacts/milestone5a-release-2820b6f21e514705a08abf6167c4e97f/corrected.wasm`.
SHA-256: `34d9ad95449b50a9ad4005d14a950b9d79b2b677ac5588aefc8640f61d15784f`.
Reproduce with `tests/prepare-milestone5a-contract.ps1`: Rust 1.99.1, offline
locked dependencies, `-C link-arg=-s --cfg near`, wasm-opt 0.116.1 size optimization
with TruncSat/BulkMemory. The per-build manifest records source, locks, optimizer
and output hashes. This is local provenance, not a hermetic-build attestation.

Expected change: enforce maximum 24-hour authorization lifetime. No production
state-field, prefix or enum-layout change. Use contract-account deploy authority
outside the agent. Do not call `new` or legacy `migrate` on the current layout.
Snapshot code/config/policy/allowlist/payment statuses/quotas/pending amounts;
rehearse representative state in a sandbox. Legacy `migrate` only reads the old
five-field state, requires pause and zero pending, and initializes deny-by-default
policy/allowlist; it is not a general upgrade command.

The creation guard does not retroactively shorten old long-lived authorizations.
Audit those and finish in-flight receipts before upgrade. Any pause/cancellation
is separately approved. Risks: state incompatibility, callback interruption,
fees and application code-hash mismatch. Refresh reviewed artifact allowlists
without bypassing release checks. Prefer approved pause/forward fix; redeploying
the previous binary reintroduces the known missing guard. Rollback cannot undo
payments and requires separate approval. Final deploy signer, gas/fee ceiling
and current code hash are still required before deployment approval.

### B. Restricted agent key

Account `0xsameey-agent.testnet`, NEAR testnet. Permission exactly:

```json
{"FunctionCall":{"receiver_id":"0xsameey-sentinel-v2.testnet","method_names":["execute_payment"],"allowance":"5000000000000000000000"}}
```

Allowance is 0.005 NEAR for gas, not a treasury spending limit. No full-access,
wildcard methods, additional receiver, attached deposit or existing-key removal.
No key was generated. The exact new public key must be bound to registration
approval after approved secure generation; this is not yet an executable AddKey
request. Do not silently restore the historical missing key. Private material
belongs only in the isolated signer, never model context or frontend variables.

Verify exact public key via direct and list queries at a finalized block. Prove
in a NEAR sandbox that Transfer, unrelated methods, set_policy, authorize_payment,
set_agent and set_recipient_allowed are rejected. Local permission-validator
tests are not runtime protocol evidence. Testnet signed negative probes also
require their own approval and may cost fees. See NEAR's [access-key semantics](https://near.github.io/nearcore/DataStructures/AccessKey.html).
Risk: compromise can burn allowance or execute existing eligible authorizations.
Rollback: separately approved DeleteKey for this exact public key, stop signer,
verify revocation. Completed payments cannot be rolled back.

### C. Application database migration

Observed target: local `sentinel_db`, role `sentinel_user`, schema `public`,
applied through `20261101154640_AddPaymentRiskAssessment`. Propose applying only
`20261103102602_SecurityGateway`. It adds three nullable security-binding columns
to payment_requests, four tables (security_assessments, human_approvals,
execution_permits, counterparty_blocklist), indexes and foreign keys. No row
deletion or policy change is intended. Generated idempotent SQL is
`.test-artifacts/milestone5a-migrations.sql`; it has not been applied here.

Before approval: confirm target/history, review SQL, retain restorable backup
and row counts, test restore, stop workers/concurrent writes as necessary,
set bounded lock/statement timeouts. Integration tests migrate isolated namespaces
only. After approved application: inspect history/constraints, preserved row
counts, API health and security routes. Risks: DDL locks, partial manual changes,
schema/application mismatch. Prefer forward correction. EF Down deletes security
tables/columns and their evidence; never automatically run it. Backup restore
requires separately approved recovery accounting for subsequent chain activity.

## Phase 4 — proposed payment and live evidence

Testnet treasury `0xsameey-sentinel-v2.testnet` → `0xsameey-agent.testnet`,
**0.001 NEAR** (`1000000000000000000000` yoctoNEAR), purpose
`Sentinel Milestone 5A controlled end-to-end verification`. No allowlist/limit
changes. Prepare a unique API payment ID and exact expiry 15 minutes after
preparation. Present both before approval; neither is fixed yet.

Separately approve (1) verified owner `authorize_payment` with exact ID,
recipient, amount, expiry and (2) restricted agent `execute_payment` with that ID.
Proposed gas 30 Tgas per call, deposit zero, combined fee ceiling 0.01 NEAR;
refresh price/allowance first. Deployment/key/migration approval never approves
payment. WARN needs exact fingerprint review; BLOCK stops before permit/signing.

After authorization finality, check immutable fields and expiry; re-assess
execution and release prerequisites. Record one-use permit consumption, both
transaction hashes, final receipts, contract Completed, recipient balance
evidence and database attempts/status/audits. A submission hash is not settlement.
Reconcile ambiguous outcomes by existing hash, never blind resubmission. Verify
duplicate report idempotency. Cancelling an unexecuted authorization needs
separate owner approval; a completed transfer has no contract rollback.

Run AI ALLOW/WARN/BLOCK plus all local suites again after approved changes.
Live readiness requires actual model responses, exact restricted-key execution,
final chain receipts and matching database evidence. Until then Sentinel is
verified for a labeled local demonstration, not the full live AI payment demo.
