# Milestone 3 agent contract

The model can read policy/recipient context, propose one bounded payment, and
request an assessment. It cannot approve, prepare owner transactions, dispatch,
consume permits, sign, or select service URLs. No signing dependency is imported.

Authenticated gateway reads (owner JWT or assessment-only agent credential):
- `GET /api/v1/gateway/context/{recipient}`: bounded structured finalized policy,
  quotas, balance and supported recipient facts. Critical failure returns 503.

Owner JWT endpoints (gateway rate limit and 8 KB body limit):
- `POST /api/v1/agent/tasks`, GUID `Idempotency-Key`, body
  `{task,recipient,maxAmountYocto}`. Explicit recipient/ceiling are human constraints,
  separate from untrusted natural-language task text. One model proposal at most.
  The server checks current owner and supported recipient, persists a run claim,
  and invokes the isolated runner with its separate service credential. It retrieves
  the assessment from PostgreSQL through the gateway; runner/model decisions are
  never authoritative. Gateway assessment requests carry `X-Sentinel-Agent-Run`;
  the gateway checks persisted task constraints and atomically binds one assessment
  to that run using a unique PostgreSQL audit claim. A response cannot substitute
  another run's assessment. Replayed run IDs return 409 without another model call.
- `GET /api/v1/agent/tasks/{id}`: owner-only persisted sanitized run status/result.

Isolated runner: authenticated `POST /tasks` with
`{runId,task,recipient,maxAmountYocto}`; result `{runId,assessmentId}`. The runner
has only its service credential, assessment credential and provider credential.
The ASP.NET API never forwards its owner JWT. Neither process forwards credentials
to the model. Provider adapter uses official OpenAI SDK Responses function calls,
strict schemas, no parallel tools/retries, bounded turns/output/time.

The dashboard presents canonical gateway details, evidence, reasons and expiry.
WARN requires explicit authenticated owner approval and fresh revalidation through
existing Milestone 2 endpoints. ALLOW is assessment only; owner transaction
preparation is an explicit separate step. New agent UI stops at the unsigned owner
authorization descriptor. Live transactions remain blocked/unperformed.
