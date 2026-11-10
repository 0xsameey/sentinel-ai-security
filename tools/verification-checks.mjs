// Pure, read-only preflight arithmetic. This does not authorize or sign payments.
import { isDeepStrictEqual } from 'node:util';

export function crossCheckSignerPermission(direct, keys, publicKey) {
  const listedKey = keys.find(key => key.public_key === publicKey);
  const queryFailed = typeof direct?.error === 'string' || direct?.error != null;
  const permission = direct?.permission;
  const listedPermission = listedKey?.access_key?.permission;
  return { listedKey, permission, listedPermission,
    queryFailed,
    confirmed: !queryFailed && permission != null && listedPermission != null && isDeepStrictEqual(permission, listedPermission) };
}

export function inspectSignerPermission(permission, contractId, gasCost) {
  const call = permission && typeof permission === 'object' ? permission.FunctionCall : null;
  const restricted = !!call && call.receiver_id === contractId &&
    Array.isArray(call.method_names) && call.method_names.length === 1 && call.method_names[0] === 'execute_payment';
  const allowance = call?.allowance;
  const finite = typeof allowance === 'string' && /^\d+$/.test(allowance) && BigInt(allowance) > 0n;
  return { restricted, finiteGasAllowance: finite,
    allowanceSufficientAtObservedPrice: finite && BigInt(allowance) >= gasCost };
}

export function assessTrialPolicy({ policy, usage, availableBalance, recipientAllowed, amount }) {
  function u128(value) {
    if (typeof value !== 'string' || !/^\d+$/.test(value) || value.length > 39 || BigInt(value) > (1n << 128n) - 1n)
      throw new Error('Invalid RPC amount.');
    return BigInt(value);
  }
  if (!Array.isArray(usage) || usage.length !== 4 || typeof recipientAllowed !== 'boolean' || amount <= 0n)
    throw new Error('Invalid preflight data.');
  const max = u128(policy.max_per_transaction);
  const daily = u128(policy.daily_limit);
  const policyPermits = max > 0n && daily > 0n && amount <= max &&
    u128(usage[1]) + amount <= daily && u128(usage[2]) + u128(usage[3]) + amount <= daily;
  const riskScore = (amount * 100n >= max * 80n ? 35 : 0) + (amount * 100n >= daily * 50n ? 20 : 0);
  return { policyPermits, sufficientBalance: u128(availableBalance) >= amount,
    policyPreviewDecision: !recipientAllowed || !policyPermits ? 'BLOCK' : riskScore >= 30 ? 'REVIEW' : 'ALLOW' };
}
