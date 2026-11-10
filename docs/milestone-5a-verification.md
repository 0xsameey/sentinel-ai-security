# Milestone 5A verification report

2026-11-03. **Sentinel is verified for the labeled local security demonstration.
It is not yet verified for the full live Claude + restricted NEAR key payment
demonstration.** No live-changing approval-gated action was executed.

## Observed test results

Final command: `tests/run-milestone5a.ps1`, native exit **0**, success markers
`MILESTONE4_DEMO_VERIFIED` and `MILESTONE5A_LOCAL_VERIFIED`.

| Check | Observed result |
| --- | --- |
| .NET unit/security | 40 passed, 0 skipped |
| .NET HTTP/PostgreSQL integration | 13 passed, 0 skipped |
| Agent/provider/adversarial/client | 55 passed |
| Isolated signer security | 25 passed |
| Read-only diagnostics | 18 passed |
| Rust contract | 15 passed, 0 failed |
| Distinct tests | **166 passed**, up from 161 |
| Agent TypeScript check | Passed in complete suite |
| API, dashboard, auth-client, demo builds | Passed |
| Dashboard type-aware lint | Passed, no reported warnings after header fix |
| Demo HTTP verifier | Passed: real Sentinel API + isolated PostgreSQL + dependency fixtures |
| Corrected WASM | Built and optimized locally; hash matches prior corrected artifact |
| Migration SQL | Generated successfully, idempotent, 16,884 bytes; not applied to application |
| Application migration history | Read-only inspected; SecurityGateway is missing |
| Official Anthropic SDK install | Blocked: npm registry fetch EACCES |
| Account model discovery / inference | Blocked by missing API key and SDK |
| Fresh testnet read-only RPC | Blocked: EACCES; no fresh live-state result |
| Interactive visual/responsive/browser-console QA | Incomplete; browser host unavailable, native review interrupted by detected user input |
| Live authorization/execution/settlement | Not performed; approval required |

The five client tests are repeated by the baseline scripts and counted only once.
The dedicated HTTP verifier is additional evidence, not another counted unit test.
Final evidence: `.test-artifacts/milestone5a-final.log`. Earlier attempts failed
because port 8040 and Debug demo outputs were in use; final verification used a
separate port and build configuration and completed successfully. Those earlier
failures are not presented as successful runs.

## Requirement-by-requirement outcome

| Requested work | Evidence and remaining boundary |
| --- | --- |
| 1. Complete ALLOW/WARN/BLOCK demo | Passed via actual HTTP task/gateway routes; scripted models and chain fixtures |
| 2. Dashboard inspection | Source reviewed; header handling, focus, wrapping and missing states corrected; visual/mobile/console gate open |
| 3. Approval, expiry, status/history/audit | HTTP verifier + integration tests pass; UI status/history still needs interactive sign-off |
| 4. Simulation/live distinction | Existing explicit simulation badges, signed=false/broadcast=false retained; no settlement claim |
| 5. Official SDK | Existing pinned package retained; normal npm install attempted and denied EACCES; no substitute SDK/lockfile |
| 6. Environment/account model | Selected claude-haiku-4-5-20251001; key absent in process and user environment; model access not verified |
| 7. Minimal real smoke | Executed preflight; failed at configuration before model request; diagnostic correctly says anthropic |
| 8. Natural-language proposal/assessment | Existing shared workflow tested with provider method doubles; real Claude transport remains blocked |
| 9. Real adversarial prompts | Not run against a real model; deterministic adversarial coverage passes; case matrix prepared |
| 10. Provider independence | Both adapters retain common agent/tools/gateway; no provider-specific signer or approval route |
| 11. Contract protections | Source inspected; 15 Rust tests pass; new 24-hour/expiry/caller/pause regressions added; no audit claim |
| 12. Corrected deployment | Local build script, manifest, exact artifact and compatibility/rollback plan prepared; not deployed |
| 13. Restricted key | Exact receiver/method/0.005 NEAR allowance proposed; no generation or registration; public key pending |
| 14. Negative key capabilities | Local permission/signer guards tested; protocol-level sandbox/live negative probes still unverified |
| 15. Database migrations | Generated SQL, inspected history; integration tests migrated isolated namespaces; application update held for approval |
| 16. 0.001 NEAR payment | Amount/recipient/gas/fee/expiry proposal prepared; exact payment ID and timestamp pending fresh preparation/approval |
| 17. Owner→agent→reconciliation/audit | Verified against synthetic chain proofs with actual database; no live hashes/receipts exist |
| 18. Complete AI security demo | Scripted ALLOW/WARN/BLOCK passes; real AI + testnet combination blocked |
| 19. BLOCK never reaches signer | BLOCK cannot prepare/override or create a permit in HTTP tests; signer independently rejects invalid permits; no signer is instantiated in demo. Live signer-call trace remains open |
| 20. Relevant tests | Complete local runner passed 166 distinct tests, builds, typecheck and lint |

## Security and compatibility findings

The source already enforces authorization expiry strictly in the future and at
most 24 hours away. Added tests accept exactly 24 hours, reject 24 hours + 1ns,
reject execution exactly at expiry, reject unrelated callers and reject execution
while paused. Existing owner authorization, immutable payment details, allowlist,
per-payment/daily quotas, checked arithmetic, balance/storage reserve, one-use
status and private callback accounting remain intact. This is scoped source and
unit-test evidence, not comprehensive formal verification or chain runtime QA.

The new tests change no compiled contract behavior. Fresh optimized corrected
WASM SHA-256 is `34d9ad95449b50a9ad4005d14a950b9d79b2b677ac5588aefc8640f61d15784f`.
Source SHA-256 is `db9a604faba40617bd66f70cdd7de54d473405d01db6f7e2b6a6988349791d48`.
Manifest: `.test-artifacts/milestone5a-release-2820b6f21e514705a08abf6167c4e97f/manifest.json`.
Earlier differential evidence showed deployed bytes lack the expiry ceiling;
this session's fresh RPC failed, so current deployment status is not asserted.
No current-layout migration is needed for the expiry-only upgrade. Legacy
`migrate` is unsafe as an automatic current-layout upgrade step.

Read-only application history ends at AddPaymentRiskAssessment. Required pending
migration is `20261103102602_SecurityGateway`. Prepared idempotent SQL SHA-256:
`8d2c29bf18bea3172f34f2695f744db960b9af3ad88598e87d9da8b554b7a4f5`.
The application database has not been migrated. No data/policy/allowlist control
was weakened. Only isolated test namespaces were created/migrated by verification.

Model output never overrides deterministic decisions. LLM adapters have no key,
permit, owner approval or signing capability. Error diagnostics redact exceptions
and now identify the selected provider accurately. No private key, API key,
worker credential or signing permit is included in this report. Local raw demo
logs can contain ephemeral owner session links and internal test traffic; do
not publish `.test-artifacts` wholesale.

## Blockers and exact next actions

1. Install the official SDK in a network-enabled normal local environment; review
   generated lockfile. Supply ANTHROPIC_API_KEY securely. The user's selected
   model is recorded but not authenticated against the API account. Run model
   discovery, one-call smoke, natural-language assessment and real adversarial
   cases. No real-model pass is claimed from a configuration failure.
2. Complete visual/mobile/browser error QA in an available browser session.
   Source/build/lint and backing HTTP tests close different verification gates.
3. Review and separately approve the exact SecurityGateway application migration.
4. Obtain fresh finalized testnet facts, rehearse state compatibility and separately
   approve corrected artifact deployment. Do not invoke new/legacy migrate.
5. Separately approve secure key generation and registration bound to its exact
   public key, receiver/method and finite allowance; verify actual restrictions.
6. Prepare an exact payment ID/expiry; separately approve owner authorization and
   restricted-agent execution, then collect final receipts and database evidence.

The [expanded milestone and action proposals](milestone-5a.md) specify accounts,
network, expected changes, risks and rollback. Missing final parameters are
explicitly marked; no blanket approval is requested or inferred. Until those
gates close, **live hackathon demo readiness is blocked**.

## Tooling limitations

Postman CLI 1.83.1 is signed out/uninitialized and reports EACCES checking skill
storage. Hosted owner discovery, Orbit and Context Graph were not used. Existing
repository-owned Postman NEAR handler ran through the Node fallback as in the
baseline; no hosted Postman simulation success is claimed. NuGet vulnerability
feed connectivity, redundant package warnings and wallet dependency bundle/eval
warnings remain. Build success does not establish dependency-audit cleanliness.
EF tools 10.0.3 warned that runtime is 10.0.14; SQL generation succeeded.
