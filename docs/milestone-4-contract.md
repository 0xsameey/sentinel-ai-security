# Milestone 4 local demo contract

The production gateway contract remains unchanged. Assessments now atomically
persist a `SecurityAssessmentRecorded` audit event with the assessment ID, stage,
actual decision, recipient, reason codes and context fingerprint.

The following routes exist **only in tools/Sentinel.Demo**, bound to loopback
port 8040. They are not registered in Sentinel.Api. Every `/demo-api` route
requires the random per-process demo owner bearer credential printed in the
startup URL fragment. No cookies or cross-origin access are enabled. The fragment
is removed from the address bar after loading. This credential authenticates a
simulated operator, not a real wallet. Owner JWT and worker credentials stay in
the tool process. Requests are serialized because fixture state is shared.

| Method / route | Behavior |
| --- | --- |
| POST /demo-api/run/{ALLOW,WARN,BLOCK,LIMIT} | Starts a scripted agent task through the real task API, returning its canonical assessment; LIMIT directly tests the gateway against an excessive adversarial proposal |
| POST /demo-api/approve/{assessmentId} | Explicit demo owner action, forwarded to real authenticated owner approval with exact fingerprint |
| POST /demo-api/execute/{assessmentId} | Revalidates, prepares, simulates chain authorization, assesses execution; either returns a new execution review or consumes a real permit and reports a simulated execution |
| POST /demo-api/finish/{assessmentId} | Execution-stage permit issuance/consumption after separate owner review; approval is independently enforced by the gateway |
| POST /demo-api/expire | Advances only the injected simulation clock three minutes |
| POST /demo-api/revalidate/{assessmentId} | Changes the fixture policy, attempts preparation, returns the actual rejection status, and restores the fixture policy |
| POST /demo-api/probes/{assessmentId} | Attempts assessment-only credential approval, forged WARN approval and unrecorded-payment execution; returns actual rejection statuses |
| GET /demo-api/audit | Latest 100 actual persisted events from the isolated schema |

Unknown scenarios return 400; missing/incorrect demo credentials return 401.
Approval/preparation failures preserve the real gateway HTTP status. Completed
demo results carry `simulated:true`, `signed:false`, `broadcast:false`, a
`SIMULATED_COMPLETED` state, actual tracking and permit replay rejection (409).
No endpoint imports a signer or broadcasts a transaction. Chain authorization,
completion and transaction proofs are explicitly synthetic local RPC responses.

Provider contract: `ModelProvider.next(input, AbortSignal)` returns normalized
`calls: {id,name,arguments}[]` and opaque continuation items. Agent-generated
results use `{type:'tool_result',callId,output}`; adapters translate to each SDK.
Only adapters know Responses/Messages payloads, tool-call formats and SDK options.
One shared agent owns all tool validation and gateway access. Provider failures
are sanitized `ProviderError` codes: `MODEL_TIMEOUT`, `MODEL_PROTOCOL_INVALID`,
`MODEL_AUTHENTICATION_FAILED`, `MODEL_RATE_LIMITED`, `MODEL_UNAVAILABLE`.
Configuration failures are explicit; providers/models are never silently changed.
