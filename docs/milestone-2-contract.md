# Security Gateway contract (Milestone 2)

All amounts/nanosecond timestamps are canonical decimal strings. Requests reject
unknown fields. Purpose is untrusted data bound into the intent hash, never a
source of policy instructions. No LLM is called by this gateway.

| Route | Authentication / effect |
| --- | --- |
| POST `/api/v1/gateway/assess` | Owner wallet JWT or separate `X-Sentinel-Agent-Key`; persists exact intent/evidence; returns ALLOW/WARN/BLOCK |
| GET `/api/v1/gateway/assessments/{id}` | Configured owner/agent only |
| POST `/api/v1/gateway/payments/{id}/assess-execution` | Owner/agent; requires a recorded, matching on-chain authorized payment |
| POST `/api/v1/gateway/assessments/{id}/approve` | Owner wallet JWT only; `{fingerprint,confirm:true}`; WARN only, revalidate before granting |
| POST `/api/v1/gateway/assessments/{id}/prepare` | Owner wallet JWT, GUID Idempotency-Key; `{approvalId?}`; revalidate, consume WARN approval for this exact intent |
| POST `/api/v1/gateway/blocklist` | Owner wallet JWT; evidence URL/hash and explicit attestation required; append-only records |
| POST `/api/v1/gateway/blocklist/{id}/revoke` | Owner wallet JWT; preserves evidence/audit trail |
| POST `/api/internal/agent/payments/{id}/consume-permit` | Worker key only; atomically consumes exact execution permit after revalidation |

Legacy payment preparation remains owner-only and runs the same gateway. Legacy
execution preparation retains the direct ContractCall shape but adds mandatory
`security: {permitId,intentHash,expiresAtUtc,signerPublicKey}`. The signer must consume it through
the API immediately before signing. There is no bare-call signing fallback.

Critical RPC, blocklist/database, pause, recipient and hard-limit failures BLOCK.
Indexed history unavailable/empty, deployed recipient code, near-limit amounts
and a high failure ratio in a bounded recent history sample WARN. Only a valid
owner approval can authorize WARN; BLOCK cannot be overridden. Approvals bind
owner/contract/intent/stage/evidence fingerprint and expire within two minutes.
Changed evidence, limits, quota, balance or purpose invalidate prior approval.
Approval/permit claims are atomic in PostgreSQL. Consumed permits never auto-reset.

Both known testnet release blockers remain mandatory at permit issue/consumption:
reviewed code hash must implement the current expiry cap and the configured
agent public key must be present and narrowly restricted with enough gas. Tests
use synthetic RPC responses only in test composition; the production release gate
itself is exercised. No production bypass flag.
