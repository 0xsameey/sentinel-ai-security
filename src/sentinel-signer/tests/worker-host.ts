// Test-only host. Synthetic submission replaces the key/NEAR SDK boundary.
// No production flag can enable this adapter.
import { createExecutor } from "../src/execution.js";
import { createSignerApp } from "../src/signerApp.js";
import { randomUUID } from "node:crypto";

const apiUrl = process.env.TEST_API_URL!;
const nearUrl = process.env.TEST_NEAR_URL!;
const executor = createExecutor({
  apiUrl, workerKey: process.env.TEST_WORKER_KEY!, contractId: "treasury.testnet", expectedSignerPublicKey: "ed25519:test-signer",
  submit: async call => {
    const response = await fetch(`${apiUrl}/api/v1/payments/${call.args.id}`);
    const payment = await response.json();
    const hash = randomUUID();
    const transaction = {
      final_execution_status: "FINAL", status: { SuccessValue: "" },
      transaction: { hash, signer_id: "agent.testnet", receiver_id: call.receiverId,
        actions: [{ FunctionCall: { method_name: call.methodName, deposit: call.deposit,
          args: Buffer.from(JSON.stringify(call.args)).toString("base64") } }] },
    };
    const saved = await fetch(`${nearUrl}/control`, { method: "POST",
      headers: { "Content-Type": "application/json" }, body: JSON.stringify({ transaction,
        payment: { id: payment.id, recipient: payment.recipient, amount_yocto: payment.amountYocto,
          expires_at_ns: String(payment.expiresAtNs), status: "Completed" } }),
    });
    if (!saved.ok) throw new Error("Fixture submission failed.");
    return { transaction: { hash }, status: { SuccessValue: "" } };
  },
});
createSignerApp(process.env.TEST_DISPATCH_KEY!, async id => {
  try { return await executor(id); } catch (error) { console.error(error instanceof Error ? error.message : "fixture execution failed"); throw error; }
})
  .listen(Number(process.env.TEST_SIGNER_PORT), "127.0.0.1", () => console.log("TEST_SIGNER_READY"));
