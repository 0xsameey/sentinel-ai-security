# Milestone 2 — Sentinel Security Gateway

Milestone 3 adds a bounded model-provider adapter, owner task API and dashboard
review flow on these same enforcement interfaces. See [Milestone 3](milestone-3.md)
for local verification and the unresolved live-provider environment blockers.

Implemented locally on 2026-11-03. No LLM integration, deployment, key provisioning,
live signing or transaction submission was performed. Contract source and storage
layout are unchanged. The two Milestone 1B release blockers remain mandatory.

## Architecture and enforcement

The assessment-only TypeScript client sends strictly structured payment proposals
to the ASP.NET gateway. Authenticated owner wallet sessions or a separate agent
credential may assess; only the current owner wallet JWT may approve WARN or
prepare owner authorization. Wallet authentication already requires an on-chain
FullAccess owner key and an atomically consumed NEP-413 challenge. No API credential
is generated or installed by this change.

The gateway reads finalized NEAR testnet state at one block: contract authorities,
pause, recipient allowlist, policy, daily authorization/execution/in-flight usage,
available treasury balance, recipient existence/code and contract code hash. A
bounded real NearBlocks v3 transaction sample supplies historical evidence. A
PostgreSQL blocklist records owner-attested evidence URL, SHA-256, reason, actor,
time and optional expiry; revocation preserves the record and audit event.
There are no synthetic signals or production test-mode switches. Blocklist
evidence is an accountable human attestation; the service does not independently
fetch or certify the supplied document.

`DeterministicRiskEngine` returns ALLOW, WARN or BLOCK with stable reason codes,
source evidence, observation/assessment times, expiry and a context fingerprint.
Hard restrictions, insufficient quotas/balance, missing recipient, active
blocklist or unavailable critical chain data deny. Missing/empty indexed history,
recipient contract code, a high recent failure ratio, or amounts at least 80% of
the per-payment limit require review. History is a sample of at most ten recent
transactions, not proof of account age or absence of malicious activity. Scores
0/50/100 are categorical labels, not calibrated probabilities.

Purpose text is bound to the exact intent hash but is never interpreted as policy.
No model, AI explanation or caller-supplied decision participates. Unknown gateway
JSON fields are rejected; amounts and nanosecond expiries use canonical bounded
decimal strings. Expiry must be in the next 24 hours. Legacy leading-zero amounts
are normalized before the same validation and security checks.

WARN requires explicit owner confirmation of a fingerprint. Approvals bind the
owner, contract, exact intent, stage and evidence/policy context, expire within
two minutes, and are claimed atomically for one payment. Changed or expired
references fail. Authorization and execution are separate stages: a review for
owner authorization cannot authorize a later WARN execution. BLOCK has no human
override. Existing payment preparation and dispatch use the same gateway.

Execution requires a persisted approved intent, its original assessment and any
consumed authorization approval, plus an exact matching on-chain Authorized
payment. Direct worker requests and chain-only authorizations cannot bypass this.
The gateway revalidates risk and release prerequisites when preparing and consuming
a 20-second single-use permit. The permit binds the actual expected signer public
key. The signer authenticates to the API, consumes that permit before its submit
adapter can run, checks decision and any human approval, and accepts only
`execute_payment({id})`, zero deposit and 30 Tgas to the configured contract.
The production signer independently checks testnet and its actual key's narrow
scope/allowance immediately before signing. There is no bare-call fallback.

The release gate has no bypass flag: the deployed code hash must match the reviewed
optimized artifact containing the 24-hour expiry guard, and the configured key
must be a finite-allowance FunctionCall key restricted to this contract and exactly
`execute_payment`. Missing, FullAccess, wildcard, other-method or insufficient
allowance keys fail closed. The Rust contract continues to enforce owner
authorization, pause, allowlist, limits, expiry, cancellation and replay protection.

Consumed permits cannot be reissued automatically after an ambiguous timeout.
Existing verified transaction reporting/sync remains available for reconciliation
without another submission. A pre-submission crash after permit consumption also
requires operator investigation; liveness is deliberately sacrificed to prevent
automatic duplicate signing.

## API, configuration and persistence

See [the gateway API contract](milestone-2-contract.md) and
[the future agent client](../src/sentinel-agent-client/README.md).

New secret configuration names are `AgentGateway:ApiKey` (assessment credential,
Base64 encoding of at least 32 random bytes) and optional `CounterpartyIndex:ApiKey`.
Use a secret store; never put either in model context or browser code. Existing
worker/dispatch credentials remain separate. `SecurityRelease:AgentPublicKey` is
public configuration, and must match the actual isolated signer key. No existing
settings or credentials were changed.

`CounterpartyIndex:BaseUrl` defaults to `https://api-testnet.nearblocks.io/`; only
HTTPS without embedded credentials/query is accepted. RPC/index timeouts are
bounded and response buffers capped at 1 MB. An unavailable index produces WARN;
an unavailable database prevents intent/permit issuance. Gateway routes have an
8 KB request limit and per-IP fixed-window rate limiting (default 30/minute,
configuration bounded to 1–300); worker routes are limited to 120/minute. Reverse
proxy trust and distributed rate limiting require deployment-specific review.

Migration `20261103102602_SecurityGateway` adds assessments, human approvals,
evidence-backed blocklist and execution permits, plus payment security references.
It was exercised only against newly created isolated test schemas. Existing public
application tables were neither migrated nor reset. Review and apply this migration
through the normal environment-specific process before running the updated API
against existing data. Old payments without gateway provenance require review and
cannot be executed automatically through the new boundary.

## Verification

Reproduce with:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tests/run-milestone2.ps1
```

The runner reads the existing local PostgreSQL connection internally from the
existing secret configuration, creates fresh `sentinel_e2e_*` schemas, asserts
isolation without public fallback and retains the schemas. It does not initialize,
delete or overwrite an existing cluster/database. Cached dependencies and the
existing Windows Visual Studio C++ environment are required; the Rust wrapper uses
the process-local prebuilt NASM fallback.

| Local check | Result |
| --- | --- |
| .NET unit tests | 40 pass |
| .NET integration tests, including real PostgreSQL | 10 pass, zero skipped |
| TypeScript signer tests | 25 pass |
| Structured agent client tests/typecheck | 5 pass / typecheck pass |
| Read-only diagnostic logic tests | 18 pass |
| Rust contract unit tests | 10 pass |
| React dashboard and auth client builds | Pass |

Integration coverage includes blocklisted/malicious recipients, missing indexed
history, RPC failure, pause/limits/in-flight quotas/balance, missing/unrestricted
keys and unreviewed deployed code, agent attempts to approve, forged JWTs and
approval fingerprints, replay/expiry/context changes, worker bypasses, atomic permit
consumption, HTTP rate limits, and the existing payment/recovery workflow.
Signer tests assert zero submissions for denied/malformed/missing permit paths.
Untrusted instruction text cannot alter the deterministic decision.

NEAR RPC/index responses and the signer submission adapter are synthetic **only in
tests**. PostgreSQL, migrations, API middleware, production intelligence parsers,
risk engine, release gate and C#↔TypeScript HTTP handoff run locally. These tests do
not establish current live network availability or successful live payment signing.
NuGet vulnerability-feed access remains unavailable in this environment (NU1900);
the dashboard build retains an upstream wallet dependency direct-eval warning.
The Postman CLI could not access its local storage in this environment; the Node
fallback served the same repository mock handler. No Postman-hosted run or
workspace publication is claimed.

The complete runner log is `.test-artifacts/milestone2-verification.log` (exit 0).
The final focused credential-redaction check increased the unit total from 39 to
40; its passing suite is recorded in `.test-artifacts/milestone2-redaction.log`
(exit 0). No other runtime changes followed verification. A
read-only reconciliation check of test schema
`sentinel_e2e_20261103105205_c7a19860` found nine tables, six applied migrations,
two Completed payments, four Verified transaction attempts and fourteen audit
events. These are synthetic-payment test records only.

## Mandatory release blockers and review decisions

1. The configured agent public key remains absent on `0xsameey-agent.testnet`.
   A separately approved key correction must create the narrow FunctionCall scope
   with an adequate finite gas allowance. No key was created, rotated or revoked.
2. `0xsameey-sentinel-v2.testnet` still lacks the current 24-hour authorization
   expiry guard. A separately approved reviewed upgrade is required. The gate pins
   reviewed optimized SHA-256
   `34d9ad95449b50a9ad4005d14a950b9d79b2b677ac5588aefc8640f61d15784f`.
   No contract was deployed. Reproducibility/differential evidence remains in
   [Milestone 1](milestone-1.md).

Before any live verification, also review/apply the database migration, configure
separate secrets and the public signer key, verify the real index response and
availability, refresh all account/policy/balance/key/code evidence, and obtain
explicit approval for the proposed 0.001 NEAR payment. Local fixtures are not live
prerequisite evidence.

The selected review thresholds and TTLs are conservative defaults for product
review. WARN approval is currently an API workflow; the dashboard displays WARN
and retains its ALLOW flow but has no dedicated review/approval screen yet.
No autonomous spending authority was added: every payment still requires an owner
on-chain authorization. Removing that requirement needs a separate contract design
and explicit review, outside Milestone 2.

Security assumes trustworthy API/database, RPC/index providers, synchronized clocks,
owner session and isolated signer process. Anyone controlling the signer key or
changing the signer binary is outside the off-chain gateway boundary; the restricted
key and contract still constrain their on-chain authority. Risk/blocklist changes
between final revalidation and inclusion cannot be made atomic off-chain. Pause,
allowlist, quota and expiry are rechecked on-chain; off-chain risk evidence is not.
If atomic on-chain risk attestation is required, that needs a separately reviewed
contract change. No such change is included here.
