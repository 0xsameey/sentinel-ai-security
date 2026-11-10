import { test } from "node:test";
import assert from "node:assert/strict";
import { createServer } from "node:http";
import type { AddressInfo } from "node:net";
import { createExecutor, ReportingPendingError, ExecutionStageError } from "../src/execution.js";
import { createSignerApp } from "../src/signerApp.js";

const call = { receiverId: "treasury.testnet", methodName: "execute_payment",
  args: { id: "p1" }, gas: "30000000000000", deposit: "0",
  security: { permitId: "11111111-1111-4111-8111-111111111111", intentHash: "a".repeat(64), expiresAtUtc: new Date(Date.now()+120000).toISOString(), signerPublicKey: "ed25519:test-signer" } };

async function scenario(options: {
  call?: unknown; prepareStatus?: number; reportStatus?: number;
  status?: unknown; hash?: string; timeout?: boolean; consumeStatus?: number; consumedDecision?: string; humanApproved?: boolean;
}) {
  let submissions = 0;
  const reports: unknown[] = [];
  const api = createServer(async (req, res) => {
    let body = ""; for await (const chunk of req) body += chunk;
    assert.equal(req.headers["x-sentinel-worker-key"], "synthetic-key");
    res.setHeader("Content-Type", "application/json");
    if (req.url?.endsWith("prepare-execution")) {
      if (options.timeout) { return; }
      res.writeHead(options.prepareStatus ?? 200);
      res.end(JSON.stringify(options.call ?? call));
    } else if (req.url?.endsWith("consume-permit")) {
      res.writeHead(options.consumeStatus ?? 200);
      res.end(JSON.stringify({ permitId: call.security.permitId, paymentId: "p1", intentHash: call.security.intentHash,
        decision: options.consumedDecision ?? "ALLOW", humanApproved: options.humanApproved ?? false,
        signerPublicKey: call.security.signerPublicKey, expiresAtUtc: call.security.expiresAtUtc, transaction: call }));
    } else {
      reports.push(JSON.parse(body));
      res.writeHead(options.reportStatus ?? 200); res.end('{}');
    }
  });
  await new Promise<void>(resolve => api.listen(0, "127.0.0.1", resolve));
  const url = `http://127.0.0.1:${(api.address() as AddressInfo).port}`;
  const execute = createExecutor({ apiUrl: url, workerKey: "synthetic-key",
    contractId: call.receiverId, timeoutMs: options.timeout ? 100 : 2000, expectedSignerPublicKey: "ed25519:test-signer",
    submit: async actual => {
      assert.deepEqual(actual, call); submissions++;
      return { transaction: { hash: options.hash ?? "test-hash" }, status: options.status ?? { SuccessValue: "" } };
    },
  });
  try { return { hash: await execute("p1"), submissions, reports, error: null }; }
  catch (error) { return { hash: null, submissions, reports, error }; }
  finally { api.closeAllConnections(); await new Promise<void>(resolve => api.close(() => resolve())); }
}

test("direct API DTO is submitted once and reported using Execution", async () => {
  const result = await scenario({});
  assert.equal(result.error, null); assert.equal(result.submissions, 1);
  assert.deepEqual(result.reports, [{ transactionHash: "test-hash", operation: "Execution" }]);
});

for (const [name, changed] of Object.entries({
  "missing signing permit": { ...call, security: undefined },
  "expired signing permit": { ...call, security: { ...call.security, expiresAtUtc: new Date(0).toISOString() } },
  "different signer key": { ...call, security: { ...call.security, signerPublicKey: "ed25519:other" } },
  "nested legacy response": { transaction: call },
  "wrong receiver": { ...call, receiverId: "attacker.testnet" },
  "wrong method": { ...call, methodName: "set_policy" },
  "attached funds": { ...call, deposit: "1" },
  "excess gas": { ...call, gas: "300000000000000" },
  "changed ID": { ...call, args: { id: "p2" } },
  "extra arguments": { ...call, args: { id: "p1", recipient: "attacker.testnet" } },
})) {
  test(`never signs ${name}`, async () => {
    const result = await scenario({ call: changed });
    assert.ok(result.error); assert.equal(result.submissions, 0); assert.equal(result.reports.length, 0);
  });
}

test("preparation failure and timeout never sign", async () => {
  for (const options of [{ prepareStatus: 409 }, { timeout: true }]) {
    const result = await scenario(options);
    assert.ok(result.error); assert.equal(result.submissions, 0);
  }
});

test("connection refusal identifies prepare stage without signing or exposing credentials",async()=>{
  let signed=0;
  const closed=createServer();await new Promise<void>(resolve=>closed.listen(0,'127.0.0.1',resolve));
  const port=(closed.address() as AddressInfo).port;await new Promise<void>(resolve=>closed.close(()=>resolve()));
  const execute=createExecutor({apiUrl:`http://127.0.0.1:${port}`,workerKey:'synthetic-secret',contractId:call.receiverId,
    expectedSignerPublicKey:'ed25519:test-signer',submit:async()=>{signed++;return {status:{SuccessValue:''}};}});
  await assert.rejects(execute('p1'),(error:unknown)=>error instanceof ExecutionStageError && error.stage==='PreparePermit' && error.code==='SIGNER_API_CONNECTION_REFUSED');
  assert.equal(signed,0);
});
test("failed outcome preserves unverified transaction evidence without claiming success", async () => {
  const result = await scenario({ status: { Failure: {} } });
  assert.ok(result.error); assert.equal(result.submissions, 1); assert.equal(result.reports.length, 1);
});
test("report failure preserves the submitted hash and never retries signing", async () => {
  const result = await scenario({ reportStatus: 502 });
  assert.ok(result.error instanceof ReportingPendingError);
  assert.equal(result.error.transactionHash, "test-hash"); assert.equal(result.submissions, 1);
});
test("dispatch authentication, ID validation and concurrent duplicate suppression", async () => {
  let release!: () => void;
  const blocked = new Promise<void>(resolve => { release = resolve; });
  let calls = 0;
  const key = "x".repeat(32);
  const server = createSignerApp(key, async () => { calls++; await blocked; return "hash"; }).listen(0, "127.0.0.1");
  await new Promise<void>(resolve => server.once("listening", resolve));
  const url = `http://127.0.0.1:${(server.address() as AddressInfo).port}/execute`;
  const send = (id: string, secret = key) => fetch(url, { method: "POST", headers: {
    "Content-Type": "application/json", "X-Sentinel-Dispatch-Key": secret,
  }, body: JSON.stringify({ paymentId: id }) });
  try {
    assert.equal((await send("p1", "bad")).status, 401);
    assert.equal((await send("../invalid")).status, 400);
    const first = send("p1");
    while (!calls) await new Promise(resolve => setTimeout(resolve, 5));
    assert.equal((await send("p1")).status, 409);
    release(); assert.equal((await first).status, 200); assert.equal(calls, 1);
  } finally { release(); server.closeAllConnections(); await new Promise<void>(resolve => server.close(() => resolve())); }
});

test("BLOCK, unapproved WARN, expired/replayed permit never reach submit", async () => {
  for (const options of [{ consumedDecision: "BLOCK" }, { consumedDecision: "WARN" }, { consumeStatus: 409 }]) {
    const result = await scenario(options); assert.ok(result.error); assert.equal(result.submissions, 0);
  }
});
test("WARN reaches submit only after authenticated gateway approval confirmation", async () => {
  const result = await scenario({ consumedDecision: "WARN", humanApproved: true });
  assert.equal(result.error, null); assert.equal(result.submissions, 1);
});
