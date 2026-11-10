# Milestone 4 verification report

Date: 2026-11-03. Scope: local security enforcement and deterministic end-to-end
demo. No model calls, blockchain signatures, submissions, key provisioning or
contract deployment were performed.

## Observed results

| Check | Result |
| --- | --- |
| .NET security/application unit tests | 40 passed, zero skipped |
| .NET HTTP/PostgreSQL integration tests | 13 passed, zero skipped |
| Agent, client, runner and provider tests | 55 passed |
| Isolated signer security tests | 25 passed |
| Read-only diagnostic tests | 18 passed |
| Rust contract tests | 10 passed |
| Agent TypeScript check | Passed |
| API, dashboard and existing auth-client builds | Passed |
| Demo host build | Passed |
| Demo HTTP verifier | Passed; emitted `MILESTONE4_DEMO_VERIFIED` |
| Interactive browser visual review | Incomplete: browser automation host unavailable; native Computer Use stopped because it could not confidently determine the current Chrome URL |
| Official Claude SDK installation / SDK transport | Unverified: npm download denied with EACCES |
| Live OpenAI/Claude responses and NEAR settlement | Not attempted |

There are **161 distinct passing tests**, plus the dedicated demo HTTP verifier.
The prior suite repeats the five client tests; these are counted only once.
Local artifacts (gitignored) are `.test-artifacts/milestone4-final.log`, `.test-artifacts/milestone4-verified.log`,
`.test-artifacts/milestone4-demo.log`, `.test-artifacts/milestone4-baseline.log`
and `.test-artifacts/milestone4-ui.log`. The UI log contains an ephemeral demo
session URL; do not publish it. It is not a model/service/wallet credential.

The final full run reached the script's explicit `exit 0`, all success markers,
and `MILESTONE4_DEMO_VERIFIED`. The tool's outer PowerShell redirection nevertheless
reported 1 when native commands wrote warnings to stderr. A minimal child that
only writes stderr and exits 0 reproduced this reporting behavior; explicitly
reading its native `$LASTEXITCODE` returned 0. The wrapper scripts explicitly exit
0 after success and throw on failed child checks. This tool/logging artifact is
separate from test failures; the actual assertions and demo verifier all passed.

## Security evidence

- The controlled invoice is hashed from the checked-in file, entered through the
  authenticated blocklist endpoint and read by the real gateway from PostgreSQL.
  The attack fixture is isolated from the production database and dependency graph.
  BLOCK includes `COUNTERPARTY_BLOCKLISTED`, the actual entry source and file digest.
- A dedicated integration test verifies BLOCK preparation refusal, owner override
  refusal, persisted `SecurityAssessmentRecorded` and `CounterpartyBlocklisted`
  events, and absence of execution permits. It enables the recipient in fixture
  facts to isolate blocklist enforcement from a separate allowlist failure.
- Both real provider adapters, using SDK method doubles, drive the same agent
  loop through the real HTTP task API/gateway/PostgreSQL flow for ALLOW, WARN and
  BLOCK. Canonical run binding, task replay protection and forged-runner result
  rejection remain covered. Neither model output nor adapter chooses the decision.
- Adapter tests reject injected recipient changes, forged approval fields,
  unauthorized signing tools and excessive task amounts for both providers. They
  verify tool-result translation, sanitized errors, aborted/hanging calls and
  incomplete-response rejection. No provider-specific agent workflow exists.
- The demo's BLOCK sequence additionally executes an adversarial redirect against
  a fixed merchant task (422), then tests the independently blocked sink proposal.
  The scripted model is explicitly not evidence of live model behavior.
- ALLOW prepares exact owner authorization, models chain authorization, rechecks
  execution/security-release facts, issues and consumes a real permit, rejects
  permit replay (409), verifies a synthetic transaction proof through the real
  reporting endpoint, and persists Completed tracking. No signer is instantiated.
- WARN requires actual owner JWT authentication plus exact fingerprint approval.
  Assessment-only approval returns 403, forged approval ID 409, unrecorded worker
  execution 404, expired approval/preparation 409, and changed-policy preparation
  409. Authorization and execution require separate approvals. Both stages complete
  only after their own approval and revalidation in the local fixture workflow.
- Existing tests still reject per-payment/daily limits, insufficient balance,
  changed authority, unavailable critical RPC data, missing/unrestricted keys,
  unreviewed code, repeated signing claims and direct gateway bypasses.

## Limits and release blockers

The dashboard is build-verified and its backing routes are exercised over HTTP;
interactive visual QA was stopped by the Computer Use URL-policy failure. No
automated screenshot or visual-completion claim is made. The native browser was
opened only to the local demo; UI automation ceased when the tool stopped it.

The official Claude adapter imports `@anthropic-ai/sdk` lazily from its isolated,
pinned provider package. Package installation, a reviewed generated lockfile and
real SDK/model transport remain future verification. No fabricated lockfile or
SDK was substituted. OpenAI remains available through its existing official SDK
adapter. Neither installation limitation affects credit-free deterministic tests.

The local RPC fixture is the existing repository-owned Postman NEAR handler served
by the Node fallback. Postman 1.83.1 reports signed out/uninitialized and cannot
check its skill storage (EACCES); hosted owner search, Orbit/context discovery and
workspace publication were not used. No Postman-hosted verification is claimed.
The HTTP tests run the real application against the local stand-in, not a mock
of Sentinel. The handler records the dependency requests; tests inspect RPC calls.

Existing NuGet vulnerability-feed access warnings, redundant package warnings,
wallet bundle size/browser externalization and upstream `eval` warnings remain.
Build success is not a dependency vulnerability audit. The demo pins the EF
Relational version to match the integration host and avoid an assembly conflict.

The two testnet release blockers remain unchanged: **missing restricted agent
access key** and **deployed contract missing the current authorization-expiry
guard**. The demo's release prerequisites are fixture facts only. No production
release check was weakened. Live model susceptibility, SDK/network integration,
live signing and NEAR settlement require separately authorized future work.

Reproduce with `tests/run-milestone4.ps1`; launch the dashboard with
`tests/start-milestone4-demo.ps1`. See [setup and walkthrough](milestone-4.md) and
[the local contract](milestone-4-contract.md).
