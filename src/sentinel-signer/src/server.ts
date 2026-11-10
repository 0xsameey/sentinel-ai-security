import "dotenv/config";
import { configuredExecutor } from "./agentExecution.js";
import { createSignerApp } from "./signerApp.js";

try {
  const key = process.env.SENTINEL_DISPATCH_KEY ?? "";
  const port = Number(process.env.SENTINEL_DISPATCH_PORT ?? "4301");
  if (!Number.isInteger(port) || port < 1 || port > 65535) throw new Error("Invalid signer port.");
  createSignerApp(key, await configuredExecutor(process.env.SENTINEL_PAYMENT_MODE === "delegated")).listen(port, "127.0.0.1", () => {
    console.log(`Sentinel signer listening on 127.0.0.1:${port}`);
  });
} catch {
  console.error("Signer startup failed. Check testnet configuration and key provisioning.");
  process.exitCode = 1;
}
