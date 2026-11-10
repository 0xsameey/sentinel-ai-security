// No environment loading or key access: this module is independently testable.
export type ExecutionCall = {
  receiverId: string;
  methodName: string;
  args: Record<string, unknown>;
  gas: string;
  deposit: string;
  security?: { permitId: string; intentHash: string; expiresAtUtc: string; signerPublicKey: string };
};

export type Submission = { transaction?: { hash?: string }; status: unknown };

export class ExecutionStageError extends Error {
  constructor(public readonly stage: string, public readonly code: string) { super(code); }
}

export class ReportingPendingError extends Error {
  constructor(public readonly transactionHash: string) {
    super("Transaction submitted; reporting pending. Reconcile before retrying.");
  }
}

export function validateExecution(call: unknown, id: string, contract: string, delegated = false): ExecutionCall {
  if (!call || typeof call !== "object") throw new Error("Missing execution call.");
  const c = call as ExecutionCall;
  if (c.receiverId !== contract || c.methodName !== (delegated ? "execute_delegated_payment" : "execute_payment") ||
      c.deposit !== "0" || c.gas !== "30000000000000" ||
      !c.args || typeof c.args !== "object" || Array.isArray(c.args) ||
      Object.keys(c.args).length !== (delegated ? 5 : 1) || c.args.id !== id ||
      (delegated && (!/^[A-Za-z0-9_-]{1,64}$/.test(String(c.args.delegation_id)) ||
        typeof c.args.recipient !== "string" || !/^[a-z0-9]+(?:[-_.][a-z0-9]+)*$/.test(c.args.recipient) || c.args.recipient.length < 2 || c.args.recipient.length > 64 ||
        typeof c.args.amount_yocto !== "string" || !/^[1-9][0-9]{0,38}$/.test(c.args.amount_yocto) || BigInt(c.args.amount_yocto) >= (1n<<128n) ||
        typeof c.args.expires_at_ns !== "string" || !/^[1-9][0-9]{0,19}$/.test(c.args.expires_at_ns) ||
        BigInt(c.args.expires_at_ns) > (1n<<64n)-1n || BigInt(c.args.expires_at_ns) <= BigInt(Date.now())*1000000n ||
        BigInt(c.args.expires_at_ns) > BigInt(Date.now())*1000000n+86400000000000n))) {
    throw new Error("Unauthorized execution call rejected.");
  }
  return c;
}

export function createExecutor(options: {
  apiUrl: string;
  workerKey: string;
  contractId: string;
  submit: (call: ExecutionCall) => Promise<Submission>;
  expectedSignerPublicKey: string;
  timeoutMs?: number;
  delegated?: boolean;
}) {
  async function request(path: string, body: unknown): Promise<unknown> {
    const stage=path.endsWith('/prepare-execution')?'PreparePermit':path.endsWith('/consume-permit')?'ConsumePermit':'ReportTransaction';
    let response: Response;
    try { response = await fetch(`${options.apiUrl.replace(/\/$/, "")}${path}`, {
      method: "POST",
      headers: { "Content-Type": "application/json", "X-Sentinel-Worker-Key": options.workerKey },
      body: JSON.stringify(body),
      redirect: "error",
      signal: AbortSignal.timeout(options.timeoutMs ?? 20000),
    }); } catch(error) {
      const cause=(error as {cause?:{code?:string}})?.cause?.code;
      const code=cause==='ECONNREFUSED'?'SIGNER_API_CONNECTION_REFUSED':
        ['DEPTH_ZERO_SELF_SIGNED_CERT','SELF_SIGNED_CERT_IN_CHAIN','UNABLE_TO_VERIFY_LEAF_SIGNATURE'].includes(cause??'')?'SIGNER_API_TLS_UNTRUSTED':'SIGNER_API_TRANSPORT_UNAVAILABLE';
      throw new ExecutionStageError(stage,code);
    }
    // Upstream bodies can contain sensitive data; never put them in errors.
    if (!response.ok) throw new ExecutionStageError(stage,`SIGNER_API_HTTP_${response.status}`);
    return response.json();
  }

  return async (id: string): Promise<string> => {
    if (!/^[a-zA-Z0-9_-]{1,64}$/.test(id)) throw new Error("Invalid payment ID.");
    const path = `/api/internal/agent/${options.delegated ? "delegated-payments" : "payments"}/${encodeURIComponent(id)}`;
    const call = validateExecution(await request(`${path}/prepare-execution`, {}), id, options.contractId, options.delegated);
    const permit = call.security;
    if (!permit || !/^[0-9a-f-]{36}$/i.test(permit.permitId) || !/^[0-9a-f]{64}$/.test(permit.intentHash) ||
        permit.signerPublicKey !== options.expectedSignerPublicKey || !Number.isFinite(Date.parse(permit.expiresAtUtc)) ||
        Date.parse(permit.expiresAtUtc) <= Date.now()) throw new Error("A fresh signing permit is required.");
    const consumed = await request(`${path}/consume-permit`, { permitId: permit.permitId, intentHash: permit.intentHash }) as {
      permitId: string; paymentId: string; intentHash: string; decision: string; humanApproved: boolean;
      expiresAtUtc: string; signerPublicKey: string; transaction: ExecutionCall;
    };
    if (consumed.permitId !== permit.permitId || consumed.paymentId !== id || consumed.intentHash !== permit.intentHash ||
        consumed.signerPublicKey !== options.expectedSignerPublicKey || Date.parse(consumed.expiresAtUtc) !== Date.parse(permit.expiresAtUtc) ||
        Date.parse(consumed.expiresAtUtc) <= Date.now() ||
        !(consumed.decision === "ALLOW" || (consumed.decision === "WARN" && consumed.humanApproved === true))) {
      throw new Error("Security decision does not authorize signing.");
    }
    const confirmed = validateExecution(consumed.transaction, id, options.contractId, options.delegated);
    if (confirmed.receiverId !== call.receiverId || confirmed.methodName !== call.methodName ||
        confirmed.gas !== call.gas || confirmed.deposit !== call.deposit ||
        Object.keys(call.args).some(k => call.args[k] !== confirmed.args[k])) throw new Error("Execution intent changed.");
    const outcome = await options.submit(call);
    const hash = outcome.transaction?.hash;
    if (!hash) throw new Error("Missing transaction hash. Inspect chain before retrying.");
    try {
      await request(`${path}/report-transaction`, { transactionHash: hash, operation: "Execution" });
    } catch {
      throw new ReportingPendingError(hash);
    }
    const status = outcome.status;
    if (!status || typeof status !== "object" || !("SuccessValue" in status)) {
      throw new Error("Execution not confirmed successful. Hash reported unverified; reconcile before retrying.");
    }
    return hash;
  };
}
