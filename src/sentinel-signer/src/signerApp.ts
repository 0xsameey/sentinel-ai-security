import express from "express";
import { timingSafeEqual } from "node:crypto";
import { ReportingPendingError, ExecutionStageError } from "./execution.js";

export function createSignerApp(key: string, execute: (id: string) => Promise<string>) {
  if (key.length < 32) throw new Error("Dispatch key must be at least 32 characters.");
  const app = express();
  app.disable("x-powered-by");
  app.use(express.json({ limit: "8kb" }));
  const inFlight = new Set<string>();
  app.post("/execute", async (req, res) => {
    const provided = Buffer.from(req.header("X-Sentinel-Dispatch-Key") ?? "");
    const expected = Buffer.from(key);
    if (provided.length !== expected.length || !timingSafeEqual(provided, expected)) {
      res.status(401).json({ message: "Unauthorized" }); return;
    }
    const id = req.body?.paymentId;
    if (typeof id !== "string" || !/^[a-zA-Z0-9_-]{1,64}$/.test(id)) {
      res.status(400).json({ message: "Invalid payment ID." }); return;
    }
    if (inFlight.has(id)) { res.status(409).json({ message: "Execution already in progress." }); return; }
    inFlight.add(id);
    try {
      res.json({ paymentId: id, transactionHash: await execute(id), status: "SubmittedUnverified" });
    } catch (error) {
      if (error instanceof ReportingPendingError) {
        res.status(202).json({ paymentId: id, transactionHash: error.transactionHash,
          status: "ReportingPending", message: error.message });
      } else {
        res.status(409).json({paymentId:id,stage:error instanceof ExecutionStageError?error.stage:'Signer',
          code:error instanceof ExecutionStageError?error.code:'SIGNER_EXECUTION_UNCONFIRMED',
          message: "Execution could not be confirmed. Reconcile before retrying." });
      }
    } finally { inFlight.delete(id); }
  });
  return app;
}
