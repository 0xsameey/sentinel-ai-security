# Dispatch 409 follow-up

Read-only investigation of run `d88fb189-497c-44aa-97ff-7f070ea9da41` found a
DelegatedAgentRunStarted audit event, without a bound proposal. The earlier 502
did not establish a model failure; the runner was reachable on 8021 during the
later inspection. That run was not retried.

Later payment `9e1c84dc-4ddf-4734-8ff0-1bdc557032d7` exists and was ALLOW /
Proposed, with no permit, consumed permit or reported transaction as inspected
2026-11-08 at 18:00:38 UTC. Its amount is **0.000001 NEAR**, not 0.001 NEAR.
Do not execute it as the requested 0.001 payment. Its original task text was not
available from the audit hash, so this does not establish whether the new exact
amount constraint was applicable or whether the runner was restarted.

Safe Node GET probes found API HTTP 5156 healthy (200), HTTPS 7201 refused
(`ECONNREFUSED`), runner 8021 alive (401 without credentials). The signer's saved
SENTINEL_API_URL uses HTTPS 7201. This is a confirmed saved-configuration/listener
mismatch and explains how preparation can fail before a permit is issued.
The running signer's environment overrides and historical stderr were not
available, so its exact historical internal exception remains unproven.

Added explicit loopback HTTP opt-in to the delegated signer launcher (`-HttpApi`)
and signer configuration. NEAR RPC must still be HTTPS. Remote HTTP, embedded
URL credentials, queries and fragments remain rejected. No TLS verification is
disabled. The signer now reports safe stage/transport codes, and the API passes
only allowlisted codes (never arbitrary worker payloads). Connection-refusal
regression verifies zero submit calls. Signer suite: 32 passing tests; API builds
successfully in isolated artifacts; PowerShell launcher parses successfully.

After stopping/rebuilding owned processes, keep local endpoints consistent:

```powershell
# API terminal
powershell -NoProfile -ExecutionPolicy Bypass -File tests/start-real-api.ps1 -HttpOnly
# Runner terminal
powershell -NoProfile -ExecutionPolicy Bypass -File tests/start-real-agent.ps1 -HttpApi
# Read-only signer check — does not start or sign
npm.cmd --prefix src/sentinel-signer run build
powershell -NoProfile -ExecutionPolicy Bypass -File tests/start-real-delegated-signer.ps1 -HttpApi -CheckOnly
```

The previously detected signer/public-pin mismatch is still blocking. Resolve
using the reviewed, existing restricted credential; no key/pin was changed here.
Only after successful attestation and explicit live-payment approval may the
signer be started by omitting `-CheckOnly`. Do not retry the old payment blindly.
Use a fresh exact-amount proposal only after reviewing its displayed amount and
obtaining the required one-payment approval; delegated ALLOW can auto-dispatch.

No task, permit request or dispatch was sent by this investigation. All probes
were GETs; database access was read-only. No credentials, policies or keys changed.
