// Only used by ASP.NET HTTP contract tests. No key or NEAR SDK is loaded.
import { createExecutor } from "../src/execution.js";
const execute = createExecutor({
  apiUrl: process.env.TEST_API_URL!, workerKey: process.env.TEST_WORKER_KEY!,
  contractId: "treasury.testnet", expectedSignerPublicKey: "ed25519:test-signer",
  submit: async () => ({ transaction: { hash: "synthetic-execution-hash" }, status: { SuccessValue: "" } }),
});
try { console.log(await execute(process.argv[2]!)); }
catch { console.error("HTTP worker fixture failed"); process.exitCode = 1; }
