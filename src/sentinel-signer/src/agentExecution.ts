import "dotenv/config";
import { Account, JsonRpcProvider, actions, KeyPairSigner } from "near-api-js";
import type { KeyPairString } from "near-api-js";
import { createExecutor } from "./execution.js";
import { requireRestrictedKey } from "./keyPermissions.js";

function required(name: string): string {
  const value = process.env[name];
  if (!value) throw new Error(`Missing configuration: ${name}`);
  return value;
}

// CLI and dispatch server share configuration and validation.
export async function configuredExecutor(delegated = false) {
  if (required("NEAR_NETWORK") !== "testnet") throw new Error("Only testnet is supported.");
  const contractId = required("NEAR_CONTRACT_ID");
  const agentId = required("NEAR_AGENT_ACCOUNT_ID");
  if (!contractId.endsWith(".testnet") || !agentId.endsWith(".testnet")) {
    throw new Error("Testnet named accounts are required.");
  }
  const apiUrl = required("SENTINEL_API_URL");
  const rpcUrl = required("NEAR_RPC_URL");
  for (const [value,isApi] of [[apiUrl,true],[rpcUrl,false]] as const) {
    const url = new URL(value);
    const localApiHttp=isApi && process.env.SENTINEL_ALLOW_LOOPBACK_HTTP==='true' && url.protocol==='http:' &&
      ['localhost','127.0.0.1','[::1]'].includes(url.hostname);
    if (!(url.protocol === "https:" || localApiHttp) || url.username || url.password || url.search || url.hash) {
      throw new Error("HTTPS URLs without embedded credentials are required.");
    }
  }
  const key = required("NEAR_AGENT_PRIVATE_KEY") as KeyPairString;
  const publicKey = (await KeyPairSigner.fromSecretKey(key).getPublicKey()).toString();
  const provider = new JsonRpcProvider({ url: rpcUrl });
  const account = new Account(agentId, provider, key);
  async function read(method: string, params: unknown) {
    const response = await fetch(rpcUrl, { method: "POST", redirect: "error", signal: AbortSignal.timeout(10000),
      headers: { "Content-Type": "application/json" }, body: JSON.stringify({ jsonrpc: "2.0", id: "sentinel-signer-check", method, params }) });
    if (!response.ok) throw new Error("Signer key attestation unavailable.");
    const data = await response.json();
    if (data.error || data.result?.error) throw new Error("Signer key attestation unavailable.");
    return data.result;
  }
  return createExecutor({
    apiUrl, contractId, delegated, workerKey: required("SENTINEL_WORKER_KEY"), expectedSignerPublicKey: publicKey,
    submit: async call => {
      if ((await read("status", [])).chain_id !== "testnet") throw new Error("Wrong signer network.");
      const access = await read("query", { request_type: "view_access_key", finality: "final", account_id: agentId, public_key: publicKey });
      const price = await read("gas_price", {});
      requireRestrictedKey(access.permission, contractId, BigInt(call.gas) * BigInt(price.gas_price), delegated ? "execute_delegated_payment" : "execute_payment");
      if (!call.security || Date.parse(call.security.expiresAtUtc) <= Date.now()) throw new Error("Signing permit expired.");
      if (delegated) {
        const view = await read("query", { request_type:"call_function", finality:"final", account_id:contractId,
          method_name:"get_delegation", args_base64:Buffer.from(JSON.stringify({id:call.args.delegation_id})).toString("base64") });
        const d = JSON.parse(Buffer.from(view.result).toString("utf8"));
        const now = BigInt(Date.now())*1000000n;
        const amount = BigInt(String(call.args.amount_yocto));
        const paymentExpiry=BigInt(String(call.args.expires_at_ns));
        if (!d || d.revoked || d.agent !== agentId || now >= BigInt(d.expires_at_ns) ||
            paymentExpiry<=now || paymentExpiry>now+86400000000000n || paymentExpiry > BigInt(d.expires_at_ns) || !d.recipients.includes(call.args.recipient) ||
            amount > BigInt(d.max_payment) || amount+BigInt(d.spent)>BigInt(d.budget) ||
            amount+(BigInt(d.day)===now/86400000000000n ? BigInt(d.spent_today) : 0n)>BigInt(d.daily_limit))
          throw new Error("Delegation no longer permits payment.");
      }
      if (!call.security || Date.parse(call.security.expiresAtUtc)<=Date.now()) throw new Error("Signing permit expired before submission.");
      return account.signAndSendTransaction({ receiverId: contractId,
        actions: [actions.functionCall(call.methodName, call.args, BigInt(call.gas), 0n)] });
    },
  });
}
