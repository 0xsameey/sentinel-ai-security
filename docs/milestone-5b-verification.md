# Milestone 5B verification report

2026-11-04. Implementation complete for the local Milestone 5B demonstration.
Live deployment/execution is not authorized or verified. No later milestone started.

## Observed verification

Final runner: `tests/run-milestone5b.ps1`, exit **0**, marker
`MILESTONE5B_LOCAL_VERIFIED`; evidence:
`.test-artifacts/milestone5b-verified-final.log`.

| Check | Result |
| --- | --- |
| Rust contract, delegated adversarial cases and prior-state rehearsal | 35 passed |
| .NET unit/security | 40 passed |
| .NET HTTP/PostgreSQL integration | 21 passed |
| Provider-independent agent, OpenAI/Claude adapters and adversarial tests | 72 passed |
| Restricted signer security | 31 passed |
| Dashboard server-render/accessibility-label/auth-state checks | 3 passed |
| Unique automated tests | **202 passed; 0 failed; 0 skipped** |
| Agent TypeScript | Passed using the installed workspace TypeScript 7.0.2 compiler |
| Dashboard/auth-client builds | Passed |
| Dashboard type-aware lint | Passed |
| Updated local attack demo | Delegated and owner-approved verifiers passed |
| Optimized WASM | Rebuilt locally; matches hard-coded release pin |
| Database migration SQL | Generated idempotently; 19,656 bytes; not applied to application tables |

Signer tests run twice in the composite runner and are counted once. Demo assertions
are additional evidence, not additional counted unit tests. PostgreSQL tests use
new `sentinel_e2e_` schemas on the existing loopback PostgreSQL service, with search
paths excluding public. New isolated test schemas are retained. Existing application
payment/auth/audit tables and their migration history were not migrated or modified.

The configured VS developer shell plus `AWS_LC_SYS_PREBUILT_NASM=1` resolved the
previous Rust build blocker. A separate `initdb` attempt failed with Windows error
87; the established isolated-schema verification runner succeeded instead.

The first delegated integration attempt had an incorrect expected HTTP status for
revocation (409 versus actual BLOCK/422). A subsequent combined run caught shared
fixture key-method state leaking into the existing owner-approved test. Both test
setups were corrected; no production gate was weakened. Final pass counts refer to
the corrected suite, not those failed attempts.

## Implementation evidence

- Rust adds owner-only immutable delegations, explicit recipient sets, positive
  payment/budget/daily limits, expiry at most 24 hours and revocation. Configured
  agent rotation invalidates old authority. No delegation exists by default.
- Delegated execution atomically charges attempt budgets, invokes the original
  authorization policy and 24-hour guard, reserves funds and execution quota, marks
  Executing, and schedules the existing transfer/callback path. Payment-to-delegation
  bindings are stored separately. Payment IDs cannot be reused after any outcome.
- Failed callbacks release treasury reservations but retain delegation charges.
  Lost callbacks remain reserved. Callback replay is rejected. Revocation/pausing
  do not corrupt a callback already in flight. No withdrawal or reservation bypass
  was added; no manual timeout-based fund release was introduced.
- PostgreSQL stores delegation observations, immutable proposals, deterministic
  evidence/fingerprints, owner approvals, permit claims, attempted status and reports.
  A 20-second exact-intent permit is issued once and consumed once using conditional
  updates. Six concurrent consumption requests yield exactly one success.
- BLOCK has no signing path. WARN requires the current owner's wallet JWT, exact
  fingerprint and an unexpired human approval. Reassessment clears prior approval.
  Preparation and consumption read current finalized delegation and policy facts.
- The signer validates method, receiver, zero deposit, gas, every argument and the
  consumed permit, then attests the exact restricted key and rereads delegation.
  Owner-approved and delegated modes use separate signer processes/keys. Both old
  reviewed code and new reviewed code remain accepted for owner-approved execution;
  delegated execution requires the new artifact hash and dedicated public-key pin.
- OpenAI and Anthropic share one bounded tool loop. ALLOW dispatch runs outside the
  model. WARN/BLOCK stop automatic dispatch. Delegation selection is captured outside
  model instructions. The model has no key, shell, policy, approval or management tool.
  Agent startup refuses known signing and worker environment configuration names.
- The dashboard defaults to owner-approved mode and adds wallet-backed delegation
  creation/revocation, finalized inspection, limits, remaining budget, daily attempts,
  expiry, recipients, proposals, decisions, review and reconciliation. It uses the
  existing white/green components and inherited typography.

The database-backed tests exercise the real API, PostgreSQL, actual TypeScript agent
loop/provider adapters and restricted executor, using explicitly simulated submission
and NEAR RPC. They establish ALLOW autonomy without a payment authorization record,
WARN owner review, malicious-recipient BLOCK, forged fields/approvals, management
access denial, quota/expiry/revocation/rotation, restricted-key/artifact gates, permit
replay/concurrency, missing/failed callbacks, recovery observations and owner-approved
regressions. They do not establish real NEAR VM execution or live model behavior.

## Storage compatibility and upgrade rehearsal

The root `Sentinel` and `Payment` Borsh layouts and existing p/a/d collection prefixes
are unchanged. Added records use separate versioned namespaces. The offline rehearsal
serializes an independently declared pre-5B policy-state layout, reads it using the
new contract, verifies root bytes, preserves all five payment statuses and pending
funds, completes an old in-flight callback, and creates a delegation afterward.
It also proves that the incompatible five-field legacy decoder rejects this state.
No call to `migrate`, `new`, or STATE overwrite on a deployed contract was attempted.

The actual deployed state/WASM provenance remains unestablished. Do not deploy until
its snapshot and prefixes match the reviewed layout and an isolated snapshot-based
code replacement rehearsal has passed. The offline rehearsal is not a real-chain
migration or receipt-rollback VM test. Unknown compatibility remains a hard stop.

Artifact provenance: `docs/milestone-5b-artifact.json`.
Corrected WASM SHA-256:
`3fdb4299956bbd008fe4851bf0dede4354253c2f4c9b9d0a3974d6010d9ae645`.
NEAR code hash: `5JGbnZu5NUppt922dqjtAH6oTixkVkXEKcynUqjjHTyE`.
Generated migration: `20261104095505_DelegatedSpending`, adding two tables/indexes.
Review `.test-artifacts/milestone5b-migrations.sql` before any approved application.

## Unverified / blocked separately from skipped tests

There are **no skipped automated tests in the final full run**. These external checks
were not performed and are not counted as passing tests:

- Real Claude inference: pinned official SDK installation fails fetching npm with
  `EACCES`; no live provider request was made. Both provider adapters passed SDK-double
  tests, including the complete HTTP agent-to-signer workflow. Normal PowerShell:

  ```powershell
  Push-Location .\src\sentinel-agent-client\providers\anthropic
  npm install --ignore-scripts
  Pop-Location
  ```

- The agent package's own TypeScript native-platform dependency is absent. The same
  TypeScript version installed for the signer checked the agent successfully:
  `node src/sentinel-signer/node_modules/typescript/bin/tsc --noEmit -p src/sentinel-agent-client/tsconfig.json`.
- Real deployed-state snapshot rehearsal, NEAR receipt rollback/gas behavior, real
  signing, transaction submission and settlement await separately approved scope.
- Native browser inspection confirmed the local owner-default dashboard, white/green
  shell and accessible mode control. Switching modes hit a native control timeout
  and an unrelated overlay; authenticated, mobile and console visual QA are not
  claimed. Component renders, type checking, production build and lint passed.
- Postman CLI workspace storage/account setup remains unavailable. The repository's
  same Postman mock handler ran through its established local Node fallback. No
  workspace publication or account setup was performed.

## Deployment prerequisites and demo

See `docs/milestone-5b.md` for the exact authority model, API, migration gate, threats,
key separation, recovery limits and deployment prerequisites. See
`postman/delegations.openapi.json` for the additive API contract.

Run `tests/start-milestone5b-demo.ps1` for the labeled local security lab. It uses an
isolated test namespace and an ephemeral simulated-owner session. ALLOW completes
without owner payment authorization; WARN requires explicit simulated owner review;
BLOCK, budget exhaustion and revocation never consume permits. Failure evidence and
permit replay are visible. No real signer or broadcast is used by the demo.

Before live use, establish deployed-state compatibility, review/apply the database
migration only with approval, approve the contract upgrade, provision separate finite
restricted keys only with approval, configure the dedicated delegated release key
and dispatcher, install the official model SDK, and perform separately authorized
read-only checks and a capped live demonstration. No live transaction, deployment or
signing-key change was performed during this implementation.
