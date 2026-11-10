export type PaymentProposal = { recipient: string; amountYocto: string; expiresAtNs: string; purpose: string };
export type GatewayAssessment = {
  assessmentId: string; intentHash: string; stage: "Authorization" | "Execution"; intent: PaymentProposal;
  risk: { decision: "ALLOW" | "WARN" | "BLOCK"; reasonCodes: string[]; fingerprint: string;
    evidence: { code: string; source: string; observedAtUtc: string; blockHash: string | null; detail: string }[];
    assessedAtUtc: string; expiresAtUtc: string };
};

export type GatewayContext = { network: "testnet"; contractId: string; paused: boolean; recipient: string;
  delegationExpiresAtNs?: string;
  recipientExists: boolean; recipientAllowed: boolean; recipientCodeHash: string; maxPerTransaction: string;
  dailyLimit: string; authorizedToday: string; executedToday: string; executingToday: string;
  availableBalance: string; blockHash: string; observedAtUtc: string };

export function validateAssessment(value: unknown, proposal?: PaymentProposal): GatewayAssessment {
  const a = value as GatewayAssessment;
  if (!a || !/^[a-f0-9-]{36}$/i.test(a.assessmentId) || !/^[a-f0-9]{64}$/.test(a.intentHash) ||
      a.stage !== "Authorization" || !a.risk || !["ALLOW", "WARN", "BLOCK"].includes(a.risk.decision) ||
      !/^[a-f0-9]{64}$/.test(a.risk.fingerprint) || !Array.isArray(a.risk.reasonCodes) ||
      a.risk.reasonCodes.length === 0 || a.risk.reasonCodes.some(x => typeof x !== "string" || !/^[A-Z_]{1,80}$/.test(x)) ||
      !Array.isArray(a.risk.evidence) || a.risk.evidence.length > 100 ||
      !Number.isFinite(Date.parse(a.risk.assessedAtUtc)) || !Number.isFinite(Date.parse(a.risk.expiresAtUtc)) ||
      Date.parse(a.risk.expiresAtUtc) <= Date.now() || Date.parse(a.risk.assessedAtUtc) > Date.now()+5000 ||
      Date.parse(a.risk.expiresAtUtc)-Date.parse(a.risk.assessedAtUtc) > 120000) throw new Error("Invalid gateway assessment.");
  const intent = validateProposal(a.intent);
  if (proposal && JSON.stringify(intent) !== JSON.stringify(validateProposal(proposal))) throw new Error("Gateway intent mismatch.");
  return a;
}

// Future structured tool contract. No key, approval, signing or execution tool is exposed.
export const assessPaymentTool = {
  type: "function", name: "assess_payment", strict: true,
  description: "Propose a testnet payment for independent Sentinel assessment. ALLOW is not permission to sign; WARN requires human review; BLOCK cannot be overridden.",
  parameters: { type: "object", additionalProperties: false,
    required: ["recipient", "amountYocto", "expiresAtNs", "purpose"],
    properties: {
      recipient: { type: "string", minLength: 2, maxLength: 64, pattern: "^[a-z0-9]+(?:[-_.][a-z0-9]+)*$" },
      amountYocto: { type: "string", pattern: "^[1-9][0-9]{0,38}$" },
      expiresAtNs: { type: "string", pattern: "^[1-9][0-9]{0,19}$" },
      purpose: { type: "string", minLength: 1, maxLength: 500 }
    }
  }
} as const;

export function validateProposal(input: unknown): PaymentProposal {
  if (!input || typeof input !== "object" || Array.isArray(input)) throw new Error("Invalid payment proposal.");
  const value = input as Record<string, unknown>;
  const fields = ["recipient", "amountYocto", "expiresAtNs", "purpose"];
  if (Object.keys(value).length !== fields.length || fields.some(x => typeof value[x] !== "string")) throw new Error("Unexpected proposal fields.");
  const p = value as PaymentProposal;
  if (p.recipient.length < 2 || p.recipient.length > 64 || !/^[a-z0-9]+(?:[-_.][a-z0-9]+)*$/.test(p.recipient) ||
      !/^[1-9][0-9]{0,38}$/.test(p.amountYocto) || BigInt(p.amountYocto) > (1n << 128n) - 1n ||
      !/^[1-9][0-9]{0,19}$/.test(p.expiresAtNs) || BigInt(p.expiresAtNs) > (1n << 64n) - 1n ||
      !p.purpose.trim() || p.purpose.length > 500) throw new Error("Invalid payment proposal.");
  return { recipient: p.recipient, amountYocto: p.amountYocto, expiresAtNs: p.expiresAtNs, purpose: p.purpose };
}

export class SentinelAgentClient {
  #base: string;
  #key: string;
  #fetch: typeof fetch;
  constructor(baseUrl: string, agentApiKey: string, transport: typeof fetch = fetch, options: {allowLoopbackHttp?: boolean} = {}) {
    const url = new URL(baseUrl);
    const localHttp = options.allowLoopbackHttp === true && url.protocol === "http:" &&
      ["localhost", "127.0.0.1", "[::1]"].includes(url.hostname);
    if (!(url.protocol === "https:" || localHttp) || url.username || url.password || url.search || url.hash || !agentApiKey) throw new Error("Invalid gateway configuration.");
    this.#base = baseUrl.replace(/\/$/, ""); this.#key = agentApiKey; this.#fetch = transport;
  }
  async #request(path: string, method: string, input?: unknown, signal?: AbortSignal, runId?: string): Promise<unknown> {
    const response = await this.#fetch(`${this.#base}${path}`, { method, redirect: "error",
      headers: { "Content-Type": "application/json", "X-Sentinel-Agent-Key": this.#key, ...(runId ? {"X-Sentinel-Agent-Run":runId} : {}) },
      ...(input === undefined ? {} : { body: JSON.stringify(input) }),
      signal: signal ? AbortSignal.any([signal, AbortSignal.timeout(20000)]) : AbortSignal.timeout(20000) });
    if (!response.ok) throw new Error(`Gateway HTTP ${response.status}`);
    const reader = response.body?.getReader();
    if (!reader) throw new Error("Gateway response missing.");
    const chunks: Uint8Array[] = []; let size = 0;
    try {
      while (true) {
        const part = await reader.read(); if (part.done) break;
        size += part.value.byteLength;
        if (size > 64000) { await reader.cancel(); throw new Error("Gateway response too large."); }
        chunks.push(part.value);
      }
    } finally { reader.releaseLock(); }
    const body = new Uint8Array(size); let offset = 0;
    for (const chunk of chunks) { body.set(chunk,offset); offset += chunk.byteLength; }
    return JSON.parse(new TextDecoder().decode(body));
  }
  async getContext(recipient: string, signal?: AbortSignal): Promise<GatewayContext> {
    if (!/^[a-z0-9]+(?:[-_.][a-z0-9]+)*$/.test(recipient) || recipient.length > 64 || recipient.length < 2) throw new Error("Invalid recipient.");
    const c = await this.#request(`/api/v1/gateway/context/${encodeURIComponent(recipient)}`, "GET", undefined, signal) as GatewayContext;
    if (!c || c.network !== "testnet" || c.recipient !== recipient || typeof c.paused !== "boolean" ||
        typeof c.contractId !== "string" || !/^[a-z0-9]+(?:[-_.][a-z0-9]+)*\.testnet$/.test(c.contractId) || c.contractId.length>64 ||
        typeof c.recipientCodeHash !== "string" || !/^[1-9A-HJ-NP-Za-km-z]{32,44}$/.test(c.recipientCodeHash) ||
        typeof c.blockHash !== "string" || !/^[1-9A-HJ-NP-Za-km-z]{32,44}$/.test(c.blockHash) ||
        typeof c.recipientAllowed !== "boolean" || typeof c.recipientExists !== "boolean" ||
        !Number.isFinite(Date.parse(c.observedAtUtc)) || Date.now()-Date.parse(c.observedAtUtc)>30000 ||
        Date.parse(c.observedAtUtc)>Date.now()+5000 ||
        [c.maxPerTransaction,c.dailyLimit,c.authorizedToday,c.executedToday,c.executingToday,c.availableBalance]
          .some(x => typeof x !== "string" || !/^(0|[1-9][0-9]{0,38})$/.test(x) || BigInt(x) > (1n<<128n)-1n))
      throw new Error("Invalid gateway context.");
    return c;
  }
  async getAssessment(id: string, signal?: AbortSignal): Promise<GatewayAssessment> {
    if (!/^[a-f0-9-]{36}$/i.test(id)) throw new Error("Invalid assessment ID.");
    const result = validateAssessment(await this.#request(`/api/v1/gateway/assessments/${id}`, "GET", undefined, signal));
    if (result.assessmentId !== id) throw new Error("Gateway assessment mismatch.");
    return result;
  }
  async assessPayment(input: unknown, signal?: AbortSignal, runId?: string): Promise<GatewayAssessment> {
    const proposal = validateProposal(input);
    return validateAssessment(await this.#request("/api/v1/gateway/assess", "POST", proposal, signal, runId), proposal);
  }
  delegatedGateway(delegationId: string) {
    if (!/^[A-Za-z0-9_-]{1,64}$/.test(delegationId)) throw new Error("Invalid delegation ID.");
    const convert = (r: unknown): GatewayAssessment => {
      const p = r as {id:string;delegationId:string;intentHash:string;intentJson:string;decisionJson:string};
      if (p.delegationId !== delegationId) throw new Error("Delegation changed.");
      const raw = JSON.parse(p.intentJson);
      return validateAssessment({assessmentId:p.id,intentHash:p.intentHash,stage:"Authorization",
        intent:{recipient:raw.Recipient,amountYocto:raw.AmountYocto,expiresAtNs:raw.ExpiresAtNs,purpose:raw.Purpose},risk:JSON.parse(p.decisionJson)});
    };
    return {
      dispatch:async(id:string,signal?:AbortSignal)=> {
        if(!/^[a-f0-9-]{36}$/i.test(id)) throw new Error("Invalid payment ID.");
        return this.#request(`/api/v1/delegated-agent/payments/${id}/dispatch`,"POST",{},signal);
      },
      getContext:async(recipient:string,signal?:AbortSignal)=>{
        const context=await this.getContext(recipient,signal);
        const view=await this.#request(`/api/v1/delegations/${encodeURIComponent(delegationId)}`,"GET",undefined,signal) as {state?:{expiresAtNs?:string}|null};
        const expiry=view.state?.expiresAtNs;
        if(typeof expiry!=="string" || !/^[1-9][0-9]{0,19}$/.test(expiry)) throw new Error("Delegation expiry unavailable.");
        return {...context,delegationExpiresAtNs:expiry};
      },
      assessPayment:async(input:PaymentProposal,signal?:AbortSignal,runId?:string)=>convert(await this.#request("/api/v1/delegations/proposals","POST",{delegationId,intent:validateProposal(input)},signal,runId)),
      getAssessment:async(id:string,signal?:AbortSignal)=>{
        if (!/^[a-f0-9-]{36}$/i.test(id)) throw new Error("Invalid delegated payment ID.");
        return convert(await this.#request(`/api/v1/delegations/payments/${id}`,"GET",undefined,signal));
      }
    };
  }

}
