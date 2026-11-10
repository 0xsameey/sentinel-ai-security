const API_URL = import.meta.env.VITE_API_URL;
import type {GatewayAssessment} from "../../sentinel-agent-client/src/client";

export type AuthChallenge = {
  challengeId: string;
  message: string;
  recipient: string;
  nonceBase64: string;
  expiresAtUtc: string;
};

export type AuthToken = {
  accessToken: string;
  tokenType: string;
  expiresIn: number;
  accountId: string;
};

export type ContractConfig = {
  owner: string;
  agent: string;
  paused: boolean;
};

export type SpendingPolicy = {
  maxPerTransactionYocto: string;
  dailyLimitYocto: string;
};

export type RiskAssessment = {
  riskScore: number;
  riskLevel: string;
  decision: "ALLOW" | "WARN" | "BLOCK";
  reasons: string[];
  aiExplanation: string | null;
};

export type Payment = {
  id: string;
  recipient: string;
  amountYocto: string;
  expiresAtNs: string;
  status: string;
};

export type PreparedPayment = {
  paymentId: string;
  transaction: {
    receiverId: string;
    methodName: string;
    args: Record<string, unknown>;
    gas: string;
    deposit: string;
  };
};

async function apiRequest<T>(
  path: string,
  options: RequestInit = {},
  token?: string,
): Promise<T> {
  const headers = new Headers(options.headers);
  if (!headers.has("Content-Type")) headers.set("Content-Type", "application/json");
  if (token) headers.set("Authorization", `Bearer ${token}`);
  const response = await fetch(`${API_URL}${path}`, {
    ...options,
    headers,
  });

  if (!response.ok) {
    const message = await response.text();

    throw new Error(`API error ${response.status}: ${message}`);
  }

  return (await response.json()) as T;
}

export const sentinelApi = {
  runAgentTask: (token:string, data:{task:string;recipient:string;maxAmountYocto:string},id:string) =>
    apiRequest<{runId:string;status:string;assessment:GatewayAssessment}>("/api/v1/agent/tasks",{
      method:"POST",headers:{"Idempotency-Key":id},body:JSON.stringify(data)},token),
  approveAssessment: (token:string,id:string,fingerprint:string) =>
    apiRequest<{approvalId:string;expiresAtUtc:string}>(`/api/v1/gateway/assessments/${id}/approve`,{
      method:"POST",body:JSON.stringify({fingerprint,confirm:true})},token),
  prepareAssessment: (token:string,id:string,approvalId:string|null,key:string) =>
    apiRequest<PreparedPayment>(`/api/v1/gateway/assessments/${id}/prepare`,{
      method:"POST",headers:{"Idempotency-Key":key},body:JSON.stringify({approvalId})},token),
  dispatchPayment: (token: string, paymentId: string) =>
    apiRequest<{
      message: string;
      result: string;
    }>(
      `/api/v1/payments/${encodeURIComponent(paymentId)}/dispatch`,
      {
        method: "POST",
      },
      token,
    ),

  reportPaymentTransaction: (token: string, paymentId: string, hash: string) =>
    apiRequest<unknown>(
      `/api/v1/payments/${encodeURIComponent(paymentId)}/transactions`,
      {
        method: "POST",
        body: JSON.stringify({
          transactionHash: hash,
          operation: "Authorization",
        }),
      },
      token,
    ),

  syncPayment: (token: string, paymentId: string) =>
    apiRequest<unknown>(
      `/api/v1/payments/${encodeURIComponent(paymentId)}/sync`,
      {
        method: "POST",
      },
      token,
    ),

  createChallenge: (accountId: string) =>
    apiRequest<AuthChallenge>("/api/v1/auth/challenge", {
      method: "POST",
      body: JSON.stringify({ accountId }),
    }),

  verifyWallet: (data: {
    challengeId: string;
    accountId: string;
    publicKey: string;
    signatureBase64: string;
  }) =>
    apiRequest<AuthToken>("/api/v1/auth/verify", {
      method: "POST",
      body: JSON.stringify(data),
    }),

  getConfig: () => apiRequest<ContractConfig>("/api/v1/contract/config"),

  getBalance: () =>
    apiRequest<{ availableBalanceYocto: string }>("/api/v1/contract/balance"),

  getPolicy: (token: string) =>
    apiRequest<SpendingPolicy>("/api/v1/policies/current", {}, token),

  assessRisk: (
    token: string,
    data: {
      recipient: string;
      amountYocto: string;
      purpose: string;
    },
  ) =>
    apiRequest<RiskAssessment>(
      "/api/v1/risk/assess",
      {
        method: "POST",
        body: JSON.stringify(data),
      },
      token,
    ),

  preparePayment: (
    token: string,
    data: {
      recipient: string;
      amountYocto: string;
      expiresAtNs: string;
      purpose: string;
    },
    idempotencyKey: string,
  ) =>
    apiRequest<PreparedPayment>(
      "/api/v1/payments/prepare",
      {
        method: "POST",
        headers: {
          "Idempotency-Key": idempotencyKey,
        },
        body: JSON.stringify(data),
      },
      token,
    ),

  getPayment: (id: string) =>
    apiRequest<Payment>(`/api/v1/payments/${encodeURIComponent(id)}`),
};

export type DelegationState = {agent:string;maxPayment:string;budget:string;dailyLimit:string;expiresAtNs:string;recipients:string[];revoked:boolean;spent:string;day:string;spentToday:string};
export type DelegatedPayment = {id:string;delegationId:string;intentJson:string;decisionJson:string;decision:string;fingerprint:string;status:string;transactionHash:string|null;approvedAtUtc:string|null;approvalExpiresAtUtc:string|null;consumedAtUtc:string|null;permitId?:string|null};
export type DelegationView = {id:string;state:DelegationState|null;remainingBudget:string;observedAtUtc:string;blockHash:string;payments:DelegatedPayment[]};
export const delegatedApi = {
  list:(token:string)=>apiRequest<{id:string}[]>("/api/v1/delegations",{},token),
  inspect:(token:string,id:string)=>apiRequest<DelegationView>(`/api/v1/delegations/${encodeURIComponent(id)}`,{},token),
  create:(token:string,data:Record<string,unknown>)=>apiRequest<PreparedPayment["transaction"]>("/api/v1/delegations",{method:"POST",body:JSON.stringify(data)},token),
  revoke:(token:string,id:string)=>apiRequest<PreparedPayment["transaction"]>(`/api/v1/delegations/${encodeURIComponent(id)}/revoke`,{method:"POST",body:"{}"},token),
  run:(token:string,data:Record<string,unknown>,runId:string=crypto.randomUUID())=>apiRequest<{payment:DelegatedPayment;status:string}>("/api/v1/delegated-agent/tasks",{method:"POST",headers:{"Idempotency-Key":runId},body:JSON.stringify(data)},token),
  approve:(token:string,p:DelegatedPayment)=>apiRequest(`/api/v1/delegations/payments/${p.id}/approve`,{method:"POST",body:JSON.stringify({confirm:true,fingerprint:p.fingerprint})},token),
  dispatch:(token:string,id:string)=>apiRequest(`/api/v1/delegated-agent/payments/${id}/dispatch`,{method:"POST",body:"{}"},token),
  reassess:(token:string,id:string)=>apiRequest(`/api/v1/delegations/payments/${id}/reassess`,{method:"POST",body:"{}"},token),
  reconcile:(token:string,id:string)=>apiRequest(`/api/v1/delegations/payments/${id}/reconcile`,{method:"POST",body:"{}"},token)
};
