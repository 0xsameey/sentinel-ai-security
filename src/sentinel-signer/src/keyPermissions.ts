export function requireRestrictedKey(permission: unknown, contract: string, gasCost: bigint, method = "execute_payment"): void {
  const call = (permission as { FunctionCall?: { receiver_id: string; method_names: string[]; allowance: string | null } })?.FunctionCall;
  if (!call || call.receiver_id !== contract || !Array.isArray(call.method_names) || call.method_names.length !== 1 ||
      call.method_names[0] !== method || typeof call.allowance !== "string" || !/^\d+$/.test(call.allowance) ||
      BigInt(call.allowance) < gasCost || gasCost <= 0n) throw new Error("Restricted execution key with sufficient allowance required.");
}
