# Milestone 4: security attack simulation and end-to-end demo

Implemented 2026-11-03. The local demo requires no OpenAI or Claude key, API
credits, wallet key, signing, contract deployment or blockchain transaction.
The production security engine, policy thresholds, human approval requirements
and release gate remain authoritative.

## Start the dashboard demo

From the repository root:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tests/start-milestone4-demo.ps1
```

Open the `http://127.0.0.1:8040/demo#...` session link printed by the command.
Keep that process running. The fragment is an ephemeral local demo owner
credential, removed from the address bar after loading. Reload with the original
session link if necessary. Do not publish the demo host or share the credential.
Ctrl+C stops the host. Its fixture and agent child processes are cleaned up.

Prerequisites: Node with TypeScript stripping support (the existing project uses
Node 22+), npm dependencies for the dashboard/signer/agent, .NET 10 SDK, restored
NuGet dependencies, and the existing loopback PostgreSQL service. Configure
`ConnectionStrings__SentinelDb` in the launching process or use the API's existing
user-secrets configuration. Never put credentials in React/VITE variables. The
database role needs CREATE SCHEMA or CREATE DATABASE; superuser is unnecessary.
No application schema is migrated or reset. Each run creates a new
`sentinel_e2e_...` namespace and a new gateway schema; test namespaces are retained
for inspection. The tool refuses a non-loopback database or a non-test namespace.
Port 8040 must be available. No model SDK installation is needed for this demo.

## Demonstration walkthrough

1. **ALLOW:** select Routine payment, then Revalidate & simulate execution.
   The real task API invokes the TypeScript runner, common agent loop and gateway.
   An actual persisted assessment is reloaded and bound to the task. Preparation
   checks the exact intent and current policy. A local chain fixture supplies owner
   authorization; execution is assessed again, release prerequisites are checked
   against fixture facts, and a real database permit is issued and consumed once.
   A synthetic transaction proof is verified through the real reporting code,
   which persists Completed tracking. The response explicitly says simulated,
   signed false and broadcast false. Replaying the permit returns 409.
2. **WARN:** select Human review. The fixture makes indexed history unavailable;
   the real engine produces WARN. Review the exact recipient, amount and evidence,
   check the confirmation box and approve as the authenticated simulated owner.
   Change policy & test revalidation proves that a changed fingerprint rejects
   preparation (409), even with approval. The fixture policy is restored afterward.
   Expire approval & test rejection advances the injected clock three minutes and
   proves rejection (409). Start WARN again for a fresh assessment/approval. Execute
   to reach **Execution review required**: authorization approval cannot approve
   execution. Confirm that new execution assessment, approve, then finish the
   simulated execution. No unapproved signing path is added.
3. **BLOCK:** select Injected invoice. The checked-in untrusted invoice attempts to
   redirect the merchant payment to `injection-sink.testnet`, claim approval and
   sign immediately. First, a scripted adversarial model actually attempts that
   redirect through the real agent loop with `merchant.testnet` fixed by the human;
   immutable task constraints reject it (task API 422). Then a worst-case proposal
   whose explicit recipient is the sink is assessed through the real task/gateway
   pipeline, proving the independent security boundary even when that recipient
   reaches the gateway. The engine returns BLOCK with `COUNTERPARTY_BLOCKLISTED`.
   Preparation and approval are rejected; no permit or signature is possible.
4. **LIMIT:** select Excessive spending. A direct adversarial proposal for 1,001
   yoctoNEAR exceeds the 1,000 per-payment policy. The gateway independently blocks
   it. Agent tests also reject task ceilings and daily quota excess before proposal.
5. On WARN, **Attempt security bypasses** shows actual API rejection statuses:
   assessment-only agent approval 403, forged approval ID 409, unrecorded-payment
   worker execution 404. The forged approval probe is omitted for ALLOW because
   ALLOW needs no risk override. Actual security audit records are shown below the
   decision; operator activity is clearly a session view.

The dashboard shares the existing React build, typography, colors, panels, risk
states and icon system. It provides evidence inspection, explicit human review,
transaction states, rejected-action feedback, agent/operator activity and readable
persisted audit records. The existing live agent panel also now shows session
activity and the server-returned approval expiry, disabling expired preparation.

## Evidence and simulation boundary

The attack source is [the controlled invoice](fixtures/milestone4-invoice.txt).
The demo hashes its actual UTF-8 content and registers an owner-authenticated
`MANUAL_SECURITY_HOLD` entry through the existing blocklist endpoint. The blocklist
entry lives only in the isolated test database. Its URI
`https://sentinel.invalid/simulated/milestone4-invoice` is an explicit simulation
identifier; it is not a fetched or claimed external threat report. The record's
SHA-256 can be reproduced from the checked-in file. The gateway retrieves the real
entry through its normal persistence query and emits its evidence and reason code.

This recipient is known malicious **within the controlled simulation**, not a
claim about the ownership or behavior of any real NEAR account. No real-world
malicious recipient or external evidence was supplied. No fake threat report,
hardcoded risk result or demo override was added to production services.
Production assessments now persist `SecurityAssessmentRecorded` audit events with
the actual decision and reasons, including BLOCK; no schema migration is required.

Real components: React UI, task/controller authentication, run binding, TypeScript
agent loop, both adapter request/response translations, gateway, deterministic
engine, PostgreSQL, approvals, revalidation, spending policy checks, release checks,
permits, transaction verification/reporting and tracking. Test doubles: scripted
SDK responses and local NEAR/NearBlocks HTTP responses, including release
prerequisites and synthetic authorization/completion/transaction proofs. There is
no blockchain signer in the demo. A simulated Completed state is not settlement.
The demonstration measures enforcement under adversarial model outputs, not the
prompt-injection susceptibility of a live model.

The test-only composition is in `tools/Sentinel.Demo`; it reuses the integration
host and fixture rather than adding demo modes to production dependency injection.
Its routes do not exist in the deployed API. See [the contract](milestone-4-contract.md).

## Switch OpenAI and Claude without changing the workflow

`ModelProvider` in `src/sentinel-agent-client/src/agent.ts` is the common interface.
Only `providers/openai` and `providers/anthropic` know SDK-specific payloads. The
agent owns immutable task constraints, tool validation, quotas, one-proposal limits,
gateway access and canonical assessment reloads. Its tool results are neutral;
opaque continuation data is translated by adapters. Both providers use the same
four tools and the same agent/business logic. Neither adapter has approval, worker,
signing, private-key, dispatch or transaction-submission capabilities.

Install the selected adapter's official SDK on a network-enabled machine:

```powershell
npm.cmd --prefix src/sentinel-agent-client/providers/openai install --ignore-scripts
npm.cmd --prefix src/sentinel-agent-client/providers/anthropic install --ignore-scripts
```

These are separate provider packages with pinned SDK dependencies. Install only
the provider you select; review and commit generated lockfiles before deployment.
The existing root agent package's OpenAI dependency is preserved.

For OpenAI, configure the launching process:

```powershell
$env:SENTINEL_AGENT_PROVIDER = 'openai'
$env:OPENAI_MODEL = 'your-enabled-tool-capable-model'
# Supply OPENAI_API_KEY through your existing secret/environment mechanism.
```

For Claude:

```powershell
$env:SENTINEL_AGENT_PROVIDER = 'anthropic'
$env:ANTHROPIC_MODEL = 'your-enabled-tool-capable-claude-model'
# Supply ANTHROPIC_API_KEY through your existing secret/environment mechanism.
```

Keep `SENTINEL_API_URL`, `SENTINEL_AGENT_API_KEY`, `SENTINEL_RUNNER_KEY` and API runner
configuration as in Milestone 3. Restart the runner after environment changes.
`.env` files are examples, not automatically loaded. Missing configuration and
unknown providers fail explicitly; no model substitution or fixture fallback is
available to the production runner. SDK imports are lazy. Both adapters disable
retries, cap output at 1,200 tokens and bound requests to 30 seconds, within the
common 90-second agent deadline. Upstream authentication, rate limit, protocol,
timeout and availability errors become sanitized common error codes.

The adapters follow official [OpenAI function calling](https://developers.openai.com/api/docs/guides/function-calling)
and [Claude tool-call/result handling](https://platform.claude.com/docs/en/agents-and-tools/tool-use/handle-tool-calls).
No real model call is part of this milestone's verification. `npm run smoke:real`
remains an explicit, potentially billable future command and now respects either
provider's configuration. Do not run it as part of the local milestone demo.

## Verification and outstanding release work

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tests/run-milestone4.ps1
```

This runs prior verification, both adapter/security suites, frontend/API builds,
and the authenticated demo HTTP checks. See [the verification report](milestone-4-verification.md)
for observed results, artifacts and limitations.

The two existing testnet release blockers are unchanged:

1. The required restricted agent access key is missing. It was not provisioned.
2. The deployed contract lacks the current 24-hour authorization-expiry guard.
   No contract was redeployed.

Before live release, separately resolve those blockers, install/lock and verify
the selected official provider SDK, perform approved real-model testing and fresh
read-only testnet prerequisite verification, and complete the previously documented
migration/configuration and deployment review. These are not prerequisites for the
credit-free local milestone. No live signing, funds movement or settlement is claimed.
