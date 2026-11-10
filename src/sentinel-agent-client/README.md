# Sentinel payment agent

`src/agent.ts` runs structured policy, recipient, proposal and assessment tools.
Provider configuration is isolated in `src/provider.mjs`;
`providers/openai/runtime.mjs` imports the official OpenAI SDK. Production has no
fixture fallback or signing dependency. Node 22.18+ is required.

From the repository root, run `tests/run-milestone3.ps1` using PowerShell with a
process-local execution-policy bypass if required. The existing signer compiler
and Node types are reused for typechecking.

For the real provider, enter `providers/openai`, run `npm install --ignore-scripts`,
then return to this package. The SDK version is pinned. SDK download and provider
lockfile generation must succeed on a network-enabled machine; this restricted
session could not download npm packages. Fixture tests do not claim a real SDK run.

Configure process environment variables using `.env.example` as a name reference.
No `.env` file is automatically loaded and no credentials are provisioned. Choose
a tool-capable model enabled for your project using `OPENAI_MODEL`.

- `npm start`: real OpenAI-backed loopback runner.
- `npm run smoke:real`: exactly one minimal model call, no gateway/NEAR access.
- `npm run demo:real`: real model proposes and assesses through Sentinel; stops
  before human approval, owner authorization and signing.
- `npm run demo:fixture`: explicit synthetic ALLOW/WARN/BLOCK demo, no network.
- `npm test`: deterministic adversarial and runner HTTP tests.

Run `npm run smoke:real` in the same PowerShell terminal where the API key is
configured, with `$env:OPENAI_MODEL` set to your project's enabled Responses API
model with function calling. The smoke call has a 30-second deadline and no
retries. A successful connectivity probe alone does not verify model access,
Responses API parameters, or function calling. PowerShell and Node can also use
different proxy and certificate settings.

Failures emit sanitized JSON with `stage`, `category`, `httpStatus`,
`providerCode`, and `networkCode`. No response bodies, messages, headers, stacks,
model inputs, or environment values are printed. Unknown provider codes are
redacted; missing environment variable names are reported. No `.env` file is
loaded automatically. To explicitly load a local `.env` file with Node 22, use
`node --env-file=.env --experimental-strip-types demo/smoke.mjs`.

API `AgentRunner:Url` is the runner base URL; `AgentRunner:ApiKey` must match
`SENTINEL_RUNNER_KEY`. Runner `SENTINEL_AGENT_API_KEY` must match API
`AgentGateway:ApiKey`. Keep these and `OPENAI_API_KEY` out of browser code and
model context. Never supply owner JWTs, worker keys, approval tokens or signing
keys to this process.

Use the dashboard's **AI payment agent** panel to set task, recipient and ceiling.
WARN review and unsigned owner authorization preparation use the existing gateway
APIs. The new feature never signs or dispatches. See [Milestone 3](../../docs/milestone-3.md).
