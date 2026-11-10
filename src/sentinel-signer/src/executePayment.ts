import { configuredExecutor } from "./agentExecution.js";
import { ReportingPendingError } from "./execution.js";

async function main() {
  const id = process.argv[2];
  if (!id) throw new Error("Usage: npm run execute -- PAYMENT_ID");
  console.log("Verified execution transaction:", await (await configuredExecutor())(id));
}
main().catch(error => {
  if (error instanceof ReportingPendingError) {
    console.error(error.message, "Transaction:", error.transactionHash);
  } else {
    console.error("Execution could not be confirmed. Reconcile before retrying.");
  }
  process.exitCode = 1;
});
