# Milestone 5B: controlled autonomous spending

Owner-approved payments remain the default. Delegated mode is explicit in the dashboard
and in a separate signer process (`SENTINEL_PAYMENT_MODE=delegated`). No delegation
exists implicitly. Both OpenAI and Anthropic use the same bounded agent loop; the
Anthropic model remains `claude-haiku-4-5-20251001`.

## Authority and payment flow

1. An authenticated owner requests a `create_delegation` wallet call. Preparation
   is recorded but is not proof of creation. The owner wallet submits it separately.
2. A finalized read verifies the configured agent, explicit recipients, positive
   limits and expiry. Delegations are immutable, limited to 24 hours and 16 recipients,
   and their IDs cannot be reused after revocation. Global policy still applies.
3. An agent proposes a payment through `POST /api/v1/delegations/proposals`.
   Deterministic ALLOW/WARN/BLOCK uses finalized policy, delegation and blocklist facts.
   Purpose and invoice prose never alter policy. Model tools cannot manage authority.
4. ALLOW can dispatch autonomously. WARN requires the JWT-authenticated owner to
   approve the exact fingerprint; approval expires in two minutes. BLOCK cannot
   release a permit. Changed facts require reassessment, which clears approval.
5. A dedicated authenticated worker prepares and consumes a 20-second permit. Its
   intent hash commits to testnet, contract, exact method, configured agent,
   delegation ID, random payment ID, recipient, amount, expiry and purpose. Atomic
   PostgreSQL conditional updates allow one claim and one consumption. A claimed
   permit is never reissued, even after timeout. There is no automatic second signing.
6. The signer compares the consumed transaction to every prepared argument, checks
   exact restricted key permissions and finite gas allowance, and reads delegation
   state again before submission. The chain rechecks all authority and balances.
7. Rust charges delegation attempt budget before scheduling the promise, shares
   authorization and execution policy checks, marks payment Executing, and reserves
   treasury and daily funds within one receipt. NEAR receipt failure rolls back that
   receipt's writes. Transfer callbacks occur in later receipts. Single-use IDs and
   Executing status prevent double execution. There is no external call before charging.
8. Callbacks release treasury reservations and mark success/failure. Delegation
   attempt budget is never refunded on transfer failure. Callback loss leaves funds
   reserved; timeout alone never proves failure. No manual unlock was added.

## API

The additive API contract is `postman/delegations.openapi.json`. Existing routes and
DTOs remain compatible. Owner management/approval require wallet JWTs and on-chain
owner identity; the proposal-only agent credential cannot grant authority. Worker
credentials authorize only preparation, consumption and unverified hash reporting.

- `GET /api/v1/delegations`, `GET /api/v1/delegations/{id}`: indexed IDs and fresh
  state/remaining budget/usage/observations; at most 100 IDs or payments per response.
- `POST /api/v1/delegations`: prepare an owner-wallet creation call.
- `POST /api/v1/delegations/{id}/revoke`: prepare an owner-wallet revocation call.
- `POST /api/v1/delegations/proposals`: persist deterministic assessment and intent.
- `GET /api/v1/delegations/payments/{id}`: persisted proposal and evidence.
- `POST .../payments/{id}/reassess`, `/approve`, `/reconcile`: fresh assessment,
  authenticated human review, and chain-derived transfer state respectively.
- `POST /api/v1/delegated-agent/tasks`: bounded owner task with single-use GUID
  Idempotency-Key; separate delegation identity is never model-editable.
- `POST /api/v1/delegated-agent/payments/{id}/dispatch`: agent/owner dispatch with
  fresh deterministic checks. No per-payment owner approval for ALLOW.
- `POST /api/internal/agent/delegated-payments/{id}/prepare-execution`,
  `/consume-permit`, `/report-transaction`: authenticated isolated worker handoff.

## Dashboard and evidence

Select Delegated autonomous spending from Payment mode. Creation and revocation
use owner-wallet calls and require finalized inspection afterward. Inspect limits,
remaining aggregate budget, attempt usage today, recipients, expiry and status;
run a bounded task; review warnings; reconcile chain evidence.

Proposed = persisted proposal. Attempted = permit consumed, with uncertain submission.
Reserved = chain payment Executing. Settled = chain payment Completed and matching
on-chain payment-to-delegation binding. Failed = chain callback marked Failed.
A reported transaction hash remains explicitly unverified and does not establish
settlement. No transaction explorer link is presented as independently verified.

## Local verification and demo

Run `powershell -NoProfile -ExecutionPolicy Bypass -File tests/run-milestone5b.ps1`.
The runner uses the established Visual Studio environment with AWS-LC prebuilt NASM
objects; PostgreSQL tests create a new `sentinel_e2e_` namespace excluding public.
It does not migrate application tables. Simulated NEAR HTTP responses and submission
callbacks are labeled test doubles; these do not constitute a real NEAR VM rehearsal.

Run `tests/start-milestone5b-demo.ps1` for the local attack lab. The ephemeral demo
owner credential in its session URL must be kept private. New scenarios demonstrate
ALLOW autonomy, WARN approval, malicious recipients, exhausted budgets, revocation,
failed transfers and replay denial. The UI states that signing/broadcasting are disabled.

## Migration and deployment prerequisites

Do not call the legacy `migrate` on policy-bearing state. `Sentinel` and `Payment`
Borsh field layouts and existing p/a/d prefixes are unchanged. New records use
`sentinel:delegation:v1:` and `sentinel:payment-delegation:v1:`. Current policy-bearing
state needs code replacement only, with no initializer or state write. The offline
`storage_rehearsal` test independently serializes the prior root layout, reads it
with the new code, preserves all payment statuses/reservations, and finishes an
in-flight callback. This establishes source-layout compatibility, not deployed provenance.

Before requesting deployment approval:

1. Obtain a read-only finalized state snapshot and deployed WASM/code hash. Match
   provenance to the policy-bearing layout and audit storage prefixes. If unknown,
   stop upgrade work; do not try an initializer, guessed migration or overwrite STATE.
2. Rehearse code replacement against that snapshot in a separate local/test account,
   checking root bytes, collection contents, balances, pending receipts and callbacks.
   Any test-account deployment/transaction requires its separately authorized scope.
3. Build with `tests/prepare-milestone5b-contract.ps1`; compare the corrected artifact
   hash to the hard-coded release pin. Never update a pin merely to bypass a mismatch.
4. Review generated `DelegatedSpending` migration and idempotent SQL. It adds only
   delegation/payment tables and indexes. Apply to the live database only with approval.
5. Obtain approval for code deployment and key provisioning. Use separate finite-
   allowance keys for exact `execute_payment` and exact `execute_delegated_payment`,
   on the configured agent and receiver. No FullAccess, wildcard or management method.
6. Configure `SecurityRelease:AgentPublicKey` and
   `SecurityRelease:DelegatedAgentPublicKey` for their separate signer processes.
   Configure `DelegatedDispatcher:Url/Key`, `AgentRunner:Url/ApiKey`, proposal-only
   gateway authentication and worker authentication separately. Service URLs are
   server-controlled, HTTPS or loopback, and redirects are rejected.
7. Keep model processes separate from signer environments and files. Agent startup
   refuses known signing/worker configuration names. It exposes no file, shell,
   credential, approval or policy tool. Signer imports no model SDK. Provision no
   signing secrets in model SDK environments, task text or tool outputs.
8. Install the pinned official Anthropic SDK from normal PowerShell if sandbox npm
   access remains blocked: change to `src/sentinel-agent-client/providers/anthropic`, then run `npm install --ignore-scripts`.
   Run the existing model smoke separately, without blockchain access. No live model
   access or settlement is established by provider doubles.

## Threats and recovery limits

A compromised model cannot exceed the task ceiling or select another recipient
through the bounded tool loop. A compromised worker might spend the entire active
owner-authorized delegation through direct contract calls; deterministic gateway
checks are not an on-chain control. The hard loss bound is the intersection of
on-chain delegation caps, global caps, explicit recipients, expiry and treasury funds.
Compromised owner credentials, malicious upgrades and dishonest RPC providers remain
outside that bound. Finalized observations reduce races but cannot eliminate changes
between inspection and inclusion; the contract is the last authority.

Revocation/rotation stop future receipts, not transfers already scheduled. Failed
transfers retain delegation charge. Missing callbacks lock treasury reservations.
Lost consumption responses, expired claimed permits and ambiguous signing are not
retried automatically. Reconcile first; use a fresh proposal only when the prior
outcome and remaining authority are understood. An owner may revoke and create a
new capped delegation; agents cannot reset counters or increase their authority.

The transfer caps do not price gas or storage collateral. The signing account pays
its own gas; persistent payment records consume treasury storage collateral. Budget
for these costs separately when creating authority, especially for many tiny payments.


## Demo startup troubleshooting

The starter prefers port 8040 and automatically selects a free loopback port when
that default (or the inherited demo port) is occupied. It never stops the existing
listener. Open the newly printed private session link; do not use an older session
URL. For a fixed port, run:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tests/start-milestone5b-demo.ps1 -Port 8050
```

An explicitly requested busy port fails before test-schema creation and asks for
another port. `-Verify` runs the owner-approved and delegated simulation checks and
exits instead of leaving the server running. The starter restores its caller's demo
port environment afterward. Demo/test Data Protection repositories live only in
memory; existing profile key files are neither read nor changed by those hosts.
All demo blockchain operations remain simulated and database writes stay inside
isolated test schemas. The live application tables are not migrated.
